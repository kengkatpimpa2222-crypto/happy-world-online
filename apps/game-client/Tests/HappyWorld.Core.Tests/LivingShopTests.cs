using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HappyWorld.Core.Api;
using HappyWorld.Core.Net;
using HappyWorld.Core.Realtime;
using HappyWorld.Core.World;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace HappyWorld.Core.Tests
{
    /// <summary>Phase 5 living shop: client-side parsing and the NPC walk planner.</summary>
    public class LivingShopUnitTests
    {
        private static MarketStall Stall(int face = 1) => new MarketStall
        {
            slot = 3,
            x = 25,
            y = 7,
            face = face,
            businessId = "b1",
            name = "ร้านป้าแดง",
            products = new List<MarketStallProduct> { new MarketStallProduct { productCode = "LEMONADE", price = 12, shelfQuantity = 10 } },
        };

        private static MarketActivity Activity(params MarketVisit[] visits) =>
            new MarketActivity { slot = 3, businessId = "b1", tick = 1000, tickSeconds = 60, visits = visits.ToList() };

        [Fact]
        public void PlannerWalksInWaitsAtTheStallAndLeaves()
        {
            var visit = new MarketVisit { index = 0, customerType = "STUDENT", productCode = "LEMONADE", outcome = "BOUGHT", quantity = 2 };
            var plan = MarketNpcPlanner.Plan(Activity(visit), Stall(), 100, new Random(1)).Single();
            Assert.InRange(plan.StartAt, 100, 100 + 42);
            float x, y;
            Assert.Equal(NpcPhase.Waiting, plan.PositionAt(plan.StartAt - 1, out x, out y));
            Assert.Equal(NpcPhase.WalkingIn, plan.PositionAt(plan.StartAt + 0.5, out x, out y));
            var atStall = plan.StartAt + plan.WalkInSeconds + 0.1;
            Assert.Equal(NpcPhase.AtStall, plan.PositionAt(atStall, out x, out y));
            Assert.InRange(y, 7 + 1.8f, 7 + 2.0f); // stands on the customers' side of the counter
            Assert.InRange(x, 25 - 1.3f, 25 + 1.3f);
            Assert.Equal(NpcPhase.WalkingOut, plan.PositionAt(atStall + plan.WaitSeconds, out x, out y));
            Assert.Equal(NpcPhase.Done, plan.PositionAt(plan.EndsAt + 0.01, out x, out y));
            Assert.Equal(MarketNpcPlanner.EntranceX, x);
            Assert.Equal(MarketNpcPlanner.EntranceY, y);
            Assert.Equal(24, plan.CoinPopup); // 2 x 12 Gold, shown only
            Assert.Equal("ขอน้ำมะนาว 2 แก้ว!", plan.Bubble);
        }

        [Fact]
        public void CustomersOnTheOtherSideStandBelowTheStall()
        {
            var v = new MarketVisit { customerType = "WORKER", productCode = "LEMONADE", outcome = "TOO_EXPENSIVE" };
            var plan = MarketNpcPlanner.Plan(Activity(v), Stall(-1), 0, new Random(2)).Single();
            float x, y;
            plan.PositionAt(plan.StartAt + plan.WalkInSeconds + 0.1, out x, out y);
            Assert.True(y < 7);
            Assert.Equal(0, plan.CoinPopup);
            Assert.Equal("น้ำมะนาว 12 ทอง แพงไปหน่อย...", plan.Bubble);
        }

        [Fact]
        public void WalkIsContinuousNoTeleports()
        {
            var v = new MarketVisit { customerType = "GRANDMA", productCode = "LEMONADE", outcome = "SOLD_OUT" };
            var plan = MarketNpcPlanner.Plan(Activity(v), Stall(), 0, new Random(3)).Single();
            float px, py;
            plan.PositionAt(plan.StartAt, out px, out py);
            for (var t = plan.StartAt; t < plan.EndsAt; t += 0.05)
            {
                float x, y;
                plan.PositionAt(t, out x, out y);
                Assert.True(Math.Sqrt((x - px) * (x - px) + (y - py) * (y - py)) <= plan.Speed * 0.05 + 0.01);
                px = x;
                py = y;
            }
        }

        [Fact]
        public void BubblesExplainEveryOutcomeInThai()
        {
            Assert.Equal("อ้าว ขนมปังหมดแล้วเหรอ", MarketNpcPlanner.BubbleText(new MarketVisit { productCode = "BREAD", outcome = "SOLD_OUT" }, 15));
            Assert.Equal("ไม่มีดอกไม้ขายเหรอ", MarketNpcPlanner.BubbleText(new MarketVisit { productCode = "FLOWER", outcome = "NOT_SELLING" }, 0));
            Assert.Equal("ขอลูกชิ้นปิ้ง 3 ไม้!", MarketNpcPlanner.BubbleText(new MarketVisit { productCode = "MEATBALL", outcome = "BOUGHT", quantity = 3 }, 20));
            Assert.Equal("คุณยาย", MarketNpcPlanner.CustomerName("GRANDMA"));
        }

        [Fact]
        public void EmptyOrUnknownInputPlansNothing()
        {
            Assert.Empty(MarketNpcPlanner.Plan(null, Stall(), 0, new Random()));
            Assert.Empty(MarketNpcPlanner.Plan(Activity(), null, 0, new Random()));
        }

        [Fact]
        public void ParsesBusinessWithStallAndCatalog()
        {
            const string json = @"{""id"":""b1"",""name"":""ร้าน"",""level"":1,""maxLevel"":5,""upgradeCost"":300,""stockCapacity"":50,
              ""finance"":{""totalRevenue"":10,""totalExpense"":5,""profit"":5},""totalUnitsSold"":1,
              ""products"":[{""id"":""x"",""businessId"":""b1"",""productCode"":""LEMONADE"",""quantity"":3,""avgUnitCost"":5,""sellPrice"":10,""shelfQuantity"":7,""onStall"":true,""unitCost"":5,""minPrice"":6,""maxPrice"":20,""basePrice"":10,""unlockLevel"":1}],
              ""stall"":{""status"":""OPEN"",""slot"":4,""cashBox"":120,""totalCustomers"":9,""shelfSlots"":2,""shelfCapacity"":20,""customersPerMinute"":1,""slotsUsed"":1},
              ""catalog"":[{""productCode"":""THAI_TEA"",""unitCost"":7,""minPrice"":9,""maxPrice"":30,""basePrice"":15,""unlockLevel"":2,""unlocked"":false}]}";
            var b = JsonConvert.DeserializeObject<Business>(json);
            Assert.True(b.stall.IsOpen);
            Assert.Equal(4, b.stall.slot);
            Assert.Equal(120, b.stall.cashBox);
            Assert.Equal(7, b.products[0].shelfQuantity);
            Assert.True(b.products[0].onStall);
            Assert.False(b.catalog[0].unlocked);
        }

        [Fact]
        public void ReportKeepsTheServersUntilStringForTheAck()
        {
            const string json = @"{""businessId"":""b1"",""since"":""2026-10-14T05:00:00.000Z"",""until"":""2026-10-14T06:00:00.123Z"",""hasNews"":true,""customers"":5,
              ""tips"":[{""code"":""GREAT_DAY"",""textTh"":""ดีมาก""}],""products"":[{""productCode"":""LEMONADE"",""unitsSold"":4,""price"":null}]}";
            var r = HappyWorldApi.Parse<ShopReport>(new HttpResult { Status = 200, Body = json });
            Assert.Equal("2026-10-14T06:00:00.123Z", r.untilIso);
            Assert.Equal(new DateTime(2026, 10, 14, 5, 0, 0, DateTimeKind.Utc), r.since.ToUniversalTime());
            Assert.Equal("GREAT_DAY", r.tips[0].code);
            Assert.Null(r.products[0].price);
        }

        [Fact]
        public void ParsesMarketActivityEvent()
        {
            var a = JObject.Parse(@"{""slot"":2,""businessId"":""b"",""tick"":29300000,""tickSeconds"":60,
              ""visits"":[{""index"":0,""customerType"":""TOURIST"",""productCode"":""FLOWER"",""outcome"":""BOUGHT"",""quantity"":2}]}").ToObject<MarketActivity>();
            Assert.Equal(2, a.slot);
            Assert.Equal("TOURIST", a.visits[0].customerType);
            Assert.Equal(29300000, a.tick);
        }
    }

    /// <summary>Against a real backend (HWO_API_URL). Customers need real minutes to arrive, so these check the flow, not sales.</summary>
    public class LivingShopIntegrationTests
    {
        private static readonly string Url = string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HWO_API_URL")) ? null : Environment.GetEnvironmentVariable("HWO_API_URL");

        private static async Task<HappyWorldApi> NewGuest(string ageBand)
        {
            var api = new HappyWorldApi(Url, new DotNetHttpTransport(), new InMemoryTokenStore());
            await api.SignInAsync("unity-test-" + Guid.NewGuid().ToString("N"), ageBand);
            return api;
        }

        [Fact]
        public async Task StockTheStallOpenItAndSeeItInTheMarket()
        {
            if (Url == null) return;
            var owner = await NewGuest(AgeBand.Teen);
            var shop = (await owner.GetBusinessesAsync()).Single();
            Assert.Equal("CLOSED", shop.stall.status);

            var renamed = await owner.RenameShopAsync(shop.id, "ร้านทดสอบ" + new Random().Next(100, 999));
            Assert.StartsWith("ร้านทดสอบ", renamed.name);

            var empty = await Assert.ThrowsAsync<ApiException>(() => owner.SetShopStatusAsync(shop.id, "OPEN"));
            Assert.Equal("SHOP_SHELF_EMPTY", empty.Code);

            await owner.RestockAsync(shop.id, "LEMONADE", 10, IdempotencyKeys.New());
            var stocked = await owner.SetShelfAsync(shop.id, "LEMONADE", 8);
            var lemon = stocked.products.Single(p => p.productCode == "LEMONADE");
            Assert.Equal(2, lemon.quantity);
            Assert.Equal(8, lemon.shelfQuantity);

            var opened = await owner.SetShopStatusAsync(shop.id, "OPEN");
            Assert.True(opened.stall.IsOpen);
            Assert.True(opened.stall.slot.HasValue);

            var viewer = await NewGuest(AgeBand.Teen);
            var market = await viewer.GetMarketAsync();
            var stall = market.stalls.Single(s => s.businessId == shop.id);
            Assert.False(stall.isMine);
            Assert.Equal(8, stall.products.Single().shelfQuantity);

            var other = await NewGuest(AgeBand.Adult);
            Assert.DoesNotContain((await other.GetMarketAsync()).stalls, s => s.businessId == shop.id);

            var report = await owner.GetShopReportAsync(shop.id);
            Assert.False(string.IsNullOrEmpty(report.untilIso));
            await owner.AckShopReportAsync(shop.id, report.untilIso);

            var collect = await Assert.ThrowsAsync<ApiException>(() => owner.CollectShopAsync(shop.id, IdempotencyKeys.New()));
            Assert.Equal("SHOP_NOTHING_TO_COLLECT", collect.Code);

            var closed = await owner.SetShopStatusAsync(shop.id, "CLOSED");
            Assert.Equal("CLOSED", closed.stall.status);
            Assert.Null(closed.stall.slot);
        }

        [Fact]
        public async Task PlayersInTheMarketHearWhenAStallChanges()
        {
            if (Url == null) return;
            var owner = await NewGuest(AgeBand.Under13);
            var shop = (await owner.GetBusinessesAsync()).Single();
            await owner.RestockAsync(shop.id, "BREAD", 10, IdempotencyKeys.New());
            await owner.SetShelfAsync(shop.id, "BREAD", 10);
            var opened = await owner.SetShopStatusAsync(shop.id, "OPEN");

            var viewer = await NewGuest(AgeBand.Under13);
            var rt = new RealtimeClient(viewer.ServerRoot, viewer.AccessToken);
            var changed = new List<int>();
            var ready = false;
            rt.Ready += _ => ready = true;
            rt.MarketStallChanged += slot => changed.Add(slot);
            try
            {
                await rt.ConnectAsync();
                await Until(() => ready, rt);
                Assert.True((await rt.JoinZoneAsync("MARKET")).Ok);
                await owner.SetShelfAsync(shop.id, "BREAD", 6);
                await Until(() => changed.Contains(opened.stall.slot.Value), rt);
            }
            finally
            {
                await owner.SetShopStatusAsync(shop.id, "CLOSED");
                await rt.CloseAsync();
                rt.Dispose();
            }
        }

        private static async Task Until(Func<bool> cond, RealtimeClient rt)
        {
            var deadline = DateTime.UtcNow.AddSeconds(8);
            while (!cond())
            {
                if (DateTime.UtcNow > deadline) throw new TimeoutException("condition not met");
                rt.Pump();
                await Task.Delay(20);
            }
        }
    }
}
