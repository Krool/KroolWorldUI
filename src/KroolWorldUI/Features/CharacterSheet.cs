using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace KroolWorldUI.Features
{
    /// <summary>One source's share of a stat: a picture and a number, with its name on hover.</summary>
    internal sealed class StatChip
    {
        public Sprite Icon;
        public string Text;
        public string Tip;
        public bool Dim;        // present but doing nothing (a weaker resistance)
        public bool Live;       // changes by itself (food wearing off): updated in place, not rebuilt
        public Color Color = StatsUi.Parchment;
    }

    internal sealed class StatRow
    {
        public Sprite Icon;
        public string Label;
        public string Value;
        public Color ValueColor = StatsUi.White;
        public string Tip;
        public string Note;     // a muted line of text in place of the chips
        public bool LiveValue;  // the value changes by itself (a timer, food wearing off)
        public readonly List<StatChip> Chips = new List<StatChip>();
    }

    internal sealed class StatSection
    {
        public string Title;
        public string Note;
        public readonly List<StatRow> Rows = new List<StatRow>();
    }

    /// <summary>
    /// The character's stats as the game computes them, with where each part comes from.
    /// Totals always come from the game's own methods (GetMaxHealth, GetBodyArmor,
    /// GetDamageModifiers, the status effect manager...), which every mod patches, so they match
    /// play. The parts come from asking each item and each status effect for its own share in the
    /// same way the game does; anything left over (another mod) shows as one more part.
    /// Read-only.
    /// </summary>
    internal static class CharacterSheet
    {
        private static AccessTools.FieldRef<Humanoid, ItemDrop.ItemData> _chest, _legs, _helmet, _shoulder;

        internal static List<StatSection> Build(Player p)
        {
            _chest ??= AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>("m_chestItem");
            _legs ??= AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>("m_legItem");
            _helmet ??= AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>("m_helmetItem");
            _shoulder ??= AccessTools.FieldRefAccess<Humanoid, ItemDrop.ItemData>("m_shoulderItem");

            var gear = p.GetInventory().GetEquippedItems();
            var effects = p.GetSEMan().GetStatusEffects().ToList();
            var el = EpicLootBridge.Present ? EpicLootBridge.Effects(p) : new List<EpicLootBridge.Effect>();

            var list = new List<StatSection>
            {
                Vitals(p, effects, el),
                Movement(p, gear, effects, el),
                Armor(p, effects),
                Resistances(p, effects, el),
            };
            var fx = Effects(effects);
            if (fx.Rows.Count > 0)
                list.Add(fx);
            var ench = Enchantments(el);
            if (ench.Rows.Count > 0)
                list.Add(ench);
            return list;
        }

        // ------------------------------------------------------------------ helpers

        private static string Loc(string token) => Localization.instance != null ? Localization.instance.Localize(token) : token;

        private static string ItemName(ItemDrop.ItemData item) => item != null ? Loc(item.m_shared.m_name) : "";

        private static string EffectName(StatusEffect se) => Loc(se.m_name);

        private static StatChip Chip(Sprite icon, string text, string tip, Color? color = null) =>
            new StatChip { Icon = icon, Text = text, Tip = tip, Color = color ?? StatsUi.Parchment };

        private static Color Signed(float v, bool lowerIsBetter = false)
        {
            if (Mathf.Abs(v) < 0.005f)
                return StatsUi.Muted;
            return (v > 0f) != lowerIsBetter ? StatsUi.Good : StatsUi.Bad;
        }

        /// <summary>Epic Loot's share of one enchantment, item by item (set bonuses without a picture).</summary>
        private static float AddEnchantChips(StatRow row, List<EpicLootBridge.Effect> el, string type, float scale,
            Func<float, string> format, bool lowerIsBetter = false)
        {
            float sum = 0f;
            foreach (var e in el.Where(e => e.Type == type))
            {
                float v = e.Value * scale;
                if (Mathf.Abs(v) < 0.0001f)
                    continue;
                sum += v;
                string name = e.Item != null ? ItemName(e.Item) : L10n.T("stats_set_bonus");
                row.Chips.Add(Chip(e.Item?.GetIcon(), format(v), $"{name}\n{EpicLootBridge.Text(type, e.Value)}", Signed(v, lowerIsBetter)));
            }
            return sum;
        }

        /// <summary>What no known source explains: another mod. Shown only when it is not a rounding crumb.</summary>
        private static void AddRest(StatRow row, float rest, Func<float, string> format, float threshold, bool lowerIsBetter = false)
        {
            if (Mathf.Abs(rest) < threshold)
                return;
            row.Chips.Add(Chip(null, format(rest), L10n.T("stats_other_mods"), Signed(rest, lowerIsBetter)));
        }

        private static string Flat(float v) => (v > 0 ? "+" : "") + StatsUi.Int(v);

        // ------------------------------------------------------------------ health and stamina

        private static StatSection Vitals(Player p, List<StatusEffect> effects, List<EpicLootBridge.Effect> el)
        {
            var s = new StatSection { Title = L10n.T("stats_vitals") };
            var heart = HeartIcon();
            var foods = p.GetFoods();

            s.Rows.Add(Pool(L10n.Game("$se_health", "Health"), heart, p.GetMaxHealth(), p.m_baseHP,
                foods.Select(f => (f, f.m_health)), el, "IncreaseHealth"));
            s.Rows.Add(Pool(L10n.Game("$se_stamina", "Stamina"), StatsUi.SkillIcon(Skills.SkillType.Run), p.GetMaxStamina(),
                p.m_baseStamina, foods.Select(f => (f, f.m_stamina)), el, "IncreaseStamina"));
            float eitr = p.GetMaxEitr();
            if (eitr > 0.5f)
                s.Rows.Add(Pool(L10n.Game("$se_eitr", "Eitr"), StatsUi.ItemIcon("Eitr"), eitr, 0f,
                    foods.Select(f => (f, f.m_eitr)), el, "IncreaseEitr"));

            s.Rows.Add(Regen(L10n.Game("$se_healthregen", "Health regen"), heart, p, effects, el, "ModifyHealthRegen",
                (SEMan m, ref float v) => m.ModifyHealthRegen(ref v), (StatusEffect e, ref float v) => e.ModifyHealthRegen(ref v), 0f, null));
            s.Rows.Add(Regen(L10n.Game("$se_staminaregen", "Stamina regen"), StatsUi.SkillIcon(Skills.SkillType.Run), p, effects, el,
                "ModifyStaminaRegen",
                (SEMan m, ref float v) => m.ModifyStaminaRegen(ref v), (StatusEffect e, ref float v) => e.ModifyStaminaRegen(ref v), 0f, null));
            if (eitr > 0.5f)
            {
                // Gear adds to the eitr regen multiplier after the status effects (Player.UpdateStats).
                var gear = p.GetInventory().GetEquippedItems().Where(i => Mathf.Abs(i.m_shared.m_eitrRegenModifier) > 0.0001f).ToList();
                s.Rows.Add(Regen(L10n.Game("$se_eitrregen", "Eitr regen"), StatsUi.ItemIcon("Eitr"), p, effects, el, "ModifyEitrRegen",
                    (SEMan m, ref float v) => m.ModifyEitrRegen(ref v), (StatusEffect e, ref float v) => e.ModifyEitrRegen(ref v),
                    p.GetEquipmentEitrRegenModifier(), gear));
            }
            return s;
        }

        /// <summary>Max health, stamina or eitr: base, each food, enchantments, then anything else.</summary>
        private static StatRow Pool(string label, Sprite icon, float max, float baseValue,
            IEnumerable<(Player.Food food, float value)> foods, List<EpicLootBridge.Effect> el, string enchant)
        {
            var row = new StatRow { Icon = icon, Label = label, Value = StatsUi.Int(max), LiveValue = true };
            if (baseValue > 0f)
                row.Chips.Add(Chip(icon, StatsUi.Int(baseValue), L10n.T("stats_base")));
            float known = baseValue;
            foreach (var (food, value) in foods.OrderByDescending(x => x.value))
            {
                if (value < 0.5f)
                    continue;
                known += value;
                var chip = Chip(food.m_item.GetIcon(), "+" + StatsUi.Int(value), ItemName(food.m_item));
                chip.Live = true;
                row.Chips.Add(chip);
            }
            known += AddEnchantChips(row, el, enchant, 1f, Flat);
            AddRest(row, max - known, Flat, 1f);
            return row;
        }

        private delegate void ManagerMod(SEMan m, ref float v);
        private delegate void EffectMod(StatusEffect e, ref float v);

        /// <summary>
        /// A regen multiplier, as the game builds it: each status effect above 1x adds its bonus,
        /// each below 1x multiplies everything before it; Epic Loot adds after; some gear adds last.
        /// </summary>
        private static StatRow Regen(string label, Sprite icon, Player p, List<StatusEffect> effects,
            List<EpicLootBridge.Effect> el, string enchant, ManagerMod all, EffectMod one, float gearAdd,
            List<ItemDrop.ItemData> gear)
        {
            float total = 1f;
            all(p.GetSEMan(), ref total);
            total += gearAdd;
            var row = new StatRow { Icon = icon, Label = label };
            row.Value = StatsUi.Pct(total - 1f);
            row.ValueColor = Signed(total - 1f);
            row.Tip = L10n.T("stats_regen_tip");
            float fromEffects = 1f;
            foreach (var se in effects)
            {
                float alone = 1f;
                one(se, ref alone);
                one(se, ref fromEffects);
                if (Mathf.Abs(alone - 1f) < 0.005f)
                    continue;
                string text = alone >= 1f ? StatsUi.Pct(alone - 1f) : StatsUi.Mono("×" + alone.ToString("0.##", L10n.Culture));
                row.Chips.Add(Chip(se.m_icon, text, EffectName(se) + (alone < 1f ? "\n" + L10n.T("stats_multiplies") : ""),
                    Signed(alone - 1f)));
            }
            float known = fromEffects + AddEnchantChips(row, el, enchant, 0.01f, v => StatsUi.Pct(v));
            if (gear != null)
            {
                foreach (var item in gear)
                {
                    known += item.m_shared.m_eitrRegenModifier;
                    row.Chips.Add(Chip(item.GetIcon(), StatsUi.Pct(item.m_shared.m_eitrRegenModifier), ItemName(item),
                        Signed(item.m_shared.m_eitrRegenModifier)));
                }
            }
            AddRest(row, total - known, v => StatsUi.Pct(v), 0.01f);
            return row;
        }

        // ------------------------------------------------------------------ movement and carrying

        private static readonly (string field, string token, string english, Skills.SkillType icon)[] StaminaUses =
        {
            ("m_jumpStaminaModifier", "$se_jumpstamina", "Jump stamina usage", Skills.SkillType.Jump),
            ("m_attackStaminaModifier", "$se_attackstamina", "Attack stamina usage", Skills.SkillType.Swords),
            ("m_blockStaminaModifier", "$se_blockstamina", "Block stamina cost", Skills.SkillType.Blocking),
            ("m_dodgeStaminaModifier", "$se_dodgestamina", "Dodge stamina usage", Skills.SkillType.Run),
            ("m_swimStaminaModifier", "$se_swimstamina", "Swim stamina usage", Skills.SkillType.Swim),
            ("m_sneakStaminaModifier", "$se_sneakstamina", "Sneak stamina usage", Skills.SkillType.Sneak),
        };

        private static StatSection Movement(Player p, List<ItemDrop.ItemData> gear, List<StatusEffect> effects,
            List<EpicLootBridge.Effect> el)
        {
            var s = new StatSection { Title = L10n.T("stats_movement") };
            var run = StatsUi.SkillIcon(Skills.SkillType.Run);

            // Gear's movement modifier (Epic Loot's included) scales jogging by 1x and running by 1.5x;
            // status effects then add to the result.
            float eq = p.GetEquipmentMovementModifier();
            float fromEffects = 0f;
            var seChips = new List<StatChip>();
            foreach (var se in effects)
            {
                float v = 1f;
                se.ModifySpeed(1f, ref v, p, Vector3.zero);
                if (Mathf.Abs(v - 1f) < 0.005f)
                    continue;
                fromEffects += v - 1f;
                seChips.Add(Chip(se.m_icon, StatsUi.Pct(v - 1f), EffectName(se), Signed(v - 1f)));
            }

            var jog = new StatRow { Icon = run, Label = L10n.Game("$item_movement_modifier", "Movement speed") };
            float jogFactor = (1f + eq) * (1f + fromEffects);
            jog.Value = StatsUi.Pct(jogFactor - 1f);
            jog.ValueColor = Signed(jogFactor - 1f);
            float known = 0f;
            foreach (var item in gear)
            {
                float v = item.m_shared.m_movementModifier;
                float ench = el.Where(e => e.Item == item && e.Type == "ModifyMovementSpeed").Sum(e => e.Value) * 0.01f;
                if (Mathf.Abs(v + ench) < 0.0001f)
                    continue;
                known += v + ench;
                jog.Chips.Add(Chip(item.GetIcon(), StatsUi.Pct(v + ench), ItemName(item), Signed(v + ench)));
            }
            float sets = el.Where(e => e.Item == null && e.Type == "ModifyMovementSpeed").Sum(e => e.Value) * 0.01f;
            if (Mathf.Abs(sets) > 0.0001f)
            {
                known += sets;
                jog.Chips.Add(Chip(null, StatsUi.Pct(sets), L10n.T("stats_set_bonus"), Signed(sets)));
            }
            AddRest(jog, eq - known, v => StatsUi.Pct(v), 0.005f);
            jog.Chips.AddRange(seChips);
            s.Rows.Add(jog);

            var sprint = new StatRow { Icon = run, Label = L10n.Game("$skill_run", "Run") };
            float skill = RunFactor(p) * 0.25f;
            float runFactor = (1f + skill) * (1f + eq * 1.5f) * (1f + fromEffects);
            sprint.Value = StatsUi.Pct(runFactor - 1f);
            sprint.ValueColor = Signed(runFactor - 1f);
            if (skill > 0.001f)
                sprint.Chips.Add(Chip(run, StatsUi.Pct(skill), L10n.Game("$skill_run", "Run") + " (" + L10n.Game("$inventory_skills", "Skills") + ")", Signed(skill)));
            if (Mathf.Abs(eq) > 0.0001f)
                sprint.Chips.Add(Chip(ArmorIcon(), StatsUi.Pct(eq * 1.5f), L10n.T("stats_gear"), Signed(eq)));
            sprint.Chips.AddRange(seChips);
            s.Rows.Add(sprint);

            // Carrying.
            float max = p.GetMaxCarryWeight();
            var carry = new StatRow { Icon = WeightIcon(), Label = L10n.Game("$se_max_carryweight", "Max carry weight"), Value = StatsUi.Int(max) };
            carry.Chips.Add(Chip(WeightIcon(), StatsUi.Int(p.m_maxCarryWeight), L10n.T("stats_base")));
            float carryKnown = p.m_maxCarryWeight;
            foreach (var se in effects)
            {
                // From a high base, so a penalty is not clipped at zero.
                float limit = 1000f;
                se.ModifyMaxCarryWeight(1000f, ref limit);
                limit -= 1000f;
                if (Mathf.Abs(limit) < 0.5f)
                    continue;
                carryKnown += limit;
                carry.Chips.Add(Chip(se.m_icon, Flat(limit), EffectName(se), Signed(limit)));
            }
            carryKnown += AddEnchantChips(carry, el, "AddCarryWeight", 1f, Flat);
            float rate = Game.m_carryWeightRate;
            if (Mathf.Abs(rate - 1f) > 0.001f)
            {
                carry.Chips.Add(Chip(null, StatsUi.Mono("×" + rate.ToString("0.##", L10n.Culture)), L10n.T("stats_world_setting")));
                carryKnown *= rate;
            }
            AddRest(carry, max - carryKnown, Flat, 1f);
            s.Rows.Add(carry);

            // Stamina costs, only those something changes. Lower is better.
            s.Rows.Add(RunStamina(p, gear, effects, eq, run));
            foreach (var (field, token, english, iconSkill) in StaminaUses)
            {
                var row = StaminaUse(p, gear, effects, field, token, english, StatsUi.SkillIcon(iconSkill));
                if (row != null)
                    s.Rows.Add(row);
            }
            s.Rows.RemoveAll(r => r == null);

            float heat = p.GetEquipmentHeatResistanceModifier();
            if (Mathf.Abs(heat) > 0.001f)
            {
                var row = new StatRow { Icon = StatsUi.EffectIcon("Burning"), Label = L10n.Game("$item_heat_modifier", "Heat resistance"),
                    Value = StatsUi.Pct(heat), ValueColor = Signed(heat) };
                foreach (var item in gear.Where(i => Mathf.Abs(i.m_shared.m_heatResistanceModifier) > 0.0001f))
                    row.Chips.Add(Chip(item.GetIcon(), StatsUi.Pct(item.m_shared.m_heatResistanceModifier), ItemName(item)));
                s.Rows.Add(row);
            }

            float fall = 1f;
            p.GetSEMan().ModifyFallDamage(1f, ref fall);
            if (Mathf.Abs(fall - 1f) > 0.005f)
            {
                var row = new StatRow { Icon = StatsUi.SkillIcon(Skills.SkillType.Jump), Label = L10n.Game("$item_falldamage", "Fall damage"),
                    Value = StatsUi.Pct(fall - 1f), ValueColor = Signed(fall - 1f, true) };
                foreach (var se in effects)
                {
                    float d = 1f;
                    se.ModifyFallDamage(1f, ref d);
                    if (Mathf.Abs(d - 1f) > 0.005f)
                        row.Chips.Add(Chip(se.m_icon, StatsUi.Pct(d - 1f), EffectName(se), Signed(d - 1f, true)));
                }
                s.Rows.Add(row);
            }
            return s;
        }

        /// <summary>
        /// Sprinting's stamina drain, built like Player.CheckRun: the Run skill takes up to half off,
        /// a movement penalty from gear adds, then gear's own run modifier, then status effects.
        /// </summary>
        private static StatRow RunStamina(Player p, List<ItemDrop.ItemData> gear, List<StatusEffect> effects, float eq, Sprite run)
        {
            float skill = Mathf.Lerp(1f, 0.5f, RunFactor(p));
            float runMod = p.GetEquipmentRunStaminaModifier();
            float se = 0f;
            var seChips = new List<StatChip>();
            foreach (var e in effects)
            {
                float v = 0f;
                e.ModifyRunStaminaDrain(1f, ref v, Vector3.zero);
                if (Mathf.Abs(v) < 0.005f)
                    continue;
                se += v;
                seChips.Add(Chip(e.m_icon, StatsUi.Pct(v), EffectName(e), Signed(v, true)));
            }
            float factor = skill * (1f - eq) * (1f + runMod) * (1f + se);
            var row = new StatRow { Icon = run, Label = L10n.Game("$se_runstamina", "Run stamina usage"),
                Value = StatsUi.Pct(factor - 1f), ValueColor = Signed(factor - 1f, true) };
            if (skill < 0.999f)
                row.Chips.Add(Chip(run, StatsUi.Pct(skill - 1f), L10n.Game("$skill_run", "Run") + " (" + L10n.Game("$inventory_skills", "Skills") + ")", Signed(skill - 1f, true)));
            if (Mathf.Abs(eq) > 0.0001f)
                row.Chips.Add(Chip(ArmorIcon(), StatsUi.Pct(-eq), L10n.Game("$item_movement_modifier", "Movement speed"), Signed(-eq, true)));
            float known = 0f;
            foreach (var item in gear.Where(i => Mathf.Abs(i.m_shared.m_runStaminaModifier) > 0.0001f))
            {
                known += item.m_shared.m_runStaminaModifier;
                row.Chips.Add(Chip(item.GetIcon(), StatsUi.Pct(item.m_shared.m_runStaminaModifier), ItemName(item), Signed(item.m_shared.m_runStaminaModifier, true)));
            }
            AddRest(row, runMod - known, v => StatsUi.Pct(v), 0.005f, true);
            row.Chips.AddRange(seChips);
            return Mathf.Abs(factor - 1f) < 0.005f && row.Chips.Count == 0 ? null : row;
        }

        /// <summary>The Run skill factor, read only when the character has the skill: asking the game creates it.</summary>
        private static float RunFactor(Player p) =>
            SkillXpBars.FindExisting(p.GetSkills(), Skills.SkillType.Run) != null ? p.GetSkillFactor(Skills.SkillType.Run) : 0f;

        private static readonly Dictionary<string, System.Reflection.FieldInfo> _fields = new Dictionary<string, System.Reflection.FieldInfo>();

        private static float SharedField(ItemDrop.ItemData item, string field)
        {
            if (!_fields.TryGetValue(field, out var fi))
                _fields[field] = fi = AccessTools.Field(typeof(ItemDrop.ItemData.SharedData), field);
            return fi != null && fi.GetValue(item.m_shared) is float v ? v : 0f;
        }

        /// <summary>One stamina cost, the number the game's own tooltip shows: gear plus status effects.</summary>
        private static StatRow StaminaUse(Player p, List<ItemDrop.ItemData> gear, List<StatusEffect> effects,
            string field, string token, string english, Sprite icon)
        {
            float total;
            var m = p.GetSEMan();
            switch (field)
            {
                case "m_jumpStaminaModifier": total = p.GetEquipmentJumpStaminaModifier(); m.ModifyJumpStaminaUsage(1f, ref total, false); break;
                case "m_attackStaminaModifier": total = p.GetEquipmentAttackStaminaModifier(); m.ModifyAttackStaminaUsage(1f, ref total, false); break;
                case "m_blockStaminaModifier": total = p.GetEquipmentBlockStaminaModifier(); m.ModifyBlockStaminaUsage(1f, ref total, false); break;
                case "m_dodgeStaminaModifier": total = p.GetEquipmentDodgeStaminaModifier(); m.ModifyDodgeStaminaUsage(1f, ref total, false); break;
                case "m_swimStaminaModifier": total = p.GetEquipmentSwimStaminaModifier(); m.ModifySwimStaminaUsage(1f, ref total, false); break;
                case "m_sneakStaminaModifier": total = p.GetEquipmentSneakStaminaModifier(); m.ModifySneakStaminaUsage(1f, ref total, false); break;
                default: return null;
            }
            if (Mathf.Abs(total) < 0.005f)
                return null;
            var row = new StatRow { Icon = icon, Label = L10n.Game(token, english), Value = StatsUi.Pct(total), ValueColor = Signed(total, true) };
            float known = 0f;
            foreach (var item in gear)
            {
                float v = SharedField(item, field);
                if (Mathf.Abs(v) < 0.0001f)
                    continue;
                known += v;
                row.Chips.Add(Chip(item.GetIcon(), StatsUi.Pct(v), ItemName(item), Signed(v, true)));
            }
            foreach (var e in effects)
            {
                float v = 0f;
                switch (field)
                {
                    case "m_jumpStaminaModifier": e.ModifyJumpStaminaUsage(1f, ref v); break;
                    case "m_attackStaminaModifier": e.ModifyAttackStaminaUsage(1f, ref v); break;
                    case "m_blockStaminaModifier": e.ModifyBlockStaminaUsage(1f, ref v); break;
                    case "m_dodgeStaminaModifier": e.ModifyDodgeStaminaUsage(1f, ref v); break;
                    case "m_swimStaminaModifier": e.ModifySwimStaminaUsage(1f, ref v); break;
                    case "m_sneakStaminaModifier": e.ModifySneakStaminaUsage(1f, ref v); break;
                }
                if (Mathf.Abs(v) < 0.005f)
                    continue;
                known += v;
                row.Chips.Add(Chip(e.m_icon, StatsUi.Pct(v), EffectName(e), Signed(v, true)));
            }
            AddRest(row, total - known, v => StatsUi.Pct(v), 0.005f, true);
            return row;
        }

        // ------------------------------------------------------------------ armor

        private static StatSection Armor(Player p, List<StatusEffect> effects)
        {
            var s = new StatSection { Title = L10n.Game("$item_armor", "Armor") };
            float armor = p.GetBodyArmor();
            var row = new StatRow { Icon = ArmorIcon(), Label = L10n.Game("$item_armor", "Armor"), Value = StatsUi.Int(armor) };
            float known = 0f;
            foreach (var item in Pieces(p))
            {
                float a = item.GetArmor();
                if (a < 0.5f)
                    continue;
                known += a;
                row.Chips.Add(Chip(item.GetIcon(), StatsUi.Int(a), ItemName(item)));
            }
            foreach (var se in effects)
            {
                float add = 0f;
                se.ModifyArmorMods(ref add);
                float hundred = 100f;
                se.ModifyArmorMods(ref hundred);
                float mult = (100f + add) > 0f ? hundred / (100f + add) - 1f : 0f;
                if (Mathf.Abs(add) >= 0.5f)
                    row.Chips.Add(Chip(se.m_icon, Flat(add), EffectName(se), Signed(add)));
                if (Mathf.Abs(mult) >= 0.005f)
                    row.Chips.Add(Chip(se.m_icon, StatsUi.Pct(mult), EffectName(se), Signed(mult)));
            }
            s.Rows.Add(row);

            // What armor does to hits of a few sizes (the game's formula: past half your armor, a hit loses
            // your armor in full; smaller hits shrink faster).
            var hits = new StatRow { Icon = null, Label = L10n.T("stats_hits"), Tip = L10n.T("stats_hits_tip") };
            foreach (float hit in new[] { 25f, 50f, 100f, 200f })
            {
                float taken = HitData.DamageTypes.ApplyArmor(hit, armor);
                hits.Chips.Add(new StatChip
                {
                    Text = $"<color={StatsUi.Hex(StatsUi.Faint)}>{StatsUi.Int(hit)}</color> {StatsUi.Int(taken)}",
                    Tip = L10n.T("stats_hit_tip", StatsUi.Int(hit), StatsUi.Int(taken)),
                });
            }
            s.Rows.Add(hits);
            return s;
        }

        private static IEnumerable<ItemDrop.ItemData> Pieces(Player p)
        {
            foreach (var item in new[] { _chest(p), _legs(p), _helmet(p), _shoulder(p) })
                if (item != null)
                    yield return item;
        }

        // ------------------------------------------------------------------ resistances

        private static readonly int[] ResistTypes = { Dmg.Blunt, Dmg.Slash, Dmg.Pierce, Dmg.Fire, Dmg.Frost, Dmg.Lightning, Dmg.Poison, Dmg.Spirit };

        internal static float Multiplier(HitData.DamageModifier m)
        {
            switch (m)
            {
                case HitData.DamageModifier.Resistant: return 0.5f;
                case HitData.DamageModifier.VeryResistant: return 0.25f;
                case HitData.DamageModifier.SlightlyResistant: return 0.75f;
                case HitData.DamageModifier.Weak: return 1.5f;
                case HitData.DamageModifier.VeryWeak: return 2f;
                case HitData.DamageModifier.SlightlyWeak: return 1.25f;
                case HitData.DamageModifier.Immune:
                case HitData.DamageModifier.Ignore: return 0f;
                default: return 1f;
            }
        }

        private static string ModifierWord(HitData.DamageModifier m)
        {
            switch (m)
            {
                case HitData.DamageModifier.Resistant: return L10n.Game("$inventory_resistant", "Resistant");
                case HitData.DamageModifier.VeryResistant: return L10n.Game("$inventory_veryresistant", "Very resistant");
                case HitData.DamageModifier.SlightlyResistant: return L10n.Game("$inventory_slightlyresistant", "Slightly resistant");
                case HitData.DamageModifier.Weak: return L10n.Game("$inventory_weak", "Weak");
                case HitData.DamageModifier.VeryWeak: return L10n.Game("$inventory_veryweak", "Very weak");
                case HitData.DamageModifier.SlightlyWeak: return L10n.Game("$inventory_slightlyweak", "Slightly weak");
                case HitData.DamageModifier.Immune: return L10n.Game("$inventory_immune", "Immune");
                default: return "";
            }
        }

        private sealed class ModSource
        {
            public string Name;
            public Sprite Icon;
            public HitData.DamageModifiers Own;   // what this source alone sets, from Normal
        }

        /// <summary>
        /// Resistances follow the game's rule (HitData.DamageModifiers.Apply): pieces of armor in
        /// order (chest, legs, helmet, cape), then status effects; each one replaces the current
        /// value unless the current one is stronger of its kind, and a weakness never replaces a
        /// resistance. So they do not add up: the one left standing counts and the rest are greyed.
        /// Epic Loot's percentages do add up, and come off before the game's modifier and armor.
        /// </summary>
        private static StatSection Resistances(Player p, List<StatusEffect> effects, List<EpicLootBridge.Effect> el)
        {
            var s = new StatSection { Title = L10n.T("stats_resist"), Note = L10n.T("stats_resist_note") };

            // Walk the game's order and remember which source set each type last.
            var sources = new List<ModSource>();
            var mods = p.m_damageModifiers.Clone();
            var winner = new int[Dmg.Count];
            for (int i = 0; i < winner.Length; i++)
                winner[i] = -1;
            foreach (var item in Pieces(p))
            {
                var own = new HitData.DamageModifiers();
                own.Apply(item.m_shared.m_damageModifiers);
                sources.Add(new ModSource { Name = ItemName(item), Icon = item.GetIcon(), Own = own });
                int index = sources.Count - 1;
                var list = item.m_shared.m_damageModifiers;
                var before = mods;
                mods.Apply(list);
                foreach (int t in ResistTypes)
                    if (mods.GetModifier(Dmg.GameTypes[t]) != before.GetModifier(Dmg.GameTypes[t]))
                        winner[t] = index;
            }
            foreach (var se in effects)
            {
                var own = new HitData.DamageModifiers();
                se.ModifyDamageMods(ref own);
                sources.Add(new ModSource { Name = EffectName(se), Icon = se.m_icon, Own = own });
                int index = sources.Count - 1;
                var before = mods;
                se.ModifyDamageMods(ref mods);
                foreach (int t in ResistTypes)
                    if (mods.GetModifier(Dmg.GameTypes[t]) != before.GetModifier(Dmg.GameTypes[t]))
                        winner[t] = index;
            }
            var final = p.GetDamageModifiers();

            foreach (int t in ResistTypes)
            {
                var mod = final.GetModifier(Dmg.GameTypes[t]);
                float vanilla = Multiplier(mod);
                float elFrac = EpicLootBridge.Resistance(p, t, out float uncapped);
                float taken = vanilla * (1f - elFrac);
                var row = new StatRow { Icon = StatsUi.TypeIcon(t), Label = Dmg.Name(t) };
                row.Value = mod == HitData.DamageModifier.Immune ? L10n.Game("$inventory_immune", "Immune") : StatsUi.Pct(taken - 1f);
                row.ValueColor = Signed(1f - taken);
                var tip = new StringBuilder();
                tip.Append(L10n.T("stats_you_take", StatsUi.Pct(taken, false)));
                if (mod != HitData.DamageModifier.Normal)
                    tip.Append("\n").Append(L10n.Game("$inventory_dmgmod", "Damage modifier")).Append(": ").Append(ModifierWord(mod));
                if (elFrac > 0f)
                    tip.Append("\nEpic Loot: ").Append(StatsUi.Pct(-elFrac));
                if (uncapped > elFrac + 0.001f)
                    tip.Append("\n").Append(L10n.T("stats_capped", StatsUi.Pct(-uncapped), StatsUi.Pct(-elFrac)));
                row.Tip = tip.ToString();

                bool matchesGame = mods.GetModifier(Dmg.GameTypes[t]) == mod;
                for (int i = 0; i < sources.Count; i++)
                {
                    var own = sources[i].Own.GetModifier(Dmg.GameTypes[t]);
                    if (own == HitData.DamageModifier.Normal || own == HitData.DamageModifier.Ignore)
                        continue;
                    bool counts = !matchesGame || winner[t] == i;
                    string text = own == HitData.DamageModifier.Immune ? ModifierWord(own) : StatsUi.Pct(Multiplier(own) - 1f);
                    string chipTip = $"{sources[i].Name}\n{ModifierWord(own)}" + (counts ? "" : "\n" + L10n.T("stats_no_effect"));
                    row.Chips.Add(new StatChip
                    {
                        Icon = sources[i].Icon, Text = counts ? text : $"<s>{text}</s>", Tip = chipTip, Dim = !counts,
                        Color = counts ? Signed(1f - Multiplier(own)) : StatsUi.Faint,
                    });
                }
                string ownType = EpicLootBridge.ResistEffect(t);
                string group = t >= Dmg.Fire && t <= Dmg.Spirit ? "AddElementalResistancePercentage" : "AddPhysicalResistancePercentage";
                AddEnchantChips(row, el, ownType, 0.01f, v => StatsUi.Pct(-v));
                AddEnchantChips(row, el, group, 0.01f, v => StatsUi.Pct(-v));
                s.Rows.Add(row);
            }
            return s;
        }

        // ------------------------------------------------------------------ effects

        private static StatSection Effects(List<StatusEffect> effects)
        {
            var s = new StatSection { Title = L10n.T("stats_effects") };
            foreach (var se in effects)
            {
                if (se.m_icon == null)
                    continue;
                var row = new StatRow { Icon = se.m_icon, Label = EffectName(se), LiveValue = true };
                if (se.m_ttl > 0f)
                    row.Value = StatsUi.Time(se.GetRemaningTime());
                row.ValueColor = StatsUi.Muted;
                row.Note = Summary(se);
                s.Rows.Add(row);
            }
            return s;
        }

        /// <summary>The effect's own tooltip, without its flavor sentence, on one line.</summary>
        private static string Summary(StatusEffect se)
        {
            string text;
            try { text = Loc(se.GetTooltipString() ?? ""); }
            catch { return ""; }
            var paragraphs = text.Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries);
            string body = paragraphs.Length > 1 ? string.Join("\n", paragraphs.Skip(1)) : text;
            var lines = body.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0);
            return string.Join("   ", lines);
        }

        // ------------------------------------------------------------------ Epic Loot

        private static StatSection Enchantments(List<EpicLootBridge.Effect> el)
        {
            var s = new StatSection { Title = "Epic Loot" };
            foreach (var group in el.GroupBy(e => e.Type))
            {
                float sum = group.Sum(e => e.Value);
                var row = new StatRow { Label = EpicLootBridge.Text(group.Key, sum) };
                var first = group.FirstOrDefault(e => e.Item != null);
                row.Icon = first.Item?.GetIcon();
                foreach (var e in group)
                {
                    string name = e.Item != null ? ItemName(e.Item) : L10n.T("stats_set_bonus");
                    row.Chips.Add(Chip(e.Item?.GetIcon(), StatsUi.Dec(e.Value), $"{name}\n{EpicLootBridge.Text(e.Type, e.Value)}"));
                }
                s.Rows.Add(row);
            }
            s.Rows.Sort((a, b) => string.Compare(Plain(a.Label), Plain(b.Label), StringComparison.CurrentCultureIgnoreCase));
            return s;
        }

        private static string Plain(string rich) => System.Text.RegularExpressions.Regex.Replace(rich ?? "", "<[^>]+>", "");

        // ------------------------------------------------------------------ icons

        internal static Sprite HeartIcon()
        {
            var hud = Hud.instance;
            var heart = hud != null && hud.m_healthPanel != null ? hud.m_healthPanel.Find("healthicon")?.GetComponent<UnityEngine.UI.Image>() : null;
            return heart != null ? heart.sprite : null;
        }

        internal static Sprite ArmorIcon()
        {
            var gui = InventoryGui.instance;
            var img = gui != null && gui.m_armor != null ? gui.m_armor.transform.parent?.Find("armor_icon")?.GetComponent<UnityEngine.UI.Image>() : null;
            return img != null ? img.sprite : null;
        }

        internal static Sprite WeightIcon()
        {
            var gui = InventoryGui.instance;
            var img = gui != null && gui.m_weight != null ? gui.m_weight.transform.parent?.Find("weight_icon")?.GetComponent<UnityEngine.UI.Image>() : null;
            return img != null ? img.sprite : null;
        }
    }
}
