# Android Build

> **ยังไม่มีการ build APK/AAB** เครื่องที่ใช้พัฒนา Phase 1–2 ไม่มี Unity Editor และ Android SDK

## สิ่งที่ต้องติดตั้ง (Phase 4)

1. Unity Hub + Unity **6 LTS** พร้อมโมดูล Android Build Support (OpenJDK, Android SDK & NDK)
2. สร้างโปรเจกต์ที่ `apps/game-client` (template: Universal 3D หรือ 2D URP)
3. Player Settings: Package name `com.<company>.happyworld`, Minimum API Level 24+, Target API ตามที่ Google Play กำหนดในปีที่ส่ง, Scripting Backend IL2CPP, ARM64
4. เก็บ keystore นอก repo (`*.keystore` อยู่ใน .gitignore) และสำรองไว้อย่างปลอดภัย

## Build

- พัฒนา: File → Build Settings → Android → Build (APK) หรือ Build And Run
- ส่ง Play Store: เปิด "Build App Bundle (Google Play)" ได้ไฟล์ `.aab`
- ทดสอบ Billing: อัปโหลดไปที่ Internal testing track และเพิ่ม License testers ใน Play Console

## การเชื่อม backend จากมือถือ

- Emulator: `http://10.0.2.2:3000/api/v1`
- เครื่องจริงในวง LAN: `http://<IP เครื่องพัฒนา>:3000/api/v1` (Android 9+ บล็อก HTTP ต้องเปิด cleartext สำหรับ dev build เท่านั้น)
- Production: HTTPS เท่านั้น
