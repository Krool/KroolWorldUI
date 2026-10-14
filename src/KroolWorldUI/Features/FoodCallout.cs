using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KroolWorldUI.Features
{
    /// <summary>
    /// A short toast (and an optional soft chime) when one of your foods drops under a few minutes,
    /// i.e. it can be eaten again to refresh it. Fires once per food per run-down. Read-only.
    /// </summary>
    internal static class FoodCallout
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> ThresholdMinutes;
        internal static ConfigEntry<float> ShowSeconds;
        internal static ConfigEntry<bool> Sound;
        internal static ConfigEntry<float> SoundVolume;
        internal static ConfigEntry<float> AnchorX;
        internal static ConfigEntry<float> AnchorY;

        private const string Section = "Food Reminder";

        internal static void BindConfig(ConfigFile config)
        {
            Enabled = config.Bind(Section, "Enabled", true,
                "Show a reminder when a food is about to run out and can be eaten again.");
            ThresholdMinutes = config.Bind(Section, "ThresholdMinutes", 4f,
                new ConfigDescription("Remind when a food has less than this many minutes left.",
                    new AcceptableValueRange<float>(1f, 15f)));
            ShowSeconds = config.Bind(Section, "ShowSeconds", 6f,
                new ConfigDescription("How long the reminder stays on screen.", new AcceptableValueRange<float>(2f, 20f)));
            Sound = config.Bind(Section, "Sound", true, "Play a soft chime with the reminder.");
            SoundVolume = config.Bind(Section, "SoundVolume", 0.35f,
                new ConfigDescription("Chime volume (also follows the game's sound settings).",
                    new AcceptableValueRange<float>(0.05f, 1f)));
            AnchorX = config.Bind(Section, "AnchorX", 0.5f,
                new ConfigDescription("Horizontal position of the reminder's center (0 = left, 1 = right).",
                    new AcceptableValueRange<float>(0f, 1f)));
            AnchorY = config.Bind(Section, "AnchorY", 0.84f,
                new ConfigDescription("Vertical position of the reminder's top edge (0 = bottom, 1 = top). " +
                                      "It moves down on its own to clear the raid banner.",
                    new AcceptableValueRange<float>(0f, 1f)));
        }

        [HarmonyPatch(typeof(Hud), "Update")]
        private static class EnsureOnHud
        {
            private static void Postfix()
            {
                if (Enabled.Value)
                    FoodCalloutHud.Ensure();
            }
        }
    }

    internal class FoodCalloutHud : MonoBehaviour
    {
        private const float RowHeight = 30f;
        private const float Pad = 6f;
        private const float Width = 330f;
        private const float FadeSeconds = 0.6f;
        private const float LoginGraceSeconds = 10f;

        private static readonly Color Parchment = new Color(0.92f, 0.87f, 0.78f, 1f);
        private static readonly Color Gold = new Color(0.87f, 0.70f, 0.36f, 1f);
        private static readonly Color Warn = new Color(1f, 0.69f, 0.25f, 1f);

        private static FoodCalloutHud _instance;
        private static bool _failed;
        private static AudioClip _chime;

        private RectTransform _rect;
        private CanvasGroup _group;
        private Image _background;
        private AudioSource _audio;
        private TMP_Text _textTemplate;
        private float _createdAt;
        private float _shownAt = -999f;
        private bool _previewing;

        private class Row
        {
            public string Key;
            public GameObject Root;
            public Image Icon;
            public TMP_Text Text;
        }

        private readonly List<Row> _rows = new List<Row>();
        // Per food (by item name): true once seen above the threshold, so the next drop below it fires.
        private readonly Dictionary<string, bool> _armed = new Dictionary<string, bool>();
        private readonly HashSet<string> _seen = new HashSet<string>();
        private readonly HashSet<string> _present = new HashSet<string>();
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
                Create(hud);
            }
            catch (System.Exception e)
            {
                _failed = true;
                Plugin.Log.LogError($"Food reminder disabled for this session: {e}");
            }
        }

        private static void Create(Hud hud)
        {
            var go = new GameObject("KroolWorldUI_FoodReminder", typeof(RectTransform), typeof(CanvasGroup));
            go.transform.SetParent(hud.m_rootObject.transform, false);
            var f = go.AddComponent<FoodCalloutHud>();
            _instance = f;
            f._rect = (RectTransform)go.transform;
            f._rect.pivot = new Vector2(0.5f, 1f);
            f._rect.sizeDelta = new Vector2(Width, RowHeight + Pad * 2f);
            f._group = go.GetComponent<CanvasGroup>();
            f._group.alpha = 0f;
            f._group.blocksRaycasts = false;
            f._group.interactable = false;

            f._background = NewImage("bg", go.transform, new Color(0.05f, 0.035f, 0.025f, 0.8f));
            Stretch(f._background.rectTransform);
            var frame = f._background.gameObject.AddComponent<Outline>();
            frame.effectColor = new Color(Gold.r, Gold.g, Gold.b, 0.45f);
            frame.effectDistance = new Vector2(1f, -1f);

            // HUD text as the template, so rows use the game's font and material.
            f._textTemplate = hud.m_healthText;

            f._audio = go.AddComponent<AudioSource>();
            f._audio.playOnAwake = false;
            f._audio.spatialBlend = 0f;
            f._audio.ignoreListenerPause = true;
            if (AudioMan.instance != null && AudioMan.instance.m_guiMixer != null)
                f._audio.outputAudioMixerGroup = AudioMan.instance.m_guiMixer; // follows the game's UI volume

            f._createdAt = Time.time;
        }

        private void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
        }

        /// <summary>Settings-window preview: show the reminder with your current foods (or a sample).</summary>
        internal static void Preview(bool on, bool playSound)
        {
            var f = _instance;
            if (f == null || _failed)
                return;
            f._previewing = on;
            if (!on)
            {
                f._shownAt = -999f; // hide now instead of holding for ShowSeconds
                f.ClearRows();
                return;
            }
            var player = Player.m_localPlayer;
            var foods = player != null ? player.GetFoods() : null;
            var keys = new HashSet<string>();
            if (foods != null)
                foreach (var food in foods)
                    if (food?.m_item?.m_shared != null)
                    {
                        f.AddOrUpdateRow(food);
                        keys.Add(food.m_item.m_shared.m_name);
                    }
            if (keys.Count == 0)
                f.AddSampleRow();
            f._shownAt = Time.time;
            if (playSound)
                f.PlayChime();
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
                Plugin.Log.LogError($"Food reminder disabled for this session after an error: {e}");
                Destroy(gameObject);
            }
        }

        private void Tick()
        {
            var player = Player.m_localPlayer;
            if (!FoodCallout.Enabled.Value || player == null)
            {
                _group.alpha = 0f;
                return;
            }

            float threshold = FoodCallout.ThresholdMinutes.Value * 60f;
            bool fired = false;
            var foods = player.GetFoods();
            var present = _present;
            present.Clear();
            foreach (var food in foods)
            {
                var shared = food?.m_item?.m_shared;
                if (shared == null)
                    continue;
                string key = shared.m_name;
                present.Add(key);
                bool low = food.m_time < threshold;
                bool firstSight = _seen.Add(key);

                if (!low)
                {
                    _armed[key] = true; // eaten/refreshed: the next run-down will remind again
                }
                else
                {
                    // Fire on the drop below the threshold, or once at login if already low. A food
                    // that starts out below the threshold when eaten (very short foods) never fires.
                    bool atLogin = firstSight && Time.time - _createdAt < LoginGraceSeconds;
                    if ((_armed.TryGetValue(key, out bool armed) && armed) || atLogin)
                    {
                        _armed[key] = false;
                        AddOrUpdateRow(food);
                        fired = true;
                    }
                }

                // Rows already on screen keep their countdown current.
                var row = _rows.Find(r => r.Key == key);
                if (row != null)
                    row.Text.text = RowText(food);
            }

            // Forget foods that expired, so eating them again later starts fresh.
            if (present.Count < _seen.Count)
            {
                _seen.RemoveWhere(k => !present.Contains(k));
                var gone = new List<string>();
                foreach (var k in _armed.Keys)
                    if (!present.Contains(k))
                        gone.Add(k);
                foreach (var k in gone)
                    _armed.Remove(k);
            }

            if (fired)
            {
                _shownAt = Time.time;
                if (FoodCallout.Sound.Value)
                    PlayChime();
            }

            float age = Time.time - _shownAt;
            float hold = FoodCallout.ShowSeconds.Value;
            bool wanted = _previewing || age <= hold + FadeSeconds;
            if (InventoryGui.IsVisible() || Minimap.IsOpen() || Menu.IsVisible())
                wanted = false;

            float alpha = _previewing ? 1f : age <= hold ? 1f : 1f - (age - hold) / FadeSeconds;
            _group.alpha = wanted ? Mathf.Clamp01(alpha) : 0f;

            if (!_previewing && age > hold + FadeSeconds && _rows.Count > 0)
                ClearRows();

            if (_group.alpha > 0f)
                Place();
        }

        private static string RowText(Player.Food food)
        {
            string name = Localization.instance != null
                ? Localization.instance.Localize(food.m_item.m_shared.m_name)
                : food.m_item.m_shared.m_name;
            int secs = Mathf.Max(0, Mathf.CeilToInt(food.m_time));
            string time = $"{secs / 60}:{secs % 60:00}";
            return $"{name}  <color=#{ColorUtility.ToHtmlStringRGB(Warn)}>{time}</color>  " +
                   $"<color=#{ColorUtility.ToHtmlStringRGB(Gold)}>eat again</color>";
        }

        private void AddOrUpdateRow(Player.Food food)
        {
            string key = food.m_item.m_shared.m_name;
            var row = _rows.Find(r => r.Key == key) ?? NewRow(key);
            if (row.Icon != null)
            {
                row.Icon.sprite = food.m_item.GetIcon();
                row.Icon.enabled = row.Icon.sprite != null;
            }
            row.Text.text = RowText(food);
        }

        private void AddSampleRow()
        {
            var row = _rows.Find(r => r.Key == "_sample") ?? NewRow("_sample");
            if (row.Icon != null)
                row.Icon.enabled = false;
            row.Text.text = $"Your food  <color=#{ColorUtility.ToHtmlStringRGB(Warn)}>3:59</color>  " +
                            $"<color=#{ColorUtility.ToHtmlStringRGB(Gold)}>eat again</color>";
        }

        private Row NewRow(string key)
        {
            var go = new GameObject("row", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.offsetMin = new Vector2(Pad, -Pad - RowHeight * (_rows.Count + 1));
            rt.offsetMax = new Vector2(-Pad, -Pad - RowHeight * _rows.Count);

            var icon = NewImage("icon", go.transform, Color.white);
            icon.preserveAspect = true;
            var irt = icon.rectTransform;
            irt.anchorMin = irt.anchorMax = new Vector2(0f, 0.5f);
            irt.pivot = new Vector2(0f, 0.5f);
            irt.sizeDelta = new Vector2(RowHeight - 4f, RowHeight - 4f);
            irt.anchoredPosition = Vector2.zero;

            var textGo = Instantiate(_textTemplate.gameObject, go.transform, false);
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
            var text = textGo.GetComponent<TMP_Text>();
            text.enableAutoSizing = false;
            text.fontSize = 18f;
            text.color = Parchment;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.raycastTarget = false;
            var trt = text.rectTransform;
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(RowHeight + 2f, 0f);
            trt.offsetMax = Vector2.zero;

            var row = new Row { Key = key, Root = go, Icon = icon, Text = text };
            _rows.Add(row);
            _rect.sizeDelta = new Vector2(Width, RowHeight * _rows.Count + Pad * 2f);
            return row;
        }

        private void ClearRows()
        {
            foreach (var r in _rows)
                if (r.Root != null)
                    Destroy(r.Root);
            _rows.Clear();
            _rect.sizeDelta = new Vector2(Width, RowHeight + Pad * 2f);
        }

        /// <summary>Configured anchor, pushed down below the raid/event banner when it is up.</summary>
        private void Place()
        {
            var anchor = new Vector2(FoodCallout.AnchorX.Value, FoodCallout.AnchorY.Value);
            _rect.anchorMin = anchor;
            _rect.anchorMax = anchor;
            _rect.anchoredPosition = Vector2.zero;

            var hud = Hud.instance;
            if (hud == null || hud.m_eventBar == null || !hud.m_eventBar.activeInHierarchy)
                return;
            ((RectTransform)hud.m_eventBar.transform).GetWorldCorners(_corners);
            float barBottom = _corners[0].y, barLeft = _corners[0].x, barRight = _corners[2].x;
            _rect.GetWorldCorners(_corners);
            float top = _corners[1].y, bottom = _corners[0].y;
            bool overlapX = barLeft < _corners[2].x && barRight > _corners[0].x;
            if (overlapX && barBottom < top && bottom < top)
            {
                float margin = 8f * _rect.lossyScale.y;
                var pos = _rect.position;
                _rect.position = new Vector3(pos.x, pos.y - (top - barBottom) - margin, pos.z);
            }
        }

        private void PlayChime()
        {
            if (_audio == null)
                return;
            if (_chime == null) // explicit: ??= skips Unity's destroyed-object check
                _chime = BuildChime();
            _audio.PlayOneShot(_chime, FoodCallout.SoundVolume.Value);
        }

        /// <summary>
        /// Two soft rising sine notes (E5 then A5) with quick attack and gentle decay, about 0.35 s.
        /// Generated at runtime, so the mod ships no audio files.
        /// </summary>
        private static AudioClip BuildChime()
        {
            const int rate = 44100;
            const float length = 0.42f;
            int n = (int)(rate * length);
            var data = new float[n];
            AddNote(data, rate, 659.25f, 0.00f, 0.30f);
            AddNote(data, rate, 880.00f, 0.11f, 0.31f);
            float peak = 0f;
            foreach (var s in data)
                peak = Mathf.Max(peak, Mathf.Abs(s));
            if (peak > 0f)
                for (int i = 0; i < n; i++)
                    data[i] = data[i] / peak * 0.8f;
            var clip = AudioClip.Create("KroolWorldUI_FoodChime", n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static void AddNote(float[] data, int rate, float freq, float start, float duration)
        {
            int s0 = (int)(start * rate);
            int len = (int)(duration * rate);
            for (int i = 0; i < len && s0 + i < data.Length; i++)
            {
                float t = i / (float)rate;
                float attack = Mathf.Clamp01(t / 0.008f);
                float decay = Mathf.Exp(-t * 9f);
                // A touch of the octave for a bell-like tone instead of a bare beep.
                float wave = Mathf.Sin(2f * Mathf.PI * freq * t) + 0.25f * Mathf.Sin(4f * Mathf.PI * freq * t);
                data[s0 + i] += wave * attack * decay;
            }
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
