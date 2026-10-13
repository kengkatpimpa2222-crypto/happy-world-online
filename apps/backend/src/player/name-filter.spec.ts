import { displayNameSchema, updateProfileSchema } from '@hwo/shared-contracts';
import { isDisplayNameAllowed } from './name-filter';

describe('display name rules', () => {
  it.each(['น้องแมว', 'Happy_Tom', 'มะลิ 2'])('accepts %s', (n) => {
    expect(displayNameSchema.safeParse(n).success).toBe(true);
    expect(isDisplayNameAllowed(n)).toBe(true);
  });

  it.each(['a', 'x'.repeat(17), 'tel 0812345678', 'me@mail.com', 'site.com', '<script>'])('schema rejects %s', (n) => {
    expect(displayNameSchema.safeParse(n).success).toBe(false);
  });

  it.each(['Admin', 'G M_Official', 'add my LINE', 'ไลน์ผม'])('filter rejects %s', (n) => {
    expect(isDisplayNameAllowed(n)).toBe(false);
  });

  it('profile update rejects unknown fields', () => {
    expect(updateProfileSchema.safeParse({ level: 50 }).success).toBe(false);
    expect(updateProfileSchema.safeParse({}).success).toBe(false);
  });
});
