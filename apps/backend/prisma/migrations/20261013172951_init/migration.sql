-- CreateEnum
CREATE TYPE "UserStatus" AS ENUM ('ACTIVE', 'SUSPENDED', 'DELETED');

-- CreateEnum
CREATE TYPE "AgeBand" AS ENUM ('UNDER_13', 'TEEN_13_17', 'ADULT_18_PLUS');

-- CreateEnum
CREATE TYPE "Currency" AS ENUM ('GOLD', 'GEMS');

-- CreateEnum
CREATE TYPE "WalletTransactionType" AS ENUM ('SIGNUP_BONUS', 'QUEST_REWARD', 'STORE_PURCHASE', 'BUSINESS_RESTOCK', 'BUSINESS_SALE', 'BUSINESS_UPGRADE', 'MINIGAME_REWARD', 'IAP_GRANT', 'IAP_REFUND', 'ADMIN_ADJUSTMENT');

-- CreateEnum
CREATE TYPE "ItemCategory" AS ENUM ('OUTFIT', 'HAIR', 'ACCESSORY', 'FURNITURE', 'FLOOR', 'WALL', 'THEME');

-- CreateEnum
CREATE TYPE "QuestCategory" AS ENUM ('NEWBIE', 'CHAPTER', 'BUSINESS', 'HOME', 'COOP', 'LIFE_SKILL');

-- CreateEnum
CREATE TYPE "QuestObjectiveType" AS ENUM ('UPDATE_PROFILE', 'BUY_ITEM', 'BUSINESS_RESTOCK', 'BUSINESS_SELL_UNITS', 'BUSINESS_EARN_PROFIT', 'BUSINESS_UPGRADE', 'PLACE_FURNITURE', 'ADD_FRIEND', 'PLAY_MINIGAME');

-- CreateEnum
CREATE TYPE "QuestStatus" AS ENUM ('IN_PROGRESS', 'COMPLETED', 'CLAIMED');

-- CreateEnum
CREATE TYPE "FriendRequestStatus" AS ENUM ('PENDING', 'ACCEPTED', 'DECLINED', 'CANCELLED');

-- CreateEnum
CREATE TYPE "ReportReason" AS ENUM ('INAPPROPRIATE_NAME', 'HARASSMENT', 'CHEATING', 'SPAM', 'ASKING_PERSONAL_INFO', 'OTHER');

-- CreateEnum
CREATE TYPE "ReportStatus" AS ENUM ('OPEN', 'REVIEWING', 'ACTIONED', 'DISMISSED');

-- CreateEnum
CREATE TYPE "PurchaseProvider" AS ENUM ('GOOGLE_PLAY', 'DEV_MOCK');

-- CreateEnum
CREATE TYPE "PurchaseStatus" AS ENUM ('PENDING', 'VERIFIED', 'GRANTED', 'REFUNDED', 'FAILED');

-- CreateEnum
CREATE TYPE "SubscriptionStatus" AS ENUM ('ACTIVE', 'GRACE_PERIOD', 'CANCELLED', 'EXPIRED');

-- CreateTable
CREATE TABLE "users" (
    "id" UUID NOT NULL,
    "status" "UserStatus" NOT NULL DEFAULT 'ACTIVE',
    "age_band" "AgeBand" NOT NULL,
    "locale" VARCHAR(8) NOT NULL DEFAULT 'th',
    "email" VARCHAR(254),
    "password_hash" TEXT,
    "linked_at" TIMESTAMP(3),
    "device_id_hash" VARCHAR(64),
    "parental_consent_at" TIMESTAMP(3),
    "created_at" TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "updated_at" TIMESTAMP(3) NOT NULL,
    "deleted_at" TIMESTAMP(3),

    CONSTRAINT "users_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "player_profiles" (
    "user_id" UUID NOT NULL,
    "display_name" VARCHAR(20) NOT NULL,
    "friend_code" VARCHAR(10) NOT NULL,
    "level" INTEGER NOT NULL DEFAULT 1,
    "xp" INTEGER NOT NULL DEFAULT 0,
    "avatar" JSONB NOT NULL DEFAULT '{}',
    "achievements" JSONB NOT NULL DEFAULT '[]',
    "created_at" TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "updated_at" TIMESTAMP(3) NOT NULL,

    CONSTRAINT "player_profiles_pkey" PRIMARY KEY ("user_id")
);

-- CreateTable
CREATE TABLE "game_sessions" (
    "id" UUID NOT NULL,
    "user_id" UUID NOT NULL,
    "refresh_token_hash" VARCHAR(64) NOT NULL,
    "user_agent" VARCHAR(255),
    "expires_at" TIMESTAMP(3) NOT NULL,
    "revoked_at" TIMESTAMP(3),
    "last_seen_at" TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "created_at" TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT "game_sessions_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "player_wallets" (
    "id" UUID NOT NULL,
    "user_id" UUID NOT NULL,
    "currency" "Currency" NOT NULL,
    "balance" INTEGER NOT NULL DEFAULT 0,
    "version" INTEGER NOT NULL DEFAULT 0,
    "created_at" TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "updated_at" TIMESTAMP(3) NOT NULL,

    CONSTRAINT "player_wallets_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "wallet_transactions" (
    "id" UUID NOT NULL,
    "user_id" UUID NOT NULL,
    "wallet_id" UUID NOT NULL,
    "currency" "Currency" NOT NULL,
    "type" "WalletTransactionType" NOT NULL,
    "amount" INTEGER NOT NULL,
    "balance_before" INTEGER NOT NULL,
    "balance_after" INTEGER NOT NULL,
    "reference_type" VARCHAR(40),
    "reference_id" VARCHAR(120),
    "idempotency_key" VARCHAR(160) NOT NULL,
    "metadata" JSONB,
    "created_at" TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT "wallet_transactions_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "items" (
    "id" VARCHAR(64) NOT NULL,
    "category" "ItemCategory" NOT NULL,
    "name_key" VARCHAR(120) NOT NULL,
    "price_gold" INTEGER,
    "price_gems" INTEGER,
    "is_premium" BOOLEAN NOT NULL DEFAULT false,
    "is_active" BOOLEAN NOT NULL DEFAULT true,
    "sort_order" INTEGER NOT NULL DEFAULT 0,
    "metadata" JSONB NOT NULL DEFAULT '{}',
    "created_at" TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "updated_at" TIMESTAMP(3) NOT NULL,
    "deleted_at" TIMESTAMP(3),

    CONSTRAINT "items_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "player_inventory" (
    "id" UUID NOT NULL,
    "user_id" UUID NOT NULL,
    "item_id" VARCHAR(64) NOT NULL,
    "quantity" INTEGER NOT NULL DEFAULT 0,
    "acquired_at" TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "updated_at" TIMESTAMP(3) NOT NULL,

    CONSTRAINT "player_inventory_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "player_homes" (
    "id" UUID NOT NULL,
    "user_id" UUID NOT NULL,
    "theme_item_id" VARCHAR(64),
    "floor_item_id" VARCHAR(64),
    "wall_item_id" VARCHAR(64),
    "grid_width" INTEGER NOT NULL DEFAULT 8,
    "grid_height" INTEGER NOT NULL DEFAULT 8,
    "layout_version" INTEGER NOT NULL DEFAULT 0,
    "created_at" TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "updated_at" TIMESTAMP(3) NOT NULL,

    CONSTRAINT "player_homes_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "home_furniture" (
    "id" UUID NOT NULL,
    "home_id" UUID NOT NULL,
    "item_id" VARCHAR(64) NOT NULL,
    "x" INTEGER NOT NULL,
    "y" INTEGER NOT NULL,
    "rotation" INTEGER NOT NULL DEFAULT 0,
    "created_at" TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "updated_at" TIMESTAMP(3) NOT NULL,

    CONSTRAINT "home_furniture_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "businesses" (
    "id" UUID NOT NULL,
    "user_id" UUID NOT NULL,
    "name" VARCHAR(30) NOT NULL,
    "level" INTEGER NOT NULL DEFAULT 1,
    "total_revenue" INTEGER NOT NULL DEFAULT 0,
    "total_expense" INTEGER NOT NULL DEFAULT 0,
    "total_units_sold" INTEGER NOT NULL DEFAULT 0,
    "last_sale_at" TIMESTAMP(3),
    "created_at" TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "updated_at" TIMESTAMP(3) NOT NULL,
    "deleted_at" TIMESTAMP(3),

    CONSTRAINT "businesses_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "business_inventory" (
    "id" UUID NOT NULL,
    "business_id" UUID NOT NULL,
    "product_code" VARCHAR(32) NOT NULL,
    "quantity" INTEGER NOT NULL DEFAULT 0,
    "avg_unit_cost" INTEGER NOT NULL DEFAULT 0,
    "sell_price" INTEGER NOT NULL,
    "updated_at" TIMESTAMP(3) NOT NULL,

    CONSTRAINT "business_inventory_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "quests" (
    "id" VARCHAR(64) NOT NULL,
    "category" "QuestCategory" NOT NULL,
    "chapter" INTEGER,
    "title_key" VARCHAR(120) NOT NULL,
    "description_key" VARCHAR(120) NOT NULL,
    "objective_type" "QuestObjectiveType" NOT NULL,
    "objective_target" INTEGER NOT NULL,
    "reward_gold" INTEGER NOT NULL DEFAULT 0,
    "reward_gems" INTEGER NOT NULL DEFAULT 0,
    "reward_xp" INTEGER NOT NULL DEFAULT 0,
    "reward_item_id" VARCHAR(64),
    "min_level" INTEGER NOT NULL DEFAULT 1,
    "prerequisite_quest_id" VARCHAR(64),
    "sort_order" INTEGER NOT NULL DEFAULT 0,
    "is_active" BOOLEAN NOT NULL DEFAULT true,
    "created_at" TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "updated_at" TIMESTAMP(3) NOT NULL,

    CONSTRAINT "quests_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "quest_progress" (
    "id" UUID NOT NULL,
    "user_id" UUID NOT NULL,
    "quest_id" VARCHAR(64) NOT NULL,
    "status" "QuestStatus" NOT NULL DEFAULT 'IN_PROGRESS',
    "progress" INTEGER NOT NULL DEFAULT 0,
    "started_at" TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "completed_at" TIMESTAMP(3),
    "claimed_at" TIMESTAMP(3),
    "updated_at" TIMESTAMP(3) NOT NULL,

    CONSTRAINT "quest_progress_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "friends" (
    "id" UUID NOT NULL,
    "user_id" UUID NOT NULL,
    "friend_id" UUID NOT NULL,
    "created_at" TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT "friends_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "friend_requests" (
    "id" UUID NOT NULL,
    "from_user_id" UUID NOT NULL,
    "to_user_id" UUID NOT NULL,
    "status" "FriendRequestStatus" NOT NULL DEFAULT 'PENDING',
    "created_at" TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "responded_at" TIMESTAMP(3),

    CONSTRAINT "friend_requests_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "blocked_players" (
    "id" UUID NOT NULL,
    "blocker_id" UUID NOT NULL,
    "blocked_id" UUID NOT NULL,
    "created_at" TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT "blocked_players_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "moderation_reports" (
    "id" UUID NOT NULL,
    "reporter_id" UUID NOT NULL,
    "reported_id" UUID NOT NULL,
    "reason" "ReportReason" NOT NULL,
    "context_code" VARCHAR(64),
    "status" "ReportStatus" NOT NULL DEFAULT 'OPEN',
    "resolved_by" VARCHAR(64),
    "resolved_at" TIMESTAMP(3),
    "created_at" TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "updated_at" TIMESTAMP(3) NOT NULL,

    CONSTRAINT "moderation_reports_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "purchases" (
    "id" UUID NOT NULL,
    "user_id" UUID NOT NULL,
    "provider" "PurchaseProvider" NOT NULL,
    "product_id" VARCHAR(100) NOT NULL,
    "purchase_token" TEXT NOT NULL,
    "purchase_token_hash" VARCHAR(64) NOT NULL,
    "order_id" VARCHAR(100),
    "status" "PurchaseStatus" NOT NULL DEFAULT 'PENDING',
    "is_test" BOOLEAN NOT NULL DEFAULT false,
    "granted_at" TIMESTAMP(3),
    "refunded_at" TIMESTAMP(3),
    "created_at" TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "updated_at" TIMESTAMP(3) NOT NULL,

    CONSTRAINT "purchases_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "purchase_events" (
    "id" UUID NOT NULL,
    "purchase_id" UUID NOT NULL,
    "type" VARCHAR(40) NOT NULL,
    "payload" JSONB NOT NULL DEFAULT '{}',
    "created_at" TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT "purchase_events_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "subscriptions" (
    "id" UUID NOT NULL,
    "user_id" UUID NOT NULL,
    "provider" "PurchaseProvider" NOT NULL,
    "product_id" VARCHAR(100) NOT NULL,
    "purchase_token_hash" VARCHAR(64) NOT NULL,
    "status" "SubscriptionStatus" NOT NULL,
    "starts_at" TIMESTAMP(3) NOT NULL,
    "expires_at" TIMESTAMP(3) NOT NULL,
    "auto_renewing" BOOLEAN NOT NULL DEFAULT false,
    "created_at" TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "updated_at" TIMESTAMP(3) NOT NULL,

    CONSTRAINT "subscriptions_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "audit_logs" (
    "id" UUID NOT NULL,
    "actor_user_id" UUID,
    "action" VARCHAR(60) NOT NULL,
    "target_type" VARCHAR(40),
    "target_id" VARCHAR(120),
    "metadata" JSONB NOT NULL DEFAULT '{}',
    "ip_hash" VARCHAR(64),
    "created_at" TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT "audit_logs_pkey" PRIMARY KEY ("id")
);

-- CreateIndex
CREATE UNIQUE INDEX "users_email_key" ON "users"("email");

-- CreateIndex
CREATE INDEX "users_device_id_hash_idx" ON "users"("device_id_hash");

-- CreateIndex
CREATE UNIQUE INDEX "player_profiles_friend_code_key" ON "player_profiles"("friend_code");

-- CreateIndex
CREATE UNIQUE INDEX "game_sessions_refresh_token_hash_key" ON "game_sessions"("refresh_token_hash");

-- CreateIndex
CREATE INDEX "game_sessions_user_id_revoked_at_idx" ON "game_sessions"("user_id", "revoked_at");

-- CreateIndex
CREATE UNIQUE INDEX "player_wallets_user_id_currency_key" ON "player_wallets"("user_id", "currency");

-- CreateIndex
CREATE INDEX "wallet_transactions_user_id_created_at_idx" ON "wallet_transactions"("user_id", "created_at");

-- CreateIndex
CREATE INDEX "wallet_transactions_reference_type_reference_id_idx" ON "wallet_transactions"("reference_type", "reference_id");

-- CreateIndex
CREATE UNIQUE INDEX "wallet_transactions_user_id_idempotency_key_key" ON "wallet_transactions"("user_id", "idempotency_key");

-- CreateIndex
CREATE INDEX "items_category_is_active_idx" ON "items"("category", "is_active");

-- CreateIndex
CREATE UNIQUE INDEX "player_inventory_user_id_item_id_key" ON "player_inventory"("user_id", "item_id");

-- CreateIndex
CREATE UNIQUE INDEX "player_homes_user_id_key" ON "player_homes"("user_id");

-- CreateIndex
CREATE INDEX "home_furniture_home_id_item_id_idx" ON "home_furniture"("home_id", "item_id");

-- CreateIndex
CREATE UNIQUE INDEX "home_furniture_home_id_x_y_key" ON "home_furniture"("home_id", "x", "y");

-- CreateIndex
CREATE INDEX "businesses_user_id_idx" ON "businesses"("user_id");

-- CreateIndex
CREATE UNIQUE INDEX "business_inventory_business_id_product_code_key" ON "business_inventory"("business_id", "product_code");

-- CreateIndex
CREATE INDEX "quests_is_active_category_idx" ON "quests"("is_active", "category");

-- CreateIndex
CREATE INDEX "quest_progress_user_id_status_idx" ON "quest_progress"("user_id", "status");

-- CreateIndex
CREATE UNIQUE INDEX "quest_progress_user_id_quest_id_key" ON "quest_progress"("user_id", "quest_id");

-- CreateIndex
CREATE INDEX "friends_friend_id_idx" ON "friends"("friend_id");

-- CreateIndex
CREATE UNIQUE INDEX "friends_user_id_friend_id_key" ON "friends"("user_id", "friend_id");

-- CreateIndex
CREATE INDEX "friend_requests_to_user_id_status_idx" ON "friend_requests"("to_user_id", "status");

-- CreateIndex
CREATE UNIQUE INDEX "friend_requests_from_user_id_to_user_id_key" ON "friend_requests"("from_user_id", "to_user_id");

-- CreateIndex
CREATE INDEX "blocked_players_blocked_id_idx" ON "blocked_players"("blocked_id");

-- CreateIndex
CREATE UNIQUE INDEX "blocked_players_blocker_id_blocked_id_key" ON "blocked_players"("blocker_id", "blocked_id");

-- CreateIndex
CREATE INDEX "moderation_reports_status_created_at_idx" ON "moderation_reports"("status", "created_at");

-- CreateIndex
CREATE INDEX "moderation_reports_reported_id_idx" ON "moderation_reports"("reported_id");

-- CreateIndex
CREATE INDEX "purchases_user_id_created_at_idx" ON "purchases"("user_id", "created_at");

-- CreateIndex
CREATE INDEX "purchases_order_id_idx" ON "purchases"("order_id");

-- CreateIndex
CREATE UNIQUE INDEX "purchases_provider_purchase_token_hash_key" ON "purchases"("provider", "purchase_token_hash");

-- CreateIndex
CREATE INDEX "purchase_events_purchase_id_created_at_idx" ON "purchase_events"("purchase_id", "created_at");

-- CreateIndex
CREATE INDEX "subscriptions_user_id_status_expires_at_idx" ON "subscriptions"("user_id", "status", "expires_at");

-- CreateIndex
CREATE UNIQUE INDEX "subscriptions_provider_purchase_token_hash_key" ON "subscriptions"("provider", "purchase_token_hash");

-- CreateIndex
CREATE INDEX "audit_logs_actor_user_id_created_at_idx" ON "audit_logs"("actor_user_id", "created_at");

-- CreateIndex
CREATE INDEX "audit_logs_action_created_at_idx" ON "audit_logs"("action", "created_at");

-- AddForeignKey
ALTER TABLE "player_profiles" ADD CONSTRAINT "player_profiles_user_id_fkey" FOREIGN KEY ("user_id") REFERENCES "users"("id") ON DELETE CASCADE ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "game_sessions" ADD CONSTRAINT "game_sessions_user_id_fkey" FOREIGN KEY ("user_id") REFERENCES "users"("id") ON DELETE CASCADE ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "player_wallets" ADD CONSTRAINT "player_wallets_user_id_fkey" FOREIGN KEY ("user_id") REFERENCES "users"("id") ON DELETE CASCADE ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "wallet_transactions" ADD CONSTRAINT "wallet_transactions_user_id_fkey" FOREIGN KEY ("user_id") REFERENCES "users"("id") ON DELETE CASCADE ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "wallet_transactions" ADD CONSTRAINT "wallet_transactions_wallet_id_fkey" FOREIGN KEY ("wallet_id") REFERENCES "player_wallets"("id") ON DELETE CASCADE ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "player_inventory" ADD CONSTRAINT "player_inventory_user_id_fkey" FOREIGN KEY ("user_id") REFERENCES "users"("id") ON DELETE CASCADE ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "player_inventory" ADD CONSTRAINT "player_inventory_item_id_fkey" FOREIGN KEY ("item_id") REFERENCES "items"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "player_homes" ADD CONSTRAINT "player_homes_user_id_fkey" FOREIGN KEY ("user_id") REFERENCES "users"("id") ON DELETE CASCADE ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "home_furniture" ADD CONSTRAINT "home_furniture_home_id_fkey" FOREIGN KEY ("home_id") REFERENCES "player_homes"("id") ON DELETE CASCADE ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "home_furniture" ADD CONSTRAINT "home_furniture_item_id_fkey" FOREIGN KEY ("item_id") REFERENCES "items"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "businesses" ADD CONSTRAINT "businesses_user_id_fkey" FOREIGN KEY ("user_id") REFERENCES "users"("id") ON DELETE CASCADE ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "business_inventory" ADD CONSTRAINT "business_inventory_business_id_fkey" FOREIGN KEY ("business_id") REFERENCES "businesses"("id") ON DELETE CASCADE ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "quest_progress" ADD CONSTRAINT "quest_progress_user_id_fkey" FOREIGN KEY ("user_id") REFERENCES "users"("id") ON DELETE CASCADE ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "quest_progress" ADD CONSTRAINT "quest_progress_quest_id_fkey" FOREIGN KEY ("quest_id") REFERENCES "quests"("id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "friends" ADD CONSTRAINT "friends_user_id_fkey" FOREIGN KEY ("user_id") REFERENCES "users"("id") ON DELETE CASCADE ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "friends" ADD CONSTRAINT "friends_friend_id_fkey" FOREIGN KEY ("friend_id") REFERENCES "users"("id") ON DELETE CASCADE ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "friend_requests" ADD CONSTRAINT "friend_requests_from_user_id_fkey" FOREIGN KEY ("from_user_id") REFERENCES "users"("id") ON DELETE CASCADE ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "friend_requests" ADD CONSTRAINT "friend_requests_to_user_id_fkey" FOREIGN KEY ("to_user_id") REFERENCES "users"("id") ON DELETE CASCADE ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "blocked_players" ADD CONSTRAINT "blocked_players_blocker_id_fkey" FOREIGN KEY ("blocker_id") REFERENCES "users"("id") ON DELETE CASCADE ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "blocked_players" ADD CONSTRAINT "blocked_players_blocked_id_fkey" FOREIGN KEY ("blocked_id") REFERENCES "users"("id") ON DELETE CASCADE ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "moderation_reports" ADD CONSTRAINT "moderation_reports_reporter_id_fkey" FOREIGN KEY ("reporter_id") REFERENCES "users"("id") ON DELETE CASCADE ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "moderation_reports" ADD CONSTRAINT "moderation_reports_reported_id_fkey" FOREIGN KEY ("reported_id") REFERENCES "users"("id") ON DELETE CASCADE ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "purchases" ADD CONSTRAINT "purchases_user_id_fkey" FOREIGN KEY ("user_id") REFERENCES "users"("id") ON DELETE CASCADE ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "purchase_events" ADD CONSTRAINT "purchase_events_purchase_id_fkey" FOREIGN KEY ("purchase_id") REFERENCES "purchases"("id") ON DELETE CASCADE ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "subscriptions" ADD CONSTRAINT "subscriptions_user_id_fkey" FOREIGN KEY ("user_id") REFERENCES "users"("id") ON DELETE CASCADE ON UPDATE CASCADE;

-- ---------------------------------------------------------------------------
-- Integrity guards (hand-written): the database itself refuses impossible states
-- even if application code has a bug.
-- ---------------------------------------------------------------------------
ALTER TABLE "player_wallets" ADD CONSTRAINT "player_wallets_balance_non_negative" CHECK ("balance" >= 0);
ALTER TABLE "player_inventory" ADD CONSTRAINT "player_inventory_quantity_non_negative" CHECK ("quantity" >= 0);
ALTER TABLE "business_inventory" ADD CONSTRAINT "business_inventory_quantity_non_negative" CHECK ("quantity" >= 0);
ALTER TABLE "business_inventory" ADD CONSTRAINT "business_inventory_price_positive" CHECK ("sell_price" > 0);
ALTER TABLE "businesses" ADD CONSTRAINT "businesses_level_range" CHECK ("level" BETWEEN 1 AND 10);
ALTER TABLE "wallet_transactions" ADD CONSTRAINT "wallet_transactions_balance_math" CHECK ("balance_after" = "balance_before" + "amount" AND "balance_after" >= 0 AND "amount" <> 0);
ALTER TABLE "quest_progress" ADD CONSTRAINT "quest_progress_non_negative" CHECK ("progress" >= 0);
ALTER TABLE "friends" ADD CONSTRAINT "friends_not_self" CHECK ("user_id" <> "friend_id");
ALTER TABLE "friend_requests" ADD CONSTRAINT "friend_requests_not_self" CHECK ("from_user_id" <> "to_user_id");
ALTER TABLE "blocked_players" ADD CONSTRAINT "blocked_players_not_self" CHECK ("blocker_id" <> "blocked_id");
ALTER TABLE "home_furniture" ADD CONSTRAINT "home_furniture_rotation" CHECK ("rotation" IN (0, 90, 180, 270));

-- Wallet ledger is append-only: block UPDATE/DELETE of history rows.
CREATE OR REPLACE FUNCTION hwo_forbid_ledger_mutation() RETURNS trigger AS $$
BEGIN
  RAISE EXCEPTION 'wallet_transactions is append-only';
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER wallet_transactions_append_only
  BEFORE UPDATE OR DELETE ON "wallet_transactions"
  FOR EACH ROW EXECUTE FUNCTION hwo_forbid_ledger_mutation();
