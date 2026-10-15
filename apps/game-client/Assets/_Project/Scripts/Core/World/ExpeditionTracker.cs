using System;
using HappyWorld.Core.Api;

namespace HappyWorld.Core.World
{
    public enum TripPhase
    {
        /// <summary>No trip (or not loaded yet).</summary>
        None,
        /// <summary>On the way; the countdown is running.</summary>
        Travelling,
        /// <summary>The countdown reached zero; asking the server whether the trip is really back.</summary>
        Checking,
        /// <summary>The server says the trip is back: "ดูผล" can be pressed.</summary>
        Ready,
    }

    /// <summary>
    /// The player's current expedition between server calls (Phase 9.1). The server decides when a trip is over:
    /// the countdown only counts down the server's <c>secondsLeft</c> on the app's own clock (never the phone's
    /// date, which players can change), and when it reaches zero the tracker asks the server once. It never
    /// claims or starts a trip by itself.
    ///
    /// Requests are rare by design: one GET when the countdown ends, one when the app comes back from the
    /// background or reconnects, and retries after a failure that back off from 5 s to a minute. No GET at all
    /// while there is no trip. Times are seconds on a monotonic clock supplied by the caller (Unity:
    /// Time.realtimeSinceStartupAsDouble), so tests can drive it.
    /// </summary>
    public sealed class ExpeditionTracker
    {
        /// <summary>Never ask the server more often than this, whatever it answers.</summary>
        public const double MinCheckGapSeconds = 3;
        public const double FirstRetrySeconds = 5;
        public const double MaxRetrySeconds = 60;

        public ExpeditionTrip Trip { get; private set; }
        public TripPhase Phase { get; private set; }
        /// <summary>True once the server's state has been read at least once this session.</summary>
        public bool Loaded { get; private set; }
        /// <summary>A claim request is on its way: the button stays disabled until it returns.</summary>
        public bool Claiming { get; private set; }
        /// <summary>The last check failed and has not succeeded since.</summary>
        public bool Offline => Failures > 0;
        /// <summary>Checks that failed in a row (0 after any success).</summary>
        public int Failures { get; private set; }
        /// <summary>The last failure was the network (no signal, timeout) rather than a server error.</summary>
        public bool LastFailureWasNetwork { get; private set; }

        /// <summary>The server confirmed a trip is back (also when a trip that ended while the app was closed is first loaded).</summary>
        public event Action<ExpeditionTrip> Arrived;

        private double _syncedAt;
        private int _leftAtSync;
        private double _nextCheckAt;
        private double _lastCheckAt = double.NegativeInfinity;
        private bool _checkInFlight;

        /// <summary>Seconds until the trip is back, by the server's count at the last sync plus the app clock since.</summary>
        public int SecondsLeft(double now)
        {
            if (Trip == null || Phase == TripPhase.Ready) return 0;
            var left = _leftAtSync - (int)Math.Floor(Math.Max(0, now - _syncedAt));
            return left > 0 ? left : 0;
        }

        /// <summary>Seconds until the next automatic retry after a failure (0 when none is waiting).</summary>
        public int RetryIn(double now) => Offline ? Math.Max(0, (int)Math.Ceiling(_nextCheckAt - now)) : 0;

        /// <summary>
        /// The server's view: <c>GET /expeditions</c>'s <c>active</c> (null when there is no trip) or the trip
        /// <c>POST /expeditions/start</c> returned.
        /// </summary>
        public void Apply(ExpeditionTrip active, double now)
        {
            _checkInFlight = false;
            Failures = 0;
            Loaded = true;
            var wasReady = Trip != null && Phase == TripPhase.Ready && active != null && Trip.id == active.id;
            if (active == null || active.claimed)
            {
                Trip = null;
                Phase = TripPhase.None;
                return;
            }
            Trip = active;
            _syncedAt = now;
            _leftAtSync = Math.Max(0, active.secondsLeft);
            if (active.ready)
            {
                Phase = TripPhase.Ready;
                if (!wasReady && Arrived != null) Arrived(active);
                return;
            }
            // not back yet: ask again when the countdown ends (a server that says "1 more second" right at
            // the end still waits for the minimum gap, so a clock mismatch can't turn into a request loop)
            Phase = _leftAtSync > 0 ? TripPhase.Travelling : TripPhase.Checking;
            _nextCheckAt = Math.Max(now + _leftAtSync, _lastCheckAt + MinCheckGapSeconds);
        }

        /// <summary>True when the caller should GET /expeditions now (then call BeginCheck, and Apply or CheckFailed).</summary>
        public bool CheckDue(double now)
        {
            if (_checkInFlight || Claiming || now < _nextCheckAt) return false;
            if (!Loaded) return true;
            return Trip != null && Phase != TripPhase.Ready;
        }

        public void BeginCheck(double now)
        {
            _checkInFlight = true;
            _lastCheckAt = now;
            if (Phase == TripPhase.Travelling && SecondsLeft(now) == 0) Phase = TripPhase.Checking;
        }

        /// <summary>The check failed: keep what we knew and retry later (5, 10, 20, 40, 60, 60 ... seconds).</summary>
        public void CheckFailed(double now, bool network)
        {
            _checkInFlight = false;
            Failures++;
            LastFailureWasNetwork = network;
            var wait = Math.Min(MaxRetrySeconds, FirstRetrySeconds * Math.Pow(2, Failures - 1));
            _nextCheckAt = now + wait;
        }

        /// <summary>
        /// Something may have changed while we weren't looking (back from the background, realtime reconnected,
        /// the explore screen opened, "ลองใหม่" pressed): read the server's state again soon.
        /// </summary>
        public void Resync(double now)
        {
            if (Loaded && Trip == null && !Offline) return; // nothing is travelling; the next start tells us
            _nextCheckAt = Math.Max(now, _lastCheckAt + MinCheckGapSeconds);
        }

        /// <summary>Starts a claim unless one is already running or the server hasn't said the trip is back.</summary>
        public bool TryBeginClaim()
        {
            if (Claiming || Trip == null || Phase != TripPhase.Ready) return false;
            Claiming = true;
            return true;
        }

        /// <summary>The claim returned (a replayed claim too): the trip is done.</summary>
        public void ClaimSucceeded()
        {
            Claiming = false;
            Trip = null;
            Phase = TripPhase.None;
        }

        /// <summary>
        /// The claim failed. Not finished yet (409) or not found (404) means our view was stale: re-read it.
        /// A network failure keeps the trip Ready; pressing "ดูผล" again is safe because claiming is idempotent.
        /// </summary>
        public void ClaimFailed(string errorCode, int status, double now)
        {
            Claiming = false;
            if (status == 404)
            {
                Trip = null;
                Phase = TripPhase.None;
                Loaded = false; // read the whole state again
            }
            else if (errorCode == "EXPEDITION_NOT_FINISHED") Phase = TripPhase.Checking;
            else return;
            _nextCheckAt = Math.Max(now, _lastCheckAt + MinCheckGapSeconds);
        }

        /// <summary>Signed out: forget everything.</summary>
        public void Reset()
        {
            Trip = null;
            Phase = TripPhase.None;
            Loaded = false;
            Claiming = false;
            Failures = 0;
            _checkInFlight = false;
            _nextCheckAt = 0;
            _lastCheckAt = double.NegativeInfinity;
        }
    }
}
