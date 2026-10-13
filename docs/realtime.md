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

`PublicPlayer = { userId, displayName, level, avatar, x, y, dir }` ไม่มี friend code, อีเมล หรือช่วงอายุ

## กฎความปลอดภัย

- **ห้องแยกตามช่วงอายุ**: ห้องคือ `zone:<ZONE>:<AGE_BAND>:<shard>` ผู้เล่นต่างช่วงอายุไม่เห็นกัน ไม่ได้ยินกัน
- **บล็อก**: ผู้ที่บล็อกกัน (ทางใดทางหนึ่ง) ไม่ได้รับ event ของกันและกัน หายจากจอทันทีที่บล็อก
- **Shard**: แต่ละโซนรับผู้เล่นได้จำกัด (PLAZA 50 คน) เกินแล้วเปิด shard ใหม่อัตโนมัติ
- หลาย instance: ตั้ง `REDIS_URL` แล้ว presence และ event จะใช้ Redis (Socket.IO Redis adapter) ร่วมกัน
