using System;
using System.Globalization;

namespace HappyWorld.Core.Art
{
    /// <summary>
    /// Which avatar parts (Resources/Art/Models/avatar, built by Tools/art/build_avatars.py) a saved avatar wears.
    /// The server only stores item ids (outfit, hair, accessory), a body colour and, new, a body type; this maps
    /// them to part files, so the realtime city keeps sending the same small avatar JSON as before.
    /// </summary>
    public static class AvatarLooks
    {
        public const string Female = "female";
        public const string Male = "male";

        /// <summary>Skin tones offered in the character creator (light to dark).</summary>
        public static readonly string[] SkinTones = { "#F5C6A5", "#F2C9A0", "#E8B48F", "#E0A47A", "#C98B62", "#B97A56", "#9C6644", "#8D5524" };

        /// <summary>
        /// The saved body type, or for avatars saved before it existed a stable pick from the user id, so an old
        /// account always looks the same to everyone until its owner chooses.
        /// </summary>
        public static string BodyType(string saved, string userId)
        {
            if (saved == Female || saved == Male) return saved;
            uint h = 2166136261;
            foreach (var c in userId ?? "") h = (h ^ c) * 16777619;
            return (h & 1) == 0 ? Female : Male;
        }

        public static string Base(string body) => "avatar/" + body;

        public static string Outfit(string body, string outfitItemId)
        {
            string id;
            switch (outfitItemId)
            {
                case "outfit_basic_tee": id = body == Female ? "basic_tee" : "casual"; break;
                case "outfit_school_uniform": id = "school_uniform"; break;
                case "outfit_thai_silk": id = "thai_silk"; break;
                case "outfit_star_idol": id = "star_idol"; break;
                default: id = "casual"; break; // nothing worn / an item without its own look yet
            }
            return "avatar/" + body + "_outfit_" + id;
        }

        public static string Hair(string body, string hairItemId)
        {
            string id;
            if (body == Female)
                id = hairItemId == "hair_long_black" ? "long_black" : hairItemId == "hair_short_brown" ? "short_brown" : "bob_black";
            else
                id = hairItemId == "hair_long_black" ? "messy_black" : "short_brown";
            return "avatar/" + body + "_hair_" + id;
        }

        /// <summary>Hat part for an accessory, or null when the accessory has no 3D look (or none is worn).</summary>
        public static string Hat(string body, string accessoryItemId) =>
            accessoryItemId == "acc_straw_hat" ? "avatar/" + body + "_hat_straw" : null;

        /// <summary>
        /// Multiplier for the skin texture so it shows the chosen tone (the textures are painted at SkinTones[0]).
        /// Colours that are not skin tones (the old creator also offered blue, green...) keep the painted skin.
        /// </summary>
        public static void SkinTint(string hex, out float r, out float g, out float b)
        {
            r = g = b = 1f;
            float cr, cg, cb;
            if (!Parse(hex, out cr, out cg, out cb)) return;
            if (!(cr >= cg && cg >= cb && cr - cb > 0.08f)) return; // skin is warm: red >= green >= blue
            const float br = 245f / 255f, bg = 198f / 255f, bb = 165f / 255f;
            r = Clamp(cr / br);
            g = Clamp(cg / bg);
            b = Clamp(cb / bb);
        }

        private static float Clamp(float v) => Math.Max(0.3f, Math.Min(1.08f, v));

        private static bool Parse(string hex, out float r, out float g, out float b)
        {
            r = g = b = 0;
            if (hex == null || hex.Length != 7 || hex[0] != '#') return false;
            int v;
            if (!int.TryParse(hex.Substring(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v)) return false;
            r = ((v >> 16) & 255) / 255f;
            g = ((v >> 8) & 255) / 255f;
            b = (v & 255) / 255f;
            return true;
        }
    }
}
