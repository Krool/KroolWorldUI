using System.Globalization;
using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace KroolWorldUI.Features
{
    /// <summary>
    /// A clear countdown before the game's auto-save (every 30 minutes), so its lag spike never
    /// catches you mid-fight. Two clocks, whichever is sooner:
    ///   - the game's own save timer (Game.m_saveTimer vs Game.m_saveInterval): exact for a host
    ///     or single player, and the character save on a client;
    ///   - the server's "World save in 30s" message, which every client receives, for the world
    ///     save on a dedicated server.
    /// Read-only.
    /// </summary>
    internal static class SaveCountdown
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<int> Seconds;
        internal static ConfigEntry<bool> Sound;
        internal static ConfigEntry<float> SoundVolume;
        internal static ConfigEntry<float> AnchorX;
        internal static ConfigEntry<float> AnchorY;

        private const string Section = "Save Countdown";
        private const string WarningToken = "$msg_worldsavewarning";

        internal static void BindConfig(ConfigFile config)
        {
            Enabled = config.Bind(Section, "Enabled", true,
                "Counts down the last seconds before the auto-save, so its lag spike does not catch you " +
                "mid-fight. Works in single player, as host, and on a dedicated server.");
            Seconds = config.Bind(Section, "Seconds", 5,
                new ConfigDescription("How many seconds before the save the countdown starts.",
                    new AcceptableValueRange<int>(3, 30)));
            Sound = config.Bind(Section, "Sound", true, "A soft tick each second of the countdown.");
            SoundVolume = config.Bind(Section, "SoundVolume", 0.35f,
                new ConfigDescription("Tick volume (also follows the game's sound settings).",
                    new AcceptableValueRange<float>(0.05f, 1f)));
            AnchorX = config.Bind(Section, "AnchorX", 0.5f,
                new ConfigDescription("Horizontal position of the countdown's center (0 = left, 1 = right).",
                    new AcceptableValueRange<float>(0f, 1f)));
            AnchorY = config.Bind(Section, "AnchorY", 0.72f,
                new ConfigDescription("Vertical position of the countdown's center (0 = bottom, 1 = top).",
                    new AcceptableValueRange<float>(0f, 1f)));
        }

        [HarmonyPatch(typeof(Hud), "Update")]
        private static class EnsureOnHud
        {
            private static void Postfix()
            {
                if (Enabled.Value)
                    SaveCountdownHud.Ensure();
            }
        }

        // The server announces the world save 30 s ahead to everyone ("$msg_worldsavewarning 30s").
        // Seen here before localization, and even while the HUD is hidden.
        [HarmonyPatch(typeof(MessageHud), nameof(MessageHud.ShowMessage))]
        private static class WatchSaveWarning
        {
            private static void Prefix(string text)
            {
                try
                {
                    if (text == null || !text.StartsWith(WarningToken))
                        return;
                    string rest = text.Substring(WarningToken.Length).Trim().TrimEnd('s');
                    if (!float.TryParse(rest, NumberStyles.Float, CultureInfo.InvariantCulture, out float secs))
                        secs = 30f;
                    SaveCountdownHud.ServerSaveAt = Time.unscaledTime + secs;
                }
                catch (System.Exception e)
                {
                    Plugin.Log.LogError($"Save countdown: {e}");
                }
            }
        }
    }

    internal class SaveCountdownHud : MonoBehaviour
    {
        private const float Width = 260f;
        private const float Height = 46f;
        private const float SavingSeconds = 2f;
        private const float PreviewSaving = 1.5f;

        /// <summary>Unscaled time the server said it will save the world; 0 when none is pending.</summary>
        internal static float ServerSaveAt;

        private static SaveCountdownHud _instance;
        private static bool _failed;
        private static AudioClip _tick, _lastTick;

        private RectTransform _rect;
        private CanvasGroup _group;
        private TMP_Text _text;
        private AudioSource _audio;
        private int _shown = -1;          // second on screen, -1 when not counting
        private float _savingUntil;
        private bool _previewing;
        private float _previewStart;

        internal static void Ensure()
        {
            if (_instance != null || _failed)
                return;
            var hud = Hud.instance;
            if (hud == null || hud.m_rootObject == null || hud.m_healthText == null || Player.m_localPlayer == null)
                return;
            try
            {
                var go = new GameObject("KroolWorldUI_SaveCountdown", typeof(RectTransform), typeof(CanvasGroup));
                go.transform.SetParent(hud.m_rootObject.transform, false);
                var c = go.AddComponent<SaveCountdownHud>();
                _instance = c;
                c._rect = (RectTransform)go.transform;
                c._rect.pivot = new Vector2(0.5f, 0.5f);
                c._rect.sizeDelta = new Vector2(Width, Height);
                c._group = go.GetComponent<CanvasGroup>();
                c._group.alpha = 0f;
                c._group.blocksRaycasts = false;
                c._group.interactable = false;
                HudKit.ToastBackground(go.transform);
                c._text = HudKit.NewText(hud.m_healthText, go.transform, "text", TextAlignmentOptions.Center,
                    HudKit.Parchment, 24f);
                c._audio = HudKit.NewAudio(go);
            }
            catch (System.Exception e)
            {
                _failed = true;
                Plugin.Log.LogError($"Save countdown disabled for this session: {e}");
            }
        }

        private void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
        }

        /// <summary>Settings-window preview: loops the countdown, with sound on the first pass only.</summary>
        internal static void Preview(bool on)
        {
            var c = _instance;
            if (c == null || _failed || c._previewing == on)
                return;
            c._previewing = on;
            c._previewStart = Time.unscaledTime;
            c._shown = -1;
            c._savingUntil = 0f;
        }

        private void Update()
        {
            if (_failed)
                return;
            try
            {
                Tick();
            }
            catch (System.Exception e)
            {
                _failed = true;
                Plugin.Log.LogError($"Save countdown disabled for this session after an error: {e}");
                Destroy(gameObject);
            }
        }

        private void Tick()
        {
            if (!SaveCountdown.Enabled.Value || Player.m_localPlayer == null)
            {
                _group.alpha = 0f;
                _shown = -1;
                return;
            }

            float now = Time.unscaledTime;
            int lead = SaveCountdown.Seconds.Value;
            bool sound = SaveCountdown.Sound.Value;
            float remaining;
            string label;
            if (_previewing)
            {
                float cycle = lead + PreviewSaving;
                float t = now - _previewStart;
                remaining = lead - t % cycle;
                label = "World save";
                sound &= t < cycle;
            }
            else
            {
                NextSave(now, out remaining, out label);
            }

            if (remaining > 0f && remaining <= lead)
            {
                int secs = Mathf.CeilToInt(remaining);
                if (secs != _shown)
                {
                    _shown = secs;
                    if (sound)
                        PlayTick(secs == 1);
                }
                var color = secs <= 2 ? HudKit.Warn : HudKit.Gold;
                _text.text = $"<size=75%><color=#{ColorUtility.ToHtmlStringRGB(HudKit.Parchment)}>{label} in</color></size>  " +
                             $"<color=#{ColorUtility.ToHtmlStringRGB(color)}><size=130%><mspace=0.6em>{secs}</mspace></size></color>";
                _savingUntil = 0f;
            }
            else if (_shown > 0)
            {
                // Hit zero, or the timer reset because the save just ran.
                _shown = -1;
                _savingUntil = now + (_previewing ? PreviewSaving : SavingSeconds);
                _text.text = $"<color=#{ColorUtility.ToHtmlStringRGB(HudKit.Gold)}>Saving...</color>";
            }

            bool visible = _shown > 0 || now < _savingUntil;
            _group.alpha = visible ? 1f : 0f;
            if (visible)
            {
                var anchor = new Vector2(SaveCountdown.AnchorX.Value, SaveCountdown.AnchorY.Value);
                _rect.anchorMin = anchor;
                _rect.anchorMax = anchor;
                _rect.anchoredPosition = Vector2.zero;
            }
        }

        /// <summary>Seconds to the sooner of the local save timer and the server's announced save.</summary>
        private static void NextSave(float now, out float remaining, out string label)
        {
            remaining = float.MaxValue;
            label = "World save";
            var game = Game.instance;
            var net = ZNet.instance;
            if (game != null && net != null)
            {
                remaining = Game.m_saveInterval - game.m_saveTimer;
                // A client's own timer saves only its character; the server saves the world.
                if (!net.IsServer())
                    label = "Character save";
            }
            if (ServerSaveAt > 0f)
            {
                float server = ServerSaveAt - now;
                if (server < -SavingSeconds)
                    ServerSaveAt = 0f;
                else if (server <= remaining + 0.5f)
                {
                    remaining = server;
                    label = "World save";
                }
            }
        }

        private void PlayTick(bool last)
        {
            if (_audio == null)
                return;
            // Explicit null checks: ??= skips Unity's destroyed-object check.
            if (_tick == null)
                _tick = HudKit.Tone("KroolWorldUI_SaveTick", 0.16f, new HudKit.Note(784f, 0f, 0.15f, 28f));
            if (_lastTick == null)
                _lastTick = HudKit.Tone("KroolWorldUI_SaveTickLast", 0.3f,
                    new HudKit.Note(784f, 0f, 0.12f, 28f), new HudKit.Note(1046.5f, 0.1f, 0.2f, 18f));
            _audio.PlayOneShot(last ? _lastTick : _tick, SaveCountdown.SoundVolume.Value);
        }
    }
}
