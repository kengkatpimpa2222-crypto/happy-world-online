# Phase 5 Plan: Living Shop (ร้านมีชีวิต)

วันที่: 14 ต.ค. 2026 · ต่อจาก branch `phase-4-5-android` (`86e4dd6`)

## 1. สิ่งที่ตรวจแล้วก่อนเริ่ม

### Baseline (รันจริงในเครื่อง cloud ก่อนแก้โค้ด)

| คำสั่ง | ผล |
|---|---|
| `npm run lint` | ✅ ผ่าน |
| `npm run typecheck` | ✅ ผ่าน |
| `npm test` (unit) | ✅ 31/31 (5 suites) |
| `npm run test:e2e` (PostgreSQL 16 จริง, ฐาน `happy_world_test`) | ✅ 92/92 (11 suites) |
| `scripts/test-game-client.sh` (.NET 8 ใน Docker, ไม่ได้ตั้ง `HWO_API_URL`) | ✅ 26/26 |

### ระบบที่มีอยู่และจะต่อยอด (ไม่สร้างซ้ำ)

| ระบบ | ที่อยู่ | สิ่งที่พบ |
|---|---|---|
| ร้านค้า (Shop entity) | `businesses`, `business_inventory`, `src/business/*` | ผู้เล่นทุกคนได้ร้าน 1 ร้านตอนสมัคร (`NewPlayerService`), มี restock / ตั้งราคา / ขาย 1 รอบ / อัปเกรด, ล็อกแถวด้วย `SELECT … FOR UPDATE` |
| สต็อกสินค้า | `business_inventory.quantity` + `avg_unit_cost` (ต้นทุนเฉลี่ยถ่วงน้ำหนัก) | เป็น "คลังสินค้า" ของร้าน ซื้อเข้าด้วย Gold ผ่าน restock |
| สินค้า | `BUSINESS_PRODUCTS` ใน `packages/shared-contracts/src/game-config.ts` | น้ำมะนาว ขนมปัง ดอกไม้ |
| เงิน | `WalletService.applyChange()` เป็นทางเดียวที่แก้ยอดเงิน, ledger `wallet_transactions` append-only + idempotency key ต่อผู้เล่น, CHECK `balance >= 0` | ใช้ต่อทั้งหมด ไม่แตะโค้ด wallet |
| Inventory ไอเทมผู้เล่น | `player_inventory` (ชุด ผม เฟอร์นิเจอร์) | เป็นของตกแต่ง ไม่ใช่สินค้าขาย จึงไม่ใช้กับแผงร้าน |
| ภารกิจ | `QuestsService.recordEvent()` ใน transaction เดียวกับการกระทำ | เพิ่ม objective ใหม่ 2 แบบ |
| แผนที่/โซน | `CITY_ZONES` (มีโซน `MARKET` 40×40 แต่ว่างเปล่า), ห้องแยกตาม zone + age band + shard | ใช้วางแผงร้าน |
| Realtime | `RealtimeService.notifyUser()`, Socket.IO namespace `/realtime` | เพิ่ม event ตลาด |
| Auth | JWT guard ทั่วระบบ + `@CurrentUser()`, ทุก query ร้านกรอง `user_id` (404 เหมือนกันทั้ง "ไม่มี" และ "ของคนอื่น") | ใช้ต่อ |
| Transaction | `PrismaService.tx()` retry เมื่อ P2002/P2034 | ใช้ต่อ |
| ความปลอดภัยเด็ก | `canInteract()` = age band เดียวกันเท่านั้น, block สองทาง | ตลาดแสดงเฉพาะแผงของ age band เดียวกันและไม่แสดงคนที่ block |

## 2. แนวคิดการออกแบบ

**คลัง → แผง → ลูกค้า → ลิ้นชักเงิน → กระเป๋า**

1. ผู้เล่นซื้อสินค้าเข้าคลังเหมือนเดิม (restock)
2. เลือกสินค้าจากคลังขึ้นแผง กำหนดจำนวนและราคาต่อสินค้า (จำนวนช่องบนแผงตามเลเวลร้าน)
3. เปิดร้าน → ได้ตำแหน่งแผงในโซนตลาด
4. **ลูกค้า NPC** มาตามเวลาจริงเป็น "ติ๊ก" ละ 60 วินาที ทั้งตอนออนไลน์และออฟไลน์ แต่ละคนมีประเภท งบประมาณ และของที่อยากได้ ถ้าราคาไม่เกินงบก็ซื้อ ถ้าแพงเกินเดินหนี ถ้าของหมดหรือร้านไม่มีของที่อยากได้ก็เดินผ่าน
5. เงินขายเข้า **ลิ้นชักเงิน (cash box)** ของร้าน ผู้เล่นกด "เก็บเงิน" เพื่อย้ายเข้ากระเป๋าผ่าน ledger เดิม
6. กลับเข้าเกมเจอ **รายงานสรุป** ช่วงที่ไม่อยู่: ลูกค้ากี่คน ซื้อกี่คน กำไร ขายดีสุด และลูกค้าที่เสียไปเพราะแพง/ของหมด/ไม่มีของ พร้อมคำแนะนำภาษาไทย

**ทำไมใช้ติ๊กแบบ deterministic:** ผลของติ๊กที่ T คำนวณจาก seed `(businessId, T)` จึงได้ผลเดิมเสมอไม่ว่าจะคำนวณตอนไหนหรือจาก instance ไหน การคำนวณย้อนหลังตอนออฟไลน์จึงเท่ากับตอนออนไลน์ทุกประการ และ `settled_tick` ที่ล็อกแถวไว้ทำให้แต่ละติ๊กถูกคิดครั้งเดียว ไม่มีทางได้เงินซ้ำ

**ป้องกันโกง:** ทุก mutation ที่มีผลกับยอดขาย (ตั้งราคา, เติมของ, ปรับแผง, อัปเกรด, เปิด/ปิด, เก็บเงิน) จะ "settle" ติ๊กที่ค้างอยู่ด้วยสถานะเดิมก่อนเปลี่ยนค่าใด ๆ ภายใต้ row lock เดียวกัน การขึ้นราคาไม่มีผลย้อนหลัง

## 3. ไฟล์ที่จะเพิ่ม/แก้

### shared-contracts
| ไฟล์ | เปลี่ยน |
|---|---|
| `game-config.ts` | เพิ่มสินค้า ชาไทย (ปลดล็อกร้านเลเวล 2) และลูกชิ้นปิ้ง (เลเวล 3) พร้อม `unlockLevel`; เพิ่ม `shelfSlots`, `shelfCapacity`, `customersPerMinute` ใน `BUSINESS_LEVELS`; ค่าคงที่ `SHOP_TICK_SECONDS`, `SHOP_MAX_OFFLINE_TICKS`, ประเภทลูกค้า `NPC_CUSTOMER_TYPES`, ตำแหน่งแผง `MARKET_STALL_SLOTS` + `marketStallPosition()` |
| `schemas.ts` | `shopNameSchema`, `renameBusinessSchema`, `setShelfSchema`, `setShopStatusSchema` |
| `errors.ts` | `BUSINESS_PRODUCT_LOCKED`, `SHOP_NAME_REJECTED`, `SHOP_SHELF_FULL`, `SHOP_NOT_ENOUGH_STOCK`, `SHOP_SHELF_EMPTY`, `SHOP_NOTHING_TO_COLLECT` |
| `social.ts` | `WsEvent.MARKET_ACTIVITY`, `WsEvent.MARKET_STALL_CHANGED`, `WsEvent.SHOP_UPDATED`; report context `MARKET_STALL` |

### Backend
| ไฟล์ | เปลี่ยน |
|---|---|
| `prisma/schema.prisma` + migration `phase5_living_shop` | `businesses`: `shop_status`, `stall_slot`, `market_age_band`, `cash_box`, `settled_tick`, `opened_at`, `report_seen_at`, `total_customers`; `business_inventory`: `shelf_quantity`, `on_stall`; ตารางใหม่ `shop_settlements`; enum `ShopStatus`; `QuestObjectiveType` + `SHOP_OPEN`, `SHOP_SERVE_CUSTOMERS`; CHECK `cash_box >= 0`, `shelf_quantity >= 0`; unique index แผง 1 ช่องต่อ 1 ร้านใน age band เดียวกัน |
| `src/business/living-shop-sim.ts` (ใหม่) | ฟังก์ชันบริสุทธิ์จำลองลูกค้า + unit test |
| `src/business/living-shop.service.ts` (ใหม่) | settle, แผง, เปิด/พัก/ปิด, เก็บเงิน, รายงาน, มุมมองตลาด |
| `src/business/shop-settler.service.ts` (ใหม่) | ตัวตั้งเวลา settle ร้านที่เปิดอยู่ + ส่ง event ให้ client ในตลาดเห็นลูกค้า |
| `src/business/market.controller.ts` (ใหม่) | `GET /market` |
| `business.service.ts` | settle ก่อน setPrice/restock/sell/upgrade, `get/list` คืนข้อมูลแผง, restock ปฏิเสธสินค้าที่ยังไม่ปลดล็อก, ต้นทุนเฉลี่ยคิดรวมของบนแผง |
| `business.controller.ts` | endpoint ใหม่ (ด้านล่าง) |
| `config/env.ts` | `LIVING_SHOP_SETTLE_INTERVAL_SECONDS` (0 = ปิด ใช้ใน test) |
| `database/seed-data.ts` | ภารกิจใหม่ |
| `test/living-shop.e2e-spec.ts` (ใหม่) | integration test |

### API ใหม่ (ของเดิมไม่ถูกลบหรือเปลี่ยนรูปแบบ มีแต่เพิ่ม field)
| Method | Path | ทำอะไร |
|---|---|---|
| PATCH | `/businesses/:id` | ตั้งชื่อร้าน (ผ่านตัวกรองคำ) |
| PUT | `/businesses/:id/shelf` | ตั้งจำนวนสินค้าบนแผง `{productCode, quantity}` (0 = เอาลง) ย้ายระหว่างคลังกับแผง |
| POST | `/businesses/:id/status` | `{status: OPEN \| PAUSED \| CLOSED}` |
| POST | `/businesses/:id/collect` | ย้ายเงินในลิ้นชักเข้ากระเป๋า (ต้องมี `Idempotency-Key`) |
| GET | `/businesses/:id/report` | รายงานตั้งแต่ครั้งล่าสุดที่อ่าน |
| POST | `/businesses/:id/report/ack` | บันทึกว่าอ่านรายงานแล้ว |
| GET | `/market` | แผงทั้งหมดในตลาดที่ผู้เล่นคนนี้เห็นได้ |

`POST /businesses/:id/sell` (ขายแบบกดปุ่ม) ยังใช้ได้เหมือนเดิมเพื่อไม่ให้ API เดิมเสีย แต่ client ใหม่ไม่เรียกแล้ว

### Unity client
| ไฟล์ | เปลี่ยน |
|---|---|
| `Core/Api/Dtos.cs`, `Core/Api/HappyWorldApi.cs` | DTO และเมธอดของ API ใหม่ |
| `Core/Realtime/RealtimeClient.cs` | event `market:activity`, `market:stall`, `shop:updated` |
| `Core/World/MarketNpcPlanner.cs` (ใหม่, ไม่พึ่ง Unity) | แปลง event เป็นคิวการเดินของ NPC + ข้อความในฟอง (test ได้ด้วย .NET) |
| `Unity/World/MarketView.cs` (ใหม่) | วาดแผงร้าน ป้ายชื่อ ป้ายราคา สถานะ และ NPC เดินมาซื้อ/เดินหนี เหรียญเด้ง |
| `Unity/UI/GameScreens.cs` | หน้าจัดการร้านใหม่, หน้ารายงานเมื่อกลับเข้าเกม, ปุ่มไปตลาด |
| `Unity/GameSession.cs` | เรียก MarketView เมื่ออยู่โซน MARKET, แสดงรายงานหลังเข้าเกม |
| `Core/Localization/Texts.cs` | ข้อความไทยใหม่ |
| `Tests/HappyWorld.Core.Tests/*` | unit + integration test ฝั่ง client |

## 4. ตัวเลขสมดุลเริ่มต้น (ปรับได้ใน `game-config.ts`)

| เลเวลร้าน | ช่องบนแผง | จุได้ต่อสินค้า | ลูกค้า/นาที |
|---|---|---|---|
| 1 | 2 | 20 | 1.0 |
| 2 | 3 | 30 | 1.3 |
| 3 | 4 | 45 | 1.6 |
| 4 | 5 | 60 | 2.0 |
| 5 | 5 | 80 | 2.5 |

ร้านเลเวล 1 ขายน้ำมะนาวราคาตลาด ขายหมดแผงราว 30–45 นาที กำไรราว 100–200 ทองต่อแผง ร้านขายต่อเองตอนออฟไลน์ได้สูงสุด 8 ชั่วโมง (ไม่มีบทลงโทษถ้าไม่เข้าเกม)

## 5. สิ่งที่ตั้งใจไม่ทำใน Phase นี้

- ผู้เล่นซื้อของจากแผงของผู้เล่นอื่น (ต้องออกแบบเศรษฐกิจระหว่างผู้เล่นแยก)
- ร้านหลายร้านต่อคน (จำกัด 1 ร้านเหมือนเดิม)
- ลบ API ขายแบบกดปุ่ม
