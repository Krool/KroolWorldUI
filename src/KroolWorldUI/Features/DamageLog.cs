using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace KroolWorldUI.Features
{
    /// <summary>Damage types in display order, read out of the game's DamageTypes.</summary>
    internal static class Dmg
    {
        internal const int Blunt = 0, Slash = 1, Pierce = 2, Fire = 3, Frost = 4, Lightning = 5, Poison = 6, Spirit = 7,
            Chop = 8, Pickaxe = 9, Other = 10, Count = 11;

        internal static readonly string[] Tokens =
        {
            "$inventory_blunt", "$inventory_slash", "$inventory_pierce", "$inventory_fire", "$inventory_frost",
            "$inventory_lightning", "$inventory_poison", "$inventory_spirit", "$inventory_chop", "$inventory_pickaxe",
            "$inventory_damage",
        };

        internal static readonly string[] English =
            { "Blunt", "Slash", "Pierce", "Fire", "Frost", "Lightning", "Poison", "Spirit", "Chop", "Pickaxe", "Damage" };

        internal static readonly HitData.DamageType[] GameTypes =
        {
            HitData.DamageType.Blunt, HitData.DamageType.Slash, HitData.DamageType.Pierce, HitData.DamageType.Fire,
            HitData.DamageType.Frost, HitData.DamageType.Lightning, HitData.DamageType.Poison, HitData.DamageType.Spirit,
            HitData.DamageType.Chop, HitData.DamageType.Pickaxe, HitData.DamageType.Damage,
        };

        internal static string Name(int type) => L10n.Game(Tokens[type], English[type]);

        internal static void Add(float[] into, HitData.DamageTypes d, float scale = 1f)
        {
            into[Blunt] += d.m_blunt * scale;
            into[Slash] += d.m_slash * scale;
            into[Pierce] += d.m_pierce * scale;
            into[Fire] += d.m_fire * scale;
            into[Frost] += d.m_frost * scale;
            into[Lightning] += d.m_lightning * scale;
            into[Poison] += d.m_poison * scale;
            into[Spirit] += d.m_spirit * scale;
            into[Chop] += d.m_chop * scale;
            into[Pickaxe] += d.m_pickaxe * scale;
            into[Other] += (d.m_damage + d.m_nonPlayer) * scale;
        }

        internal static float Sum(float[] a)
        {
            float s = 0f;
            for (int i = 0; i < a.Length; i++)
                s += a[i];
            return s;
        }

        /// <summary>The split of <paramref name="d"/> by type, scaled so it adds up to <paramref name="total"/>.</summary>
        internal static float[] Split(HitData.DamageTypes d, float total)
        {
            var a = new float[Count];
            Add(a, d);
            float s = Sum(a);
            if (s <= 0f)
            {
                a[Other] = total;
                return a;
            }
            for (int i = 0; i < Count; i++)
                a[i] *= total / s;
            return a;
        }

        internal static int Largest(float[] a)
        {
            int best = Other;
            float max = 0f;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] > max)
                {
                    max = a[i];
                    best = i;
                }
            }
            return best;
        }
    }

    /// <summary>Who dealt a hit: a player (by name), a creature (by prefab) or the world (fall, drowning...).</summary>
    internal sealed class Who
    {
        public string Key;      // "p:Name", "c:Prefab:level" or "e:HitType"
        public string Name;     // player name, or a token to localize when shown
        public string Prefab;   // creature prefab, for its trophy icon
        public bool IsPlayer;
        public int Level;
        public HitData.HitType Cause;

        internal static Who Of(Character c, HitData.HitType cause)
        {
            if (c == null)
                return World(cause);
            if (c is Player p)
                return new Who { Key = "p:" + p.GetPlayerName(), Name = p.GetPlayerName(), IsPlayer = true, Cause = cause };
            string prefab = Utils.GetPrefabName(c.gameObject);
            int level = c.GetLevel();
            return new Who { Key = $"c:{prefab}:{level}", Name = c.m_name, Prefab = prefab, Level = level, Cause = cause };
        }

        internal static Who World(HitData.HitType cause) =>
            new Who { Key = "e:" + (int)cause, Name = "", Cause = cause };

        internal static Who Player(string name) => new Who { Key = "p:" + name, Name = name, IsPlayer = true };
    }

    /// <summary>One hit you took, kept for the death recap.</summary>
    internal sealed class HitEvent
    {
        public float Time;      // Time.time
        public Who Who;
        public float Amount;    // what the hit took off (past 0 on the killing blow)
        public float Raw;       // the hit before armor, resistances and blocking (direct hits only)
        public float[] Types;
        public bool Dot;        // a burning or poison tick
        public bool Blocked;
        public float HealthAfter;
        public float Overkill;  // the part of the hit past 0 health (the killing blow only)
    }

    /// <summary>
    /// Records damage for the death recap (every hit the local player takes) and the boss recap
    /// (every hit a boss takes while this game runs it, plus which weapon or arrow each of your
    /// own hits came from). Read-only: Harmony prefixes and postfixes that only look.
    ///
    /// Damage over time is credited to whoever set it: fire, spirit and poison from a hit go into
    /// the target's burning or poison effect and come off later in ticks with no attacker, so each
    /// target keeps a pool per effect of who put how much in, and a tick is split by those shares.
    /// </summary>
    internal static class DamageLog
    {
        // ------------------------------------------------------------------ context

        /// <summary>What Character.RPC_Damage is working on, so ApplyDamage inside it knows the attacker.</summary>
        private sealed class Ctx
        {
            public Character Target;
            public Who Who;
            public Skills.SkillType Skill;
            public float RawDirect;
            public bool Blocked;
        }

        private static readonly List<Ctx> _ctx = new List<Ctx>();

        private static Ctx Top(Character target)
        {
            for (int i = _ctx.Count - 1; i >= 0; i--)
                if (_ctx[i].Target == target)
                    return _ctx[i];
            return null;
        }

        private static bool Wanted(Character c, bool needOwner)
        {
            if (c == null)
                return false;
            bool local = c == Player.m_localPlayer && DeathRecap.Enabled.Value;
            bool boss = c.m_boss && BossRecap.Enabled.Value;
            if (!local && !boss)
                return false;
            return !needOwner || c.IsOwner();
        }

        [HarmonyPatch(typeof(Character), "RPC_Damage")]
        private static class TrackRpcDamage
        {
            [HarmonyPriority(Priority.First)]
            private static void Prefix(Character __instance, HitData hit, ref bool __state)
            {
                __state = false;
                try
                {
                    if (!Wanted(__instance, true))
                        return;
                    var attacker = hit.GetAttacker();
                    var d = hit.m_damage;
                    // The hit as the game will size it before armor and blocking: world level and
                    // difficulty included, burning and poison left out (they come later as ticks).
                    float raw = hit.GetTotalDamage() - d.m_fire - d.m_poison - d.m_spirit;
                    if (attacker != null && !attacker.IsPlayer() && Game.instance != null)
                        raw *= Game.instance.GetDifficultyDamageScalePlayer(__instance.transform.position) * Game.m_enemyDamageRate;
                    if (__instance.IsPlayer())
                        raw *= Game.m_localDamgeTakenRate;
                    var ctx = new Ctx
                    {
                        Target = __instance,
                        Who = Who.Of(attacker, hit.m_hitType),
                        Skill = hit.m_skill,
                        RawDirect = raw,
                        Blocked = hit.m_blockable && __instance.IsBlocking(),
                    };
                    _ctx.Add(ctx);
                    __state = true;
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"Damage log: {e}");
                }
            }

            private static Exception Finalizer(Exception __exception, bool __state)
            {
                if (__state && _ctx.Count > 0)
                    _ctx.RemoveAt(_ctx.Count - 1);
                return __exception;
            }
        }

        private struct Before
        {
            public float Health;
            public float Fire, Spirit, Poison;
        }

        [HarmonyPatch(typeof(Character), nameof(Character.ApplyDamage))]
        private static class TrackApplyDamage
        {
            private static void Prefix(Character __instance, HitData hit, ref Before __state)
            {
                __state.Health = -1f;
                try
                {
                    if (!Wanted(__instance, true))
                        return;
                    __state.Health = __instance.GetHealth();
                    __state.Fire = hit.m_damage.m_fire;
                    __state.Spirit = hit.m_damage.m_spirit;
                    __state.Poison = hit.m_damage.m_poison;
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"Damage log: {e}");
                }
            }

            private static void Postfix(Character __instance, HitData hit, Before __state)
            {
                if (__state.Health < 0f)
                    return;
                try
                {
                    float after = __instance.GetHealth();
                    float lost = __state.Health - Mathf.Max(0f, after);
                    float total = hit.GetTotalDamage();
                    if (lost <= 0.01f || total <= 0f)
                        return;
                    Record(__instance, hit, __state, lost, total, after);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"Damage log: {e}");
                }
            }
        }

        private static void Record(Character target, HitData hit, Before before, float lost, float total, float after)
        {
            bool local = target == Player.m_localPlayer;
            // The local player's recap shows the hit's full size (the killing blow past 0); a boss's counts what it took off.
            float amount = local ? total : lost;
            float overkill = local ? Mathf.Max(0f, total - lost) : 0f;
            var ctx = Top(target);
            if (ctx != null)
            {
                Credit(target, ctx.Who, ctx.Skill, Dmg.Split(hit.m_damage, amount), false,
                    ctx.RawDirect, ctx.Blocked, after, overkill);
                return;
            }

            // A tick of burning (fire or spirit) or poison: split by who put how much in.
            if (hit.m_hitType == HitData.HitType.Burning || hit.m_hitType == HitData.HitType.Poisoned)
            {
                int pool = hit.m_hitType == HitData.HitType.Poisoned ? PoolPoison : before.Spirit > before.Fire ? PoolSpirit : PoolFire;
                float raw = pool == PoolPoison ? before.Poison : pool == PoolSpirit ? before.Spirit : before.Fire;
                var types = Dmg.Split(hit.m_damage, amount);
                if (CreditPool(target, pool, raw, types, after, overkill))
                    return;
                Credit(target, Who.World(hit.m_hitType), Skills.SkillType.None, types, true, 0f, false, after, overkill);
                return;
            }

            Credit(target, Who.Of(hit.GetAttacker(), hit.m_hitType), hit.m_skill,
                Dmg.Split(hit.m_damage, amount), false, 0f, false, after, overkill);
        }

        private static void Credit(Character target, Who who, Skills.SkillType skill, float[] types,
            bool dot, float raw, bool blocked, float healthAfter, float overkill)
        {
            if (target == Player.m_localPlayer)
            {
                float amount = Dmg.Sum(types);
                _taken.Add(new HitEvent
                {
                    Time = Time.time, Who = who, Amount = amount, Raw = dot ? amount : Mathf.Max(raw, 0f),
                    Types = types, Dot = dot, Blocked = blocked, HealthAfter = healthAfter, Overkill = overkill,
                });
                TrimTaken();
                return;
            }
            if (target.m_boss)
                BossFights.Taken(target, who, skill, types, dot);
        }

        // ------------------------------------------------------------------ damage over time

        private const int PoolFire = 0, PoolSpirit = 1, PoolPoison = 2;

        private sealed class Share
        {
            public Who Who;
            public Skills.SkillType Skill;
            public float Left;
        }

        private static readonly Dictionary<Character, List<Share>[]> _pools = new Dictionary<Character, List<Share>[]>();
        private static AccessTools.FieldRef<SE_Poison, float> _poisonLeft;
        private static AccessTools.FieldRef<SE_Burning, float> _fireLeft, _spiritLeft;

        private static List<Share> Pool(Character c, int pool)
        {
            if (!_pools.TryGetValue(c, out var pools))
                _pools[c] = pools = new[] { new List<Share>(), new List<Share>(), new List<Share>() };
            return pools[pool];
        }

        private static void AddShare(List<Share> list, Ctx ctx, float amount)
        {
            foreach (var s in list)
            {
                if (s.Who.Key == ctx.Who.Key && s.Skill == ctx.Skill)
                {
                    s.Left += amount;
                    return;
                }
            }
            list.Add(new Share { Who = ctx.Who, Skill = ctx.Skill, Left = amount });
        }

        [HarmonyPatch(typeof(Character), "AddFireDamage")]
        private static class FireIn
        {
            private static void Prefix(Character __instance, float damage) => Burn(__instance, damage, PoolFire, SEMan.s_statusEffectBurning);
        }

        [HarmonyPatch(typeof(Character), "AddSpiritDamage")]
        private static class SpiritIn
        {
            private static void Prefix(Character __instance, float damage) => Burn(__instance, damage, PoolSpirit, SEMan.s_statusEffectSpirit);
        }

        private static void Burn(Character target, float damage, int pool, int effect)
        {
            try
            {
                var ctx = damage > 0f ? Top(target) : null;
                if (ctx == null)
                    return;
                var list = Pool(target, pool);
                // Burning adds up while it lasts; a new one (or one whose fire has run out, which a
                // weapon's own burn effect can leave behind) starts from nothing.
                _fireLeft ??= AccessTools.FieldRefAccess<SE_Burning, float>("m_fireDamageLeft");
                _spiritLeft ??= AccessTools.FieldRefAccess<SE_Burning, float>("m_spiritDamageLeft");
                var se = target.GetSEMan().GetStatusEffect(effect) as SE_Burning;
                if (se == null || (pool == PoolFire ? _fireLeft(se) : _spiritLeft(se)) <= 0f)
                    list.Clear();
                AddShare(list, ctx, damage);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Damage log (burning): {e}");
            }
        }

        [HarmonyPatch(typeof(Character), "AddPoisonDamage")]
        private static class PoisonIn
        {
            private static void Prefix(Character __instance, float damage)
            {
                try
                {
                    var ctx = damage > 0f ? Top(__instance) : null;
                    if (ctx == null)
                        return;
                    // Poison does not add up: a stronger dose replaces what is left, a weaker one is ignored.
                    var se = __instance.GetSEMan().GetStatusEffect(SEMan.s_statusEffectPoison) as SE_Poison;
                    _poisonLeft ??= AccessTools.FieldRefAccess<SE_Poison, float>("m_damageLeft");
                    if (se != null && damage < _poisonLeft(se))
                        return;
                    var list = Pool(__instance, PoolPoison);
                    list.Clear();
                    AddShare(list, ctx, damage);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"Damage log (poison): {e}");
                }
            }
        }

        /// <summary>Splits one tick between the pool's shares and uses up what each put in. False when the pool is empty.</summary>
        private static bool CreditPool(Character target, int pool, float raw, float[] types, float healthAfter, float overkill)
        {
            if (!_pools.TryGetValue(target, out var pools))
                return false;
            var list = pools[pool];
            float sum = 0f;
            foreach (var s in list)
                sum += s.Left;
            if (sum <= 0.001f)
                return false;
            foreach (var s in list.ToArray())
            {
                float frac = s.Left / sum;
                var part = new float[Dmg.Count];
                for (int i = 0; i < Dmg.Count; i++)
                    part[i] = types[i] * frac;
                Credit(target, s.Who, s.Skill, part, true, 0f, false, healthAfter, overkill * frac);
                s.Left -= Mathf.Max(raw, 0f) * frac;
            }
            list.RemoveAll(s => s.Left <= 0.01f);
            return true;
        }

        [HarmonyPatch(typeof(Character), "OnDestroy")]
        private static class Forget
        {
            private static void Postfix(Character __instance)
            {
                _pools.Remove(__instance);
            }
        }

        // ------------------------------------------------------------------ the local player's hits taken

        private static readonly List<HitEvent> _taken = new List<HitEvent>();

        private static void TrimTaken()
        {
            float cutoff = Time.time - 120f;
            int drop = 0;
            while (drop < _taken.Count && _taken[drop].Time < cutoff)
                drop++;
            if (drop > 0)
                _taken.RemoveRange(0, drop);
            if (_taken.Count > 400)
                _taken.RemoveRange(0, _taken.Count - 400);
        }

        [HarmonyPatch(typeof(Player), nameof(Player.OnDeath))]
        private static class LocalDeath
        {
            [HarmonyPriority(Priority.First)]
            private static void Prefix(Player __instance)
            {
                try
                {
                    if (__instance != Player.m_localPlayer || !DeathRecap.Enabled.Value)
                        return;
                    DeathRecap.Record(new List<HitEvent>(_taken), __instance.GetMaxHealth());
                    _taken.Clear();
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"Death recap: {e}");
                }
            }
        }

        // ------------------------------------------------------------------ the local player's own hits on bosses

        /// <summary>
        /// What the local player is hitting with right now: set while one of their melee swings,
        /// projectiles or area hits is dealing damage, read when that damage reaches a boss.
        /// "weaponPrefab|ammoPrefab|effect".
        /// </summary>
        private static readonly List<string> _sources = new List<string>();

        private static string PendingLabel => _sources.Count > 0 ? _sources[_sources.Count - 1] : "||";

        internal static string Label(ItemDrop.ItemData weapon, ItemDrop.ItemData ammo, string effect)
        {
            string w = weapon?.m_dropPrefab != null ? weapon.m_dropPrefab.name : "";
            string a = ammo?.m_dropPrefab != null ? ammo.m_dropPrefab.name : "";
            return $"{w}|{a}|{effect ?? ""}";
        }

        private static bool Mine(Character c) => c != null && c == Player.m_localPlayer;

        private static AccessTools.FieldRef<Attack, Humanoid> _attackCharacter;
        private static AccessTools.FieldRef<Attack, ItemDrop.ItemData> _attackWeapon;
        private static AccessTools.FieldRef<Projectile, Character> _projOwner;
        private static AccessTools.FieldRef<Projectile, ItemDrop.ItemData> _projWeapon, _projAmmo;
        private static AccessTools.FieldRef<Aoe, Character> _aoeOwner;
        private static AccessTools.FieldRef<Aoe, ItemDrop.ItemData> _aoeItem, _aoeAmmo;

        private static void Push(string label) => _sources.Add(label);

        private static void Pop(bool pushed)
        {
            if (pushed && _sources.Count > 0)
                _sources.RemoveAt(_sources.Count - 1);
        }

        [HarmonyPatch(typeof(Attack), "DoMeleeAttack")]
        private static class MeleeSource
        {
            private static void Prefix(Attack __instance, ref bool __state) => __state = FromAttack(__instance);
            private static Exception Finalizer(Exception __exception, bool __state) { Pop(__state); return __exception; }
        }

        [HarmonyPatch(typeof(Attack), "DoAreaAttack")]
        private static class AreaSource
        {
            private static void Prefix(Attack __instance, ref bool __state) => __state = FromAttack(__instance);
            private static Exception Finalizer(Exception __exception, bool __state) { Pop(__state); return __exception; }
        }

        private static bool FromAttack(Attack attack)
        {
            try
            {
                if (!BossRecap.Enabled.Value)
                    return false;
                _attackCharacter ??= AccessTools.FieldRefAccess<Attack, Humanoid>("m_character");
                _attackWeapon ??= AccessTools.FieldRefAccess<Attack, ItemDrop.ItemData>("m_weapon");
                if (!Mine(_attackCharacter(attack)))
                    return false;
                Push(Label(_attackWeapon(attack), null, null));
                return true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Damage log (attack): {e}");
                return false;
            }
        }

        [HarmonyPatch(typeof(Projectile), nameof(Projectile.OnHit))]
        private static class ProjectileSource
        {
            private static void Prefix(Projectile __instance, ref bool __state)
            {
                __state = false;
                try
                {
                    if (!BossRecap.Enabled.Value)
                        return;
                    _projOwner ??= AccessTools.FieldRefAccess<Projectile, Character>("m_owner");
                    _projWeapon ??= AccessTools.FieldRefAccess<Projectile, ItemDrop.ItemData>("m_weapon");
                    _projAmmo ??= AccessTools.FieldRefAccess<Projectile, ItemDrop.ItemData>("m_ammo");
                    if (!Mine(_projOwner(__instance)))
                        return;
                    Push(Label(_projWeapon(__instance), _projAmmo(__instance), null));
                    __state = true;
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"Damage log (projectile): {e}");
                }
            }

            private static Exception Finalizer(Exception __exception, bool __state) { Pop(__state); return __exception; }
        }

        [HarmonyPatch(typeof(Aoe), "OnHit")]
        private static class AoeSource
        {
            private static void Prefix(Aoe __instance, ref bool __state)
            {
                __state = false;
                try
                {
                    if (!BossRecap.Enabled.Value)
                        return;
                    _aoeOwner ??= AccessTools.FieldRefAccess<Aoe, Character>("m_owner");
                    _aoeItem ??= AccessTools.FieldRefAccess<Aoe, ItemDrop.ItemData>("m_itemData");
                    _aoeAmmo ??= AccessTools.FieldRefAccess<Aoe, ItemDrop.ItemData>("m_ammo");
                    if (!Mine(_aoeOwner(__instance)))
                        return;
                    Push(Label(_aoeItem(__instance), _aoeAmmo(__instance), AoeEffect(Utils.GetPrefabName(__instance.gameObject))));
                    __state = true;
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"Damage log (area): {e}");
                }
            }

            private static Exception Finalizer(Exception __exception, bool __state) { Pop(__state); return __exception; }
        }

        /// <summary>Area hits from Epic Loot enchantments, by the prefab Epic Loot spawns for them.</summary>
        private static string AoeEffect(string prefab)
        {
            switch (prefab)
            {
                case "EL_ExplosiveArrow": return "ExplosiveArrows";
                case "ChainLightning": return "ChainLightning";
                case "skeleton_firenova_aoe": return "Firebrand";
                default: return ""; // the weapon's own blast (a staff's fireball): counts as the weapon
            }
        }

        // The local player's hit on a boss, as it leaves. Last of all patches, so Epic Loot's
        // attacker-side bonuses (crits, double damage, poison turned into another type) are in it;
        // the game sends a copy, so RPC_Damage never changes this one.
        [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
        private static class OutgoingToBoss
        {
            [HarmonyPriority(Priority.Last)]
            private static void Postfix(Character __instance, HitData hit)
            {
                try
                {
                    if (__instance == null || !__instance.m_boss || !BossRecap.Enabled.Value || Player.m_localPlayer == null)
                        return;
                    if (hit.m_attacker != Player.m_localPlayer.GetZDOID())
                        return;
                    BossFights.Outgoing(__instance, PendingLabel, hit);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"Boss recap (your hit): {e}");
                }
            }
        }

        // A prefix: OnDeath ends by destroying the object, after which it has no ZDO.
        [HarmonyPatch(typeof(Character), nameof(Character.OnDeath))]
        private static class BossDeath
        {
            private static void Prefix(Character __instance)
            {
                try
                {
                    if (__instance != null && __instance.m_boss && __instance.IsOwner() && BossRecap.Enabled.Value)
                        BossFights.Died(__instance);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"Boss recap: {e}");
                }
            }
        }

        [HarmonyPatch(typeof(Game), nameof(Game.Logout))]
        private static class Reset
        {
            private static void Postfix()
            {
                _ctx.Clear();
                _sources.Clear();
                _pools.Clear();
                _taken.Clear();
                BossFights.Clear();
            }
        }
    }
}
