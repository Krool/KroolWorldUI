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
    /// selected-piece box counts the materials you carry: what the selected piece costs, the
    /// common building materials (each with its own When setting), and how many of the piece you
    /// can afford. Counts use the same inventory count the game checks when you place. Read-only.
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
        internal static readonly (string Key, string Prefab, Show Default)[] Materials =
        {
            ("Wood", "Wood", Show.WhenKnown),
            ("Stone", "Stone", Show.WhenKnown),
            ("CoreWood", "RoundLog", Show.WhenKnown),
            ("FineWood", "FineWood", Show.WhenKnown),
            ("Resin", "Resin", Show.WhenCarried),
            ("BronzeNails", "BronzeNails", Show.WhenCarried),
            ("IronNails", "IronNails", Show.WhenCarried),
            ("BlackMarble", "BlackMarble", Show.WhenCarried),
            ("YggdrasilWood", "YggdrasilWood", Show.WhenCarried),
            ("Grausten", "Grausten", Show.WhenCarried),
            ("Ashwood", "Blackwood", Show.WhenCarried),
        };

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> SelectedPiece;
        internal static ConfigEntry<Show>[] MaterialShow;
        internal static ConfigEntry<string> OtherMaterials;
        internal static ConfigEntry<bool> CanBuildCount;
        internal static ConfigEntry<Place> Placement;
        internal static ConfigEntry<float> AnchorX;
        internal static ConfigEntry<float> AnchorY;

        private const string Section = "Build Materials";

        internal static void BindConfig(ConfigFile config)
        {
            Enabled = config.Bind(Section, "Enabled", true,
                "While a build tool is out, count the materials you carry, so you can watch your wood and " +
                "stone as you build.");
            SelectedPiece = config.Bind(Section, "SelectedPiece", true,
                "List what the selected piece costs, each with how many you carry. Red when you are short.");
            CanBuildCount = config.Bind(Section, "CanBuildCount", true,
                "Show how many of the selected piece your materials cover.");
            Placement = config.Bind(Section, "Placement", Place.BesideBuildInfo,
                "Beside Build Info: right of the selected-piece box at the bottom center. " +
                "Custom: placed by Anchor X and Anchor Y.");
            AnchorX = config.Bind(Section, "AnchorX", 0.8f,
                new ConfigDescription("Custom placement: left edge (0 = left of screen, 1 = right).",
                    new AcceptableValueRange<float>(0f, 1f)));
            AnchorY = config.Bind(Section, "AnchorY", 0.1f,
                new ConfigDescription("Custom placement: bottom edge (0 = bottom of screen, 1 = top).",
                    new AcceptableValueRange<float>(0f, 1f)));

            MaterialShow = new ConfigEntry<Show>[Materials.Length];
            for (int i = 0; i < Materials.Length; i++)
            {
                string name = ConfigText.Pretty(Materials[i].Key);
                MaterialShow[i] = config.Bind(Section, Materials[i].Key, Materials[i].Default,
                    $"When to list {name}. When Known: once your character has picked one up. " +
                    "When Carried: only while you have some.");
            }
            OtherMaterials = config.Bind(Section, "OtherMaterials", "",
                "More materials to always list, by the names the game shows, separated by commas " +
                "(for example: Copper, Deer Hide).");
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
        private const float Width = 210f;
        private const float RowHeight = 28f;
        private const float Pad = 6f;
        private const float Gap = 12f;
        private const float RefreshSeconds = 0.2f;

        private static BuildMaterialsHud _instance;
        private static bool _failed;

        private RectTransform _rect;
        private CanvasGroup _group;
        private TMP_Text _template;
        private RectTransform _buildInfo;
        private float _nextRefresh;

        // Items for BuildMaterials.Materials (same order; null if a game update renamed one) and for
        // Other Materials, resolved once ObjectDB is loaded and again when Other Materials changes.
        private ItemDrop.ItemData.SharedData[] _common;
        private string _otherFrom;
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
            if (_instance != null || _failed)
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
            }
            catch (System.Exception e)
            {
                _failed = true;
                Plugin.Log.LogError($"Build materials disabled for this session: {e}");
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
                Plugin.Log.LogError($"Build materials disabled for this session after an error: {e}");
                Destroy(gameObject);
            }
        }

        private void Tick()
        {
            var player = Player.m_localPlayer;
            bool building = player != null && player.InPlaceMode();
            bool preview = SettingsWindow.IsOpen && SettingsWindow.Previewing;
            bool wanted = BuildMaterials.Enabled.Value && player != null && (building || preview)
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
            _group.alpha = _rows.Count > 0 && _rows[0].Root.activeSelf ? 1f : 0f;
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
                    SetRow(shown++, shared, $"{have}<size=75%><color=#{Hex(HudKit.Muted)}>  /{req.m_amount}</color></size>", color);
                }
            }

            Resolve();
            // The hoe and cultivator build terrain and crops; wood and stone counts are noise there.
            bool listMaterials = !UsingTerrainTool(player);
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
                    ? $"Enough for <color=#{Hex(HudKit.Gold)}>{canBuild}</color>"
                    : $"<color=#{Hex(HudKit.Warn)}>Not enough</color>";
            }

            int lines = shown + (footer ? 1 : 0);
            for (int i = 0; i < shown; i++)
                Lay((RectTransform)_rows[i].Root.transform, i);
            if (footer)
                Lay(_footer.rectTransform, shown);
            _rect.sizeDelta = new Vector2(Width, RowHeight * Mathf.Max(1, lines) + Pad * 2f);
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
            if ((_common != null && other == _otherFrom) || ObjectDB.instance == null || ObjectDB.instance.m_items.Count == 0)
                return;
            var byPrefab = new Dictionary<string, ItemDrop.ItemData.SharedData>();
            var byShown = new Dictionary<string, ItemDrop.ItemData.SharedData>();
            foreach (var prefab in ObjectDB.instance.m_items)
            {
                var shared = prefab != null ? prefab.GetComponent<ItemDrop>()?.m_itemData?.m_shared : null;
                if (shared == null)
                    continue;
                byPrefab[Key(prefab.name)] = shared;
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

        private static string Hex(Color c) => ColorUtility.ToHtmlStringRGB(c);

        /// <summary>Right of the build menu's selected-piece box, or the custom anchor.</summary>
        private void Place()
        {
            bool beside = BuildMaterials.Placement.Value == BuildMaterials.Place.BesideBuildInfo;
            if (beside && _buildInfo != null && _buildInfo.gameObject.activeInHierarchy)
            {
                _buildInfo.GetWorldCorners(_corners);
                float right = _corners[2].x, left = _corners[0].x, bottom = _corners[0].y;
                float gap = Gap * _rect.lossyScale.x;
                float width = _rect.rect.width * _rect.lossyScale.x;
                // Not enough room on the right (narrow screens): sit left of the box instead.
                float x = right + gap + width <= Screen.width ? right + gap : left - gap - width;
                _rect.anchorMin = _rect.anchorMax = Vector2.zero;
                _rect.position = new Vector3(x, bottom, _rect.position.z);
                return;
            }
            // Custom, or the build menu is not up (Preview on HUD): bottom center, right of middle.
            var anchor = beside ? new Vector2(0.5f, 0f) : new Vector2(BuildMaterials.AnchorX.Value, BuildMaterials.AnchorY.Value);
            _rect.anchorMin = anchor;
            _rect.anchorMax = anchor;
            _rect.anchoredPosition = beside ? new Vector2(270f, 95f) : Vector2.zero;
        }
    }
}
