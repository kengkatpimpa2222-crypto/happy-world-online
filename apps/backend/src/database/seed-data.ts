import { ItemCategory, QuestCategory, QuestObjectiveType } from '@prisma/client';

/** Catalog prices are starting assumptions for testing; admins can change them in the `items` table. */
export const ITEMS: {
  id: string;
  category: ItemCategory;
  priceGold: number | null;
  priceGems: number | null;
  isPremium?: boolean;
  metadata?: Record<string, unknown>;
}[] = [
  // starter (not sold)
  { id: 'outfit_basic_tee', category: 'OUTFIT', priceGold: null, priceGems: null },
  { id: 'hair_short_brown', category: 'HAIR', priceGold: null, priceGems: null },
  { id: 'furn_wood_chair', category: 'FURNITURE', priceGold: 50, priceGems: null, metadata: { footprint: [1, 1] } },
  // gold items
  { id: 'outfit_school_uniform', category: 'OUTFIT', priceGold: 150, priceGems: null },
  { id: 'outfit_thai_silk', category: 'OUTFIT', priceGold: 400, priceGems: null },
  { id: 'hair_long_black', category: 'HAIR', priceGold: 120, priceGems: null },
  { id: 'acc_straw_hat', category: 'ACCESSORY', priceGold: 80, priceGems: null },
  { id: 'furn_wood_table', category: 'FURNITURE', priceGold: 120, priceGems: null, metadata: { footprint: [2, 1] } },
  { id: 'furn_plant_pot', category: 'FURNITURE', priceGold: 40, priceGems: null, metadata: { footprint: [1, 1] } },
  { id: 'furn_single_bed', category: 'FURNITURE', priceGold: 200, priceGems: null, metadata: { footprint: [1, 2] } },
  { id: 'floor_wood_light', category: 'FLOOR', priceGold: 150, priceGems: null },
  { id: 'wall_pastel_blue', category: 'WALL', priceGold: 150, priceGems: null },
  // premium (gems)
  { id: 'outfit_star_idol', category: 'OUTFIT', priceGold: null, priceGems: 60, isPremium: true },
  { id: 'acc_rainbow_wings', category: 'ACCESSORY', priceGold: null, priceGems: 120, isPremium: true },
  { id: 'furn_sofa_cloud', category: 'FURNITURE', priceGold: null, priceGems: 80, isPremium: true, metadata: { footprint: [2, 1] } },
  { id: 'theme_songkran', category: 'THEME', priceGold: null, priceGems: 150, isPremium: true },
  // city festival keepsakes (Phase 6): earned when the whole city reaches tier 3, never sold
  { id: 'furn_fest_beach_umbrella', category: 'FURNITURE', priceGold: null, priceGems: null, metadata: { footprint: [1, 1], festival: 'COOL_SUMMER' } },
  { id: 'furn_fest_bread_oven', category: 'FURNITURE', priceGold: null, priceGems: null, metadata: { footprint: [1, 1], festival: 'BAKERY' } },
  { id: 'furn_fest_flower_arch', category: 'FURNITURE', priceGold: null, priceGems: null, metadata: { footprint: [2, 1], festival: 'FLOWER' } },
  { id: 'furn_fest_food_cart', category: 'FURNITURE', priceGold: null, priceGems: null, metadata: { footprint: [2, 1], festival: 'STREET_FOOD' } },
  { id: 'furn_fest_lantern', category: 'FURNITURE', priceGold: null, priceGems: null, metadata: { footprint: [1, 1], festival: 'LANTERN' } },
];

export const QUESTS: {
  id: string;
  category: QuestCategory;
  chapter?: number;
  objectiveType: QuestObjectiveType;
  objectiveTarget: number;
  rewardGold?: number;
  rewardGems?: number;
  rewardXp?: number;
  rewardItemId?: string;
  prerequisiteQuestId?: string;
  minLevel?: number;
}[] = [
  // newbie chain
  { id: 'newbie_set_profile', category: 'NEWBIE', objectiveType: 'UPDATE_PROFILE', objectiveTarget: 1, rewardGold: 100, rewardXp: 20 },
  { id: 'newbie_first_purchase', category: 'NEWBIE', objectiveType: 'BUY_ITEM', objectiveTarget: 1, rewardGold: 100, rewardXp: 30, prerequisiteQuestId: 'newbie_set_profile' },
  { id: 'newbie_open_shop', category: 'NEWBIE', objectiveType: 'BUSINESS_RESTOCK', objectiveTarget: 10, rewardGold: 50, rewardXp: 30, prerequisiteQuestId: 'newbie_first_purchase' },
  // chapter 1
  { id: 'ch1_sell_lemonade', category: 'CHAPTER', chapter: 1, objectiveType: 'BUSINESS_SELL_UNITS', objectiveTarget: 20, rewardGold: 150, rewardXp: 50 },
  // business growth
  { id: 'biz_first_profit', category: 'BUSINESS', objectiveType: 'BUSINESS_EARN_PROFIT', objectiveTarget: 100, rewardGold: 200, rewardXp: 60 },
  { id: 'biz_upgrade_shop', category: 'BUSINESS', objectiveType: 'BUSINESS_UPGRADE', objectiveTarget: 1, rewardGold: 100, rewardGems: 5, rewardXp: 80 },
  // living shop (Phase 5)
  { id: 'shop_open_stall', category: 'BUSINESS', objectiveType: 'SHOP_OPEN', objectiveTarget: 1, rewardGold: 100, rewardXp: 30 },
  { id: 'shop_serve_customers', category: 'BUSINESS', objectiveType: 'SHOP_SERVE_CUSTOMERS', objectiveTarget: 15, rewardGold: 150, rewardXp: 50, prerequisiteQuestId: 'shop_open_stall' },
  { id: 'festival_first_help', category: 'COOP', objectiveType: 'FESTIVAL_CONTRIBUTE', objectiveTarget: 10, rewardGold: 120, rewardXp: 40, prerequisiteQuestId: 'shop_open_stall' },
  // home
  { id: 'home_first_furniture', category: 'HOME', objectiveType: 'PLACE_FURNITURE', objectiveTarget: 1, rewardGold: 80, rewardXp: 20 },
  // co-op & life skill
  { id: 'coop_make_friend', category: 'COOP', objectiveType: 'ADD_FRIEND', objectiveTarget: 1, rewardGold: 100, rewardXp: 30 },
  { id: 'life_budget_buyer', category: 'LIFE_SKILL', objectiveType: 'BUY_ITEM', objectiveTarget: 3, rewardGold: 120, rewardXp: 40, rewardItemId: 'furn_plant_pot' },
];
