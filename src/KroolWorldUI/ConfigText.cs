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
    /// Plain managed code only: SettingsDoc runs it outside Unity.
    /// </summary>
    internal static class ConfigText
    {
        /// <summary>Tag for entries the window and the docs skip (window state kept in the cfg).</summary>
        internal sealed class Hidden
        {
            internal static readonly Hidden Tag = new Hidden();
        }

        internal static bool IsHidden(ConfigEntryBase entry) =>
            entry.Description?.Tags?.Any(t => t is Hidden) == true;

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

        /// <summary>Hover text: the description, then the default and the allowed values.</summary>
        internal static string Tooltip(ConfigEntryBase entry)
        {
            string text = entry.Description?.Description ?? "";
            text += $"\n\nDefault: {Value(entry.DefaultValue)}";
            string options = Options(entry);
            if (entry.Description?.AcceptableValues != null)
                text += $"    Range: {options}";
            else if (entry.SettingType == typeof(Color))
                text += "    (hex RRGGBBAA, AA = opacity)";
            return text.Trim();
        }

        /// <summary>RRGGBBAA, the form BepInEx writes colors in.</summary>
        internal static string Hex(Color c)
        {
            int B(float v) => Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255);
            return $"{B(c.r):X2}{B(c.g):X2}{B(c.b):X2}{B(c.a):X2}";
        }
    }
}
