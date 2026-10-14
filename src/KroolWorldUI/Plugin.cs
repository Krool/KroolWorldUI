using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using KroolWorldUI.Features;

namespace KroolWorldUI
{
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "krool.kroolworldui";
        public const string Name = "Krool World UI";
        public const string Version = "0.2.0";
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

            // Each feature binds its own config section with an Enabled toggle. The built-in
            // settings window (F7) lists every entry automatically.
            SettingsWindow.BindConfig(Config);
            SkillXpBars.BindConfig(Config);
            WeightDisplay.BindConfig(Config);
            FoodCallout.BindConfig(Config);
            gameObject.AddComponent<SettingsWindow>();

            _harmony = new Harmony(Guid);
            _harmony.PatchAll(typeof(Plugin).Assembly);
            Log.LogInfo($"{Name} {Version}{Flavor} loaded");
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }
    }
}
