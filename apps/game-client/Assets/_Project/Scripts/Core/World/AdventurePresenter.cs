using System;
using System.Collections.Generic;
using System.Linq;
using HappyWorld.Core.Api;

namespace HappyWorld.Core.World
{
    /// <summary>
    /// Thai text for exploring and crafting (Phase 9). The server sends codes only (sites, plans, events,
    /// materials); this turns a claimed trip into a short story the player reads on the way home.
    /// Codes mirror packages/shared-contracts/src/adventure.ts.
    /// </summary>
    public static class AdventurePresenter
    {
        public static readonly string[] MaterialOrder =
            { "MANGO", "COCONUT", "STICKY_RICE", "JASMINE", "MARIGOLD", "WILD_HONEY", "LEMONGRASS", "HERB", "BEESWAX" };

        public static string SiteName(string site)
        {
            switch (site)
            {
                case "ORCHARD": return "สวนผลไม้ริมคลอง";
                case "FLOWER_FOREST": return "ป่าดอกไม้";
                case "HERB_VALLEY": return "หุบเขาสมุนไพร";
                default: return site ?? "";
            }
        }

        public static string SiteBlurb(string site)
        {
            switch (site)
            {
                case "ORCHARD": return "มะม่วง มะพร้าว ข้าวเหนียว สำหรับข้าวเหนียวมะม่วง";
                case "FLOWER_FOREST": return "มะลิ ดาวเรือง และน้ำผึ้งป่า สำหรับพวงมาลัยและแยม";
                case "HERB_VALLEY": return "ตะไคร้ สมุนไพร ขี้ผึ้ง สำหรับยาหม่อง";
                default: return "";
            }
        }

        public static string PlanName(string plan)
        {
            switch (plan)
            {
                case "CAREFUL": return "ไปแบบระวัง";
                case "STEADY": return "ไปแบบปกติ";
                case "BOLD": return "ไปแบบบุก";
                default: return plan ?? "";
            }
        }

        public static string PlanBlurb(string plan)
        {
            switch (plan)
            {
                case "CAREFUL": return "ผ่านเหตุการณ์ได้ง่าย ไม่เสียของ แต่เก็บได้น้อยลง";
                case "STEADY": return "เก็บของได้ปกติ พลาดก็ไม่เสียของ";
                case "BOLD": return "เก็บได้เยอะสุด แต่ถ้าพลาดจะทำของหล่นหาย";
                default: return "";
            }
        }

        public static string MaterialName(string code)
        {
            switch (code)
            {
                case "MANGO": return "มะม่วง";
                case "COCONUT": return "มะพร้าว";
                case "STICKY_RICE": return "ข้าวเหนียว";
                case "JASMINE": return "ดอกมะลิ";
                case "MARIGOLD": return "ดาวเรือง";
                case "WILD_HONEY": return "น้ำผึ้งป่า";
                case "LEMONGRASS": return "ตะไคร้";
                case "HERB": return "สมุนไพร";
                case "BEESWAX": return "ขี้ผึ้ง";
                default: return code ?? "";
            }
        }

        public static string RecipeName(string recipe)
        {
            switch (recipe)
            {
                case "MANGO_STICKY_RICE": return "ข้าวเหนียวมะม่วง";
                case "JASMINE_GARLAND": return "พวงมาลัยมะลิ";
                case "HERBAL_BALM": return "ยาหม่องสมุนไพร";
                case "HONEY_MANGO_JAM": return "แยมมะม่วงน้ำผึ้ง";
                default: return recipe ?? "";
            }
        }

        public static string SkillName(string skill) => skill == "EXPLORER" ? "นักสำรวจ" : skill == "CRAFTER" ? "ช่างฝีมือ" : skill ?? "";

        /// <summary>"มะม่วง 3 · มะพร้าว 2" in a fixed order; empty bag gives "-".</summary>
        public static string Bag(IDictionary<string, int> bag)
        {
            if (bag == null) return "-";
            var parts = MaterialOrder.Where(c => bag.TryGetValue(c, out var n) && n > 0).Select(c => MaterialName(c) + " " + bag[c]).ToList();
            return parts.Count == 0 ? "-" : string.Join(" · ", parts);
        }

        /// <summary>What happened at one event, success or not.</summary>
        public static string EventLine(ExpeditionEventOutcome e)
        {
            string ok, bad;
            switch (e.@event)
            {
                case "MONKEYS": ok = "ฝูงลิงแอบมาขโมยมะม่วง คุณเบี่ยงความสนใจด้วยกล้วยแล้วเก็บมะม่วงคืนมาได้"; bad = "ฝูงลิงไวกว่า คว้ามะม่วงหนีขึ้นต้นไม้ไป"; break;
                case "RAIN_SHOWER": ok = "ฝนตกปรอย ๆ คุณหลบใต้ศาลาแล้วเจอมะพร้าวหล่นอยู่ข้างทาง"; bad = "ฝนตกหนักจนต้องรีบกลับ ของในตะกร้าเปียกไปบางส่วน"; break;
                case "LOST_PUPPY": ok = "ลูกหมาหลงทาง คุณพาไปส่งบ้าน ป้าเจ้าของสวนขอบใจเลยให้ข้าวเหนียวมา"; bad = "ลูกหมาวิ่งหนีไปก่อน หวังว่ามันจะหาทางกลับบ้านได้นะ"; break;
                case "WILD_BEES": ok = "เจอรังผึ้งป่า คุณค่อย ๆ เก็บน้ำผึ้งโดยไม่ให้ผึ้งตกใจ"; bad = "ผึ้งหวงรัง ต้องรีบวิ่งหนี ของหล่นระหว่างทาง"; break;
                case "FOG": ok = "หมอกลงหนา คุณเดินตามกลิ่นดอกมะลิจนเจอพุ่มใหญ่"; bad = "หมอกลงหนาจนหลงทาง วนกลับมาที่เดิม"; break;
                case "BUTTERFLIES": ok = "ฝูงผีเสื้อพาไปเจอทุ่งดาวเรืองบานสะพรั่ง"; bad = "ไล่ตามผีเสื้อจนเหนื่อย แต่ไม่เจออะไรเพิ่ม"; break;
                case "BUFFALO_BRIDGE": ok = "ควายป่าตัวใหญ่ยืนขวางสะพาน คุณใจเย็นรอจนมันเดินหลีกไป ฝั่งโน้นมีขี้ผึ้งกับสมุนไพรเต็มเลย"; bad = "ควายป่าไม่ยอมหลบ ต้องเดินอ้อมทางไกล ของในย่ามหล่นหาย"; break;
                case "SLIPPERY_ROCKS": ok = "ข้ามลำธารบนหินลื่นได้อย่างปลอดภัย เจอตะไคร้ขึ้นริมน้ำ"; bad = "ลื่นหินตกน้ำ ของบางส่วนลอยไปกับลำธาร"; break;
                case "HERB_DOCTOR": ok = "คุณตาหมอยาสอนวิธีเลือกสมุนไพร แล้วแบ่งให้มาด้วย"; bad = "คุณตาหมอยาไม่อยู่บ้าน ไว้มาใหม่นะ"; break;
                case "HIVE_IN_TREE": ok = "เจอรังผึ้งในโพรงไม้ ได้ทั้งขี้ผึ้งและน้ำผึ้ง"; bad = "รังผึ้งอยู่สูงเกินไป ปีนแล้วพลาดทำของหล่น"; break;
                default: ok = e.@event; bad = e.@event; break;
            }
            var line = e.success ? "✓ " + ok : "✗ " + bad;
            if (e.success && e.gained != null && e.gained.Count > 0) line += " (+" + Bag(e.gained) + ")";
            if (!e.success && e.lost != null && e.lost.Count > 0) line += " (-" + Bag(e.lost) + ")";
            return line;
        }

        /// <summary>The whole trip as lines: where, each event, what came home and XP.</summary>
        public static List<string> Story(ExpeditionTrip trip)
        {
            var lines = new List<string> { "<b>" + SiteName(trip.site) + "</b> · " + PlanName(trip.plan) };
            var r = trip.result;
            if (r == null) return lines;
            lines.Add("ระหว่างทางเก็บได้ " + Bag(r.gathered));
            foreach (var e in r.events) lines.Add(EventLine(e));
            lines.Add("<b>ได้กลับบ้าน: " + Bag(r.loot) + "</b>");
            var xp = "XP นักสำรวจ +" + r.explorerXp + " · XP ตัวละคร +" + r.playerXp;
            if (r.explorerLeveledUp) xp += " · <b>นักสำรวจเลเวล " + r.explorerLevelAfter + "!</b>";
            if (r.playerLevel.HasValue) xp += " · <b>เลเวล " + r.playerLevel + "!</b>";
            lines.Add(xp);
            return lines;
        }

        /// <summary>"เหลือ 2:05" style countdown.</summary>
        public static string TimeLeft(int seconds)
        {
            if (seconds <= 0) return "ถึงแล้ว";
            var t = TimeSpan.FromSeconds(seconds);
            return "เหลือ " + (int)t.TotalMinutes + ":" + t.Seconds.ToString("00");
        }

        /// <summary>Recipe inputs vs what I have: "มะม่วง 1/3 · ...".</summary>
        public static string Inputs(RecipeInfo r, IDictionary<string, int> have)
        {
            var parts = MaterialOrder.Where(c => r.inputs.ContainsKey(c)).Select(c =>
            {
                have.TryGetValue(c, out var n);
                var need = r.inputs[c];
                var txt = MaterialName(c) + " " + need + " (มี " + n + ")";
                return n >= need ? txt : "<color=#C0392B>" + txt + "</color>";
            });
            return string.Join(" + ", parts);
        }
    }
}
