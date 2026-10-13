# รายงาน Phase 3 — Multiplayer, Friends, Safety, WebSocket

วันที่: 13 ต.ค. 2026 · branch `phase-3-multiplayer` (merge เข้า `main` แบบ fast-forward)

## ก่อนเริ่ม (ตรวจจาก repository จริง)

| รายการ | ผล |
|---|---|
| Branch / working tree | `main` สะอาด, 2 commits จาก Phase 1–2 |
| Baseline unit tests | ✅ 26/26 |
| Baseline integration tests | ✅ 64/64 (9 suites) |
| Migration | 1 ไฟล์ (`init`), `prisma migrate status` = up to date |
| Test ที่ล้มเหลวตั้งแต่ก่อนเริ่ม | ไม่มี |

PostgreSQL และ Redis ใน container ปิดอยู่ตอนเริ่ม (container ถูกรีสตาร์ต) จึงสั่ง start ใหม่ ข้อมูลยังอยู่ครบ ไม่มีการ reset ฐานข้อมูลใด

## สิ่งที่สร้าง

**Friends** (`src/social/friends.service.ts`): ส่งคำขอด้วย friend code หรือ player id, รายการขาเข้า/ขาออก, accept/decline (ผู้รับเท่านั้น), ยกเลิก (ผู้ส่งเท่านั้น), รายชื่อเพื่อนพร้อมสถานะออนไลน์, ลบเพื่อน
- คำขอถึงตัวเอง, คำขอซ้ำ, คำขอไขว้, เพิ่มเพื่อนซ้ำ, ถูกบล็อก, ต่างช่วงอายุ ถูกกันทั้งหมด
- กันการกดพร้อมกันด้วย `pg_advisory_xact_lock` ต่อคู่ผู้เล่น และ partial unique index ในฐานข้อมูล
- คำขอไขว้ (A→B แล้ว B→A) = เป็นเพื่อนกันทันที แทนที่จะมีคำขอค้างสองอัน
- รับเพื่อนแล้วเดินภารกิจ `coop_make_friend` ของทั้งสองฝ่าย

**Block และ Moderation** (`blocks.service.ts`, `moderation.service.ts`): บล็อก/ยกเลิก/รายการ, รายงานพร้อมบล็อกในขั้นเดียว, ดูรายงานของตัวเอง
- บล็อกแล้ว: ลบความเป็นเพื่อน, ยกเลิกคำขอค้าง, ส่งคำขอหากันไม่ได้ทั้งสองทาง, หายจากจอกันทันทีในเมือง
- ผู้ถูกบล็อกไม่รู้ว่าถูกบล็อก (ได้ error เดียวกับกรณีต่างช่วงอายุ)

**City + WebSocket** (`src/realtime/`, `src/presence/`): Socket.IO namespace `/realtime`
- ยืนยันตัวตนตอน handshake ด้วย access token + ตรวจ session, 1 คน 1 การเชื่อมต่อ
- 5 โซน (ลานกลางเมือง, บ้าน, ตลาด, พื้นที่กิจกรรม, ประตูมินิเกม) แยกห้องตาม **ช่วงอายุ** และแบ่ง shard เมื่อคนเต็ม
- ขยับตัว: server ตรวจขอบเขตและความเร็ว ถ้าผิดส่ง `move:corrected`
- ข้อความสำเร็จรูป 10 แบบ (ไม่มีแชตอิสระ), เชิญเพื่อน, แจ้งเพื่อนออนไลน์/ออฟไลน์, แจ้งคำขอเพื่อนแบบ realtime
- Rate limit ต่อ socket, presence ใน Redis (หรือ memory), Socket.IO Redis adapter สำหรับหลาย instance
- `GET /city`, `GET /city/players`

**Migration ใหม่** `20261013175827_phase3_social`: เปลี่ยน unique ของ friend_requests เป็น "คู่ละ 1 คำขอที่ค้าง" (ของเดิมทำให้ส่งใหม่ไม่ได้ตลอดไปหลังถูกปฏิเสธ), เพิ่ม `pair_key` แบบเติมค่าจากข้อมูลเดิม, partial unique สำหรับรายงานที่ยังเปิดอยู่ ไม่มีการลบตารางหรือคอลัมน์

**ไม่ได้แตะ**: wallet, store, business, purchases, ภารกิจ (นอกจากเรียก `recordEvent('ADD_FRIEND')` ที่มีอยู่แล้ว), NestJS ยังเป็น 11, Google Play ยังปิด

## ไฟล์

ใหม่: `src/social/*` (5 ไฟล์), `src/realtime/*` (6), `src/presence/*` (4), `src/auth/session-verifier.service.ts`, `packages/shared-contracts/src/social.ts`, migration 1 ไฟล์, `test/social.e2e-spec.ts`, `test/realtime.e2e-spec.ts`, `docs/realtime.md`
แก้: `jwt-auth.guard.ts` (ย้ายการตรวจ token ไป `SessionVerifier` เพื่อใช้ร่วมกับ WebSocket), `app.module.ts`, `app-setup.ts`, `schema.prisma`, `env.ts`, test helpers, docs

## คำสั่งที่รันจริงและผลลัพธ์

| คำสั่ง | ผล |
|---|---|
| `npx prisma migrate dev` (dev DB) | ✅ apply migration ใหม่ |
| `prisma migrate dev --create-only` อีกครั้งเพื่อตรวจ drift | ✅ ได้ migration ว่าง (schema ตรงกับฐานข้อมูล) แล้วลบทิ้ง |
| `npm run lint` | ✅ |
| `npm run typecheck` | ✅ |
| `npm run build` | ✅ |
| `npm test` | ✅ **31/31** (เพิ่ม 5) |
| `npm run test:e2e` (in-memory) | ✅ **92/92** ใน 11 suites (เพิ่ม 28: social 17, realtime 11) |
| `HWO_TEST_REDIS_URL=redis://localhost:6379/15 npm run test:e2e` | ✅ **92/92** โดยใช้ Redis จริง (presence, rate limit, Socket.IO adapter) |
| `node dist/main.js` + Socket.IO client จริง เข้าโซน PLAZA | ✅ ack ถูกต้อง, `GET /city/players` ตอบ, presence ใน Redis ถูกลบเมื่อตัดการเชื่อมต่อ |

### ปัญหาที่เจอระหว่างทางและแก้แล้ว

1. Rate limit คำขอเพื่อน (30/ชม.) ทำให้ test ล้ม → ทำให้ตั้งค่าได้ผ่าน env
2. Socket disconnect handler ทำงานหลังปิดฐานข้อมูลแล้วเกิด unhandled rejection → ห่อด้วย try/catch (ใน production จะทำให้ process ล่มได้)
3. Redis adapter ไม่ปิดการเชื่อมต่อตอน shutdown ทำให้ process ไม่จบ → ปิดใน `dispose()`
4. ตอนรัน Phase 1 test ครั้งแรก Prisma โหลด `REDIS_URL` จาก `.env` ทำให้มี rate-limit counter ค้างใน Redis ของเครื่องพัฒนา → ล้างแล้ว (เฉพาะ key `hwo:throttle:*` ในเครื่องพัฒนา)

## ข้อจำกัด / สิ่งที่ยังเหลือ

1. **ข้อความ Phase 3 ที่ส่งมาถูกตัดกลางประโยค** ที่ "ผู้เล่นที่บล็อกกันต้อง..." ส่วนที่เหลือของสเปก (ถ้ามี) ยังไม่ได้รับ จึงใช้กฎจาก master prompt เดิมแทน
2. กฎช่วงอายุตอนนี้เข้มที่สุด (เจอกันได้เฉพาะช่วงเดียวกัน) ถ้าต้องการให้วัยรุ่นกับผู้ใหญ่เล่นด้วยกันได้ ต้องแก้ `canInteract` ที่เดียวและควรผ่านการพิจารณาด้านความปลอดภัยก่อน
3. ยังไม่มีหน้าผู้ดูแลสำหรับจัดการรายงาน (รายงานถูกเก็บพร้อม audit log แล้ว)
4. ประตูระหว่างโซน (portal) มีพิกัดใน config แล้ว แต่ server ยังไม่บังคับว่าต้องเดินไปถึงประตูก่อนย้ายโซน
5. ยังไม่มี load test (จำนวนผู้เล่นพร้อมกันสูงสุดยังไม่ได้วัด)
6. Docker image ของ backend ยังไม่ได้ build สำเร็จ (ข้อจำกัด network เดิมของ sandbox)

## คำสั่งสำหรับทำงานต่อ

```bash
npm install
./scripts/dev-setup.sh
cd apps/backend && npx prisma migrate deploy   # apply migration ของ Phase 3
npm run dev
./scripts/test-all.sh
HWO_TEST_REDIS_URL=redis://localhost:6379/15 npm run test:e2e
```

ถัดไป: **Phase 4 — Unity Client** (ต้องใช้เครื่องที่มี Unity 6 LTS + Android Build Support)
