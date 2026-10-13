using System;
using System.Collections.Generic;
using HappyWorld.Core.Api;
using HappyWorld.Core.Realtime;

namespace HappyWorld.Core.World
{
    /// <summary>One remote player as the client sees it, with simple interpolation toward the last server position.</summary>
    public sealed class RemotePlayer
    {
        public string UserId;
        public string DisplayName;
        public int Level;
        public bool IsFriend;
        public Newtonsoft.Json.Linq.JToken Avatar;
        public float X, Y;             // rendered position (server units)
        public float TargetX, TargetY; // last position from the server
        public int Dir;

        /// <summary>Moves the rendered position toward the target. Snaps when far (teleport / zone change).</summary>
        public void Step(float dt, float speed = 10f, float snapDistance = 6f)
        {
            float dx = TargetX - X, dy = TargetY - Y;
            float d = (float)Math.Sqrt(dx * dx + dy * dy);
            if (d > snapDistance || d <= speed * dt)
            {
                X = TargetX;
                Y = TargetY;
                return;
            }
            X += dx / d * speed * dt;
            Y += dy / d * speed * dt;
        }
    }

    /// <summary>
    /// Engine-independent model of the zone the player is in. RealtimeClient events update it;
    /// the Unity layer listens to Added/Removed and reads positions every frame.
    /// </summary>
    public sealed class CityState
    {
        private readonly Dictionary<string, RemotePlayer> _players = new Dictionary<string, RemotePlayer>();
        private readonly HashSet<string> _hidden = new HashSet<string>();

        public string Zone { get; private set; }
        public string Room { get; private set; }
        public string MyUserId { get; set; }
        public IEnumerable<RemotePlayer> Players => _players.Values;
        public int Count => _players.Count;

        public event Action<RemotePlayer> Added;
        public event Action<string> Removed;
        /// <summary>Server rejected our position: put the local player here.</summary>
        public event Action<float, float> Corrected;
        public event Action<string> ZoneChanged;

        public RemotePlayer Get(string userId) => _players.TryGetValue(userId, out var p) ? p : null;

        public void Bind(RealtimeClient rt)
        {
            rt.ZoneState += ApplyZoneState;
            rt.PlayerJoined += Upsert;
            rt.PlayerLeft += Remove;
            rt.PlayerMoved += ApplyMove;
            rt.MoveCorrected += c => Corrected?.Invoke(c.x, c.y);
        }

        public void ApplyZoneState(ZoneState s)
        {
            foreach (var id in new List<string>(_players.Keys)) Remove(id);
            Zone = s.zone;
            Room = s.room;
            ZoneChanged?.Invoke(Zone);
            foreach (var p in s.players) Upsert(p);
        }

        public void Upsert(RemotePlayerInfo info)
        {
            if (info == null || info.userId == MyUserId || _hidden.Contains(info.userId)) return;
            if (_players.TryGetValue(info.userId, out var p))
            {
                p.TargetX = info.x;
                p.TargetY = info.y;
                p.Dir = info.dir;
                return;
            }
            p = new RemotePlayer
            {
                UserId = info.userId,
                DisplayName = info.displayName,
                Level = info.level,
                IsFriend = info.isFriend,
                Avatar = info.avatar,
                X = info.x, Y = info.y, TargetX = info.x, TargetY = info.y,
                Dir = info.dir,
            };
            _players[info.userId] = p;
            Added?.Invoke(p);
        }

        public void ApplyMove(PlayerMoved m)
        {
            if (_players.TryGetValue(m.userId, out var p))
            {
                p.TargetX = m.x;
                p.TargetY = m.y;
                p.Dir = m.dir;
            }
        }

        public void Remove(string userId)
        {
            if (userId != null && _players.Remove(userId)) Removed?.Invoke(userId);
        }

        /// <summary>Hide someone immediately after the local player blocks them (the server also stops sending them).</summary>
        public void Hide(string userId)
        {
            _hidden.Add(userId);
            Remove(userId);
        }

        public void Step(float dt)
        {
            foreach (var p in _players.Values) p.Step(dt);
        }

        public void Clear()
        {
            foreach (var id in new List<string>(_players.Keys)) Remove(id);
            Zone = null;
            Room = null;
        }
    }
}
