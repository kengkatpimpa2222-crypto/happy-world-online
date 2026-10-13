-- Phase 3: friends / block / moderation hardening.
-- Hand-edited so it is safe on databases that already contain friend requests.

-- A player may re-send a request after it was declined/cancelled, so the old
-- (from_user_id, to_user_id) unique key is replaced by "one PENDING request per pair".
DROP INDEX "friend_requests_from_user_id_to_user_id_key";

-- Add pair_key, back-fill existing rows, then make it required.
ALTER TABLE "friend_requests" ADD COLUMN "pair_key" VARCHAR(73);
UPDATE "friend_requests"
   SET "pair_key" = LEAST("from_user_id"::text, "to_user_id"::text) || ':' || GREATEST("from_user_id"::text, "to_user_id"::text);
ALTER TABLE "friend_requests" ALTER COLUMN "pair_key" SET NOT NULL;
ALTER TABLE "friend_requests" ADD CONSTRAINT "friend_requests_pair_key_matches"
  CHECK ("pair_key" = LEAST("from_user_id"::text, "to_user_id"::text) || ':' || GREATEST("from_user_id"::text, "to_user_id"::text));

-- If old data already holds two PENDING requests for one pair, keep the oldest and cancel the rest
-- so the unique index below can be created.
UPDATE "friend_requests" fr
   SET "status" = 'CANCELLED', "responded_at" = now()
 WHERE fr."status" = 'PENDING'
   AND EXISTS (SELECT 1 FROM "friend_requests" o
                WHERE o."pair_key" = fr."pair_key" AND o."status" = 'PENDING'
                  AND (o."created_at", o."id") < (fr."created_at", fr."id"));

CREATE INDEX "friend_requests_from_user_id_status_idx" ON "friend_requests"("from_user_id", "status");
CREATE INDEX "friend_requests_pair_key_idx" ON "friend_requests"("pair_key");
-- At most one open request per pair: blocks duplicates AND crossed requests (A->B while B->A).
CREATE UNIQUE INDEX "friend_requests_one_pending_per_pair"
  ON "friend_requests"("pair_key") WHERE "status" = 'PENDING';

CREATE INDEX "moderation_reports_reporter_id_created_at_idx" ON "moderation_reports"("reporter_id", "created_at");
-- Retrying the same report while it is still open must not create duplicates.
CREATE UNIQUE INDEX "moderation_reports_one_open_per_reason"
  ON "moderation_reports"("reporter_id", "reported_id", "reason") WHERE "status" = 'OPEN';
