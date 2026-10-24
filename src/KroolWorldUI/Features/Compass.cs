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
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> FieldOfView;
        internal static ConfigEntry<float> Width;
        internal static ConfigEntry<bool> Heading;
        internal static ConfigEntry<bool> Friends;
        internal static ConfigEntry<bool> Pins;
        internal static ConfigEntry<float> PinRange;
        internal static ConfigEntry<float> HeightArrowMeters;
        internal static ConfigEntry<float> Opacity;
        internal static ConfigEntry<float> OffsetY;

        private const string Section = "Compass";

        internal static void BindConfig(ConfigFile config)
        {
            Enabled = config.Bind(Section, "Enabled", true,
                "A compass strip at the top of the screen with your heading, friends and nearby map pins.");
            FieldOfView = config.Bind(Section, "FieldOfView", 120f,
                new ConfigDescription("How many degrees of the compass the strip shows. Narrower is easier to read.",
                    new AcceptableValueRange<float>(60f, 240f)));
            Width = config.Bind(Section, "Width", 520f,
                new ConfigDescription("Strip width in HUD pixels.", new AcceptableValueRange<float>(300f, 1000f)));
            Heading = config.Bind(Section, "Heading", true,
                "Your exact heading under the center mark (0 = north, 90 = east), for call-outs. " +
                "The marker nearest the center shows its name and distance beside it.");
            Friends = config.Bind(Section, "Friends", true,
                "Other players. Always shown, held at the strip's edge when they are behind you. " +
                "Far-away players show only if they are visible on the map.");
            Pins = config.Bind(Section, "Pins", true,
                "Map pins within Pin Range: your own pins, bench pins, bed, tombstone, bosses, and map pings.");
            PinRange = config.Bind(Section, "PinRange", 300f,
                new ConfigDescription("Map pins farther than this (meters) are left off. Pings always show.",
                    new AcceptableValueRange<float>(50f, 2000f)));
            HeightArrowMeters = config.Bind(Section, "HeightArrowMeters", 10f,
                new ConfigDescription("A small up or down arrow on markers this far above or below you. 0 = off.",
                    new AcceptableValueRange<float>(0f, 50f)));
            Opacity = config.Bind(Section, "Opacity", 0.9f,
                new ConfigDescription("Overall opacity of the compass.", new AcceptableValueRange<float>(0.2f, 1f)));
            OffsetY = config.Bind(Section, "OffsetY", 0f,
                new ConfigDescription("Nudge down from the top of the screen, in HUD pixels.",
                    new AcceptableValueRange<float>(0f, 300f)));
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
        // Layout, top to bottom, in HUD pixels.
        private const float MarkerRow = 22f;     // icons
        private const float LabelRow = 18f;      // N, NE, 15, 30...
        private const float TickRow = 7f;
        private const float PointerSize = 9f;
        private const float HeadingRow = 18f;
        private const float Height = MarkerRow + LabelRow + TickRow + PointerSize + HeadingRow;
        private const float IconSize = 20f;
        private const float EdgeFadeStart = 0.7f;   // fraction of the half-window where fading starts
        private const float RefreshInterval = 0.25f;
        private const int MaxMarkers = 16;

        private static readonly Color[] FriendColors =
        {
            new Color(0.40f, 0.78f, 1f), new Color(0.55f, 0.90f, 0.45f), new Color(1f, 0.60f, 0.85f),
            new Color(0.75f, 0.60f, 1f), new Color(1f, 0.85f, 0.35f), new Color(0.45f, 0.95f, 0.85f),
        };

        private static CompassHud _instance;
        private static bool _failed;
        private static Sprite _triangle;
        private static AccessTools.FieldRef<Minimap, List<Minimap.PinData>> _pins;

        private RectTransform _rect;
        private CanvasGroup _group;
        private RectTransform _strip;
        private readonly Image[] _ticks = new Image[72];          // one per 5 degrees
        private readonly TMP_Text[] _labels = new TMP_Text[24];   // one per 15 degrees
        private TMP_Text _heading;
        private TMP_Text _focus;
        private int _shownHeading = -1;
        private string _shownFocus;
        private Marker[] _markers;
        private readonly List<Target> _targets = new List<Target>();
        private readonly List<Target> _visible = new List<Target>();
        private float _nextRefresh;

        private struct Target
        {
            public Vector3 Pos;
            public Sprite Icon;
            public Color Color;
            public string Name;
            public bool Friend;
            public bool Ping;
            // Filled per frame.
            public float Delta, Distance, X, Alpha, Scale;
            public int Count;
        }

        private class Marker
        {
            public RectTransform Rect;
            public Image Icon;
            public Image Arrow;
            public TMP_Text Count;
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

            var go = new GameObject("KroolWorldUI_Compass", typeof(RectTransform), typeof(CanvasGroup));
            go.transform.SetParent(hud.m_rootObject.transform, false);
            var c = go.AddComponent<CompassHud>();
            _instance = c;
            c._rect = (RectTransform)go.transform;
            c._rect.anchorMin = c._rect.anchorMax = new Vector2(0.5f, 1f);
            c._rect.pivot = new Vector2(0.5f, 1f);
            c._group = go.GetComponent<CanvasGroup>();
            c._group.blocksRaycasts = false;
            c._group.interactable = false;

            // The scale: a faint band behind labels and ticks, fading out at both ends.
            var band = new GameObject("band", typeof(RectTransform), typeof(RawImage));
            band.transform.SetParent(go.transform, false);
            c._strip = (RectTransform)band.transform;
            c._strip.anchorMin = new Vector2(0f, 1f);
            c._strip.anchorMax = new Vector2(1f, 1f);
            c._strip.pivot = new Vector2(0.5f, 1f);
            c._strip.anchoredPosition = new Vector2(0f, -MarkerRow);
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
                var label = HudKit.NewText(hud.m_healthText, c._strip, "label" + deg, TextAlignmentOptions.Center,
                    deg == 0 ? HudKit.Gold : cardinal ? HudKit.Parchment : inter ? HudKit.Parchment : HudKit.Muted,
                    cardinal ? 18f : inter ? 14f : 12f);
                var rt = label.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
                rt.pivot = new Vector2(0.5f, 0f);
                rt.sizeDelta = new Vector2(40f, LabelRow);
                rt.anchoredPosition = new Vector2(0f, TickRow);
                label.text = Name(deg);
                c._labels[i] = label;
            }

            // Center mark: a small triangle under the scale pointing up at the current heading.
            var pointer = HudKit.NewImage("pointer", go.transform, HudKit.Gold);
            pointer.sprite = _triangle;
            var prt = pointer.rectTransform;
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 1f);
            prt.pivot = new Vector2(0.5f, 1f);
            prt.sizeDelta = new Vector2(PointerSize * 1.4f, PointerSize);
            prt.anchoredPosition = new Vector2(0f, -(MarkerRow + LabelRow + TickRow));

            c._heading = HudKit.NewText(hud.m_healthText, go.transform, "heading", TextAlignmentOptions.Top,
                HudKit.Parchment, 15f);
            var hrt = c._heading.rectTransform;
            hrt.anchorMin = hrt.anchorMax = new Vector2(0.5f, 1f);
            hrt.pivot = new Vector2(0.5f, 1f);
            hrt.sizeDelta = new Vector2(400f, HeadingRow);
            hrt.anchoredPosition = new Vector2(0f, -(MarkerRow + LabelRow + TickRow + PointerSize));
            hrt.sizeDelta = new Vector2(40f, HeadingRow);

            // Name and distance of the marker nearest the center, to the right of the heading.
            c._focus = HudKit.NewText(hud.m_healthText, go.transform, "focus", TextAlignmentOptions.TopLeft,
                HudKit.Muted, 13f);
            var frt = c._focus.rectTransform;
            frt.anchorMin = frt.anchorMax = new Vector2(0.5f, 1f);
            frt.pivot = new Vector2(0f, 1f);
            frt.sizeDelta = new Vector2(300f, HeadingRow);
            frt.anchoredPosition = new Vector2(24f, -(MarkerRow + LabelRow + TickRow + PointerSize) - 1f);

            c._markers = new Marker[MaxMarkers];
            for (int i = 0; i < MaxMarkers; i++)
            {
                var icon = HudKit.NewImage("marker" + i, go.transform, Color.white);
                icon.preserveAspect = true;
                var rt = icon.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(IconSize, IconSize);
                var outline = icon.gameObject.AddComponent<Outline>();
                outline.effectColor = new Color(0f, 0f, 0f, 0.6f);
                outline.effectDistance = new Vector2(1f, -1f);

                var arrow = HudKit.NewImage("height", rt, HudKit.Parchment);
                arrow.sprite = _triangle;
                var art = arrow.rectTransform;
                art.anchorMin = art.anchorMax = new Vector2(1f, 1f);
                art.pivot = new Vector2(0.5f, 0.5f);
                art.sizeDelta = new Vector2(8f, 6f);
                art.anchoredPosition = new Vector2(1f, -2f);

                var count = HudKit.NewText(hud.m_healthText, rt, "count", TextAlignmentOptions.BottomRight,
                    HudKit.Parchment, 11f);
                var crt = count.rectTransform;
                crt.anchorMin = crt.anchorMax = new Vector2(1f, 0f);
                crt.pivot = new Vector2(1f, 0f);
                crt.sizeDelta = new Vector2(20f, 12f);
                crt.anchoredPosition = new Vector2(6f, -4f);

                c._markers[i] = new Marker { Rect = rt, Icon = icon, Arrow = arrow, Count = count };
                icon.gameObject.SetActive(false);
            }
        }

        private void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
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
            var player = Player.m_localPlayer;
            var cam = GameCamera.instance;
            bool show = Compass.Enabled.Value && player != null && !player.IsDead() && cam != null
                        && !Minimap.IsOpen() && !Menu.IsVisible();
            if (!show)
            {
                _group.alpha = 0f;
                return;
            }
            _group.alpha = Compass.Opacity.Value;

            float width = Compass.Width.Value;
            float fov = Compass.FieldOfView.Value;
            float half = fov * 0.5f;
            float pxPerDeg = width / fov;
            _rect.sizeDelta = new Vector2(width, Height);
            _rect.anchoredPosition = new Vector2(0f, TopOffset());

            var fwd = cam.transform.forward;
            float heading = Mathf.Repeat(Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg, 360f);

            for (int i = 0; i < _ticks.Length; i++)
                Place(_ticks[i].rectTransform, _ticks[i].gameObject, Mathf.DeltaAngle(heading, i * 5f), half, pxPerDeg, 0.8f);
            for (int i = 0; i < _labels.Length; i++)
                Place(_labels[i].rectTransform, _labels[i].gameObject, Mathf.DeltaAngle(heading, i * 15f), half, pxPerDeg, 1f);

            if (Time.time >= _nextRefresh)
            {
                _nextRefresh = Time.time + RefreshInterval;
                Collect(player);
            }
            string focus = UpdateMarkers(player, heading, half, pxPerDeg, width);

            int h = Mathf.RoundToInt(heading) % 360;
            bool headingOn = Compass.Heading.Value;
            if (_heading.gameObject.activeSelf != headingOn)
            {
                _heading.gameObject.SetActive(headingOn);
                _focus.gameObject.SetActive(headingOn);
            }
            if (headingOn && h != _shownHeading)
            {
                _shownHeading = h;
                _heading.text = h.ToString();
            }
            if (headingOn && focus != _shownFocus)
            {
                _shownFocus = focus;
                _focus.text = focus ?? "";
            }
        }

        /// <summary>Positions a scale element by its angle from the heading, fading near the ends.</summary>
        private static void Place(RectTransform rt, GameObject go, float delta, float half, float pxPerDeg, float alpha)
        {
            float edge = Mathf.Abs(delta) / half;
            bool on = edge <= 1f;
            if (go.activeSelf != on)
                go.SetActive(on);
            if (!on)
                return;
            rt.anchoredPosition = new Vector2(delta * pxPerDeg, rt.anchoredPosition.y);
            var g = go.GetComponent<Graphic>();
            var col = g.color;
            col.a = alpha * EdgeFade(edge);
            g.color = col;
        }

        private static float EdgeFade(float edge) =>
            1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(EdgeFadeStart, 1f, edge));

        /// <summary>Gathers friends and map pins worth showing; positions only, angles come per frame.</summary>
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
                        Pos = other.transform.position, Icon = PlayerIcon(map), Color = FriendColor(name),
                        Name = name, Friend = true,
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
                switch (pin.m_type)
                {
                    case Minimap.PinType.Player:
                        // Far players visible on the map; nearby ones are already in from GetAllPlayers.
                        if (!Compass.Friends.Value || nearbyNames.Contains(pin.m_name))
                            continue;
                        _targets.Add(new Target
                        {
                            Pos = pin.m_pos, Icon = pin.m_icon, Color = FriendColor(pin.m_name),
                            Name = pin.m_name, Friend = true,
                        });
                        continue;
                    case Minimap.PinType.Ping:
                        if (!Compass.Pins.Value)
                            continue;
                        _targets.Add(new Target
                        {
                            Pos = pin.m_pos, Icon = pin.m_icon, Color = Color.white,
                            Name = Localization.instance.Localize(pin.m_name), Ping = true,
                        });
                        continue;
                    case Minimap.PinType.Shout:
                    case Minimap.PinType.None:
                    case Minimap.PinType.RandomEvent:
                    case Minimap.PinType.EventArea:
                        continue;
                }
                if (!Compass.Pins.Value || Utils.DistanceXZ(pin.m_pos, origin) > range)
                    continue;
                _targets.Add(new Target
                {
                    Pos = pin.m_pos, Icon = pin.m_icon, Color = Color.white, Name = PinName(pin),
                });
            }
        }

        /// <summary>Places markers; returns "name  120 m" for the one nearest the center, or null.</summary>
        private string UpdateMarkers(Player player, float heading, float half, float pxPerDeg, float width)
        {
            var origin = player.transform.position;
            float range = Compass.PinRange.Value;
            float edgeX = width * 0.5f - IconSize * 0.5f;
            _visible.Clear();
            foreach (var t0 in _targets)
            {
                var t = t0;
                var to = t.Pos - origin;
                t.Distance = new Vector2(to.x, to.z).magnitude;
                float bearing = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
                t.Delta = Mathf.DeltaAngle(heading, bearing);
                float edge = Mathf.Abs(t.Delta) / half;
                if (edge > 1f && !t.Friend)
                    continue;
                // Friends behind you wait at the nearest edge; the rest fade near the ends.
                t.X = Mathf.Clamp(t.Delta * pxPerDeg, -edgeX, edgeX);
                t.Alpha = t.Friend ? Mathf.Max(0.75f, EdgeFade(edge)) : EdgeFade(edge);
                if (!t.Friend && !t.Ping)
                    // Fade out over the last 15% of the range, so pins do not pop at the limit.
                    t.Alpha *= 1f - Mathf.InverseLerp(range * 0.85f, range, t.Distance);
                t.Scale = t.Friend || t.Ping ? 1f : Mathf.Lerp(1f, 0.75f, t.Distance / range);
                t.Count = 1;
                if (t.Alpha > 0.02f)
                    _visible.Add(t);
            }

            // Pings and friends first, then nearest; a marker overlapping one already placed folds into its count.
            _visible.Sort((a, b) =>
            {
                int pa = a.Ping ? 0 : a.Friend ? 1 : 2, pb = b.Ping ? 0 : b.Friend ? 1 : 2;
                return pa != pb ? pa.CompareTo(pb) : a.Distance.CompareTo(b.Distance);
            });
            int placed = 0;
            for (int i = 0; i < _visible.Count; i++)
            {
                var t = _visible[i];
                int hit = -1;
                for (int j = 0; j < placed; j++)
                    if (Mathf.Abs(_visible[j].X - t.X) < IconSize * 0.7f)
                    {
                        hit = j;
                        break;
                    }
                if (hit >= 0)
                {
                    var keep = _visible[hit];
                    keep.Count++;
                    _visible[hit] = keep;
                    continue;
                }
                if (placed >= MaxMarkers)
                    continue;
                _visible[placed++] = t;
            }

            string focus = null;
            float bestDelta = 6f;   // degrees either side of center that count as "looking at it"
            float heightArrow = Compass.HeightArrowMeters.Value;
            for (int i = 0; i < MaxMarkers; i++)
            {
                var m = _markers[i];
                bool on = i < placed;
                if (m.Icon.gameObject.activeSelf != on)
                    m.Icon.gameObject.SetActive(on);
                if (!on)
                    continue;
                var t = _visible[i];
                m.Rect.anchoredPosition = new Vector2(t.X, -MarkerRow * 0.5f);
                m.Rect.localScale = new Vector3(t.Scale, t.Scale, 1f);
                if (m.Icon.sprite != t.Icon)
                    m.Icon.sprite = t.Icon;
                var col = t.Color;
                col.a = t.Alpha;
                m.Icon.color = col;

                float dy = t.Pos.y - origin.y;
                bool arrow = heightArrow > 0f && Mathf.Abs(dy) >= heightArrow && t.Distance < 2000f;
                if (m.Arrow.gameObject.activeSelf != arrow)
                    m.Arrow.gameObject.SetActive(arrow);
                if (arrow)
                    m.Arrow.rectTransform.localEulerAngles = new Vector3(0f, 0f, dy > 0f ? 0f : 180f);

                string count = t.Count > 1 ? t.Count.ToString() : "";
                if (m.Count.text != count)
                    m.Count.text = count;

                if (Mathf.Abs(t.Delta) < bestDelta)
                {
                    bestDelta = Mathf.Abs(t.Delta);
                    string dist = $"{Mathf.RoundToInt(t.Distance)} m";
                    focus = string.IsNullOrEmpty(t.Name) ? dist : $"{t.Name}  {dist}";
                }
            }
            return focus;
        }

        /// <summary>
        /// Top offset in the HUD: below the top edge, and below a boss health bar or raid banner
        /// when one is up, since both sit at the top center.
        /// </summary>
        private float TopOffset()
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

        private static readonly Vector3[] Corners = new Vector3[4];

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

        private static string PinName(Minimap.PinData pin)
        {
            if (!string.IsNullOrEmpty(pin.m_name))
                return Localization.instance.Localize(pin.m_name);
            return pin.m_type == Minimap.PinType.Bed ? Localization.instance.Localize("$piece_bed") : "";
        }

        private Sprite _playerIcon;

        /// <summary>The map's own player icon, taken from any player pin once one exists.</summary>
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
