using System.Collections.Generic;
using HappyWorld.Core.Api;
using HappyWorld.Core.World;
using Newtonsoft.Json;
using Xunit;

namespace HappyWorld.Core.Tests
{
    /// <summary>Phase 9.1: the live trip countdown. The server decides; the tracker only counts down and asks rarely.</summary>
    public class ExpeditionTrackerTests
    {
        private static ExpeditionTrip Trip(int secondsLeft, bool ready = false, string id = "t1") =>
            new ExpeditionTrip { id = id, site = "ORCHARD", plan = "STEADY", secondsLeft = secondsLeft, ready = ready };

        [Fact]
        public void Loads_once_and_never_polls_while_there_is_no_trip()
        {
            var t = new ExpeditionTracker();
            Assert.True(t.CheckDue(0)); // first load after entering the city
            t.BeginCheck(0);
            Assert.False(t.CheckDue(0)); // one request at a time
            t.Apply(null, 0.2);
            Assert.Equal(TripPhase.None, t.Phase);
            for (double now = 1; now < 3600; now += 1) Assert.False(t.CheckDue(now));
            t.Resync(100); // back from the background with no trip: still nothing to ask
            Assert.False(t.CheckDue(200));
        }

        [Fact]
        public void Counts_down_the_servers_seconds_on_the_app_clock_and_asks_once_at_zero()
        {
            var t = new ExpeditionTracker();
            t.Apply(Trip(180), 1000);
            Assert.Equal(TripPhase.Travelling, t.Phase);
            Assert.Equal(180, t.SecondsLeft(1000));
            Assert.Equal(125, t.SecondsLeft(1055.4));
            int checks = 0;
            for (double now = 1000; now < 1180; now += 0.25) if (t.CheckDue(now)) checks++;
            Assert.Equal(0, checks); // no requests while travelling
            Assert.Equal(0, t.SecondsLeft(1180));
            Assert.True(t.CheckDue(1180));
            t.BeginCheck(1180);
            Assert.Equal(TripPhase.Checking, t.Phase);
            ExpeditionTrip arrived = null;
            t.Arrived += x => arrived = x;
            t.Apply(Trip(0, ready: true), 1180.3);
            Assert.Equal(TripPhase.Ready, t.Phase);
            Assert.Same(t.Trip, arrived);
            Assert.False(t.CheckDue(5000)); // nothing more to ask once the server says it is back
        }

        [Fact]
        public void A_server_that_says_not_yet_is_believed_without_a_request_loop()
        {
            var t = new ExpeditionTracker();
            t.Apply(Trip(2), 0);
            Assert.True(t.CheckDue(2));
            t.BeginCheck(2);
            t.Apply(Trip(1), 2.1); // the server's clock is a second behind ours
            Assert.Equal(TripPhase.Travelling, t.Phase);
            Assert.False(t.CheckDue(3.2)); // its 1 s is up, but the minimum gap holds
            Assert.True(t.CheckDue(2 + ExpeditionTracker.MinCheckGapSeconds));
            t.BeginCheck(5);
            t.Apply(Trip(0), 5.1); // still not ready at 0 s left: keep checking, at most every 3 s
            Assert.Equal(TripPhase.Checking, t.Phase);
            Assert.False(t.CheckDue(7));
            Assert.True(t.CheckDue(8));
        }

        [Fact]
        public void Failures_back_off_from_5_seconds_to_a_minute_and_keep_the_countdown()
        {
            var t = new ExpeditionTracker();
            t.Apply(Trip(10), 0);
            double now = 10;
            var waits = new List<int>();
            for (int i = 0; i < 6; i++)
            {
                Assert.True(t.CheckDue(now));
                t.BeginCheck(now);
                t.CheckFailed(now, network: true);
                Assert.True(t.Offline);
                waits.Add(t.RetryIn(now));
                Assert.False(t.CheckDue(now + t.RetryIn(now) - 0.5));
                now += t.RetryIn(now);
            }
            Assert.Equal(new[] { 5, 10, 20, 40, 60, 60 }, waits);
            Assert.Equal("t1", t.Trip.id); // the trip is not forgotten while offline
            Assert.Contains("เชื่อมต่อเซิร์ฟเวอร์ไม่ได้", AdventurePresenter.TripStatus(t, now - 1));
            t.BeginCheck(now);
            t.Apply(Trip(0, ready: true), now);
            Assert.False(t.Offline);
            Assert.Equal(0, t.Failures);
        }

        [Fact]
        public void Coming_back_from_the_background_rereads_the_trip_soon_but_not_in_a_burst()
        {
            var t = new ExpeditionTracker();
            t.Apply(Trip(600), 0);
            Assert.False(t.CheckDue(30));
            t.Resync(30); // the app was paused; the app clock may not have run
            Assert.True(t.CheckDue(30));
            t.BeginCheck(30);
            t.Apply(Trip(120), 30.2); // much of the trip went by in the background
            Assert.Equal(120, t.SecondsLeft(30.2));
            t.Resync(31);
            t.Resync(31.5); // focus + pause + reconnect at once
            Assert.False(t.CheckDue(32));
            Assert.True(t.CheckDue(33));
        }

        [Fact]
        public void A_trip_that_came_back_while_the_app_was_closed_is_announced_once()
        {
            var t = new ExpeditionTracker();
            int arrivals = 0;
            t.Arrived += _ => arrivals++;
            t.BeginCheck(0);
            t.Apply(Trip(0, ready: true), 0.1);
            t.Apply(Trip(0, ready: true), 5); // the explore screen re-read it
            Assert.Equal(1, arrivals);
        }

        [Fact]
        public void Claiming_happens_once_however_often_the_button_is_pressed()
        {
            var t = new ExpeditionTracker();
            Assert.False(t.TryBeginClaim()); // nothing to claim
            t.Apply(Trip(30), 0);
            Assert.False(t.TryBeginClaim()); // not back yet: the client never decides it is
            t.Apply(Trip(0, ready: true), 30);
            Assert.True(t.TryBeginClaim());
            Assert.False(t.TryBeginClaim());
            Assert.False(t.CheckDue(100)); // no checks during a claim
            Assert.Equal("กำลังเปิดดูผล...", AdventurePresenter.TripStatus(t, 31));
            t.ClaimFailed("NETWORK_ERROR", 0, 32); // a timeout: still ready, pressing again is safe (the server replays)
            Assert.Equal(TripPhase.Ready, t.Phase);
            Assert.True(t.TryBeginClaim());
            t.ClaimSucceeded();
            Assert.Null(t.Trip);
            Assert.Equal(TripPhase.None, t.Phase);
            Assert.False(t.TryBeginClaim());
        }

        [Fact]
        public void A_claim_the_server_refuses_rereads_the_state()
        {
            var t = new ExpeditionTracker();
            t.Apply(Trip(0, ready: true), 0);
            Assert.True(t.TryBeginClaim());
            t.ClaimFailed("EXPEDITION_NOT_FINISHED", 409, 1);
            Assert.Equal(TripPhase.Checking, t.Phase);
            Assert.True(t.CheckDue(3.1));

            var gone = new ExpeditionTracker();
            gone.Apply(Trip(0, ready: true), 0);
            gone.TryBeginClaim();
            gone.ClaimFailed("NOT_FOUND", 404, 1);
            Assert.Null(gone.Trip);
            Assert.True(gone.CheckDue(1)); // reload everything
        }

        [Fact]
        public void Bad_or_missing_times_from_the_server_never_go_negative()
        {
            var t = new ExpeditionTracker();
            t.Apply(Trip(-30), 0);
            Assert.Equal(0, t.SecondsLeft(0));
            Assert.Equal(TripPhase.Checking, t.Phase);
            Assert.Equal(0, t.SecondsLeft(-5)); // a clock that went backwards
            t.Apply(new ExpeditionTrip { id = "x", claimed = true }, 1); // a claimed trip is no active trip
            Assert.Null(t.Trip);
        }

        [Fact]
        public void Status_lines_and_the_HUD_badge()
        {
            var t = new ExpeditionTracker();
            Assert.Equal("", AdventurePresenter.HudBadge(t, 0));
            t.Apply(Trip(125), 0);
            Assert.Equal("2:05", AdventurePresenter.HudBadge(t, 0));
            Assert.Equal("เหลือ 2:05 · ปิดเกมไว้ได้ การเดินทางยังไปต่อ", AdventurePresenter.TripStatus(t, 0));
            Assert.Equal("1:59", AdventurePresenter.HudBadge(t, 6));
            Assert.Contains("กำลังเช็กกับเซิร์ฟเวอร์", AdventurePresenter.TripStatus(t, 200));
            t.Apply(Trip(0, ready: true), 201);
            Assert.Equal("ถึงแล้ว!", AdventurePresenter.HudBadge(t, 201));
            Assert.Equal("กลับถึงบ้านแล้ว มาดูว่าได้อะไรมาบ้าง", AdventurePresenter.TripStatus(t, 201));
            Assert.Equal("1:02:05", AdventurePresenter.Clock(3725));
            t.Reset();
            Assert.False(t.Loaded);
            Assert.True(t.CheckDue(0));
        }

        // POST /expeditions/:id/claim from a dev backend (EXPEDITION_TIME_SCALE=0.02), 15 Oct 2026: a new player's first
        // orchard trip gathered no sticky rice, so the first-visit net topped up one (skills/eventChances trimmed)
        private const string FirstVisitJson = @"{""id"":""5e247a78-7175-4774-9088-f77f9eb1f623"",""site"":""ORCHARD"",""plan"":""CAREFUL"",""explorerLevel"":1,""startedAt"":""2026-10-15T04:24:43.972Z"",""endsAt"":""2026-10-15T04:24:47.972Z"",""secondsLeft"":0,""ready"":false,""claimed"":true,""result"":{""loot"":{""MANGO"":3,""COCONUT"":4,""STICKY_RICE"":1},""events"":[{""lost"":{},""event"":""MONKEYS"",""gained"":{""MANGO"":2},""success"":true},{""lost"":{},""event"":""RAIN_SHOWER"",""gained"":{""COCONUT"":1},""success"":true}],""gathered"":{""MANGO"":1,""COCONUT"":3},""playerXp"":5,""explorerXp"":16,""playerLevel"":null,""firstVisitBonus"":{""STICKY_RICE"":1},""explorerLeveledUp"":false,""explorerLevelAfter"":1},""replayed"":false,""materials"":{""MANGO"":3,""COCONUT"":4,""STICKY_RICE"":1,""JASMINE"":0,""MARIGOLD"":0,""WILD_HONEY"":0,""LEMONGRASS"":0,""HERB"":0,""BEESWAX"":0}}";

        [Fact]
        public void First_visit_bonus_is_told_as_a_welcome_gift_and_hinted_before_leaving()
        {
            var t = JsonConvert.DeserializeObject<ExpeditionTrip>(FirstVisitJson);
            Assert.Equal(1, t.result.firstVisitBonus["STICKY_RICE"]);
            Assert.Equal(1, t.result.loot["STICKY_RICE"]); // the bonus is already in the loot, not on top of it
            var lines = AdventurePresenter.Story(t);
            Assert.Contains(lines, l => l.Contains("ป้าเจ้าของสวนฝากของต้อนรับ") && l.Contains("ข้าวเหนียว 1") && l.Contains("ข้าวเหนียวมะม่วง"));
            // an older trip (no bonus field) or a later one (empty bonus) tells no gift
            var later = JsonConvert.DeserializeObject<ExpeditionTrip>(FirstVisitJson.Replace(@"""firstVisitBonus"":{""STICKY_RICE"":1}", @"""firstVisitBonus"":{}"));
            Assert.DoesNotContain(AdventurePresenter.Story(later), l => l.Contains("ต้อนรับ"));
            var old = JsonConvert.DeserializeObject<ExpeditionTrip>(FirstVisitJson.Replace(@"""firstVisitBonus"":{""STICKY_RICE"":1},", ""));
            Assert.DoesNotContain(AdventurePresenter.Story(old), l => l.Contains("ต้อนรับ"));

            var site = JsonConvert.DeserializeObject<ExpeditionSiteInfo>(@"{""site"": ""FLOWER_FOREST"", ""unlocked"": true, ""firstVisit"": true, ""firstVisitRecipe"": ""JASMINE_GARLAND""}");
            Assert.Contains("พวงมาลัยมะลิ", AdventurePresenter.FirstVisitHint(site));
            site.firstVisit = false;
            Assert.Null(AdventurePresenter.FirstVisitHint(site));
            foreach (var s in new[] { "ORCHARD", "FLOWER_FOREST", "HERB_VALLEY" }) Assert.NotNull(AdventurePresenter.FirstVisitRecipe(s));
        }
    }
}
