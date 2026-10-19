using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace KroolWorldUI.Features
{
    /// <summary>
    /// Opening a crafting station repairs everything it can, as if you pressed its repair button
    /// once per item. Each item goes through the game's own InventoryGui.CanRepair (station type
    /// and level) and gets what InventoryGui.RepairOneItem gives it: full durability and the same
    /// Crafting skill raise. One repair effect and one message for the lot. Owner-approved write
    /// (2026-10-19).
    /// </summary>
    internal static class AutoRepair
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> Announce;

        private const string Section = "Auto Repair";

        // Built on first use, not in a static initializer: tools\SettingsDoc runs BindConfig outside the game.
        private static System.Func<InventoryGui, ItemDrop.ItemData, bool> _canRepair;

        internal static void BindConfig(ConfigFile config)
        {
            Enabled = config.Bind(Section, "Enabled", true,
                "Opening a workbench, forge or other crafting station repairs every item it can, " +
                "like pressing its repair button once per item.");
            Announce = config.Bind(Section, "Announce", true,
                "Show how many items were repaired in the middle of the screen.");
        }

        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Show))]
        private static class RepairOnOpen
        {
            private static void Postfix(InventoryGui __instance)
            {
                try
                {
                    if (Enabled.Value)
                        RepairAll(__instance);
                }
                catch (System.Exception e)
                {
                    Plugin.Log.LogError($"Auto repair: {e}");
                }
            }
        }

        private static void RepairAll(InventoryGui gui)
        {
            var player = Player.m_localPlayer;
            if (player == null)
                return;
            var station = player.GetCurrentCraftingStation();
            if (station == null || !station.m_canRepair || !station.CheckUsable(player, showMessage: false))
                return;

            _canRepair ??= AccessTools.MethodDelegate<System.Func<InventoryGui, ItemDrop.ItemData, bool>>(
                AccessTools.Method(typeof(InventoryGui), "CanRepair"));
            var worn = new List<ItemDrop.ItemData>();
            player.GetInventory().GetWornItems(worn);

            int repaired = 0;
            string onlyName = null;
            foreach (var item in worn)
            {
                if (!_canRepair(gui, item))
                    continue;
                float max = item.GetMaxDurability();
                player.RaiseSkill(Skills.SkillType.Crafting, 1f - item.m_durability / max);
                item.m_durability = max;
                repaired++;
                onlyName = item.m_shared.m_name;
            }
            if (repaired == 0)
                return;

            station.m_repairItemDoneEffects.Create(station.transform.position, Quaternion.identity);
            if (Announce.Value)
                player.Message(MessageHud.MessageType.Center, repaired == 1
                    ? Localization.instance.Localize("$msg_repaired", onlyName)
                    : $"Repaired {repaired} items");
        }
    }
}
