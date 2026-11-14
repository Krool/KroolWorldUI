using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace KroolWorldUI.Features
{
    /// <summary>One line of a boss recap: a weapon, an arrow, an enchantment or a kind of damage.</summary>
    internal sealed class RecapSource
    {
        public string Weapon;   // item prefab, may be empty
        public string Ammo;     // item prefab, may be empty
        public string Effect;   // Epic Loot effect id, "aoe", or empty
        public int Skill;       // Skills.SkillType, for lines without an item
        public int Type = -1;   // a damage type, for lines with nothing else to name them
        public float Total;
        public int Hits;
        public float[] Types = new float[Dmg.Count];
    }

    /// <summary>Everyone who hurt a boss, with what they used.</summary>
    internal sealed class RecapFighter
    {
        public Who Who;
        public bool IsLocal;
        public bool Estimated;  // no game that ran the boss recorded this player's hits; their own count, before the boss's resistances
        public float Total;
        public int Hits;
        public float[] Types = new float[Dmg.Count];
        public List<RecapSource> Sources = new List<RecapSource>();
    }

    internal sealed class BossRecapData
    {
        public string Name;       // token
        public string Prefab;
        public float MaxHealth;
        public float Seconds;
        public float Total;
        public DateTime When;
        public List<RecapFighter> Fighters = new List<RecapFighter>();
    }

    /// <summary>
    /// Boss fights in progress, and the recap when one dies.
    ///
    /// Only the game that runs a boss (its owner, usually the first player near it) sees every
    /// hit it takes, after its resistances. Only the player who hits knows which weapon or arrow
    /// it was. So each game keeps both halves: the hits a boss took while it ran the boss, and
    /// the local player's own hits by weapon. When a boss dies, its owner announces it (or, when
    /// the owner lacks this mod, the game of any player who hit it does); every
    /// game with this mod then sends both halves to everyone, and each game combines them: each
    /// player's damage of each type is split across their weapons by what each weapon dealt of
    /// that type. Players without the mod still show, by weapon skill.
    /// </summary>
    internal static class BossFights
    {
        private const string RpcDown = "KroolWorldUI_BossDown";
        private const string RpcPart = "KroolWorldUI_BossPart";
        private const int Format = 1;
        private const float CollectSeconds = 2.5f;
        private const float LateSeconds = 10f;

        private const byte KindDirect = 0, KindDot = 1;

        private sealed class Bucket
        {
            public Who Who;          // Taken: the attacker. Outgoing: unused.
            public int Skill;
            public byte Kind;
            public string Label = "||";
            public int Hits;
            public float[] Dmg = new float[global::KroolWorldUI.Features.Dmg.Count];
        }

        private sealed class Fight
        {
            public string Name, Prefab;
            public float MaxHealth;
            public double Start, Last;
            // Watched on games that do not run the boss, in case the one that does lacks this mod.
            public Character Boss;
            public float LastHealth = float.MaxValue;
            public Vector3 LastPosition;
            public float GoneAt = -1f;
            public readonly Dictionary<string, Bucket> Taken = new Dictionary<string, Bucket>();
            public readonly Dictionary<string, Bucket> Outgoing = new Dictionary<string, Bucket>();
        }

        private sealed class Part
        {
            public string Sender;
            public double Start;
            public List<Bucket> Taken = new List<Bucket>();
            public List<Bucket> Outgoing = new List<Bucket>();
        }

        private sealed class Pending
        {
            public bool Announced;   // the owner's "boss died" message is in (parts can arrive first)
            public string Name, Prefab;
            public float MaxHealth;
            public double End;
            public Vector3 Position;
            public float Due;
            public float Until;
            public readonly List<Part> Parts = new List<Part>();
            public readonly HashSet<string> Senders = new HashSet<string>();
            public BossRecapData Shown;
        }

        private static readonly Dictionary<ZDOID, Fight> _fights = new Dictionary<ZDOID, Fight>();
        private static readonly Dictionary<ZDOID, Pending> _pending = new Dictionary<ZDOID, Pending>();

        /// <summary>The most recent boss recaps, newest first.</summary>
        internal static readonly List<BossRecapData> History = new List<BossRecapData>();

        internal static void Clear()
        {
            _fights.Clear();
            _pending.Clear();
        }

        private static double Now => ZNet.instance != null ? ZNet.instance.GetTimeSeconds() : Time.time;

        private static Fight FightFor(Character boss)
        {
            var id = boss.GetZDOID();
            if (!_fights.TryGetValue(id, out var f))
            {
                _fights[id] = f = new Fight
                {
                    Name = boss.m_name, Prefab = Utils.GetPrefabName(boss.gameObject),
                    MaxHealth = boss.GetMaxHealth(), Start = Now,
                };
                Prune();
            }
            f.Last = Now;
            f.Boss = boss;
            f.GoneAt = -1f; // back in view: judge it afresh when it next vanishes
            return f;
        }

        /// <summary>Fights nobody has touched for 20 minutes are over (the boss despawned or everyone left).</summary>
        private static void Prune()
        {
            double cutoff = Now - 1200.0;
            foreach (var id in _fights.Where(kv => kv.Value.Last < cutoff).Select(kv => kv.Key).ToList())
                _fights.Remove(id);
        }

        /// <summary>A hit the boss took while this game runs it.</summary>
        internal static void Taken(Character boss, Who who, Skills.SkillType skill, float[] types, bool dot)
        {
            var f = FightFor(boss);
            byte kind = dot ? KindDot : KindDirect;
            string key = $"{who.Key}|{(int)skill}|{kind}";
            if (!f.Taken.TryGetValue(key, out var b))
                f.Taken[key] = b = new Bucket { Who = who, Skill = (int)skill, Kind = kind };
            for (int i = 0; i < types.Length; i++)
                b.Dmg[i] += types[i];
            if (!dot)
                b.Hits++;
        }

        /// <summary>The local player's hit on a boss as it leaves, with what it came from.</summary>
        internal static void Outgoing(Character boss, string label, HitData hit)
        {
            var f = FightFor(boss);
            string key = $"{label}|{(int)hit.m_skill}";
            if (!f.Outgoing.TryGetValue(key, out var b))
                f.Outgoing[key] = b = new Bucket { Skill = (int)hit.m_skill, Label = label };
            Dmg.Add(b.Dmg, hit.m_damage);
            b.Hits++;
        }

        // ------------------------------------------------------------------ network

        [HarmonyPatch(typeof(ZRoutedRpc), MethodType.Constructor, typeof(bool))]
        private static class Register
        {
            private static void Postfix(ZRoutedRpc __instance)
            {
                try
                {
                    __instance.Register<ZPackage>(RpcDown, OnDown);
                    __instance.Register<ZPackage>(RpcPart, OnPart);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"Boss recap (network): {e}");
                }
            }
        }

        /// <summary>The boss died in this game: tell every game, this one included.</summary>
        internal static void Died(Character boss)
        {
            var f = FightFor(boss);
            Plugin.Log.LogInfo($"Boss recap: {f.Prefab} died in this game; asking every game for its part.");
            Announce(boss.GetZDOID(), f, boss.transform.position);
        }

        /// <summary>
        /// When a boss dies, the game that runs it tells every player who hit it (the game's own
        /// kill count, Game.RPC_RegisterKill), whether or not that game has this mod. Its own
        /// announcement, when it has the mod, is sent before that over the same route, so arrives
        /// first; a fight still open here when the kill comes in was not announced, so start it here.
        /// </summary>
        [HarmonyPatch(typeof(Game), nameof(Game.RPC_RegisterKill))]
        private static class KillCounted
        {
            private static void Postfix(string enemyName)
            {
                try
                {
                    if (BossRecap.Enabled.Value && _fights.Count > 0)
                        Killed(enemyName);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"Boss recap (kill): {e}");
                }
            }
        }

        private static void Killed(string name)
        {
            ZDOID id = ZDOID.None;
            Fight f = null;
            foreach (var kv in _fights)
            {
                if (kv.Value.Name == name && (f == null || kv.Value.Last > f.Last))
                {
                    id = kv.Key;
                    f = kv.Value;
                }
            }
            if (f == null || (_pending.TryGetValue(id, out var p) && p.Announced))
                return;
            Plugin.Log.LogInfo($"Boss recap: the game counted a kill of {f.Prefab} that no game announced; announcing it from here.");
            Announce(id, f, f.Boss != null ? f.Boss.transform.position : f.LastPosition);
        }

        private static void Announce(ZDOID id, Fight f, Vector3 position)
        {
            var pkg = new ZPackage();
            pkg.Write(Format);
            pkg.Write(id);
            pkg.Write(f.Name ?? "");
            pkg.Write(f.Prefab ?? "");
            pkg.Write(f.MaxHealth);
            pkg.Write(Now);
            pkg.Write(position);
            ZRoutedRpc.instance?.InvokeRoutedRPC(ZRoutedRpc.Everybody, RpcDown, pkg);
        }

        private const float LowHealth = 0.15f;
        private const float NearMeters = 60f;
        private const float WaitSeconds = 1.5f;

        /// <summary>
        /// The game that runs a boss announces its death. If that game lacks this mod, the game's
        /// own kill count starts the recap (see KillCounted). Behind that, a game that does not
        /// run the boss watches it, and when it vanishes at low health with the local player close
        /// by (a boss is only unloaded when nobody is near), it waits a moment for an announcement
        /// and then makes one itself.
        /// </summary>
        private static void Watch()
        {
            if (_fights.Count == 0)
                return;
            var player = Player.m_localPlayer;
            foreach (var kv in _fights.ToList())
            {
                var f = kv.Value;
                // The game drops a dead or unloaded boss's network data first and the object itself
                // at the end of the frame; in between, its health reads as full. Gone already.
                if (f.Boss != null && f.Boss.GetZDOID().IsNone())
                    f.Boss = null;
                if (f.Boss != null)
                {
                    if (!f.Boss.IsOwner())
                    {
                        f.LastHealth = f.Boss.GetHealth();
                        f.LastPosition = f.Boss.transform.position;
                    }
                    continue;
                }
                if (f.GoneAt < 0f)
                {
                    bool near = player != null && Vector3.Distance(player.transform.position, f.LastPosition) < NearMeters;
                    bool low = f.LastHealth <= f.MaxHealth * LowHealth;
                    f.GoneAt = near && low ? Time.unscaledTime : float.MaxValue;
                    continue;
                }
                if (Time.unscaledTime < f.GoneAt + WaitSeconds)
                    continue;
                f.GoneAt = float.MaxValue;
                if (_pending.TryGetValue(kv.Key, out var p) && p.Announced)
                    continue;
                Plugin.Log.LogInfo($"Boss recap: {f.Prefab} vanished nearby at low health and no game announced it; announcing it from here.");
                Announce(kv.Key, f, f.LastPosition);
            }
        }

        private static Pending PendingFor(ZDOID id)
        {
            if (!_pending.TryGetValue(id, out var p))
            {
                _pending[id] = p = new Pending
                {
                    Due = Time.unscaledTime + CollectSeconds, Until = Time.unscaledTime + LateSeconds,
                };
            }
            return p;
        }

        private static void OnDown(long sender, ZPackage pkg)
        {
            try
            {
                if (!BossRecap.Enabled.Value || pkg.ReadInt() != Format)
                    return;
                var id = pkg.ReadZDOID();
                var p = PendingFor(id);
                if (p.Announced)
                    return;
                p.Announced = true;
                p.Name = pkg.ReadString();
                p.Prefab = pkg.ReadString();
                p.MaxHealth = pkg.ReadSingle();
                p.End = pkg.ReadDouble();
                p.Position = pkg.ReadVector3();
                Plugin.Log.LogInfo($"Boss recap: {p.Prefab} is down; this game {(_fights.ContainsKey(id) ? "sends its part" : "saw none of the fight")}.");
                SendPart(id);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Boss recap (down): {e}");
            }
        }

        private static string LocalName => Player.m_localPlayer != null ? Player.m_localPlayer.GetPlayerName()
            : Game.instance?.GetPlayerProfile()?.GetName() ?? "";

        /// <summary>Sends what this game saw of the fight to everyone (and to itself), then forgets it.</summary>
        private static void SendPart(ZDOID id)
        {
            if (!_fights.TryGetValue(id, out var f))
                return;
            _fights.Remove(id);
            var pkg = new ZPackage();
            pkg.Write(Format);
            pkg.Write(id);
            pkg.Write(LocalName);
            pkg.Write(f.Start);
            pkg.Write(f.Taken.Count);
            foreach (var b in f.Taken.Values)
            {
                pkg.Write(b.Who.Key);
                pkg.Write(b.Who.Name ?? "");
                pkg.Write(b.Who.Prefab ?? "");
                pkg.Write(b.Who.IsPlayer);
                pkg.Write(b.Who.Level);
                pkg.Write((int)b.Who.Cause);
                WriteBucket(pkg, b);
            }
            pkg.Write(f.Outgoing.Count);
            foreach (var b in f.Outgoing.Values)
            {
                pkg.Write(b.Label);
                WriteBucket(pkg, b);
            }
            ZRoutedRpc.instance?.InvokeRoutedRPC(ZRoutedRpc.Everybody, RpcPart, pkg);
        }

        private static void WriteBucket(ZPackage pkg, Bucket b)
        {
            pkg.Write(b.Skill);
            pkg.Write((int)b.Kind);
            pkg.Write(b.Hits);
            pkg.Write(Dmg.Count);
            foreach (float v in b.Dmg)
                pkg.Write(v);
        }

        private static void ReadBucket(ZPackage pkg, Bucket b)
        {
            b.Skill = pkg.ReadInt();
            b.Kind = (byte)pkg.ReadInt();
            b.Hits = pkg.ReadInt();
            int n = pkg.ReadInt();
            for (int i = 0; i < n; i++)
            {
                float v = pkg.ReadSingle();
                if (i < Dmg.Count)
                    b.Dmg[i] = v;
            }
        }

        private static void OnPart(long sender, ZPackage pkg)
        {
            try
            {
                if (!BossRecap.Enabled.Value || pkg.ReadInt() != Format)
                    return;
                var id = pkg.ReadZDOID();
                var part = new Part { Sender = pkg.ReadString(), Start = pkg.ReadDouble() };
                int n = pkg.ReadInt();
                for (int i = 0; i < n; i++)
                {
                    var who = new Who
                    {
                        Key = pkg.ReadString(), Name = pkg.ReadString(), Prefab = pkg.ReadString(),
                        IsPlayer = pkg.ReadBool(), Level = pkg.ReadInt(), Cause = (HitData.HitType)pkg.ReadInt(),
                    };
                    var b = new Bucket { Who = who };
                    ReadBucket(pkg, b);
                    part.Taken.Add(b);
                }
                n = pkg.ReadInt();
                for (int i = 0; i < n; i++)
                {
                    var b = new Bucket { Label = pkg.ReadString() };
                    ReadBucket(pkg, b);
                    part.Outgoing.Add(b);
                }
                var p = PendingFor(id);
                if (Time.unscaledTime > p.Until)
                    return;
                string senderKey = sender + "/" + part.Sender;
                if (!p.Senders.Add(senderKey))
                    return;
                p.Parts.Add(part);
                if (p.Shown != null)
                    Publish(id, p); // a late part: update the recap already on screen
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Boss recap (part): {e}");
            }
        }

        /// <summary>Called every frame (from the HUD): turns collected fights into recaps once their parts are in.</summary>
        internal static void Update()
        {
            Watch();
            if (_pending.Count == 0)
                return;
            float now = Time.unscaledTime;
            foreach (var kv in _pending.ToList())
            {
                var p = kv.Value;
                if (p.Shown == null && p.Announced && now >= p.Due)
                    Publish(kv.Key, p);
                if (now > p.Until)
                {
                    if (p.Shown == null && p.Announced)
                        Plugin.Log.LogInfo($"Boss recap: {p.Prefab}: no game recorded any damage to it ({p.Parts.Count} parts); nothing to show.");
                    _pending.Remove(kv.Key);
                }
            }
        }

        private static void Publish(ZDOID id, Pending p)
        {
            var recap = Build(p);
            if (recap == null)
                return;
            bool wasShown = p.Shown != null;
            if (wasShown)
                History.Remove(p.Shown);
            else if (!Involved(p, recap))
            {
                Plugin.Log.LogInfo($"Boss recap: {p.Prefab}: you neither hit it nor were near; not shown.");
                p.Shown = recap; // remember, but nothing to show here
                p.Until = 0f;
                return;
            }
            if (!wasShown)
                Plugin.Log.LogInfo($"Boss recap: {p.Prefab}: {recap.Fighters.Count} fighters, from {p.Parts.Count} games' parts.");
            p.Shown = recap;
            History.Insert(0, recap);
            if (History.Count > 10)
                History.RemoveRange(10, History.Count - 10);
            BossRecap.Show(recap, wasShown);
        }

        /// <summary>The local player hit the boss, or was close when it died.</summary>
        private static bool Involved(Pending p, BossRecapData recap)
        {
            if (recap.Fighters.Any(f => f.IsLocal))
                return true;
            var player = Player.m_localPlayer;
            return player != null && Vector3.Distance(player.transform.position, p.Position) < 80f;
        }

        // ------------------------------------------------------------------ combining

        private static BossRecapData Build(Pending p)
        {
            string me = LocalName;
            double start = p.Parts.Count > 0 ? p.Parts.Min(x => x.Start) : p.End;
            var recap = new BossRecapData
            {
                Name = p.Name, Prefab = p.Prefab, MaxHealth = p.MaxHealth, When = DateTime.Now,
                Seconds = (float)Math.Max(0.0, p.End - start),
            };

            // What the boss took, per attacker, weapon skill and kind (direct or over time).
            var taken = new Dictionary<string, Bucket>();
            var whos = new Dictionary<string, Who>();
            foreach (var part in p.Parts)
            {
                foreach (var b in part.Taken)
                {
                    whos[b.Who.Key] = b.Who;
                    string key = $"{b.Who.Key}|{b.Skill}|{b.Kind}";
                    if (!taken.TryGetValue(key, out var sum))
                        taken[key] = sum = new Bucket { Who = b.Who, Skill = b.Skill, Kind = b.Kind };
                    Add(sum, b);
                }
            }

            // What each player says they hit with.
            var outgoing = new Dictionary<string, List<Bucket>>();
            foreach (var part in p.Parts)
            {
                if (part.Outgoing.Count == 0 || string.IsNullOrEmpty(part.Sender))
                    continue;
                string key = "p:" + part.Sender;
                if (!outgoing.TryGetValue(key, out var list))
                    outgoing[key] = list = new List<Bucket>();
                foreach (var b in part.Outgoing)
                {
                    var same = list.FirstOrDefault(x => x.Label == b.Label && x.Skill == b.Skill);
                    if (same == null)
                        list.Add(same = new Bucket { Label = b.Label, Skill = b.Skill });
                    Add(same, b);
                }
                if (!whos.ContainsKey(key))
                    whos[key] = Who.Player(part.Sender);
            }

            foreach (var whoKey in whos.Keys)
            {
                var who = whos[whoKey];
                var fighter = new RecapFighter { Who = who, IsLocal = who.IsPlayer && who.Name == me };
                var sources = new Dictionary<string, RecapSource>();
                outgoing.TryGetValue(whoKey, out var mine);
                var theirs = taken.Values.Where(b => b.Who.Key == whoKey).ToList();

                if (theirs.Count == 0 && mine != null)
                {
                    // Nobody running the boss had the mod: their own numbers, before the boss's resistances.
                    fighter.Estimated = true;
                    foreach (var b in mine)
                        Credit(sources, b.Label, b.Skill, -1, b.Dmg, 1f, b.Hits);
                }
                else
                {
                    foreach (var b in theirs)
                    {
                        for (int t = 0; t < Dmg.Count; t++)
                        {
                            float amount = b.Dmg[t];
                            if (amount <= 0f)
                                continue;
                            // The player's own lines with this skill that dealt this type, by how much each dealt.
                            var lines = mine?.Where(m => m.Skill == b.Skill && m.Dmg[t] > 0f).ToList();
                            float raw = lines?.Sum(m => m.Dmg[t]) ?? 0f;
                            if (raw <= 0f)
                            {
                                Credit(sources, null, b.Skill, t, OneType(t, amount), 1f, 0);
                                continue;
                            }
                            foreach (var m in lines)
                                Credit(sources, m.Label, m.Skill, -1, OneType(t, amount), m.Dmg[t] / raw, 0);
                        }
                    }
                    // Hits: the player's own count per line where they have one, the boss's count otherwise.
                    var direct = theirs.Where(b => b.Kind == KindDirect).ToList();
                    if (mine != null)
                    {
                        foreach (var m in mine)
                            if (sources.TryGetValue(Key(m.Label, m.Skill, -1), out var s))
                                s.Hits += m.Hits;
                    }
                    foreach (var b in direct)
                    {
                        if (b.Skill == 0)
                            continue; // lines named by damage type would all share one count; leave it out
                        foreach (var s in sources.Values.Where(s => s.Weapon == null && s.Skill == b.Skill))
                            s.Hits = Math.Max(s.Hits, b.Hits);
                    }
                }

                foreach (var s in sources.Values)
                {
                    s.Total = Dmg.Sum(s.Types);
                    for (int t = 0; t < Dmg.Count; t++)
                        fighter.Types[t] += s.Types[t];
                }
                fighter.Total = Dmg.Sum(fighter.Types);
                if (fighter.Total < 0.5f)
                    continue;
                fighter.Sources = sources.Values.Where(s => s.Total >= 0.5f).OrderByDescending(s => s.Total).ToList();
                fighter.Hits = theirs.Where(b => b.Kind == KindDirect).Sum(b => b.Hits);
                if (fighter.Hits == 0)
                    fighter.Hits = mine?.Sum(m => m.Hits) ?? 0;
                recap.Fighters.Add(fighter);
            }

            if (recap.Fighters.Count == 0)
                return null;
            recap.Fighters = recap.Fighters.OrderByDescending(f => f.Total).ToList();
            recap.Total = recap.Fighters.Sum(f => f.Total);
            return recap;
        }

        private static void Add(Bucket into, Bucket b)
        {
            into.Hits += b.Hits;
            for (int i = 0; i < Dmg.Count; i++)
                into.Dmg[i] += b.Dmg[i];
        }

        private static float[] OneType(int type, float amount)
        {
            var a = new float[Dmg.Count];
            a[type] = amount;
            return a;
        }

        private static string Key(string label, int skill, int type) =>
            label != null ? label : $"skill:{skill}:{type}";

        private static void Credit(Dictionary<string, RecapSource> sources, string label, int skill, int type,
            float[] dmg, float frac, int hits)
        {
            // Lines without an item are named by their weapon skill, or by damage type when there is no skill.
            if (label == null && skill != 0)
                type = -1;
            string key = Key(label, skill, type);
            if (!sources.TryGetValue(key, out var s))
            {
                s = new RecapSource { Skill = skill, Type = type };
                if (label != null)
                {
                    var parts = label.Split('|');
                    s.Weapon = parts.Length > 0 ? parts[0] : "";
                    s.Ammo = parts.Length > 1 ? parts[1] : "";
                    s.Effect = parts.Length > 2 ? parts[2] : "";
                }
                sources[key] = s;
            }
            for (int i = 0; i < Dmg.Count; i++)
                s.Types[i] += dmg[i] * frac;
            s.Hits += hits;
        }
    }
}
