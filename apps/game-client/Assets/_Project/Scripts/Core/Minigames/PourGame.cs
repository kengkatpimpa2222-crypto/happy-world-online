using System;
using System.Collections.Generic;

namespace HappyWorld.Core.Minigames
{
    /// <summary>
    /// "ชงชาไทย": hold to pour tea into the glass (the stream speeds up the longer you hold), let go inside the
    /// green band. Overflowing spills the glass. Each glass has its own band, set by the round's seed.
    /// </summary>
    public sealed class PourGame
    {
        public const float ServeSeconds = 0.7f;

        public readonly List<GameEvent> Events = new List<GameEvent>();
        private readonly SeededRandom _r;
        public float Duration { get; }
        public float Time { get; private set; }
        public int Score { get; private set; }
        public int Cups { get; private set; }
        public int Perfects { get; private set; }
        public float Fill { get; private set; }
        public float Target { get; private set; }
        public float HalfBand { get; private set; }
        public bool Pouring { get; private set; }
        public float ServingUntil { get; private set; }
        private float _holdTime;
        public bool Finished => Time >= Duration;

        public PourGame(int seed, float duration = 40f)
        {
            Duration = duration;
            _r = new SeededRandom(seed);
            NextCup();
        }

        /// <summary>Litres per second (as a share of the glass) after holding for <paramref name="held"/> seconds.</summary>
        public static float Rate(float held) => 0.3f + 0.55f * held;

        private void NextCup()
        {
            Fill = 0f;
            _holdTime = 0f;
            Target = _r.Range(0.55f, 0.85f);
            HalfBand = 0.075f - 0.025f * Math.Min(1f, Time / Duration);
        }

        public void Step(float dt, bool holding)
        {
            Events.Clear();
            if (Finished) return;
            dt = Math.Min(dt, 0.1f);
            Time += dt;
            if (Time < ServingUntil) { Pouring = false; return; }
            if (holding)
            {
                Pouring = true;
                _holdTime += dt;
                Fill += Rate(_holdTime) * dt;
                if (Fill >= 1f)
                {
                    Fill = 1f;
                    Events.Add(new GameEvent { Kind = "spill", Index = Cups });
                    Finish(0);
                }
                return;
            }
            if (Pouring) // finger lifted: judge this glass
            {
                Pouring = false;
                var d = Math.Abs(Fill - Target);
                int pts;
                string kind;
                if (d <= HalfBand)
                {
                    pts = 50 + (int)Math.Round(60 * (1 - d / HalfBand));
                    kind = d <= HalfBand * 0.35f ? "perfect" : "good";
                    if (kind == "perfect") Perfects++;
                }
                else if (Fill < Target) { pts = (int)(25 * Fill / Target); kind = "under"; }
                else { pts = 20; kind = "over"; }
                Events.Add(new GameEvent { Kind = kind, Points = pts, Index = Cups, X = Fill });
                Finish(pts);
            }
        }

        private void Finish(int pts)
        {
            Score += pts;
            Cups++;
            ServingUntil = Time + ServeSeconds;
            NextCup();
        }
    }
}
