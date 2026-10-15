using System;
using System.Collections.Generic;
using System.Linq;
using HappyWorld.Core.Api;

namespace HappyWorld.Core.World
{
    /// <summary>
    /// The "decorate my home" session (Phase 10), engine-free so it is unit tested with .NET.
    /// The player picks a piece from the bag or picks up a placed one, moves and turns it, and drops it where it
    /// fits; nothing reaches the server until Save, which sends the whole layout (PUT /homes/me/layout) with the
    /// layoutVersion the edit started from. Cancel simply throws the editor away.
    ///
    /// The fit rules mirror packages/shared-contracts/src/home.ts (findLayoutProblem). They only colour the preview
    /// green or red: the server checks again and decides.
    /// </summary>
    public sealed class HomeLayoutEditor
    {
        public const int MaxFurniture = 40;

        public sealed class Piece
        {
            /// <summary>Server id; null for a piece that comes out of the bag on Save.</summary>
            public string Id;
            public string ItemId;
            public int X, Y, Rotation;
            public int FootW = 1, FootH = 1;

            /// <summary>Width and depth on the grid after turning.</summary>
            public int W => Rotation == 90 || Rotation == 270 ? FootH : FootW;
            public int H => Rotation == 90 || Rotation == 270 ? FootW : FootH;

            public bool Covers(int x, int y) => x >= X && x < X + W && y >= Y && y < Y + H;

            public Piece Clone() => (Piece)MemberwiseClone();
        }

        private readonly List<Piece> _pieces = new List<Piece>();
        private readonly Dictionary<string, int> _bag = new Dictionary<string, int>();
        private readonly Dictionary<string, int[]> _footprints = new Dictionary<string, int[]>();
        private Piece _heldOriginal; // where a picked-up piece stood, to put it back on cancel

        public int GridWidth { get; }
        public int GridHeight { get; }
        /// <summary>layoutVersion the edit started from (sent as baseVersion).</summary>
        public int BaseVersion { get; }
        public IReadOnlyList<Piece> Pieces => _pieces;
        /// <summary>The piece in hand (following the cursor/finger), or null.</summary>
        public Piece Held { get; private set; }
        /// <summary>Anything changed since the editor opened.</summary>
        public bool Dirty { get; private set; }

        public HomeLayoutEditor(HomeView home, IEnumerable<FurnitureBagItem> bag)
        {
            GridWidth = home.gridWidth;
            GridHeight = home.gridHeight;
            BaseVersion = home.layoutVersion;
            foreach (var b in bag ?? Enumerable.Empty<FurnitureBagItem>())
            {
                _bag[b.itemId] = b.inBag;
                if (b.footprint != null && b.footprint.Length == 2) _footprints[b.itemId] = b.footprint;
            }
            foreach (var f in home.furniture)
            {
                var fp = f.footprint != null && f.footprint.Length == 2 ? f.footprint : Footprint(f.itemId);
                _footprints[f.itemId] = fp;
                _pieces.Add(new Piece { Id = f.id, ItemId = f.itemId, X = f.x, Y = f.y, Rotation = f.rotation, FootW = fp[0], FootH = fp[1] });
            }
        }

        public int InBag(string itemId) => _bag.TryGetValue(itemId, out var n) ? n : 0;

        public IEnumerable<string> BagItems => _bag.Where(kv => kv.Value > 0).Select(kv => kv.Key);

        public int[] Footprint(string itemId) => _footprints.TryGetValue(itemId, out var fp) ? fp : new[] { 1, 1 };

        /// <summary>Total pieces in the room, counting the one in hand.</summary>
        public int Count => _pieces.Count + (Held != null ? 1 : 0);

        // ---------- picking up ----------

        /// <summary>Takes one piece out of the bag into the hand, near the middle of the room.</summary>
        public bool TakeFromBag(string itemId)
        {
            if (Held != null || InBag(itemId) <= 0 || _pieces.Count >= MaxFurniture) return false;
            _bag[itemId]--;
            var fp = Footprint(itemId);
            Held = new Piece { ItemId = itemId, FootW = fp[0], FootH = fp[1], X = Math.Max(0, (GridWidth - fp[0]) / 2), Y = Math.Max(0, (GridHeight - fp[1]) / 2) };
            _heldOriginal = null;
            Dirty = true;
            return true;
        }

        /// <summary>Picks up the piece standing on a cell (to move, turn or put away).</summary>
        public bool PickUpAt(int x, int y)
        {
            if (Held != null) return false;
            var p = PieceAt(x, y);
            if (p == null) return false;
            _pieces.Remove(p);
            _heldOriginal = p.Clone();
            Held = p;
            return true;
        }

        public Piece PieceAt(int x, int y) => _pieces.LastOrDefault(p => p.Covers(x, y));

        // ---------- the piece in hand ----------

        /// <summary>Moves the held piece so its corner is on (x, y), kept inside the room where possible.</summary>
        public void MoveHeld(int x, int y)
        {
            if (Held == null) return;
            Held.X = Clamp(x, 0, Math.Max(0, GridWidth - Held.W));
            Held.Y = Clamp(y, 0, Math.Max(0, GridHeight - Held.H));
        }

        /// <summary>Turns the held piece 90 degrees clockwise.</summary>
        public void RotateHeld()
        {
            if (Held == null) return;
            Held.Rotation = (Held.Rotation + 90) % 360;
            MoveHeld(Held.X, Held.Y); // a long piece may need to step back inside the walls
        }

        /// <summary>Green or red preview: the held piece fits where it is.</summary>
        public bool HeldFits => Held != null && Fits(Held);

        /// <summary>Puts the held piece down if it fits.</summary>
        public bool Drop()
        {
            if (Held == null || !Fits(Held)) return false;
            if (_heldOriginal == null || _heldOriginal.X != Held.X || _heldOriginal.Y != Held.Y || _heldOriginal.Rotation != Held.Rotation) Dirty = true;
            _pieces.Add(Held);
            Held = null;
            _heldOriginal = null;
            return true;
        }

        /// <summary>Puts the held piece back in the bag (a placed piece is removed from the home on Save).</summary>
        public void PutHeldInBag()
        {
            if (Held == null) return;
            _bag[Held.ItemId] = InBag(Held.ItemId) + 1;
            Held = null;
            _heldOriginal = null;
            Dirty = true;
        }

        /// <summary>Changes its mind: a new piece goes back to the bag, a picked-up one back where it stood.</summary>
        public void CancelHeld()
        {
            if (Held == null) return;
            if (_heldOriginal == null) _bag[Held.ItemId] = InBag(Held.ItemId) + 1;
            else _pieces.Add(_heldOriginal);
            Held = null;
            _heldOriginal = null;
        }

        // ---------- rules ----------

        /// <summary>Inside the room and not on top of another piece.</summary>
        public bool Fits(Piece p)
        {
            if (p.X < 0 || p.Y < 0 || p.X + p.W > GridWidth || p.Y + p.H > GridHeight) return false;
            foreach (var o in _pieces)
            {
                if (ReferenceEquals(o, p)) continue;
                if (p.X < o.X + o.W && o.X < p.X + p.W && p.Y < o.Y + o.H && o.Y < p.Y + p.H) return false;
            }
            return true;
        }

        /// <summary>
        /// First problem in a whole layout, as the server would say it (OUT_OF_BOUNDS, OVERLAP, TOO_MANY), or null.
        /// </summary>
        public static string FindProblem(IReadOnlyList<Piece> pieces, int gridWidth, int gridHeight)
        {
            if (pieces.Count > MaxFurniture) return "TOO_MANY";
            var taken = new HashSet<long>();
            foreach (var p in pieces)
            {
                if (p.Rotation % 90 != 0 || p.Rotation < 0 || p.Rotation >= 360) return "BAD_ROTATION";
                for (int dx = 0; dx < p.W; dx++)
                    for (int dy = 0; dy < p.H; dy++)
                    {
                        int x = p.X + dx, y = p.Y + dy;
                        if (x < 0 || y < 0 || x >= gridWidth || y >= gridHeight) return "OUT_OF_BOUNDS";
                        if (!taken.Add(((long)x << 32) | (uint)y)) return "OVERLAP";
                    }
            }
            return null;
        }

        /// <summary>The body of PUT /homes/me/layout. A piece still in hand is not part of the layout.</summary>
        public List<HomeLayoutPiece> ToRequest() =>
            _pieces.Select(p => new HomeLayoutPiece { id = p.Id, itemId = p.ItemId, x = p.X, y = p.Y, rotation = p.Rotation }).ToList();

        private static int Clamp(int v, int lo, int hi) => v < lo ? lo : v > hi ? hi : v;
    }
}
