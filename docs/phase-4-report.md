# รายงาน Phase 4: Unity Client + เมืองออนไลน์ 3D + Character Controller + เชื่อม Backend

วันที่: 13 ต.ค. 2026 · branch `phase-4-unity-client` (merge เข้า `main` แบบ fast-forward)

## สรุปสั้น

- **เขียนครบ**: Unity client ที่เล่นได้ตั้งแต่ login ถึงเดินในเมืองเห็นผู้เล่นอื่น พร้อมทุกฟีเจอร์ที่ขอ
- **ทดสอบจริงแล้ว**: ส่วน C# Core (API, Socket.IO, การเดิน, สถานะเมือง) compile ด้วย .NET 8 และผ่าน **26/26 test** รวม integration กับ backend ที่รันอยู่จริง
- **ยังไม่ได้ทำ**: เปิดใน Unity Editor, กด Play, build APK เพราะเครื่องนี้ไม่มี Unity Editor และ Android SDK **จึงยังไม่มี APK**

## ก่อนเริ่ม (ตรวจจากเครื่องและ repository จริง)

| รายการ | ผล |
|---|---|
| Git | `main` สะอาด, Phase 1–3 ครบ |
| Unity Editor / Unity Hub / Android SDK | ไม่มี |
| .NET SDK | ไม่มีในเครื่อง, สคริปต์ติดตั้งของ Microsoft ถูก proxy บล็อก → ใช้ Docker image `mcr.microsoft.com/dotnet/sdk:8.0` แทน |
| สัญญา API / WebSocket | อ่านจากโค้ดจริง: `apps/backend/src/**`, `packages/shared-contracts/src/{schemas,social,errors}.ts`, `docs/realtime.md` (ไม่เดา) |
| Backend | PostgreSQL 16 + Redis 7 + `node dist/main.js` พอร์ต 3000 |

เวอร์ชัน Unity ที่เลือก: **Unity 6 LTS (6000.0.x)** เพราะเป็น LTS ปัจจุบัน รองรับ Android API 24+ และ `ClientWebSocket` บน IL2CPP

## สิ่งที่สร้าง

ผู้เล่นทำได้ (ทุกข้อเรียก API จริงของ backend เดิม ไม่ได้สร้าง backend ใหม่ และไม่ได้แก้ backend):

| ฟีเจอร์ | ทำงานอย่างไร |
|---|---|
| Guest login | เลือกช่วงอายุ → `POST /auth/guest`; เปิดแอปครั้งต่อไปใช้ refresh token เข้าบัญชีเดิม |
| สร้าง/แต่งตัวละคร | ตั้งชื่อ, สีตัว 8 สี, ชุด/ผม/เครื่องประดับจากของที่มี → `PATCH /player/profile` (server กรองชื่อและตรวจว่ามีของจริง) |
| เดินในเมือง 3D | 5 โซนสร้างจาก `GET /city`, joystick บนจอ + WASD, กล้องตาม, เดินเข้าวงสีเหลืองเพื่อย้ายโซน |
| เห็นผู้เล่นอื่น | Socket.IO `/realtime`: เข้า/ออก/เดิน แบบ realtime พร้อม interpolation, server แก้ตำแหน่งถ้าเดินเร็วเกิน |
| ข้อความสำเร็จรูป | 10 ข้อความจาก server แสดงเป็นบอลลูนเหนือหัว |
| โปรไฟล์ | ชื่อ, เลเวล, XP, รหัสเพื่อน, แก้ตัวละคร, ออกจากระบบ |
| เพื่อน | เพิ่มด้วยรหัสเพื่อนหรือแตะผู้เล่น, รับ/ปฏิเสธคำขอ, รายชื่อพร้อมสถานะออนไลน์และโซน, ชวนมาโซนเดียวกัน, ลบเพื่อน, แจ้งเตือนคำขอใหม่แบบ realtime |
| บล็อก / รายงาน | แตะผู้เล่น → บล็อก (หายจากจอทันที) หรือรายงาน 6 เหตุผล (บล็อกให้ด้วย), เลิกบล็อกในหน้าเพื่อน |
| ภารกิจ | รายการ, เริ่ม, รับรางวัล (ยอดจาก server) |
| กระเป๋า | ทอง/เพชร, ของที่มี, รายการเงินล่าสุด |
| ร้านค้า (จำลอง) | ซื้อด้วยทอง/เพชรในเกม, `Idempotency-Key` ใหม่ทุกการกด, ไม่มีการจ่ายเงินจริง |
| ร้านของฉัน | ปรับราคา, เติมสต็อก, เปิดขาย 1 รอบ (ผลขายและคำแนะนำจาก server), อัปเกรด |
| เชื่อมต่อใหม่ | หลุดแล้วมีปุ่มเชื่อมต่อใหม่ (refresh token ก่อน), แจ้งเมื่อบัญชีถูกใช้จากเครื่องอื่น |

ไฟล์ใหม่ทั้งหมดอยู่ใน `apps/game-client/` (24 ไฟล์ C#/asmdef/config, ประมาณ 3,700 บรรทัด) + `docs/unity-client.md`, `docs/phase-4-report.md`, `scripts/test-game-client.sh` รายละเอียดโครงสร้างดู [unity-client.md](unity-client.md)

แก้ไฟล์เดิม: `README.md`, `apps/game-client/README.md`, `.gitignore` เท่านั้น **ไม่ได้แตะ backend, schema, wallet, store, quest หรือ WebSocket**

## คำสั่งที่รันจริงและผลลัพธ์

| คำสั่ง | ผล |
|---|---|
| `dotnet test` (unit, .NET 8 ใน Docker, `LangVersion 9` เท่ากับที่ Unity รองรับ) | ✅ 21/21: Socket.IO packet parser/encoder, MovementLimiter, CityState, error mapping |
| `HWO_API_URL=http://localhost:3000 dotnet test` | ✅ **26/26** (เพิ่ม integration 5 ชุดกับ backend จริง) |
| `HWO_API_URL=... ./scripts/test-game-client.sh` | ✅ 26/26 (ยืนยันว่าสคริปต์ใช้งานได้) |
| Type-check Core + Unity layer (ไม่รวม Editor script) กับ reference DLL ของ UnityEngine 2021.3 + uGUI (NuGet `UnityEngine.Modules`, `Unity3D.UnityEngine.UI`, ใช้ใน scratch เท่านั้น ไม่ได้ commit) | ✅ build ผ่าน 0 error หลังแก้ 1 จุด |
| `npm test` (backend unit) | ✅ 31/31 (ยืนยันว่าไม่กระทบ backend) |

Integration test ที่ผ่าน ใช้โค้ดเดียวกับที่เกมใช้:
1. Guest → เข้าบัญชีเดิมด้วย refresh token → ตั้งชื่อไทย + สีตัว → ชื่อมีเบอร์โทรถูกปฏิเสธ → ทอง 500 → ซื้อของ 2 ครั้งด้วย key เดิม หักเงินครั้งเดียว → เริ่มภารกิจ → รับรางวัลก่อนเสร็จถูกปฏิเสธ → มีร้าน 1 ร้าน
2. ส่งคำขอเพื่อนด้วยรหัส (ตัวพิมพ์เล็ก) → รับ → ปฏิเสธอีกคำขอ → รายงาน + บล็อก → ผู้ถูกบล็อกส่งคำขอไม่ได้
3. ผู้เล่น 2 คนเข้า PLAZA เห็นกัน → เดินแล้วอีกฝั่งเห็นตำแหน่งใหม่ → กระโดดไกลเกินถูก server แก้ตำแหน่ง → ข้อความสำเร็จรูปถึงอีกฝั่ง → ข้อความที่ไม่อยู่ในรายการถูกปฏิเสธ → heartbeat → ปิดแอปแล้วอีกฝั่งเห็นออก
4. เด็กต่ำกว่า 13 กับผู้ใหญ่ในโซนเดียวกันไม่เห็นกัน และส่งคำขอเพื่อนหากันไม่ได้
5. token ปลอมถูกปฏิเสธตอนเชื่อมต่อ WebSocket

### ปัญหาที่เจอและแก้แล้ว

1. **รอ ack แล้วค้างตลอดไป**: ตอนแรกส่ง ack เข้าคิวที่ทำงานเฉพาะตอน `Pump()` ถ้าโค้ดรอ ack โดยไม่ pump จะค้าง (test ค้างจนหมดเวลา 10 นาที) → ให้ ack ปลด Task โดยตรง, event อื่นยังผ่านคิวเพื่ออยู่บน main thread ของ Unity
2. **`Avatar` ชนกับ `UnityEngine.Avatar`**: เจอจาก type-check → ใช้ alias
3. Test ตั้งสมมติฐานผิดว่าชื่อที่มีเบอร์โทรได้ `DISPLAY_NAME_REJECTED` จริง ๆ schema regex ตีกลับก่อนเป็น `VALIDATION_FAILED` → test ยอมรับทั้งสองแบบ
4. Rate limit การสร้าง guest (10/ชม./IP) ทำงานถูกต้องและทำให้ test รอบหลังล้ม → รัน backend สำหรับ test ด้วย `RATE_LIMIT_GUEST_PER_HOUR=1000` (ตั้งผ่าน env ที่มีอยู่แล้ว ไม่ได้แก้โค้ด) และล้าง key `hwo:throttle:*` ใน Redis ของเครื่องพัฒนา
5. Shader: `Shader.Find` อาจได้ null บนมือถือ → ใช้ material ของ primitive ที่ Unity ใส่ใน build เสมอ

## สิ่งที่ยังรอ Unity Editor (ยังไม่ได้ยืนยัน)

| รายการ | สถานะ |
|---|---|
| เปิดโปรเจกต์ใน Unity 6 แล้ว compile ผ่าน | ยังไม่ได้ลอง (type-check กับ Unity 2021.3 ผ่าน แต่ไม่เท่ากับ Unity 6 จริง) |
| `ProjectSetup.cs` (Editor script ตั้งค่า + build APK) | **ยังไม่ได้ compile เลย** (หา reference DLL ของ UnityEditor 6 ไม่ได้) |
| กด Play: หน้าตา UI, ขนาดปุ่ม, กล้อง, joystick | ยังไม่เคยเห็นบนจอจริง อาจต้องปรับตำแหน่ง/ขนาด |
| Build APK และรันบนมือถือ/emulator | **ยังไม่มี APK** |
| `ClientWebSocket` บน Android IL2CPP | ควรใช้ได้ใน Unity 6 แต่ยังไม่ได้ทดสอบบนเครื่องจริง |
| การแสดงสระ/วรรณยุกต์ไทยด้วย uGUI Text | อาจซ้อนกันบางตัว (ข้อจำกัดของ Text แบบเก่า) |

ขั้นตอนตรวจบนเครื่องที่มี Unity อยู่ใน [unity-client.md](unity-client.md): เปิดโปรเจกต์ → Play → เมนู Happy World → Build Android APK

## ข้อจำกัด / งานต่อ

1. **ข้อความ Phase 4 ถูกตัดท้าย** ที่ "เน้นให้ตัวเกมเล่นได้จริงก่อน ไม่ต้องสร้างฟีเจอร์ทุก..." จึงตีความว่าให้เน้นเล่นได้จริงมากกว่าครบทุกฟีเจอร์ บ้าน/เฟอร์นิเจอร์และมินิเกมยังไม่ได้ทำ (โซนมีแล้วแต่ยังว่าง)
2. ภาพเป็น primitive ทั้งหมด ยังไม่มีโมเดล/อนิเมชัน
3. Refresh token อยู่ใน PlayerPrefs (ไม่เข้ารหัส) ต้องเปลี่ยนเป็น Keystore/Keychain ก่อนปล่อย
4. ฟอนต์ Loma เป็น GPL-2+ with font exception ควรเปลี่ยนเป็น OFL + TextMeshPro ก่อนปล่อยเชิงพาณิชย์
5. ภาษาอังกฤษมีแค่ไม่กี่ข้อความ, ภาษาจีนยังไม่มี (โครงสร้างพร้อมเพิ่ม)
6. ยังไม่มีหน้าผูกอีเมล (API มีแล้ว `LinkAccountAsync`); guest ที่ออกจากระบบจะเข้าบัญชีเดิมไม่ได้ (มีคำเตือนในเกม)
7. Backend Docker image ยังไม่ได้ build สำเร็จ (ข้อจำกัดเดิมของ sandbox)

## คำสั่งสำหรับทำงานต่อ

```bash
# backend
./scripts/dev-setup.sh && npm run dev
# test C# Core (ไม่ต้องมี Unity)
HWO_API_URL=http://localhost:3000 ./scripts/test-game-client.sh
# บนเครื่องที่มี Unity 6 LTS + Android Build Support
<Unity> -batchmode -quit -projectPath apps/game-client \
  -executeMethod HappyWorld.EditorTools.ProjectSetup.BuildAndroidDev -logFile build.log
adb install -r apps/game-client/Builds/Android/HappyWorldOnline-dev.apk
```
