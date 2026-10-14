// QA helper: a second player for testing the game client. Joins PLAZA (age band 18+), walks in a circle near spawn,
// says the HELLO preset every 8s and (optionally) sends a friend request. Use against a dev backend only.
// usage: node scripts/qa-bot.js [friendCodeToRequest] [seconds]      STILL=1 keeps it standing at spawn
// HWO_API_URL overrides http://localhost:3000
const { io } = require('socket.io-client');
const BASE = process.env.HWO_API_URL || 'http://localhost:3000';
const API = BASE + '/api/v1';
const code = process.argv[2];
const seconds = Number(process.argv[3] || 600);

async function call(method, path, token, body) {
  const res = await fetch(API + path, {
    method,
    headers: { 'content-type': 'application/json', ...(token ? { authorization: 'Bearer ' + token } : {}) },
    body: body ? JSON.stringify(body) : undefined,
  });
  const text = await res.text();
  let json; try { json = JSON.parse(text); } catch { json = text; }
  if (!res.ok) throw new Error(`${method} ${path} ${res.status} ${text}`);
  return json;
}

(async () => {
  const auth = await call('POST', '/auth/guest', null, { deviceId: 'qa-bot-' + Date.now(), ageBand: 'ADULT_18_PLUS', locale: 'th' });
  const token = auth.accessToken;
  await call('PATCH', '/player/profile', token, { displayName: 'QABot' });
  const me = await call('GET', '/player/profile', token);
  console.log('bot ready', me.displayName, me.friendCode);
  if (code) {
    const r = await call('POST', '/friends/requests', token, { friendCode: code });
    console.log('friend request sent', JSON.stringify(r).slice(0, 200));
  }
  const s = io(BASE + '/realtime', { path: '/ws', auth: { token }, transports: ['websocket'] });
  s.on('move:corrected', (c) => console.log('corrected', c.x, c.y));
  s.on('connect_error', (e) => console.log('connect_error', e.message, e.data));
  s.on('player:joined', (p) => console.log('saw joined', p.displayName));
  s.on('player:left', (p) => console.log('saw left', p.userId));
  s.on('chat:preset', (c) => console.log('chat', c.displayName, c.presetId));
  s.on('friend:accepted', (f) => console.log('friend accepted', JSON.stringify(f).slice(0, 120)));
  s.on('ready', async () => {
    const st = await s.emitWithAck('zone:join', { zone: 'PLAZA' });
    console.log('joined', st.ok, 'players visible:', (st.players || st.state?.players || []).map((p) => p.displayName).join(','));
    const x = st.x, y = st.y;
    const cx = x - 4, cy = y; let t = 0; // circle of radius 4 starting at spawn
    const mv = process.env.STILL ? null : setInterval(() => { t += 0.1; s.emit('move', { x: cx + Math.cos(t) * 4, y: cy + Math.sin(t) * 4, dir: 0 }); }, 150);
    const hb = setInterval(() => s.emit('heartbeat'), 20000);
    const ch = setInterval(async () => { const a = await s.emitWithAck('chat:preset', { presetId: 'HELLO' }); if (!a.ok) console.log('chat err', a.error?.code); }, 8000);
    setTimeout(() => { clearInterval(mv); clearInterval(hb); clearInterval(ch); s.close(); console.log('bot done'); process.exit(0); }, seconds * 1000);
  });
})().catch((e) => { console.error(e.message); process.exit(1); });
