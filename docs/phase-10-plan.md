# Phase 10 — บ้านหลังแรกและเขตที่อยู่อาศัย (แผน)

Base: `claude/project-thread-0yx7ct` (PR #4, Phase 9 ยังไม่ merge) → branch `claude/phase-10-first-home-jva6lv`

## สิ่งที่มีอยู่แล้ว (ตรวจจาก source)

| ส่วน | สถานะก่อน Phase 10 |
|---|---|
| `player_homes` | มีตั้งแต่ migration แรก สร้างให้ทุกคนตอนสมัคร (`NewPlayerService`) มี `grid_width/height = 8`, `layout_version` แต่ไม่มี API ใดอ่าน/เขียน |
| `home_furniture` | มีตาราง + unique `(home_id, x, y)` + CHECK rotation ∈ {0,90,180,270} ไม่มี API |
| ไอเทมเฟอร์นิเจอร์ | `items.category = FURNITURE`, ขนาดอยู่ใน `metadata.footprint [w, h]` ซื้อได้ใน store (stack ได้) และได้จากเทศกาล |
| คลังของ | `player_inventory` + ledger `inventory_transactions` (Phase 9, append-only, idempotency key) |
| เควสต์ `PLACE_FURNITURE` | มี quest `home_first_furniture` แต่ไม่มีโค้ดใดส่ง event |
| โซน `HOMES` | โซน 40×40 ใน realtime แต่มีแค่บ้านสุ่มประดับ ไม่ผูกกับผู้เล่น |
| `VISIT_HOME`, `HOME_VISIT`, `NICE_HOUSE` | มีใน invite / report context / preset chat แล้ว แต่ยังไม่มีบ้านให้ไป |

## สิ่งที่จะเพิ่ม (ใช้ของเดิมให้มากที่สุด)

**Migration เดียว** `20261018090000_phase10_homes` (เพิ่มคอลัมน์ ไม่สร้างตารางบ้านใหม่)
- `player_homes`: `plot_age_band`, `plot_number`, `claimed_at`, `visit_policy` (PRIVATE / FRIENDS / PUBLIC, ค่าเริ่มต้น FRIENDS), `layout_save_key`
- partial unique `(plot_age_band, plot_number)`: หนึ่งแปลงหนึ่งเจ้าของ, CHECK ให้ 3 คอลัมน์ของการรับบ้านเป็น null หรือไม่ null พร้อมกัน
- `home_furniture`: CHECK x, y ≥ 0
- `InventoryTransactionType`: `HOME_PLACE`, `HOME_REMOVE`, `HOME_STARTER_GIFT`

**ความเป็นเจ้าของเฟอร์นิเจอร์** = `player_inventory.quantity` (ในกระเป๋า) + จำนวนแถว `home_furniture` (วางอยู่) วางของ = ledger −1, เก็บของ = ledger +1 ใน transaction เดียวกับการเปลี่ยน layout จึงไม่มีทางงอก/หาย

**API** (`/api/v1/homes`)
- `GET /homes/me` บ้านของฉัน + layout
- `POST /homes/me/claim` รับบ้านเริ่มต้น (ได้แปลงว่างแรกในเขตของช่วงอายุ + ชุดเฟอร์นิเจอร์ต้อนรับ) ครั้งเดียว
- `GET /homes/me/furniture` เฟอร์นิเจอร์ที่มี: ในกระเป๋า / วางแล้ว / ขนาด
- `PUT /homes/me/layout` บันทึก layout ทั้งชุด (วาง ย้าย หมุน เก็บ) + `baseVersion` (optimistic lock) + `Idempotency-Key`
- `PATCH /homes/me/settings` สิทธิ์ผู้เยี่ยม
- `GET /homes/district?street=n` แปลงบ้านในถนนหนึ่ง (เจ้าของ, เข้าได้ไหม)
- `GET /homes/:ownerId` เยี่ยมบ้าน (ตรวจสิทธิ์ฝั่งเซิร์ฟเวอร์)

**Realtime**: `home:enter` / `home:state` / `home:layout` / `home:kicked` ห้องละบ้าน ส่งเฉพาะคนในบ้าน, เปลี่ยนสิทธิ์/เลิกเป็นเพื่อน/บล็อก แล้วผู้เยี่ยมที่ไม่มีสิทธิ์ถูกพาออกทันที

**Unity**: ถนนในเขต HOMES + บ้านตามแปลงจริง + ป้ายชื่อเจ้าของ, ประตูเข้า/ออก, ห้องในบ้าน 8×8, แผงจัดบ้าน (กระเป๋าเฟอร์นิเจอร์, เงาตัวอย่าง เขียว/แดง, หมุน, ย้าย, เก็บ, บันทึก/ยกเลิก), ตั้งสิทธิ์ผู้เยี่ยม, สถานะโหลด/ผิดพลาด/หลุดเน็ต ตรรกะจัดบ้านอยู่ใน Core (ไม่พึ่ง Unity) จึงเทสด้วย .NET ได้

**ไม่ทำใน Phase 10**: ซื้อ/เช่าที่ดิน, เปลี่ยนพื้น/ผนัง/ธีม, กด "ชอบบ้าน", คราฟต์เฟอร์นิเจอร์, งาน, ตลาดผู้เล่น, กิลด์, เงินจริง
