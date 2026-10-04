"""Align the sets you logged with the sets the watch recorded.

**Reps come from the phone, never from the watch.** The watch's rep counter is
not merely noisy, it fails outright on some movements - 10 kettlebell adductors
are reported as 0 - so it cannot be used as evidence of anything, including for
matching. An earlier version of this scored the alignment on rep similarity;
that was building on sand.

What is left is structure, and it is enough:

* Both sides are the same session, so both are a contiguous ordered sequence.
* `ExerciseSet` has no timestamp, only `Exercise.Order` and `SetNumber`, so
  position is the only thing the two sides share.
* The log knows exactly how many sets each exercise had. The watch records sets
  in time order. So the recorded sets partition into one contiguous run per
  logged exercise, with run lengths equal to the logged set counts.

When the totals agree, that partition is exact and there is nothing to guess.
When they differ the difference has to land somewhere, and no signal available
here says where - so the placement is a documented convention rather than a
pretend inference, and the affected exercise is reported.

The watch contributes heart rate, rest and timing. That is all.
"""

from __future__ import annotations

# Where an unavoidable discrepancy is placed, and why:
#
#   more logged than recorded  -> shortfall taken from the LAST exercises.
#       A watch stopped or paused mid-session loses the end; sets added to the
#       log afterwards are also appended at the end.
#   more recorded than logged  -> extras left unmatched at the START.
#       Warmup sets the watch detected and you did not log are at the front.
#
# Both are conventions. Neither is evidence.

CONFIDENCE_EXACT = 1.0        # totals agreed; the partition is forced
CONFIDENCE_ADJUSTED = 0.5     # this exercise absorbed part of a discrepancy


def plan(group_sizes: list[int], recorded: int) -> list[int]:
    """How many recorded sets each logged exercise gets.

    Returns one length per exercise, summing to min(total_logged, recorded).
    Any shortfall is taken from the last exercises first.
    """
    if not group_sizes:
        return []

    total = sum(group_sizes)
    if recorded >= total:
        # Every logged set gets a partner; the surplus stays unmatched at the
        # front and is not distributed into the runs.
        return list(group_sizes)

    plan_sizes = list(group_sizes)
    shortfall = total - recorded
    for i in range(len(plan_sizes) - 1, -1, -1):
        if shortfall <= 0:
            break
        take = min(shortfall, plan_sizes[i])
        plan_sizes[i] -= take
        shortfall -= take
    return plan_sizes


def align_by_exercise(
    group_sizes: list[int],
    recorded: int,
) -> list[tuple[int | None, int | None, float | None]]:
    """Pair logged set positions with recorded set positions.

    `group_sizes` is the number of logged sets per exercise, in `Exercise.Order`.
    `recorded` is how many sets the watch captured.

    Returns (logged_index, recorded_index, confidence) over the flattened logged
    sequence. A None on either side is an unmatched set, which is a normal
    outcome and must stay readable as "unmatched" rather than "no heart rate".
    """
    total_logged = sum(group_sizes)

    if not group_sizes:
        return [(None, j, None) for j in range(recorded)]
    if recorded == 0:
        return [(i, None, None) for i in range(total_logged)]

    plan_sizes = plan(group_sizes, recorded)
    surplus = recorded - total_logged

    out: list[tuple[int | None, int | None, float | None]] = []

    # Unlogged sets at the front - warmups the watch saw and you did not record.
    cursor = 0
    if surplus > 0:
        out.extend((None, j, None) for j in range(surplus))
        cursor = surplus

    logged_index = 0
    for size, assigned in zip(group_sizes, plan_sizes):
        confidence = CONFIDENCE_EXACT if assigned == size else CONFIDENCE_ADJUSTED
        for position in range(size):
            if position < assigned:
                out.append((logged_index, cursor, confidence))
                cursor += 1
            else:
                # This exercise absorbed part of the shortfall. The unmatched
                # sets are its last ones, by the convention above.
                out.append((logged_index, None, None))
            logged_index += 1

    return out


def summarise(
    alignment: list[tuple[int | None, int | None, float | None]],
) -> dict[str, float | int]:
    """A one-line verdict on the pairing, for the sync log.

    Low `coverage` means something systematic: the wrong activity attached to
    the workout, or a session only half logged. Worth seeing in the output
    rather than discovering later in a chart that looks subtly off.
    """
    matched = [a for a in alignment if a[0] is not None and a[1] is not None]
    logged_total = len([a for a in alignment if a[0] is not None])
    recorded_total = len([a for a in alignment if a[1] is not None])
    adjusted = len([a for a in matched if a[2] == CONFIDENCE_ADJUSTED])

    return {
        "matched": len(matched),
        "logged_unmatched": logged_total - len(matched),
        "recorded_unmatched": recorded_total - len(matched),
        "coverage": round(len(matched) / max(logged_total, recorded_total, 1), 3),
        "adjusted": adjusted,
    }
