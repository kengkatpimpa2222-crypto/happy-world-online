# Game Client (Unity) — ยังไม่ได้สร้าง

Phase 1–2 ทำเฉพาะ backend เพราะเครื่องพัฒนาไม่มี Unity Editor การเขียน C# โดยไม่ได้ compile และเปิดใน Editor จริงเสี่ยงได้โค้ดที่ใช้ไม่ได้ จึงเลื่อนไปทำใน Phase 4 บนเครื่องที่มี Unity

## โครงสร้างที่วางแผนไว้ (Phase 4)

```
Assets/_Project/
  Scripts/
    Core/        GameBootstrap, ServiceLocator, AutoSave
    Network/     ApiClient (UnityWebRequest), AuthService (เก็บ refresh token), RealtimeClient (WebSocket)
    Data/        DTO ที่ตรงกับ packages/shared-contracts
    Gameplay/    PlayerController (joystick + tap-to-move), CameraFollow, Zones
    UI/          Wallet, Store, Quests, Business, Profile (UI Toolkit)
  Localization/  th, en, zh (Unity Localization package; error code → ข้อความ)
  Scenes/        Boot, Town, Home, MiniGame_Delivery, MiniGame_Market
```

กฎสำคัญ: client ไม่คำนวณรางวัลหรือยอดเงินเอง แสดงผลตามที่ server ตอบกลับเท่านั้น และสร้าง `Idempotency-Key` ใหม่ทุกครั้งที่ผู้เล่นกดซื้อ (retry ใช้ค่าเดิม)
