# ฐานข้อมูล

Schema: [`apps/backend/prisma/schema.prisma`](../apps/backend/prisma/schema.prisma) · Migration แรก: `apps/backend/prisma/migrations/*_init/migration.sql`

ชื่อตารางเป็น snake_case ตามที่กำหนด (Prisma model ใช้ PascalCase แล้ว `@@map`) PK เป็น UUID ยกเว้น `items.id` และ `quests.id` ที่เป็นรหัสข้อความ (เช่น `furn_wood_table`) เพื่อให้อ่านง่ายใน config และ log

| ตาราง | หน้าที่ | ข้อจำกัดสำคัญ |
|---|---|---|
| users | บัญชี (Guest หรือผูกอีเมลแล้ว), ช่วงอายุ | email unique, soft delete `deleted_at` |
| player_profiles | ชื่อ, เลเวล, XP, avatar, friend code | PK = user_id, friend_code unique |
| game_sessions | refresh token (เก็บเป็น sha256) ต่อ 1 อุปกรณ์ | refresh_token_hash unique |
| player_wallets | ยอด GOLD / GEMS | unique (user_id, currency), **CHECK balance >= 0** |
| wallet_transactions | สมุดบัญชีทุกการเปลี่ยนยอด | unique (user_id, idempotency_key), **CHECK after = before + amount**, **trigger ห้าม UPDATE/DELETE** |
| items | แคตตาล็อก ราคาแก้ได้โดยผู้ดูแล | soft delete |
| player_inventory | ไอเทมที่ผู้เล่นมี | unique (user_id, item_id), CHECK quantity >= 0 |
| player_homes / home_furniture | บ้านและการวางของ (API ใน Phase 5) | unique (home_id, x, y), rotation ∈ {0,90,180,270} |
| businesses / business_inventory | ร้านจำลองและสต็อก | CHECK quantity >= 0, sell_price > 0, level 1–10 |
| quests / quest_progress | นิยามภารกิจและความคืบหน้า | unique (user_id, quest_id) |
| friends | ความเป็นเพื่อน เก็บ 2 แถวต่อคู่ | unique (user_id, friend_id), CHECK ห้ามเป็นตัวเอง |
| friend_requests | คำขอเพื่อน (เก็บประวัติ) | `pair_key` + **partial unique (pair_key) WHERE status='PENDING'**: คู่ละ 1 คำขอที่ค้าง กันคำขอซ้ำและคำขอไขว้ |
| blocked_players | การบล็อก | unique (blocker_id, blocked_id) |
| moderation_reports | รายงานพฤติกรรม | **partial unique (reporter, reported, reason) WHERE status='OPEN'** |
| purchases / purchase_events | การซื้อเงินจริงและประวัติ | unique (provider, purchase_token_hash) |
| subscriptions | VIP / Season Pass | unique (provider, purchase_token_hash) |
| audit_logs | เหตุการณ์ด้านความปลอดภัยและการเงิน | index (actor_user_id, created_at) |

## Migration

- พัฒนา: แก้ schema แล้ว `npx prisma migrate dev --name <ชื่อ>` (ตรวจ SQL ก่อน commit)
- Deploy: `npx prisma migrate deploy` รันซ้ำได้อย่างปลอดภัย ใช้เฉพาะ migration ที่ยังไม่ได้ apply (มี test ยืนยัน)
- ห้ามแก้ไฟล์ migration ที่ deploy ไปแล้ว ให้สร้างไฟล์ใหม่เสมอ
- ข้อมูลอ้างอิง (items, quests) seed ด้วย `npm run db:seed` แบบ upsert: รันทุกครั้งที่ deploy ได้ และ **ไม่ทับราคาที่ผู้ดูแลแก้ไว้**

## Migration ของ Phase 3 (`*_phase3_social`)

- เปลี่ยน unique `(from_user_id, to_user_id)` ของ friend_requests เป็น "คู่ละ 1 คำขอที่ค้าง" เพราะของเดิมทำให้ส่งคำขอใหม่ไม่ได้ตลอดไปหลังถูกปฏิเสธ
- เพิ่ม `pair_key` แบบปลอดภัยกับข้อมูลเดิม: เพิ่มคอลัมน์ → เติมค่าจากข้อมูลเดิม → ตั้ง NOT NULL → ยกเลิกคำขอค้างที่ซ้ำ (ถ้ามี) ก่อนสร้าง unique index
- **Partial index ไม่อยู่ใน schema.prisma** (Prisma ยังไม่รองรับ) แต่ตรวจแล้วว่า `prisma migrate dev` ไม่สร้าง migration ลบมัน ถ้าวันหนึ่ง migration ใหม่มีคำสั่ง `DROP INDEX "friend_requests_one_pending_per_pair"` หรือ `"moderation_reports_one_open_per_reason"` ให้ลบบรรทัดนั้นออก

## Migration ของ Phase 5 (`*_phase5_living_shop`)

- เพิ่มอย่างเดียว ไม่แก้ข้อมูลเดิม: ร้านเดิมทั้งหมดเริ่มเป็น `CLOSED` แผงว่าง `cash_box = 0`
- `businesses`: `shop_status`, `stall_slot` + `market_age_band` (ตำแหน่งแผงในตลาดของ age band นั้น), `cash_box` (เงินจากลูกค้า NPC ที่ยังไม่เก็บ), `settled_tick` (นาทีล่าสุดที่คำนวณลูกค้าแล้ว), `opened_at`, `report_seen_at`, `total_customers`
- `business_inventory`: `shelf_quantity` (บนแผง แยกจาก `quantity` ในคลัง), `on_stall`
- ตารางใหม่ `shop_settlements` = สรุปลูกค้าต่อการคำนวณ 1 ครั้ง ใช้ทำรายงานเมื่อกลับเข้าเกม
- CHECK: `cash_box >= 0`, `shelf_quantity >= 0`, ค่าในสรุปไม่ติดลบ
- **Partial unique index `businesses_one_shop_per_stall`** `(market_age_band, stall_slot) WHERE stall_slot IS NOT NULL` อยู่ใน migration SQL เท่านั้น ตรวจแล้วว่า `prisma migrate diff` ไม่สร้างคำสั่งลบ ถ้า migration ใหม่มี `DROP INDEX "businesses_one_shop_per_stall"` ให้ลบบรรทัดนั้นออก
- เงินจากลูกค้า NPC เข้า `cash_box` ก่อน (ไม่ใช่ wallet) จึงไม่มีแถว ledger จนกว่าผู้เล่นกด "เก็บเงิน" ซึ่งเขียน `wallet_transactions` ประเภท `BUSINESS_SALE`, `reference_type = 'shop-collect'`

## Migration ของ Phase 6 (`*_phase6_city_festival`)

- เพิ่มอย่างเดียว: enum `WalletTransactionType.FESTIVAL_REWARD`, `QuestObjectiveType.FESTIVAL_CONTRIBUTE`
- `festival_city_progress` (PK `festival_key, age_band`): แต้มรวมและจำนวนผู้ช่วยของแต่ละเมือง (แยกช่วงอายุ)
- `festival_contributions` (PK `festival_key, user_id`): แต้มของผู้เล่นแต่ละคน บวกใน transaction เดียวกับการคิดบัญชีร้าน (lock แผงก่อน แล้วค่อยแถวของเมือง ลำดับเดียวกันเสมอ จึงไม่ deadlock)
- `festival_reward_claims` (PK `festival_key, user_id, tier`): กันรับรางวัลซ้ำ เงินรางวัลเขียน `wallet_transactions` ประเภท `FESTIVAL_REWARD`, `reference_type = 'festival'`, `reference_id = '<key>:<tier>'`
- CHECK: แต้มไม่ติดลบ, `tier` 1-3
- ไม่มีตารางปฏิทิน: เทศกาลของแต่ละสัปดาห์คำนวณจากเวลา `festival_key` เช่น `BAKERY-41` = งานขนมอบ สัปดาห์ที่ 41 นับจาก 5 ม.ค. 2026

## Migration ของ Phase 7 (`*_phase7_city_growth`)

- เพิ่มอย่างเดียว ไม่แตะตารางเดิม
- `city_growth` (PK `age_band`): แต้มสร้างเมืองรวมของแต่ละเมือง ทุกชิ้นที่ร้านในตลาดขายได้ = 1 แต้ม บวกใน transaction เดียวกับการคิดบัญชีร้าน (lock แผง → แถวเทศกาล → แถวเมือง ลำดับเดียวกันเสมอ)
- `city_project_completions` (PK `age_band, project_code`): โครงการที่สร้างเสร็จพร้อม `completed_at` ผลกับตลาดเริ่มจากเวลานี้ (การคิดบัญชีย้อนหลังของร้านที่ออฟไลน์จะไม่ได้ผลก่อนเวลาที่สร้างเสร็จ)
- `city_builder_contributions` (PK `project_code, user_id`): แต้มที่ผู้เล่นแต่ละคนช่วยสร้างแต่ละโครงการ ถ้าการขายรอบเดียวคร่อมสองโครงการ แต้มจะถูกแบ่งให้ถูกโครงการ
- CHECK: แต้มไม่ติดลบ
- ไม่มีตารางอากาศหรือรายการโครงการ: อากาศคำนวณจากเวลา รายการโครงการอยู่ใน `packages/shared-contracts/src/city-growth.ts`

## Migration ของ Phase 8 (`*_minigames`)

- เพิ่มอย่างเดียว ไม่แตะตารางเดิม
- `minigame_sessions`: หนึ่งแถวต่อหนึ่งรอบมินิเกม (`seed` ที่ server สุ่ม, `bkk_day` วันเวลาไทยที่เริ่ม ใช้นับโควตารายวัน, คะแนน ดาว ของที่ได้ XP ที่ได้) index (user_id, bkk_day) และ (user_id, game, score)
- CHECK: game ต้องเป็น CATCH/GRILL/POUR, ดาว 0–3, คะแนนและของที่ได้ไม่ติดลบ
- จบรอบล็อก `player_profiles` ของผู้เล่นก่อน แล้วค่อยล็อกแถวรอบ กันการกดจบหลายรอบพร้อมกันจนเกินโควตา 15 รอบ/วัน

## ข้อควรรู้

- `wallet_transactions` ลบไม่ได้โดยออกแบบ การลบบัญชีผู้เล่น (สิทธิ์ตาม PDPA) ให้ทำแบบ anonymize/soft delete (`users.status = DELETED`, ล้างอีเมลและชื่อ) แทนการ hard delete
- ยอดเงินเป็น `Int` (หน่วยเต็ม) ไม่มีทศนิยม ไม่มีปัญหา floating point
