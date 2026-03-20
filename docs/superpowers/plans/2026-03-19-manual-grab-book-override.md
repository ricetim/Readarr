# Manual Grab Book Override Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** When a user manually grabs a release, bypass the file-tag identification pipeline and import directly against the book they searched for, with only a free-space check.

**Architecture:** Four independent layers, implemented bottom-up: (1) add `BypassMatchingSpecs` flag + spec filtering to `ImportDecisionMaker`, (2) propagate `IsManualGrab` through `TrackedDownload`, (3) short-circuit `IdentificationService.IdentifyRelease()` when a Book override is present with bypass, (4) wire `CompletedDownloadService` to look up the book and pass it through `ProcessPath`. Each layer is independently testable.

**Tech Stack:** C# / NUnit / Moq / FizzWare.NBuilder

---

## Chunk 1: Spec Filtering + Identification Bypass

### Task 1: `BypassMatchingSpecs` flag and spec filtering in `ImportDecisionMaker`

**Files:**
- Modify: `src/NzbDrone.Core/MediaFiles/BookImport/ImportDecisionMaker.cs:37-44` (add flag to config)
- Modify: `src/NzbDrone.Core/MediaFiles/BookImport/ImportDecisionMaker.cs:197-215` (filter book specs)
- Modify: `src/NzbDrone.Core/MediaFiles/BookImport/ImportDecisionMaker.cs:229-245` (filter track specs)
- Create: `src/NzbDrone.Core/MediaFiles/BookImport/Specifications/IAlwaysRunSpec.cs`
- Modify: `src/NzbDrone.Core/MediaFiles/BookImport/Specifications/FreeSpaceSpecification.cs`
- Test: `src/NzbDrone.Core.Test/MediaFiles/TrackImport/ImportDecisionMakerFixture.cs`

**Context:**
- `ImportDecisionMakerConfig` is a plain class defined at line 37 of `ImportDecisionMaker.cs` (in the same file as the maker)
- `GetDecision(LocalEdition, ...)` at line ~197 uses `_bookSpecifications`
- `GetDecision(LocalBook, ...)` at line ~229 uses `_trackSpecifications`
- `FreeSpaceSpecification` is at `src/NzbDrone.Core/MediaFiles/BookImport/Specifications/FreeSpaceSpecification.cs`
- In the fixture, `_bookfail1` etc. are `Mock<IImportDecisionEngineSpecification<LocalEdition>>`, `_fail1` etc. are `Mock<IImportDecisionEngineSpecification<LocalBook>>`

- [ ] **Step 1: Write the failing tests**

Add these tests to `ImportDecisionMakerFixture`:

```csharp
[Test]
public void should_skip_book_specs_when_bypass_matching_specs_is_true()
{
    // Register book specs so we can verify they are NOT called
    GivenSpecifications(_bookpass1, _bookfail1, _bookfail2, _bookfail3);
    GivenSpecifications(_pass1);

    _idConfig.BypassMatchingSpecs = true;
    _idOverrides.Book = _book;

    Subject.GetImportDecisions(_fileInfos, _idOverrides, null, _idConfig);

    _bookfail1.Verify(c => c.IsSatisfiedBy(It.IsAny<LocalEdition>(), It.IsAny<DownloadClientItem>()), Times.Never());
    _bookfail2.Verify(c => c.IsSatisfiedBy(It.IsAny<LocalEdition>(), It.IsAny<DownloadClientItem>()), Times.Never());
    _bookfail3.Verify(c => c.IsSatisfiedBy(It.IsAny<LocalEdition>(), It.IsAny<DownloadClientItem>()), Times.Never());
}

[Test]
public void should_skip_non_always_run_track_specs_when_bypass_matching_specs_is_true()
{
    // Register track specs so we can verify non-always-run ones are NOT called
    GivenSpecifications(_bookpass1);
    GivenSpecifications(_pass1, _fail1, _fail2, _fail3);

    _idConfig.BypassMatchingSpecs = true;
    _idOverrides.Book = _book;

    Subject.GetImportDecisions(_fileInfos, _idOverrides, null, _idConfig);

    _fail1.Verify(c => c.IsSatisfiedBy(It.IsAny<LocalBook>(), It.IsAny<DownloadClientItem>()), Times.Never());
    _fail2.Verify(c => c.IsSatisfiedBy(It.IsAny<LocalBook>(), It.IsAny<DownloadClientItem>()), Times.Never());
    _fail3.Verify(c => c.IsSatisfiedBy(It.IsAny<LocalBook>(), It.IsAny<DownloadClientItem>()), Times.Never());
}

[Test]
public void should_run_always_run_track_specs_when_bypass_matching_specs_is_true()
{
    // _pass1 implements IAlwaysRunSpec, _pass2 does not
    _pass1.As<IAlwaysRunSpec>();
    GivenSpecifications(_bookpass1);
    GivenSpecifications(_pass1, _pass2);

    _idConfig.BypassMatchingSpecs = true;
    _idOverrides.Book = _book;

    Subject.GetImportDecisions(_fileInfos, _idOverrides, null, _idConfig);

    _pass1.Verify(c => c.IsSatisfiedBy(It.IsAny<LocalBook>(), It.IsAny<DownloadClientItem>()), Times.Once());
    _pass2.Verify(c => c.IsSatisfiedBy(It.IsAny<LocalBook>(), It.IsAny<DownloadClientItem>()), Times.Never());
}
```

- [ ] **Step 2: Build and confirm tests fail**

```bash
dotnet build src/Readarr.sln -p:Configuration=Debug -p:Platform=Posix --no-restore 2>&1 | tail -5
dotnet test src/NzbDrone.Core.Test/Readarr.Core.Test.csproj \
  --filter "FullyQualifiedName~ImportDecisionMakerFixture&FullyQualifiedName~bypass" \
  -p:Platform=Posix --no-build 2>&1 | tail -10
```

Expected: build succeeds, tests FAIL (flag doesn't exist yet).

- [ ] **Step 3: Create the `IAlwaysRunSpec` marker interface**

Create `src/NzbDrone.Core/MediaFiles/BookImport/Specifications/IAlwaysRunSpec.cs`:

```csharp
namespace NzbDrone.Core.MediaFiles.BookImport.Specifications
{
    /// <summary>
    /// Marker interface for import specs that run even when BypassMatchingSpecs is true.
    /// Only FreeSpaceSpecification should implement this.
    /// </summary>
    public interface IAlwaysRunSpec
    {
    }
}
```

- [ ] **Step 4: Add `BypassMatchingSpecs` to `ImportDecisionMakerConfig`**

In `ImportDecisionMaker.cs`, modify `ImportDecisionMakerConfig`:

```csharp
public class ImportDecisionMakerConfig
{
    public FilterFilesType Filter { get; set; }
    public bool NewDownload { get; set; }
    public bool SingleRelease { get; set; }
    public bool IncludeExisting { get; set; }
    public bool AddNewAuthors { get; set; }
    public bool KeepAllEditions { get; set; }
    public bool BypassMatchingSpecs { get; set; }
}
```

- [ ] **Step 5: Filter `_bookSpecifications` in `GetDecision(LocalEdition, ...)`**

Find the `GetDecision(LocalEdition localEdition, DownloadClientItem downloadClientItem)` method. It currently looks like:

```csharp
private ImportDecision<LocalEdition> GetDecision(LocalEdition localEdition, DownloadClientItem downloadClientItem)
{
    ...
    var reasons = _bookSpecifications.Select(c => EvaluateSpec(c, localEdition, downloadClientItem))
        .Where(c => c != null);
    ...
}
```

This method doesn't have access to `config` directly. The cleanest fix is to make `BypassMatchingSpecs` available to it via the existing `localEdition.NewDownload`-style pattern, or by checking whether the `localEdition.Edition` was set by an override. The simplest approach: **pass `config` into the method**. Change the signature:

Find both `GetDecision` private methods and add `ImportDecisionMakerConfig config` as a parameter. Update the callers in the main loop.

In `GetDecision(LocalEdition, DownloadClientItem, ImportDecisionMakerConfig)`:
```csharp
private ImportDecision<LocalEdition> GetDecision(LocalEdition localEdition, DownloadClientItem downloadClientItem, ImportDecisionMakerConfig config)
{
    ImportDecision<LocalEdition> decision = null;

    if (localEdition.Edition == null)
    {
        decision = new ImportDecision<LocalEdition>(localEdition, new Rejection($"Couldn't find similar book for {localEdition}"));
    }
    else if (config.BypassMatchingSpecs)
    {
        // All book-level specs bypassed — treat edition as approved
        decision = new ImportDecision<LocalEdition>(localEdition);
    }
    else
    {
        var reasons = _bookSpecifications.Select(c => EvaluateSpec(c, localEdition, downloadClientItem))
            .Where(c => c != null);
        decision = new ImportDecision<LocalEdition>(localEdition, reasons.ToArray());
    }
    // ... rest of logging unchanged
    return decision;
}
```

In `GetDecision(LocalBook, DownloadClientItem, ImportDecisionMakerConfig)`:
```csharp
private ImportDecision<LocalBook> GetDecision(LocalBook localBook, DownloadClientItem downloadClientItem, ImportDecisionMakerConfig config)
{
    ImportDecision<LocalBook> decision = null;

    if (localBook.Book == null)
    {
        decision = new ImportDecision<LocalBook>(localBook, new Rejection($"Couldn't parse book from: {localBook.FileTrackInfo}"));
    }
    else
    {
        var specs = config.BypassMatchingSpecs
            ? _trackSpecifications.Where(s => s is IAlwaysRunSpec)
            : _trackSpecifications;

        var reasons = specs.Select(c => EvaluateSpec(c, localBook, downloadClientItem))
            .Where(c => c != null);
        decision = new ImportDecision<LocalBook>(localBook, reasons.ToArray());
    }
    // ... rest of logging unchanged
    return decision;
}
```

Update the callers — find all calls to `GetDecision(release, ...)` and `GetDecision(localTrack, ...)` in `GetImportDecisions` and add `config` as the last argument.

- [ ] **Step 6: Mark `FreeSpaceSpecification` as `IAlwaysRunSpec`**

In `src/NzbDrone.Core/MediaFiles/BookImport/Specifications/FreeSpaceSpecification.cs`:

```csharp
public class FreeSpaceSpecification : IImportDecisionEngineSpecification<LocalBook>, IAlwaysRunSpec
```

(Add `, IAlwaysRunSpec` to the class declaration. No method changes needed — it's a marker.)

- [ ] **Step 7: Build and run the new tests**

```bash
dotnet build src/Readarr.sln -p:Configuration=Debug -p:Platform=Posix --no-restore 2>&1 | tail -5
dotnet test src/NzbDrone.Core.Test/Readarr.Core.Test.csproj \
  --filter "FullyQualifiedName~ImportDecisionMakerFixture&FullyQualifiedName~bypass" \
  -p:Platform=Posix --no-build 2>&1 | tail -10
```

Expected: `Passed! - Failed: 0, Passed: 3`

- [ ] **Step 8: Run full ImportDecisionMaker suite to confirm no regressions**

```bash
dotnet test src/NzbDrone.Core.Test/Readarr.Core.Test.csproj \
  --filter "FullyQualifiedName~ImportDecisionMakerFixture" \
  -p:Platform=Posix --no-build 2>&1 | tail -10
```

Expected: all pass.

- [ ] **Step 9: Commit**

```bash
git add src/NzbDrone.Core/MediaFiles/BookImport/ImportDecisionMaker.cs \
        src/NzbDrone.Core/MediaFiles/BookImport/Specifications/IAlwaysRunSpec.cs \
        src/NzbDrone.Core/MediaFiles/BookImport/Specifications/FreeSpaceSpecification.cs \
        src/NzbDrone.Core.Test/MediaFiles/TrackImport/ImportDecisionMakerFixture.cs
git commit -m "feat(import): add BypassMatchingSpecs flag to ImportDecisionMaker

When set, skips all book-level specs and all track-level specs except
those marked IAlwaysRunSpec. FreeSpaceSpecification implements IAlwaysRunSpec.

Co-Authored-By: Claude Sonnet 4.6 <noreply@anthropic.com>"
```

---

### Task 2: Bypass in `IdentificationService.IdentifyRelease()`

**Files:**
- Modify: `src/NzbDrone.Core/MediaFiles/BookImport/Identification/IdentificationService.cs:126`
- Create: `src/NzbDrone.Core.Test/MediaFiles/TrackImport/Identification/IdentificationServiceBypassFixture.cs`

**Context:**
- `IdentifyRelease(LocalEdition, IdentificationOverrides, ImportDecisionMakerConfig)` is the private method at line 126
- `_candidateService` is injected — mock it to throw if unexpectedly called in the test
- `GetLocalBookReleases()` skips `ITrackGroupingService` when `config.SingleRelease = true`, just creating a `new LocalEdition(localTracks)` — use this to keep the test simple
- `LocalEdition(List<LocalBook>)` constructor exists; populate `LocalBooks` via it
- `idOverrides.Book.Editions.Value` must have at least one edition with `Monitored = true`
- The bypass block does NOT call `PopulateMatch` (avoids NullReferenceException from null `ExistingTracks`); it assigns `localTrack.Edition/Book/Author` directly in a foreach — the test verifies `LocalBooks[0]` directly

- [ ] **Step 10: Write the failing test**

Create `src/NzbDrone.Core.Test/MediaFiles/TrackImport/Identification/IdentificationServiceBypassFixture.cs`:

```csharp
using System.Collections.Generic;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.MediaFiles.BookImport;
using NzbDrone.Core.MediaFiles.BookImport.Identification;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaFiles.TrackImport.Identification
{
    [TestFixture]
    public class IdentificationServiceBypassFixture : CoreTest<IdentificationService>
    {
        private Book _book;
        private Edition _edition;
        private Author _author;
        private LocalBook _localBook;

        [SetUp]
        public void SetUp()
        {
            _author = Builder<Author>.CreateNew().With(a => a.Id = 1).Build();

            _edition = Builder<Edition>.CreateNew()
                .With(e => e.Id = 1)
                .With(e => e.Monitored = true)
                .Build();

            _book = Builder<Book>.CreateNew()
                .With(b => b.Id = 1)
                .With(b => b.AuthorMetadataId = _author.AuthorMetadataId)
                .Build();

            _book.Editions = new NzbDrone.Core.Datastore.LazyLoaded<List<Edition>>(new List<Edition> { _edition });
            _edition.Book = new NzbDrone.Core.Datastore.LazyLoaded<Book>(_book);

            _localBook = Builder<LocalBook>.CreateNew()
                .With(l => l.Path = "/books/test.epub")
                .With(l => l.FileTrackInfo = new ParsedTrackInfo { BookTitle = "Some Book", Authors = new List<string> { "Some Author" } })
                .Build();

            // Augmenting service is auto-mocked (no-op) — fine
        }

        [Test]
        public void should_not_call_candidate_service_when_book_override_and_bypass_set()
        {
            var idOverrides = new IdentificationOverrides
            {
                Author = _author,
                Book = _book
            };

            var config = new ImportDecisionMakerConfig
            {
                BypassMatchingSpecs = true,
                SingleRelease = true  // skip TrackGroupingService
            };

            Subject.Identify(new List<LocalBook> { _localBook }, idOverrides, config);

            Mocker.GetMock<ICandidateService>()
                .Verify(c => c.GetDbCandidatesFromTags(It.IsAny<LocalEdition>(), It.IsAny<IdentificationOverrides>(), It.IsAny<bool>()), Times.Never());

            Mocker.GetMock<ICandidateService>()
                .Verify(c => c.GetRemoteCandidates(It.IsAny<LocalEdition>(), It.IsAny<IdentificationOverrides>()), Times.Never());
        }

        [Test]
        public void should_assign_override_book_and_edition_to_local_tracks_when_bypass_set()
        {
            var idOverrides = new IdentificationOverrides
            {
                Author = _author,
                Book = _book
            };

            var config = new ImportDecisionMakerConfig
            {
                BypassMatchingSpecs = true,
                SingleRelease = true
            };

            var results = Subject.Identify(new List<LocalBook> { _localBook }, idOverrides, config);

            results.Should().HaveCount(1);
            results[0].Edition.Should().Be(_edition);
            results[0].Distance.NormalizedDistance().Should().Be(0.0);
            // The bypass assigns directly to LocalBooks — verify the track was updated
            results[0].LocalBooks[0].Book.Should().Be(_book);
            results[0].LocalBooks[0].Author.Should().Be(_author);
            results[0].LocalBooks[0].Edition.Should().Be(_edition);
        }
    }
}
```

- [ ] **Step 11: Build and confirm tests fail**

```bash
dotnet build src/Readarr.sln -p:Configuration=Debug -p:Platform=Posix --no-restore 2>&1 | tail -5
dotnet test src/NzbDrone.Core.Test/Readarr.Core.Test.csproj \
  --filter "FullyQualifiedName~IdentificationServiceBypassFixture" \
  -p:Platform=Posix --no-build 2>&1 | tail -10
```

Expected: build succeeds, tests FAIL.

- [ ] **Step 12: Add the bypass to `IdentifyRelease()`**

In `src/NzbDrone.Core/MediaFiles/BookImport/Identification/IdentificationService.cs`, at the very top of the `IdentifyRelease` method body (line ~128, before `_candidateService.GetDbCandidatesFromTags`):

```csharp
private void IdentifyRelease(LocalEdition localBookRelease, IdentificationOverrides idOverrides, ImportDecisionMakerConfig config)
{
    // When a specific book override is active and specs are being bypassed (manual grab),
    // skip all candidate generation and scoring. Directly assign the override book.
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

        // Do NOT call PopulateMatch() — ExistingTracks is null for new downloads
        // and PopulateMatch would NullReferenceException trying to Concat it.
        return;
    }

    var watch = System.Diagnostics.Stopwatch.StartNew();
    // ... rest of existing method unchanged
```

- [ ] **Step 13: Build and run the new tests**

```bash
dotnet build src/Readarr.sln -p:Configuration=Debug -p:Platform=Posix --no-restore 2>&1 | tail -5
dotnet test src/NzbDrone.Core.Test/Readarr.Core.Test.csproj \
  --filter "FullyQualifiedName~IdentificationServiceBypassFixture" \
  -p:Platform=Posix --no-build 2>&1 | tail -10
```

Expected: `Passed! - Failed: 0, Passed: 2`

- [ ] **Step 14: Commit**

```bash
git add src/NzbDrone.Core/MediaFiles/BookImport/Identification/IdentificationService.cs \
        src/NzbDrone.Core.Test/MediaFiles/TrackImport/Identification/IdentificationServiceBypassFixture.cs
git commit -m "feat(import): bypass identification scoring when Book override + BypassMatchingSpecs set

Skips candidate generation and distance scoring entirely when a specific
book override is provided and BypassMatchingSpecs is true (manual grab).
Directly assigns the override book/edition to all local files.

Co-Authored-By: Claude Sonnet 4.6 <noreply@anthropic.com>"
```

---

## Chunk 2: TrackedDownload + Wire-up

### Task 3: `IsManualGrab` on `TrackedDownload`

**Files:**
- Modify: `src/NzbDrone.Core/Download/TrackedDownloads/TrackedDownload.cs`
- Modify: `src/NzbDrone.Core/Download/TrackedDownloads/TrackedDownloadService.cs`
- Test: `src/NzbDrone.Core.Test/Download/TrackedDownloads/TrackedDownloadServiceFixture.cs`

**Context:**
- `TrackedDownload` is defined in `TrackedDownload.cs` — just add a bool property
- In `TrackedDownloadService.TrackDownload()`, the `grabbedEvent` is already retrieved at line ~185:
  `var grabbedEvent = historyItems.FirstOrDefault(v => v.EventType == EntityHistoryEventType.Grabbed);`
- `Data["ReleaseSource"]` is already written by `HistoryService` — value is `"InteractiveSearch"` for manual grabs
- The `IsManualGrab` line should go immediately after the existing `trackedDownload.Indexer = grabbedEvent?.Data?.GetValueOrDefault("indexer");` line
- In the existing fixture, `GivenDownloadHistory()` creates `EntityHistory` entries — extend it to also populate `Data`

- [ ] **Step 15: Write the failing tests**

Add to `TrackedDownloadServiceFixture.cs`:

```csharp
private void GivenManualGrabHistory()
{
    Mocker.GetMock<IHistoryService>()
        .Setup(s => s.FindByDownloadId(It.Is<string>(sr => sr == "35238")))
        .Returns(new List<EntityHistory>
        {
            new EntityHistory
            {
                DownloadId   = "35238",
                SourceTitle  = "Audio Author - Audio Book [2018 - FLAC]",
                AuthorId     = 5,
                BookId       = 4,
                EventType    = EntityHistoryEventType.Grabbed,
                Data         = new Dictionary<string, string>
                {
                    { "ReleaseSource", "InteractiveSearch" }
                }
            }
        });
}

private void GivenAutomaticGrabHistory()
{
    Mocker.GetMock<IHistoryService>()
        .Setup(s => s.FindByDownloadId(It.Is<string>(sr => sr == "35238")))
        .Returns(new List<EntityHistory>
        {
            new EntityHistory
            {
                DownloadId   = "35238",
                SourceTitle  = "Audio Author - Audio Book [2018 - FLAC]",
                AuthorId     = 5,
                BookId       = 4,
                EventType    = EntityHistoryEventType.Grabbed,
                Data         = new Dictionary<string, string>
                {
                    { "ReleaseSource", "Search" }
                }
            }
        });
}

[Test]
public void should_set_is_manual_grab_true_when_release_source_is_interactive_search()
{
    GivenManualGrabHistory();

    var client = new DownloadClientDefinition { Id = 1, Protocol = DownloadProtocol.Torrent };
    var item   = new DownloadClientItem
    {
        Title      = "Audio Author - Audio Book [2018 - FLAC]",
        DownloadId = "35238",
        DownloadClientInfo = new DownloadClientItemClientInfo
        {
            Protocol = client.Protocol,
            Id       = client.Id,
            Name     = client.Name
        }
    };

    var trackedDownload = Subject.TrackDownload(client, item);

    trackedDownload.Should().NotBeNull();
    trackedDownload.IsManualGrab.Should().BeTrue();
}

[Test]
public void should_set_is_manual_grab_false_when_release_source_is_not_interactive_search()
{
    GivenAutomaticGrabHistory();

    var client = new DownloadClientDefinition { Id = 1, Protocol = DownloadProtocol.Torrent };
    var item   = new DownloadClientItem
    {
        Title      = "Audio Author - Audio Book [2018 - FLAC]",
        DownloadId = "35238",
        DownloadClientInfo = new DownloadClientItemClientInfo
        {
            Protocol = client.Protocol,
            Id       = client.Id,
            Name     = client.Name
        }
    };

    var trackedDownload = Subject.TrackDownload(client, item);

    trackedDownload.Should().NotBeNull();
    trackedDownload.IsManualGrab.Should().BeFalse();
}

[Test]
public void should_set_is_manual_grab_false_when_release_source_key_absent()
{
    // GivenDownloadHistory() sets up history with no Data dictionary
    GivenDownloadHistory();

    var client = new DownloadClientDefinition { Id = 1, Protocol = DownloadProtocol.Torrent };
    var item   = new DownloadClientItem
    {
        Title      = "The torrent release folder",
        DownloadId = "35238",
        DownloadClientInfo = new DownloadClientItemClientInfo
        {
            Protocol = client.Protocol,
            Id       = client.Id,
            Name     = client.Name
        }
    };

    var trackedDownload = Subject.TrackDownload(client, item);

    trackedDownload.Should().NotBeNull();
    trackedDownload.IsManualGrab.Should().BeFalse();
}
```

- [ ] **Step 16: Build and confirm tests fail**

```bash
dotnet build src/Readarr.sln -p:Configuration=Debug -p:Platform=Posix --no-restore 2>&1 | tail -5
dotnet test src/NzbDrone.Core.Test/Readarr.Core.Test.csproj \
  --filter "FullyQualifiedName~TrackedDownloadServiceFixture&FullyQualifiedName~manual_grab" \
  -p:Platform=Posix --no-build 2>&1 | tail -10
```

Expected: build succeeds, tests FAIL.

- [ ] **Step 17: Add `IsManualGrab` to `TrackedDownload`**

In `src/NzbDrone.Core/Download/TrackedDownloads/TrackedDownload.cs`, add after `IsTrackable`:

```csharp
public bool IsTrackable { get; set; }
public bool IsManualGrab { get; set; }
```

- [ ] **Step 18: Set `IsManualGrab` in `TrackedDownloadService.TrackDownload()`**

Find the line:
```csharp
trackedDownload.Indexer = grabbedEvent?.Data?.GetValueOrDefault("indexer");
```

Add immediately after it:
```csharp
trackedDownload.IsManualGrab =
    grabbedEvent?.Data?.GetValueOrDefault("ReleaseSource") == "InteractiveSearch";
```

- [ ] **Step 19: Build and run the new tests**

```bash
dotnet build src/Readarr.sln -p:Configuration=Debug -p:Platform=Posix --no-restore 2>&1 | tail -5
dotnet test src/NzbDrone.Core.Test/Readarr.Core.Test.csproj \
  --filter "FullyQualifiedName~TrackedDownloadServiceFixture" \
  -p:Platform=Posix --no-build 2>&1 | tail -10
```

Expected: all pass.

- [ ] **Step 20: Commit**

```bash
git add src/NzbDrone.Core/Download/TrackedDownloads/TrackedDownload.cs \
        src/NzbDrone.Core/Download/TrackedDownloads/TrackedDownloadService.cs \
        src/NzbDrone.Core.Test/Download/TrackedDownloads/TrackedDownloadServiceFixture.cs
git commit -m "feat(download): set IsManualGrab on TrackedDownload from history ReleaseSource

Reads the existing Data[\"ReleaseSource\"] field written at grab time by
HistoryService. InteractiveSearch grabs set IsManualGrab = true; all
others default to false.

Co-Authored-By: Claude Sonnet 4.6 <noreply@anthropic.com>"
```

---

### Task 4: Wire `bookOverride` through `ProcessPath` and `CompletedDownloadService`

**Files:**
- Modify: `src/NzbDrone.Core/MediaFiles/DownloadedBooksImportService.cs` (interface + implementation)
- Modify: `src/NzbDrone.Core/Download/CompletedDownloadService.cs`
- Test: `src/NzbDrone.Core.Test/Download/CompletedDownloadServiceTests/ProcessFixture.cs`

**Context:**

`IDownloadedBooksImportService` interface lives at the top of `DownloadedBooksImportService.cs` line 23:
```csharp
List<ImportResult> ProcessPath(string path, ImportMode importMode = ImportMode.Auto, Author author = null, DownloadClientItem downloadClientItem = null);
```

The public `ProcessPath` implementation at line 79 dispatches to private overloads based on whether path is dir or file, and whether `author` is null. Only the `author`-accepting private overloads (`ProcessFolder(IDirectoryInfo, ImportMode, Author, DownloadClientItem)` and `ProcessFile(IFileInfo, ImportMode, Author, DownloadClientItem)`) need to receive `bookOverride`. The no-author overloads always have `bookOverride = null` since manual grabs always have an author.

In those two private overloads, the `idOverrides` and `idConfig` objects are constructed locally (lines ~209-230 and ~295-310). Add `Book = bookOverride` to `idOverrides` and `BypassMatchingSpecs = bookOverride != null` to `idConfig`.

`CompletedDownloadService` currently does NOT inject `IBookService`. It needs to be added as a new constructor parameter. `CompletedDownloadService` constructor is at line 34. DryIoc picks up new constructor params automatically by type.

In `ProcessFixture.cs`, `CompletedDownloadService` is tested via `CoreTest<CompletedDownloadService>`. AutoMoq will auto-mock `IBookService` — set it up in the relevant tests.

History lookup for BookId: in `Import()`, the existing `_historyService` is already available. Use `_historyService.FindByDownloadId(trackedDownload.DownloadItem.DownloadId)` (already called in `TrackedDownloadService`) — but `CompletedDownloadService` doesn't call this directly. Use `_historyService.GetByEventType(EntityHistoryEventType.Grabbed, trackedDownload.DownloadItem.DownloadId)` if it exists, or `_historyService.FindByDownloadId(...)` — check what's available on `IHistoryService`.

Check: grep for available `IHistoryService` methods that return by DownloadId.

- [ ] **Step 21: Check available `IHistoryService` methods**

```bash
grep -n "FindByDownloadId\|GetByDownloadId\|interface IHistoryService" \
  src/NzbDrone.Core/History/HistoryService.cs | head -15
```

Use whichever method returns `List<EntityHistory>` by DownloadId (it's `FindByDownloadId`).

- [ ] **Step 22: Write the failing tests**

Add to `ProcessFixture.cs`:

```csharp
private void GivenManualGrab(int bookId)
{
    _trackedDownload.IsManualGrab = true;

    Mocker.GetMock<IHistoryService>()
        .Setup(s => s.FindByDownloadId(_trackedDownload.DownloadItem.DownloadId))
        .Returns(new List<EntityHistory>
        {
            new EntityHistory
            {
                EventType = EntityHistoryEventType.Grabbed,
                BookId    = bookId
            }
        });

    var book = Builder<Book>.CreateNew().With(b => b.Id = bookId).Build();

    Mocker.GetMock<IBookService>()
        .Setup(s => s.GetBook(bookId))
        .Returns(book);
}

[Test]
public void should_pass_book_override_to_process_path_for_manual_grab()
{
    GivenManualGrab(bookId: 42);

    Subject.Import(_trackedDownload);

    Mocker.GetMock<IDownloadedBooksImportService>()
        .Verify(s => s.ProcessPath(
            It.IsAny<string>(),
            It.IsAny<ImportMode>(),
            It.IsAny<Author>(),
            It.IsAny<DownloadClientItem>(),
            It.Is<Book>(b => b.Id == 42)),
        Times.Once());
}

[Test]
public void should_not_pass_book_override_to_process_path_for_automatic_grab()
{
    // _trackedDownload.IsManualGrab defaults to false

    Subject.Import(_trackedDownload);

    Mocker.GetMock<IDownloadedBooksImportService>()
        .Verify(s => s.ProcessPath(
            It.IsAny<string>(),
            It.IsAny<ImportMode>(),
            It.IsAny<Author>(),
            It.IsAny<DownloadClientItem>(),
            null),
        Times.Once());
}

[Test]
public void should_not_pass_book_override_when_book_not_found_in_db()
{
    _trackedDownload.IsManualGrab = true;

    Mocker.GetMock<IHistoryService>()
        .Setup(s => s.FindByDownloadId(_trackedDownload.DownloadItem.DownloadId))
        .Returns(new List<EntityHistory>
        {
            new EntityHistory { EventType = EntityHistoryEventType.Grabbed, BookId = 99 }
        });

    // Book 99 not in DB
    Mocker.GetMock<IBookService>()
        .Setup(s => s.GetBook(99))
        .Returns((Book)null);

    Subject.Import(_trackedDownload);

    Mocker.GetMock<IDownloadedBooksImportService>()
        .Verify(s => s.ProcessPath(
            It.IsAny<string>(),
            It.IsAny<ImportMode>(),
            It.IsAny<Author>(),
            It.IsAny<DownloadClientItem>(),
            null),
        Times.Once());
}
```

- [ ] **Step 23: Build and confirm tests fail**

```bash
dotnet build src/Readarr.sln -p:Configuration=Debug -p:Platform=Posix --no-restore 2>&1 | tail -5
dotnet test src/NzbDrone.Core.Test/Readarr.Core.Test.csproj \
  --filter "FullyQualifiedName~CompletedDownloadServiceTests.ProcessFixture&FullyQualifiedName~book_override" \
  -p:Platform=Posix --no-build 2>&1 | tail -10
```

Expected: build fails (interface not yet changed). That's expected at this stage.

- [ ] **Step 24: Add `Book bookOverride` to `IDownloadedBooksImportService` and implementation**

In `DownloadedBooksImportService.cs`, change the interface declaration:

```csharp
List<ImportResult> ProcessPath(string path,
    ImportMode importMode = ImportMode.Auto,
    Author author = null,
    DownloadClientItem downloadClientItem = null,
    Book bookOverride = null);
```

Change the public implementation signature to match.

In the public `ProcessPath` body, thread `bookOverride` to the `author`-accepting dispatches:
```csharp
// directory path — with author
return ProcessFolder(directoryInfo, importMode, author, downloadClientItem, bookOverride);

// file path — with author
return ProcessFile(fileInfo, importMode, author, downloadClientItem, bookOverride);
```

The no-author dispatches (`ProcessFolder(directoryInfo, importMode, downloadClientItem)` and `ProcessFile(fileInfo, importMode, downloadClientItem)`) are **not changed** — `bookOverride` is always null when author is null.

Add `Book bookOverride = null` parameter to the two author-accepting private overloads:

```csharp
private List<ImportResult> ProcessFolder(IDirectoryInfo directoryInfo, ImportMode importMode, Author author, DownloadClientItem downloadClientItem, Book bookOverride = null)
```

```csharp
private List<ImportResult> ProcessFile(IFileInfo fileInfo, ImportMode importMode, Author author, DownloadClientItem downloadClientItem, Book bookOverride = null)
```

In each, update the `idOverrides` and `idConfig` construction:

```csharp
var idOverrides = new IdentificationOverrides
{
    Author = author,
    Book   = bookOverride   // null for normal imports
};
var idConfig = new ImportDecisionMakerConfig
{
    Filter              = FilterFilesType.None,
    NewDownload         = true,
    SingleRelease       = false,
    IncludeExisting     = false,
    AddNewAuthors       = false,
    BypassMatchingSpecs = bookOverride != null   // true only for manual grabs
};
```

- [ ] **Step 25: Inject `IBookService` into `CompletedDownloadService`**

Add to the constructor in `CompletedDownloadService.cs`:

```csharp
public CompletedDownloadService(IEventAggregator eventAggregator,
                                IHistoryService historyService,
                                IProvideImportItemService provideImportItemService,
                                IDownloadedBooksImportService downloadedTracksImportService,
                                ITrackedDownloadAlreadyImported trackedDownloadAlreadyImported,
                                IBookService bookService,           // new
                                Logger logger)
```

Add field: `private readonly IBookService _bookService;`
Assign in constructor body: `_bookService = bookService;`

Add `using NzbDrone.Core.Books;` to the usings if not already present.

- [ ] **Step 26: Look up book and pass to `ProcessPath` in `Import()`**

In `Import()`, replace:
```csharp
var importResults = _downloadedTracksImportService.ProcessPath(outputPath, ImportMode.Auto, trackedDownload.RemoteBook?.Author, trackedDownload.DownloadItem);
```

With:
```csharp
Book bookOverride = null;
if (trackedDownload.IsManualGrab)
{
    var historyItems = _historyService.FindByDownloadId(trackedDownload.DownloadItem.DownloadId);
    var bookId = historyItems
        .Where(h => h.EventType == EntityHistoryEventType.Grabbed)
        .Select(h => h.BookId)
        .FirstOrDefault();

    if (bookId > 0)
    {
        bookOverride = _bookService.GetBook(bookId);
    }
}

var importResults = _downloadedTracksImportService.ProcessPath(
    outputPath,
    ImportMode.Auto,
    trackedDownload.RemoteBook?.Author,
    trackedDownload.DownloadItem,
    bookOverride);
```

Add `using NzbDrone.Core.History;` if not already present (for `EntityHistoryEventType`).

- [ ] **Step 27: Build and run all new tests**

```bash
dotnet build src/Readarr.sln -p:Configuration=Debug -p:Platform=Posix --no-restore 2>&1 | tail -5
dotnet test src/NzbDrone.Core.Test/Readarr.Core.Test.csproj \
  --filter "FullyQualifiedName~CompletedDownloadServiceTests.ProcessFixture" \
  -p:Platform=Posix --no-build 2>&1 | tail -10
```

Expected: all pass.

- [ ] **Step 28: Run full test suite for all changed areas**

```bash
dotnet test src/NzbDrone.Core.Test/Readarr.Core.Test.csproj \
  --filter "FullyQualifiedName~ImportDecisionMakerFixture|FullyQualifiedName~IdentificationServiceBypassFixture|FullyQualifiedName~TrackedDownloadServiceFixture|FullyQualifiedName~CompletedDownloadServiceTests" \
  -p:Platform=Posix --no-build 2>&1 | tail -15
```

Expected: all pass, 0 failures.

- [ ] **Step 29: Commit**

```bash
git add src/NzbDrone.Core/MediaFiles/DownloadedBooksImportService.cs \
        src/NzbDrone.Core/Download/CompletedDownloadService.cs \
        src/NzbDrone.Core.Test/Download/CompletedDownloadServiceTests/ProcessFixture.cs
git commit -m "feat(import): pass book override from manual grab through to import pipeline

When a download was manually grabbed (IsManualGrab = true), look up the
BookId from grab history, fetch the Book, and pass it as a bookOverride
to ProcessPath. DownloadedBooksImportService sets BypassMatchingSpecs on
the config when bookOverride is non-null, skipping all identification
scoring and most import specs.

Co-Authored-By: Claude Sonnet 4.6 <noreply@anthropic.com>"
```

---

## Chunk 3: Deploy

- [ ] **Step 30: Rebuild Docker image**

```bash
docker compose build 2>&1 | tail -5
```

Expected: `readarr  Built`

- [ ] **Step 31: Restart container**

```bash
docker stop readarr && docker rm readarr && docker compose up -d
sleep 5 && docker ps | grep readarr
```

Expected: container running.

- [ ] **Step 32: Verify in production logs**

Manually search for a book, grab a release, wait for import. Then check:

```bash
curl -s "http://192.168.0.250:8787/api/v1/log?page=1&pageSize=50&sortKey=time&sortDirection=descending" \
  -H "X-Api-Key: f294d4fef4ca475fae02645ad734fd6f" | python3 -c "
import sys, json
data = json.load(sys.stdin)
for r in data['records']:
    if any(x in r['message'] for x in ['bypass', 'BypassMatchingSpecs', 'Book accepted', 'Book rejected', 'Identifying book']):
        print(f\"{r['time']} {r['logger']}: {r['message']}\")
" | head -20
```

Expected: no "Identifying book" log entries for the manual grab (scoring was skipped), and "Book accepted" appears without a distance rejection.

- [ ] **Step 33: Push to GitHub**

```bash
git push origin develop
```
