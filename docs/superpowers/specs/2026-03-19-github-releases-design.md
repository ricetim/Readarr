# GitHub Releases & Build-from-Source Instructions — Design Spec

**Date:** 2026-03-19
**Status:** Approved

---

## Overview

Two deliverables:

1. **Tag-triggered GitHub releases** — pushing a `v*` tag creates a GitHub release with auto-generated changelog and a versioned Docker Hub tag alongside `:latest`.
2. **Build-from-source README section** — full prerequisite and build instructions for non-Docker users on Linux, macOS, and Windows, sufficient to deploy without consulting outside documentation.

---

## Part 1: Release Workflow

### Trigger

A push of any tag matching `v*` (e.g., `v10.0.1`) to the repository. Releases are cut manually by the maintainer; no automatic release on every commit.

To release:
```bash
git tag v10.0.1
git push origin v10.0.1
```

### What the Workflow Does

**Step 1 — Docker build & push**
Reuses the existing Docker build process. Pushes two tags to Docker Hub:
- `ricetim/readarr-rresurrected:latest`
- `ricetim/readarr-rresurrected:<tag>` (e.g., `ricetim/readarr-rresurrected:v10.0.1`)

The existing `docker.yml` is updated to also trigger on version tags and push the versioned tag in addition to `:latest`.

**Step 2 — GitHub Release creation**
A new `release.yml` workflow runs on the same tag push. It uses `softprops/action-gh-release` with `generate_release_notes: true` to create a GitHub Release with:
- Release name: the tag (e.g., `v10.0.1`)
- Auto-generated changelog: GitHub pulls commit messages since the previous tag
- No binary artifacts attached (Docker is the distribution mechanism)

### Files Changed

- `.github/workflows/docker.yml` — add `tags: [v*]` trigger and versioned Docker tag output
- `.github/workflows/release.yml` — new workflow for GitHub Release creation

---

## Part 2: README Build-from-Source Instructions

### Scope

Replace the existing minimal "Local Development" section in `README.md` with a comprehensive **"Building from Source"** section. This section targets users who want to run readarr-rresurrected without Docker on their own machine.

### Structure

1. **Maintainer note** — Linux is the only tested platform; Windows/macOS instructions are best-effort.

2. **Prerequisites** — what must be installed, with OS-specific install commands:
   - .NET 6 SDK
   - Node.js 20+ and Yarn
   - Python 3.10+ and pip
   - (Windows only) Git for Windows

3. **Clone the repository**

4. **Build the frontend** — `yarn install && yarn build` (outputs to `_output/UI/`)

5. **Build the backend** — `./build.sh` with `READARRVERSION` set; explain the env var; note Windows uses a different invocation since `build.sh` is a bash script.

6. **Set up bookinfo** — install Python deps, run `uvicorn app:app`, explain it must be reachable by Readarr at a known URL.

7. **Run Readarr** — binary invocation with `--nobrowser --data=<path>`; where to find the API key in `config.xml`; how to configure the bookinfo metadata URL via the Development settings API.

8. **Run as a system service** — systemd unit file example (Linux); launchd plist stub (macOS); Windows Service note pointing to NSSM.

### OS Coverage

Each prerequisite install step includes tabs/blocks for:
- **Linux (Debian/Ubuntu)** — `apt` commands
- **macOS** — Homebrew commands
- **Windows** — `winget` or direct download links; note that `build.sh` requires WSL2 or Git Bash

The section opens with a callout:
> **Note:** readarr-rresurrected is developed and tested on Linux. Windows and macOS instructions are provided as a best effort but are not regularly verified by the maintainer.

---

## Out of Scope

- Pre-built binary artifacts attached to GitHub releases (future work)
- Automatic version bumping on tag push
- Release branches or branching strategy changes
