import { z } from 'zod';

export type AgeBandValue = 'UNDER_13' | 'TEEN_13_17' | 'ADULT_18_PLUS';

/**
 * Child-safety rule for every social interaction (friend requests, invites, seeing each other
 * in the city, preset chat): players only meet players in the SAME age band.
 * Relaxing this (e.g. teens <-> teens + under-13 with parental approval) must be a deliberate,
 * reviewed change in this one function.
 */
export function canInteract(a: AgeBandValue, b: AgeBandValue): boolean {
  return a === b;
}

export const MAX_FRIENDS = 200;
export const MAX_PENDING_OUTGOING_REQUESTS = 50;

export function pairKey(a: string, b: string): string {
  return a < b ? `${a}:${b}` : `${b}:${a}`;
}

// ---------- City ----------
export interface ZoneConfig {
  id: string;
  nameKey: string;
  width: number;
  height: number;
  spawn: { x: number; y: number };
  /** Walk onto a portal to move to another zone. */
  portals: { to: string; x: number; y: number }[];
  /** Max players per zone shard; extra players are placed in another shard of the same zone. */
  capacity: number;
}

export const CITY_ZONES: readonly ZoneConfig[] = [
  {
    id: 'PLAZA',
    nameKey: 'zone.plaza',
    width: 60,
    height: 60,
    spawn: { x: 30, y: 30 },
    portals: [
      { to: 'HOMES', x: 5, y: 30 },
      { to: 'MARKET', x: 55, y: 30 },
      { to: 'ACTIVITY', x: 30, y: 55 },
      { to: 'MINIGAME_GATE', x: 30, y: 5 },
    ],
    capacity: 50,
  },
  { id: 'HOMES', nameKey: 'zone.homes', width: 40, height: 40, spawn: { x: 35, y: 20 }, portals: [{ to: 'PLAZA', x: 38, y: 20 }], capacity: 40 },
  { id: 'MARKET', nameKey: 'zone.market', width: 40, height: 40, spawn: { x: 5, y: 20 }, portals: [{ to: 'PLAZA', x: 2, y: 20 }], capacity: 40 },
  { id: 'ACTIVITY', nameKey: 'zone.activity', width: 50, height: 40, spawn: { x: 25, y: 5 }, portals: [{ to: 'PLAZA', x: 25, y: 2 }], capacity: 40 },
  { id: 'MINIGAME_GATE', nameKey: 'zone.minigame_gate', width: 30, height: 30, spawn: { x: 15, y: 25 }, portals: [{ to: 'PLAZA', x: 15, y: 28 }], capacity: 30 },
];
export const ZONE_IDS = CITY_ZONES.map((z) => z.id) as [string, ...string[]];
export function zoneById(id: string): ZoneConfig | undefined {
  return CITY_ZONES.find((z) => z.id === id);
}

/** Max walking speed in world units per second (server rejects faster movement). */
export const MAX_MOVE_SPEED = 8;
/** Presence expires if the client sends nothing for this long. */
export const PRESENCE_TTL_SECONDS = 60;

// ---------- Preset messages (no free-text chat in MVP) ----------
export const PRESET_MESSAGES = [
  'HELLO',
  'THANK_YOU',
  'GOOD_JOB',
  'LETS_PLAY',
  'FOLLOW_ME',
  'WAIT_PLEASE',
  'BYE',
  'NICE_SHOP',
  'NICE_HOUSE',
  'HAPPY_FACE',
] as const;
export type PresetMessageId = (typeof PRESET_MESSAGES)[number];

/** Default Thai text; the client localizes by id. */
export const PRESET_MESSAGE_TEXT_TH: Record<PresetMessageId, string> = {
  HELLO: 'สวัสดี!',
  THANK_YOU: 'ขอบคุณนะ',
  GOOD_JOB: 'เก่งมาก!',
  LETS_PLAY: 'มาเล่นด้วยกันไหม?',
  FOLLOW_ME: 'ตามมาเลย',
  WAIT_PLEASE: 'รอแป๊บนึงนะ',
  BYE: 'บ๊ายบาย',
  NICE_SHOP: 'ร้านสวยจัง',
  NICE_HOUSE: 'บ้านน่ารักมาก',
  HAPPY_FACE: '😊',
};

export const INVITE_ACTIVITIES = ['JOIN_ZONE', 'VISIT_HOME', 'MINIGAME_DELIVERY', 'MINIGAME_MARKET'] as const;

export const REPORT_CONTEXT_CODES = [
  'CITY',
  'PRESET_CHAT',
  'FRIEND_REQUEST',
  'PROFILE_NAME',
  'AVATAR',
  'HOME_VISIT',
  'MINIGAME',
  'MARKET_STALL',
] as const;

// ---------- REST schemas ----------
export const sendFriendRequestSchema = z
  .object({
    friendCode: z.string().regex(/^[A-Z2-9]{8}$/).optional(),
    playerId: z.uuid().optional(),
  })
  .strict()
  .refine((v) => (v.friendCode ? 1 : 0) + (v.playerId ? 1 : 0) === 1, 'Send exactly one of friendCode or playerId');
export type SendFriendRequest = z.infer<typeof sendFriendRequestSchema>;

export const reportReasonSchema = z.enum(['INAPPROPRIATE_NAME', 'HARASSMENT', 'CHEATING', 'SPAM', 'ASKING_PERSONAL_INFO', 'OTHER']);
export const createReportSchema = z
  .object({
    reportedPlayerId: z.uuid(),
    reason: reportReasonSchema,
    contextCode: z.enum(REPORT_CONTEXT_CODES).optional(),
    /** Block the player in the same step (recommended in the UI). */
    alsoBlock: z.boolean().default(false),
  })
  .strict();
export type CreateReportRequest = z.infer<typeof createReportSchema>;

export const cityPlayersQuerySchema = z.object({ zone: z.enum(ZONE_IDS) }).strict();

// ---------- WebSocket messages (client -> server) ----------
export const WS_NAMESPACE = '/realtime';
export const WS_PATH = '/ws';

const coord = z.number().finite();
export const wsZoneJoinSchema = z.object({ zone: z.enum(ZONE_IDS) }).strict();
export const wsMoveSchema = z
  .object({ x: coord, y: coord, dir: z.number().int().min(0).max(7).optional(), seq: z.number().int().min(0).optional() })
  .strict();
export const wsPresetChatSchema = z.object({ presetId: z.enum(PRESET_MESSAGES) }).strict();
export const wsInviteSchema = z.object({ friendId: z.uuid(), activity: z.enum(INVITE_ACTIVITIES) }).strict();

/** Event names, so client and server never drift. */
export const WsEvent = {
  // client -> server
  ZONE_JOIN: 'zone:join',
  ZONE_LEAVE: 'zone:leave',
  MOVE: 'move',
  CHAT_PRESET: 'chat:preset',
  FRIEND_INVITE: 'friend:invite',
  HEARTBEAT: 'heartbeat',
  // server -> client
  READY: 'ready',
  ZONE_STATE: 'zone:state',
  PLAYER_JOINED: 'player:joined',
  PLAYER_LEFT: 'player:left',
  PLAYER_MOVED: 'player:moved',
  MOVE_CORRECTED: 'move:corrected',
  CHAT_PRESET_RECEIVED: 'chat:preset',
  FRIEND_ONLINE: 'friend:online',
  FRIEND_OFFLINE: 'friend:offline',
  FRIEND_REQUEST_RECEIVED: 'friend:request',
  FRIEND_REQUEST_ACCEPTED: 'friend:accepted',
  FRIEND_INVITED: 'friend:invited',
  SESSION_REPLACED: 'session:replaced',
  // Phase 5: living shop
  /** NPC customer visits at stalls, sent to players in the MARKET zone of the same age band */
  MARKET_ACTIVITY: 'market:activity',
  /** a stall opened, paused, closed or changed its goods: clients re-fetch GET /market */
  MARKET_STALL_CHANGED: 'market:stall',
  /** to the owner only: cash box / shelf changed after customers bought something */
  SHOP_UPDATED: 'shop:updated',
  ERROR: 'error:app',
} as const;
