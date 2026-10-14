using System;
using HappyWorld.Core.Api;
using HappyWorld.Core.Localization;

namespace HappyWorld.Core.World
{
    /// <summary>
    /// Engine-free festival helpers (Phase 6): what the HUD banner says, how decorated the city is,
    /// and the festival colour. Unit-tested with .NET; the Unity layer only draws the result.
    /// </summary>
    public static class FestivalPresenter
    {
        /// <summary>
        /// 0 = no festival, 1 = festival running (banner + bunting), 2 = tier 1 (lanterns),
        /// 3 = tier 2 (lights), 4 = tier 3 (fireworks). The city literally dresses up as players sell.
        /// </summary>
        public static int DecorStage(FestivalInfo f)
        {
            if (f == null || !f.Active) return 0;
            return 1 + Math.Max(0, Math.Min(3, f.city.tiersReached));
        }

        public static string Name(FestivalInfo f)
        {
            if (f == null) return "";
            var t = Texts.T(f.nameKey);
            return t == f.nameKey && !string.IsNullOrEmpty(f.nameTh) ? f.nameTh : t;
        }

        /// <summary>"อีก 2 วัน 5 ชม." / "อีก 3 ชม. 20 นาที" / "อีก 12 นาที"</summary>
        public static string TimeLeft(TimeSpan left)
        {
            if (left <= TimeSpan.Zero) return "หมดเวลาแล้ว";
            if (left.TotalDays >= 1) return "อีก " + (int)left.TotalDays + " วัน " + left.Hours + " ชม.";
            if (left.TotalHours >= 1) return "อีก " + (int)left.TotalHours + " ชม. " + left.Minutes + " นาที";
            return "อีก " + Math.Max(1, (int)Math.Ceiling(left.TotalMinutes)) + " นาที";
        }

        /// <summary>One line for the HUD button. <paramref name="now"/> is the server's clock (UTC).</summary>
        public static string Banner(FestivalInfo f, DateTime now)
        {
            if (f == null) return Texts.T("ui.festival");
            var name = Name(f);
            if (f.status == "UPCOMING") return name + " เริ่ม" + TimeLeft(f.startsAt - now);
            if (!f.Active) return name + " จบแล้ว";
            var next = f.NextTier();
            var progress = next != null ? f.city.points + "/" + next.target : "ครบทุกขั้น!";
            return name + " · เมือง " + progress;
        }

        /// <summary>Fraction 0..1 toward the next tier (1 when all tiers are reached).</summary>
        public static float Progress(FestivalInfo f)
        {
            if (f == null) return 0f;
            var next = f.NextTier();
            if (next == null) return 1f;
            int prev = 0;
            foreach (var t in f.tiers) if (t.reached) prev = t.target;
            var span = Math.Max(1, next.target - prev);
            return Math.Max(0f, Math.Min(1f, (f.city.points - prev) / (float)span));
        }

        /// <summary>Hint shown next to a product in the shop manager ("ขายดีช่วงเทศกาล x3").</summary>
        public static string ProductHint(FestivalInfo f, string productCode)
        {
            if (f == null || f.status == "ENDED") return "";
            var d = f.DemandOf(productCode);
            if (d <= 1) return "";
            return f.Active ? "ขายดีช่วงเทศกาล x" + d : "เทศกาลนี้จะขายดี x" + d;
        }

        /// <summary>Parses "#RRGGBB" into 0..1 floats; falls back to a warm yellow.</summary>
        public static void ParseColor(string hex, out float r, out float g, out float b)
        {
            r = 1f; g = 0.84f; b = 0.31f;
            if (string.IsNullOrEmpty(hex)) return;
            var h = hex.TrimStart('#');
            if (h.Length != 6) return;
            try
            {
                r = Convert.ToInt32(h.Substring(0, 2), 16) / 255f;
                g = Convert.ToInt32(h.Substring(2, 2), 16) / 255f;
                b = Convert.ToInt32(h.Substring(4, 2), 16) / 255f;
            }
            catch (FormatException)
            {
                r = 1f; g = 0.84f; b = 0.31f;
            }
        }

        /// <summary>Line announcing a tier the city just reached.</summary>
        public static string MilestoneText(int tier) => "ทั้งเมืองช่วยกันสำเร็จ! " + Texts.T("festival.tier." + tier);

        /// <summary>Name for the top helpers list (under-13 viewers get no names of other players).</summary>
        public static string HelperName(FestivalHelper h) =>
            h.isMe ? (string.IsNullOrEmpty(h.displayName) ? "คุณ" : h.displayName + " (คุณ)") : (h.displayName ?? "ผู้ช่วยอันดับ " + h.rank);
    }
}
