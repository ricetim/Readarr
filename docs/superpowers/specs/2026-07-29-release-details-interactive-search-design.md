# Release Details in Interactive Search — Design Spec

**Date:** 2026-07-29
**Status:** Implemented

## Overview

The interactive (manual) search dialog shows only the standard release columns: title, age, indexer, size, peers, quality, language, rejections. MyAnonamouse already returns considerably more per torrent — narrator, file count, series, tags, category, and a full description — and Readarr discards all of it.

This feature carries that data through to the search dialog, surfaced in an expandable row beneath each release.

## Goals

1. Populate narrator, file count, description, series, tags, and category from the MAM search response
2. Surface them in an expandable detail panel in the interactive search table
3. Add the model plumbing in a way that extends to other indexers later without rework

## Non-Goals

- **Bibliotik and all other indexers.** Bibliotik's parser scrapes the search results table, which carries only title/author/year/format/size/peers/date. Narrator and description live on the per-torrent detail page and would cost one extra HTTP request per release against a private tracker. Out of scope; the design leaves a clean extension point.
- Filtering or sorting on the new fields. They are display-only.
- Rendering BBCode as formatted markup (see Section 4).
- Any database migration. Nothing new is persisted.

---

## Section 1: Data Availability (why MAM-only)

Confirmed against MAM's API documentation (`.dev/Endpoint Torrent Search (JSON) _ My Anonamouse.htm`) and the current parser:

| Field | MAM status | Currently used? |
|---|---|---|
| `narrator_info` | In response by default | No — declared in DTO, never read |
| `numfiles` | In response by default | No — not even in the DTO |
| `series_info` | In response by default | No — declared, never read |
| `tags` | In response by default | No — declared, never read |
| `catname` | In response by default | No — declared, never read |
| `description` | **Opt-in request flag** | No |
| `language` | In response by default | Yes |
| `filetype` | In response by default | Yes |

`description` is a request-level flag, not per-release: setting it returns descriptions for every result in the response, or none. There is no way to fetch one release's description without a separate call. **Decision: always request it.** One call either way, no extra round trips, and expanding a row is instant.

## Section 2: Core Model

New file `src/NzbDrone.Core/Parser/Model/ReleaseDetails.cs`:

```csharp
public class ReleaseDetails
{
    public List<string> Narrators { get; set; }
    public int? FileCount { get; set; }
    public string Description { get; set; }
    public string Series { get; set; }
    public string Tags { get; set; }
    public string Category { get; set; }
}
```

One property added to `ReleaseInfo`:

```csharp
[JsonIgnore]
public ReleaseDetails Details { get; set; }
```

### Why a sub-object rather than flat properties

`ReleaseResourceMapper` already carries a standing complaint:

> `// TODO: Clean this mess up. don't mix data from multiple classes, use sub-resources instead?`

Adding six MAM-specific properties to the model every indexer shares is exactly what that TODO warns against. A single nullable sub-object keeps `ReleaseInfo` from sprouting fields that only one indexer populates, and gives Bibliotik somewhere to write later without another round of model surgery.

### Why `[JsonIgnore]`

`PendingRelease.Release` is a `ReleaseInfo` persisted as JSON into the `PendingReleases` table (`TableMapping.cs:194`). Without `[JsonIgnore]`, full BBCode descriptions would be written to the database for every delayed release, for no benefit.

`ReleaseInfo` already uses this exact pattern for `IndexerFlags` and `PendingReleaseReason`: the attribute excludes the field from database persistence, while `ReleaseResourceMapper` maps it to the API resource explicitly from the in-memory object during search. This is a pre-existing and admittedly confusing bit of the design, but it is the established convention here and it is what lets this feature ship without a migration.

**Accepted trade-off:** `Details` does not survive a grab-from-pending round trip. It is display-only data for the search dialog and nothing in the decision pipeline reads it.

### Why `Tags` is a string

MAM sends tags as a free-text blob: `"Love at Stake series unabridged 64–128 Kbps Fiction Paranormal Romance Fantasy Vampires Bestseller mp3 m4a"`. It is space-separated but contains multi-word tags with no delimiter. Splitting on whitespace would invent structure that isn't there and mangle "Paranormal Romance" into two tags. Display verbatim.

## Section 3: MAM Indexer Changes

### DTO (`MyAnonamouseInfo.cs`)

Add to `MyAnonamouseTorrent`:

```csharp
public string Numfiles { get; set; }
public string Description { get; set; }
```

`Narrator_Info`, `Series_Info`, `Tags`, and `Catname` are already declared. Property names must match the JSON keys — a prior bug in this indexer was caused by a `Name`/`title` mismatch silently yielding nulls.

### Request generator (`MyAnonamouseRequestGenerator.cs`)

Add the `description` flag as a top-level key alongside `tor` and `perpage`, in all three builders: `GetRecentRequest`, `GetBookSearchRequests`, `GetAuthorSearchRequest`.

### Parser (`MyAnonamouseParser.cs`)

Populate `Details` on each `MyAnonamouseInfo`.

`ParseAuthorInfo` generalizes to `ParseNamePairs(string) → List<string>`, since `narrator_info` has the identical double-encoded `{"id": "name"}` shape — a JSON object encoded as a string inside a JSON field. `author_info` continues to use it via `.FirstOrDefault()`. The existing swallow-on-failure behavior is retained; malformed data yields an empty list, never an exception.

`series_info` needs its own parser: its shape is `{"67": ["Love at Stake", "01-16, 13.5"]}` — the value is a *list*, not a string. Rendered as `Love at Stake (01-16, 13.5)`.

## Section 4: BBCode Handling

Descriptions arrive as tracker-controlled BBCode: `[size=4][b]…[/b][/size]`, `[url]`, `[img]`.

**Decision: strip to plain text server-side** in the parser, preserving line breaks and dropping `[img]` content entirely.

The alternative — shipping raw markup to the frontend and rendering it — requires either a new BBCode dependency or `dangerouslySetInnerHTML` applied to a field that private-tracker uploaders control. That is an XSS vector into an authenticated Readarr session, and formatted text in a search dialog does not justify it. Stripping keeps `ReleaseDetails.Description` a safe plain string end to end.

## Section 5: API Layer

- New `ReleaseDetailsResource` mirroring the model.
- `ReleaseResource` gains `public ReleaseDetailsResource Details { get; set; }`.
- `ToResource()` maps it when non-null.
- `ToModel()` ignores it — display-only, and the grab path does not need it.

## Section 6: Frontend

- `InteractiveSearch.js:13` — prepend an expander column to the module-level `columns` array.
- `InteractiveSearchRow.js` — add `isExpanded` state and a chevron cell. When expanded, render a second `<TableRow>` spanning all columns. **The chevron renders only when `details` is non-null**, so non-MAM releases are visually unchanged.
- New `ReleaseDetails.js` and `ReleaseDetails.css` in `frontend/src/InteractiveSearch/` — labelled grid, description line-clamped with a *more* toggle.
- New translation keys for the labels, following the pattern used by the existing `indexerFlags` and `language` columns.

Target layout:

```
 ▾  2d   Kerrelyn Sparks - Love at Stake   MAM   5.9GB  12/3  ⬇
┌──────────────────────────────────────────────────────────┐
│  Narrator   Abby Craden, Deanna Hurst, +4                │
│  Language   English          Files   149                 │
│  Series     Love at Stake (01-16, 13.5)                  │
│  Tags       unabridged · paranormal romance · 64-128kbps │
│  ──────────────────────────────────────────────────────  │
│  Welcome to the dangerous—and hilarious—world of modern  │
│  day vampires. There are those who…            [more ▾]  │
└──────────────────────────────────────────────────────────┘
 ▸  5d   Katherine Addison - The Goblin Emperor  MAM  1.2GB
```

## Section 7: Error Handling

| Condition | Behavior |
|---|---|
| `narrator_info` malformed or absent | `Narrators` empty; row renders without the narrator line |
| `numfiles` absent or non-numeric | `FileCount` null; line omitted |
| `description` absent (flag rejected) | `Description` null; panel shows metadata only |
| `series_info` malformed | `Series` null; line omitted |
| All fields absent | `Details` left null → **no chevron**, row identical to today |
| Non-MAM indexer | `Details` null → no chevron |

No partial failure may throw. A malformed field degrades to a missing line, never a failed search — consistent with the existing parser's swallow-and-continue posture.

## Section 8: Testing

- `MyAnonamouseFixture.cs` — extend `MyAnonamouse.json` with `narrator_info`, `numfiles`, `description`, `series_info`; assert `Details` fully populated.
- Null-safety case: a torrent with none of those fields yields `Details` fields null without throwing. This is where a naive `.First()` on narrator parsing would break.
- BBCode stripper unit tests: nested tags, `[img]` removal, line-break preservation.
- `MyAnonamouseRequestGeneratorFixture.cs` — assert the `description` flag is present in the request body.
- The existing 17 MAM tests must remain green.

## Open Risk

MAM's documentation shows the `description` flag as a bare key in form-encoded requests (`&description`), but does not specify the JSON equivalent — `""`, `true`, or presence-only. To be verified against the live API during implementation; fallback is to form-encode that parameter. Everything else in this spec is confirmed against the API documentation and current code.
