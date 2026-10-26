using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using BepInEx.Configuration;
using UnityEngine;

namespace KroolWorldUI
{
    /// <summary>
    /// How config entries read to players. Shared by the F7 window (labels, tooltips) and
    /// tools\SettingsDoc (the README settings tables), so both always say the same thing.
    /// Plain managed code only: SettingsDoc runs it outside Unity. English here; the window
    /// translates through L10n.
    /// </summary>
    internal static class ConfigText
    {
        /// <summary>Tag for entries the window and the docs skip (window state kept in the cfg).</summary>
        internal sealed class Hidden
        {
            internal static readonly Hidden Tag = new Hidden();
        }

        /// <summary>
        /// Extra layout for one setting, as a ConfigDescription tag:
        /// Group starts a sub-heading in its section when it differs from the row above;
        /// When greys the row out unless it returns true (WhenText says why, for tooltips and docs);
        /// Step is the slider/arrow step (0 = picked from the range).
        /// </summary>
        internal sealed class Meta
        {
            public string Label;   // shown name, when the spaced key is not right ("Corewood", as the game spells it)
            public string Group;
            public Func<bool> When;
            public string WhenText;
            public float Step;
        }

        internal static ConfigDescription Desc(string text, AcceptableValueBase range = null, string group = null,
            Func<bool> when = null, string whenText = null, float step = 0f, string label = null)
        {
            return new ConfigDescription(text, range,
                new Meta { Group = group, When = when, WhenText = whenText, Step = step, Label = label });
        }

        /// <summary>The setting's name as players see it: its Label, or the key spaced out.</summary>
        internal static string Name(ConfigEntryBase entry) => MetaOf(entry)?.Label ?? Pretty(entry.Definition.Key);

        internal static Meta MetaOf(ConfigEntryBase entry) =>
            entry.Description?.Tags?.OfType<Meta>().FirstOrDefault();

        internal static bool IsHidden(ConfigEntryBase entry) =>
            entry.Description?.Tags?.Any(t => t is Hidden) == true;

        /// <summary>False when the setting it depends on makes it irrelevant.</summary>
        internal static bool IsActive(ConfigEntryBase entry)
        {
            var when = MetaOf(entry)?.When;
            if (when == null)
                return true;
            try { return when(); }
            catch { return true; }
        }

        /// <summary>"NearLimitPercent" -> "Near Limit Percent".</summary>
        internal static string Pretty(string key) =>
            Regex.Replace(key, "(?<=[a-z])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])", " ");

        internal static string Value(object value)
        {
            switch (value)
            {
                case bool b: return b ? "On" : "Off";
                case float f: return f.ToString("0.###", CultureInfo.InvariantCulture);
                case Color c: return Hex(c);
                case Enum e: return Pretty(e.ToString());
                case null: return "";
                default: return value.ToString();
            }
        }

        /// <summary>The allowed values, e.g. "100 to 500" or "Always, Near Limit, When Changed".</summary>
        internal static string Options(ConfigEntryBase entry)
        {
            switch (entry.Description?.AcceptableValues)
            {
                case AcceptableValueRange<float> r: return $"{Value(r.MinValue)} to {Value(r.MaxValue)}";
                case AcceptableValueRange<int> r: return $"{r.MinValue} to {r.MaxValue}";
            }
            if (entry.SettingType.IsEnum)
                return string.Join(", ", Enum.GetNames(entry.SettingType).Select(Pretty));
            if (entry.SettingType == typeof(Color))
                return "Hex RRGGBBAA (AA = opacity)";
            if (entry.SettingType == typeof(KeyboardShortcut))
                return "Any key";
            return "";
        }

        /// <summary>The description, plus when it applies if it depends on another setting.</summary>
        internal static string Describe(ConfigEntryBase entry)
        {
            string text = entry.Description?.Description ?? "";
            string when = MetaOf(entry)?.WhenText;
            return string.IsNullOrEmpty(when) ? text : $"{text} Used when {when}.";
        }

        /// <summary>RRGGBBAA, the form BepInEx writes colors in.</summary>
        internal static string Hex(Color c)
        {
            int B(float v) => Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255);
            return $"{B(c.r):X2}{B(c.g):X2}{B(c.b):X2}{B(c.a):X2}";
        }
    }
}
