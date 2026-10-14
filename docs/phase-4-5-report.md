# รายงาน Phase 4.5: Unity 6 LTS + Android APK + QA บน Emulator

วันที่: 14 ต.ค. 2026 · branch `phase-4-5-android` (แตกจาก `main` ที่ `4ae7968`)

## สรุปสั้น

- **Unity 6000.0.84f1 (Unity 6 LTS) compile ผ่านจริง** ทั้ง 3 assembly (`HappyWorld.Core`, `HappyWorld.Unity`, `HappyWorld.Editor`) 0 error 0 warning
- **Build Android APK (Development, debug key) ผ่านจริง** ที่ `apps/game-client/Builds/Android/HappyWorldOnline-dev.apk`
- **QA บน Android Emulator ครบ 14 ข้อ ผ่านทั้งหมด** หลังแก้บั๊กที่เจอเฉพาะตอนรันจริง 8 จุด (ไม่มี test ไหนจับได้มาก่อน เพราะต้องรันใน Unity/Android จริง)
- **Backend Phase 1–3 ไม่ถูกแก้** และยังผ่าน regression ครบ
- **ยังไม่ได้ทดสอบบนมือถือจริง** และยังไม่ได้ build release

## สภาพแวดล้อมที่ใช้จริง

| รายการ | ค่า |
|---|---|
| เครื่อง | Windows 11 Pro (build 26300), RAM 16 GB, Region ภาษาไทย (ปฏิทินพุทธ) |
| Unity | **6000.0.84f1** (`ProjectSettings/ProjectVersion.txt` ถูก pin แล้ว) ติดตั้งที่ `D:\Unity\Editors\6000.0.84f1` |
| Android | Android Build Support + OpenJDK 17.0.18, Android SDK (platforms 34/35/36/37, build-tools 36.0.0, platform-tools 36), NDK r27c (27.2.12479018), Gradle 9.3.1 (มากับ Unity) |
| Emulator | Android Emulator 37.2.12, image `android-34;google_apis;x86_64`, Pixel 6, WHPX acceleration, GPU SwiftShader (OpenGL ES 3.0) |
| Backend | `node dist/main.js` พอร์ต 3000 + PostgreSQL 16 + Redis 7 (Docker) |

## คำสั่งที่รันจริงและผลลัพธ์

| คำสั่ง | ผล |
|---|---|
| `npm run lint` / `npm run typecheck` | ✅ ผ่าน |
| `npm test` (backend unit) | ✅ 31/31 |
| `npm run test:e2e` (backend integration, PostgreSQL จริง) | ✅ 92/92 (11 suites) |
| `dotnet test` C# Core (.NET 8 ใน Docker) + `HWO_API_URL` ชี้ backend จริง | ✅ 26/26 |
| `Unity.exe -batchmode -quit -projectPath apps/game-client` | ✅ compile ผ่าน, exit 0 |
| `Unity.exe -batchmode -quit -projectPath apps/game-client -executeMethod HappyWorld.EditorTools.ProjectSetup.BuildAndroidDev` | ✅ `[HappyWorld] Android build Succeeded` |
| `aapt2 dump badging` | package `com.happyworld.online`, version 0.4.0 (code 4), minSdk 24, targetSdk 36, ABI `arm64-v8a` + `x86_64`, permission `INTERNET` อย่างเดียว, debuggable |
| `apksigner verify --print-certs` | เซ็นด้วย `CN=Android Debug` เท่านั้น ไม่มี production key |
| `adb install` + เปิดแอป | ✅ ติดตั้งและเปิดได้ (ผล QA ด้านล่าง) |

ไฟล์ APK: `apps/game-client/Builds/Android/HappyWorldOnline-dev.apk` (ไม่ commit, อยู่ใน `.gitignore`) ขนาด 140,287,088 ไบต์ SHA-256 `6800607953d8e318f960a4e2f9a6390b7c2d1e98152e65f18ad1210417b896ba` (build ยืนยันรอบสุดท้าย หลังปิด Gradle daemon ทั้งหมด เพื่อพิสูจน์ว่าการแก้ปฏิทินพุทธใช้ได้โดยไม่พึ่ง daemon เดิม) ใช้โค้ดชุดเดียวกับ APK ที่ทดสอบ QA

permission อื่นที่เห็นคือ `com.happyworld.online.DYNAMIC_RECEIVER_NOT_EXPORTED_PERMISSION` ซึ่ง AndroidX สร้างให้แอปใช้ภายในเท่านั้น ไม่ใช่ permission ที่ขอจากผู้ใช้

## QA บน Android Emulator

ผู้เล่นคนที่สองคือบอท Node.js ที่ใช้ REST + Socket.IO จริงของ backend (สร้าง guest, เข้า PLAZA, เดิน, ส่งข้อความสำเร็จรูป, ส่งคำขอเพื่อน) ยืนยันผลฝั่ง server ด้วยการ query PostgreSQL ตรง

| # | รายการ | ผล | หลักฐาน |
|---|---|---|---|
| 1 | ติดตั้งและเปิดแอป | ✅ ผ่าน | หน้าเลือกช่วงอายุขึ้น ภาษาไทยแสดงผล ช่อง Server (dev) = `http://10.0.2.2:3000` |
| 2 | เข้าเล่น Guest | ✅ ผ่าน (หลังแก้บั๊ก #2) | server สร้างบัญชีและแอปไปหน้าสร้างตัวละคร |
| 3 | สร้างตัวละคร | ✅ ผ่าน | เลือกสีตัว ชุด ผม เครื่องประดับ แล้วเข้าเมือง (พิมพ์ชื่อภาษาไทยผ่าน adb ไม่ได้ **ยังไม่ได้ทดสอบการพิมพ์ไทย**) |
| 4 | โหลดโปรไฟล์ | ✅ ผ่าน | ชื่อ เลเวล 1 XP 0/50 รหัสเพื่อน |
| 5 | เดินด้วย joystick | ✅ ผ่าน | ลาก joystick แล้วตัวละครและกล้องเคลื่อนที่ในเมือง 3D |
| 6 | เชื่อม WebSocket | ✅ ผ่าน | บอทเห็น `Player2063` ใน PLAZA และแอปเห็นบอท |
| 7 | เข้าพื้นที่เมือง | ✅ ผ่าน | ลานกลางเมือง (PLAZA) สร้างจาก `GET /city` มีพื้น รั้ว ต้นไม้ บ้าน วงประตู **ยังไม่ได้ทดสอบเดินเข้าวงเพื่อย้ายโซน** |
| 8 | เห็นผู้เล่นอื่น | ✅ ผ่าน | บอทเดินเป็นวงกลมแบบ realtime และมีบอลลูน "สวัสดี!" เหนือหัว |
| 9 | Reconnect | ✅ ผ่าน (แบบกดปุ่ม) | ปิด backend → แถบ "การเชื่อมต่อหลุด" + ปุ่ม "เชื่อมต่อใหม่" → เปิด backend แล้วกด → กลับเข้าเมืองได้ ยังไม่มีการเชื่อมต่อใหม่อัตโนมัติ |
| 10 | Friends, Block, Report | ✅ ผ่าน | แตะบอท → ส่งคำขอเพื่อน (DB: `PENDING`), รับคำขอที่บอทส่งมา → เพื่อน (1), รายงาน "สแปม" → DB `moderation_reports` = `SPAM/OPEN`, `blocked_players` +1 และบอทหายจากจอทันที |
| 11 | ภารกิจและกระเป๋าเงิน | ✅ ผ่าน | รายการภารกิจโหลด, กดเริ่มแล้วขึ้น 0/1, กระเป๋าแสดงทอง/เพชร ของที่มี และรายการเงิน (**ยังไม่ได้ทดสอบกดรับรางวัล**) |
| 12 | ซื้อไอเทมด้วยเงินจำลอง | ✅ ผ่าน | ซื้อชุดนักเรียน 150 ทอง: DB ทอง 500 → 350, ร้านแสดง "(มี 1)" และ HUD อัปเดต; ร้านของฉัน เติมสินค้า + เปิดขาย 1 รอบ: รายรับ 50 รายจ่าย 25 กำไร 25, ทอง 350 → 375 |
| 13 | ปิดและเปิดแอปใหม่ | ✅ ผ่าน | `am force-stop` แล้วเปิดใหม่ เข้าบัญชีเดิมอัตโนมัติ (refresh token) ทองและของยังอยู่ |
| 14 | ตรวจ crash และ log | ✅ ผ่าน | build สุดท้าย: `logcat -b crash` 0 บรรทัด, ไม่มี Unity error ตลอดรอบทดสอบ, หน่วยความจำ ~267 MB PSS |

### ยังไม่ได้ทดสอบ

- **มือถือ Android จริง (ARM64)** ทั้งหมด รวมความลื่นไหลบน GPU จริง (emulator ใช้ software GPU)
- พิมพ์ชื่อและรหัสเพื่อนด้วยคีย์บอร์ดไทยบนมือถือ
- เดินเข้าวงประตูเพื่อย้ายโซน, กดรับรางวัลภารกิจ, อัปเกรดร้าน, ชวนเพื่อน (`friend:invite`)
- หน้าจออัตราส่วนอื่น (ทดสอบแค่ 2400×1080 แนวนอน), แท็บเล็ต
- เด็กต่ำกว่า 13 / วัยรุ่น บนแอปจริง (กฎแยกห้องตามอายุผ่านใน integration test ของ Phase 3–4 แล้ว)

## ปัญหาที่เจอและแก้แล้ว

### เครื่องมือ / การติดตั้ง (ไม่ได้แก้โค้ดเกม)

1. **ตัวติดตั้ง Unity ล้ม exit code 2 ทุกครั้ง** สาเหตุ: ตัวติดตั้งแตกไฟล์ชั่วคราว ~5 GB ลง `%TEMP%` บนไดรฟ์ C ที่เหลือ 5 GB (Unity Hub เก็บไฟล์ดาวน์โหลดไว้ ~10 GB) แก้: ตั้ง `TEMP`/`TMP` ไปไดรฟ์ D แล้วรันตัวติดตั้งแบบ `/S /D=...`
2. **Unity ไม่เห็น license** ทั้งที่ผู้ใช้ activate ใน Hub แล้ว สาเหตุ: Unity Hub เวอร์ชัน Microsoft Store (MSIX) เก็บ `UnityEntitlementLicense.xml` ในโฟลเดอร์ sandbox ของแอป แก้: คัดลอกไปที่ `%LOCALAPPDATA%\Unity\licenses` (แนะนำใช้ Unity Hub ตัวติดตั้งจาก unity.com แทนเวอร์ชัน Store)

### Build (แก้ใน `Assets/_Project/Editor/ProjectSetup.cs`)

3. **Gradle ล้มที่ `:launcher:mergeDebugJavaResource` (`VerifyException` ใน `MsDosDateTimeUtils.packDate`)** สาเหตุ: Windows ตั้ง region ไทย Java จึงใช้ปฏิทินพุทธ (ปี 2569) เกินปีสูงสุดของวันที่ใน zip (2107) การตั้งใน `gradle.properties` และ `JAVA_TOOL_OPTIONS` ใช้ไม่ได้ เพราะ Unity ส่ง `-Dorg.gradle.jvmargs` ทับ และไม่ส่งต่อ env แก้: `GradleLocaleFix` (`IPostGenerateGradleAndroidProject`) เติม `java.util.Locale.setDefault(java.util.Locale.US)` ท้าย `settings.gradle` ที่ Unity export ทุก build ยืนยันโดย build ใหม่หลังปิด Gradle daemon ทั้งหมด
4. **แอปเด้งทันทีบน emulator (SIGILL)** สาเหตุ: emulator เป็น x86_64 ต้องแปลงคำสั่ง ARM64 และ `libunity.so` ใช้คำสั่งที่ตัวแปลงไม่รองรับ แก้: dev build ใส่ทั้ง ARM64 และ x86_64 (`Apply()` ยังตั้ง ARM64 อย่างเดียวสำหรับ build อื่น)
5. **จอสีม่วงทั้งจอ** สาเหตุ: โปรเจกต์ไม่มี `ProjectSettings` มาก่อน รายการ Always Included Shaders จึงว่าง และทุกอย่างสร้างจากโค้ด ไม่มี asset อ้าง shader build จึงตัดทิ้ง แก้: `EnsureAlwaysIncludedShaders()` ใส่ `Legacy Shaders/Diffuse`, `Unlit/Color`, `Skybox/Procedural`, `UI/Default`, `Sprites/*`, `Hidden/Cube*` (การใส่ `Standard` หรือ shader จาก default resources ทำให้ build ล้มที่ `unity_builtin_extra` จึงไม่ใส่)
6. **พื้นเมืองหาย + แตะผู้เล่นไม่ได้** สาเหตุ: IL2CPP "Strip Engine Code" ตัด `BoxCollider`/`SphereCollider`/`MeshCollider` ออก เพราะ `CreatePrimitive` เพิ่ม collider ตอนรันเท่านั้น แก้: `PlayerSettings.stripEngineCode = false` (APK ใหญ่ขึ้น)

### โค้ดเกม

7. **เข้าเล่น Guest แล้วค้าง** (`UnityException: TrySetSetString can only be called from the main thread`) สาเหตุ: `HappyWorldApi` ใช้ `ConfigureAwait(false)` จึงบันทึก token จาก thread pool แต่ `PlayerPrefs` ใช้ได้บน main thread เท่านั้น แก้: `PlayerPrefsTokenStore` เก็บ token ในหน่วยความจำ และส่งการเขียน `PlayerPrefs` ไป main thread ผ่าน `MainThread.Post`
8. **ราคาและยอดเงินไม่มีหน่วย / HUD ไม่อัปเดตหลังซื้อ** สาเหตุ: ใช้ emoji 🪙💎 ที่ฟอนต์ไทยไม่มี glyph จึงหายไป ปุ่มซื้อด้วยทองกับเพชรแยกได้แค่ด้วยสี แก้: ใช้คำ "ทอง"/"เพชร" จาก `Texts` และเรียก `RefreshHud()` หลังซื้อ
9. `CityBuilder.Mat` เปลี่ยนจาก material ของ primitive (Standard) เป็น `Legacy Shaders/Diffuse` เพื่อให้ shader อยู่ใน build แน่นอนและเบากว่าบนมือถือ

อื่น ๆ: เพิ่ม `.gitattributes` (`*.sh eol=lf`) เพราะ `core.autocrlf=true` บน Windows ทำให้ `postgres-init/01-test-db.sh` เป็น CRLF และ container สร้างฐาน test ไม่ได้, เพิ่ม `.utmp/` และ `*.slnx` ใน `.gitignore`, commit `ProjectSettings/`, `Packages/packages-lock.json`, `Scenes/Main.unity` และไฟล์ `.meta` ตามที่ `docs/unity-client.md` ระบุ

## ความปลอดภัยและการตั้งค่า

- ไม่มี secret ใน source code หรือ APK: client มีแค่ URL ของ backend (ค่าเริ่มต้น `http://10.0.2.2:3000` บน Android) ไม่มี JWT secret, service account หรือ key ใด ๆ (ตรวจด้วยการค้นคำ key/secret/token/password ในไฟล์ทั้งหมดของ repo)
- `insecureHttpOption = DevelopmentOnly`: `http://` ใช้ได้เฉพาะ development build, release ต้องใช้ `https://`/`wss://`
- ไม่มีการสร้างหรือใช้ production keystore (`AndroidKeystoreName` ว่าง), APK เซ็นด้วย debug key ของ Android SDK
- `localhost` บนมือถือไม่ใช่เครื่อง backend: emulator ใช้ `10.0.2.2`, มือถือจริงต้องพิมพ์ IP ของเครื่องในช่อง Server (dev)
- Target API: Unity ตั้ง "Automatic" ได้ 36 (สูงสุดที่ติดตั้ง) ซึ่งตรงกับข้อกำหนด Google Play ปี 2026 ควร pin เป็นค่าคงที่ก่อนส่ง Store
- Refresh token ยังอยู่ใน `PlayerPrefs` ไม่เข้ารหัส (ข้อจำกัดเดิมจาก Phase 4)

## วิธีลองเล่นเอง

### บน Emulator ในเครื่องนี้ (ตั้งค่าไว้แล้ว)

```bash
# 1) backend (Docker Desktop ต้องเปิดอยู่)
cd D:/happy-world-online
HWO_REDIS_PORT=6380 HWO_PG_PORT=5434 docker compose -f infrastructure/docker/docker-compose.yml up -d postgres redis
cd apps/backend && RATE_LIMIT_GUEST_PER_HOUR=1000 node dist/main.js

# 2) emulator (อีกหน้าต่าง)
D:/Unity/android/SDK/emulator/emulator.exe -avd hwo_api34 -gpu swiftshader_indirect

# 3) ติดตั้งและเปิดเกม
D:/Unity/android/SDK/platform-tools/adb.exe install -r apps/game-client/Builds/Android/HappyWorldOnline-dev.apk
```

หมายเหตุเครื่องนี้: พอร์ต 5432 และ 6379 มีโปรแกรมอื่นใช้อยู่ จึงใช้ 5434/6380 และ `apps/backend/.env` ชี้พอร์ตเหล่านี้แล้ว (ไฟล์ `.env` ไม่ถูก commit)

### บนมือถือ Android จริง

1. เปิด "ตัวเลือกสำหรับนักพัฒนา" และ "USB debugging" บนมือถือ แล้วต่อ USB (หรือส่งไฟล์ APK ไปที่มือถือแล้วกดติดตั้ง อนุญาต "ติดตั้งแอปที่ไม่รู้จัก")
2. มือถือและคอมต้องอยู่ Wi-Fi เดียวกัน เปิด firewall ของ Windows ให้พอร์ต 3000
3. หน้าแรกของเกม ช่อง **Server (dev)** ใส่ `http://<IP ของคอม>:3000` (ดู IP ด้วย `ipconfig`)
4. อยากเล่นหลายคน: ให้ทุกเครื่องเลือก **ช่วงอายุเดียวกัน** (กฎความปลอดภัยเด็ก) เครื่องที่สองใช้ emulator หรือรันบอท `node scripts/qa-bot.js` (ผู้เล่นปลอมช่วงอายุ 18+ เดินวนใกล้จุดเกิด)

## ข้อเสนอการพัฒนาต่อ (จากการเล่นจริง)

เรียงตามผลต่อ "เกมที่คนอยากกลับมาเล่น":

1. **ทดสอบบนมือถือจริง 2–3 รุ่น** (ราคาประหยัดด้วย) ก่อนเพิ่มฟีเจอร์ เพราะ emulator ใช้ software GPU วัดความลื่นไม่ได้
2. **ความรู้สึกตอนเล่น (game feel)**: ตัวละครเป็นทรงกระบอกเล็กมากบนจอ กล้องสูงไป ชื่อผู้เล่นและบอลลูนข้อความเล็กจนอ่านไม่ออก ป้ายชื่อโซนบนวงประตูก็เล็ก ควรขยายตัวละคร ลดระยะกล้อง และขยายป้าย 2–3 เท่า เพิ่มอนิเมชันเดินและเสียง
3. **ฟอนต์ไทย**: ย้ายไป TextMeshPro + ฟอนต์ OFL (Sarabun/Noto Sans Thai) วรรณยุกต์บางตัวยังวางไม่สวยใน uGUI Text และฟอนต์ไม่มี emoji/ไอคอน ควรใช้รูปไอคอนเหรียญ/เพชรแทน
4. **Reconnect อัตโนมัติ** (ตอนนี้ต้องกดปุ่ม) และแจ้งเตือนเมื่อมีคำขอเพื่อนเข้ามาขณะอยู่ในเมือง
5. **วงจรให้กลับมาเล่น**: ภารกิจ "ตั้งชื่อและแต่งตัวละคร" นับ 0/1 แม้ทำไปแล้วก่อนกดเริ่ม ควรเริ่มภารกิจแรกให้อัตโนมัติ หรือนับย้อนหลัง; เพิ่ม onboarding ช่วง 5 นาทีแรก
6. **บ้านและมินิเกม** (โซนมีแล้วแต่ยังว่าง) ตามเป้าหมาย "แต่งบ้าน เปิดร้าน ทำกิจกรรม"
7. **ก่อนเปิดทดสอบจริง (Phase 5)**: เก็บ token ใน Android Keystore, ใช้ HTTPS/WSS, deploy backend, สร้าง release keystore (ต้องได้รับอนุญาตก่อน) และ AAB สำหรับ Play Console internal testing, ลดขนาด APK (เปิด engine stripping กลับพร้อม `link.xml` แทนการปิดทั้งหมด, build ARM64 อย่างเดียวสำหรับ release)

## คำสั่งสำหรับทำงานต่อ

```bash
# build dev APK (ARM64 + x86_64)
"D:/Unity/Editors/6000.0.84f1/Editor/Unity.exe" -batchmode -quit -projectPath apps/game-client \
  -executeMethod HappyWorld.EditorTools.ProjectSetup.BuildAndroidDev -logFile build.log
# ติดตั้ง
adb install -r apps/game-client/Builds/Android/HappyWorldOnline-dev.apk
# log ของเกม
adb logcat -s Unity
# regression
npm run lint && npm run typecheck && npm test && npm run test:e2e
HWO_API_URL=http://localhost:3000 ./scripts/test-game-client.sh
```

ข้อควรรู้บนเครื่อง Windows ที่ตั้งภาษาไทย: ถ้าเครื่องเหลือที่ไดรฟ์ C น้อย ให้ตั้ง `TEMP`/`TMP` ไปไดรฟ์อื่นก่อนติดตั้งหรือ build Unity
