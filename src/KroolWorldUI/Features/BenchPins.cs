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
    /// Taking the bench down with the hammer removes that pin again (or moves it to a bench nearby).
    /// </summary>
    internal static class BenchPins
    {
        /// <summary>The five pin icons the map offers, in the map's pin picker order.</summary>
        internal enum PinIcon { Fire, House, Hammer, Dot, Portal }
        internal enum Naming { RandomName, StationName, NoName }
        internal enum Stations { WorkbenchOnly, AllCraftingStations }

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<Stations> PinStations;
        internal static ConfigEntry<PinIcon> Icon;
        internal static ConfigEntry<Naming> Names;
        internal static ConfigEntry<float> SkipNearPinMeters;
        internal static ConfigEntry<bool> RemoveWithBench;
        internal static ConfigEntry<bool> Announce;
        internal static ConfigEntry<string> NamePrefixes;
        internal static ConfigEntry<string> NameSuffixes;

        private const string Section = "Bench Pins";
        private const string WorkbenchName = "$piece_workbench";

        // First half + second half = "Hrafnvik", "Trollhaven", "Meadhall", "Odinheim".
        // 100 x 100 = 10,000 names.
        private const string DefaultPrefixes =
            "Ulf, Hrafn, Jarn, Frost, Grim, Skjold, Bjorn, Eld, Svart, Thor, Odin, Rune, Troll, Mead, " +
            "Ash, Ygg, Valk, Draugr, Fenris, Storm, Iron, Wolf, Raven, Boar, Elk, Ember, Hel, Njord, " +
            "Sig, Hjalm, Gunn, Kald, Myrk, Vind, Stein, Gull, Sol, Eikthyr, Bonemass, Greyling, " +
            "Freyr, Freya, Baldr, Tyr, Loki, Heimdal, Vidar, Skadi, Bragi, Idun, Frigg, Ullr, Mimir, " +
            "Surt, Ymir, Jotun, Fafnir, Nidhogg, Sleipnir, Hugin, Munin, Geri, Freki, Moder, Yagluth, " +
            "Fulling, Lox, Serpent, Neck, Blod, Dverg, Alf, Hvit, Raud, Gra, Kol, Haug, Skog, Fjall, " +
            "Snae, Bear, Orn, Oak, Birch, Pine, Thunder, Bone, Copper, Silver, Amber, Horn, Tusk, " +
            "Antler, Mist, Ice, Black, Gold, Saga, Skald, Wyrm";
        private const string DefaultSuffixes =
            "heim, gard, vik, stad, borg, holt, fjord, hall, by, hold, keep, stead, haven, mark, fell, " +
            "dal, ness, wall, rest, ford, hof, skali, rock, watch, hearth, thorp, tun, vang, berg, havn, " +
            "sund, ey, lund, mire, moor, mere, strand, haug, stein, garth, helm, hollow, hill, crag, " +
            "peak, spire, tower, gate, bridge, crossing, ridge, reach, rise, falls, wood, grove, glade, " +
            "field, vale, marsh, fen, bay, port, harbor, shore, cove, isle, point, cliff, cairn, barrow, " +
            "howe, mound, ring, forge, mill, lodge, camp, post, fort, bastion, bulwark, rampart, " +
            "longhouse, steading, saeter, hus, bol, toft, rud, vin, akr, hagi, lid, bekk, foss, hammar, " +
            "tind, vatn, grip";

        // The 0.5.0 defaults; anyone still on them moves to the bigger bank.
        private const string OldPrefixes =
            "Ulf, Hrafn, Jarn, Frost, Grim, Skjold, Bjorn, Eld, Svart, Thor, Odin, Rune, Troll, Mead, " +
            "Ash, Ygg, Valk, Draugr, Fenris, Storm, Iron, Wolf, Raven, Boar, Elk, Ember, Hel, Njord, " +
            "Sig, Hjalm, Gunn, Kald, Myrk, Vind, Stein, Gull, Sol, Eikthyr, Bonemass, Greyling";
        private const string OldSuffixes =
            "heim, gard, vik, stad, borg, holt, fjord, hall, by, hold, keep, stead, haven, mark, fell, " +
            "dal, ness, wall, rest, ford, hof, skali, rock, watch, hearth";

        // A pin this mod made sits exactly on its bench.
        private const float PinMatchMeters = 1f;

        // Built on first use, not in a static initializer: tools\SettingsDoc runs BindConfig outside the game.
        private static AccessTools.FieldRef<Minimap, List<Minimap.PinData>> _pins;
        private static List<Minimap.PinData> Pins(Minimap map) =>
            (_pins ??= AccessTools.FieldRefAccess<Minimap, List<Minimap.PinData>>("m_pins"))(map);
        private static AccessTools.FieldRef<List<CraftingStation>> _stations;
        private static List<CraftingStation> AllStations() =>
            (_stations ??= AccessTools.StaticFieldRefAccess<List<CraftingStation>>(
                AccessTools.Field(typeof(CraftingStation), "m_allStations")))();

        /// <summary>True while the local player's hammer is removing a piece.</summary>
        private static bool _removing;

        internal static void BindConfig(ConfigFile config)
        {
            Enabled = config.Bind(Section, "Enabled", true,
                "Adds a map pin when you build a workbench, so you can find your camps again. " +
                "This adds a pin to your map like you would by hand.");
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
            RemoveWithBench = config.Bind(Section, "RemoveWithBench", true,
                "Taking the bench down with the hammer removes its pin, so a bench dropped to fix a boat " +
                "leaves no trace. If another bench still stands within Skip Near Pin Meters, the pin moves to it instead.");
            Announce = config.Bind(Section, "Announce", true, "Show the new pin's name in the top-left messages.");
            NamePrefixes = config.Bind(Section, "NamePrefixes", DefaultPrefixes,
                "First halves of random names, separated by commas.");
            NameSuffixes = config.Bind(Section, "NameSuffixes", DefaultSuffixes,
                "Second halves of random names, separated by commas.");
            if (NamePrefixes.Value == OldPrefixes)
                NamePrefixes.Value = DefaultPrefixes;
            if (NameSuffixes.Value == OldSuffixes)
                NameSuffixes.Value = DefaultSuffixes;
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
                    if (!Pinned(station))
                        return;
                    AddPin(station, pos);
                }
                catch (System.Exception e)
                {
                    Plugin.Log.LogError($"Bench pin: {e}");
                }
            }
        }

        // Not a plain cast: Minimap.PinType runs Icon0..Icon3, Death, Bed, Icon4.
        private static Minimap.PinType PinType()
        {
            switch (Icon.Value)
            {
                case PinIcon.Fire: return Minimap.PinType.Icon0;
                case PinIcon.Hammer: return Minimap.PinType.Icon2;
                case PinIcon.Dot: return Minimap.PinType.Icon3;
                case PinIcon.Portal: return Minimap.PinType.Icon4;
                default: return Minimap.PinType.Icon1;
            }
        }

        private static bool Pinned(CraftingStation station) =>
            station != null && (PinStations.Value == Stations.AllCraftingStations || station.m_name == WorkbenchName);

        // Player.RemovePiece calls WearNTear.Remove on the piece under the crosshair once every check
        // has passed; the flag keeps us to hammer removals by the local player (not decay or damage).
        [HarmonyPatch(typeof(Player), "RemovePiece")]
        private static class TrackHammerRemove
        {
            private static void Prefix(Player __instance) => _removing = __instance == Player.m_localPlayer;
            private static void Finalizer() => _removing = false;
        }

        [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.Remove))]
        private static class UnpinOnRemove
        {
            private static void Prefix(WearNTear __instance)
            {
                try
                {
                    if (!_removing || !Enabled.Value || !RemoveWithBench.Value || Minimap.instance == null)
                        return;
                    var station = __instance.GetComponentInChildren<CraftingStation>(true);
                    if (Pinned(station))
                        RemoveOrMovePin(station);
                }
                catch (System.Exception e)
                {
                    Plugin.Log.LogError($"Bench pin removal: {e}");
                }
            }
        }

        /// <summary>
        /// Drops the pin this mod put on <paramref name="station"/>, or moves it to another bench of
        /// the same base (within the skip distance) so the base keeps its pin.
        /// </summary>
        private static void RemoveOrMovePin(CraftingStation station)
        {
            var map = Minimap.instance;
            var pos = station.transform.position;
            var type = PinType();
            var pin = Pins(map).FirstOrDefault(p => p.m_save && p.m_type == type &&
                                                    Utils.DistanceXZ(p.m_pos, pos) < PinMatchMeters);
            if (pin == null)
                return;

            float near = Mathf.Max(SkipNearPinMeters.Value, PinMatchMeters);
            CraftingStation other = null;
            float best = float.MaxValue;
            foreach (var s in AllStations())
            {
                if (s == null || s == station || !Pinned(s))
                    continue;
                float d = Utils.DistanceXZ(s.transform.position, pos);
                if (d < near && d < best)
                {
                    best = d;
                    other = s;
                }
            }

            map.RemovePin(pin);
            if (other != null)
                map.AddPin(other.transform.position, pin.m_type, pin.m_name, save: true, isChecked: pin.m_checked);
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
            var pin = map.AddPin(pos, PinType(), name, save: true, isChecked: false);
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
