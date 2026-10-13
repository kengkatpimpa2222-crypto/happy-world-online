namespace HappyWorld.Core.Api
{
    /// <summary>Persists the refresh token (the guest's only credential). Unity: PlayerPrefsTokenStore.</summary>
    public interface ITokenStore
    {
        string LoadRefreshToken();
        void SaveRefreshToken(string token);
        void Clear();
    }

    public sealed class InMemoryTokenStore : ITokenStore
    {
        private string _token;
        public string LoadRefreshToken() => _token;
        public void SaveRefreshToken(string token) => _token = token;
        public void Clear() => _token = null;
    }
}
