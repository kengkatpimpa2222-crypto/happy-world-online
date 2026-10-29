using System;
using System.Linq;
using HappyWorld.Core.Api;
using HappyWorld.Core.Net;
using HappyWorld.Core.World;
using Newtonsoft.Json.Linq;
using Xunit;

namespace HappyWorld.Core.Tests
{
    /// <summary>Phase 13 Splash Arena: parsing the server's messages, the match model and the Thai lines.</summary>
    public class ArenaTests
    {
        // shapes copied from the backend's arena.gateway.ts / arena.service.ts
        private const string StartJson = @"{
          ""matchId"":""9f1c2d3e-0000-4000-8000-000000000001"",
          ""you"":{""userId"":""me"",""team"":""RED""},
          ""startsAt"":""2026-10-29T10:00:03.000Z"",""endsAt"":""2026-10-29T10:02:03.000Z"",""serverTime"":""2026-10-29T10:00:00.000Z"",
          ""field"":{""width"":32,""height"":20},
          ""rules"":{""throwRange"":6,""throwCooldownMs"":800,""hitsToSoak"":4},
          ""withBots"":true,
          ""players"":[
            {""id"":""me"",""team"":""RED"",""isBot"":false,""x"":3,""y"":10,""hits"":0,""soaked"":false,""displayName"":""ต้อง"",""level"":5,""avatar"":{""bodyColor"":""#F5C6A5""},""rating"":1016},
            {""id"":""bot:1"",""team"":""RED"",""isBot"":true,""x"":3,""y"":12,""hits"":0,""soaked"":false,""displayName"":""น้องเป็ดน้ำ"",""level"":1,""avatar"":null,""rating"":null},
            {""id"":""foe"",""team"":""BLUE"",""isBot"":false,""x"":29,""y"":10,""hits"":0,""soaked"":false,""displayName"":""มิว"",""level"":3,""avatar"":{},""rating"":990},
            {""id"":""bot:2"",""team"":""BLUE"",""isBot"":true,""x"":29,""y"":12,""hits"":0,""soaked"":false,""displayName"":""น้องปลาทอง"",""level"":1,""avatar"":null,""rating"":null}]}";

        private const string MeJson = @"{""season"":""2026-10"",""seasonEndsAt"":""2026-10-31T17:00:00.000Z"",""rating"":1080,""peak"":1096,""tier"":""BRONZE"",""tierNameTh"":""บรอนซ์"",
          ""nextTierAt"":1100,""wins"":5,""losses"":2,""draws"":1,""position"":3,""rewardedMatchesLeft"":4,""rewardsResetAt"":""2026-10-29T17:00:00.000Z"",
          ""rewardGold"":{""WIN"":20,""DRAW"":12,""LOSS"":6},
          ""recent"":[{""matchId"":""m1"",""endedAt"":""2026-10-29T10:02:03.000Z"",""team"":""RED"",""outcome"":""WIN"",""leftEarly"":false,""score"":{""RED"":3,""BLUE"":1},""withBots"":true,""soaks"":2,""delta"":8,""rewardGold"":20}]}";

        private static ArenaMatch NewMatch(double clock = 100) => new ArenaMatch(JObject.Parse(StartJson).ToObject<ArenaStart>(), clock);

        [Fact]
        public void Start_message_builds_both_teams_and_runs_on_the_app_clock()
        {
            var m = NewMatch(100);
            Assert.Equal("RED", m.MyTeam);
            Assert.Equal(4, m.Fighters.Count());
            Assert.True(m.Me.IsMe);
            Assert.True(m.Get("bot:2").IsBot);
            Assert.True(m.IsEnemy(m.Get("foe")));
            Assert.False(m.IsEnemy(m.Get("bot:1")));
            Assert.False(m.Started(102.9));
            Assert.True(m.Started(103));
            Assert.Equal(120, m.SecondsLeft(100), 3);       // counting starts at the whistle
            Assert.Equal(60, m.SecondsLeft(163), 3);
            Assert.Equal("เริ่มใน 3", ArenaPresenter.ClockLine(m, 100));
            Assert.Equal("1:00", ArenaPresenter.ClockLine(m, 163));
        }

        [Fact]
        public void Throw_button_picks_the_closest_fair_enemy_in_range_and_prefers_the_tapped_one()
        {
            var m = NewMatch();
            Assert.Null(m.PickTarget(3, 10)); // everyone is far away at the start
            Assert.Equal("foe", m.NearestEnemy(3, 10).Id);

            m.SetMyPosition(25, 10);
            Assert.Equal("foe", m.PickTarget(25, 10).Id);
            Assert.Equal("bot:2", m.PickTarget(25, 10, "bot:2").Id);
            Assert.Equal("foe", m.PickTarget(25, 10, "bot:1").Id); // can't aim at a teammate

            m.OnThrown(new ArenaThrown { by = "me", target = "foe", hit = true, targetHits = 0, soaked = true, score = new ArenaScore { RED = 1 } });
            Assert.Equal("bot:2", m.PickTarget(25, 10).Id); // a soaked player is out of play
            Assert.Equal(1, m.Score.RED);
            Assert.Contains("แดง 1", ArenaPresenter.ScoreLine(m));

            m.OnRespawn(new ArenaRespawn { id = "foe", x = 29, y = 10 });
            Assert.False(m.Get("foe").Soaked);
            m.OnLeft("bot:2");
            Assert.Equal("foe", m.PickTarget(25, 10, "bot:2").Id);
        }

        [Fact]
        public void Cooldown_and_soaking_stop_my_throws_until_the_server_says_so()
        {
            var m = NewMatch(100);
            Assert.False(m.CanThrow(102)); // before the whistle
            Assert.True(m.CanThrow(104));
            m.MarkThrown(104);
            Assert.False(m.CanThrow(104.5));
            Assert.True(m.CanThrow(104.8));

            Assert.Equal("ความแห้ง ●●●●", ArenaPresenter.HitsLine(m));
            m.OnThrown(new ArenaThrown { by = "foe", target = "me", hit = true, targetHits = 3, score = new ArenaScore() });
            Assert.Equal("ความแห้ง ●○○○", ArenaPresenter.HitsLine(m));
            m.OnThrown(new ArenaThrown { by = "foe", target = "me", hit = true, targetHits = 0, soaked = true, score = new ArenaScore { BLUE = 1 } });
            Assert.False(m.CanThrow(110));
            Assert.StartsWith("เปียกโชก", ArenaPresenter.HitsLine(m));
        }

        [Fact]
        public void Others_glide_towards_their_server_position_while_mine_follows_my_controller()
        {
            var m = NewMatch();
            m.OnMoved(new ArenaMoved { id = "foe", x = 28, y = 10 });
            m.OnMoved(new ArenaMoved { id = "me", x = 20, y = 20 }); // my own echo never moves me
            m.Step(0.05f);
            Assert.Equal(28.5f, m.Get("foe").X, 3);
            m.Step(1f);
            Assert.Equal(28f, m.Get("foe").X, 3);
            Assert.Equal(3f, m.Me.X, 3);
        }

        [Fact]
        public void End_message_reports_my_rank_change_and_reward()
        {
            var m = NewMatch();
            var end = JObject.Parse(@"{""matchId"":""9f1c2d3e-0000-4000-8000-000000000001"",""score"":{""RED"":3,""BLUE"":1},""outcome"":{""RED"":""WIN"",""BLUE"":""LOSS""},""saved"":true,
              ""players"":[],""results"":[{""userId"":""me"",""team"":""RED"",""outcome"":""WIN"",""leftEarly"":false,""ratingBefore"":1094,""ratingAfter"":1102,""delta"":8,""tierBefore"":""BRONZE"",""tier"":""SILVER"",""rewardGold"":20}]}");
            m.OnEnd(end.ToObject<ArenaEnd>());
            Assert.True(m.Over);
            Assert.False(m.CanThrow(1000));
            Assert.Equal("ชนะแล้ว!", ArenaPresenter.ResultTitle(m));
            var lines = ArenaPresenter.ResultLines(m);
            Assert.Contains(lines, l => l.Contains("1094 → <b>1102</b> (+8)"));
            Assert.Contains(lines, l => l.Contains("เลื่อนขั้นเป็น") && l.Contains("ซิลเวอร์"));
            Assert.Contains(lines, l => l.Contains("20 Gold"));
            Assert.Contains(lines, l => l.Contains("ครึ่งเดียว"));
        }

        [Fact]
        public void Rank_card_lines()
        {
            var me = HappyWorldApi.Parse<ArenaMe>(new HttpResult { Status = 200, Body = MeJson });
            Assert.Equal(1100, me.nextTierAt);
            Assert.Single(me.recent);
            var line = ArenaPresenter.RankLine(me);
            Assert.Contains("บรอนซ์", line);
            Assert.Contains("อีก 20 แต้มถึงซิลเวอร์", line);
            Assert.Contains("เหลือ 3 วัน", ArenaPresenter.SeasonLine(me, new DateTime(2026, 10, 29, 10, 0, 0, DateTimeKind.Utc)));
            Assert.Contains("อีก 4 แมตช์", ArenaPresenter.RewardLine(me));
            me.rewardedMatchesLeft = 0;
            Assert.Contains("รับเหรียญครบแล้ว", ArenaPresenter.RewardLine(me));
            var q = new ArenaQueued { waiting = 2, botsJoinAt = new DateTime(2026, 10, 29, 10, 0, 15, DateTimeKind.Utc) };
            Assert.Contains("อีก 15 วิ", ArenaPresenter.QueueLine(q, new DateTime(2026, 10, 29, 10, 0, 0, DateTimeKind.Utc)));
        }
    }
}
