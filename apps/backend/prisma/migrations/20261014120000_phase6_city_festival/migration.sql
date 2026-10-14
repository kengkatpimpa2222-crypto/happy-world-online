-- AlterEnum
ALTER TYPE "QuestObjectiveType" ADD VALUE 'FESTIVAL_CONTRIBUTE';

-- AlterEnum
ALTER TYPE "WalletTransactionType" ADD VALUE 'FESTIVAL_REWARD';

-- CreateTable
CREATE TABLE "festival_city_progress" (
    "festival_key" VARCHAR(40) NOT NULL,
    "age_band" "AgeBand" NOT NULL,
    "points" INTEGER NOT NULL DEFAULT 0,
    "contributors" INTEGER NOT NULL DEFAULT 0,
    "updated_at" TIMESTAMP(3) NOT NULL,

    CONSTRAINT "festival_city_progress_pkey" PRIMARY KEY ("festival_key","age_band")
);

-- CreateTable
CREATE TABLE "festival_contributions" (
    "festival_key" VARCHAR(40) NOT NULL,
    "user_id" UUID NOT NULL,
    "age_band" "AgeBand" NOT NULL,
    "points" INTEGER NOT NULL DEFAULT 0,
    "created_at" TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "updated_at" TIMESTAMP(3) NOT NULL,

    CONSTRAINT "festival_contributions_pkey" PRIMARY KEY ("festival_key","user_id")
);

-- CreateTable
CREATE TABLE "festival_reward_claims" (
    "festival_key" VARCHAR(40) NOT NULL,
    "user_id" UUID NOT NULL,
    "tier" INTEGER NOT NULL,
    "claimed_at" TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT "festival_reward_claims_pkey" PRIMARY KEY ("festival_key","user_id","tier")
);

-- CreateIndex
CREATE INDEX "festival_contributions_festival_key_age_band_points_idx" ON "festival_contributions"("festival_key", "age_band", "points" DESC);

-- AddForeignKey
ALTER TABLE "festival_contributions" ADD CONSTRAINT "festival_contributions_user_id_fkey" FOREIGN KEY ("user_id") REFERENCES "users"("id") ON DELETE CASCADE ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "festival_reward_claims" ADD CONSTRAINT "festival_reward_claims_user_id_fkey" FOREIGN KEY ("user_id") REFERENCES "users"("id") ON DELETE CASCADE ON UPDATE CASCADE;


-- Phase 6 hand-written constraints (Prisma cannot express these)
ALTER TABLE "festival_city_progress" ADD CONSTRAINT "festival_city_progress_points_check" CHECK ("points" >= 0 AND "contributors" >= 0);
ALTER TABLE "festival_contributions" ADD CONSTRAINT "festival_contributions_points_check" CHECK ("points" >= 0);
ALTER TABLE "festival_reward_claims" ADD CONSTRAINT "festival_reward_claims_tier_check" CHECK ("tier" BETWEEN 1 AND 3);
