using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KroolWorldUI.Features
{
    /// <summary>
    /// Carry weight as a vertical bar left of the food column, built like the health bar next to
    /// it: a weight icon beside the fork (as the heart sits under the health bar), the bar rising
    /// above it, and the numbers on top. Same numbers as the inventory screen. Read-only.
    /// </summary>
    internal static class WeightDisplay
    {
        internal enum ShowMode { Always, NearLimit, WhenChanged }
        internal enum ColorMode { Thresholds, Gradient, Single }
        internal enum TextMode { CurrentAndMax, Current, Percent, None }

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<ShowMode> Mode;
        internal static ConfigEntry<float> NearLimitPercent;
        internal static ConfigEntry<float> ChangedSeconds;
        internal static ConfigEntry<ColorMode> Colors;
        internal static ConfigEntry<Color> FillColor;
        internal static ConfigEntry<Color> NearColor;
        internal static ConfigEntry<Color> OverColor;
        internal static ConfigEntry<TextMode> Text;
        internal static ConfigEntry<float> TextSize;
        internal static ConfigEntry<float> BarLength;
        internal static ConfigEntry<float> BarThickness;
        internal static ConfigEntry<bool> ScaleWithMaxWeight;
        internal static ConfigEntry<float> OffsetX;
        internal static ConfigEntry<float> OffsetY;

        /// <summary>Vanilla base carry weight; BarLength is the bar length at this max.</summary>
        internal const float BaseMaxWeight = 300f;

        private const string Section = "Weight";

        internal static void BindConfig(ConfigFile config)
        {
            Enabled = config.Bind(Section, "Enabled", true,
                "Show your carry weight on the HUD, left of the food icons.");
            Mode = config.Bind(Section, "Show", ShowMode.Always,
                "Always; NearLimit = only at or above NearLimitPercent; WhenChanged = for a few seconds after your weight changes.");
            NearLimitPercent = config.Bind(Section, "NearLimitPercent", 80f,
                new ConfigDescription("Percent of max weight where the bar turns to the near-limit color (and appears, in NearLimit mode).",
                    new AcceptableValueRange<float>(10f, 100f)));
            ChangedSeconds = config.Bind(Section, "ChangedSeconds", 3f,
                new ConfigDescription("WhenChanged mode: seconds the bar stays after a change.",
                    new AcceptableValueRange<float>(1f, 15f)));
            Colors = config.Bind(Section, "Colors", ColorMode.Thresholds,
                "Thresholds: fill color, near-limit color past NearLimitPercent, over color when overweight. " +
                "Gradient: blends fill -> near-limit -> over as you fill up. Single: always the fill color.");
            FillColor = config.Bind(Section, "FillColor", new Color(0.80f, 0.62f, 0.30f, 1f), "Bar color under the limit (hex RRGGBBAA).");
            NearColor = config.Bind(Section, "NearLimitColor", new Color(1f, 0.55f, 0.12f, 1f), "Bar color near the limit.");
            OverColor = config.Bind(Section, "OverColor", new Color(0.86f, 0.16f, 0.12f, 1f), "Bar color when overweight.");
            Text = config.Bind(Section, "Text", TextMode.CurrentAndMax,
                "Numbers above the bar: current over max, current only, percent, or none.");
            TextSize = config.Bind(Section, "TextSize", 16f,
                new ConfigDescription("Font size of the numbers.", new AcceptableValueRange<float>(10f, 28f)));
            BarLength = config.Bind(Section, "BarLength", 110f,
                new ConfigDescription("Bar length in HUD pixels at the normal 300 max weight.",
                    new AcceptableValueRange<float>(40f, 220f)));
            BarThickness = config.Bind(Section, "BarThickness", 16f,
                new ConfigDescription("Bar thickness in HUD pixels (the health bar is 32).",
                    new AcceptableValueRange<float>(6f, 32f)));
            ScaleWithMaxWeight = config.Bind(Section, "ScaleWithMaxWeight", true,
                "Grow the bar with your max weight: a Megingjord (+150) makes it 50% longer.");
            OffsetX = config.Bind(Section, "OffsetX", 0f,
                new ConfigDescription("Nudge left (-) or right (+), in HUD pixels.", new AcceptableValueRange<float>(-200f, 200f)));
            OffsetY = config.Bind(Section, "OffsetY", 0f,
                new ConfigDescription("Nudge down (-) or up (+), in HUD pixels.", new AcceptableValueRange<float>(-200f, 300f)));
        }

        // Hud.Update runs every frame in game; cheap check that builds the widget once the HUD,
        // inventory screen and player all exist (it is rebuilt after each logout, with the HUD).
        [HarmonyPatch(typeof(Hud), "Update")]
        private static class EnsureOnHud
        {
            private static void Postfix()
            {
                if (Enabled.Value)
                    WeightHud.Ensure();
            }
        }
    }

    internal class WeightHud : MonoBehaviour
    {
        private const float Gap = 4f;           // HUD pixels between fork, icon and bar
        private const float RefreshInterval = 0.2f;

        private static readonly Color Parchment = new Color(0.92f, 0.87f, 0.78f, 1f);
        private static readonly Color Muted = new Color(0.72f, 0.66f, 0.56f, 1f);

        private static WeightHud _instance;
        private static bool _failed;

        private RectTransform _rect;      // our root: a point in the health panel at the fork's center
        private CanvasGroup _group;
        private RectTransform _icon;
        private RectTransform _bar;       // clone of the health bar root, rotated like it
        private GuiBar _fill;
        private TMP_Text _text;
        private Transform _fork;
        private Transform _food0;

        private float _total;
        private float _max = WeightDisplay.BaseMaxWeight;
        private int _weight = -1;
        private int _maxShown = -1;
        private string _shownText;
        private float _lastChange;
        private float _nextRefresh;

        internal static void Ensure()
        {
            if (_instance != null || _failed)
                return;
            var hud = Hud.instance;
            var inv = InventoryGui.instance;
            if (hud == null || hud.m_healthPanel == null || hud.m_healthBarRoot == null || hud.m_foodIcons == null
                || hud.m_foodIcons.Length == 0 || inv == null || inv.m_weight == null || Player.m_localPlayer == null)
                return;
            try
            {
                Create(hud, inv);
            }
            catch (System.Exception e)
            {
                _failed = true;
                Plugin.Log.LogError($"Weight bar disabled for this session: {e}");
            }
        }

        private static void Create(Hud hud, InventoryGui inv)
        {
            var go = new GameObject("KroolWorldUI_Weight", typeof(RectTransform), typeof(CanvasGroup), typeof(LayoutElement));
            go.GetComponent<LayoutElement>().ignoreLayout = true;
            go.transform.SetParent(hud.m_healthPanel, false);
            var w = go.AddComponent<WeightHud>();
            _instance = w;
            w._rect = (RectTransform)go.transform;
            w._rect.anchorMin = w._rect.anchorMax = new Vector2(0.5f, 0.5f);
            w._rect.pivot = new Vector2(0.5f, 0.5f);
            w._rect.sizeDelta = Vector2.zero;
            w._group = go.GetComponent<CanvasGroup>();
            w._group.blocksRaycasts = false;
            w._group.interactable = false;
            w._group.alpha = 0f;

            w._food0 = hud.m_foodIcons[0].transform.parent; // the slot frame, not the icon inside it
            w._fork = FindFork(hud.m_healthPanel);

            // The bar: a copy of the game's health bar, minus its second (slow) fill and text.
            var barGo = Instantiate(hud.m_healthBarRoot.gameObject, go.transform, false);
            barGo.name = "bar";
            w._bar = (RectTransform)barGo.transform;
            string fastName = hud.m_healthBarFast != null ? hud.m_healthBarFast.name : "fast";
            foreach (var g in barGo.GetComponentsInChildren<GuiBar>(true))
            {
                if (w._fill == null && g.name == fastName)
                    w._fill = g;
                else
                    DestroyImmediate(g.gameObject);
            }
            foreach (var t in barGo.GetComponentsInChildren<TMP_Text>(true))
                DestroyImmediate(t.gameObject);
            // Our own additions to the health bar (the food max-health segment) are not wanted here.
            foreach (var t in barGo.GetComponentsInChildren<Transform>(true))
                if (t != null && t != barGo.transform && t.name.StartsWith("KroolWorldUI"))
                    DestroyImmediate(t.gameObject);
            foreach (var gr in barGo.GetComponentsInChildren<Graphic>(true))
                gr.raycastTarget = false;
            if (w._fill == null || w._fill.m_bar == null)
                throw new System.Exception("health bar layout changed: no fill bar found");
            w._bar.anchorMin = w._bar.anchorMax = new Vector2(0.5f, 0.5f);
            w._bar.pivot = new Vector2(0f, 0.5f); // grows from its base, like health
            var fillRt = w._fill.m_bar;
            fillRt.anchorMin = fillRt.anchorMax = new Vector2(0f, 0.5f);
            fillRt.pivot = new Vector2(0f, 0.5f);
            fillRt.anchoredPosition = Vector2.zero;
            w._fill.SetMaxValue(1f);
            w._fill.SetValue(0f);

            var iconSprite = FindWeightIcon(inv.m_weight.transform);
            var icon = NewImage("icon", go.transform, Color.white);
            icon.sprite = iconSprite;
            icon.enabled = iconSprite != null;
            icon.preserveAspect = true;
            w._icon = icon.rectTransform;
            w._icon.anchorMin = w._icon.anchorMax = new Vector2(0.5f, 0.5f);
            w._icon.pivot = new Vector2(0.5f, 0.5f);

            // Same text style as the food timers next to it.
            TMP_Text template = hud.m_foodTime != null && hud.m_foodTime.Length > 0 && hud.m_foodTime[0] != null
                ? hud.m_foodTime[0] : (TMP_Text)inv.m_weight;
            w._text = CloneText(template, go.transform);
            w._text.alignment = TextAlignmentOptions.Bottom;
            w._text.rectTransform.anchorMin = w._text.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            w._text.rectTransform.pivot = new Vector2(0.5f, 0f);
            w._text.rectTransform.sizeDelta = new Vector2(80f, 50f);

            w._lastChange = Time.time;
            w.Layout();
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
                Plugin.Log.LogError($"Weight bar disabled for this session after an error: {e}");
                Destroy(gameObject);
            }
        }

        private void Tick()
        {
            var player = Player.m_localPlayer;
            if (!WeightDisplay.Enabled.Value || player == null)
            {
                _group.alpha = 0f;
                return;
            }

            if (Time.time >= _nextRefresh)
            {
                _nextRefresh = Time.time + RefreshInterval;
                _total = player.GetInventory().GetTotalWeight();
                _max = player.GetMaxCarryWeight();
                int wi = Mathf.CeilToInt(_total), mi = Mathf.CeilToInt(_max);
                if (wi != _weight || mi != _maxShown)
                {
                    if (_weight >= 0)
                        _lastChange = Time.time;
                    _weight = wi;
                    _maxShown = mi;
                }
            }

            float ratio = _max > 0f ? _total / _max : 0f;
            bool over = _weight > _maxShown;
            bool near = ratio * 100f >= WeightDisplay.NearLimitPercent.Value;

            bool wanted;
            switch (WeightDisplay.Mode.Value)
            {
                case WeightDisplay.ShowMode.NearLimit: wanted = near || over; break;
                case WeightDisplay.ShowMode.WhenChanged: wanted = Time.time - _lastChange <= WeightDisplay.ChangedSeconds.Value; break;
                default: wanted = true; break;
            }
            if (SettingsWindow.IsOpen)
                wanted = true; // visible while tuning it

            _group.alpha = Mathf.MoveTowards(_group.alpha, wanted ? 1f : 0f, Time.deltaTime * 4f);
            if (_group.alpha <= 0f && !wanted)
                return;

            Layout();
            _fill.SetValue(Mathf.Clamp01(ratio));
            // Same flash as the inventory screen when over capacity.
            bool flash = over && Mathf.Sin(Time.time * 10f) > 0f;
            _fill.SetColor(flash ? Color.Lerp(BarColor(ratio, over, near), Color.white, 0.35f) : BarColor(ratio, over, near));
            UpdateText(ratio, over, near);
        }

        private static Color BarColor(float ratio, bool over, bool near)
        {
            var fill = WeightDisplay.FillColor.Value;
            var warn = WeightDisplay.NearColor.Value;
            var bad = WeightDisplay.OverColor.Value;
            switch (WeightDisplay.Colors.Value)
            {
                case WeightDisplay.ColorMode.Single:
                    return fill;
                case WeightDisplay.ColorMode.Gradient:
                    float nearAt = Mathf.Max(0.01f, WeightDisplay.NearLimitPercent.Value / 100f);
                    if (over)
                        return bad;
                    return ratio < nearAt
                        ? Color.Lerp(fill, warn, ratio / nearAt)
                        : Color.Lerp(warn, bad, Mathf.InverseLerp(nearAt, 1f, ratio));
                default:
                    return over ? bad : near ? warn : fill;
            }
        }

        private void UpdateText(float ratio, bool over, bool near)
        {
            string current = over || near
                ? $"<color=#{ColorUtility.ToHtmlStringRGB(over ? WeightDisplay.OverColor.Value : WeightDisplay.NearColor.Value)}>{_weight}</color>"
                : _weight.ToString();
            string label;
            switch (WeightDisplay.Text.Value)
            {
                case WeightDisplay.TextMode.None: label = ""; break;
                case WeightDisplay.TextMode.Current: label = current; break;
                case WeightDisplay.TextMode.Percent: label = $"{Mathf.RoundToInt(ratio * 100f)}%"; break;
                // Stacked, so it stays narrow enough for the space left of the food column.
                default: label = $"{current}\n<size=80%><color=#{ColorUtility.ToHtmlStringRGB(Muted)}>{_maxShown}</color></size>"; break;
            }
            if (label != _shownText)
            {
                // Rebuilding a TMP mesh is not free; only when the text actually changes.
                _shownText = label;
                _text.text = label;
            }
        }

        /// <summary>
        /// Lays the widget out in the health panel's space: icon left of the fork, bar rising from
        /// just above the icon, numbers above the bar.
        /// </summary>
        private void Layout()
        {
            float iconSize = 32f;
            Vector3 forkPos;
            if (_fork != null)
            {
                var fr = (RectTransform)_fork;
                iconSize = Mathf.Max(16f, Mathf.Min(fr.rect.width, fr.rect.height));
                forkPos = fr.localPosition + (Vector3)RectCenterOffset(fr);
            }
            else
            {
                // No fork found: one slot below the first food slot.
                var f0 = (RectTransform)_food0;
                forkPos = f0.localPosition + (Vector3)RectCenterOffset(f0) + new Vector3(0f, -(f0.rect.height + iconSize) * 0.5f, 0f);
            }
            float slotHalf = _fork != null ? iconSize * 0.5f : ((RectTransform)_food0).rect.width * 0.5f;

            var origin = new Vector3(forkPos.x - slotHalf - Gap - iconSize * 0.5f + WeightDisplay.OffsetX.Value,
                forkPos.y + WeightDisplay.OffsetY.Value, 0f);
            _rect.localPosition = origin;

            _icon.sizeDelta = new Vector2(iconSize, iconSize);
            _icon.anchoredPosition = Vector2.zero;

            float scale = WeightDisplay.ScaleWithMaxWeight.Value
                ? Mathf.Clamp(_max / WeightDisplay.BaseMaxWeight, 0.5f, 3f) : 1f;
            float length = Mathf.Round(WeightDisplay.BarLength.Value * scale);
            float thick = WeightDisplay.BarThickness.Value;
            float barBase = iconSize * 0.5f + Gap;
            _bar.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, length);
            _bar.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, thick);
            _bar.anchoredPosition = new Vector2(0f, barBase);
            _fill.m_bar.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, thick);
            _fill.SetWidth(length);

            // The clone keeps the health bar's 90 degree turn, so its length runs up the screen.
            _text.fontSize = WeightDisplay.TextSize.Value;
            _text.rectTransform.anchoredPosition = new Vector2(0f, barBase + length + Gap);
        }

        /// <summary>Center of a rect relative to its own pivot, in its parent's units.</summary>
        private static Vector2 RectCenterOffset(RectTransform rt)
        {
            var r = rt.rect;
            return new Vector2(r.center.x, r.center.y);
        }

        /// <summary>
        /// The fork icon under the food column. Hud keeps no reference to it, so look for an active
        /// image named like the food icons that is not inside a food slot.
        /// </summary>
        private static Transform FindFork(Transform panel)
        {
            Transform best = null;
            foreach (Transform child in panel)
            {
                if (!child.gameObject.activeSelf || child.GetComponent<Image>() == null)
                    continue;
                string n = child.name.ToLowerInvariant();
                if (n.StartsWith("foodicon"))
                    best = child;
            }
            if (best == null)
                Plugin.Log.LogWarning("Food fork icon not found; weight bar placed from the food slots instead.");
            return best;
        }

        private static TMP_Text CloneText(TMP_Text source, Transform parent)
        {
            var textGo = Instantiate(source.gameObject, parent, false);
            textGo.name = "text";
            textGo.SetActive(true);
            // The source may be rotated or scaled in its own panel; the copy must sit flat.
            textGo.transform.localRotation = Quaternion.identity;
            textGo.transform.localScale = Vector3.one;
            for (int i = textGo.transform.childCount - 1; i >= 0; i--)
                Destroy(textGo.transform.GetChild(i).gameObject);
            foreach (var c in textGo.GetComponents<Component>())
                if (!(c is Transform) && !(c is TMP_Text) && !(c is CanvasRenderer))
                    Destroy(c);
            var text = textGo.GetComponent<TMP_Text>();
            text.enableAutoSizing = false;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            text.richText = true;
            text.raycastTarget = false;
            text.color = Parchment;
            return text;
        }

        private static Sprite FindWeightIcon(Transform weightText)
        {
            var parent = weightText.parent;
            if (parent == null)
                return null;
            foreach (var img in parent.GetComponentsInChildren<Image>(true))
            {
                var sp = img.sprite;
                if (sp != null && sp.name.ToLowerInvariant().Contains("weight"))
                    return sp;
            }
            return null;
        }

        private static Image NewImage(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }
    }
}
