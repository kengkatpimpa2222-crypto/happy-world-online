# Deploy และ Rollback

> ยังไม่ได้ deploy ไปที่ใดจริง เอกสารนี้คือขั้นตอนที่เตรียมไว้

## Image

```bash
docker build -f infrastructure/docker/backend.Dockerfile -t happy-world-backend:<git-sha> .
```

Container เริ่มด้วย `prisma migrate deploy` → `node dist/database/seed.js` → `node dist/main.js` ทั้งสองขั้นแรกรันซ้ำได้ปลอดภัย

## ตัวแปรที่ต้องตั้งใน production (ผ่าน secret manager ไม่ใช่ไฟล์)

`NODE_ENV=production`, `DATABASE_URL`, `REDIS_URL`, `JWT_ACCESS_SECRET` (สุ่ม ≥ 48 bytes), `CORS_ORIGINS`, `TRUST_PROXY=true` (ถ้าอยู่หลัง load balancer), `BILLING_MODE=disabled` จนกว่า Google Play จะพร้อม

Server จะไม่ยอมเริ่มถ้า secret ดูเป็นค่าตัวอย่างหรือ `BILLING_MODE=mock`

## ขั้นตอน deploy

1. CI: `./scripts/test-all.sh` ต้องผ่าน
2. Backup ฐานข้อมูล (`pg_dump` หรือ snapshot ของผู้ให้บริการ)
3. Deploy image ใหม่ทีละ instance (rolling); readiness probe = `GET /api/v1/health`, liveness = `GET /api/v1/health/live`
4. ตรวจ log และ error rate 15 นาที

## Rollback

- **โค้ดอย่างเดียว**: deploy image เวอร์ชันก่อนหน้า
- **มี migration**: เขียน migration ให้ backward-compatible เสมอ (เพิ่มคอลัมน์ nullable ก่อน, ลบทีหลังใน release ถัดไป) แล้ว rollback แค่โค้ด
- **กรณีเลวร้าย**: restore จาก backup ข้อ 2 (ข้อมูลหลังจากนั้นจะหาย ต้องประกาศผู้เล่น)

ห้าม `prisma migrate reset` หรือ `db push --force-reset` บนฐานข้อมูลที่มีผู้เล่นจริง
