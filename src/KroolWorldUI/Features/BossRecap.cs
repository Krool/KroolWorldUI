using System;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace KroolWorldUI.Features
{
    /// <summary>
    /// When a boss dies, who did how much of the damage, and with what: weapons, arrows, Epic Loot
    /// enchantments, damage over time, by damage type. A short popup at the kill; the full
    /// breakdown in the stats window under Bosses. Players without the mod still show (by weapon
    /// skill). See BossFights for how the numbers are gathered. Read-only.
    /// </summary>
    internal static class BossRecap
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> Popup;
        internal static ConfigEntry<float> PopupSeconds;
        internal static ConfigEntry<float> Size;
        internal static ConfigEntry<float> Opacity;
        internal static ConfigEntry<float> AnchorX;
        internal static ConfigEntry<float> AnchorY;

        private const string Section = "Boss Recap";

        internal static void BindConfig(ConfigFile config)
        {
            Enabled = config.Bind(Section, "Enabled", true,
                "After a boss fight, how much damage each player did and with what: weapons, arrows, Epic Loot " +
                "enchantments, fire and poison over time. Friends with this mod share their numbers automatically. " +
                "The full breakdown is in the stats window under Bosses.");
            Popup = config.Bind(Section, "Popup", true,
                ConfigText.Desc("A short summary on screen when the boss dies.", when: () => Enabled.Value,
                    whenText: "Boss Recap is on"));
            PopupSeconds = config.Bind(Section, "PopupSeconds", 15f,
                ConfigText.Desc("How long the summary stays.", new AcceptableValueRange<float>(5f, 60f),
                    when: () => Popup.Value, whenText: "Popup is on", step: 1f));

            const string look = "Look";
            Size = config.Bind(Section, "Size", 1f,
                ConfigText.Desc("1 is the normal size.", new AcceptableValueRange<float>(0.75f, 1.5f), look,
                    () => Popup.Value, "Popup is on", 0.05f));
            Opacity = config.Bind(Section, "Opacity", 0.95f,
                ConfigText.Desc("1 is solid; lower shows the game through it.", new AcceptableValueRange<float>(0.3f, 1f), look,
                    () => Popup.Value, "Popup is on", 0.05f));

            const string placement = "Placement";
            AnchorX = config.Bind(Section, "AnchorX", 0.82f,
                ConfigText.Desc("Center of the summary. 0 is the left of the screen, 1 the right.",
                    new AcceptableValueRange<float>(0f, 1f), placement, () => Popup.Value, "Popup is on", 0.01f));
            AnchorY = config.Bind(Section, "AnchorY", 0.55f,
                ConfigText.Desc("Center of the summary. 0 is the bottom of the screen, 1 the top.",
                    new AcceptableValueRange<float>(0f, 1f), placement, () => Popup.Value, "Popup is on", 0.01f));
        }

        internal static void Show(BossRecapData recap, bool update)
        {
            if (Popup.Value)
                BossRecapHud.Show(recap, update);
            StatsWindow.Refresh();
        }

        [HarmonyPatch(typeof(Hud), "Update")]
        private static class Tick
        {
            private static void Postfix()
            {
                if (!Enabled.Value)
                    return;
                try
                {
                    BossFights.Update();
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"Boss recap: {e}");
                }
                BossRecapHud.Ensure();
            }
        }

        /// <summary>A made-up fight, for the settings window's preview.</summary>
        internal static BossRecapData Sample()
        {
            float[] T(params (int type, float v)[] parts)
            {
                var a = new float[Dmg.Count];
                foreach (var p in parts)
                    a[p.type] = p.v;
                return a;
            }
            RecapSource S(string weapon, string ammo, int skill, float[] types, int hits)
            {
                var s = new RecapSource { Weapon = weapon, Ammo = ammo, Skill = skill, Types = types, Hits = hits };
                s.Total = Dmg.Sum(types);
                return s;
            }
            RecapFighter F(Who who, bool local, params RecapSource[] sources)
            {
                var f = new RecapFighter { Who = who, IsLocal = local };
                foreach (var s in sources)
                {
                    f.Sources.Add(s);
                    for (int i = 0; i < Dmg.Count; i++)
                        f.Types[i] += s.Types[i];
                    f.Hits += s.Hits;
                }
                f.Total = Dmg.Sum(f.Types);
                return f;
            }
            string me = Player.m_localPlayer != null ? Player.m_localPlayer.GetPlayerName() : "You";
            var recap = new BossRecapData { Name = "$enemy_bonemass", Prefab = "Bonemass", MaxHealth = 5000f, Seconds = 192f, When = DateTime.Now };
            recap.Fighters.Add(F(Who.Player(me), true,
                S("BowFineWood", "ArrowFire", (int)Skills.SkillType.Bows, T((Dmg.Pierce, 900f), (Dmg.Fire, 640f)), 38),
                S("MaceIron", "", (int)Skills.SkillType.Clubs, T((Dmg.Blunt, 1210f)), 22)));
            recap.Fighters.Add(F(Who.Player("Astrid"), false,
                S("SledgeIron", "", (int)Skills.SkillType.Clubs, T((Dmg.Blunt, 1650f), (Dmg.Lightning, 120f)), 18),
                S("", "", 0, T((Dmg.Fire, 480f)), 0)));
            recap.Total = 0f;
            foreach (var f in recap.Fighters)
                recap.Total += f.Total;
            return recap;
        }
    }

    /// <summary>The summary that appears when a boss dies: each fighter's share, bar and top weapons.</summary>
    internal class BossRecapHud : MonoBehaviour
    {
        private const float Width = 460f;

        private static BossRecapHud _instance;
        private static readonly HudKit.FailFlag _failed = new HudKit.FailFlag();

        private RectTransform _rect;
        private CanvasGroup _group;
        private RectTransform _body;
        private BossRecapData _data;
        private float _until;
        private bool _previewing;

        internal static void Ensure()
        {
            if (_instance != null || _failed.IsSet)
                return;
            var hud = Hud.instance;
            if (hud == null || hud.m_rootObject == null || Player.m_localPlayer == null || StatsUi.GetArt() == null)
                return;
            try
            {
                var go = new GameObject("KroolWorldUI_BossRecap", typeof(RectTransform), typeof(CanvasGroup));
                go.transform.SetParent(hud.m_rootObject.transform, false);
                var c = go.AddComponent<BossRecapHud>();
                _instance = c;
                c._rect = (RectTransform)go.transform;
                c._rect.pivot = new Vector2(0.5f, 0.5f);
                c._group = go.GetComponent<CanvasGroup>();
                c._group.alpha = 0f;
                c._group.blocksRaycasts = false;
                c._group.interactable = false;
                go.SetActive(false);
            }
            catch (Exception e)
            {
                _failed.Set();
                Plugin.Log.LogError($"Boss recap popup disabled for this session: {e}");
            }
        }

        private void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
        }

        internal static void Show(BossRecapData recap, bool update)
        {
            Ensure();
            var c = _instance;
            if (c == null)
                return;
            // A late update to a summary already gone stays in the stats window only.
            if (update && (c._data == null || !c.gameObject.activeSelf))
                return;
            try
            {
                c._previewing = false;
                c.Build(recap);
                if (!update)
                    c._until = Time.unscaledTime + BossRecap.PopupSeconds.Value;
            }
            catch (Exception e)
            {
                _failed.Set();
                Plugin.Log.LogError($"Boss recap popup: {e}");
            }
        }

        internal static void Preview(bool on)
        {
            var c = _instance;
            if (c == null || _failed.IsSet || c._previewing == on)
                return;
            c._previewing = on;
            if (on)
                c.Build(BossRecap.Sample());
            else
                c._until = 0f;
        }

        private void Build(BossRecapData recap)
        {
            _data = recap;
            gameObject.SetActive(true);
            if (_body != null)
                Destroy(_body.gameObject);
            var art = StatsUi.GetArt();
            _body = StatsUi.Rect("body", transform);
            var back = StatsUi.CopyOf(art.Frame, _body, Color.white);
            if (art.Frame == null)
                back.color = new Color(0.08f, 0.06f, 0.04f, 0.92f);
            StatsUi.TopLeft(back.rectTransform);
            const float pad = 20f;
            var inner = StatsUi.Rect("inner", _body);
            float h = RecapView.Boss(art, inner, recap, Width - pad * 2f, compact: true);
            string key = StatsWindow.KeyLabel();
            if (!string.IsNullOrEmpty(key))
            {
                StatsUi.Text(art, inner, L10n.T("boss_hint", key), 0f, h, Width - pad * 2f, 20f, 14f, StatsUi.Faint,
                    TMPro.TextAlignmentOptions.MidlineRight);
                h += 22f;
            }
            StatsUi.Place(inner, pad, pad, Width - pad * 2f, h);
            float total = h + pad * 2f;
            StatsUi.Place(back.rectTransform, 0f, 0f, Width, total);
            _body.anchorMin = _body.anchorMax = new Vector2(0.5f, 0.5f);
            _body.pivot = new Vector2(0.5f, 0.5f);
            _body.anchoredPosition = Vector2.zero;
            _body.sizeDelta = new Vector2(Width, total);
            _rect.sizeDelta = new Vector2(Width, total);
        }

        private void Update()
        {
            try
            {
                bool showing = _previewing || Time.unscaledTime < _until;
                float target = showing && HudKit.Allowed ? BossRecap.Opacity.Value : 0f;
                _group.alpha = Mathf.MoveTowards(_group.alpha, target, Time.unscaledDeltaTime * 3f);
                if (!showing && _group.alpha <= 0.001f)
                {
                    _data = null;
                    gameObject.SetActive(false);
                    return;
                }
                float scale = BossRecap.Size.Value;
                _rect.localScale = new Vector3(scale, scale, 1f);
                _rect.anchorMin = _rect.anchorMax = new Vector2(BossRecap.AnchorX.Value, BossRecap.AnchorY.Value);
                _rect.anchoredPosition = Vector2.zero;
            }
            catch (Exception e)
            {
                _failed.Set();
                Plugin.Log.LogError($"Boss recap popup: {e}");
                gameObject.SetActive(false);
            }
        }
    }
}
