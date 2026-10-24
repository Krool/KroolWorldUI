using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KroolWorldUI.Features
{
    /// <summary>
    /// A PUBG-style compass strip at the top center: a narrow window of headings that scrolls as
    /// the camera turns, a heading readout for call-outs ("wolves at 210"), and markers for
    /// friends and nearby map pins. North is +Z, like the map. Read-only.
    /// </summary>
    internal static class Compass
    {
        internal enum DistanceMode { Focused, AllMarkers, Off }

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> Size;
        internal static ConfigEntry<float> FieldOfView;
        internal static ConfigEntry<float> Width;
        internal static ConfigEntry<float> Opacity;
        internal static ConfigEntry<float> OffsetY;
        internal static ConfigEntry<bool> Heading;
        internal static ConfigEntry<bool> HeadingDirection;
        internal static ConfigEntry<bool> FocusInfo;
        internal static ConfigEntry<DistanceMode> Distance;
        internal static ConfigEntry<bool> Friends;
        internal static ConfigEntry<bool> Pins;
        internal static ConfigEntry<bool> Bed;
        internal static ConfigEntry<bool> Tombstone;
        internal static ConfigEntry<bool> Bosses;
        internal static ConfigEntry<bool> Pings;
        internal static ConfigEntry<float> PinRange;
        internal static ConfigEntry<int> MaxMarkers;
        internal static ConfigEntry<float> HeightArrowMeters;
        internal static ConfigEntry<bool> Animations;

        private const string Section = "Compass";

        internal static void BindConfig(ConfigFile config)
        {
            Enabled = config.Bind(Section, "Enabled", true,
                "A compass strip at the top of the screen with your heading, friends and nearby map pins.");
            Size = config.Bind(Section, "Size", 1f,
                new ConfigDescription("Overall size of the compass.", new AcceptableValueRange<float>(0.6f, 2f)));
            FieldOfView = config.Bind(Section, "FieldOfView", 120f,
                new ConfigDescription("How many degrees of the compass the strip shows. Narrower is easier to read.",
                    new AcceptableValueRange<float>(60f, 240f)));
            Width = config.Bind(Section, "Width", 520f,
                new ConfigDescription("Strip width in HUD pixels.", new AcceptableValueRange<float>(300f, 1000f)));
            Opacity = config.Bind(Section, "Opacity", 0.9f,
                new ConfigDescription("Overall opacity of the compass.", new AcceptableValueRange<float>(0.2f, 1f)));
            OffsetY = config.Bind(Section, "OffsetY", 0f,
                new ConfigDescription("Nudge down from the top of the screen, in HUD pixels.",
                    new AcceptableValueRange<float>(0f, 300f)));
            Heading = config.Bind(Section, "Heading", true,
                "Your exact heading under the center mark (0 = north, 90 = east), for call-outs.");
            HeadingDirection = config.Bind(Section, "HeadingDirection", false,
                "Add the compass direction after the heading: 212 SW.");
            FocusInfo = config.Bind(Section, "FocusInfo", true,
                "Name of the marker you are looking at, beside the heading. Stacked markers show how many are there.");
            Distance = config.Bind(Section, "Distance", DistanceMode.Focused,
                "Focused: the distance of the marker you are looking at, beside its name. All Markers: also a small " +
                "number under every marker. Off: no distances.");
            Friends = config.Bind(Section, "Friends", true,
                "Other players, each in their own color. Held at the strip's edge when they are behind you. " +
                "Far-away players show only if they are visible on the map.");
            Pins = config.Bind(Section, "Pins", true, "Pins you placed on the map, bench pins included.");
            Bed = config.Bind(Section, "Bed", true, "Your bed (spawn point).");
            Tombstone = config.Bind(Section, "Tombstone", true, "Where you died.");
            Bosses = config.Bind(Section, "Bosses", true, "Boss altars and other boss locations on your map.");
            Pings = config.Bind(Section, "Pings", true, "Map pings from you and your friends, at any distance.");
            PinRange = config.Bind(Section, "PinRange", 300f,
                new ConfigDescription("Pins farther than this (meters) are left off. Friends and pings ignore it.",
                    new AcceptableValueRange<float>(50f, 2000f)));
            MaxMarkers = config.Bind(Section, "MaxMarkers", 12,
                new ConfigDescription("Most markers on the strip at once; pings and friends come first, then the nearest.",
                    new AcceptableValueRange<int>(3, 24)));
            HeightArrowMeters = config.Bind(Section, "HeightArrowMeters", 10f,
                new ConfigDescription("A small up or down arrow on markers this far above or below you. 0 = off.",
                    new AcceptableValueRange<float>(0f, 50f)));
            Animations = config.Bind(Section, "Animations", true,
                "Markers ease in and out, grow when you look at them, and pings pulse. Off: plain fades only.");
        }

        [HarmonyPatch(typeof(Hud), "Update")]
        private static class EnsureOnHud
        {
            private static void Postfix()
            {
                if (Enabled.Value)
                    CompassHud.Ensure();
            }
        }
    }

    internal class CompassHud : MonoBehaviour
    {
        // Layout, top to bottom, in HUD pixels at Size 1.
        private const float MarkerRowPlain = 24f;      // icons
        private const float MarkerRowDistance = 34f;   // icons with a distance under each
        private const float LabelRow = 18f;      // N, NE, 15, 30...
        private const float TickRow = 7f;
        private const float PointerSize = 9f;
        private const float HeadingRow = 18f;
        private const float IconSize = 20f;
        private const float EdgeFadeStart = 0.7f;   // fraction of the half-window where fading starts
        private const float RefreshInterval = 0.25f;
        private const float FocusDegrees = 6f;      // either side of center that counts as looking at it
        private const float FocusScale = 1.3f;
        private const float MaxDt = 0.05f;

        private static readonly Color[] FriendColors =
        {
            new Color(0.40f, 0.78f, 1f), new Color(0.55f, 0.90f, 0.45f), new Color(1f, 0.60f, 0.85f),
            new Color(0.75f, 0.60f, 1f), new Color(1f, 0.85f, 0.35f), new Color(0.45f, 0.95f, 0.85f),
        };

        private static CompassHud _instance;
        private static bool _failed;
        private static Sprite _triangle, _ring;
        private static AccessTools.FieldRef<Minimap, List<Minimap.PinData>> _pins;
        private static readonly Vector3[] Corners = new Vector3[4];

        private RectTransform _rect;
        private CanvasGroup _group;
        private RectTransform _strip;
        private RectTransform _pointer;
        private float _markerRow = -1f;   // current marker row height, set by Layout
        private readonly Image[] _ticks = new Image[72];          // one per 5 degrees
        private readonly TMP_Text[] _labels = new TMP_Text[24];   // one per 15 degrees
        private readonly float[] _labelBase = new float[24];
        private TMP_Text _heading;
        private TMP_Text _focus;
        private RectTransform _focusRect;
        private string _shownHeading;
        private float _headingWidth;
        private bool _headingMeasuredWithDirection;
        private string _shownFocus;
        private object _focusKey;
        private float _focusAlpha, _focusSlide;
        private float _shown;                 // 0..1 whole-compass fade
        private float _offset, _offsetVel;    // smoothed top offset
        private bool _offsetSet;
        private Sprite _playerIcon;
        private Hud _hud;

        private readonly List<Target> _targets = new List<Target>();
        private readonly List<Target> _candidates = new List<Target>();
        private readonly Dictionary<object, View> _views = new Dictionary<object, View>();
        private readonly Stack<View> _pool = new Stack<View>();
        private readonly List<object> _released = new List<object>();
        private float _nextRefresh;

        /// <summary>Something that can be on the strip; position is read live each frame.</summary>
        private class Target
        {
            public object Key;
            public Transform Live;            // a nearby player
            public Minimap.PinData Pin;       // a pin (far players move smoothly on the map)
            public Sprite Icon;
            public Color Color;
            public string Name;
            public bool Friend, Ping;
            // Per frame.
            public Vector3 Pos;
            public float Delta, Distance, X, Alpha, Scale;
            public bool Clamped;
            public int Count;

            public Vector3 Position => Live != null ? Live.position : Pin != null ? Pin.m_pos : Pos;
        }

        /// <summary>One marker on screen, kept per target so it can ease in, out and between states.</summary>
        private class View
        {
            public RectTransform Rect;
            public Image Icon, Arrow, Chevron, Ring;
            public TMP_Text Count, Dist;
            public RectTransform CountRect;
            public Vector3 WorldPos;
            public bool Friend, Ping, Seen;
            public float Alpha, TargetAlpha;
            public float Scale, ScaleVel, TargetScale;
            public float ArrowAlpha, ChevronAlpha;
            public float CountPunch;
            public int ShownCount;
            public string ShownDist;
            public float PulsePhase;
        }

        internal static void Ensure()
        {
            if (_instance != null || _failed)
                return;
            var hud = Hud.instance;
            if (hud == null || hud.m_rootObject == null || hud.m_healthText == null || Player.m_localPlayer == null)
                return;
            try
            {
                Create(hud);
            }
            catch (System.Exception e)
            {
                _failed = true;
                Plugin.Log.LogError($"Compass disabled for this session: {e}");
            }
        }

        private static void Create(Hud hud)
        {
            _pins ??= AccessTools.FieldRefAccess<Minimap, List<Minimap.PinData>>("m_pins");
            if (_triangle == null)
                _triangle = TriangleSprite();
            if (_ring == null)
                _ring = RingSprite();

            var go = new GameObject("KroolWorldUI_Compass", typeof(RectTransform), typeof(CanvasGroup));
            go.transform.SetParent(hud.m_rootObject.transform, false);
            var c = go.AddComponent<CompassHud>();
            _instance = c;
            c._hud = hud;
            c._rect = (RectTransform)go.transform;
            c._rect.anchorMin = c._rect.anchorMax = new Vector2(0.5f, 1f);
            c._rect.pivot = new Vector2(0.5f, 1f);
            c._group = go.GetComponent<CanvasGroup>();
            c._group.alpha = 0f;
            c._group.blocksRaycasts = false;
            c._group.interactable = false;

            // The scale: a faint band behind labels and ticks, fading out at both ends.
            var band = new GameObject("band", typeof(RectTransform), typeof(RawImage));
            band.transform.SetParent(go.transform, false);
            c._strip = (RectTransform)band.transform;
            c._strip.anchorMin = new Vector2(0f, 1f);
            c._strip.anchorMax = new Vector2(1f, 1f);
            c._strip.pivot = new Vector2(0.5f, 1f);

            c._strip.sizeDelta = new Vector2(0f, LabelRow + TickRow);
            var raw = band.GetComponent<RawImage>();
            raw.texture = BandTexture();
            raw.raycastTarget = false;

            for (int i = 0; i < c._ticks.Length; i++)
            {
                int deg = i * 5;
                var tick = HudKit.NewImage("tick" + deg, c._strip, HudKit.Parchment);
                var rt = tick.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
                rt.pivot = new Vector2(0.5f, 0f);
                bool major = deg % 15 == 0;
                rt.sizeDelta = new Vector2(major ? 2f : 1f, major ? TickRow : TickRow * 0.55f);
                c._ticks[i] = tick;
            }
            for (int i = 0; i < c._labels.Length; i++)
            {
                int deg = i * 15;
                bool cardinal = deg % 90 == 0, inter = deg % 45 == 0;
                var label = HudKit.NewText(hud.m_healthText, c._strip, "label" + deg, TextAlignmentOptions.Bottom,
                    deg == 0 ? HudKit.Gold : cardinal || inter ? HudKit.Parchment : HudKit.Muted,
                    cardinal ? 18f : inter ? 14f : 12f);
                var rt = label.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
                rt.pivot = new Vector2(0.5f, 0f);
                rt.sizeDelta = new Vector2(40f, LabelRow);
                rt.anchoredPosition = new Vector2(0f, TickRow);
                label.text = Name(deg);
                c._labels[i] = label;
                c._labelBase[i] = label.color.a;
            }

            // Center mark: a small triangle under the scale pointing up at the current heading.
            var pointer = HudKit.NewImage("pointer", go.transform, HudKit.Gold);
            pointer.sprite = _triangle;
            var prt = pointer.rectTransform;
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 1f);
            prt.pivot = new Vector2(0.5f, 1f);
            prt.sizeDelta = new Vector2(PointerSize * 1.4f, PointerSize);
            c._pointer = prt;

            c._heading = HudKit.NewText(hud.m_healthText, go.transform, "heading", TextAlignmentOptions.Top,
                HudKit.Parchment, 15f);
            var hrt = c._heading.rectTransform;
            hrt.anchorMin = hrt.anchorMax = new Vector2(0.5f, 1f);
            hrt.pivot = new Vector2(0.5f, 1f);
            hrt.sizeDelta = new Vector2(80f, HeadingRow);


            // Name and distance of the marker you are looking at, to the right of the heading.
            c._focus = HudKit.NewText(hud.m_healthText, go.transform, "focus", TextAlignmentOptions.TopLeft,
                HudKit.Parchment, 13f);
            c._focusRect = c._focus.rectTransform;
            c._focusRect.anchorMin = c._focusRect.anchorMax = new Vector2(0.5f, 1f);
            c._focusRect.pivot = new Vector2(0f, 1f);
            c._focusRect.sizeDelta = new Vector2(300f, HeadingRow);

            c._focus.alpha = 0f;
        }

        private View NewView()
        {
            if (_pool.Count > 0)
            {
                var pooled = _pool.Pop();
                pooled.Rect.gameObject.SetActive(true);
                return pooled;
            }
            var icon = HudKit.NewImage("marker", transform, Color.white);
            icon.preserveAspect = true;
            var rt = icon.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(IconSize, IconSize);
            var outline = icon.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.6f);
            outline.effectDistance = new Vector2(1f, -1f);

            var ring = HudKit.NewImage("pulse", rt, Color.white);
            ring.sprite = _ring;
            ring.rectTransform.sizeDelta = new Vector2(IconSize, IconSize);
            ring.transform.SetAsFirstSibling();

            var arrow = HudKit.NewImage("height", rt, HudKit.Parchment);
            arrow.sprite = _triangle;
            var art = arrow.rectTransform;
            art.anchorMin = art.anchorMax = new Vector2(1f, 1f);
            art.sizeDelta = new Vector2(8f, 6f);
            art.anchoredPosition = new Vector2(1f, -2f);

            var chevron = HudKit.NewImage("edge", rt, HudKit.Parchment);
            chevron.sprite = _triangle;
            chevron.rectTransform.sizeDelta = new Vector2(9f, 7f);

            var count = HudKit.NewText(_hud.m_healthText, rt, "count", TextAlignmentOptions.BottomRight,
                HudKit.Parchment, 11f);
            var crt = count.rectTransform;
            crt.anchorMin = crt.anchorMax = new Vector2(1f, 0f);
            crt.pivot = new Vector2(1f, 0f);
            crt.sizeDelta = new Vector2(20f, 12f);
            crt.anchoredPosition = new Vector2(6f, -4f);

            var dist = HudKit.NewText(_hud.m_healthText, rt, "distance", TextAlignmentOptions.Top, HudKit.Muted, 10f);
            var drt = dist.rectTransform;
            drt.anchorMin = drt.anchorMax = new Vector2(0.5f, 0f);
            drt.pivot = new Vector2(0.5f, 1f);
            drt.sizeDelta = new Vector2(48f, 11f);
            drt.anchoredPosition = new Vector2(0f, 0f);

            return new View { Rect = rt, Icon = icon, Ring = ring, Arrow = arrow, Chevron = chevron, Count = count, CountRect = crt, Dist = dist };
        }

        private void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
        }

        /// <summary>
        /// World-space corners of the compass while it is turned on, so the buff icons can keep
        /// clear of it. Ignores its fades (map, menu), so the buffs do not jump around under them.
        /// </summary>
        internal static bool TryGetBounds(out Vector3 min, out Vector3 max)
        {
            min = max = Vector3.zero;
            var c = _instance;
            if (c == null || !Compass.Enabled.Value)
                return false;
            c._rect.GetWorldCorners(Corners);
            min = Corners[0];
            max = Corners[2];
            return true;
        }

        private void Update()
        {
            if (_failed)
                return;
            try
            {
                Tick();
            }
            catch (System.Exception e)
            {
                _failed = true;
                Plugin.Log.LogError($"Compass disabled for this session after an error: {e}");
                Destroy(gameObject);
            }
        }

        private void Tick()
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, MaxDt);
            var player = Player.m_localPlayer;
            var cam = GameCamera.instance;
            bool show = Compass.Enabled.Value && player != null && !player.IsDead() && cam != null
                        && !Minimap.IsOpen() && !Menu.IsVisible();
            _shown = Mathf.MoveTowards(_shown, show ? 1f : 0f, dt / 0.18f);
            _group.alpha = Compass.Opacity.Value * Ease(_shown);
            if (_shown <= 0f || player == null || cam == null)
                return;

            float size = Compass.Size.Value;
            float width = Compass.Width.Value;
            float fov = Compass.FieldOfView.Value;
            float half = fov * 0.5f;
            float pxPerDeg = width / fov;
            Layout(Compass.Distance.Value == Compass.DistanceMode.AllMarkers ? MarkerRowDistance : MarkerRowPlain, width);
            _rect.localScale = new Vector3(size, size, 1f);

            // Glide down under a boss bar or raid banner instead of jumping.
            float topTarget = TopOffset(size);
            if (!_offsetSet)
            {
                _offset = topTarget;
                _offsetSet = true;
            }
            _offset = Mathf.SmoothDamp(_offset, topTarget, ref _offsetVel, 0.15f, Mathf.Infinity, dt);
            _rect.anchoredPosition = new Vector2(0f, _offset);

            var fwd = cam.transform.forward;
            float heading = Mathf.Repeat(Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg, 360f);
            bool animate = Compass.Animations.Value;

            for (int i = 0; i < _ticks.Length; i++)
                PlaceScale(_ticks[i], Mathf.DeltaAngle(heading, i * 5f), half, pxPerDeg, 0.8f, 1f);
            for (int i = 0; i < _labels.Length; i++)
            {
                float delta = Mathf.DeltaAngle(heading, i * 15f);
                // Labels swell a little as they pass the center, like a lens.
                float lens = animate ? 1f + 0.18f * (1f - Mathf.SmoothStep(0f, 1f, Mathf.Abs(delta) / 14f)) : 1f;
                PlaceScale(_labels[i], delta, half, pxPerDeg, _labelBase[i], lens);
            }

            if (Time.time >= _nextRefresh)
            {
                _nextRefresh = Time.time + RefreshInterval;
                Collect(player);
            }
            var focus = UpdateMarkers(player, heading, half, pxPerDeg, width, dt, animate);
            UpdateReadout(heading, focus, dt, animate);
        }

        private static float Ease(float t) => t * t * (3f - 2f * t);

        /// <summary>Positions a scale element by its angle from the heading, fading near the ends.</summary>
        private static void PlaceScale(Graphic g, float delta, float half, float pxPerDeg, float alpha, float scale)
        {
            float edge = Mathf.Abs(delta) / half;
            bool on = edge <= 1f;
            var go = g.gameObject;
            if (go.activeSelf != on)
                go.SetActive(on);
            if (!on)
                return;
            var rt = g.rectTransform;
            rt.anchoredPosition = new Vector2(delta * pxPerDeg, rt.anchoredPosition.y);
            rt.localScale = new Vector3(scale, scale, 1f);
            var col = g.color;
            col.a = alpha * EdgeFade(edge);
            g.color = col;
        }

        private static float EdgeFade(float edge) =>
            1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(EdgeFadeStart, 1f, edge));

        /// <summary>Gathers friends and map pins worth showing; positions are read live per frame.</summary>
        private void Collect(Player player)
        {
            _targets.Clear();
            var map = Minimap.instance;
            var nearbyNames = new HashSet<string>();

            if (Compass.Friends.Value)
            {
                foreach (var other in Player.GetAllPlayers())
                {
                    if (other == null || other == player || other.IsDead())
                        continue;
                    string name = other.GetPlayerName();
                    nearbyNames.Add(name);
                    _targets.Add(new Target
                    {
                        Key = "friend:" + name, Live = other.transform, Icon = PlayerIcon(map),
                        Color = FriendColor(name), Name = name, Friend = true,
                    });
                }
            }
            if (map == null)
                return;

            float range = Compass.PinRange.Value;
            var origin = player.transform.position;
            foreach (var pin in _pins(map))
            {
                if (pin == null || pin.m_icon == null)
                    continue;
                bool wanted;
                switch (pin.m_type)
                {
                    case Minimap.PinType.Player:
                        // Far players visible on the map; nearby ones are already in from GetAllPlayers.
                        if (Compass.Friends.Value && !nearbyNames.Contains(pin.m_name))
                            _targets.Add(new Target
                            {
                                Key = "friend:" + pin.m_name, Pin = pin, Icon = pin.m_icon,
                                Color = FriendColor(pin.m_name), Name = pin.m_name, Friend = true,
                            });
                        continue;
                    case Minimap.PinType.Ping:
                        if (Compass.Pings.Value)
                            _targets.Add(new Target
                            {
                                Key = pin, Pin = pin, Icon = pin.m_icon, Color = Color.white,
                                Name = Localization.instance.Localize(pin.m_name), Ping = true,
                            });
                        continue;
                    case Minimap.PinType.Bed: wanted = Compass.Bed.Value; break;
                    case Minimap.PinType.Death: wanted = Compass.Tombstone.Value; break;
                    case Minimap.PinType.Boss: wanted = Compass.Bosses.Value; break;
                    case Minimap.PinType.Icon0:
                    case Minimap.PinType.Icon1:
                    case Minimap.PinType.Icon2:
                    case Minimap.PinType.Icon3:
                    case Minimap.PinType.Icon4:
                        wanted = Compass.Pins.Value; break;
                    default: wanted = false; break;   // shouts, events, Hildir quests
                }
                if (!wanted || Utils.DistanceXZ(pin.m_pos, origin) > range * 1.05f)
                    continue;
                _targets.Add(new Target { Key = pin, Pin = pin, Icon = pin.m_icon, Color = Color.white, Name = PinName(pin) });
            }
        }

        /// <summary>Places and animates markers; returns the one nearest the center, or null.</summary>
        private Target UpdateMarkers(Player player, float heading, float half, float pxPerDeg, float width,
            float dt, bool animate)
        {
            var origin = player.transform.position;
            float range = Compass.PinRange.Value;
            float edgeX = width * 0.5f - IconSize * 0.5f - 6f;

            _candidates.Clear();
            foreach (var t in _targets)
            {
                if (t.Live == null && t.Pin == null)
                    continue;
                t.Pos = t.Position;
                var to = t.Pos - origin;
                t.Distance = new Vector2(to.x, to.z).magnitude;
                t.Delta = Mathf.DeltaAngle(heading, Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg);
                float edge = Mathf.Abs(t.Delta) / half;
                if (edge > 1f && !t.Friend)
                    continue;
                // Friends behind you wait at the nearest edge; the rest fade near the ends.
                float x = t.Delta * pxPerDeg;
                t.Clamped = Mathf.Abs(x) > edgeX;
                t.X = Mathf.Clamp(x, -edgeX, edgeX);
                t.Alpha = t.Friend ? Mathf.Max(0.8f, EdgeFade(edge)) : EdgeFade(edge);
                if (!t.Friend && !t.Ping)
                    // Fade out over the last 15% of the range, so pins never pop at the limit.
                    t.Alpha *= 1f - Mathf.InverseLerp(range * 0.85f, range, t.Distance);
                t.Scale = t.Friend || t.Ping ? 1f : Mathf.Lerp(1f, 0.75f, Mathf.Clamp01(t.Distance / range));
                t.Count = 1;
                if (t.Alpha > 0.02f)
                    _candidates.Add(t);
            }

            // Pings and friends first, then nearest; a marker overlapping one already kept folds into its count.
            _candidates.Sort((a, b) =>
            {
                int pa = a.Ping ? 0 : a.Friend ? 1 : 2, pb = b.Ping ? 0 : b.Friend ? 1 : 2;
                return pa != pb ? pa.CompareTo(pb) : a.Distance.CompareTo(b.Distance);
            });
            int max = Compass.MaxMarkers.Value;
            bool allDistances = Compass.Distance.Value == Compass.DistanceMode.AllMarkers;
            // Distance numbers are wider than icons; give them room so they never overlap.
            float fold = allDistances ? 34f : IconSize * 0.75f;
            int kept = 0;
            for (int i = 0; i < _candidates.Count; i++)
            {
                var t = _candidates[i];
                int hit = -1;
                for (int j = 0; j < kept; j++)
                    if (Mathf.Abs(_candidates[j].X - t.X) < fold)
                    {
                        hit = j;
                        break;
                    }
                if (hit >= 0)
                    _candidates[hit].Count++;
                else if (kept < max)
                    _candidates[kept++] = t;
            }

            Target focus = null;
            float best = FocusDegrees;
            for (int i = 0; i < kept; i++)
                if (Mathf.Abs(_candidates[i].Delta) < best)
                {
                    best = Mathf.Abs(_candidates[i].Delta);
                    focus = _candidates[i];
                }

            foreach (var v in _views.Values)
                v.Seen = false;
            float heightArrow = Compass.HeightArrowMeters.Value;
            for (int i = 0; i < kept; i++)
            {
                var t = _candidates[i];
                if (!_views.TryGetValue(t.Key, out var v))
                {
                    v = NewView();
                    _views[t.Key] = v;
                    v.Alpha = 0f;
                    v.Scale = animate ? 0.4f : t.Scale;
                    v.ScaleVel = 0f;
                    v.ArrowAlpha = v.ChevronAlpha = 0f;
                    v.ShownCount = 1;
                    v.Count.text = "";
                    v.ShownDist = "";
                    v.Dist.text = "";
                    v.PulsePhase = Random.value;
                }
                v.Seen = true;
                v.WorldPos = t.Pos;
                v.Friend = t.Friend;
                v.Ping = t.Ping;
                v.TargetAlpha = t.Alpha;
                v.TargetScale = t.Scale * (animate && t == focus ? FocusScale : 1f);
                if (v.Icon.sprite != t.Icon)
                    v.Icon.sprite = t.Icon;
                v.Icon.color = new Color(t.Color.r, t.Color.g, t.Color.b, v.Icon.color.a);
                v.Rect.anchoredPosition = new Vector2(t.X, -(IconSize * 0.5f + 2f));

                float dy = t.Pos.y - origin.y;
                bool up = dy > 0f;
                float arrowTarget = heightArrow > 0f && Mathf.Abs(dy) >= heightArrow ? 1f : 0f;
                v.ArrowAlpha = Mathf.MoveTowards(v.ArrowAlpha, arrowTarget, dt / 0.2f);
                v.Arrow.rectTransform.localEulerAngles = new Vector3(0f, 0f, up ? 0f : 180f);

                // A chevron on the outer side of a friend held at the edge, pointing the way to turn.
                v.ChevronAlpha = Mathf.MoveTowards(v.ChevronAlpha, t.Clamped ? 1f : 0f, dt / 0.2f);
                bool right = t.X > 0f;
                var crt = v.Chevron.rectTransform;
                crt.anchoredPosition = new Vector2(right ? IconSize * 0.5f + 6f : -IconSize * 0.5f - 6f, 0f);
                crt.localEulerAngles = new Vector3(0f, 0f, right ? -90f : 90f);

                string d = allDistances ? FormatDistance(t.Distance) : "";
                if (d != v.ShownDist)
                {
                    v.ShownDist = d;
                    v.Dist.text = d;
                }

                if (t.Count != v.ShownCount)
                {
                    v.ShownCount = t.Count;
                    v.Count.text = t.Count > 1 ? t.Count.ToString() : "";
                    v.CountPunch = animate ? 1f : 0f;
                }
            }

            // Animate everything on screen, including markers on their way out.
            _released.Clear();
            foreach (var kv in _views)
            {
                var v = kv.Value;
                if (!v.Seen)
                {
                    v.TargetAlpha = 0f;
                    v.TargetScale = animate ? 0.6f : v.TargetScale;
                    // Keep tracking its bearing while it fades, so it never freezes in place.
                    var to = v.WorldPos - origin;
                    float delta = Mathf.DeltaAngle(heading, Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg);
                    v.Rect.anchoredPosition = new Vector2(Mathf.Clamp(delta * pxPerDeg, -width * 0.5f, width * 0.5f), -(IconSize * 0.5f + 2f));
                    v.ArrowAlpha = Mathf.MoveTowards(v.ArrowAlpha, 0f, dt / 0.2f);
                    v.ChevronAlpha = Mathf.MoveTowards(v.ChevronAlpha, 0f, dt / 0.2f);
                }
                Animate(v, dt, animate);
                if (!v.Seen && v.Alpha <= 0.01f)
                    _released.Add(kv.Key);
            }
            foreach (var key in _released)
            {
                var v = _views[key];
                _views.Remove(key);
                v.Rect.gameObject.SetActive(false);
                _pool.Push(v);
            }
            return focus;
        }

        private static void Animate(View v, float dt, bool animate)
        {
            // Fade in a touch faster than out.
            float rate = v.TargetAlpha > v.Alpha ? 10f : 8f;
            v.Alpha = Mathf.Lerp(v.Alpha, v.TargetAlpha, 1f - Mathf.Exp(-rate * dt));
            if (Mathf.Abs(v.Alpha - v.TargetAlpha) < 0.003f)
                v.Alpha = v.TargetAlpha;

            if (animate)
            {
                // A soft spring: a small overshoot on arrival and on focus, then settles.
                const float stiffness = 240f, damping = 20f;
                v.ScaleVel += (v.TargetScale - v.Scale) * stiffness * dt;
                v.ScaleVel *= Mathf.Exp(-damping * dt);
                v.Scale += v.ScaleVel * dt;
            }
            else
            {
                v.Scale = v.TargetScale;
            }
            v.Rect.localScale = new Vector3(v.Scale, v.Scale, 1f);

            var col = v.Icon.color;
            col.a = v.Alpha;
            v.Icon.color = col;
            SetAlpha(v.Arrow, v.ArrowAlpha * v.Alpha);
            SetAlpha(v.Chevron, v.ChevronAlpha * v.Alpha * 0.9f);

            v.CountPunch = Mathf.MoveTowards(v.CountPunch, 0f, dt / 0.25f);
            float punch = 1f + 0.5f * Ease(v.CountPunch);
            v.CountRect.localScale = new Vector3(punch, punch, 1f);
            v.Count.alpha = v.Alpha;
            v.Dist.alpha = v.Alpha * 0.9f;

            // Pings send out a slow ring, like a ripple on the map.
            bool pulse = v.Ping && animate;
            if (v.Ring.gameObject.activeSelf != pulse)
                v.Ring.gameObject.SetActive(pulse);
            if (pulse)
            {
                v.PulsePhase = Mathf.Repeat(v.PulsePhase + dt / 1.4f, 1f);
                float p = v.PulsePhase;
                float s = 1f + 1.3f * (1f - (1f - p) * (1f - p));
                v.Ring.rectTransform.localScale = new Vector3(s, s, 1f);
                SetAlpha(v.Ring, 0.7f * (1f - p) * (1f - p) * v.Alpha);
            }
        }

        private static void SetAlpha(Graphic g, float a)
        {
            bool on = a > 0.005f;
            if (g.gameObject.activeSelf != on)
                g.gameObject.SetActive(on);
            if (!on)
                return;
            var c = g.color;
            c.a = a;
            g.color = c;
        }

        /// <summary>Heading number, and the focused marker's name and distance easing in beside it.</summary>
        private void UpdateReadout(float heading, Target focus, float dt, bool animate)
        {
            bool headingOn = Compass.Heading.Value;
            if (_heading.gameObject.activeSelf != headingOn)
                _heading.gameObject.SetActive(headingOn);
            if (headingOn)
            {
                int h = Mathf.RoundToInt(heading) % 360;
                string text = Compass.HeadingDirection.Value
                    ? $"{h} <size=80%><color=#{ColorUtility.ToHtmlStringRGB(HudKit.Muted)}>{Direction(h)}</color></size>"
                    : h.ToString();
                if (text != _shownHeading)
                {
                    _shownHeading = text;
                    _heading.text = text;
                }
            }

            // The readout sits right of the heading, or under the center when the heading is off.
            // Measured on the widest heading for the style, so the name never shifts as the number changes.
            bool withDirection = Compass.HeadingDirection.Value;
            if (withDirection != _headingMeasuredWithDirection || _headingWidth <= 0f)
            {
                _headingMeasuredWithDirection = withDirection;
                _headingWidth = _heading.GetPreferredValues(withDirection ? "000 <size=80%>NW</size>" : "000").x;
            }
            float headingWidth = headingOn ? _headingWidth : 0f;
            var mode = Compass.Distance.Value;
            bool focusOn = focus != null && (Compass.FocusInfo.Value || mode != Compass.DistanceMode.Off);
            if (focusOn)
            {
                if (!Equals(focus.Key, _focusKey))
                {
                    _focusKey = focus.Key;
                    _focusSlide = animate ? 1f : 0f;
                    if (_focusAlpha > 0.5f && animate)
                        _focusAlpha = 0.35f;   // quick hand-off between two markers
                }
                string muted = ColorUtility.ToHtmlStringRGB(HudKit.Muted);
                string name = Compass.FocusInfo.Value ? focus.Name : null;
                string dist = mode != Compass.DistanceMode.Off ? FormatDistance(focus.Distance) : null;
                string more = Compass.FocusInfo.Value && focus.Count > 1 ? $"+{focus.Count - 1} more" : null;
                var parts = new List<string>(3);
                if (!string.IsNullOrEmpty(name))
                    parts.Add(name);
                if (dist != null)
                    parts.Add(parts.Count > 0 ? $"<color=#{muted}>{dist}</color>" : dist);
                if (more != null)
                    parts.Add($"<color=#{muted}><size=85%>{more}</size></color>");
                string text = string.Join("  ", parts);
                if (text != _shownFocus)
                {
                    _shownFocus = text;
                    _focus.text = text;
                }
                _focus.color = focus.Friend ? focus.Color : HudKit.Parchment;
            }
            else
            {
                _focusKey = null;
            }
            _focusAlpha = Mathf.MoveTowards(_focusAlpha, focusOn ? 1f : 0f, dt / (focusOn ? 0.15f : 0.25f));
            _focusSlide = Mathf.MoveTowards(_focusSlide, 0f, dt / 0.2f);
            _focus.alpha = Ease(_focusAlpha);
            float baseX = headingOn ? headingWidth * 0.5f + 10f : 0f;
            _focusRect.pivot = new Vector2(headingOn ? 0f : 0.5f, 1f);
            _focus.alignment = headingOn ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.Top;
            _focusRect.anchoredPosition = new Vector2(baseX - 10f * Ease(_focusSlide), _focusRect.anchoredPosition.y);
        }

        /// <summary>
        /// Top offset in the HUD: below the top edge, and below a boss health bar or raid banner
        /// when one is up, since both sit at the top center.
        /// </summary>
        private float TopOffset(float size)
        {
            float y = -8f - Compass.OffsetY.Value;
            var parent = _rect.parent as RectTransform;
            if (parent == null)
                return y;
            float lowest = float.MaxValue;
            var hud = Hud.instance;
            if (hud != null && hud.m_eventBar != null && hud.m_eventBar.activeInHierarchy)
                lowest = Mathf.Min(lowest, ScreenBottom(hud.m_eventBar.transform as RectTransform));
            var enemy = EnemyHud.instance;
            if (enemy != null && enemy.m_hudRoot != null && enemy.m_baseHudBoss != null && enemy.ShowingBossHud())
            {
                string boss = enemy.m_baseHudBoss.name;
                foreach (Transform child in enemy.m_hudRoot.transform)
                    if (child.gameObject.activeInHierarchy && child.name.StartsWith(boss))
                        lowest = Mathf.Min(lowest, ScreenBottom(child as RectTransform));
            }
            if (lowest == float.MaxValue)
                return y;
            var canvas = _rect.GetComponentInParent<Canvas>();
            var cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, new Vector2(Screen.width * 0.5f, lowest), cam, out var local))
                return y;
            // Anchored to the parent's top edge.
            return Mathf.Min(y, local.y - parent.rect.yMax - 6f);
        }

        private static float ScreenBottom(RectTransform rt)
        {
            if (rt == null)
                return float.MaxValue;
            var canvas = rt.GetComponentInParent<Canvas>();
            var cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            rt.GetWorldCorners(Corners);
            float min = float.MaxValue;
            foreach (var c in Corners)
                min = Mathf.Min(min, RectTransformUtility.WorldToScreenPoint(cam, c).y);
            return min;
        }

        /// <summary>Stacks the rows under the marker row; only touches the layout when its height changes.</summary>
        private void Layout(float markerRow, float width)
        {
            float height = markerRow + LabelRow + TickRow + PointerSize + HeadingRow;
            _rect.sizeDelta = new Vector2(width, height);
            if (Mathf.Approximately(markerRow, _markerRow))
                return;
            _markerRow = markerRow;
            _strip.anchoredPosition = new Vector2(0f, -markerRow);
            _pointer.anchoredPosition = new Vector2(0f, -(markerRow + LabelRow + TickRow));
            float readoutY = -(markerRow + LabelRow + TickRow + PointerSize);
            _heading.rectTransform.anchoredPosition = new Vector2(0f, readoutY);
            _focusRect.anchoredPosition = new Vector2(_focusRect.anchoredPosition.x, readoutY - 1f);
        }

        /// <summary>
        /// Rounded so it does not flicker as you walk: 1 m up close, 5 m past 50, 10 m past 200,
        /// then kilometers with one decimal.
        /// </summary>
        internal static string FormatDistance(float meters)
        {
            if (meters >= 1000f)
                return (meters / 1000f).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " km";
            float step = meters >= 200f ? 10f : meters >= 50f ? 5f : 1f;
            return $"{Mathf.RoundToInt(meters / step) * (int)step} m";
        }

        private static string Name(int deg)
        {
            switch (deg)
            {
                case 0: return "N";
                case 45: return "NE";
                case 90: return "E";
                case 135: return "SE";
                case 180: return "S";
                case 225: return "SW";
                case 270: return "W";
                case 315: return "NW";
                default: return deg.ToString();
            }
        }

        private static readonly string[] Winds = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };

        private static string Direction(int deg) => Winds[Mathf.RoundToInt(deg / 45f) % 8];

        private static string PinName(Minimap.PinData pin)
        {
            if (!string.IsNullOrEmpty(pin.m_name))
                return Localization.instance.Localize(pin.m_name);
            return pin.m_type == Minimap.PinType.Bed ? Localization.instance.Localize("$piece_bed") : "";
        }

        /// <summary>The map's own player icon.</summary>
        private Sprite PlayerIcon(Minimap map)
        {
            if (_playerIcon != null || map == null)
                return _playerIcon != null ? _playerIcon : _triangle;
            foreach (var s in map.m_icons)
                if (s.m_name == Minimap.PinType.Player)
                    return _playerIcon = s.m_icon;
            return _triangle;
        }

        private static Color FriendColor(string name)
        {
            int h = 0;
            foreach (char ch in name ?? "")
                h = h * 31 + ch;
            return FriendColors[(h & 0x7fffffff) % FriendColors.Length];
        }

        private static Sprite TriangleSprite()
        {
            const int w = 32, hgt = 24;
            var tex = new Texture2D(w, hgt, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            for (int y = 0; y < hgt; y++)
                for (int x = 0; x < w; x++)
                {
                    // Point up: full width at the bottom row, a point at the top; 1 px soft edge.
                    float halfW = (1f - (y + 0.5f) / hgt) * w * 0.5f;
                    float d = halfW - Mathf.Abs(x + 0.5f - w * 0.5f);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(d)));
                }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, w, hgt), new Vector2(0.5f, 0.5f));
        }

        private static Sprite RingSprite()
        {
            const int n = 64;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            float r = n * 0.5f - 2f, thick = 3f;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n * 0.5f, n * 0.5f));
                    float a = Mathf.Clamp01(thick * 0.5f - Mathf.Abs(d - (r - thick * 0.5f)) + 0.5f);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));
        }

        /// <summary>A faint dark band, strongest in the middle and gone at the ends.</summary>
        private static Texture2D BandTexture()
        {
            const int w = 128;
            var tex = new Texture2D(w, 1, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp,
            };
            for (int x = 0; x < w; x++)
            {
                float edge = Mathf.Abs((x + 0.5f) / w * 2f - 1f);
                tex.SetPixel(x, 0, new Color(0f, 0f, 0f, 0.35f * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.5f, 1f, edge)))));
            }
            tex.Apply();
            return tex;
        }
    }
}
