import { canInteract, pairKey } from '@hwo/shared-contracts';
import { MemoryPresenceStore, PresenceEntry } from './presence.store';

const entry = (over: Partial<PresenceEntry> = {}): PresenceEntry => ({
  userId: 'u1',
  socketId: 's1',
  ageBand: 'TEEN_13_17',
  displayName: 'A',
  level: 1,
  avatar: {},
  zone: 'PLAZA',
  room: 'zone:PLAZA:TEEN_13_17:0',
  x: 1,
  y: 1,
  dir: 0,
  lastMoveAt: 0,
  lastSeen: 0,
  ...over,
});

describe('MemoryPresenceStore', () => {
  it('lists players by room', async () => {
    const s = new MemoryPresenceStore();
    await s.upsert(entry(), 60);
    await s.upsert(entry({ userId: 'u2', socketId: 's2', room: 'other' }), 60);
    expect((await s.listRoom('zone:PLAZA:TEEN_13_17:0')).map((e) => e.userId)).toEqual(['u1']);
  });

  it('a stale socket cannot remove the presence of a newer connection', async () => {
    const s = new MemoryPresenceStore();
    await s.upsert(entry({ socketId: 'new' }), 60);
    expect(await s.remove('u1', 'old')).toBeNull();
    expect(await s.get('u1')).not.toBeNull();
    expect(await s.remove('u1', 'new')).not.toBeNull();
    expect(await s.get('u1')).toBeNull();
  });

  it('entries expire', async () => {
    const s = new MemoryPresenceStore();
    await s.upsert(entry(), -1);
    expect(await s.get('u1')).toBeNull();
  });
});

describe('social rules', () => {
  it('only the same age band can interact', () => {
    expect(canInteract('UNDER_13', 'UNDER_13')).toBe(true);
    expect(canInteract('UNDER_13', 'ADULT_18_PLUS')).toBe(false);
    expect(canInteract('ADULT_18_PLUS', 'TEEN_13_17')).toBe(false);
  });

  it('pair key is order-independent', () => {
    expect(pairKey('b', 'a')).toBe(pairKey('a', 'b'));
    expect(pairKey('a', 'b')).toBe('a:b');
  });
});
