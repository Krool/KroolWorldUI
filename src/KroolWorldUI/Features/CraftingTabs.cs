using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace KroolWorldUI.Features
{
    /// <summary>
    /// Category tabs and a sort order for the crafting list (any station, and hand crafting).
    /// A strip of item-type icons sits above the recipe list and a sort button below it; the list
    /// itself is the game's, filtered and reordered after InventoryGui.UpdateRecipeList builds it,
    /// so selecting, crafting and the Upgrade tab work as before. Categories with nothing in them
    /// at this station are hidden. The chosen category is remembered per station for the
    /// session. On a controller, the D-pad left/right changes category. Display only.
    /// </summary>
    internal static class CraftingTabs
    {
        internal enum Category { All, Weapons, Armor, Tools, Ammo, Food, Materials, Other }

        /// <summary>
        /// Default: the game's order (craftable first, then its own grouping).
        /// Name: A to Z. Type: grouped by category. Station Level: newest unlocks first.
        /// </summary>
        internal enum SortOrder { Default, Name, Type, StationLevel }

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> Categories;
        internal static ConfigEntry<SortOrder> Sort;
        internal static ConfigEntry<bool> CraftableFirst;
        internal static ConfigEntry<bool> SortButton;

        private const string Section = "Crafting";

        // An item icon per category (prefab names, language-independent); All and Other are drawn.
        private static readonly string[] IconPrefabs =
            { null, "SwordBronze", "HelmetBronze", "Hammer", "ArrowWood", "CookedMeat", "Bronze", null };

        private static readonly string[] CategoryKeys =
            { "craft_all", "craft_weapons", "craft_armor", "craft_tools", "craft_ammo", "craft_food", "craft_materials", "craft_other" };

        private static readonly string[] SortKeys =
            { "craft_sort_default", "craft_sort_name", "craft_sort_type", "craft_sort_level" };

        internal static void BindConfig(ConfigFile config)
        {
            Enabled = config.Bind(Section, "Enabled", true,
                "Category tabs and a sort order for the crafting list at every station.");
            Categories = config.Bind(Section, "Categories", true,
                "A row of tabs above the list: weapons, armor, tools, ammo, food, materials. " +
                "On a controller, the D-pad left and right switch tabs.");
            Sort = config.Bind(Section, "Sort", SortOrder.Default,
                "Default: the game's order. Name: A to Z. Type: grouped like the tabs. " +
                "Station Level: newest unlocks first.");
            CraftableFirst = config.Bind(Section, "CraftableFirst", true,
                ConfigText.Desc("List what you can craft right now at the top.",
                    when: () => Sort.Value != SortOrder.Default, whenText: "Sort is not Default"));
            SortButton = config.Bind(Section, "SortButton", true,
                "A button under the list that changes the sort order. Right-click goes back.");

            Enabled.SettingChanged += (s, e) => Refresh();
            Categories.SettingChanged += (s, e) => Refresh();
            SortButton.SettingChanged += (s, e) => Refresh();
            Sort.SettingChanged += (s, e) => Refresh();
            CraftableFirst.SettingChanged += (s, e) => Refresh();
        }

        // ------------------------------------------------------------------ state

        private static InventoryGui _gui;          // the panel our layout was applied to
        private static InventoryGui _session;      // the panel the remembered tabs belong to (one per login)
        private static RectTransform _list;        // the game's scroll view (Recipes), stretched inside RecipeList
        private static Vector2 _listMin, _listMax; // its original offsets
        private static RectTransform _scroll;      // the list's scrollbar, when it shares the frame
        private static Vector2 _scrollMin, _scrollMax;
        private static float _baseSize;
        private static float _shrink;              // how much the list was shortened
        private static GameObject _bar;
        private static Slot[] _slots;
        private static GameObject _sortRow;
        private static TMP_Text _sortText;
        private static readonly HudKit.FailFlag Failed = new HudKit.FailFlag();

        private static Category _current = Category.All;      // what the list shows now
        private static readonly int[] _counts = new int[8];
        private static readonly Dictionary<string, Category> _perStation = new Dictionary<string, Category>();
        private static readonly List<UnityEngine.Object> _owned = new List<UnityEngine.Object>(); // drawn icons
        private static bool _padLabel;             // the sort button shows the controller hint

        private sealed class Slot
        {
            public Category Category;
            public Button Button;
            public Image Icon;
            public Image Selected;
            public UITooltip Tooltip;
        }

        // Built on first use, not in a static initializer: tools\SettingsDoc runs BindConfig outside the game.
        private static FieldInfo _available;
        private static AccessTools.FieldRef<InventoryGui, float> _baseSizeRef;
        private static Action<InventoryGui, bool> _updatePanel;
        private static PropertyInfo _pRecipe, _pItem, _pElement, _pCanCraft;

        private static bool Wanted => Enabled.Value && (Categories.Value || SortButton.Value || Sort.Value != SortOrder.Default);

        // ------------------------------------------------------------------ patches

        [HarmonyPatch(typeof(InventoryGui), "UpdateRecipeList")]
        private static class FilterAndSort
        {
            private static void Postfix(InventoryGui __instance)
            {
                if (Failed.IsSet)
                    return;
                try
                {
                    Apply(__instance);
                }
                catch (Exception e)
                {
                    Failed.Set();
                    Plugin.Log.LogError($"Crafting tabs: {e}");
                    try { RestoreLayout(); } catch { /* best effort */ }
                }
            }
        }

        // Only runs while the crafting part of the inventory screen has the controller.
        [HarmonyPatch(typeof(InventoryGui), "UpdateRecipeGamepadInput")]
        private static class PadInput
        {
            private static void Postfix()
            {
                if (Failed.IsSet || !Enabled.Value || !Categories.Value || _bar == null)
                    return;
                try
                {
                    if (ZInput.GetButtonDown("JoyDPadLeft"))
                        Step(-1);
                    else if (ZInput.GetButtonDown("JoyDPadRight"))
                        Step(1);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"Crafting tabs (controller): {e}");
                }
            }
        }

        // The sort button shows the D-pad hint while a controller is in use; swap when that changes.
        [HarmonyPatch(typeof(InventoryGui), "Update")]
        private static class PadLabel
        {
            private static void Postfix()
            {
                if (_sortText != null && (ZInput.IsGamepadActive() && Categories.Value) != _padLabel)
                    UpdateSortLabel();
            }
        }

        // ------------------------------------------------------------------ list

        private static void Apply(InventoryGui gui)
        {
            if (!Wanted)
            {
                RestoreLayout();
                return;
            }
            Bind();
            if (!ReferenceEquals(_session, gui))
            {
                _session = gui;
                _perStation.Clear(); // a new login: start from All everywhere
            }
            EnsureLayout(gui);

            var list = (IList)_available.GetValue(gui);
            var rows = new List<object>(list.Count);
            foreach (var row in list)
                rows.Add(row);

            // What each tab would hold, before filtering.
            Array.Clear(_counts, 0, _counts.Length);
            foreach (var row in rows)
                _counts[(int)CategoryOf(RecipeOf(row))]++;
            _counts[0] = rows.Count;

            // The station's remembered tab, or All when it has nothing for that tab.
            _current = Categories.Value && _perStation.TryGetValue(StationKey(), out var chosen) ? chosen : Category.All;
            if (_counts[(int)_current] == 0)
                _current = Category.All;

            if (_current != Category.All)
            {
                for (int i = rows.Count - 1; i >= 0; i--)
                {
                    if (CategoryOf(RecipeOf(rows[i])) == _current)
                        continue;
                    var element = ElementOf(rows[i]);
                    element.SetActive(false); // Destroy waits for the end of the frame
                    UnityEngine.Object.Destroy(element);
                    rows.RemoveAt(i);
                }
            }
            if (Sort.Value != SortOrder.Default)
                SortRows(rows);

            list.Clear();
            foreach (var row in rows)
                list.Add(row);

            // Lay the rows out again, as the game does after its own sort.
            float space = gui.m_recipeListSpace;
            for (int i = 0; i < rows.Count; i++)
                ((RectTransform)ElementOf(rows[i]).transform).anchoredPosition = new Vector2(0f, i * -space);
            gui.m_recipeListRoot.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,
                Mathf.Max(_baseSizeRef(gui), rows.Count * space));

            UpdateSlots();
            UpdateSortLabel();
        }

        private static void SortRows(List<object> rows)
        {
            var order = Sort.Value;
            bool craftableFirst = CraftableFirst.Value;
            var names = new Dictionary<Recipe, string>();
            // List.Sort is not stable; ties keep the game's order.
            var position = new Dictionary<object, int>(rows.Count, ReferenceComparer.Instance);
            for (int i = 0; i < rows.Count; i++)
                position[rows[i]] = i;
            string NameOf(Recipe r)
            {
                if (!names.TryGetValue(r, out var n))
                    names[r] = n = Localization.instance.Localize(r.m_item.m_itemData.m_shared.m_name);
                return n;
            }
            rows.Sort((a, b) =>
            {
                var ra = RecipeOf(a);
                var rb = RecipeOf(b);
                int c = 0;
                if (craftableFirst)
                    c = CanCraftOf(b).CompareTo(CanCraftOf(a));
                if (c == 0 && order == SortOrder.Type)
                {
                    c = CategoryOf(ra).CompareTo(CategoryOf(rb));
                    if (c == 0)
                        c = ra.m_item.m_itemData.m_shared.m_itemType.CompareTo(rb.m_item.m_itemData.m_shared.m_itemType);
                }
                if (c == 0 && order == SortOrder.StationLevel)
                {
                    c = rb.m_minStationLevel.CompareTo(ra.m_minStationLevel);
                    if (c == 0)
                        c = ra.m_listSortWeight.CompareTo(rb.m_listSortWeight);
                }
                if (c == 0)
                    c = string.Compare(NameOf(ra), NameOf(rb), StringComparison.CurrentCultureIgnoreCase);
                if (c == 0)
                {
                    // Upgrade tab: the same item at several qualities, highest first.
                    var ia = ItemOf(a);
                    var ib = ItemOf(b);
                    if (ia != null && ib != null)
                        c = ib.m_quality.CompareTo(ia.m_quality);
                }
                return c != 0 ? c : position[a].CompareTo(position[b]);
            });
        }

        private sealed class ReferenceComparer : IEqualityComparer<object>
        {
            internal static readonly ReferenceComparer Instance = new ReferenceComparer();
            public new bool Equals(object x, object y) => ReferenceEquals(x, y);
            public int GetHashCode(object o) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(o);
        }

        internal static Category CategoryOf(Recipe recipe)
        {
            var shared = recipe?.m_item?.m_itemData?.m_shared;
            if (shared == null)
                return Category.Other;
            switch (shared.m_itemType)
            {
                case ItemDrop.ItemData.ItemType.OneHandedWeapon:
                case ItemDrop.ItemData.ItemType.TwoHandedWeapon:
                case ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft:
                case ItemDrop.ItemData.ItemType.Bow:
                case ItemDrop.ItemData.ItemType.Shield:
                case ItemDrop.ItemData.ItemType.Attach_Atgeir:
                    // Pickaxes and fishing rods are weapons to the game; to a player they are tools.
                    return shared.m_skillType == Skills.SkillType.Pickaxes || shared.m_skillType == Skills.SkillType.Fishing
                        ? Category.Tools : Category.Weapons;
                case ItemDrop.ItemData.ItemType.Helmet:
                case ItemDrop.ItemData.ItemType.Chest:
                case ItemDrop.ItemData.ItemType.Legs:
                case ItemDrop.ItemData.ItemType.Shoulder:
                case ItemDrop.ItemData.ItemType.Hands:
                case ItemDrop.ItemData.ItemType.Utility:
                case ItemDrop.ItemData.ItemType.Trinket:
                    return Category.Armor;
                case ItemDrop.ItemData.ItemType.Tool:
                case ItemDrop.ItemData.ItemType.Torch:
                    return Category.Tools;
                case ItemDrop.ItemData.ItemType.Ammo:
                case ItemDrop.ItemData.ItemType.AmmoNonEquipable:
                    return Category.Ammo;
                case ItemDrop.ItemData.ItemType.Consumable:
                    return Category.Food;
                case ItemDrop.ItemData.ItemType.Material:
                    return Category.Materials;
                default:
                    return Category.Other;
            }
        }

        private static void Bind()
        {
            if (_available != null)
                return;
            _available = AccessTools.Field(typeof(InventoryGui), "m_availableRecipes");
            _baseSizeRef = AccessTools.FieldRefAccess<InventoryGui, float>("m_recipeListBaseSize");
            _updatePanel = AccessTools.MethodDelegate<Action<InventoryGui, bool>>(
                AccessTools.Method(typeof(InventoryGui), "UpdateCraftingPanel"));
            var pair = AccessTools.Inner(typeof(InventoryGui), "RecipeDataPair");
            _pRecipe = AccessTools.Property(pair, "Recipe");
            _pItem = AccessTools.Property(pair, "ItemData");
            _pElement = AccessTools.Property(pair, "InterfaceElement");
            _pCanCraft = AccessTools.Property(pair, "CanCraft");
        }

        private static Recipe RecipeOf(object row) => (Recipe)_pRecipe.GetValue(row);
        private static ItemDrop.ItemData ItemOf(object row) => (ItemDrop.ItemData)_pItem.GetValue(row);
        private static GameObject ElementOf(object row) => (GameObject)_pElement.GetValue(row);
        private static bool CanCraftOf(object row) => (bool)_pCanCraft.GetValue(row);

        private static string StationKey()
        {
            var station = Player.m_localPlayer != null ? Player.m_localPlayer.GetCurrentCraftingStation() : null;
            return station != null ? station.m_name : "";
        }

        // ------------------------------------------------------------------ tabs

        private static void Select(Category category)
        {
            if (category == _current || _counts[(int)category] == 0)
                return;
            _perStation[StationKey()] = category;
            Rebuild();
        }

        /// <summary>The next tab with something in it, wrapping around.</summary>
        private static void Step(int direction)
        {
            int n = _counts.Length;
            int i = (int)_current;
            for (int k = 0; k < n; k++)
            {
                i = (i + direction + n) % n;
                if (_counts[i] > 0)
                {
                    Select((Category)i);
                    return;
                }
            }
        }

        private static void CycleSort(int direction)
        {
            int n = Enum.GetValues(typeof(SortOrder)).Length;
            Sort.Value = (SortOrder)(((int)Sort.Value + direction + n) % n); // SettingChanged rebuilds the list
        }

        /// <summary>Rebuilds the list through the game, keeping the selected recipe if it is still listed.</summary>
        private static void Rebuild()
        {
            var gui = InventoryGui.instance;
            if (gui != null && InventoryGui.IsVisible() && Player.m_localPlayer != null && _updatePanel != null)
                _updatePanel(gui, true);
        }

        private static void Refresh()
        {
            try
            {
                if (_updatePanel == null && InventoryGui.instance != null)
                    Bind();
                if (!Wanted)
                    RestoreLayout();
                Rebuild();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Crafting tabs: {e}");
            }
        }

        private static void UpdateSlots()
        {
            if (_slots == null || _bar == null)
                return;
            // Only the tabs this station has something for, spread across the strip.
            int shown = 0;
            foreach (var slot in _slots)
                if (_counts[(int)slot.Category] > 0)
                    shown++;
            const float gap = 1f;
            float w = (((RectTransform)_bar.transform).rect.width - gap * (shown - 1)) / Mathf.Max(1, shown);
            int k = 0;
            foreach (var slot in _slots)
            {
                bool any = _counts[(int)slot.Category] > 0;
                slot.Button.gameObject.SetActive(any);
                if (!any)
                    continue;
                var rt = (RectTransform)slot.Button.transform;
                rt.anchoredPosition = new Vector2(k++ * (w + gap), 0f);
                rt.sizeDelta = new Vector2(w, BarHeight);
                slot.Selected.enabled = slot.Category == _current;
                if (slot.Tooltip != null)
                {
                    slot.Tooltip.m_topic = "";
                    slot.Tooltip.m_text = $"{L10n.T(CategoryKeys[(int)slot.Category])} ({_counts[(int)slot.Category]})";
                }
            }
        }

        private static void UpdateSortLabel()
        {
            if (_sortText == null)
                return;
            _padLabel = ZInput.IsGamepadActive() && Categories.Value;
            string text = _padLabel
                ? L10n.T("craft_pad_hint", L10n.T(CategoryKeys[(int)_current]))
                : L10n.T("craft_sort", L10n.T(SortKeys[(int)Sort.Value]));
            if (_sortText.text != text)
                _sortText.text = text;
        }

        // ------------------------------------------------------------------ layout

        private const float BarHeight = 30f;
        private const float SortHeight = 26f;
        private const float Gap = 4f;

        /// <summary>
        /// Shortens the game's recipe list to make room for the tab strip above it and the sort
        /// button below, measured from where the list actually is (another mod may have moved it).
        /// The list is stretched inside its frame, so this works on offsets, which mean the same
        /// whatever the anchors and pivot. Applied once per inventory screen; the screen is rebuilt
        /// after each logout.
        /// </summary>
        private static void EnsureLayout(InventoryGui gui)
        {
            bool wantBar = Categories.Value;
            bool wantSort = SortButton.Value;
            if (_gui == gui && _list != null && (_bar != null) == wantBar && (_sortRow != null) == wantSort)
                return;
            RestoreLayout();

            _gui = gui;
            _list = gui.m_recipeListRoot.parent as RectTransform;
            if (_list == null)
                throw new InvalidOperationException("recipe list has no parent");
            var parent = _list.parent as RectTransform;
            if (parent == null)
                throw new InvalidOperationException("recipe list frame is not a RectTransform");
            _listMin = _list.offsetMin;
            _listMax = _list.offsetMax;
            _baseSize = _baseSizeRef(gui);

            // The list's top and bottom edges in the frame's own space, before shrinking.
            var frame = parent.rect;
            float listTop = frame.yMin + _list.anchorMax.y * frame.height + _listMax.y;
            float listBottom = frame.yMin + _list.anchorMin.y * frame.height + _listMin.y;

            float top = wantBar ? BarHeight + Gap : 0f;
            float bottom = wantSort ? SortHeight + Gap : 0f;
            _shrink = top + bottom;
            _list.offsetMax = _listMax - new Vector2(0f, top);
            _list.offsetMin = _listMin + new Vector2(0f, bottom);
            _baseSizeRef(gui) = _baseSize - _shrink;

            _scroll = gui.m_recipeListScroll != null ? gui.m_recipeListScroll.transform as RectTransform : null;
            if (_scroll != null && _scroll.parent == parent)
            {
                _scrollMin = _scroll.offsetMin;
                _scrollMax = _scroll.offsetMax;
                _scroll.offsetMax = _scrollMax - new Vector2(0f, top);
                _scroll.offsetMin = _scrollMin + new Vector2(0f, bottom);
            }
            else
            {
                _scroll = null;
            }

            if (wantBar)
                BuildBar(gui, Strip("KroolWorldUI_CraftTabs", parent, 1f, listTop - frame.yMax - BarHeight, BarHeight));
            if (wantSort)
                BuildSortRow(gui, Strip("KroolWorldUI_CraftSort", parent, 0f, listBottom - frame.yMin, SortHeight));
            Plugin.Log.LogDebug($"Crafting tabs: frame {frame}, list {listBottom}..{listTop}, now {_list.rect}");
        }

        private static void RestoreLayout()
        {
            if (_bar != null)
                UnityEngine.Object.Destroy(_bar);
            if (_sortRow != null)
                UnityEngine.Object.Destroy(_sortRow);
            _bar = null;
            _sortRow = null;
            _slots = null;
            _sortText = null;
            if (_gui != null && _list != null)
            {
                _list.offsetMin = _listMin;
                _list.offsetMax = _listMax;
                if (_scroll != null)
                {
                    _scroll.offsetMin = _scrollMin;
                    _scroll.offsetMax = _scrollMax;
                }
                _baseSizeRef(_gui) = _baseSize;
            }
            _gui = null;
            _list = null;
            _scroll = null;
            foreach (var o in _owned)
                HudKit.Free(o);
            _owned.Clear();
        }

        /// <summary>
        /// A strip the frame's full width, anchored to its top (<paramref name="edge"/> 1) or bottom
        /// (0) edge, with its bottom <paramref name="y"/> from that edge.
        /// </summary>
        private static RectTransform Strip(string name, Transform parent, float edge, float y, float height)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0f, edge);
            rt.anchorMax = new Vector2(1f, edge);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(0f, y);
            rt.offsetMax = new Vector2(0f, y + height);
            return rt;
        }

        private static void BuildBar(InventoryGui gui, RectTransform bar)
        {
            _bar = bar.gameObject;
            GetRecipeSprites(gui, out var bkg, out var selected);
            var tooltipPrefab = gui.m_craftButton != null ? gui.m_craftButton.GetComponent<UITooltip>()?.m_tooltipPrefab : null;
            var sfx = gui.m_tabCraft != null ? gui.m_tabCraft.GetComponent<ButtonSfx>() : null;

            int n = CategoryKeys.Length;
            float gap = 1f;
            float w = (((RectTransform)bar.parent).rect.width - gap * (n - 1)) / n;
            _slots = new Slot[n];
            for (int i = 0; i < n; i++)
            {
                var cat = (Category)i;
                var slotGo = new GameObject("tab_" + cat, typeof(RectTransform), typeof(Image), typeof(Button));
                slotGo.transform.SetParent(bar, false);
                var rt = (RectTransform)slotGo.transform;
                rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
                rt.pivot = new Vector2(0f, 1f);
                rt.anchoredPosition = new Vector2(i * (w + gap), 0f);
                rt.sizeDelta = new Vector2(w, BarHeight);

                var back = slotGo.GetComponent<Image>();
                CopySprite(bkg, back, new Color(0f, 0f, 0f, 0.5f));

                var sel = HudKit.NewImage("selected", rt, new Color(1f, 0.75f, 0.3f, 0.6f));
                CopySprite(selected, sel, new Color(1f, 0.75f, 0.3f, 0.6f));
                HudKit.Stretch(sel.rectTransform);

                var icon = HudKit.NewImage("icon", rt, Color.white);
                icon.sprite = IconFor(cat);
                icon.preserveAspect = true;
                // Item icons carry their own transparent margin, so they may reach past the slot a little.
                float side = Mathf.Min(w, BarHeight) + 3f;
                icon.rectTransform.sizeDelta = new Vector2(side, side);

                var button = slotGo.GetComponent<Button>();
                button.targetGraphic = back;
                button.navigation = new Navigation { mode = Navigation.Mode.None };
                var colors = button.colors;
                colors.normalColor = new Color(0.8f, 0.8f, 0.8f, 1f);
                colors.highlightedColor = Color.white;
                colors.selectedColor = colors.normalColor;
                colors.pressedColor = new Color(0.65f, 0.65f, 0.65f, 1f);
                colors.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.6f);
                button.colors = colors;
                button.onClick.AddListener(() => Select(cat));
                if (sfx != null)
                    slotGo.AddComponent<ButtonSfx>().m_sfxPrefab = sfx.m_sfxPrefab;

                UITooltip tip = null;
                if (tooltipPrefab != null)
                {
                    tip = slotGo.AddComponent<UITooltip>();
                    tip.m_tooltipPrefab = tooltipPrefab;
                }
                _slots[i] = new Slot { Category = cat, Button = button, Icon = icon, Selected = sel, Tooltip = tip };
            }
        }

        private static void BuildSortRow(InventoryGui gui, RectTransform row)
        {
            _sortRow = row.gameObject;
            GetRecipeSprites(gui, out var bkg, out _);

            var back = row.gameObject.AddComponent<Image>();
            CopySprite(bkg, back, new Color(0f, 0f, 0f, 0.5f));
            var button = row.gameObject.AddComponent<Button>();
            button.targetGraphic = back;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            var colors = button.colors;
            colors.normalColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            colors.highlightedColor = Color.white;
            colors.selectedColor = colors.normalColor;
            colors.pressedColor = new Color(0.65f, 0.65f, 0.65f, 1f);
            button.colors = colors;
            button.onClick.AddListener(() => CycleSort(1));
            row.gameObject.AddComponent<RightClick>().OnRightClick = () => CycleSort(-1);
            var sfx = gui.m_tabCraft != null ? gui.m_tabCraft.GetComponent<ButtonSfx>() : null;
            if (sfx != null)
                row.gameObject.AddComponent<ButtonSfx>().m_sfxPrefab = sfx.m_sfxPrefab;

            var template = gui.m_recipeElementPrefab.transform.Find("name")?.GetComponent<TMP_Text>();
            if (template == null)
                return;
            _sortText = HudKit.NewText(template, row, "label", TextAlignmentOptions.Center, HudKit.Parchment, 15f);
            _sortText.enableAutoSizing = true;
            _sortText.fontSizeMin = 10f;
            _sortText.fontSizeMax = 15f;
            _sortText.overflowMode = TextOverflowModes.Ellipsis;
            _sortText.rectTransform.offsetMin = new Vector2(6f, 0f);
            _sortText.rectTransform.offsetMax = new Vector2(-6f, 0f);
        }

        /// <summary>Right-click on the sort button steps back through the orders.</summary>
        private sealed class RightClick : MonoBehaviour, IPointerClickHandler
        {
            public Action OnRightClick;

            public void OnPointerClick(PointerEventData data)
            {
                if (data.button == PointerEventData.InputButton.Right)
                    OnRightClick?.Invoke();
            }
        }

        // ------------------------------------------------------------------ art

        /// <summary>The recipe row's own background and highlight, so the tabs match the list.</summary>
        private static void GetRecipeSprites(InventoryGui gui, out Image bkg, out Image selected)
        {
            var prefab = gui.m_recipeElementPrefab != null ? gui.m_recipeElementPrefab.transform : null;
            bkg = prefab != null ? prefab.Find("bkg")?.GetComponent<Image>() : null;
            selected = prefab != null ? prefab.Find("selected")?.GetComponent<Image>() : null;
        }

        private static void CopySprite(Image from, Image to, Color fallback)
        {
            if (from == null || from.sprite == null)
            {
                to.color = fallback;
                return;
            }
            to.sprite = from.sprite;
            to.type = from.type;
            to.color = from.color;
            to.material = from.material;
            to.pixelsPerUnitMultiplier = from.pixelsPerUnitMultiplier;
            to.raycastTarget = true;
        }

        private static Sprite IconFor(Category category)
        {
            string prefab = IconPrefabs[(int)category];
            if (prefab != null && ObjectDB.instance != null)
            {
                var drop = ObjectDB.instance.GetItemPrefab(prefab)?.GetComponent<ItemDrop>();
                var sprite = drop != null ? drop.m_itemData.GetIcon() : null;
                if (sprite != null)
                    return sprite;
            }
            return DrawnIcon(category == Category.All ? 2 : 3);
        }

        /// <summary>A grid of soft dots: 2x2 for All, a row of three for Other (and any missing icon).</summary>
        private static Sprite DrawnIcon(int kind)
        {
            const int size = 32;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            var pixels = new Color32[size * size];
            var centers = kind == 2
                ? new[] { new Vector2(10, 10), new Vector2(22, 10), new Vector2(10, 22), new Vector2(22, 22) }
                : new[] { new Vector2(6, 16), new Vector2(16, 16), new Vector2(26, 16) };
            float radius = kind == 2 ? 5f : 3.5f;
            var ink = new Color(0.92f, 0.87f, 0.78f, 1f);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float a = 0f;
                    foreach (var c in centers)
                        a = Mathf.Max(a, Mathf.Clamp01(radius + 0.5f - Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), c)));
                    pixels[y * size + x] = new Color(ink.r, ink.g, ink.b, a);
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            _owned.Add(tex);
            _owned.Add(sprite);
            return sprite;
        }
    }
}
