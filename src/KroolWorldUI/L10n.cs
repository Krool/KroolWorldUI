using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using HarmonyLib;

namespace KroolWorldUI
{
    /// <summary>
    /// Text in the player's language. The mod's lines live in embedded Translations/&lt;Language&gt;.txt
    /// files, named like Valheim's languages ("German", "Portuguese_Brazilian"). Whenever the game
    /// loads a language, they are added to its own table as $kwui_&lt;key&gt;, so they follow the game's
    /// language setting with nothing to configure. A line missing from a language falls back to English.
    /// Where the game already has the words ("World save in", "Repaired $1", "Day $1"), use its token.
    /// </summary>
    internal static class L10n
    {
        private const string Prefix = "kwui_";
        private const string ResourceFolder = "KroolWorldUI.Translations.";

        private static readonly Dictionary<string, Dictionary<string, string>> _loaded =
            new Dictionary<string, Dictionary<string, string>>();
        private static Action<Localization, string, string> _addWord;
        private static AccessTools.FieldRef<Localization, object> _cache;
        private static string _language = "English";
        private static CultureInfo _culture = CultureInfo.InvariantCulture;
        private static bool _hooked;

        /// <summary>Raised after the game switches language, for text that is set once and kept.</summary>
        internal static event Action Changed;

        /// <summary>The game's current language name, e.g. "German".</summary>
        internal static string Language => _language;

        /// <summary>Number formatting for the game's language (decimal comma in German, and so on).</summary>
        internal static CultureInfo Culture => _culture;

        /// <summary>
        /// Called once the plugin has patched. If the game already built its language table, add our
        /// lines now; if not, the SetupLanguage postfix adds them when it does. Never creates the
        /// table itself: that is too early in startup (the platform and language are not set yet).
        /// </summary>
        internal static void Init()
        {
            try
            {
                if (!_hooked)
                {
                    _hooked = true;
                    Localization.OnLanguageChange += () =>
                    {
                        try { Changed?.Invoke(); }
                        catch (Exception e) { Plugin.Log.LogError($"Language change: {e}"); }
                    };
                }
                var existing = AccessTools.StaticFieldRefAccess<Localization, Localization>("m_instance");
                if (existing != null)
                    Inject(existing, existing.GetSelectedLanguage());
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Translations not loaded, using English: {e}");
            }
        }

        /// <summary>One of the mod's lines, with $1, $2... filled in.</summary>
        internal static string T(string key, params string[] args)
        {
            string s = null;
            try
            {
                var loc = Localization.instance;
                if (loc != null)
                {
                    s = loc.Localize("$" + Prefix + key, args);
                    if (s.StartsWith("[" + Prefix))
                        s = null; // not injected yet
                }
            }
            catch
            {
                s = null;
            }
            if (s == null)
            {
                Table("English").TryGetValue(key, out s);
                s = Insert(s ?? key, args);
            }
            return s;
        }

        /// <summary>
        /// Settings window text: the translation for the game's language, or the English given
        /// (which lives in the code, next to the setting).
        /// </summary>
        internal static string W(string key, string english) =>
            Table(_language).TryGetValue(key, out var s) ? s : english;

        /// <summary>A game token ("$msg_worldsavewarning"), or the English fallback if the game lacks it.</summary>
        internal static string Game(string token, string fallback, params string[] args)
        {
            try
            {
                var loc = Localization.instance;
                if (loc != null)
                {
                    string s = loc.Localize(token, args);
                    if (!string.IsNullOrEmpty(s) && !s.StartsWith("[") && !s.StartsWith("$"))
                        return s;
                }
            }
            catch
            {
                // fall through
            }
            return Insert(fallback, args);
        }

        private static bool IsWindowKey(string key) =>
            key.StartsWith("s_") || key.StartsWith("sec_") || key.StartsWith("grp_") || key.StartsWith("enum_");

        /// <summary>A number in the game language's format.</summary>
        internal static string Num(float value, string format) => value.ToString(format, _culture);

        private static string Insert(string text, string[] args)
        {
            if (args == null)
                return text;
            for (int i = 0; i < args.Length; i++)
                text = text.Replace("$" + (i + 1), args[i]);
            return text;
        }

        // Valheim builds its table language by language (English first, then the chosen one on top).
        [HarmonyPatch(typeof(Localization), nameof(Localization.SetupLanguage))]
        private static class AddOurLines
        {
            private static void Postfix(Localization __instance, string language)
            {
                Inject(__instance, language);
            }
        }

        private static void Inject(Localization loc, string language)
        {
            try
            {
                if (loc == null || string.IsNullOrEmpty(language))
                    return;
                _addWord ??= AccessTools.MethodDelegate<Action<Localization, string, string>>(
                    AccessTools.Method(typeof(Localization), "AddWord"));
                var english = Table("English");
                var local = Table(language);
                foreach (var kv in english)
                {
                    if (IsWindowKey(kv.Key))
                        continue; // settings window text is read straight from our tables
                    _addWord(loc, Prefix + kv.Key, local.TryGetValue(kv.Key, out var t) ? t : kv.Value);
                }
                _language = language;
                _culture = CultureFor(language);

                // Anything looked up before this (cached as "[kwui_...]") must be looked up again.
                _cache ??= AccessTools.FieldRefAccess<Localization, object>("m_cache");
                var cache = _cache(loc);
                cache?.GetType().GetMethod("EvictAll")?.Invoke(cache, null);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Translations for {language}: {e}");
            }
        }

        /// <summary>key = text lines from Translations/&lt;language&gt;.txt; # starts a comment; \n is a line break.</summary>
        internal static Dictionary<string, string> Table(string language)
        {
            if (_loaded.TryGetValue(language, out var table))
                return table;
            table = new Dictionary<string, string>();
            _loaded[language] = table;
            // HUD lines in <language>.txt, settings window text in <language>.settings.txt.
            Read(table, ResourceFolder + language + ".txt");
            Read(table, ResourceFolder + language + ".settings.txt");
            return table;
        }

        private static void Read(Dictionary<string, string> table, string resource)
        {
            var stream = typeof(L10n).Assembly.GetManifestResourceStream(resource);
            if (stream == null)
                return;
            using (var reader = new StreamReader(stream, System.Text.Encoding.UTF8))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    line = line.Trim();
                    if (line.Length == 0 || line[0] == '#')
                        continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0)
                        continue;
                    string key = line.Substring(0, eq).Trim();
                    string text = line.Substring(eq + 1).Trim().Replace("\\n", "\n");
                    if (text.Length > 0)
                        table[key] = text;
                }
            }
        }

        private static readonly Dictionary<string, string> Cultures = new Dictionary<string, string>
        {
            { "Swedish", "sv-SE" }, { "French", "fr-FR" }, { "Italian", "it-IT" }, { "German", "de-DE" },
            { "Spanish", "es-ES" }, { "Russian", "ru-RU" }, { "Romanian", "ro-RO" }, { "Bulgarian", "bg-BG" },
            { "Macedonian", "mk-MK" }, { "Finnish", "fi-FI" }, { "Danish", "da-DK" }, { "Norwegian", "nb-NO" },
            { "Icelandic", "is-IS" }, { "Turkish", "tr-TR" }, { "Lithuanian", "lt-LT" }, { "Czech", "cs-CZ" },
            { "Hungarian", "hu-HU" }, { "Slovak", "sk-SK" }, { "Polish", "pl-PL" }, { "Dutch", "nl-NL" },
            { "Portuguese_European", "pt-PT" }, { "Portuguese_Brazilian", "pt-BR" }, { "Chinese", "zh-CN" },
            { "Chinese_Trad", "zh-TW" }, { "Japanese", "ja-JP" }, { "Korean", "ko-KR" }, { "Hindi", "hi-IN" },
            { "Thai", "th-TH" }, { "Croatian", "hr-HR" }, { "Georgian", "ka-GE" }, { "Greek", "el-GR" },
            { "Serbian", "sr-Latn-RS" }, { "Ukrainian", "uk-UA" }, { "Latvian", "lv-LV" },
        };

        private static CultureInfo CultureFor(string language)
        {
            if (!Cultures.TryGetValue(language, out var name))
                return CultureInfo.InvariantCulture;
            try
            {
                // Only the number format is used; keep it, drop the rest.
                var nf = (NumberFormatInfo)CultureInfo.GetCultureInfo(name).NumberFormat.Clone();
                var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
                culture.NumberFormat = nf;
                return culture;
            }
            catch
            {
                return CultureInfo.InvariantCulture;
            }
        }
    }
}
