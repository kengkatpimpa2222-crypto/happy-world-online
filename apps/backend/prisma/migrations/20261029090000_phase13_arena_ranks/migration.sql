-- Phase 13: Splash Arena (opt-in team mode) with seasonal ranks; rewards go through the wallet ledger

-- AlterEnum
ALTER TYPE "WalletTransactionType" ADD VALUE 'ARENA_REWARD';

-- CreateTable
CREATE TABLE "arena_ratings" (
    "user_id" UUID NOT NULL,
    "season" VARCHAR(7) NOT NULL,
    "age_band" "AgeBand" NOT NULL,
    "rating" INTEGER NOT NULL,
    "peak" INTEGER NOT NULL,
    "wins" INTEGER NOT NULL DEFAULT 0,
    "losses" INTEGER NOT NULL DEFAULT 0,
    "draws" INTEGER NOT NULL DEFAULT 0,
    "updated_at" TIMESTAMP(3) NOT NULL,

    CONSTRAINT "arena_ratings_pkey" PRIMARY KEY ("user_id","season")
);

-- CreateTable
CREATE TABLE "arena_matches" (
    "id" UUID NOT NULL,
    "season" VARCHAR(7) NOT NULL,
    "age_band" "AgeBand" NOT NULL,
    "with_bots" BOOLEAN NOT NULL,
    "score_red" INTEGER NOT NULL,
    "score_blue" INTEGER NOT NULL,
    "winner" VARCHAR(4) NOT NULL,
    "started_at" TIMESTAMP(3) NOT NULL,
    "ended_at" TIMESTAMP(3) NOT NULL,

    CONSTRAINT "arena_matches_pkey" PRIMARY KEY ("id")
);

-- CreateTable
CREATE TABLE "arena_match_players" (
    "match_id" UUID NOT NULL,
    "user_id" UUID NOT NULL,
    "team" VARCHAR(4) NOT NULL,
    "outcome" VARCHAR(4) NOT NULL,
    "left_early" BOOLEAN NOT NULL,
    "soaks" INTEGER NOT NULL,
    "soaked_times" INTEGER NOT NULL,
    "rating_before" INTEGER NOT NULL,
    "rating_after" INTEGER NOT NULL,
    "reward_gold" INTEGER NOT NULL,
    "bkk_day" VARCHAR(10) NOT NULL,

    CONSTRAINT "arena_match_players_pkey" PRIMARY KEY ("match_id","user_id")
);

-- CreateIndex
CREATE INDEX "arena_ratings_season_age_band_rating_idx" ON "arena_ratings"("season", "age_band", "rating" DESC);

-- CreateIndex
CREATE INDEX "arena_match_players_user_id_bkk_day_idx" ON "arena_match_players"("user_id", "bkk_day");

-- AddForeignKey
ALTER TABLE "arena_ratings" ADD CONSTRAINT "arena_ratings_user_id_fkey" FOREIGN KEY ("user_id") REFERENCES "users"("id") ON DELETE CASCADE ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "arena_match_players" ADD CONSTRAINT "arena_match_players_match_id_fkey" FOREIGN KEY ("match_id") REFERENCES "arena_matches"("id") ON DELETE CASCADE ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "arena_match_players" ADD CONSTRAINT "arena_match_players_user_id_fkey" FOREIGN KEY ("user_id") REFERENCES "users"("id") ON DELETE CASCADE ON UPDATE CASCADE;


ALTER TABLE "arena_ratings" ADD CONSTRAINT "arena_ratings_values_check" CHECK ("rating" >= 0 AND "peak" >= "rating" AND "wins" >= 0 AND "losses" >= 0 AND "draws" >= 0);
ALTER TABLE "arena_matches" ADD CONSTRAINT "arena_matches_winner_check" CHECK ("winner" IN ('RED', 'BLUE', 'DRAW'));
ALTER TABLE "arena_matches" ADD CONSTRAINT "arena_matches_score_check" CHECK ("score_red" >= 0 AND "score_blue" >= 0 AND "ended_at" >= "started_at");
ALTER TABLE "arena_match_players" ADD CONSTRAINT "arena_match_players_team_check" CHECK ("team" IN ('RED', 'BLUE'));
ALTER TABLE "arena_match_players" ADD CONSTRAINT "arena_match_players_outcome_check" CHECK ("outcome" IN ('WIN', 'DRAW', 'LOSS'));
-- leaving early is always a loss; nobody earns Gold for walking out
ALTER TABLE "arena_match_players" ADD CONSTRAINT "arena_match_players_left_check" CHECK (NOT "left_early" OR ("outcome" = 'LOSS' AND "reward_gold" = 0));
ALTER TABLE "arena_match_players" ADD CONSTRAINT "arena_match_players_values_check" CHECK ("soaks" >= 0 AND "soaked_times" >= 0 AND "rating_after" >= 0 AND "reward_gold" >= 0);
