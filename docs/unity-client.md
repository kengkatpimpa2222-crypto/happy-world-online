# Unity Client: วิธีเปิด รัน และ build

Unity project อยู่ที่ `apps/game-client` เกมสร้างตัวเองตอนรัน (เมือง ตัวละคร UI) จึง **ไม่ต้องลากวาง prefab หรือตั้งค่า scene เอง**

## สิ่งที่ต้องมี

| เครื่องมือ | หมายเหตุ |
|---|---|
| Unity Hub + **Unity 6 LTS (6000.0.x)** | ตอนติดตั้งให้ติ๊ก **Android Build Support** พร้อม OpenJDK และ Android SDK & NDK Tools |
| Backend ของโปรเจกต์ | รันตาม README หลัก (`npm run dev` ที่พอร์ต 3000) |
| (ไม่บังคับ) .NET 8 SDK หรือ Docker | สำหรับรัน test ของ C# Core โดยไม่ต้องเปิด Unity |

## เปิดโปรเจกต์ครั้งแรก

1. Unity Hub → **Add** → เลือกโฟลเดอร์ `apps/game-client` → เปิดด้วย Unity 6 LTS
2. รอ Unity ดาวน์โหลด package (uGUI, Newtonsoft Json) และ compile
3. ครั้งแรกสคริปต์ `ProjectSetup` จะสร้าง `Assets/_Project/Scenes/Main.unity` และตั้ง Player Settings ให้อัตโนมัติ (ทำซ้ำได้ที่เมนู **Happy World → Apply Project Settings**)
4. Commit ไฟล์ `.meta` และ `ProjectSettings/` ที่ Unity สร้างขึ้นเข้า git

## เล่นใน Editor

1. เปิด backend ไว้ก่อน (`http://localhost:3000`)
2. กด **Play** ใน scene ไหนก็ได้
3. เลือกช่วงอายุ → เริ่มเล่น → ตั้งชื่อและแต่งตัว → เข้าเมือง
4. เดินด้วย WASD/ลูกศร หรือลาก joystick มุมซ้ายล่าง เดินไปที่วงสีเหลืองเพื่อย้ายโซน คลิกผู้เล่นอื่นเพื่อเพิ่มเพื่อน บล็อก หรือรายงาน

ทดสอบหลายคนพร้อมกัน: build เป็น PC (File → Build Profiles → Windows/Mac) แล้วเปิดหลายหน้าต่าง หรือเปิด Editor + มือถือ ทุก instance เป็นผู้เล่นใหม่ได้ด้วยการกด "ออกจากระบบ" ในหน้าโปรไฟล์ **ต้องเลือกช่วงอายุเดียวกัน** จึงจะเห็นกัน (กฎความปลอดภัยเด็ก)

## Build APK (Android)

- ใน Editor: เมนู **Happy World → Build Android APK (Development)** ได้ไฟล์ที่ `apps/game-client/Builds/Android/HappyWorldOnline-dev.apk`
- แบบ command line:
  ```bash
  <Unity> -batchmode -quit -projectPath apps/game-client \
    -executeMethod HappyWorld.EditorTools.ProjectSetup.BuildAndroidDev -logFile build.log
  ```
  exit code 0 = สำเร็จ
- ติดตั้ง: `adb install -r apps/game-client/Builds/Android/HappyWorldOnline-dev.apk`

### ที่อยู่ server บนมือถือ

| อุปกรณ์ | URL |
|---|---|
| Unity Editor / PC | `http://localhost:3000` (ค่าเริ่มต้น) |
| Android Emulator | `http://10.0.2.2:3000` (ค่าเริ่มต้นบน Android) |
| มือถือจริงใน Wi-Fi เดียวกัน | `http://<IP ของเครื่องที่รัน backend>:3000` พิมพ์ในช่อง "Server (dev)" หน้าแรก (มีเฉพาะ development build) และเปิด firewall พอร์ต 3000 |

`http://` ใช้ได้เฉพาะ **development build** (`insecureHttpOption = DevelopmentOnly`) build สำหรับปล่อยจริงต้องใช้ `https://` และ `wss://`

## โครงสร้างโค้ด

```
Assets/_Project/
  Scripts/Core/      (HappyWorld.Core.asmdef, noEngineReferences) ไม่พึ่ง Unity, ทดสอบด้วย dotnet test
    Api/             HappyWorldApi (REST ทุก endpoint ที่เกมใช้), DTO, ApiException, TokenStore
    Net/             IHttpTransport, DotNetHttpTransport
    Realtime/        SocketIoClient (Socket.IO v5/Engine.IO v4 บน ClientWebSocket), RealtimeClient (event ตาม docs/realtime.md)
    World/           CityState (ผู้เล่นอื่น + interpolation), MovementLimiter (10 Hz, ไม่เกิน 8 หน่วย/วินาที)
    Localization/    Texts (ข้อความไทย/อังกฤษ, error code → ข้อความ)
  Scripts/Unity/     (HappyWorld.Unity.asmdef)
    GameSession.cs   จุดเริ่มเกม (RuntimeInitializeOnLoadMethod), flow login → สร้างตัวละคร → เมือง
    Net/             UnityWebRequestTransport, PlayerPrefsTokenStore, MainThread
    World/           CityBuilder (สร้างโซนจาก GET /city), AvatarView, LocalPlayerController, CameraFollow
    UI/              UiKit (uGUI จากโค้ด), VirtualJoystick, GameScreens (ทุกหน้าจอ)
  Editor/            ProjectSetup (ตั้งค่า + build APK)
  Resources/         HWOFont.otf (ฟอนต์ไทย)
Tests/HappyWorld.Core.Tests   xunit: unit + integration กับ backend จริง
```

แนวคิด: ทุกอย่างที่ test ได้โดยไม่ต้องมี Unity (protocol, API, กฎการเดิน, สถานะเมือง) อยู่ใน Core ส่วน Unity layer บางที่สุด

## ข้อควรรู้

- **ฟอนต์ไทย**: ใช้ Loma (TLWG, GPL-2+ with font exception) ที่ `Resources/HWOFont.otf` เพื่อให้ไทยแสดงผลได้ทุกเครื่อง uGUI Text แบบ legacy วางสระบน/วรรณยุกต์ได้ไม่สวยทุกกรณี ก่อนปล่อยจริงแนะนำเปลี่ยนเป็นฟอนต์ OFL (Sarabun, Noto Sans Thai) และย้ายไป TextMeshPro + font asset ที่ทำ Thai shaping
- **Refresh token** เก็บใน PlayerPrefs (ไม่เข้ารหัส) ใช้ได้เฉพาะช่วงพัฒนา ก่อนปล่อยต้องเปลี่ยนเป็น Android Keystore / iOS Keychain (`ITokenStore` แทนได้ทันที)
- **ภาพ**: ตัวละครและเมืองเป็น primitive (กล่อง ทรงกระบอก ทรงกลม) เพื่อให้เล่นได้ก่อน เปลี่ยนเป็นโมเดลจริงได้ใน `AvatarView` และ `CityBuilder` โดย layout ยังมาจาก server
- **Input**: ใช้ Input Manager แบบเดิม (ไม่ต้องลง Input System package) ถ้า Unity ถามให้เปิด Input System ให้ตอบ No หรือตั้ง Active Input Handling = Both
- Client ไม่คำนวณเงินหรือรางวัลเอง แสดงตามที่ server ตอบเท่านั้น ปุ่มซื้อสร้าง `Idempotency-Key` ใหม่ต่อการกดหนึ่งครั้ง

## ทดสอบ C# Core โดยไม่ต้องเปิด Unity

```bash
./scripts/test-game-client.sh                                   # unit tests
HWO_API_URL=http://localhost:3000 ./scripts/test-game-client.sh # + integration กับ backend ที่รันอยู่
```

Integration test สร้างบัญชี guest ทิ้งไว้ ใช้กับ backend สำหรับพัฒนาเท่านั้น และควรรัน backend ด้วย `RATE_LIMIT_GUEST_PER_HOUR=1000` (ค่าเริ่มต้น 10 ต่อชั่วโมงจะเต็มหลังรันไม่กี่รอบ)
