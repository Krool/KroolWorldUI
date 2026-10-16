using BepInEx.Configuration;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KroolWorldUI.Features
{
    /// <summary>
    /// Counts down to Rested while you rest. The game's "Resting" effect (SE_Cozy) adds Rested once
    /// it has run m_delay seconds, so the countdown is m_delay minus its elapsed time. Rested lasts
    /// SE_Rested.m_baseTTL plus m_TTLPerComfortLevel per comfort level above 1. Read-only.
    /// </summary>
    internal static class RestedCountdown
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> ShowDuration;
        internal static ConfigEntry<bool> Sound;
        internal static ConfigEntry<float> SoundVolume;
        internal static ConfigEntry<float> AnchorX;
        internal static ConfigEntry<float> AnchorY;

        private const string Section = "Rested Countdown";

        internal static void BindConfig(ConfigFile config)
        {
            Enabled = config.Bind(Section, "Enabled", true,
                "While you are resting (sitting or under a roof, near a fire), counts down the seconds until you become Rested.");
            ShowDuration = config.Bind(Section, "ShowDuration", true,
                "Also show your comfort level and how long Rested will last at that comfort.");
            Sound = config.Bind(Section, "Sound", true, "A soft chime when you become Rested.");
            SoundVolume = config.Bind(Section, "SoundVolume", 0.35f,
                new ConfigDescription("Chime volume (also follows the game's sound settings).",
                    new AcceptableValueRange<float>(0.05f, 1f)));
            AnchorX = config.Bind(Section, "AnchorX", 0.5f,
                new ConfigDescription("Horizontal position of the countdown's center (0 = left, 1 = right).",
                    new AcceptableValueRange<float>(0f, 1f)));
            AnchorY = config.Bind(Section, "AnchorY", 0.64f,
                new ConfigDescription("Vertical position of the countdown's center (0 = bottom, 1 = top).",
                    new AcceptableValueRange<float>(0f, 1f)));
        }

        [HarmonyPatch(typeof(Hud), "Update")]
        private static class EnsureOnHud
        {
            private static void Postfix()
            {
                if (Enabled.Value)
                    RestedCountdownHud.Ensure();
            }
        }

        /// <summary>Seconds Rested lasts at a comfort level, from the game's Rested effect.</summary>
        internal static float RestedSeconds(int comfort)
        {
            var rested = ObjectDB.instance?.GetStatusEffect(SEMan.s_statusEffectRested) as SE_Rested;
            float baseTtl = rested != null ? rested.m_baseTTL : 300f;
            float perLevel = rested != null ? rested.m_TTLPerComfortLevel : 60f;
            return baseTtl + (comfort - 1) * perLevel;
        }
    }

    internal class RestedCountdownHud : MonoBehaviour
    {
        private const float Width = 300f;
        private const float Height = 50f;
        private const float DoneSeconds = 2.5f;
        private const float PreviewDelay = 10f;

        private static RestedCountdownHud _instance;
        private static bool _failed;
        private static AudioClip _chime;

        private RectTransform _rect;
        private CanvasGroup _group;
        private TMP_Text _text;
        private RectTransform _fill;
        private AudioSource _audio;
        private bool _counting;
        private float _doneUntil;
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
                var go = new GameObject("KroolWorldUI_RestedCountdown", typeof(RectTransform), typeof(CanvasGroup));
                go.transform.SetParent(hud.m_rootObject.transform, false);
                var c = go.AddComponent<RestedCountdownHud>();
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
                    HudKit.Parchment, 22f);
                c._text.rectTransform.offsetMin = new Vector2(0f, 4f);

                // Thin progress line along the bottom edge.
                var track = HudKit.NewImage("track", go.transform, new Color(0f, 0f, 0f, 0.5f));
                var trackRect = track.rectTransform;
                trackRect.anchorMin = new Vector2(0f, 0f);
                trackRect.anchorMax = new Vector2(1f, 0f);
                trackRect.pivot = new Vector2(0.5f, 0f);
                trackRect.offsetMin = new Vector2(6f, 4f);
                trackRect.offsetMax = new Vector2(-6f, 8f);
                var fill = HudKit.NewImage("fill", track.transform, HudKit.Gold);
                c._fill = fill.rectTransform;
                c._fill.anchorMin = Vector2.zero;
                c._fill.anchorMax = new Vector2(0f, 1f);
                c._fill.offsetMin = Vector2.zero;
                c._fill.offsetMax = Vector2.zero;

                c._audio = HudKit.NewAudio(go);
            }
            catch (System.Exception e)
            {
                _failed = true;
                Plugin.Log.LogError($"Rested countdown disabled for this session: {e}");
            }
        }

        private void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
        }

        /// <summary>Settings-window preview: loops a short countdown, chiming on the first pass only.</summary>
        internal static void Preview(bool on)
        {
            var c = _instance;
            if (c == null || _failed || c._previewing == on)
                return;
            c._previewing = on;
            c._previewStart = Time.unscaledTime;
            c._counting = false;
            c._doneUntil = 0f;
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
                Plugin.Log.LogError($"Rested countdown disabled for this session after an error: {e}");
                Destroy(gameObject);
            }
        }

        private void Tick()
        {
            var player = Player.m_localPlayer;
            if (!RestedCountdown.Enabled.Value || player == null)
            {
                _group.alpha = 0f;
                _counting = false;
                return;
            }

            float now = Time.unscaledTime;
            int comfort = player.GetComfortLevel();
            float delay, elapsed;
            bool resting, sound = RestedCountdown.Sound.Value;
            if (_previewing)
            {
                float cycle = PreviewDelay + DoneSeconds;
                float t = now - _previewStart;
                delay = PreviewDelay;
                elapsed = t % cycle;
                resting = true;
                sound &= t < cycle;
                comfort = Mathf.Max(comfort, 2);
            }
            else
            {
                var cozy = player.GetSEMan().GetStatusEffect(SEMan.s_statusEffectResting) as SE_Cozy;
                resting = cozy != null;
                delay = cozy != null ? cozy.m_delay : 0f;
                elapsed = cozy != null ? cozy.GetDuration() : 0f;
            }

            float remaining = delay - elapsed;
            if (resting && remaining > 0f)
            {
                _counting = true;
                _doneUntil = 0f;
                int secs = Mathf.CeilToInt(remaining);
                _text.text = $"<size=75%>Rested in</size>  <color=#{Hex(HudKit.Gold)}><size=125%><mspace=0.6em>{secs}</mspace></size></color>" +
                             Duration(comfort);
                SetFill(delay > 0f ? elapsed / delay : 1f, HudKit.Gold);
            }
            else if (_counting)
            {
                // Became Rested, or stopped resting before the end.
                _counting = false;
                if (resting)
                {
                    _doneUntil = now + DoneSeconds;
                    _text.text = $"<color=#{Hex(HudKit.Ready)}>Rested</color>" + Duration(comfort);
                    SetFill(1f, HudKit.Ready);
                    if (sound)
                        PlayChime();
                }
            }

            bool visible = _counting || now < _doneUntil;
            _group.alpha = visible ? 1f : 0f;
            if (visible)
            {
                var anchor = new Vector2(RestedCountdown.AnchorX.Value, RestedCountdown.AnchorY.Value);
                _rect.anchorMin = anchor;
                _rect.anchorMax = anchor;
                _rect.anchoredPosition = Vector2.zero;
            }
        }

        private static string Duration(int comfort)
        {
            if (!RestedCountdown.ShowDuration.Value)
                return "";
            string time = StatusEffect.GetTimeString(RestedCountdown.RestedSeconds(comfort), sufix: true);
            return $"  <size=70%><color=#{Hex(HudKit.Muted)}>Comfort {comfort}, {time}</color></size>";
        }

        private void SetFill(float fraction, Color color)
        {
            _fill.anchorMax = new Vector2(Mathf.Clamp01(fraction), 1f);
            var image = _fill.GetComponent<Image>();
            if (image.color != color)
                image.color = color;
        }

        private static string Hex(Color c) => ColorUtility.ToHtmlStringRGB(c);

        private void PlayChime()
        {
            if (_audio == null)
                return;
            if (_chime == null)
                _chime = HudKit.Tone("KroolWorldUI_RestedChime", 0.6f,
                    new HudKit.Note(523.25f, 0f, 0.5f, 7f), new HudKit.Note(659.25f, 0.12f, 0.45f, 7f));
            _audio.PlayOneShot(_chime, RestedCountdown.SoundVolume.Value);
        }
    }
}
