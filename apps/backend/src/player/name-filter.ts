/**
 * Minimal display-name filter (MVP). Production should add a maintained Thai/English/Chinese
 * word list and human review via moderation reports.
 */
const BLOCKED_FRAGMENTS = [
  'admin',
  'moderator',
  'official',
  'staff',
  'fuck',
  'shit',
  'bitch',
  'sex',
  'ควย',
  'เหี้ย',
  'สัส',
  'เย็ด',
  'แอดมิน',
  'line',
  'ไลน์',
  'facebook',
  'tiktok',
];

export function isDisplayNameAllowed(name: string): boolean {
  const normalized = name.toLowerCase().replace(/[\s_]+/g, '');
  return !BLOCKED_FRAGMENTS.some((f) => normalized.includes(f));
}
