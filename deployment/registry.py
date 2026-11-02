"""Manage only this application's private GHCR package; never delete the package."""
import argparse
import json
import os
import re
import sys
import urllib.error
import urllib.request

OWNER = "xyzkljl1"
PACKAGE = "sendmethefuckinglog"
REPOSITORY = "xyzkljl1/SendMeTheFuckingLog"
IMAGE = f"ghcr.io/{OWNER}/{PACKAGE}"
API = f"https://api.github.com/users/{OWNER}/packages/container/{PACKAGE}"
DIGEST = re.compile(r"sha256:[a-f0-9]{64}")


def api(path="", method="GET", allow_missing=False):
    request = urllib.request.Request(API + path, method=method, headers={
        "Authorization": f"Bearer {os.environ['GH_TOKEN']}",
        "Accept": "application/vnd.github+json",
        "X-GitHub-Api-Version": "2022-11-28",
    })
    try:
        with urllib.request.urlopen(request, timeout=30) as response:
            body = response.read()
            return json.loads(body) if body else None
    except urllib.error.HTTPError as error:
        if error.code == 404 and allow_missing:
            return None
        raise RuntimeError(f"GHCR {method} {path or '/'} failed: HTTP {error.code}") from None


def check_package(allow_missing=False):
    package = api(allow_missing=allow_missing)
    if package is None:
        print("Package is absent or inaccessible; first push may create it as private.")
        return
    if (package.get("name") != PACKAGE or package.get("package_type") != "container"
            or package.get("owner", {}).get("login", "").lower() != OWNER
            or package.get("visibility") != "private"
            or package.get("repository", {}).get("full_name", "").lower() != REPOSITORY.lower()):
        raise RuntimeError("Package identity, private visibility, or linked repository does not match.")


def versions():
    result = []
    page = 1
    while True:
        batch = api(f"/versions?state=active&per_page=100&page={page}")
        result.extend(batch)
        if len(batch) < 100:
            return result
        page += 1


def cleanup_plan(items, keep_digest):
    if not DIGEST.fullmatch(keep_digest):
        raise ValueError("Invalid deployed image digest.")
    keep = [item for item in items if item.get("name") == keep_digest]
    if len(keep) != 1:
        raise RuntimeError("Deployed digest must match exactly one package version; refusing cleanup.")
    # The workflow publishes a single-platform manifest without attestations.
    # No multi-platform child manifests need independent retention.
    return [item["id"] for item in items if item["id"] != keep[0]["id"]]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("command", choices=["preflight", "verify", "cleanup"])
    parser.add_argument("--digest")
    args = parser.parse_args()
    check_package(allow_missing=args.command == "preflight")
    if args.command == "cleanup":
        for version_id in cleanup_plan(versions(), args.digest or ""):
            api(f"/versions/{int(version_id)}", method="DELETE")
            print(f"Deleted old GHCR version {version_id}.")
        remaining = versions()
        if len(remaining) != 1 or remaining[0]["name"] != args.digest:
            raise RuntimeError("GHCR cleanup verification failed; extra or missing versions remain.")
        print("GHCR contains only the deployed version.")


if __name__ == "__main__":
    try:
        main()
    except Exception as error:
        print(f"::error::{error}", file=sys.stderr)
        sys.exit(1)
