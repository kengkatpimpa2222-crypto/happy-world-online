-- Mini-games: make goods by hand (CATCH / GRILL / POUR rounds)

-- CreateTable
CREATE TABLE "minigame_sessions" (
    "id" UUID NOT NULL,
    "user_id" UUID NOT NULL,
    "business_id" UUID NOT NULL,
    "game" VARCHAR(16) NOT NULL,
    "product_code" VARCHAR(32) NOT NULL,
    "seed" INTEGER NOT NULL,
    "bkk_day" VARCHAR(10) NOT NULL,
    "started_at" TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "finished_at" TIMESTAMP(3),
    "score" INTEGER,
    "stars" INTEGER,
    "rewarded" BOOLEAN NOT NULL DEFAULT false,
    "units_awarded" INTEGER NOT NULL DEFAULT 0,
    "xp_awarded" INTEGER NOT NULL DEFAULT 0,

    CONSTRAINT "minigame_sessions_pkey" PRIMARY KEY ("id"),
    CONSTRAINT "minigame_sessions_game_check" CHECK ("game" IN ('CATCH', 'GRILL', 'POUR')),
    CONSTRAINT "minigame_sessions_score_check" CHECK ("score" IS NULL OR "score" >= 0),
    CONSTRAINT "minigame_sessions_stars_check" CHECK ("stars" IS NULL OR "stars" BETWEEN 0 AND 3),
    CONSTRAINT "minigame_sessions_units_check" CHECK ("units_awarded" >= 0 AND "xp_awarded" >= 0)
);

-- CreateIndex
CREATE INDEX "minigame_sessions_user_id_bkk_day_idx" ON "minigame_sessions"("user_id", "bkk_day");

-- CreateIndex
CREATE INDEX "minigame_sessions_user_id_game_score_idx" ON "minigame_sessions"("user_id", "game", "score");

-- AddForeignKey
ALTER TABLE "minigame_sessions" ADD CONSTRAINT "minigame_sessions_user_id_fkey" FOREIGN KEY ("user_id") REFERENCES "users"("id") ON DELETE CASCADE ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "minigame_sessions" ADD CONSTRAINT "minigame_sessions_business_id_fkey" FOREIGN KEY ("business_id") REFERENCES "businesses"("id") ON DELETE CASCADE ON UPDATE CASCADE;
