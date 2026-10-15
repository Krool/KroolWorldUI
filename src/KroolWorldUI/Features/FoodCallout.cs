using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KroolWorldUI.Features
{
    /// <summary>
    /// Food reminders in one small toast near the top of the screen, with an optional soft chime:
    /// a food running low, a food that can be eaten again (past half its time), and a message that
    /// stays up while you have no food until you eat or dismiss it. Each fires once per food per
    /// run-down. Read-only.
    /// </summary>
    internal static class FoodCallout
    {
        internal enum EmptyWhen { NoFood, AnySlotEmpty }

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> RunningOut;
        internal static ConfigEntry<float> ThresholdMinutes;
        internal static ConfigEntry<bool> CanEatAgain;
        internal static ConfigEntry<bool> NoFood;
        internal static ConfigEntry<EmptyWhen> NoFoodWhen;
        internal static ConfigEntry<KeyboardShortcut> DismissKey;
        internal static ConfigEntry<float> ShowSeconds;
        internal static ConfigEntry<bool> Sound;
        internal static ConfigEntry<float> SoundVolume;
        internal static ConfigEntry<float> AnchorX;
        internal static ConfigEntry<float> AnchorY;

        private const string Section = "Food Reminder";

        internal static void BindConfig(ConfigFile config)
        {
            Enabled = config.Bind(Section, "Enabled", true,
                "Food reminders on the HUD. The switches below pick which ones.");
            RunningOut = config.Bind(Section, "RunningOut", true,
                "Remind when a food has less than ThresholdMinutes left.");
            ThresholdMinutes = config.Bind(Section, "ThresholdMinutes", 4f,
                new ConfigDescription("Running-out reminder: minutes left that trigger it.",
                    new AcceptableValueRange<float>(1f, 15f)));
            CanEatAgain = config.Bind(Section, "CanEatAgain", false,
                "Remind when a food can be eaten again (the game allows it once half its time has passed).");
            NoFood = config.Bind(Section, "NoFoodMessage", true,
                "Keep a message up while you have no food, until you eat or press DismissKey. " +
                "Shows on a new character after the arrival, and after respawning.");
            NoFoodWhen = config.Bind(Section, "NoFoodWhen", EmptyWhen.NoFood,
                "NoFood: only when all food is gone. AnySlotEmpty: whenever a food slot is free.");
            DismissKey = config.Bind(Section, "DismissKey", new KeyboardShortcut(KeyCode.Backspace),
                "Hides the no-food message until you next eat.");
            ShowSeconds = config.Bind(Section, "ShowSeconds", 6f,
                new ConfigDescription("How long a reminder stays on screen.", new AcceptableValueRange<float>(2f, 20f)));
            Sound = config.Bind(Section, "Sound", true, "Play a soft chime with each reminder.");
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
        private const float Width = 290f;
        private const float TimeWidth = 92f;
        private const float FadeSeconds = 0.6f;
        private const float LoginGraceSeconds = 10f;
        private const float NoFoodDelaySeconds = 3f; // foods load with the player; do not flash before
        private const string NoFoodKey = "_nofood";

        private static readonly Color Parchment = new Color(0.92f, 0.87f, 0.78f, 1f);
        private static readonly Color Gold = new Color(0.87f, 0.70f, 0.36f, 1f);
        private static readonly Color Warn = new Color(1f, 0.69f, 0.25f, 1f);
        private static readonly Color Ready = new Color(0.62f, 0.86f, 0.45f, 1f);
        private static readonly Color Muted = new Color(0.72f, 0.66f, 0.56f, 1f);

        private static FoodCalloutHud _instance;
        private static bool _failed;
        private static AudioClip _chime;

        private RectTransform _rect;
        private CanvasGroup _group;
        private AudioSource _audio;
        private TMP_Text _textTemplate;
        private Sprite _forkSprite;
        private float _createdAt;
        private bool _previewing;
        private bool _noFoodDismissed;

        private enum Kind { RunningOut, CanEat, NoFood, Sample }

        private class Row
        {
            public string Key;
            public Kind Kind;
            public GameObject Root;
            public RectTransform Rect;
            public CanvasGroup Group;
            public Image Icon;
            public TMP_Text Name;
            public TMP_Text Time; // right-aligned, fixed-width digits, so the countdown holds still
            public float ShownAt;
            public bool Persistent;
        }

        private readonly List<Row> _rows = new List<Row>();
        // Per food (by item name): true once seen above the threshold, so the next drop below it fires.
        private readonly Dictionary<string, bool> _armedLow = new Dictionary<string, bool>();
        // Per food: true once seen while it could not be eaten again, so the next "can eat" fires.
        private readonly Dictionary<string, bool> _armedEat = new Dictionary<string, bool>();
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

            var background = NewImage("bg", go.transform, new Color(0.05f, 0.035f, 0.025f, 0.8f));
            Stretch(background.rectTransform);
            var frame = background.gameObject.AddComponent<Outline>();
            frame.effectColor = new Color(Gold.r, Gold.g, Gold.b, 0.45f);
            frame.effectDistance = new Vector2(1f, -1f);

            // HUD text as the template, so rows use the game's font and material.
            f._textTemplate = hud.m_healthText;
            f._forkSprite = FindForkSprite(hud);

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

        /// <summary>Settings-window preview: one sample row per enabled reminder.</summary>
        internal static void Preview(bool on, bool playSound)
        {
            var f = _instance;
            if (f == null || _failed)
                return;
            f._previewing = on;
            if (!on)
            {
                f.RemoveRows(r => r.Kind == Kind.Sample);
                return;
            }
            if (f._rows.Exists(r => r.Kind == Kind.Sample))
                return;
            if (FoodCallout.RunningOut.Value)
                f.SampleRow("_s1", "Running low", f.TimeText(239f), Warn);
            if (FoodCallout.CanEatAgain.Value)
                f.SampleRow("_s2", "Can eat again", f.TimeText(1199f), Ready);
            if (FoodCallout.NoFood.Value)
                f.SampleRow("_s3", "No food", f.DismissHint(), Muted);
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

            bool fired = false;
            var foods = player.GetFoods();
            CheckFoods(foods, ref fired);
            CheckNoFood(player, foods, ref fired);

            if (fired && FoodCallout.Sound.Value)
                PlayChime();

            // Per-row fade; persistent and preview rows stay at full.
            float hold = FoodCallout.ShowSeconds.Value;
            float now = Time.time;
            bool removed = false;
            for (int i = _rows.Count - 1; i >= 0; i--)
            {
                var row = _rows[i];
                if (row.Persistent || row.Kind == Kind.Sample)
                {
                    row.Group.alpha = 1f;
                    continue;
                }
                float age = now - row.ShownAt;
                if (age > hold + FadeSeconds)
                {
                    Destroy(row.Root);
                    _rows.RemoveAt(i);
                    removed = true;
                    continue;
                }
                row.Group.alpha = age <= hold ? 1f : 1f - (age - hold) / FadeSeconds;
            }
            if (removed)
                Relayout();

            bool wanted = _rows.Count > 0;
            if (InventoryGui.IsVisible() || Minimap.IsOpen() || Menu.IsVisible())
                wanted = false;
            _group.alpha = wanted ? 1f : 0f;
            if (wanted)
                Place();
        }

        private void CheckFoods(List<Player.Food> foods, ref bool fired)
        {
            float threshold = FoodCallout.ThresholdMinutes.Value * 60f;
            bool atLogin = Time.time - _createdAt < LoginGraceSeconds;
            var present = _present;
            present.Clear();
            foreach (var food in foods)
            {
                var shared = food?.m_item?.m_shared;
                if (shared == null)
                    continue;
                string key = shared.m_name;
                present.Add(key);
                bool firstSight = _seen.Add(key);

                // Running low: fire on the drop below the threshold, or once at login if already
                // low. A food that starts out below the threshold (very short foods) never fires.
                bool low = food.m_time < threshold;
                if (!low)
                    _armedLow[key] = true;
                else if (FoodCallout.RunningOut.Value && ((_armedLow.TryGetValue(key, out bool armed) && armed) || (firstSight && atLogin)))
                {
                    _armedLow[key] = false;
                    Show(food, Kind.RunningOut);
                    fired = true;
                }

                // Can eat again: the game's own rule (past half the burn time).
                bool canEat = food.CanEatAgain();
                if (!canEat)
                    _armedEat[key] = true;
                else if (FoodCallout.CanEatAgain.Value && ((_armedEat.TryGetValue(key, out bool armedE) && armedE) || (firstSight && atLogin)))
                {
                    _armedEat[key] = false;
                    // Running low also means it can be eaten; one row per food is enough.
                    if (!_rows.Exists(r => r.Key == key && r.Kind == Kind.RunningOut))
                    {
                        Show(food, Kind.CanEat);
                        fired = true;
                    }
                }

                // Rows on screen keep their countdown current.
                foreach (var row in _rows)
                    if (row.Key == key)
                        row.Time.text = TimeText(food.m_time);
            }

            // Forget foods that expired, so eating them again later starts fresh.
            if (present.Count < _seen.Count)
            {
                _seen.RemoveWhere(k => !present.Contains(k));
                Forget(_armedLow, present);
                Forget(_armedEat, present);
            }
        }

        private static void Forget(Dictionary<string, bool> map, HashSet<string> keep)
        {
            var gone = new List<string>();
            foreach (var k in map.Keys)
                if (!keep.Contains(k))
                    gone.Add(k);
            foreach (var k in gone)
                map.Remove(k);
        }

        /// <summary>
        /// The no-food message: up while the condition holds, not dismissed, and the player is
        /// actually in control (not mid-arrival on a new character, not dead).
        /// </summary>
        private void CheckNoFood(Player player, List<Player.Food> foods, ref bool fired)
        {
            bool empty = FoodCallout.NoFoodWhen.Value == FoodCallout.EmptyWhen.AnySlotEmpty
                ? foods.Count < 3
                : foods.Count == 0;
            if (!empty)
                _noFoodDismissed = false; // ate: the next time you run out, it shows again

            var row = _rows.Find(r => r.Key == NoFoodKey);
            if (row != null && FoodCallout.DismissKey.Value.IsDown() && !TextFocus())
                _noFoodDismissed = true;

            bool want = FoodCallout.NoFood.Value && empty && !_noFoodDismissed
                        && Time.time - _createdAt >= NoFoodDelaySeconds
                        && !player.InIntro() && !player.IsDead();
            if (!want)
            {
                if (row != null)
                    RemoveRows(r => r.Key == NoFoodKey);
                return;
            }

            string label = foods.Count == 0 ? "No food" : $"Food slot empty ({foods.Count}/3)";
            if (row == null)
            {
                row = NewRow(NoFoodKey, Kind.NoFood);
                row.Persistent = true;
                Relayout(); // to the top
                row.Icon.sprite = _forkSprite;
                row.Icon.enabled = _forkSprite != null;
                row.Time.color = Muted;
                fired = true;
            }
            row.Name.text = label;
            row.Time.text = DismissHint();
        }

        private string DismissHint()
        {
            var key = FoodCallout.DismissKey.Value;
            return key.MainKey == KeyCode.None ? "" : $"<size=75%>[{key}] hide</size>";
        }

        private static bool TextFocus()
        {
            return global::Console.IsVisible() || TextInput.IsVisible()
                   || (Chat.instance != null && Chat.instance.HasFocus()) || SettingsWindow.IsOpen;
        }

        private static string FoodName(Player.Food food)
        {
            string raw = food.m_item.m_shared.m_name;
            return Localization.instance != null ? Localization.instance.Localize(raw) : raw;
        }

        // <mspace> gives every digit the same advance, so 3:59 -> 3:11 does not shift.
        private string TimeText(float seconds)
        {
            int secs = Mathf.Max(0, Mathf.CeilToInt(seconds));
            return $"<mspace=0.6em>{secs / 60}:{secs % 60:00}</mspace>";
        }

        private void Show(Player.Food food, Kind kind)
        {
            string key = food.m_item.m_shared.m_name;
            var row = _rows.Find(r => r.Key == key && !r.Persistent);
            if (row == null)
                row = NewRow(key, kind);
            row.Kind = kind;
            row.ShownAt = Time.time;
            row.Icon.sprite = food.m_item.GetIcon();
            row.Icon.enabled = row.Icon.sprite != null;
            string name = FoodName(food);
            if (kind == Kind.CanEat)
                name += $" <size=80%><color=#{ColorUtility.ToHtmlStringRGB(Muted)}>can eat</color></size>";
            row.Name.text = name;
            row.Time.color = kind == Kind.CanEat ? Ready : Warn;
            row.Time.text = TimeText(food.m_time);
        }

        private void SampleRow(string key, string name, string time, Color timeColor)
        {
            var row = NewRow(key, Kind.Sample);
            row.Icon.sprite = _forkSprite;
            row.Icon.enabled = _forkSprite != null;
            row.Name.text = name;
            row.Time.text = time;
            row.Time.color = timeColor;
        }

        private Row NewRow(string key, Kind kind)
        {
            var go = new GameObject("row", typeof(RectTransform), typeof(CanvasGroup));
            go.transform.SetParent(transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);

            var icon = NewImage("icon", go.transform, Color.white);
            icon.preserveAspect = true;
            var irt = icon.rectTransform;
            irt.anchorMin = irt.anchorMax = new Vector2(0f, 0.5f);
            irt.pivot = new Vector2(0f, 0.5f);
            irt.sizeDelta = new Vector2(RowHeight - 4f, RowHeight - 4f);
            irt.anchoredPosition = Vector2.zero;

            var name = NewText(go.transform, "name", TextAlignmentOptions.MidlineLeft, Parchment);
            name.overflowMode = TextOverflowModes.Ellipsis;
            name.rectTransform.offsetMin = new Vector2(RowHeight + 2f, 0f);
            name.rectTransform.offsetMax = new Vector2(-(TimeWidth + 6f), 0f);

            var time = NewText(go.transform, "time", TextAlignmentOptions.MidlineRight, Warn);
            var tr = time.rectTransform;
            tr.anchorMin = new Vector2(1f, 0f);
            tr.anchorMax = new Vector2(1f, 1f);
            tr.pivot = new Vector2(1f, 0.5f);
            tr.sizeDelta = new Vector2(TimeWidth, 0f);
            tr.anchoredPosition = new Vector2(-2f, 0f);

            var row = new Row
            {
                Key = key, Kind = kind, Root = go, Rect = rt, Group = go.GetComponent<CanvasGroup>(),
                Icon = icon, Name = name, Time = time, ShownAt = Time.time,
            };
            _rows.Add(row);
            Relayout();
            return row;
        }

        /// <summary>The no-food message first, then reminders in the order they came.</summary>
        private void Relayout()
        {
            // Stable: List.Sort could shuffle rows that compare equal.
            var ordered = _rows.FindAll(r => r.Persistent);
            ordered.AddRange(_rows.FindAll(r => !r.Persistent));
            _rows.Clear();
            _rows.AddRange(ordered);
            for (int i = 0; i < _rows.Count; i++)
            {
                var rt = _rows[i].Rect;
                rt.offsetMin = new Vector2(Pad, -Pad - RowHeight * (i + 1));
                rt.offsetMax = new Vector2(-Pad, -Pad - RowHeight * i);
            }
            _rect.sizeDelta = new Vector2(Width, RowHeight * Mathf.Max(1, _rows.Count) + Pad * 2f);
        }

        private void RemoveRows(System.Predicate<Row> match)
        {
            for (int i = _rows.Count - 1; i >= 0; i--)
            {
                if (!match(_rows[i]))
                    continue;
                if (_rows[i].Root != null)
                    Destroy(_rows[i].Root);
                _rows.RemoveAt(i);
            }
            Relayout();
        }

        /// <summary>A text cloned from the HUD's own, so it uses the game's font and material.</summary>
        private TMP_Text NewText(Transform parent, string name, TextAlignmentOptions align, Color color)
        {
            var textGo = Instantiate(_textTemplate.gameObject, parent, false);
            textGo.name = name;
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
            text.color = color;
            text.alignment = align;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            text.richText = true;
            text.raycastTarget = false;
            var rt = text.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return text;
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

        /// <summary>The fork icon under the HUD's food column, for the no-food row.</summary>
        private static Sprite FindForkSprite(Hud hud)
        {
            if (hud.m_healthPanel == null)
                return null;
            foreach (Transform child in hud.m_healthPanel)
            {
                var img = child.GetComponent<Image>();
                if (img != null && img.sprite != null && child.name.ToLowerInvariant().StartsWith("foodicon"))
                    return img.sprite;
            }
            return null;
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
