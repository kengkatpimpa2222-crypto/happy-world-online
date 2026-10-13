using System.Collections.Generic;
using HappyWorld.Core.Api;
using HappyWorld.Core.Localization;
using HappyWorld.Core.Net;
using HappyWorld.Core.Realtime;
using HappyWorld.Core.World;
using Newtonsoft.Json.Linq;
using Xunit;

namespace HappyWorld.Core.Tests
{
    public class SocketIoPacketTests
    {
        [Fact]
        public void ParsesEngineOpen()
        {
            var p = SocketIoPacket.Parse("0{\"sid\":\"abc\",\"pingInterval\":25000}");
            Assert.Equal(PacketKind.Open, p.Kind);
            Assert.Equal("abc", (string)p.Data["sid"]);
        }

        [Fact]
        public void ParsesNamespacedEvent()
        {
            var p = SocketIoPacket.Parse("42/realtime,[\"player:moved\",{\"userId\":\"u1\",\"x\":3.5,\"y\":4}]");
            Assert.Equal(PacketKind.Event, p.Kind);
            Assert.Equal("/realtime", p.Namespace);
            Assert.Null(p.AckId);
            Assert.Equal("player:moved", (string)p.Data[0]);
            Assert.Equal(3.5, (double)p.Data[1]["x"]);
        }

        [Fact]
        public void ParsesAckWithId()
        {
            var p = SocketIoPacket.Parse("43/realtime,12[{\"ok\":true,\"zone\":\"PLAZA\"}]");
            Assert.Equal(PacketKind.Ack, p.Kind);
            Assert.Equal(12, p.AckId);
            Assert.True((bool)p.Data[0]["ok"]);
        }

        [Fact]
        public void ParsesConnectAndConnectError()
        {
            Assert.Equal(PacketKind.Connect, SocketIoPacket.Parse("40/realtime,{\"sid\":\"x\"}").Kind);
            var err = SocketIoPacket.Parse("44/realtime,{\"message\":\"UNAUTHORIZED\"}");
            Assert.Equal(PacketKind.ConnectError, err.Kind);
            Assert.Equal("UNAUTHORIZED", (string)err.Data["message"]);
        }

        [Fact]
        public void ParsesPingAndThaiPayload()
        {
            Assert.Equal(PacketKind.Ping, SocketIoPacket.Parse("2").Kind);
            var p = SocketIoPacket.Parse("42/realtime,[\"chat:preset\",{\"displayName\":\"น้องแมว\"}]");
            Assert.Equal("น้องแมว", (string)p.Data[1]["displayName"]);
        }

        [Fact]
        public void EncodesEventWithAck()
        {
            var s = SocketIoPacket.EncodeEvent("/realtime", new JArray { "zone:join", new JObject { ["zone"] = "PLAZA" } }, 7);
            Assert.Equal("42/realtime,7[\"zone:join\",{\"zone\":\"PLAZA\"}]", s);
            Assert.Equal("42[\"heartbeat\"]", SocketIoPacket.EncodeEvent("/", new JArray { "heartbeat" }, null));
        }
    }

    public class MovementLimiterTests
    {
        [Fact]
        public void SendsAtMostTenTimesASecond()
        {
            var m = new MovementLimiter();
            m.Reset(30, 30, 60, 60, 0f);
            Assert.False(m.TryMove(30.5f, 30, 0.05f, out _, out _, out _, out _));
            Assert.True(m.TryMove(30.5f, 30, 0.1f, out var x, out _, out var dir, out var seq));
            Assert.Equal(30.5f, x, 3);
            Assert.Equal(2, dir); // +x = east
            Assert.Equal(1, seq);
        }

        [Fact]
        public void ClampsSpeedAndBounds()
        {
            var m = new MovementLimiter();
            m.Reset(30, 30, 60, 60, 0f);
            Assert.True(m.TryMove(40, 30, 0.1f, out var x, out var y, out _, out _));
            Assert.Equal(30.8f, x, 3); // 8 u/s * 0.1 s
            Assert.Equal(30f, y, 3);
            m.Reset(59.9f, 1, 60, 60, 0f);
            Assert.True(m.TryMove(70, -5, 1f, out x, out y, out _, out _));
            Assert.InRange(x, 0f, 60f);
            Assert.InRange(y, 0f, 60f);
        }

        [Fact]
        public void IgnoresTinyMovesAndDoesHeartbeat()
        {
            var m = new MovementLimiter();
            m.Reset(10, 10, 40, 40, 0f);
            Assert.False(m.TryMove(10.01f, 10, 1f, out _, out _, out _, out _));
            Assert.False(m.HeartbeatDue(5f));
            Assert.True(m.HeartbeatDue(21f));
            Assert.False(m.HeartbeatDue(22f));
        }

        [Theory]
        [InlineData(0, 1, 0)]
        [InlineData(1, 1, 1)]
        [InlineData(1, 0, 2)]
        [InlineData(0, -1, 4)]
        [InlineData(-1, 0, 6)]
        [InlineData(-1, 1, 7)]
        public void EightWayDirection(float dx, float dy, int expected)
        {
            Assert.Equal(expected, MovementLimiter.DirFromDelta(dx, dy));
        }
    }

    public class CityStateTests
    {
        private static RemotePlayerInfo P(string id, float x = 1, float y = 1) =>
            new RemotePlayerInfo { userId = id, displayName = id, x = x, y = y };

        [Fact]
        public void ZoneStateReplacesPlayersAndSkipsSelf()
        {
            var c = new CityState { MyUserId = "me" };
            var removed = new List<string>();
            c.Removed += removed.Add;
            c.Upsert(P("old"));
            c.ApplyZoneState(new ZoneState { zone = "MARKET", players = new List<RemotePlayerInfo> { P("a"), P("me") } });
            Assert.Equal("MARKET", c.Zone);
            Assert.Equal(1, c.Count);
            Assert.NotNull(c.Get("a"));
            Assert.Contains("old", removed);
        }

        [Fact]
        public void MovesInterpolateAndSnapWhenFar()
        {
            var c = new CityState();
            c.Upsert(P("a", 0, 0));
            c.ApplyMove(new PlayerMoved { userId = "a", x = 1, y = 0 });
            c.Step(0.05f); // 10 u/s * 0.05 = 0.5
            Assert.Equal(0.5f, c.Get("a").X, 3);
            c.ApplyMove(new PlayerMoved { userId = "a", x = 50, y = 0 });
            c.Step(0.016f);
            Assert.Equal(50f, c.Get("a").X, 3);
        }

        [Fact]
        public void HiddenPlayersStayHidden()
        {
            var c = new CityState();
            c.Upsert(P("bad"));
            c.Hide("bad");
            c.Upsert(P("bad"));
            Assert.Equal(0, c.Count);
        }
    }

    public class ApiParsingTests
    {
        [Fact]
        public void MapsErrorBody()
        {
            var ex = Assert.Throws<ApiException>(() => HappyWorldApiTestHooks.Parse(new HttpResult
            {
                Status = 400,
                Body = "{\"error\":{\"code\":\"INSUFFICIENT_FUNDS\",\"message\":\"Not enough\",\"requestId\":\"r1\"}}",
            }));
            Assert.Equal("INSUFFICIENT_FUNDS", ex.Code);
            Assert.Equal("r1", ex.RequestId);
            Assert.Equal("เงินไม่พอ", Texts.Error(ex.Code));
        }

        [Fact]
        public void NetworkAndHtmlErrors()
        {
            var net = Assert.Throws<ApiException>(() => HappyWorldApiTestHooks.Parse(new HttpResult { Status = 0, TransportError = "refused" }));
            Assert.True(net.IsNetworkError);
            var html = Assert.Throws<ApiException>(() => HappyWorldApiTestHooks.Parse(new HttpResult { Status = 502, Body = "<html>bad gateway</html>" }));
            Assert.Equal("HTTP_502", html.Code);
            Assert.Equal(Texts.T("error.generic"), Texts.Error("HTTP_502"));
        }

        [Fact]
        public void IdempotencyKeyMatchesServerRule()
        {
            Assert.Matches("^[A-Za-z0-9_-]{8,64}$", IdempotencyKeys.New());
        }
    }
}

namespace HappyWorld.Core.Tests
{
    // Core sources are compiled into this assembly, so internal members are reachable.
    internal static class HappyWorldApiTestHooks
    {
        public static JObject Parse(HttpResult r) => HappyWorldApi.Parse<JObject>(r);
    }
}
