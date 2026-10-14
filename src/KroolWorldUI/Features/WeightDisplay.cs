using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KroolWorldUI.Features
{
    /// <summary>
    /// Carry weight on the HUD as one slim bar with the numbers inside, using the same numbers as
    /// the inventory screen (ceil(total) / ceil(max), red flash when over). Read-only.
    /// </summary>
    internal static class WeightDisplay
    {
        internal enum ShowMode { Always, NearLimit, WhenChanged }
        internal enum Placement { AboveHotbar, BelowHotbar, Custom }

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<ShowMode> Mode;
        internal static ConfigEntry<float> NearLimitPercent;
        internal static ConfigEntry<float> ChangedSeconds;
        internal static ConfigEntry<Placement> Position;
        internal static ConfigEntry<float> Height;
        internal static ConfigEntry<float> BaseWidth;

        /// <summary>Vanilla base carry weight; BaseWidth is the bar width at this max.</summary>
        internal const float BaseMaxWeight = 300f;
        internal static ConfigEntry<float> AnchorX;
        internal static ConfigEntry<float> AnchorY;

        private const string Section = "Weight";

        internal static void BindConfig(ConfigFile config)
        {
            Enabled = config.Bind(Section, "Enabled", true,
                "Show your carry weight on the HUD without opening the inventory.");
            Mode = config.Bind(Section, "Show", ShowMode.Always,
                "Always; NearLimit = only at or above NearLimitPercent; WhenChanged = for a few seconds after your weight changes.");
            NearLimitPercent = config.Bind(Section, "NearLimitPercent", 80f,
                new ConfigDescription("Percent of max weight where the bar turns orange (and appears, in NearLimit mode).",
                    new AcceptableValueRange<float>(10f, 100f)));
            ChangedSeconds = config.Bind(Section, "ChangedSeconds", 3f,
                new ConfigDescription("WhenChanged mode: seconds the bar stays after a change.",
                    new AcceptableValueRange<float>(1f, 15f)));
            Position = config.Bind(Section, "Position", Placement.AboveHotbar,
                "AboveHotbar / BelowHotbar: lined up with the hotbar's first slot. Custom: use AnchorX/AnchorY.");
            Height = config.Bind(Section, "Height", 18f,
                new ConfigDescription("Bar height in HUD pixels.", new AcceptableValueRange<float>(12f, 40f)));
            BaseWidth = config.Bind(Section, "BaseWidth", 140f,
                new ConfigDescription("Bar width in HUD pixels at the normal 300 max weight (about two hotbar slots). " +
                                      "It grows with your max weight: a Megingjord (+150) makes it 50% longer.",
                    new AcceptableValueRange<float>(60f, 400f)));
            AnchorX = config.Bind(Section, "AnchorX", 0.01f,
                new ConfigDescription("Custom position: left edge (0 = left of screen, 1 = right).",
                    new AcceptableValueRange<float>(0f, 1f)));
            AnchorY = config.Bind(Section, "AnchorY", 0.98f,
                new ConfigDescription("Custom position: top edge (0 = bottom of screen, 1 = top).",
                    new AcceptableValueRange<float>(0f, 1f)));
        }

        // Hud.Update runs every frame in game; cheap check that builds the bar once the HUD,
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
        private const float Gap = 4f;          // HUD pixels between the bar and the hotbar
        private const float MinWidth = 60f;
        private const float RefreshInterval = 0.2f;

        private static readonly Color Normal = new Color(0.92f, 0.87f, 0.78f, 1f);
        private static readonly Color Warn = new Color(1f, 0.69f, 0.25f, 1f);
        private static readonly Color Over = new Color(0.9f, 0.2f, 0.15f, 1f);
        private static readonly Color Fill = new Color(0.58f, 0.43f, 0.18f, 0.95f);
        private static readonly Color FillWarn = new Color(0.72f, 0.42f, 0.1f, 0.95f);
        private static readonly Color FillOver = new Color(0.62f, 0.13f, 0.1f, 0.95f);

        private static WeightHud _instance;
        private static bool _failed;

        private RectTransform _rect;
        private CanvasGroup _group;
        private TMP_Text _text;
        private RectTransform _icon;
        private RectTransform _fill;
        private Image _fillImage;

        private int _weight = -1;
        private int _max = -1;
        private float _ratio;
        private string _shownText;
        private float _lastChange;
        private float _nextRefresh;
        private float _nextHotbarScan;
        private Rect? _hotbar; // world-space rect of the main hotbar's first slot, if found
        private float _maxWeight = WeightDisplay.BaseMaxWeight;
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
                Plugin.Log.LogError($"Weight bar disabled for this session: {e}");
            }
        }

        private static void Create(Hud hud, InventoryGui inv)
        {
            var go = new GameObject("KroolWorldUI_Weight", typeof(RectTransform), typeof(CanvasGroup));
            go.transform.SetParent(hud.m_rootObject.transform, false);
            var w = go.AddComponent<WeightHud>();
            _instance = w;
            w._rect = (RectTransform)go.transform;
            w._group = go.GetComponent<CanvasGroup>();
            w._group.blocksRaycasts = false;
            w._group.interactable = false;
            w._group.alpha = 0f;

            // Track, fill, then icon and text drawn on top of the fill.
            var track = NewImage("track", go.transform, new Color(0.05f, 0.035f, 0.025f, 0.75f));
            Stretch(track.rectTransform);
            var frame = track.gameObject.AddComponent<Outline>();
            frame.effectColor = new Color(0.87f, 0.70f, 0.36f, 0.35f);
            frame.effectDistance = new Vector2(1f, -1f);

            w._fillImage = NewImage("fill", go.transform, Fill);
            w._fill = w._fillImage.rectTransform;
            w._fill.anchorMin = Vector2.zero;
            w._fill.anchorMax = new Vector2(0f, 1f);
            w._fill.pivot = new Vector2(0f, 0.5f);
            w._fill.offsetMin = Vector2.zero;
            w._fill.offsetMax = Vector2.zero;

            var iconSprite = FindWeightIcon(inv.m_weight.transform);
            if (iconSprite != null)
            {
                var icon = NewImage("icon", go.transform, Color.white);
                icon.sprite = iconSprite;
                icon.preserveAspect = true;
                w._icon = icon.rectTransform;
                w._icon.anchorMin = new Vector2(0f, 0.5f);
                w._icon.anchorMax = new Vector2(0f, 0.5f);
                w._icon.pivot = new Vector2(0f, 0.5f);
                w._icon.anchoredPosition = new Vector2(3f, 0f);
            }

            // Clone the inventory's own weight text so font and material match the game.
            var textGo = Instantiate(inv.m_weight.gameObject, go.transform, false);
            textGo.name = "text";
            textGo.SetActive(true);
            // The source text may be rotated or scaled in its own panel (the HUD health text is
            // turned 90 degrees); the copy must sit flat.
            textGo.transform.localRotation = Quaternion.identity;
            textGo.transform.localScale = Vector3.one;
            for (int i = textGo.transform.childCount - 1; i >= 0; i--)
                Destroy(textGo.transform.GetChild(i).gameObject);
            foreach (var c in textGo.GetComponents<Component>())
                if (!(c is Transform) && !(c is TMP_Text) && !(c is CanvasRenderer))
                    Destroy(c);
            w._text = textGo.GetComponent<TMP_Text>();
            w._text.enableAutoSizing = false;
            w._text.alignment = TextAlignmentOptions.Center;
            w._text.textWrappingMode = TextWrappingModes.NoWrap;
            w._text.overflowMode = TextOverflowModes.Overflow;
            w._text.raycastTarget = false;
            w._text.color = Normal;
            Stretch(w._text.rectTransform);

            w._lastChange = Time.time;
            w.ApplySize(w.CurrentWidth());
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
                _maxWeight = max;
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

            _group.alpha = Mathf.MoveTowards(_group.alpha, wanted ? 1f : 0f, Time.deltaTime * 4f);
            if (_group.alpha <= 0f && !wanted)
                return;

            // Same flash as the inventory screen when over capacity.
            bool flashRed = over && Mathf.Sin(Time.time * 10f) > 0f;
            Color numColor = flashRed ? Over : near ? Warn : Normal;
            string label = $"<color=#{ColorUtility.ToHtmlStringRGB(numColor)}>{_weight}</color> / {_max}";
            if (label != _shownText)
            {
                // Rebuilding a TMP mesh is not free; only when the text actually changes.
                _shownText = label;
                _text.text = label;
            }

            _fill.anchorMax = new Vector2(Mathf.Clamp01(_ratio), 1f);
            _fillImage.color = over ? FillOver : near ? FillWarn : Fill;

            Place();
        }

        /// <summary>BaseWidth at 300 max weight, scaled by your actual max (belt: 450 = 1.5x).</summary>
        private float CurrentWidth()
        {
            float scale = Mathf.Clamp(_maxWeight / WeightDisplay.BaseMaxWeight, 0.5f, 3f);
            return WeightDisplay.BaseWidth.Value * scale;
        }

        private void ApplySize(float width)
        {
            float h = WeightDisplay.Height.Value;
            _rect.sizeDelta = new Vector2(Mathf.Max(MinWidth, width), h);
            _text.fontSize = Mathf.Round(h * 0.78f);
            if (_icon != null)
                _icon.sizeDelta = new Vector2(h - 4f, h - 4f);
        }

        /// <summary>
        /// Sits just above the hotbar's first slot, left-aligned with it. Falls back to just below
        /// when there is no room above (large UI scale), or to the custom anchor.
        /// </summary>
        private void Place()
        {
            if (Time.time >= _nextHotbarScan)
            {
                _nextHotbarScan = Time.time + 1f;
                _hotbar = FindFirstSlot() ?? _hotbar; // keep the last good position if the bar hides briefly
            }

            ApplySize(CurrentWidth());

            var mode = WeightDisplay.Position.Value;
            if (mode == WeightDisplay.Placement.Custom || _hotbar == null)
            {
                var anchor = new Vector2(WeightDisplay.AnchorX.Value, WeightDisplay.AnchorY.Value);
                _rect.anchorMin = anchor;
                _rect.anchorMax = anchor;
                _rect.pivot = new Vector2(0f, 1f);
                _rect.anchoredPosition = Vector2.zero;
                return;
            }

            var slot = _hotbar.Value;
            var parent = (RectTransform)_rect.parent;
            float worldPerPixel = Mathf.Max(0.0001f, parent.lossyScale.y);
            parent.GetWorldCorners(_corners);
            float screenTop = _corners[1].y;
            float gap = Gap * worldPerPixel;
            float height = WeightDisplay.Height.Value * worldPerPixel;
            bool roomAbove = slot.yMax + gap + height <= screenTop - 1f;

            _rect.anchorMin = _rect.anchorMax = new Vector2(0.5f, 0.5f);
            if (mode == WeightDisplay.Placement.AboveHotbar && roomAbove)
            {
                _rect.pivot = new Vector2(0f, 0f);
                _rect.position = new Vector3(slot.xMin, slot.yMax + gap, _rect.position.z);
            }
            else
            {
                _rect.pivot = new Vector2(0f, 1f);
                _rect.position = new Vector3(slot.xMin, slot.yMin - gap, _rect.position.z);
            }
        }

        /// <summary>
        /// World rect of the main hotbar's first slot frame. The game places slot i at
        /// (i * m_elementSpace, 0) from the HotkeyBar origin using m_elementPrefab, so the frame is
        /// fixed no matter which items or gear you have; measuring visible graphics was not.
        /// The main hotbar is the highest HotkeyBar on screen (mods such as ExtraSlots add more).
        /// </summary>
        private Rect? FindFirstSlot()
        {
            Rect? best = null;
            foreach (var bar in FindObjectsByType<HotkeyBar>(FindObjectsSortMode.None))
            {
                if (!bar.isActiveAndEnabled || bar.m_elementPrefab == null)
                    continue;
                var prefab = bar.m_elementPrefab.transform as RectTransform;
                if (prefab == null)
                    continue;
                Vector2 size = prefab.rect.size, pivot = prefab.pivot;
                if (size.x < 1f || size.y < 1f) // stretched prefab outside a parent: fall back to the slot spacing
                    size = new Vector2(bar.m_elementSpace, bar.m_elementSpace);
                var t = bar.transform;
                Vector3 bl = t.TransformPoint(new Vector3(-pivot.x * size.x, -pivot.y * size.y, 0f));
                Vector3 tr = t.TransformPoint(new Vector3((1f - pivot.x) * size.x, (1f - pivot.y) * size.y, 0f));
                var r = Rect.MinMaxRect(Mathf.Min(bl.x, tr.x), Mathf.Min(bl.y, tr.y), Mathf.Max(bl.x, tr.x), Mathf.Max(bl.y, tr.y));
                if (best == null || r.yMax > best.Value.yMax)
                    best = r;
            }
            return best;
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

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}
