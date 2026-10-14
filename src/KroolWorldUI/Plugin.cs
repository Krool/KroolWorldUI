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
        public const string Version = "0.1.0";

        internal static Plugin Instance;
        internal static BepInEx.Logging.ManualLogSource Log;

        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            // Each feature binds its own config section. Every feature has an Enabled toggle,
            // which shows as a checkbox in ConfigurationManager (F1) if that mod is installed.
            SkillXpBars.BindConfig(Config);

            _harmony = new Harmony(Guid);
            _harmony.PatchAll(typeof(Plugin).Assembly);
            Log.LogInfo($"{Name} {Version} loaded");
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }
    }
}
