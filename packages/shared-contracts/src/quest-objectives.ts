/**
 * Things a player does that quests and missions count (mirror of the Prisma enum QuestObjectiveType;
 * a backend unit test keeps the two in step).
 */
export const QUEST_OBJECTIVES = [
  'UPDATE_PROFILE',
  'BUY_ITEM',
  'BUSINESS_RESTOCK',
  'BUSINESS_SELL_UNITS',
  'BUSINESS_EARN_PROFIT',
  'BUSINESS_UPGRADE',
  'PLACE_FURNITURE',
  'ADD_FRIEND',
  'PLAY_MINIGAME',
  'SHOP_OPEN',
  'SHOP_SERVE_CUSTOMERS',
  'FESTIVAL_CONTRIBUTE',
  'EXPEDITION_COMPLETE',
  'GATHER_MATERIAL',
  'CRAFT_ITEM',
  'JOB_SHIFT_COMPLETE',
] as const;
export type QuestObjective = (typeof QUEST_OBJECTIVES)[number];
