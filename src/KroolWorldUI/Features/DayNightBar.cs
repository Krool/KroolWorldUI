using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KroolWorldUI.Features
{
    /// <summary>
    /// A day and night strip above the minimap, midnight to midnight: night at both ends with a
    /// moon, day in the middle with a sun, a marker for now, and the time until dusk or dawn.
    /// Timing is the game's own (EnvMan): the cycle is m_dayLengthSec long (1200 s by default),
    /// counted from midnight on ZNet's synced clock; dawn is at 15% of it and dusk at 85%
    /// (EnvMan rescales those to the sun's 0.25 and 0.75). Read-only.
    /// </summary>
    internal static class DayNightBar
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> Countdown;
        internal static ConfigEntry<bool> DayNumber;
        internal static ConfigEntry<bool> Clock;
        internal static ConfigEntry<float> DuskWarningSeconds;
        internal static ConfigEntry<int> BarHeight;
        internal static ConfigEntry<Color> DayColor;
        internal static ConfigEntry<Color> NightColor;

        private const string Section = "Day Night Bar";

        internal const float Dawn = 0.15f;
        internal const float Dusk = 0.85f;

        internal static void BindConfig(ConfigFile config)
        {
            Enabled = config.Bind(Section, "Enabled", true,
                "A day and night bar above the minimap: where you are in the day, and how long until dusk or dawn.");
            Countdown = config.Bind(Section, "Countdown", true, "Show the time left until dusk (by day) or dawn (by night).");
            DayNumber = config.Bind(Section, "DayNumber", true, "Show the day number.");
            Clock = config.Bind(Section, "Clock", false,
                "Show the time of day as a 24-hour clock: 06:00 is sunrise, 18:00 sunset.");
            DuskWarningSeconds = config.Bind(Section, "DuskWarningSeconds", 60f,
                new ConfigDescription("The dusk countdown turns orange this many seconds before night. 0 = never.",
                    new AcceptableValueRange<float>(0f, 300f)));
            BarHeight = config.Bind(Section, "BarHeight", 10,
                new ConfigDescription("Bar height in HUD pixels.", new AcceptableValueRange<int>(6, 18)));
            DayColor = config.Bind(Section, "DayColor", new Color(0.86f, 0.68f, 0.32f, 0.85f), "Color of the day part.");
            NightColor = config.Bind(Section, "NightColor", new Color(0.13f, 0.17f, 0.36f, 0.9f), "Color of the night part.");
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

        [HarmonyPatch(typeof(Minimap), "Update")]
        private static class EnsureOnMinimap
        {
            private static void Postfix(Minimap __instance)
            {
                if (Enabled.Value)
                    DayNightBarHud.Ensure(__instance);
            }
        }
    }

    internal class DayNightBarHud : MonoBehaviour
    {
        private const float Inset = 6f;      // the minimap's own frame
        private const float Above = 6f;      // gap between the minimap and the bar
        private const float TextHeight = 18f;
        private const float TextGap = 1f;
        private const int StripWidth = 256;

        private static DayNightBarHud _instance;
        private static bool _failed;
        private static Texture2D _sun, _moon;

        private RectTransform _rect;
        private RawImage _strip;
        private Texture2D _stripTex;
        private Color _stripDay, _stripNight;
        private RectTransform _sunIcon, _moonLeft, _moonRight, _marker;
        private TMP_Text _left, _center, _right;
        private int _height = -1;

        internal static void Ensure(Minimap map)
        {
            if (_instance != null || _failed || map.m_smallRoot == null || map.m_biomeNameSmall == null)
                return;
            try
            {
                var go = new GameObject("KroolWorldUI_DayNightBar", typeof(RectTransform));
                go.transform.SetParent(map.m_smallRoot.transform, false);
                var b = go.AddComponent<DayNightBarHud>();
                _instance = b;
                b.Build(map.m_biomeNameSmall);
            }
            catch (System.Exception e)
            {
                _failed = true;
                Plugin.Log.LogError($"Day night bar disabled for this session: {e}");
            }
        }

        private void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
        }

        private void Build(TMP_Text template)
        {
            // A band along the top edge of the minimap, rising above it.
            _rect = (RectTransform)transform;
            _rect.anchorMin = new Vector2(0f, 1f);
            _rect.anchorMax = new Vector2(1f, 1f);
            _rect.pivot = new Vector2(0.5f, 0f);

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
            return text;
        }

        private void LateUpdate()
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
                Plugin.Log.LogError($"Day night bar disabled for this session after an error: {e}");
                Destroy(gameObject);
            }
        }

        private void Tick()
        {
            bool on = DayNightBar.Enabled.Value && Player.m_localPlayer != null
                      && DayNightBar.TryGetTime(out _, out _, out _);
            for (int i = 0; i < transform.childCount; i++)
                transform.GetChild(i).gameObject.SetActive(on);
            if (!on)
                return;

            DayNightBar.TryGetTime(out float f, out float dayLength, out int day);
            Layout();
            UpdateStrip();

            _marker.anchorMin = _marker.anchorMax = new Vector2(f, 0f);

            bool daytime = f >= DayNightBar.Dawn && f < DayNightBar.Dusk;
            float left = daytime
                ? (DayNightBar.Dusk - f) * dayLength
                : ((f < DayNightBar.Dawn ? DayNightBar.Dawn : 1f + DayNightBar.Dawn) - f) * dayLength;

            _left.text = DayNightBar.DayNumber.Value ? DayText(day) : "";
            if (DayNightBar.Clock.Value)
            {
                int minutes = Mathf.FloorToInt(DayNightBar.SunFraction(f) * 24f * 60f) % (24 * 60);
                _center.text = $"<mspace=0.55em>{minutes / 60:00}:{minutes % 60:00}</mspace>";
            }
            else
            {
                _center.text = "";
            }
            if (DayNightBar.Countdown.Value)
            {
                bool warn = daytime && left <= DayNightBar.DuskWarningSeconds.Value;
                var color = warn ? HudKit.Warn : daytime ? HudKit.Gold : new Color(0.7f, 0.78f, 1f, 1f);
                _right.text = $"{(daytime ? "Dusk" : "Dawn")} in <color=#{ColorUtility.ToHtmlStringRGB(color)}>" +
                              $"<mspace=0.55em>{Clock(left)}</mspace></color>";
            }
            else
            {
                _right.text = "";
            }
        }

        private static string DayText(int day)
        {
            // The game's own "Day N" (shown at each sunrise), in the player's language.
            string s = Localization.instance != null ? Localization.instance.Localize("$msg_newday", day.ToString()) : "";
            return string.IsNullOrEmpty(s) || s.StartsWith("[") ? $"Day {day}" : s;
        }

        private static string Clock(float seconds)
        {
            int s = Mathf.Max(0, Mathf.CeilToInt(seconds));
            return $"{s / 60}:{s % 60:00}";
        }

        /// <summary>Sizes everything from Bar Height; only when it changes.</summary>
        private void Layout()
        {
            int h = DayNightBar.BarHeight.Value;
            if (h == _height)
                return;
            _height = h;
            _rect.anchoredPosition = new Vector2(0f, Above);
            _rect.sizeDelta = new Vector2(-Inset * 2f, h);
            _strip.rectTransform.sizeDelta = new Vector2(0f, h);

            // Icons sit on the middle of their part of the bar: anchored by fraction, so they follow its width.
            float icon = h + 8f;
            Place(_sunIcon, 0.5f, icon);
            Place(_moonLeft, DayNightBar.Dawn * 0.5f, icon - 2f);
            Place(_moonRight, (1f + DayNightBar.Dusk) * 0.5f, icon - 2f);
            _marker.sizeDelta = new Vector2(2f, h + 6f);
            _marker.anchoredPosition = new Vector2(0f, h * 0.5f);

            foreach (var t in new[] { _left, _center, _right })
            {
                var rt = t.rectTransform;
                rt.sizeDelta = new Vector2(0f, TextHeight);
                rt.anchoredPosition = new Vector2(0f, h + TextGap + 2f);
            }
        }

        private void Place(RectTransform icon, float at, float size)
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
