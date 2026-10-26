using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KroolWorldUI
{
    /// <summary>
    /// A "Krool World UI" button in the game's pause menu, under Settings, that opens the settings
    /// window. The way in on a controller or a Steam Deck, where F7 is out of reach. It joins the
    /// menu's own up/down navigation.
    /// </summary>
    internal static class MenuButton
    {
        private const string ObjectName = "KroolWorldUI_MenuButton";
        private static Button _button;

        [HarmonyPatch(typeof(Menu), nameof(Menu.Show))]
        private static class AddOnShow
        {
            private static void Postfix(Menu __instance)
            {
                try
                {
                    Ensure(__instance);
                }
                catch (System.Exception e)
                {
                    Plugin.Log.LogError($"Pause menu button: {e}");
                }
            }
        }

        // The menu wires its buttons for up/down navigation here; put ours after Settings.
        [HarmonyPatch(typeof(Menu), "UpdateNavigation")]
        private static class JoinNavigation
        {
            private static void Postfix(Menu __instance)
            {
                try
                {
                    JoinAfterSettings(__instance);
                }
                catch (System.Exception e)
                {
                    Plugin.Log.LogError($"Pause menu button navigation: {e}");
                }
            }
        }

        private static void JoinAfterSettings(Menu menu)
        {
            if (_button == null || !_button.gameObject.activeInHierarchy || menu.m_settingsButton == null)
                return;
            var settings = menu.m_settingsButton;
            var below = settings.navigation.selectOnDown;
            if (below == _button)
                return; // already in place
            Link(settings, _button);
            if (below != null)
                Link(_button, below);
        }

        private static void Link(Selectable up, Selectable down)
        {
            if (up == null || down == null)
                return;
            var a = up.navigation;
            a.mode = Navigation.Mode.Explicit;
            a.selectOnDown = down;
            up.navigation = a;
            var b = down.navigation;
            b.mode = Navigation.Mode.Explicit;
            b.selectOnUp = up;
            down.navigation = b;
        }

        private static void Ensure(Menu menu)
        {
            if (_button != null || menu.m_settingsButton == null)
                return;
            var source = menu.m_settingsButton;
            var go = Object.Instantiate(source.gameObject, source.transform.parent, false);
            go.name = ObjectName;
            go.transform.SetSiblingIndex(source.transform.GetSiblingIndex() + 1);
            _button = go.GetComponent<Button>();
            // A fresh click event: the copy would otherwise also carry the original's "open Settings".
            _button.onClick = new Button.ButtonClickedEvent();
            _button.onClick.AddListener(() =>
            {
                Menu.instance?.Hide();
                SettingsWindow.Open();
            });
            foreach (var text in go.GetComponentsInChildren<TMP_Text>(true))
                text.text = Plugin.Name;
            // The menu wired its navigation before this button existed; join it now.
            JoinAfterSettings(menu);
        }
    }
}
