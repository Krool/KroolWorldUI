using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace KroolWorldUI.Features
{
    /// <summary>
    /// Extends the health bar with a faint segment for the max health your current foods have
    /// lost to time. The game scales each food's health by (time left / burn time)^0.3, so a food
    /// at half time gives about 81% of its health. The segment is what eating fresh would restore.
    /// Read-only.
    /// </summary>
    internal static class FoodHealthGhost
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<Color> GhostColor;

        private const string Section = "Food Max Health";
        private const string ObjectName = "KroolWorldUI_FoodHealthGhost";

        // Hud sizes the health bar at 32 HUD pixels per 25 health.
        private const float PixelsPerHealth = 32f / 25f;

        private static RectTransform _ghost;
        private static bool _failed;

        internal static void BindConfig(ConfigFile config)
        {
            Enabled = config.Bind(Section, "Enabled", true,
                "Extend the health bar with a faint segment showing max health your foods have lost as they wear off.");
            GhostColor = config.Bind(Section, "Color", new Color(0.75f, 0.18f, 0.14f, 0.45f),
                "Color of the lost-health segment (hex RRGGBBAA; the last two digits are opacity).");
        }

        /// <summary>Health the current foods would add if they were all freshly eaten.</summary>
        internal static float MissingHealth(Player player)
        {
            float missing = 0f;
            foreach (var food in player.GetFoods())
            {
                var shared = food?.m_item?.m_shared;
                if (shared != null)
                    missing += Mathf.Max(0f, shared.m_food - food.m_health);
            }
            return missing;
        }

        [HarmonyPatch(typeof(Hud), "UpdateHealth")]
        private static class AfterHealth
        {
            private static void Postfix(Hud __instance, Player player)
            {
                if (_failed)
                    return;
                try
                {
                    Apply(__instance, player);
                }
                catch (System.Exception e)
                {
                    _failed = true;
                    Plugin.Log.LogError($"Food max health segment disabled for this session after an error: {e}");
                    if (_ghost != null)
                        Object.Destroy(_ghost.gameObject);
                }
            }
        }

        private static void Apply(Hud hud, Player player)
        {
            var root = hud.m_healthBarRoot;
            if (root == null || player == null)
                return;

            float missing = Enabled.Value ? MissingHealth(player) : 0f;
            if (SettingsWindow.IsOpen && SettingsWindow.Previewing && missing < 1f)
                missing = 20f; // sample while tuning the color
            float width = Mathf.Round(missing * PixelsPerHealth);

            if (_ghost == null)
            {
                if (width < 1f)
                    return;
                _ghost = Create(root);
            }

            bool show = width >= 1f;
            if (_ghost.gameObject.activeSelf != show)
                _ghost.gameObject.SetActive(show);
            if (!show)
                return;
            _ghost.sizeDelta = new Vector2(width, 0f);
            var img = _ghost.GetComponent<Image>();
            if (img.color != GhostColor.Value)
                img.color = GhostColor.Value;
        }

        /// <summary>
        /// A child of the health bar root that starts at its far end and runs on in the same
        /// direction, so it follows the bar's rotation and length with no layout code of our own.
        /// </summary>
        private static RectTransform Create(RectTransform root)
        {
            var go = new GameObject(ObjectName, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(root, false);
            go.transform.SetAsFirstSibling(); // the bar's own frame draws over our edge
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(1f, 0f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            var img = go.GetComponent<Image>();
            img.raycastTarget = false;
            img.color = GhostColor.Value;
            var edge = go.AddComponent<Outline>();
            edge.effectColor = new Color(0f, 0f, 0f, 0.5f);
            edge.effectDistance = new Vector2(1f, -1f);
            return rt;
        }
    }
}
