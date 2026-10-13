# ความปลอดภัยและความเป็นส่วนตัว

## สิ่งที่ทำแล้ว (มี test ยืนยันตามวงเล็บ)

| ด้าน | การป้องกัน |
|---|---|
| Input validation | zod schema แบบ strict ทุก body/param; field แปลกปลอมถูกปฏิเสธ (`player.e2e`, `economy.e2e`) |
| Authentication | JWT HS256 อายุ 15 นาที + refresh token สุ่ม 256 bit เก็บเป็น sha256, หมุนทุกครั้ง, ใช้ซ้ำ = revoke ทุก session (`auth.e2e`) |
| Authorization | ไม่มี playerId ใน request; ทุก query กรองด้วย user ของ token; ของคนอื่นตอบ 404 (`security.e2e`) |
| Logout | ตรวจ session ใน DB ทุก request จึงมีผลทันที (`auth.e2e`) |
| Password | scrypt (N=16384) + salt สุ่ม, เปรียบเทียบแบบ timing-safe, login ที่ไม่มีอีเมลก็ใช้เวลาเท่ากัน |
| Rate limiting | ทั่วไป 120/นาที/IP, สร้าง Guest 10/ชม./IP, login/link 20/15 นาที; ใช้ Redis เมื่อมี (`rate-limit.e2e`) |
| HTTP security | helmet (CSP, X-Frame-Options, nosniff ฯลฯ), ปิด x-powered-by, body จำกัด 64 KB, CORS allow-list (`foundation.e2e`) |
| SQL injection | Prisma + `$queryRaw` แบบ tagged template (parameterized) เท่านั้น, param ตรวจรูปแบบก่อน (`security.e2e`) |
| Replay / duplicate | Idempotency-Key, state machine, unique purchase token (`economy.e2e`, `quests.e2e`, `purchases.e2e`) |
| ยอดเงิน | ห้ามติดลบทั้งในโค้ดและ CHECK constraint, ledger แก้/ลบไม่ได้ (`foundation.e2e`) |
| Audit log | สร้างบัญชี, ผูกบัญชี, login (สำเร็จ/ล้มเหลว), ใช้ refresh token ซ้ำ, ซื้อของ, รับรางวัล, อัปเกรดร้าน, ซื้อเงินจริง |
| Error handling | ทุก error เป็น `{ error: { code, message, requestId } }` ไม่มี stack trace ใน response; stack อยู่ใน log ฝั่ง server |
| Secrets | อ่านจาก env เท่านั้น ตรวจด้วย schema ตอนเริ่ม; production ปฏิเสธ secret ตัวอย่างและ BILLING_MODE=mock |
| Swagger | เปิดเมื่อ `SWAGGER_ENABLED=true` และไม่ใช่ production เท่านั้น |

## ความปลอดภัยของเด็กและข้อมูลส่วนบุคคล

- เก็บแค่ **ช่วงอายุ** (UNDER_13 / TEEN_13_17 / ADULT_18_PLUS) ไม่เก็บวันเกิด
- device id เก็บเป็น hash ใช้ตรวจการสร้างบัญชีจำนวนมากเท่านั้น
- IP เก็บเป็น hash ใน audit log
- ชื่อผู้เล่นห้ามมี @ . / และตัวเลขติดกัน 5 หลักขึ้นไป (กันเบอร์โทร/ไอดีไลน์/URL) และมี word filter เบื้องต้น
- ผู้เล่นอายุต่ำกว่า 13 ปีผูกอีเมลไม่ได้จนกว่าจะมีการยืนยันจากผู้ปกครอง (`parental_consent_at`) ซึ่งยังต้องออกแบบขั้นตอนยืนยันใน Phase ถัดไป
- **ทำแล้วใน Phase 3** (มี test ใน `social.e2e`, `realtime.e2e`):
  - ผู้เล่นมีปฏิสัมพันธ์ได้เฉพาะ **ช่วงอายุเดียวกัน** (`canInteract` ใน shared-contracts): ขอเป็นเพื่อน เชิญ เห็นกันในเมือง ได้ยินข้อความ ผู้ใหญ่จึงติดต่อเด็กไม่ได้เลย
  - ไม่มีแชตพิมพ์อิสระ มีเฉพาะข้อความสำเร็จรูป 10 แบบ
  - ข้อมูลผู้เล่นที่คนอื่นเห็นในเมือง: ชื่อ เลเวล avatar ตำแหน่ง เท่านั้น
  - บล็อกได้ทุกคนทุกเมื่อ มีผลทันทีทั้ง REST และ WebSocket ทั้งสองทิศทาง; ผู้ถูกบล็อกไม่รู้ว่าถูกบล็อก (ได้ error เดียวกับกรณีอื่น)
  - รายงานพร้อมบล็อกได้ในขั้นเดียว รายงานซ้ำไม่สร้างแถวซ้ำ จำกัด 20 ครั้ง/ชม.
  - WebSocket: ยืนยันตัวตนตอน handshake, จำกัดความถี่ต่อ socket, server ตรวจตำแหน่ง/ความเร็ว, payload สูงสุด 4 KB

ก่อนเปิดให้บริการจริงต้องให้นักกฎหมายตรวจสอบตาม **PDPA (พ.ร.บ.คุ้มครองข้อมูลส่วนบุคคล)**, นโยบาย Google Play Families และ COPPA/GDPR-K หากเปิดตลาดต่างประเทศ

## การชำระเงินจริง

- ไม่เก็บข้อมูลบัตรเครดิต ใช้ Google Play Billing เท่านั้น
- `POST /purchases/verify` ต้องตรวจ purchase token กับ Google ที่ server ก่อนให้ของ
- **ตอนนี้ adapter ของ Google Play ยังไม่ทำงาน** และตอบ 503 `BILLING_NOT_CONFIGURED` โดยตั้งใจ จนกว่าจะตั้งค่าบัญชี Play Console, service account และทดสอบ Sandbox สำเร็จ (Phase 6)
- `DEV_MOCK` ใช้ทดสอบ flow ในเครื่องเท่านั้น ถูกปฏิเสธใน production
- ที่ต้องทำใน Phase 6: acknowledge/consume ภายใน 3 วัน, Real-time Developer Notifications สำหรับคืนเงิน/ยกเลิก, Voided Purchases API

## สิ่งที่ยังเหลือ

- ระบบผู้ดูแล (admin console) สำหรับตรวจรายงาน ระงับบัญชี และเปลี่ยนชื่อที่ไม่เหมาะสม (ตอนนี้รายงานถูกเก็บแต่ยังไม่มีหน้าจัดการ)
- Rate limit แยกตามผู้เล่น (ตอนนี้ตาม IP ซึ่งผู้เล่นหลายคนใน NAT เดียวกันจะแชร์โควตา)
- Word filter ภาษาไทย/อังกฤษ/จีนแบบครบถ้วน และระบบรีวิวโดยคน
- Admin API และ RBAC สำหรับปรับราคา/ดูรายงาน
- การยืนยันอีเมลและรีเซ็ตรหัสผ่าน (ต้องใช้บริการส่งอีเมล)
