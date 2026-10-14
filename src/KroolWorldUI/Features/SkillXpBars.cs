using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KroolWorldUI.Features
{
    /// <summary>
    /// Shows a skill row (cloned from the Skills tab, so it uses the game's own art) on the HUD
    /// whenever the local player gains XP in a skill. The bar shows progress to the next level in
    /// two shades: darker for where it stood when the row appeared, lighter for XP gained since.
    /// Each row fades out after a few seconds without XP; the gained portion resets when it does.
    /// Read-only: never writes skill data.
    /// </summary>
    internal static class SkillXpBars
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> HoldSeconds;
        internal static ConfigEntry<float> FadeSeconds;
        internal static ConfigEntry<int> MaxBars;
        internal static ConfigEntry<float> AnchorX;
        internal static ConfigEntry<float> AnchorY;
        internal static ConfigEntry<float> BarWidth;
        internal static ConfigEntry<bool> ShowGainText;

        private const string Section = "Skill XP Bars";

        internal static void BindConfig(ConfigFile config)
        {
            Enabled = config.Bind(Section, "Enabled", true,
                "Show a skill bar on the HUD while you gain XP in that skill.");
            ShowGainText = config.Bind(Section, "ShowGainText", true,
                "Show the XP gained since the bar appeared, as a percent of a level, next to the skill name.");
            HoldSeconds = config.Bind(Section, "HoldSeconds", 3f,
                new ConfigDescription("Seconds a bar stays fully visible after the last XP gain.",
                    new AcceptableValueRange<float>(0.5f, 30f)));
            FadeSeconds = config.Bind(Section, "FadeSeconds", 0.6f,
                new ConfigDescription("Seconds the fade-out takes.", new AcceptableValueRange<float>(0f, 5f)));
            MaxBars = config.Bind(Section, "MaxBars", 5,
                new ConfigDescription("Most bars shown at once. The stalest bar is reused past this.",
                    new AcceptableValueRange<int>(1, 10)));
            AnchorX = config.Bind(Section, "AnchorX", 0.985f,
                new ConfigDescription("Horizontal position of the stack's right edge (0 = left of screen, 1 = right).",
                    new AcceptableValueRange<float>(0f, 1f)));
            AnchorY = config.Bind(Section, "AnchorY", 0.6f,
                new ConfigDescription("Vertical position of the stack's top edge (0 = bottom of screen, 1 = top). " +
                                      "The stack moves down on its own if the minimap or status effects reach it.",
                    new AcceptableValueRange<float>(0f, 1f)));
            BarWidth = config.Bind(Section, "BarWidth", 260f,
                new ConfigDescription("Width of one bar in HUD pixels (at 1080p, before the game's UI scale).",
                    new AcceptableValueRange<float>(120f, 600f)));
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

        [HarmonyPatch(typeof(Skills), nameof(Skills.RaiseSkill))]
        private static class RaiseSkillPatch
        {
            private static void Prefix(Skills __instance, Skills.SkillType __0, out Snapshot __state)
            {
                __state = default;
                if (!Enabled.Value || !IsLocalPlayer(__instance))
                    return;
                var skill = FindExisting(__instance, __0);
                if (skill == null)
                    return;
                __state = new Snapshot { Exists = true, Level = skill.m_level, Accumulator = skill.m_accumulator };
            }

            private static void Postfix(Skills __instance, Skills.SkillType __0, Snapshot __state)
            {
                if (!Enabled.Value || !IsLocalPlayer(__instance))
                    return;
                var skill = FindExisting(__instance, __0);
                if (skill == null)
                    return;
                bool changed = !__state.Exists
                    || skill.m_level != __state.Level
                    || skill.m_accumulator != __state.Accumulator;
                if (!changed)
                    return;

                try
                {
                    SkillXpBarStack.GetOrCreate()?.OnXpGained(__instance, skill, __state);
                }
                catch (System.Exception e)
                {
                    // Never let a UI problem break the game's skill code.
                    Plugin.Log.LogError($"Skill XP bar failed: {e}");
                }
            }
        }
    }

    /// <summary>Lives under the HUD root, owns the visible bars, handles fade and layout.</summary>
    internal class SkillXpBarStack : MonoBehaviour
    {
        private const string ObjectName = "KroolWorldUI_SkillXpBars";
        private const float FallbackWidth = 320f;
        private const float FallbackHeight = 40f;
        private const float RowGap = 4f;
        private const float BlockerMargin = 12f;

        private static SkillXpBarStack _instance;
        private static bool _loggedPrefab;
        private static readonly AccessTools.FieldRef<GuiBar, float> GuiBarWidth =
            AccessTools.FieldRefAccess<GuiBar, float>("m_width");

        private class Bar
        {
            public Skills.SkillType Type;
            public GameObject Root;
            public RectTransform Rect;
            public CanvasGroup Group;
            public Image Icon;
            public TMP_Text Name;
            public TMP_Text LevelText;
            public TMP_Text BonusText;
            public XpFill Fill;
            public float LastGain;

            // Session: reset each time the bar appears.
            public float StartLevel;
            public float StartPercent;
            public bool SessionStarted;
        }

        /// <summary>
        /// Two fills over one bar track: base (darker) up to the session start, gained (lighter)
        /// from there to the current progress.
        /// </summary>
        private class XpFill
        {
            public RectTransform Base;
            public RectTransform Gain;
            public float Left;
            public float Width;
            public float TargetBase;
            public float TargetGain;
            public float ShownBase;
            public float ShownGain;
            public Color GainColor = Color.white;

            public void Apply()
            {
                SetSpan(Base, Left, ShownBase * Width);
                SetSpan(Gain, Left + ShownBase * Width, ShownGain * Width);
            }

            private static void SetSpan(RectTransform rt, float x, float w)
            {
                rt.anchoredPosition = new Vector2(x, rt.anchoredPosition.y);
                rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, Mathf.Max(0f, w));
            }
        }

        private readonly List<Bar> _bars = new List<Bar>();
        private RectTransform _rect;
        private CanvasGroup _group;
        private Vector2 _rowSize = new Vector2(FallbackWidth, FallbackHeight);
        private readonly Vector3[] _corners = new Vector3[4];

        internal static SkillXpBarStack GetOrCreate()
        {
            if (_instance != null)
                return _instance;
            var hud = Hud.instance;
            if (hud == null || hud.m_rootObject == null)
                return null;

            var go = new GameObject(ObjectName, typeof(RectTransform), typeof(CanvasGroup));
            go.transform.SetParent(hud.m_rootObject.transform, false);
            _instance = go.AddComponent<SkillXpBarStack>();
            _instance._rect = (RectTransform)go.transform;
            _instance._rect.sizeDelta = Vector2.zero;
            _instance._group = go.GetComponent<CanvasGroup>();
            _instance._group.blocksRaycasts = false;
            _instance._group.interactable = false;
            return _instance;
        }

        private void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
        }

        private float RowScale => SkillXpBars.BarWidth.Value / Mathf.Max(1f, _rowSize.x);
        private float TargetY(int index) => -index * (_rowSize.y + RowGap);

        internal void OnXpGained(Skills skills, Skills.Skill skill, SkillXpBars.Snapshot before)
        {
            var type = skill.m_info.m_skill;
            var bar = _bars.Find(b => b.Type == type);
            if (bar == null)
            {
                if (_bars.Count >= SkillXpBars.MaxBars.Value)
                {
                    // Reuse the bar that has gone longest without XP, as a fresh session.
                    bar = _bars[0];
                    foreach (var b in _bars)
                        if (b.LastGain < bar.LastGain)
                            bar = b;
                    bar.Type = type;
                    bar.SessionStarted = false;
                }
                else
                {
                    bar = CreateBar(type);
                    if (bar == null)
                        return;
                    _bars.Add(bar);
                    bar.Rect.anchoredPosition = new Vector2(0f, TargetY(_bars.Count - 1));
                }
            }

            if (!bar.SessionStarted)
            {
                // Start from the progress before this XP tick, so the very first gain shows too.
                bar.SessionStarted = true;
                bar.StartLevel = before.Exists ? before.Level : skill.m_level;
                bar.StartPercent = before.Exists ? PercentOf(skill, before) : 0f;
                if (bar.Fill != null)
                {
                    bar.Fill.ShownBase = bar.StartPercent;
                    bar.Fill.ShownGain = 0f;
                }
            }

            UpdateContents(bar, skills, skill);
            bar.LastGain = Time.time;
            bar.Group.alpha = 1f;
        }

        // Progress percent for a snapshot taken before the raise. Requirement depends only on level.
        private static float PercentOf(Skills.Skill skill, SkillXpBars.Snapshot s)
        {
            float level = skill.m_level, acc = skill.m_accumulator;
            skill.m_level = s.Level;
            skill.m_accumulator = s.Accumulator;
            float pct = skill.GetLevelPercentage();
            skill.m_level = level;
            skill.m_accumulator = acc;
            return pct;
        }

        private void Update()
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
                if (alpha <= 0f || bar.Root == null)
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
                // Slide remaining bars up into gaps left by faded ones.
                var pos = bar.Rect.anchoredPosition;
                pos.y = Mathf.Lerp(pos.y, TargetY(i), t);
                pos.x = 0f;
                bar.Rect.anchoredPosition = pos;

                if (bar.Fill != null)
                {
                    bar.Fill.ShownBase = Mathf.Lerp(bar.Fill.ShownBase, bar.Fill.TargetBase, t);
                    bar.Fill.ShownGain = Mathf.Lerp(bar.Fill.ShownGain, bar.Fill.TargetGain, t);
                    bar.Fill.Apply();
                }
            }

            Place();
        }

        /// <summary>
        /// Puts the stack at its configured anchor, then pushes it down below any HUD element it
        /// would overlap (minimap, status effects). The full MaxBars height is reserved so the stack
        /// does not jump around as bars come and go.
        /// </summary>
        private void Place()
        {
            var anchor = new Vector2(SkillXpBars.AnchorX.Value, SkillXpBars.AnchorY.Value);
            _rect.anchorMin = anchor;
            _rect.anchorMax = anchor;
            _rect.pivot = new Vector2(1f, 1f);
            _rect.anchoredPosition = Vector2.zero;
            float s = RowScale;
            _rect.localScale = new Vector3(s, s, 1f);

            Vector3 top = _rect.position;
            float worldScale = _rect.lossyScale.y;
            float height = SkillXpBars.MaxBars.Value * (_rowSize.y + RowGap) * worldScale;
            float width = _rowSize.x * _rect.lossyScale.x;
            float margin = BlockerMargin * (worldScale / Mathf.Max(0.0001f, s));
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

        private Bar CreateBar(Skills.SkillType type)
        {
            var dialog = InventoryGui.instance != null ? InventoryGui.instance.m_skillsDialog : null;
            var prefab = dialog != null ? dialog.m_elementPrefab : null;
            if (prefab == null)
            {
                Plugin.Log.LogWarning("Skills dialog row prefab not found; skill XP bar skipped.");
                return null;
            }

            var go = Instantiate(prefab, transform, false);
            go.name = "SkillXpBar";
            go.SetActive(true);

            // Display only: no hover tooltip, no clicks, no gamepad focus.
            foreach (var tip in go.GetComponentsInChildren<UITooltip>(true))
                tip.enabled = false;
            foreach (var sel in go.GetComponentsInChildren<Selectable>(true))
            {
                sel.interactable = false;
                sel.enabled = false;
            }
            foreach (var g in go.GetComponentsInChildren<Graphic>(true))
                g.raycastTarget = false;

            var group = go.GetComponent<CanvasGroup>() ?? go.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;

            var rect = (RectTransform)go.transform;
            var size = rect.sizeDelta;
            if (!_loggedPrefab)
            {
                _loggedPrefab = true;
                Plugin.Log.LogInfo($"Skill row prefab: size={size} anchorMin={rect.anchorMin} anchorMax={rect.anchorMax} " +
                                   $"pivot={rect.pivot} spacing={dialog.m_spacing}");
            }
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.sizeDelta = new Vector2(size.x > 50f ? size.x : FallbackWidth, size.y > 10f ? size.y : FallbackHeight);
            _rowSize = rect.sizeDelta;

            var bar = new Bar
            {
                Type = type,
                Root = go,
                Rect = rect,
                Group = group,
                Icon = FindChild(go.transform, "icon")?.GetComponent<Image>(),
                Name = FindChild(go.transform, "name")?.GetComponent<TMP_Text>(),
                LevelText = FindChild(go.transform, "leveltext")?.GetComponent<TMP_Text>(),
                BonusText = FindChild(go.transform, "bonustext")?.GetComponent<TMP_Text>(),
            };
            bar.Fill = BuildFill(go.transform);
            return bar;
        }

        /// <summary>
        /// The skills row has up to three GuiBars (level, level with bonus, progress to next level).
        /// The largest one becomes the XP bar; the others are hidden so there is one clear bar.
        /// </summary>
        private static XpFill BuildFill(Transform row)
        {
            GuiBar host = null;
            float hostArea = -1f;
            var all = row.GetComponentsInChildren<GuiBar>(true);
            foreach (var g in all)
            {
                if (g.m_bar == null) continue;
                var r = ((RectTransform)g.transform).rect;
                float area = Mathf.Max(GuiBarWidth(g), r.width) * Mathf.Max(r.height, g.m_bar.rect.height);
                if (area > hostArea)
                {
                    host = g;
                    hostArea = area;
                }
            }
            if (host == null)
            {
                Plugin.Log.LogWarning("Skill row prefab layout changed: no bar found; XP fill disabled.");
                return null;
            }
            foreach (var g in all)
                if (g != host)
                    g.gameObject.SetActive(false);

            float width = GuiBarWidth(host);
            if (width <= 0f)
                width = host.m_bar.rect.width;
            host.enabled = false; // we drive the fill ourselves

            var baseRt = host.m_bar;
            float left = baseRt.anchoredPosition.x - baseRt.pivot.x * baseRt.rect.width;
            baseRt.pivot = new Vector2(0f, baseRt.pivot.y);

            var gainGo = Instantiate(baseRt.gameObject, baseRt.parent, false);
            gainGo.name = "gain";
            gainGo.transform.SetSiblingIndex(baseRt.GetSiblingIndex() + 1);
            var gainRt = (RectTransform)gainGo.transform;

            // Same hue, two shades: settled progress darker, fresh XP brighter.
            var baseImg = baseRt.GetComponent<Image>();
            var gainImg = gainRt.GetComponent<Image>();
            if (baseImg != null && gainImg != null)
            {
                Color c = baseImg.color;
                Color.RGBToHSV(c, out float h, out float sat, out float v);
                Color dark = Color.HSVToRGB(h, sat, v * 0.55f);
                Color bright = Color.HSVToRGB(h, sat * 0.7f, Mathf.Min(1f, v * 1.25f + 0.1f));
                dark.a = c.a;
                bright.a = c.a;
                baseImg.color = dark;
                gainImg.color = bright;
            }

            var fill = new XpFill { Base = baseRt, Gain = gainRt, Left = left, Width = width };
            if (gainImg != null)
                fill.GainColor = gainImg.color;
            return fill;
        }

        private static void UpdateContents(Bar bar, Skills skills, Skills.Skill skill)
        {
            var type = skill.m_info.m_skill;
            float level = skill.m_level;
            float total = skills.GetSkillLevel(type);
            float pct = skill.GetLevelPercentage();
            int levelsGained = Mathf.RoundToInt(level - bar.StartLevel);

            if (bar.Fill != null)
            {
                if (levelsGained <= 0)
                {
                    bar.Fill.TargetBase = bar.StartPercent;
                    bar.Fill.TargetGain = Mathf.Max(0f, pct - bar.StartPercent);
                }
                else
                {
                    // Leveled up this session: everything on the bar is new.
                    bar.Fill.TargetBase = 0f;
                    bar.Fill.TargetGain = pct;
                    if (bar.Fill.ShownBase > 0f && levelsGained == 1 && bar.Fill.ShownBase + bar.Fill.ShownGain > pct)
                    {
                        // Snap on the wrap instead of draining backwards.
                        bar.Fill.ShownBase = 0f;
                        bar.Fill.ShownGain = 0f;
                    }
                }
            }

            if (bar.Icon != null)
                bar.Icon.sprite = skill.m_info.m_icon;
            if (bar.Name != null)
            {
                string name = SkillName(type);
                if (SkillXpBars.ShowGainText.Value)
                {
                    float gained = levelsGained + pct - bar.StartPercent;
                    string colorHex = ColorUtility.ToHtmlStringRGB(bar.Fill != null ? bar.Fill.GainColor : Color.white);
                    name += $"  <color=#{colorHex}>+{gained * 100f:0.#}%</color>";
                }
                bar.Name.text = name;
            }
            if (bar.LevelText != null)
                bar.LevelText.text = ((int)level).ToString();
            if (bar.BonusText != null)
            {
                int bonus = Mathf.RoundToInt(total - level);
                bar.BonusText.gameObject.SetActive(bonus != 0);
                bar.BonusText.text = bonus > 0 ? "+" + bonus : bonus.ToString();
            }
        }

        private static string SkillName(Skills.SkillType type)
        {
            string key = "$skill_" + type.ToString().ToLower();
            string name = Localization.instance != null ? Localization.instance.Localize(key) : key;
            // Unknown tokens come back bracketed or unchanged (custom skills without a translation).
            if (string.IsNullOrEmpty(name) || name.StartsWith("[") || name.StartsWith("$"))
                return type.ToString();
            return name;
        }

        private static Transform FindChild(Transform root, string name)
        {
            if (root.name == name)
                return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var found = FindChild(root.GetChild(i), name);
                if (found != null)
                    return found;
            }
            return null;
        }
    }
}
