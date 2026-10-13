using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace HappyWorld.Core.Realtime
{
    /// <summary>
    /// Minimal Socket.IO v5 / Engine.IO v4 client over the WebSocket transport only.
    /// Enough for the backend's /realtime namespace: auth on connect, events, acks, ping/pong.
    ///
    /// Threading: the socket runs on background threads. Event, connect and disconnect callbacks
    /// are queued and only run when the owner calls <see cref="Pump"/> (Unity: from Update()).
    /// Acks complete Tasks directly, so awaiting them never depends on pumping; Unity's
    /// SynchronizationContext resumes the awaiting code on the main thread.
    /// </summary>
    public sealed class SocketIoClient : IDisposable
    {
        private readonly string _url;
        private readonly string _nsp;
        private readonly JObject _auth;
        private readonly ConcurrentQueue<Action> _inbox = new ConcurrentQueue<Action>();
        private readonly Dictionary<string, List<Action<JToken>>> _handlers = new Dictionary<string, List<Action<JToken>>>();
        private readonly ConcurrentDictionary<int, Action<JToken>> _acks = new ConcurrentDictionary<int, Action<JToken>>();
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);
        private ClientWebSocket _ws;
        private CancellationTokenSource _cts;
        private int _nextAckId;

        public bool Connected { get; private set; }

        /// <summary>Namespace connected (after the server accepted the auth token).</summary>
        public event Action OnConnected;
        /// <summary>Server refused the namespace connection, e.g. bad token. Arg: message from the server.</summary>
        public event Action<string> OnConnectError;
        /// <summary>Connection closed for any reason. Arg: reason.</summary>
        public event Action<string> OnDisconnected;

        /// <param name="serverRoot">http(s)://host:port</param>
        /// <param name="path">Socket.IO path, "/ws" on this backend</param>
        /// <param name="nsp">namespace, "/realtime"</param>
        public SocketIoClient(string serverRoot, string path, string nsp, object auth)
        {
            var root = serverRoot.TrimEnd('/');
            if (root.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) root = "wss://" + root.Substring(8);
            else if (root.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) root = "ws://" + root.Substring(7);
            _url = root + "/" + path.Trim('/') + "/?EIO=4&transport=websocket";
            _nsp = nsp;
            _auth = auth == null ? null : JObject.FromObject(auth);
        }

        public void On(string evt, Action<JToken> handler)
        {
            if (!_handlers.TryGetValue(evt, out var list)) _handlers[evt] = list = new List<Action<JToken>>();
            list.Add(handler);
        }

        public async Task ConnectAsync(CancellationToken ct = default(CancellationToken))
        {
            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _ws = new ClientWebSocket();
            await _ws.ConnectAsync(new Uri(_url), _cts.Token).ConfigureAwait(false);
            var loop = Task.Run(() => ReceiveLoop(_cts.Token));
        }

        /// <summary>Runs queued callbacks on the caller's thread. Call every frame.</summary>
        public int Pump()
        {
            int n = 0;
            while (_inbox.TryDequeue(out var a))
            {
                a();
                n++;
            }
            return n;
        }

        public Task EmitAsync(string evt, object payload = null) => SendEventAsync(evt, payload, null);

        /// <summary>Emits and completes with the server's ack payload (or throws TimeoutException).</summary>
        public Task<JToken> EmitWithAckAsync(string evt, object payload = null, int timeoutMs = 8000)
        {
            var tcs = new TaskCompletionSource<JToken>(TaskCreationOptions.RunContinuationsAsynchronously);
            var id = Interlocked.Increment(ref _nextAckId);
            _acks[id] = r => tcs.TrySetResult(r);
            var timer = new CancellationTokenSource(timeoutMs);
            timer.Token.Register(() =>
            {
                if (_acks.TryRemove(id, out _)) tcs.TrySetException(new TimeoutException("No ack for " + evt));
            });
            SendEventAsync(evt, payload, id).ContinueWith(t =>
            {
                if (t.IsFaulted && _acks.TryRemove(id, out _)) tcs.TrySetException(t.Exception.GetBaseException());
            });
            return tcs.Task;
        }

        public async Task CloseAsync()
        {
            if (_ws == null) return;
            try
            {
                if (_ws.State == WebSocketState.Open)
                {
                    await SendRawAsync("41" + _nsp + ",").ConfigureAwait(false);
                    await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch (Exception)
            {
                // already gone
            }
            _cts?.Cancel();
        }

        public void Dispose()
        {
            _cts?.Cancel();
            _ws?.Dispose();
        }

        // ---------- protocol ----------

        private Task SendEventAsync(string evt, object payload, int? ackId)
        {
            var arr = new JArray { evt };
            if (payload != null) arr.Add(payload is JToken jt ? jt : JToken.FromObject(payload));
            return SendRawAsync(SocketIoPacket.EncodeEvent(_nsp, arr, ackId));
        }

        private async Task SendRawAsync(string text)
        {
            var ws = _ws;
            if (ws == null || ws.State != WebSocketState.Open) throw new InvalidOperationException("Socket not open");
            var bytes = Encoding.UTF8.GetBytes(text);
            await _sendLock.WaitAsync().ConfigureAwait(false);
            try
            {
                await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None).ConfigureAwait(false);
            }
            finally
            {
                _sendLock.Release();
            }
        }

        private async Task ReceiveLoop(CancellationToken ct)
        {
            var buf = new byte[16 * 1024];
            var sb = new StringBuilder();
            string reason = "closed";
            try
            {
                while (!ct.IsCancellationRequested && _ws.State == WebSocketState.Open)
                {
                    sb.Clear();
                    WebSocketReceiveResult r;
                    do
                    {
                        r = await _ws.ReceiveAsync(new ArraySegment<byte>(buf), ct).ConfigureAwait(false);
                        if (r.MessageType == WebSocketMessageType.Close) { reason = "server closed"; goto done; }
                        sb.Append(Encoding.UTF8.GetString(buf, 0, r.Count));
                    } while (!r.EndOfMessage);
                    await HandleAsync(sb.ToString()).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                reason = "client closed";
            }
            catch (Exception e)
            {
                reason = "transport error: " + e.Message;
            }
        done:
            ;
            Connected = false;
            foreach (var id in _acks.Keys)
            {
                if (_acks.TryRemove(id, out var cb)) cb(JObject.FromObject(new { ok = false, error = new { code = "DISCONNECTED", message = reason } }));
            }
            _inbox.Enqueue(() => OnDisconnected?.Invoke(reason));
        }

        private async Task HandleAsync(string text)
        {
            var p = SocketIoPacket.Parse(text);
            switch (p.Kind)
            {
                case PacketKind.Open:
                    // Engine.IO open → connect to the namespace with the auth payload
                    await SendRawAsync("40" + _nsp + "," + (_auth == null ? "" : _auth.ToString(Formatting.None))).ConfigureAwait(false);
                    break;
                case PacketKind.Ping:
                    await SendRawAsync("3").ConfigureAwait(false);
                    break;
                case PacketKind.Connect when p.Namespace == _nsp:
                    Connected = true;
                    _inbox.Enqueue(() => OnConnected?.Invoke());
                    break;
                case PacketKind.ConnectError when p.Namespace == _nsp:
                    var msg = (string)p.Data?["message"] ?? "connect_error";
                    _inbox.Enqueue(() => OnConnectError?.Invoke(msg));
                    break;
                case PacketKind.Disconnect when p.Namespace == _nsp:
                    Connected = false;
                    break;
                case PacketKind.Event when p.Namespace == _nsp:
                    var args = (JArray)p.Data;
                    var evt = (string)args[0];
                    var payload = args.Count > 1 ? args[1] : null;
                    _inbox.Enqueue(() => Dispatch(evt, payload));
                    break;
                case PacketKind.Ack when p.Namespace == _nsp && p.AckId.HasValue:
                    if (_acks.TryRemove(p.AckId.Value, out var cb))
                    {
                        var arr = p.Data as JArray;
                        var res = arr != null && arr.Count > 0 ? arr[0] : null;
                        cb(res); // completes a TaskCompletionSource; `await` resumes on the caller's context (Unity main thread)
                    }
                    break;
            }
        }

        private void Dispatch(string evt, JToken payload)
        {
            if (!_handlers.TryGetValue(evt, out var list)) return;
            foreach (var h in list.ToArray()) h(payload);
        }
    }

    public enum PacketKind
    {
        Unknown,
        Open,
        Close,
        Ping,
        Pong,
        Connect,
        Disconnect,
        Event,
        Ack,
        ConnectError,
    }

    /// <summary>Engine.IO v4 + Socket.IO v5 text packet codec (no binary attachments).</summary>
    public struct SocketIoPacket
    {
        public PacketKind Kind;
        public string Namespace;
        public int? AckId;
        public JToken Data;

        public static SocketIoPacket Parse(string text)
        {
            var p = new SocketIoPacket { Kind = PacketKind.Unknown, Namespace = "/" };
            if (string.IsNullOrEmpty(text)) return p;
            switch (text[0])
            {
                case '0': p.Kind = PacketKind.Open; p.Data = text.Length > 1 ? JToken.Parse(text.Substring(1)) : null; return p;
                case '1': p.Kind = PacketKind.Close; return p;
                case '2': p.Kind = PacketKind.Ping; return p;
                case '3': p.Kind = PacketKind.Pong; return p;
                case '4': break; // Engine.IO "message" → Socket.IO packet follows
                default: return p;
            }
            if (text.Length < 2) return p;
            int i = 2;
            switch (text[1])
            {
                case '0': p.Kind = PacketKind.Connect; break;
                case '1': p.Kind = PacketKind.Disconnect; break;
                case '2': p.Kind = PacketKind.Event; break;
                case '3': p.Kind = PacketKind.Ack; break;
                case '4': p.Kind = PacketKind.ConnectError; break;
                default: return p;
            }
            if (i < text.Length && text[i] == '/')
            {
                int comma = text.IndexOf(',', i);
                if (comma < 0) { p.Namespace = text.Substring(i); return p; }
                p.Namespace = text.Substring(i, comma - i);
                i = comma + 1;
            }
            int start = i;
            while (i < text.Length && char.IsDigit(text[i])) i++;
            if (i > start) p.AckId = int.Parse(text.Substring(start, i - start));
            if (i < text.Length) p.Data = JToken.Parse(text.Substring(i));
            return p;
        }

        public static string EncodeEvent(string nsp, JArray args, int? ackId)
        {
            var sb = new StringBuilder("42");
            if (!string.IsNullOrEmpty(nsp) && nsp != "/") sb.Append(nsp).Append(',');
            if (ackId.HasValue) sb.Append(ackId.Value);
            sb.Append(args.ToString(Formatting.None));
            return sb.ToString();
        }
    }
}
