"""Turn a Garmin strength activity into per-set rows with heart rate and rest.

The watch records sets as an alternating ACTIVE/REST sequence and the heart rate
separately, as one long series for the whole session. Joining them is this
module's whole job: window the series over each ACTIVE set's own start..end and
keep three numbers, so the ~174KB series never has to be stored.
"""

from __future__ import annotations

from datetime import datetime, timezone
from typing import Any

# Garmin reports set weight in grams.
GRAMS_PER_KG = 1000.0


def _epoch(ts: str | None) -> float | None:
    """Set timestamps come back as GMT with no zone marker, e.g.
    '2026-10-04T08:55:47.0'. Treating them as local would shift every window by
    the UTC offset and quietly produce heart rates from the wrong minutes."""
    if not ts:
        return None
    try:
        return (
            datetime.strptime(str(ts)[:19], "%Y-%m-%dT%H:%M:%S")
            .replace(tzinfo=timezone.utc)
            .timestamp()
        )
    except ValueError:
        return None


def heart_rate_series(details: dict[str, Any] | None) -> list[tuple[float, int]]:
    """[(epoch_seconds, bpm)], sorted. Empty when the activity has no HR."""
    if not isinstance(details, dict):
        return []

    index = {
        d["key"]: d["metricsIndex"]
        for d in (details.get("metricDescriptors") or [])
        if isinstance(d, dict) and d.get("key") is not None and d.get("metricsIndex") is not None
    }
    hr_i, ts_i = index.get("directHeartRate"), index.get("directTimestamp")
    if hr_i is None or ts_i is None:
        return []

    out: list[tuple[float, int]] = []
    for sample in details.get("activityDetailMetrics") or []:
        values = sample.get("metrics") if isinstance(sample, dict) else None
        if not isinstance(values, list) or len(values) <= max(hr_i, ts_i):
            continue
        hr, ts = values[hr_i], values[ts_i]
        if hr is None or ts is None:
            continue
        try:
            # directTimestamp is epoch milliseconds.
            out.append((float(ts) / 1000.0, int(hr)))
        except (TypeError, ValueError):
            continue

    out.sort()
    return out


def _window(series: list[tuple[float, int]], start: float, end: float):
    """HR min/max/avg over [start, end], or Nones when the window is empty.

    A short set can fall between two samples, so an empty window is an expected
    outcome rather than a problem to paper over with the session average.
    """
    inside = [hr for t, hr in series if start <= t <= end]
    if not inside:
        return None, None, None
    return min(inside), max(inside), round(sum(inside) / len(inside))


def extract_sets(
    sets_payload: dict[str, Any] | None,
    details: dict[str, Any] | None,
) -> list[dict[str, Any]]:
    """Per-set rows, ACTIVE only, in time order with SetIndex 0..n-1.

    Rest is read from the REST entry that follows a working set in the raw
    sequence, which is how the watch represents it - not computed from the gap
    between consecutive ACTIVE sets, since that would also swallow the time
    spent walking to the next machine.
    """
    raw = (sets_payload or {}).get("exerciseSets") or []
    if not isinstance(raw, list):
        return []

    # Order by the watch's own sequence number where present; startTime is the
    # fallback, and some sets carry neither.
    ordered = sorted(
        (s for s in raw if isinstance(s, dict)),
        key=lambda s: (
            s.get("messageIndex") if isinstance(s.get("messageIndex"), int) else 0,
            _epoch(s.get("startTime")) or 0.0,
        ),
    )

    series = heart_rate_series(details)
    rows: list[dict[str, Any]] = []
    index = 0

    for position, entry in enumerate(ordered):
        if entry.get("setType") != "ACTIVE":
            continue

        start = _epoch(entry.get("startTime"))
        duration = entry.get("duration")
        duration = float(duration) if isinstance(duration, (int, float)) else None

        lo = hi = avg = None
        if start is not None and duration:
            lo, hi, avg = _window(series, start, start + duration)

        # The immediately following entry, when it is a REST, is this set's rest.
        rest = None
        if position + 1 < len(ordered):
            nxt = ordered[position + 1]
            if nxt.get("setType") != "ACTIVE":
                d = nxt.get("duration")
                if isinstance(d, (int, float)):
                    rest = round(d)

        exercise = (entry.get("exercises") or [{}])[0]
        if not isinstance(exercise, dict):
            exercise = {}

        reps = entry.get("repetitionCount")
        weight = entry.get("weight")

        rows.append(
            {
                "set_index": index,
                # Aware, deliberately. Stripping tzinfo here would let Postgres
                # reinterpret it in the server's TimeZone and shift every set.
                "start": datetime.fromtimestamp(start, timezone.utc)
                if start is not None
                else None,
                "duration": duration,
                "reps": reps if isinstance(reps, int) else None,
                # `name` is null in practice; `category` is the usable field.
                "category": (exercise.get("category") or None),
                "probability": exercise.get("probability"),
                "hr_min": lo,
                "hr_max": hi,
                "hr_avg": avg,
                "rest_after": rest,
                # Null on every set of the probed session - the watch does not
                # know the load. Kept because a user who enters weights in Garmin
                # Connect would populate it.
                "weight_kg": (weight / GRAMS_PER_KG) if isinstance(weight, (int, float)) and weight else None,
            }
        )
        index += 1

    return rows
