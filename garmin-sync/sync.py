"""Pull Garmin data into the WorkoutLogger database.

Shape follows the one already used for workouts: the raw payload is kept as
jsonb, with the handful of columns worth querying extracted alongside it. That
way a Garmin field nobody thought about today is still there tomorrow, and
adding a column later is a migration rather than a re-fetch.

Deliberately does NOT write to Activities. Health Connect remains the activity
spine; these tables sit beside it. Changing that is a decision to make after
reading probe.py's output, not a default.

    docker compose run --rm garmin-sync python sync.py            # incremental
    docker compose run --rm garmin-sync python sync.py --days 365 # backfill
"""

from __future__ import annotations

import argparse
import json
import logging
import os
import sys
from datetime import date, datetime, timedelta, timezone
from typing import Any

import psycopg
from psycopg.types.json import Jsonb

import matcher
import sets as setlib
from garmin_client import GarminNotConfigured, GarminUnavailable, connect

logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)-7s %(message)s")
logger = logging.getLogger("garmin-sync")

USER_ID = int(os.getenv("GARMIN_SYNC_USER_ID", "0"))


def dsn() -> str:
    url = os.getenv("GARMIN_SYNC_DSN")
    if url:
        return url
    return (
        f"host={os.getenv('PGHOST', 'db')} "
        f"dbname={os.getenv('PGDATABASE', 'workoutlogger')} "
        f"user={os.getenv('PGUSER', 'postgres')} "
        f"password={os.getenv('PGPASSWORD', '')}"
    )


def _num(d: Any, *path: str) -> Any:
    """Walk a nested payload, returning None rather than raising on any miss."""
    cur = d
    for key in path:
        if not isinstance(cur, dict):
            return None
        cur = cur.get(key)
    return cur if isinstance(cur, (int, float)) else None


def _first(value: Any) -> dict[str, Any]:
    """Several of these endpoints return a single-element list."""
    if isinstance(value, list):
        return value[0] if value and isinstance(value[0], dict) else {}
    return value if isinstance(value, dict) else {}


def safe(label: str, fn: Any) -> Any:
    try:
        return fn()
    except Exception as e:  # noqa: BLE001
        logger.debug("%s unavailable: %s", label, e)
        return None


def _training_status(payload: dict[str, Any]) -> str | None:
    """Two shapes exist in the wild, so try both.

    The probe showed this endpoint returning `mostRecentTrainingStatus` (null on
    a device that does not compute training status at all), while older accounts
    return `latestTrainingStatusData` keyed by device serial. An earlier version
    of this function only knew the second shape and so silently returned None
    for everyone on the first.
    """
    payload = payload or {}

    recent = payload.get("mostRecentTrainingStatus")
    if isinstance(recent, dict):
        devices = recent.get("latestTrainingStatusData")
        if isinstance(devices, dict):
            for entry in devices.values():
                if isinstance(entry, dict):
                    phrase = (entry.get("trainingStatusFeedbackPhrase")
                              or entry.get("trainingStatus"))
                    if phrase:
                        return str(phrase)[:40]
        phrase = recent.get("trainingStatusFeedbackPhrase") or recent.get("trainingStatus")
        if phrase:
            return str(phrase)[:40]

    devices = payload.get("latestTrainingStatusData")
    if isinstance(devices, dict):
        for entry in devices.values():
            if isinstance(entry, dict):
                phrase = (entry.get("trainingStatusFeedbackPhrase")
                          or entry.get("trainingStatus"))
                if phrase:
                    return str(phrase)[:40]
    return None


def _vo2max(max_metrics: dict[str, Any], status: dict[str, Any]) -> float | None:
    """get_max_metrics came back [] on the probed account, so fall back to the
    copy carried inside get_training_status. Both are null on a device that does
    not estimate VO2max at all, which is a legitimate answer, not an error."""
    direct = _num(max_metrics, "generic", "vo2MaxPreciseValue")
    if direct is not None:
        return direct
    return _num(status, "mostRecentVO2Max", "generic", "vo2MaxPreciseValue")

# --------------------------------------------------------------------------
# daily metrics
# --------------------------------------------------------------------------

DAILY_UPSERT = """
INSERT INTO "GarminDailyMetrics" (
    "UserId", "Date", "RestingHeartRate", "HrvLastNightAvg", "HrvStatus",
    "BodyBatteryHigh", "BodyBatteryLow", "SleepScore", "SleepSeconds",
    "TrainingReadinessScore", "TrainingStatus", "Vo2MaxRunning",
    "AverageStress", "RawJson", "CreatedAt", "UpdatedAt"
) VALUES (
    %(user_id)s, %(date)s, %(resting_hr)s, %(hrv_avg)s, %(hrv_status)s,
    %(bb_high)s, %(bb_low)s, %(sleep_score)s, %(sleep_seconds)s,
    %(readiness)s, %(training_status)s, %(vo2max)s,
    %(avg_stress)s, %(raw)s, %(now)s, %(now)s
)
ON CONFLICT ("UserId", "Date") DO UPDATE SET
    "RestingHeartRate"       = EXCLUDED."RestingHeartRate",
    "HrvLastNightAvg"        = EXCLUDED."HrvLastNightAvg",
    "HrvStatus"              = EXCLUDED."HrvStatus",
    "BodyBatteryHigh"        = EXCLUDED."BodyBatteryHigh",
    "BodyBatteryLow"         = EXCLUDED."BodyBatteryLow",
    "SleepScore"             = EXCLUDED."SleepScore",
    "SleepSeconds"           = EXCLUDED."SleepSeconds",
    "TrainingReadinessScore" = EXCLUDED."TrainingReadinessScore",
    "TrainingStatus"         = EXCLUDED."TrainingStatus",
    "Vo2MaxRunning"          = EXCLUDED."Vo2MaxRunning",
    "AverageStress"          = EXCLUDED."AverageStress",
    "RawJson"                = EXCLUDED."RawJson",
    "UpdatedAt"              = EXCLUDED."UpdatedAt";
"""


def sync_day(api: Any, cur: psycopg.Cursor, day: date) -> None:
    cdate = day.isoformat()

    summary = safe("user_summary", lambda: api.get_user_summary(cdate)) or {}
    hrv = safe("hrv", lambda: api.get_hrv_data(cdate)) or {}
    sleep = safe("sleep", lambda: api.get_sleep_data(cdate)) or {}
    readiness = _first(safe("readiness", lambda: api.get_training_readiness(cdate)))
    status = safe("training_status", lambda: api.get_training_status(cdate)) or {}
    max_metrics = _first(safe("max_metrics", lambda: api.get_max_metrics(cdate)))

    raw = {
        "userSummary": summary,
        "hrv": hrv,
        "sleep": sleep,
        "trainingReadiness": readiness,
        "trainingStatus": status,
        "maxMetrics": max_metrics,
    }

    if not any(raw.values()):
        logger.info("%s  no data", cdate)
        return

    cur.execute(
        DAILY_UPSERT,
        {
            "user_id": USER_ID,
            "date": day,
            "resting_hr": _num(summary, "restingHeartRate"),
            "hrv_avg": _num(hrv, "hrvSummary", "lastNightAvg"),
            "hrv_status": (hrv.get("hrvSummary") or {}).get("status"),
            "bb_high": _num(summary, "bodyBatteryHighestValue"),
            "bb_low": _num(summary, "bodyBatteryLowestValue"),
            "sleep_score": _num(sleep, "dailySleepDTO", "sleepScores", "overall", "value"),
            "sleep_seconds": _num(sleep, "dailySleepDTO", "sleepTimeSeconds"),
            "readiness": _num(readiness, "score"),
            "training_status": _training_status(status),
            "vo2max": _vo2max(max_metrics, status),
            "avg_stress": _num(summary, "averageStressLevel"),
            "raw": Jsonb(raw),
            "now": datetime.now(timezone.utc),
        },
    )
    logger.info("%s  upserted", cdate)


# --------------------------------------------------------------------------
# activity detail
# --------------------------------------------------------------------------

ACTIVITY_UPSERT = """
INSERT INTO "GarminActivityDetails" (
    "UserId", "GarminActivityId", "StartTime", "EndTime", "ActivityType", "Title",
    "DurationSeconds", "AverageHeartRate", "MaxHeartRate", "Calories",
    "DistanceMeters", "AerobicTrainingEffect", "AnaerobicTrainingEffect",
    "AverageWatts", "ActiveSetCount", "TotalReps", "TotalWeightKg",
    "RawSummaryJson", "RawSetsJson", "CreatedAt", "UpdatedAt"
) VALUES (
    %(user_id)s, %(garmin_id)s, %(start)s, %(end)s, %(type)s, %(title)s,
    %(duration)s, %(avg_hr)s, %(max_hr)s, %(calories)s,
    %(distance)s, %(aerobic)s, %(anaerobic)s,
    %(watts)s, %(set_count)s, %(reps)s, %(weight)s,
    %(raw_summary)s, %(raw_sets)s, %(now)s, %(now)s
)
ON CONFLICT ("UserId", "GarminActivityId") DO UPDATE SET
    -- StartTime is updated too. It was omitted originally on the assumption it
    -- never changes, which made the timezone bug above unrepairable by re-sync.
    "StartTime"               = EXCLUDED."StartTime",
    "EndTime"                 = EXCLUDED."EndTime",
    "Title"                   = EXCLUDED."Title",
    "DurationSeconds"         = EXCLUDED."DurationSeconds",
    "AverageHeartRate"        = EXCLUDED."AverageHeartRate",
    "MaxHeartRate"            = EXCLUDED."MaxHeartRate",
    "Calories"                = EXCLUDED."Calories",
    "DistanceMeters"          = EXCLUDED."DistanceMeters",
    "AerobicTrainingEffect"   = EXCLUDED."AerobicTrainingEffect",
    "AnaerobicTrainingEffect" = EXCLUDED."AnaerobicTrainingEffect",
    "AverageWatts"            = EXCLUDED."AverageWatts",
    "ActiveSetCount"          = EXCLUDED."ActiveSetCount",
    "TotalReps"               = EXCLUDED."TotalReps",
    "TotalWeightKg"           = EXCLUDED."TotalWeightKg",
    "RawSummaryJson"          = EXCLUDED."RawSummaryJson",
    "RawSetsJson"             = EXCLUDED."RawSetsJson",
    "UpdatedAt"               = EXCLUDED."UpdatedAt"
RETURNING "Id";
"""

# Attach to an existing Health-Connect activity by overlap. Same session
# recorded twice by two paths, so the windows are close but never identical -
# the watch and the phone do not agree on the second.
LINK_SQL = """
UPDATE "GarminActivityDetails" g
SET "ActivityId" = a."Id"
FROM "Activities" a
WHERE g."ActivityId" IS NULL
  AND a."UserId" = g."UserId"
  AND a."DeletedAt" IS NULL
  AND a."StartTime" < g."EndTime" + interval '5 minutes'
  AND a."EndTime"   > g."StartTime" - interval '5 minutes';
"""


SET_UPSERT = """
INSERT INTO "GarminExerciseSets" (
    "GarminActivityDetailId", "SetIndex", "StartTime", "DurationSeconds",
    "GarminReps", "GarminCategory", "GarminCategoryProbability",
    "AverageHeartRate", "MaxHeartRate", "MinHeartRate", "RestSecondsAfter",
    "CreatedAt", "UpdatedAt"
) VALUES (
    %(detail_id)s, %(set_index)s, %(start)s, %(duration)s,
    %(reps)s, %(category)s, %(probability)s,
    %(hr_avg)s, %(hr_max)s, %(hr_min)s, %(rest_after)s,
    %(now)s, %(now)s
)
ON CONFLICT ("GarminActivityDetailId", "SetIndex") DO UPDATE SET
    "StartTime"                 = EXCLUDED."StartTime",
    "DurationSeconds"           = EXCLUDED."DurationSeconds",
    "GarminReps"                = EXCLUDED."GarminReps",
    "GarminCategory"            = EXCLUDED."GarminCategory",
    "GarminCategoryProbability" = EXCLUDED."GarminCategoryProbability",
    "AverageHeartRate"          = EXCLUDED."AverageHeartRate",
    "MaxHeartRate"              = EXCLUDED."MaxHeartRate",
    "MinHeartRate"              = EXCLUDED."MinHeartRate",
    "RestSecondsAfter"          = EXCLUDED."RestSecondsAfter",
    "UpdatedAt"                 = EXCLUDED."UpdatedAt";
    -- ExerciseSetId and MatchConfidence are deliberately NOT touched here.
    -- The matcher owns them, and a re-sync of the Garmin side must not silently
    -- discard a pairing (or a hand correction) that is still valid.
"""

# The logged sets for whichever workout this activity is attached to, in the only
# order the two sides share: exercise order, then set number.
LOGGED_SETS_SQL = """
SELECT es."Id", es."Repetitions", e."Order"
FROM "GarminActivityDetails" g
JOIN "Activities"   a  ON a."Id" = g."ActivityId"
JOIN "Workouts"     w  ON w."Id" = a."WorkoutId"
JOIN "Exercises"    e  ON e."WorkoutId" = w."Id"
JOIN "ExerciseSets" es ON es."ExerciseId" = e."Id"
WHERE g."Id" = %(detail_id)s
  AND a."DeletedAt" IS NULL
  AND w."DeletedAt" IS NULL
ORDER BY e."Order", es."SetNumber";
"""

GARMIN_SETS_SQL = """
SELECT "Id", "SetIndex", "GarminReps"
FROM "GarminExerciseSets"
WHERE "GarminActivityDetailId" = %(detail_id)s
ORDER BY "SetIndex";
"""

APPLY_MATCH_SQL = """
UPDATE "GarminExerciseSets"
SET "ExerciseSetId" = %(exercise_set_id)s,
    "MatchConfidence" = %(confidence)s,
    "UpdatedAt" = %(now)s
WHERE "Id" = %(id)s;
"""

# Activities worth matching: attached to a workout, and carrying sets.
MATCHABLE_SQL = """
SELECT DISTINCT g."Id"
FROM "GarminActivityDetails" g
JOIN "GarminExerciseSets" s ON s."GarminActivityDetailId" = g."Id"
WHERE g."UserId" = %(user_id)s
  AND g."ActivityId" IS NOT NULL
  AND g."StartTime" >= %(since)s;
"""


def match_sets(cur: psycopg.Cursor, detail_id: int) -> dict[str, Any] | None:
    """Pair this activity's recorded sets with the logged ones.

    Exercise, reps and weight stay with the logged set - Garmin's labels are
    unreliable and it never knows the load. What gets attached is heart rate and
    rest. Where the alignment cannot place a set, the pairing is left null rather
    than forced.
    """
    cur.execute(LOGGED_SETS_SQL, {"detail_id": detail_id})
    logged = cur.fetchall()
    cur.execute(GARMIN_SETS_SQL, {"detail_id": detail_id})
    recorded = cur.fetchall()

    if not logged or not recorded:
        return None

    # Set counts per exercise are the whole basis of the match. Reps are not
    # used: the watch's rep counter fails outright on some movements (10
    # kettlebell adductors reported as 0), so it is not evidence of anything.
    sizes: list[int] = []
    current_order = None
    for _set_id, _reps, order in logged:
        if order != current_order:
            sizes.append(0)
            current_order = order
        sizes[-1] += 1

    alignment = matcher.align_by_exercise(sizes, len(recorded))
    now = datetime.now(timezone.utc)

    for logged_i, garmin_i, conf in alignment:
        if garmin_i is None:
            continue
        cur.execute(
            APPLY_MATCH_SQL,
            {
                "id": recorded[garmin_i][0],
                "exercise_set_id": logged[logged_i][0] if logged_i is not None else None,
                "confidence": conf,
                "now": now,
            },
        )

    return matcher.summarise(alignment)


def _parse(ts: str | None) -> datetime | None:
    """Parse a Garmin `startTimeGMT` into an AWARE UTC datetime.

    The tzinfo is load-bearing, not decoration. EF maps DateTime to
    `timestamptz`, and handing psycopg a NAIVE datetime makes Postgres
    interpret it in the session's TimeZone - Europe/Kiev here - so 08:55 GMT
    was stored as 08:55+03:00, i.e. 05:55 UTC. Every Garmin row landed three
    hours early, the +/-5 minute overlap in LINK_SQL could never match, and the
    symptom looked exactly like "no activity attached".
    """
    if not ts:
        return None
    for fmt in ("%Y-%m-%d %H:%M:%S", "%Y-%m-%dT%H:%M:%S.%f", "%Y-%m-%dT%H:%M:%S"):
        try:
            return datetime.strptime(ts, fmt).replace(tzinfo=timezone.utc)
        except ValueError:
            continue
    return None


def sync_activities(api: Any, cur: psycopg.Cursor, start: date, end: date) -> int:
    found = safe("activities", lambda: api.get_activities_by_date(start.isoformat(), end.isoformat())) or []
    count = 0

    for a in found:
        garmin_id = a.get("activityId")
        if not garmin_id:
            continue

        started = _parse(a.get("startTimeGMT"))
        duration = a.get("duration") or 0
        if not started:
            continue

        sets_payload = safe("sets", lambda: api.get_activity_exercise_sets(garmin_id)) or {}
        active = [s for s in (sets_payload.get("exerciseSets") or []) if s.get("setType") == "ACTIVE"]
        total_reps = sum(s.get("repetitionCount") or 0 for s in active)
        # Garmin stores set weight in grams.
        total_weight = sum(
            (s.get("weight") or 0) / 1000.0 * (s.get("repetitionCount") or 0) for s in active
        )

        cur.execute(
            ACTIVITY_UPSERT,
            {
                "user_id": USER_ID,
                "garmin_id": garmin_id,
                "start": started,
                "end": started + timedelta(seconds=float(duration)),
                "type": (a.get("activityType") or {}).get("typeKey", "unknown")[:60],
                "title": (a.get("activityName") or "")[:200] or None,
                "duration": float(duration),
                "avg_hr": _num(a, "averageHR"),
                "max_hr": _num(a, "maxHR"),
                "calories": _num(a, "calories"),
                "distance": _num(a, "distance"),
                "aerobic": _num(a, "aerobicTrainingEffect"),
                "anaerobic": _num(a, "anaerobicTrainingEffect"),
                "watts": _num(a, "avgPower"),
                "set_count": len(active) or None,
                "reps": total_reps or None,
                "weight": round(total_weight, 2) or None,
                "raw_summary": Jsonb(a),
                "raw_sets": Jsonb(sets_payload) if sets_payload else None,
                "now": datetime.now(timezone.utc),
            },
        )
        detail_id = cur.fetchone()[0]
        count += 1

        if not active:
            # Nothing to break into sets - a run, a walk, a ride. The heart-rate
            # series is 170KB+ and only earns that by being windowed per set, so
            # it is not fetched for these at all.
            continue

        details = safe("details", lambda: api.get_activity_details(garmin_id))
        rows = setlib.extract_sets(sets_payload, details)
        now = datetime.now(timezone.utc)
        for row in rows:
            cur.execute(SET_UPSERT, {**row, "detail_id": detail_id, "now": now})

        with_hr = len([r for r in rows if r["hr_avg"] is not None])
        logger.info(
            "  activity %s: %d sets, heart rate on %d", garmin_id, len(rows), with_hr
        )

    return count


REQUIRED_TABLES = ("GarminDailyMetrics", "GarminActivityDetails", "GarminExerciseSets")


def check_schema(cur: psycopg.Cursor) -> list[str]:
    """Which of our tables are missing from this database.

    Worth a check of its own rather than letting the first INSERT fail: the
    traceback from psycopg names one table and says nothing about the actual
    cause, which is almost always that `dotnet ef database update` has not been
    run, or has been run against a different Postgres than the one this
    container reaches.
    """
    cur.execute(
        """
        SELECT table_name FROM information_schema.tables
        WHERE table_schema = 'public' AND table_name = ANY(%s)
        """,
        (list(REQUIRED_TABLES),),
    )
    present = {r[0] for r in cur.fetchall()}
    return [t for t in REQUIRED_TABLES if t not in present]


def describe_database(cur: psycopg.Cursor) -> str:
    """Enough to tell 'wrong database' apart from 'migration not applied'."""
    cur.execute("SELECT current_database(), inet_server_addr()::text, inet_server_port()")
    db, host, port = cur.fetchone()
    cur.execute(
        "SELECT count(*) FROM information_schema.tables "
        "WHERE table_schema = 'public' AND table_name = 'Workouts'"
    )
    has_app = cur.fetchone()[0] > 0
    detail = f"{db} on {host or 'socket'}:{port}"
    if has_app:
        cur.execute('SELECT count(*) FROM "Workouts"')
        return f"{detail} - the app schema IS here ({cur.fetchone()[0]} workouts)"
    return f"{detail} - the app schema is NOT here either (no Workouts table)"


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--days", type=int, default=int(os.getenv("GARMIN_SYNC_DAYS", "7")))
    args = parser.parse_args()

    if USER_ID <= 0:
        print("GARMIN_SYNC_USER_ID is not set. See .env.example.", file=sys.stderr)
        return 2

    try:
        api = connect()
    except GarminNotConfigured as e:
        # Setup was never finished. This must be loud: a silent exit 0 here
        # means every future run "succeeds" while syncing nothing.
        logger.error("%s", e)
        return 2
    except GarminUnavailable as e:
        # Garmin being unreachable IS a normal Tuesday for a reverse-engineered
        # client, and should not fail a scheduled run.
        logger.warning("%s", e)
        return 0

    end = date.today()
    start = end - timedelta(days=args.days)

    with psycopg.connect(dsn()) as conn:
        with conn.cursor() as cur:
            missing = check_schema(cur)
            if missing:
                logger.error(
                    "These tables do not exist: %s\n"
                    "Connected to: %s\n"
                    "The schema is owned by EF Core, not by this sidecar, so it has to be\n"
                    "created there:\n"
                    "    dotnet ef migrations add AddGarminExerciseSets   (if not added yet)\n"
                    "    dotnet ef database update\n"
                    "If the line above says the app schema is NOT here, then `dotnet ef` is\n"
                    "pointing at a different Postgres than this container reaches - compare\n"
                    "its connection string (dotnet user-secrets list) with GARMIN_SYNC_DSN.",
                    ", ".join(missing), describe_database(cur),
                )
                return 3

            day = start
            while day <= end:
                sync_day(api, cur, day)
                day += timedelta(days=1)

            n = sync_activities(api, cur, start, end)
            logger.info("%d activities upserted", n)

            cur.execute(LINK_SQL)
            logger.info("%d newly linked to a Health Connect activity", cur.rowcount)

            # Matching runs last and over the whole window, not just the rows
            # touched above: a workout logged or edited after the watch session
            # was already synced has to get picked up on a later run.
            cur.execute(MATCHABLE_SQL, {"user_id": USER_ID, "since": start})
            for (detail_id,) in cur.fetchall():
                result = match_sets(cur, detail_id)
                if result is None:
                    continue
                if result["coverage"] < 0.5:
                    logger.warning(
                        "  activity detail %s: only %.0f%% of sets could be paired "
                        "(%d logged and %d recorded left over) - check the right "
                        "activity is attached to the workout",
                        detail_id, result["coverage"] * 100,
                        result["logged_unmatched"], result["recorded_unmatched"],
                    )
                else:
                    note = ""
                    if result["adjusted"]:
                        note = (f", {result['adjusted']} in an exercise that absorbed "
                                f"a set-count discrepancy")
                    logger.info(
                        "  activity detail %s: %d sets paired, coverage %.0f%%%s",
                        detail_id, result["matched"], result["coverage"] * 100, note,
                    )

        conn.commit()

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
