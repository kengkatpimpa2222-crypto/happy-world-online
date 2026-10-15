# Realtime (WebSocket) protocol

Transport: **Socket.IO 4** · namespace `/realtime` · path `/ws` · ชื่อ event ทั้งหมดอยู่ใน `packages/shared-contracts/src/social.ts` (`WsEvent`)

```js
// Unity: ใช้ Socket.IO client สำหรับ C# (เช่น SocketIOUnity) ตั้งค่าเดียวกัน
const socket = io('https://api.example.com/realtime', { path: '/ws', auth: { token: accessToken }, transports: ['websocket'] });
socket.on('ready', () => socket.emitWithAck('zone:join', { zone: 'PLAZA' }));
```

- ยืนยันตัวตนตอน handshake ด้วย access token เดียวกับ REST (ตรวจ session ใน DB ด้วย) ถ้าไม่ผ่านได้ `connect_error` พร้อม `data.code`
- ผู้เล่น 1 คนเชื่อมต่อได้ 1 socket การเชื่อมต่อใหม่จะตัดอันเก่า (อันเก่าได้ `session:replaced`)
- ทุก event จาก client ตอบกลับเป็น ack: `{ ok: true, ... }` หรือ `{ ok: false, error: { code, message } }`
- access token หมดอายุ 15 นาที: socket ที่เชื่อมอยู่ไม่ถูกตัด แต่ถ้าหลุดต้อง refresh token ก่อนเชื่อมใหม่

## Client → Server

| Event | Body | ทำอะไร | จำกัดความถี่ |
|---|---|---|---|
| `zone:join` | `{ zone }` | เข้าโซน (ย้ายโซนได้) ได้ตำแหน่งเกิดและรายชื่อผู้เล่นที่มองเห็น | 1/วินาที (burst 5) |
| `zone:leave` | – | ออกจากโซน | ร่วมกับ zone |
| `move` | `{ x, y, dir?, seq? }` | ขยับตัว server ตรวจขอบเขตและความเร็วสูงสุด 8 หน่วย/วินาที | 20/วินาที (burst 30) |
| `chat:preset` | `{ presetId }` | ส่งข้อความสำเร็จรูปในโซน (ไม่มีแชตพิมพ์อิสระ) | 1 ทุก 2 วินาที (burst 3) |
| `friend:invite` | `{ friendId, activity }` | ชวนเพื่อนที่ออนไลน์ | 1 ทุก 3 วินาที (burst 2) |
| `home:enter` | `{ ownerId }` | (Phase 10) เข้าบ้าน ตรวจสิทธิ์แบบเดียวกับ `GET /homes/:ownerId` ห้องเต็ม (12 คน) = `HOME_FULL` ack คืนสถานะบ้าน ออกจากบ้านด้วย `zone:join` หรือ `zone:leave` | ร่วมกับ zone |
| `heartbeat` | – | ต่ออายุสถานะออนไลน์ (หมดอายุใน 60 วินาทีถ้าเงียบ) | 1/วินาที |

## Server → Client

| Event | Payload |
|---|---|
| `ready` | `{ userId }` |
| `zone:state` | `{ zone, room, you: {x,y}, players: PublicPlayer[] }` |
| `player:joined` / `player:left` | `PublicPlayer` / `{ userId }` |
| `player:moved` | `{ userId, x, y, dir, seq }` (volatile: อาจตกหล่นได้เมื่อเน็ตช้า) |
| `move:corrected` | `{ x, y, seq }` ตำแหน่งที่ server ยอมรับล่าสุด ให้ client กระโดดกลับ |
| `chat:preset` | `{ userId, displayName, presetId }` |
| `friend:online` / `friend:offline` | `{ userId }` |
| `friend:request` / `friend:accepted` | คำขอเพื่อนใหม่ / ตอบรับแล้ว |
| `friend:invited` | `{ fromUserId, displayName, activity, zone }` |
| `session:replaced` | ถูกแทนที่ด้วยการเชื่อมต่อใหม่ |
| `market:activity` | (Phase 5) ส่งให้ทุกคนในโซน MARKET ของ age band เดียวกับแผง: `{ slot, businessId, tick, tickSeconds, visits: [{ index, customerType, productCode, outcome, quantity }] }` ลูกค้า NPC ของนาทีล่าสุด (สูงสุด 8 คน) `outcome` = `BOUGHT \| TOO_EXPENSIVE \| SOLD_OUT \| NOT_SELLING` |
| `market:stall` | (Phase 5) `{ slot }` แผงเปิด/พัก/ปิด/เปลี่ยนของหรือราคา ให้ client โหลด `GET /market` ใหม่ |
| `shop:updated` | (Phase 5) ส่งให้เจ้าของร้านเท่านั้น: `{ businessId, cashBox, customers, buyers, unitsSold, revenue, stall: [{ productCode, shelfQuantity }], festivalPoints, cityPoints }` (`festivalPoints` เพิ่มใน Phase 6 = สินค้าเทศกาลที่ขายได้ในรอบนี้, `cityPoints` เพิ่มใน Phase 7 = แต้มสร้างเมืองจากรอบนี้) ตั้งแต่ Phase 7 ส่งด้วยเมื่อร้านถูกคิดบัญชีตอนเจ้าของตั้งราคา/เติมของ/อัปเกรด |
| `festival:milestone` | (Phase 6) ส่งให้ทุกคนในทุกโซนของเมืองช่วงอายุเดียวกัน เมื่อการขายทำให้เมืองถึงขั้นใหม่: `{ festivalKey, code, tier, points }` client ประดับเมืองเพิ่มและจุดพลุ |
| `city:growth` | (Phase 7) ส่งให้ทุกคนในทุกโซนของเมืองช่วงอายุเดียวกัน เมื่อการขายทำให้โครงการเมืองสร้างเสร็จ: `{ projectCode, nameTh, effectTh, index, points, next }` client สร้างตึกใหม่และขึ้นข้อความ |
| `home:state` | (Phase 10) `{ ownerId, ownerName, isMine, room, interior: {width, height, spawn}, layoutVersion, you: {x,y}, players }` หลัง `home:enter` (ack ก็คืนค่าเดียวกัน) เฟอร์นิเจอร์โหลดจาก `GET /homes/:ownerId` |
| `home:layout` | (Phase 10) ส่งให้คนในบ้านนั้นเท่านั้น: `{ ownerId, layoutVersion }` เจ้าของบันทึกผังใหม่ ให้โหลด `GET /homes/:ownerId` ใหม่ |
| `home:kicked` | (Phase 10) `{ ownerId }` หมดสิทธิ์อยู่ในบ้าน (เจ้าของเปลี่ยนเป็นส่วนตัว, เลิกเป็นเพื่อน, บล็อก) server พาออกจากห้องแล้ว |

`PublicPlayer = { userId, displayName, level, avatar, x, y, dir }` ไม่มี friend code, อีเมล หรือช่วงอายุ

## กฎความปลอดภัย

- **ห้องแยกตามช่วงอายุ**: ห้องคือ `zone:<ZONE>:<AGE_BAND>:<shard>` ผู้เล่นต่างช่วงอายุไม่เห็นกัน ไม่ได้ยินกัน
- **บล็อก**: ผู้ที่บล็อกกัน (ทางใดทางหนึ่ง) ไม่ได้รับ event ของกันและกัน หายจากจอทันทีที่บล็อก
- **Shard**: แต่ละโซนรับผู้เล่นได้จำกัด (PLAZA 50 คน) เกินแล้วเปิด shard ใหม่อัตโนมัติ
- **บ้าน** (Phase 10): ห้อง `home:<ownerId>` ไม่ broadcast ออกนอกบ้าน `move` ในบ้านใช้ขอบเขตห้อง เมื่อสิทธิ์เปลี่ยน server ตรวจผู้มาเยี่ยมทุกคนใหม่ (ผ่าน Redis ถ้ามีหลาย instance)
- หลาย instance: ตั้ง `REDIS_URL` แล้ว presence และ event จะใช้ Redis (Socket.IO Redis adapter) ร่วมกัน
