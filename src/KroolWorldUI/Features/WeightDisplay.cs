using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KroolWorldUI.Features
{
    /// <summary>
    /// Carry weight on the HUD, using the same numbers as the inventory screen
    /// (ceil(total) / ceil(max), red flash when over). Read-only.
    /// </summary>
    internal static class WeightDisplay
    {
        internal enum ShowMode { Always, NearLimit, WhenChanged }

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<ShowMode> Mode;
        internal static ConfigEntry<float> NearLimitPercent;
        internal static ConfigEntry<float> ChangedSeconds;
        internal static ConfigEntry<bool> ShowBar;
        internal static ConfigEntry<float> AnchorX;
        internal static ConfigEntry<float> AnchorY;
        internal static ConfigEntry<float> Scale;

        private const string Section = "Weight";

        internal static void BindConfig(ConfigFile config)
        {
            Enabled = config.Bind(Section, "Enabled", true,
                "Show your carry weight on the HUD without opening the inventory.");
            Mode = config.Bind(Section, "Show", ShowMode.Always,
                "Always; NearLimit = only at or above NearLimitPercent; WhenChanged = for a few seconds after your weight changes.");
            NearLimitPercent = config.Bind(Section, "NearLimitPercent", 80f,
                new ConfigDescription("Percent of max weight where the readout turns orange (and appears, in NearLimit mode).",
                    new AcceptableValueRange<float>(10f, 100f)));
            ChangedSeconds = config.Bind(Section, "ChangedSeconds", 3f,
                new ConfigDescription("WhenChanged mode: seconds the readout stays after a change.",
                    new AcceptableValueRange<float>(1f, 15f)));
            ShowBar = config.Bind(Section, "ShowBar", true, "Show a thin fill bar under the numbers.");
            AnchorX = config.Bind(Section, "AnchorX", 0.008f,
                new ConfigDescription("Horizontal position of the readout's left edge (0 = left of screen, 1 = right).",
                    new AcceptableValueRange<float>(0f, 1f)));
            AnchorY = config.Bind(Section, "AnchorY", 0.99f,
                new ConfigDescription("Vertical position of the readout's top edge (0 = bottom, 1 = top). " +
                                      "It moves down on its own to stay below the hotbar.",
                    new AcceptableValueRange<float>(0f, 1f)));
            Scale = config.Bind(Section, "Scale", 1f,
                new ConfigDescription("Size of the readout.", new AcceptableValueRange<float>(0.5f, 2f)));
        }

        // Hud.Update runs every frame in game; cheap check that builds the readout once the HUD,
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
        private const float Width = 150f;
        private const float TextHeight = 30f;
        private const float BarHeight = 5f;
        private const float Pad = 6f;
        private const float IconSize = 24f;
        private const float BlockerMargin = 8f;
        private const float RefreshInterval = 0.2f;

        private static readonly Color Normal = new Color(0.92f, 0.87f, 0.78f, 1f);
        private static readonly Color Warn = new Color(1f, 0.69f, 0.25f, 1f);
        private static readonly Color Over = new Color(0.9f, 0.2f, 0.15f, 1f);
        private static readonly Color BarFill = new Color(0.87f, 0.70f, 0.36f, 1f);

        private static WeightHud _instance;
        private static bool _failed;

        private RectTransform _rect;
        private CanvasGroup _group;
        private TMP_Text _text;
        private GameObject _barRoot;
        private RectTransform _barFill;
        private Image _barFillImage;

        private int _weight = -1;
        private int _max = -1;
        private float _ratio;
        private string _shownText;
        private float _lastChange;
        private float _nextRefresh;
        private float _nextBlockerScan;
        private readonly List<Rect> _blockers = new List<Rect>();
        private readonly Vector3[] _corners = new Vector3[4];

        internal static void Ensure()
        {
            if (_instance != null || _failed)
                return;
            var hud = Hud.instance;
            var inv = InventoryGui.instance;
            if (hud == null || hud.m_rootObject == null || inv == null || inv.m_weight == null || Player.m_localPlayer == null)
                return;
            try
            {
                Create(hud, inv);
            }
            catch (System.Exception e)
            {
                _failed = true;
                Plugin.Log.LogError($"Weight readout disabled for this session: {e}");
            }
        }

        private static void Create(Hud hud, InventoryGui inv)
        {
            var go = new GameObject("KroolWorldUI_Weight", typeof(RectTransform), typeof(CanvasGroup));
            go.transform.SetParent(hud.m_rootObject.transform, false);
            var w = go.AddComponent<WeightHud>();
            _instance = w;
            w._rect = (RectTransform)go.transform;
            w._rect.pivot = new Vector2(0f, 1f);
            w._rect.sizeDelta = new Vector2(Width, TextHeight + BarHeight + Pad);
            w._group = go.GetComponent<CanvasGroup>();
            w._group.blocksRaycasts = false;
            w._group.interactable = false;
            w._group.alpha = 0f;

            var bg = NewImage("bg", go.transform, new Color(0.06f, 0.045f, 0.03f, 0.6f));
            Stretch(bg.rectTransform, 0f, 0f, 0f, 0f);

            // The inventory's weight icon, if the game still names it that way; text-only otherwise.
            var iconSprite = FindWeightIcon(inv.m_weight.transform);
            float textLeft = Pad;
            if (iconSprite != null)
            {
                var icon = NewImage("icon", go.transform, Color.white);
                icon.sprite = iconSprite;
                icon.preserveAspect = true;
                var irt = icon.rectTransform;
                irt.anchorMin = irt.anchorMax = new Vector2(0f, 1f);
                irt.pivot = new Vector2(0f, 1f);
                irt.sizeDelta = new Vector2(IconSize, IconSize);
                irt.anchoredPosition = new Vector2(Pad, -(TextHeight - IconSize) / 2f - 2f);
                textLeft = Pad + IconSize + 4f;
            }

            // Clone the inventory's own weight text so font and material match the game.
            var textGo = Instantiate(inv.m_weight.gameObject, go.transform, false);
            textGo.name = "text";
            textGo.SetActive(true);
            for (int i = textGo.transform.childCount - 1; i >= 0; i--)
                Destroy(textGo.transform.GetChild(i).gameObject);
            foreach (var c in textGo.GetComponents<Component>())
                if (!(c is Transform) && !(c is TMP_Text) && !(c is CanvasRenderer))
                    Destroy(c);
            w._text = textGo.GetComponent<TMP_Text>();
            w._text.enableAutoSizing = false;
            w._text.fontSize = 22f;
            w._text.alignment = TextAlignmentOptions.MidlineLeft;
            w._text.textWrappingMode = TextWrappingModes.NoWrap;
            w._text.raycastTarget = false;
            var trt = w._text.rectTransform;
            trt.anchorMin = new Vector2(0f, 1f);
            trt.anchorMax = new Vector2(1f, 1f);
            trt.pivot = new Vector2(0f, 1f);
            trt.offsetMin = new Vector2(textLeft, -TextHeight);
            trt.offsetMax = new Vector2(-Pad, 0f);

            w._barRoot = new GameObject("bar", typeof(RectTransform));
            w._barRoot.transform.SetParent(go.transform, false);
            var brt = (RectTransform)w._barRoot.transform;
            brt.anchorMin = new Vector2(0f, 0f);
            brt.anchorMax = new Vector2(1f, 0f);
            brt.pivot = new Vector2(0f, 0f);
            brt.offsetMin = new Vector2(Pad, Pad - 2f);
            brt.offsetMax = new Vector2(-Pad, Pad - 2f + BarHeight);
            var track = NewImage("track", brt, new Color(0f, 0f, 0f, 0.6f));
            Stretch(track.rectTransform, 0f, 0f, 0f, 0f);
            w._barFillImage = NewImage("fill", brt, BarFill);
            w._barFill = w._barFillImage.rectTransform;
            w._barFill.anchorMin = new Vector2(0f, 0f);
            w._barFill.anchorMax = new Vector2(0f, 1f);
            w._barFill.pivot = new Vector2(0f, 0.5f);
            w._barFill.offsetMin = Vector2.zero;
            w._barFill.offsetMax = Vector2.zero;

            w._lastChange = Time.time;
            w.Place();
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
                Plugin.Log.LogError($"Weight readout disabled for this session after an error: {e}");
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
                float total = player.GetInventory().GetTotalWeight();
                float max = player.GetMaxCarryWeight();
                int wi = Mathf.CeilToInt(total), mi = Mathf.CeilToInt(max);
                if (wi != _weight || mi != _max)
                {
                    if (_weight >= 0)
                        _lastChange = Time.time;
                    _weight = wi;
                    _max = mi;
                }
                _ratio = max > 0f ? total / max : 0f;
            }

            bool over = _weight > _max;
            bool near = _ratio * 100f >= WeightDisplay.NearLimitPercent.Value;

            bool wanted;
            switch (WeightDisplay.Mode.Value)
            {
                case WeightDisplay.ShowMode.NearLimit: wanted = near || over; break;
                case WeightDisplay.ShowMode.WhenChanged: wanted = Time.time - _lastChange <= WeightDisplay.ChangedSeconds.Value; break;
                default: wanted = true; break;
            }
            if (SettingsWindow.IsOpen)
                wanted = true; // visible while positioning it
            if (InventoryGui.IsVisible() || Minimap.IsOpen() || Menu.IsVisible())
                wanted = false; // the inventory shows weight itself; maps and menus need the space

            float target = wanted ? 1f : 0f;
            _group.alpha = Mathf.MoveTowards(_group.alpha, target, Time.deltaTime * 4f);
            if (_group.alpha <= 0f && !wanted)
                return;

            // Same flash as the inventory screen when over capacity.
            bool flashRed = over && Mathf.Sin(Time.time * 10f) > 0f;
            Color numColor = flashRed ? Over : near ? Warn : Normal;
            string label = $"<color=#{ColorUtility.ToHtmlStringRGB(numColor)}>{_weight}</color>/{_max}";
            if (label != _shownText)
            {
                // Rebuilding a TMP mesh is not free; only when the text actually changes.
                _shownText = label;
                _text.text = label;
                _text.color = Normal;
            }

            _barRoot.SetActive(WeightDisplay.ShowBar.Value);
            if (WeightDisplay.ShowBar.Value)
            {
                _barFill.anchorMax = new Vector2(Mathf.Clamp01(_ratio), 1f);
                _barFillImage.color = over ? Over : near ? Warn : BarFill;
            }

            Place();
        }

        /// <summary>Configured anchor, then pushed down below the hotbar (or anything else in the way).</summary>
        private void Place()
        {
            var anchor = new Vector2(WeightDisplay.AnchorX.Value, WeightDisplay.AnchorY.Value);
            _rect.anchorMin = anchor;
            _rect.anchorMax = anchor;
            _rect.anchoredPosition = Vector2.zero;
            float s = WeightDisplay.Scale.Value;
            _rect.localScale = new Vector3(s, s, 1f);

            if (Time.time >= _nextBlockerScan)
            {
                _nextBlockerScan = Time.time + 1f;
                ScanBlockers();
            }

            _rect.GetWorldCorners(_corners);
            float xMin = _corners[0].x, xMax = _corners[2].x;
            float yTop = _corners[1].y, height = _corners[1].y - _corners[0].y;
            float margin = BlockerMargin * _rect.lossyScale.y / Mathf.Max(0.0001f, s);
            float start = yTop;

            for (int pass = 0; pass < 3; pass++)
            {
                bool moved = false;
                foreach (var b in _blockers)
                {
                    bool overlapX = b.xMin < xMax && b.xMax > xMin;
                    bool overlapY = b.yMin < yTop && b.yMax > yTop - height;
                    if (overlapX && overlapY)
                    {
                        yTop = b.yMin - margin;
                        moved = true;
                    }
                }
                if (!moved)
                    break;
            }

            if (yTop != start)
            {
                var pos = _rect.position;
                _rect.position = new Vector3(pos.x, pos.y + (yTop - start), pos.z);
            }
        }

        // Hotbars (vanilla, plus any mod that reuses HotkeyBar) measured by their visible graphics,
        // since the HotkeyBar object itself can be zero-sized.
        private void ScanBlockers()
        {
            _blockers.Clear();
            foreach (var bar in FindObjectsByType<HotkeyBar>(FindObjectsSortMode.None))
            {
                if (!bar.isActiveAndEnabled)
                    continue;
                bool any = false;
                float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
                foreach (var g in bar.GetComponentsInChildren<Graphic>())
                {
                    if (!g.isActiveAndEnabled || g.color.a < 0.01f)
                        continue;
                    g.rectTransform.GetWorldCorners(_corners);
                    x0 = Mathf.Min(x0, _corners[0].x);
                    y0 = Mathf.Min(y0, _corners[0].y);
                    x1 = Mathf.Max(x1, _corners[2].x);
                    y1 = Mathf.Max(y1, _corners[2].y);
                    any = true;
                }
                if (any)
                    _blockers.Add(Rect.MinMaxRect(x0, y0, x1, y1));
            }
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

        private static void Stretch(RectTransform rt, float l, float b, float r, float t)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(l, b);
            rt.offsetMax = new Vector2(-r, -t);
        }
    }
}
