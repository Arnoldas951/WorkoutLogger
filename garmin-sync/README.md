# garmin-sync

Pulls Garmin Connect data into the WorkoutLogger database, as a sidecar
container beside `api` and `db`.

## Order of operations

**Your API never needs to be running.** The sidecar talks to Postgres directly;
it does not go through the API, and the probe does not touch Postgres at all.

| # | Step | Needs |
|---|------|-------|
| 1 | Fill in `.env` (incl. `JWT_KEY`, see below) | nothing |
| 2 | `docker compose run --rm garmin-sync python probe.py` | a Garmin login. **No database, no migration, no API.** Writes JSON to `probe-output/` and prints a report. |
| 3 | *Read the report and decide whether this is worth continuing.* | — |
| 4 | `dotnet ef migrations add AddGarminTables && dotnet ef database update` | the database the app actually uses |
| 5 | `docker compose run --rm garmin-sync python sync.py` | the tables from step 4 |

Steps 4 and 5 are pointless until step 3 says the data is good enough to keep.

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

## First run

1. `cp garmin-sync/.env.example .env` in the repo root, and fill it in.

   `GARMIN_EMAIL` / `GARMIN_PASSWORD` are your real Garmin Connect login —
   Garmin issues no app passwords or personal OAuth tokens, so there is no
   lesser credential available. `GARMIN_SYNC_USER_ID` is **not** a Garmin
   value: it is the `Id` from your own `Users` table.

2. Apply the migration (the tables do not exist yet):

       dotnet ef migrations add AddGarminTables
       dotnet ef database update

3. Log in once, interactively, so MFA can be answered:

       docker compose run --rm garmin-sync python probe.py

   The token is written to the `garmin-tokens` volume and reused after that.

4. **Blank `GARMIN_PASSWORD` in `.env`.** Once a token exists the password is
   dead weight sitting in plaintext; the sync authenticates with the rotating
   refresh token instead. Put it back only if the token is ever revoked.

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

## What is deliberately not here

No controller and no endpoint. Whether the app reads a day of context beside a
workout, or Garmin replaces Health Connect as the activity source outright, is a
decision to make after reading the probe output — not one to bake into a route
before knowing whether the set data is any good.
