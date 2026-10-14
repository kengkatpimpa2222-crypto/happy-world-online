using System;
using System.Linq;
using HappyWorld.Core.Minigames;
using Xunit;
using Xunit.Abstractions;

namespace HappyWorld.Core.Tests
{
    /// <summary>
    /// The mini-games are tuned by playing them with bots: a flawless bot must reach 3 stars but stay under the
    /// server's score cap; a typical player gets 1-2 stars; someone who doesn't touch the screen gets nothing.
    /// </summary>
    public class MinigameTests
    {
        private readonly ITestOutputHelper _out;
        public MinigameTests(ITestOutputHelper output) { _out = output; }
        private static readonly int[] Seeds = Enumerable.Range(1, 40).Select(i => i * 7919).ToArray();
        private const float Dt = 1f / 60f;

        // ---------- catch ----------

        private static int PlayCatch(int seed, Func<CatchGame, float> policy)
        {
            var g = new CatchGame(seed);
            while (!g.Finished) g.Step(Dt, policy(g));
            return g.Score;
        }

        private static float PerfectCatch(CatchGame g)
        {
            // go to the next good item that will land, ignore bad ones
            var next = g.Items.Where(i => !i.Done && !i.Bad && g.Time >= i.SpawnAt - 0.5f).OrderBy(i => (i.Y - CatchGame.BasketLine) / i.Speed + Math.Max(0, i.SpawnAt - g.Time)).FirstOrDefault();
            return next != null ? next.X : 0.5f;
        }

        private static Func<CatchGame, float> HumanCatch(int seed)
        {
            var r = new SeededRandom(seed * 31);
            float target = 0.5f, decideAt = 0f;
            return g =>
            {
                if (g.Time >= decideAt) // looks up every ~0.35 s, aims a bit off, sometimes chases the wrong thing
                {
                    decideAt = g.Time + 0.45f;
                    var next = g.Items.Where(i => i.Live && !i.Done && (!i.Bad || r.Next() < 0.3f)).OrderBy(i => i.Y).FirstOrDefault();
                    if (next != null) target = next.X + r.Range(-0.12f, 0.12f);
                }
                return target;
            };
        }

        [Fact]
        public void CatchIsTunedForStars()
        {
            var perfect = Seeds.Select(s => PlayCatch(s, PerfectCatch)).ToArray();
            var human = Seeds.Select(s => PlayCatch(s, HumanCatch(s))).ToArray();
            var idle = Seeds.Select(s => PlayCatch(s, _ => 0.5f)).ToArray();
            Report("CATCH", perfect, human, idle);
            AssertTuned(MinigameRules.Catch, perfect, human, idle);
        }

        [Fact]
        public void CatchScoresCombosAndResetsOnMisses()
        {
            var g = new CatchGame(42);
            var first = g.Items.First(i => !i.Bad);
            while (!first.Done) g.Step(Dt, first.X);
            Assert.Equal(1, g.Combo);
            Assert.Equal(11, g.Score);
            // the basket can't jump across the field in one frame
            g.Step(Dt, 0f);
            Assert.True(g.BasketX > first.X - CatchGame.BasketSpeedLimit * Dt - 0.001f);
            Assert.Equal(PlayCatch(42, PerfectCatch), PlayCatch(42, PerfectCatch)); // same seed, same round
        }

        // ---------- grill ----------

        private static int PlayGrill(int seed, Action<GrillGame> policy)
        {
            var g = new GrillGame(seed);
            while (!g.Finished)
            {
                g.Step(Dt);
                policy(g);
            }
            return g.Score;
        }

        private static void PerfectGrill(GrillGame g)
        {
            for (int i = 0; i < GrillGame.Slots; i++)
            {
                var s = g.Grill[i];
                if (!s.Busy || s.Burnt || s.Cook(g.Time) >= 1f) g.Tap(i);
            }
        }

        private static Action<GrillGame> HumanGrill(int seed)
        {
            var r = new SeededRandom(seed * 17);
            float nextLook = 0f;
            return g =>
            {
                if (g.Time < nextLook) return;
                nextLook = g.Time + r.Range(0.25f, 0.6f); // glances at the grill now and then, two hands busy
                for (int i = 0; i < GrillGame.Slots; i++)
                {
                    var s = g.Grill[i];
                    if (i == 3 && r.Next() < 0.6f) continue; // tends to forget the far spot
                    var c = s.Cook(g.Time);
                    if (!s.Busy || s.Burnt || c >= 0.85f + r.Range(0f, 0.3f)) g.Tap(i);
                }
            };
        }

        [Fact]
        public void GrillIsTunedForStars()
        {
            var perfect = Seeds.Select(s => PlayGrill(s, PerfectGrill)).ToArray();
            var human = Seeds.Select(s => PlayGrill(s, HumanGrill(s))).ToArray();
            var idle = Seeds.Select(s => PlayGrill(s, g => { })).ToArray();
            Report("GRILL", perfect, human, idle);
            AssertTuned(MinigameRules.Grill, perfect, human, idle);
        }

        [Fact]
        public void GrillRewardsGoldenAndBurnsLateFood()
        {
            var g = new GrillGame(9);
            g.Tap(0);
            g.Step(0.05f);
            g.Tap(0);
            Assert.Equal("raw", g.Events.Last().Kind);
            Assert.True(g.Grill[0].Busy);
            while (g.Grill[0].Cook(g.Time) < 1f) g.Step(Dt);
            g.Tap(0);
            Assert.Equal("perfect", g.Events.Last().Kind);
            Assert.Equal(30, g.Score);
            g.Tap(1);
            while (!g.Grill[1].Burnt) g.Step(Dt);
            g.Tap(1);
            Assert.False(g.Grill[1].Busy);
            Assert.Equal(30, g.Score);
        }

        // ---------- pour ----------

        private static int PlayPour(int seed, Func<PourGame, bool> holding)
        {
            var g = new PourGame(seed);
            while (!g.Finished) g.Step(Dt, holding(g));
            return g.Score;
        }

        private static bool PerfectPour(PourGame g) => g.Time >= g.ServingUntil && g.Fill + PourGame.Rate(0.3f) * Dt < g.Target;

        private static Func<PourGame, bool> HumanPour(int seed)
        {
            var r = new SeededRandom(seed * 13);
            float aim = -1f;
            int cup = -1;
            return g =>
            {
                if (g.Cups != cup) { cup = g.Cups; aim = g.Target + r.Range(-0.1f, 0.08f); } // reaction time error
                return g.Fill < aim;
            };
        }

        [Fact]
        public void PourIsTunedForStars()
        {
            var perfect = Seeds.Select(s => PlayPour(s, PerfectPour)).ToArray();
            var human = Seeds.Select(s => PlayPour(s, HumanPour(s))).ToArray();
            var idle = Seeds.Select(s => PlayPour(s, _ => false)).ToArray();
            Report("POUR", perfect, human, idle);
            AssertTuned(MinigameRules.Pour, perfect, human, idle);
        }

        [Fact]
        public void PourSpillsWhenOverfilled()
        {
            var g = new PourGame(5);
            while (g.Cups == 0) g.Step(Dt, true);
            Assert.Equal(0, g.Score);
        }

        [Fact]
        public void EveryProductHasAGame()
        {
            foreach (var p in new[] { "LEMONADE", "BREAD", "FLOWER", "THAI_TEA", "MEATBALL" }) Assert.NotNull(MinigameRules.ForProduct(p));
            Assert.Equal(0, MinigameRules.Stars("CATCH", 0));
            Assert.Equal(3, MinigameRules.Stars("CATCH", 9999));
        }

        // ---------- helpers ----------

        private void Report(string game, int[] perfect, int[] human, int[] idle)
        {
            _out.WriteLine(game + " perfect min/avg/max " + perfect.Min() + "/" + (int)perfect.Average() + "/" + perfect.Max()
                + "  human " + human.Min() + "/" + (int)human.Average() + "/" + human.Max() + "  idle max " + idle.Max()
                + "  human stars " + string.Join(",", Enumerable.Range(0, 4).Select(st => human.Count(h => MinigameRules.Stars(game, h) == st))));
        }

        private static void AssertTuned(string game, int[] perfect, int[] human, int[] idle)
        {
            Assert.All(perfect, p => Assert.True(p <= MinigameRules.MaxScore(game), "over the server cap: " + p));
            Assert.True(perfect.Count(p => MinigameRules.Stars(game, p) == 3) >= perfect.Length * 0.9, "a flawless round should earn 3 stars");
            Assert.All(idle, p => Assert.Equal(0, MinigameRules.Stars(game, p)));
            var humanStars = human.Select(h => MinigameRules.Stars(game, h)).ToArray();
            Assert.True(humanStars.Count(s => s >= 1 && s <= 2) >= human.Length * 0.7, "a typical player should mostly get 1-2 stars");
        }
    }
}
