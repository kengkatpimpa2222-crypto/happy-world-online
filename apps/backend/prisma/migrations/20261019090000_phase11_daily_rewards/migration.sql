-- Phase 11: daily check-in (7-day track) and daily/weekly missions; rewards go through the existing ledgers

-- AlterEnum
ALTER TYPE "WalletTransactionType" ADD VALUE 'DAILY_CHECKIN';
ALTER TYPE "WalletTransactionType" ADD VALUE 'MISSION_REWARD';
ALTER TYPE "InventoryTransactionType" ADD VALUE 'DAILY_CHECKIN';
ALTER TYPE "InventoryTransactionType" ADD VALUE 'MISSION_REWARD';

-- CreateTable
CREATE TABLE "player_checkins" (
    "user_id" UUID NOT NULL,
    "step" INTEGER NOT NULL,
    "last_day" VARCHAR(10) NOT NULL,
    "total_days" INTEGER NOT NULL,
    "updated_at" TIMESTAMP(3) NOT NULL,

    CONSTRAINT "player_checkins_pkey" PRIMARY KEY ("user_id")
);

-- CreateTable
CREATE TABLE "daily_checkins" (
    "user_id" UUID NOT NULL,
    "bkk_day" VARCHAR(10) NOT NULL,
    "step" INTEGER NOT NULL,
    "reward_gold" INTEGER NOT NULL,
    "reward_item_id" VARCHAR(64),
    "reward_qty" INTEGER NOT NULL DEFAULT 0,
    "claimed_at" TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT "daily_checkins_pkey" PRIMARY KEY ("user_id","bkk_day")
);

-- CreateTable
CREATE TABLE "mission_progress" (
    "user_id" UUID NOT NULL,
    "period_key" VARCHAR(16) NOT NULL,
    "mission_id" VARCHAR(48) NOT NULL,
    "objective" VARCHAR(40) NOT NULL,
    "target" INTEGER NOT NULL,
    "progress" INTEGER NOT NULL DEFAULT 0,
    "status" "QuestStatus" NOT NULL DEFAULT 'IN_PROGRESS',
    "completed_at" TIMESTAMP(3),
    "claimed_at" TIMESTAMP(3),
    "created_at" TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "updated_at" TIMESTAMP(3) NOT NULL,

    CONSTRAINT "mission_progress_pkey" PRIMARY KEY ("user_id","period_key","mission_id")
);

-- AddForeignKey
ALTER TABLE "player_checkins" ADD CONSTRAINT "player_checkins_user_id_fkey" FOREIGN KEY ("user_id") REFERENCES "users"("id") ON DELETE CASCADE ON UPDATE CASCADE;
ALTER TABLE "daily_checkins" ADD CONSTRAINT "daily_checkins_user_id_fkey" FOREIGN KEY ("user_id") REFERENCES "users"("id") ON DELETE CASCADE ON UPDATE CASCADE;
ALTER TABLE "mission_progress" ADD CONSTRAINT "mission_progress_user_id_fkey" FOREIGN KEY ("user_id") REFERENCES "users"("id") ON DELETE CASCADE ON UPDATE CASCADE;

-- Guards the code relies on
ALTER TABLE "player_checkins" ADD CONSTRAINT "player_checkins_step_check" CHECK ("step" BETWEEN 1 AND 7 AND "total_days" >= 1);
ALTER TABLE "daily_checkins" ADD CONSTRAINT "daily_checkins_step_check" CHECK ("step" BETWEEN 1 AND 7 AND "reward_gold" >= 0 AND "reward_qty" >= 0);
ALTER TABLE "mission_progress" ADD CONSTRAINT "mission_progress_values_check" CHECK ("target" > 0 AND "progress" BETWEEN 0 AND "target");
ALTER TABLE "mission_progress" ADD CONSTRAINT "mission_progress_period_check" CHECK ("period_key" ~ '^[DW][0-9]{4}-[0-9]{2}-[0-9]{2}$');
-- A claimed mission is a finished mission
ALTER TABLE "mission_progress" ADD CONSTRAINT "mission_progress_claim_check" CHECK ("status" <> 'CLAIMED' OR ("progress" = "target" AND "claimed_at" IS NOT NULL));

-- Append-only history, like the wallet and item ledgers
CREATE OR REPLACE FUNCTION daily_checkins_append_only() RETURNS trigger AS $$
BEGIN
  RAISE EXCEPTION 'daily_checkins is append-only';
END;
$$ LANGUAGE plpgsql;
CREATE TRIGGER daily_checkins_no_update BEFORE UPDATE ON "daily_checkins" FOR EACH ROW EXECUTE FUNCTION daily_checkins_append_only();
