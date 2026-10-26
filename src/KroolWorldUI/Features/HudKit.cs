using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KroolWorldUI.Features
{
    /// <summary>
    /// Shared building blocks for the HUD toasts (food reminder, save countdown): the toast look,
    /// game-font text, plain images and quiet synthesized sounds. No assets ship with the mod.
    /// </summary>
    internal static class HudKit
    {
        internal static readonly Color Parchment = new Color(0.92f, 0.87f, 0.78f, 1f);
        internal static readonly Color Gold = new Color(0.87f, 0.70f, 0.36f, 1f);
        internal static readonly Color Muted = new Color(0.72f, 0.66f, 0.56f, 1f);

        // Warning and "ready" colors. The colorblind set (General > Colorblind Friendly) swaps the
        // orange/green pair, which deuteranopes and protanopes confuse with the gold, for
        // Okabe-Ito vermillion and sky blue.
        private static readonly Color WarnNormal = new Color(1f, 0.69f, 0.25f, 1f);
        private static readonly Color ReadyNormal = new Color(0.62f, 0.86f, 0.45f, 1f);
        private static readonly Color BadNormal = new Color(0.95f, 0.3f, 0.22f, 1f);
        private static readonly Color WarnSafe = new Color(0.94f, 0.47f, 0.26f, 1f);
        private static readonly Color ReadySafe = new Color(0.45f, 0.75f, 0.98f, 1f);
        private static readonly Color BadSafe = new Color(0.98f, 0.38f, 0.75f, 1f);

        internal static Color Warn => General.Colorblind ? WarnSafe : WarnNormal;
        internal static Color Ready => General.Colorblind ? ReadySafe : ReadyNormal;
        internal static Color Bad => General.Colorblind ? BadSafe : BadNormal;

        internal static string Hex(Color c) => ColorUtility.ToHtmlStringRGB(c);

        /// <summary>
        /// Whether a feature's element may draw at all: off while General > Hide Key has hidden
        /// the mod's HUD, and while the game's own HUD is hidden (Ctrl+F3 or the photo mode).
        /// </summary>
        internal static bool Allowed => !General.HiddenByKey && !Hud.IsUserHidden();

        /// <summary>Width that fits <paramref name="text"/> at the text's current size, plus padding, at least min.</summary>
        internal static float FitWidth(TMP_Text text, string sample, float min, float pad) =>
            Mathf.Max(min, Mathf.Ceil(text.GetPreferredValues(sample).x) + pad);

        /// <summary>
        /// "This feature broke; stop trying" for one HUD. A HUD is built per session (it is rebuilt
        /// after each logout), so a feature that failed once gets a fresh try next session instead
        /// of staying off until the game restarts.
        /// </summary>
        internal sealed class FailFlag
        {
            private bool _set;
            private object _hud; // object, not Hud: SettingsDoc loads these types outside the game

            public bool IsSet
            {
                get
                {
                    if (_set && !ReferenceEquals(_hud, Hud.instance))
                        _set = false;
                    return _set;
                }
            }

            public void Set()
            {
                _set = true;
                _hud = Hud.instance;
            }
        }

        /// <summary>Destroys a runtime texture or material; Unity does not free them with their objects.</summary>
        internal static void Free(Object o)
        {
            if (o != null)
                Object.Destroy(o);
        }

        /// <summary>Dark translucent panel with a thin gold frame, stretched over its parent.</summary>
        internal static Image ToastBackground(Transform parent)
        {
            var background = NewImage("bg", parent, new Color(0.05f, 0.035f, 0.025f, 0.8f));
            Stretch(background.rectTransform);
            var frame = background.gameObject.AddComponent<Outline>();
            frame.effectColor = new Color(Gold.r, Gold.g, Gold.b, 0.45f);
            frame.effectDistance = new Vector2(1f, -1f);
            return background;
        }

        /// <summary>
        /// A text cloned from a HUD text (Hud.m_healthText), so it uses the game's font and
        /// material, stretched over its parent.
        /// </summary>
        internal static TMP_Text NewText(TMP_Text template, Transform parent, string name,
            TextAlignmentOptions align, Color color, float size = 18f)
        {
            var textGo = Object.Instantiate(template.gameObject, parent, false);
            textGo.name = name;
            textGo.SetActive(true);
            // The source text may be rotated or scaled in its own panel (the HUD health text is
            // turned 90 degrees); the copy must sit flat.
            textGo.transform.localRotation = Quaternion.identity;
            textGo.transform.localScale = Vector3.one;
            for (int i = textGo.transform.childCount - 1; i >= 0; i--)
                Object.Destroy(textGo.transform.GetChild(i).gameObject);
            foreach (var c in textGo.GetComponents<Component>())
                if (!(c is Transform) && !(c is TMP_Text) && !(c is CanvasRenderer))
                    Object.Destroy(c);
            var text = textGo.GetComponent<TMP_Text>();
            text.enableAutoSizing = false;
            text.fontSize = size;
            text.color = color;
            text.alignment = align;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            text.richText = true;
            text.raycastTarget = false;
            var rt = text.rectTransform;
            rt.pivot = new Vector2(0.5f, 0.5f);
            Stretch(rt);
            return text;
        }

        internal static Image NewImage(string name, Transform parent, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        internal static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        /// <summary>A 2D audio source on the game's UI mixer, so it follows the game's volume.</summary>
        internal static AudioSource NewAudio(GameObject go)
        {
            var audio = go.AddComponent<AudioSource>();
            audio.playOnAwake = false;
            audio.spatialBlend = 0f;
            audio.ignoreListenerPause = true;
            if (AudioMan.instance != null && AudioMan.instance.m_guiMixer != null)
                audio.outputAudioMixerGroup = AudioMan.instance.m_guiMixer;
            return audio;
        }

        /// <summary>One note for <see cref="Tone"/>: frequency in Hz, start and length in seconds, decay rate.</summary>
        internal struct Note
        {
            public float Freq, Start, Duration, Decay;
            public Note(float freq, float start, float duration, float decay = 9f)
            {
                Freq = freq; Start = start; Duration = duration; Decay = decay;
            }
        }

        /// <summary>
        /// Soft bell-like notes (sine plus a touch of the octave, quick attack, exponential decay),
        /// normalized to 0.8 peak. Generated at runtime.
        /// </summary>
        internal static AudioClip Tone(string name, float length, params Note[] notes)
        {
            const int rate = 44100;
            int n = (int)(rate * length);
            var data = new float[n];
            foreach (var note in notes)
            {
                int s0 = (int)(note.Start * rate);
                int len = (int)(note.Duration * rate);
                for (int i = 0; i < len && s0 + i < n; i++)
                {
                    float t = i / (float)rate;
                    float attack = Mathf.Clamp01(t / 0.008f);
                    float decay = Mathf.Exp(-t * note.Decay);
                    float wave = Mathf.Sin(2f * Mathf.PI * note.Freq * t) + 0.25f * Mathf.Sin(4f * Mathf.PI * note.Freq * t);
                    data[s0 + i] += wave * attack * decay;
                }
            }
            float peak = 0f;
            foreach (var s in data)
                peak = Mathf.Max(peak, Mathf.Abs(s));
            if (peak > 0f)
                for (int i = 0; i < n; i++)
                    data[i] = data[i] / peak * 0.8f;
            var clip = AudioClip.Create(name, n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
