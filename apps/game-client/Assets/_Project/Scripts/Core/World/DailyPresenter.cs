using System;
using System.Collections.Generic;
using System.Linq;
using HappyWorld.Core.Api;
using HappyWorld.Core.Localization;

namespace HappyWorld.Core.World
{
    /// <summary>
    /// Engine-free helpers for the daily rewards panel (Phase 11): what the HUD button says, reward lines,
    /// mission order and progress. Unit-tested with .NET; the Unity layer only draws the result.
    /// </summary>
    public static class DailyPresenter
    {
        /// <summary>HUD button: "รางวัลวันนี้ (2)" while something waits, else today's mission progress.</summary>
        public static string HudLabel(DailyState s)
        {
            var title = Texts.T("ui.daily");
            if (s == null) return title;
            if (s.claimable > 0) return title + " (" + s.claimable + ")";
            var today = s.daily.missions.Where(m => !m.fromPreviousPeriod).ToList();
            if (today.Count == 0) return title;
            return "ภารกิจวันนี้ " + today.Count(m => m.status != "IN_PROGRESS") + "/" + today.Count;
        }

        /// <summary>"50 ทอง + มะม่วง ×3"</summary>
        public static string RewardText(int gold, RewardItem item, int xp = 0)
        {
            var parts = new List<string>();
            if (gold > 0) parts.Add(gold + " " + Texts.T("ui.gold"));
            if (item != null) parts.Add(Texts.T(item.itemId) + (item.quantity > 1 ? " ×" + item.quantity : ""));
            if (xp > 0) parts.Add("XP " + xp);
            return string.Join(" + ", parts);
        }

        public static string StepTitle(CheckinStep s) => s.step == 7 ? "วันที่ 7 ของขวัญพิเศษ" : "วันที่ " + s.step;

        /// <summary>Short text inside a check-in tile: the reward and its state.</summary>
        public static string StepTile(CheckinStep s)
        {
            var mark = s.state == "CLAIMED" ? "✓" : s.state == "TODAY" ? "วันนี้" : "";
            var reward = s.item != null ? Texts.T(s.item.itemId) + (s.item.quantity > 1 ? " ×" + s.item.quantity : "") : "";
            if (s.gold > 0) reward = s.gold + " " + Texts.T("ui.gold") + (reward.Length > 0 ? "\n+" + reward : "");
            return "<b>" + s.step + "</b>\n<size=20>" + reward + "</size>" + (mark.Length > 0 ? "\n<size=20>" + mark + "</size>" : "");
        }

        /// <summary>The headline over the 7 tiles.</summary>
        public static string CheckinHeadline(CheckinView c)
        {
            if (c == null) return "";
            var step = c.track.FirstOrDefault(t => t.step == c.todayStep);
            var reward = step != null ? RewardText(step.gold, step.item) : "";
            return c.claimedToday
                ? "เช็กอินวันนี้แล้ว (รวม " + c.totalDays + " วัน) พรุ่งนี้มีรางวัลวันที่ " + (c.todayStep % 7 + 1) + " รออยู่"
                : "เช็กอินวันนี้รับ " + reward + " (วันที่ " + c.todayStep + " จาก 7)";
        }

        /// <summary>One gentle line under the track: missing a day never resets it.</summary>
        public const string NoPressureNote = "ไม่ต้องเข้าทุกวันก็ได้ วันไหนไม่ได้มา รางวัลจะรอที่เดิมไม่หายไปไหน";

        /// <summary>A toast line for the first time the HUD sees the day's state; null when nothing waits.</summary>
        public static string LoginNudge(DailyState s)
        {
            if (s == null || s.checkin == null || s.checkin.claimedToday) return null;
            var step = s.checkin.track.FirstOrDefault(t => t.step == s.checkin.todayStep);
            return step == null ? null : "เช็กอินวันนี้รับ " + RewardText(step.gold, step.item);
        }

        public static string Title(Mission m) =>
            Texts.Locale == "en" && !string.IsNullOrEmpty(m.titleEn) ? m.titleEn : (string.IsNullOrEmpty(m.titleTh) ? m.id : m.titleTh);

        /// <summary>"6/10", "สำเร็จ!" or "✓ รับแล้ว".</summary>
        public static string ProgressText(Mission m) =>
            m.Claimed ? "✓ รับแล้ว" : m.CanClaim ? "สำเร็จ!" : Math.Min(m.progress, m.target) + "/" + m.target;

        public static float Progress(Mission m) => m.target <= 0 ? 1f : Math.Max(0f, Math.Min(1f, m.progress / (float)m.target));

        /// <summary>Ready-to-claim first (yesterday's leftovers before today's), then in progress, claimed last.</summary>
        public static List<Mission> Sorted(IEnumerable<Mission> missions) =>
            missions.Select((m, i) => (m, i))
                .OrderBy(x => x.m.CanClaim ? 0 : x.m.Claimed ? 2 : 1)
                .ThenBy(x => x.m.fromPreviousPeriod ? 0 : 1)
                .ThenBy(x => x.i)
                .Select(x => x.m)
                .ToList();

        /// <summary>"ภารกิจใหม่อีก 3 ชม. 20 นาที" for the panel's section headers.</summary>
        public static string ResetLine(DateTime endsAt, DateTime serverNow, bool weekly) =>
            (weekly ? "ภารกิจรายสัปดาห์ใหม่" : "ภารกิจใหม่") + FestivalPresenter.TimeLeft(endsAt.ToUniversalTime() - serverNow.ToUniversalTime());

        /// <summary>Toast after a check-in.</summary>
        public static string CheckinToast(CheckinResult r) =>
            "เช็กอินวันที่ " + r.step + " ได้รับ " + RewardText(r.rewards.gold, r.rewards.item) + (r.step == 7 ? "  ครบ 7 วันแล้ว เก่งมาก!" : "");

        public static string ClaimToast(MissionClaimResult r) =>
            "ได้รับ " + RewardText(r.rewards.gold, r.rewards.item, r.rewards.xp) + (r.level.HasValue ? "  เลเวล " + r.level : "");
    }

    /// <summary>
    /// Keeps the last GET /daily and the server's clock (its "now" plus the app clock since), and says when to
    /// read it again: right after Bangkok midnight (new check-in + new missions) and every few minutes,
    /// with a short back-off after a failed read. No Unity types, so it is unit-tested with .NET.
    /// </summary>
    public sealed class DailyTracker
    {
        public const double ResyncSeconds = 300;
        public const double RetrySeconds = 20;

        public DailyState State { get; private set; }
        private double _fetchedAt;
        private double _nextFetchAt;
        private bool _nudged;

        /// <summary>True once (per state) when the HUD should gently mention today's check-in.</summary>
        public bool TakeNudge()
        {
            if (_nudged || State == null || State.checkin.claimedToday) return false;
            _nudged = true;
            return true;
        }

        public void Apply(DailyState s, double clock)
        {
            if (s == null) return;
            var newDay = State == null || State.checkin.today != s.checkin.today;
            State = s;
            _fetchedAt = clock;
            if (newDay) _nudged = false;
            var untilReset = (s.checkin.nextResetAt.ToUniversalTime() - s.now.ToUniversalTime()).TotalSeconds;
            // a moment after midnight so the server is surely on the new day
            _nextFetchAt = clock + Math.Max(1, Math.Min(ResyncSeconds, untilReset + 2));
        }

        public void Failed(double clock) => _nextFetchAt = clock + RetrySeconds;

        public bool Due(double clock) => clock >= _nextFetchAt;

        public DateTime ServerNow(double clock) =>
            State == null ? DateTime.UtcNow : State.now.ToUniversalTime().AddSeconds(Math.Max(0, clock - _fetchedAt));

        public void Clear()
        {
            State = null;
            _nextFetchAt = 0;
            _nudged = false;
        }
    }
}
