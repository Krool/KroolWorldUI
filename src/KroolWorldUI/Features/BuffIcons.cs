using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace KroolWorldUI.Features
{
    /// <summary>
    /// Sizes the game's status effect (buff) icons at the top right and wraps them into rows
    /// before they run into the compass. Hud.UpdateStatusEffects places icon i at
    /// (-4 - column * m_statusEffectSpacing, -row * m_statusEffectSpacing) with m_effectsPerRow
    /// per row; this re-places them after it with a scaled spacing and a per-row count that
    /// fits the free space. Display only.
    /// </summary>
    internal static class BuffIcons
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> Size;
        internal static ConfigEntry<bool> MakeRoomForCompass;
        internal static ConfigEntry<int> PerRow;

        private const string Section = "Buffs";
        private const float CompassGap = 12f;   // HUD pixels kept clear left of the buffs
        private const float RowGap = 6f;        // extra space between wrapped rows, at size 1

        // Built on first use, not in a static initializer: tools\SettingsDoc runs BindConfig outside the game.
        private static AccessTools.FieldRef<Hud, List<RectTransform>> _icons;
        private static readonly Vector3[] Corners = new Vector3[4];

        internal static void BindConfig(ConfigFile config)
        {
            Enabled = config.Bind(Section, "Enabled", true,
                "Resize the buff icons at the top right and keep them clear of the compass.");
            Size = config.Bind(Section, "Size", 0.9f,
                ConfigText.Desc("Buff icon size. 1 is the game's size.", new AcceptableValueRange<float>(0.5f, 1f), step: 0.05f));
            // 0.12.0 shipped 0.7; anyone still on it moves to the new default.
            if (Mathf.Abs(Size.Value - 0.7f) < 0.001f)
                Size.Value = 0.9f;
            PerRow = config.Bind(Section, "PerRow", 0,
                ConfigText.Desc("Most buffs in one row before a new row starts. 0 = the game's 7.",
                    new AcceptableValueRange<int>(0, 12)));
            MakeRoomForCompass = config.Bind(Section, "MakeRoomForCompass", true,
                ConfigText.Desc("Start a new row before the buffs reach the compass.",
                    when: () => Compass.Enabled.Value, whenText: "the compass is on"));
        }

        [HarmonyPatch(typeof(Hud), "UpdateStatusEffects")]
        private static class Relayout
        {
            private static void Postfix(Hud __instance)
            {
                try
                {
                    Layout(__instance);
                }
                catch (System.Exception e)
                {
                    Plugin.Log.LogError($"Buff icons: {e}");
                }
            }
        }

        private static void Layout(Hud hud)
        {
            _icons ??= AccessTools.FieldRefAccess<Hud, List<RectTransform>>("m_statusEffects");
            var icons = _icons(hud);
            if (icons == null || icons.Count == 0)
                return;

            bool on = Enabled.Value;
            float scale = on ? Size.Value : 1f;
            float spacing = hud.m_statusEffectSpacing * scale;
            int perRow = Mathf.Max(1, on && PerRow.Value > 0 ? PerRow.Value : hud.m_effectsPerRow);
            if (on && MakeRoomForCompass.Value)
                perRow = Mathf.Min(perRow, FitBesideCompass(hud, spacing, scale));

            // Wrapped rows sit further apart than the game's square grid: with the rested countdown
            // on, by its height, so it fits above a Resting icon in a lower row without covering the
            // row above. A fixed gap, so rows never move while you play.
            float rowGap = RestedCountdown.Enabled.Value ? RestedCountdown.TextSize.Value + 6f : RowGap * scale;
            float rowStep = spacing + rowGap;

            for (int i = 0; i < icons.Count; i++)
            {
                var icon = icons[i];
                if (icon == null)
                    continue;
                int row = i / perRow, col = i % perRow;
                var pos = new Vector2(-4f - col * spacing, -row * rowStep);
                if (icon.anchoredPosition != pos)
                    icon.anchoredPosition = pos;
                if (!Mathf.Approximately(icon.localScale.x, scale))
                    icon.localScale = new Vector3(scale, scale, 1f);
            }
        }

        /// <summary>How many icons fit in a row between the buff list's start and the compass.</summary>
        private static int FitBesideCompass(Hud hud, float spacing, float scale)
        {
            if (!CompassHud.TryGetBounds(out var compassMin, out var compassMax))
                return int.MaxValue;
            var root = hud.m_statusEffectListRoot;
            var template = hud.m_statusEffectTemplate;
            if (root == null || template == null)
                return int.MaxValue;

            // Only matters when the compass and the first row share a height.
            var rowTop = root.TransformPoint(Vector3.zero);
            var rowBottom = root.TransformPoint(new Vector3(0f, -template.rect.height * scale, 0f));
            if (compassMin.y > rowTop.y || compassMax.y < rowBottom.y)
                return int.MaxValue;

            // The compass's right edge, in the list's local units; icons grow left (negative x).
            float limit = root.InverseTransformPoint(compassMax).x + CompassGap;
            float width = template.rect.width * scale;
            float pivot = template.pivot.x;
            int fit = 0;
            while (fit < 64)
            {
                float left = -4f - fit * spacing - width * pivot;
                if (left < limit)
                    break;
                fit++;
            }
            return Mathf.Max(1, fit);
        }
    }
}
