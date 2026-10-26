using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KroolWorldUI.Features
{
    /// <summary>
    /// A day and night strip, midnight to midnight: night at both ends with a moon, day in the
    /// middle with a sun, a marker for now, and the time until dusk or dawn. Sits on the minimap
    /// by default, follows it, and keeps its place when there is no minimap (No Map worlds).
    /// Timing is the game's own (EnvMan): the cycle is m_dayLengthSec long (1200 s by default),
    /// counted from midnight on ZNet's synced clock; dawn is at 15% of it and dusk at 85%
    /// (EnvMan rescales those to the sun's 0.25 and 0.75). Read-only.
    /// </summary>
    internal static class DayNightBar
    {
        internal enum Place { AboveMinimap, TopCenter, Custom }

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> Countdown;
        internal static ConfigEntry<float> DuskWarningSeconds;
        internal static ConfigEntry<bool> DayNumber;
        internal static ConfigEntry<bool> Clock;
        internal static ConfigEntry<int> BarHeight;
        internal static ConfigEntry<float> Size;
        internal static ConfigEntry<float> Opacity;
        internal static ConfigEntry<Color> DayColor;
        internal static ConfigEntry<Color> NightColor;
        internal static ConfigEntry<Place> Placement;
        internal static ConfigEntry<float> Width;
        internal static ConfigEntry<float> AnchorX;
        internal static ConfigEntry<float> AnchorY;
        internal static ConfigEntry<float> OffsetX;
        internal static ConfigEntry<float> OffsetY;

        private const string Section = "Day Night Bar";

        internal const float Dawn = 0.15f;
        internal const float Dusk = 0.85f;

        internal static void BindConfig(ConfigFile config)
        {
            Enabled = config.Bind(Section, "Enabled", true,
                "A day and night bar on the minimap: the time of day and how long until dusk or dawn.");

            const string text = "Text";
            Countdown = config.Bind(Section, "Countdown", true,
                ConfigText.Desc("Time left until dusk by day, or dawn by night.", group: text));
            DuskWarningSeconds = config.Bind(Section, "DuskWarningSeconds", 60f,
                ConfigText.Desc("The dusk countdown turns orange this many seconds before night. 0 = never.",
                    new AcceptableValueRange<float>(0f, 300f), text, () => Countdown.Value, "Countdown is on", 5f));
            DayNumber = config.Bind(Section, "DayNumber", true, ConfigText.Desc("The day number.", group: text));
            Clock = config.Bind(Section, "Clock", false,
                ConfigText.Desc("The time as a 24-hour clock. 06:00 is sunrise, 18:00 sunset.", group: text));

            const string look = "Look";
            BarHeight = config.Bind(Section, "BarHeight", 10,
                ConfigText.Desc("Bar height in HUD pixels.", new AcceptableValueRange<int>(6, 18), look));
            Size = config.Bind(Section, "Size", 1f,
                ConfigText.Desc("Text and icon size.", new AcceptableValueRange<float>(0.75f, 1.5f), look, step: 0.05f));
            Opacity = config.Bind(Section, "Opacity", 1f,
                ConfigText.Desc("1 is solid; lower shows the game through it.", new AcceptableValueRange<float>(0.2f, 1f), look, step: 0.05f));
            DayColor = config.Bind(Section, "DayColor", new Color(0.86f, 0.68f, 0.32f, 0.85f),
                ConfigText.Desc("Color of the day part.", group: look));
            NightColor = config.Bind(Section, "NightColor", new Color(0.13f, 0.17f, 0.36f, 0.9f),
                ConfigText.Desc("Color of the night part.", group: look));

            const string placement = "Placement";
            Placement = config.Bind(Section, "Placement", Place.AboveMinimap,
                ConfigText.Desc("Above Minimap: along its top edge, or the top right corner without a minimap. " +
                                "Top Center: under the compass. Custom: at Anchor X and Anchor Y.", group: placement));
            Width = config.Bind(Section, "Width", 220f,
                ConfigText.Desc("Bar width in HUD pixels. On the minimap it matches the minimap instead.",
                    new AcceptableValueRange<float>(120f, 600f), placement, step: 10f));
            AnchorX = config.Bind(Section, "AnchorX", 0.85f,
                ConfigText.Desc("Center of the bar. 0 is the left of the screen, 1 the right.",
                    new AcceptableValueRange<float>(0f, 1f), placement,
                    () => Placement.Value == Place.Custom, "Placement is Custom", 0.01f));
            AnchorY = config.Bind(Section, "AnchorY", 0.95f,
                ConfigText.Desc("Bottom of the bar. 0 is the bottom of the screen, 1 the top.",
                    new AcceptableValueRange<float>(0f, 1f), placement,
                    () => Placement.Value == Place.Custom, "Placement is Custom", 0.01f));
            OffsetX = config.Bind(Section, "OffsetX", 0f,
                ConfigText.Desc("Move left (-) or right (+), in HUD pixels.",
                    new AcceptableValueRange<float>(-400f, 400f), placement));
            OffsetY = config.Bind(Section, "OffsetY", 0f,
                ConfigText.Desc("Move down (-) or up (+), in HUD pixels.",
                    new AcceptableValueRange<float>(-400f, 400f), placement));
        }

        /// <summary>Where in the cycle we are (0 = midnight), the cycle length in seconds, and the day number.</summary>
        internal static bool TryGetTime(out float fraction, out float dayLength, out int day)
        {
            fraction = 0f;
            dayLength = 0f;
            day = 0;
            var env = EnvMan.instance;
            var net = ZNet.instance;
            if (env == null || net == null || env.m_dayLengthSec <= 0)
                return false;
            double now = net.GetTimeSeconds();
            dayLength = env.m_dayLengthSec;
            fraction = (float)(now % dayLength / dayLength);
            day = env.GetDay();
            return true;
        }

        /// <summary>EnvMan.RescaleDayFraction: the sun's position, where 0.25 is sunrise and 0.75 sunset.</summary>
        internal static float SunFraction(float f)
        {
            if (f >= Dawn && f <= Dusk)
                return 0.25f + (f - Dawn) / (Dusk - Dawn) * 0.5f;
            if (f < 0.5f)
                return f / Dawn * 0.25f;
            return 0.75f + (f - Dusk) / (1f - Dusk) * 0.25f;
        }

        [HarmonyPatch(typeof(Hud), "Update")]
        private static class EnsureOnHud
        {
            private static void Postfix()
            {
                if (Enabled.Value)
                    DayNightBarHud.Ensure();
            }
        }
    }

    internal class DayNightBarHud : MonoBehaviour
    {
        private const float Inset = 6f;      // the minimap's own frame
        private const float Above = 6f;      // gap between the minimap and the bar
        private const float TextHeight = 18f;
        private const float TextGap = 1f;
        private const float LabelGap = 10f;  // least space kept between the three labels
        private const float CornerMargin = 24f;
        private const int StripWidth = 256;

        private static readonly Color NightText = new Color(0.7f, 0.78f, 1f, 1f);

        private static DayNightBarHud _instance;
        private static readonly HudKit.FailFlag _failed = new HudKit.FailFlag();
        private static Texture2D _sun, _moon;
        private static readonly Vector3[] Corners = new Vector3[4];

        private RectTransform _rect;
        private CanvasGroup _group;
        private RawImage _strip;
        private Texture2D _stripTex;
        private Color _stripDay, _stripNight;
        private RectTransform _sunIcon, _moonLeft, _moonRight, _marker;
        private TMP_Text _left, _center, _right;
        private readonly List<Material> _materials = new List<Material>();
        private int _height = -1;
        private float _size = -1f;

        internal static void Ensure()
        {
            if (_instance != null || _failed.IsSet)
                return;
            var hud = Hud.instance;
            if (hud == null || hud.m_rootObject == null || hud.m_healthText == null || Player.m_localPlayer == null)
                return;
            try
            {
                var go = new GameObject("KroolWorldUI_DayNightBar", typeof(RectTransform), typeof(CanvasGroup));
                go.transform.SetParent(hud.m_rootObject.transform, false);
                var b = go.AddComponent<DayNightBarHud>();
                _instance = b;
                b.Build(hud.m_healthText);
            }
            catch (System.Exception e)
            {
                _failed.Set();
                Plugin.Log.LogError($"Day night bar disabled for this session: {e}");
            }
        }

        private void OnDestroy()
        {
            HudKit.Free(_stripTex);
            foreach (var m in _materials)
                HudKit.Free(m);
            if (_instance == this)
                _instance = null;
        }

        private void Build(TMP_Text template)
        {
            _rect = (RectTransform)transform;
            _rect.pivot = new Vector2(0.5f, 0f);
            _group = GetComponent<CanvasGroup>();
            _group.alpha = 0f;
            _group.blocksRaycasts = false;
            _group.interactable = false;

            var stripGo = new GameObject("strip", typeof(RectTransform), typeof(RawImage));
            stripGo.transform.SetParent(transform, false);
            _strip = stripGo.GetComponent<RawImage>();
            _strip.raycastTarget = false;
            var frame = stripGo.AddComponent<Outline>();
            frame.effectColor = new Color(0f, 0f, 0f, 0.6f);
            frame.effectDistance = new Vector2(1f, -1f);
            var srt = _strip.rectTransform;
            srt.anchorMin = new Vector2(0f, 0f);
            srt.anchorMax = new Vector2(1f, 0f);
            srt.pivot = new Vector2(0.5f, 0f);
            srt.anchoredPosition = Vector2.zero;

            if (_sun == null) _sun = SunTexture();
            if (_moon == null) _moon = MoonTexture();
            _sunIcon = Icon("sun", _sun, new Color(1f, 0.86f, 0.45f, 1f));
            _moonLeft = Icon("moon", _moon, new Color(0.82f, 0.88f, 1f, 1f));
            _moonRight = Icon("moon", _moon, new Color(0.82f, 0.88f, 1f, 1f));

            var markerImg = HudKit.NewImage("now", transform, Color.white);
            markerImg.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.85f);
            _marker = markerImg.rectTransform;
            _marker.anchorMin = _marker.anchorMax = new Vector2(0f, 0f);
            _marker.pivot = new Vector2(0.5f, 0.5f);

            _left = Label(template, "day", TextAlignmentOptions.BottomLeft);
            _center = Label(template, "clock", TextAlignmentOptions.Bottom);
            _right = Label(template, "countdown", TextAlignmentOptions.BottomRight);
        }

        private RectTransform Icon(string name, Texture2D tex, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(RawImage));
            go.transform.SetParent(transform, false);
            var img = go.GetComponent<RawImage>();
            img.texture = tex;
            img.color = color;
            img.raycastTarget = false;
            var rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            return rt;
        }

        private TMP_Text Label(TMP_Text template, string name, TextAlignmentOptions align)
        {
            var text = HudKit.NewText(template, transform, name, align, HudKit.Parchment, 14f);
            var rt = text.rectTransform;
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            // A dark outline so the text reads over bright sky and snow (fontMaterial is this text's own copy).
            var mat = text.fontMaterial;
            _materials.Add(mat);
            mat.EnableKeyword("OUTLINE_ON");
            text.outlineWidth = 0.2f;
            text.outlineColor = new Color32(0, 0, 0, 220);
            return text;
        }

        private void LateUpdate()
        {
            if (_failed.IsSet)
                return;
            try
            {
                Tick();
            }
            catch (System.Exception e)
            {
                _failed.Set();
                Plugin.Log.LogError($"Day night bar disabled for this session after an error: {e}");
                Destroy(gameObject);
            }
        }

        private void Tick()
        {
            bool on = DayNightBar.Enabled.Value && HudKit.Allowed && Player.m_localPlayer != null
                      && !Minimap.IsOpen() && !Menu.IsVisible()
                      && DayNightBar.TryGetTime(out _, out _, out _);
            _group.alpha = Mathf.MoveTowards(_group.alpha, on ? DayNightBar.Opacity.Value : 0f, Time.unscaledDeltaTime * 6f);
            if (!on)
                return; // hidden, or fading out on its last frame

            DayNightBar.TryGetTime(out float f, out float dayLength, out int day);
            Layout();
            Place();
            UpdateStrip();

            _marker.anchorMin = _marker.anchorMax = new Vector2(f, 0f);

            bool daytime = f >= DayNightBar.Dawn && f < DayNightBar.Dusk;
            float left = daytime
                ? (DayNightBar.Dusk - f) * dayLength
                : ((f < DayNightBar.Dawn ? DayNightBar.Dawn : 1f + DayNightBar.Dawn) - f) * dayLength;

            SetText(_left, DayNightBar.DayNumber.Value ? L10n.Game("$msg_newday", "Day $1", day.ToString()) : "");
            if (DayNightBar.Clock.Value)
            {
                int minutes = Mathf.FloorToInt(DayNightBar.SunFraction(f) * 24f * 60f) % (24 * 60);
                SetText(_center, $"<mspace=0.55em>{minutes / 60:00}:{minutes % 60:00}</mspace>");
            }
            else
            {
                SetText(_center, "");
            }
            if (DayNightBar.Countdown.Value)
            {
                bool warn = daytime && left <= DayNightBar.DuskWarningSeconds.Value;
                var color = warn ? HudKit.Warn : daytime ? HudKit.Gold : NightText;
                string time = $"<color=#{HudKit.Hex(color)}><mspace=0.55em>{Clock(left)}</mspace></color>";
                SetText(_right, L10n.T(daytime ? "dusk_in" : "dawn_in", time));
            }
            else
            {
                SetText(_right, "");
            }
            FitLabels();
        }

        private static void SetText(TMP_Text t, string s)
        {
            if (t.text != s)
                t.text = s;
        }

        /// <summary>
        /// The day sits left, the clock center, the countdown right, all on one line. When long
        /// words (some languages) or a narrow bar would make them touch, the clock gives way
        /// first, then the day number.
        /// </summary>
        private void FitLabels()
        {
            float width = _rect.rect.width;
            float l = _left.text.Length > 0 ? _left.preferredWidth : 0f;
            float r = _right.text.Length > 0 ? _right.preferredWidth : 0f;
            float c = _center.text.Length > 0 ? _center.preferredWidth : 0f;
            float clockLeft = width * 0.5f - c * 0.5f;
            bool clockFits = c <= 0f || (clockLeft >= l + LabelGap && clockLeft >= r + LabelGap);
            if (_center.gameObject.activeSelf != clockFits)
                _center.gameObject.SetActive(clockFits);
            bool dayFits = l + r + LabelGap <= width;
            if (_left.gameObject.activeSelf != dayFits)
                _left.gameObject.SetActive(dayFits);
        }

        private static string Clock(float seconds)
        {
            int s = Mathf.Max(0, Mathf.CeilToInt(seconds));
            return $"{s / 60}:{s % 60:00}";
        }

        /// <summary>On the minimap's top edge, under the compass, or at the custom anchor; plus the offsets.</summary>
        private void Place()
        {
            var root = _rect.parent as RectTransform;
            if (root == null)
                return;
            var offset = new Vector2(DayNightBar.OffsetX.Value, DayNightBar.OffsetY.Value);
            float width = DayNightBar.Width.Value;
            float height = _height;
            var map = Minimap.instance;
            bool onMap = map != null && map.m_smallRoot != null && map.m_smallRoot.activeInHierarchy;
            switch (DayNightBar.Placement.Value)
            {
                case DayNightBar.Place.AboveMinimap when onMap:
                {
                    // Follows the minimap wherever the game (or another mod) puts it.
                    ((RectTransform)map.m_smallRoot.transform).GetWorldCorners(Corners);
                    Vector3 min = root.InverseTransformPoint(Corners[0]);
                    Vector3 max = root.InverseTransformPoint(Corners[2]);
                    _rect.anchorMin = _rect.anchorMax = new Vector2(0.5f, 0.5f);
                    _rect.sizeDelta = new Vector2(Mathf.Max(80f, max.x - min.x - Inset * 2f), height);
                    _rect.localPosition = new Vector3((min.x + max.x) * 0.5f + offset.x, max.y + Above + offset.y, 0f);
                    break;
                }
                case DayNightBar.Place.AboveMinimap:
                    // No minimap (No Map world, or turned off): the same corner, at the configured width.
                    _rect.anchorMin = _rect.anchorMax = new Vector2(1f, 1f);
                    _rect.sizeDelta = new Vector2(width, height);
                    _rect.anchoredPosition = new Vector2(-CornerMargin - width * 0.5f, -CornerMargin - TotalHeight()) + offset;
                    break;
                case DayNightBar.Place.TopCenter:
                {
                    _rect.anchorMin = _rect.anchorMax = new Vector2(0.5f, 1f);
                    _rect.sizeDelta = new Vector2(width, height);
                    float y = -CornerMargin - TotalHeight();
                    if (CompassHud.TryGetBounds(out var compassMin, out _))
                        y = Mathf.Min(y, root.InverseTransformPoint(compassMin).y - root.rect.yMax - 6f - TotalHeight());
                    _rect.anchoredPosition = new Vector2(0f, y) + offset;
                    break;
                }
                default:
                    _rect.anchorMin = _rect.anchorMax = new Vector2(DayNightBar.AnchorX.Value, DayNightBar.AnchorY.Value);
                    _rect.sizeDelta = new Vector2(width, height);
                    _rect.anchoredPosition = offset;
                    break;
            }
        }

        /// <summary>Bar plus the text line above it, in HUD pixels.</summary>
        private float TotalHeight() => _height + TextGap + 2f + TextHeight * DayNightBar.Size.Value;

        /// <summary>Sizes the parts from Bar Height and Size; only when they change.</summary>
        private void Layout()
        {
            int h = DayNightBar.BarHeight.Value;
            float size = DayNightBar.Size.Value;
            if (h == _height && Mathf.Approximately(size, _size))
                return;
            _height = h;
            _size = size;
            _strip.rectTransform.sizeDelta = new Vector2(0f, h);

            // Icons sit on the middle of their part of the bar: anchored by fraction, so they follow its width.
            float icon = (h + 8f) * size;
            PlaceIcon(_sunIcon, 0.5f, icon);
            PlaceIcon(_moonLeft, DayNightBar.Dawn * 0.5f, icon - 2f);
            PlaceIcon(_moonRight, (1f + DayNightBar.Dusk) * 0.5f, icon - 2f);
            _marker.sizeDelta = new Vector2(2f, h + 6f);
            _marker.anchoredPosition = new Vector2(0f, h * 0.5f);

            foreach (var t in new[] { _left, _center, _right })
            {
                t.fontSize = 14f * size;
                var rt = t.rectTransform;
                rt.sizeDelta = new Vector2(0f, TextHeight * size);
                rt.anchoredPosition = new Vector2(0f, h + TextGap + 2f);
            }
        }

        private void PlaceIcon(RectTransform icon, float at, float size)
        {
            icon.anchorMin = icon.anchorMax = new Vector2(at, 0f);
            icon.sizeDelta = new Vector2(size, size);
            icon.anchoredPosition = new Vector2(0f, _height * 0.5f);
        }

        /// <summary>The strip texture: night, a short blend at dawn and dusk, day. Rebuilt when the colors change.</summary>
        private void UpdateStrip()
        {
            var day = DayNightBar.DayColor.Value;
            var night = DayNightBar.NightColor.Value;
            if (_stripTex != null && day == _stripDay && night == _stripNight)
                return;
            _stripDay = day;
            _stripNight = night;
            if (_stripTex == null)
                _stripTex = new Texture2D(StripWidth, 1, TextureFormat.RGBA32, false)
                    { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
            const float blend = 0.02f;
            var px = new Color[StripWidth];
            for (int x = 0; x < StripWidth; x++)
            {
                float f = (x + 0.5f) / StripWidth;
                float dayness = Mathf.Min(
                    Mathf.SmoothStep(0f, 1f, (f - DayNightBar.Dawn + blend) / (2f * blend)),
                    Mathf.SmoothStep(0f, 1f, (DayNightBar.Dusk + blend - f) / (2f * blend)));
                px[x] = Color.Lerp(night, day, dayness);
            }
            _stripTex.SetPixels(px);
            _stripTex.Apply();
            _strip.texture = _stripTex;
        }

        /// <summary>A soft disc with eight short rays.</summary>
        private static Texture2D SunTexture()
        {
            const int n = 32;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            var px = new Color[n * n];
            var c = new Vector2((n - 1) * 0.5f, (n - 1) * 0.5f);
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    var d = new Vector2(x, y) - c;
                    float r = d.magnitude;
                    float disc = Mathf.Clamp01(8.5f - r);
                    float angle = Mathf.Atan2(d.y, d.x);
                    float ray = Mathf.Pow(Mathf.Abs(Mathf.Cos(angle * 4f)), 12f);
                    float rays = r > 10f && r < 15f ? ray * Mathf.Clamp01(15f - r) : 0f;
                    px[y * n + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(disc + rays));
                }
            }
            t.SetPixels(px);
            t.Apply();
            return t;
        }

        /// <summary>A crescent: a disc with an offset disc cut out of it.</summary>
        private static Texture2D MoonTexture()
        {
            const int n = 32;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            var px = new Color[n * n];
            var c = new Vector2((n - 1) * 0.5f, (n - 1) * 0.5f);
            var cut = c + new Vector2(5f, 3f);
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    var p = new Vector2(x, y);
                    float body = Mathf.Clamp01(12f - (p - c).magnitude);
                    float hole = Mathf.Clamp01(11f - (p - cut).magnitude);
                    px[y * n + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(body - hole));
                }
            }
            t.SetPixels(px);
            t.Apply();
            return t;
        }
    }
}
