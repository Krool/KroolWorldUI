using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;

namespace KroolWorldUI.Features
{
    /// <summary>
    /// Reads Epic Loot's enchantments by reflection when it is installed; nothing when it is not.
    /// Read-only: only Epic Loot's own getters are called. Values come back the way Epic Loot
    /// stores them (15 means 15%), unless a scale is given.
    /// </summary>
    internal static class EpicLootBridge
    {
        private static bool _tried;
        private static MethodInfo _total, _magicEquipment, _setEffects, _getMagicItem, _effectText, _defGet, _resist;
        private static FieldInfo _effects, _effectValue;
        private static MemberInfo _effectType;

        internal static bool Present
        {
            get
            {
                Bind();
                return _total != null;
            }
        }

        private static void Bind()
        {
            if (_tried)
                return;
            _tried = true;
            try
            {
                // Epic Loot's assembly, found by one of its types. Not AccessTools.TypeByName: without
                // Epic Loot that scans every loaded type and logs a Harmony warning on each player's game.
                Type ext = null;
                foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (a.IsDynamic)
                        continue;
                    try { ext = a.GetType("EpicLoot.PlayerExtensions", false); }
                    catch { continue; }
                    if (ext != null)
                        break;
                }
                if (ext == null)
                    return;
                var asm = ext.Assembly;
                Type T(string name) => asm.GetType(name, false);
                _total = AccessTools.Method(ext, "GetTotalActiveMagicEffectValue");
                _magicEquipment = AccessTools.Method(ext, "GetMagicEquipment");
                _setEffects = AccessTools.Method(ext, "GetAllActiveSetMagicEffects");
                _getMagicItem = AccessTools.Method(T("EpicLoot.ItemDataExtensions"), "GetMagicItem");
                var magicItem = T("EpicLoot.MagicItem");
                _effects = AccessTools.Field(magicItem, "Effects");
                var effect = T("EpicLoot.MagicItemEffect");
                _effectValue = AccessTools.Field(effect, "EffectValue");
                _effectType = (MemberInfo)AccessTools.Property(effect, "EffectType") ?? AccessTools.Field(effect, "EffectType");
                var defType = T("EpicLoot.MagicItemEffectDefinition");
                _effectText = defType != null ? AccessTools.Method(magicItem, "GetEffectText", new[] { defType, typeof(float) }) : null;
                _defGet = AccessTools.Method(T("EpicLoot.MagicItemEffectDefinitions"), "Get");
                _resist = AccessTools.Method(T("EpicLoot.MagicItemEffects.ModifyResistance"), "GetResistanceFraction");
                if (_total == null || _total.GetParameters().Length < 3)
                    _total = null;
                Plugin.Log.LogInfo(_total != null ? "Epic Loot found: its enchantments show in the stats window." : "Epic Loot found, but not a version the stats window can read.");
            }
            catch (Exception e)
            {
                _total = null;
                Plugin.Log.LogWarning($"Epic Loot not readable: {e.Message}");
            }
        }

        /// <summary>The player's total of one enchantment (items, sockets, set bonuses).</summary>
        internal static float Total(Player player, string type, float scale = 1f)
        {
            Bind();
            if (_total == null || player == null)
                return 0f;
            try
            {
                var args = _total.GetParameters().Length == 4
                    ? new object[] { player, type, scale, null }
                    : new object[] { player, type, scale };
                return (float)_total.Invoke(null, args);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Epic Loot total {type}: {e.Message}");
                return 0f;
            }
        }

        /// <summary>
        /// The share of a damage type Epic Loot's resistances take off before the game's own
        /// resistances and armor: the type's own resistance plus the elemental or physical group's,
        /// capped where Epic Loot caps it.
        /// </summary>
        internal static float Resistance(Player player, int dmgType, out float uncapped)
        {
            uncapped = 0f;
            string own = ResistEffect(dmgType);
            if (own == null || !Present)
                return 0f;
            bool elemental = dmgType >= Dmg.Fire && dmgType <= Dmg.Spirit;
            float group = Total(player, elemental ? "AddElementalResistancePercentage" : "AddPhysicalResistancePercentage", 0.01f);
            uncapped = Total(player, own, 0.01f) + group;
            if (_resist == null)
                return UnityEngine.Mathf.Clamp01(uncapped);
            try
            {
                return (float)_resist.Invoke(null, new object[] { player, own, group });
            }
            catch
            {
                return UnityEngine.Mathf.Clamp01(uncapped);
            }
        }

        internal static string ResistEffect(int dmgType)
        {
            switch (dmgType)
            {
                case Dmg.Blunt: return "AddBluntResistancePercentage";
                case Dmg.Slash: return "AddSlashingResistancePercentage";
                case Dmg.Pierce: return "AddPiercingResistancePercentage";
                case Dmg.Chop: return "AddChoppingResistancePercentage";
                case Dmg.Fire: return "AddFireResistancePercentage";
                case Dmg.Frost: return "AddFrostResistancePercentage";
                case Dmg.Lightning: return "AddLightningResistancePercentage";
                case Dmg.Poison: return "AddPoisonResistancePercentage";
                case Dmg.Spirit: return "AddSpiritResistancePercentage";
                default: return null;
            }
        }

        internal struct Effect
        {
            public string Type;
            public float Value;
            public ItemDrop.ItemData Item; // null for a set bonus
        }

        /// <summary>Every enchantment on what the player has equipped, item by item, then set bonuses.</summary>
        internal static List<Effect> Effects(Player player)
        {
            var list = new List<Effect>();
            Bind();
            if (_total == null || player == null || _magicEquipment == null || _getMagicItem == null || _effects == null)
                return list;
            try
            {
                if (_magicEquipment.Invoke(null, new object[] { player }) is IEnumerable items)
                {
                    foreach (var o in items)
                    {
                        if (!(o is ItemDrop.ItemData item))
                            continue;
                        var magic = _getMagicItem.Invoke(null, new object[] { item });
                        if (magic == null || !(_effects.GetValue(magic) is IEnumerable effects))
                            continue;
                        foreach (var e in effects)
                            list.Add(new Effect { Type = TypeOf(e), Value = ValueOf(e), Item = item });
                    }
                }
                if (_setEffects != null && _setEffects.Invoke(null, new object[] { player, null }) is IEnumerable sets)
                {
                    foreach (var e in sets)
                        list.Add(new Effect { Type = TypeOf(e), Value = ValueOf(e) });
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Epic Loot effects: {e.Message}");
            }
            list.RemoveAll(e => string.IsNullOrEmpty(e.Type));
            return list;
        }

        private static string TypeOf(object effect) =>
            _effectType is PropertyInfo p ? p.GetValue(effect) as string
            : _effectType is FieldInfo f ? f.GetValue(effect) as string : null;

        private static float ValueOf(object effect) =>
            _effectValue != null && _effectValue.GetValue(effect) is float v ? v : 0f;

        /// <summary>Epic Loot's own line for an enchantment ("Fire Resistance 15%"), in the player's language.</summary>
        internal static string Text(string type, float value)
        {
            Bind();
            try
            {
                var def = _defGet?.Invoke(null, new object[] { type });
                if (def != null && _effectText != null && _effectText.Invoke(null, new[] { def, (object)value }) is string s
                    && !string.IsNullOrEmpty(s))
                    return s;
            }
            catch
            {
                // fall through
            }
            return $"{ConfigText.Pretty(type)} {value:0.#}";
        }

        /// <summary>An enchantment's name without its number: "Explosive Shot" out of "Explosive Shot +{0}%".</summary>
        internal static string Name(string type, string fallback)
        {
            string token = $"$mod_epicloot_me_{type.ToLowerInvariant()}_display";
            string text = L10n.Game(token, "");
            if (string.IsNullOrEmpty(text))
                return fallback;
            text = Regex.Replace(text, @"\s*[+\-]?\s*\{0[^}]*\}\s*%?", " ").Trim();
            return text.Length > 0 ? text : fallback;
        }
    }
}
