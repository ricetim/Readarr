# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Status

Upstream Readarr was **retired** in early 2025 when its Goodreads metadata source became unusable.

This repository is an actively developed **fork** that keeps the codebase alive. The two main
divergences from upstream are:

1. **A self-hosted metadata service** (`bookinfo/`) replaces the dead Readarr cloud API.
2. **Native private-tracker indexers** (MyAnonamouse, Bibliotik) instead of Prowlarr-only access.

When reading upstream docs or old issues, assume anything about `api.bookinfo.club` or the
Readarr cloud metadata server is no longer true here.

## Architecture Overview

Readarr is an ebook/audiobook manager — a .NET 6 backend with a React/Redux frontend, modeled
after Sonarr/Radarr. This fork adds a Python metadata sidecar.

### Backend (`src/`)

The backend is split into these key assemblies (all loaded at startup via `Bootstrap.ASSEMBLIES`):

- **`NzbDrone.Core`** — All business logic: books, authors, download clients, indexers, metadata, media files, quality profiles, scheduler. The largest and most important project.
- **`Readarr.Api.V1`** — REST API controllers (ASP.NET Core). Each domain has its own subfolder mirroring `NzbDrone.Core`.
- **`Readarr.Http`** — HTTP middleware, auth, SignalR hub setup.
- **`NzbDrone.Host`** — Application bootstrap, DI container setup (DryIoc), startup/shutdown lifecycle.
- **`NzbDrone.Console`** — Entry point for the console executable.
- **`NzbDrone.Mono` / `NzbDrone.Windows`** — Platform-specific implementations (OS service, disk access).
- **`NzbDrone.SignalR`** — Real-time push notifications to the frontend.

**Important namespace quirk:** Despite the executable being `Readarr`, all C# namespaces are `NzbDrone.*` (original project name). This is intentional and set in `src/Directory.Build.props` via `RootNamespace`.

**Dependency injection:** DryIoc. Services are auto-discovered by convention (interfaces `IFoo` implemented by `Foo` in the same assembly).

**Data access:** Dapper ORM against SQLite (default) or PostgreSQL. Repositories inherit from `BasicRepository<TModel>`. Database migrations are sequential numbered files in `src/NzbDrone.Core/Datastore/Migration/` (currently through `043_quality_profile_allowed_languages.cs`).

**Command/event bus:** `NzbDrone.Core.Messaging` — commands implement `IExecute<TCommand>`, events implement `IHandle<TEvent>`. This is the primary way services communicate.

### Metadata service (`bookinfo/`)

A standalone **FastAPI (Python 3.12)** app that serves the metadata contract the C# side expects.
It fetches from Goodreads' GraphQL and XML APIs (the query shapes are replicated from
`rreading-glasses`) and enriches results via Google Books.

- Entry point `bookinfo/app.py`; Goodreads client in `goodreads.py`; Google Books in `google_books.py`; edition classification/dedup in `models.py`.
- Listens on port **28202**.
- Routes: `/author/{id}`, `/author/changed`, `/work/{id}`, `/book/{edition_id}`, `/book/bulk` (GET + POST), `/series/{id}`, `/search`, `/recommended`, and `DELETE /cache/author/{id}`.
- Long author fetches are backgrounded; on completion the service calls back into Readarr's
  `POST /api/v1/command` with `RefreshAuthor` (needs `READARR_URL` + `READARR_API_KEY`).
- Env vars: `GOOGLE_BOOKS_API_KEY`, `BOOKINFO_LOG_DIR`, `BOOKINFO_LOG_KEEP`, `BOOKINFO_GR_RATE` (Goodreads req/sec), `BOOKINFO_BATCH_SIZE`, `READARR_URL`, `READARR_API_KEY`.

**How the C# side reaches it:** `src/NzbDrone.Core/MetadataSource/MetadataRequestBuilder.cs` reads
the `READARR_METADATA_URL` env var, defaulting to `http://localhost:28202/{route}`. There is **no
database setting for this** — the old `MetadataSource` config row was removed. To point at a
different server (e.g. a separate `rreading-glasses` instance), set the env var before launching.

The consuming C# code lives in `src/NzbDrone.Core/MetadataSource/BookInfo/` (`BookInfoProxy`,
`BookInfoCacheService`, and the `BookInfoResource/` DTOs). The legacy `Goodreads/` and
`GoodreadsSearchProxy/` folders remain but are not the live path.

### Frontend (`frontend/`)

React 17 + Redux + React Router 5. The codebase is in a mixed JS→TypeScript migration: newer components use `.tsx`, older ones are `.js`. Both coexist under the same webpack build.

State management follows a consistent pattern:
- `frontend/src/Store/Actions/` — Redux action creators (one file per domain)
- `frontend/src/Store/Selectors/` — Reselect selectors
- Page-level components live in domain folders (`Author/`, `Book/`, `Settings/`, etc.)

CSS uses PostCSS with CSS Modules (`.css` files colocated with components).

## Commands

### Backend

```bash
# First-time setup (restore NuGet packages)
dotnet restore src/Readarr.sln

# Build backend (MUST build from solution, not individual project — SolutionDir needed for stylecop.json)
dotnet build src/Readarr.sln -p:Configuration=Debug -p:Platform=Posix --no-restore

# Build everything (backend + frontend + lint) for release
./build.sh

# Run a specific test class (build from solution first, then --no-build to avoid SA1200 errors)
dotnet build src/Readarr.sln -p:Configuration=Debug -p:Platform=Posix --no-restore && \
dotnet test src/NzbDrone.Core.Test/Readarr.Core.Test.csproj \
  --filter "FullyQualifiedName~SomeTestClass" \
  -p:Platform=Posix --no-build

# Run backend unit tests (Linux) — requires built _tests/ packages from build.sh
./test.sh Linux Unit Test

# Run backend integration tests (Linux)
./test.sh Linux Integration Test
```

### Frontend

```bash
# Install dependencies
yarn install

# Build frontend (outputs to _output/UI)
yarn build

# Watch mode for development
yarn start

# Lint JS/TS
yarn lint
yarn lint-fix

# Lint CSS
yarn stylelint-linux
```

### Metadata service

```bash
pip install -r bookinfo/requirements.txt

# Run tests (pytest.ini sets asyncio_mode=auto and pythonpath=.)
cd bookinfo && pytest

# Run locally — Readarr will find it on the default port
cd bookinfo && uvicorn app:app --host 127.0.0.1 --port 28202
```

Tests use `respx` to mock the Goodreads/Google Books HTTP calls — never hit the live APIs in tests.

### Docker

The `Dockerfile` is a three-stage build (node → dotnet SDK → Alpine runtime) that packages the
frontend, the .NET backend, and the Python `bookinfo` app into a **single container**.
`docker/supervisord.conf` runs both processes: `bookinfo` first (priority 10), then `Readarr`
(priority 20). Config volume is `/config`; the app listens on 8787.

```bash
docker compose up -d --build
```

## Code Style

**C#:** StyleCop enforced at build time. 4-space indentation, `using` directives outside namespace, `system` usings first. Warnings are treated as errors — the build will fail on style violations.

**Frontend JS:** ESLint with Prettier. 2-space indentation, single quotes, semicolons required. File names must match the exported name (`filenames/match-exported`). Import order is enforced (`simple-import-sort`).

**Frontend TypeScript:** Same ESLint config with `@typescript-eslint` rules added. Prettier is required for `.ts`/`.tsx` files.

**Python (`bookinfo/`):** `from __future__ import annotations` at the top, type hints throughout, module-level `logger = logging.getLogger(__name__)`.

## Key Patterns

**Adding a new API endpoint:** Create a controller in `src/Readarr.Api.V1/<Domain>/`, inherit from the appropriate base class. Mirror the resource model from `NzbDrone.Core`.

**Adding a command:** Create a `Command` subclass and an `IExecute<YourCommand>` implementation in `NzbDrone.Core`. DI picks it up automatically.

**Database schema changes:** Add a new migration file in `src/NzbDrone.Core/Datastore/Migration/` following the sequential numbering pattern (e.g., `044_your_change.cs`).

**LazyLoaded properties:** Many model properties use `LazyLoaded<T>` — these are populated on-demand by the repository layer and should not be assumed to be populated unless explicitly queried.

**Adding a native indexer:** Follow the five-file convention used by `Indexers/MyAnonamouse/` and
`Indexers/Bibliotik/`:

| File | Role |
| --- | --- |
| `X.cs` | `HttpIndexerBase<XSettings>` subclass; declares `Name`, `Protocol`, `SupportsRss`, `SupportsSearch`, `PageSize`; wires up the generator and parser |
| `XRequestGenerator.cs` | `IIndexerRequestGenerator` — builds RSS and per-search-criteria requests |
| `XParser.cs` | `IParseIndexerResponse` — maps the tracker's response into `ReleaseInfo` |
| `XSettings.cs` | `IIndexerSettings` with `FluentValidation` rules; surfaces UI fields |
| `XInfo.cs` | Response DTOs for JSON deserialization |

Two hard-won gotchas when writing parsers for these trackers:

- **Titles must contain the author.** `DownloadDecisionMaker` runs
  `ParseBookTitleWithSearchCriteria()`, which fuzzy-matches the author against the title string.
  Trackers that return bare book titles will produce `foundAuthor == null` and every release gets
  rejected as "Unable to parse release". Format the title as `"Author - Book Title"` in the parser.
- **Don't reuse `BookQuery.GetQueryTitle()` for JSON bodies.** It URL-encodes spaces to `+` for
  Newznab query params. For JSON POST bodies use the raw value, e.g.
  `BookTitle.SplitBookTitle(Author.Name).Item1`.

Tests for indexers live in `src/NzbDrone.Core.Test/IndexerTests/<Name>Tests/`, with JSON/HTML
fixtures under `src/NzbDrone.Core.Test/Files/Indexers/<Name>/`.

## Scratch and Design Notes

- `docs/` — durable design docs (e.g. `file-to-book-matching.md`) and dated implementation plans under `docs/plans/`.
- `.dev/` — untracked working notes, captured tracker API pages, and cache/webhook design drafts. Useful background, but treat as scratch rather than spec.
