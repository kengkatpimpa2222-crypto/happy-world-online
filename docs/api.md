# API (v1)

Base URL: `/api/v1` · สเปกเต็ม: [openapi.json](openapi.json) (สร้างจาก server จริง) · Swagger UI: `/api/docs` (dev เท่านั้น)

## หลักทั่วไป

- ส่ง `Authorization: Bearer <accessToken>` ทุก endpoint ยกเว้น health, `auth/guest`, `auth/login`, `auth/refresh`
- คำขอที่ใช้เงิน (`POST /store/purchases`, `POST /businesses/:id/restock`, `POST /businesses/:id/collect`) ต้องมี header `Idempotency-Key` (8–64 ตัว `[A-Za-z0-9_-]`) สร้างใหม่ทุกครั้งที่ผู้เล่นกดซื้อ และใช้ค่าเดิมเมื่อ retry
- Error ทุกตัวมีรูปแบบเดียวกัน:

```json
{ "error": { "code": "INSUFFICIENT_FUNDS", "message": "Not enough GOLD", "details": { "required": 360, "balance": 200 }, "requestId": "..." } }
```

รายการ `code` ทั้งหมดอยู่ที่ `packages/shared-contracts/src/errors.ts` ให้ client แปลงเป็นข้อความภาษาไทย/อังกฤษ/จีน

## ตัวอย่าง flow

```bash
API=http://localhost:3000/api/v1
# 1) สร้าง Guest
curl -s -XPOST $API/auth/guest -H 'content-type: application/json' \
  -d '{"deviceId":"my-device-123","ageBand":"TEEN_13_17","locale":"th"}'
# -> { userId, accessToken, refreshToken, ... }  เก็บ refreshToken ไว้ในเครื่องอย่างปลอดภัย

# 2) ดูกระเป๋าเงิน
curl -s $API/player/wallet -H "authorization: Bearer $TOKEN"

# 3) ซื้อโต๊ะ 1 ตัวด้วย Gold
curl -s -XPOST $API/store/purchases -H "authorization: Bearer $TOKEN" \
  -H "Idempotency-Key: $(uuidgen)" -H 'content-type: application/json' \
  -d '{"itemId":"furn_wood_table","quantity":1,"currency":"GOLD"}'

# 4) เปิดร้าน: ซื้อวัตถุดิบ ตั้งราคา ขาย
curl -s $API/businesses -H "authorization: Bearer $TOKEN"     # ได้ id ร้าน
curl -s -XPOST $API/businesses/$SHOP/restock -H "authorization: Bearer $TOKEN" -H "Idempotency-Key: $(uuidgen)" \
  -H 'content-type: application/json' -d '{"productCode":"LEMONADE","quantity":20}'
curl -s -XPATCH $API/businesses/$SHOP/pricing -H "authorization: Bearer $TOKEN" \
  -H 'content-type: application/json' -d '{"productCode":"LEMONADE","price":10}'
curl -s -XPOST $API/businesses/$SHOP/sell -H "authorization: Bearer $TOKEN"
# -> totals { unitsSold, revenue, costOfGoods, profit } + tipsText คำอธิบายภาษาไทย
```

## Endpoints

| Method | Path | หมายเหตุ |
|---|---|---|
| GET | /health | 200 ok / 503 degraded พร้อมสถานะ database, redis |
| GET | /health/live | liveness |
| POST | /auth/guest | `{deviceId, ageBand, locale?}` → token pair |
| POST | /auth/link-account | `{email, password}` ผูกบัญชี Guest (UNDER_13 ต้องมีผู้ปกครองยินยอม) |
| POST | /auth/login | `{email, password}` กู้คืนบัญชีบนอุปกรณ์ใหม่ |
| POST | /auth/refresh | `{refreshToken}` → token pair ใหม่ (token เก่าใช้ไม่ได้อีก) |
| POST | /auth/logout | 204, ยกเลิก session ปัจจุบัน |
| GET | /player/profile | โปรไฟล์ เลเวล XP avatar friendCode |
| PATCH | /player/profile | `{displayName?, avatar?, locale?}` avatar ใส่ได้เฉพาะไอเทมที่มี |
| GET | /player/inventory | ไอเทมที่มี |
| GET | /player/wallet | ยอด GOLD/GEMS + ธุรกรรมล่าสุด 20 รายการ |
| GET | /quests | ภารกิจทั้งหมด + ความคืบหน้า + canStart |
| POST | /quests/:questId/start | เริ่มภารกิจ (ตรวจเลเวลและภารกิจก่อนหน้า) |
| POST | /quests/:questId/claim | รับรางวัลครั้งเดียว |
| GET | /store/catalog | ไอเทมที่ขาย ราคาจาก server |
| POST | /store/purchases | `{itemId, quantity, currency}` + Idempotency-Key |
| POST | /purchases/verify | `{provider, productId, purchaseToken}` ตรวจการซื้อเงินจริง |
| GET | /businesses | ร้านของฉัน |
| GET | /businesses/:id | รายละเอียดร้าน รายรับ รายจ่าย กำไร |
| PATCH | /businesses/:id/pricing | `{productCode, price}` ภายในช่วง min–max |
| POST | /businesses/:id/restock | `{productCode, quantity}` + Idempotency-Key |
| POST | /businesses/:id/sell | ขาย 1 รอบ (มี cooldown ค่าเริ่มต้น 30 วินาที) **deprecated ตั้งแต่ Phase 5** ยังใช้ได้ แต่เกมใช้แผงในตลาดแทน |
| POST | /businesses/:id/upgrade | `{expectedLevel}` กดซ้ำไม่จ่ายซ้ำ |
| PATCH | /businesses/:id | `{name}` ตั้งชื่อร้าน (2–20 ตัว, ผ่านตัวกรองคำ) |
| PUT | /businesses/:id/shelf | `{productCode, quantity}` จำนวนบนแผง (ย้ายจาก/กลับคลัง), 0 = เอาลงจากแผง |
| POST | /businesses/:id/status | `{status: OPEN \| PAUSED \| CLOSED}` เปิดร้าน = ได้ตำแหน่งแผงในตลาด |
| POST | /businesses/:id/collect | ย้ายเงินในลิ้นชักเข้ากระเป๋า + Idempotency-Key |
| GET | /businesses/:id/report | รายงานตั้งแต่อ่านครั้งล่าสุด: ลูกค้า ยอดขาย กำไร ลูกค้าที่พลาด คำแนะนำ |
| POST | /businesses/:id/report/ack | `{until?}` บันทึกว่าอ่านรายงานถึงเวลานี้แล้ว (204) |
| GET | /market | แผงในตลาดที่ผู้เล่นเห็นได้ (age band เดียวกัน, ไม่รวมคนที่ block) + แผงของฉัน |

| POST | /friends/requests | `{friendCode}` หรือ `{playerId}`; ส่งซ้ำได้ request เดิม; ถ้าอีกฝ่ายขอมาก่อน = เป็นเพื่อนทันที (`autoAccepted`) |
| GET | /friends/requests/incoming | คำขอที่รอเราตอบ |
| GET | /friends/requests/outgoing | คำขอที่เราส่งและยังรออยู่ |
| POST | /friends/requests/:id/accept | ผู้รับเท่านั้น (คนอื่นได้ 404); กดซ้ำได้ |
| POST | /friends/requests/:id/decline | ผู้รับเท่านั้น; กดซ้ำได้ |
| DELETE | /friends/requests/:id | ผู้ส่งยกเลิกคำขอ |
| GET | /friends | รายชื่อเพื่อน + `online`, `zone` |
| DELETE | /friends/:friendId | เลิกเป็นเพื่อน (ทั้งสองฝั่ง) ไม่ลบข้อมูลอื่น |
| POST | /players/:id/block | บล็อก: ลบความเป็นเพื่อน ยกเลิกคำขอ ซ่อนกันในเมืองทันที |
| DELETE | /players/:id/block | ยกเลิกบล็อก |
| GET | /players/blocked | รายชื่อที่เราบล็อก |
| POST | /moderation/reports | `{reportedPlayerId, reason, contextCode?, alsoBlock?}` ไม่มีช่องพิมพ์ข้อความอิสระ |
| GET | /moderation/reports/me | รายงานของเรา (เห็นแค่สถานะ) |
| GET | /city | ผังเมือง โซน ประตู ข้อความสำเร็จรูป และวิธีเชื่อม WebSocket |
| GET | /city/players?zone= | ผู้เล่นออนไลน์ในโซนที่เรามองเห็นได้ |

WebSocket: ดู [realtime.md](realtime.md)

หมายเหตุ: ร้านค้าเพิ่ม `GET /businesses`, `PATCH /pricing`, `POST /restock` นอกเหนือจากรายการขั้นต่ำ เพราะต้องใช้สำหรับ "ตั้งราคา" และ "ซื้อวัตถุดิบ" ตามที่ระบุในฟีเจอร์ 3.4

## ร้านมีชีวิต (Phase 5)

```bash
# วางของขึ้นแผงแล้วเปิดร้าน (ของต้องอยู่ในคลังก่อน: restock)
curl -s -XPUT $API/businesses/$SHOP/shelf -H "authorization: Bearer $TOKEN" -H 'content-type: application/json' -d '{"productCode":"LEMONADE","quantity":20}'
curl -s -XPOST $API/businesses/$SHOP/status -H "authorization: Bearer $TOKEN" -H 'content-type: application/json' -d '{"status":"OPEN"}'
# ลูกค้า NPC มาทุกนาทีทั้งตอนออนไลน์และออฟไลน์ (สูงสุด 8 ชม.) เงินเข้าลิ้นชัก (stall.cashBox)
curl -s $API/businesses/$SHOP/report -H "authorization: Bearer $TOKEN"
curl -s -XPOST $API/businesses/$SHOP/collect -H "authorization: Bearer $TOKEN" -H "Idempotency-Key: $(uuidgen | tr -d -)"
```

`GET /businesses` และ `GET /businesses/:id` คืน field เดิมทั้งหมด และเพิ่ม `products[].shelfQuantity/onStall/unlockLevel`, `stall { status, slot, cashBox, totalCustomers, shelfSlots, shelfCapacity, customersPerMinute, slotsUsed }` และ `catalog[]` (สินค้าทุกชนิดพร้อมเลเวลที่ปลดล็อก) การอ่านร้านจะคำนวณลูกค้าที่ค้างอยู่ก่อนเสมอ

## เทศกาลเมือง (Phase 6)

ทุกสัปดาห์มีเทศกาล 1 งาน (หมุนเวียน 5 งาน) จัด **ศุกร์ 00:00 ถึงอาทิตย์ 24:00 เวลาไทย** ระหว่างงาน ลูกค้า NPC อยากได้สินค้าหลักของงาน x3 และสินค้ารอง x2 คนในตลาดมากขึ้น x1.3 และมี "นักเที่ยวงาน" (`FESTIVAL_GOER`) มาด้วย ทุกชิ้นของสินค้าเทศกาลที่ขายได้นับเป็นแต้มของผู้เล่นและของทั้งเมือง (แยกตามช่วงอายุ) เมืองถึงขั้นที่ 150 / 500 / 1200 ชิ้น ผู้ที่ช่วยขายอย่างน้อย 10 ชิ้นรับรางวัลขั้นนั้นได้ (ขั้น 3 ได้เฟอร์นิเจอร์ที่ระลึกของงาน)

```bash
curl -s $API/festival -H "authorization: Bearer $TOKEN"
# -> { now, current: { key: "COOL_SUMMER-40", status: UPCOMING|ACTIVE|ENDED, startsAt, endsAt, featured[], city { points, contributors, tiersReached },
#      tiers[{ tier, target, reached, rewardGold, rewardXp, rewardItemId, claimed, claimable }], me { points, minToClaim }, top[] }, previous }
curl -s -XPOST $API/festival/claim -H "authorization: Bearer $TOKEN" -H 'content-type: application/json' -d '{"festivalKey":"COOL_SUMMER-40","tier":1}'
```

| Method | Path | หมายเหตุ |
|---|---|---|
| GET | `/festival` | เทศกาลของสัปดาห์นี้ (กำลังจะเริ่มหรือกำลังจัด) + `previous` = สัปดาห์ก่อน เฉพาะเมื่อยังมีรางวัลให้รับ `top` = ผู้ช่วย 5 อันดับ ช่วงอายุเดียวกัน ไม่รวมคนที่บล็อกกัน ผู้เล่นอายุต่ำกว่า 13 ไม่เห็นชื่อคนอื่น |
| POST | `/festival/claim` | `{ festivalKey, tier }` รับได้ครั้งเดียวต่อขั้น จ่ายผ่าน ledger (`FESTIVAL_REWARD`) รับของสัปดาห์นี้หรือสัปดาห์ก่อนได้ error: `FESTIVAL_NOT_FOUND` (404), `FESTIVAL_CLAIM_EXPIRED`, `FESTIVAL_TIER_NOT_REACHED`, `FESTIVAL_NOT_ENOUGH_CONTRIBUTION`, `FESTIVAL_ALREADY_CLAIMED` (409) |

ปฏิทินคำนวณจากนาฬิกาล้วน ๆ (`packages/shared-contracts/src/festival.ts`) ไม่ต้องตั้งค่าใน DB สำหรับทดสอบบนเครื่อง ตั้ง `FESTIVAL_CLOCK_OFFSET_HOURS` เพื่อเลื่อนปฏิทิน (ห้ามใช้ใน production ระบบไม่ยอมเปิด)

