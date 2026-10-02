"""Dump everything Garmin will give you for one activity and one day, then say
plainly how that compares to what Health Connect already provides.

This exists to answer one question before any schema is committed to: is the
extra detail worth a second data source? Run it, read the report, keep the JSON.

    docker compose run --rm garmin-sync python probe.py
    docker compose run --rm garmin-sync python probe.py --activity-id 123456789
    docker compose run --rm garmin-sync python probe.py --date 2026-09-19

Output lands in ./probe-output/ (gitignored).
"""

from __future__ import annotations

import argparse
import json
import sys
from datetime import date, datetime, timedelta
from pathlib import Path
from typing import Any, Callable

from garmin_client import GarminUnavailable, connect

OUT = Path(__file__).parent / "probe-output"

# What Activity.cs stores today, straight off the entity. This is the baseline
# the Garmin payload gets measured against - everything Health Connect gives
# you, and nothing more.
HEALTH_CONNECT_BASELINE = [
    ("ActivityType", "exercise type, free text"),
    ("Title", "session title"),
    ("StartTime / EndTime", "window, duration derived"),
    ("AverageHeartRate", "single aggregate"),
    ("MaxHeartRate", "single aggregate"),
    ("MinHeartRate", "single aggregate"),
    ("ActiveCalories", "single aggregate"),
    ("TotalCalories", "single aggregate"),
    ("DistanceMeters", "single aggregate"),
    ("Steps", "single aggregate"),
]


class Probe:
    """Runs a labelled call, records what came back, never raises."""

    def __init__(self) -> None:
        self.results: list[dict[str, Any]] = []

    def run(self, label: str, fn: Callable[[], Any], note: str = "") -> Any:
        try:
            value = fn()
        except Exception as e:  # noqa: BLE001 - a probe reports failures, it does not propagate them
            self.results.append(
                {"label": label, "status": "ERROR", "detail": f"{type(e).__name__}: {e}", "note": note}
            )
            print(f"  {label:<34} ERROR  {type(e).__name__}")
            return None

        status, detail = _describe(value)
        self.results.append({"label": label, "status": status, "detail": detail, "note": note})
        print(f"  {label:<34} {status:<6} {detail}")

        if value is not None:
            path = OUT / f"{label}.json"
            try:
                path.write_text(json.dumps(value, indent=2, default=str), encoding="utf-8")
            except TypeError:
                path.write_text(repr(value), encoding="utf-8")
        return value


def _describe(value: Any) -> tuple[str, str]:
    """One line saying what shape came back, without dumping it."""
    if value is None:
        return "EMPTY", "null"
    if isinstance(value, dict):
        if not value:
            return "EMPTY", "{}"
        keys = list(value.keys())
        shown = ", ".join(keys[:6])
        more = f" (+{len(keys) - 6} more)" if len(keys) > 6 else ""
        return "OK", f"{len(keys)} keys: {shown}{more}"
    if isinstance(value, list):
        if not value:
            return "EMPTY", "[]"
        first = value[0]
        inner = f", first has {len(first)} keys" if isinstance(first, dict) else ""
        return "OK", f"{len(value)} items{inner}"
    if isinstance(value, (bytes, bytearray)):
        return "OK", f"{len(value):,} bytes"
    return "OK", str(value)[:80]


def pick_activity(api: Any, args: argparse.Namespace) -> dict[str, Any] | None:
    """Prefer a strength session: that is the one this app actually cares about."""
    if args.activity_id:
        return {"activityId": args.activity_id}

    if args.date:
        found = api.get_activities_by_date(args.date, args.date)
        if not found:
            print(f"No activities on {args.date}.")
            return None
        return found[0]

    recent = api.get_activities(0, 30) or []
    if isinstance(recent, dict):
        recent = recent.get("activityList", [])
    if not recent:
        print("No activities found on this account at all.")
        return None

    for a in recent:
        type_key = (a.get("activityType") or {}).get("typeKey", "")
        if "strength" in type_key or "gym" in type_key or "fitness_equipment" in type_key:
            print(f"Picked the most recent strength session (pass --activity-id to override).")
            return a

    print("No strength session in the last 30 activities; using the most recent one.")
    return recent[0]


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--activity-id", type=int, help="probe this specific activity")
    parser.add_argument("--date", help="probe the first activity on this date (YYYY-MM-DD)")
    parser.add_argument("--no-fit", action="store_true", help="skip the FIT file download")
    args = parser.parse_args()

    OUT.mkdir(exist_ok=True)

    try:
        api = connect()
    except GarminUnavailable as e:
        print(f"\n{e}\n", file=sys.stderr)
        return 1

    activity = pick_activity(api, args)
    if not activity:
        return 1

    activity_id = str(activity["activityId"])
    start = activity.get("startTimeLocal") or activity.get("startTimeGMT") or ""
    on_date = start[:10] if start else date.today().isoformat()
    type_key = (activity.get("activityType") or {}).get("typeKey", "?")

    print(f"\nActivity {activity_id}  |  {type_key}  |  {start}")
    print(f"Day metrics for {on_date}\n")

    probe = Probe()

    print("ACTIVITY")
    summary = probe.run("activity_summary", lambda: api.get_activity(activity_id))
    details = probe.run(
        "activity_details",
        lambda: api.get_activity_details(activity_id),
        "per-sample time series",
    )
    probe.run("activity_splits", lambda: api.get_activity_splits(activity_id))
    probe.run("activity_typed_splits", lambda: api.get_activity_typed_splits(activity_id))
    probe.run("activity_split_summaries", lambda: api.get_activity_split_summaries(activity_id))
    probe.run("activity_hr_zones", lambda: api.get_activity_hr_in_timezones(activity_id))
    probe.run("activity_weather", lambda: api.get_activity_weather(activity_id))
    sets = probe.run(
        "activity_exercise_sets",
        lambda: api.get_activity_exercise_sets(activity_id),
        "per-set reps and weight",
    )

    if not args.no_fit:
        def _fit() -> bytes:
            return api.download_activity(activity_id, dl_fmt=api.ActivityDownloadFormat.ORIGINAL)

        raw = probe.run("activity_fit_zip", _fit, "lossless original")
        if isinstance(raw, (bytes, bytearray)):
            (OUT / "activity_fit.zip").write_bytes(raw)

    print("\nDAY METRICS")
    probe.run("day_user_summary", lambda: api.get_user_summary(on_date))
    probe.run("day_hrv", lambda: api.get_hrv_data(on_date))
    probe.run("day_sleep", lambda: api.get_sleep_data(on_date))
    probe.run("day_body_battery", lambda: api.get_body_battery(on_date))
    probe.run("day_training_readiness", lambda: api.get_training_readiness(on_date))
    probe.run("day_training_status", lambda: api.get_training_status(on_date))
    probe.run("day_max_metrics", lambda: api.get_max_metrics(on_date), "VO2max")
    probe.run("day_resting_hr", lambda: api.get_rhr_day(on_date))
    probe.run("day_stress", lambda: api.get_stress_data(on_date))
    probe.run("day_respiration", lambda: api.get_respiration_data(on_date))
    probe.run("day_spo2", lambda: api.get_spo2_data(on_date))
    probe.run("day_intensity_minutes", lambda: api.get_intensity_minutes_data(on_date))

    report(summary, details, sets, probe)

    (OUT / "_probe_results.json").write_text(
        json.dumps(probe.results, indent=2, default=str), encoding="utf-8"
    )
    print(f"\nFull JSON written to {OUT}/")
    return 0


def report(
    summary: dict[str, Any] | None,
    details: dict[str, Any] | None,
    sets: dict[str, Any] | None,
    probe: Probe,
) -> None:
    """The part worth reading: what this buys over Health Connect."""
    rule = "=" * 78
    print(f"\n{rule}\nWHAT THIS BUYS OVER HEALTH CONNECT\n{rule}")

    print("\nHealth Connect gives Activity.cs these, and only these:\n")
    for name, shape in HEALTH_CONNECT_BASELINE:
        print(f"  {name:<24} {shape}")

    # --- time series -------------------------------------------------------
    print("\nGarmin adds, for the same session:\n")

    series_keys: list[str] = []
    sample_count = 0
    if isinstance(details, dict):
        descriptors = details.get("metricDescriptors") or []
        series_keys = [
            d.get("key") for d in descriptors if isinstance(d, dict) and d.get("key")
        ]
        sample_count = len(details.get("activityDetailMetrics") or [])

    if series_keys:
        print(f"  TIME SERIES - {len(series_keys)} channels x {sample_count:,} samples")
        print("  (Health Connect gives you one average for the whole session.)")
        for key in sorted(series_keys):
            print(f"      {key}")
    else:
        print("  TIME SERIES - none returned for this activity.")

    # --- exercise sets -----------------------------------------------------
    exercise_sets = []
    if isinstance(sets, dict):
        exercise_sets = [
            s for s in (sets.get("exerciseSets") or []) if s.get("setType") == "ACTIVE"
        ]

    print()
    if exercise_sets:
        print(f"  EXERCISE SETS - {len(exercise_sets)} working sets, named and counted")
        print("  (Health Connect has no concept of a set at all.)")
        for s in exercise_sets[:12]:
            exercises = s.get("exercises") or [{}]
            name = exercises[0].get("name") or exercises[0].get("category") or "?"
            reps = s.get("repetitionCount")
            weight = s.get("weight")
            kg = f"{weight / 1000:g} kg" if isinstance(weight, (int, float)) and weight else "bodyweight"
            dur = s.get("duration")
            secs = f"{dur:.0f}s" if isinstance(dur, (int, float)) else "?"
            print(f"      {str(name):<28} {str(reps):>3} reps  {kg:>12}  {secs:>6}")
        if len(exercise_sets) > 12:
            print(f"      ... and {len(exercise_sets) - 12} more")
        print("\n  ^ This is the one that matters. If these line up with what you log")
        print("    by hand, Garmin can corroborate or replace manual set entry.")
    else:
        print("  EXERCISE SETS - none on this activity.")
        print("  Only strength/gym activities carry them, and only when the watch")
        print("  was in a strength activity profile. Re-run with --activity-id for a")
        print("  gym session before concluding they are unavailable.")

    # --- summary fields not in the baseline --------------------------------
    if isinstance(summary, dict):
        interesting = {
            k: v
            for k, v in summary.items()
            if v not in (None, 0, "", [], {})
            and any(
                token in k.lower()
                for token in (
                    "trainingeffect", "vo2", "power", "cadence", "stride", "lactate",
                    "intensity", "load", "recovery", "respiration", "temperature",
                    "elevation", "speed", "zone", "hrtimein", "moderate", "vigorous",
                )
            )
        }
        if interesting:
            print(f"\n  SESSION FIELDS Health Connect has no equivalent for ({len(interesting)}):\n")
            for k in sorted(interesting)[:30]:
                print(f"      {k:<38} {str(interesting[k])[:32]}")

    # --- day context -------------------------------------------------------
    day_ok = [r for r in probe.results if r["label"].startswith("day_") and r["status"] == "OK"]
    print(f"\n  DAILY CONTEXT - {len(day_ok)} of 12 endpoints returned data:\n")
    for r in day_ok:
        print(f"      {r['label'][4:]:<22} {r['detail'][:46]}")
    print("\n  None of this exists in Health Connect in any form. It is also the part")
    print("  that is per-day rather than per-session, so it can land in its own table")
    print("  without touching Activity at all.")

    # --- verdict scaffolding ----------------------------------------------
    failed = [r for r in probe.results if r["status"] == "ERROR"]
    empty = [r for r in probe.results if r["status"] == "EMPTY"]
    print(f"\n{rule}")
    print(f"{len(probe.results)} endpoints probed: "
          f"{len(probe.results) - len(failed) - len(empty)} returned data, "
          f"{len(empty)} empty, {len(failed)} errored.")
    if failed:
        print("\nErrored:")
        for r in failed:
            print(f"  {r['label']:<30} {r['detail'][:60]}")
        print("\nAn error here is information, not a bug to fix: it usually means the")
        print("account or the device does not produce that metric.")
    print(rule)


if __name__ == "__main__":
    raise SystemExit(main())
