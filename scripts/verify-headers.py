#!/usr/bin/env python3
"""Wave 10 slice 10.3 — verify the prod nginx emits the expected security headers.

USAGE
    python3 scripts/verify-headers.py <target-url> [<target-url> ...]
    python3 scripts/verify-headers.py https://localhost
    python3 scripts/verify-headers.py http://localhost:18080 http://localhost:18081

WHAT IT DOES
    For each target URL it:
      1. Sends an HTTP HEAD request (no TLS verification — works against self-signed dev certs).
      2. Asserts the 6 expected security headers are present with the right shape.
      3. Confirms the legacy Wave 9 baseline (X-Frame-Options, X-Content-Type-Options,
         Referrer-Policy) is still present.
      4. Returns non-zero exit code if any header is missing or malformed.

THIS SCRIPT REQUIRES DOCKER ONLY IF YOU ALSO WANT TO SPIN UP AN NGINX CONTAINER.
It can run against any URL (docker compose up'd nginx, k8s ingress, real prod, etc.).

EXIT CODES
    0 — all headers present + correctly shaped
    1 — at least one header missing or malformed (per-URL breakdown printed)
    2 — usage error (no URL provided, or URL is malformed)

NOTE
    The full runtime harness (docker compose up + curl + verify) is in
    scripts/verify-security-headers.sh. This Python helper is the lightweight
    version for ad-hoc checks + CI matrix runs.
"""

from __future__ import annotations

import sys
import urllib.error
import urllib.request

# Per spec/security-headers/spec.md + the Wave 9 baseline. Each entry is a
# (header_name, expected_substring) pair — the substring check is robust to
# future header value changes (e.g. nonce rotation) while still asserting the
# directive is present with the right shape.
REQUIRED_HEADERS: dict[str, str] = {
    # Wave 10 slice 10.3 — the new security headers.
    "content-security-policy": "default-src 'self'",
    "strict-transport-security": "max-age=63072000",
    "permissions-policy": "camera=()",
    # Wave 9 baseline — must NOT regress when slice 10.3 lands.
    "x-frame-options": "DENY",
    "x-content-type-options": "nosniff",
    "referrer-policy": "strict-origin-when-cross-origin",
}

# When set, the script also asserts no header contains a wildcard default-src
# (which is effectively no CSP). Substring-style to keep the check cheap.
FORBIDDEN_SUBSTRINGS: dict[str, str] = {
    "content-security-policy": "default-src *",
}


def _check_one(url: str) -> list[str]:
    """Return a list of failure messages for `url`. Empty list = OK."""
    failures: list[str] = []
    try:
        # Disable TLS verification — works against self-signed dev certs. In
        # CI against real prod, TLS verification is enforced upstream by the
        # reverse proxy itself (Caddy), not by this script.
        ctx = urllib.request.ssl._create_unverified_context()  # noqa: SLF001
        req = urllib.request.Request(url, method="HEAD")
        with urllib.request.urlopen(req, timeout=10, context=ctx) as resp:
            # urllib normalizes header keys to title-case; we lowercase for lookup.
            headers = {k.lower(): v for k, v in resp.headers.items()}
    except urllib.error.URLError as exc:
        return [f"  could not reach {url}: {exc}"]

    for header_name, expected_substring in REQUIRED_HEADERS.items():
        actual = headers.get(header_name)
        if actual is None:
            failures.append(f"  MISSING header '{header_name}'")
            continue
        if expected_substring not in actual:
            failures.append(
                f"  header '{header_name}' does not contain "
                f"'{expected_substring}' (got: {actual!r})"
            )

    for header_name, forbidden in FORBIDDEN_SUBSTRINGS.items():
        actual = headers.get(header_name, "")
        if forbidden in actual:
            failures.append(
                f"  header '{header_name}' contains forbidden '{forbidden}'"
            )

    return failures


def main(argv: list[str]) -> int:
    if not argv:
        print(__doc__)
        return 2

    overall_failures = 0
    for url in argv:
        print(f"--> {url}")
        failures = _check_one(url)
        if failures:
            overall_failures += len(failures)
            print("FAILED:")
            for f in failures:
                print(f)
        else:
            print("OK — all security headers present")

    if overall_failures:
        print(
            f"\n{overall_failures} failure(s) across {len(argv)} URL(s).",
            file=sys.stderr,
        )
        return 1
    print(f"\n{len(argv)} URL(s) checked, all headers OK.")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))