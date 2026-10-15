using System;
using HappyWorld.Core.Api;

namespace HappyWorld.Core.World
{
    /// <summary>
    /// Thai text and grid maths for homes (Phase 10). Codes mirror packages/shared-contracts/src/home.ts.
    /// A home is a grid of cells, CellSize world units each; cell (0,0) is the corner left of the door.
    /// </summary>
    public static class HomePresenter
    {
        public const float CellSize = 1.5f;
        public static readonly string[] Policies = { "PRIVATE", "FRIENDS", "PUBLIC" };

        public static string PolicyName(string policy)
        {
            switch (policy)
            {
                case "PRIVATE": return "ส่วนตัว";
                case "FRIENDS": return "เฉพาะเพื่อน";
                case "PUBLIC": return "ทุกคน";
                default: return policy ?? "";
            }
        }

        public static string PolicyBlurb(string policy)
        {
            switch (policy)
            {
                case "PRIVATE": return "มีแค่คุณที่เข้าบ้านได้";
                case "FRIENDS": return "เพื่อนแวะมาเยี่ยมได้";
                case "PUBLIC": return "ใครในเมืองก็แวะมาชมได้ (ยกเว้นคนที่บล็อกกัน)";
                default: return "";
            }
        }

        public static string Address(HomePlot plot) => plot == null ? "-" : "ถนนที่ " + (plot.street + 1) + " บ้านเลขที่ " + (plot.slot + 1);

        /// <summary>The sign above a house in the district.</summary>
        public static string PlotSign(DistrictPlot p)
        {
            if (p == null || !p.taken) return "ที่ว่าง";
            if (p.isMine) return "บ้านของฉัน";
            if (p.owner == null) return "บ้าน";
            return (p.isFriend ? "♥ " : "") + "บ้าน " + p.owner.displayName;
        }

        /// <summary>The button shown when standing at a door; null when there is nothing to do there.</summary>
        public static string DoorPrompt(DistrictPlot p)
        {
            if (p == null || !p.taken) return null;
            if (p.isMine) return "เข้าบ้าน";
            if (p.owner == null) return null;
            return p.canVisit ? "เยี่ยมบ้าน " + p.owner.displayName : "บ้าน " + p.owner.displayName + " (ยังไม่เปิดให้เยี่ยม)";
        }

        public static string HomeTitle(string ownerName, bool mine) => mine ? "บ้านของฉัน" : "บ้านของ " + ownerName;

        public static string GiftText(int count) => "ได้บ้านหลังแรกแล้ว! มีเฟอร์นิเจอร์ต้อนรับให้ " + count + " ชิ้นในกระเป๋า ลองกด \"จัดบ้าน\" ดูนะ";

        /// <summary>Hint under the decorate toolbar.</summary>
        public static string EditHint(HomeLayoutEditor e)
        {
            if (e == null) return "";
            if (e.Held == null) return "แตะเฟอร์นิเจอร์ในกระเป๋าเพื่อหยิบ หรือแตะของในห้องเพื่อย้าย  (" + e.Count + "/" + HomeLayoutEditor.MaxFurniture + ")";
            return e.HeldFits ? "วางตรงนี้ได้ แตะช่องเดิมอีกครั้งหรือกด \"วาง\"" : "วางตรงนี้ไม่ได้ ชนของอื่นหรือล้นห้อง";
        }

        // ---------- grid <-> world ----------

        /// <summary>Cell under a world point inside the room (may be outside the grid: callers clamp).</summary>
        public static void CellAt(float worldX, float worldZ, out int cx, out int cy)
        {
            cx = (int)Math.Floor(worldX / CellSize);
            cy = (int)Math.Floor(worldZ / CellSize);
        }

        /// <summary>World centre of a piece, for drawing it.</summary>
        public static void PieceCenter(HomeLayoutEditor.Piece p, out float x, out float z)
        {
            x = (p.X + p.W / 2f) * CellSize;
            z = (p.Y + p.H / 2f) * CellSize;
        }

        /// <summary>A plain piece (from GET /homes) in the editor's shape, for drawing outside the editor.</summary>
        public static HomeLayoutEditor.Piece ToPiece(HomePiece f)
        {
            var fp = f.footprint != null && f.footprint.Length == 2 ? f.footprint : new[] { 1, 1 };
            return new HomeLayoutEditor.Piece { Id = f.id, ItemId = f.itemId, X = f.x, Y = f.y, Rotation = f.rotation, FootW = fp[0], FootH = fp[1] };
        }

        /// <summary>Standing near the door inside the room (to walk out).</summary>
        public static bool AtExit(float x, float y, float roomWidth) => Math.Abs(x - roomWidth / 2f) < 1.2f && y < 0.6f;
    }
}
