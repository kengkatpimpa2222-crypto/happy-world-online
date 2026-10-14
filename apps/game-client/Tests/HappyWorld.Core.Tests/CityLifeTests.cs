using System;
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
    /// <summary>Phase 7 living city: time of day, weather and city projects as the client shows them.</summary>
    public class CityLifeUnitTests
    {
        private const string NowJson = @"{""now"":""2026-10-14T07:05:00.000Z"",""hour"":14.083,""clock"":""14:05"",""phase"":""AFTERNOON"",""phaseTh"":""ช่วงบ่าย"",
          ""season"":""RAINY"",""seasonTh"":""หน้าฝน"",""weather"":""RAIN"",""weatherTh"":""ฝนตก"",""tipTh"":""ฝนตก คนน้อยลง"",""crowd"":0.7,""busyTypes"":[],
          ""demand"":[{""productCode"":""MEATBALL"",""demand"":1.5},{""productCode"":""LEMONADE"",""demand"":0.6}],
          ""nextChangeAt"":""2026-10-14T08:00:00.000Z"",
          ""forecast"":[{""at"":""2026-10-14T08:00:00.000Z"",""weather"":""CLOUDY"",""weatherTh"":""ครึ้มฟ้า""},{""at"":""2026-10-14T11:00:00.000Z"",""weather"":""SUNNY"",""weatherTh"":""แดดดี""}]}";

        private const string GrowthJson = @"{""points"":1620,""builtCount"":1,""current"":""BUS_STOP"",
          ""projects"":[
            {""code"":""PARK"",""nameKey"":""project.park"",""nameTh"":""สวนสาธารณะ"",""effectTh"":""คนมาเดินเล่น"",""cost"":1500,""zone"":""PLAZA"",""x"":15,""y"":16,""status"":""DONE"",""progress"":1500,""completedAt"":""2026-10-14T06:00:00.000Z""},
            {""code"":""BUS_STOP"",""nameKey"":""project.bus_stop"",""nameTh"":""ป้ายรถเมล์หน้าตลาด"",""effectTh"":""นักท่องเที่ยวมากขึ้น"",""cost"":3500,""zone"":""MARKET"",""x"":6,""y"":26,""status"":""BUILDING"",""progress"":120,""completedAt"":null},
            {""code"":""SCHOOL"",""nameKey"":""project.school"",""nameTh"":""โรงเรียน"",""effectTh"":""นักเรียน"",""cost"":7000,""zone"":""ACTIVITY"",""x"":25,""y"":28,""status"":""LOCKED"",""progress"":0,""completedAt"":null}],
          ""me"":{""points"":12,""total"":40},
          ""top"":[{""rank"":1,""displayName"":null,""points"":60,""isMe"":false}]}";

        private static CityNow Now() => HappyWorldApi.Parse<CityNow>(new HttpResult { Status = 200, Body = NowJson });
        private static CityGrowth Growth() => HappyWorldApi.Parse<CityGrowth>(new HttpResult { Status = 200, Body = GrowthJson });

        [Fact]
        public void ParsesCityNowAndShowsTheHudLine()
        {
            var n = Now();
            Assert.Equal("RAIN", n.weather);
            Assert.Equal(1.5f, n.DemandOf("MEATBALL"), 3);
            Assert.Equal(1f, n.DemandOf("BREAD"), 3);
            Assert.Equal("14:04 ช่วงบ่าย · ฝนตก · หน้าฝน", CityLifePresenter.HudLine(n, n.hour));
            Assert.Equal("ต่อไป 15:00 ครึ้มฟ้า, 18:00 แดดดี", CityLifePresenter.ForecastLine(n));
            Assert.True(CityLifePresenter.Raining(n));
        }

        [Fact]
        public void TheClockKeepsTickingBetweenReads()
        {
            var n = Now();
            Assert.Equal(15.083f, CityLifePresenter.LocalHour(n, 3600), 3);
            n.hour = 23.5f;
            Assert.Equal(0.5f, CityLifePresenter.LocalHour(n, 3600), 3);
            Assert.Equal("EVENING", CityLifePresenter.Phase(19.9f));
            Assert.Equal("NIGHT", CityLifePresenter.Phase(20f));
            Assert.Equal("00:30", CityLifePresenter.Clock(0.5f));
        }

        [Fact]
        public void DayAndNightLight()
        {
            Assert.Equal(0f, CityLifePresenter.Daylight(2f));
            Assert.Equal(1f, CityLifePresenter.Daylight(12f));
            Assert.InRange(CityLifePresenter.Daylight(6f), 0.3f, 0.7f);
            Assert.InRange(CityLifePresenter.Daylight(18.5f), 0.3f, 0.7f);
            Assert.True(CityLifePresenter.LampsOn(21f));
            Assert.False(CityLifePresenter.LampsOn(12f));
            Assert.True(CityLifePresenter.SunPitch(12f) > CityLifePresenter.SunPitch(7f));
            Assert.True(CityLifePresenter.SunIntensity(12f, "SUNNY") > CityLifePresenter.SunIntensity(12f, "RAIN"));
            Assert.True(CityLifePresenter.SunIntensity(12f, "SUNNY") > CityLifePresenter.SunIntensity(23f, "SUNNY"));

            float r, g, b, nr, ng, nb;
            CityLifePresenter.Sky(12f, "SUNNY", out r, out g, out b);
            CityLifePresenter.Sky(23f, "SUNNY", out nr, out ng, out nb);
            Assert.True(b > nb && g > ng);
            float rr, rg, rb;
            CityLifePresenter.Sky(12f, "RAIN", out rr, out rg, out rb);
            Assert.True(Math.Abs(rr - rb) < Math.Abs(r - b)); // greyer sky in the rain
        }

        [Fact]
        public void ShopHintsFollowTheWeather()
        {
            var n = Now();
            Assert.Equal("ฝนตก ขายดี x1.5", CityLifePresenter.ProductHint(n, "MEATBALL"));
            Assert.Equal("ฝนตก ขายยาก", CityLifePresenter.ProductHint(n, "LEMONADE"));
            Assert.Equal("", CityLifePresenter.ProductHint(n, "BREAD"));
            Assert.Equal("", CityLifePresenter.ProductHint(null, "BREAD"));
        }

        [Fact]
        public void CityProjectsByZoneWithAScaffoldThatRises()
        {
            var g = Growth();
            Assert.Equal("BUS_STOP", g.Current().code);
            Assert.NotNull(g.projects[0].completedAt);
            Assert.Null(g.projects[1].completedAt);
            Assert.Equal("กำลังสร้าง ป้ายรถเมล์หน้าตลาด 120/3500", CityLifePresenter.Banner(g));
            Assert.Equal(new[] { "PARK" }, CityLifePresenter.InZone(g, "PLAZA").Select(p => p.code));
            Assert.Equal(new[] { "BUS_STOP" }, CityLifePresenter.InZone(g, "MARKET").Select(p => p.code));
            Assert.Empty(CityLifePresenter.InZone(g, "ACTIVITY")); // locked projects are not shown
            Assert.Equal(1, CityLifePresenter.ScaffoldStage(g.projects[1]));
            g.projects[1].progress = 3000;
            Assert.Equal(4, CityLifePresenter.ScaffoldStage(g.projects[1]));
            Assert.Equal(0, CityLifePresenter.ScaffoldStage(g.projects[0]));
            Assert.Equal(1f, CityLifePresenter.Progress(g.projects[0]));
            Assert.Equal("ผู้ช่วยอันดับ 1", FestivalPresenter.HelperName(g.top[0]));
            foreach (var p in g.projects) p.status = "DONE";
            Assert.Equal("เมืองสร้างครบทุกอย่างแล้ว!", CityLifePresenter.Banner(g));
        }

        [Fact]
        public void ParsesCityGrowthEventAndShopUpdateCityPoints()
        {
            var e = JObject.Parse(@"{""projectCode"":""PARK"",""nameTh"":""สวนสาธารณะ"",""effectTh"":""คนมาเดินเล่นในเมืองมากขึ้น 5%"",""index"":0,""points"":1502,""next"":""BUS_STOP""}").ToObject<CityGrowthEvent>();
            Assert.Equal("ทั้งเมืองช่วยกันสร้าง สวนสาธารณะ เสร็จแล้ว! คนมาเดินเล่นในเมืองมากขึ้น 5%", CityLifePresenter.BuiltText(e));
            Assert.Equal("BUS_STOP", e.next);
            var u = JObject.Parse(@"{""businessId"":""b"",""cashBox"":30,""unitsSold"":3,""cityPoints"":3,""stall"":[]}").ToObject<ShopUpdated>();
            Assert.Equal(3, u.cityPoints);
        }
    }

    /// <summary>Against a real backend (HWO_API_URL).</summary>
    public class CityLifeIntegrationTests
    {
        private static readonly string Url = string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HWO_API_URL")) ? null : Environment.GetEnvironmentVariable("HWO_API_URL");

        [Fact]
        public async Task ReadsTheCityClockWeatherAndProjects()
        {
            if (Url == null) return;
            var api = new HappyWorldApi(Url, new DotNetHttpTransport(), new InMemoryTokenStore());
            await api.SignInAsync("unity-test-" + Guid.NewGuid().ToString("N"), AgeBand.Teen);
            var n = await api.GetCityNowAsync();
            Assert.InRange(n.hour, 0f, 24f);
            Assert.Contains(n.weather, new[] { "SUNNY", "HOT", "CLOUDY", "RAIN" });
            Assert.Equal(4, n.forecast.Count);
            Assert.True(n.nextChangeAt > n.now);

            var g = await api.GetCityGrowthAsync();
            Assert.Equal(5, g.projects.Count);
            Assert.Equal(0, g.me.total);
            Assert.True(g.projects.Count(p => p.Building) <= 1);
            Assert.Equal("PARK", g.projects[0].code);
        }
    }
}
