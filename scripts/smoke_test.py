"""Check the deployed Angular + .NET starter without third-party packages.

Usage:
    python scripts/smoke_test.py http://localhost:10000
    python scripts/smoke_test.py https://YOUR-SERVICE.onrender.com
"""

import json
import sys
import urllib.error
import urllib.request


def request(url: str) -> tuple[int, bytes]:
    with urllib.request.urlopen(url, timeout=90) as response:
        return response.status, response.read()


def main() -> int:
    if len(sys.argv) != 2:
        print("Usage: python scripts/smoke_test.py <base-url>")
        return 2
    base = sys.argv[1].rstrip("/")
    try:
        status, page = request(base + "/")
        assert status == 200 and b"<app-root>" in page, "Angular host page missing"
        print("PASS / (Angular HTML)")

        status, health_payload = request(base + "/api/health")
        health = json.loads(health_payload)
        assert status == 200 and health.get("status") == "healthy", "Unhealthy API"
        print("PASS /api/health")

        status, message_payload = request(base + "/api/welcome")
        welcome = json.loads(message_payload)
        assert status == 200 and welcome.get("application") == "Academy ERP", "Unexpected API"
        print("PASS /api/welcome")
        print("Starter deployment checks passed.")
        return 0
    except (urllib.error.URLError, AssertionError, ValueError, TimeoutError) as exc:
        print(f"FAIL: {exc}")
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
