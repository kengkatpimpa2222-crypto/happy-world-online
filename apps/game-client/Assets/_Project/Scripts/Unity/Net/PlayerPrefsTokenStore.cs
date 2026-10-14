using HappyWorld.Core.Api;
using UnityEngine;

namespace HappyWorld.Unity.Net
{
    /// <summary>
    /// Keeps the refresh token between app launches.
    /// DEV ONLY: PlayerPrefs is not encrypted. Before release replace with Android Keystore / iOS Keychain
    /// (see docs/unity-client.md). The access token is never stored.
    /// HappyWorldApi resumes on thread-pool threads (ConfigureAwait(false)) and PlayerPrefs is main-thread only,
    /// so the token is cached in memory and disk writes are posted to the main thread (guest login failed on
    /// device in Phase 4.5 with "TrySetSetString can only be called from the main thread").
    /// Construct it on the main thread (GameSession does).
    /// </summary>
    public sealed class PlayerPrefsTokenStore : ITokenStore
    {
        private const string Key = "hwo.refresh";
        private static readonly object Gate = new object();
        private static bool _loaded;
        private static string _token;

        public PlayerPrefsTokenStore()
        {
            lock (Gate)
            {
                if (_loaded) return;
                _token = PlayerPrefs.GetString(Key, null);
                _loaded = true;
            }
        }

        public string LoadRefreshToken()
        {
            lock (Gate) return _token;
        }

        public void SaveRefreshToken(string token)
        {
            lock (Gate) _token = token;
            MainThread.Post(() =>
            {
                PlayerPrefs.SetString(Key, token);
                PlayerPrefs.Save();
            });
        }

        public void Clear()
        {
            lock (Gate) _token = null;
            MainThread.Post(() =>
            {
                PlayerPrefs.DeleteKey(Key);
                PlayerPrefs.Save();
            });
        }
    }
}
