import { hashPassword, randomFriendCode, randomToken, sha256, verifyPassword } from './crypto';

describe('crypto helpers', () => {
  it('hashes and verifies passwords with scrypt and a random salt', async () => {
    const a = await hashPassword('s3cret-password');
    const b = await hashPassword('s3cret-password');
    expect(a).not.toBe(b);
    expect(await verifyPassword('s3cret-password', a)).toBe(true);
    expect(await verifyPassword('wrong', a)).toBe(false);
    expect(await verifyPassword('x', 'garbage')).toBe(false);
  });

  it('creates unguessable tokens and friend codes', () => {
    expect(randomToken()).toHaveLength(43);
    expect(randomToken()).not.toBe(randomToken());
    expect(randomFriendCode()).toMatch(/^[A-HJ-NP-Z2-9]{8}$/);
    expect(sha256('a')).toHaveLength(64);
  });
});
