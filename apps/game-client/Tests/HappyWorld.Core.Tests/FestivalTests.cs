using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HappyWorld.Core.Api;
using HappyWorld.Core.Net;
using HappyWorld.Core.Realtime;
using HappyWorld.Core.World;
using Newtonsoft.Json.Linq;
using Xunit;

namespace HappyWorld.Core.Tests
{
    /// <summary>Phase 6 city festival: parsing and what the HUD / decorations show.</summary>
    public class FestivalUnitTests
    {
        private const string StateJson = @"{""now"":""2026-10-17T05:00:00.000Z"",
          ""current"":{""key"":""BAKERY-40"",""code"":""BAKERY"",""nameKey"":""festival.bakery"",""nameTh"":""งานขนมอบหอมกรุ่น"",""color"":""#FFB74D"",
            ""status"":""ACTIVE"",""startsAt"":""2026-10-15T17:00:00.000Z"",""endsAt"":""2026-10-18T17:00:00.000Z"",
            ""featured"":[{""productCode"":""BREAD"",""demand"":3},{""productCode"":""THAI_TEA"",""demand"":2}],""rewardItemId"":""furn_fest_bread_oven"",
            ""city"":{""points"":320,""contributors"":7,""tiersReached"":1},
            ""tiers"":[{""tier"":1,""target"":150,""reached"":true,""rewardGold"":50,""rewardXp"":20,""rewardItemId"":null,""claimed"":false,""claimable"":true},
                       {""tier"":2,""target"":500,""reached"":false,""rewardGold"":100,""rewardXp"":40,""rewardItemId"":null,""claimed"":false,""claimable"":false},
                       {""tier"":3,""target"":1200,""reached"":false,""rewardGold"":150,""rewardXp"":60,""rewardItemId"":""furn_fest_bread_oven"",""claimed"":false,""claimable"":false}],
            ""me"":{""points"":12,""minToClaim"":10},
            ""top"":[{""rank"":1,""displayName"":null,""points"":80,""isMe"":false},{""rank"":2,""displayName"":""ต้น"",""points"":12,""isMe"":true}]},
          ""previous"":null}";

        private static FestivalState State() => HappyWorldApi.Parse<FestivalState>(new HttpResult { Status = 200, Body = StateJson });

        [Fact]
        public void ParsesTheFestivalState()
        {
            var s = State();
            var f = s.current;
            Assert.True(f.Active);
            Assert.Equal(new DateTime(2026, 10, 18, 17, 0, 0, DateTimeKind.Utc), f.endsAt.ToUniversalTime());
            Assert.Equal(3, f.DemandOf("BREAD"));
            Assert.Equal(1, f.DemandOf("FLOWER"));
            Assert.Equal(1, f.ClaimableCount());
            Assert.Equal(500, f.NextTier().target);
            Assert.Null(f.top[0].displayName);
            Assert.Null(s.previous);
        }

        [Fact]
        public void BannerShowsProgressTowardTheNextTier()
        {
            var s = State();
            Assert.Equal("งานขนมอบหอมกรุ่น · เมือง 320/500", FestivalPresenter.Banner(s.current, s.now));
            Assert.Equal((320f - 150) / (500 - 150), FestivalPresenter.Progress(s.current), 3);

            s.current.status = "UPCOMING";
            var now = new DateTime(2026, 10, 13, 15, 0, 0, DateTimeKind.Utc); // 2 days 2 h before
            Assert.Equal("งานขนมอบหอมกรุ่น เริ่มอีก 2 วัน 2 ชม.", FestivalPresenter.Banner(s.current, now));

            s.current.status = "ACTIVE";
            s.current.city.points = 1500;
            foreach (var t in s.current.tiers) t.reached = true;
            Assert.EndsWith("ครบทุกขั้น!", FestivalPresenter.Banner(s.current, now));
            Assert.Equal(1f, FestivalPresenter.Progress(s.current));
        }

        [Fact]
        public void TheCityDressesUpTierByTier()
        {
            var f = State().current;
            Assert.Equal(0, FestivalPresenter.DecorStage(null));
            f.city.tiersReached = 0;
            Assert.Equal(1, FestivalPresenter.DecorStage(f));
            f.city.tiersReached = 3;
            Assert.Equal(4, FestivalPresenter.DecorStage(f));
            f.status = "UPCOMING";
            Assert.Equal(0, FestivalPresenter.DecorStage(f));
        }

        [Fact]
        public void TimeLeftAndHints()
        {
            Assert.Equal("อีก 1 วัน 3 ชม.", FestivalPresenter.TimeLeft(TimeSpan.FromHours(27)));
            Assert.Equal("อีก 3 ชม. 20 นาที", FestivalPresenter.TimeLeft(TimeSpan.FromMinutes(200)));
            Assert.Equal("อีก 1 นาที", FestivalPresenter.TimeLeft(TimeSpan.FromSeconds(10)));
            Assert.Equal("หมดเวลาแล้ว", FestivalPresenter.TimeLeft(TimeSpan.Zero));

            var f = State().current;
            Assert.Equal("ขายดีช่วงเทศกาล x3", FestivalPresenter.ProductHint(f, "BREAD"));
            Assert.Equal("", FestivalPresenter.ProductHint(f, "LEMONADE"));
            f.status = "UPCOMING";
            Assert.Equal("เทศกาลนี้จะขายดี x2", FestivalPresenter.ProductHint(f, "THAI_TEA"));
        }

        [Fact]
        public void ColoursAndNames()
        {
            float r, g, b;
            FestivalPresenter.ParseColor("#FFB74D", out r, out g, out b);
            Assert.Equal(1f, r, 3);
            Assert.Equal(0xB7 / 255f, g, 3);
            FestivalPresenter.ParseColor("nope", out r, out g, out b);
            Assert.Equal(0.84f, g, 3);
            var top = State().current.top;
            Assert.Equal("ผู้ช่วยอันดับ 1", FestivalPresenter.HelperName(top[0]));
            Assert.Equal("ต้น (คุณ)", FestivalPresenter.HelperName(top[1]));
            Assert.Equal("งานขนมอบหอมกรุ่น", FestivalPresenter.Name(State().current));
        }

        [Fact]
        public void FestivalGoersCraveTheFestivalGoods()
        {
            var v = new MarketVisit { customerType = "FESTIVAL_GOER", productCode = "BREAD", outcome = "BOUGHT", quantity = 2 };
            Assert.Equal("งานเทศกาลต้องมีขนมปัง! 2 ชิ้น", MarketNpcPlanner.BubbleText(v, 12));
            Assert.Equal("นักเที่ยวงาน", MarketNpcPlanner.CustomerName("FESTIVAL_GOER"));
        }

        [Fact]
        public void ParsesMilestoneEventAndShopUpdateFestivalPoints()
        {
            var m = JObject.Parse(@"{""festivalKey"":""BAKERY-40"",""code"":""BAKERY"",""tier"":2,""points"":503}").ToObject<FestivalMilestone>();
            Assert.Equal(2, m.tier);
            var u = JObject.Parse(@"{""businessId"":""b"",""cashBox"":30,""festivalPoints"":4,""stall"":[]}").ToObject<ShopUpdated>();
            Assert.Equal(4, u.festivalPoints);
        }
    }

    /// <summary>Against a real backend (HWO_API_URL).</summary>
    public class FestivalIntegrationTests
    {
        private static readonly string Url = string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HWO_API_URL")) ? null : Environment.GetEnvironmentVariable("HWO_API_URL");

        [Fact]
        public async Task ReadsThisWeeksFestivalAndRefusesRewardsNobodyEarned()
        {
            if (Url == null) return;
            var api = new HappyWorldApi(Url, new DotNetHttpTransport(), new InMemoryTokenStore());
            await api.SignInAsync("unity-test-" + Guid.NewGuid().ToString("N"), AgeBand.Teen);
            var s = await api.GetFestivalAsync();
            Assert.NotNull(s.current);
            Assert.Contains(s.current.status, new[] { "UPCOMING", "ACTIVE" });
            Assert.Equal(3, s.current.tiers.Count);
            Assert.Equal(2, s.current.featured.Count);
            Assert.Equal(0, s.current.me.points);
            Assert.True(s.current.endsAt > s.current.startsAt);

            var e = await Assert.ThrowsAsync<ApiException>(() => api.ClaimFestivalAsync(s.current.key, 1));
            Assert.Equal("FESTIVAL_NOT_ENOUGH_CONTRIBUTION", e.Code);
        }
    }
}
