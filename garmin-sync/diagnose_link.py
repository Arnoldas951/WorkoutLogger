"""Why aren't Garmin activities linking to Health Connect activities?

The chain is GarminActivityDetail -> Activity -> Workout, and the first hop is a
+/-5 minute time-window overlap. If the two sides store times in different zones
the overlap can never hit, and the symptom is indistinguishable from "you never
attached anything". Read-only.

    docker compose run --rm garmin-sync python diagnose_link.py
"""

from __future__ import annotations

import psycopg

from sync import dsn


def main() -> int:
    with psycopg.connect(dsn()) as conn, conn.cursor() as cur:
        cur.execute('''SELECT "Id","StartTime","EndTime","ActivityType","WorkoutId","Source"
                       FROM "Activities" WHERE "DeletedAt" IS NULL
                       ORDER BY "StartTime" DESC LIMIT 12''')
        activities = cur.fetchall()
        cur.execute('''SELECT "Id","StartTime","EndTime","GarminActivityId","ActivityId","ActivityType"
                       FROM "GarminActivityDetails" ORDER BY "StartTime" DESC''')
        garmin = cur.fetchall()

        print(f'Activities (Health Connect side), newest {len(activities)}:')
        print(f'  {"id":>4} {"StartTime":<20}{"type":<22}{"workout":>8}  source')
        for aid, st, en, typ, wid, src in activities:
            print(f'  {aid:>4} {str(st):<20}{str(typ)[:20]:<22}{(wid if wid else "-"):>8}  {src}')

        print(f'\nGarminActivityDetails, all {len(garmin)}:')
        print(f'  {"id":>4} {"StartTime":<20}{"garminId":>13}{"linked":>8}')
        for gid, st, en, gaid, aid, typ in garmin:
            print(f'  {gid:>4} {str(st):<20}{gaid:>13}{(aid if aid else "-"):>8}')

        print("\nnearest Activity to each Garmin activity, by start time:")
        print(f'  {"garmin":>7}  {"nearest activity":>16}  {"offset":>12}  verdict')
        skews = []
        for gid, gst, gen, gaid, aid, typ in garmin:
            best = None
            for a in activities:
                delta = (a[1] - gst).total_seconds()
                if best is None or abs(delta) < abs(best[1]):
                    best = (a[0], delta)
            if not best:
                continue
            mins = best[1] / 60.0
            skews.append(mins)
            if abs(mins) <= 5:
                verdict = "should link"
            elif abs(mins) < 24 * 60:
                verdict = f"off by {mins/60:+.2f}h  <-- looks like a timezone"
            else:
                verdict = "no counterpart"
            print(f'  {gid:>7}  {best[0]:>16}  {mins:>+10.1f}m  {verdict}')

        if skews:
            consistent = [m for m in skews if abs(m) < 24 * 60]
            if consistent and max(consistent) - min(consistent) < 5:
                hours = sum(consistent) / len(consistent) / 60.0
                print(f'\n  >> Every Garmin activity sits a consistent {hours:+.2f}h from its')
                print(f'     Health Connect counterpart. That is a fixed offset, not drift:')
                print(f'     the two sides are storing different time zones.')
                print(f'     Compare against the activity\'s true UTC start. If the')
                print(f'     Activities row is correct, the Garmin row is the wrong one:')
                print(f'     a NAIVE datetime inserted into a timestamptz column gets')
                print(f'     interpreted in the server TimeZone shown below, not as UTC.')

        cur.execute('''SELECT count(*) FROM "Activities"
                       WHERE "WorkoutId" IS NOT NULL AND "DeletedAt" IS NULL''')
        print(f'\n  Activities attached to a workout: {cur.fetchone()[0]}')
        cur.execute('SELECT count(*) FROM "GarminActivityDetails" WHERE "ActivityId" IS NOT NULL')
        print(f'  GarminActivityDetails linked to an Activity: {cur.fetchone()[0]}')
        cur.execute("SELECT current_setting('TimeZone')")
        print(f'  Postgres TimeZone: {cur.fetchone()[0]}')
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
