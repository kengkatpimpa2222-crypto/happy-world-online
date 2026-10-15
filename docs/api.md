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

## เมืองมีชีวิต (Phase 7)

เมืองเดินตาม **เวลาจริงของกรุงเทพ**: กลางวัน/กลางคืน คนเดินตลาดตามชั่วโมง (เช้าเด็กไปโรงเรียน เที่ยงคนทำงาน ค่ำนักท่องเที่ยว ดึกเงียบ) ฤดูกาลไทย 3 ฤดู และอากาศที่เปลี่ยนทุก 3 ชั่วโมง (แดดดี / ร้อนจัด / ครึ้มฟ้า / ฝนตก) ทั้งหมดมีผลกับลูกค้า NPC เช่น ฝนตกคนน้อยลงแต่ลูกชิ้นปิ้งขายดี x1.5 และ **ทุกชิ้นที่ขายได้ในตลาด = 1 แต้มสร้างเมือง** แต้มของทั้งเมือง (แยกตามช่วงอายุ) สร้างโครงการตามลำดับ: สวนสาธารณะ 1,500 → ป้ายรถเมล์ 3,500 → โรงเรียน 7,000 → อาคารสำนักงาน 12,000 → หอนาฬิกา 20,000 สร้างเสร็จแล้วอยู่ถาวรและมีผลกับตลาดตั้งแต่วินาทีที่เสร็จ

```bash
curl -s $API/city/now -H "authorization: Bearer $TOKEN"
# -> { now, hour: 14.08, clock: "14:05", phase: AFTERNOON, phaseTh, season: RAINY, seasonTh, weather: RAIN, weatherTh, tipTh,
#      crowd: 0.7, busyTypes: ["STUDENT"], demand: [{ productCode: "MEATBALL", demand: 1.5 }, ...], nextChangeAt, forecast: [{ at, weather, weatherTh }] x4 }
curl -s $API/city/growth -H "authorization: Bearer $TOKEN"
# -> { points, builtCount, current: "BUS_STOP", projects: [{ code, nameKey, nameTh, effectTh, cost, zone, x, y, status: DONE|BUILDING|LOCKED, progress, completedAt }],
#      me: { points, total }, top: [{ rank, displayName, points, isMe }] }
```

| Method | Path | หมายเหตุ |
|---|---|---|
| GET | `/city/now` | เวลา ช่วงของวัน ฤดู อากาศ คำแนะนำ สินค้าที่ขายดี/ขายยากในอากาศนี้ ลูกค้ากลุ่มที่มาเยอะช่วงนี้ และพยากรณ์ 4 ช่วงถัดไป |
| GET | `/city/growth` | โครงการทั้งหมดของเมืองตัวเอง `top` = ช่างใหญ่ 5 อันดับของโครงการที่กำลังสร้าง กฎเดียวกับผู้ช่วยเทศกาล (ช่วงอายุเดียวกัน ไม่รวมคนที่บล็อกกัน ต่ำกว่า 13 ไม่เห็นชื่อคนอื่น) |

`GET /city` และ `GET /city/players` เดิมทำงานเหมือนเดิม อากาศคำนวณจากนาฬิกา + hash (`packages/shared-contracts/src/city-time.ts`) ทุกเครื่องเห็นเหมือนกัน สำหรับทดสอบบนเครื่อง ตั้ง `CITY_WEATHER_OVERRIDE=RAIN` (หรือ SUNNY / HOT / CLOUDY) เพื่อบังคับอากาศ และ `FESTIVAL_CLOCK_OFFSET_HOURS` เลื่อนนาฬิกาเมืองไปพร้อมกับปฏิทินเทศกาล (ห้ามใช้ทั้งสองค่าใน production ระบบไม่ยอมเปิด)


## มินิเกมทำของขาย (Phase 8)

ทำสินค้าเข้าคลังร้านด้วยมือ: เล่นรอบสั้น ๆ ได้ดาว 0–3 ดวง ได้ของ 1/4/7/10 ชิ้น (ต้นทุน 0) และ XP 1/3/5/8 วันละ 15 รอบที่ได้ของ (นับตามวันเวลาไทย) เกินจากนั้นเล่นได้แต่ไม่ได้ของ

| เกม | สินค้า | เวลา | คะแนนดาว 1/2/3 | คะแนนสูงสุดที่รับ |
|---|---|---|---|---|
| CATCH เก็บมะนาว/ดอกไม้ | LEMONADE, FLOWER | 45 วิ | 250 / 500 / 750 | 2800 |
| GRILL ปิ้งลูกชิ้น/อบขนมปัง | MEATBALL, BREAD | 60 วิ | 500 / 1100 / 2000 | 6000 |
| POUR ชงชาไทย | THAI_TEA | 40 วิ | 450 / 1100 / 1700 | 3000 |

| Method | Path | หมายเหตุ |
|---|---|---|
| GET | `/minigames` | กติกา คะแนนดาว รอบที่ยังได้ของวันนี้ (`rewardedPlaysLeft`, `resetsAt`) และคะแนนสูงสุดของฉันต่อเกม |
| POST | `/minigames/start` | `{businessId, productCode}` สินค้าต้องปลดล็อกแล้ว คืน `{sessionId, game, seed, durationSeconds, starScores, unitsByStars, rewardedPlaysLeft}` เกมใช้ `seed` สร้างด่านแบบเดียวกันทุกเครื่อง |
| POST | `/minigames/:id/finish` | `{score}` ส่งก่อนหมดเวลาเกิน 3 วิ = `MINIGAME_TOO_EARLY`, ช้ากว่าหมดเวลาเกิน 10 นาที = `MINIGAME_EXPIRED`, คะแนนเกินเพดานเกม = `MINIGAME_SCORE_INVALID` เรียกซ้ำได้ผลเดิม (`replayed: true`) ไม่ได้ของซ้ำ คืน `{stars, rewarded, unitsAwarded, xpAwarded, stockFull, level, best, rewardedPlaysLeft, business}` |

คลังเต็มจะได้ของเท่าที่ใส่ได้ (`stockFull: true`) จบรอบนับเควส `PLAY_MINIGAME` (เควสใหม่ "ทำของขายเอง 3 รอบ")

## ออกสำรวจและงานฝีมือ (Phase 9)

วงจรวัตถุดิบ: ออกสำรวจ → ได้วัตถุดิบ → คราฟต์เป็นสินค้าพิเศษเข้าคลังร้าน → วางขายให้ลูกค้า NPC กติกาทั้งหมดอยู่ใน `packages/shared-contracts/src/adventure.ts`

| สถานที่ | ต้องเป็นนักสำรวจ Lv. | เวลา | เหตุการณ์ | วัตถุดิบ |
|---|---|---|---|---|
| ORCHARD สวนผลไม้ริมคลอง | 1 | 3 นาที | 2 จาก 3 | MANGO, COCONUT, STICKY_RICE |
| FLOWER_FOREST ป่าดอกไม้ | 2 | 6 นาที | 3 จาก 3 | JASMINE, MARIGOLD, WILD_HONEY |
| HERB_VALLEY หุบเขาสมุนไพร | 4 | 10 นาที | 3 จาก 4 | LEMONGRASS, HERB, BEESWAX, WILD_HONEY |

แผน: `CAREFUL` (โอกาสผ่าน +20%, เก็บได้ 70%, พลาดไม่เสียของ), `STEADY` (ปกติ), `BOLD` (โอกาสผ่าน -15%, เก็บได้ 150%, พลาดแล้วของหาย) นักสำรวจแต่ละเลเวลเพิ่มโอกาสผ่าน 6%

| Method | Path | หมายเหตุ |
|---|---|---|
| GET | `/expeditions` | สถานที่ (`unlocked`, `seconds`), แผน, ทริปที่กำลังไป (`active`), ทริปล่าสุด, ทักษะ `EXPLORER`/`CRAFTER` และวัตถุดิบของฉัน |
| POST | `/expeditions/start` | `{site, plan}` ไปได้ทีละทริป (`EXPEDITION_IN_PROGRESS`), สถานที่ยังล็อก = `EXPEDITION_LOCKED` server สุ่ม seed เก็บไว้ ไม่ส่งให้เครื่อง |
| POST | `/expeditions/:id/claim` | ยังไม่ถึงเวลา = `EXPEDITION_NOT_FINISHED` (`secondsLeft`) ถึงแล้ว server คำนวณเรื่องราวและของที่ได้ คืน `{result: {gathered, events[], loot, explorerXp, playerXp, ...}, skills, materials}` เรียกซ้ำได้ผลเดิม (`replayed: true`) |
| GET | `/crafting` | สูตร (วัตถุดิบที่ใช้, ได้กี่ชิ้น, ค่าแรง, ทำได้กี่ครั้ง `canMake`), วัตถุดิบของฉัน, ทักษะช่าง |
| POST | `/crafting/craft` | ต้องมี `Idempotency-Key` `{recipe, businessId, times 1-10}` จ่ายค่าแรง Gold ใช้วัตถุดิบ ใส่สินค้าเข้าคลังร้าน วัตถุดิบไม่พอ = `NOT_ENOUGH_MATERIALS`, สูตรล็อก = `RECIPE_LOCKED`, คลังเต็ม = `BUSINESS_STOCK_FULL` |

| สูตร | ใช้ | ได้ | ค่าแรง | ช่าง Lv. | ราคาตลาด |
|---|---|---|---|---|---|
| MANGO_STICKY_RICE | มะม่วง 1 + ข้าวเหนียว 1 + มะพร้าว 1 | 3 | 6 | 1 | 35 |
| JASMINE_GARLAND | มะลิ 3 + ดาวเรือง 1 | 2 | 4 | 1 | 28 |
| HERBAL_BALM | สมุนไพร 2 + ตะไคร้ 1 + ขี้ผึ้ง 1 | 2 | 8 | 2 | 45 |
| HONEY_MANGO_JAM | มะม่วง 2 + น้ำผึ้งป่า 1 | 2 | 6 | 2 | 55 |

ช่างเลเวล 4, 7, 10 ได้เพิ่มครั้งละ 1 ชิ้น สินค้าเหล่านี้เป็น `craftOnly`: `POST /businesses/:id/restock` ตอบ `PRODUCT_CRAFT_ONLY` และ `POST /minigames/start` ตอบ `NO_MINIGAME_FOR_PRODUCT` ต้นทุนต่อชิ้นของร้าน = ค่าแรง ÷ จำนวนชิ้น (วัตถุดิบจ่ายด้วยเวลา เหมือนของจากมินิเกม) ค่าแรงลงเป็นรายจ่ายของร้าน

ทดสอบบนเครื่อง: `EXPEDITION_TIME_SCALE=0.02` ทำให้ทริป 3 นาทีเหลือ 4 วินาที (ห้ามใช้ใน production ระบบไม่ยอมเปิด)

## บ้านหลังแรก (Phase 10)

ผู้เล่นทุกคนมีแถว `player_homes` ตั้งแต่สมัคร แต่ยังไม่มีที่ดินจนกว่าจะกด "รับบ้าน" ซึ่งได้ที่ดินว่างแปลงแรกของย่านบ้านในช่วงอายุเดียวกัน พร้อมเฟอร์นิเจอร์ต้อนรับ (เตียงเดี่ยว 1, โต๊ะไม้ 1, กระถางต้นไม้ 2) เข้ากระเป๋า กติกาอยู่ใน `packages/shared-contracts/src/home.ts`

ห้องเป็นตาราง `gridWidth × gridHeight` ช่อง (เริ่มต้น 8×8, ช่องละ 1.5 หน่วย) เฟอร์นิเจอร์แต่ละชิ้นกินพื้นที่ `footprint` [กว้าง, ลึก] จาก `items.metadata` (ไม่ระบุ = 1×1) หมุนได้ 0/90/180/270 วางได้ไม่เกิน 40 ชิ้น

**ใครเป็นเจ้าของเฟอร์นิเจอร์**: ของที่มี = จำนวนในกระเป๋า (`player_inventory`) + ชิ้นที่วางในบ้าน (`home_furniture`) วางของ = ledger `HOME_PLACE` ลดในกระเป๋า เก็บของ = `HOME_REMOVE` คืนเข้ากระเป๋า ทั้งหมดผ่าน `InventoryService` ของ Phase 9 ใน transaction เดียวกับการเปลี่ยนผังบ้าน ของจึงไม่งอกและไม่หาย

| Method | Path | หมายเหตุ |
|---|---|---|
| GET | `/homes/me` | บ้านของฉัน: `{homeId, owner, isMine, isFriend, claimed, claimedAt, plot {plotNumber, street, slot, x, y, door, facing}, visitPolicy, gridWidth, gridHeight, interior, layoutVersion, furniture[{id, itemId, x, y, rotation, footprint}]}` ยังไม่รับบ้าน = `claimed: false`, `plot: null` |
| POST | `/homes/me/claim` | รับบ้าน (ครั้งเดียว) คืน `{claimed, plotNumber, gift[{itemId, quantity}], home}` รับซ้ำ = `HOME_ALREADY_CLAIMED` 409 กดพร้อมกันหลายครั้งได้บ้านหลังเดียว ของขวัญครั้งเดียว |
| GET | `/homes/me/furniture` | เฟอร์นิเจอร์ที่ฉันมี: `[{itemId, nameKey, footprint, inBag, placed}]` |
| PUT | `/homes/me/layout` | ต้องมี `Idempotency-Key` บันทึกผังทั้งห้อง `{baseVersion, pieces[{id?, itemId, x, y, rotation}]}` ชิ้นที่มี `id` = ของเดิม (ย้าย/หมุน), ไม่มี `id` = หยิบจากกระเป๋า, ชิ้นเดิมที่ไม่ส่งมา = เก็บเข้ากระเป๋า คืน `{replayed, placed, moved, removed, version, home}` |
| PATCH | `/homes/me/settings` | `{visitPolicy: PRIVATE \| FRIENDS \| PUBLIC}` (เริ่มต้น FRIENDS) ผู้มาเยี่ยมที่หมดสิทธิ์ถูกพาออกทันที |
| GET | `/homes/district?street=` | ย่านบ้านของช่วงอายุฉัน ถนนละ 12 แปลง: `{street, streets, myPlot, plots[{plotNumber, slot, x, y, door, facing, taken, owner, isMine, isFriend, visitPolicy, canVisit}]}` เจ้าของที่บล็อกกันแสดงเป็นบ้านไม่มีชื่อ |
| GET | `/homes/:ownerId` | ดูบ้านคนอื่น (สิทธิ์ตามด้านล่าง) ไม่มีวันเห็น `visitPolicy` ของบ้านคนอื่น |

**สิทธิ์เข้าบ้าน** (`HomeAccessService` ใช้ร่วมกันทั้ง REST และ realtime): เจ้าของเข้าได้เสมอ, ยังไม่รับบ้าน/ต่างช่วงอายุ/บล็อกกัน/บัญชีไม่ active = 404 `NOT_FOUND` (ไม่บอกว่ามีบ้านอยู่), `PRIVATE` หรือ `FRIENDS` แต่ไม่ใช่เพื่อน = 403 `HOME_VISIT_NOT_ALLOWED` ผู้มาเยี่ยมแก้บ้านคนอื่นไม่ได้เลย: ทุก endpoint ที่เขียนเป็น `/homes/me/*` เท่านั้น

**ข้อผิดพลาดตอนบันทึกผัง**
- `HOME_NOT_CLAIMED` 409 ยังไม่รับบ้าน
- `HOME_LAYOUT_CONFLICT` 409 บ้านถูกแก้จากเครื่องอื่นหลังเริ่มจัด (`baseVersion` ไม่ตรง) คืน `layoutVersion` ล่าสุด ให้โหลดใหม่
- `HOME_PLACEMENT_INVALID` 400 `{problem: OUT_OF_BOUNDS | OVERLAP | BAD_ROTATION | TOO_MANY | UNKNOWN_PIECE | NOT_FURNITURE, index?}`
- `ITEM_NOT_OWNED` 409 ของในกระเป๋าไม่พอ (เช่น ขายไปแล้วระหว่างจัดบ้าน)
- `IDEMPOTENCY_KEY_REUSED` 409 ใช้ key เดิมกับผังอื่น ส่ง key เดิมกับผังเดิมซ้ำ = ได้ผลเดิม (`replayed: true`) ไม่วางซ้ำ

จำกัดความถี่: เขียน (`claim`, `layout`, `settings`) `RATE_LIMIT_HOME_WRITES_PER_MINUTE` (30/นาที), ดูบ้านคนอื่น `RATE_LIMIT_HOME_VISITS_PER_MINUTE` (60/นาที) ทุกการเขียนมี audit log: `home.claim`, `home.layout_save`, `home.visit_policy`
