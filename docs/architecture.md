# สถาปัตยกรรม

```
 Unity Client (Android → iOS/PC)
   │  HTTPS REST /api/v1  (JWT access token 15 นาที + refresh token หมุนเวียน)
   │  WebSocket (Phase 3: สถานะออนไลน์, ตำแหน่ง)
   ▼
 NestJS backend (stateless, scale แนวนอนได้)
   ├─ Guards: Throttler (Redis หรือ memory) → JwtAuthGuard (ตรวจ session ใน DB ทุกครั้ง)
   ├─ Modules: auth · player · wallet · quests · store · business · purchases · health · audit
   ├─ Validation: zod schemas จาก packages/shared-contracts
   ▼
 PostgreSQL 16 (ข้อมูลถาวร, transaction, CHECK constraints)   Redis 7 (rate limit, ต่อไป: presence/pub-sub)
```

## การแบ่งความรับผิดชอบ

| ชั้น | หน้าที่ |
|---|---|
| Controller | รับ HTTP, ตรวจ input ด้วย zod, ดึงผู้เล่นจาก token (`@CurrentUser`) |
| Service | กฎเกม, transaction, ตรวจกรรมสิทธิ์ |
| `WalletService.applyChange` | จุดเดียวที่เปลี่ยนยอดเงิน เขียน ledger ทุกครั้ง |
| `QuestsService.recordEvent` | เดินภารกิจจากเหตุการณ์ฝั่ง server ภายใน transaction เดียวกับการกระทำ |
| `business-sim.ts` | สูตรจำลองยอดขายแบบ pure function ทดสอบง่าย อธิบายผู้เล่นได้ |
| `shared-contracts` | schema/ค่า config ที่ client ใช้แสดงผลได้ แต่ไม่ใช้คำนวณรางวัล |

## รูปแบบป้องกันการโกงและคำขอซ้ำ

1. **Conditional update**: `UPDATE player_wallets SET balance = balance + :amt WHERE ... AND balance + :amt >= 0` ทำให้หักเงินพร้อมกันหลายคำขอก็ไม่ติดลบ
2. **Idempotency key**: unique `(user_id, idempotency_key)` ใน `wallet_transactions` คำขอซ้ำได้ผลลัพธ์เดิม ไม่ตัดเงินซ้ำ
3. **State machine**: ภารกิจ `COMPLETED → CLAIMED` ด้วย `updateMany where status = COMPLETED` มีแค่คำขอเดียวที่สำเร็จ
4. **Row lock**: ร้านค้าใช้ `SELECT ... FOR UPDATE` ก่อนแก้ไข ทำให้คำขอบนร้านเดียวกันเรียงคิว
5. **Retry on conflict**: `PrismaService.tx()` ลองใหม่เมื่อชน unique (P2002) ครั้งที่สองจะเข้าทาง "ทำไปแล้ว"

## การเลือกเทคโนโลยี

- **NestJS 11 (CommonJS)**: NestJS 12 ออกแล้วแต่เป็น ESM-only ซึ่ง Jest/decorator metadata ยังยุ่งยาก เลือก 11 ที่เสถียรก่อน อัปเกรดได้ภายหลัง
- **Prisma 6**: Prisma 7 เปลี่ยนวิธี config มาก รอให้นิ่งก่อน
- **zod 4** แทน class-validator: schema เดียวใช้ได้ทั้ง validation, type และ OpenAPI
