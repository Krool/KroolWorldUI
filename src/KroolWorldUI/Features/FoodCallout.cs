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
        internal enum PadButton { None, LeftStickClick, RightStickClick, Back }

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> RunningOut;
        internal static ConfigEntry<float> ThresholdMinutes;
        internal static ConfigEntry<bool> FinalMinute;
        internal static ConfigEntry<bool> CanEatAgain;
        internal static ConfigEntry<float> ShowSeconds;
        internal static ConfigEntry<bool> NoFood;
        internal static ConfigEntry<EmptyWhen> NoFoodWhen;
        internal static ConfigEntry<KeyboardShortcut> DismissKey;
        internal static ConfigEntry<PadButton> DismissButton;
        internal static ConfigEntry<bool> Sound;
        internal static ConfigEntry<float> SoundVolume;
        internal static ConfigEntry<float> Size;
        internal static ConfigEntry<float> Opacity;
        internal static ConfigEntry<float> AnchorX;
        internal static ConfigEntry<float> AnchorY;

        private const string Section = "Food Reminder";

        internal static void BindConfig(ConfigFile config)
        {
            Enabled = config.Bind(Section, "Enabled", true,
                "Food reminders in a small box at the top of the screen.");

            const string reminders = "Reminders";
            RunningOut = config.Bind(Section, "RunningOut", true,
                ConfigText.Desc("Remind when a food is about to run out.", group: reminders));
            ThresholdMinutes = config.Bind(Section, "ThresholdMinutes", 4f,
                ConfigText.Desc("Remind when a food has this many minutes left.", new AcceptableValueRange<float>(1f, 15f),
                    reminders, () => RunningOut.Value, "Running Out is on", 0.5f));
            FinalMinute = config.Bind(Section, "FinalMinute", false,
                ConfigText.Desc("A second reminder with one minute left.", group: reminders,
                    when: () => RunningOut.Value, whenText: "Running Out is on"));
            CanEatAgain = config.Bind(Section, "CanEatAgain", false,
                ConfigText.Desc("Remind when a food can be eaten again, half way through its time.", group: reminders));
            ShowSeconds = config.Bind(Section, "ShowSeconds", 6f,
                ConfigText.Desc("Seconds a reminder stays on screen.", new AcceptableValueRange<float>(2f, 20f), reminders,
                    () => RunningOut.Value || CanEatAgain.Value, "Running Out or Can Eat Again is on", 0.5f));

            const string none = "No food";
            NoFood = config.Bind(Section, "NoFoodMessage", true,
                ConfigText.Desc("Keep a message up while you have no food, until you eat or dismiss it. " +
                                "Also after you respawn.", group: none));
            NoFoodWhen = config.Bind(Section, "NoFoodWhen", EmptyWhen.NoFood,
                ConfigText.Desc("No Food: when all your food is gone. Any Slot Empty: whenever a food slot is free.",
                    group: none, when: () => NoFood.Value, whenText: "No Food Message is on"));
            DismissKey = config.Bind(Section, "DismissKey", new KeyboardShortcut(KeyCode.Backspace),
                ConfigText.Desc("Hides the no-food message until you next eat.", group: none,
                    when: () => NoFood.Value, whenText: "No Food Message is on"));
            DismissButton = config.Bind(Section, "DismissButton", PadButton.None,
                ConfigText.Desc("Controller button that hides the no-food message. Each also does something in " +
                                "the game (map, run, crouch), so pick the one you use least.", group: none,
                    when: () => NoFood.Value, whenText: "No Food Message is on"));

            const string sound = "Sound";
            Sound = config.Bind(Section, "Sound", true, ConfigText.Desc("A soft chime with each reminder.", group: sound));
            SoundVolume = config.Bind(Section, "SoundVolume", 0.35f,
                ConfigText.Desc("Chime volume, on top of the game's own volume.", new AcceptableValueRange<float>(0.05f, 1f),
                    sound, () => Sound.Value, "Sound is on", 0.05f));

            const string look = "Look";
            Size = config.Bind(Section, "Size", 1f,
                ConfigText.Desc("1 is the normal size.", new AcceptableValueRange<float>(0.75f, 1.5f), look, step: 0.05f));
            Opacity = config.Bind(Section, "Opacity", 1f,
                ConfigText.Desc("1 is solid; lower shows the game through it.", new AcceptableValueRange<float>(0.2f, 1f), look, step: 0.05f));

            const string placement = "Placement";
            AnchorX = config.Bind(Section, "AnchorX", 0.5f,
                ConfigText.Desc("Center of the box. 0 is the left of the screen, 1 the right.",
                    new AcceptableValueRange<float>(0f, 1f), placement, step: 0.01f));
            AnchorY = config.Bind(Section, "AnchorY", 0.84f,
                ConfigText.Desc("Top of the box. 0 is the bottom of the screen, 1 the top. Moves down by itself " +
                                "to clear the raid banner.", new AcceptableValueRange<float>(0f, 1f), placement, step: 0.01f));
        }

        /// <summary>The ZInput name of the dismiss button, or null.</summary>
        internal static string DismissButtonName()
        {
            switch (DismissButton.Value)
            {
                case PadButton.LeftStickClick: return "JoyLStick";
                case PadButton.RightStickClick: return "JoyRStick";
                case PadButton.Back: return "JoyBack";
                default: return null;
            }
        }

        /// <summary>The button as the game writes it in its own hints ("RS").</summary>
        internal static string DismissButtonLabel()
        {
            switch (DismissButton.Value)
            {
                case PadButton.LeftStickClick: return L10n.Game("$button_ls", "LS");
                case PadButton.RightStickClick: return L10n.Game("$button_rs", "RS");
                case PadButton.Back: return L10n.Game("$button_back", "Back");
                default: return null;
            }
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
        private const float Width = 290f;      // narrowest; grows to fit long names
        private const float MaxWidth = 460f;
        private const float TimeWidth = 92f;
        private const float FadeSeconds = 0.6f;
        private const float LoginGraceSeconds = 10f;
        private const float NoFoodDelaySeconds = 3f; // foods load with the player; do not flash before
        private const string NoFoodKey = "_nofood";

        private static Color Parchment => HudKit.Parchment;
        private static Color Warn => HudKit.Warn;
        private static Color Ready => HudKit.Ready;
        private static Color Muted => HudKit.Muted;

        private static FoodCalloutHud _instance;
        private static readonly HudKit.FailFlag _failed = new HudKit.FailFlag();
        private static AudioClip _chime;

        private RectTransform _rect;
        private CanvasGroup _group;
        private AudioSource _audio;
        private TMP_Text _textTemplate;
        private Sprite _forkSprite;
        private float _createdAt;
        private bool _previewing;
        private bool _noFoodDismissed;
        private Player _noFoodPlayer;
        private float _playerSince;

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
        // Per food: true once seen with more than a minute left, for the final-minute reminder.
        private readonly Dictionary<string, bool> _armedFinal = new Dictionary<string, bool>();
        private const float FinalSeconds = 60f;
        private readonly HashSet<string> _seen = new HashSet<string>();
        private readonly HashSet<string> _present = new HashSet<string>();
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
                Create(hud);
            }
            catch (System.Exception e)
            {
                _failed.Set();
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

            HudKit.ToastBackground(go.transform);

            // HUD text as the template, so rows use the game's font and material.
            f._textTemplate = hud.m_healthText;
            f._forkSprite = FindForkSprite(hud);

            f._audio = HudKit.NewAudio(go);

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
            if (f == null || _failed.IsSet)
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
                f.SampleRow("_s1", L10n.T("food_running_low"), f.TimeText(239f), Warn);
            if (FoodCallout.CanEatAgain.Value)
                f.SampleRow("_s2", L10n.T("food_can_eat_again"), f.TimeText(1199f), Ready);
            if (FoodCallout.NoFood.Value)
                f.SampleRow("_s3", L10n.T("food_none"), f.DismissHint(), Muted);
            if (playSound)
                f.PlayChime();
        }

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
                Plugin.Log.LogError($"Food reminder disabled for this session after an error: {e}");
                Destroy(gameObject);
            }
        }

        private void Tick()
        {
            var player = Player.m_localPlayer;
            if (!FoodCallout.Enabled.Value || !HudKit.Allowed || player == null)
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
            _group.alpha = wanted ? FoodCallout.Opacity.Value : 0f;
            if (wanted)
            {
                float size = FoodCallout.Size.Value;
                if (!Mathf.Approximately(_rect.localScale.x, size))
                    _rect.localScale = new Vector3(size, size, 1f);
                Place();
            }
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

                // The last minute, once more (when the first reminder came well before it).
                bool last = food.m_time < FinalSeconds;
                if (!last)
                    _armedFinal[key] = true;
                else if (FoodCallout.RunningOut.Value && FoodCallout.FinalMinute.Value && threshold > FinalSeconds + 15f
                         && _armedFinal.TryGetValue(key, out bool armedF) && armedF)
                {
                    _armedFinal[key] = false;
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
                Forget(_armedFinal, present);
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
            // Ate, or a new body after a respawn: the next time you have no food, it shows again.
            if (player != _noFoodPlayer)
            {
                _noFoodPlayer = player;
                _playerSince = Time.time;
                _noFoodDismissed = false;
            }
            if (!empty)
                _noFoodDismissed = false;

            // Only a message you can see can be dismissed (it hides under the inventory and maps).
            var row = _rows.Find(r => r.Key == NoFoodKey);
            string pad = FoodCallout.DismissButtonName();
            bool dismiss = (FoodCallout.DismissKey.Value.MainKey != KeyCode.None && FoodCallout.DismissKey.Value.IsDown())
                           || (pad != null && ZInput.GetButtonDown(pad));
            if (row != null && _group.alpha > 0f && dismiss && !TextFocus())
                _noFoodDismissed = true;

            bool want = FoodCallout.NoFood.Value && empty && !_noFoodDismissed
                        && Time.time - _playerSince >= NoFoodDelaySeconds
                        && !player.InIntro() && !player.IsDead();
            if (!want)
            {
                if (row != null)
                    RemoveRows(r => r.Key == NoFoodKey);
                return;
            }

            string label = foods.Count == 0 ? L10n.T("food_none") : L10n.T("food_slot_empty", $"{foods.Count}/3");
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
            string hint = DismissHint();
            if (row.Name.text != label || row.Time.text != hint)
            {
                row.Name.text = label;
                row.Time.text = hint;
                Relayout();
            }
        }

        /// <summary>"[Backspace] hide", or the controller button while a controller is in use.</summary>
        private string DismissHint()
        {
            string button;
            if (ZInput.IsGamepadActive())
                button = FoodCallout.DismissButtonLabel();
            else
                button = FoodCallout.DismissKey.Value.MainKey == KeyCode.None ? null : FoodCallout.DismissKey.Value.ToString();
            return string.IsNullOrEmpty(button) ? "" : $"<size=75%>{L10n.T("dismiss_hint", button)}</size>";
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
                name += $" <size=80%><color=#{HudKit.Hex(Muted)}>{L10n.T("food_can_eat")}</color></size>";
            row.Name.text = name;
            row.Time.color = kind == Kind.CanEat ? Ready : Warn;
            row.Time.text = TimeText(food.m_time);
            Relayout();
        }

        private void SampleRow(string key, string name, string time, Color timeColor)
        {
            var row = NewRow(key, Kind.Sample);
            row.Icon.sprite = _forkSprite;
            row.Icon.enabled = _forkSprite != null;
            row.Name.text = name;
            row.Time.text = time;
            row.Time.color = timeColor;
            Relayout(); // sized to the sample text, which NewRow had not set yet
        }

        private Row NewRow(string key, Kind kind)
        {
            var go = new GameObject("row", typeof(RectTransform), typeof(CanvasGroup));
            go.transform.SetParent(transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);

            var icon = HudKit.NewImage("icon", go.transform, Color.white);
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
            // Columns sized to the longest name and time (or hint) shown, so translations fit.
            float timeColumn = TimeWidth, nameColumn = 0f;
            foreach (var row in _rows)
            {
                timeColumn = Mathf.Max(timeColumn, Mathf.Ceil(row.Time.GetPreferredValues(row.Time.text).x) + 4f);
                nameColumn = Mathf.Max(nameColumn, Mathf.Ceil(row.Name.GetPreferredValues(row.Name.text).x));
            }
            float width = Mathf.Clamp(Pad * 2f + RowHeight + 2f + nameColumn + 6f + timeColumn + 2f, Width, MaxWidth);
            for (int i = 0; i < _rows.Count; i++)
            {
                var row = _rows[i];
                var rt = row.Rect;
                rt.offsetMin = new Vector2(Pad, -Pad - RowHeight * (i + 1));
                rt.offsetMax = new Vector2(-Pad, -Pad - RowHeight * i);
                row.Time.rectTransform.sizeDelta = new Vector2(timeColumn, 0f);
                row.Name.rectTransform.offsetMax = new Vector2(-(timeColumn + 6f), 0f);
            }
            _rect.sizeDelta = new Vector2(width, RowHeight * Mathf.Max(1, _rows.Count) + Pad * 2f);
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

        private TMP_Text NewText(Transform parent, string name, TextAlignmentOptions align, Color color) =>
            HudKit.NewText(_textTemplate, parent, name, align, color);
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
            _audio.PlayOneShot(_chime, General.Vol(FoodCallout.SoundVolume.Value));
        }

        /// <summary>Two soft rising notes (E5 then A5), about 0.35 s.</summary>
        private static AudioClip BuildChime() =>
            HudKit.Tone("KroolWorldUI_FoodChime", 0.42f,
                new HudKit.Note(659.25f, 0.00f, 0.30f), new HudKit.Note(880.00f, 0.11f, 0.31f));
    }
}
