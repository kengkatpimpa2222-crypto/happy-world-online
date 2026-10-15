-- Phase 10: first home and the residential district (reuses player_homes / home_furniture from the first migration)

-- CreateEnum
CREATE TYPE "HomeVisitPolicy" AS ENUM ('PRIVATE', 'FRIENDS', 'PUBLIC');

-- AlterEnum
-- This migration adds more than one value to an enum.
-- With PostgreSQL versions 11 and earlier, this is not possible
-- in a single migration. This can be worked around by creating
-- multiple migrations, each migration adding only one value to
-- the enum.


ALTER TYPE "InventoryTransactionType" ADD VALUE 'HOME_PLACE';
ALTER TYPE "InventoryTransactionType" ADD VALUE 'HOME_REMOVE';
ALTER TYPE "InventoryTransactionType" ADD VALUE 'HOME_STARTER_GIFT';

-- AlterTable
ALTER TABLE "player_homes" ADD COLUMN     "claimed_at" TIMESTAMP(3),
ADD COLUMN     "layout_save_key" VARCHAR(64),
ADD COLUMN     "plot_age_band" "AgeBand",
ADD COLUMN     "plot_number" INTEGER,
ADD COLUMN     "visit_policy" "HomeVisitPolicy" NOT NULL DEFAULT 'FRIENDS';


-- One owner per plot in each age band's district
CREATE UNIQUE INDEX "player_homes_plot_key" ON "player_homes"("plot_age_band", "plot_number") WHERE "plot_number" IS NOT NULL;

-- A home is either unclaimed (no plot) or claimed (plot + age band + time), never half of each
ALTER TABLE "player_homes" ADD CONSTRAINT "player_homes_claim_check" CHECK (
  ("plot_number" IS NULL AND "plot_age_band" IS NULL AND "claimed_at" IS NULL)
  OR ("plot_number" IS NOT NULL AND "plot_age_band" IS NOT NULL AND "claimed_at" IS NOT NULL)
);
ALTER TABLE "player_homes" ADD CONSTRAINT "player_homes_plot_number_check" CHECK ("plot_number" IS NULL OR "plot_number" >= 0);
ALTER TABLE "player_homes" ADD CONSTRAINT "player_homes_grid_check" CHECK ("grid_width" BETWEEN 1 AND 64 AND "grid_height" BETWEEN 1 AND 64);
ALTER TABLE "player_homes" ADD CONSTRAINT "player_homes_layout_version_check" CHECK ("layout_version" >= 0);
ALTER TABLE "home_furniture" ADD CONSTRAINT "home_furniture_position_check" CHECK ("x" >= 0 AND "y" >= 0);
