# GitHub Releases & Build-from-Source Instructions Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add tag-triggered GitHub releases with versioned Docker tags, and replace the minimal README dev section with comprehensive build-from-source instructions covering Linux, macOS, and Windows.

**Architecture:** Two GitHub Actions workflows handle releases — the existing `docker.yml` is updated to push a versioned Docker tag on `v*` tag pushes, and a new `release.yml` creates the GitHub Release with auto-generated changelog. The README gets an expanded section with OS-specific prerequisite instructions and full deployment steps.

**Tech Stack:** GitHub Actions, `docker/build-push-action@v6`, `softprops/action-gh-release@v2`, Markdown

---

## Chunk 1: Release Workflows

### Task 1: Update docker.yml to push versioned tag on releases

**Files:**
- Modify: `.github/workflows/docker.yml`

The workflow needs to also trigger on `v*` tag pushes and, when on a tag, push an additional versioned Docker tag alongside `:latest`.

- [ ] **Step 1: Add tag trigger and dynamic tag logic to docker.yml**

Replace `.github/workflows/docker.yml` with:

```yaml
name: Build and Push Docker Image

on:
  push:
    branches:
      - develop
    tags:
      - 'v*'
  workflow_dispatch:

jobs:
  docker:
    runs-on: ubuntu-latest

    steps:
      - name: Checkout
        uses: actions/checkout@v4

      - name: Set up Docker Buildx
        uses: docker/setup-buildx-action@v3

      - name: Log in to Docker Hub
        uses: docker/login-action@v3
        with:
          username: ${{ secrets.DOCKERHUB_USERNAME }}
          password: ${{ secrets.DOCKERHUB_TOKEN }}

      - name: Compute Docker tags
        id: tags
        run: |
          BASE="ricetim/readarr-rresurrected"
          if [[ "$GITHUB_REF" == refs/tags/* ]]; then
            VERSION="${GITHUB_REF#refs/tags/}"
            echo "tags=${BASE}:latest,${BASE}:${VERSION}" >> "$GITHUB_OUTPUT"
          else
            echo "tags=${BASE}:latest" >> "$GITHUB_OUTPUT"
          fi

      - name: Build and push image
        uses: docker/build-push-action@v6
        with:
          context: .
          push: true
          tags: ${{ steps.tags.outputs.tags }}
          cache-from: type=gha
          cache-to: type=gha,mode=max

      - name: Update Docker Hub README
        uses: peter-evans/dockerhub-description@v4
        with:
          username: ${{ secrets.DOCKERHUB_USERNAME }}
          password: ${{ secrets.DOCKERHUB_PASSWORD }}
          repository: ricetim/readarr-rresurrected
          readme-filepath: ./README-docker.md
```

- [ ] **Step 2: Verify the file looks correct**

```bash
cat .github/workflows/docker.yml
```

- [ ] **Step 3: Commit**

```bash
git add .github/workflows/docker.yml
git commit -m "ci: push versioned Docker tag on v* releases"
```

---

### Task 2: Create release.yml workflow

**Files:**
- Create: `.github/workflows/release.yml`

This workflow triggers on `v*` tag pushes and creates a GitHub Release with auto-generated changelog (commit messages since the previous tag).

- [ ] **Step 1: Create .github/workflows/release.yml**

```yaml
name: Create GitHub Release

on:
  push:
    tags:
      - 'v*'

jobs:
  release:
    runs-on: ubuntu-latest
    permissions:
      contents: write

    steps:
      - name: Checkout
        uses: actions/checkout@v4

      - name: Create GitHub Release
        uses: softprops/action-gh-release@v2
        with:
          generate_release_notes: true
```

- [ ] **Step 2: Verify the file looks correct**

```bash
cat .github/workflows/release.yml
```

- [ ] **Step 3: Commit**

```bash
git add .github/workflows/release.yml
git commit -m "ci: add tag-triggered GitHub release workflow"
```

---

## Chunk 2: README Build-from-Source Instructions

### Task 3: Replace README dev section with comprehensive build-from-source guide

**Files:**
- Modify: `README.md`

The existing "Building from Source" section has a minimal "Local Development (without Docker)" subsection. Replace the entire "Building from Source" section with a comprehensive guide structured as follows:

1. Maintainer platform note
2. Prerequisites (with Linux/macOS/Windows install commands for each)
3. Clone
4. Build frontend
5. Build backend
6. Set up bookinfo
7. Run Readarr
8. Configure bookinfo as the metadata source
9. Run as a system service (Linux systemd, macOS launchd, Windows NSSM)

- [ ] **Step 1: Replace the "Building from Source" section in README.md**

Find the `## Building from Source` heading and replace everything from that heading through the end of that section (up to but not including `## Migrating from an Existing Readarr Installation`) with the following:

```markdown
## Building from Source

> **Platform note:** readarr-rresurrected is developed and tested on Linux. The instructions below for macOS and Windows are provided as a best effort but are not regularly verified by the maintainer. If you run into platform-specific issues, Docker is the recommended path.

### Prerequisites

You need four things installed before building: the .NET 6 SDK, Node.js 20+, Yarn, and Python 3.10+.

#### .NET 6 SDK

<details>
<summary>Linux (Debian/Ubuntu)</summary>

```bash
wget https://packages.microsoft.com/config/ubuntu/$(lsb_release -rs)/packages-microsoft-prod.deb -O /tmp/packages-microsoft-prod.deb
sudo dpkg -i /tmp/packages-microsoft-prod.deb
sudo apt update && sudo apt install -y dotnet-sdk-6.0
```
</details>

<details>
<summary>macOS</summary>

```bash
brew install --cask dotnet-sdk
# Verify: dotnet --version should print 6.x.x
```
</details>

<details>
<summary>Windows</summary>

Download and run the .NET 6 SDK installer from:
https://dotnet.microsoft.com/en-us/download/dotnet/6.0

Or via winget:
```powershell
winget install Microsoft.DotNet.SDK.6
```
</details>

#### Node.js 20+ and Yarn

<details>
<summary>Linux (Debian/Ubuntu)</summary>

```bash
curl -fsSL https://deb.nodesource.com/setup_20.x | sudo -E bash -
sudo apt install -y nodejs
sudo npm install -g yarn
```
</details>

<details>
<summary>macOS</summary>

```bash
brew install node@20
npm install -g yarn
```
</details>

<details>
<summary>Windows</summary>

```powershell
winget install OpenJS.NodeJS.LTS
npm install -g yarn
```
</details>

#### Python 3.10+

<details>
<summary>Linux (Debian/Ubuntu)</summary>

```bash
sudo apt install -y python3 python3-pip python3-venv
```
</details>

<details>
<summary>macOS</summary>

```bash
brew install python3
```
</details>

<details>
<summary>Windows</summary>

```powershell
winget install Python.Python.3.12
```
</details>

---

### Clone the Repository

```bash
git clone https://github.com/ricetim/readarr-rresurrected.git
cd readarr-rresurrected
```

---

### Build the Frontend

```bash
yarn install --frozen-lockfile
yarn build
```

Output lands in `_output/UI/`. This step is required — Readarr serves the UI from that folder at runtime.

---

### Build the Backend

`build.sh` compiles the .NET backend, copies the frontend, and packages everything into `_output/net6.0/`.

```bash
export READARRVERSION="10.0.0.1"   # set to whatever version string you want
./build.sh
```

> **Windows users:** `build.sh` is a bash script. Run it in WSL2 or Git Bash. Alternatively, run the underlying dotnet command directly:
> ```powershell
> dotnet msbuild src/Readarr.sln `
>   -restore `
>   -p:Configuration=Release `
>   -p:Platform=Posix `
>   -p:RuntimeIdentifiers=win-x64 `
>   -p:EnableAnalyzers=false `
>   -p:TreatWarningsAsErrors=false `
>   -t:PublishAllRids
> ```

The compiled output is in `_output/net6.0/`.

---

### Set Up bookinfo

bookinfo is the bundled metadata service that replaces the defunct Goodreads cloud API. It must be running for author and book searches to work.

```bash
cd bookinfo
python3 -m venv .venv
source .venv/bin/activate          # Windows: .venv\Scripts\activate
pip install -r requirements.txt
uvicorn app:app --host 127.0.0.1 --port 28202 --workers 1
```

Leave this running in a separate terminal (or set it up as a service — see below).

---

### Run Readarr

```bash
_output/net6.0/Readarr --nobrowser --data=/path/to/your/data
```

Replace `/path/to/your/data` with a directory where Readarr will store its database, logs, and `config.xml`. It will be created on first run.

Open `http://localhost:8787` in your browser. Your API key is in `<your-data-dir>/config.xml` under the `<ApiKey>` element.

---

### Configure bookinfo as the Metadata Source

On first run, Readarr is pointed at the defunct upstream metadata API. Tell it to use your local bookinfo instance instead:

```bash
curl -s -X PUT http://localhost:8787/api/v1/config/development \
  -H "X-Api-Key: <your-api-key>" \
  -H "Content-Type: application/json" \
  -d '{"metadataSource": "http://127.0.0.1:28202"}'
```

After this, author searches and book lookups will go through bookinfo.

---

### Run as a System Service

Running Readarr and bookinfo manually works for testing, but you'll want them to start automatically and stay running.

#### Linux — systemd

Create `/etc/systemd/system/bookinfo.service`:

```ini
[Unit]
Description=bookinfo metadata service for readarr-rresurrected
After=network.target

[Service]
Type=simple
User=readarr
WorkingDirectory=/opt/readarr-rresurrected/bookinfo
ExecStart=/opt/readarr-rresurrected/bookinfo/.venv/bin/uvicorn app:app --host 127.0.0.1 --port 28202 --workers 1
Restart=on-failure

[Install]
WantedBy=multi-user.target
```

Create `/etc/systemd/system/readarr.service`:

```ini
[Unit]
Description=readarr-rresurrected
After=network.target bookinfo.service

[Service]
Type=simple
User=readarr
ExecStart=/opt/readarr-rresurrected/_output/net6.0/Readarr --nobrowser --data=/var/lib/readarr
Restart=on-failure

[Install]
WantedBy=multi-user.target
```

Then enable and start:

```bash
sudo systemctl daemon-reload
sudo systemctl enable --now bookinfo readarr
```

Adjust `User=`, `WorkingDirectory=`, `ExecStart=`, and data paths to match your actual install location.

#### macOS — launchd

Create `~/Library/LaunchAgents/com.readarr.bookinfo.plist`:

```xml
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>Label</key>
  <string>com.readarr.bookinfo</string>
  <key>ProgramArguments</key>
  <array>
    <string>/path/to/readarr-rresurrected/bookinfo/.venv/bin/uvicorn</string>
    <string>app:app</string>
    <string>--host</string><string>127.0.0.1</string>
    <string>--port</string><string>28202</string>
    <string>--workers</string><string>1</string>
  </array>
  <key>WorkingDirectory</key>
  <string>/path/to/readarr-rresurrected/bookinfo</string>
  <key>RunAtLoad</key>
  <true/>
  <key>KeepAlive</key>
  <true/>
</dict>
</plist>
```

Create a similar plist for Readarr itself, then load both:

```bash
launchctl load ~/Library/LaunchAgents/com.readarr.bookinfo.plist
launchctl load ~/Library/LaunchAgents/com.readarr.readarr.plist
```

#### Windows — NSSM

[NSSM](https://nssm.cc) (the Non-Sucking Service Manager) wraps any executable as a Windows service.

```powershell
# Install NSSM via winget or download from https://nssm.cc
winget install NSSM.NSSM

# Register bookinfo
nssm install bookinfo "C:\path\to\readarr-rresurrected\bookinfo\.venv\Scripts\uvicorn.exe" `
  "app:app --host 127.0.0.1 --port 28202 --workers 1"
nssm set bookinfo AppDirectory "C:\path\to\readarr-rresurrected\bookinfo"
nssm start bookinfo

# Register Readarr
nssm install readarr "C:\path\to\readarr-rresurrected\_output\net6.0\Readarr.exe" `
  "--nobrowser --data=C:\ProgramData\readarr"
nssm start readarr
```

```

- [ ] **Step 2: Verify the README renders correctly (spot-check)**

```bash
grep -n "## Building from Source" README.md
grep -n "## Migrating" README.md
# Confirm the new section sits between those two headings
```

- [ ] **Step 3: Commit**

```bash
git add README.md
git commit -m "docs: comprehensive build-from-source instructions with OS-specific prereqs"
```

---

### Task 4: Push and verify

- [ ] **Step 1: Push to develop**

```bash
git push origin develop
```

- [ ] **Step 2: Verify Docker workflow still passes on develop push**

```bash
gh run list --repo ricetim/readarr-rresurrected --workflow=docker.yml --limit=3
```

Expected: the new run completes with `success`.

- [ ] **Step 3: Smoke-test a release**

```bash
git tag v10.0.1
git push origin v10.0.1
```

- [ ] **Step 4: Verify both workflows triggered**

```bash
gh run list --repo ricetim/readarr-rresurrected --limit=5
```

Expected: one run of "Build and Push Docker Image" and one of "Create GitHub Release", both succeeding.

- [ ] **Step 5: Verify GitHub Release was created**

```bash
gh release list --repo ricetim/readarr-rresurrected --limit=3
```

Expected: `v10.0.1` appears with auto-generated release notes.

- [ ] **Step 6: Verify versioned Docker tag on Docker Hub**

```bash
docker pull ricetim/readarr-rresurrected:v10.0.1
```

Expected: pulls successfully.

- [ ] **Step 7: Delete the test tag if this was a dry run (skip if this is the real first release)**

```bash
git tag -d v10.0.1
git push origin --delete v10.0.1
gh release delete v10.0.1 --repo ricetim/readarr-rresurrected --yes
```
