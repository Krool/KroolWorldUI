using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using KroolWorldUI.Features;
using UnityEngine;
using UnityEngine.EventSystems;

namespace KroolWorldUI
{
    /// <summary>
    /// Built-in settings window (F7, or Krool World UI in the pause menu), so the mod needs no
    /// ConfigurationManager. It draws every entry in this plugin's config generically, one
    /// collapsible section per config section, in Bind order, so a new feature only needs its
    /// config.Bind calls (see Plugin.BindAll). ConfigText.Meta tags add sub-headings, slider steps
    /// and "greyed out unless" conditions.
    ///   bool = On/Off, ranged number = slider plus a typed value, enum = arrows to step through,
    ///   Color = swatch, hex value and an HSV picker, KeyboardShortcut = press-a-key, text = field.
    /// Works with the mouse, the keyboard (arrows, Enter, Esc) and a controller (D-pad, A, B, X, Y,
    /// bumpers). Text is drawn at the screen's real resolution, so it stays sharp at any size.
    /// While open the game ignores movement, look and scroll input.
    /// </summary>
    internal class SettingsWindow : MonoBehaviour
    {
        internal static bool IsOpen { get; private set; }
        /// <summary>"Preview on HUD" is on: features show samples even without real data.</summary>
        internal static bool Previewing { get; private set; }
        /// <summary>A key binding is being recorded: the next key press is the binding, not a command.</summary>
        internal static bool IsCapturing => _instance != null && _instance._capturingKey != null;

        private static SettingsWindow _instance;

        private const int WindowId = 0x4B57_5549; // "KWUI"
        private const float BaseWidth = 580f;
        private const float BaseHeight = 760f;
        private const float MinBaseWidth = 460f;
        private const float MinBaseHeight = 420f;
        private const float LabelWidth = 190f;
        private const float RowHeight = 28f;
        private const float TitleBand = 44f;
        private const float DescribeHeight = 104f;
        private const float RepeatDelay = 0.35f;
        private const float RepeatRate = 0.06f;

        // ---------- state ----------
        private Rect _window;           // screen pixels
        private bool _windowPlaced;
        private Vector2 _scroll;
        private float _scale = -1f;
        private Styles _styles;
        private string _search = "";
        private readonly HashSet<string> _expanded = new HashSet<string>();
        private bool _preview, _previewWas;
        private bool _wasSaveOnSet;
        private bool _closeRequested;
        private bool _eventSystemWas;
        private EventSystem _eventSystem;
        private bool _resizing;
        private Vector2 _resizeFrom, _resizeSize, _resizeTo;
        private int _openedFrame = -1;
        private bool _applySize;
        private bool _mouseInList;
        private ConfigEntryBase _pickFor;
        private float _pickH, _pickS;

        private ConfigEntryBase _capturingKey;
        private ConfigEntryBase _colorOpen;
        private readonly Dictionary<ConfigEntryBase, string> _textEdits = new Dictionary<ConfigEntryBase, string>();

        // Navigation: everything focusable, in draw order, from the last layout pass.
        private readonly List<object> _nav = new List<object>();
        private readonly List<object> _navBuilding = new List<object>();
        private object _focus;
        private object _describe;          // what the description strip shows
        private bool _keyboardMode;        // focus outline shown (keys or controller used last)
        private bool _scrollToFocus;
        // Row rects from the last repaint (read), and the ones being recorded this repaint (write).
        // Rows in the list are in its scroll content space; Preview and Close in the window's area space.
        private Dictionary<object, Rect> _rowRects = new Dictionary<object, Rect>();
        private Dictionary<object, Rect> _rectsNext = new Dictionary<object, Rect>();
        private Rect _listRect;            // window space
        private Vector2 _lastMouse;

        // Controller repeat.
        private string _heldButton;
        private float _heldSince, _nextRepeat;

        private static readonly object PreviewButton = new object();
        private static readonly object CloseButton = new object();

        private List<(string Name, List<ConfigEntryBase> Entries)> _sections;

        // ---------- lifecycle ----------

        private void Awake()
        {
            _instance = this;
            foreach (var name in General.ExpandedSections.Value.Split('|'))
                if (name.Length > 0) _expanded.Add(name);
        }

        private void OnDestroy()
        {
            if (IsOpen)
                SetOpen(false);
            _styles?.Destroy();
            if (_instance == this)
                _instance = null;
        }

        internal static void Open()
        {
            if (_instance != null)
                _instance.SetOpen(true);
        }

        private void Update()
        {
            if (_capturingKey == null)
            {
                bool back = ZInput.GetKeyDown(KeyCode.Escape) || ZInput.GetButtonDown("JoyButtonB") || ZInput.GetButtonDown("JoyMenu");
                if (!IsOpen || !EditingText())
                {
                    if (General.SettingsKey.Value.MainKey != KeyCode.None && General.SettingsKey.Value.IsDown() && !General.TextFocus())
                        SetOpen(!IsOpen);
                    else if (IsOpen && back && Time.frameCount != _openedFrame)
                    {
                        // Back out one step at a time: the color picker, then the window.
                        if (_colorOpen != null)
                        {
                            if (_focus is PickKey)
                                _focus = _colorOpen; // back to the color's own row
                            _colorOpen = null;
                        }
                        else
                            _closeRequested = true; // closed in LateUpdate, after Menu.Update has been skipped
                    }
                }
                else if (back && !ZInput.GetKeyDown(KeyCode.Escape))
                {
                    GUIUtility.keyboardControl = 0; // a controller's B leaves the text field (Esc is handled in OnGUI)
                }
            }
            // A controller's B or Start cancels a key capture (it cannot press a keyboard key to end it).
            if (_capturingKey != null && (ZInput.GetButtonDown("JoyButtonB") || ZInput.GetButtonDown("JoyMenu")))
                _capturingKey = null;
            // Not on the frame it opened: the A that pressed the pause menu button must not also press a row.
            if (IsOpen && _capturingKey == null && Time.frameCount != _openedFrame)
                GamepadInput();
            UpdatePreview();
        }

        private void LateUpdate()
        {
            if (_closeRequested)
            {
                _closeRequested = false;
                SetOpen(false);
            }
        }

        /// <summary>Shows every feature's sample on the HUD while Preview is on; chime once on enable.</summary>
        private void UpdatePreview()
        {
            bool active = IsOpen && _preview && Player.m_localPlayer != null;
            Previewing = active;
            if (active)
            {
                SkillXpBarStack.GetOrCreate()?.Preview(Player.m_localPlayer.GetSkills());
                FoodCalloutHud.Preview(true, playSound: !_previewWas && FoodCallout.Sound.Value);
                SaveCountdownHud.Preview(true);
                RestedCountdownHud.Preview(true);
            }
            else if (_previewWas)
            {
                FoodCalloutHud.Preview(false, playSound: false);
                SaveCountdownHud.Preview(false);
                RestedCountdownHud.Preview(false);
            }
            _previewWas = active;
        }

        private void SetOpen(bool open)
        {
            if (open == IsOpen)
                return;
            var config = Plugin.Instance.Config;
            if (open)
            {
                // Sliders change values every frame; write the file once on close instead.
                _wasSaveOnSet = config.SaveOnConfigSet;
                config.SaveOnConfigSet = false;
                // Clicks must not reach the inventory, crafting panel or map underneath.
                if (InventoryGui.instance != null && InventoryGui.IsVisible())
                    InventoryGui.instance.Hide();
                if (Minimap.instance != null && Minimap.IsOpen())
                    Minimap.instance.SetMapMode(Minimap.MapMode.Small);
                if (Menu.instance != null && Menu.IsVisible())
                    Menu.instance.Hide();
                // Kept by reference: once disabled, EventSystem.current no longer returns it.
                _eventSystem = EventSystem.current;
                _eventSystemWas = _eventSystem != null && _eventSystem.enabled;
                if (_eventSystem != null)
                    _eventSystem.enabled = false;
                _focus = null;
                _keyboardMode = ZInput.IsGamepadActive();
                _openedFrame = Time.frameCount;
            }
            else
            {
                SaveWindowRect(); // before Save, so it is written with the rest
                config.Save();
                config.SaveOnConfigSet = _wasSaveOnSet;
                _capturingKey = null;
                _colorOpen = null;
                _preview = false;
                _textEdits.Clear();
                GUIUtility.keyboardControl = 0;
                if (_eventSystem != null && _eventSystemWas)
                    _eventSystem.enabled = true;
                _eventSystem = null;
            }
            IsOpen = open;
        }

        private static bool EditingText() => GUIUtility.keyboardControl != 0;

        /// <summary>Typing in this window's search or a text field: keys are text, not hotkeys.</summary>
        internal static bool IsTyping => IsOpen && EditingText();

        // ---------- scale and window placement ----------

        /// <summary>
        /// Screen pixels per window unit: the screen against a 1080p reference, times the game's
        /// GUI scale and Window Scale. Applied to sizes and fonts directly (not GUI.matrix), so text
        /// is rendered at its real size and stays sharp.
        /// </summary>
        private float CurrentScale()
        {
            float screen = Mathf.Min(Screen.width / 1920f, Screen.height / 1080f);
            float gui = PlatformPrefs.GetFloat("GuiScale", 1f);
            float s = screen * gui * General.WindowScale.Value;
            // Never so small that a button is under about 22 px (Steam Deck), never past the screen.
            s = Mathf.Max(s, 24f / RowHeight);
            s = Mathf.Min(s, Screen.height / (MinBaseHeight + 40f), Screen.width / (MinBaseWidth + 40f));
            return s;
        }

        private float P(float v) => Mathf.Round(v * _scale);

        private void PlaceWindow()
        {
            float w = P(BaseWidth), h = Mathf.Min(P(BaseHeight), Screen.height - P(40f));
            if (!_windowPlaced)
            {
                _windowPlaced = true;
                _window = new Rect(P(60f), P(60f), w, h);
                var saved = General.WindowRect.Value.Split(',');
                if (saved.Length == 4 && float.TryParse(saved[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
                    && float.TryParse(saved[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y)
                    && float.TryParse(saved[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float sw)
                    && float.TryParse(saved[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float sh))
                {
                    // Saved as fractions of the screen and window units, so it survives a resolution change.
                    _window = new Rect(x * Screen.width, y * Screen.height, P(sw), P(sh));
                }
            }
            _window.width = Mathf.Clamp(_window.width, P(MinBaseWidth), Screen.width);
            _window.height = Mathf.Clamp(_window.height, Mathf.Min(P(MinBaseHeight), Screen.height), Screen.height);
            _window.x = Mathf.Clamp(_window.x, 0f, Mathf.Max(0f, Screen.width - _window.width));
            _window.y = Mathf.Clamp(_window.y, 0f, Mathf.Max(0f, Screen.height - _window.height));
        }

        private void SaveWindowRect()
        {
            if (!_windowPlaced || _scale <= 0f)
                return;
            General.WindowRect.Value = string.Join(",",
                (_window.x / Screen.width).ToString("0.####", CultureInfo.InvariantCulture),
                (_window.y / Screen.height).ToString("0.####", CultureInfo.InvariantCulture),
                (_window.width / _scale).ToString("0", CultureInfo.InvariantCulture),
                (_window.height / _scale).ToString("0", CultureInfo.InvariantCulture));
        }

        // ---------- drawing ----------

        private void OnGUI()
        {
            if (!IsOpen)
                return;
            float scale = CurrentScale();
            if (_styles == null || !Mathf.Approximately(scale, _scale) || _styles.Language != L10n.Language)
            {
                float old = _scale;
                _scale = scale;
                _styles?.Destroy();
                _styles = new Styles(scale, L10n.Language);
                if (old > 0f && _windowPlaced)
                {
                    _window.width *= scale / old;
                    _window.height *= scale / old;
                }
            }
            GUI.matrix = Matrix4x4.identity;
            PlaceWindow();

            var e = Event.current;
            if (_capturingKey != null)
                CaptureKey();
            else if (e.type == EventType.KeyDown)
                KeyboardInput(e);

            // The mouse moving hands over from keys/controller (MouseMove is not sent at runtime, so compare).
            if (e.type == EventType.Repaint)
            {
                if ((e.mousePosition - _lastMouse).sqrMagnitude > 4f)
                {
                    if (_lastMouse != Vector2.zero)
                        _keyboardMode = false;
                    _lastMouse = e.mousePosition;
                }
            }
            // A click anywhere leaves a text field; a click on a field takes focus again while drawing.
            if (e.type == EventType.MouseDown)
                GUIUtility.keyboardControl = 0;

            _window = GUI.Window(WindowId, _window, DrawWindow, "", _styles.Window);
            // Set after GUI.Window: it hands back its own copy of the rect, so a size changed inside would be lost.
            if (_resizing || _applySize)
            {
                _window.size = _resizeTo;
                _applySize = false;
            }
            // Bring it to the front once on opening, not every frame (that would steal focus from other mods' windows).
            if (Time.frameCount == _openedFrame || Time.frameCount == _openedFrame + 1)
                GUI.FocusWindow(WindowId);
        }

        private void DrawWindow(int id)
        {
            var config = Plugin.Instance.Config;
            _sections ??= config.Keys
                .GroupBy(k => k.Section)
                .Select(g => (Name: g.Key, Entries: g.Select(d => config[d]).Where(e => !ConfigText.IsHidden(e)).ToList()))
                .Where(s => s.Entries.Count > 0)
                .ToList();

            bool layout = Event.current.type == EventType.Layout;
            bool repaint = Event.current.type == EventType.Repaint;
            if (layout)
                _navBuilding.Clear();
            if (repaint)
                _rectsNext.Clear();

            // Title and close.
            var titleRect = new Rect(0f, 0f, _window.width, P(TitleBand));
            GUI.Label(titleRect, $"{Plugin.Name}  <size={P(15)}><color=#B3A38C>v{Plugin.Version}{Plugin.Flavor}</color></size>", _styles.Title);
            var closeX = new Rect(_window.width - P(40f), P(8f), P(30f), P(28f));
            if (GUI.Button(closeX, GUIContent.none, _styles.CloseX))
                _closeRequested = true;
            if (repaint)
                GUI.DrawTexture(new Rect(closeX.center.x - P(6f), closeX.center.y - P(6f), P(12f), P(12f)), _styles.Cross);

            GUILayout.BeginArea(new Rect(P(14f), P(TitleBand), _window.width - P(28f), _window.height - P(TitleBand) - P(12f)));

            // Search and expand/collapse.
            GUILayout.BeginHorizontal(GUILayout.Height(P(30f)));
            GUI.SetNextControlName("kwui_search");
            string typed = GUILayout.TextField(_search, 60, _styles.Field, GUILayout.Height(P(28f)), GUILayout.ExpandWidth(true));
            var searchRect = GUILayoutUtility.GetLastRect();
            if (typed != _search)
            {
                _search = typed;
                _scroll = Vector2.zero;
            }
            if (_search.Length == 0 && Event.current.type == EventType.Repaint && GUI.GetNameOfFocusedControl() != "kwui_search")
                _styles.Placeholder.Draw(searchRect, L10n.T("win_search"), false, false, false, false);
            GUILayout.Space(P(6f));
            if (GUILayout.Button(L10n.T("win_expand_all"), _styles.Small, GUILayout.Height(P(28f))))
                SetExpanded(_sections.Select(s => s.Name), true);
            if (GUILayout.Button(L10n.T("win_collapse_all"), _styles.Small, GUILayout.Height(P(28f))))
                SetExpanded(_sections.Select(s => s.Name), false);
            GUILayout.EndHorizontal();
            GUILayout.Space(P(6f));

            // The settings.
            _mouseInList = _listRect.Contains(Event.current.mousePosition);
            _scroll = GUILayout.BeginScrollView(_scroll, false, false, GUIStyle.none, _styles.Scrollbar,
                GUILayout.ExpandHeight(true));
            int shownSections = 0;
            foreach (var (name, entries) in _sections)
                if (DrawSection(name, entries))
                    shownSections++;
            if (shownSections == 0)
                GUILayout.Label(L10n.T("win_no_results"), _styles.Hint, GUILayout.Height(P(RowHeight)));
            GUILayout.EndScrollView();
            if (repaint)
                _listRect = GUILayoutUtility.GetLastRect();

            // What the hovered or focused setting does.
            GUILayout.Space(P(6f));
            DrawDescription();

            // Preview and Close.
            GUILayout.Space(P(6f));
            GUILayout.BeginHorizontal();
            bool canPreview = Player.m_localPlayer != null;
            GUI.enabled = canPreview;
            bool preview = GUILayout.Toggle(_preview,
                canPreview ? L10n.T("win_preview") : L10n.T("win_preview_ingame"),
                _styles.Button, GUILayout.Height(P(34f)));
            if (preview != _preview)
                _preview = preview;
            Track(PreviewButton, layout);
            GUI.enabled = true;
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(L10n.Game("$menu_close", "Close"), _styles.Button, GUILayout.MinWidth(P(110f)), GUILayout.Height(P(34f))))
                _closeRequested = true;
            Track(CloseButton, layout);
            GUILayout.EndHorizontal();

            GUILayout.EndArea();

            ResizeGrip();
            GUI.DragWindow(new Rect(0f, 0f, _window.width - P(48f), P(TitleBand)));

            if (repaint)
            {
                // This frame's rects replace last frame's, so rows no longer drawn (a collapsed
                // section, a search) cannot be hovered where they used to be.
                (_rowRects, _rectsNext) = (_rectsNext, _rowRects);
                KeepFocusInView();
            }
            if (layout)
            {
                _nav.Clear();
                _nav.AddRange(_navBuilding);
                if (_focus != null && !_nav.Contains(_focus))
                    _focus = null;
                // A controller always has something selected (opened from the pause menu, on a Steam Deck).
                if (_focus == null && _keyboardMode && _nav.Count > 0)
                    _focus = _nav[0];
            }
        }

        /// <summary>One feature's header and, when open, its settings. False if search hides all of it.</summary>
        private bool DrawSection(string name, List<ConfigEntryBase> entries)
        {
            var enabled = entries.FirstOrDefault(e => e.Definition.Key == "Enabled" && e.SettingType == typeof(bool));
            string title = SectionName(name);
            bool searching = _search.Trim().Length > 0;
            List<ConfigEntryBase> matches = entries;
            if (searching)
            {
                bool sectionMatch = Matches(title) || Matches(name);
                matches = entries.Where(e => sectionMatch || Matches(Label(e)) || Matches(Describe(e)) || Matches(e.Definition.Key)).ToList();
                if (matches.Count == 0)
                    return false;
            }
            bool open = searching || _expanded.Contains(name);
            bool layout = Event.current.type == EventType.Layout;
            int changed = entries.Count(IsChanged);

            // Header.
            GUILayout.BeginHorizontal(_styles.Header);
            if (GUILayout.Button(title, _styles.HeaderButton, GUILayout.MinWidth(0f), GUILayout.ExpandWidth(true), GUILayout.Height(P(32f))))
            {
                if (!searching)
                    SetExpanded(new[] { name }, !open);
                Focus(name, keyboard: false);
            }
            var headerButton = GUILayoutUtility.GetLastRect();
            if (Event.current.type == EventType.Repaint)
            {
                var arrow = open ? _styles.ArrowDown : _styles.ArrowRight;
                GUI.DrawTexture(new Rect(headerButton.x + P(8f), headerButton.center.y - P(6f), P(12f), P(12f)), arrow);
                if (changed > 0)
                {
                    float w = _styles.HeaderButton.CalcSize(new GUIContent(title)).x;
                    var tag = new Rect(headerButton.x + w + P(6f), headerButton.y, headerButton.width - w - P(6f), headerButton.height);
                    _styles.Changed.Draw(tag, L10n.T("win_changed", changed.ToString()), false, false, false, false);
                }
            }
            if (changed > 0 && GUILayout.Button(L10n.T("win_reset_all"), _styles.Small, GUILayout.Height(P(26f))))
                ResetAll(entries);
            if (enabled != null)
                OnOff(enabled, P(72f));
            GUILayout.EndHorizontal();
            Track(name, layout);

            if (open)
            {
                bool featureOn = enabled == null || (bool)enabled.BoxedValue;
                string group = null;
                foreach (var entry in matches)
                {
                    if (entry == enabled)
                        continue;
                    var meta = ConfigText.MetaOf(entry);
                    if (meta?.Group != group)
                    {
                        group = meta?.Group;
                        if (group != null)
                            GUILayout.Label(GroupName(group), _styles.Group, GUILayout.Height(P(24f)));
                    }
                    DrawEntry(entry, featureOn, layout);
                }
                GUILayout.Space(P(8f));
            }
            GUILayout.Space(P(4f));
            return true;
        }

        private void DrawEntry(ConfigEntryBase entry, bool featureOn, bool layout)
        {
            bool active = ConfigText.IsActive(entry);
            bool focused = _keyboardMode && Equals(_focus, entry);
            if (Event.current.type == EventType.Repaint && _rowRects.TryGetValue(entry, out var last))
            {
                // Inside the scroll view the mouse is in the list's own coordinates, like the rects.
                if (focused)
                    GUI.DrawTexture(last, _styles.RowFocus);
                else if (!_keyboardMode && last.Contains(Event.current.mousePosition))
                    GUI.DrawTexture(last, _styles.RowHover);
            }

            var oldColor = GUI.color;
            if (!featureOn || !active)
                GUI.color = new Color(1f, 1f, 1f, active ? 0.55f : 0.38f);
            GUI.enabled = active;

            GUILayout.BeginHorizontal(GUILayout.MinHeight(P(RowHeight)));
            GUILayout.Space(P(8f));
            var label = new GUIContent(Label(entry));
            float labelWidth = Mathf.Max(P(LabelWidth), _styles.Label.CalcSize(label).x + P(8f));
            GUILayout.Label(label, _styles.Label, GUILayout.Width(labelWidth), GUILayout.Height(P(RowHeight)));
            DrawControl(entry);
            if (IsChanged(entry))
            {
                if (GUILayout.Button(L10n.T("win_reset"), _styles.Small, GUILayout.Width(P(64f)), GUILayout.Height(P(26f))))
                    ResetAll(new[] { entry });
            }
            else
            {
                GUILayout.Space(P(64f) + _styles.Small.margin.horizontal);
            }
            GUILayout.EndHorizontal();
            GUI.enabled = true;
            GUI.color = oldColor;

            // Rows stay focusable while greyed out, so the description can say why.
            Track(entry, layout);
            if (Event.current.type == EventType.MouseDown && _mouseInList && _rowRects.TryGetValue(entry, out var rect)
                && rect.Contains(Event.current.mousePosition))
                Focus(entry, keyboard: false);

            if (_colorOpen == entry && entry.BoxedValue is Color col)
                DrawColorPicker(entry, col);
        }

        private void DrawControl(ConfigEntryBase entry)
        {
            var range = entry.Description?.AcceptableValues;
            switch (entry.BoxedValue)
            {
                case bool _:
                    GUILayout.FlexibleSpace();
                    OnOff(entry, P(88f));
                    break;

                case float f when range is AcceptableValueRange<float> fr:
                {
                    float step = Step(entry, fr.MinValue, fr.MaxValue);
                    float raw = GUILayout.HorizontalSlider(f, fr.MinValue, fr.MaxValue, _styles.Slider, _styles.Thumb,
                        GUILayout.ExpandWidth(true), GUILayout.Height(P(RowHeight)));
                    // Only write when the user actually moved the slider: snapping an untouched value
                    // (0.985 -> 0.99) would silently change settings just by opening the window.
                    if (raw != f)
                        entry.BoxedValue = Mathf.Clamp(Snap(raw, step, fr.MinValue), fr.MinValue, fr.MaxValue);
                    NumberField(entry, f, fr.MinValue, fr.MaxValue, step);
                    break;
                }

                case int i when range is AcceptableValueRange<int> ir:
                {
                    int ni = Mathf.RoundToInt(GUILayout.HorizontalSlider(i, ir.MinValue, ir.MaxValue, _styles.Slider,
                        _styles.Thumb, GUILayout.ExpandWidth(true), GUILayout.Height(P(RowHeight))));
                    if (ni != i)
                        entry.BoxedValue = ni;
                    NumberField(entry, i, ir.MinValue, ir.MaxValue, 1f);
                    break;
                }

                case System.Enum e:
                {
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("<", _styles.Arrow, GUILayout.Width(P(26f)), GUILayout.Height(P(26f))))
                        StepEnum(entry, -1);
                    // Click for the next choice, right-click for the previous one.
                    var content = new GUIContent(EnumName(e));
                    var rect = GUILayoutUtility.GetRect(content, _styles.Button, GUILayout.MinWidth(P(170f)), GUILayout.Height(P(26f)));
                    if (Event.current.type == EventType.MouseDown && Event.current.button == 1 && rect.Contains(Event.current.mousePosition))
                    {
                        StepEnum(entry, -1);
                        Event.current.Use();
                    }
                    else if (GUI.Button(rect, content, _styles.Button))
                    {
                        StepEnum(entry, +1);
                    }
                    if (GUILayout.Button(">", _styles.Arrow, GUILayout.Width(P(26f)), GUILayout.Height(P(26f))))
                        StepEnum(entry, +1);
                    break;
                }

                case Color col:
                {
                    GUILayout.FlexibleSpace();
                    var swatch = GUILayoutUtility.GetRect(P(40f), P(26f), GUILayout.Width(P(40f)), GUILayout.Height(P(26f)));
                    if (GUI.Button(swatch, GUIContent.none, _styles.SwatchFrame))
                        _colorOpen = _colorOpen == entry ? null : entry;
                    if (Event.current.type == EventType.Repaint)
                    {
                        var inner = new Rect(swatch.x + 2f, swatch.y + 2f, swatch.width - 4f, swatch.height - 4f);
                        GUI.DrawTexture(inner, _styles.Checker);
                        var old = GUI.color;
                        GUI.color = new Color(col.r, col.g, col.b, col.a * old.a);
                        GUI.DrawTexture(inner, Texture2D.whiteTexture);
                        GUI.color = old;
                    }
                    // Typed text lives in a buffer, so a half-typed value is not snapped back.
                    if (!_textEdits.TryGetValue(entry, out var shown))
                        shown = ConfigText.Hex(col);
                    GUI.SetNextControlName(FieldName(entry));
                    string typed = GUILayout.TextField(shown, 8, _styles.Field, GUILayout.Width(P(112f)), GUILayout.Height(P(26f)));
                    if (typed != shown)
                    {
                        _textEdits[entry] = typed;
                        if ((typed.Length == 6 || typed.Length == 8) && ColorUtility.TryParseHtmlString("#" + typed, out var parsed))
                            entry.BoxedValue = parsed;
                    }
                    break;
                }

                case KeyboardShortcut k:
                {
                    GUILayout.FlexibleSpace();
                    string text = _capturingKey == entry ? L10n.T("win_press_key")
                        : k.MainKey == KeyCode.None ? L10n.T("win_none") : k.ToString();
                    if (GUILayout.Button(text, _styles.Button, GUILayout.MinWidth(P(170f)), GUILayout.Height(P(26f))))
                        _capturingKey = entry;
                    break;
                }

                default:
                {
                    string s = entry.GetSerializedValue();
                    if (!_textEdits.TryGetValue(entry, out var shown))
                        shown = s;
                    GUI.SetNextControlName(FieldName(entry));
                    string typed = GUILayout.TextField(shown, _styles.Field, GUILayout.MinWidth(P(120f)),
                        GUILayout.ExpandWidth(true), GUILayout.Height(P(26f)));
                    if (typed != shown)
                    {
                        _textEdits[entry] = typed;
                        try { entry.SetSerializedValue(typed); }
                        catch { /* keep typing; invalid text is ignored */ }
                    }
                    break;
                }
            }
        }

        /// <summary>The value next to a slider, as a box you can click and type into.</summary>
        private void NumberField(ConfigEntryBase entry, float value, float min, float max, float step)
        {
            string format = step >= 1f ? "0" : step >= 0.1f ? "0.0" : "0.00";
            string name = FieldName(entry);
            bool editing = GUI.GetNameOfFocusedControl() == name;
            if (!_textEdits.TryGetValue(entry, out var shown) || !editing)
                shown = value.ToString(format, CultureInfo.InvariantCulture);
            GUI.SetNextControlName(name);
            string typed = GUILayout.TextField(shown, 8, _styles.Number, GUILayout.Width(P(64f)), GUILayout.Height(P(26f)));
            if (typed != shown)
            {
                _textEdits[entry] = typed;
                // Applied as you type while it is a valid, in-range number; clamped when you leave.
                if (TryNumber(typed, out float v) && v >= min && v <= max)
                    SetNumber(entry, v);
            }
            if (!editing && _textEdits.TryGetValue(entry, out var pending))
            {
                _textEdits.Remove(entry);
                if (TryNumber(pending, out float v))
                    SetNumber(entry, Mathf.Clamp(v, min, max));
            }
        }

        private static bool TryNumber(string s, out float v) =>
            float.TryParse((s ?? "").Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v);

        private static void SetNumber(ConfigEntryBase entry, float v)
        {
            if (entry.SettingType == typeof(int))
                entry.BoxedValue = Mathf.RoundToInt(v);
            else
                entry.BoxedValue = v;
        }

        private void OnOff(ConfigEntryBase entry, float width)
        {
            bool on = (bool)entry.BoxedValue;
            string onText = L10n.Game("$hud_on", "On"), offText = L10n.Game("$hud_off", "Off");
            // Wide enough for the longer of the two words, so the button does not jump or clip.
            width = Mathf.Max(width, Mathf.Max(_styles.Button.CalcSize(new GUIContent(onText)).x,
                _styles.Button.CalcSize(new GUIContent(offText)).x) + P(8f));
            bool now = GUILayout.Toggle(on, on ? onText : offText, _styles.Button, GUILayout.Width(width), GUILayout.Height(P(26f)));
            if (now != on)
                entry.BoxedValue = now;
        }

        // ---------- color picker ----------

        private void DrawColorPicker(ConfigEntryBase entry, Color col)
        {
            Color.RGBToHSV(col, out float h, out float s, out float v);
            // Black or grey has no hue (and black no saturation): keep the ones you set, or dragging
            // brightness to 0 and back would turn the color red.
            if (_pickFor == entry)
            {
                if (v <= 0.001f) { h = _pickH; s = _pickS; }
                else if (s <= 0.001f) h = _pickH;
            }
            _pickFor = entry;
            _pickH = h;
            _pickS = s;
            _styles.UpdatePickerTextures(h, s, v, col);
            GUILayout.BeginVertical(_styles.Panel);
            bool layout = Event.current.type == EventType.Layout;
            float nh = PickerSlider(entry, 0, "H", h, _styles.HueTrack, layout);
            float ns = PickerSlider(entry, 1, "S", s, _styles.SatTrack, layout);
            float nv = PickerSlider(entry, 2, "V", v, _styles.ValTrack, layout);
            float na = PickerSlider(entry, 3, "A", col.a, _styles.AlphaTrack, layout);
            GUILayout.EndVertical();
            if (nh != h || ns != s || nv != v || na != col.a)
            {
                var c = Color.HSVToRGB(nh, ns, nv);
                c.a = na;
                entry.BoxedValue = c;
                _textEdits.Remove(entry);
            }
        }

        private float PickerSlider(ConfigEntryBase entry, int channel, string label, float value, GUIStyle track, bool layout)
        {
            var key = new PickKey(entry, channel);
            if (Event.current.type == EventType.Repaint && _keyboardMode && Equals(_focus, key) && _rowRects.TryGetValue(key, out var r))
                GUI.DrawTexture(r, _styles.RowFocus);
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, _styles.PickerLabel, GUILayout.Width(P(22f)), GUILayout.Height(P(22f)));
            float nv = GUILayout.HorizontalSlider(value, 0f, 1f, track, _styles.PickerThumb, GUILayout.ExpandWidth(true), GUILayout.Height(P(22f)));
            GUILayout.Label(Mathf.RoundToInt(value * 100f).ToString(), _styles.Value, GUILayout.Width(P(40f)), GUILayout.Height(P(22f)));
            GUILayout.EndHorizontal();
            Track(key, layout); // the picker's sliders are reachable with the keys and a controller too
            return nv;
        }

        /// <summary>One channel (H, S, V, A) of an open color picker, as a navigation target.</summary>
        private sealed class PickKey
        {
            public readonly ConfigEntryBase Entry;
            public readonly int Channel;
            public PickKey(ConfigEntryBase entry, int channel) { Entry = entry; Channel = channel; }
            public override bool Equals(object o) => o is PickKey k && k.Entry == Entry && k.Channel == Channel;
            public override int GetHashCode() => Entry.GetHashCode() * 4 + Channel;
        }

        /// <summary>Left/Right on a picker channel.</summary>
        private void ChangeColor(PickKey key, int dir, bool fast)
        {
            if (!(key.Entry.BoxedValue is Color col))
                return;
            Color.RGBToHSV(col, out float h, out float s, out float v);
            if (_pickFor == key.Entry)
            {
                if (v <= 0.001f) { h = _pickH; s = _pickS; }
                else if (s <= 0.001f) h = _pickH;
            }
            float step = (fast ? 0.05f : 0.01f) * dir;
            float a = col.a;
            switch (key.Channel)
            {
                case 0: h = Mathf.Repeat(h + step, 1f); break;
                case 1: s = Mathf.Clamp01(s + step); break;
                case 2: v = Mathf.Clamp01(v + step); break;
                default: a = Mathf.Clamp01(a + step); break;
            }
            _pickFor = key.Entry;
            _pickH = h;
            _pickS = s;
            var c = Color.HSVToRGB(h, s, v);
            c.a = a;
            key.Entry.BoxedValue = c;
            _textEdits.Remove(key.Entry);
        }

        // ---------- description strip ----------

        private void DrawDescription()
        {
            object target = _keyboardMode ? _focus : HoveredRow() ?? _focus;
            if (target is PickKey pick)
                target = pick.Entry;
            string title = "", body = "", footer = "";
            if (target is ConfigEntryBase entry)
            {
                title = Label(entry);
                body = Describe(entry);
                footer = L10n.T("win_default", DisplayValue(entry, entry.DefaultValue));
                string options = Options(entry);
                if (options.Length > 0)
                    footer += "    " + options;
                if (!ConfigText.IsActive(entry))
                    footer = $"<color=#{HudKit.Hex(HudKit.Warn)}>{WhenText(entry)}</color>    " + footer;
                if (_capturingKey == entry)
                    footer = L10n.T("win_press_key");
            }
            else if (target is string section)
            {
                title = SectionName(section);
                var enabled = _sections.First(s => s.Name == section).Entries
                    .FirstOrDefault(e => e.Definition.Key == "Enabled" && e.SettingType == typeof(bool));
                body = enabled != null ? Describe(enabled) : "";
            }
            else if (target == PreviewButton)
            {
                title = L10n.T("win_preview");
                body = L10n.T("win_preview_desc");
            }
            var rect = GUILayoutUtility.GetRect(0f, P(DescribeHeight), GUILayout.ExpandWidth(true));
            if (Event.current.type != EventType.Repaint)
                return;
            _styles.Describe.Draw(rect, GUIContent.none, false, false, false, false);
            var inner = new Rect(rect.x + P(10f), rect.y + P(6f), rect.width - P(20f), rect.height - P(12f));
            if (title.Length == 0)
            {
                string controls = ZInput.IsGamepadActive() ? L10n.T("win_pad_hint") : L10n.T("win_key_hint");
                _styles.DescribeBody.Draw(inner, $"{L10n.T("win_describe_hint")}\n<color=#B3A38C>{controls}</color>",
                    false, false, false, false);
                return;
            }
            float titleH = P(20f);
            _styles.DescribeTitle.Draw(new Rect(inner.x, inner.y, inner.width, titleH), title, false, false, false, false);
            float footerH = footer.Length > 0 ? P(18f) : 0f;
            _styles.DescribeBody.Draw(new Rect(inner.x, inner.y + titleH, inner.width, inner.height - titleH - footerH),
                body, false, false, false, false);
            if (footer.Length > 0)
                _styles.DescribeFooter.Draw(new Rect(inner.x, inner.yMax - footerH, inner.width, footerH), footer,
                    false, false, false, false);
        }

        private object HoveredRow()
        {
            var mouse = Event.current.mousePosition;
            if (!_listRect.Contains(mouse))
                return _rowRects.TryGetValue(PreviewButton, out var pr) && pr.Contains(mouse) ? PreviewButton : null;
            var local = mouse - _listRect.position + _scroll;
            foreach (var kv in _rowRects)
                if (kv.Key != PreviewButton && kv.Key != CloseButton && kv.Value.Contains(local))
                    return kv.Key;
            return null;
        }

        // ---------- navigation ----------

        /// <summary>Remembers a row's rect (from the repaint pass) and its place in the navigation order.</summary>
        private void Track(object key, bool layout)
        {
            if (layout)
                _navBuilding.Add(key);
            if (Event.current.type == EventType.Repaint)
                _rectsNext[key] = GUILayoutUtility.GetLastRect();
        }

        private void Focus(object key, bool keyboard = true)
        {
            _focus = key;
            _describe = key;
            if (keyboard)
            {
                _keyboardMode = true;
                _scrollToFocus = true;
            }
        }

        private void MoveFocus(int delta)
        {
            if (_nav.Count == 0)
                return;
            GUIUtility.keyboardControl = 0;
            int i = _focus != null ? _nav.IndexOf(_focus) : -1;
            i = i < 0 ? (delta > 0 ? 0 : _nav.Count - 1) : Mathf.Clamp(i + delta, 0, _nav.Count - 1);
            Focus(_nav[i]);
        }

        /// <summary>Jump to the previous or next section header.</summary>
        private void JumpSection(int dir)
        {
            if (_nav.Count == 0)
                return;
            int i = _focus != null ? _nav.IndexOf(_focus) : -1;
            for (int n = i + dir; n >= 0 && n < _nav.Count; n += dir)
                if (_nav[n] is string)
                {
                    Focus(_nav[n]);
                    return;
                }
        }

        private void KeepFocusInView()
        {
            if (!_scrollToFocus || _focus == null || !_rowRects.TryGetValue(_focus, out var r))
                return;
            _scrollToFocus = false;
            if (_focus == PreviewButton || _focus == CloseButton)
                return;
            float view = _listRect.height;
            float margin = P(RowHeight);
            if (r.yMin - margin < _scroll.y)
                _scroll.y = Mathf.Max(0f, r.yMin - margin);
            else if (r.yMax + margin > _scroll.y + view)
                _scroll.y = r.yMax + margin - view;
        }

        /// <summary>Enter/Space on the focused row: open a section, flip a bool, type a number, and so on.</summary>
        private void Activate()
        {
            switch (_focus)
            {
                case string section:
                    if (_search.Trim().Length == 0)
                        SetExpanded(new[] { section }, !_expanded.Contains(section));
                    break;
                case ConfigEntryBase entry when !ConfigText.IsActive(entry):
                    break;
                case ConfigEntryBase entry:
                    switch (entry.BoxedValue)
                    {
                        case bool b: entry.BoxedValue = !b; break;
                        case System.Enum _: StepEnum(entry, +1); break;
                        case Color _: _colorOpen = _colorOpen == entry ? null : entry; break;
                        // A controller cannot type a key; it would be stuck waiting for one.
                        case KeyboardShortcut _ when !ZInput.IsGamepadActive(): _capturingKey = entry; break;
                        default:
                            if (!ZInput.IsGamepadActive())
                                GUI.FocusControl(FieldName(entry));
                            break;
                    }
                    break;
                default:
                    if (_focus == PreviewButton && Player.m_localPlayer != null)
                        _preview = !_preview;
                    else if (_focus == CloseButton)
                        _closeRequested = true;
                    break;
            }
        }

        /// <summary>Left/Right on the focused row: step a number, an enum or a bool.</summary>
        private void Change(int dir, bool fast)
        {
            if (_focus is PickKey pick)
            {
                ChangeColor(pick, dir, fast);
                return;
            }
            if (!(_focus is ConfigEntryBase entry) || !ConfigText.IsActive(entry))
            {
                if (_focus is string section && _search.Trim().Length == 0)
                    SetExpanded(new[] { section }, dir > 0);
                return;
            }
            var range = entry.Description?.AcceptableValues;
            switch (entry.BoxedValue)
            {
                case bool b:
                    entry.BoxedValue = dir > 0;
                    break;
                case float f when range is AcceptableValueRange<float> fr:
                {
                    float step = Step(entry, fr.MinValue, fr.MaxValue) * (fast ? 5f : 1f);
                    entry.BoxedValue = Mathf.Clamp(Snap(f + dir * step, Step(entry, fr.MinValue, fr.MaxValue), fr.MinValue), fr.MinValue, fr.MaxValue);
                    _textEdits.Remove(entry);
                    break;
                }
                case int i when range is AcceptableValueRange<int> ir:
                    entry.BoxedValue = Mathf.Clamp(i + dir * (fast ? 5 : 1), ir.MinValue, ir.MaxValue);
                    _textEdits.Remove(entry);
                    break;
                case System.Enum _:
                    StepEnum(entry, dir);
                    break;
            }
        }

        private void KeyboardInput(Event e)
        {
            // Typing in a field: only Enter/Escape/Tab leave it.
            if (EditingText())
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter || e.keyCode == KeyCode.Escape)
                {
                    GUIUtility.keyboardControl = 0;
                    e.Use();
                }
                return;
            }
            bool fast = e.shift;
            switch (e.keyCode)
            {
                case KeyCode.UpArrow: MoveFocus(-1); break;
                case KeyCode.DownArrow: MoveFocus(+1); break;
                case KeyCode.Tab: MoveFocus(e.shift ? -1 : +1); break;
                case KeyCode.PageUp: MoveFocus(-8); break;
                case KeyCode.PageDown: MoveFocus(+8); break;
                case KeyCode.Home: if (_nav.Count > 0) Focus(_nav[0]); break;
                case KeyCode.End: if (_nav.Count > 0) Focus(_nav[_nav.Count - 1]); break;
                case KeyCode.LeftArrow: Change(-1, fast); _keyboardMode = true; break;
                case KeyCode.RightArrow: Change(+1, fast); _keyboardMode = true; break;
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                case KeyCode.Space:
                    Activate();
                    break;
                case KeyCode.Delete:
                    if (_focus is ConfigEntryBase entry)
                        ResetAll(new[] { entry });
                    break;
                case KeyCode.F when e.control:
                    GUI.FocusControl("kwui_search");
                    break;
                default:
                    return;
            }
            e.Use();
        }

        private void GamepadInput()
        {
            if (!ZInput.IsGamepadActive())
                return;
            if (ZInput.GetButtonDown("JoyButtonA")) { _keyboardMode = true; Activate(); }
            if (ZInput.GetButtonDown("JoyButtonX") && Player.m_localPlayer != null) _preview = !_preview;
            if (ZInput.GetButtonDown("JoyButtonY") && _focus is ConfigEntryBase entry) ResetAll(new[] { entry });
            // Tab buttons are unbound in some controller layouts; the bumpers always exist.
            if (ZInput.GetButtonDown("JoyTabLeft") || ZInput.GetButtonDown("JoyLBumper")) JumpSection(-1);
            if (ZInput.GetButtonDown("JoyTabRight") || ZInput.GetButtonDown("JoyRBumper")) JumpSection(+1);

            // D-pad and left stick, with hold-to-repeat.
            string held = null;
            foreach (var b in new[] { "JoyDPadUp", "JoyLStickUp", "JoyDPadDown", "JoyLStickDown", "JoyDPadLeft", "JoyLStickLeft", "JoyDPadRight", "JoyLStickRight" })
                if (ZInput.GetButton(b)) { held = b; break; }
            if (held == null)
            {
                _heldButton = null;
                return;
            }
            float now = Time.unscaledTime;
            bool fire;
            if (held != _heldButton)
            {
                _heldButton = held;
                _heldSince = now;
                _nextRepeat = now + RepeatDelay;
                fire = true;
            }
            else
            {
                fire = now >= _nextRepeat;
                if (fire)
                    _nextRepeat = now + RepeatRate;
            }
            if (!fire)
                return;
            bool fast = now - _heldSince > 1.2f;
            if (held.EndsWith("Up")) MoveFocus(-1);
            else if (held.EndsWith("Down")) MoveFocus(+1);
            else if (held.EndsWith("Left")) { _keyboardMode = true; Change(-1, fast); }
            else if (held.EndsWith("Right")) { _keyboardMode = true; Change(+1, fast); }
        }

        /// <summary>Records the next key press (with Ctrl, Shift or Alt held) as the binding.</summary>
        private void CaptureKey()
        {
            var e = Event.current;
            if (e.type != EventType.KeyDown || e.keyCode == KeyCode.None)
                return;
            var key = e.keyCode;
            if (key == KeyCode.LeftShift || key == KeyCode.RightShift || key == KeyCode.LeftControl || key == KeyCode.RightControl
                || key == KeyCode.LeftAlt || key == KeyCode.RightAlt || key == KeyCode.LeftCommand || key == KeyCode.RightCommand)
                return; // wait for the key that goes with the modifier
            if (key == KeyCode.Escape)
            {
                // Cancel.
            }
            else if (key == KeyCode.Backspace || key == KeyCode.Delete)
            {
                _capturingKey.BoxedValue = new KeyboardShortcut(KeyCode.None);
            }
            else
            {
                // The side actually held: a binding made with Right Ctrl must fire with Right Ctrl.
                var input = BepInEx.UnityInput.Current;
                var mods = new List<KeyCode>();
                if (e.control) mods.Add(input.GetKey(KeyCode.RightControl) ? KeyCode.RightControl : KeyCode.LeftControl);
                if (e.shift) mods.Add(input.GetKey(KeyCode.RightShift) ? KeyCode.RightShift : KeyCode.LeftShift);
                if (e.alt) mods.Add(input.GetKey(KeyCode.RightAlt) ? KeyCode.RightAlt : KeyCode.LeftAlt);
                _capturingKey.BoxedValue = new KeyboardShortcut(key, mods.ToArray());
            }
            _capturingKey = null;
            e.Use();
        }

        // ---------- resizing ----------

        private void ResizeGrip()
        {
            int id = GUIUtility.GetControlID(FocusType.Passive);
            var grip = new Rect(_window.width - P(20f), _window.height - P(20f), P(20f), P(20f));
            var e = Event.current;
            switch (e.GetTypeForControl(id))
            {
                case EventType.Repaint:
                    GUI.DrawTexture(grip, _styles.Grip);
                    break;
                case EventType.MouseDown when grip.Contains(e.mousePosition):
                    // Holding the control keeps the drag events coming even when the mouse leaves the window.
                    GUIUtility.hotControl = id;
                    _resizing = true;
                    _resizeFrom = GUIUtility.GUIToScreenPoint(e.mousePosition);
                    _resizeSize = _resizeTo = _window.size;
                    e.Use();
                    break;
                case EventType.MouseDrag when GUIUtility.hotControl == id:
                {
                    var now = GUIUtility.GUIToScreenPoint(e.mousePosition);
                    _resizeTo = new Vector2(Mathf.Max(P(MinBaseWidth), _resizeSize.x + now.x - _resizeFrom.x),
                        Mathf.Max(P(MinBaseHeight), _resizeSize.y + now.y - _resizeFrom.y));
                    e.Use();
                    break;
                }
                case EventType.MouseUp when GUIUtility.hotControl == id:
                    GUIUtility.hotControl = 0;
                    _resizing = false;
                    _applySize = true;
                    e.Use();
                    break;
            }
        }

        // ---------- helpers ----------

        private static float Step(ConfigEntryBase entry, float min, float max)
        {
            float meta = ConfigText.MetaOf(entry)?.Step ?? 0f;
            if (meta > 0f)
                return meta;
            float span = max - min;
            return span >= 50f ? 1f : span >= 5f ? 0.1f : 0.01f;
        }

        private static string FieldName(ConfigEntryBase e) => "kwui_field_" + e.Definition.Section + "_" + e.Definition.Key;

        private static float Snap(float v, float step, float origin) =>
            Mathf.Round((v - origin) / step) * step + origin;

        private static void StepEnum(ConfigEntryBase entry, int dir)
        {
            var values = System.Enum.GetValues(entry.SettingType);
            int idx = System.Array.IndexOf(values, entry.BoxedValue);
            entry.BoxedValue = values.GetValue(((idx + dir) % values.Length + values.Length) % values.Length);
        }

        private bool Matches(string text) =>
            !string.IsNullOrEmpty(text) && text.IndexOf(_search.Trim(), System.StringComparison.OrdinalIgnoreCase) >= 0;

        private void SetExpanded(IEnumerable<string> names, bool open)
        {
            foreach (var name in names)
            {
                if (open) _expanded.Add(name);
                else _expanded.Remove(name);
            }
            General.ExpandedSections.Value = string.Join("|", _expanded.OrderBy(n => n));
        }

        private void ResetAll(IEnumerable<ConfigEntryBase> entries)
        {
            foreach (var entry in entries)
            {
                entry.BoxedValue = entry.DefaultValue;
                _textEdits.Remove(entry);
            }
        }

        // Compared as saved text: colors are stored to 1/255, so a loaded color never equals its float default exactly.
        private static bool IsChanged(ConfigEntryBase entry) =>
            TomlTypeConverter.ConvertToString(entry.BoxedValue, entry.SettingType)
            != TomlTypeConverter.ConvertToString(entry.DefaultValue, entry.SettingType);

        // ---------- text, in the game's language where translated ----------

        private static string Slug(string s) => new string((s ?? "").Where(char.IsLetterOrDigit).ToArray());

        private static string Key(ConfigEntryBase e) => $"s_{Slug(e.Definition.Section)}_{e.Definition.Key}";

        private static string SectionName(string section) => L10n.W($"sec_{Slug(section)}", section);

        private static string GroupName(string group) => L10n.W($"grp_{Slug(group)}", group);

        private static string Label(ConfigEntryBase e) => L10n.W(Key(e), ConfigText.Name(e));

        private static string Describe(ConfigEntryBase e) => L10n.W(Key(e) + "_d", e.Description?.Description ?? "");

        private static string WhenText(ConfigEntryBase e)
        {
            string when = ConfigText.MetaOf(e)?.WhenText;
            return string.IsNullOrEmpty(when) ? "" : L10n.W(Key(e) + "_w", $"Used when {when}.");
        }

        // Keyed by the declaring feature too: two features both have a "Place" enum.
        private static string EnumName(System.Enum e) =>
            L10n.W($"enum_{e.GetType().DeclaringType?.Name}{e.GetType().Name}_{e}", ConfigText.Pretty(e.ToString()));

        private static string DisplayValue(ConfigEntryBase entry, object value)
        {
            switch (value)
            {
                case bool b: return b ? L10n.Game("$hud_on", "On") : L10n.Game("$hud_off", "Off");
                case System.Enum e: return EnumName(e);
                case KeyboardShortcut k: return k.MainKey == KeyCode.None ? L10n.T("win_none") : k.ToString();
                default: return ConfigText.Value(value);
            }
        }

        private static string Options(ConfigEntryBase entry)
        {
            switch (entry.Description?.AcceptableValues)
            {
                case AcceptableValueRange<float> r:
                    return L10n.T("win_range", $"{ConfigText.Value(r.MinValue)} - {ConfigText.Value(r.MaxValue)}");
                case AcceptableValueRange<int> r:
                    return L10n.T("win_range", $"{r.MinValue} - {r.MaxValue}");
            }
            if (entry.SettingType.IsEnum)
                return L10n.T("win_options", string.Join(", ",
                    System.Enum.GetValues(entry.SettingType).Cast<System.Enum>().Select(EnumName)));
            return "";
        }

        /// <summary>Dark wood and gold, close to Valheim's own menus. Built for one scale and language.</summary>
        private class Styles
        {
            public readonly string Language;
            public readonly GUIStyle Window, Title, CloseX, Header, HeaderButton, Changed, Group, Label, Value, Hint, Placeholder,
                Button, Small, Arrow, Slider, Thumb, Field, Number, Scrollbar, Describe, DescribeTitle, DescribeBody,
                DescribeFooter, Panel, PickerLabel, PickerThumb, SwatchFrame, HueTrack, SatTrack, ValTrack, AlphaTrack;
            public readonly Texture2D RowHover, RowFocus, ArrowRight, ArrowDown, Grip, Checker, Cross;
            private readonly Texture2D _hue, _sat, _val, _alpha;

            // Every texture these styles made, freed when the styles are replaced (new scale or language).
            private readonly List<Texture2D> _owned = new List<Texture2D>();
            private static List<Texture2D> _building;
            private static readonly Dictionary<string, Font> Fonts = new Dictionary<string, Font>();

            public void Destroy()
            {
                foreach (var t in _owned)
                    if (t != null)
                        Object.Destroy(t);
                _owned.Clear();
            }

            private static Texture2D Own(Texture2D t)
            {
                _building?.Add(t);
                return t;
            }

            private static readonly Color Wood = new Color(0.13f, 0.09f, 0.06f, 0.97f);
            private static readonly Color WoodLight = new Color(0.24f, 0.17f, 0.11f, 1f);
            private static readonly Color WoodHover = new Color(0.33f, 0.24f, 0.15f, 1f);
            private static readonly Color Gold = new Color(0.87f, 0.70f, 0.36f, 1f);
            private static readonly Color Parchment = new Color(0.92f, 0.87f, 0.78f, 1f);
            private static readonly Color Muted = new Color(0.70f, 0.64f, 0.55f, 1f);

            public Styles(float s, string language)
            {
                _building = _owned;
                Language = language;
                int F(float size) => Mathf.Max(9, Mathf.RoundToInt(size * s));
                int I(float v) => Mathf.RoundToInt(v * s);
                var font = FontFor(language);

                Window = new GUIStyle(GUI.skin.window) { padding = new RectOffset(0, 0, 0, 0), border = new RectOffset(4, 4, 4, 4) };
                SetAll(Window, Tex(Wood, Gold), Gold);
                Title = Text(font, F(21), Gold, FontStyle.Bold);
                Title.alignment = TextAnchor.MiddleCenter;
                Title.richText = true;
                CloseX = new GUIStyle(GUI.skin.button) { font = font, fontSize = F(22), alignment = TextAnchor.MiddleCenter };
                SetAll(CloseX, null, Muted);
                CloseX.normal.background = null;
                CloseX.hover.background = Tex(WoodHover, WoodHover);
                CloseX.hover.textColor = Parchment;

                Header = new GUIStyle { padding = new RectOffset(0, I(6), I(3), I(3)), margin = new RectOffset(0, 0, I(2), I(2)),
                    border = new RectOffset(2, 2, 2, 2) };
                Header.normal.background = Tex(new Color(0.19f, 0.13f, 0.08f, 1f), new Color(0.45f, 0.35f, 0.18f, 1f));
                HeaderButton = Text(font, F(19), Gold, FontStyle.Bold);
                HeaderButton.alignment = TextAnchor.MiddleLeft;
                HeaderButton.padding = new RectOffset(I(28), I(4), 0, 0);
                HeaderButton.hover.textColor = Parchment;
                HeaderButton.active.textColor = Parchment;
                Changed = Text(font, F(13), new Color(0.70f, 0.64f, 0.55f, 1f));
                Changed.alignment = TextAnchor.MiddleLeft;
                Group = Text(font, F(14), new Color(Gold.r, Gold.g, Gold.b, 0.85f), FontStyle.Bold);
                Group.alignment = TextAnchor.LowerLeft;
                Group.padding = new RectOffset(I(10), 0, 0, I(2));

                Label = Text(font, F(16), Parchment);
                Label.alignment = TextAnchor.MiddleLeft;
                Value = Text(font, F(15), Gold);
                Value.alignment = TextAnchor.MiddleRight;
                Hint = Text(font, F(14), Muted);
                Hint.alignment = TextAnchor.MiddleLeft;
                Placeholder = Text(font, F(15), new Color(Muted.r, Muted.g, Muted.b, 0.7f));
                Placeholder.alignment = TextAnchor.MiddleLeft;
                Placeholder.padding = new RectOffset(I(8), 0, 0, 0);

                Button = new GUIStyle(GUI.skin.button) { font = font, fontSize = F(16), wordWrap = false, clipping = TextClipping.Clip,
                    padding = new RectOffset(I(10), I(10), I(3), I(3)) };
                SetAll(Button, Tex(WoodLight, Gold), Parchment);
                Button.hover.background = Tex(WoodHover, Gold);
                Button.active.background = Tex(Gold, Gold);
                Button.active.textColor = Wood;
                Button.onNormal.background = Tex(new Color(0.45f, 0.33f, 0.16f, 1f), Gold);
                Button.onHover.background = Tex(new Color(0.52f, 0.39f, 0.2f, 1f), Gold);
                Button.border = new RectOffset(2, 2, 2, 2);
                Small = new GUIStyle(Button) { fontSize = F(13), padding = new RectOffset(I(8), I(8), I(2), I(2)) };
                Arrow = new GUIStyle(Button) { fontSize = F(15), padding = new RectOffset(0, 0, 0, 0), alignment = TextAnchor.MiddleCenter };

                Slider = new GUIStyle(GUI.skin.horizontalSlider) { fixedHeight = Mathf.Max(4, I(6)) };
                Slider.normal.background = Tex(WoodLight, WoodLight);
                Slider.margin = new RectOffset(I(6), I(8), I(11), I(4));
                Thumb = new GUIStyle(GUI.skin.horizontalSliderThumb) { fixedWidth = I(13), fixedHeight = I(20) };
                Thumb.normal.background = Tex(Gold, Gold);
                Thumb.hover.background = Tex(Parchment, Parchment);
                Thumb.active.background = Tex(Parchment, Parchment);
                Thumb.margin = new RectOffset(0, 0, -I(7), 0);

                Field = new GUIStyle(GUI.skin.textField) { font = font, fontSize = F(16), alignment = TextAnchor.MiddleLeft,
                    padding = new RectOffset(I(8), I(8), I(3), I(3)) };
                SetAll(Field, Tex(Wood, Gold), Parchment);
                Field.focused.background = Tex(new Color(0.18f, 0.12f, 0.07f, 1f), Parchment);
                Number = new GUIStyle(Field) { alignment = TextAnchor.MiddleRight };
                SetAll(Number, null, Gold);

                // The thumb comes from the shared skin; only the track is ours (changing the skin would
                // restyle other mods' windows too).
                Scrollbar = new GUIStyle(GUI.skin.verticalScrollbar) { fixedWidth = I(12) };
                Scrollbar.normal.background = Tex(new Color(0.08f, 0.055f, 0.035f, 1f), new Color(0.08f, 0.055f, 0.035f, 1f));

                Describe = new GUIStyle { border = new RectOffset(2, 2, 2, 2) };
                Describe.normal.background = Tex(new Color(0.08f, 0.055f, 0.035f, 0.98f), new Color(0.45f, 0.35f, 0.18f, 1f));
                DescribeTitle = Text(font, F(16), Gold, FontStyle.Bold);
                DescribeBody = Text(font, F(14), Parchment);
                DescribeBody.wordWrap = true;
                DescribeBody.alignment = TextAnchor.UpperLeft;
                DescribeFooter = Text(font, F(13), Muted);
                DescribeFooter.richText = true;

                Panel = new GUIStyle { padding = new RectOffset(I(16), I(76), I(6), I(8)), margin = new RectOffset(I(8), 0, 0, I(6)) };
                Panel.normal.background = Tex(new Color(0.08f, 0.055f, 0.035f, 1f), new Color(0.45f, 0.35f, 0.18f, 1f));
                PickerLabel = Text(font, F(14), Muted);
                PickerLabel.alignment = TextAnchor.MiddleLeft;
                PickerThumb = new GUIStyle(Thumb) { fixedWidth = I(8), fixedHeight = I(20) };
                PickerThumb.normal.background = Tex(Parchment, Wood);
                SwatchFrame = new GUIStyle(GUI.skin.button);
                SetAll(SwatchFrame, Tex(Wood, Gold), Parchment);

                _hue = Own(new Texture2D(64, 1, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp });
                for (int x = 0; x < 64; x++)
                    _hue.SetPixel(x, 0, Color.HSVToRGB(x / 63f, 1f, 1f));
                _hue.Apply();
                _sat = Gradient(); _val = Gradient(); _alpha = Gradient();
                HueTrack = Track(_hue, I(14));
                SatTrack = Track(_sat, I(14));
                ValTrack = Track(_val, I(14));
                AlphaTrack = Track(_alpha, I(14));

                var hover = new Color(1f, 0.85f, 0.55f, 0.07f);
                RowHover = Tex(hover, hover);
                RowFocus = Tex(new Color(1f, 0.85f, 0.55f, 0.12f), Gold);
                ArrowRight = Arrow24(down: false);
                ArrowDown = Arrow24(down: true);
                Grip = GripTexture();
                Checker = CheckerTexture();
                Cross = CrossTexture();
                _building = null;
            }

            /// <summary>Recolors the S, V and A tracks for the color being edited.</summary>
            public void UpdatePickerTextures(float h, float s, float v, Color c)
            {
                for (int x = 0; x < 64; x++)
                {
                    float t = x / 63f;
                    _sat.SetPixel(x, 0, Color.HSVToRGB(h, t, Mathf.Max(v, 0.25f)));
                    _val.SetPixel(x, 0, Color.HSVToRGB(h, s, t));
                    _alpha.SetPixel(x, 0, new Color(c.r * t + 0.15f * (1f - t), c.g * t + 0.12f * (1f - t), c.b * t + 0.1f * (1f - t), 1f));
                }
                _sat.Apply();
                _val.Apply();
                _alpha.Apply();
            }

            private static Texture2D Gradient() =>
                Own(new Texture2D(64, 1, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp });

            private static GUIStyle Track(Texture2D tex, int height)
            {
                var s = new GUIStyle(GUI.skin.horizontalSlider) { fixedHeight = height, border = new RectOffset(0, 0, 0, 0) };
                s.normal.background = tex;
                s.margin = new RectOffset(4, 4, 4, 4);
                return s;
            }

            private static GUIStyle Text(Font font, int size, Color color, FontStyle style = FontStyle.Normal)
            {
                // One line, clipped: wrapped text would spill over the next row (rows have fixed heights).
                var s = new GUIStyle(GUI.skin.label)
                {
                    font = font, fontSize = size, fontStyle = style, wordWrap = false, clipping = TextClipping.Clip,
                    richText = true,
                };
                s.normal.textColor = color;
                return s;
            }

            private static void SetAll(GUIStyle s, Texture2D bg, Color text)
            {
                foreach (var st in new[] { s.normal, s.hover, s.active, s.focused, s.onNormal, s.onHover, s.onActive, s.onFocused })
                {
                    if (bg != null) st.background = bg;
                    st.textColor = text;
                }
            }

            /// <summary>Solid fill with a 1px border, built at runtime so the mod ships no assets.</summary>
            private static Texture2D Tex(Color fill, Color border)
            {
                const int n = 8;
                var t = new Texture2D(n, n) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point };
                var px = new Color[n * n];
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                        px[y * n + x] = x == 0 || y == 0 || x == n - 1 || y == n - 1 ? border : fill;
                t.SetPixels(px);
                t.Apply();
                return Own(t);
            }

            /// <summary>Gold expand/collapse triangle (the game font has no arrow glyphs).</summary>
            private static Texture2D Arrow24(bool down)
            {
                const int n = 24;
                var t = new Texture2D(n, n) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear };
                var px = new Color[n * n];
                for (int y = 0; y < n; y++)
                {
                    for (int x = 0; x < n; x++)
                    {
                        float along = down ? (n - 1 - y) : x;
                        float across = down ? x : y;
                        float half = (19f - along) * 0.5f;
                        bool inside = along >= 5f && along <= 19f && Mathf.Abs(across - 11.5f) <= half;
                        px[y * n + x] = inside ? Gold : Color.clear;
                    }
                }
                t.SetPixels(px);
                t.Apply();
                return Own(t);
            }

            /// <summary>An X for the close button (drawn, since the game font may not have one).</summary>
            private static Texture2D CrossTexture()
            {
                const int n = 24;
                var t = new Texture2D(n, n) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear };
                var px = new Color[n * n];
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        float d = Mathf.Min(Mathf.Abs(x - y), Mathf.Abs(x - (n - 1 - y)));
                        bool inBox = x >= 3 && x <= n - 4 && y >= 3 && y <= n - 4;
                        px[y * n + x] = new Color(Parchment.r, Parchment.g, Parchment.b, inBox ? Mathf.Clamp01(1.8f - d) : 0f);
                    }
                t.SetPixels(px);
                t.Apply();
                return Own(t);
            }

            /// <summary>Three diagonal lines in the corner, the usual resize grip.</summary>
            private static Texture2D GripTexture()
            {
                const int n = 18;
                var t = new Texture2D(n, n) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear };
                var px = new Color[n * n];
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                    {
                        int d = x - y; // texture y runs bottom-up: the lines sit in the bottom-right corner
                        bool line = d == 4 || d == 9 || d == 14;
                        px[y * n + x] = line ? new Color(Gold.r, Gold.g, Gold.b, 0.7f) : Color.clear;
                    }
                t.SetPixels(px);
                t.Apply();
                return Own(t);
            }

            /// <summary>A small checkerboard, shown behind a translucent color swatch.</summary>
            private static Texture2D CheckerTexture()
            {
                const int n = 8;
                var t = new Texture2D(n, n) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat };
                for (int y = 0; y < n; y++)
                    for (int x = 0; x < n; x++)
                        t.SetPixel(x, y, ((x / 4) + (y / 4)) % 2 == 0 ? new Color(0.6f, 0.6f, 0.6f) : new Color(0.35f, 0.35f, 0.35f));
                t.Apply();
                return Own(t);
            }

            /// <summary>
            /// Valheim's UI typeface for Latin-script languages; it has no Cyrillic, Greek or Asian
            /// glyphs, so those use the system's UI fonts (Windows ships them all).
            /// </summary>
            private static Font FontFor(string language)
            {
                // One font per language for the whole session; the styles are rebuilt more often.
                if (Fonts.TryGetValue(language ?? "", out var cached) && cached != null)
                    return cached;
                var font = PickFont(language);
                Fonts[language ?? ""] = font;
                return font;
            }

            private static Font PickFont(string language)
            {
                switch (language)
                {
                    case "Russian": case "Ukrainian": case "Bulgarian": case "Macedonian": case "Serbian": case "Greek":
                    case "Georgian": case "Chinese": case "Chinese_Trad": case "Japanese": case "Korean": case "Thai": case "Hindi":
                        try
                        {
                            return Font.CreateDynamicFontFromOSFont(new[]
                            {
                                "Segoe UI", "Microsoft YaHei UI", "Microsoft JhengHei UI", "Yu Gothic UI", "Malgun Gothic",
                                "Leelawadee UI", "Nirmala UI", "Sylfaen", "Noto Sans", "DejaVu Sans", "Arial",
                            }, 16);
                        }
                        catch
                        {
                            return GUI.skin.font;
                        }
                }
                var fonts = Resources.FindObjectsOfTypeAll<Font>();
                return fonts.FirstOrDefault(f => f.name == "AveriaSerifLibre-Bold")
                       ?? fonts.FirstOrDefault(f => f.name.StartsWith("AveriaSerifLibre"))
                       ?? GUI.skin.font;
            }
        }

        // ---------- game hooks ----------

        // While the window is open the game must not move, swing, look around or zoom, and the
        // cursor must stay free (Valheim re-locks it every frame).
        [HarmonyPatch(typeof(PlayerController), "TakeInput")]
        private static class BlockControllerInput
        {
            private static void Postfix(ref bool __result)
            {
                if (IsOpen) __result = false;
            }
        }

        [HarmonyPatch(typeof(Player), "TakeInput")]
        private static class BlockPlayerInput
        {
            private static void Postfix(ref bool __result)
            {
                if (IsOpen) __result = false;
            }
        }

        // The wheel scrolls this window; without this, GameCamera also zooms (and build mode
        // rotates the piece). IMGUI reads the wheel from its own events, not ZInput.
        [HarmonyPatch(typeof(ZInput), nameof(ZInput.GetMouseScrollWheel))]
        private static class BlockScrollWheel
        {
            private static void Postfix(ref float __result)
            {
                if (IsOpen) __result = 0f;
            }
        }

        [HarmonyPatch(typeof(GameCamera), nameof(GameCamera.UpdateMouseCapture))]
        private static class FreeCursor
        {
            private static void Postfix()
            {
                if (!IsOpen) return;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        // Tab/I (inventory), M (map) and the controller's Y and map buttons would open those behind
        // the window, where clicks cannot reach them: keep them shut while it is open.
        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Show))]
        private static class NoInventoryBehind
        {
            private static bool Prefix() => !IsOpen;
        }

        [HarmonyPatch(typeof(Minimap), nameof(Minimap.SetMapMode))]
        private static class NoLargeMapBehind
        {
            private static bool Prefix(Minimap.MapMode mode) => !IsOpen || mode != Minimap.MapMode.Large;
        }

        // Esc and the controller's B/Start close this window only, instead of also opening the game menu.
        [HarmonyPatch(typeof(Menu), "Update")]
        private static class SwallowEscape
        {
            private static bool Prefix() => !IsOpen;
        }
    }
}
