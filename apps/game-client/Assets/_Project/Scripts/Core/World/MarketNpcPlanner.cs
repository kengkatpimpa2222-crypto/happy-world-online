using System;
using System.Collections.Generic;
using HappyWorld.Core.Api;
using HappyWorld.Core.Realtime;

namespace HappyWorld.Core.World
{
    public enum NpcPhase
    {
        Waiting,
        WalkingIn,
        AtStall,
        WalkingOut,
        Done,
    }

    /// <summary>
    /// One NPC customer's walk: market entrance → in front of the stall → back to the entrance.
    /// The server already decided what happened (bought / too expensive / sold out / not selling);
    /// this only plays it out so players can SEE customers react to their prices.
    /// </summary>
    public sealed class NpcPlan
    {
        public string Id;
        public int Slot;
        public MarketVisit Visit;
        public double StartAt;
        public float Speed = 3f;
        public float WaitSeconds = 2.4f;
        /// <summary>Path in server units (x, y): entrance, walkway, stall front.</summary>
        public readonly List<float[]> PathIn = new List<float[]>();

        /// <summary>Speech bubble at the stall, Thai.</summary>
        public string Bubble;
        /// <summary>Gold shown popping up at the stall (0 = none). Purely visual; the server holds the money.</summary>
        public int CoinPopup;

        public double WalkInSeconds => PathLength() / Speed;
        public double EndsAt => StartAt + WalkInSeconds * 2 + WaitSeconds;

        private float PathLength()
        {
            float d = 0;
            for (int i = 1; i < PathIn.Count; i++) d += Dist(PathIn[i - 1], PathIn[i]);
            return d;
        }

        /// <summary>Where the NPC is at time <paramref name="now"/> (same clock as StartAt).</summary>
        public NpcPhase PositionAt(double now, out float x, out float y)
        {
            var t = now - StartAt;
            var walk = WalkInSeconds;
            if (t < 0) { x = PathIn[0][0]; y = PathIn[0][1]; return NpcPhase.Waiting; }
            if (t < walk) { Along((float)(t * Speed), false, out x, out y); return NpcPhase.WalkingIn; }
            if (t < walk + WaitSeconds) { var end = PathIn[PathIn.Count - 1]; x = end[0]; y = end[1]; return NpcPhase.AtStall; }
            var back = t - walk - WaitSeconds;
            if (back < walk) { Along((float)(back * Speed), true, out x, out y); return NpcPhase.WalkingOut; }
            x = PathIn[0][0];
            y = PathIn[0][1];
            return NpcPhase.Done;
        }

        private void Along(float distance, bool reverse, out float x, out float y)
        {
            int n = PathIn.Count;
            for (int i = 1; i < n; i++)
            {
                var a = PathIn[reverse ? n - i : i - 1];
                var b = PathIn[reverse ? n - i - 1 : i];
                var seg = Dist(a, b);
                if (distance <= seg && seg > 0)
                {
                    var k = distance / seg;
                    x = a[0] + (b[0] - a[0]) * k;
                    y = a[1] + (b[1] - a[1]) * k;
                    return;
                }
                distance -= seg;
            }
            var last = PathIn[reverse ? 0 : n - 1];
            x = last[0];
            y = last[1];
        }

        private static float Dist(float[] a, float[] b)
        {
            float dx = b[0] - a[0], dy = b[1] - a[1];
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }
    }

    /// <summary>Turns market:activity messages into NPC walks (engine-free, unit-tested with .NET).</summary>
    public static class MarketNpcPlanner
    {
        /// <summary>Market entrance next to the portal back to the plaza (see CITY_ZONES MARKET).</summary>
        public const float EntranceX = 4f, EntranceY = 20f;

        /// <param name="stall">The stall as GET /market returned it (position, prices).</param>
        /// <param name="now">Current time on the caller's clock (e.g. Time.time).</param>
        /// <param name="rng">Spreads arrivals over the minute; pass a seeded Random in tests.</param>
        public static List<NpcPlan> Plan(MarketActivity activity, MarketStall stall, double now, Random rng)
        {
            var plans = new List<NpcPlan>();
            if (activity == null || stall == null || activity.visits == null) return plans;
            // Arrivals are spread over most of a minute so the stall looks busy, not bursty.
            var spread = Math.Max(5, Math.Min(activity.tickSeconds, 60) * 0.7);
            for (int i = 0; i < activity.visits.Count; i++)
            {
                var v = activity.visits[i];
                var plan = new NpcPlan
                {
                    Id = activity.businessId + ":" + activity.tick + ":" + v.index,
                    Slot = activity.slot,
                    Visit = v,
                    StartAt = now + rng.NextDouble() * spread,
                    Speed = 2.6f + (float)rng.NextDouble() * 0.8f,
                };
                // small sideways offset so several customers don't stand inside each other
                var side = ((float)rng.NextDouble() - 0.5f) * 2.4f;
                var frontY = stall.y + stall.face * 1.9f;
                plan.PathIn.Add(new[] { EntranceX, EntranceY });
                plan.PathIn.Add(new[] { stall.x + side, EntranceY });
                plan.PathIn.Add(new[] { stall.x + side, frontY });
                plan.Bubble = BubbleText(v, PriceOf(stall, v.productCode));
                plan.CoinPopup = v.outcome == "BOUGHT" ? v.quantity * Math.Max(0, PriceOf(stall, v.productCode)) : 0;
                plans.Add(plan);
            }
            return plans;
        }

        private static int PriceOf(MarketStall stall, string productCode)
        {
            foreach (var p in stall.products) if (p.productCode == productCode) return p.price;
            return 0;
        }

        /// <summary>What the customer says at the stall. Teaches cause and effect in plain Thai.</summary>
        public static string BubbleText(MarketVisit v, int price)
        {
            var name = ProductName(v.productCode);
            switch (v.outcome)
            {
                case "BOUGHT":
                    if (v.customerType == "FESTIVAL_GOER") return "งานเทศกาลต้องมี" + name + "! " + v.quantity + " " + ProductUnit(v.productCode);
                    return "ขอ" + name + " " + v.quantity + " " + ProductUnit(v.productCode) + "!";
                case "TOO_EXPENSIVE":
                    return price > 0 ? name + " " + price + " ทอง แพงไปหน่อย..." : "แพงไปหน่อย...";
                case "SOLD_OUT":
                    return "อ้าว " + name + "หมดแล้วเหรอ";
                case "NOT_SELLING":
                    return "ไม่มี" + name + "ขายเหรอ";
                default:
                    return "";
            }
        }

        public static string ProductName(string code)
        {
            switch (code)
            {
                case "LEMONADE": return "น้ำมะนาว";
                case "BREAD": return "ขนมปัง";
                case "FLOWER": return "ดอกไม้";
                case "THAI_TEA": return "ชาไทย";
                case "MEATBALL": return "ลูกชิ้นปิ้ง";
                default: return code ?? "";
            }
        }

        public static string ProductUnit(string code)
        {
            switch (code)
            {
                case "LEMONADE":
                case "THAI_TEA": return "แก้ว";
                case "BREAD": return "ชิ้น";
                case "FLOWER": return "ช่อ";
                case "MEATBALL": return "ไม้";
                default: return "ชิ้น";
            }
        }

        public static string CustomerName(string type)
        {
            switch (type)
            {
                case "STUDENT": return "นักเรียน";
                case "WORKER": return "พนักงาน";
                case "TOURIST": return "นักท่องเที่ยว";
                case "GRANDMA": return "คุณยาย";
                case "FESTIVAL_GOER": return "นักเที่ยวงาน";
                default: return "ลูกค้า";
            }
        }
    }
}
