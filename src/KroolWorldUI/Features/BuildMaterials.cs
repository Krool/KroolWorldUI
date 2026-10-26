using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KroolWorldUI.Features
{
    /// <summary>
    /// While a build tool is out (hammer, hoe, cultivator), a small panel beside the build menu's
    /// selected-piece box counts the materials you carry: what the selected piece costs and how
    /// many of it you can afford, or with nothing selected, the common building materials (each
    /// with its own When setting). Counts use the same inventory count the game checks when you place. Read-only.
    /// </summary>
    internal static class BuildMaterials
    {
        internal enum Place { BesideBuildInfo, Custom }

        /// <summary>
        /// When a material is listed. When Known: once the character has ever picked one up (the
        /// game's "new material" unlock, saved with the character). When Carried: while you have any.
        /// </summary>
        internal enum Show { Always, WhenKnown, WhenCarried, Never }

        /// <summary>
        /// The common building materials, in progression order. Setting key, the item's prefab name
        /// (language-independent, used to find it), and its default: the four everyday ones once
        /// found, later ones only while carried so a late-game panel stays short.
        /// </summary>
        internal static readonly (string Key, string Prefab, string Name, Show Default)[] Materials =
        {
            ("Wood", "Wood", "Wood", Show.WhenKnown),
            ("Stone", "Stone", "Stone", Show.WhenKnown),
            ("CoreWood", "RoundLog", "Corewood", Show.WhenKnown),
            ("FineWood", "FineWood", "Finewood", Show.WhenKnown),
            ("Resin", "Resin", "Resin", Show.WhenCarried),
            ("BronzeNails", "BronzeNails", "Bronze Nails", Show.WhenCarried),
            ("IronNails", "IronNails", "Iron Nails", Show.WhenCarried),
            ("BlackMarble", "BlackMarble", "Black Marble", Show.WhenCarried),
            ("YggdrasilWood", "YggdrasilWood", "Yggdrasil Wood", Show.WhenCarried),
            ("Grausten", "Grausten", "Grausten", Show.WhenCarried),
            ("Ashwood", "Blackwood", "Ashwood", Show.WhenCarried),
        };

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> SelectedPiece;
        internal static ConfigEntry<bool> CanBuildCount;
        internal static ConfigEntry<bool> CommonWithCost;
        internal static ConfigEntry<Show>[] MaterialShow;
        internal static ConfigEntry<string> OtherMaterials;
        internal static ConfigEntry<float> Size;
        internal static ConfigEntry<float> Opacity;
        internal static ConfigEntry<Place> Placement;
        internal static ConfigEntry<float> AnchorX;
        internal static ConfigEntry<float> AnchorY;
        internal static ConfigEntry<float> OffsetX;
        internal static ConfigEntry<float> OffsetY;

        private const string Section = "Build Materials";

        internal static void BindConfig(ConfigFile config)
        {
            Enabled = config.Bind(Section, "Enabled", true,
                "With the hammer out, a panel beside the build menu counts the materials you carry.");

            const string piece = "Selected piece";
            SelectedPiece = config.Bind(Section, "SelectedPiece", true,
                ConfigText.Desc("List what the selected piece costs and how many you have. Red when short.", group: piece));
            CanBuildCount = config.Bind(Section, "CanBuildCount", true,
                ConfigText.Desc("How many of the selected piece you can build.", group: piece,
                    when: () => SelectedPiece.Value, whenText: "Selected Piece is on"));
            CommonWithCost = config.Bind(Section, "CommonWithCost", false,
                ConfigText.Desc("Also list the materials below while a piece is selected. Off: only its cost.",
                    group: piece, when: () => SelectedPiece.Value, whenText: "Selected Piece is on"));

            const string common = "Materials (nothing selected)";
            MaterialShow = new ConfigEntry<Show>[Materials.Length];
            for (int i = 0; i < Materials.Length; i++)
            {
                string name = Materials[i].Name; // the game's English spelling
                MaterialShow[i] = config.Bind(Section, Materials[i].Key, Materials[i].Default,
                    ConfigText.Desc($"When to list {name}. When Known: once you have found one. " +
                                    "When Carried: while you have some.", group: common, label: name));
            }
            OtherMaterials = config.Bind(Section, "OtherMaterials", "",
                ConfigText.Desc("More materials to list, separated by commas: Copper, Deer Hide. Names as the game " +
                                "shows them, or item names (DeerHide), which work in every language.", group: common));

            const string look = "Look";
            Size = config.Bind(Section, "Size", 1f,
                ConfigText.Desc("Panel size. 1 is the normal size.", new AcceptableValueRange<float>(0.75f, 1.5f), look, step: 0.05f));
            Opacity = config.Bind(Section, "Opacity", 1f,
                ConfigText.Desc("1 is solid; lower shows the game through it.", new AcceptableValueRange<float>(0.2f, 1f), look, step: 0.05f));

            const string placement = "Placement";
            Placement = config.Bind(Section, "Placement", Place.BesideBuildInfo,
                ConfigText.Desc("Beside Build Info: right of the piece box at the bottom of the screen. " +
                                "Custom: at Anchor X and Anchor Y.", group: placement));
            AnchorX = config.Bind(Section, "AnchorX", 0.8f,
                ConfigText.Desc("Left edge. 0 is the left of the screen, 1 the right.",
                    new AcceptableValueRange<float>(0f, 1f), placement,
                    () => Placement.Value == Place.Custom, "Placement is Custom", 0.01f));
            AnchorY = config.Bind(Section, "AnchorY", 0.1f,
                ConfigText.Desc("Bottom edge. 0 is the bottom of the screen, 1 the top.",
                    new AcceptableValueRange<float>(0f, 1f), placement,
                    () => Placement.Value == Place.Custom, "Placement is Custom", 0.01f));
            OffsetX = config.Bind(Section, "OffsetX", 0f,
                ConfigText.Desc("Move left (-) or right (+), in HUD pixels.",
                    new AcceptableValueRange<float>(-400f, 400f), placement));
            OffsetY = config.Bind(Section, "OffsetY", 0f,
                ConfigText.Desc("Move down (-) or up (+), in HUD pixels.",
                    new AcceptableValueRange<float>(-400f, 400f), placement));
        }
        [HarmonyPatch(typeof(Hud), "Update")]
        private static class EnsureOnHud
        {
            private static void Postfix()
            {
                if (Enabled.Value)
                    BuildMaterialsHud.Ensure();
            }
        }
    }

    internal class BuildMaterialsHud : MonoBehaviour
    {
        private const float Width = 210f;          // narrowest; grows to fit long names
        private const float MaxWidth = 380f;
        private const float MinCountWidth = 76f;
        private const float RowHeight = 28f;
        private const float Pad = 6f;
        private const float Gap = 12f;
        private const float RefreshSeconds = 0.2f;

        private static BuildMaterialsHud _instance;
        private static readonly HudKit.FailFlag _failed = new HudKit.FailFlag();

        private RectTransform _rect;
        private CanvasGroup _group;
        private TMP_Text _template;
        private RectTransform _buildInfo;
        private float _nextRefresh;

        // Items for BuildMaterials.Materials (same order; null if a game update renamed one) and for
        // Other Materials, resolved once ObjectDB is loaded and again when Other Materials changes.
        private ItemDrop.ItemData.SharedData[] _common;
        private string _otherFrom;
        private string _otherTyped;
        private float _otherTypedAt;
        private const float TypingPause = 1f;
        private readonly List<ItemDrop.ItemData.SharedData> _other = new List<ItemDrop.ItemData.SharedData>();

        private class Row
        {
            public GameObject Root;
            public Image Icon;
            public TMP_Text Name;
            public TMP_Text Count;
        }

        private readonly List<Row> _rows = new List<Row>();
        private TMP_Text _footer;
        private readonly Vector3[] _corners = new Vector3[4];

        internal static void Ensure()
        {
            if (_instance != null || _failed.IsSet)
                return;
            var hud = Hud.instance;
            if (hud == null || hud.m_rootObject == null || hud.m_healthText == null || Player.m_localPlayer == null)
                return;
            try
            {
                var go = new GameObject("KroolWorldUI_BuildMaterials", typeof(RectTransform), typeof(CanvasGroup));
                go.transform.SetParent(hud.m_rootObject.transform, false);
                var b = go.AddComponent<BuildMaterialsHud>();
                _instance = b;
                b._rect = (RectTransform)go.transform;
                b._rect.pivot = Vector2.zero;
                b._rect.sizeDelta = new Vector2(Width, RowHeight + Pad * 2f);
                b._group = go.GetComponent<CanvasGroup>();
                b._group.alpha = 0f;
                b._group.blocksRaycasts = false;
                b._group.interactable = false;
                HudKit.ToastBackground(go.transform);
                b._template = hud.m_healthText;
                b._buildInfo = hud.m_buildHud != null ? hud.m_buildHud.transform.Find("SelectedInfo") as RectTransform : null;
                b._footer = HudKit.NewText(b._template, go.transform, "footer", TextAlignmentOptions.MidlineLeft, HudKit.Muted, 15f);
                L10n.Changed += b.OnLanguage;
            }
            catch (System.Exception e)
            {
                _failed.Set();
                Plugin.Log.LogError($"Build materials disabled for this session: {e}");
            }
        }

        private void OnDestroy()
        {
            L10n.Changed -= OnLanguage;
            if (_instance == this)
                _instance = null;
        }

        // Other Materials are matched by their names in the game's language: look them up again.
        private void OnLanguage() => _otherFrom = null;

        private void Update()
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
                Plugin.Log.LogError($"Build materials disabled for this session after an error: {e}");
                Destroy(gameObject);
            }
        }

        private void Tick()
        {
            var player = Player.m_localPlayer;
            bool building = player != null && player.InPlaceMode();
            bool preview = SettingsWindow.IsOpen && SettingsWindow.Previewing;
            bool wanted = BuildMaterials.Enabled.Value && HudKit.Allowed && player != null && (building || preview)
                          && !InventoryGui.IsVisible() && !Minimap.IsOpen() && !Menu.IsVisible();
            if (!wanted)
            {
                _group.alpha = 0f;
                return;
            }

            if (Time.unscaledTime >= _nextRefresh)
            {
                _nextRefresh = Time.unscaledTime + RefreshSeconds;
                Refresh(player, building ? player.GetSelectedPiece() : null);
            }
            _group.alpha = _rows.Count > 0 && _rows[0].Root.activeSelf ? BuildMaterials.Opacity.Value : 0f;
            float size = BuildMaterials.Size.Value;
            if (!Mathf.Approximately(_rect.localScale.x, size))
                _rect.localScale = new Vector3(size, size, 1f);
            Place();
        }

        private void Refresh(Player player, Piece piece)
        {
            var inventory = player.GetInventory();
            int shown = 0;
            int canBuild = int.MaxValue;
            bool hasCost = false;
            var listed = new HashSet<string>();

            if (piece != null && BuildMaterials.SelectedPiece.Value && piece.m_resources != null)
            {
                foreach (var req in piece.m_resources)
                {
                    var shared = req?.m_resItem?.m_itemData?.m_shared;
                    if (shared == null || req.m_amount <= 0 || !listed.Add(shared.m_name))
                        continue;
                    int have = inventory.CountItems(shared.m_name);
                    hasCost = true;
                    canBuild = Mathf.Min(canBuild, have / req.m_amount);
                    var color = have >= req.m_amount ? HudKit.Parchment : HudKit.Warn;
                    SetRow(shown++, shared, $"{have}<size=75%><color=#{HudKit.Hex(HudKit.Muted)}>  /{req.m_amount}</color></size>", color);
                }
            }

            Resolve();
            // With a costed piece selected, its cost is the whole list unless Common With Cost is on.
            // The hoe and cultivator build terrain and crops; wood and stone counts are noise there.
            bool listMaterials = !UsingTerrainTool(player) && !(hasCost && !BuildMaterials.CommonWithCost.Value);
            if (_common != null && listMaterials)
            {
                for (int i = 0; i < _common.Length; i++)
                {
                    var shared = _common[i];
                    if (shared == null || listed.Contains(shared.m_name))
                        continue;
                    int have = inventory.CountItems(shared.m_name);
                    bool show;
                    switch (BuildMaterials.MaterialShow[i].Value)
                    {
                        case BuildMaterials.Show.Always: show = true; break;
                        case BuildMaterials.Show.WhenKnown: show = have > 0 || player.IsKnownMaterial(shared.m_name); break;
                        case BuildMaterials.Show.WhenCarried: show = have > 0; break;
                        default: show = false; break;
                    }
                    if (show && listed.Add(shared.m_name))
                        SetRow(shown++, shared, have.ToString(), HudKit.Parchment);
                }
            }
            foreach (var shared in _other)
            {
                if (listMaterials && listed.Add(shared.m_name))
                    SetRow(shown++, shared, inventory.CountItems(shared.m_name).ToString(), HudKit.Parchment);
            }

            for (int i = shown; i < _rows.Count; i++)
                _rows[i].Root.SetActive(false);

            // Free building (no-cost cheat or a world's free-build key) makes the count meaningless.
            bool free = piece != null && (player.NoCostCheat()
                                          || (ZoneSystem.instance != null && ZoneSystem.instance.GetGlobalKey(piece.FreeBuildKey())));
            bool footer = BuildMaterials.CanBuildCount.Value && hasCost && !free;
            _footer.gameObject.SetActive(footer);
            if (footer)
            {
                _footer.text = canBuild > 0
                    ? L10n.T("enough_for", $"<color=#{HudKit.Hex(HudKit.Gold)}>{canBuild}</color>")
                    : $"<color=#{HudKit.Hex(HudKit.Warn)}>{L10n.T("not_enough")}</color>";
            }

            // As wide as the longest name and count need (names run long in some languages).
            float countColumn = MinCountWidth, nameColumn = 0f;
            for (int i = 0; i < shown; i++)
            {
                var row = _rows[i];
                countColumn = Mathf.Max(countColumn, Mathf.Ceil(row.Count.GetPreferredValues(row.Count.text).x) + 4f);
                nameColumn = Mathf.Max(nameColumn, Mathf.Ceil(row.Name.GetPreferredValues(row.Name.text).x));
            }
            float width = Pad * 2f + RowHeight + 2f + nameColumn + 8f + countColumn;
            if (footer)
                width = Mathf.Max(width, Pad * 2f + 4f + Mathf.Ceil(_footer.GetPreferredValues(_footer.text).x) + 8f);
            width = Mathf.Clamp(width, Width, MaxWidth);

            int lines = shown + (footer ? 1 : 0);
            for (int i = 0; i < shown; i++)
            {
                Lay((RectTransform)_rows[i].Root.transform, i);
                _rows[i].Name.rectTransform.offsetMax = new Vector2(-(countColumn + 2f), 0f);
                _rows[i].Count.rectTransform.sizeDelta = new Vector2(countColumn, 0f);
            }
            if (footer)
                Lay(_footer.rectTransform, shown);
            _rect.sizeDelta = new Vector2(width, RowHeight * Mathf.Max(1, lines) + Pad * 2f);
        }

        /// <summary>Line i from the top, inside the padding.</summary>
        private void Lay(RectTransform rt, int i)
        {
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(Pad + (rt == _footer.rectTransform ? 4f : 0f), -Pad - RowHeight * (i + 1));
            rt.offsetMax = new Vector2(-Pad, -Pad - RowHeight * i);
        }

        private void SetRow(int index, ItemDrop.ItemData.SharedData shared, string count, Color countColor)
        {
            while (_rows.Count <= index)
                _rows.Add(NewRow());
            var row = _rows[index];
            row.Root.SetActive(true);
            var icon = shared.m_icons != null && shared.m_icons.Length > 0 ? shared.m_icons[0] : null;
            row.Icon.sprite = icon;
            row.Icon.enabled = icon != null;
            row.Name.text = Localization.instance != null ? Localization.instance.Localize(shared.m_name) : shared.m_name;
            row.Count.text = count;
            row.Count.color = countColor;
        }

        private Row NewRow()
        {
            var go = new GameObject("row", typeof(RectTransform));
            go.transform.SetParent(transform, false);

            var icon = HudKit.NewImage("icon", go.transform, Color.white);
            icon.preserveAspect = true;
            var irt = icon.rectTransform;
            irt.anchorMin = irt.anchorMax = new Vector2(0f, 0.5f);
            irt.pivot = new Vector2(0f, 0.5f);
            irt.sizeDelta = new Vector2(RowHeight - 4f, RowHeight - 4f);
            irt.anchoredPosition = Vector2.zero;

            var name = HudKit.NewText(_template, go.transform, "name", TextAlignmentOptions.MidlineLeft, HudKit.Parchment, 16f);
            name.overflowMode = TextOverflowModes.Ellipsis;
            name.rectTransform.offsetMin = new Vector2(RowHeight + 2f, 0f);
            name.rectTransform.offsetMax = new Vector2(-78f, 0f);

            var count = HudKit.NewText(_template, go.transform, "count", TextAlignmentOptions.MidlineRight, HudKit.Parchment, 18f);
            var crt = count.rectTransform;
            crt.anchorMin = new Vector2(1f, 0f);
            crt.anchorMax = new Vector2(1f, 1f);
            crt.pivot = new Vector2(1f, 0.5f);
            crt.sizeDelta = new Vector2(76f, 0f);
            crt.anchoredPosition = new Vector2(-2f, 0f);

            return new Row { Root = go, Icon = icon, Name = name, Count = count };
        }

        /// <summary>
        /// Finds the common materials by prefab name (works in any language) and Other Materials by
        /// the names the game shows (prefab names work too), ignoring case and spaces.
        /// </summary>
        private void Resolve()
        {
            string other = BuildMaterials.OtherMaterials.Value ?? "";
            if (ObjectDB.instance == null || ObjectDB.instance.m_items.Count == 0)
                return;
            // While the list is being typed, wait until it has been still for a moment.
            if (other != _otherTyped)
            {
                _otherTyped = other;
                _otherTypedAt = Time.unscaledTime;
            }
            bool otherReady = other != _otherFrom && Time.unscaledTime - _otherTypedAt >= TypingPause;
            if (_common != null && !otherReady)
                return;

            var byPrefab = new Dictionary<string, ItemDrop.ItemData.SharedData>();
            var byShown = new Dictionary<string, ItemDrop.ItemData.SharedData>();
            foreach (var prefab in ObjectDB.instance.m_items)
            {
                var shared = prefab != null ? prefab.GetComponent<ItemDrop>()?.m_itemData?.m_shared : null;
                if (shared == null)
                    continue;
                byPrefab[Key(prefab.name)] = shared;
                // "$item_deerhide" also matches "Deer Hide" typed in English while playing in another language.
                string token = shared.m_name ?? "";
                if (token.StartsWith("$item_"))
                    byPrefab.TryAdd(Key(token.Substring(6)), shared);
                string shownName = Key(Localization.instance != null ? Localization.instance.Localize(shared.m_name) : shared.m_name);
                if (!byShown.ContainsKey(shownName))
                    byShown[shownName] = shared;
            }

            if (_common == null)
            {
                _common = new ItemDrop.ItemData.SharedData[BuildMaterials.Materials.Length];
                for (int i = 0; i < _common.Length; i++)
                {
                    byPrefab.TryGetValue(Key(BuildMaterials.Materials[i].Prefab), out _common[i]);
                    if (_common[i] == null)
                        Plugin.Log.LogWarning($"Build materials: item '{BuildMaterials.Materials[i].Prefab}' not found.");
                }
            }

            if (!otherReady && _otherFrom != null)
                return;
            _otherFrom = other;
            _other.Clear();
            foreach (var part in other.Split(','))
            {
                string key = Key(part);
                if (key.Length == 0)
                    continue;
                if (byShown.TryGetValue(key, out var shared) || byPrefab.TryGetValue(key, out shared))
                {
                    if (!_other.Contains(shared))
                        _other.Add(shared);
                }
                else
                {
                    Plugin.Log.LogWarning($"Build materials: no item named '{part.Trim()}'.");
                }
            }
        }

        private static bool UsingTerrainTool(Player player)
        {
            if (!player.InPlaceMode())
                return false;
            foreach (var item in player.GetInventory().GetEquippedItems())
            {
                string tool = item?.m_dropPrefab != null ? item.m_dropPrefab.name : null;
                if (tool == "Hoe" || tool == "Cultivator")
                    return true;
            }
            return false;
        }

        private static string Key(string s) => new string((s ?? "").Where(c => !char.IsWhiteSpace(c)).ToArray()).ToLowerInvariant();

        /// <summary>Right of the build menu's selected-piece box, or the custom anchor, plus the offsets.</summary>
        private void Place()
        {
            var offset = new Vector2(BuildMaterials.OffsetX.Value, BuildMaterials.OffsetY.Value);
            var root = _rect.parent as RectTransform;
            bool beside = BuildMaterials.Placement.Value == BuildMaterials.Place.BesideBuildInfo;
            if (beside && root != null && _buildInfo != null && _buildInfo.gameObject.activeInHierarchy)
            {
                // Worked out in the HUD's own space, so it holds for any canvas mode and GUI scale.
                _buildInfo.GetWorldCorners(_corners);
                Vector3 boxMin = root.InverseTransformPoint(_corners[0]);
                Vector3 boxMax = root.InverseTransformPoint(_corners[2]);
                float width = _rect.rect.width * _rect.localScale.x;
                // Not enough room on the right (narrow screens): sit left of the box instead.
                float x = boxMax.x + Gap + width <= root.rect.xMax ? boxMax.x + Gap : boxMin.x - Gap - width;
                _rect.localPosition = new Vector3(x + offset.x, boxMin.y + offset.y, 0f);
                return;
            }
            // Custom, or the build menu is not up (Preview on HUD): bottom center, right of middle.
            var anchor = beside ? new Vector2(0.5f, 0f) : new Vector2(BuildMaterials.AnchorX.Value, BuildMaterials.AnchorY.Value);
            _rect.anchorMin = anchor;
            _rect.anchorMax = anchor;
            _rect.anchoredPosition = (beside ? new Vector2(270f, 95f) : Vector2.zero) + offset;
        }
    }
}
