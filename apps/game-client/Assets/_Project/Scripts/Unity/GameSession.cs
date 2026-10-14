using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HappyWorld.Core.Api;
using Avatar = HappyWorld.Core.Api.Avatar; // UnityEngine also has an Avatar type
using HappyWorld.Core.Localization;
using HappyWorld.Core.Realtime;
using HappyWorld.Core.World;
using HappyWorld.Unity.Audio;
using HappyWorld.Unity.Net;
using HappyWorld.Unity.UI;
using HappyWorld.Unity.World;
using UnityEngine;
using UnityEngine.EventSystems;

namespace HappyWorld.Unity
{
    /// <summary>
    /// The game's single entry point. It starts itself in ANY scene (RuntimeInitializeOnLoadMethod),
    /// so opening the project and pressing Play is enough: no scene or prefab wiring is required.
    ///
    /// Flow: sign in (guest) → character creator (first time) → connect realtime → join PLAZA → walk.
    /// The server decides everything that matters (money, rewards, positions it accepts, who you can see).
    /// </summary>
    public sealed class GameSession : MonoBehaviour
    {
        public static GameSession I { get; private set; }

        public HappyWorldApi Api { get; private set; }
        public RealtimeClient Rt { get; private set; }
        public PlayerProfile Profile { get; private set; }
        public CityLayout Layout { get; private set; }
        public Zone CurrentZone { get; private set; }
        public readonly CityState City = new CityState();
        public readonly MovementLimiter Limiter = new MovementLimiter();

        private GameScreens _ui;
        private LocalPlayerController _local;
        private AvatarView _localView;
        private Transform _zoneRoot, _playersRoot;
        private readonly Dictionary<string, AvatarView> _views = new Dictionary<string, AvatarView>();
        private List<CityBuilder.PortalInfo> _portals = new List<CityBuilder.PortalInfo>();
        private bool _joining, _replaced, _reconnecting;
        private int _autoRetries;
        private float _joinedAt;
        private string _wantZone = "PLAZA";
        private MarketView _market;
        private float _marketRefreshAt = -1f;
        private bool _marketLoading;
        private const float MarketResyncSeconds = 30f;
        // Phase 6: city festival
        public FestivalState Festival { get; private set; }
        private float _festivalFetchedAt;
        private float _festivalRefreshAt = -1f;
        private bool _festivalLoading;
        private FestivalDecor _decor;
        private const float FestivalResyncSeconds = 60f;
        // Phase 7: the living city (time of day, weather, city projects)
        public CityNow CityNow { get; private set; }
        public CityGrowth Growth { get; private set; }
        private float _cityNowFetchedAt;
        private float _cityNowRefreshAt = -1f, _growthRefreshAt = -1f, _cityLineAt;
        private bool _cityNowLoading, _growthLoading;
        private CityAmbience _ambience;
        private CityProjectsView _projects;
        private const float CityNowResyncSeconds = 600f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (FindAnyObjectByType<GameSession>() == null) new GameObject("HappyWorld").AddComponent<GameSession>();
        }

        // ---------- configuration ----------

        /// <summary>
        /// Backend address. Editor/PC: localhost. Android emulator: 10.0.2.2 (the host machine).
        /// A real phone needs the PC's LAN IP: type it on the login screen (development builds only).
        /// </summary>
        public static string ServerUrl
        {
            get
            {
                var saved = PlayerPrefs.GetString("hwo.server", "");
                if (!string.IsNullOrEmpty(saved)) return saved;
                return Application.platform == RuntimePlatform.Android ? "http://10.0.2.2:3000" : "http://localhost:3000";
            }
            set
            {
                PlayerPrefs.SetString("hwo.server", value.Trim());
                PlayerPrefs.Save();
            }
        }

        /// <summary>Random per-install id (not the hardware id) used by the server only for abuse limits.</summary>
        private static string DeviceId
        {
            get
            {
                var id = PlayerPrefs.GetString("hwo.device", "");
                if (id.Length == 0)
                {
                    id = "u-" + Guid.NewGuid().ToString("N");
                    PlayerPrefs.SetString("hwo.device", id);
                    PlayerPrefs.Save();
                }
                return id;
            }
        }

        // ---------- lifecycle ----------

        private void Awake()
        {
            if (I != null) { Destroy(gameObject); return; }
            I = this;
            DontDestroyOnLoad(gameObject);
            gameObject.AddComponent<MainThread>();
            Application.targetFrameRate = 60;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            Texts.Locale = "th";

            if (Camera.main == null)
            {
                var cam = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
                cam.tag = "MainCamera";
            }
            Camera.main.backgroundColor = new Color(0.62f, 0.85f, 1f);
            Camera.main.clearFlags = CameraClearFlags.SolidColor;
            if (FindAnyObjectByType<Light>() == null)
            {
                var sun = new GameObject("Sun").AddComponent<Light>();
                sun.type = LightType.Directional;
                sun.intensity = 1.1f;
                sun.transform.rotation = Quaternion.Euler(50, -30, 0);
            }
            RenderSettings.ambientLight = new Color(0.6f, 0.6f, 0.65f);
            _ambience = CityAmbience.Create(transform);
            GameAudio.Create(transform).SetTown(12f, "SUNNY"); // the day theme already plays on the login screen

            _zoneRoot = new GameObject("Zone").transform;
            _playersRoot = new GameObject("Players").transform;
            DontDestroyOnLoad(_zoneRoot.gameObject);
            DontDestroyOnLoad(_playersRoot.gameObject);

            City.Added += OnPlayerAdded;
            City.Removed += OnPlayerRemoved;
            City.Corrected += (x, y) =>
            {
                if (_local == null) return;
                _local.Teleport(x, y);
                Limiter.Corrected(x, y, Time.time);
            };
        }

        private void Start()
        {
            _ui = new GameScreens(this);
            var store = new PlayerPrefsTokenStore();
            if (!string.IsNullOrEmpty(store.LoadRefreshToken())) Run(ResumeAsync);
            else _ui.ShowLogin(null);
        }

        private void OnApplicationPause(bool paused)
        {
            // Coming back from background: presence may have expired (60 s), so rejoin the zone.
            if (!paused && Rt != null && Rt.Connected && City.Zone != null) Run(() => JoinZoneAsync(City.Zone));
        }

        private void OnDestroy()
        {
            if (Rt != null)
            {
                Rt.CloseAsync();
                Rt.Dispose();
            }
        }

        // ---------- sign in ----------

        private HappyWorldApi NewApi() => new HappyWorldApi(ServerUrl, new UnityWebRequestTransport(), new PlayerPrefsTokenStore());

        private async Task ResumeAsync()
        {
            _ui.ShowLoading(Texts.T("ui.connecting"));
            Api = NewApi();
            try
            {
                await Api.RefreshAsync();
            }
            catch (ApiException e) when (e.Status == 401)
            {
                new PlayerPrefsTokenStore().Clear(); // revoked/expired: start over as a new guest
                _ui.ShowLogin(null);
                return;
            }
            catch (ApiException e)
            {
                _ui.ShowLogin(Texts.Error(e.Code, e.Message));
                return;
            }
            await AfterSignInAsync();
        }

        /// <summary>New guest. The age band decides who the player can meet (same band only) and cannot be changed later.</summary>
        public async Task NewGuestAsync(string ageBand)
        {
            _ui.ShowLoading(Texts.T("ui.connecting"));
            Api = NewApi();
            try
            {
                await Api.CreateGuestAsync(DeviceId, ageBand, "th");
            }
            catch (ApiException e)
            {
                _ui.ShowLogin(Texts.Error(e.Code, e.Message));
                return;
            }
            await AfterSignInAsync();
        }

        private async Task AfterSignInAsync()
        {
            Profile = await Api.GetProfileAsync();
            City.MyUserId = Profile.userId;
            if (PlayerPrefs.GetInt("hwo.created." + Profile.userId, 0) == 1) await EnterCityAsync();
            else _ui.ShowCharacterCreator(Profile, await Api.GetInventoryAsync(), true);
        }

        /// <summary>Saves name/avatar on the server (it checks the name filter and item ownership).</summary>
        public async Task SaveCharacterAsync(string displayName, Avatar avatar, bool firstTime)
        {
            Profile = await Api.UpdateProfileAsync(displayName: displayName, avatar: avatar);
            PlayerPrefs.SetInt("hwo.created." + Profile.userId, 1);
            PlayerPrefs.Save();
            if (_localView != null)
            {
                _localView.ApplyAvatar(Profile.avatar);
                _localView.SetName(Profile.displayName, Profile.level, false);
            }
            if (firstTime) await EnterCityAsync();
            else _ui.ShowHud();
        }

        public async Task LogoutAsync()
        {
            try { if (Rt != null) await Rt.CloseAsync(); } catch (Exception) { }
            try { await Api.LogoutAsync(); } catch (ApiException) { }
            ClearWorld();
            _ui.ShowLogin(null);
        }

        // ---------- city ----------

        private async Task EnterCityAsync()
        {
            _ui.ShowLoading(Texts.T("ui.loading"));
            Layout = await Api.GetCityAsync();
            if (_local == null) SpawnLocalPlayer();
            _ui.ShowHud();
            await ConnectRealtimeAsync();
            RefreshFestival();
            RefreshCityNow();
            RefreshGrowth();
            Run(ShowWelcomeBackReportAsync);
        }

        /// <summary>Living shop: if customers came while the player was away, show what happened.</summary>
        private async Task ShowWelcomeBackReportAsync()
        {
            var shops = await Api.GetBusinessesAsync();
            if (shops.Count == 0 || shops[0].stall == null || (shops[0].stall.status == "CLOSED" && shops[0].stall.cashBox == 0)) return;
            var report = await Api.GetShopReportAsync(shops[0].id);
            if (report.hasNews && !_ui.ModalOpen) _ui.ShowShopReport(report, true);
        }

        private void SpawnLocalPlayer()
        {
            var go = new GameObject("LocalPlayer");
            DontDestroyOnLoad(go);
            _local = go.AddComponent<LocalPlayerController>();
            _local.Joystick = _ui.Joystick;
            _localView = AvatarView.Create(go.transform, Profile.userId, Profile.displayName, Profile.level);
            Destroy(_localView.GetComponent<Collider>()); // the CharacterController is the local collider
            _localView.ApplyAvatar(Profile.avatar);
            var follow = Camera.main.gameObject.GetComponent<CameraFollow>();
            if (follow == null) follow = Camera.main.gameObject.AddComponent<CameraFollow>(); // no ?? on Unity objects
            follow.Target = go.transform;
            _ambience.Target = go.transform;
        }

        private async Task ConnectRealtimeAsync()
        {
            if (Rt != null)
            {
                try { await Rt.CloseAsync(); } catch (Exception) { }
                Rt.Dispose();
            }
            var path = Layout != null && Layout.realtime != null ? Layout.realtime.path : RealtimeClient.Path;
            Rt = new RealtimeClient(Api.ServerRoot, Api.AccessToken, path);
            City.Bind(Rt);
            Rt.Ready += _ =>
            {
                _autoRetries = 0;
                Run(() => JoinZoneAsync(_wantZone));
            };
            Rt.ZoneState += OnZoneState;
            Rt.PresetChat += m =>
            {
                AvatarView v;
                if (_views.TryGetValue(m.userId, out v)) v.ShowChat(PresetText(m.presetId));
            };
            Rt.FriendRequest += _ => _ui.Toast(Texts.T("ui.incoming") + " ✉", Texts.T("ui.friends"), () => _ui.ShowFriends());
            Rt.FriendAccepted += _ => _ui.Toast(Texts.T("ui.now_friends") + " ♥");
            Rt.FriendOnline += id => MarkFriend(id, true);
            Rt.FriendInvited += inv => _ui.Toast(inv.displayName + " ชวนคุณไป " + Texts.T("zone." + (inv.zone ?? "plaza").ToLowerInvariant()),
                "ไปเลย", () => Run(() => JoinZoneAsync(inv.zone ?? "PLAZA")));
            Rt.MarketActivity += a =>
            {
                if (_market != null) _market.OnActivity(a);
            };
            Rt.MarketStallChanged += _ =>
            {
                if (_market != null) _marketRefreshAt = Time.time + 0.8f; // batch several changes into one GET
            };
            Rt.ShopUpdated += u =>
            {
                _ui.OnShopUpdated(u);
                if (u.festivalPoints > 0) _festivalRefreshAt = Time.time + 2f; // my sales moved the city's progress
                // my sales also build the city; the scaffold only needs an occasional refresh
                if (u.cityPoints > 0 && (_growthRefreshAt < 0 || _growthRefreshAt > Time.time + 20f)) _growthRefreshAt = Time.time + 20f;
            };
            Rt.FestivalMilestone += m =>
            {
                if (_decor != null) _decor.Celebrate(m.tier);
                GameAudio.Sfx("sfx_fanfare");
                _ui.Toast(FestivalPresenter.MilestoneText(m.tier), "ดูรางวัล", () => _ui.ShowFestival());
                RefreshFestival();
            };
            Rt.CityGrowth += e =>
            {
                GameAudio.Sfx("sfx_fanfare");
                _ui.Toast(CityLifePresenter.BuiltText(e), Texts.T("ui.city"), () => _ui.ShowCity());
                RefreshGrowth();
            };
            Rt.SessionReplaced += () =>
            {
                _replaced = true;
                _ui.ShowBanner(Texts.T("ui.session_replaced"), null);
            };
            Rt.ConnectError += msg =>
            {
                // usually an expired access token: refresh and retry once by itself, then let the player decide
                if (_autoRetries++ < 1) Run(ReconnectAsync);
                else _ui.ShowBanner(Texts.T("DISCONNECTED"), () => Run(ReconnectAsync));
            };
            Rt.Disconnected += reason =>
            {
                if (_replaced || this == null) return;
                _ui.ShowBanner(Texts.T("DISCONNECTED"), () => Run(ReconnectAsync));
            };
            await Rt.ConnectAsync();
        }

        private async Task ReconnectAsync()
        {
            if (_reconnecting) return;
            _reconnecting = true;
            try
            {
                _ui.ShowBanner(Texts.T("ui.connecting"), null);
                await Api.RefreshAsync();
                await ConnectRealtimeAsync();
                _ui.HideBanner();
            }
            catch (Exception e)
            {
                var api = e as ApiException;
                _ui.ShowBanner(api != null ? Texts.Error(api.Code, api.Message) : Texts.T("DISCONNECTED"), () => Run(ReconnectAsync));
            }
            finally
            {
                _reconnecting = false;
            }
        }

        public async Task JoinZoneAsync(string zone)
        {
            if (Rt == null || _joining) return;
            _joining = true;
            _wantZone = zone;
            try
            {
                var ack = await Rt.JoinZoneAsync(zone);
                if (!ack.Ok) _ui.Toast(Texts.Error(ack.ErrorCode, ack.ErrorMessage));
                // the zone itself is applied in OnZoneState (sent right before the ack)
            }
            finally
            {
                _joining = false;
            }
        }

        private void OnZoneState(ZoneState s)
        {
            CurrentZone = Layout.zones.FirstOrDefault(z => z.id == s.zone);
            if (CurrentZone == null) return;
            _portals = CityBuilder.Build(CurrentZone, _zoneRoot); // also removes the previous zone's market view
            _market = null;
            if (CurrentZone.id == "MARKET")
            {
                _market = MarketView.Create(_zoneRoot);
                RefreshMarket();
            }
            _decor = FestivalDecor.Create(_zoneRoot, CurrentZone);
            _decor.Apply(Festival != null ? Festival.current : null);
            _ambience.BuildLamps(CurrentZone, _zoneRoot);
            _projects = CityProjectsView.Create(_zoneRoot, CurrentZone);
            _projects.Apply(Growth);
            _local.Width = CurrentZone.width;
            _local.Height = CurrentZone.height;
            _local.Teleport(s.you.x, s.you.y);
            Limiter.Reset(s.you.x, s.you.y, CurrentZone.width, CurrentZone.height, Time.time);
            _joinedAt = Time.time;
            _ui.SetZone(Texts.T(CurrentZone.nameKey));
        }

        private void OnPlayerAdded(RemotePlayer p)
        {
            var v = AvatarView.Create(_playersRoot, p.UserId, p.DisplayName, p.Level);
            v.ApplyAvatar(p.Avatar);
            v.SetName(p.DisplayName, p.Level, p.IsFriend);
            v.transform.position = new Vector3(p.X, 0.05f, p.Y);
            _views[p.UserId] = v;
        }

        private void OnPlayerRemoved(string userId)
        {
            AvatarView v;
            if (_views.TryGetValue(userId, out v))
            {
                Destroy(v.gameObject);
                _views.Remove(userId);
            }
        }

        private void MarkFriend(string userId, bool friend)
        {
            var p = City.Get(userId);
            AvatarView v;
            if (p != null && _views.TryGetValue(userId, out v))
            {
                p.IsFriend = friend;
                v.SetName(p.DisplayName, p.Level, friend);
            }
        }

        /// <summary>Re-reads GET /market when standing in the market (after my shop changed, or a stall-changed event).</summary>
        public void RefreshMarket()
        {
            if (_market == null || _marketLoading) return;
            _marketLoading = true;
            _marketRefreshAt = Time.time + MarketResyncSeconds; // periodic resync corrects any drift in shelf counts
            Run(async () =>
            {
                try
                {
                    var state = await Api.GetMarketAsync();
                    if (_market != null) _market.Apply(state);
                }
                finally
                {
                    _marketLoading = false;
                }
            });
        }

        /// <summary>The server's clock (UTC), from the last GET /festival plus the time since.</summary>
        public DateTime ServerNow => Festival != null
            ? Festival.now.ToUniversalTime().AddSeconds(Time.realtimeSinceStartup - _festivalFetchedAt)
            : DateTime.UtcNow;

        /// <summary>Re-reads GET /festival: HUD banner, city decorations, shop hints.</summary>
        public void RefreshFestival()
        {
            if (_festivalLoading || Api == null) return;
            _festivalLoading = true;
            _festivalRefreshAt = Time.time + FestivalResyncSeconds;
            Run(async () =>
            {
                try
                {
                    OnFestival(await Api.GetFestivalAsync());
                }
                finally
                {
                    _festivalLoading = false;
                }
            });
        }

        /// <summary>Applies a fresh festival state (also called by the festival panel).</summary>
        public void OnFestival(FestivalState st)
        {
            if (st == null || st.current == null) return;
            Festival = st;
            _festivalFetchedAt = Time.realtimeSinceStartup;
            _ui.SetFestival(st);
            if (_decor != null) _decor.Apply(st.current);
            // refresh right when the festival starts or ends, so the city changes on time
            var now = ServerNow;
            var edge = st.current.status == "UPCOMING" ? st.current.startsAt : st.current.endsAt;
            var untilEdge = (float)(edge.ToUniversalTime() - now).TotalSeconds + 2f;
            if (untilEdge > 0 && untilEdge < FestivalResyncSeconds) _festivalRefreshAt = Time.time + untilEdge;
        }

        // ---------- living city (Phase 7) ----------

        /// <summary>The city's Bangkok hour now (server hour plus the time since it was read).</summary>
        public float CityHour => CityLifePresenter.LocalHour(CityNow, Time.realtimeSinceStartup - _cityNowFetchedAt);

        /// <summary>Re-reads GET /city/now: sky, lamps, rain, HUD line, shop hints.</summary>
        public void RefreshCityNow()
        {
            if (_cityNowLoading || Api == null) return;
            _cityNowLoading = true;
            _cityNowRefreshAt = Time.time + CityNowResyncSeconds;
            Run(async () =>
            {
                try
                {
                    OnCityNow(await Api.GetCityNowAsync());
                }
                finally
                {
                    _cityNowLoading = false;
                }
            });
        }

        public void OnCityNow(CityNow n)
        {
            if (n == null) return;
            CityNow = n;
            _cityNowFetchedAt = Time.realtimeSinceStartup;
            // the weather changes every 3 hours: read it again right after the change
            var untilChange = (float)(n.nextChangeAt.ToUniversalTime() - n.now.ToUniversalTime()).TotalSeconds + 3f;
            if (untilChange > 0 && untilChange < CityNowResyncSeconds) _cityNowRefreshAt = Time.time + untilChange;
        }

        /// <summary>Re-reads GET /city/growth: buildings and scaffold in the current zone.</summary>
        public void RefreshGrowth()
        {
            if (_growthLoading || Api == null) return;
            _growthLoading = true;
            _growthRefreshAt = -1f;
            Run(async () =>
            {
                try
                {
                    OnCityGrowth(await Api.GetCityGrowthAsync());
                }
                finally
                {
                    _growthLoading = false;
                }
            });
        }

        public void OnCityGrowth(CityGrowth g)
        {
            if (g == null) return;
            Growth = g;
            if (_projects != null) _projects.Apply(g);
        }

        /// <summary>Called after the local player blocks someone: they disappear at once.</summary>
        public void HidePlayer(string userId) => City.Hide(userId);

        private void ClearWorld()
        {
            City.Clear();
            for (int i = _zoneRoot.childCount - 1; i >= 0; i--) Destroy(_zoneRoot.GetChild(i).gameObject);
            if (_local != null) Destroy(_local.gameObject);
            _local = null;
            _localView = null;
            Rt = null;
            Festival = null;
            _festivalRefreshAt = -1f;
            _decor = null;
            CityNow = null;
            Growth = null;
            _cityNowRefreshAt = -1f;
            _growthRefreshAt = -1f;
            _projects = null;
            if (_ambience != null)
            {
                _ambience.Target = null;
                _ambience.Apply(12f, "SUNNY"); // back to a plain sky (and no rain) on the login screen
            }
        }

        public string PresetText(string id)
        {
            var p = Layout != null ? Layout.presetMessages.FirstOrDefault(m => m.id == id) : null;
            return p != null ? p.textTh : id;
        }

        public async Task SendPresetAsync(string presetId)
        {
            if (Rt == null) return;
            var ack = await Rt.SendPresetAsync(presetId);
            if (ack.Ok) _localView.ShowChat(PresetText(presetId)); // the server sends it to everyone else
            else _ui.Toast(Texts.Error(ack.ErrorCode, ack.ErrorMessage));
        }

        // ---------- frame loop ----------

        private void Update()
        {
            if (Rt == null) return;
            Rt.Pump();
            City.Step(Time.deltaTime);
            foreach (var p in City.Players)
            {
                AvatarView v;
                if (!_views.TryGetValue(p.UserId, out v)) continue;
                var target = new Vector3(p.X, 0.05f, p.Y);
                var delta = target - v.transform.position;
                v.transform.position = target;
                if (new Vector2(delta.x, delta.z).sqrMagnitude > 0.0001f)
                    v.transform.rotation = Quaternion.Euler(0, MovementLimiter.DirToYawDegrees(p.Dir), 0);
            }

            if (_market != null && _marketRefreshAt > 0 && Time.time >= _marketRefreshAt) RefreshMarket();
            if (_festivalRefreshAt > 0 && Time.time >= _festivalRefreshAt) RefreshFestival();
            if (_cityNowRefreshAt > 0 && Time.time >= _cityNowRefreshAt) RefreshCityNow();
            if (_growthRefreshAt > 0 && Time.time >= _growthRefreshAt) RefreshGrowth();
            if (CityNow != null)
            {
                var hour = CityHour;
                _ambience.Apply(hour, CityNow.weather);
                GameAudio.I.SetTown(hour, CityNow.weather);
                if (Time.time >= _cityLineAt)
                {
                    _cityLineAt = Time.time + 1f;
                    _ui.SetCityLine(CityLifePresenter.HudLine(CityNow, hour));
                }
            }

            if (_local == null || CurrentZone == null || !Rt.Connected) return;
            _local.InputEnabled = !_ui.ModalOpen;

            var pos = _local.ServerPos;
            float sx, sy;
            int dir, seq;
            if (Limiter.TryMove(pos.x, pos.y, Time.time, out sx, out sy, out dir, out seq))
                Fire(Rt.SendMoveAsync(sx, sy, dir, seq));
            if (Limiter.HeartbeatDue(Time.time)) Fire(Rt.HeartbeatAsync());

            // walking onto a portal changes zone
            if (!_joining && Time.time - _joinedAt > 1.5f)
            {
                foreach (var portal in _portals)
                {
                    if (Vector2.Distance(pos, new Vector2(portal.Position.x, portal.Position.z)) < 1.3f)
                    {
                        Run(() => JoinZoneAsync(portal.To));
                        break;
                    }
                }
            }

            HandleTap();
        }

        /// <summary>Tap/click on another player opens their actions (add friend, block, report); on a market stall, its details.</summary>
        private void HandleTap()
        {
            if (!Input.GetMouseButtonDown(0) || _ui.ModalOpen) return;
            if (EventSystem.current != null)
            {
                if (EventSystem.current.IsPointerOverGameObject()) return;
                for (int i = 0; i < Input.touchCount; i++)
                    if (EventSystem.current.IsPointerOverGameObject(Input.GetTouch(i).fingerId)) return;
            }
            var ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            RaycastHit hit;
            if (!Physics.Raycast(ray, out hit, 200f, ~0, QueryTriggerInteraction.Collide)) return;
            var stall = hit.collider.GetComponentInParent<StallView>();
            if (stall != null && stall.Data != null)
            {
                _ui.ShowStallInfo(stall.Data);
                return;
            }
            var view = hit.collider.GetComponentInParent<AvatarView>();
            if (view == null || view.UserId == Profile.userId) return;
            var p = City.Get(view.UserId);
            if (p != null) _ui.ShowPlayerActions(p);
        }

        // ---------- helpers ----------

        /// <summary>Runs an async UI action and shows any server/network error as a Thai toast.</summary>
        public async void Run(Func<Task> action)
        {
            try
            {
                await action();
            }
            catch (ApiException e)
            {
                Debug.LogWarning("API " + e.Status + " " + e.Code + " " + e.Message + " req=" + e.RequestId);
                if (_ui != null) _ui.Toast(Texts.Error(e.Code, e.Message));
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                if (_ui != null) _ui.Toast(Texts.T("error.generic"));
            }
        }

        private static async void Fire(Task t)
        {
            try { await t; }
            catch (Exception) { /* moves/heartbeats are best effort; a dropped socket raises Disconnected */ }
        }
    }
}
