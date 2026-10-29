using System;
using System.Collections.Generic;
using System.Linq;
using HappyWorld.Core.Api;
using Newtonsoft.Json.Linq;

namespace HappyWorld.Core.World
{
    // ---------- realtime messages (backend arena.gateway.ts) ----------

    public sealed class ArenaFighterInfo
    {
        public string id;
        public string team;
        public bool isBot;
        public float x, y;
        public int hits;
        public bool soaked;
        public string displayName;
        public int level;
        public JToken avatar;
        public int? rating;
    }

    public sealed class ArenaYou
    {
        public string userId;
        public string team;
    }

    public sealed class ArenaField
    {
        public float width = 32, height = 20;
    }

    public sealed class ArenaRules
    {
        public float throwRange = 6;
        public int throwCooldownMs = 800;
        public int hitsToSoak = 4;
    }

    /// <summary>arena:start</summary>
    public sealed class ArenaStart
    {
        public string matchId;
        public ArenaYou you = new ArenaYou();
        public DateTime startsAt;
        public DateTime endsAt;
        public DateTime serverTime;
        public ArenaField field = new ArenaField();
        public ArenaRules rules = new ArenaRules();
        public bool withBots;
        public List<ArenaFighterInfo> players = new List<ArenaFighterInfo>();
    }

    /// <summary>arena:queued</summary>
    public sealed class ArenaQueued
    {
        public int waiting;
        public DateTime botsJoinAt;
    }

    /// <summary>arena:moved</summary>
    public sealed class ArenaMoved
    {
        public string id;
        public float x, y;
    }

    /// <summary>arena:thrown — a balloon flew from `by` to `target`.</summary>
    public sealed class ArenaThrown
    {
        public string by;
        public string target;
        public bool hit;
        public int targetHits;
        public bool soaked;
        public ArenaScore score = new ArenaScore();
    }

    /// <summary>arena:respawn — a soaked player is back at their base (shielded for a moment).</summary>
    public sealed class ArenaRespawn
    {
        public string id;
        public float x, y;
    }

    public sealed class ArenaPlayerResult
    {
        public string userId;
        public string team;
        public string outcome;
        public bool leftEarly;
        public int ratingBefore, ratingAfter, delta;
        public string tier, tierBefore;
        public int rewardGold;
    }

    public sealed class ArenaOutcome
    {
        public string RED, BLUE;
    }

    /// <summary>arena:end</summary>
    public sealed class ArenaEnd
    {
        public string matchId;
        public ArenaScore score = new ArenaScore();
        public ArenaOutcome outcome = new ArenaOutcome();
        public bool saved;
        public List<ArenaPlayerResult> results = new List<ArenaPlayerResult>();
    }

    /// <summary>One fighter as this client draws it: interpolated toward the last server position.</summary>
    public sealed class ArenaFighter
    {
        public string Id, Team, Name;
        public bool IsBot, IsMe;
        public JToken Avatar;
        public float X, Y, TargetX, TargetY;
        public int Hits;
        public bool Soaked, Left;

        public void Step(float dt) => Lerp(ref X, ref Y, TargetX, TargetY, dt);

        internal static void Lerp(ref float x, ref float y, float tx, float ty, float dt, float speed = 10f)
        {
            float dx = tx - x, dy = ty - y;
            var d = (float)Math.Sqrt(dx * dx + dy * dy);
            if (d > 6f || d <= speed * dt)
            {
                x = tx;
                y = ty;
                return;
            }
            x += dx / d * speed * dt;
            y += dy / d * speed * dt;
        }
    }

    /// <summary>
    /// Engine-free model of one Splash Arena match (Phase 13): who is where, the score, the clock and whether my
    /// next balloon can fly. The server decides every hit; this only mirrors its events and picks a sensible
    /// target for the big "throw" button. Unit-tested with .NET.
    /// </summary>
    public sealed class ArenaMatch
    {
        private readonly Dictionary<string, ArenaFighter> _fighters = new Dictionary<string, ArenaFighter>();
        private readonly double _startsAt, _endsAt;
        private double _lastThrowAt = double.NegativeInfinity;

        public string MatchId { get; }
        public string MyId { get; }
        public string MyTeam { get; }
        public bool WithBots { get; }
        public ArenaField Field { get; }
        public ArenaRules Rules { get; }
        public ArenaScore Score { get; private set; } = new ArenaScore();
        public ArenaEnd Result { get; private set; }
        public IEnumerable<ArenaFighter> Fighters => _fighters.Values;
        public ArenaFighter Me => Get(MyId);

        /// <param name="clockNow">the app's monotonic clock (seconds) when arena:start arrived</param>
        public ArenaMatch(ArenaStart s, double clockNow)
        {
            MatchId = s.matchId;
            MyId = s.you.userId;
            MyTeam = s.you.team;
            WithBots = s.withBots;
            Field = s.field ?? new ArenaField();
            Rules = s.rules ?? new ArenaRules();
            // server times become offsets on the app clock: the phone's own date setting never matters
            _startsAt = clockNow + (s.startsAt - s.serverTime).TotalSeconds;
            _endsAt = clockNow + (s.endsAt - s.serverTime).TotalSeconds;
            foreach (var p in s.players)
            {
                _fighters[p.id] = new ArenaFighter
                {
                    Id = p.id,
                    Team = p.team,
                    Name = p.displayName,
                    IsBot = p.isBot,
                    IsMe = p.id == MyId,
                    Avatar = p.avatar,
                    X = p.x,
                    Y = p.y,
                    TargetX = p.x,
                    TargetY = p.y,
                    Hits = p.hits,
                    Soaked = p.soaked,
                };
            }
        }

        public ArenaFighter Get(string id) => id != null && _fighters.TryGetValue(id, out var f) ? f : null;
        public bool IsEnemy(ArenaFighter f) => f != null && f.Team != MyTeam;
        public bool Started(double clock) => clock >= _startsAt;
        public bool Over => Result != null;
        public double SecondsToStart(double clock) => Math.Max(0, _startsAt - clock);
        public double SecondsLeft(double clock) => Math.Max(0, _endsAt - (clock < _startsAt ? _startsAt : clock));
        public double CooldownLeft(double clock) => Math.Max(0, _lastThrowAt + Rules.throwCooldownMs / 1000.0 - clock);

        public void OnMoved(ArenaMoved m)
        {
            var f = Get(m.id);
            if (f == null || f.IsMe) return;
            f.TargetX = m.x;
            f.TargetY = m.y;
        }

        public void OnThrown(ArenaThrown t)
        {
            Score = t.score ?? Score;
            var target = Get(t.target);
            if (target == null) return;
            target.Hits = t.targetHits;
            if (t.soaked) target.Soaked = true;
        }

        public void OnRespawn(ArenaRespawn r)
        {
            var f = Get(r.id);
            if (f == null) return;
            f.Soaked = false;
            f.Hits = 0;
            f.X = f.TargetX = r.x;
            f.Y = f.TargetY = r.y;
        }

        public void OnLeft(string id)
        {
            var f = Get(id);
            if (f != null) f.Left = true;
        }

        public void OnEnd(ArenaEnd e)
        {
            Result = e;
            if (e.score != null) Score = e.score;
        }

        /// <summary>My own position as the local controller reports it (the server checks every step).</summary>
        public void SetMyPosition(float x, float y)
        {
            var me = Me;
            if (me == null) return;
            me.X = me.TargetX = x;
            me.Y = me.TargetY = y;
        }

        public void Step(float dt)
        {
            foreach (var f in _fighters.Values) if (!f.IsMe) f.Step(dt);
        }

        /// <summary>
        /// Who the "throw" button aims at: the preferred target (the enemy I tapped) if still fair game,
        /// else the closest enemy in range who isn't soaked. Null = nobody in range.
        /// </summary>
        public ArenaFighter PickTarget(float x, float y, string preferredId = null)
        {
            bool Fair(ArenaFighter f) => IsEnemy(f) && !f.Soaked && !f.Left && Dist(f, x, y) <= Rules.throwRange;
            var preferred = Get(preferredId);
            if (preferred != null && Fair(preferred)) return preferred;
            return _fighters.Values.Where(Fair).OrderBy(f => Dist(f, x, y)).FirstOrDefault();
        }

        /// <summary>The nearest enemy at all (for the "walk closer" hint).</summary>
        public ArenaFighter NearestEnemy(float x, float y) =>
            _fighters.Values.Where(f => IsEnemy(f) && !f.Left && !f.Soaked).OrderBy(f => Dist(f, x, y)).FirstOrDefault();

        /// <summary>Whether a throw is worth sending now (the server checks it again).</summary>
        public bool CanThrow(double clock) => Started(clock) && !Over && Me != null && !Me.Soaked && CooldownLeft(clock) <= 0;

        public void MarkThrown(double clock) => _lastThrowAt = clock;

        public ArenaPlayerResult MyResult => Result?.results?.FirstOrDefault(r => r.userId == MyId);

        private static float Dist(ArenaFighter f, float x, float y) => (float)Math.Sqrt((f.X - x) * (f.X - x) + (f.Y - y) * (f.Y - y));
    }

    /// <summary>Thai lines for the arena screens (ranks, HUD, results).</summary>
    public static class ArenaPresenter
    {
        public static readonly string[] Tiers = { "BRONZE", "SILVER", "GOLD", "PLATINUM", "DIAMOND", "STAR" };

        public static string TierName(string tier)
        {
            switch (tier)
            {
                case "BRONZE": return "บรอนซ์";
                case "SILVER": return "ซิลเวอร์";
                case "GOLD": return "โกลด์";
                case "PLATINUM": return "แพลทินัม";
                case "DIAMOND": return "ไดมอนด์";
                case "STAR": return "ดาวเด่น";
                default: return tier ?? "";
            }
        }

        public static string TierIcon(string tier)
        {
            switch (tier)
            {
                case "SILVER": return "◆";
                case "GOLD": return "★";
                case "PLATINUM": return "✦";
                case "DIAMOND": return "♦";
                case "STAR": return "✪";
                default: return "●";
            }
        }

        public static string TeamName(string team) => team == "RED" ? "ทีมแดง" : "ทีมฟ้า";

        public static string RankLine(ArenaMe me)
        {
            var line = TierIcon(me.tier) + " <b>" + TierName(me.tier) + "</b>  " + me.rating + " แต้ม  (อันดับ " + me.position + ")";
            line += "\n<size=24>ชนะ " + me.wins + "  เสมอ " + me.draws + "  แพ้ " + me.losses;
            line += me.nextTierAt.HasValue ? "  ·  อีก " + (me.nextTierAt.Value - me.rating) + " แต้มถึง" + TierName(NextTier(me.tier)) : "  ·  ระดับสูงสุดแล้ว!";
            return line + "</size>";
        }

        public static string SeasonLine(ArenaMe me, DateTime nowUtc)
        {
            var days = Math.Max(0, (int)Math.Ceiling((me.seasonEndsAt - nowUtc).TotalDays));
            return "ซีซั่น " + me.season + " · เหลือ " + days + " วัน (ซีซั่นใหม่เก็บแต้มไว้ครึ่งหนึ่ง)";
        }

        public static string RewardLine(ArenaMe me) =>
            me.rewardedMatchesLeft > 0
                ? "วันนี้ยังได้เหรียญอีก " + me.rewardedMatchesLeft + " แมตช์ (ชนะ " + me.rewardGold.WIN + " เสมอ " + me.rewardGold.DRAW + " แพ้ " + me.rewardGold.LOSS + ")"
                : "วันนี้รับเหรียญครบแล้ว เล่นต่อได้ แต้มแรงค์ยังนับนะ";

        public static string NextTier(string tier)
        {
            var i = Array.IndexOf(Tiers, tier);
            return i >= 0 && i + 1 < Tiers.Length ? Tiers[i + 1] : tier;
        }

        public static string QueueLine(ArenaQueued q, DateTime nowUtc)
        {
            var s = Math.Max(0, (int)Math.Ceiling((q.botsJoinAt - nowUtc).TotalSeconds));
            return "กำลังหาทีม... มีผู้เล่นรออยู่ " + q.waiting + " คน" + (s > 0 ? "\n<size=24>อีก " + s + " วิ ถ้ายังไม่ครบ จะมีบอทใจดีมาเล่นด้วย</size>" : "");
        }

        public static string ScoreLine(ArenaMatch m) => "<color=#E25555>แดง " + m.Score.RED + "</color>  :  <color=#3D8BFD>" + m.Score.BLUE + " ฟ้า</color>";

        public static string ClockLine(ArenaMatch m, double clock)
        {
            if (!m.Started(clock)) return "เริ่มใน " + Math.Ceiling(m.SecondsToStart(clock));
            var s = (int)Math.Ceiling(m.SecondsLeft(clock));
            return (s / 60) + ":" + (s % 60).ToString("00");
        }

        public static string HitsLine(ArenaMatch m)
        {
            var me = m.Me;
            if (me == null) return "";
            if (me.Soaked) return "เปียกโชก! รอกลับฐานแป๊บนึง";
            var dry = Math.Max(0, m.Rules.hitsToSoak - me.Hits);
            return "ความแห้ง " + new string('●', dry) + new string('○', m.Rules.hitsToSoak - dry);
        }

        public static string ResultTitle(ArenaMatch m)
        {
            var r = m.MyResult;
            var outcome = r != null ? r.outcome : (m.Result?.outcome != null ? (m.MyTeam == "RED" ? m.Result.outcome.RED : m.Result.outcome.BLUE) : null);
            switch (outcome)
            {
                case "WIN": return "ชนะแล้ว!";
                case "DRAW": return "เสมอกัน";
                default: return "แพ้นิดเดียว ลองใหม่นะ";
            }
        }

        public static List<string> ResultLines(ArenaMatch m)
        {
            var lines = new List<string> { ScoreLine(m) };
            var r = m.MyResult;
            if (r == null)
            {
                lines.Add(m.Result != null && !m.Result.saved ? "บันทึกผลไม่สำเร็จ แต้มแรงค์ไม่เปลี่ยน" : "");
                return lines;
            }
            lines.Add("แต้มแรงค์ " + r.ratingBefore + " → <b>" + r.ratingAfter + "</b> (" + (r.delta >= 0 ? "+" : "") + r.delta + ")");
            if (r.tier != r.tierBefore)
                lines.Add(Array.IndexOf(Tiers, r.tier) > Array.IndexOf(Tiers, r.tierBefore) ? "เลื่อนขั้นเป็น " + TierIcon(r.tier) + " " + TierName(r.tier) + "!" : "ลงไปอยู่ " + TierName(r.tier) + " สู้ใหม่ได้เสมอ");
            if (r.rewardGold > 0) lines.Add("ได้รับ " + r.rewardGold + " Gold");
            if (m.WithBots) lines.Add("<size=24>มีบอทร่วมเล่น แต้มแรงค์ขยับครึ่งเดียว</size>");
            return lines;
        }

        public static string ThrowRefused(string reason)
        {
            switch (reason)
            {
                case "OUT_OF_RANGE": return "ไกลไป เดินเข้าไปใกล้อีกนิด";
                case "COOLDOWN": return "เติมลูกโป่งแป๊บนึง";
                case "SOAKED": return "เปียกอยู่ รอกลับฐานก่อนนะ";
                case "NOT_STARTED": return "ยังไม่เริ่มเลย";
                default: return null;
            }
        }
    }
}
