# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Status

Readarr has been **retired** as of early 2025. The project's metadata (Goodreads) became unusable. Development is no longer active, but the codebase remains functional for local/fork use.

## Architecture Overview

Readarr is an ebook/audiobook manager — a .NET 6 backend with a React/Redux frontend, modeled after Sonarr/Radarr.

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

**Data access:** Dapper ORM against SQLite (default) or PostgreSQL. Repositories inherit from `BasicRepository<TModel>`. Database migrations are sequential numbered files in `src/NzbDrone.Core/Datastore/Migration/`.

**Command/event bus:** `NzbDrone.Core.Messaging` — commands implement `IExecute<TCommand>`, events implement `IHandle<TEvent>`. This is the primary way services communicate.

**Metadata source:** Historically Goodreads (`src/NzbDrone.Core/MetadataSource/Goodreads/`). The `IProvideBookInfo` / `IProvideAuthorInfo` interfaces abstract the source.

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

## Code Style

**C#:** StyleCop enforced at build time. 4-space indentation, `using` directives outside namespace, `system` usings first. Warnings are treated as errors — the build will fail on style violations.

**Frontend JS:** ESLint with Prettier. 2-space indentation, single quotes, semicolons required. File names must match the exported name (`filenames/match-exported`). Import order is enforced (`simple-import-sort`).

**Frontend TypeScript:** Same ESLint config with `@typescript-eslint` rules added. Prettier is required for `.ts`/`.tsx` files.

## Key Patterns

**Adding a new API endpoint:** Create a controller in `src/Readarr.Api.V1/<Domain>/`, inherit from the appropriate base class. Mirror the resource model from `NzbDrone.Core`.

**Adding a command:** Create a `Command` subclass and an `IExecute<YourCommand>` implementation in `NzbDrone.Core`. DI picks it up automatically.

**Database schema changes:** Add a new migration file in `src/NzbDrone.Core/Datastore/Migration/` following the sequential numbering pattern (e.g., `042_your_change.cs`).

**LazyLoaded properties:** Many model properties use `LazyLoaded<T>` — these are populated on-demand by the repository layer and should not be assumed to be populated unless explicitly queried.
