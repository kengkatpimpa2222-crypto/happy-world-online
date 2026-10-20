-- Phase 12: part-time jobs (shifts paid through the existing wallet and item ledgers, Worker skill)

-- AlterEnum
ALTER TYPE "WalletTransactionType" ADD VALUE 'JOB_WAGE';
ALTER TYPE "InventoryTransactionType" ADD VALUE 'JOB_WAGE';
ALTER TYPE "QuestObjectiveType" ADD VALUE 'JOB_SHIFT_COMPLETE';

-- CreateTable
CREATE TABLE "job_shifts" (
    "id" UUID NOT NULL,
    "user_id" UUID NOT NULL,
    "job" VARCHAR(16) NOT NULL,
    "seed" INTEGER NOT NULL,
    "bkk_day" VARCHAR(10) NOT NULL,
    "worker_level" INTEGER NOT NULL,
    "status" VARCHAR(10) NOT NULL DEFAULT 'ACTIVE',
    "started_at" TIMESTAMP(3) NOT NULL DEFAULT CURRENT_TIMESTAMP,
    "finished_at" TIMESTAMP(3),
    "paid" BOOLEAN NOT NULL DEFAULT false,
    "correct" INTEGER NOT NULL DEFAULT 0,
    "wage_gold" INTEGER NOT NULL DEFAULT 0,
    "worker_xp" INTEGER NOT NULL DEFAULT 0,
    "result" JSONB,

    CONSTRAINT "job_shifts_pkey" PRIMARY KEY ("id")
);

-- CreateIndex
CREATE INDEX "job_shifts_user_id_bkk_day_idx" ON "job_shifts"("user_id", "bkk_day");

-- AddForeignKey
ALTER TABLE "job_shifts" ADD CONSTRAINT "job_shifts_user_id_fkey" FOREIGN KEY ("user_id") REFERENCES "users"("id") ON DELETE CASCADE ON UPDATE CASCADE;

-- One open shift per player at a time
CREATE UNIQUE INDEX "job_shifts_one_active_per_user" ON "job_shifts"("user_id") WHERE "status" = 'ACTIVE';

ALTER TABLE "job_shifts" ADD CONSTRAINT "job_shifts_job_check" CHECK ("job" IN ('CASHIER', 'FARMHAND', 'PACKER'));
ALTER TABLE "job_shifts" ADD CONSTRAINT "job_shifts_status_check" CHECK ("status" IN ('ACTIVE', 'DONE', 'EXPIRED'));
-- a handed-in shift has a finish time; only a paid shift has a wage
ALTER TABLE "job_shifts" ADD CONSTRAINT "job_shifts_finish_check" CHECK (("status" = 'ACTIVE') = ("finished_at" IS NULL));
ALTER TABLE "job_shifts" ADD CONSTRAINT "job_shifts_values_check" CHECK ("correct" >= 0 AND "wage_gold" >= 0 AND "worker_xp" >= 0 AND ("paid" OR "wage_gold" = 0));

-- The Worker skill joins Explorer and Crafter
ALTER TABLE "player_skills" DROP CONSTRAINT "player_skills_skill_check";
ALTER TABLE "player_skills" ADD CONSTRAINT "player_skills_skill_check" CHECK ("skill" IN ('EXPLORER', 'CRAFTER', 'WORKER'));
