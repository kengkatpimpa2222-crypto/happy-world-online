using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HappyWorld.Core.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HappyWorld.Core.Api
{
    /// <summary>
    /// Typed client for the Happy World Online REST API (/api/v1).
    /// - Keeps the access token in memory and the refresh token in an ITokenStore.
    /// - On 401 it refreshes once (single-flight) and retries the request.
    /// - Never sends a player id for "me": the server identifies the player from the token.
    /// </summary>
    public sealed class HappyWorldApi
    {
        private static readonly JsonSerializerSettings Json = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            MissingMemberHandling = MissingMemberHandling.Ignore,
            // keep ISO timestamps as the server sent them when the target is a string (e.g. ShopReport.untilIso,
            // which is sent back unchanged); DateTime fields are still parsed
            DateParseHandling = DateParseHandling.None,
        };

        private readonly IHttpTransport _http;
        private readonly ITokenStore _tokens;
        private readonly string _base;
        private readonly SemaphoreSlim _refreshLock = new SemaphoreSlim(1, 1);
        private string _accessToken;

        public string UserId { get; private set; }
        public string AccessToken => _accessToken;
        public bool HasSavedAccount => !string.IsNullOrEmpty(_tokens.LoadRefreshToken());

        /// <param name="baseUrl">e.g. http://10.0.2.2:3000 (Android emulator → host machine)</param>
        public HappyWorldApi(string baseUrl, IHttpTransport http, ITokenStore tokens)
        {
            _base = baseUrl.TrimEnd('/') + "/api/v1";
            _http = http;
            _tokens = tokens;
        }

        // ---------- Auth ----------

        /// <summary>Resumes the saved account, or creates a new guest when there is none (or it was revoked).</summary>
        public async Task<AuthResponse> SignInAsync(string deviceId, string ageBand, string locale = "th")
        {
            if (HasSavedAccount)
            {
                try
                {
                    return await RefreshAsync().ConfigureAwait(false);
                }
                catch (ApiException e) when (e.Status == 401)
                {
                    _tokens.Clear(); // revoked/expired: fall through to a new guest
                }
            }
            return await CreateGuestAsync(deviceId, ageBand, locale).ConfigureAwait(false);
        }

        public async Task<AuthResponse> CreateGuestAsync(string deviceId, string ageBand, string locale = "th")
        {
            var res = await SendAsync<AuthResponse>("POST", "/auth/guest", new { deviceId, ageBand, locale }, auth: false).ConfigureAwait(false);
            Accept(res);
            return res;
        }

        public async Task<AuthResponse> RefreshAsync()
        {
            var token = _tokens.LoadRefreshToken();
            if (string.IsNullOrEmpty(token)) throw new ApiException(401, "REFRESH_TOKEN_INVALID", "No saved account");
            var res = await SendAsync<AuthResponse>("POST", "/auth/refresh", new { refreshToken = token }, auth: false).ConfigureAwait(false);
            Accept(res);
            return res;
        }

        public Task LinkAccountAsync(string email, string password) =>
            SendAsync<JObject>("POST", "/auth/link-account", new { email, password });

        public async Task<AuthResponse> LoginAsync(string email, string password)
        {
            var res = await SendAsync<AuthResponse>("POST", "/auth/login", new { email, password }, auth: false).ConfigureAwait(false);
            Accept(res);
            return res;
        }

        public async Task LogoutAsync()
        {
            try { await SendAsync<JObject>("POST", "/auth/logout", null).ConfigureAwait(false); }
            finally
            {
                _tokens.Clear();
                _accessToken = null;
            }
        }

        private void Accept(AuthResponse res)
        {
            _accessToken = res.accessToken;
            UserId = res.userId;
            _tokens.SaveRefreshToken(res.refreshToken);
        }

        // ---------- Player ----------
        public Task<PlayerProfile> GetProfileAsync() => SendAsync<PlayerProfile>("GET", "/player/profile");

        /// <summary>Only non-null fields are sent. Avatar items must be owned (server checks).</summary>
        public Task<PlayerProfile> UpdateProfileAsync(string displayName = null, Avatar avatar = null, string locale = null) =>
            SendAsync<PlayerProfile>("PATCH", "/player/profile", new { displayName, avatar, locale });

        public Task<Wallet> GetWalletAsync() => SendAsync<Wallet>("GET", "/player/wallet");
        public Task<List<InventoryItem>> GetInventoryAsync() => SendAsync<List<InventoryItem>>("GET", "/player/inventory");

        // ---------- Store ----------
        public Task<List<CatalogItem>> GetCatalogAsync() => SendAsync<List<CatalogItem>>("GET", "/store/catalog");

        /// <param name="idempotencyKey">Create once per tap (IdempotencyKeys.New()); reuse it when retrying the same tap.</param>
        public Task<StorePurchaseResult> BuyItemAsync(string itemId, int quantity, string currency, string idempotencyKey) =>
            SendAsync<StorePurchaseResult>("POST", "/store/purchases", new { itemId, quantity, currency }, idempotencyKey: idempotencyKey);

        // ---------- Quests ----------
        public Task<List<Quest>> GetQuestsAsync() => SendAsync<List<Quest>>("GET", "/quests");
        public Task StartQuestAsync(string questId) => SendAsync<JObject>("POST", "/quests/" + Uri.EscapeDataString(questId) + "/start", null);
        public Task<QuestClaimResult> ClaimQuestAsync(string questId) =>
            SendAsync<QuestClaimResult>("POST", "/quests/" + Uri.EscapeDataString(questId) + "/claim", null);

        // ---------- Business (simulated shop) ----------
        public Task<List<Business>> GetBusinessesAsync() => SendAsync<List<Business>>("GET", "/businesses");
        public Task<Business> SetPriceAsync(string businessId, string productCode, int price) =>
            SendAsync<Business>("PATCH", "/businesses/" + businessId + "/pricing", new { productCode, price });
        public Task<JObject> RestockAsync(string businessId, string productCode, int quantity, string idempotencyKey) =>
            SendAsync<JObject>("POST", "/businesses/" + businessId + "/restock", new { productCode, quantity }, idempotencyKey: idempotencyKey);
        public Task<SaleResult> SellAsync(string businessId) => SendAsync<SaleResult>("POST", "/businesses/" + businessId + "/sell", null);
        public Task<JObject> UpgradeBusinessAsync(string businessId, int expectedLevel) =>
            SendAsync<JObject>("POST", "/businesses/" + businessId + "/upgrade", new { expectedLevel });

        // ---------- Living shop: market stall (Phase 5) ----------
        public Task<Business> GetBusinessAsync(string businessId) => SendAsync<Business>("GET", "/businesses/" + businessId);
        public Task<Business> RenameShopAsync(string businessId, string name) =>
            SendAsync<Business>("PATCH", "/businesses/" + businessId, new { name });
        /// <summary>Sets how many units stand on the stall (taken from / returned to the shop's storage). 0 = off the stall.</summary>
        public Task<Business> SetShelfAsync(string businessId, string productCode, int quantity) =>
            SendAsync<Business>("PUT", "/businesses/" + businessId + "/shelf", new { productCode, quantity });
        /// <param name="status">OPEN, PAUSED or CLOSED</param>
        public Task<Business> SetShopStatusAsync(string businessId, string status) =>
            SendAsync<Business>("POST", "/businesses/" + businessId + "/status", new { status });
        public Task<CollectResult> CollectShopAsync(string businessId, string idempotencyKey) =>
            SendAsync<CollectResult>("POST", "/businesses/" + businessId + "/collect", null, idempotencyKey: idempotencyKey);
        public Task<ShopReport> GetShopReportAsync(string businessId) => SendAsync<ShopReport>("GET", "/businesses/" + businessId + "/report");
        public Task AckShopReportAsync(string businessId, string untilIso) =>
            SendAsync<JObject>("POST", "/businesses/" + businessId + "/report/ack", new { until = untilIso });
        public Task<MarketState> GetMarketAsync() => SendAsync<MarketState>("GET", "/market");

        // ---------- City festival (Phase 6) ----------
        public Task<FestivalState> GetFestivalAsync() => SendAsync<FestivalState>("GET", "/festival");
        public Task<FestivalClaimResult> ClaimFestivalAsync(string festivalKey, int tier) =>
            SendAsync<FestivalClaimResult>("POST", "/festival/claim", new { festivalKey, tier });

        // ---------- Living city (Phase 7) ----------
        public Task<CityNow> GetCityNowAsync() => SendAsync<CityNow>("GET", "/city/now");
        public Task<CityGrowth> GetCityGrowthAsync() => SendAsync<CityGrowth>("GET", "/city/growth");

        // ---------- Mini-games: make goods by hand ----------
        public Task<MinigameOverview> GetMinigamesAsync() => SendAsync<MinigameOverview>("GET", "/minigames");
        public Task<MinigameRound> StartMinigameAsync(string businessId, string productCode) =>
            SendAsync<MinigameRound>("POST", "/minigames/start", new { businessId, productCode });
        /// <summary>Safe to retry: the round id makes it idempotent (the server returns the first result).</summary>
        public Task<MinigameResult> FinishMinigameAsync(string sessionId, int score) =>
            SendAsync<MinigameResult>("POST", "/minigames/" + sessionId + "/finish", new { score });

        // ---------- Friends / safety ----------
        public Task<FriendRequestResult> SendFriendRequestByCodeAsync(string friendCode) =>
            SendAsync<FriendRequestResult>("POST", "/friends/requests", new { friendCode = friendCode.Trim().ToUpperInvariant() });
        public Task<FriendRequestResult> SendFriendRequestAsync(string playerId) =>
            SendAsync<FriendRequestResult>("POST", "/friends/requests", new { playerId });
        public Task<List<IncomingRequest>> GetIncomingRequestsAsync() => SendAsync<List<IncomingRequest>>("GET", "/friends/requests/incoming");
        public Task<List<OutgoingRequest>> GetOutgoingRequestsAsync() => SendAsync<List<OutgoingRequest>>("GET", "/friends/requests/outgoing");
        public Task AcceptFriendRequestAsync(string requestId) => SendAsync<JObject>("POST", "/friends/requests/" + requestId + "/accept", null);
        public Task DeclineFriendRequestAsync(string requestId) => SendAsync<JObject>("POST", "/friends/requests/" + requestId + "/decline", null);
        public Task CancelFriendRequestAsync(string requestId) => SendAsync<JObject>("DELETE", "/friends/requests/" + requestId, null);
        public Task<List<Friend>> GetFriendsAsync() => SendAsync<List<Friend>>("GET", "/friends");
        public Task RemoveFriendAsync(string friendId) => SendAsync<JObject>("DELETE", "/friends/" + friendId, null);
        public Task BlockAsync(string playerId) => SendAsync<JObject>("POST", "/players/" + playerId + "/block", null);
        public Task UnblockAsync(string playerId) => SendAsync<JObject>("DELETE", "/players/" + playerId + "/block", null);
        public Task<List<BlockedPlayer>> GetBlockedAsync() => SendAsync<List<BlockedPlayer>>("GET", "/players/blocked");
        public Task<ReportResult> ReportAsync(string playerId, string reason, string contextCode = null, bool alsoBlock = false) =>
            SendAsync<ReportResult>("POST", "/moderation/reports", new { reportedPlayerId = playerId, reason, contextCode, alsoBlock });

        // ---------- City ----------
        public Task<CityLayout> GetCityAsync() => SendAsync<CityLayout>("GET", "/city");
        public Task<CityPlayers> GetCityPlayersAsync(string zone) => SendAsync<CityPlayers>("GET", "/city/players?zone=" + Uri.EscapeDataString(zone));

        /// <summary>Base URL of the server root (for the realtime connection).</summary>
        public string ServerRoot => _base.Substring(0, _base.Length - "/api/v1".Length);

        // ---------- plumbing ----------

        private async Task<T> SendAsync<T>(string method, string path, object body = null, bool auth = true, string idempotencyKey = null)
        {
            var result = await SendOnceAsync(method, path, body, auth, idempotencyKey).ConfigureAwait(false);
            if (result.Status == 401 && auth && HasSavedAccount)
            {
                var tokenBefore = _accessToken;
                await _refreshLock.WaitAsync().ConfigureAwait(false);
                try
                {
                    if (_accessToken == tokenBefore) await RefreshAsync().ConfigureAwait(false); // another call may have refreshed already
                }
                finally
                {
                    _refreshLock.Release();
                }
                result = await SendOnceAsync(method, path, body, auth, idempotencyKey).ConfigureAwait(false);
            }
            return Parse<T>(result);
        }

        private Task<HttpResult> SendOnceAsync(string method, string path, object body, bool auth, string idempotencyKey)
        {
            var spec = new HttpRequestSpec { Method = method, Url = _base + path };
            spec.Headers["Accept"] = "application/json";
            if (auth && _accessToken != null) spec.Headers["Authorization"] = "Bearer " + _accessToken;
            if (idempotencyKey != null) spec.Headers["Idempotency-Key"] = idempotencyKey;
            if (body != null) spec.Body = JsonConvert.SerializeObject(body, Json);
            return _http.SendAsync(spec);
        }

        internal static T Parse<T>(HttpResult r)
        {
            if (r.Status == 0) throw new ApiException(0, "NETWORK_ERROR", r.TransportError ?? "Network error");
            if (r.Status >= 200 && r.Status < 300)
            {
                if (string.IsNullOrEmpty(r.Body)) return default(T);
                return JsonConvert.DeserializeObject<T>(r.Body, Json);
            }
            string code = "HTTP_" + r.Status, message = "Request failed", requestId = null;
            try
            {
                var err = JObject.Parse(r.Body)["error"];
                if (err != null)
                {
                    code = (string)err["code"] ?? code;
                    message = (string)err["message"] ?? message;
                    requestId = (string)err["requestId"];
                }
            }
            catch (JsonException)
            {
                // non-JSON error body (proxy, HTML page): keep the generic code
            }
            throw new ApiException(r.Status, code, message, requestId);
        }
    }

    public static class IdempotencyKeys
    {
        /// <summary>32 hex chars, valid for the backend's Idempotency-Key rule [A-Za-z0-9_-]{8,64}.</summary>
        public static string New() => Guid.NewGuid().ToString("N");
    }
}
