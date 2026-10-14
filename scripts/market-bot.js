// QA helper for the Phase 5 living shop: a shop owner who stocks a market stall, opens it, stands in
// the MARKET zone and prints the NPC customers the server sends (market:activity) plus the owner's
// live cash box (shop:updated). At the end it prints the "while you were away" report, collects the
// cash box and closes the stall. Use against a dev backend only.
// usage: node scripts/market-bot.js [seconds] [ageBand]     (default 180 s, ADULT_18_PLUS)
// HWO_API_URL overrides http://localhost:3000. KEEP_OPEN=1 leaves the stall open at the end.
const { io } = require('socket.io-client');
const { randomUUID } = require('node:crypto');
const BASE = process.env.HWO_API_URL || 'http://localhost:3000';
const API = BASE + '/api/v1';
const seconds = Number(process.argv[2] || 180);
const ageBand = process.argv[3] || 'ADULT_18_PLUS';

async function call(method, path, token, body, headers = {}) {
  const res = await fetch(API + path, {
    method,
    headers: { 'content-type': 'application/json', ...(token ? { authorization: 'Bearer ' + token } : {}), ...headers },
    body: body ? JSON.stringify(body) : undefined,
  });
  const text = await res.text();
  let json;
  try {
    json = JSON.parse(text);
  } catch {
    json = text;
  }
  if (!res.ok) throw new Error(`${method} ${path} ${res.status} ${text}`);
  return json;
}
const key = () => ({ 'idempotency-key': randomUUID().replace(/-/g, '') });
const t = () => new Date().toISOString().slice(11, 19);

(async () => {
  const auth = await call('POST', '/auth/guest', null, { deviceId: 'market-bot-' + Date.now(), ageBand, locale: 'th' });
  const token = auth.accessToken;
  await call('PATCH', '/player/profile', token, { displayName: 'MarketBot' });
  let [shop] = await call('GET', '/businesses', token);
  shop = await call('PATCH', `/businesses/${shop.id}`, token, { name: 'ร้านน้ำมะนาวบอท' });
  await call('POST', `/businesses/${shop.id}/restock`, token, { productCode: 'LEMONADE', quantity: 20 }, key());
  await call('POST', `/businesses/${shop.id}/restock`, token, { productCode: 'BREAD', quantity: 20 }, key());
  await call('PUT', `/businesses/${shop.id}/shelf`, token, { productCode: 'LEMONADE', quantity: 20 });
  await call('PATCH', `/businesses/${shop.id}/pricing`, token, { productCode: 'BREAD', price: 17 });
  await call('PUT', `/businesses/${shop.id}/shelf`, token, { productCode: 'BREAD', quantity: 20 });
  shop = await call('POST', `/businesses/${shop.id}/status`, token, { status: 'OPEN' });
  console.log(t(), 'stall open', JSON.stringify(shop.stall));

  const s = io(BASE + '/realtime', { path: '/ws', auth: { token }, transports: ['websocket'] });
  let visits = 0;
  s.on('connect_error', (e) => console.log('connect_error', e.message));
  s.on('ready', () =>
    s.emit('zone:join', { zone: 'MARKET' }, (ack) => console.log(t(), 'joined MARKET', ack.ok, 'room', ack.room)),
  );
  s.on('market:activity', (m) => {
    visits += m.visits.length;
    console.log(t(), `market:activity slot=${m.slot} tick=${m.tick}`, m.visits.map((v) => `${v.customerType}:${v.productCode}:${v.outcome}x${v.quantity}`).join(' '));
  });
  s.on('market:stall', (m) => console.log(t(), 'market:stall', JSON.stringify(m)));
  s.on('shop:updated', (u) => console.log(t(), `shop:updated cashBox=${u.cashBox} +${u.revenue} customers=${u.customers} stall=${JSON.stringify(u.stall)}`));
  const hb = setInterval(() => s.emit('heartbeat', {}, () => {}), 20000);

  await new Promise((r) => setTimeout(r, seconds * 1000));
  clearInterval(hb);
  const report = await call('GET', `/businesses/${shop.id}/report`, token);
  console.log(t(), 'report', JSON.stringify({ customers: report.customers, buyers: report.buyers, unitsSold: report.unitsSold, revenue: report.revenue, profit: report.profit, lostTooExpensive: report.lostTooExpensive, lostSoldOut: report.lostSoldOut, lostNotSelling: report.lostNotSelling, bestSeller: report.bestSeller, tips: report.tips.map((x) => x.textTh) }));
  if (report.cashBox > 0) {
    const c = await call('POST', `/businesses/${shop.id}/collect`, token, null, key());
    console.log(t(), 'collected', c.collected, 'wallet', JSON.stringify(c.wallet));
  }
  if (!process.env.KEEP_OPEN) await call('POST', `/businesses/${shop.id}/status`, token, { status: 'CLOSED' });
  console.log(t(), 'animated visits received', visits);
  s.close();
})().catch((e) => {
  console.error(e);
  process.exit(1);
});
