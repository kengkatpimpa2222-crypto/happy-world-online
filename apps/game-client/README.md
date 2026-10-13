# Game Client (Unity 6 LTS)

Unity client ของ Happy World Online: เมือง 3D ออนไลน์, ตัวละคร, joystick, เพื่อน, บล็อก/รายงาน, ภารกิจ, กระเป๋า, ร้านค้า (เงินในเกม), ร้านของฉัน, ข้อความสำเร็จรูป

- วิธีเปิด รัน และ build APK: [docs/unity-client.md](../../docs/unity-client.md)
- รายงานและสิ่งที่ยังรอ Unity Editor: [docs/phase-4-report.md](../../docs/phase-4-report.md)
- ทดสอบ C# Core: `./scripts/test-game-client.sh` (เพิ่ม `HWO_API_URL=http://localhost:3000` เพื่อทดสอบกับ backend จริง)

กฎสำคัญ: client ไม่คำนวณรางวัลหรือยอดเงินเอง แสดงผลตามที่ server ตอบกลับเท่านั้น และสร้าง `Idempotency-Key` ใหม่ทุกครั้งที่ผู้เล่นกดซื้อ (retry ใช้ค่าเดิม)
