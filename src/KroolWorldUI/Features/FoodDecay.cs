using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace KroolWorldUI.Features
{
    /// <summary>
    /// Extends the health, stamina and eitr bars with a faint segment for the max your current
    /// foods have lost to time. Player.UpdateFood scales each food's health, stamina and eitr by
    /// (time left / burn time)^0.3, so a food at half time gives about 81% of each. The segment is
    /// what eating fresh would restore. Read-only.
    /// </summary>
    internal static class FoodDecay
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> Health;
        internal static ConfigEntry<Color> HealthColor;
        internal static ConfigEntry<bool> Stamina;
        internal static ConfigEntry<Color> StaminaColor;
        internal static ConfigEntry<bool> Eitr;
        internal static ConfigEntry<Color> EitrColor;

        private const string Section = "Food Decay";

        // Hud sizes all three bars at 32 HUD pixels per 25 points.
        private const float PixelsPerPoint = 32f / 25f;
        private const float PreviewPoints = 20f;

        private enum Stat { Health, Stamina, Eitr }

        private static readonly Segment HealthSegment = new Segment("KroolWorldUI_FoodHealthGhost");
        private static readonly Segment StaminaSegment = new Segment("KroolWorldUI_FoodStaminaGhost");
        private static readonly Segment EitrSegment = new Segment("KroolWorldUI_FoodEitrGhost");

        internal static void BindConfig(ConfigFile config)
        {
            Enabled = config.Bind(Section, "Enabled", true,
                "A faint segment past the end of the health, stamina and eitr bars: the max your food has " +
                "lost as it wears off, which fresh food would give back.");
            Health = config.Bind(Section, "Health", true, "Segment on the health bar.");
            HealthColor = config.Bind(Section, "HealthColor", new Color(0.75f, 0.18f, 0.14f, 0.45f),
                ConfigText.Desc("Color of the health segment.", when: () => Health.Value, whenText: "Health is on"));
            Stamina = config.Bind(Section, "Stamina", true,
                "Segment on the stamina bar. Like the bar, it shows while stamina is not full.");
            StaminaColor = config.Bind(Section, "StaminaColor", new Color(0.92f, 0.72f, 0.2f, 0.45f),
                ConfigText.Desc("Color of the stamina segment.", when: () => Stamina.Value, whenText: "Stamina is on"));
            Eitr = config.Bind(Section, "Eitr", true,
                "Segment on the eitr bar, for eitr foods. Like the bar, it shows while eitr is not full.");
            EitrColor = config.Bind(Section, "EitrColor", new Color(0.6f, 0.38f, 0.9f, 0.45f),
                ConfigText.Desc("Color of the eitr segment.", when: () => Eitr.Value, whenText: "Eitr is on"));
        }

        /// <summary>Points of a stat the current foods would add if they were all freshly eaten.</summary>
        private static float Missing(Player player, Stat stat)
        {
            float missing = 0f;
            foreach (var food in player.GetFoods())
            {
                var shared = food?.m_item?.m_shared;
                if (shared == null)
                    continue;
                switch (stat)
                {
                    case Stat.Health: missing += Mathf.Max(0f, shared.m_food - food.m_health); break;
                    case Stat.Stamina: missing += Mathf.Max(0f, shared.m_foodStamina - food.m_stamina); break;
                    case Stat.Eitr: missing += Mathf.Max(0f, shared.m_foodEitr - food.m_eitr); break;
                }
            }
            return missing;
        }

        [HarmonyPatch(typeof(Hud), "UpdateHealth")]
        private static class AfterHealth
        {
            private static void Postfix(Hud __instance, Player player) =>
                HealthSegment.Update(__instance.m_healthBarRoot, player, Stat.Health, Health, HealthColor);
        }

        // The stamina and eitr bars fill an inner rect ("Stamina") inside their panels; the segment
        // hangs off its far end, and fades and slides with the panel's own show/hide animation.
        [HarmonyPatch(typeof(Hud), "UpdateStamina")]
        private static class AfterStamina
        {
            private static void Postfix(Hud __instance, Player player) =>
                StaminaSegment.Update(BarRoot(__instance.m_staminaBar2Slow), player, Stat.Stamina, Stamina, StaminaColor);
        }

        [HarmonyPatch(typeof(Hud), "UpdateEitr")]
        private static class AfterEitr
        {
            private static void Postfix(Hud __instance, Player player) =>
                EitrSegment.Update(BarRoot(__instance.m_eitrBarSlow), player, Stat.Eitr, Eitr, EitrColor);
        }

        private static RectTransform BarRoot(Component slowBar) =>
            slowBar != null ? slowBar.transform.parent as RectTransform : null;

        /// <summary>
        /// One faint segment: a child of a bar's root that starts at its far end and runs on in
        /// the same direction, so it follows the bar's rotation and length with no layout code.
        /// </summary>
        private class Segment
        {
            private readonly string _objectName;
            private RectTransform _rect;
            private Image _image;
            private readonly HudKit.FailFlag _failed = new HudKit.FailFlag();

            public Segment(string objectName) => _objectName = objectName;

            public void Update(RectTransform root, Player player, Stat stat, ConfigEntry<bool> on, ConfigEntry<Color> color)
            {
                if (_failed.IsSet || root == null || player == null)
                    return;
                try
                {
                    float missing = Enabled.Value && on.Value && HudKit.Allowed ? Missing(player, stat) : 0f;
                    // Sample while tuning the colors.
                    if (SettingsWindow.IsOpen && SettingsWindow.Previewing && Enabled.Value && on.Value && missing < 1f)
                        missing = PreviewPoints;
                    float width = Mathf.Round(missing * PixelsPerPoint);

                    if (_rect == null)
                    {
                        if (width < 1f)
                            return;
                        Create(root, color.Value);
                    }

                    bool show = width >= 1f;
                    if (_rect.gameObject.activeSelf != show)
                        _rect.gameObject.SetActive(show);
                    if (!show)
                        return;
                    _rect.sizeDelta = new Vector2(width, 0f);
                    if (_image.color != color.Value)
                        _image.color = color.Value;
                }
                catch (System.Exception e)
                {
                    _failed.Set();
                    Plugin.Log.LogError($"Food decay {stat} segment disabled for this session after an error: {e}");
                    if (_rect != null)
                        Object.Destroy(_rect.gameObject);
                }
            }

            private void Create(RectTransform root, Color color)
            {
                var go = new GameObject(_objectName, typeof(RectTransform), typeof(Image));
                go.transform.SetParent(root, false);
                go.transform.SetAsFirstSibling(); // the bar's own frame draws over our edge
                _rect = (RectTransform)go.transform;
                _rect.anchorMin = new Vector2(1f, 0f);
                _rect.anchorMax = new Vector2(1f, 1f);
                _rect.pivot = new Vector2(0f, 0.5f);
                _rect.anchoredPosition = Vector2.zero;
                _image = go.GetComponent<Image>();
                _image.raycastTarget = false;
                _image.color = color;
                var edge = go.AddComponent<Outline>();
                edge.effectColor = new Color(0f, 0f, 0f, 0.5f);
                edge.effectDistance = new Vector2(1f, -1f);
            }
        }
    }
}
