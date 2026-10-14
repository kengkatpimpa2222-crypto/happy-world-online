-- CreateTable
CREATE TABLE "city_growth" (
    "age_band" "AgeBand" NOT NULL,
    "points" INTEGER NOT NULL DEFAULT 0,
    "updated_at" TIMESTAMP(3) NOT NULL,

    CONSTRAINT "city_growth_pkey" PRIMARY KEY ("age_band")
);

-- CreateTable
CREATE TABLE "city_project_completions" (
    "age_band" "AgeBand" NOT NULL,
    "project_code" VARCHAR(30) NOT NULL,
    "completed_at" TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,

    CONSTRAINT "city_project_completions_pkey" PRIMARY KEY ("age_band","project_code")
);

-- CreateTable
CREATE TABLE "city_builder_contributions" (
    "project_code" VARCHAR(30) NOT NULL,
    "user_id" UUID NOT NULL,
    "age_band" "AgeBand" NOT NULL,
    "points" INTEGER NOT NULL DEFAULT 0,
    "created_at" TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "updated_at" TIMESTAMP(3) NOT NULL,

    CONSTRAINT "city_builder_contributions_pkey" PRIMARY KEY ("project_code","user_id")
);

-- CreateIndex
CREATE INDEX "city_builder_contributions_project_code_age_band_points_idx" ON "city_builder_contributions"("project_code", "age_band", "points" DESC);

-- CreateIndex
CREATE INDEX "city_builder_contributions_user_id_idx" ON "city_builder_contributions"("user_id");

-- AddForeignKey
ALTER TABLE "city_builder_contributions" ADD CONSTRAINT "city_builder_contributions_user_id_fkey" FOREIGN KEY ("user_id") REFERENCES "users"("id") ON DELETE CASCADE ON UPDATE CASCADE;



-- Phase 7 hand-written constraints (Prisma cannot express these)
ALTER TABLE "city_growth" ADD CONSTRAINT "city_growth_points_check" CHECK ("points" >= 0);
ALTER TABLE "city_builder_contributions" ADD CONSTRAINT "city_builder_contributions_points_check" CHECK ("points" >= 0);
