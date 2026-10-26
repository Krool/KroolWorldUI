using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace KroolWorldUI
{
    /// <summary>Settings that cover the whole mod, and the one-time setting migrations.</summary>
    internal static class General
    {
        internal static ConfigEntry<KeyboardShortcut> SettingsKey;
        internal static ConfigEntry<float> WindowScale;
        internal static ConfigEntry<KeyboardShortcut> HideKey;
        internal static ConfigEntry<float> Volume;
        internal static ConfigEntry<bool> ColorblindFriendly;
        internal static ConfigEntry<string> ExpandedSections;
        internal static ConfigEntry<string> WindowRect;
        internal static ConfigEntry<int> ConfigVersion;

        /// <summary>Hide Key has hidden every Krool World UI element (until pressed again).</summary>
        internal static bool HiddenByKey;

        internal static bool Colorblind => ColorblindFriendly != null && ColorblindFriendly.Value;

        private const string Section = "General";

        /// <summary>
        /// Bumped when a migration is added below. Version 1 is 0.14.0: Compass Offset Y changed
        /// direction (+ is up everywhere now).
        /// </summary>
        private const int CurrentConfigVersion = 1;

        internal static void BindConfig(ConfigFile config)
        {
            // Saved values of settings whose range changes are clamped as they bind, so read the
            // ones a migration needs first, while they are still raw text in the file.
            _rawCompassOffsetY = RawValue(config, "Compass", "OffsetY");

            SettingsKey = config.Bind(Section, "SettingsKey", new KeyboardShortcut(KeyCode.F7),
                "Opens this window. On a controller, use Krool World UI in the pause menu.");
            WindowScale = config.Bind(Section, "WindowScale", 1f,
                ConfigText.Desc("Size of this window. It also follows the game's GUI scale.",
                    new AcceptableValueRange<float>(0.75f, 1.5f), step: 0.05f));
            HideKey = config.Bind(Section, "HideKey", new KeyboardShortcut(KeyCode.None),
                "Hides everything this mod draws until pressed again. Unset by default.");
            Volume = config.Bind(Section, "Volume", 1f,
                ConfigText.Desc("Volume of all the mod's sounds, on top of each feature's own volume. 0 mutes them.",
                    new AcceptableValueRange<float>(0f, 1f), step: 0.05f));
            ColorblindFriendly = config.Bind(Section, "ColorblindFriendly", false,
                "Warning colors that stay distinct with red-green color blindness: orange-red and pink " +
                "instead of orange and red, sky blue instead of green.");
            ExpandedSections = config.Bind(Section, "ExpandedSections", "",
                new ConfigDescription("Settings window sections left open.", null, ConfigText.Hidden.Tag));
            WindowRect = config.Bind(Section, "WindowRect", "",
                new ConfigDescription("Settings window position and size.", null, ConfigText.Hidden.Tag));
            ConfigVersion = config.Bind(Section, "ConfigVersion", 0,
                new ConfigDescription("Settings format version, for upgrades.", null, ConfigText.Hidden.Tag));
        }

        /// <summary>After every feature has bound: upgrade settings saved by older versions, once.</summary>
        internal static void Migrate()
        {
            int from = ConfigVersion.Value;
            if (from >= CurrentConfigVersion)
                return;
            if (from < 1)
            {
                // Compass Offset Y meant "down" (0 to 300); now + is up, like every other offset.
                // The bound value is already clamped to the new range (-400 to 0), so use the raw one.
                if (float.TryParse(_rawCompassOffsetY, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float old) && old > 0f)
                    Features.Compass.OffsetY.Value = -old;
            }
            ConfigVersion.Value = CurrentConfigVersion;
        }

        private static string _rawCompassOffsetY;

        /// <summary>A setting's saved text before anything has bound it (BepInEx keeps it as an orphan until then).</summary>
        private static string RawValue(ConfigFile config, string section, string key)
        {
            try
            {
                // Plain reflection, not Harmony: tools\SettingsDoc runs this outside the game.
                var prop = typeof(ConfigFile).GetProperty("OrphanedEntries",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                if (prop?.GetValue(config) is System.Collections.Generic.Dictionary<ConfigDefinition, string> orphans
                    && orphans.TryGetValue(new ConfigDefinition(section, key), out var text))
                    return text;
            }
            catch
            {
                // No raw value: nothing to migrate.
            }
            return null;
        }

        [HarmonyPatch(typeof(Hud), "Update")]
        private static class WatchHideKey
        {
            private static void Postfix()
            {
                if (HideKey.Value.MainKey != KeyCode.None && HideKey.Value.IsDown() && !TextFocus()
                    && !SettingsWindow.IsCapturing && !SettingsWindow.IsTyping)
                    HiddenByKey = !HiddenByKey;
            }
        }

        /// <summary>Typing in chat, the console or a text box, where keys are text, not commands.</summary>
        internal static bool TextFocus()
        {
            return global::Console.IsVisible() || TextInput.IsVisible()
                   || (Chat.instance != null && Chat.instance.HasFocus());
        }

        /// <summary>A feature's volume times the mod-wide Volume.</summary>
        internal static float Vol(float feature) => feature * (Volume != null ? Volume.Value : 1f);
    }
}
