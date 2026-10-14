using System;
using System.Collections.Generic;
using System.Globalization;
using HappyWorld.Core.Api;
using HappyWorld.Core.Localization;
using HappyWorld.Core.Realtime;

namespace HappyWorld.Core.World
{
    /// <summary>
    /// Engine-free helpers for the living city (Phase 7): how light the sky is at a Bangkok hour, what the
    /// HUD says about time and weather, hints in the shop, and the city projects. Unit-tested with .NET;
    /// the Unity layer only draws the result. Text has no emoji: the bundled Thai font has no emoji glyphs.
    /// </summary>
    public static class CityLifePresenter
    {
        private static readonly Dictionary<string, string> PhaseTh = new Dictionary<string, string>
        {
            ["NIGHT"] = "กลางคืน", ["DAWN"] = "เช้ามืด", ["MORNING"] = "ช่วงเช้า",
            ["NOON"] = "ช่วงเที่ยง", ["AFTERNOON"] = "ช่วงบ่าย", ["EVENING"] = "ช่วงเย็น",
        };

        // ---------- clock ----------

        /// <summary>The city hour now: the server's hour plus the time since it was read (wraps at 24).</summary>
        public static float LocalHour(CityNow n, double secondsSinceFetch)
        {
            if (n == null) return 12f;
            var h = (n.hour + secondsSinceFetch / 3600.0) % 24.0;
            return (float)(h < 0 ? h + 24 : h);
        }

        /// <summary>Same boundaries as shared-contracts dayPhase().</summary>
        public static string Phase(float hour)
        {
            if (hour < 5) return "NIGHT";
            if (hour < 7) return "DAWN";
            if (hour < 11) return "MORNING";
            if (hour < 14) return "NOON";
            if (hour < 17) return "AFTERNOON";
            if (hour < 20) return "EVENING";
            return "NIGHT";
        }

        public static string Clock(float hour)
        {
            var total = (int)Math.Floor(hour * 60) % (24 * 60);
            return (total / 60).ToString("00") + ":" + (total % 60).ToString("00");
        }

        /// <summary>"14:05 ช่วงบ่าย · ฝนตก · หน้าฝน"</summary>
        public static string HudLine(CityNow n, float hour)
        {
            if (n == null) return "";
            return Clock(hour) + " " + PhaseTh[Phase(hour)] + " · " + n.weatherTh + " · " + n.seasonTh;
        }

        /// <summary>"ต่อไป 15:00 ฝนตก, 18:00 ครึ้มฟ้า" (Bangkok time).</summary>
        public static string ForecastLine(CityNow n, int count = 2)
        {
            if (n == null || n.forecast == null || n.forecast.Count == 0) return "";
            var parts = new List<string>();
            for (int i = 0; i < Math.Min(count, n.forecast.Count); i++)
            {
                var f = n.forecast[i];
                parts.Add(f.at.ToUniversalTime().AddHours(7).ToString("HH:mm", CultureInfo.InvariantCulture) + " " + f.weatherTh);
            }
            return "ต่อไป " + string.Join(", ", parts);
        }

        // ---------- light and sky ----------

        /// <summary>0 = night, 1 = full day, with a smooth dawn (5-7) and dusk (17.5-19.5).</summary>
        public static float Daylight(float hour)
        {
            if (hour >= 7 && hour <= 17.5f) return 1f;
            if (hour > 5 && hour < 7) return Smooth((hour - 5) / 2f);
            if (hour > 17.5f && hour < 19.5f) return 1f - Smooth((hour - 17.5f) / 2f);
            return 0f;
        }

        /// <summary>Street lamps and shop lights switch on when it gets dark.</summary>
        public static bool LampsOn(float hour) => Daylight(hour) < 0.5f;

        /// <summary>Sun height in degrees: low at dawn and dusk, high at noon; a high moon at night.</summary>
        public static float SunPitch(float hour)
        {
            var d = Daylight(hour);
            if (d <= 0f) return 40f;
            var t = Clamp01((hour - 6f) / 12.5f);
            return 10f + 70f * (float)Math.Sin(t * Math.PI);
        }

        public static float SunIntensity(float hour, string weather)
        {
            var i = 0.18f + 0.92f * Daylight(hour);
            return i * (weather == "RAIN" ? 0.6f : weather == "CLOUDY" ? 0.8f : weather == "HOT" ? 1.1f : 1f);
        }

        /// <summary>Sky colour (camera background): blue day, orange dusk, dark blue night, grey when it rains.</summary>
        public static void Sky(float hour, string weather, out float r, out float g, out float b)
        {
            var d = Daylight(hour);
            r = Lerp(0.07f, 0.62f, d);
            g = Lerp(0.09f, 0.85f, d);
            b = Lerp(0.22f, 1f, d);
            // a warm glow while the sun is low
            var glow = d > 0f && d < 1f ? 1f - Math.Abs(d - 0.5f) * 2f : 0f;
            r = Lerp(r, 1f, glow * 0.6f);
            g = Lerp(g, 0.6f, glow * 0.5f);
            b = Lerp(b, 0.45f, glow * 0.6f);
            if (weather == "RAIN" || weather == "CLOUDY")
            {
                var grey = (r + g + b) / 3f;
                var k = weather == "RAIN" ? 0.7f : 0.45f;
                r = Lerp(r, grey, k);
                g = Lerp(g, grey, k);
                b = Lerp(b, grey * 1.05f, k);
            }
            else if (weather == "HOT")
            {
                r = Lerp(r, 1f, 0.12f * d);
                g = Lerp(g, 0.9f, 0.08f * d);
            }
        }

        /// <summary>Ambient light: bright by day, bluish and dim at night.</summary>
        public static void Ambient(float hour, string weather, out float r, out float g, out float b)
        {
            var d = Daylight(hour) * (weather == "RAIN" ? 0.8f : 1f);
            r = Lerp(0.22f, 0.6f, d);
            g = Lerp(0.24f, 0.6f, d);
            b = Lerp(0.38f, 0.65f, d);
        }

        public static bool Raining(CityNow n) => n != null && n.weather == "RAIN";

        // ---------- shop ----------

        /// <summary>Hint next to a product in the shop ("ฝนตก ขายดี x1.5" / "ฝนตก ขายยาก").</summary>
        public static string ProductHint(CityNow n, string productCode)
        {
            if (n == null) return "";
            var d = n.DemandOf(productCode);
            if (d >= 1.2f) return n.weatherTh + " ขายดี x" + d.ToString("0.#", CultureInfo.InvariantCulture);
            if (d <= 0.8f) return n.weatherTh + " ขายยาก";
            return "";
        }

        // ---------- city growth ----------

        public static string ProjectName(CityProject p)
        {
            if (p == null) return "";
            var t = Texts.T(p.nameKey);
            return t == p.nameKey && !string.IsNullOrEmpty(p.nameTh) ? p.nameTh : t;
        }

        public static float Progress(CityProject p)
        {
            if (p == null || p.cost <= 0) return 0f;
            return p.Done ? 1f : Clamp01(p.progress / (float)p.cost);
        }

        /// <summary>"กำลังสร้าง ป้ายรถเมล์หน้าตลาด 120/3500" or "เมืองสร้างครบทุกอย่างแล้ว!"</summary>
        public static string Banner(CityGrowth g)
        {
            if (g == null) return Texts.T("ui.city");
            var c = g.Current();
            if (c == null) return "เมืองสร้างครบทุกอย่างแล้ว!";
            return "กำลังสร้าง " + ProjectName(c) + " " + c.progress + "/" + c.cost;
        }

        /// <summary>How tall the scaffold stands: 1..4 as the project fills up.</summary>
        public static int ScaffoldStage(CityProject p)
        {
            if (p == null || !p.Building) return 0;
            return 1 + Math.Min(3, (int)(Progress(p) * 4));
        }

        /// <summary>Projects that stand (or are being built) in a zone.</summary>
        public static List<CityProject> InZone(CityGrowth g, string zoneId)
        {
            var list = new List<CityProject>();
            if (g == null) return list;
            foreach (var p in g.projects)
                if (p.zone == zoneId && (p.Done || p.Building)) list.Add(p);
            return list;
        }

        public static string BuiltText(CityGrowthEvent e) =>
            "ทั้งเมืองช่วยกันสร้าง " + e.nameTh + " เสร็จแล้ว! " + e.effectTh;

        private static float Smooth(float t)
        {
            t = Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        private static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);

        private static float Clamp01(float v) => v < 0 ? 0 : v > 1 ? 1 : v;
    }
}
