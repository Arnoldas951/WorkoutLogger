"""Show what actually landed in the database. Read-only.

    docker compose run --rm garmin-sync python verify.py
    docker compose run --rm garmin-sync python verify.py --workout 42

Exists because "it ran without errors" has twice meant "it did nothing", and the
only way to tell those apart is to look at the rows.
"""

from __future__ import annotations

import argparse
import sys

import psycopg

from sync import dsn


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--workout", type=int, help="show this workout instead of the latest")
    args = parser.parse_args()

    with psycopg.connect(dsn()) as conn, conn.cursor() as cur:
        cur.execute("SELECT current_database(), inet_server_addr()::text")
        db, host = cur.fetchone()
        print(f"database: {db} at {host or 'socket'}\n")

        print("row counts")
        for table in ("Workouts", "Activities", "GarminDailyMetrics",
                      "GarminActivityDetails", "GarminExerciseSets"):
            try:
                cur.execute(f'SELECT count(*) FROM "{table}"')
                print(f"  {table:<24}{cur.fetchone()[0]:>6}")
            except psycopg.Error:
                conn.rollback()
                print(f"  {table:<24}{'MISSING':>6}")

        cur.execute('SELECT count(*) FROM "GarminExerciseSets" WHERE "ExerciseSetId" IS NOT NULL')
        paired = cur.fetchone()[0]
        cur.execute('SELECT count(*) FROM "GarminExerciseSets"')
        total = cur.fetchone()[0]
        print(f"\n  sets paired to a logged set: {paired}/{total}")
        if total and not paired:
            print("  -> nothing paired. Either no workout is attached to the watch activity,")
            print("     or the attach happened after the sync ran. Re-run sync.py.")

        print("\nmost recent daily metrics")
        cur.execute('''SELECT "Date","RestingHeartRate","HrvLastNightAvg","HrvStatus",
                              "SleepScore","BodyBatteryLow","BodyBatteryHigh","AverageStress"
                       FROM "GarminDailyMetrics" ORDER BY "Date" DESC LIMIT 5''')
        rows = cur.fetchall()
        if not rows:
            print("  none")
        else:
            print(f"  {'date':<12}{'restHR':>7}{'HRV':>5} {'status':<10}{'sleep':>6}{'battery':>9}{'stress':>7}")
            for d, rhr, hrv, st, sleep, bl, bh, stress in rows:
                bb = f"{bl}-{bh}" if bl is not None and bh is not None else "-"
                print(f"  {str(d):<12}{rhr if rhr is not None else '-':>7}{hrv if hrv is not None else '-':>5} "
                      f"{str(st or '-'):<10}{sleep if sleep is not None else '-':>6}{bb:>9}"
                      f"{stress if stress is not None else '-':>7}")

        # the payoff: logged sets enriched with the watch's heart rate and rest
        if args.workout:
            cur.execute('SELECT "Id","Name","Date" FROM "Workouts" WHERE "Id" = %s', (args.workout,))
        else:
            cur.execute('''SELECT w."Id", w."Name", w."Date"
                           FROM "Workouts" w
                           JOIN "Activities" a ON a."WorkoutId" = w."Id" AND a."DeletedAt" IS NULL
                           JOIN "GarminActivityDetails" g ON g."ActivityId" = a."Id"
                           JOIN "GarminExerciseSets" s ON s."GarminActivityDetailId" = g."Id"
                                                      AND s."ExerciseSetId" IS NOT NULL
                           WHERE w."DeletedAt" IS NULL
                           ORDER BY w."Date" DESC LIMIT 1''')
        workout = cur.fetchone()
        if not workout:
            print("\nNo workout yet has a watch activity attached AND matched sets.")
            print("Attach the activity to the workout in the app, then re-run sync.py.")
            return 0

        wid, name, date = workout
        print(f"\n{name} ({date:%Y-%m-%d})  workout {wid}")
        cur.execute('''SELECT e."Name", es."SetNumber", es."Repetitions", es."Weight",
                              g."AverageHeartRate", g."MaxHeartRate", g."RestSecondsAfter",
                              g."GarminReps", g."MatchConfidence"
                       FROM "ExerciseSets" es
                       JOIN "Exercises" e ON e."Id" = es."ExerciseId"
                       LEFT JOIN "GarminExerciseSets" g ON g."ExerciseSetId" = es."Id"
                       WHERE e."WorkoutId" = %s
                       ORDER BY e."Order", es."SetNumber"''', (wid,))
        # Garmin's rep count is deliberately NOT shown. It fails outright on
        # some movements (10 kettlebell adductors read as 0), so displaying it
        # beside the logged reps invites treating a bad number as a correction.
        # Reps come from the phone. The watch contributes heart rate and rest.
        print(f"  {'exercise':<20}{'set':>4}{'reps':>5}{'kg':>7}{'HRavg':>7}{'HRmax':>7}{'rest':>7}")
        for ex, sn, reps, w, avg, mx, rest, _greps, conf in cur.fetchall():
            note = ""
            if avg is None and rest is None:
                note = "   (no watch data for this set)"
            elif conf is not None and conf < 1.0:
                note = "   (set count differed; pairing is approximate)"
            print(f"  {ex[:20]:<20}{sn:>4}{reps:>5}{w:>7.0f}"
                  f"{avg if avg is not None else '-':>7}{mx if mx is not None else '-':>7}"
                  f"{(str(rest)+'s') if rest is not None else '-':>7}{note}")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
