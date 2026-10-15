using System.Collections.Generic;

namespace HappyWorld.Core.Localization
{
    /// <summary>
    /// Client-side strings. Thai is the default; English is the fallback. Chinese can be added as a third table.
    /// Keys are the backend's nameKey/titleKey/error codes, so new server content shows its key until translated.
    /// </summary>
    public static class Texts
    {
        public static string Locale = "th";

        private static readonly Dictionary<string, string> Th = new Dictionary<string, string>
        {
            // errors: packages/shared-contracts/src/errors.ts (+ client-only NETWORK_ERROR, DISCONNECTED)
            ["NETWORK_ERROR"] = "เชื่อมต่อเซิร์ฟเวอร์ไม่ได้ ลองใหม่อีกครั้ง",
            ["DISCONNECTED"] = "การเชื่อมต่อหลุด",
            ["VALIDATION_FAILED"] = "ข้อมูลไม่ถูกต้อง",
            ["UNAUTHORIZED"] = "กรุณาเข้าสู่ระบบใหม่",
            ["FORBIDDEN"] = "ทำรายการนี้ไม่ได้",
            ["NOT_FOUND"] = "ไม่พบข้อมูล",
            ["RATE_LIMITED"] = "ทำบ่อยเกินไป รอสักครู่",
            ["INTERNAL_ERROR"] = "เซิร์ฟเวอร์มีปัญหา ลองใหม่ภายหลัง",
            ["INSUFFICIENT_FUNDS"] = "เงินไม่พอ",
            ["ITEM_NOT_AVAILABLE"] = "ไอเท็มนี้ซื้อไม่ได้ตอนนี้",
            ["ITEM_NOT_OWNED"] = "ยังไม่มีไอเท็มนี้",
            ["REFRESH_TOKEN_INVALID"] = "เซสชันหมดอายุ กรุณาเข้าเล่นใหม่",
            ["DISPLAY_NAME_REJECTED"] = "ชื่อนี้ใช้ไม่ได้ (ห้ามมีเบอร์โทร ลิงก์ หรือคำไม่สุภาพ)",
            ["QUEST_NOT_AVAILABLE"] = "ยังเริ่มภารกิจนี้ไม่ได้",
            ["QUEST_ALREADY_STARTED"] = "เริ่มภารกิจนี้แล้ว",
            ["QUEST_NOT_COMPLETED"] = "ภารกิจยังไม่สำเร็จ",
            ["QUEST_ALREADY_CLAIMED"] = "รับรางวัลไปแล้ว",
            ["BUSINESS_MAX_LEVEL"] = "ร้านเลเวลสูงสุดแล้ว",
            ["BUSINESS_STOCK_FULL"] = "สต็อกเต็มแล้ว",
            ["BUSINESS_NOTHING_TO_SELL"] = "ไม่มีสินค้าให้ขาย เติมสินค้าก่อน",
            ["BUSINESS_SALE_COOLDOWN"] = "เพิ่งขายไป รออีกสักครู่",
            ["BUSINESS_PRICE_OUT_OF_RANGE"] = "ราคานี้อยู่นอกช่วงที่ตั้งได้",
            ["BILLING_NOT_CONFIGURED"] = "ยังไม่เปิดการซื้อด้วยเงินจริง",
            ["CANNOT_TARGET_SELF"] = "ทำกับตัวเองไม่ได้",
            ["ALREADY_FRIENDS"] = "เป็นเพื่อนกันอยู่แล้ว",
            ["FRIEND_REQUEST_NOT_ALLOWED"] = "ส่งคำขอถึงผู้เล่นนี้ไม่ได้",
            ["FRIEND_LIMIT_REACHED"] = "รายชื่อเพื่อนเต็มแล้ว",
            ["NOT_FRIENDS"] = "ชวนได้เฉพาะเพื่อน",
            ["FRIEND_REQUEST_CLOSED"] = "คำขอนี้ถูกจัดการไปแล้ว",
            ["PLAYER_OFFLINE"] = "เพื่อนออฟไลน์อยู่",
            ["ZONE_NOT_FOUND"] = "ไม่พบโซนนี้",
            ["NOT_IN_ZONE"] = "ยังไม่ได้เข้าโซน",
            ["MOVE_REJECTED"] = "ตำแหน่งไม่ถูกต้อง",
            ["BUSINESS_PRODUCT_LOCKED"] = "สินค้านี้ยังไม่ปลดล็อก อัปเกรดร้านก่อน",
            ["SHOP_NAME_REJECTED"] = "ชื่อร้านนี้ใช้ไม่ได้ (ห้ามมีเบอร์โทร ลิงก์ หรือคำไม่สุภาพ)",
            ["SHOP_SHELF_FULL"] = "แผงเต็มแล้ว (จำนวนช่องหรือจำนวนชิ้นเกินที่แผงรับได้)",
            ["SHOP_NOT_ENOUGH_STOCK"] = "ของในคลังไม่พอ ซื้อสินค้าเข้าร้านก่อน",
            ["SHOP_SHELF_EMPTY"] = "ยังไม่มีของบนแผง วางสินค้าขึ้นแผงก่อนเปิดร้าน",
            ["SHOP_NOTHING_TO_COLLECT"] = "ลิ้นชักยังว่างอยู่",
            ["FESTIVAL_NOT_FOUND"] = "ไม่พบเทศกาลนี้",
            ["FESTIVAL_CLAIM_EXPIRED"] = "หมดเวลารับรางวัลของเทศกาลนี้แล้ว",
            ["FESTIVAL_TIER_NOT_REACHED"] = "เมืองยังไปไม่ถึงขั้นนี้ ช่วยกันขายของเทศกาลอีกนิด",
            ["FESTIVAL_NOT_ENOUGH_CONTRIBUTION"] = "ขายของเทศกาลให้ครบ 10 ชิ้นก่อน ถึงจะรับรางวัลของเมืองได้",
            ["FESTIVAL_ALREADY_CLAIMED"] = "รับรางวัลขั้นนี้ไปแล้ว",
            ["error.generic"] = "เกิดข้อผิดพลาด ลองใหม่อีกครั้ง",

            // UI
            ["ui.loading"] = "กำลังโหลด...",
            ["ui.connecting"] = "กำลังเชื่อมต่อเซิร์ฟเวอร์...",
            ["ui.play"] = "เริ่มเล่น",
            ["ui.create_character"] = "สร้างตัวละคร",
            ["ui.name"] = "ชื่อในเกม",
            ["ui.color"] = "สีตัวละคร",
            ["ui.age_band"] = "อายุของคุณ",
            ["ui.age.under13"] = "ต่ำกว่า 13 ปี",
            ["ui.age.teen"] = "13–17 ปี",
            ["ui.age.adult"] = "18 ปีขึ้นไป",
            ["ui.save"] = "บันทึก",
            ["ui.close"] = "ปิด",
            ["ui.profile"] = "โปรไฟล์",
            ["ui.friends"] = "เพื่อน",
            ["ui.quests"] = "ภารกิจ",
            ["ui.bag"] = "กระเป๋า",
            ["ui.store"] = "ร้านค้า",
            ["ui.shop"] = "ร้านของฉัน",
            ["ui.chat"] = "ทักทาย",
            ["ui.level"] = "เลเวล",
            ["ui.friend_code"] = "รหัสเพื่อน",
            ["ui.add_friend"] = "เพิ่มเพื่อน",
            ["ui.accept"] = "รับ",
            ["ui.decline"] = "ปฏิเสธ",
            ["ui.block"] = "บล็อก",
            ["ui.report"] = "รายงาน",
            ["ui.start"] = "เริ่ม",
            ["ui.claim"] = "รับรางวัล",
            ["ui.buy"] = "ซื้อ",
            ["ui.sell"] = "เปิดร้านขาย 1 รอบ",
            ["ui.restock"] = "เติมสินค้า",
            ["ui.online"] = "ออนไลน์",
            ["ui.offline"] = "ออฟไลน์",
            ["ui.incoming"] = "คำขอเป็นเพื่อน",
            ["ui.gold"] = "ทอง",
            ["ui.gems"] = "เพชร",
            ["ui.sim_note"] = "ร้านค้านี้ใช้เงินในเกมเท่านั้น ไม่มีการจ่ายเงินจริง",
            ["ui.blocked_done"] = "บล็อกแล้ว",
            ["ui.reported_done"] = "ส่งรายงานแล้ว ขอบคุณที่ช่วยดูแลชุมชน",
            ["ui.request_sent"] = "ส่งคำขอเป็นเพื่อนแล้ว",
            ["ui.now_friends"] = "เป็นเพื่อนกันแล้ว",
            ["ui.session_replaced"] = "บัญชีนี้เข้าเล่นจากอุปกรณ์อื่น",
            ["ui.reconnect"] = "เชื่อมต่อใหม่",
            ["ui.go_market"] = "ไปตลาด",
            ["ui.festival"] = "เทศกาล",
            ["ui.festival_claim"] = "รับรางวัล",
            ["ui.festival_claimed"] = "รับแล้ว",
            ["ui.city"] = "เมืองของเรา",
            ["ui.make"] = "ทำเอง",
            ["MINIGAME_TOO_EARLY"] = "รอบนี้ยังไม่จบ เล่นต่อให้ครบเวลาก่อนนะ",
            ["MINIGAME_EXPIRED"] = "รอบนี้ค้างไว้นานเกินไป ลองเล่นรอบใหม่ได้เลย",
            ["MINIGAME_SCORE_INVALID"] = "คะแนนรอบนี้ไม่ถูกต้อง ลองเล่นใหม่อีกครั้ง",
            // exploring and crafting (Phase 9)
            ["ui.explore"] = "ออกสำรวจ",
            ["ui.crafting"] = "งานฝีมือ",
            ["EXPEDITION_LOCKED"] = "ที่นี่ยังไปไม่ได้ ฝึกเป็นนักสำรวจให้เก่งขึ้นก่อนนะ",
            ["EXPEDITION_IN_PROGRESS"] = "ยังเดินทางอยู่ รอกลับมาก่อนค่อยออกไปใหม่",
            ["EXPEDITION_NOT_FINISHED"] = "ยังเดินทางไม่ถึง รออีกนิดนะ",
            ["RECIPE_LOCKED"] = "สูตรนี้ต้องเป็นช่างฝีมือที่เก่งกว่านี้ก่อน",
            ["NOT_ENOUGH_MATERIALS"] = "วัตถุดิบไม่พอ ออกไปสำรวจหาเพิ่มได้นะ",
            ["PRODUCT_CRAFT_ONLY"] = "สินค้านี้ซื้อไม่ได้ ต้องทำเองจากวัตถุดิบในงานฝีมือ",
            ["NO_MINIGAME_FOR_PRODUCT"] = "สินค้านี้ทำในงานฝีมือ ไม่มีมินิเกม",
            ["explore_first_trip"] = "ออกสำรวจครั้งแรก",
            ["craft_first_batch"] = "ทำงานฝีมือชิ้นแรก",
            ["explore_gather_30"] = "เก็บวัตถุดิบ 30 ชิ้น",
            ["mat_mango"] = "มะม่วง",
            ["mat_coconut"] = "มะพร้าว",
            ["mat_sticky_rice"] = "ข้าวเหนียว",
            ["mat_jasmine"] = "ดอกมะลิ",
            ["mat_marigold"] = "ดาวเรือง",
            ["mat_wild_honey"] = "น้ำผึ้งป่า",
            ["mat_lemongrass"] = "ตะไคร้",
            ["mat_herb"] = "สมุนไพร",
            ["mat_beeswax"] = "ขี้ผึ้ง",
            // city projects (packages/shared-contracts/src/city-growth.ts)
            ["project.park"] = "สวนสาธารณะ",
            ["project.bus_stop"] = "ป้ายรถเมล์หน้าตลาด",
            ["project.school"] = "โรงเรียนประจำเมือง",
            ["project.office"] = "อาคารสำนักงาน",
            ["project.clock_tower"] = "หอนาฬิกากลางเมือง",

            // city festivals (packages/shared-contracts/src/festival.ts)
            ["festival.cool_summer"] = "เทศกาลคลายร้อน",
            ["festival.bakery"] = "งานขนมอบหอมกรุ่น",
            ["festival.flower"] = "เทศกาลดอกไม้บาน",
            ["festival.street_food"] = "ถนนคนเดินของกินเล่น",
            ["festival.lantern"] = "คืนโคมไฟ",
            // Names follow what FestivalDecor shows: flags and the gate are up as soon as the festival starts,
            // so tier 1 adds lanterns and tier 2 the twinkling lights (the old names were one tier off).
            ["festival.tier.1"] = "แขวนโคมไฟทั้งเมือง",
            ["festival.tier.2"] = "ไฟกะพริบส่องสว่าง",
            ["festival.tier.3"] = "พลุเต็มฟ้า",

            // items and quests by id (apps/backend/src/database/seed-data.ts)
            ["outfit_basic_tee"] = "เสื้อยืดพื้นฐาน",
            ["hair_short_brown"] = "ผมสั้นสีน้ำตาล",
            ["furn_wood_chair"] = "เก้าอี้ไม้",
            ["outfit_school_uniform"] = "ชุดนักเรียน",
            ["outfit_thai_silk"] = "ชุดผ้าไหมไทย",
            ["hair_long_black"] = "ผมยาวสีดำ",
            ["acc_straw_hat"] = "หมวกฟาง",
            ["furn_wood_table"] = "โต๊ะไม้",
            ["furn_plant_pot"] = "กระถางต้นไม้",
            ["furn_single_bed"] = "เตียงเดี่ยว",
            ["floor_wood_light"] = "พื้นไม้สีอ่อน",
            ["wall_pastel_blue"] = "ผนังฟ้าพาสเทล",
            ["outfit_star_idol"] = "ชุดไอดอลดาวเด่น",
            ["acc_rainbow_wings"] = "ปีกสายรุ้ง",
            ["furn_sofa_cloud"] = "โซฟาก้อนเมฆ",
            ["theme_songkran"] = "ธีมสงกรานต์",
            ["furn_fest_beach_umbrella"] = "ร่มชายหาดคลายร้อน",
            ["furn_fest_bread_oven"] = "เตาอบขนมปังจิ๋ว",
            ["furn_fest_flower_arch"] = "ซุ้มดอกไม้",
            ["furn_fest_food_cart"] = "รถเข็นของกินเล่น",
            ["furn_fest_lantern"] = "โคมไฟเทศกาล",
            ["newbie_set_profile"] = "ตั้งชื่อและแต่งตัวละคร",
            ["newbie_first_purchase"] = "ซื้อของชิ้นแรก",
            ["newbie_open_shop"] = "เปิดร้านครั้งแรก",
            ["ch1_sell_lemonade"] = "ขายน้ำมะนาว",
            ["biz_first_profit"] = "กำไรครั้งแรก",
            ["biz_upgrade_shop"] = "อัปเกรดร้าน",
            ["home_first_furniture"] = "แต่งบ้านชิ้นแรก",
            ["coop_make_friend"] = "มีเพื่อนคนแรก",
            ["life_budget_buyer"] = "นักช้อปรู้คุณค่า",
            ["shop_open_stall"] = "เปิดแผงในตลาดครั้งแรก",
            ["shop_serve_customers"] = "ขายให้ลูกค้า 15 คน",
            ["festival_first_help"] = "ช่วยเมืองจัดเทศกาล",
            ["life_homemade_goods"] = "ทำของขายเอง 3 รอบ",

            // zones (nameKey from GET /city)
            ["zone.plaza"] = "ลานกลางเมือง",
            ["zone.homes"] = "ย่านบ้าน",
            ["zone.market"] = "ตลาด",
            ["zone.activity"] = "ลานกิจกรรม",
            ["zone.minigame_gate"] = "ประตูมินิเกม",
        };

        private static readonly Dictionary<string, string> En = new Dictionary<string, string>
        {
            ["NETWORK_ERROR"] = "Can't reach the server. Try again.",
            ["INSUFFICIENT_FUNDS"] = "Not enough money",
            ["error.generic"] = "Something went wrong. Try again.",
            ["ui.play"] = "Play",
            ["ui.loading"] = "Loading...",
        };

        public static string T(string key)
        {
            if (string.IsNullOrEmpty(key)) return "";
            var table = Locale == "en" ? En : Th;
            if (table.TryGetValue(key, out var s)) return s;
            if (En.TryGetValue(key, out s)) return s;
            return key;
        }

        /// <summary>Message for an API/realtime error code, falling back to the server's message, then a generic one.</summary>
        public static string Error(string code, string serverMessage = null)
        {
            var table = Locale == "en" ? En : Th;
            if (code != null && table.TryGetValue(code, out var s)) return s;
            return !string.IsNullOrEmpty(serverMessage) ? serverMessage : T("error.generic");
        }
    }
}
