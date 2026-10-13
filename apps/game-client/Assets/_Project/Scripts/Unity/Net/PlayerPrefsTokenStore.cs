using HappyWorld.Core.Api;
using UnityEngine;

namespace HappyWorld.Unity.Net
{
    /// <summary>
    /// Keeps the refresh token between app launches.
    /// DEV ONLY: PlayerPrefs is not encrypted. Before release replace with Android Keystore / iOS Keychain
    /// (see docs/unity-client.md). The access token is never stored.
    /// </summary>
    public sealed class PlayerPrefsTokenStore : ITokenStore
    {
        private const string Key = "hwo.refresh";

        public string LoadRefreshToken() => PlayerPrefs.GetString(Key, null);

        public void SaveRefreshToken(string token)
        {
            PlayerPrefs.SetString(Key, token);
            PlayerPrefs.Save();
        }

        public void Clear()
        {
            PlayerPrefs.DeleteKey(Key);
            PlayerPrefs.Save();
        }
    }
}
