using System;

namespace HappyWorld.Core.Minigames
{
    /// <summary>
    /// Rules shared with the server (packages/shared-contracts/src/minigame.ts): game length, star scores and
    /// the hard score cap. The server decides stars and rewards; the client uses these only to draw the UI.
    /// Keep both files in step (MinigameTests checks the client copy against the bots below).
    /// </summary>
    public static class MinigameRules
    {
        public const string Catch = "CATCH", Grill = "GRILL", Pour = "POUR";

        public static string ForProduct(string productCode)
        {
            switch (productCode)
            {
                case "LEMONADE": case "FLOWER": return Catch;
                case "MEATBALL": case "BREAD": return Grill;
                case "THAI_TEA": return Pour;
                default: return null;
            }
        }

        public static float Duration(string game) => game == Catch ? 45f : game == Grill ? 60f : 40f;

        /// <summary>Score needed for 1, 2 and 3 stars.</summary>
        public static int[] StarScores(string game) =>
            game == Catch ? new[] { 250, 500, 750 } : game == Grill ? new[] { 500, 1100, 2000 } : new[] { 450, 1100, 1700 };

        /// <summary>No honest round can score above this (the server rejects anything higher).</summary>
        public static int MaxScore(string game) => game == Catch ? 2800 : game == Grill ? 6000 : 3000;

        public static int Stars(string game, int score)
        {
            var s = StarScores(game);
            return score >= s[2] ? 3 : score >= s[1] ? 2 : score >= s[0] ? 1 : 0;
        }

        public static string Title(string game, string productCode)
        {
            switch (game)
            {
                case Catch: return productCode == "FLOWER" ? "เก็บดอกไม้ทำช่อ" : "เก็บมะนาวคั้นน้ำ";
                case Grill: return productCode == "BREAD" ? "อบขนมปัง" : "ปิ้งลูกชิ้น";
                case Pour: return "ชงชาไทย";
                default: return game;
            }
        }

        public static string HowTo(string game, string productCode)
        {
            switch (game)
            {
                case Catch: return "ลากตะกร้าซ้าย-ขวา รับ" + (productCode == "FLOWER" ? "ดอกไม้" : "มะนาวและน้ำแข็ง") + "\nหลบใบไม้แห้งกับก้อนหิน รับต่อกันได้คะแนนคอมโบ";
                case Grill: return "แตะเตาว่างเพื่อวาง" + (productCode == "BREAD" ? "แป้ง" : "ไม้ลูกชิ้น") + "\nแตะอีกครั้งตอนเป็นสีทอง ถ้าช้าไปจะไหม้";
                case Pour: return "แตะค้างเพื่อเทชา ยิ่งค้างยิ่งไหลเร็ว\nปล่อยนิ้วให้พอดีแถบสีเขียว ถ้าล้นแก้วจะหก";
                default: return "";
            }
        }
    }

    /// <summary>Small deterministic random (mulberry32) so a round's layout comes from the server's seed.</summary>
    public sealed class SeededRandom
    {
        private uint _s;

        public SeededRandom(int seed) { _s = (uint)seed; }

        public float Next()
        {
            unchecked
            {
                _s += 0x6D2B79F5;
                uint t = _s;
                t = (t ^ (t >> 15)) * (t | 1);
                t ^= t + (t ^ (t >> 7)) * (t | 61);
                return ((t ^ (t >> 14)) >> 8) / 16777216f;
            }
        }

        public float Range(float a, float b) => a + (b - a) * Next();
    }

    /// <summary>Something that happened this frame, for the UI to animate and play sounds.</summary>
    public struct GameEvent
    {
        public string Kind;   // "catch", "bad", "miss", "perfect", "good", "over", "raw", "burnt", "spill", "serve"
        public int Points;
        public int Index;     // item / slot / cup
        public float X;       // where (0..1), for effects
    }
}
