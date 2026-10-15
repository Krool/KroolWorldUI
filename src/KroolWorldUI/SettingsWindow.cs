using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using KroolWorldUI.Features;
using UnityEngine;

namespace KroolWorldUI
{
    /// <summary>
    /// Built-in settings window (F7), so the mod needs no ConfigurationManager. It draws every
    /// entry in this plugin's config generically, one collapsible section per config section, so a
    /// new feature only needs its config.Bind calls (see Plugin.BindAll):
    ///   bool = On/Off, ranged number = slider, enum = click to cycle, Color = swatch + hex,
    ///   KeyboardShortcut = press-a-key. A section's "Enabled" bool becomes the header's On/Off.
    /// Hovering a row shows its description, default and range (ConfigText.Tooltip).
    /// While open the cursor is free and the game ignores movement, look and scroll input.
    /// </summary>
    internal class SettingsWindow : MonoBehaviour
    {
        internal static ConfigEntry<KeyboardShortcut> ToggleKey;
        private static ConfigEntry<string> ExpandedSections;
        internal static bool IsOpen { get; private set; }
        /// <summary>"Preview on HUD" is on: features show samples even without real data.</summary>
        internal static bool Previewing { get; private set; }

        private const int WindowId = 0x4B57_5549; // "KWUI"
        private const float RefHeight = 1080f;
        private const float LabelWidth = 180f;
        private const float ControlHeight = 26f;
        private const float TooltipDelay = 0.35f;
        private const float TooltipWidth = 340f;

        private Rect _window = new Rect(80f, 80f, 540f, 700f);
        private Vector2 _scroll;
        private ConfigEntryBase _capturingKey;
        private readonly Dictionary<ConfigEntryBase, string> _colorEdits = new Dictionary<ConfigEntryBase, string>();
        private readonly HashSet<string> _expanded = new HashSet<string>();
        private bool _preview;
        private bool _previewWas;
        private bool _wasSaveOnSet;
        private bool _closeRequested;

        // Hover state. Rects come from the previous repaint (layout rects are only valid then).
        private Rect _listRect;                                             // window space
        private readonly Dictionary<object, Rect> _rowRects = new Dictionary<object, Rect>(); // list content space
        private object _hoverKey, _nextHoverKey;
        private string _hoverTip, _nextHoverTip;
        private float _hoverSince;

        private Styles _styles;

        internal static void BindConfig(ConfigFile config)
        {
            ToggleKey = config.Bind("General", "SettingsKey", new KeyboardShortcut(KeyCode.F7),
                "Key that opens this settings window.");
            ExpandedSections = config.Bind("General", "ExpandedSections", "",
                new ConfigDescription("Settings window sections left open, remembered for next time.", null,
                    ConfigText.Hidden.Tag));
        }

        private void Awake()
        {
            foreach (var name in ExpandedSections.Value.Split('|'))
                if (name.Length > 0) _expanded.Add(name);
        }

        private void Update()
        {
            // While a key is being recorded, the next press is the new binding, not a command.
            if (_capturingKey == null)
            {
                if (ToggleKey.Value.IsDown() && !TextFocusElsewhere())
                    SetOpen(!IsOpen);
                else if (IsOpen && Input.GetKeyDown(KeyCode.Escape))
                    _closeRequested = true; // closed in LateUpdate, after Menu.Update has been skipped
            }
            UpdatePreview();
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
            }
            else if (_previewWas)
            {
                FoodCalloutHud.Preview(false, playSound: false);
            }
            _previewWas = active;
        }

        private void LateUpdate()
        {
            if (_closeRequested)
            {
                _closeRequested = false;
                SetOpen(false);
            }
        }

        private static bool TextFocusElsewhere()
        {
            return Console.IsVisible() || TextInput.IsVisible()
                   || (Chat.instance != null && Chat.instance.HasFocus());
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
            }
            else
            {
                config.Save();
                config.SaveOnConfigSet = _wasSaveOnSet;
                _capturingKey = null;
                _preview = false;
                _colorEdits.Clear();
                _hoverKey = null;
            }
            IsOpen = open;
        }

        private void OnDestroy()
        {
            if (IsOpen)
                SetOpen(false);
        }

        private void OnGUI()
        {
            if (!IsOpen)
                return;
            _styles ??= new Styles();

            float scale = Screen.height / RefHeight;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            // Keep the window on screen (a smaller window or an ultrawide-to-16:9 switch).
            float refWidth = Screen.width / scale;
            _window.height = Mathf.Min(700f, RefHeight - 40f);
            _window.x = Mathf.Clamp(_window.x, 0f, Mathf.Max(0f, refWidth - _window.width));
            _window.y = Mathf.Clamp(_window.y, 0f, RefHeight - _window.height);
            _window = GUI.Window(WindowId, _window, DrawWindow, $"{Plugin.Name} v{Plugin.Version}{Plugin.Flavor}", _styles.Window);

            if (_capturingKey != null && Event.current.type == EventType.KeyDown && Event.current.keyCode != KeyCode.None)
            {
                var key = Event.current.keyCode;
                _capturingKey.BoxedValue = key == KeyCode.Escape ? _capturingKey.DefaultValue : new KeyboardShortcut(key);
                _capturingKey = null;
                Event.current.Use();
            }
        }

        private void DrawWindow(int id)
        {
            var config = Plugin.Instance.Config;
            bool repaint = Event.current.type == EventType.Repaint;
            Vector2 mouse = Event.current.mousePosition; // window space
            bool mouseInList = _listRect.Contains(mouse);
            if (repaint)
            {
                _nextHoverKey = null;
                _nextHoverTip = null;
            }

            var sections = config.Keys
                .GroupBy(k => k.Section)
                .Select(g => (Name: g.Key, Entries: g.Select(d => config[d]).Where(e => !ConfigText.IsHidden(e)).ToList()))
                .Where(s => s.Entries.Count > 0)
                .ToList();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Hover a setting for details.", _styles.Hint);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Expand all", _styles.Small, GUILayout.Width(96f), GUILayout.Height(24f)))
                SetExpanded(sections.Select(s => s.Name), true);
            if (GUILayout.Button("Collapse all", _styles.Small, GUILayout.Width(96f), GUILayout.Height(24f)))
                SetExpanded(sections.Select(s => s.Name), false);
            GUILayout.EndHorizontal();
            GUILayout.Space(6f);

            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true));
            foreach (var (name, entries) in sections)
                DrawSection(name, entries, mouseInList);
            GUILayout.EndScrollView();
            if (repaint)
                _listRect = GUILayoutUtility.GetLastRect();

            GUILayout.Space(8f);
            GUILayout.BeginHorizontal();
            bool canPreview = Player.m_localPlayer != null;
            GUI.enabled = canPreview;
            _preview = GUILayout.Toggle(_preview,
                new GUIContent(canPreview ? "Preview on HUD" : "Preview on HUD (in game only)"),
                _styles.Button, GUILayout.Height(32f));
            GUI.enabled = true;
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Close", _styles.Button, GUILayout.Width(110f), GUILayout.Height(32f)))
                SetOpen(false);
            GUILayout.EndHorizontal();

            if (repaint)
                UpdateTooltip(mouse);

            GUI.DragWindow(new Rect(0f, 0f, 10000f, 28f));
        }

        private void DrawSection(string name, List<ConfigEntryBase> entries, bool mouseInList)
        {
            var enabled = entries.FirstOrDefault(e => e.Definition.Key == "Enabled" && e.SettingType == typeof(bool));
            bool open = _expanded.Contains(name);
            int changed = entries.Count(IsChanged);

            GUILayout.BeginHorizontal(_styles.Header);
            string title = changed > 0 ? $"{name}   <size=13><color=#B3A38C>{changed} changed</color></size>" : name;
            if (GUILayout.Button(title, _styles.HeaderButton, GUILayout.ExpandWidth(true), GUILayout.Height(30f)))
                SetExpanded(new[] { name }, !open);
            if (Event.current.type == EventType.Repaint)
            {
                var r = GUILayoutUtility.GetLastRect();
                GUI.DrawTexture(new Rect(r.x + 8f, r.center.y - 6f, 12f, 12f), open ? _styles.ArrowDown : _styles.ArrowRight);
            }
            if (changed > 0 && GUILayout.Button("Reset all", _styles.Small, GUILayout.Width(76f), GUILayout.Height(24f)))
                ResetAll(entries);
            if (enabled != null)
                OnOff(enabled, 64f);
            GUILayout.EndHorizontal();
            string headerTip = enabled != null
                ? $"{enabled.Description?.Description}\n\nClick the name to {(open ? "hide" : "show")} its settings."
                : $"Click to {(open ? "hide" : "show")} these settings.";
            TrackHover(name, headerTip, mouseInList);

            if (open)
            {
                // A feature that is off still lets you set it up, dimmed.
                var oldColor = GUI.color;
                if (enabled != null && !(bool)enabled.BoxedValue)
                    GUI.color = new Color(1f, 1f, 1f, 0.5f);
                foreach (var entry in entries)
                    if (entry != enabled)
                        DrawEntry(entry, mouseInList);
                GUI.color = oldColor;
                GUILayout.Space(8f);
            }
            GUILayout.Space(4f);
        }

        private void DrawEntry(ConfigEntryBase entry, bool mouseInList)
        {
            if (mouseInList && Event.current.type == EventType.Repaint
                && _rowRects.TryGetValue(entry, out var last) && last.Contains(Event.current.mousePosition))
                GUI.DrawTexture(last, _styles.RowHover);

            var range = entry.Description?.AcceptableValues;
            GUILayout.BeginHorizontal();
            GUILayout.Space(6f);
            GUILayout.Label(ConfigText.Pretty(entry.Definition.Key), _styles.Label, GUILayout.Width(LabelWidth));
            switch (entry.BoxedValue)
            {
                case bool _:
                    GUILayout.FlexibleSpace();
                    OnOff(entry, 80f);
                    break;

                case float f when range is AcceptableValueRange<float> fr:
                {
                    bool whole = fr.MaxValue - fr.MinValue >= 50f;
                    float raw = GUILayout.HorizontalSlider(f, fr.MinValue, fr.MaxValue, _styles.Slider, _styles.Thumb,
                        GUILayout.ExpandWidth(true));
                    GUILayout.Label(f.ToString(whole ? "0" : "0.##"), _styles.Value, GUILayout.Width(56f));
                    // Only write when the user actually moved the slider. Snapping an untouched value
                    // (e.g. 0.985 -> 0.99) would silently change settings just by opening the window.
                    if (raw != f)
                    {
                        float step = whole ? 1f : 0.01f;
                        entry.BoxedValue = Mathf.Clamp(Mathf.Round(raw / step) * step, fr.MinValue, fr.MaxValue);
                    }
                    break;
                }

                case int i when range is AcceptableValueRange<int> ir:
                {
                    int ni = Mathf.RoundToInt(GUILayout.HorizontalSlider(i, ir.MinValue, ir.MaxValue, _styles.Slider,
                        _styles.Thumb, GUILayout.ExpandWidth(true)));
                    GUILayout.Label(ni.ToString(), _styles.Value, GUILayout.Width(56f));
                    if (ni != i) entry.BoxedValue = ni;
                    break;
                }

                case System.Enum e:
                    GUILayout.FlexibleSpace();
                    // Click to cycle through the choices.
                    if (GUILayout.Button(ConfigText.Value(e), _styles.Button, GUILayout.MinWidth(150f), GUILayout.Height(ControlHeight)))
                    {
                        var values = System.Enum.GetValues(e.GetType());
                        int idx = System.Array.IndexOf(values, e);
                        entry.BoxedValue = values.GetValue((idx + 1) % values.Length);
                    }
                    break;

                case Color col:
                {
                    GUILayout.FlexibleSpace();
                    // Swatch, then the hex value to edit (RRGGBBAA; AA is opacity).
                    var swatch = GUILayoutUtility.GetRect(ControlHeight, ControlHeight,
                        GUILayout.Width(ControlHeight), GUILayout.Height(ControlHeight));
                    if (Event.current.type == EventType.Repaint)
                    {
                        var old = GUI.color;
                        GUI.color = new Color(col.r, col.g, col.b, old.a);
                        GUI.DrawTexture(swatch, Texture2D.whiteTexture);
                        GUI.color = old;
                    }
                    // Typed text lives in a buffer, so a half-typed value is not snapped back.
                    if (!_colorEdits.TryGetValue(entry, out var shown))
                        shown = ConfigText.Hex(col);
                    string typed = GUILayout.TextField(shown, 8, _styles.Field, GUILayout.Width(110f), GUILayout.Height(ControlHeight));
                    if (typed != shown)
                    {
                        _colorEdits[entry] = typed;
                        if ((typed.Length == 6 || typed.Length == 8) && ColorUtility.TryParseHtmlString("#" + typed, out var parsed))
                            entry.BoxedValue = parsed;
                    }
                    break;
                }

                case KeyboardShortcut k:
                    GUILayout.FlexibleSpace();
                    string text = _capturingKey == entry ? "Press a key (Esc = default)" : k.ToString();
                    if (GUILayout.Button(text, _styles.Button, GUILayout.MinWidth(150f), GUILayout.Height(ControlHeight)))
                        _capturingKey = entry;
                    break;

                default:
                {
                    string s = entry.GetSerializedValue();
                    string ns = GUILayout.TextField(s, _styles.Field, GUILayout.Height(ControlHeight));
                    if (ns != s)
                    {
                        try { entry.SetSerializedValue(ns); }
                        catch { /* keep typing; invalid text is ignored */ }
                    }
                    break;
                }
            }

            if (IsChanged(entry))
            {
                if (GUILayout.Button("Reset", _styles.Small, GUILayout.Width(52f), GUILayout.Height(24f)))
                    ResetAll(new[] { entry });
            }
            else
            {
                GUILayout.Space(56f);
            }
            GUILayout.EndHorizontal();
            TrackHover(entry, ConfigText.Tooltip(entry), mouseInList);
        }

        private void OnOff(ConfigEntryBase entry, float width)
        {
            bool on = (bool)entry.BoxedValue;
            bool now = GUILayout.Toggle(on, on ? "On" : "Off", _styles.Button, GUILayout.Width(width), GUILayout.Height(24f));
            if (now != on) entry.BoxedValue = now;
        }

        /// <summary>Call right after a row's EndHorizontal: remembers its rect and whether the mouse is on it.</summary>
        private void TrackHover(object key, string tip, bool mouseInList)
        {
            if (Event.current.type != EventType.Repaint)
                return;
            var rect = GUILayoutUtility.GetLastRect();
            _rowRects[key] = rect;
            if (mouseInList && rect.Contains(Event.current.mousePosition))
            {
                _nextHoverKey = key;
                _nextHoverTip = tip;
            }
        }

        /// <summary>Shows the hovered row's tooltip next to the cursor after a short delay, kept inside the window.</summary>
        private void UpdateTooltip(Vector2 mouse)
        {
            if (!Equals(_nextHoverKey, _hoverKey))
            {
                _hoverKey = _nextHoverKey;
                _hoverSince = Time.unscaledTime;
            }
            _hoverTip = _nextHoverTip;
            if (_hoverKey == null || string.IsNullOrEmpty(_hoverTip) || _capturingKey != null
                || GUIUtility.hotControl != 0 || Time.unscaledTime - _hoverSince < TooltipDelay)
                return;

            var content = new GUIContent(_hoverTip);
            float w = Mathf.Min(TooltipWidth, _window.width - 16f);
            float h = _styles.Tooltip.CalcHeight(content, w);
            float x = mouse.x + 16f, y = mouse.y + 22f;
            if (x + w > _window.width - 8f) x = _window.width - 8f - w;
            if (y + h > _window.height - 8f) y = mouse.y - h - 10f;
            GUI.Label(new Rect(Mathf.Max(8f, x), Mathf.Max(8f, y), w, h), content, _styles.Tooltip);
        }

        private void SetExpanded(IEnumerable<string> names, bool open)
        {
            foreach (var name in names)
            {
                if (open) _expanded.Add(name);
                else _expanded.Remove(name);
            }
            ExpandedSections.Value = string.Join("|", _expanded.OrderBy(n => n));
        }

        private void ResetAll(IEnumerable<ConfigEntryBase> entries)
        {
            foreach (var entry in entries)
            {
                entry.BoxedValue = entry.DefaultValue;
                _colorEdits.Remove(entry);
            }
        }

        private static bool IsChanged(ConfigEntryBase entry) => !Equals(entry.BoxedValue, entry.DefaultValue);

        /// <summary>Dark wood and gold, close to Valheim's own menus.</summary>
        private class Styles
        {
            public readonly GUIStyle Window, Header, HeaderButton, Label, Value, Hint, Tooltip, Button, Small, Slider, Thumb, Field;
            public readonly Texture2D RowHover, ArrowRight, ArrowDown;

            private static readonly Color Wood = new Color(0.13f, 0.09f, 0.06f, 0.96f);
            private static readonly Color WoodLight = new Color(0.24f, 0.17f, 0.11f, 1f);
            private static readonly Color WoodHover = new Color(0.33f, 0.24f, 0.15f, 1f);
            private static readonly Color Gold = new Color(0.87f, 0.70f, 0.36f, 1f);
            private static readonly Color Parchment = new Color(0.92f, 0.87f, 0.78f, 1f);
            private static readonly Color Muted = new Color(0.70f, 0.64f, 0.55f, 1f);

            public Styles()
            {
                var font = FindValheimFont();

                Window = new GUIStyle(GUI.skin.window)
                {
                    font = font, fontSize = 22, fontStyle = FontStyle.Bold,
                    padding = new RectOffset(16, 16, 40, 14), border = new RectOffset(4, 4, 4, 4),
                    alignment = TextAnchor.UpperCenter,
                };
                SetAll(Window, Tex(Wood, Gold), Gold);

                Header = new GUIStyle { padding = new RectOffset(0, 6, 3, 3), margin = new RectOffset(0, 0, 2, 2),
                    border = new RectOffset(2, 2, 2, 2) };
                Header.normal.background = Tex(new Color(0.19f, 0.13f, 0.08f, 1f), new Color(0.45f, 0.35f, 0.18f, 1f));
                HeaderButton = Text(font, 19, Gold, FontStyle.Bold);
                HeaderButton.alignment = TextAnchor.MiddleLeft;
                HeaderButton.padding = new RectOffset(28, 4, 0, 0);
                HeaderButton.richText = true;
                HeaderButton.hover.textColor = Parchment;
                HeaderButton.active.textColor = Parchment;

                Label = Text(font, 16, Parchment);
                Label.alignment = TextAnchor.MiddleLeft;
                Label.fixedHeight = ControlHeight;
                Value = Text(font, 16, Gold);
                Value.alignment = TextAnchor.MiddleRight;
                Value.fixedHeight = ControlHeight;
                Hint = Text(font, 14, Muted);
                Hint.alignment = TextAnchor.MiddleLeft;
                Hint.fixedHeight = 24f;

                Tooltip = Text(font, 15, Parchment);
                Tooltip.wordWrap = true;
                Tooltip.padding = new RectOffset(10, 10, 8, 8);
                Tooltip.border = new RectOffset(2, 2, 2, 2);
                Tooltip.normal.background = Tex(new Color(0.08f, 0.055f, 0.035f, 0.98f), Gold);

                Button = new GUIStyle(GUI.skin.button) { font = font, fontSize = 16 };
                SetAll(Button, Tex(WoodLight, Gold), Parchment);
                Button.hover.background = Tex(WoodHover, Gold);
                Button.active.background = Tex(Gold, Gold);
                Button.onNormal.background = Tex(new Color(0.45f, 0.33f, 0.16f, 1f), Gold);
                Button.onHover.background = Tex(new Color(0.52f, 0.39f, 0.2f, 1f), Gold);
                Button.border = new RectOffset(2, 2, 2, 2);

                Small = new GUIStyle(Button) { fontSize = 13 };

                Slider = new GUIStyle(GUI.skin.horizontalSlider);
                Slider.normal.background = Tex(WoodLight, WoodLight);
                Slider.fixedHeight = 6f;
                Slider.margin = new RectOffset(4, 4, 11, 4);
                Thumb = new GUIStyle(GUI.skin.horizontalSliderThumb);
                Thumb.normal.background = Tex(Gold, Gold);
                Thumb.hover.background = Tex(Parchment, Parchment);
                Thumb.active.background = Tex(Parchment, Parchment);
                Thumb.fixedWidth = 12f;
                Thumb.fixedHeight = 18f;
                Thumb.margin = new RectOffset(0, 0, -6, 0);

                Field = new GUIStyle(GUI.skin.textField) { font = font, fontSize = 16, alignment = TextAnchor.MiddleLeft };
                SetAll(Field, Tex(Wood, Gold), Parchment);

                var hover = new Color(1f, 0.85f, 0.55f, 0.07f);
                RowHover = Tex(hover, hover);
                ArrowRight = Arrow(down: false);
                ArrowDown = Arrow(down: true);
            }

            private static GUIStyle Text(Font font, int size, Color color, FontStyle style = FontStyle.Normal)
            {
                var s = new GUIStyle(GUI.skin.label) { font = font, fontSize = size, fontStyle = style };
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
                return t;
            }

            /// <summary>Gold expand/collapse triangle (the game font has no arrow glyphs).</summary>
            private static Texture2D Arrow(bool down)
            {
                const int n = 24;
                var t = new Texture2D(n, n) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear };
                var px = new Color[n * n];
                for (int y = 0; y < n; y++)
                {
                    for (int x = 0; x < n; x++)
                    {
                        // Right-pointing: x from 5 to 19, half-height shrinking toward the tip.
                        // Down-pointing is the same shape transposed (texture y runs bottom-up).
                        float along = down ? (n - 1 - y) : x;
                        float across = down ? x : y;
                        float half = (19f - along) * 0.5f;
                        bool inside = along >= 5f && along <= 19f && Mathf.Abs(across - 11.5f) <= half;
                        px[y * n + x] = inside ? Gold : Color.clear;
                    }
                }
                t.SetPixels(px);
                t.Apply();
                return t;
            }

            private static Font FindValheimFont()
            {
                // Valheim's UI typeface; fall back to Unity's default if a game update renames it.
                var fonts = Resources.FindObjectsOfTypeAll<Font>();
                return fonts.FirstOrDefault(f => f.name == "AveriaSerifLibre-Bold")
                       ?? fonts.FirstOrDefault(f => f.name.StartsWith("AveriaSerifLibre"))
                       ?? GUI.skin.font;
            }
        }

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

        // Esc closes this window only, instead of also opening the game menu.
        [HarmonyPatch(typeof(Menu), "Update")]
        private static class SwallowEscape
        {
            private static bool Prefix() => !IsOpen;
        }
    }
}
