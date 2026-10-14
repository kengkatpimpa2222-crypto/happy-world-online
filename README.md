# Happy World Online

เกมโลกออนไลน์แนว Social + Life Simulation + Business Simulation สำหรับทุกวัย เริ่มที่ Android ในประเทศไทย

> สถานะปัจจุบัน: **Phase 1–3 เสร็จและผ่านการทดสอบแล้ว** (Foundation, Core Game API, Multiplayer: เพื่อน บล็อก รายงาน WebSocket เมืองออนไลน์)
> **Phase 4 (Unity client)**: เปิดด้วย Unity 6000.0.84f1 (Unity 6 LTS) compile ผ่าน, build Android APK (development) ผ่าน และ QA บน Android emulator ครบ 14 ข้อแล้ว (Phase 4.5) ยังไม่ได้ทดสอบบนมือถือจริง ดู [docs/phase-4-5-report.md](docs/phase-4-5-report.md), [docs/phase-4-report.md](docs/phase-4-report.md) และ [docs/unity-client.md](docs/unity-client.md)

## โครงสร้างโปรเจกต์

```
happy-world-online/
├── apps/
│   ├── backend/              NestJS 11 + Prisma 6 + PostgreSQL (+ Redis)
│   │   ├── prisma/           schema.prisma และ migrations
│   │   ├── src/              โค้ด API แยกตามโมดูล (auth, player, wallet, quests, store, business, purchases)
│   │   └── test/             integration tests (ใช้ฐานข้อมูลทดสอบแยก)
│   └── game-client/          Unity 6 project (Phase 4): Assets/_Project, Tests (dotnet)
├── packages/
│   └── shared-contracts/     zod schemas, error codes, ค่าสมดุลเกม ใช้ร่วมกันระหว่าง server/client
├── infrastructure/docker/    Docker Compose (Postgres, Redis, backend) และ Dockerfile
├── docs/                     architecture, api, database, security, android-build, deployment
├── scripts/                  dev-setup.sh, test-all.sh
└── .env.example
```

## สิ่งที่ต้องมี

| เครื่องมือ | เวอร์ชันที่ทดสอบแล้ว |
|---|---|
| Node.js | 22.x (npm 10) |
| PostgreSQL | 16 |
| Redis | 7 (ไม่บังคับ ถ้าไม่มีจะใช้ rate limit แบบ in-memory) |
| Docker + Compose | ไม่บังคับ ใช้รัน Postgres/Redis ให้อัตโนมัติ |

## เริ่มต้นแบบเร็ว (มี Docker)

```bash
./scripts/dev-setup.sh      # npm install, เปิด Postgres+Redis, migrate, seed, สร้าง .env พร้อม secret สุ่ม
npm run dev                 # API ที่ http://localhost:3000/api/v1
```

เปิดเอกสาร API (Swagger) ที่ http://localhost:3000/api/docs (เปิดเฉพาะตอนพัฒนา ปิดเสมอใน production)

## เริ่มต้นแบบไม่ใช้ Docker (ใช้ PostgreSQL ในเครื่อง)

```bash
npm install
# สร้างผู้ใช้และฐานข้อมูล (ทำครั้งเดียว)
sudo -u postgres psql -c "CREATE USER hwo WITH PASSWORD 'hwo_dev_password' CREATEDB;"
sudo -u postgres createdb -O hwo happy_world_dev
sudo -u postgres createdb -O hwo happy_world_test

cp apps/backend/.env.example apps/backend/.env   # แล้วแก้ JWT_ACCESS_SECRET
cd apps/backend
npx prisma migrate deploy
npm run db:seed
npm run start:dev
```

## คำสั่งที่ใช้บ่อย (รันจาก root)

| คำสั่ง | ทำอะไร |
|---|---|
| `npm run dev` | รัน API โหมดพัฒนา |
| `npm run build` | build shared-contracts และ backend |
| `npm run lint` | ESLint |
| `npm run typecheck` | TypeScript type check |
| `npm test` | unit tests (ไม่ต้องใช้ฐานข้อมูล) |
| `npm run test:e2e` | integration tests กับ PostgreSQL จริง (ฐาน `happy_world_test` เท่านั้น) |
| `HWO_TEST_REDIS_URL=redis://localhost:6379/15 npm run test:e2e` | รันชุดเดียวกันโดยใช้ Redis จริง (ล้าง Redis db 15 ก่อนรัน) |
| `./scripts/test-all.sh` | รันทั้งหมดข้างบน |
| `./scripts/test-game-client.sh` | compile และ test C# Core ของ Unity client (.NET 8 หรือ Docker) |
| `cd apps/backend && npx prisma migrate dev --name <ชื่อ>` | สร้าง migration ใหม่หลังแก้ schema |

Integration tests จะ **ปฏิเสธการรัน** ถ้า `DATABASE_URL` ใน `apps/backend/.env.test` ไม่ได้ชี้ไปที่ฐานที่ชื่อลงท้ายด้วย `_test` เพื่อป้องกันการล้างข้อมูลผู้เล่นจริง

## API ที่ใช้งานได้แล้ว (prefix `/api/v1`)

| กลุ่ม | Endpoint |
|---|---|
| Health | `GET /health`, `GET /health/live` |
| Auth | `POST /auth/guest`, `POST /auth/link-account`, `POST /auth/login`, `POST /auth/refresh`, `POST /auth/logout` |
| Player | `GET/PATCH /player/profile`, `GET /player/inventory`, `GET /player/wallet` |
| Quests | `GET /quests`, `POST /quests/:questId/start`, `POST /quests/:questId/claim` |
| Store | `GET /store/catalog`, `POST /store/purchases` (ต้องมี header `Idempotency-Key`) |
| Purchases | `POST /purchases/verify` (Google Play ยังปิดอยู่, มี DEV_MOCK สำหรับพัฒนา) |
| Business | `GET /businesses`, `GET /businesses/:id`, `PATCH /businesses/:id/pricing`, `POST /businesses/:id/restock`, `POST /businesses/:id/sell`, `POST /businesses/:id/upgrade` |
| Friends | `POST /friends/requests`, `GET /friends/requests/incoming`, `GET /friends/requests/outgoing`, `POST /friends/requests/:id/accept`, `POST /friends/requests/:id/decline`, `DELETE /friends/requests/:id`, `GET /friends`, `DELETE /friends/:friendId` |
| Block / Report | `POST /players/:id/block`, `DELETE /players/:id/block`, `GET /players/blocked`, `POST /moderation/reports`, `GET /moderation/reports/me` |
| City | `GET /city`, `GET /city/players?zone=` และ WebSocket `/realtime` (ดู [docs/realtime.md](docs/realtime.md)) |

รายละเอียดดู [docs/api.md](docs/api.md) และ [docs/openapi.json](docs/openapi.json)

ยังไม่ได้ทำ: build APK จริง (รอเครื่องที่มี Unity), บ้านและเฟอร์นิเจอร์, มินิเกม, VIP/Season Pass, Google Play Billing จริง, ระบบผู้ดูแล (admin) สำหรับจัดการรายงาน

## หลักการสำคัญ

- **เซิร์ฟเวอร์ตัดสินทุกอย่าง**: ราคา รางวัล ยอดขาย กำไร ถูกคำนวณที่ server เท่านั้น client ส่งได้แค่ "ต้องการทำอะไร"
- **ไม่มี playerId ใน request**: ตัวตนมาจาก access token เสมอ จึงแก้ข้อมูลคนอื่นไม่ได้
- **เงินไม่มีวันติดลบหรือจ่ายซ้ำ**: conditional UPDATE + CHECK constraint + ledger แบบ append-only + idempotency key
- **ไม่บังคับเข้าเกมทุกวัน**: ไม่มี daily streak ที่ลงโทษ ร้านค้าขายเป็นรอบตามที่ผู้เล่นกด
- **ปลอดภัยสำหรับเด็ก**: เก็บแค่ช่วงอายุ ชื่อห้ามมีเบอร์โทร/URL ผู้เล่นต่ำกว่า 13 ปีต้องมีผู้ปกครองยินยอมก่อนผูกอีเมล ผู้เล่นเจอและเป็นเพื่อนได้เฉพาะช่วงอายุเดียวกัน แชตเป็นข้อความสำเร็จรูปเท่านั้น

ดูเพิ่มเติม: [docs/architecture.md](docs/architecture.md) · [docs/realtime.md](docs/realtime.md) · [docs/database.md](docs/database.md) · [docs/security.md](docs/security.md) · [docs/deployment.md](docs/deployment.md) · [docs/android-build.md](docs/android-build.md)
