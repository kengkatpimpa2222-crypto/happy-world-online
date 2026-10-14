# Phase 5 — Living Shop: รายงานผล

วันที่ 2026-10-14 · branch `claude/project-thread-3h4ruz` (แตกจาก `phase-4-5-android` 86e4dd6)
แผนงานและการสำรวจระบบเดิม: [phase-5-plan.md](phase-5-plan.md)

## สรุปสั้น

ร้านของผู้เล่นกลายเป็น **แผงในโซนตลาด** ที่มีลูกค้า NPC เดินมาซื้อเองทุกนาที ทั้งตอนออนไลน์และออฟไลน์ (สูงสุด 8 ชั่วโมง)
เงินเข้า "กล่องเงิน" ของร้านก่อน แล้วผู้เล่นกด "เก็บเงิน" เข้ากระเป๋าผ่าน Wallet/Ledger เดิม กลับเข้าเกมจะเห็นรายงาน "ระหว่างที่คุณไม่อยู่"
ทุกอย่าง **ต่อยอดจากของเดิม** (Business, BusinessInventory, WalletService, City zone MARKET, Quests) ไม่ได้สร้างระบบร้าน/เงินชุดใหม่ และ API เดิมยังทำงานครบ (e2e เดิมผ่านทั้งหมด)

## หมายเหตุเรื่องสเปก

ข้อความสเปก Phase 5 ที่ได้รับถูกตัดกลางหัวข้อ 3 (จบที่ "เดินเข้าพื้นที่ตลาด * เลือก")
ส่วนที่เหลือ **ผมตัดสินใจเอง** ได้แก่ พฤติกรรมลูกค้า NPC (4 แบบ งบ/ความชอบ/การต่อราคา), กฎออฟไลน์ 8 ชม., กล่องเงิน+เก็บเงิน, รายงานกลับเข้าเกม, คำแนะนำภาษาไทย, เควสต์ใหม่ 2 อัน และตัวเลขสมดุล
ถ้าสเปกฉบับเต็มต่างจากนี้ บอกได้เลย ปรับได้

## สิ่งที่ทำ

| ส่วน | รายละเอียด |
|---|---|
| ฐานข้อมูล | migration `20261014090000_phase5_living_shop`: ฟิลด์ใหม่ใน `businesses` (สถานะร้าน แผง กล่องเงิน tick ที่คิดแล้ว) และ `business_inventory` (ของบนแผง), ตาราง `shop_settlements`, CHECK constraints, unique index แผงละร้าน |
| จำลองลูกค้า | `living-shop-sim.ts` deterministic: seed = `businessId:tick` → ออนไลน์หรือออฟไลน์ได้ลูกค้าชุดเดียวกัน |
| ความปลอดภัยการเงิน | ปิดบัญชี tick ใต้ `SELECT … FOR UPDATE`, settler ใช้ `SKIP LOCKED`, เก็บเงินผ่าน `WalletService.applyChange` (ledger `BUSINESS_SALE`, idempotency key), เปลี่ยนราคาจะคิดบัญชีที่ค้างก่อนเสมอ (ไม่มีผลย้อนหลัง) |
| API ใหม่ | `PATCH /businesses/:id`, `PUT /:id/shelf`, `POST /:id/status`, `POST /:id/collect`, `GET /:id/report`, `POST /:id/report/ack`, `GET /market` (ดู docs/api.md) |
| Realtime | `shop:updated` (เจ้าของ), `market:activity` และ `market:stall` (ทุกคนในโซนตลาด) |
| เควสต์ | `shop_open_stall` เปิดแผงครั้งแรก, `shop_serve_customers` บริการลูกค้า 15 คน |
| สินค้าใหม่ | ชาไทย (ปลดล็อก Lv2), ลูกชิ้นปิ้ง (Lv3) |
| Unity | ตลาดมีแผงจริงตามตำแหน่งจาก server, NPC เดินเข้า-ยืนพูด-เดินออก พร้อมเหรียญเด้ง, หน้าจัดการร้านใหม่ (วางของ ตั้งราคา เปิด/ปิด/พัก เปลี่ยนชื่อ เก็บเงิน), ปุ่ม "ไปตลาด", ปุ่มกล่องเงินบน HUD, รายงานต้อนรับกลับ |
| ความปลอดภัยเด็ก | แผงแยกตามช่วงอายุ, ชื่อร้านผ่านตัวกรองชื่อเดิม, ผู้เล่นที่ถูกบล็อกไม่แสดงในตลาด, รายงานแผงร้านได้ (context `MARKET_STALL`) |
| QA bot | `scripts/market-bot.js` เปิดแผงจริงแล้วพิมพ์ลูกค้าที่ server ส่งมา |

## คำสั่งที่รันจริงและผล

**ก่อนแก้ (baseline บน 86e4dd6):** backend unit 31/31, e2e 92/92, C# Core 26/26

**หลังแก้:**

| คำสั่ง | ผล |
|---|---|
| `./scripts/test-all.sh` (รอบสุดท้าย) | exit 0 · unit **41/41** (6 suites) · e2e **108/108** (12 suites) |
| e2e โหมด Redis (`REDIS_URL` ตั้งไว้) | 108/108 ผ่าน |
| `npx prisma migrate diff` หลัง migrate | ว่าง (schema ตรงกับ migration) |
| lint + typecheck backend / shared-contracts | ผ่าน |
| `dotnet test` C# Core ใน Docker (.NET 8, `HWO_API_URL` ชี้ backend จริง) | **36/36** ผ่าน (รวม integration กับ API ใหม่) |
| Type-check โค้ดชั้น Unity กับ UnityEngine.Modules 2021.3.33 (NuGet) ใน Docker | คอมไพล์ผ่าน |
| `node scripts/market-bot.js 200` กับ backend ที่รันจริง (settler ทุก 20 วิ) | ลูกค้ามาจริง 5 คนใน ~3 นาที (ซื้อ 3 แพงไป 2), รายได้ 57 กำไร 29, เก็บเงินเข้ากระเป๋า 57 ทอง, ได้คำแนะนำ "ลองลดราคาลงนิดนึง" |

ตัวอย่าง log จาก bot:

```
06:32:00 market:activity slot=0 tick=29865992 STUDENT:LEMONADE:BOUGHTx2
06:33:00 market:activity slot=0 tick=29865993 GRANDMA:LEMONADE:TOO_EXPENSIVEx0 STUDENT:BREAD:TOO_EXPENSIVEx0
06:34:00 market:activity slot=0 tick=29865994 TOURIST:LEMONADE:BOUGHTx2 WORKER:BREAD:BOUGHTx1
06:34:50 collected 57 wallet {"GOLD":297,"GEMS":0}
```

**สมดุลเศรษฐกิจ** (น้ำมะนาว Lv1 ราว 63 ลูกค้า/ชม. คำนวณจาก simulator): ราคา 7 → กำไร 291/ชม., 8 → 421, 10 → 323, 12 → 200, 15 → 62
ราคาที่ดีที่สุดไม่ใช่ถูกสุดหรือแพงสุด ซึ่งคือบทเรียนที่ต้องการ
แผง Lv1 เต็ม (มะนาว 20 + ขนมปัง 20) ได้กำไรราว 240 ทองต่อรอบ

## ปัญหาที่ยังเหลือ / สิ่งที่ยังไม่ได้ทดสอบ

- **ยังไม่ได้คอมไพล์ใน Unity 6 จริงและยังไม่ได้ build APK** (ทำได้แค่ type-check กับ UnityEngine 2021.3 ใน Docker) ต้องเปิดโปรเจกต์ในเครื่อง
- ยังไม่ได้ทดสอบบน emulator หรือมือถือจริง และยังไม่ได้ดูหน้าตา NPC/แผงด้วยตา
- e2e realtime ของตลาดเคยล้ม 1 ครั้งในการรันเต็มชุด ผมทำซ้ำไม่ได้ (รันเต็มอีก 6+ รอบ รวมโหมด Redis ผ่านหมด) จึงปรับเทสต์ให้ทนขึ้นแล้ว แต่ยังไม่รู้สาเหตุแน่ชัด
- `POST /businesses/:id/sell` แบบเดิมยังใช้ได้ (ไม่ทำให้ API เดิมเสีย) แต่ทำเครื่องหมาย deprecated แล้ว และ UI ไม่ใช้แล้ว
- NPC เป็นภาพประกอบฝั่ง client เท่านั้น ผู้เล่นคนอื่นเห็น NPC ของแผงที่ server ส่ง activity มา (สูงสุด 8 คน/นาที/แผง)

## คำสั่งถัดไป (บนเครื่องคุณ)

```bash
git fetch origin && git checkout claude/project-thread-3h4ruz
./scripts/dev-setup.sh                     # npm install, Postgres+Redis, migrate (รวม migration Phase 5), seed
npm run dev                                # backend ที่ http://localhost:3000
./scripts/test-all.sh                      # ควรได้ unit 41/41 · e2e 108/108
node scripts/market-bot.js 180             # ดูลูกค้า NPC แบบสด
```

จากนั้นเปิด `apps/game-client` ใน Unity 6 → รอคอมไพล์ → build APK ตามขั้นตอน Phase 4.5 → ในเกม: เปิดร้าน → วางของบนแผง → เปิดร้าน → กด "ไปตลาด" ดูลูกค้าเดินมาซื้อ
