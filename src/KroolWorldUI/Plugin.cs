using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using KroolWorldUI.Features;

// tools\SettingsDoc calls BindAll and ConfigText to write the README settings tables.
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("SettingsDoc")]

namespace KroolWorldUI
{
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "krool.kroolworldui";
        public const string Name = "Krool World UI";
        public const string Version = "0.8.0";
#if DEV_BUILD
        public const string Flavor = " dev"; // scripts\deploy-dev.ps1 build; shown in the F7 title
#else
        public const string Flavor = "";
#endif

        internal static Plugin Instance;
        internal static BepInEx.Logging.ManualLogSource Log;

        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            BindAll(Config);
            gameObject.AddComponent<SettingsWindow>();

            _harmony = new Harmony(Guid);
            _harmony.PatchAll(typeof(Plugin).Assembly);
            Log.LogInfo($"{Name} {Version}{Flavor} loaded");
        }

        /// <summary>
        /// Every feature's settings, in the order the F7 window and the README list them. A new
        /// feature binds its own section (with an "Enabled" toggle, which becomes the section
        /// header's On/Off) and is added here; the window and tools\SettingsDoc pick it up.
        /// </summary>
        internal static void BindAll(ConfigFile config)
        {
            SettingsWindow.BindConfig(config);
            SkillXpBars.BindConfig(config);
            WeightDisplay.BindConfig(config);
            FoodDecay.BindConfig(config);
            FoodCallout.BindConfig(config);
            BuildMaterials.BindConfig(config);
            DayNightBar.BindConfig(config);
            RestedCountdown.BindConfig(config);
            SaveCountdown.BindConfig(config);
            BenchPins.BindConfig(config);
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }
    }
}
