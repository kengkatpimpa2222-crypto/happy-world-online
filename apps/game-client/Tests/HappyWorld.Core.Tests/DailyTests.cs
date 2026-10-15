using System;
using System.Linq;
using HappyWorld.Core.Api;
using HappyWorld.Core.Net;
using HappyWorld.Core.World;
using Xunit;

namespace HappyWorld.Core.Tests
{
    /// <summary>Phase 11 daily rewards: parsing GET /daily and what the HUD / panel show.</summary>
    public class DailyTests
    {
        // shape copied from the backend's DailyRewardsService.state()
        private const string StateJson = @"{""now"":""2026-10-15T05:00:00.000Z"",
          ""checkin"":{""today"":""2026-10-15"",""claimedToday"":false,""todayStep"":2,""totalDays"":1,""nextResetAt"":""2026-10-15T17:00:00.000Z"",
            ""track"":[{""step"":1,""gold"":50,""item"":null,""state"":""CLAIMED""},
                       {""step"":2,""gold"":0,""item"":{""itemId"":""mat_mango"",""quantity"":3},""state"":""TODAY""},
                       {""step"":3,""gold"":80,""item"":null,""state"":""UPCOMING""},
                       {""step"":4,""gold"":0,""item"":{""itemId"":""furn_plant_pot"",""quantity"":1},""state"":""UPCOMING""},
                       {""step"":5,""gold"":120,""item"":null,""state"":""UPCOMING""},
                       {""step"":6,""gold"":0,""item"":{""itemId"":""mat_coconut"",""quantity"":3},""state"":""UPCOMING""},
                       {""step"":7,""gold"":200,""item"":{""itemId"":""furn_nang_kwak"",""quantity"":1},""state"":""UPCOMING""}]},
          ""daily"":{""periodKey"":""D2026-10-15"",""endsAt"":""2026-10-15T17:00:00.000Z"",""missions"":[
            {""id"":""d_craft_1"",""periodKey"":""D2026-10-14"",""period"":""DAILY"",""objective"":""CRAFT_ITEM"",""target"":1,""progress"":1,""status"":""COMPLETED"",
             ""rewards"":{""gold"":50,""xp"":12,""item"":null},""titleTh"":""แปรรูปสินค้า 1 ครั้ง"",""titleEn"":""Craft once"",""fromPreviousPeriod"":true},
            {""id"":""d_sell_10"",""periodKey"":""D2026-10-15"",""period"":""DAILY"",""objective"":""BUSINESS_SELL_UNITS"",""target"":10,""progress"":6,""status"":""IN_PROGRESS"",
             ""rewards"":{""gold"":40,""xp"":10,""item"":null},""titleTh"":""ขายสินค้าให้ได้ 10 ชิ้น"",""titleEn"":""Sell 10 goods"",""fromPreviousPeriod"":false},
            {""id"":""d_minigame_1"",""periodKey"":""D2026-10-15"",""period"":""DAILY"",""objective"":""PLAY_MINIGAME"",""target"":1,""progress"":1,""status"":""CLAIMED"",
             ""rewards"":{""gold"":30,""xp"":10,""item"":null},""titleTh"":""ทำสินค้าเองด้วยมินิเกม 1 รอบ"",""titleEn"":""Make goods by hand once"",""fromPreviousPeriod"":false},
            {""id"":""d_explore_1"",""periodKey"":""D2026-10-15"",""period"":""DAILY"",""objective"":""EXPEDITION_COMPLETE"",""target"":1,""progress"":1,""status"":""COMPLETED"",
             ""rewards"":{""gold"":40,""xp"":10,""item"":null},""titleTh"":""ออกสำรวจให้สำเร็จ 1 ครั้ง"",""titleEn"":""Finish 1 expedition"",""fromPreviousPeriod"":false}]},
          ""weekly"":{""periodKey"":""W2026-10-12"",""endsAt"":""2026-10-18T17:00:00.000Z"",""missions"":[
            {""id"":""w_visit_4"",""periodKey"":""W2026-10-12"",""period"":""WEEKLY"",""objective"":""CHECK_IN"",""target"":4,""progress"":1,""status"":""IN_PROGRESS"",
             ""rewards"":{""gold"":150,""xp"":30,""item"":{""itemId"":""mat_sticky_rice"",""quantity"":3}},""titleTh"":""แวะมาเช็กอิน 4 วันในสัปดาห์นี้ (ไม่ต้องติดกัน)"",""titleEn"":""Check in on 4 days this week (any days)"",""fromPreviousPeriod"":false}]},
          ""claimable"":3}";

        private static DailyState State() => HappyWorldApi.Parse<DailyState>(new HttpResult { Status = 200, Body = StateJson });

        [Fact]
        public void ParsesTheServerState()
        {
            var s = State();
            Assert.Equal(new DateTime(2026, 10, 15, 5, 0, 0, DateTimeKind.Utc), s.now.ToUniversalTime());
            Assert.Equal(2, s.checkin.todayStep);
            Assert.Equal(7, s.checkin.track.Count);
            Assert.Equal("mat_mango", s.checkin.track[1].item.itemId);
            Assert.Null(s.checkin.track[0].item);
            Assert.True(s.daily.missions[0].CanClaim);
            Assert.True(s.daily.missions[0].fromPreviousPeriod);
            Assert.True(s.daily.missions[2].Claimed);
            Assert.Equal(3, s.weekly.missions[0].rewards.item.quantity);
            Assert.Equal(3, s.claimable);
        }

        [Fact]
        public void HudLabelShowsWaitingRewardsThenTodaysProgress()
        {
            var s = State();
            Assert.Equal("รางวัลวันนี้ (3)", DailyPresenter.HudLabel(s));
            s.claimable = 0;
            // leftovers from yesterday don't count toward today's 3
            Assert.Equal("ภารกิจวันนี้ 2/3", DailyPresenter.HudLabel(s));
            Assert.Equal("รางวัลวันนี้", DailyPresenter.HudLabel(null));
        }

        [Fact]
        public void RewardAndTileTexts()
        {
            Assert.Equal("50 ทอง", DailyPresenter.RewardText(50, null));
            Assert.Equal("มะม่วง ×3", DailyPresenter.RewardText(0, new RewardItem { itemId = "mat_mango", quantity = 3 }));
            Assert.Equal("200 ทอง + ตุ๊กตานางกวัก + XP 5", DailyPresenter.RewardText(200, new RewardItem { itemId = "furn_nang_kwak", quantity = 1 }, 5));
            var s = State();
            Assert.Contains("✓", DailyPresenter.StepTile(s.checkin.track[0]));
            Assert.Contains("วันนี้", DailyPresenter.StepTile(s.checkin.track[1]));
            Assert.DoesNotContain("วันนี้", DailyPresenter.StepTile(s.checkin.track[2]));
            Assert.Contains("ตุ๊กตานางกวัก", DailyPresenter.StepTile(s.checkin.track[6]));
            Assert.Equal("วันที่ 7 ของขวัญพิเศษ", DailyPresenter.StepTitle(s.checkin.track[6]));
        }

        [Fact]
        public void CheckinHeadlineAndNudge()
        {
            var s = State();
            Assert.Equal("เช็กอินวันนี้รับ มะม่วง ×3 (วันที่ 2 จาก 7)", DailyPresenter.CheckinHeadline(s.checkin));
            Assert.Equal("เช็กอินวันนี้รับ มะม่วง ×3", DailyPresenter.LoginNudge(s));
            s.checkin.claimedToday = true;
            Assert.Null(DailyPresenter.LoginNudge(s));
            Assert.Contains("พรุ่งนี้มีรางวัลวันที่ 3", DailyPresenter.CheckinHeadline(s.checkin));
            s.checkin.todayStep = 7;
            Assert.Contains("วันที่ 1", DailyPresenter.CheckinHeadline(s.checkin)); // the track wraps
        }

        [Fact]
        public void MissionsSortReadyFirstAndClaimedLast()
        {
            var order = DailyPresenter.Sorted(State().daily.missions).Select(m => m.id).ToArray();
            Assert.Equal(new[] { "d_craft_1", "d_explore_1", "d_sell_10", "d_minigame_1" }, order);
        }

        [Fact]
        public void MissionProgressTexts()
        {
            var m = State().daily.missions;
            Assert.Equal("สำเร็จ!", DailyPresenter.ProgressText(m[0]));
            Assert.Equal("6/10", DailyPresenter.ProgressText(m[1]));
            Assert.Equal("✓ รับแล้ว", DailyPresenter.ProgressText(m[2]));
            Assert.Equal(0.6f, DailyPresenter.Progress(m[1]), 3);
            Assert.Equal("ขายสินค้าให้ได้ 10 ชิ้น", DailyPresenter.Title(m[1]));
            m[1].progress = 99;
            Assert.Equal(1f, DailyPresenter.Progress(m[1]));
            Assert.Equal("10/10", DailyPresenter.ProgressText(m[1]));
            var now = new DateTime(2026, 10, 15, 13, 40, 0, DateTimeKind.Utc);
            Assert.Equal("ภารกิจใหม่อีก 3 ชม. 20 นาที", DailyPresenter.ResetLine(new DateTime(2026, 10, 15, 17, 0, 0, DateTimeKind.Utc), now, false));
        }

        [Fact]
        public void ToastsAfterClaims()
        {
            var r = new CheckinResult { step = 7, rewards = new CheckinRewards { gold = 200, item = new RewardItem { itemId = "furn_nang_kwak", quantity = 1 } } };
            Assert.Equal("เช็กอินวันที่ 7 ได้รับ 200 ทอง + ตุ๊กตานางกวัก  ครบ 7 วันแล้ว เก่งมาก!", DailyPresenter.CheckinToast(r));
            var c = new MissionClaimResult { rewards = new MissionRewards { gold = 40, xp = 10 }, level = 3 };
            Assert.Equal("ได้รับ 40 ทอง + XP 10  เลเวล 3", DailyPresenter.ClaimToast(c));
        }

        [Fact]
        public void TrackerRereadsJustAfterBangkokMidnightAndKeepsServerTime()
        {
            var t = new DailyTracker();
            Assert.True(t.Due(0));
            var s = State();
            s.now = new DateTime(2026, 10, 15, 16, 58, 0, DateTimeKind.Utc); // 2 minutes to midnight in Bangkok
            t.Apply(s, 1000);
            Assert.False(t.Due(1100));
            Assert.True(t.Due(1000 + 122)); // 120 s to midnight + 2 s margin
            Assert.Equal(new DateTime(2026, 10, 15, 16, 58, 30, DateTimeKind.Utc), t.ServerNow(1030));

            var noon = State(); // 12 hours before reset: plain resync
            t.Apply(noon, 2000);
            Assert.False(t.Due(2000 + DailyTracker.ResyncSeconds - 1));
            Assert.True(t.Due(2000 + DailyTracker.ResyncSeconds));
            t.Failed(3000);
            Assert.False(t.Due(3000 + DailyTracker.RetrySeconds - 1));
            Assert.True(t.Due(3000 + DailyTracker.RetrySeconds));
        }

        [Fact]
        public void NudgeOncePerDayOnlyWhileTheCheckinWaits()
        {
            var t = new DailyTracker();
            Assert.False(t.TakeNudge());
            t.Apply(State(), 0);
            Assert.True(t.TakeNudge());
            Assert.False(t.TakeNudge());
            t.Apply(State(), 10); // same day: still quiet
            Assert.False(t.TakeNudge());
            var tomorrow = State();
            tomorrow.checkin.today = "2026-10-16";
            t.Apply(tomorrow, 20);
            Assert.True(t.TakeNudge());
            var done = State();
            done.checkin.today = "2026-10-17";
            done.checkin.claimedToday = true;
            t.Apply(done, 30);
            Assert.False(t.TakeNudge());
            t.Clear();
            Assert.Null(t.State);
        }
    }
}
