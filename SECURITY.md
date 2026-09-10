# Security notes

## Burned secrets

Two secrets were hard-coded in `appsettings.json` early on and are therefore in
git history permanently. Removing them from the working tree does not remove
them from history.

| Secret | Old value | Status |
| --- | --- | --- |
| JWT signing key | `78945612364589712klsdjhmweiojd^%$@` | **Rotated.** New key is in user-secrets. |
| Postgres password | `tainted` | **Still in use.** Change before this runs anywhere but localhost. |

Anyone with a copy of the repo can recover the old signing key from history. It
no longer works, because rotating it invalidated every token signed with it.

The database password has not been changed. Low risk while Postgres binds to
localhost only, but it is public. To fix:

```sql
ALTER USER postgres WITH PASSWORD '<something new>';
```

Then update `ConnectionStrings:DefaultConnection` in user-secrets, and the
`docker-compose.yml` environment, to match.

## Where secrets live

**Local development** — .NET user-secrets, at
`%APPDATA%\Microsoft\UserSecrets\935fca06-6f43-40d6-b35f-99c4d96192d0\secrets.json`.
View with `dotnet user-secrets list` from the project folder.

Note this file is **plain JSON, not encrypted**. User-secrets keeps values out of
the repo so they cannot be committed by accident; it is not protection against
someone reading your disk.

```bash
dotnet user-secrets set "Jwt:Key" "<48+ random bytes, base64>"
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Database=workoutlogger;Username=postgres;Password=<password>"
```

On Windows, generate and set a key in one go:

```powershell
dotnet user-secrets set "Jwt:Key" ([Convert]::ToBase64String((1..48 | ForEach-Object { Get-Random -Max 256 })))
```

**Deployed** — environment variables; the double underscore maps to config nesting:

```
Jwt__Key=<48+ random bytes, base64>
ConnectionStrings__DefaultConnection=Host=db;Database=workoutlogger;Username=postgres;Password=<password>
Cors__AllowedOrigins__0=https://your-web-client
```

The app refuses to start if `Jwt:Key` is missing or under 32 bytes. That is
deliberate: it previously logged a warning and carried on *without registering
authentication at all*, which meant a misconfigured deploy served every
`[Authorize]` endpoint to anyone.

That check does not affect EF tooling — `Context/WorkoutDbContextFactory.cs`
implements `IDesignTimeDbContextFactory`, so `dotnet ef` builds a context from
the connection string without booting the app.

## Token model

| | Lifetime | Revocable | Stored |
| --- | --- | --- | --- |
| Access token | 15 min | No | Client only |
| Refresh token | 30 days | Yes | SHA-256 hash in `RefreshTokens` |

Access tokens are signed JWTs and are not tracked, so they cannot be revoked
early — the 15 minute lifetime *is* the revocation window. Refresh tokens are
opaque random values; only their hash is stored, so a database leak does not
hand over usable credentials.

Refresh tokens rotate on every use. Two things can leave one revoked, and they
are treated differently:

| Cause | `ReplacedByTokenId` | Presenting it again |
| --- | --- | --- |
| Rotated out by a refresh | set | Treated as theft — **every** session for that user is revoked |
| Explicit logout | null | Rejected, other sessions untouched |

A token that was rotated out and then reappears means two parties hold it, and
there is no way to tell the attacker from the user. A token revoked by logout
just means a stale client retried; signing out on a phone must not sign the user
out on their laptop.

**Consequence for clients: never fire two refreshes concurrently.** Both would
present the same token and the second would trip replay detection, logging the
user out at random. The web and mobile clients each share a single in-flight
refresh promise for exactly this reason.

## Known gaps

- **No rate limiting on `/api/auth/login`.** Nothing stops password guessing.
- **No password strength requirement.** `"a"` is currently valid.
- **Usernames are case sensitive.** `Alice` and `alice` are different accounts.
- **Expired and revoked `RefreshTokens` rows are never cleaned up.**
- **The API speaks plain HTTP in development** and binds `0.0.0.0` on the
  `http-lan` profile. Fine on a home network; do not run that profile on
  untrusted WiFi.
- **No backups.** The Postgres volume becomes training history that cannot be
  recreated.

These are acceptable for a single user on a home network. All of them need
addressing before anyone else uses this, or before it is reachable from the
internet.
