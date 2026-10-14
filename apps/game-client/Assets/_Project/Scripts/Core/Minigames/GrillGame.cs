using System;
using System.Collections.Generic;

namespace HappyWorld.Core.Minigames
{
    /// <summary>
    /// "ปิ้งลูกชิ้น / อบขนมปัง": four spots on the grill. Tap an empty spot to put food on; it browns over a few
    /// seconds (each piece at its own pace); tap it while golden. Too early it's raw, too late it burns.
    /// </summary>
    public sealed class GrillGame
    {
        public const int Slots = 4;
        // cook progress c = time on the grill / that piece's golden time
        public const float RawUntil = 0.8f, PerfectFrom = 0.92f, PerfectTo = 1.08f, GoodTo = 1.2f, BurnAt = 1.45f;

        public sealed class Slot
        {
            public bool Busy;
            public float PlacedAt, GoldenAfter;
            public bool Burnt;
            /// <summary>0 = raw … 1 = golden … &gt;1.45 burnt.</summary>
            public float Cook(float now) => Busy ? (now - PlacedAt) / GoldenAfter : 0f;
        }

        public readonly Slot[] Grill = new Slot[Slots];
        public readonly List<GameEvent> Events = new List<GameEvent>();
        private readonly SeededRandom _r;
        public float Duration { get; }
        public float Time { get; private set; }
        public int Score { get; private set; }
        public int Perfects { get; private set; }
        public int Served { get; private set; }
        public int Streak { get; private set; }
        public bool Finished => Time >= Duration;

        public GrillGame(int seed, float duration = 60f)
        {
            Duration = duration;
            _r = new SeededRandom(seed);
            for (int i = 0; i < Slots; i++) Grill[i] = new Slot();
        }

        public void Step(float dt)
        {
            Events.Clear();
            if (Finished) return;
            Time += Math.Min(dt, 0.1f);
            for (int i = 0; i < Slots; i++)
            {
                var s = Grill[i];
                if (s.Busy && !s.Burnt && s.Cook(Time) >= BurnAt)
                {
                    s.Burnt = true;
                    Streak = 0;
                    Events.Add(new GameEvent { Kind = "burnt", Index = i });
                }
            }
        }

        public void Tap(int i)
        {
            if (Finished || i < 0 || i >= Slots) return;
            var s = Grill[i];
            if (!s.Busy)
            {
                s.Busy = true;
                s.Burnt = false;
                s.PlacedAt = Time;
                // pieces speed up a little as the round goes on, so it never gets sleepy
                s.GoldenAfter = _r.Range(2.6f, 4.0f) * (1f - 0.2f * Time / Duration);
                Events.Add(new GameEvent { Kind = "place", Index = i });
                return;
            }
            if (s.Burnt)
            {
                s.Busy = false;
                Events.Add(new GameEvent { Kind = "clear", Index = i });
                return;
            }
            var c = s.Cook(Time);
            if (c < RawUntil)
            {
                Events.Add(new GameEvent { Kind = "raw", Index = i });
                return; // still raw: stays on the grill
            }
            int pts;
            string kind;
            if (c >= PerfectFrom && c <= PerfectTo) { kind = "perfect"; Streak++; Perfects++; pts = 30 + Math.Min(Streak - 1, 3) * 5; }
            else if (c <= GoodTo) { kind = "good"; Streak = 0; pts = 20; }
            else { kind = "over"; Streak = 0; pts = 10; }
            Score += pts;
            Served++;
            s.Busy = false;
            Events.Add(new GameEvent { Kind = kind, Points = pts, Index = i });
        }
    }
}
