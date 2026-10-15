using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using BepInEx.Configuration;
using HarmonyLib;
using KroolWorldUI.Features;
using UnityEngine;

namespace KroolWorldUI
{
    /// <summary>
    /// Built-in settings window, so the mod needs no ConfigurationManager. It lists every entry in
    /// this plugin's config file by section, so new features show up here with no extra work:
    /// bool = checkbox, ranged number = slider, KeyboardShortcut = press-a-key button.
    /// While open, the cursor is free and the player ignores input.
    /// </summary>
    internal class SettingsWindow : MonoBehaviour
    {
        internal static ConfigEntry<KeyboardShortcut> ToggleKey;
        internal static bool IsOpen { get; private set; }
        /// <summary>"Preview on HUD" is on: features show samples even without real data.</summary>
        internal static bool Previewing { get; private set; }

        private const int WindowId = 0x4B57_5549; // "KWUI"
        private const float RefHeight = 1080f;

        private Rect _window = new Rect(80f, 80f, 520f, 640f);
        private Vector2 _scroll;
        private ConfigEntryBase _capturingKey;
        private readonly Dictionary<ConfigEntryBase, string> _colorEdits = new Dictionary<ConfigEntryBase, string>();
        private bool _preview;
        private bool _previewWas;
        private bool _wasSaveOnSet;
        private bool _closeRequested;

        private Styles _styles;

        internal static void BindConfig(ConfigFile config)
        {
            ToggleKey = config.Bind("General", "SettingsKey", new KeyboardShortcut(KeyCode.F7),
                "Key that opens this settings window.");
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
            var sections = config.Keys.GroupBy(k => k.Section);

            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true));
            foreach (var section in sections)
            {
                GUILayout.Label(section.Key, _styles.Section);
                foreach (var def in section)
                    DrawEntry(config[def]);
                GUILayout.Space(10f);
            }
            GUILayout.EndScrollView();

            GUILayout.Label(string.IsNullOrEmpty(GUI.tooltip) ? " " : GUI.tooltip, _styles.Help, GUILayout.Height(40f));

            GUILayout.BeginHorizontal();
            bool canPreview = Player.m_localPlayer != null;
            GUI.enabled = canPreview;
            _preview = GUILayout.Toggle(_preview, canPreview ? "Preview on HUD" : "Preview on HUD (in game only)",
                _styles.Button, GUILayout.Height(32f));
            GUI.enabled = true;
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Close", _styles.Button, GUILayout.Width(110f), GUILayout.Height(32f)))
                SetOpen(false);
            GUILayout.EndHorizontal();

            GUI.DragWindow(new Rect(0f, 0f, 10000f, 28f));
        }

        private void DrawEntry(ConfigEntryBase entry)
        {
            var label = new GUIContent(Pretty(entry.Definition.Key), entry.Description?.Description ?? "");
            var range = entry.Description?.AcceptableValues;

            GUILayout.BeginHorizontal();
            switch (entry.BoxedValue)
            {
                case bool b:
                    GUILayout.Label(label, _styles.Label, GUILayout.Width(170f));
                    GUILayout.FlexibleSpace();
                    bool nb = GUILayout.Toggle(b, b ? "On" : "Off", _styles.Button, GUILayout.Width(80f), GUILayout.Height(26f));
                    if (nb != b) entry.BoxedValue = nb;
                    break;

                case float f when range is AcceptableValueRange<float> fr:
                    GUILayout.Label(label, _styles.Label, GUILayout.Width(170f));
                    float raw = GUILayout.HorizontalSlider(f, fr.MinValue, fr.MaxValue, _styles.Slider, _styles.Thumb,
                        GUILayout.ExpandWidth(true));
                    GUILayout.Label(f.ToString(fr.MaxValue - fr.MinValue >= 50f ? "0" : "0.###"), _styles.Value, GUILayout.Width(60f));
                    // Only write when the user actually moved the slider. Snapping an untouched value
                    // (e.g. 0.985 -> 0.99) would silently change settings just by opening the window.
                    if (raw != f)
                    {
                        float step = fr.MaxValue - fr.MinValue >= 50f ? 1f : 0.01f;
                        entry.BoxedValue = Mathf.Clamp(Mathf.Round(raw / step) * step, fr.MinValue, fr.MaxValue);
                    }
                    break;

                case int i when range is AcceptableValueRange<int> ir:
                    GUILayout.Label(label, _styles.Label, GUILayout.Width(170f));
                    int ni = Mathf.RoundToInt(GUILayout.HorizontalSlider(i, ir.MinValue, ir.MaxValue, _styles.Slider,
                        _styles.Thumb, GUILayout.ExpandWidth(true)));
                    GUILayout.Label(ni.ToString(), _styles.Value, GUILayout.Width(60f));
                    if (ni != i) entry.BoxedValue = ni;
                    break;

                case System.Enum e:
                    GUILayout.Label(label, _styles.Label, GUILayout.Width(170f));
                    GUILayout.FlexibleSpace();
                    // Click to cycle through the choices.
                    if (GUILayout.Button(Pretty(e.ToString()), _styles.Button, GUILayout.MinWidth(130f), GUILayout.Height(26f)))
                    {
                        var values = System.Enum.GetValues(e.GetType());
                        int idx = System.Array.IndexOf(values, e);
                        entry.BoxedValue = values.GetValue((idx + 1) % values.Length);
                    }
                    break;

                case Color col:
                    GUILayout.Label(label, _styles.Label, GUILayout.Width(170f));
                    GUILayout.FlexibleSpace();
                    // Swatch, then the hex value to edit (RRGGBBAA; AA is opacity).
                    var swatch = GUILayoutUtility.GetRect(26f, 26f, GUILayout.Width(26f), GUILayout.Height(26f));
                    var old = GUI.color;
                    GUI.color = new Color(col.r, col.g, col.b, 1f);
                    GUI.DrawTexture(swatch, Texture2D.whiteTexture);
                    GUI.color = old;
                    // Typed text lives in a buffer, so a half-typed value is not snapped back.
                    string hex = ColorUtility.ToHtmlStringRGBA(col);
                    if (!_colorEdits.TryGetValue(entry, out var shown))
                        shown = hex;
                    string nh = GUILayout.TextField(shown, 8, _styles.Field, GUILayout.Width(110f), GUILayout.Height(26f));
                    if (nh != shown)
                    {
                        _colorEdits[entry] = nh;
                        if ((nh.Length == 6 || nh.Length == 8) && ColorUtility.TryParseHtmlString("#" + nh, out var parsed))
                            entry.BoxedValue = parsed;
                    }
                    break;

                case KeyboardShortcut k:
                    GUILayout.Label(label, _styles.Label, GUILayout.Width(170f));
                    string text = _capturingKey == entry ? "Press a key (Esc = default)" : k.ToString();
                    if (GUILayout.Button(text, _styles.Button))
                        _capturingKey = entry;
                    break;

                default:
                    GUILayout.Label(label, _styles.Label, GUILayout.Width(170f));
                    string s = entry.GetSerializedValue();
                    string ns = GUILayout.TextField(s, _styles.Field);
                    if (ns != s)
                    {
                        try { entry.SetSerializedValue(ns); }
                        catch { /* keep typing; invalid text is ignored */ }
                    }
                    break;
            }

            if (!Equals(entry.BoxedValue, entry.DefaultValue))
            {
                if (GUILayout.Button(new GUIContent("Reset", "Back to the default value"), _styles.Small, GUILayout.Width(52f)))
                {
                    entry.BoxedValue = entry.DefaultValue;
                    _colorEdits.Remove(entry);
                }
            }
            else
            {
                GUILayout.Space(56f);
            }
            GUILayout.EndHorizontal();
        }

        private static string Pretty(string key) => Regex.Replace(key, "(?<=[a-z])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])", " ");

        /// <summary>Dark wood and gold, close to Valheim's own menus.</summary>
        private class Styles
        {
            public readonly GUIStyle Window, Section, Label, Value, Help, Button, Small, Slider, Thumb, Field;

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

                Section = Text(font, 19, Gold, FontStyle.Bold);
                Section.margin = new RectOffset(0, 0, 6, 4);
                Label = Text(font, 16, Parchment);
                Value = Text(font, 16, Gold);
                Value.alignment = TextAnchor.MiddleRight;
                Help = Text(font, 14, Muted);
                Help.wordWrap = true;
                Help.alignment = TextAnchor.UpperLeft;

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
                Slider.margin = new RectOffset(4, 4, 12, 4);
                Thumb = new GUIStyle(GUI.skin.horizontalSliderThumb);
                Thumb.normal.background = Tex(Gold, Gold);
                Thumb.hover.background = Tex(Parchment, Parchment);
                Thumb.active.background = Tex(Parchment, Parchment);
                Thumb.fixedWidth = 12f;
                Thumb.fixedHeight = 18f;
                Thumb.margin = new RectOffset(0, 0, -6, 0);

                Field = new GUIStyle(GUI.skin.textField) { font = font, fontSize = 16 };
                SetAll(Field, Tex(Wood, Gold), Parchment);
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

            private static Font FindValheimFont()
            {
                // Valheim's UI typeface; fall back to Unity's default if a game update renames it.
                var fonts = Resources.FindObjectsOfTypeAll<Font>();
                return fonts.FirstOrDefault(f => f.name == "AveriaSerifLibre-Bold")
                       ?? fonts.FirstOrDefault(f => f.name.StartsWith("AveriaSerifLibre"))
                       ?? GUI.skin.font;
            }
        }

        // While the window is open the game must not move, swing or look around, and the cursor
        // must stay free (Valheim re-locks it every frame).
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
