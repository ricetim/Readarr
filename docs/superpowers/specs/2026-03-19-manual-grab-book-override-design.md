# Manual Grab Book Override — Design Spec

**Date:** 2026-03-19
**Status:** Approved

---

## Problem

When a user manually selects a release from the search results and sends it to the download client, Readarr already knows which book the release is for. However, when the download completes, the import pipeline discards that knowledge and runs the full tag-reading + distance-scoring identification pipeline from scratch. This causes unnecessary mismatches on releases with poor tags, mangled filenames, or unusual metadata.

The user made an explicit human decision when grabbing — that decision should be trusted at import time.

---

## Solution

`ReleaseSource` is already set to `ReleaseSourceType.InteractiveSearch` on `RemoteBook` for manual grabs, and is already written to `EntityHistory.Data["ReleaseSource"]` by `HistoryService`. No new event plumbing is needed.

At import time: read `ReleaseSource` from history, promote the known `BookId` to a full `IdentificationOverrides.Book`, and bypass all import decision specs except `FreeSpaceSpecification`.

Automatic grabs (`ReleaseSourceType.Search`, `Rss`, etc.) are unchanged.

---

## Scope

- **One new parameter** on `IDownloadedBooksImportService.ProcessPath`: `Book bookOverride = null`
- **One new flag** in `ImportDecisionMakerConfig`: `BypassMatchingSpecs`
- **One new property** on `TrackedDownload`: `IsManualGrab`
- **No DB schema migration** — uses existing `Data["ReleaseSource"]` already written by `HistoryService`
- **No changes** to `BookGrabbedEvent`, `DownloadService`, or `HistoryService`

---

## Data Flow

```
GRAB TIME (no changes)
──────────────────────
HistoryService already writes Data["ReleaseSource"] = "InteractiveSearch"
for manual grabs (ReleaseSourceType.InteractiveSearch).

IMPORT TIME (when download completes)
──────────────────────────────────────
TrackedDownloadService.TrackDownload()
    → already looks up EntityHistory by DownloadId
    → new: reads Data["ReleaseSource"], sets IsManualGrab = true
      when value == "InteractiveSearch"

CompletedDownloadService.Import()
    → if IsManualGrab: look up BookId from history, fetch Book from DB
    → call ProcessPath(..., bookOverride: book)
    → if book lookup fails: call ProcessPath with no override (normal pipeline)

DownloadedBooksImportService.ProcessPath()
    → if bookOverride != null:
        build IdentificationOverrides { Author, Book = bookOverride }
        build ImportDecisionMakerConfig { BypassMatchingSpecs = true }

IdentificationService.IdentifyRelease()
    → if idOverrides.Book != null AND config.BypassMatchingSpecs:
        skip candidate generation and distance scoring
        directly populate localBookRelease with override book + monitored edition
        Distance = new Distance()  // 0.0
        return

ImportDecisionMaker.GetImportDecisions()
    → if config.BypassMatchingSpecs:
        _bookSpecifications: skip all
        _trackSpecifications: run only FreeSpaceSpecification
```

---

## Component Changes

### 1. `TrackedDownload.cs`
Add property:
```csharp
public bool IsManualGrab { get; set; }
```

### 2. `TrackedDownloadService.cs`
In the existing history lookup block, after retrieving `grabbedEvent`:
```csharp
trackedDownload.IsManualGrab =
    grabbedEvent?.Data?.GetValueOrDefault("ReleaseSource") == "InteractiveSearch";
```
Note: on process restart the cache is empty, so `TrackDownload` always re-reads history and re-sets this flag correctly.

### 3. `IDownloadedBooksImportService.cs` + `DownloadedBooksImportService.cs`
Add optional parameter to `ProcessPath`:
```csharp
List<ImportResult> ProcessPath(string path,
    ImportMode importMode = ImportMode.Auto,
    Author author = null,
    DownloadClientItem downloadClientItem = null,
    Book bookOverride = null);   // new
```
In the implementation, when `bookOverride != null`:
```csharp
var idOverrides = new IdentificationOverrides
{
    Author = author,
    Book   = bookOverride
};
// Pass BypassMatchingSpecs = true through to ImportDecisionMakerConfig
// built inside ProcessFolder / ProcessFile
```
Thread `bookOverride` through to the `GetImportDecisions()` call via the `Author`-accepting private overloads of `ProcessFolder` and `ProcessFile` only — these are the overloads that construct `IdentificationOverrides` and `ImportDecisionMakerConfig`. The no-`Author` private overloads (which parse author from folder/filename) are not changed, as `bookOverride` is always `null` when `author` is `null`.

### 4. `CompletedDownloadService.cs`
After the existing author/history reconstruction, add:
```csharp
Book bookOverride = null;
if (trackedDownload.IsManualGrab)
{
    var bookId = historyItems
        .Where(h => h.EventType == EntityHistoryEventType.Grabbed)
        .Select(h => h.BookId)
        .FirstOrDefault();
    bookOverride = bookId > 0 ? _bookService.GetBook(bookId) : null;
}

var importResults = _downloadedTracksImportService.ProcessPath(
    outputPath,
    ImportMode.Auto,
    trackedDownload.RemoteBook?.Author,
    trackedDownload.DownloadItem,
    bookOverride);   // new
```

### 5. `ImportDecisionMakerConfig.cs`
Add property:
```csharp
public bool BypassMatchingSpecs { get; set; }
```

### 6. `IdentificationService.cs` — bypass inside `IdentifyRelease()`
At the top of `IdentifyRelease()`, before `_candidateService.GetDbCandidatesFromTags()`:
```csharp
if (idOverrides?.Book != null && config.BypassMatchingSpecs)
{
    var edition = idOverrides.Book.Editions.Value
                      .FirstOrDefault(e => e.Monitored)
                  ?? idOverrides.Book.Editions.Value.First();

    localBookRelease.Edition  = edition;
    localBookRelease.Distance = new Distance();

    foreach (var localTrack in localBookRelease.LocalBooks)
    {
        localTrack.Edition = edition;
        localTrack.Book    = idOverrides.Book;
        localTrack.Author  = idOverrides.Author;
    }

    localBookRelease.PopulateMatch(config.KeepAllEditions);
    return;
}
```
`GetLocalBookReleases()` (track grouping + augmentation) still runs — only the candidate generation and distance scoring are skipped.

### 7. `ImportDecisionMaker.cs` — filter both spec pipelines
A new marker interface `IAlwaysRunSpec` (no members) is introduced. `FreeSpaceSpecification` implements it. The filtering uses this interface:

```csharp
// Book-level specs (LocalEdition): skip all when bypassing
// → handled by the GetDecision(LocalEdition) override returning an approved decision directly

// Track-level specs (LocalBook): run only IAlwaysRunSpec specs when bypassing
var specs = config.BypassMatchingSpecs
    ? _trackSpecifications.Where(s => s is IAlwaysRunSpec)
    : _trackSpecifications;
```
This skips `CloseBookMatchSpecification` and `AlreadyImportedSpecification` (both `LocalEdition` level) while still enforcing free space via `IAlwaysRunSpec`.

---

## Error Handling & Fallbacks

All failure scenarios degrade gracefully to the normal import pipeline:

| Scenario | Result |
|----------|--------|
| `Data["ReleaseSource"]` absent from history | `IsManualGrab = false` → normal pipeline |
| BookId in history but book deleted from DB | `GetBook()` returns null → `bookOverride = null` → normal pipeline |
| History lookup fails entirely (DownloadId mismatch) | `IsManualGrab = false` → normal pipeline |
| `Editions` collection empty on override book | Fall through to normal pipeline |

No new exceptions are introduced.

---

## Tests

### `TrackedDownloadServiceFixture` (extend existing)
- History with `ReleaseSource = "InteractiveSearch"` → `IsManualGrab = true`
- History with `ReleaseSource = "Search"` → `IsManualGrab = false`
- History with no `ReleaseSource` key → `IsManualGrab = false`

### `CompletedDownloadServiceFixture` (extend existing)
- `IsManualGrab = true`, valid BookId in history → `ProcessPath` called with `bookOverride` populated
- `IsManualGrab = true`, BookId not found in DB → `ProcessPath` called with `bookOverride = null`
- `IsManualGrab = false` → `ProcessPath` called with `bookOverride = null`

### `IdentificationServiceFixture` (extend existing)
- `idOverrides.Book != null` + `BypassMatchingSpecs = true` → candidate service never called, returned `LocalEdition.Book` equals override book, `Distance.NormalizedDistance() == 0.0`

### `ImportDecisionMakerFixture` (extend existing)
- `BypassMatchingSpecs = true` → `FreeSpaceSpecification` evaluated, `CloseBookMatchSpecification` and `AlreadyImportedSpecification` skipped

---

## Out of Scope

- Applying book-level override to automatic grabs (future work)
- Any UI changes — behavior is silent/internal
- Changing RSS grab handling
