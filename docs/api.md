# API (v1)

Base URL: `/api/v1` · สเปกเต็ม: [openapi.json](openapi.json) (สร้างจาก server จริง) · Swagger UI: `/api/docs` (dev เท่านั้น)

## หลักทั่วไป

- ส่ง `Authorization: Bearer <accessToken>` ทุก endpoint ยกเว้น health, `auth/guest`, `auth/login`, `auth/refresh`
- คำขอที่ใช้เงิน (`POST /store/purchases`, `POST /businesses/:id/restock`) ต้องมี header `Idempotency-Key` (8–64 ตัว `[A-Za-z0-9_-]`) สร้างใหม่ทุกครั้งที่ผู้เล่นกดซื้อ และใช้ค่าเดิมเมื่อ retry
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
| POST | /businesses/:id/sell | ขาย 1 รอบ (มี cooldown ค่าเริ่มต้น 30 วินาที) |
| POST | /businesses/:id/upgrade | `{expectedLevel}` กดซ้ำไม่จ่ายซ้ำ |

หมายเหตุ: ร้านค้าเพิ่ม `GET /businesses`, `PATCH /pricing`, `POST /restock` นอกเหนือจากรายการขั้นต่ำ เพราะต้องใช้สำหรับ "ตั้งราคา" และ "ซื้อวัตถุดิบ" ตามที่ระบุในฟีเจอร์ 3.4
