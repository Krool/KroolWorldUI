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
    /// selected-piece box counts the materials you carry: what the selected piece costs, plus an
    /// always-shown list (wood and stone by default), and how many of the piece you can afford.
    /// Counts use the same inventory count the game checks when you place. Read-only.
    /// </summary>
    internal static class BuildMaterials
    {
        internal enum Place { BesideBuildInfo, Custom }

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> SelectedPiece;
        internal static ConfigEntry<string> AlwaysShow;
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
            AlwaysShow = config.Bind(Section, "AlwaysShow", "Wood, Stone",
                "Materials always listed, separated by commas. Use the item's name as the game shows it " +
                "(Fine Wood, Iron Nails) or its prefab name (FineWood, BronzeNails).");
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

        // Always-shown list, resolved from AlwaysShow once ObjectDB is loaded.
        private string _resolvedFrom;
        private readonly List<ItemDrop.ItemData.SharedData> _always = new List<ItemDrop.ItemData.SharedData>();

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

            ResolveAlwaysShow();
            foreach (var shared in _always)
            {
                if (!listed.Add(shared.m_name))
                    continue;
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

        /// <summary>Turns AlwaysShow into items, matching prefab names or shown names, ignoring case and spaces.</summary>
        private void ResolveAlwaysShow()
        {
            string wanted = BuildMaterials.AlwaysShow.Value ?? "";
            if (wanted == _resolvedFrom || ObjectDB.instance == null || ObjectDB.instance.m_items.Count == 0)
                return;
            _resolvedFrom = wanted;
            _always.Clear();
            var byName = new Dictionary<string, ItemDrop.ItemData.SharedData>();
            foreach (var prefab in ObjectDB.instance.m_items)
            {
                var shared = prefab != null ? prefab.GetComponent<ItemDrop>()?.m_itemData?.m_shared : null;
                if (shared == null)
                    continue;
                byName[Key(prefab.name)] = shared;
                if (Localization.instance != null)
                {
                    string shownName = Key(Localization.instance.Localize(shared.m_name));
                    if (!byName.ContainsKey(shownName))
                        byName[shownName] = shared;
                }
            }
            foreach (var part in wanted.Split(','))
            {
                string key = Key(part);
                if (key.Length == 0)
                    continue;
                if (byName.TryGetValue(key, out var shared) && !_always.Contains(shared))
                    _always.Add(shared);
                else if (!byName.ContainsKey(key))
                    Plugin.Log.LogWarning($"Build materials: no item named '{part.Trim()}'.");
            }
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
