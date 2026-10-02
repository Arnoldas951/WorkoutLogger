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
    """Garmin keys this by device id, so the phrase is two levels down under a
    key whose name is a serial number. Take the first device that has one."""
    devices = (payload or {}).get("latestTrainingStatusData") or {}
    if not isinstance(devices, dict):
        return None
    for entry in devices.values():
        if not isinstance(entry, dict):
            continue
        phrase = entry.get("trainingStatusFeedbackPhrase") or entry.get("trainingStatus")
        if phrase:
            return str(phrase)[:40]
    return None

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
            "vo2max": _num(max_metrics, "generic", "vo2MaxPreciseValue"),
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
    "UpdatedAt"               = EXCLUDED."UpdatedAt";
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


def _parse(ts: str | None) -> datetime | None:
    if not ts:
        return None
    for fmt in ("%Y-%m-%d %H:%M:%S", "%Y-%m-%dT%H:%M:%S.%f", "%Y-%m-%dT%H:%M:%S"):
        try:
            return datetime.strptime(ts, fmt)
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
        count += 1

    return count


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
            day = start
            while day <= end:
                sync_day(api, cur, day)
                day += timedelta(days=1)

            n = sync_activities(api, cur, start, end)
            logger.info("%d activities upserted", n)

            cur.execute(LINK_SQL)
            logger.info("%d newly linked to a Health Connect activity", cur.rowcount)

        conn.commit()

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
