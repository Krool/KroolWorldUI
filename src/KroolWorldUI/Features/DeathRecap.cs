using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace KroolWorldUI.Features
{
    /// <summary>Everything that hurt you in the fight you died in, by source.</summary>
    internal sealed class DeathRecapData
    {
        internal sealed class Row
        {
            public Who Who;
            public float Taken;
            public float Raw;      // before armor, resistances and blocking (direct hits)
            public int Hits;       // direct hits
            public int Ticks;      // burning or poison ticks
            public int Blocked;
            public float[] Types = new float[Dmg.Count];
        }

        public DateTime When;
        public Who Killer;
        public float KillingBlow;
        public float Overkill;
        public float[] KillingTypes;
        public float Taken;
        public float Stopped;      // what armor, resistances and blocking took off the direct hits
        public float Seconds;
        public float MaxHealth;
        public List<Row> Rows = new List<Row>();
    }

    /// <summary>
    /// When you die, a recap of the fight: who and what hurt you, how much, with what damage
    /// types, and the killing blow. Shown over the death screen (it stays readable while the game
    /// fades to black) and kept for the stats window. The fight is the run of hits before your
    /// death with no gap longer than 15 seconds. Read-only.
    /// </summary>
    internal static class DeathRecap
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> Popup;
        internal static ConfigEntry<float> AfterRespawn;
        internal static ConfigEntry<float> Size;
        internal static ConfigEntry<float> Opacity;

        private const string Section = "Death Recap";
        private const float Gap = 15f;
        private const float MaxSeconds = 90f;

        /// <summary>Recent deaths this session, newest first.</summary>
        internal static readonly List<DeathRecapData> History = new List<DeathRecapData>();

        internal static void BindConfig(ConfigFile config)
        {
            Enabled = config.Bind(Section, "Enabled", true,
                "Keep a recap of what killed you: every source of damage in the fight, the killing blow and the damage types. " +
                "Shown in the stats window under Deaths.");
            Popup = config.Bind(Section, "Popup", true,
                ConfigText.Desc("Show the recap on the death screen.", when: () => Enabled.Value, whenText: "Death Recap is on"));
            AfterRespawn = config.Bind(Section, "AfterRespawn", 6f,
                ConfigText.Desc("Seconds the recap stays up after you respawn.", new AcceptableValueRange<float>(0f, 30f),
                    when: () => Popup.Value, whenText: "Popup is on", step: 1f));

            const string look = "Look";
            Size = config.Bind(Section, "Size", 1f,
                ConfigText.Desc("1 is the normal size.", new AcceptableValueRange<float>(0.75f, 1.5f), look,
                    () => Popup.Value, "Popup is on", 0.05f));
            Opacity = config.Bind(Section, "Opacity", 0.95f,
                ConfigText.Desc("1 is solid; lower shows the game through it.", new AcceptableValueRange<float>(0.3f, 1f), look,
                    () => Popup.Value, "Popup is on", 0.05f));
        }

        internal static void Record(List<HitEvent> events, float maxHealth)
        {
            if (events.Count == 0)
                return;
            // The fight: back from the last hit until a quiet gap.
            int first = events.Count - 1;
            float end = events[first].Time;
            while (first > 0 && events[first].Time - events[first - 1].Time <= Gap && end - events[first - 1].Time <= MaxSeconds)
                first--;
            var fight = events.GetRange(first, events.Count - first);

            // The killing blow: everything in the last moment (a burning tick shared by two sources
            // is two entries), credited to whoever did most of it.
            var blow = fight.Where(e => e.Time >= end - 0.001f).ToList();
            var killer = blow.OrderByDescending(e => e.Amount).First();
            var data = new DeathRecapData
            {
                When = DateTime.Now, Killer = killer.Who, KillingBlow = blow.Sum(e => e.Amount),
                Overkill = blow.Sum(e => e.Overkill), KillingTypes = killer.Types,
                MaxHealth = maxHealth, Seconds = Mathf.Max(0f, end - fight[0].Time),
            };

            var rows = new Dictionary<string, DeathRecapData.Row>();
            foreach (var e in fight)
            {
                if (!rows.TryGetValue(e.Who.Key, out var row))
                    rows[e.Who.Key] = row = new DeathRecapData.Row { Who = e.Who };
                row.Taken += e.Amount;
                for (int i = 0; i < Dmg.Count; i++)
                    row.Types[i] += e.Types[i];
                if (e.Dot)
                {
                    row.Ticks++;
                }
                else
                {
                    row.Hits++;
                    row.Raw += e.Raw;
                    if (e.Blocked)
                        row.Blocked++;
                }
            }
            data.Rows = rows.Values.OrderByDescending(r => r.Taken).ToList();
            data.Taken = data.Rows.Sum(r => r.Taken);
            float directTaken = fight.Where(e => !e.Dot).Sum(e => e.Amount);
            data.Stopped = Mathf.Max(0f, data.Rows.Sum(r => r.Raw) - directTaken);

            History.Insert(0, data);
            if (History.Count > 5)
                History.RemoveRange(5, History.Count - 5);
            if (Popup.Value)
                DeathRecapHud.Show(data);
        }

        [HarmonyPatch(typeof(Hud), "Update")]
        private static class EnsureOnHud
        {
            private static void Postfix()
            {
                if (Enabled.Value)
                    DeathRecapHud.Ensure();
            }
        }

        /// <summary>A made-up fight, for the settings window's preview.</summary>
        internal static DeathRecapData Sample()
        {
            float[] T(params (int type, float v)[] parts)
            {
                var a = new float[Dmg.Count];
                foreach (var p in parts)
                    a[p.type] = p.v;
                return a;
            }
            var troll = new Who { Key = "c:Troll:2", Name = "$enemy_troll", Prefab = "Troll", Level = 2 };
            var shaman = new Who { Key = "c:GoblinShaman:1", Name = "$enemy_goblinshaman", Prefab = "GoblinShaman", Level = 1 };
            var burn = Who.World(HitData.HitType.Burning);
            var data = new DeathRecapData
            {
                When = DateTime.Now, Killer = troll, KillingBlow = 86f, KillingTypes = T((Dmg.Blunt, 86f)), Overkill = 31f,
                MaxHealth = 140f, Seconds = 14f, Stopped = 212f,
            };
            data.Rows.Add(new DeathRecapData.Row { Who = troll, Taken = 172f, Hits = 3, Types = T((Dmg.Blunt, 172f)) });
            data.Rows.Add(new DeathRecapData.Row { Who = shaman, Taken = 58f, Hits = 2, Types = T((Dmg.Fire, 40f), (Dmg.Blunt, 18f)) });
            data.Rows.Add(new DeathRecapData.Row { Who = burn, Taken = 21f, Ticks = 6, Types = T((Dmg.Fire, 21f)) });
            data.Taken = 251f;
            return data;
        }
    }

    /// <summary>The recap over the death screen. Sits above the game's fade to black so it stays readable.</summary>
    internal class DeathRecapHud : MonoBehaviour
    {
        private const float Width = 470f;

        private static DeathRecapHud _instance;
        private static readonly HudKit.FailFlag _failed = new HudKit.FailFlag();

        private RectTransform _rect;
        private CanvasGroup _group;
        private RectTransform _body;
        private DeathRecapData _data;
        private float _shownAt;
        private float _respawnedAt = -1f;
        private bool _previewing;

        internal static void Ensure()
        {
            if (_instance != null || _failed.IsSet)
                return;
            var hud = Hud.instance;
            if (hud == null || hud.m_loadingScreen == null || StatsUi.GetArt() == null)
                return;
            try
            {
                var go = new GameObject("KroolWorldUI_DeathRecap", typeof(RectTransform), typeof(CanvasGroup));
                // Beside the black screen, drawn after it.
                go.transform.SetParent(hud.m_loadingScreen.transform.parent, false);
                var c = go.AddComponent<DeathRecapHud>();
                _instance = c;
                c._rect = (RectTransform)go.transform;
                c._rect.anchorMin = c._rect.anchorMax = new Vector2(0.5f, 0.5f);
                c._rect.pivot = new Vector2(0.5f, 1f);
                c._group = go.GetComponent<CanvasGroup>();
                c._group.alpha = 0f;
                c._group.blocksRaycasts = false;
                c._group.interactable = false;
                go.SetActive(false);
            }
            catch (Exception e)
            {
                _failed.Set();
                Plugin.Log.LogError($"Death recap disabled for this session: {e}");
            }
        }

        private void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
        }

        internal static void Show(DeathRecapData data)
        {
            Ensure();
            var c = _instance;
            if (c == null)
                return;
            try
            {
                c._previewing = false;
                c.Build(data);
                c._shownAt = Time.unscaledTime;
                c._respawnedAt = -1f;
            }
            catch (Exception e)
            {
                _failed.Set();
                Plugin.Log.LogError($"Death recap: {e}");
            }
        }

        internal static void Preview(bool on)
        {
            var c = _instance;
            if (c == null || _failed.IsSet || c._previewing == on)
                return;
            c._previewing = on;
            if (on)
                c.Build(DeathRecap.Sample());
            else
                c._data = null; // the sample goes (a real recap on screen was replaced by it anyway)
            c._shownAt = Time.unscaledTime;
        }

        private void Build(DeathRecapData data)
        {
            _data = data;
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            if (_body != null)
                Destroy(_body.gameObject);
            var art = StatsUi.GetArt();
            _body = StatsUi.Rect("body", transform);
            var back = StatsUi.CopyOf(art.Frame, _body, Color.white);
            if (art.Frame == null)
                back.color = new Color(0.08f, 0.06f, 0.04f, 0.92f);
            StatsUi.TopLeft(back.rectTransform);
            const float pad = 22f;
            var inner = StatsUi.Rect("inner", _body);
            float h = RecapView.Death(art, inner, data, Width - pad * 2f, compact: true);
            StatsUi.Place(inner, pad, pad, Width - pad * 2f, h);
            float total = h + pad * 2f;
            StatsUi.Place(back.rectTransform, 0f, 0f, Width, total);
            _body.anchorMin = _body.anchorMax = new Vector2(0.5f, 1f);
            _body.pivot = new Vector2(0.5f, 1f);
            _body.anchoredPosition = Vector2.zero;
            _body.sizeDelta = new Vector2(Width, total);
            _rect.sizeDelta = new Vector2(Width, total);
        }

        private void Update()
        {
            try
            {
                bool showing = _previewing;
                if (!showing && _data != null)
                {
                    var player = Player.m_localPlayer;
                    bool dead = player == null || player.IsDead() || (Game.instance != null && Game.instance.WaitingForRespawn());
                    if (!dead && _respawnedAt < 0f)
                        _respawnedAt = Time.unscaledTime;
                    showing = dead || Time.unscaledTime - _respawnedAt < DeathRecap.AfterRespawn.Value;
                }
                if (!showing && _group.alpha <= 0.001f)
                {
                    _data = null;
                    gameObject.SetActive(false);
                    return;
                }
                // A moment after death, so the killing blow lands first.
                bool ready = _previewing || Time.unscaledTime - _shownAt > 1.2f;
                float target = showing && ready && (HudKit.Allowed || _previewing) ? DeathRecap.Opacity.Value : 0f;
                _group.alpha = Mathf.MoveTowards(_group.alpha, target, Time.unscaledDeltaTime * 2.5f);
                float scale = DeathRecap.Size.Value;
                _rect.localScale = new Vector3(scale, scale, 1f);
                // Below the game's "You died" in the middle of the screen.
                _rect.anchoredPosition = new Vector2(0f, -60f);
            }
            catch (Exception e)
            {
                _failed.Set();
                Plugin.Log.LogError($"Death recap: {e}");
                gameObject.SetActive(false);
            }
        }
    }
}
