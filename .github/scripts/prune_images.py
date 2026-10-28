#!/usr/bin/env python3
"""Deletes old container image versions from GHCR, keeping everything a release still needs.

Kept: versions with a release tag (1.2.3) and everything they reference (platform manifests of a multi-arch
index, cosign signatures and attestations stored as sha256-<digest> tags, and what those reference), plus every
version younger than --days. Everything else (commit SHA builds of pull requests, their signatures, orphaned
platform manifests) is deleted. Without --apply nothing is deleted.
"""

from __future__ import annotations

import argparse
import json
import re
import subprocess
import sys
from datetime import datetime, timedelta, timezone

RELEASE_TAG = re.compile(r"^v?\d+\.\d+\.\d+$")


def gh(*args: str) -> str:
    return subprocess.run(["gh", "api", *args], check=True, capture_output=True, text=True).stdout


def list_versions(scope: str, package: str) -> list[dict]:
    pages = gh("--paginate", "--slurp", f"{scope}/packages/container/{package}/versions?per_page=100")
    return [version for page in json.loads(pages) for version in page]


def referenced_digests(image: str, digest: str) -> list[str]:
    """Digests listed by an index (platform manifests, attestation manifests). Empty for a plain manifest."""
    raw = subprocess.run(
        ["docker", "buildx", "imagetools", "inspect", "--raw", f"{image}@{digest}"],
        check=True, capture_output=True, text=True,
    ).stdout
    return [entry["digest"] for entry in json.loads(raw).get("manifests", [])]


def tags(version: dict) -> list[str]:
    return version.get("metadata", {}).get("container", {}).get("tags", [])


def plan(versions: list[dict], references, now: datetime, days: int) -> tuple[list[dict], list[dict]]:
    """Splits versions into (keep, delete). `references(digest)` returns the digests an index points to."""
    by_digest = {v["name"]: v for v in versions}
    cutoff = now - timedelta(days=days)

    def created(version: dict) -> datetime:
        return datetime.fromisoformat(version["created_at"].replace("Z", "+00:00"))

    roots = [v for v in versions if any(RELEASE_TAG.match(t) for t in tags(v)) or created(v) > cutoff]
    protected: set[str] = set()
    pending = [v["name"] for v in roots]
    while pending:
        digest = pending.pop()
        if digest in protected:
            continue
        protected.add(digest)
        pending.extend(d for d in references(digest) if d in by_digest)
        # cosign stores signatures, attestations and its referrers fallback under tags derived from the digest.
        prefix = digest.replace(":", "-")
        pending.extend(v["name"] for v in versions if any(t.startswith(prefix) for t in tags(v)))

    keep = [v for v in versions if v["name"] in protected]
    delete = [v for v in versions if v["name"] not in protected]
    return keep, delete


def main() -> int:
    parser = argparse.ArgumentParser(description="Delete old GHCR image versions.")
    parser.add_argument("--owner", required=True)
    parser.add_argument("--owner-type", choices=["User", "Organization"], required=True)
    parser.add_argument("--days", type=int, default=14)
    parser.add_argument("--apply", action="store_true", help="delete for real (default: report only)")
    parser.add_argument("packages", nargs="+")
    args = parser.parse_args()

    scope = f"orgs/{args.owner}" if args.owner_type == "Organization" else f"users/{args.owner}"
    now = datetime.now(timezone.utc)
    print(f"| Package | Kept | {'Deleted' if args.apply else 'Would delete'} |")
    print("|---|---|---|")
    for package in args.packages:
        image = f"ghcr.io/{args.owner.lower()}/{package}"
        try:
            versions = list_versions(scope, package)
        except subprocess.CalledProcessError as error:
            if "404" in error.stderr:
                print(f"| {package} | not published yet | - |")
                continue
            raise
        keep, delete = plan(versions, lambda digest: referenced_digests(image, digest), now, args.days)
        if args.apply:
            for version in delete:
                gh("--method", "DELETE", f"{scope}/packages/container/{package}/versions/{version['id']}")
        print(f"| {package} | {len(keep)} | {len(delete)} |")
    return 0


if __name__ == "__main__":
    sys.exit(main())
