"""Verify public DNS, port 443 and the trusted HTTPS endpoint from the GitHub runner."""
import os
import sys
import urllib.error
import urllib.request
from settings import domain


def main():
    host = domain(os.environ.get("SITE_DOMAIN", ""))
    url = "https://" + host + "/"
    try:
        with urllib.request.urlopen(url, timeout=30) as response:
            if response.status != 200 or response.url != url:
                raise RuntimeError("Unexpected public website response.")
    except urllib.error.HTTPError as error:
        if error.code != 429 or error.url != url:
            raise
    print("PUBLIC HTTPS VERIFIED: " + url)


if __name__ == "__main__":
    try:
        main()
    except Exception as error:
        print(f"::error::Public HTTPS verification failed: {error}", file=sys.stderr)
        sys.exit(1)
