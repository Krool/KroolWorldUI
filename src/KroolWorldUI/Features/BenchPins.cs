using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace KroolWorldUI.Features
{
    /// <summary>
    /// Drops a map pin when you build a workbench (or any crafting station), named from a bank of
    /// Norse-sounding settlement names. The one feature that changes anything: it adds a pin to
    /// your own map the same way you would by hand (Minimap.AddPin, saved with your character).
    /// Removing the bench leaves the pin.
    /// </summary>
    internal static class BenchPins
    {
        /// <summary>The five pin icons the map offers, in the game's order (Minimap.PinType Icon0..Icon4).</summary>
        internal enum PinIcon { Fire, House, Hammer, Dot, Portal }
        internal enum Naming { RandomName, StationName, NoName }
        internal enum Stations { WorkbenchOnly, AllCraftingStations }

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<Stations> PinStations;
        internal static ConfigEntry<PinIcon> Icon;
        internal static ConfigEntry<Naming> Names;
        internal static ConfigEntry<float> SkipNearPinMeters;
        internal static ConfigEntry<bool> Announce;
        internal static ConfigEntry<string> NamePrefixes;
        internal static ConfigEntry<string> NameSuffixes;

        private const string Section = "Bench Pins";
        private const string WorkbenchName = "$piece_workbench";

        // First half + second half = "Hrafnvik", "Trollhaven", "Meadhall", "Odinheim".
        private const string DefaultPrefixes =
            "Ulf, Hrafn, Jarn, Frost, Grim, Skjold, Bjorn, Eld, Svart, Thor, Odin, Rune, Troll, Mead, " +
            "Ash, Ygg, Valk, Draugr, Fenris, Storm, Iron, Wolf, Raven, Boar, Elk, Ember, Hel, Njord, " +
            "Sig, Hjalm, Gunn, Kald, Myrk, Vind, Stein, Gull, Sol, Eikthyr, Bonemass, Greyling";
        private const string DefaultSuffixes =
            "heim, gard, vik, stad, borg, holt, fjord, hall, by, hold, keep, stead, haven, mark, fell, " +
            "dal, ness, wall, rest, ford, hof, skali, rock, watch, hearth";

        // Built on first use, not in a static initializer: tools\SettingsDoc runs BindConfig outside the game.
        private static AccessTools.FieldRef<Minimap, List<Minimap.PinData>> _pins;
        private static List<Minimap.PinData> Pins(Minimap map) =>
            (_pins ??= AccessTools.FieldRefAccess<Minimap, List<Minimap.PinData>>("m_pins"))(map);

        internal static void BindConfig(ConfigFile config)
        {
            Enabled = config.Bind(Section, "Enabled", true,
                "Adds a map pin when you build a workbench, so you can find your camps again. " +
                "This adds a pin to your map like you would by hand; removing the bench leaves the pin.");
            PinStations = config.Bind(Section, "PinStations", Stations.WorkbenchOnly,
                "Workbench Only, or every crafting station (forge, stonecutter, artisan table, and so on).");
            Icon = config.Bind(Section, "Icon", PinIcon.House, "Which map pin icon to use.");
            Names = config.Bind(Section, "Names", Naming.RandomName,
                "Random Name: a Norse-sounding name from Name Prefixes + Name Suffixes. " +
                "Station Name: \"Workbench\". No Name: just the icon.");
            SkipNearPinMeters = config.Bind(Section, "SkipNearPinMeters", 50f,
                new ConfigDescription("No new pin if one is already on your map this close (meters), " +
                                      "so a base gets one pin, not one per bench. 0 = always pin.",
                    new AcceptableValueRange<float>(0f, 200f)));
            Announce = config.Bind(Section, "Announce", true, "Show the new pin's name in the top-left messages.");
            NamePrefixes = config.Bind(Section, "NamePrefixes", DefaultPrefixes,
                "First halves of random names, separated by commas.");
            NameSuffixes = config.Bind(Section, "NameSuffixes", DefaultSuffixes,
                "Second halves of random names, separated by commas.");
        }

        [HarmonyPatch(typeof(Player), nameof(Player.PlacePiece))]
        private static class PinOnPlace
        {
            private static void Postfix(Player __instance, Piece piece, Vector3 pos)
            {
                try
                {
                    if (!Enabled.Value || __instance != Player.m_localPlayer || piece == null || Minimap.instance == null)
                        return;
                    var station = piece.GetComponentInChildren<CraftingStation>(true);
                    if (station == null)
                        return;
                    if (PinStations.Value == Stations.WorkbenchOnly && station.m_name != WorkbenchName)
                        return;
                    AddPin(station, pos);
                }
                catch (System.Exception e)
                {
                    Plugin.Log.LogError($"Bench pin: {e}");
                }
            }
        }

        private static void AddPin(CraftingStation station, Vector3 pos)
        {
            var map = Minimap.instance;
            var pins = Pins(map);
            float near = SkipNearPinMeters.Value;
            if (near > 0f && pins.Any(p => p.m_save && Utils.DistanceXZ(p.m_pos, pos) < near))
                return;

            string stationName = Localization.instance.Localize(station.m_name);
            string name;
            switch (Names.Value)
            {
                case Naming.StationName: name = stationName; break;
                case Naming.NoName: name = ""; break;
                default: name = RandomName(pins); break;
            }
            var pin = map.AddPin(pos, (Minimap.PinType)(int)Icon.Value, name, save: true, isChecked: false);
            if (Announce.Value)
                Player.m_localPlayer.Message(MessageHud.MessageType.TopLeft,
                    $"$msg_pin_added: {(name.Length > 0 ? name : stationName)}", 0, pin?.m_icon);
        }

        /// <summary>Prefix + suffix, avoiding names already on the map when it can.</summary>
        internal static string RandomName(List<Minimap.PinData> pins)
        {
            var prefixes = Split(NamePrefixes.Value, DefaultPrefixes);
            var suffixes = Split(NameSuffixes.Value, DefaultSuffixes);
            string name = "";
            for (int tries = 0; tries < 12; tries++)
            {
                name = prefixes[Random.Range(0, prefixes.Length)] + suffixes[Random.Range(0, suffixes.Length)];
                if (pins == null || !pins.Any(p => p.m_name == name))
                    break;
            }
            return name;
        }

        private static string[] Split(string list, string fallback)
        {
            var parts = list.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();
            return parts.Length > 0 ? parts : Split(fallback, "x");
        }
    }
}
