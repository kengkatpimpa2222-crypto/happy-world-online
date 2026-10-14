using System;
using System.Collections.Generic;

namespace HappyWorld.Core.Minigames
{
    /// <summary>
    /// "เก็บมะนาวคั้นน้ำ / เก็บดอกไม้": goods fall, the player drags a basket to catch them and dodges dry
    /// leaves and pebbles. Field is 0..1 wide, items fall from y=1 to the basket line. Pure logic, no engine.
    /// </summary>
    public sealed class CatchGame
    {
        public sealed class Item
        {
            public float SpawnAt, X, Speed;
            public bool Bad;
            public int Variant;   // which picture (lemon / ice / sugar, or flower colours)
            public float Y = 1.05f;
            public bool Live, Done;
        }

        public const float BasketLine = 0.12f;
        public const float BasketHalfWidth = 0.085f;
        public const float BasketSpeedLimit = 3.5f; // field widths per second: a finger can't teleport

        public readonly List<Item> Items = new List<Item>();
        public readonly List<GameEvent> Events = new List<GameEvent>();
        public float Duration { get; }
        public float Time { get; private set; }
        public int Score { get; private set; }
        public int Combo { get; private set; }
        public int BestCombo { get; private set; }
        public int Caught { get; private set; }
        public float BasketX { get; private set; } = 0.5f;
        public bool Finished => Time >= Duration;

        public CatchGame(int seed, float duration = 45f)
        {
            Duration = duration;
            var r = new SeededRandom(seed);
            float t = 1.2f, lastX = 0.5f;
            while (t < duration - 1.5f)
            {
                var progress = t / duration;
                var x = Clamp(lastX + r.Range(-0.55f, 0.55f), 0.08f, 0.92f);
                Items.Add(new Item
                {
                    SpawnAt = t,
                    X = x,
                    Bad = r.Next() < 0.22f,
                    Variant = (int)(r.Next() * 3),
                    Speed = 0.45f + 0.5f * progress + r.Range(0f, 0.1f),
                });
                lastX = x;
                t += 0.85f - 0.45f * progress + r.Range(-0.08f, 0.08f);
            }
        }

        /// <summary>The finger wants the basket at <paramref name="x"/>; it follows as fast as allowed.</summary>
        public void Step(float dt, float wantX)
        {
            Events.Clear();
            if (Finished) return;
            dt = Math.Min(dt, 0.1f);
            Time += dt;
            var max = BasketSpeedLimit * dt;
            BasketX = Clamp(BasketX + Clamp(wantX - BasketX, -max, max), 0.05f, 0.95f);
            for (int i = 0; i < Items.Count; i++)
            {
                var it = Items[i];
                if (it.Done || Time < it.SpawnAt) continue;
                it.Live = true;
                it.Y -= it.Speed * dt;
                if (it.Y > BasketLine) continue;
                it.Done = true;
                it.Live = false;
                bool inBasket = Math.Abs(it.X - BasketX) <= BasketHalfWidth;
                if (inBasket && !it.Bad)
                {
                    Combo++;
                    Caught++;
                    BestCombo = Math.Max(BestCombo, Combo);
                    var pts = 10 + Math.Min(Combo, 10);
                    Score += pts;
                    Events.Add(new GameEvent { Kind = "catch", Points = pts, Index = i, X = it.X });
                }
                else if (inBasket)
                {
                    Combo = 0;
                    Events.Add(new GameEvent { Kind = "bad", Index = i, X = it.X });
                }
                else if (!it.Bad)
                {
                    Combo = 0;
                    Events.Add(new GameEvent { Kind = "miss", Index = i, X = it.X });
                }
            }
        }

        private static float Clamp(float v, float a, float b) => v < a ? a : v > b ? b : v;
    }
}
