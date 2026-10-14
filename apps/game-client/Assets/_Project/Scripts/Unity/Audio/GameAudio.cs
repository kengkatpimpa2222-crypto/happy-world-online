using System.Collections.Generic;
using UnityEngine;

namespace HappyWorld.Unity.Audio
{
    /// <summary>
    /// Music, ambience and sound effects (Resources/Art/Audio, see ART_CREDITS.md).
    /// The town plays a day or a night theme by the city's Bangkok hour, with street ambience by day and
    /// rain when the server says it rains; mini-games switch to their own theme. Volumes are per device
    /// (PlayerPrefs) and either can be turned off in the profile screen.
    /// Every call is safe before the manager exists or when a clip is missing: sound never breaks play.
    /// </summary>
    public sealed class GameAudio : MonoBehaviour
    {
        public static GameAudio I { get; private set; }

        private AudioSource _musicA, _musicB, _amb, _rain, _loop, _sfx;
        private AudioSource _musicNow;
        private string _musicName, _ambName, _loopName;
        private string _override; // a mini-game's theme wins over the town's
        private float _fade = 1f;
        private const float FadeSeconds = 1.6f;
        private static readonly Dictionary<string, AudioClip> Clips = new Dictionary<string, AudioClip>();
        private static readonly Dictionary<string, float> LastPlayed = new Dictionary<string, float>();

        public static float MusicVolume
        {
            get => PlayerPrefs.GetFloat("hwo.vol.music", 0.55f);
            set { PlayerPrefs.SetFloat("hwo.vol.music", Mathf.Clamp01(value)); PlayerPrefs.Save(); }
        }

        public static float SfxVolume
        {
            get => PlayerPrefs.GetFloat("hwo.vol.sfx", 0.9f);
            set { PlayerPrefs.SetFloat("hwo.vol.sfx", Mathf.Clamp01(value)); PlayerPrefs.Save(); }
        }

        public static GameAudio Create(Transform parent)
        {
            var go = new GameObject("GameAudio");
            go.transform.SetParent(parent, false);
            var a = go.AddComponent<GameAudio>();
            I = a;
            a._musicA = a.Source(true);
            a._musicB = a.Source(true);
            a._amb = a.Source(true);
            a._rain = a.Source(true);
            a._loop = a.Source(true);
            a._sfx = a.Source(false);
            a._musicNow = a._musicA;
            return a;
        }

        private AudioSource Source(bool loop)
        {
            var s = gameObject.AddComponent<AudioSource>();
            s.loop = loop;
            s.playOnAwake = false;
            s.spatialBlend = 0f;
            return s;
        }

        public static AudioClip Clip(string name)
        {
            AudioClip c;
            if (Clips.TryGetValue(name, out c)) return c;
            c = Resources.Load<AudioClip>("Art/Audio/" + name);
            if (c == null) Debug.LogWarning("[HappyWorld] sound missing: " + name);
            Clips[name] = c;
            return c;
        }

        /// <summary>One-shot sound ("sfx_coin"). Repeats of the same sound within 50 ms are dropped.</summary>
        public static void Sfx(string name, float volume = 1f, float pitch = 1f)
        {
            if (I == null || SfxVolume <= 0f) return;
            float last;
            if (LastPlayed.TryGetValue(name, out last) && Time.unscaledTime - last < 0.05f) return;
            LastPlayed[name] = Time.unscaledTime;
            var c = Clip(name);
            if (c == null) return;
            I._sfx.pitch = pitch;
            I._sfx.PlayOneShot(c, volume * SfxVolume);
        }

        /// <summary>Town mood from the city clock: called every frame by GameSession (cheap when nothing changes).</summary>
        public void SetTown(float hour, string weather)
        {
            bool day = hour >= 6f && hour < 18.5f;
            Music(_override ?? (day ? "music_town_day" : "music_town_night"));
            Ambience(_override == null && day && weather != "RAIN" ? "amb_city" : null);
            Rain(weather == "RAIN");
        }

        /// <summary>A mini-game (or null when it ends) takes over the music; its looping effect (grill, pouring) too.</summary>
        public void SetOverride(string music, string loop = null)
        {
            _override = music;
            if (music != null) Music(music);
            Loop(loop);
        }

        public void Loop(string name)
        {
            if (name == _loopName) return;
            _loopName = name;
            var c = name != null ? Clip(name) : null;
            if (c == null) { _loop.Stop(); return; }
            _loop.clip = c;
            _loop.volume = 0.5f * SfxVolume;
            _loop.Play();
        }

        public void SetLoopVolume(float v) => _loop.volume = Mathf.Clamp01(v) * SfxVolume;

        private void Music(string name)
        {
            if (name == _musicName) return;
            var c = Clip(name);
            if (c == null) return;
            _musicName = name;
            var next = _musicNow == _musicA ? _musicB : _musicA;
            next.clip = c;
            next.volume = 0f;
            next.Play();
            _musicNow = next;
            _fade = 0f;
        }

        private void Ambience(string name)
        {
            if (name == _ambName) return;
            _ambName = name;
            var c = name != null ? Clip(name) : null;
            if (c == null) { _amb.Stop(); return; }
            _amb.clip = c;
            _amb.Play();
        }

        private void Rain(bool on)
        {
            if (on == _rain.isPlaying) return;
            if (!on) { _rain.Stop(); return; }
            var c = Clip("amb_rain");
            if (c == null) return;
            _rain.clip = c;
            _rain.Play();
        }

        private void Update()
        {
            var mv = MusicVolume;
            _fade = Mathf.Min(1f, _fade + Time.unscaledDeltaTime / FadeSeconds);
            var other = _musicNow == _musicA ? _musicB : _musicA;
            _musicNow.volume = _fade * mv * 0.8f;
            other.volume = (1f - _fade) * mv * 0.8f;
            if (_fade >= 1f && other.isPlaying) other.Stop();
            _amb.volume = 0.35f * SfxVolume;
            _rain.volume = 0.5f * SfxVolume;
        }
    }
}
