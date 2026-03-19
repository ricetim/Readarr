# Scope Rescan to Author Path — Design Spec

**Date:** 2026-03-19
**Status:** Approved

---

## Problem

When a new author is added (or an existing author's metadata is refreshed), `RefreshAuthorService.Rescan()` queues a `RescanFoldersCommand` targeting **all root folders**. With a large library this scans every file on disk — 685 files, 122 identification passes — for every single author add or refresh.

Root cause (line 336, `RefreshAuthorService.cs`):
```csharp
var folders = _rootFolderService.All().Select(x => x.Path).ToList();
```

---

## Solution

Replace the all-root-folders query with a lookup of the specific author path(s) being refreshed. The `authorIds` list is already available in scope and `_authorService` is already injected.

```csharp
List<string> folders;
if (authorIds != null && authorIds.Any())
{
    folders = _authorService.GetAuthors(authorIds)
        .Select(a => a.Path)
        .Where(p => !string.IsNullOrEmpty(p))
        .Distinct()
        .ToList();
}
else
{
    folders = _rootFolderService.All().Select(x => x.Path).ToList();
}
_commandQueueManager.Push(new RescanFoldersCommand(folders, FilterFilesType.Matched, false, authorIds));
```

The `else` branch preserves existing behaviour for the edge case where no author IDs are present (e.g., a bulk rescan initiated without specific IDs). The `DiskScanService.Scan()` method handles subfolder paths correctly — no changes needed there.

---

## Scope

- **Fixes both cases:** new author add (`isNew == true`) and metadata refresh (`isNew == false`)
- **One file changed:** `src/NzbDrone.Core/Books/Services/RefreshAuthorService.cs`
- **No new dependencies:** `_authorService` is already injected

---

## Tests

New test fixture: `src/NzbDrone.Core.Test/Books/RefreshAuthorServiceFixture.cs`

- `rescan_scopes_to_author_path_not_root_folder` — when `Rescan()` runs with a specific author ID, verifies `RescanFoldersCommand` is queued with `author.Path`, not root folder paths
- `rescan_falls_back_to_root_folders_when_no_author_ids` — when `authorIds` is empty, verifies root folder paths are used

---

## Out of Scope

- Changing the scheduled 24-hour full-library rescan (intentionally scans all root folders)
- Changing the filesystem watcher rescan (also intentionally broad)
- Changing `DiskScanService` internals
