# Scope Rescan to Author Path Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** When a new author is added or an existing author's metadata is refreshed, scope the resulting disk scan to that author's folder instead of scanning all root folders.

**Architecture:** Single method change in `RefreshAuthorService.Rescan()` — replace the `_rootFolderService.All()` call with a lookup of the specific author path(s) from `authorIds`, falling back to root folders only if no paths are available. A new test fixture validates both cases. `DiskScanService` is unchanged; it handles subfolder paths correctly.

**Tech Stack:** C# / NUnit / Moq / FizzWare.NBuilder

---

## Chunk 1: Tests + Implementation

### Task 1: Write the failing tests

**Files:**
- Create: `src/NzbDrone.Core.Test/Books/RefreshAuthorServiceFixture.cs`

**Context:**
- `RefreshAuthorService` has no existing test fixture — create a new one
- `RefreshSelectedAuthors` wraps per-author metadata work in `try/catch`, so `Rescan` at line 399 is always reached even when inner AutoMoq defaults cause failures
- `IManageCommandQueue` is `IManageCommandQueue` (field `_commandQueueManager`), defined in `CommandQueueManager.cs`
- `RefreshAuthorCommand` constructors: `RefreshAuthorCommand()` and `RefreshAuthorCommand(int? authorId, bool isNewAuthor = false)`

- [ ] **Step 1: Create the test fixture**

```csharp
using System.Collections.Generic;
using FizzWare.NBuilder;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Books.Commands;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaFiles.Commands;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Books
{
    [TestFixture]
    public class RefreshAuthorServiceFixture : CoreTest<RefreshAuthorService>
    {
        private Author _author;

        [SetUp]
        public void SetUp()
        {
            _author = Builder<Author>.CreateNew()
                .With(a => a.Id = 1)
                .With(a => a.Path = "/books/Terry Pratchett")
                .Build();

            Mocker.GetMock<IAuthorService>()
                .Setup(s => s.GetAuthors(It.IsAny<List<int>>()))
                .Returns(new List<Author> { _author });

            Mocker.GetMock<IRootFolderService>()
                .Setup(s => s.All())
                .Returns(new List<RootFolder>
                {
                    new RootFolder { Path = "/books" }
                });

            Mocker.GetMock<IConfigService>()
                .Setup(s => s.RescanAfterRefresh)
                .Returns(RescanAfterRefreshType.Always);
        }

        [Test]
        public void rescan_scopes_to_author_path_not_root_folder()
        {
            Subject.Execute(new RefreshAuthorCommand(1));

            Mocker.GetMock<IManageCommandQueue>()
                .Verify(
                    q => q.Push(
                        It.Is<RescanFoldersCommand>(c =>
                            c.Folders.Count == 1 &&
                            c.Folders[0] == "/books/Terry Pratchett"),
                        It.IsAny<CommandPriority>(),
                        It.IsAny<CommandTrigger>()),
                    Times.Once);

            Mocker.GetMock<IManageCommandQueue>()
                .Verify(
                    q => q.Push(
                        It.Is<RescanFoldersCommand>(c =>
                            c.Folders.Contains("/books")),
                        It.IsAny<CommandPriority>(),
                        It.IsAny<CommandTrigger>()),
                    Times.Never);
        }

        [Test]
        public void rescan_falls_back_to_root_folder_when_author_has_no_path()
        {
            // Author with empty path — e.g. not yet written to disk
            _author.Path = string.Empty;

            Subject.Execute(new RefreshAuthorCommand(1));

            Mocker.GetMock<IManageCommandQueue>()
                .Verify(
                    q => q.Push(
                        It.Is<RescanFoldersCommand>(c =>
                            c.Folders.Contains("/books")),
                        It.IsAny<CommandPriority>(),
                        It.IsAny<CommandTrigger>()),
                    Times.Once);
        }
    }
}
```

- [ ] **Step 2: Build and run the new tests — confirm they fail**

```bash
dotnet build src/Readarr.sln -p:Configuration=Debug -p:Platform=Posix --no-restore 2>&1 | tail -5
dotnet test src/NzbDrone.Core.Test/Readarr.Core.Test.csproj \
  --filter "FullyQualifiedName~RefreshAuthorServiceFixture" \
  -p:Platform=Posix --no-build 2>&1 | tail -15
```

Expected: build succeeds; both tests FAIL (rescan still uses root folder path `/books`).

---

### Task 2: Implement the fix

**Files:**
- Modify: `src/NzbDrone.Core/Books/Services/RefreshAuthorService.cs:332-339`

- [ ] **Step 3: Replace the root-folder scan with an author-path scan**

Find this block (around line 332):

```csharp
if (shouldRescan)
{
    // some metadata has updated so rescan unmatched
    // (but don't add new authors to reduce repeated searches against api)
    var folders = _rootFolderService.All().Select(x => x.Path).ToList();

    _commandQueueManager.Push(new RescanFoldersCommand(folders, FilterFilesType.Matched, false, authorIds));
}
```

Replace with:

```csharp
if (shouldRescan)
{
    // Scope the scan to the specific author path(s) to avoid re-reading the
    // entire library on every author add or refresh. Fall back to root folders
    // if no paths are available (e.g. author path not yet set).
    var authorPaths = (authorIds != null && authorIds.Any())
        ? _authorService.GetAuthors(authorIds)
            .Select(a => a.Path)
            .Where(p => !string.IsNullOrEmpty(p))
            .Distinct()
            .ToList()
        : new List<string>();

    var folders = authorPaths.Any()
        ? authorPaths
        : _rootFolderService.All().Select(x => x.Path).ToList();

    _commandQueueManager.Push(new RescanFoldersCommand(folders, FilterFilesType.Matched, false, authorIds));
}
```

- [ ] **Step 4: Build**

```bash
dotnet build src/Readarr.sln -p:Configuration=Debug -p:Platform=Posix --no-restore 2>&1 | tail -5
```

Expected: `Build succeeded.  0 Warning(s)  0 Error(s)`

- [ ] **Step 5: Run the new tests — confirm they pass**

```bash
dotnet test src/NzbDrone.Core.Test/Readarr.Core.Test.csproj \
  --filter "FullyQualifiedName~RefreshAuthorServiceFixture" \
  -p:Platform=Posix --no-build 2>&1 | tail -10
```

Expected: `Passed! - Failed: 0, Passed: 2`

- [ ] **Step 6: Run the full DiskScan suite to confirm no regressions**

```bash
dotnet test src/NzbDrone.Core.Test/Readarr.Core.Test.csproj \
  --filter "FullyQualifiedName~DiskScanService" \
  -p:Platform=Posix --no-build 2>&1 | tail -10
```

Expected: all pass.

- [ ] **Step 7: Commit**

```bash
git add src/NzbDrone.Core/Books/Services/RefreshAuthorService.cs \
        src/NzbDrone.Core.Test/Books/RefreshAuthorServiceFixture.cs
git commit -m "fix(scan): scope post-refresh rescan to author path, not all root folders

Previously, adding or refreshing any author triggered a full disk scan
of all root folders. With a large library this meant reading and
identifying hundreds of files on every author add. Now only the specific
author folder(s) are scanned; falls back to all root folders only when
no author paths are available.

Co-Authored-By: Claude Sonnet 4.6 <noreply@anthropic.com>"
```

---

## Chunk 2: Deploy

### Task 3: Build and deploy Docker

- [ ] **Step 8: Rebuild Docker image**

```bash
docker compose build 2>&1 | tail -5
```

Expected: `readarr  Built`

- [ ] **Step 9: Restart container**

```bash
docker stop readarr && docker rm readarr && docker compose up -d
sleep 5 && docker ps | grep readarr
```

Expected: container running.

- [ ] **Step 10: Verify fix in production logs**

Add a new author in the UI, then check:

```bash
curl -s "http://192.168.0.250:8787/api/v1/log?page=1&pageSize=50&sortKey=time&sortDirection=descending" \
  -H "X-Api-Key: f294d4fef4ca475fae02645ad734fd6f" | python3 -c "
import sys, json
data = json.load(sys.stdin)
for r in data['records']:
    if any(x in r['message'] for x in ['Scanning', 'Reading file', 'Identifying book', 'AddAuthor']):
        print(f\"{r['time']} {r['logger']}: {r['message']}\")
" | head -20
```

Expected: `DiskScanService: Scanning /books/Audiobooks/<AuthorName>/` (not `/books/Audiobooks/`), and `Reading file` count is small (just that author's files).

- [ ] **Step 11: Push to GitHub (triggers Docker Hub build)**

```bash
git push origin develop
```
