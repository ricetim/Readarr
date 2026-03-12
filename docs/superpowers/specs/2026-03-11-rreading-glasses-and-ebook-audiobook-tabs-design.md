# Design: rreading-glasses Integration & Ebooks/Audiobooks Tab Split

**Date:** 2026-03-11
**Status:** Approved

---

## Overview

Two related changes that ship together:

1. **rreading-glasses sidecar** — bundle the rreading-glasses metadata service directly into the Readarr Docker container, absorbing the existing Python sanitization proxy into `BookInfoProxy.cs`
2. **Ebooks / Audiobooks tab split** — replace the single "Books" tab on the author detail page with two tabs: Ebooks and Audiobooks, filtered by edition metadata

---

## Architecture

### Container layout

The Readarr container runs two processes:
- **rreading-glasses** (Go binary) — started by `entrypoint.sh` on `localhost:28202` before Readarr starts, cache DB at `/config/rreading-glasses/`
- **Readarr** (.NET) — connects to rreading-glasses at the hardcoded internal URL

The existing external Python bookinfo-proxy service is eliminated. Its sanitization logic moves into `BookInfoProxy.cs`.

### Data flow

```
Readarr (BookInfoProxy.cs)
  → http://localhost:28202  (rreading-glasses)
  → sanitize response in-process
  → return to Readarr core
```

### Frontend classification

Edition data (`Format`, `IsEbook`) already flows through the existing `/api/v1/books` API response. No backend API changes are needed. The frontend classifies books client-side.

---

## Section 1: rreading-glasses Sidecar

### Dockerfile changes

Add a stage that fetches the rreading-glasses binary from GitHub releases and copies it into the runtime image at `/app/bin/rreading-glasses`.

### entrypoint.sh changes

Before starting Readarr, start rreading-glasses in the background:

```sh
mkdir -p /config/rreading-glasses
/app/bin/rreading-glasses --data /config/rreading-glasses --port 28202 &
```

No readiness loop required — `BookInfoProxy.cs` already handles HTTP errors gracefully on startup.

### BookInfoProxy.cs changes

**1. Hardcode the metadata URL**
Remove the user-configurable BookInfo URL setting and its UI. Replace with the constant `http://localhost:28202`. Remove the `MetadataSource` settings class and any related UI settings.

**2. Sanitize author responses**
After deserializing any author/works response, filter out works where `Books` is null/empty or `Authors` is null/empty. This replicates the Python proxy's `valid_work()` logic:

```csharp
// Drop works with no books or no authors — mirrors bookinfo-proxy sanitization
works = works.Where(w =>
    w.Books != null && w.Books.Count > 0 &&
    w.Authors != null && w.Authors.Count > 0
).ToList();
```

Log the count of dropped works at debug level.

---

## Section 2: Ebooks / Audiobooks Tab Split

### Classification logic

A utility function determines which categories a book's editions belong to. A book can belong to both.

**Ebook** — edition where `IsEbook === true` OR `Format` matches (case-insensitive):
- `Kindle Edition`, `Nook`, `ebook`, `EPUB`, `PDF`

**Audiobook** — edition where `Format` matches (case-insensitive):
- `Audiobook`, `Audio CD`, `Audio Cassette`, `Audible Audio`, `CD-ROM`, `MP3 CD`

Books with no edition matching either category (e.g. paperback-only) are hidden from both tabs.

### Frontend changes

**`AuthorDetails.js`**
- Remove the "Books" tab
- Add "Ebooks" tab and "Audiobooks" tab in its place
- Tab badge counts show how many titles exist in each category for the author

**`AuthorDetailsSeason.js`** (or new wrapper component)
- Accept a `bookType` prop: `'ebook'` or `'audiobook'`
- Filter the book list through the classification function before rendering
- Pass filtered list to the existing book table renderer

### No backend API changes

The existing `/api/v1/books?authorId=X` endpoint already returns `Editions` with `Format` and `IsEbook` fields. No new endpoints or fields are needed.

---

## Out of Scope

- Rewriting the metadata engine (future work — replace rreading-glasses with a native .NET implementation)
- Indexer-availability filtering (tabs are metadata-driven only, not based on what's downloadable)
- Quality profile or monitoring changes

---

## Files to Change

| File | Change |
|------|--------|
| `Dockerfile` | Add rreading-glasses binary fetch/copy stage |
| `docker/entrypoint.sh` | Start rreading-glasses before Readarr |
| `src/NzbDrone.Core/MetadataSource/BookInfo/BookInfoProxy.cs` | Hardcode URL, add work sanitization |
| `src/NzbDrone.Core/MetadataSource/BookInfo/BookInfoSettings.cs` | Remove (or gut) configurable URL setting |
| `frontend/src/Author/Details/AuthorDetails.js` | Replace Books tab with Ebooks + Audiobooks tabs |
| `frontend/src/Author/Details/AuthorDetailsSeason.js` | Accept `bookType` prop, filter books |
| `frontend/src/Author/Details/bookTypeUtils.js` | New: classification utility function |
| `.gitignore` | Add `.superpowers/` |
