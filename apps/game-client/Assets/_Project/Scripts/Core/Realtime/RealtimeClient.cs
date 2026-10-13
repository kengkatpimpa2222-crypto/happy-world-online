using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HappyWorld.Core.Api;
using Newtonsoft.Json.Linq;

namespace HappyWorld.Core.Realtime
{
    /// <summary>Result of an acked realtime call: {ok:true,...} or {ok:false,error:{code,message}}.</summary>
    public sealed class RealtimeAck
    {
        public bool Ok;
        public string ErrorCode;
        public string ErrorMessage;
        public JObject Body;

        public static RealtimeAck From(JToken t)
        {
            var o = t as JObject ?? new JObject();
            var err = o["error"] as JObject;
            return new RealtimeAck
            {
                Ok = (bool?)o["ok"] ?? false,
                ErrorCode = err != null ? (string)err["code"] : null,
                ErrorMessage = err != null ? (string)err["message"] : null,
                Body = o,
            };
        }
    }

    public sealed class ZoneState
    {
        public string zone;
        public string room;
        public ZonePoint you;
        public List<RemotePlayerInfo> players = new List<RemotePlayerInfo>();
    }

    public sealed class PlayerMoved
    {
        public string userId;
        public float x;
        public float y;
        public int dir;
        public int? seq;
    }

    public sealed class MoveCorrected
    {
        public float x;
        public float y;
        public int? seq;
    }

    public sealed class PresetChat
    {
        public string userId;
        public string displayName;
        public string presetId;
    }

    public sealed class FriendInvite
    {
        public string fromUserId;
        public string displayName;
        public string activity;
        public string zone;
    }

    /// <summary>
    /// Typed wrapper around the backend's /realtime namespace (docs/realtime.md).
    /// Events fire from <see cref="Pump"/>, i.e. on the thread that pumps (Unity main thread).
    /// </summary>
    public sealed class RealtimeClient : IDisposable
    {
        public const string Namespace = "/realtime";
        public const string Path = "/ws";
        private readonly SocketIoClient _io;

        public event Action<string> Ready;               // userId
        public event Action<ZoneState> ZoneState;
        public event Action<RemotePlayerInfo> PlayerJoined;
        public event Action<string> PlayerLeft;          // userId
        public event Action<PlayerMoved> PlayerMoved;
        public event Action<MoveCorrected> MoveCorrected;
        public event Action<PresetChat> PresetChat;
        public event Action<string> FriendOnline;        // userId
        public event Action<string> FriendOffline;       // userId
        public event Action<JToken> FriendRequest;
        public event Action<JToken> FriendAccepted;
        public event Action<FriendInvite> FriendInvited;
        public event Action SessionReplaced;
        public event Action<string> ConnectError;
        public event Action<string> Disconnected;

        public bool Connected => _io.Connected;

        public RealtimeClient(string serverRoot, string accessToken, string path = Path)
        {
            _io = new SocketIoClient(serverRoot, path, Namespace, new { token = accessToken });
            _io.OnConnectError += m => ConnectError?.Invoke(m);
            _io.OnDisconnected += r => Disconnected?.Invoke(r);
            _io.On("ready", p => Ready?.Invoke((string)p?["userId"]));
            _io.On("zone:state", p => ZoneState?.Invoke(p.ToObject<ZoneState>()));
            _io.On("player:joined", p => PlayerJoined?.Invoke(p.ToObject<RemotePlayerInfo>()));
            _io.On("player:left", p => PlayerLeft?.Invoke((string)p?["userId"]));
            _io.On("player:moved", p => PlayerMoved?.Invoke(p.ToObject<PlayerMoved>()));
            _io.On("move:corrected", p => MoveCorrected?.Invoke(p.ToObject<MoveCorrected>()));
            _io.On("chat:preset", p => PresetChat?.Invoke(p.ToObject<PresetChat>()));
            _io.On("friend:online", p => FriendOnline?.Invoke((string)p?["userId"]));
            _io.On("friend:offline", p => FriendOffline?.Invoke((string)p?["userId"]));
            _io.On("friend:request", p => FriendRequest?.Invoke(p));
            _io.On("friend:accepted", p => FriendAccepted?.Invoke(p));
            _io.On("friend:invited", p => FriendInvited?.Invoke(p.ToObject<FriendInvite>()));
            _io.On("session:replaced", _ => SessionReplaced?.Invoke());
        }

        /// <summary>Opens the socket. Completion means the TCP/WebSocket is open; wait for <see cref="Ready"/> before joining a zone.</summary>
        public Task ConnectAsync() => _io.ConnectAsync();

        public int Pump() => _io.Pump();

        public async Task<RealtimeAck> JoinZoneAsync(string zone) =>
            RealtimeAck.From(await _io.EmitWithAckAsync("zone:join", new { zone }).ConfigureAwait(false));

        public async Task<RealtimeAck> LeaveZoneAsync() =>
            RealtimeAck.From(await _io.EmitWithAckAsync("zone:leave").ConfigureAwait(false));

        /// <summary>Fire-and-forget move (no ack), sent at ~10 Hz by MovementLimiter. Server replies with move:corrected only when it rejects.</summary>
        public Task SendMoveAsync(float x, float y, int dir, int seq) => _io.EmitAsync("move", new { x, y, dir, seq });

        public async Task<RealtimeAck> SendPresetAsync(string presetId) =>
            RealtimeAck.From(await _io.EmitWithAckAsync("chat:preset", new { presetId }).ConfigureAwait(false));

        public async Task<RealtimeAck> InviteFriendAsync(string friendId, string activity) =>
            RealtimeAck.From(await _io.EmitWithAckAsync("friend:invite", new { friendId, activity }).ConfigureAwait(false));

        public async Task<RealtimeAck> HeartbeatAsync() =>
            RealtimeAck.From(await _io.EmitWithAckAsync("heartbeat").ConfigureAwait(false));

        public Task CloseAsync() => _io.CloseAsync();

        public void Dispose() => _io.Dispose();
    }
}
