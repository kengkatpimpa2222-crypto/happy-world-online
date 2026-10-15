# Phase 11: รางวัลรายวันและภารกิจ (Daily Rewards & Missions)

Base branch: `claude/phase-10-first-home-jva6lv` (PR #5 ซึ่งต่อจาก PR #4 ทั้งสองยังไม่ merge) ไม่มีระบบเติมเงินจริงในเฟสนี้

## หลักออกแบบ: ชวนให้กลับมา ไม่ลงโทษคนที่หายไป

เป้าหมายข้อ 1 ของโปรเจกต์บอกว่า "ไม่บังคับให้เข้าเกมทุกวัน" ระบบรางวัลรายวันส่วนใหญ่ในตลาดทำตรงข้าม (ขาดวันเดียวรีเซ็ต) เฟสนี้จึงเลือกทางที่ใจดีกว่าแต่ยังได้ผลเรื่องการกลับมาเล่น

- **เช็กอิน 7 วันที่ไม่รีเซ็ต**: เช็กอินวันไหนก็เลื่อนหนึ่งขั้น คนที่เล่นแค่จันทร์กับพฤหัสก็ได้ของขวัญวันที่ 7 เหมือนกัน แค่ช้ากว่า ขั้น 7 คือ **ตุ๊กตานางกวัก** เฟอร์นิเจอร์ที่หาที่อื่นไม่ได้ วางในบ้านได้ (เชื่อมกับ Phase 10) และเหมาะกับคนที่เปิดร้าน
- **ภารกิจรายวัน 3 อย่าง** ที่เป็นสิ่งที่เล่นตามปกติใน 5-15 นาทีอยู่แล้ว (ขายของ ออกสำรวจ ทำมินิเกม แปรรูป จัดบ้าน ทำกำไร) ไม่ต้องฝืนทำอะไรแปลก ๆ และแต่ละคนได้ชุดไม่เหมือนกัน
- **ภารกิจรายสัปดาห์นับเป็นจำนวนวัน ไม่ใช่วันติดกัน** เช่น "แวะมาเช็กอิน 4 วันในสัปดาห์นี้ (ไม่ต้องติดกัน)"
- **ไม่ทำให้เสียของ**: ภารกิจที่ทำเสร็จตอน 23:59 แล้วยังไม่ได้กด รับได้ทั้งวันพรุ่งนี้ (รายสัปดาห์ รับได้ทั้งสัปดาห์ถัดไป)
- **ไม่มีภารกิจเพิ่มเพื่อน**: ไม่ให้รางวัลผลักเด็กให้เพิ่มคนแปลกหน้า (มี unit test กันไว้)
- รางวัลเป็น Gold วัตถุดิบ และเฟอร์นิเจอร์ ไม่แจก Gems (สกุลเงินพรีเมียม) เพื่อไม่ให้กระทบระบบรายได้ในอนาคต

### สิ่งที่ผู้เล่นเห็น (Unity)

- ปุ่ม **"รางวัลวันนี้"** ใต้แถบเงินมุมซ้ายบน (ปุ่มใหญ่ กดง่ายบนมือถือ ไม่ชนปุ่มขวา 10 ปุ่มเดิม) มีตัวเลขเมื่อมีของรอรับ เช่น "รางวัลวันนี้ (2)" ถ้ารับครบแล้วแสดง "ภารกิจวันนี้ 2/3"
- เข้าเกมวันใหม่มี toast สุภาพหนึ่งครั้ง "เช็กอินวันนี้รับ 50 ทอง [เช็กอิน]" ไม่มีหน้าต่างเด้งบังเกม
- หน้ารางวัล: หัวข้อบอกรางวัลวันนี้ + ปุ่มเช็กอิน, แถว 7 ช่อง (รับแล้วสีเขียว ✓, วันนี้สีส้ม), ข้อความ "ไม่ต้องเข้าทุกวันก็ได้...", ภารกิจวันนี้พร้อมแถบความคืบหน้าและเวลาที่เหลือ, ภารกิจสัปดาห์นี้ ภารกิจที่พร้อมรับขึ้นก่อน ที่รับแล้วอยู่ล่าง
- ข้อความ HUD อัปเดตเองหลังทำกิจกรรม (ขาย เก็บเงิน แปรรูป รับรางวัล) ทุก 5 นาที และหลังเที่ยงคืนเวลาไทยพอดี

## ความน่าเชื่อถือของรางวัล

| ความเสี่ยง | วิธีกัน | ทดสอบ |
|---|---|---|
| เปลี่ยนนาฬิกาเครื่องเพื่อรับซ้ำ | ใช้เวลา server เท่านั้น (วันไทย UTC+7) client ไม่ส่งวันที่ | e2e ขอบเที่ยงคืน 23:59 / 00:01 |
| กดเช็กอินรัว ๆ / หลายเครื่องพร้อมกัน | upsert มีเงื่อนไข `last_day < today` แถวเดียว + PK (ผู้เล่น, วัน) + ledger key `checkin:<day>:GOLD` | e2e 10 requests พร้อมกัน จ่ายครั้งเดียว |
| กดรับภารกิจซ้ำ | `COMPLETED → CLAIMED` แบบมีเงื่อนไข + ledger key `mission:<period>:<id>:GOLD` | e2e 10 requests พร้อมกัน จ่ายครั้งเดียว |
| ปลอมความคืบหน้า | นับจาก server ใน transaction เดียวกับการกระทำจริง (`QuestsService.recordEvent` เดิม) ไม่มี endpoint ให้ส่งความคืบหน้า | e2e: เติมสต็อกเกินที่เก็บถูกปฏิเสธ ภารกิจไม่ขยับ |
| รับภารกิจของคนอื่น / ของวันอื่น / ส่งรางวัลมาเอง | server เลือกภารกิจเองจาก (ผู้เล่น, วัน) schema `.strict()` | e2e 404/409/400 |
| เงินงอก | Gold ผ่าน `WalletService` ไอเทมผ่าน `InventoryService` ledger เดิม (append-only) ไม่มีการแก้ยอดตรง | e2e ตรวจแถว ledger ทุกชนิด |
| นาฬิกาทดสอบหลุดไป production | `DAILY_CLOCK_OFFSET_HOURS` ≠ 0 ใน production = server ไม่ยอมเริ่ม | unit `env.spec.ts` |

## ไฟล์ที่เปลี่ยน

**Shared contracts**
- `src/daily.ts` (ใหม่): เส้นทางเช็กอิน 7 วัน, pool ภารกิจรายวัน 9 แบบ, ภารกิจรายสัปดาห์ 4 แบบ, คณิตวัน/สัปดาห์เวลาไทย, การเลือกภารกิจแบบ deterministic (FNV-1a)
- `src/quest-objectives.ts` (ใหม่): รายชื่อ objective (มี test ให้ตรงกับ enum ของ Prisma)
- `errors.ts` (5 error ใหม่), `schemas.ts` (`claimMissionSchema`), `index.ts`

**Backend**
- `prisma/schema.prisma`, `prisma/migrations/20261019090000_phase11_daily_rewards/migration.sql` (3 ตารางใหม่ + 4 ค่า enum + CHECK + trigger append-only)
- `src/quests/daily-rewards.service.ts`, `daily-rewards.controller.ts`, `daily-rewards.spec.ts` (ใหม่), `quests.service.ts` (`recordEvent` นับภารกิจด้วย จุดเดียว ไม่ต้องแก้ทุก service), `quests.module.ts`
- `src/database/seed-data.ts` (`furn_nang_kwak`), `src/config/env.ts` + `env.spec.ts`, `.env.example`
- `test/daily-rewards.e2e-spec.ts` (ใหม่ 14 tests), `test/helpers.ts` (`setDailyClock`)

**Unity client**
- Core: `Api/Dtos.cs`, `Api/HappyWorldApi.cs`, `Localization/Texts.cs`, `World/DailyPresenter.cs` (ใหม่: `DailyPresenter` + `DailyTracker`) + `.meta`
- Unity: `GameSession.cs` (`RefreshDaily`, `OnDaily`, nudge), `UI/GameScreens.cs` (ปุ่ม HUD + หน้า `ShowDaily`), `World/HomeInteriorView.cs` (โมเดลนางกวัก)
- Tests: `DailyTests.cs` (ใหม่ 9 tests), `IntegrationTests.cs` (`DailyCheckinOnceAndMissionsFromServerTime`)

**เอกสาร**: `api.md`, `database.md`, รายงานนี้

## ผลทดสอบ (รันจริงใน container นี้)

| คำสั่ง | ก่อนเริ่ม (ตามรายงานล่าสุด) | หลังทำเสร็จ |
|---|---|---|
| `./scripts/test-all.sh` (lint + typecheck + unit + e2e, PostgreSQL 16 + Redis จริง) | unit 83, e2e 164 | **exit 0**, unit **92/92** (11 suites), e2e **178/178** (19 suites) |
| `HWO_API_URL=http://localhost:3000 ./scripts/test-game-client.sh` (.NET 8, integration กับ backend ที่รันจริง) | 92 | **102/102** |
| Type-check โค้ด Unity layer ทั้งหมด (netstandard2.1 + NuGet `UnityEngine.Modules` 2021.3.33 + `Unity3D.UnityEngine.UI` 2020.3.21) | - | **0 errors** (ไม่ใช่ Unity Editor จริง) |

**ยังไม่ได้ทดสอบ (NOT TESTED)**: เปิดใน Unity 6 Editor, build APK, ลองบนมือถือจริง (ขนาดปุ่ม/ตัวอักษรในช่อง 7 วันบนจอเล็ก, การกด, best-fit ของชื่อไอเทมยาว), โมเดลนางกวักในบ้าน container นี้ไม่มี Unity/Android SDK ส่งต่อให้ thread "Unity 6 APK Build และ QA" บนเครื่องผู้ใช้

## จุดที่ควรรู้

- ภารกิจนับตามเวลาที่ server บันทึกการกระทำ ยอดขายออฟไลน์ของร้าน (Phase 5) นับเข้าวันที่ร้านถูก settle ไม่ใช่วันที่ลูกค้าซื้อจริง
- ภารกิจ "แปรรูป" อาจต้องออกสำรวจก่อนเพื่อหาวัตถุดิบ (2 ขั้น) ยังทำได้ใน session เดียว
- เปลี่ยนรางวัล/ภารกิจได้ที่ `daily.ts` โดยไม่ต้อง migrate แต่ถ้าเปลี่ยน pool รายวันกลางวัน ภารกิจของวันนั้นอาจสลับ (ควรเปลี่ยนพร้อม deploy หลังเที่ยงคืน)
- ยังไม่มี analytics ว่าผู้เล่นกลับมากี่วัน (Phase 12 playtest readiness ควรอ่านจาก `daily_checkins` ได้เลย)

## คำสั่งถัดไป

```bash
git fetch origin claude/phase-11-daily-rewards-d1wobb && git checkout claude/phase-11-daily-rewards-d1wobb
npm install && npm run build
cd apps/backend && npx prisma migrate deploy && npm run db:seed
# ลองเช็กอิน "พรุ่งนี้" บนเครื่อง dev: ตั้ง DAILY_CLOCK_OFFSET_HOURS=24 แล้วรีสตาร์ท backend
```
Unity: เปิด `apps/game-client` ใน Unity 6 แล้ว build APK ตาม `docs/android-build.md` จุดที่ควรดูบนมือถือ: ปุ่ม "รางวัลวันนี้" ใต้แถบเงิน, แถว 7 ช่อง, ปุ่มรับภารกิจ, toast ตอนเข้าเกม
