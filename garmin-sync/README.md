# garmin-sync

Pulls Garmin Connect data into the WorkoutLogger database, as a sidecar
container beside `api` and `db`.

## Step by step

Run everything from the **repo root** (where `docker-compose.yml` is), not from
this folder. Your API does **not** need to be running at any point — the sidecar
talks to Postgres directly, and the probe does not touch Postgres at all.

### 1. Check `.env` exists in the repo root and is filled in

    GARMIN_EMAIL=<your Garmin Connect login>
    GARMIN_PASSWORD=<your Garmin password>      <- required for the FIRST login
    GARMIN_SYNC_USER_ID=<Id from your Users table>
    JWT_KEY=REPLACE_ME

Edit `.env`, **never** `.env.example` — only `.env` is gitignored.

### 2. Build the image

    docker compose build garmin-sync

First build takes a minute or two.

### 3. Run the probe — note the explicit `python probe.py`

    docker compose run --rm garmin-sync python probe.py

**The arguments matter.** `docker compose run --rm garmin-sync` with nothing
after it runs the image's default command, which is `sync.py`, not the probe.

Run it from a terminal you can type into: if your Garmin account has 2FA it will
ask for a code.

### 4. Confirm it actually worked

Success looks like a long report ending in a line like
`21 endpoints probed: 17 returned data, 3 empty, 1 errored`, and:

    ls garmin-sync/probe-output

should list ~20 `.json` files. **An empty `probe-output/` means the probe did not
run**, whatever the exit code said.

### 5. Blank the password

Once step 3 succeeds a token is stored in the `garmin-tokens` volume and rotates
on its own. Set `GARMIN_PASSWORD=` empty in `.env` so your Garmin password is not
sitting in plaintext. Put it back only if the token is ever revoked.

### 6. Read the report, then decide

Everything past here is only worth doing if the report says the data is good
enough — in particular whether `activity_exercise_sets` came back populated.

### 7. Only then: create the tables and sync

    dotnet ef migrations add AddGarminExerciseSets
    dotnet ef database update
    docker compose run --rm garmin-sync python sync.py

`AddGarminTables` already exists. `AddGarminExerciseSets` is a **second**
migration rather than a regeneration of the first, so it applies cleanly whether
or not you have already run `database update`.

`dotnet ef` must point at the same database the app uses; see "Which database"
below.

## Why this and not the alternatives

Garmin has no personal API. Everything in this space is reverse-engineered, so
the only real question is which reverse-engineering breaks least often.

- **garth** — deprecated 27 March 2026. Garmin changed the auth flow and it,
  plus everything built on it, stopped working. Do not start here.
- **garmin-givemydata** — works, and pulls more (50 tables, FIT files), but
  drives a real Chrome through SeleniumBase to get past Cloudflare. That means
  a browser in the image, and its `cf_clearance` cookie is bound to your IP, so
  a new DHCP lease logs you out. AGPL-3.0.
- **python-garminconnect** — what this uses. Reimplements the mobile SSO flow
  the official Android app uses; plain HTTP, no browser, token-based, not
  IP-bound. MIT.

It can still break. The library survived the March change, which is the
evidence available, not a guarantee.

## Two things that bite on the first run

**`JWT_KEY is missing a value`.** Compose interpolates the entire file before
running anything, so the api service's `${JWT_KEY:?...}` is evaluated even by
`docker compose run garmin-sync`. Set `JWT_KEY` in `.env`. Keep it deliberately
invalid until you actually want the dockerised api — see the note in
`.env.example` for why a fresh key there is a data-loss path.

**Which database.** `docker compose run garmin-sync` starts the compose `db`
container and writes there. If you normally run the API with `dotnet run` against
a Postgres that is not that container, the Garmin rows land somewhere your app
never reads. Check which one is listening:

    docker compose ps db
    docker compose exec db psql -U postgres -d workoutlogger -c 'select count(*) from "Workouts";'

A count matching what you see in the app means it is the right database. If not,
set `GARMIN_SYNC_DSN` in `.env` to point at the real one.

## probe.py — read this before wiring anything to a screen

Dumps everything Garmin has for one session and one day into
`garmin-sync/probe-output/`, then prints a comparison against the fields
`Activity.cs` currently stores.

    docker compose run --rm garmin-sync python probe.py
    docker compose run --rm garmin-sync python probe.py --activity-id 123456789
    docker compose run --rm garmin-sync python probe.py --date 2026-09-19

It picks your most recent strength session by default, because per-set reps and
weight are the only part of this that could change how the app works. Everything
else is context.

The report tells you three things:

- how many time-series channels and samples a session carries, against the
  single average Health Connect gives you
- whether `activity_exercise_sets` came back populated, and what it says
- which of the twelve daily endpoints your account and device actually produce

## sync.py

    docker compose run --rm garmin-sync python sync.py             # last 7 days
    docker compose run --rm garmin-sync python sync.py --days 365  # backfill

Writes `GarminDailyMetrics` (one row per day) and `GarminActivityDetails` (one
row per Garmin activity), each keeping the full payload as `jsonb` next to the
extracted columns — the same trade `Workout` already makes.

It does **not** write to `Activities`. Health Connect stays the activity spine;
these tables sit beside it and are matched by a ±5 minute time overlap, which is
why `GarminActivityDetails.ActivityId` is nullable. Unmatched is normal.

Failing to reach Garmin exits 0 with a warning, so a scheduled run does not page
you over an upstream change you cannot do anything about at 3am.

## Per-set heart rate and rest, matched to your logged sets

| comes from the phone | comes from the watch |
|---|---|
| exercise name | heart rate avg / max / min per set |
| **reps** | rest seconds after each set |
| weight | set timing |

**Reps come from the phone, always.** The watch's rep counter does not just
drift, it fails: 10 kettlebell adductors are reported as 0. `GarminReps` is
stored as a raw record of what the device claimed and is never displayed beside
the logged reps, where a wrong number reads as a correction. Garmin's exercise
`category` is equally unusable — the literal string `UNKNOWN` on 11 of 30 sets
in a real session, and `SQUAT` in the middle of a lateral-raise block.

### How sets are matched

Not on reps. An earlier version scored the alignment on rep similarity, which
was building on sand once the rep counter turned out to fail outright.

What is reliable is structure:

- Both sides are the same session, so both are contiguous and ordered.
- `ExerciseSet` has no timestamp — only `Exercise.Order` and `SetNumber` — so
  position is all the two sides share.
- The log knows exactly how many sets each exercise had.

So the recorded sets partition into one contiguous run per logged exercise, with
run lengths equal to the logged set counts. **When the totals agree that
partition is forced and there is nothing to guess.**

When the totals differ, the difference has to land somewhere and no available
signal says where, so the placement is a stated convention rather than a pretend
inference:

- **more logged than recorded** → shortfall taken from the last exercises. A
  watch that stopped or paused loses the end; sets added to the log afterwards
  are appended at the end too.
- **more recorded than logged** → extras left unmatched at the front, which is
  where unlogged warmups are.

Affected sets get `MatchConfidence` 0.5 instead of 1.0, and the sync log names
the count. Coverage below 50% is a warning: that usually means the wrong
activity is attached to the workout rather than a bad alignment.

Matching runs over the whole sync window every time, not just newly fetched
rows, so a workout logged or edited after its watch session was synced is picked
up on the next run. A re-sync never clears an existing pairing.

### Reading it back

    SELECT e."Name", es."SetNumber", es."Repetitions", es."Weight",
           g."AverageHeartRate", g."MaxHeartRate", g."RestSecondsAfter",
           g."GarminReps", g."MatchConfidence"
    FROM "ExerciseSets" es
    JOIN "Exercises" e ON e."Id" = es."ExerciseId"
    LEFT JOIN "GarminExerciseSets" g ON g."ExerciseSetId" = es."Id"
    WHERE e."WorkoutId" = @id
    ORDER BY e."Order", es."SetNumber";

`GarminReps` is selected above only so it is visible that the column exists and
is being ignored. Do not surface it in the app.

## What is deliberately not here

No controller and no endpoint. Whether the app reads a day of context beside a
workout, or Garmin replaces Health Connect as the activity source outright, is a
decision to make after reading the probe output — not one to bake into a route
before knowing whether the set data is any good.
