using System.Collections;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KroolWorldUI.Features
{
    /// <summary>
    /// Shows a slim skill bar on the HUD whenever the local player gains XP in a skill: progress to
    /// the next level in two shades, darker for where it stood when the bar appeared, lighter for
    /// XP gained since. Each bar fades after a few seconds without XP. Optionally floats a small
    /// colored "+x% Skill" over the player, like the damage numbers. Read-only: never writes skill data.
    /// </summary>
    internal static class SkillXpBars
    {
        internal enum LabelStyle { NameAndLevel, IconOnly }
        internal enum Placement { BelowStamina, RightSide, Custom }
        internal enum FloatMode { Off, EveryGain, LevelUpOnly }

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<LabelStyle> Labels;
        internal static ConfigEntry<bool> ShowGainText;
        internal static ConfigEntry<Placement> Position;
        internal static ConfigEntry<float> BarWidth;
        internal static ConfigEntry<float> BarHeight;
        internal static ConfigEntry<Color> BarColor;
        internal static ConfigEntry<float> HoldSeconds;
        internal static ConfigEntry<float> FadeSeconds;
        internal static ConfigEntry<int> MaxBars;
        internal static ConfigEntry<float> OffsetX;
        internal static ConfigEntry<float> OffsetY;
        internal static ConfigEntry<float> AnchorX;
        internal static ConfigEntry<float> AnchorY;
        internal static ConfigEntry<FloatMode> FloatingText;
        internal static ConfigEntry<Color> FloatingColor;

        private const string Section = "Skill XP Bars";

        internal static void BindConfig(ConfigFile config)
        {
            Enabled = config.Bind(Section, "Enabled", true,
                "Show a skill bar on the HUD while you gain XP in that skill.");
            Labels = config.Bind(Section, "Labels", LabelStyle.NameAndLevel,
                "NameAndLevel: icon, skill name and level on the bar. IconOnly: just the icon and the bar.");
            ShowGainText = config.Bind(Section, "ShowGainText", true,
                "With labels: show the XP gained since the bar appeared, as a percent of a level.");
            Position = config.Bind(Section, "Position", Placement.BelowStamina,
                "BelowStamina: centered under the stamina bar, stacking down. RightSide: right edge of the " +
                "screen, below the minimap. Custom: AnchorX/AnchorY.");
            BarWidth = config.Bind(Section, "BarWidth", 220f,
                new ConfigDescription("Width of one bar in HUD pixels.", new AcceptableValueRange<float>(100f, 500f)));
            BarHeight = config.Bind(Section, "BarHeight", 22f,
                new ConfigDescription("Height of one bar in HUD pixels.", new AcceptableValueRange<float>(12f, 48f)));
            BarColor = config.Bind(Section, "BarColor", new Color(0.86f, 0.66f, 0.28f, 1f),
                "Bar color (hex RRGGBBAA). Progress you had is a darker shade, new XP a lighter one.");
            HoldSeconds = config.Bind(Section, "HoldSeconds", 3f,
                new ConfigDescription("Seconds a bar stays fully visible after the last XP gain.",
                    new AcceptableValueRange<float>(0.5f, 30f)));
            FadeSeconds = config.Bind(Section, "FadeSeconds", 0.6f,
                new ConfigDescription("Seconds the fade-out takes.", new AcceptableValueRange<float>(0f, 5f)));
            MaxBars = config.Bind(Section, "MaxBars", 4,
                new ConfigDescription("Most bars shown at once (fewer if they would not fit on screen). " +
                                      "The stalest bar is reused past this.",
                    new AcceptableValueRange<int>(1, 10)));
            OffsetX = config.Bind(Section, "OffsetX", 0f,
                new ConfigDescription("Nudge left (-) or right (+), in HUD pixels.", new AcceptableValueRange<float>(-400f, 400f)));
            OffsetY = config.Bind(Section, "OffsetY", 0f,
                new ConfigDescription("Nudge down (-) or up (+), in HUD pixels.", new AcceptableValueRange<float>(-400f, 400f)));
            AnchorX = config.Bind(Section, "AnchorX", 0.5f,
                new ConfigDescription("Custom position: horizontal center of the stack (0 = left of screen, 1 = right).",
                    new AcceptableValueRange<float>(0f, 1f)));
            AnchorY = config.Bind(Section, "AnchorY", 0.3f,
                new ConfigDescription("Custom position: top edge of the stack (0 = bottom of screen, 1 = top).",
                    new AcceptableValueRange<float>(0f, 1f)));
            FloatingText = config.Bind(Section, "FloatingText", FloatMode.EveryGain,
                "Float the XP gain over your character like a damage number. LevelUpOnly: just the new level.");
            FloatingColor = config.Bind(Section, "FloatingTextColor", new Color(0.45f, 0.72f, 1f, 1f),
                "Color of the floating XP text.");
        }

        internal static bool IsLocalPlayer(Skills skills)
        {
            var local = Player.m_localPlayer;
            return local != null && skills != null && skills.gameObject == local.gameObject;
        }

        // Looks a skill up without creating it. Skills.GetSkill adds a missing skill to the save,
        // which a display mod must never cause.
        internal static Skills.Skill FindExisting(Skills skills, Skills.SkillType type)
        {
            foreach (var skill in skills.GetSkillList())
            {
                if (skill?.m_info != null && skill.m_info.m_skill == type)
                    return skill;
            }
            return null;
        }

        internal struct Snapshot
        {
            public bool Exists;
            public float Level;
            public float Accumulator;
        }

        // Progress percent from a snapshot, using the game's requirement curve
        // (Skills.Skill.GetNextLevelRequirement is private): floor(level + 1)^1.5 * 0.5 + 0.5.
        internal static float Percent(float level, float accumulator)
        {
            if (level >= 100f)
                return 0f;
            float req = Mathf.Pow(Mathf.Floor(level + 1f), 1.5f) * 0.5f + 0.5f;
            return req > 0f ? Mathf.Clamp01(accumulator / req) : 0f;
        }

        internal static string SkillName(Skills.SkillType type)
        {
            string key = "$skill_" + type.ToString().ToLowerInvariant();
            string name = Localization.instance != null ? Localization.instance.Localize(key) : key;
            // Unknown tokens come back bracketed or unchanged (custom skills without a translation).
            if (string.IsNullOrEmpty(name) || name.StartsWith("[") || name.StartsWith("$"))
                return type.ToString();
            return name;
        }

        [HarmonyPatch(typeof(Skills), nameof(Skills.RaiseSkill))]
        private static class RaiseSkillPatch
        {
            private static void Prefix(Skills __instance, Skills.SkillType __0, out Snapshot __state)
            {
                __state = default;
                if (!IsLocalPlayer(__instance))
                    return;
                var skill = FindExisting(__instance, __0);
                if (skill == null)
                    return;
                __state = new Snapshot { Exists = true, Level = skill.m_level, Accumulator = skill.m_accumulator };
            }

            private static void Postfix(Skills __instance, Skills.SkillType __0, Snapshot __state)
            {
                if (!IsLocalPlayer(__instance))
                    return;
                var skill = FindExisting(__instance, __0);
                if (skill == null)
                    return;
                bool changed = !__state.Exists
                    || skill.m_level != __state.Level
                    || skill.m_accumulator != __state.Accumulator;
                if (!changed)
                    return;

                // Never let a UI problem break the game's skill code.
                if (Enabled.Value)
                {
                    try
                    {
                        var stack = SkillXpBarStack.GetOrCreate();
                        if (stack != null && !SkillXpBarStack.Failed)
                            stack.OnXpGained(__instance, skill, __state);
                    }
                    catch (System.Exception e)
                    {
                        Plugin.Log.LogError($"Skill XP bar failed: {e}");
                    }
                }
                if (FloatingText.Value != FloatMode.Off)
                {
                    try
                    {
                        SkillFloatingText.OnXpGained(skill, __state);
                    }
                    catch (System.Exception e)
                    {
                        Plugin.Log.LogError($"Skill floating text failed: {e}");
                    }
                }
            }
        }
    }

    /// <summary>
    /// Floating "+x% Skill" text through the game's own damage-number system, shown on this
    /// client only (DamageText.ShowText would broadcast it to every player).
    /// </summary>
    internal static class SkillFloatingText
    {
        private const float MinInterval = 1.2f; // per skill: gains in between are summed into the next one

        private static readonly System.Reflection.MethodInfo AddText = AccessTools.Method(typeof(DamageText), "AddInworldText");
        private static readonly System.Reflection.FieldInfo WorldTexts = AccessTools.Field(typeof(DamageText), "m_worldTexts");
        private static bool _broken;

        private class Pending
        {
            public float Gain;
            public float LastShown = -999f;
        }

        private static readonly Dictionary<Skills.SkillType, Pending> _pending = new Dictionary<Skills.SkillType, Pending>();

        internal static void OnXpGained(Skills.Skill skill, SkillXpBars.Snapshot before)
        {
            if (_broken)
                return;
            var type = skill.m_info.m_skill;
            bool leveled = before.Exists && Mathf.Floor(skill.m_level) > Mathf.Floor(before.Level);
            if (SkillXpBars.FloatingText.Value == SkillXpBars.FloatMode.LevelUpOnly)
            {
                if (leveled)
                    Show($"{SkillXpBars.SkillName(type)} {(int)skill.m_level}", big: true);
                return;
            }

            float now = before.Exists ? skill.m_level + skill.GetLevelPercentage() : skill.m_level;
            float was = before.Exists ? before.Level + SkillXpBars.Percent(before.Level, before.Accumulator) : now;
            if (!_pending.TryGetValue(type, out var p))
                _pending[type] = p = new Pending();
            p.Gain += Mathf.Max(0f, now - was);
            if (Time.time - p.LastShown < MinInterval && !leveled)
                return;
            float gain = p.Gain;
            p.Gain = 0f;
            p.LastShown = Time.time;
            if (leveled)
                Show($"{SkillXpBars.SkillName(type)} {(int)skill.m_level}", big: true);
            else if (gain > 0f)
                Show($"+{gain * 100f:0.#}% {SkillXpBars.SkillName(type)}", big: false);
        }

        internal static void Show(string text, bool big)
        {
            var dt = DamageText.instance;
            var player = Player.m_localPlayer;
            var cam = Utils.GetMainCamera();
            if (dt == null || player == null || cam == null || Hud.IsUserHidden())
                return;
            if (AddText == null || WorldTexts == null)
            {
                _broken = true;
                Plugin.Log.LogWarning("DamageText layout changed; skill floating text disabled.");
                return;
            }
            Vector3 pos = player.transform.position + Vector3.up * 2.1f;
            float distance = Vector3.Distance(cam.transform.position, pos);
            var type = big ? DamageText.TextType.Bonus : DamageText.TextType.Normal;
            AddText.Invoke(dt, new object[] { type, pos, distance, text, false });

            // Recolor the entry just added; the game only fades its alpha from here on.
            if (WorldTexts.GetValue(dt) is IList list && list.Count > 0)
            {
                var field = Traverse.Create(list[list.Count - 1]).Field("m_textField").GetValue<TMP_Text>();
                if (field != null)
                    field.color = SkillXpBars.FloatingColor.Value;
            }
        }
    }

    /// <summary>Lives under the HUD root, owns the visible bars, handles fade and layout.</summary>
    internal class SkillXpBarStack : MonoBehaviour
    {
        private const string ObjectName = "KroolWorldUI_SkillXpBars";
        private const float RowGap = 3f;
        private const float BlockerMargin = 12f;
        private const float StaminaMargin = 6f;

        private static readonly Color Parchment = new Color(0.92f, 0.87f, 0.78f, 1f);

        private static SkillXpBarStack _instance;
        private static bool _updateFailed;

        internal static bool Failed => _updateFailed;

        private class Bar
        {
            public Skills.SkillType Type;
            public GameObject Root;
            public RectTransform Rect;
            public CanvasGroup Group;
            public Image Icon;
            public RectTransform Track;
            public RectTransform Base;
            public RectTransform Gain;
            public Image BaseImage;
            public Image GainImage;
            public TMP_Text Name;
            public TMP_Text Level;
            public float LastGain;

            // Session: reset each time the bar appears.
            public float StartLevel;
            public float StartPercent;
            public bool SessionStarted;
            public float ShownLevel;
            public float TargetBase, TargetGain, ShownBase, ShownGain;

            // Layout these were built for; rebuilt when settings change.
            public float Width, Height;
            public SkillXpBars.LabelStyle Labels;
        }

        private readonly List<Bar> _bars = new List<Bar>();
        private RectTransform _rect;
        private CanvasGroup _group;
        private TMP_Text _textTemplate;
        private int _fitBars = int.MaxValue;
        private readonly Vector3[] _corners = new Vector3[4];

        private float Pitch => SkillXpBars.BarHeight.Value + RowGap;
        private int Capacity => Mathf.Max(1, Mathf.Min(SkillXpBars.MaxBars.Value, _fitBars));

        internal static SkillXpBarStack GetOrCreate()
        {
            if (_instance != null)
                return _instance;
            var hud = Hud.instance;
            if (hud == null || hud.m_rootObject == null || hud.m_healthText == null)
                return null;

            var go = new GameObject(ObjectName, typeof(RectTransform), typeof(CanvasGroup));
            go.transform.SetParent(hud.m_rootObject.transform, false);
            _instance = go.AddComponent<SkillXpBarStack>();
            _instance._rect = (RectTransform)go.transform;
            _instance._rect.sizeDelta = Vector2.zero;
            _instance._group = go.GetComponent<CanvasGroup>();
            _instance._group.blocksRaycasts = false;
            _instance._group.interactable = false;
            _instance._textTemplate = hud.m_healthText;
            return _instance;
        }

        private void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
        }

        private float TargetY(int index) => -index * Pitch;

        internal void OnXpGained(Skills skills, Skills.Skill skill, SkillXpBars.Snapshot before)
        {
            var bar = GetOrAddBar(skill.m_info.m_skill);
            if (bar == null)
                return;
            if (!bar.SessionStarted)
            {
                // Start from the progress before this XP tick, so the very first gain shows too.
                if (before.Exists)
                    StartSession(bar, before.Level, SkillXpBars.Percent(before.Level, before.Accumulator));
                else
                    StartSession(bar, skill.m_level, 0f);
            }
            Touch(bar, skills, skill);
        }

        /// <summary>
        /// Settings-window preview: keeps a bar up for each of the player's top skills, with a
        /// made-up gain, so position and size can be tuned live.
        /// </summary>
        internal void Preview(Skills skills)
        {
            var list = new List<Skills.Skill>(skills.GetSkillList());
            list.RemoveAll(s => s?.m_info == null);
            list.Sort((a, b) => b.m_level.CompareTo(a.m_level));
            int count = Mathf.Min(Capacity, list.Count);
            for (int i = 0; i < count; i++)
            {
                var skill = list[i];
                var bar = GetOrAddBar(skill.m_info.m_skill);
                if (bar == null)
                    return;
                if (!bar.SessionStarted)
                    StartSession(bar, skill.m_level, Mathf.Max(0f, skill.GetLevelPercentage() - 0.2f));
                Touch(bar, skills, skill);
            }
        }

        private Bar GetOrAddBar(Skills.SkillType type)
        {
            var bar = _bars.Find(b => b.Type == type);
            if (bar != null)
                return bar;
            Place(); // refresh how many fit before deciding to reuse
            if (_bars.Count >= Capacity)
            {
                // Reuse the bar that has gone longest without XP, as a fresh session.
                bar = _bars[0];
                foreach (var b in _bars)
                    if (b.LastGain < bar.LastGain)
                        bar = b;
                bar.Type = type;
                bar.SessionStarted = false;
                return bar;
            }
            bar = CreateBar(type);
            _bars.Add(bar);
            LayoutBar(bar);
            bar.Rect.anchoredPosition = new Vector2(0f, TargetY(_bars.Count - 1));
            return bar;
        }

        private static void StartSession(Bar bar, float level, float percent)
        {
            bar.SessionStarted = true;
            bar.StartLevel = level;
            bar.ShownLevel = level;
            bar.StartPercent = percent;
            bar.ShownBase = percent;
            bar.ShownGain = 0f;
        }

        private void Touch(Bar bar, Skills skills, Skills.Skill skill)
        {
            UpdateContents(bar, skills, skill);
            bar.LastGain = Time.time;
            bar.Group.alpha = 1f;
        }

        private void Update()
        {
            if (_bars.Count == 0 || _updateFailed)
                return; // idle: no per-frame work
            try
            {
                Tick();
            }
            catch (System.Exception e)
            {
                // One error, logged once; a broken bar must not spam the log every frame.
                _updateFailed = true;
                Plugin.Log.LogError($"Skill XP bars disabled for this session after an error: {e}");
                foreach (var bar in _bars)
                    if (bar.Root != null)
                        Destroy(bar.Root);
                _bars.Clear();
            }
        }

        private void Tick()
        {
            bool blocked = InventoryGui.IsVisible() || Minimap.IsOpen() || Menu.IsVisible();
            _group.alpha = blocked ? 0f : 1f;

            float hold = SkillXpBars.HoldSeconds.Value;
            float fade = SkillXpBars.FadeSeconds.Value;
            float now = Time.time;

            for (int i = _bars.Count - 1; i >= 0; i--)
            {
                var bar = _bars[i];
                float age = now - bar.LastGain;
                float alpha = age <= hold ? 1f : (fade <= 0f ? 0f : 1f - (age - hold) / fade);
                if (alpha <= 0f || bar.Root == null || i >= Capacity)
                {
                    if (bar.Root != null)
                        Destroy(bar.Root);
                    _bars.RemoveAt(i);
                    continue;
                }
                bar.Group.alpha = alpha;
            }

            float t = 1f - Mathf.Exp(-12f * Time.deltaTime);
            for (int i = 0; i < _bars.Count; i++)
            {
                var bar = _bars[i];
                LayoutBar(bar);
                // Slide remaining bars up into gaps left by faded ones.
                var pos = bar.Rect.anchoredPosition;
                pos.y = Mathf.Lerp(pos.y, TargetY(i), t);
                pos.x = 0f;
                bar.Rect.anchoredPosition = pos;

                bar.ShownBase = Mathf.Lerp(bar.ShownBase, bar.TargetBase, t);
                bar.ShownGain = Mathf.Lerp(bar.ShownGain, bar.TargetGain, t);
                ApplyFill(bar);
            }

            Place();
        }

        // ---------- placement ----------

        /// <summary>
        /// Puts the stack where Position says, and works out how many rows fit below that point.
        /// </summary>
        private void Place()
        {
            var hud = Hud.instance;
            float width = SkillXpBars.BarWidth.Value;
            float pitch = Pitch;
            var root = (RectTransform)_rect.parent;
            float unit = Mathf.Max(0.0001f, root.lossyScale.y); // world units per HUD pixel
            var offset = new Vector3(SkillXpBars.OffsetX.Value, SkillXpBars.OffsetY.Value, 0f) * unit;
            _rect.sizeDelta = new Vector2(width, pitch * SkillXpBars.MaxBars.Value);

            var mode = SkillXpBars.Position.Value;
            if (mode == SkillXpBars.Placement.BelowStamina && hud != null && hud.m_staminaBar2Root != null)
            {
                _rect.anchorMin = _rect.anchorMax = new Vector2(0.5f, 0.5f);
                _rect.pivot = new Vector2(0.5f, 1f);
                var stamina = hud.m_staminaBar2Root;
                float bottom = LowestBottom(stamina);
                if (hud.m_adrenalineBarRoot != null)
                    bottom = Mathf.Min(bottom, LowestBottom(hud.m_adrenalineBarRoot));
                float top = bottom - StaminaMargin;
                _rect.position = new Vector3(stamina.position.x, stamina.position.y + top * unit, _rect.position.z) + offset;
            }
            else if (mode == SkillXpBars.Placement.Custom)
            {
                var anchor = new Vector2(SkillXpBars.AnchorX.Value, SkillXpBars.AnchorY.Value);
                _rect.anchorMin = _rect.anchorMax = anchor;
                _rect.pivot = new Vector2(0.5f, 1f);
                _rect.anchoredPosition = new Vector2(SkillXpBars.OffsetX.Value, SkillXpBars.OffsetY.Value);
            }
            else
            {
                var anchor = new Vector2(0.985f, 0.6f);
                _rect.anchorMin = _rect.anchorMax = anchor;
                _rect.pivot = new Vector2(1f, 1f);
                _rect.anchoredPosition = new Vector2(SkillXpBars.OffsetX.Value, SkillXpBars.OffsetY.Value);
                AvoidBlockers(width * unit, pitch * SkillXpBars.MaxBars.Value * unit, unit);
            }

            // Rows hang from the stack's top, aligned with its pivot.
            foreach (var bar in _bars)
            {
                bar.Rect.anchorMin = bar.Rect.anchorMax = new Vector2(_rect.pivot.x, 1f);
                bar.Rect.pivot = new Vector2(_rect.pivot.x, 1f);
            }

            // How many rows fit between the stack's top and the bottom of the screen.
            root.GetWorldCorners(_corners);
            float screenBottom = _corners[0].y + 4f * unit;
            float room = (_rect.position.y - screenBottom) / (pitch * unit);
            _fitBars = Mathf.Max(1, Mathf.FloorToInt(room + RowGap / pitch));
        }

        /// <summary>
        /// Lowest edge of a stamina-style panel's bars, in HUD pixels relative to the panel's pivot.
        /// Read from the layout rather than the drawn size, because the panel's show/hide animation
        /// would otherwise move the skill bars every time stamina fills up.
        /// </summary>
        private static float LowestBottom(RectTransform panel)
        {
            float lowest = panel.rect.yMin;
            foreach (Transform child in panel)
            {
                if (child is RectTransform rt)
                    lowest = Mathf.Min(lowest, rt.localPosition.y + rt.rect.yMin);
            }
            return lowest;
        }

        /// <summary>RightSide: push the stack down below the minimap and status effects.</summary>
        private void AvoidBlockers(float width, float height, float unit)
        {
            Vector3 top = _rect.position;
            float margin = BlockerMargin * unit;
            float xMin = top.x - width, xMax = top.x;
            float yTop = top.y;
            for (int pass = 0; pass < 3; pass++)
            {
                bool moved = false;
                foreach (var blocker in Blockers())
                {
                    blocker.GetWorldCorners(_corners);
                    float bxMin = _corners[0].x, bxMax = _corners[2].x;
                    float byMin = _corners[0].y, byMax = _corners[2].y;
                    bool overlapX = bxMin < xMax && bxMax > xMin;
                    bool overlapY = byMin < yTop && byMax > yTop - height;
                    if (overlapX && overlapY && byMin - margin < yTop)
                    {
                        yTop = byMin - margin;
                        moved = true;
                    }
                }
                if (!moved)
                    break;
            }
            if (yTop != top.y)
                _rect.position = new Vector3(top.x, yTop, top.z);
        }

        private IEnumerable<RectTransform> Blockers()
        {
            var map = Minimap.instance;
            if (map != null && map.m_smallRoot != null && map.m_smallRoot.activeInHierarchy)
                yield return (RectTransform)map.m_smallRoot.transform;

            var hud = Hud.instance;
            if (hud != null && hud.m_statusEffectListRoot != null)
            {
                foreach (Transform child in hud.m_statusEffectListRoot)
                    if (child.gameObject.activeInHierarchy && child is RectTransform rt)
                        yield return rt;
            }
        }

        // ---------- one bar ----------

        /// <summary>
        /// A row: dark panel, skill icon on the left, and a bar filling the rest, with the skill
        /// name on the left of the bar and the level on the right (unless IconOnly).
        /// </summary>
        private Bar CreateBar(Skills.SkillType type)
        {
            var go = new GameObject("SkillXpBar", typeof(RectTransform), typeof(CanvasGroup));
            go.transform.SetParent(transform, false);
            var group = go.GetComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;

            var bg = NewImage("bg", go.transform, new Color(0.05f, 0.035f, 0.025f, 0.78f));
            Stretch(bg.rectTransform, 0f);
            var frame = bg.gameObject.AddComponent<Outline>();
            frame.effectColor = new Color(0.87f, 0.70f, 0.36f, 0.35f);
            frame.effectDistance = new Vector2(1f, -1f);

            var icon = NewImage("icon", go.transform, Color.white);
            icon.preserveAspect = true;

            var track = NewImage("track", go.transform, new Color(0f, 0f, 0f, 0.55f));
            var baseImg = NewImage("base", track.transform, Color.white);
            var gainImg = NewImage("gain", track.transform, Color.white);

            var name = NewText(go.transform, "name", TextAlignmentOptions.MidlineLeft);
            name.overflowMode = TextOverflowModes.Ellipsis;
            var level = NewText(go.transform, "level", TextAlignmentOptions.MidlineRight);

            return new Bar
            {
                Type = type,
                Root = go,
                Rect = (RectTransform)go.transform,
                Group = group,
                Icon = icon,
                Track = track.rectTransform,
                Base = baseImg.rectTransform,
                Gain = gainImg.rectTransform,
                BaseImage = baseImg,
                GainImage = gainImg,
                Name = name,
                Level = level,
                Width = -1f,
            };
        }

        /// <summary>Sizes a row's parts from the current settings; cheap when nothing changed.</summary>
        private void LayoutBar(Bar bar)
        {
            float w = SkillXpBars.BarWidth.Value, h = SkillXpBars.BarHeight.Value;
            var labels = SkillXpBars.Labels.Value;

            // Colors follow the setting live (cheap compare).
            Color c = SkillXpBars.BarColor.Value;
            Color.RGBToHSV(c, out float hue, out float sat, out float val);
            Color dark = Color.HSVToRGB(hue, sat, val * 0.55f);
            Color bright = Color.HSVToRGB(hue, sat * 0.7f, Mathf.Min(1f, val * 1.25f + 0.1f));
            dark.a = bright.a = c.a;
            if (bar.BaseImage.color != dark) bar.BaseImage.color = dark;
            if (bar.GainImage.color != bright) bar.GainImage.color = bright;

            if (bar.Width == w && bar.Height == h && bar.Labels == labels)
                return;
            bar.Width = w;
            bar.Height = h;
            bar.Labels = labels;

            bar.Rect.sizeDelta = new Vector2(w, h);
            float pad = Mathf.Max(2f, Mathf.Round(h * 0.12f));
            float iconSize = h - pad * 2f + 2f;

            var irt = bar.Icon.rectTransform;
            irt.anchorMin = irt.anchorMax = new Vector2(0f, 0.5f);
            irt.pivot = new Vector2(0f, 0.5f);
            irt.sizeDelta = new Vector2(iconSize, iconSize);
            irt.anchoredPosition = new Vector2(pad - 1f, 0f);

            // The bar fills the rest of the row; text sits on top of it, like the health number.
            var trt = bar.Track;
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(pad + iconSize + pad, pad);
            trt.offsetMax = new Vector2(-pad, -pad);

            foreach (var fill in new[] { bar.Base, bar.Gain })
            {
                fill.pivot = new Vector2(0f, 0.5f);
                fill.offsetMin = Vector2.zero;
                fill.offsetMax = Vector2.zero;
            }

            bool text = labels == SkillXpBars.LabelStyle.NameAndLevel;
            bar.Name.gameObject.SetActive(text);
            bar.Level.gameObject.SetActive(text);
            float fontSize = Mathf.Clamp(Mathf.Round(h * 0.6f), 9f, 26f);
            foreach (var t in new[] { bar.Name, bar.Level })
            {
                t.fontSize = fontSize;
                var r = t.rectTransform;
                r.anchorMin = Vector2.zero;
                r.anchorMax = Vector2.one;
            }
            float left = pad + iconSize + pad + 4f;
            bar.Name.rectTransform.offsetMin = new Vector2(left, 0f);
            bar.Name.rectTransform.offsetMax = new Vector2(-(pad + 4f), 0f); // narrowed to fit the level text in UpdateContents
            bar.Level.rectTransform.offsetMin = new Vector2(left, 0f);
            bar.Level.rectTransform.offsetMax = new Vector2(-(pad + 4f), 0f);
        }

        /// <summary>Two fills on one track: base up to the session start, gain from there to now.</summary>
        private static void ApplyFill(Bar bar)
        {
            float b = Mathf.Clamp01(bar.ShownBase);
            float g = Mathf.Clamp01(b + Mathf.Max(0f, bar.ShownGain));
            bar.Base.anchorMin = new Vector2(0f, 0f);
            bar.Base.anchorMax = new Vector2(b, 1f);
            bar.Gain.anchorMin = new Vector2(b, 0f);
            bar.Gain.anchorMax = new Vector2(g, 1f);
        }

        private static void UpdateContents(Bar bar, Skills skills, Skills.Skill skill)
        {
            var type = skill.m_info.m_skill;
            float level = skill.m_level;
            float total = skills.GetSkillLevel(type);
            float pct = skill.GetLevelPercentage();
            int levelsGained = Mathf.RoundToInt(level - bar.StartLevel);

            if (levelsGained <= 0)
            {
                bar.TargetBase = bar.StartPercent;
                bar.TargetGain = Mathf.Max(0f, pct - bar.StartPercent);
            }
            else
            {
                // Leveled up this session: everything on the bar is new.
                bar.TargetBase = 0f;
                bar.TargetGain = pct;
            }
            if (level != bar.ShownLevel)
            {
                // Every level-up wraps the bar: restart the fill from empty instead of draining backwards.
                bar.ShownBase = 0f;
                bar.ShownGain = 0f;
            }
            bar.ShownLevel = level;

            bar.Icon.sprite = skill.m_info.m_icon;
            bar.Icon.enabled = bar.Icon.sprite != null;

            bar.Name.text = SkillXpBars.SkillName(type);
            int bonus = Mathf.RoundToInt(total - level);
            string levelText = ((int)level).ToString();
            if (bonus != 0)
                levelText += $"<size=80%><color=#9fd27a>{(bonus > 0 ? "+" : "")}{bonus}</color></size>";
            if (SkillXpBars.ShowGainText.Value)
            {
                float gained = levelsGained + pct - bar.StartPercent;
                string hex = ColorUtility.ToHtmlStringRGB(bar.GainImage.color);
                levelText = $"<size=85%><color=#{hex}>+{gained * 100f:0.#}%</color></size>  {levelText}";
            }
            bar.Level.text = levelText;
            // The name gets whatever the level text leaves, then ellipsis.
            bar.Name.rectTransform.offsetMax = new Vector2(bar.Level.rectTransform.offsetMax.x - bar.Level.preferredWidth - 6f, 0f);
        }

        private TMP_Text NewText(Transform parent, string name, TextAlignmentOptions align)
        {
            var textGo = Instantiate(_textTemplate.gameObject, parent, false);
            textGo.name = name;
            textGo.SetActive(true);
            // The HUD health text is turned 90 degrees in its own panel; the copy must sit flat.
            textGo.transform.localRotation = Quaternion.identity;
            textGo.transform.localScale = Vector3.one;
            for (int i = textGo.transform.childCount - 1; i >= 0; i--)
                Destroy(textGo.transform.GetChild(i).gameObject);
            foreach (var c in textGo.GetComponents<Component>())
                if (!(c is Transform) && !(c is TMP_Text) && !(c is CanvasRenderer))
                    Destroy(c);
            var text = textGo.GetComponent<TMP_Text>();
            text.enableAutoSizing = false;
            text.color = Parchment;
            text.alignment = align;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            text.richText = true;
            text.raycastTarget = false;
            var rt = text.rectTransform;
            rt.pivot = new Vector2(0.5f, 0.5f);
            return text;
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

        private static void Stretch(RectTransform rt, float inset)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(inset, inset);
            rt.offsetMax = new Vector2(-inset, -inset);
        }
    }
}
