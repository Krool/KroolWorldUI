using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace KroolWorldUI.Features
{
    /// <summary>
    /// A stats window inside the inventory screen, made from the game's own Skills dialog so it
    /// looks like part of the game. Three tabs: Stats (the character sheet), Deaths (death
    /// recaps) and Bosses (boss recaps). Opened with a key (K), by clicking the armor box in the
    /// inventory, or with a controller button while the inventory is open. Read-only.
    /// </summary>
    internal static class StatsWindow
    {
        internal enum Tab { Stats, Deaths, Bosses }

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<KeyboardShortcut> Key;
        internal static ConfigEntry<FoodCallout.PadButton> ControllerButton;
        internal static ConfigEntry<bool> ArmorButton;

        private const string Section = "Stats Window";

        internal static void BindConfig(ConfigFile config)
        {
            Enabled = config.Bind(Section, "Enabled", true,
                "A stats window in the inventory: health, stamina, regen, speed, carry weight, armor and resistances, " +
                "with what each item, food, potion and Epic Loot enchantment adds. Also your death recaps and boss recaps.");
            Key = config.Bind(Section, "Key", new KeyboardShortcut(KeyCode.K),
                ConfigText.Desc("Opens and closes the window, with or without the inventory open.",
                    when: () => Enabled.Value, whenText: "Stats Window is on"));
            ControllerButton = config.Bind(Section, "ControllerButton", FoodCallout.PadButton.RightStickClick,
                ConfigText.Desc("Controller button that opens and closes the window while the inventory is open.",
                    when: () => Enabled.Value, whenText: "Stats Window is on"));
            ArmorButton = config.Bind(Section, "ArmorButton", true,
                ConfigText.Desc("Click the armor value in the inventory to open the window. A small mark shows it can be clicked.",
                    when: () => Enabled.Value, whenText: "Stats Window is on"));
            Enabled.SettingChanged += (s, e) => { if (!Enabled.Value) Close(true); ArmorBadge.Sync(); };
            ArmorButton.SettingChanged += (s, e) => ArmorBadge.Sync();
        }

        internal static string PadName(FoodCallout.PadButton b)
        {
            switch (b)
            {
                case FoodCallout.PadButton.LeftStickClick: return "JoyLStick";
                case FoodCallout.PadButton.RightStickClick: return "JoyRStick";
                case FoodCallout.PadButton.Back: return "JoyBack";
                default: return null;
            }
        }

        internal static string PadLabel(FoodCallout.PadButton b)
        {
            switch (b)
            {
                case FoodCallout.PadButton.LeftStickClick: return L10n.Game("$button_ls", "LS");
                case FoodCallout.PadButton.RightStickClick: return L10n.Game("$button_rs", "RS");
                case FoodCallout.PadButton.Back: return L10n.Game("$button_back", "Back");
                default: return null;
            }
        }

        /// <summary>The key or button that opens the window, for hints. Empty when there is none.</summary>
        internal static string KeyLabel()
        {
            if (Enabled == null || !Enabled.Value)
                return "";
            if (ZInput.IsGamepadActive())
                return PadLabel(ControllerButton.Value) ?? "";
            return Key.Value.MainKey == KeyCode.None ? "" : Key.Value.ToString();
        }

        // ------------------------------------------------------------------ open and close

        internal static bool IsOpen => StatsPanel.Current != null && StatsPanel.Current.Showing;

        private static Tab _tab = Tab.Stats;
        private static int _closedFrame = -1;
        private static bool _openedInventory; // the key opened the inventory too, so closing closes both

        private static bool CanOpen()
        {
            var player = Player.m_localPlayer;
            if (player == null || player.IsDead() || player.InCutscene() || player.IsTeleporting())
                return false;
            if (InventoryGui.instance == null || General.TextFocus() || Menu.IsVisible() || Minimap.IsOpen()
                || SettingsWindow.IsOpen || GameCamera.InFreeFly() || (TextViewer.instance != null && TextViewer.instance.IsVisible()))
                return false;
            return InventoryGui.IsVisible() || (!Hud.InRadial() && !Hud.InBuildUi() && !StoreGui.IsVisible());
        }

        internal static void Toggle(Tab? tab = null)
        {
            if (IsOpen && (tab == null || tab == _tab))
                Close(false);
            else
                Open(tab ?? _tab);
        }

        internal static void Open(Tab tab)
        {
            if (!Enabled.Value || !CanOpen())
                return;
            try
            {
                var gui = InventoryGui.instance;
                if (!InventoryGui.IsVisible())
                {
                    gui.Show(null);
                    _openedInventory = true;
                }
                else if (!IsOpen)
                {
                    _openedInventory = false;
                }
                var panel = StatsPanel.For(gui);
                if (panel == null)
                    return;
                _tab = tab;
                panel.Open(tab);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Stats window: {e}");
            }
        }

        internal static void Close(bool instant)
        {
            var panel = StatsPanel.Current;
            if (panel == null || !panel.Showing)
                return;
            _closedFrame = Time.frameCount;
            panel.Close(instant);
            if (!instant && _openedInventory && InventoryGui.IsVisible())
                InventoryGui.instance.Hide();
            _openedInventory = false;
        }

        internal static void SelectTab(Tab tab)
        {
            _tab = tab;
            StatsPanel.Current?.Show(tab);
        }

        /// <summary>A new recap arrived: redraw if it is on screen.</summary>
        internal static void Refresh()
        {
            if (IsOpen)
                StatsPanel.Current.Rebuild();
        }

        // ------------------------------------------------------------------ input

        [HarmonyPatch(typeof(Hud), "Update")]
        private static class WatchKey
        {
            private static void Postfix()
            {
                try
                {
                    if (!Enabled.Value || Key.Value.MainKey == KeyCode.None || !Key.Value.IsDown())
                        return;
                    if (General.TextFocus() || SettingsWindow.IsCapturing || SettingsWindow.IsTyping)
                        return;
                    Toggle();
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"Stats window (key): {e}");
                }
            }
        }

        // Esc and B close this window rather than the whole inventory; the controller button toggles it.
        [HarmonyPatch(typeof(InventoryGui), "Update")]
        private static class InventoryInput
        {
            [HarmonyPriority(Priority.High)]
            private static bool Prefix(InventoryGui __instance)
            {
                try
                {
                    if (!Enabled.Value)
                        return true;
                    if (General.TextFocus() || Menu.IsVisible())
                        return true;
                    bool back = ZInput.GetKeyDown(KeyCode.Escape) || ZInput.GetButtonDown("JoyButtonB");
                    if (back && (IsOpen || _closedFrame == Time.frameCount))
                    {
                        Close(false);
                        ZInput.ResetButtonStatus("JoyButtonB");
                        return false;
                    }
                    string pad = PadName(ControllerButton.Value);
                    if (pad != null && InventoryGui.IsVisible() && ZInput.IsGamepadActive() && ZInput.GetButtonDown(pad))
                    {
                        ZInput.ResetButtonStatus(pad);
                        Toggle();
                    }
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"Stats window (input): {e}");
                }
                return true;
            }

            private static void Postfix(InventoryGui __instance)
            {
                try
                {
                    ArmorBadge.Ensure(__instance);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"Stats window (armor button): {e}");
                }
            }
        }

        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Hide))]
        private static class InventoryClosed
        {
            private static void Postfix() => Close(true);
        }
    }

    /// <summary>The window: a copy of the game's Skills dialog with tabs and a scrolling page.</summary>
    internal class StatsPanel : MonoBehaviour
    {
        internal static StatsPanel Current;

        private const float FrameWidth = 690f;

        private InventoryGui _gui;
        private CanvasGroup _group;
        private RectTransform _frame;
        private TMP_Text _title;
        private ScrollRect _scroll;
        private RectTransform _content;
        private Scrollbar _bar;
        private readonly List<(Button button, GameObject selected)> _tabs = new List<(Button, GameObject)>();
        private TMP_Text _padLeft, _padRight;
        private StatsWindow.Tab _tab;
        private bool _closing;
        private float _nextRefresh;
        private string _signature;
        private float _width;
        private readonly List<TMP_Text> _live = new List<TMP_Text>(); // texts of live values, in build order

        internal bool Showing => gameObject.activeSelf && !_closing;

        internal static StatsPanel For(InventoryGui gui)
        {
            if (Current != null && Current._gui == gui)
                return Current;
            if (Current != null)
                Destroy(Current.gameObject);
            Current = Build(gui);
            return Current;
        }

        private static StatsPanel Build(InventoryGui gui)
        {
            var art = StatsUi.GetArt();
            var source = gui.m_skillsDialog != null ? gui.m_skillsDialog.gameObject : null;
            if (art == null || source == null)
                return null;
            var go = Instantiate(source, source.transform.parent, false);
            go.name = "KroolWorldUI_Stats";
            go.SetActive(false);
            DestroyImmediate(go.GetComponent<SkillsDialog>());
            var p = go.AddComponent<StatsPanel>();
            p._gui = gui;
            p._group = go.GetComponent<CanvasGroup>() ?? go.AddComponent<CanvasGroup>();

            p._frame = go.transform.Find("SkillsFrame") as RectTransform;
            var area = p._frame != null ? p._frame.Find("Skills") as RectTransform : null;
            if (p._frame == null || area == null)
            {
                Destroy(go);
                throw new InvalidOperationException("Skills dialog layout changed");
            }
            foreach (string gone in new[] { "totalskills_topic", "totalskills" })
            {
                var t = p._frame.Find(gone);
                if (t != null)
                    Destroy(t.gameObject);
            }
            p._title = p._frame.Find("topic")?.GetComponent<TMP_Text>();
            if (p._title != null)
                StripLocalize(p._title.gameObject);

            // Both close buttons (the one in the frame, and the dark area around it).
            foreach (var b in go.GetComponentsInChildren<Button>(true))
            {
                if (b.name != "Closebutton")
                    continue;
                b.onClick = new Button.ButtonClickedEvent();
                b.onClick.AddListener(() => StatsWindow.Close(false));
            }

            // The list: a scroll view whose content we fill ourselves.
            p._scroll = area.GetComponentInChildren<ScrollRect>(true);
            p._bar = area.GetComponentInChildren<Scrollbar>(true);
            var element = area.Find("SkillList/SkillElement");
            if (element != null)
                Destroy(element.gameObject);
            p._content = p._scroll != null ? p._scroll.content : null;
            if (p._content == null)
            {
                Destroy(go);
                throw new InvalidOperationException("Skills dialog has no scroll content");
            }
            foreach (Transform child in p._content)
                Destroy(child.gameObject);
            // Grows down from the top edge.
            p._content.anchorMin = new Vector2(0f, 1f);
            p._content.anchorMax = new Vector2(1f, 1f);
            p._content.pivot = new Vector2(0.5f, 1f);
            p._content.anchoredPosition = Vector2.zero;
            p._scroll.horizontal = false;
            p._scroll.scrollSensitivity = 40f;

            p.Layout(area);
            p.BuildTabs(art);

            // The inventory's controller buttons must not fire while this window is up, as with the game's own dialogs.
            foreach (var pad in gui.GetComponentsInChildren<UIGamePad>(true))
            {
                if (pad.transform.IsChildOf(go.transform))
                    continue;
                pad.m_blockingElements ??= new List<GameObject>();
                if (!pad.m_blockingElements.Contains(go))
                    pad.m_blockingElements.Add(go);
            }
            return p;
        }

        private static void StripLocalize(GameObject go)
        {
            foreach (var c in go.GetComponents<MonoBehaviour>())
                if (c.GetType().Name == "Localize")
                    DestroyImmediate(c);
        }

        private void Layout(RectTransform area)
        {
            var canvas = _frame.GetComponentInParent<Canvas>();
            float canvasH = canvas != null ? ((RectTransform)canvas.rootCanvas.transform).rect.height : 1080f;
            float h = Mathf.Clamp(canvasH - 70f, 520f, 900f);
            _frame.sizeDelta = new Vector2(FrameWidth, h);
            if (_title != null)
            {
                var rt = _title.rectTransform;
                rt.sizeDelta = new Vector2(FrameWidth - 120f, rt.sizeDelta.y);
            }
            // Under the title and tabs, above the close button.
            area.anchorMin = area.anchorMax = new Vector2(0.5f, 1f);
            area.pivot = new Vector2(0.5f, 1f);
            area.anchoredPosition = new Vector2(-4f, -104f);
            area.sizeDelta = new Vector2(FrameWidth - 52f, h - 104f - 84f);
            _width = area.sizeDelta.x - 26f;
        }

        private static readonly string[] TabKeys = { "stats_tab_stats", "stats_tab_deaths", "stats_tab_bosses" };

        private void BuildTabs(StatsUi.Art art)
        {
            var template = _gui.m_tabCraft != null ? _gui.m_tabCraft.gameObject : null;
            const float w = 160f, h = 32f, gap = 6f;
            float x0 = -(w * 3f + gap * 2f) / 2f + w / 2f;
            for (int i = 0; i < 3; i++)
            {
                var tab = (StatsWindow.Tab)i;
                GameObject go;
                Button button;
                GameObject selected = null;
                if (template != null)
                {
                    go = Instantiate(template, _frame, false);
                    foreach (var c in go.GetComponentsInChildren<MonoBehaviour>(true))
                    {
                        string n = c.GetType().Name;
                        if (n == "UIGamePad" || n == "UIInputHint" || n == "Localize")
                            DestroyImmediate(c);
                    }
                    var hint = go.transform.Find("gamepad_hint");
                    if (hint != null)
                        Destroy(hint.gameObject);
                    button = go.GetComponent<Button>();
                    selected = go.transform.Find("Selected")?.gameObject;
                    foreach (var t in go.GetComponentsInChildren<TMP_Text>(true))
                        t.text = L10n.T(TabKeys[i]);
                }
                else
                {
                    go = new GameObject("tab", typeof(RectTransform), typeof(Image), typeof(Button));
                    go.transform.SetParent(_frame, false);
                    go.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.4f);
                    button = go.GetComponent<Button>();
                    var t = StatsUi.Text(art, go.transform, L10n.T(TabKeys[i]), 0f, 0f, w, h, 18f, StatsUi.Parchment,
                        TextAlignmentOptions.Center);
                    HudKit.Stretch(t.rectTransform);
                }
                go.name = "tab_" + tab;
                go.SetActive(true);
                var rt = (RectTransform)go.transform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = new Vector2(x0 + i * (w + gap), -76f);
                rt.sizeDelta = new Vector2(w, h);
                button.onClick = new Button.ButtonClickedEvent();
                button.onClick.AddListener(() => StatsWindow.SelectTab(tab));
                button.navigation = new Navigation { mode = Navigation.Mode.None };
                _tabs.Add((button, selected));
            }
            // Bumper hints at both ends, shown on a controller.
            float edge = (w * 3f + gap * 2f) / 2f + 22f;
            _padLeft = PadHint(art, -edge, L10n.Game("$button_lb", "LB"));
            _padRight = PadHint(art, edge, L10n.Game("$button_rb", "RB"));
        }

        private TMP_Text PadHint(StatsUi.Art art, float x, string text)
        {
            var t = StatsUi.Text(art, _frame, text, 0f, 0f, 40f, 24f, 15f, StatsUi.Muted, TextAlignmentOptions.Center);
            var rt = t.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(x, -76f);
            return t;
        }

        // ------------------------------------------------------------------ state

        internal void Open(StatsWindow.Tab tab)
        {
            _closing = false;
            if (!gameObject.activeSelf)
            {
                gameObject.SetActive(true);
                _group.alpha = 0f;
            }
            transform.SetAsLastSibling();
            Show(tab);
            // Give the controller to the info panel, as the game's Skills dialog does.
            try
            {
                var groups = _gui.m_uiGroups;
                if (groups != null && groups.Length > 2)
                    AccessTools.Method(typeof(InventoryGui), "SetActiveGroup", new[] { typeof(UIGroupHandler), typeof(bool) })
                        ?.Invoke(_gui, new object[] { groups[2], false });
            }
            catch
            {
                // only affects controller focus
            }
        }

        internal void Close(bool instant)
        {
            if (instant)
            {
                _closing = false;
                gameObject.SetActive(false);
                return;
            }
            _closing = true;
        }

        internal void Show(StatsWindow.Tab tab)
        {
            _tab = tab;
            for (int i = 0; i < _tabs.Count; i++)
            {
                bool on = i == (int)tab;
                _tabs[i].button.interactable = !on;
                if (_tabs[i].selected != null)
                    _tabs[i].selected.SetActive(on);
            }
            if (_title != null)
                _title.text = Player.m_localPlayer != null ? Player.m_localPlayer.GetPlayerName() : L10n.T(TabKeys[(int)tab]);
            _signature = null;
            Rebuild();
            _scroll.verticalNormalizedPosition = 1f;
        }

        internal void Rebuild()
        {
            var art = StatsUi.GetArt();
            var player = Player.m_localPlayer;
            if (art == null || player == null)
                return;
            try
            {
                switch (_tab)
                {
                    case StatsWindow.Tab.Stats:
                        var sheet = CharacterSheet.Build(player);
                        string sig = Signature(sheet);
                        if (sig == _signature)
                        {
                            // Only timers and food values moved: change those texts, keep the page (and its tooltips).
                            var values = LiveTexts(sheet);
                            if (values.Count == _live.Count)
                            {
                                for (int i = 0; i < values.Count; i++)
                                    if (_live[i] != null && _live[i].text != values[i])
                                        _live[i].text = values[i];
                                return;
                            }
                        }
                        _signature = sig;
                        Clear();
                        _live.Clear();
                        SetHeight(Sheet(art, sheet));
                        break;
                    case StatsWindow.Tab.Deaths:
                        Clear();
                        SetHeight(Deaths(art));
                        break;
                    default:
                        Clear();
                        SetHeight(Bosses(art));
                        break;
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Stats window ({_tab}): {e}");
            }
        }

        private void Clear()
        {
            for (int i = _content.childCount - 1; i >= 0; i--)
            {
                var c = _content.GetChild(i).gameObject;
                c.SetActive(false);
                Destroy(c);
            }
        }

        private void SetHeight(float h)
        {
            var view = (RectTransform)_scroll.transform;
            _content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Mathf.Max(h + 12f, view.rect.height));
        }

        private RectTransform Page()
        {
            var r = StatsUi.Rect("page", _content);
            StatsUi.Place(r, 8f, 4f, _width, 10f);
            return r;
        }

        private void Update()
        {
            try
            {
                float target = _closing ? 0f : 1f;
                _group.alpha = Mathf.MoveTowards(_group.alpha, target, Time.unscaledDeltaTime * 9f);
                if (_closing && _group.alpha <= 0.001f)
                {
                    _closing = false;
                    gameObject.SetActive(false);
                    return;
                }
                if (_tab == StatsWindow.Tab.Stats && Time.unscaledTime >= _nextRefresh)
                {
                    _nextRefresh = Time.unscaledTime + 0.5f;
                    Rebuild();
                }
                bool pad = ZInput.IsGamepadActive();
                if (_padLeft != null)
                {
                    _padLeft.gameObject.SetActive(pad);
                    _padRight.gameObject.SetActive(pad);
                }
                if (!_closing && pad)
                {
                    if (ZInput.GetButtonDown("JoyLBumper") || ZInput.GetButtonDown("JoyTabLeft"))
                        StatsWindow.SelectTab((StatsWindow.Tab)(((int)_tab + 2) % 3));
                    else if (ZInput.GetButtonDown("JoyRBumper") || ZInput.GetButtonDown("JoyTabRight"))
                        StatsWindow.SelectTab((StatsWindow.Tab)(((int)_tab + 1) % 3));
                    float stick = ZInput.GetJoyRightStickY() + ZInput.GetJoyLeftStickY();
                    float dpad = ZInput.GetButton("JoyDPadUp") ? -1f : ZInput.GetButton("JoyDPadDown") ? 1f : 0f;
                    float move = Mathf.Abs(stick) > 0.15f ? stick : dpad;
                    float range = _content.rect.height - ((RectTransform)_scroll.transform).rect.height;
                    if (Mathf.Abs(move) > 0.01f && range > 1f)
                        _scroll.verticalNormalizedPosition = Mathf.Clamp01(_scroll.verticalNormalizedPosition - move * 900f * Time.unscaledDeltaTime / range);
                }
                if (_bar != null)
                {
                    float view = ((RectTransform)_scroll.transform).rect.height;
                    _bar.size = Mathf.Clamp01(view / Mathf.Max(1f, _content.rect.height));
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Stats window: {e}");
                gameObject.SetActive(false);
            }
        }

        private void OnDestroy()
        {
            if (Current == this)
                Current = null;
        }

        // ------------------------------------------------------------------ pages

        private const float RowH = 32f;
        private const float IconSize = 26f;
        private const float LabelX = 38f;
        private const float ValueX = 214f;
        private const float ValueW = 84f;
        private const float ChipX = 314f;

        /// <summary>What the page shows, without the values that change by themselves.</summary>
        private static string Signature(List<StatSection> sheet)
        {
            var sb = new StringBuilder();
            foreach (var s in sheet)
            {
                if (s.Rows.Count == 0)
                    continue;
                sb.Append(s.Title).Append('|');
                foreach (var r in s.Rows)
                {
                    sb.Append(r.Label).Append(r.LiveValue ? "~" : r.Value).Append(r.Note);
                    foreach (var c in r.Chips)
                        sb.Append(c.Live ? "~" : c.Text).Append(c.Icon != null ? c.Icon.GetInstanceID() : 0);
                    sb.Append(';');
                }
            }
            return sb.ToString();
        }

        /// <summary>The live values, in the order StatRowView creates their texts: a row's chips, then its value.</summary>
        private static List<string> LiveTexts(List<StatSection> sheet)
        {
            var list = new List<string>();
            foreach (var s in sheet)
            {
                if (s.Rows.Count == 0)
                    continue;
                foreach (var r in s.Rows)
                {
                    foreach (var c in r.Chips)
                        if (c.Live)
                            list.Add(c.Text ?? "");
                    if (r.LiveValue && !string.IsNullOrEmpty(r.Value))
                        list.Add(r.Value);
                }
            }
            return list;
        }

        private float Sheet(StatsUi.Art art, List<StatSection> sheet)
        {
            var page = Page();
            float w = _width;
            float y = 0f;
            foreach (var section in sheet)
            {
                if (section.Rows.Count == 0)
                    continue;
                y += StatsUi.Heading(art, page, section.Title, y, w);
                if (!string.IsNullOrEmpty(section.Note))
                {
                    var note = StatsUi.Text(art, page, section.Note, 4f, y, w - 8f, 20f, 14f, StatsUi.Faint, TextAlignmentOptions.TopLeft);
                    note.textWrappingMode = TextWrappingModes.Normal;
                    float nh = Mathf.Ceil(note.GetPreferredValues(section.Note, w - 8f, 0f).y) + 4f;
                    note.rectTransform.sizeDelta = new Vector2(w - 8f, nh);
                    y += nh + 2f;
                }
                int i = 0;
                foreach (var row in section.Rows)
                    y = StatRowView(art, page, row, y, w, i++ % 2 == 0);
                y += 14f;
            }
            return y;
        }

        private float StatRowView(StatsUi.Art art, RectTransform page, StatRow row, float y, float w, bool shade)
        {
            // Chips first, to know how tall the row is.
            var holder = StatsUi.Rect("row", page);
            float chipsW = w - ChipX - 4f;
            float cx = 0f, cy = 0f, line = 28f;
            if (!string.IsNullOrEmpty(row.Note))
            {
                var note = StatsUi.Text(art, holder, row.Note, ChipX, 0f, chipsW, line, 14f, StatsUi.Muted, TextAlignmentOptions.TopLeft);
                note.textWrappingMode = TextWrappingModes.Normal;
                float nh = Mathf.Ceil(note.GetPreferredValues(row.Note, chipsW, 0f).y);
                note.rectTransform.sizeDelta = new Vector2(chipsW, nh);
                StatsUi.Place(note.rectTransform, ChipX, 7f, chipsW, nh);
                cy = Mathf.Max(0f, nh + 14f - RowH);
            }
            foreach (var chip in row.Chips)
            {
                float iconW = chip.Icon != null ? 24f : 0f;
                var text = StatsUi.Text(art, holder, chip.Text ?? "", 0f, 0f, 120f, line, 15f, chip.Color);
                float tw = string.IsNullOrEmpty(chip.Text) ? 0f : Mathf.Ceil(text.GetPreferredValues(chip.Text).x) + 2f;
                float cw = iconW + (iconW > 0f && tw > 0f ? 3f : 0f) + tw;
                if (cx > 0f && cx + cw > chipsW)
                {
                    cx = 0f;
                    cy += line;
                }
                float top = 2f + cy;
                if (chip.Icon != null)
                {
                    var img = StatsUi.Image(holder, chip.Icon, ChipX + cx, top + 2f, 24f, 24f,
                        chip.Dim ? new Color(1f, 1f, 1f, 0.35f) : Color.white);
                    StatsUi.Tip(art, img.gameObject, "", chip.Tip);
                }
                StatsUi.Place(text.rectTransform, ChipX + cx + iconW + (iconW > 0f ? 3f : 0f), top, tw + 8f, line);
                StatsUi.Tip(art, text.gameObject, "", chip.Tip);
                if (chip.Live)
                    _live.Add(text);
                cx += cw + 12f;
            }
            float h = RowH + cy;
            StatsUi.Place(holder, 0f, y, w, h);
            if (shade)
            {
                StatsUi.RowBack(art, holder, 0f, w, h, 0.4f);
                holder.GetChild(holder.childCount - 1).SetAsFirstSibling();
            }

            if (row.Icon != null)
                StatsUi.Image(holder, row.Icon, 4f, (RowH - IconSize) / 2f, IconSize, IconSize, Color.white);
            var label = StatsUi.Text(art, holder, row.Label, LabelX, 0f, ValueX - LabelX - 6f, RowH, 17f, StatsUi.Parchment);
            if (!string.IsNullOrEmpty(row.Tip))
                StatsUi.Tip(art, label.gameObject, row.Label, row.Tip);
            if (!string.IsNullOrEmpty(row.Value))
            {
                var value = StatsUi.Text(art, holder, row.Value, ValueX, 0f, ValueW, RowH, 18f, row.ValueColor,
                    TextAlignmentOptions.MidlineRight);
                if (!string.IsNullOrEmpty(row.Tip))
                    StatsUi.Tip(art, value.gameObject, row.Label, row.Tip);
                if (row.LiveValue)
                    _live.Add(value);
            }
            return y + h;
        }

        private float Deaths(StatsUi.Art art)
        {
            var page = Page();
            float w = _width;
            if (!DeathRecap.Enabled.Value)
                return Empty(art, page, L10n.T("stats_deaths_off"));
            if (DeathRecap.History.Count == 0)
                return Empty(art, page, L10n.T("stats_no_deaths"));
            float y = 0f;
            foreach (var d in DeathRecap.History)
            {
                var block = StatsUi.Rect("death", page);
                float h = RecapView.Death(art, block, d, w, compact: false, when: RecapView.Ago(d.When));
                StatsUi.Place(block, 0f, y, w, h);
                y += h + 26f;
            }
            return y;
        }

        private float Bosses(StatsUi.Art art)
        {
            var page = Page();
            float w = _width;
            if (!BossRecap.Enabled.Value)
                return Empty(art, page, L10n.T("stats_bosses_off"));
            if (BossFights.History.Count == 0)
                return Empty(art, page, L10n.T("stats_no_bosses"));
            float y = 0f;
            foreach (var b in BossFights.History)
            {
                var block = StatsUi.Rect("boss", page);
                float h = RecapView.Boss(art, block, b, w, compact: false, when: RecapView.Ago(b.When));
                StatsUi.Place(block, 0f, y, w, h);
                y += h + 26f;
            }
            return y;
        }

        private static float Empty(StatsUi.Art art, RectTransform page, string text)
        {
            StatsUi.Text(art, page, text, 0f, 40f, page.sizeDelta.x, 30f, 18f, StatsUi.Muted, TextAlignmentOptions.Center);
            return 100f;
        }
    }

    /// <summary>
    /// The armor box in the inventory opens the stats window: hover lights it up, a small mark
    /// in its corner says it can be clicked, and on a controller the button's name shows there.
    /// </summary>
    internal class ArmorBadge : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        private static ArmorBadge _instance;
        private static Sprite _mark;
        private Image _glow;
        private Image _icon;
        private TMP_Text _pad;
        private bool _hover;

        internal static void Ensure(InventoryGui gui)
        {
            if (_instance != null || gui == null || gui.m_armor == null)
                return;
            if (!StatsWindow.Enabled.Value || !StatsWindow.ArmorButton.Value)
                return;
            var box = gui.m_armor.transform.parent as RectTransform;
            var art = StatsUi.GetArt();
            if (box == null || art == null)
                return;
            var go = new GameObject("KroolWorldUI_StatsButton", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(box, false);
            var rt = (RectTransform)go.transform;
            HudKit.Stretch(rt);
            var hit = go.GetComponent<Image>();
            hit.color = new Color(0f, 0f, 0f, 0f);
            hit.raycastTarget = true;
            var b = go.AddComponent<ArmorBadge>();
            _instance = b;

            var bkg = box.Find("bkg")?.GetComponent<Image>();
            b._glow = StatsUi.CopyOf(bkg, rt, new Color(1f, 0.82f, 0.45f, 0.35f));
            HudKit.Stretch(b._glow.rectTransform);
            b._glow.raycastTarget = false;
            b._glow.enabled = false;

            b._icon = HudKit.NewImage("mark", rt, new Color(StatsUi.Parchment.r, StatsUi.Parchment.g, StatsUi.Parchment.b, 0.7f));
            b._icon.sprite = Mark();
            var irt = b._icon.rectTransform;
            irt.anchorMin = irt.anchorMax = new Vector2(1f, 1f);
            irt.pivot = new Vector2(1f, 1f);
            irt.anchoredPosition = new Vector2(-5f, -5f);
            irt.sizeDelta = new Vector2(14f, 14f);

            b._pad = HudKit.NewText(art.Body, rt, "pad", TextAlignmentOptions.TopRight, StatsUi.Muted, 12f);
            var prt = b._pad.rectTransform;
            prt.anchorMin = prt.anchorMax = new Vector2(1f, 1f);
            prt.pivot = new Vector2(1f, 1f);
            prt.anchoredPosition = new Vector2(-21f, -3f);
            prt.sizeDelta = new Vector2(40f, 16f);

            StatsUi.Tip(art, go, "", L10n.T("stats_open"));
        }

        /// <summary>Settings changed: drop the button; it is rebuilt if still wanted.</summary>
        internal static void Sync()
        {
            if (_instance != null)
                Destroy(_instance.gameObject);
            _instance = null;
        }

        private void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
        }

        private void Update()
        {
            bool pad = ZInput.IsGamepadActive();
            string label = pad ? StatsWindow.PadLabel(StatsWindow.ControllerButton.Value) : null;
            _pad.text = label ?? "";
            _pad.gameObject.SetActive(!string.IsNullOrEmpty(label));
            _glow.enabled = _hover || StatsWindow.IsOpen;
            _icon.color = new Color(StatsUi.Parchment.r, StatsUi.Parchment.g, StatsUi.Parchment.b, _hover ? 1f : 0.7f);
            var tip = GetComponent<UITooltip>();
            if (tip != null)
            {
                string key = StatsWindow.KeyLabel();
                tip.m_text = string.IsNullOrEmpty(key) ? L10n.T("stats_open") : L10n.T("stats_open_key", key);
            }
        }

        public void OnPointerEnter(PointerEventData e) => _hover = true;

        public void OnPointerExit(PointerEventData e) => _hover = false;

        public void OnPointerClick(PointerEventData e)
        {
            if (e.button == PointerEventData.InputButton.Left)
                StatsWindow.Toggle(StatsWindow.Tab.Stats);
        }

        /// <summary>Three rising bars: "stats".</summary>
        private static Sprite Mark()
        {
            if (_mark != null)
                return _mark;
            const int size = 28;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            var px = new Color32[size * size];
            int[] heights = { 10, 17, 24 };
            for (int bar = 0; bar < 3; bar++)
            {
                int x0 = 2 + bar * 9;
                for (int y = 2; y < 2 + heights[bar]; y++)
                    for (int x = x0; x < x0 + 6; x++)
                        px[y * size + x] = new Color32(255, 255, 255, 255);
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            _mark = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            return _mark;
        }
    }
}
