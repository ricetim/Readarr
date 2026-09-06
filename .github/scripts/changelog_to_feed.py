#!/usr/bin/env python3
"""Turn CHANGELOG.md into the update feed Readarr polls.

Readarr expects two endpoints under a base URL:

    /update/{branch}.json          -> {"available": bool, "updatePackage": {...}}
    /update/{branch}/changes.json  -> [ {...}, {...} ]

The ".json" suffixes matter for static hosting: "develop" cannot be both a file and a
directory, so the plain upstream paths cannot be served from GitHub Pages. The matching
resource paths are set in UpdatePackageProvider.

Versions are emitted with four parts on purpose. BuildInfo reads
Assembly.GetName().Version, which is always four-part, and System.Version treats
"11.0.0" (revision -1) as neither equal to nor greater than "11.0.0.0" — so a
three-part feed would stop the running version being marked as installed.
"""
from __future__ import annotations

import argparse
import json
import os
import re
from pathlib import Path

VERSION_HEADING = re.compile(r"^##\s*\[(?P<version>[^\]]+)\]\s*-\s*(?P<date>\d{4}-\d{2}-\d{2})\s*$")
SECTION_HEADING = re.compile(r"^###\s+(?P<name>New|Fixed)\s*$", re.IGNORECASE)
BULLET = re.compile(r"^-\s+(?P<text>.+?)\s*$")


def four_part(version: str) -> str:
    parts = version.strip().split(".")
    if not all(p.isdigit() for p in parts):
        raise ValueError(f"non-numeric version in changelog: {version!r}")
    parts += ["0"] * (4 - len(parts))
    return ".".join(parts[:4])


def strip_markdown(text: str) -> str:
    """Readarr renders these as plain list items, so inline markup would show literally."""
    text = re.sub(r"\[([^\]]+)\]\([^)]+\)", r"\1", text)   # links -> label
    text = re.sub(r"\*\*([^*]+)\*\*", r"\1", text)          # bold
    text = re.sub(r"(?<!\*)\*([^*]+)\*(?!\*)", r"\1", text)  # italic
    text = re.sub(r"`([^`]+)`", r"\1", text)                # code
    return re.sub(r"\s+", " ", text).strip()


def parse(path: Path) -> list[dict]:
    releases: list[dict] = []
    current: dict | None = None
    section: str | None = None
    pending: list[str] = []

    def flush() -> None:
        if current is not None and section and pending:
            current["changes"][section].append(strip_markdown(" ".join(pending)))
        pending.clear()

    for raw in path.read_text(encoding="utf-8").splitlines():
        heading = VERSION_HEADING.match(raw)
        if heading:
            flush()
            version = heading.group("version")
            if version.lower() == "unreleased":
                current, section = None, None
                continue
            current = {
                "version": four_part(version),
                "releaseDate": f"{heading.group('date')}T00:00:00Z",
                "changes": {"new": [], "fixed": []},
            }
            releases.append(current)
            section = None
            continue

        if current is None:
            continue

        sec = SECTION_HEADING.match(raw)
        if sec:
            flush()
            section = sec.group("name").lower()
            continue

        if raw.startswith("## "):        # a non-version h2 ends the release
            flush()
            current, section = None, None
            continue

        if section is None:
            continue

        bullet = BULLET.match(raw)
        if bullet:
            flush()
            pending.append(bullet.group("text"))
        elif raw.startswith("  ") and raw.strip() and pending:
            pending.append(raw.strip())   # continuation of a wrapped bullet
        elif not raw.strip():
            flush()

    flush()
    return releases


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--changelog", default="CHANGELOG.md", type=Path)
    ap.add_argument("--out", default="_site", type=Path)
    ap.add_argument("--branch", default="develop")
    ap.add_argument("--package-url", default="", help="Download URL advertised for the release")
    args = ap.parse_args()

    releases = parse(args.changelog)
    if not releases:
        raise SystemExit("no releases found in changelog")

    for release in releases:
        release["branch"] = args.branch
        release["fileName"] = ""
        release["url"] = args.package_url
        release["hash"] = ""

    latest = releases[0]
    update_dir = args.out / "v1" / "update"
    update_dir.mkdir(parents=True, exist_ok=True)

    # Readarr decides installability itself by comparing against its own version,
    # so this is always "available"; it only reports what the newest release is.
    (update_dir / f"{args.branch}.json").write_text(
        json.dumps({"available": True, "updatePackage": latest}, indent=2), encoding="utf-8"
    )

    changes_dir = update_dir / args.branch
    changes_dir.mkdir(parents=True, exist_ok=True)
    (changes_dir / "changes.json").write_text(
        json.dumps(releases, indent=2), encoding="utf-8"
    )

    print(f"latest: {latest['version']} ({len(releases)} releases)")
    for r in releases:
        print(f"  {r['version']}  new={len(r['changes']['new'])} fixed={len(r['changes']['fixed'])}")


if __name__ == "__main__":
    main()
