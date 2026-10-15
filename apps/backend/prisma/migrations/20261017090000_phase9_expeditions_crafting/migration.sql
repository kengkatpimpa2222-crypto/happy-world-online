-- Phase 9: exploring, materials (item ledger) and crafting

-- CreateEnum
CREATE TYPE "InventoryTransactionType" AS ENUM ('EXPEDITION_LOOT', 'CRAFT_CONSUME', 'ADMIN_ADJUSTMENT');

-- AlterEnum
ALTER TYPE "WalletTransactionType" ADD VALUE 'CRAFT_FEE';

-- AlterEnum
ALTER TYPE "ItemCategory" ADD VALUE 'MATERIAL';

-- AlterEnum
-- This migration adds more than one value to an enum.
-- With PostgreSQL versions 11 and earlier, this is not possible
-- in a single migration. This can be worked around by creating
-- multiple migrations, each migration adding only one value to
-- the enum.


ALTER TYPE "QuestObjectiveType" ADD VALUE 'EXPEDITION_COMPLETE';
ALTER TYPE "QuestObjectiveType" ADD VALUE 'GATHER_MATERIAL';
ALTER TYPE "QuestObjectiveType" ADD VALUE 'CRAFT_ITEM';

-- CreateTable
CREATE TABLE "inventory_transactions" (
    "id" UUID NOT NULL,
    "user_id" UUID NOT NULL,
    "item_id" VARCHAR(64) NOT NULL,
    "type" "InventoryTransactionType" NOT NULL,
    "delta" INTEGER NOT NULL,
    "quantity_after" INTEGER NOT NULL,
    "reference_type" VARCHAR(40),
    "reference_id" VARCHAR(120),
    "idempotency_key" VARCHAR(160) NOT NULL,
    "created_at" TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT "inventory_transactions_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "player_skills" (
    "user_id" UUID NOT NULL,
    "skill" VARCHAR(16) NOT NULL,
    "xp" INTEGER NOT NULL DEFAULT 0,
    "updated_at" TIMESTAMP(3) NOT NULL,

    CONSTRAINT "player_skills_pkey" PRIMARY KEY ("user_id","skill")
);

-- CreateTable
CREATE TABLE "expeditions" (
    "id" UUID NOT NULL,
    "user_id" UUID NOT NULL,
    "site" VARCHAR(24) NOT NULL,
    "plan" VARCHAR(12) NOT NULL,
    "seed" INTEGER NOT NULL,
    "explorer_level" INTEGER NOT NULL,
    "started_at" TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "ends_at" TIMESTAMP(3) NOT NULL,
    "claimed_at" TIMESTAMP(3),
    "result" JSONB,

    CONSTRAINT "expeditions_pkey" PRIMARY KEY ("id")
);

-- CreateIndex
CREATE INDEX "inventory_transactions_user_id_created_at_idx" ON "inventory_transactions"("user_id", "created_at");

-- CreateIndex
CREATE UNIQUE INDEX "inventory_transactions_user_id_idempotency_key_key" ON "inventory_transactions"("user_id", "idempotency_key");

-- CreateIndex
CREATE INDEX "expeditions_user_id_started_at_idx" ON "expeditions"("user_id", "started_at");

-- AddForeignKey
ALTER TABLE "inventory_transactions" ADD CONSTRAINT "inventory_transactions_user_id_fkey" FOREIGN KEY ("user_id") REFERENCES "users"("id") ON DELETE CASCADE ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "player_skills" ADD CONSTRAINT "player_skills_user_id_fkey" FOREIGN KEY ("user_id") REFERENCES "users"("id") ON DELETE CASCADE ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "expeditions" ADD CONSTRAINT "expeditions_user_id_fkey" FOREIGN KEY ("user_id") REFERENCES "users"("id") ON DELETE CASCADE ON UPDATE CASCADE;


-- One unclaimed trip per player at a time
CREATE UNIQUE INDEX "expeditions_one_active_per_user" ON "expeditions"("user_id") WHERE "claimed_at" IS NULL;

ALTER TABLE "expeditions" ADD CONSTRAINT "expeditions_site_check" CHECK ("site" IN ('ORCHARD', 'FLOWER_FOREST', 'HERB_VALLEY'));
ALTER TABLE "expeditions" ADD CONSTRAINT "expeditions_plan_check" CHECK ("plan" IN ('CAREFUL', 'STEADY', 'BOLD'));
ALTER TABLE "expeditions" ADD CONSTRAINT "expeditions_time_check" CHECK ("ends_at" > "started_at");
ALTER TABLE "player_skills" ADD CONSTRAINT "player_skills_skill_check" CHECK ("skill" IN ('EXPLORER', 'CRAFTER'));
ALTER TABLE "player_skills" ADD CONSTRAINT "player_skills_xp_check" CHECK ("xp" >= 0);
ALTER TABLE "inventory_transactions" ADD CONSTRAINT "inventory_transactions_delta_check" CHECK ("delta" <> 0 AND "quantity_after" >= 0);

-- The item ledger is append-only, like wallet_transactions
CREATE OR REPLACE FUNCTION hwo_forbid_item_ledger_mutation() RETURNS trigger AS $$
BEGIN
  RAISE EXCEPTION 'inventory_transactions is append-only';
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER inventory_transactions_append_only
  BEFORE UPDATE OR DELETE ON "inventory_transactions"
  FOR EACH ROW EXECUTE FUNCTION hwo_forbid_item_ledger_mutation();
