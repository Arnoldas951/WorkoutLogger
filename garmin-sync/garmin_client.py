"""Login, shared by probe.py and sync.py.

Garmin has no personal API. This library reproduces the SSO flow the official
Android app uses, so a login here is indistinguishable from the phone's as far
as Garmin is concerned - which is also why it can break without warning. Every
caller should treat a failure here as "no Garmin data today", never as fatal.
"""

from __future__ import annotations

import logging
import os
import sys
from pathlib import Path

from garminconnect import (
    Garmin,
    GarminConnectAuthenticationError,
    GarminConnectConnectionError,
    GarminConnectTooManyRequestsError,
)

logger = logging.getLogger(__name__)

# Tokens, not credentials, are what persist. The first run does a full SSO
# exchange (and prompts for MFA if the account has it); every run after that
# reuses the refresh token, which the library rotates on its own. Unlike the
# Cloudflare-cookie approach other tools use, this is not bound to an IP - it
# survives a new DHCP lease, which matters here.
TOKEN_STORE = os.getenv("GARMINTOKENS", "/tokens")


class GarminUnavailable(RuntimeError):
    """Garmin could not be reached or would not authenticate.

    Expected occasionally and not worth failing a scheduled run over: the API
    is reverse-engineered and Garmin is entitled to have a bad day.
    """


class GarminNotConfigured(GarminUnavailable):
    """No credentials and no token - nobody has finished the setup.

    Deliberately a separate type. "Garmin is down" should pass quietly in a
    scheduled run; "you never filled in the password" must not, or the sync
    reports success forever while doing nothing at all.
    """


def _prompt_mfa() -> str:
    if not sys.stdin.isatty():
        raise GarminUnavailable(
            "Garmin asked for an MFA code but there is no terminal attached. "
            "Run the first login interactively:\n"
            "  docker compose run --rm garmin-sync python probe.py\n"
            "After that the stored token is reused and this will not be asked again."
        )
    return input("Garmin MFA code: ").strip()


def _has_stored_token() -> bool:
    """True if a previous login left tokens behind."""
    store = Path(TOKEN_STORE)
    if store.is_dir():
        return any(store.glob("*token*.json"))
    return store.is_file()


def connect() -> Garmin:
    """Return a logged-in client, reusing the stored token when possible.

    The password is only needed for the FIRST login. After that the stored
    refresh token is what authenticates, and the library rotates it on its own,
    so GARMIN_PASSWORD can be blanked out of .env and the sync keeps working.
    """
    email = os.getenv("GARMIN_EMAIL")
    password = os.getenv("GARMIN_PASSWORD")

    if not (email and password) and not _has_stored_token():
        raise GarminNotConfigured(
            "No stored Garmin token, and GARMIN_EMAIL / GARMIN_PASSWORD are not "
            "both set in .env.\n"
            "The password IS required for the first login - it only becomes "
            "optional once a token has been stored.\n"
            "Set GARMIN_PASSWORD in .env, then run:\n"
            "  docker compose run --rm garmin-sync python probe.py"
        )

    if not password and _has_stored_token():
        logger.info("Using the stored token; no password needed.")

    api = Garmin(email=email, password=password, prompt_mfa=_prompt_mfa)

    try:
        # Passing the token store makes login() load an existing token if one is
        # there and write a fresh one back if it had to re-authenticate.
        api.login(TOKEN_STORE)
    except GarminConnectAuthenticationError as e:
        raise GarminUnavailable(
            f"Garmin would not authenticate: {e}. The stored token has probably been "
            f"revoked - put GARMIN_PASSWORD back in .env and log in once more:\n"
            f"  docker compose run --rm garmin-sync python probe.py"
        ) from e
    except GarminConnectTooManyRequestsError as e:
        raise GarminUnavailable(
            f"Rate limited by Garmin: {e}. Back off for an hour; do not retry in a loop."
        ) from e
    except GarminConnectConnectionError as e:
        raise GarminUnavailable(f"Could not reach Garmin: {e}") from e
    except Exception as e:
        # The auth flow is reverse-engineered, so an unclassified failure here is
        # expected eventually and should still read as "Garmin is unavailable"
        # rather than a stack trace in the sync log.
        raise GarminUnavailable(f"Unexpected failure logging in to Garmin: {e}") from e

    logger.info("Logged in to Garmin as %s", api.display_name or email)
    return api
