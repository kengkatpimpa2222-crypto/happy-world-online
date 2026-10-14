-- Phase 5: living shop (market stall, NPC customers, offline sales, return report).
-- Additive only: existing businesses start CLOSED with an empty stall, so current players are unaffected.

-- CreateEnum
CREATE TYPE "ShopStatus" AS ENUM ('CLOSED', 'OPEN', 'PAUSED');

ALTER TYPE "QuestObjectiveType" ADD VALUE 'SHOP_OPEN';
ALTER TYPE "QuestObjectiveType" ADD VALUE 'SHOP_SERVE_CUSTOMERS';

-- AlterTable
ALTER TABLE "business_inventory" ADD COLUMN     "on_stall" BOOLEAN NOT NULL DEFAULT false,
ADD COLUMN     "shelf_quantity" INTEGER NOT NULL DEFAULT 0;

-- AlterTable
ALTER TABLE "businesses" ADD COLUMN     "cash_box" INTEGER NOT NULL DEFAULT 0,
ADD COLUMN     "market_age_band" "AgeBand",
ADD COLUMN     "opened_at" TIMESTAMP(3),
ADD COLUMN     "report_seen_at" TIMESTAMP(3),
ADD COLUMN     "settled_tick" INTEGER,
ADD COLUMN     "shop_status" "ShopStatus" NOT NULL DEFAULT 'CLOSED',
ADD COLUMN     "stall_slot" INTEGER,
ADD COLUMN     "total_customers" INTEGER NOT NULL DEFAULT 0;

-- CreateTable
CREATE TABLE "shop_settlements" (
    "id" UUID NOT NULL,
    "business_id" UUID NOT NULL,
    "from_tick" INTEGER NOT NULL,
    "to_tick" INTEGER NOT NULL,
    "customers" INTEGER NOT NULL,
    "buyers" INTEGER NOT NULL,
    "units_sold" INTEGER NOT NULL,
    "revenue" INTEGER NOT NULL,
    "cost_of_goods" INTEGER NOT NULL,
    "lost_too_expensive" INTEGER NOT NULL,
    "lost_sold_out" INTEGER NOT NULL,
    "lost_not_selling" INTEGER NOT NULL,
    "lines" JSONB NOT NULL,
    "created_at" TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT "shop_settlements_pkey" PRIMARY KEY ("id")
);

-- CreateIndex
CREATE INDEX "shop_settlements_business_id_created_at_idx" ON "shop_settlements"("business_id", "created_at");

-- CreateIndex
CREATE UNIQUE INDEX "shop_settlements_business_id_to_tick_key" ON "shop_settlements"("business_id", "to_tick");

-- CreateIndex
CREATE INDEX "businesses_shop_status_settled_tick_idx" ON "businesses"("shop_status", "settled_tick");

-- AddForeignKey
ALTER TABLE "shop_settlements" ADD CONSTRAINT "shop_settlements_business_id_fkey" FOREIGN KEY ("business_id") REFERENCES "businesses"("id") ON DELETE CASCADE ON UPDATE CASCADE;


-- Hand-written guards (Prisma cannot express these).
ALTER TABLE "businesses" ADD CONSTRAINT "businesses_cash_box_non_negative" CHECK ("cash_box" >= 0);
ALTER TABLE "businesses" ADD CONSTRAINT "businesses_stall_slot_range" CHECK ("stall_slot" IS NULL OR "stall_slot" >= 0);
ALTER TABLE "businesses" ADD CONSTRAINT "businesses_stall_needs_age_band" CHECK ("stall_slot" IS NULL OR "market_age_band" IS NOT NULL);
ALTER TABLE "business_inventory" ADD CONSTRAINT "business_inventory_shelf_non_negative" CHECK ("shelf_quantity" >= 0);
ALTER TABLE "shop_settlements" ADD CONSTRAINT "shop_settlements_non_negative"
  CHECK ("customers" >= 0 AND "buyers" >= 0 AND "units_sold" >= 0 AND "revenue" >= 0 AND "cost_of_goods" >= 0
         AND "lost_too_expensive" >= 0 AND "lost_sold_out" >= 0 AND "lost_not_selling" >= 0 AND "to_tick" > "from_tick");

-- One shop per stall spot in each age band's market (the age bands never see each other's market).
CREATE UNIQUE INDEX "businesses_one_shop_per_stall"
  ON "businesses"("market_age_band", "stall_slot") WHERE "stall_slot" IS NOT NULL;
