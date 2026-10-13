using System;

namespace HappyWorld.Core.World
{
    /// <summary>
    /// Decides when to send the local position and keeps it inside what the server accepts
    /// (bounds + MAX_MOVE_SPEED 8 units/s; the server allows ×1.25 + 0.5 for jitter).
    /// Pure logic: the caller gives it the time, so it is unit-testable.
    /// </summary>
    public sealed class MovementLimiter
    {
        public const float MaxSpeed = 8f;           // packages/shared-contracts MAX_MOVE_SPEED
        public const float SendInterval = 0.1f;     // 10 Hz, server limit is 20/s
        public const float MinDelta = 0.05f;
        public const float HeartbeatInterval = 20f; // presence expires after 60 s of silence

        private float _lastX, _lastY, _lastSentAt = -999f, _lastAnyAt = -999f;
        private int _lastDir = -1;
        private int _seq;

        public float Width { get; private set; }
        public float Height { get; private set; }

        public void Reset(float x, float y, float width, float height, float now)
        {
            _lastX = x;
            _lastY = y;
            Width = width;
            Height = height;
            _lastSentAt = now;
            _lastAnyAt = now;
            _lastDir = -1;
        }

        public float ClampX(float x) => Math.Max(0f, Math.Min(Width, x));
        public float ClampY(float y) => Math.Max(0f, Math.Min(Height, y));

        /// <summary>
        /// Returns true when a move should be sent now; outputs the (clamped) position, dir 0..7 and seq.
        /// </summary>
        public bool TryMove(float x, float y, float now, out float sx, out float sy, out int dir, out int seq)
        {
            sx = ClampX(x);
            sy = ClampY(y);
            dir = _lastDir < 0 ? 0 : _lastDir;
            seq = _seq;
            if (now - _lastSentAt < SendInterval) return false;
            float dx = sx - _lastX, dy = sy - _lastY;
            float dist = (float)Math.Sqrt(dx * dx + dy * dy);
            if (dist < MinDelta) return false;

            // never claim more distance than the server allows for the elapsed time
            float dt = Math.Max(0.05f, now - _lastSentAt);
            float allowed = MaxSpeed * dt;
            if (dist > allowed)
            {
                sx = _lastX + dx / dist * allowed;
                sy = _lastY + dy / dist * allowed;
            }
            dir = DirFromDelta(dx, dy);
            seq = ++_seq;
            _lastX = sx;
            _lastY = sy;
            _lastDir = dir;
            _lastSentAt = now;
            _lastAnyAt = now;
            return true;
        }

        /// <summary>Server corrected us: accept its position as the new baseline.</summary>
        public void Corrected(float x, float y, float now)
        {
            _lastX = x;
            _lastY = y;
            _lastSentAt = now;
        }

        public bool HeartbeatDue(float now)
        {
            if (now - _lastAnyAt < HeartbeatInterval) return false;
            _lastAnyAt = now;
            return true;
        }

        /// <summary>8-way direction: 0 = +y (north), clockwise. Server only stores it (0..7).</summary>
        public static int DirFromDelta(float dx, float dy)
        {
            double ang = Math.Atan2(dx, dy); // 0 at +y, positive toward +x
            int d = (int)Math.Round(ang / (Math.PI / 4));
            return ((d % 8) + 8) % 8;
        }

        public static float DirToYawDegrees(int dir) => dir * 45f;
    }
}
