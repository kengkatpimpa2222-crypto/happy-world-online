using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HappyWorld.Core.Api;
using HappyWorld.Core.Net;
using HappyWorld.Core.Realtime;
using HappyWorld.Core.World;
using Xunit;

namespace HappyWorld.Core.Tests
{
    /// <summary>
    /// Runs the real client code against a running backend. Skipped (passes trivially) unless HWO_API_URL is set,
    /// e.g. HWO_API_URL=http://localhost:3000. Creates throwaway guest accounts; never point it at production.
    /// </summary>
    public class IntegrationTests
    {
        private static readonly string Url = string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HWO_API_URL")) ? null : Environment.GetEnvironmentVariable("HWO_API_URL");

        private static async Task<HappyWorldApi> NewGuest(string ageBand = AgeBand.Adult)
        {
            var api = new HappyWorldApi(Url, new DotNetHttpTransport(), new InMemoryTokenStore());
            await api.SignInAsync("unity-test-" + Guid.NewGuid().ToString("N"), ageBand);
            return api;
        }

        private static async Task Until(Func<bool> cond, params RealtimeClient[] pump)
        {
            var deadline = DateTime.UtcNow.AddSeconds(8);
            while (!cond())
            {
                if (DateTime.UtcNow > deadline) throw new TimeoutException("condition not met");
                foreach (var r in pump) r.Pump();
                await Task.Delay(20);
            }
        }

        [Fact]
        public async Task GuestProfileWalletStoreAndQuests()
        {
            if (Url == null) return;
            var tokens = new InMemoryTokenStore();
            var api = new HappyWorldApi(Url, new DotNetHttpTransport(), tokens);
            var auth = await api.SignInAsync("unity-test-" + Guid.NewGuid().ToString("N"), AgeBand.Adult);
            Assert.True(auth.isGuest);
            Assert.False(string.IsNullOrEmpty(tokens.LoadRefreshToken()));

            // resume the same account from the saved refresh token (what a restarted app does)
            var again = new HappyWorldApi(Url, new DotNetHttpTransport(), tokens);
            var resumed = await again.SignInAsync("ignored-device", AgeBand.Adult);
            Assert.Equal(auth.userId, resumed.userId);

            var name = "ทดสอบ" + new Random().Next(1000, 9999);
            var profile = await again.UpdateProfileAsync(displayName: name, avatar: new Avatar { bodyColor = "#FFAA33" });
            Assert.Equal(name, profile.displayName);
            Assert.Equal("#FFAA33", profile.avatar.bodyColor);
            Assert.False(string.IsNullOrEmpty(profile.friendCode));

            var bad = await Assert.ThrowsAsync<ApiException>(() => again.UpdateProfileAsync(displayName: "โทร0812345678"));
            Assert.Contains(bad.Code, new[] { "DISPLAY_NAME_REJECTED", "VALIDATION_FAILED" }); // phone numbers fail the schema regex first

            var wallet = await again.GetWalletAsync();
            Assert.Equal(500, wallet.Gold);
            Assert.NotEmpty(await again.GetInventoryAsync());

            var catalog = await again.GetCatalogAsync();
            var item = catalog.Where(c => c.priceGold.HasValue && c.priceGold <= 500).OrderBy(c => c.priceGold).First();
            var key = IdempotencyKeys.New();
            var buy1 = await again.BuyItemAsync(item.itemId, 1, "GOLD", key);
            var buy2 = await again.BuyItemAsync(item.itemId, 1, "GOLD", key); // retry of the same tap
            Assert.False(buy1.replayed);
            Assert.True(buy2.replayed);
            Assert.Equal(500 - item.priceGold.Value, (await again.GetWalletAsync()).Gold);

            var quests = await again.GetQuestsAsync();
            Assert.NotEmpty(quests);
            var q = quests.First(x => x.canStart && x.progress == null);
            await again.StartQuestAsync(q.id);
            var notDone = await Assert.ThrowsAsync<ApiException>(() => again.ClaimQuestAsync(q.id));
            Assert.Equal("QUEST_NOT_COMPLETED", notDone.Code);

            var biz = await again.GetBusinessesAsync();
            Assert.Single(biz);
            Assert.NotEmpty(biz[0].products);
        }

        [Fact]
        public async Task FriendsBlockAndReport()
        {
            if (Url == null) return;
            var a = await NewGuest();
            var b = await NewGuest();
            var c = await NewGuest();
            var bProfile = await b.GetProfileAsync();

            var req = await a.SendFriendRequestByCodeAsync(bProfile.friendCode.ToLowerInvariant());
            Assert.True(req.created);
            var incoming = await b.GetIncomingRequestsAsync();
            Assert.Contains(incoming, r => r.from.userId == a.UserId);
            await b.AcceptFriendRequestAsync(incoming.First(r => r.from.userId == a.UserId).requestId);
            Assert.Contains(await a.GetFriendsAsync(), f => f.userId == b.UserId);

            // decline path
            await c.SendFriendRequestAsync(a.UserId);
            var fromC = (await a.GetIncomingRequestsAsync()).First(r => r.from.userId == c.UserId);
            await a.DeclineFriendRequestAsync(fromC.requestId);
            Assert.Empty(await a.GetIncomingRequestsAsync());

            var report = await a.ReportAsync(c.UserId, ReportReason.Spam, "CITY", alsoBlock: true);
            Assert.True(report.blocked);
            Assert.Contains(await a.GetBlockedAsync(), x => x.playerId == c.UserId);
            var blockedReq = await Assert.ThrowsAsync<ApiException>(() => c.SendFriendRequestAsync(a.UserId));
            Assert.Equal("FRIEND_REQUEST_NOT_ALLOWED", blockedReq.Code);
        }

        [Fact]
        public async Task TwoPlayersSeeEachOtherJoinMoveChatAndLeave()
        {
            if (Url == null) return;
            var a = await NewGuest(AgeBand.Teen);
            var b = await NewGuest(AgeBand.Teen);
            var layout = await a.GetCityAsync();
            var plaza = layout.zones.First(z => z.id == "PLAZA");
            Assert.Equal("/realtime", layout.realtime.@namespace);

            var ra = new RealtimeClient(a.ServerRoot, a.AccessToken, layout.realtime.path);
            var rb = new RealtimeClient(b.ServerRoot, b.AccessToken, layout.realtime.path);
            var cityA = new CityState { MyUserId = a.UserId };
            cityA.Bind(ra);
            string readyA = null, readyB = null;
            ra.Ready += id => readyA = id;
            rb.Ready += id => readyB = id;
            PresetChat chat = null;
            ra.PresetChat += m => chat = m;
            try
            {
                await ra.ConnectAsync();
                await rb.ConnectAsync();
                await Until(() => readyA != null && readyB != null, ra, rb);
                Assert.Equal(a.UserId, readyA);

                var joinA = await ra.JoinZoneAsync("PLAZA");
                Assert.True(joinA.Ok, joinA.ErrorCode);
                var joinB = await rb.JoinZoneAsync("PLAZA");
                Assert.True(joinB.Ok, joinB.ErrorCode);
                await Until(() => cityA.Get(b.UserId) != null, ra, rb);
                Assert.Equal(plaza.spawn.x, cityA.Get(b.UserId).TargetX);

                // B walks with the same limiter the game uses
                var lim = new MovementLimiter();
                lim.Reset(plaza.spawn.x, plaza.spawn.y, plaza.width, plaza.height, 0f);
                Assert.True(lim.TryMove(plaza.spawn.x + 0.5f, plaza.spawn.y, 0.2f, out var sx, out var sy, out var dir, out var seq));
                await Task.Delay(200);
                await rb.SendMoveAsync(sx, sy, dir, seq);
                await Until(() => Math.Abs(cityA.Get(b.UserId).TargetX - sx) < 0.001f, ra, rb);

                // a cheat-speed jump is corrected by the server
                MoveCorrected corrected = null;
                rb.MoveCorrected += m => corrected = m;
                await rb.SendMoveAsync(plaza.spawn.x + 20, plaza.spawn.y, 2, seq + 1);
                await Until(() => corrected != null, ra, rb);
                Assert.Equal(sx, corrected.x, 3);

                var sent = await rb.SendPresetAsync("HELLO");
                Assert.True(sent.Ok, sent.ErrorCode);
                await Until(() => chat != null, ra, rb);
                Assert.Equal("HELLO", chat.presetId);
                Assert.Equal(b.UserId, chat.userId);

                var bad = await rb.SendPresetAsync("FREE_TEXT_NOT_ALLOWED");
                Assert.False(bad.Ok);
                Assert.Equal("VALIDATION_FAILED", bad.ErrorCode);

                Assert.True((await ra.HeartbeatAsync()).Ok);

                await rb.CloseAsync();
                await Until(() => cityA.Get(b.UserId) == null, ra, rb);
            }
            finally
            {
                await ra.CloseAsync();
                await rb.CloseAsync();
            }
        }

        [Fact]
        public async Task DifferentAgeBandsDoNotSeeEachOther()
        {
            if (Url == null) return;
            var kid = await NewGuest(AgeBand.Under13);
            var adult = await NewGuest(AgeBand.Adult);
            var rk = new RealtimeClient(kid.ServerRoot, kid.AccessToken);
            var ra = new RealtimeClient(adult.ServerRoot, adult.AccessToken);
            var cityKid = new CityState { MyUserId = kid.UserId };
            cityKid.Bind(rk);
            bool rdyK = false, rdyA = false;
            rk.Ready += _ => rdyK = true;
            ra.Ready += _ => rdyA = true;
            try
            {
                await rk.ConnectAsync();
                await ra.ConnectAsync();
                await Until(() => rdyK && rdyA, rk, ra);
                Assert.True((await rk.JoinZoneAsync("PLAZA")).Ok);
                Assert.True((await ra.JoinZoneAsync("PLAZA")).Ok);
                await Task.Delay(300);
                rk.Pump();
                Assert.Null(cityKid.Get(adult.UserId));
                var req = await Assert.ThrowsAsync<ApiException>(() => adult.SendFriendRequestAsync(kid.UserId));
                Assert.Equal("FRIEND_REQUEST_NOT_ALLOWED", req.Code);
            }
            finally
            {
                await rk.CloseAsync();
                await ra.CloseAsync();
            }
        }

        /// <summary>Phase 10: claim a home, decorate it, log out and back in, and let a friend walk in.</summary>
        [Fact]
        public async Task FirstHomeDecorateAndVisit()
        {
            if (Url == null) return;
            var owner = await NewGuest();
            var home = await owner.GetMyHomeAsync();
            Assert.False(home.claimed);
            var claim = await owner.ClaimHomeAsync();
            Assert.True(claim.home.claimed);
            var again = await Assert.ThrowsAsync<ApiException>(() => owner.ClaimHomeAsync());
            Assert.Equal("HOME_ALREADY_CLAIMED", again.Code);

            var editor = new HomeLayoutEditor(claim.home, await owner.GetHomeFurnitureAsync());
            Assert.True(editor.TakeFromBag("furn_single_bed"));
            editor.MoveHeld(0, 0);
            Assert.True(editor.Drop());
            Assert.True(editor.TakeFromBag("furn_wood_table"));
            editor.MoveHeld(4, 4);
            editor.RotateHeld();
            Assert.True(editor.Drop());
            var key = IdempotencyKeys.New();
            var saved = await owner.SaveHomeLayoutAsync(editor.BaseVersion, editor.ToRequest(), key);
            var retried = await owner.SaveHomeLayoutAsync(editor.BaseVersion, editor.ToRequest(), key);
            Assert.False(saved.replayed);
            Assert.True(retried.replayed);
            Assert.Equal(2, saved.home.furniture.Count);
            var stale = await Assert.ThrowsAsync<ApiException>(() => owner.SaveHomeLayoutAsync(0, editor.ToRequest(), IdempotencyKeys.New()));
            Assert.Equal("HOME_LAYOUT_CONFLICT", stale.Code);
            var bag = await owner.GetHomeFurnitureAsync();
            Assert.Equal(0, bag.Single(b => b.itemId == "furn_single_bed").inBag);
            Assert.Equal(1, bag.Single(b => b.itemId == "furn_single_bed").placed);

            // log out, log in on "another device" with e-mail: the same layout comes back
            var email = "home-" + Guid.NewGuid().ToString("N").Substring(0, 12) + "@example.com";
            await owner.LinkAccountAsync(email, "correct horse battery");
            await owner.LogoutAsync();
            var pc = new HappyWorldApi(Url, new DotNetHttpTransport(), new InMemoryTokenStore());
            await pc.LoginAsync(email, "correct horse battery");
            var back = await pc.GetMyHomeAsync();
            Assert.Equal(saved.home.layoutVersion, back.layoutVersion);
            Assert.Equal(saved.home.furniture.Select(f => f.id + f.x + f.y + f.rotation), back.furniture.Select(f => f.id + f.x + f.y + f.rotation));

            // a stranger is turned away; once friends, they may visit and walk in
            var friend = await NewGuest();
            var refused = await Assert.ThrowsAsync<ApiException>(() => friend.VisitHomeAsync(pc.UserId));
            Assert.Equal("HOME_VISIT_NOT_ALLOWED", refused.Code);
            var code = (await pc.GetProfileAsync()).friendCode;
            var req = await friend.SendFriendRequestByCodeAsync(code);
            await pc.AcceptFriendRequestAsync(req.requestId);
            var visit = await friend.VisitHomeAsync(pc.UserId);
            Assert.False(visit.isMine);
            Assert.Equal(2, visit.furniture.Count);
            Assert.Null(visit.visitPolicy);
            var district = await friend.GetHomeDistrictAsync(back.plot.street);
            Assert.Contains(district.plots, p => p.owner != null && p.owner.userId == pc.UserId && p.canVisit);

            var rf = new RealtimeClient(Url, friend.AccessToken);
            bool ready = false;
            HomeState state = null;
            string kicked = null;
            rf.Ready += _ => ready = true;
            rf.HomeState += s => state = s;
            rf.HomeKicked += id => kicked = id;
            try
            {
                await rf.ConnectAsync();
                await Until(() => ready, rf);
                var ack = await rf.EnterHomeAsync(pc.UserId);
                Assert.True(ack.Ok, ack.ErrorCode);
                await Until(() => state != null, rf);
                Assert.Equal(pc.UserId, state.ownerId);
                Assert.False(state.isMine);
                await pc.SetHomeVisitPolicyAsync("PRIVATE");
                await Until(() => kicked != null, rf);
                Assert.Equal(pc.UserId, kicked);
            }
            finally
            {
                await rf.CloseAsync();
            }
        }

        [Fact]
        public async Task DailyCheckinOnceAndMissionsFromServerTime()
        {
            if (Url == null) return;
            var api = await NewGuest();
            var st = await api.GetDailyAsync();
            Assert.False(st.checkin.claimedToday);
            Assert.Equal(7, st.checkin.track.Count);
            Assert.Equal(3, st.daily.missions.Count(m => !m.fromPreviousPeriod));
            Assert.Equal(4, st.weekly.missions.Count);
            Assert.True(st.checkin.nextResetAt > st.now);
            Assert.StartsWith(DailyPresenter.LoginNudge(st).Substring(0, 6), "เช็กอินวันนี้รับ");

            var wallet = await api.GetWalletAsync();
            var r = await api.CheckinAsync();
            Assert.Equal(st.checkin.todayStep, r.step);
            Assert.Equal(wallet.Gold + r.rewards.gold, r.wallet["GOLD"]);
            var again = await Assert.ThrowsAsync<ApiException>(() => api.CheckinAsync());
            Assert.Equal("CHECKIN_ALREADY_CLAIMED", again.Code);

            var after = await api.GetDailyAsync();
            Assert.True(after.checkin.claimedToday);
            Assert.Equal(1, after.weekly.missions.First(m => m.id == "w_visit_4").progress);
            var notDone = after.daily.missions.First();
            var e = await Assert.ThrowsAsync<ApiException>(() => api.ClaimMissionAsync(notDone.periodKey, notDone.id));
            Assert.Equal("MISSION_NOT_COMPLETED", e.Code);
        }

        [Fact]
        public async Task BadTokenIsRefused()
        {
            if (Url == null) return;
            var r = new RealtimeClient(Url, "not-a-token");
            string err = null, disc = null;
            r.ConnectError += m => err = m;
            r.Disconnected += d => disc = d;
            await r.ConnectAsync();
            await Until(() => err != null || disc != null, r);
            Assert.False(r.Connected);
            await r.CloseAsync();
        }
    }
}
