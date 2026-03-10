# MyAnonamouse Native Indexer — Design

**Date:** 2026-03-10
**Status:** Approved

## Summary

Add a native MyAnonamouse (MAM) indexer to Readarr that queries the MAM JSON search API directly, without requiring a Torznab/Prowlarr intermediary. Users configure a single session cookie credential. The indexer supports active search (by title, author, ISBN) and RSS-style monitoring for new uploads.

## Architecture

Six files under `src/NzbDrone.Core/Indexers/MyAnonamouse/`, modeled on the existing Gazelle indexer:

```
MyAnonamouse.cs                  — HttpIndexerBase<MyAnonamouseSettings> subclass
MyAnonamouseRequestGenerator.cs  — IIndexerRequestGenerator: builds search + RSS requests
MyAnonamouseParser.cs            — IParseIndexerResponse: JSON → ReleaseInfo
MyAnonamouseSettings.cs          — ITorrentIndexerSettings with [FieldDefinition] UI annotations
MyAnonamouseInfo.cs              — Response DTOs and TorrentInfo subclass of ReleaseInfo
```

DryIoc discovers and registers the indexer automatically via naming convention — no registration code needed.

## Authentication

- **Mechanism:** Single `mam_id` session cookie, pasted by the user from their browser.
- **No login flow.** The cookie is attached as a request header on every API call.
- **Test override:** Fires a minimal search (empty text, `perpage=5`) and checks the response for `{"error": "..."}` — MAM returns this on auth failure rather than an HTTP error code.
- **Failure handling:** Standard `IndexerStatusService` backoff; auth errors surface in the UI like any other indexer failure.

## Request Generation

**Endpoint:** `POST https://www.myanonamouse.net/tor/js/loadSearchJSONbasic.php`
**Content-Type:** `application/json`
**Auth:** Cookie header `mam_id={value}`

### RSS Monitoring (`GetRecentRequests`)

```json
{
  "tor": {
    "main_cat": [13, 14],
    "sortType": "dateDesc",
    "searchType": "all",
    "startDate": "<last_rss_sync_timestamp | omit on first run>",
    "startNumber": 0
  },
  "perpage": 100
}
```

Uses `IndexerStatus.LastRssSyncReleaseInfo.PublishDate` as `startDate`. On first run, omits `startDate` to return the most recent 100 results.

### Book Search (`GetSearchRequests(BookSearchCriteria)`)

Two requests in the same `IndexerPageableRequestChain` tier (results are merged):

1. **Title + author text search:**
```json
{
  "tor": {
    "text": "<BookQuery>",
    "srchIn": ["title", "author"],
    "main_cat": [13, 14],
    "searchType": "<configurable, default: active>",
    "sortType": "default",
    "startNumber": 0
  },
  "perpage": 100
}
```

2. **ISBN search** (only when `BookIsbn` is non-empty):
```json
{
  "tor": {
    "main_cat": [13, 14],
    "searchType": "<configurable>",
    "sortType": "default",
    "startNumber": 0
  },
  "isbn": "<BookIsbn>",
  "perpage": 100
}
```

### Author Search (`GetSearchRequests(AuthorSearchCriteria)`)

```json
{
  "tor": {
    "text": "<AuthorQuery>",
    "srchIn": ["author"],
    "main_cat": [13, 14],
    "searchType": "<configurable>",
    "sortType": "default",
    "startNumber": 0
  },
  "perpage": 100
}
```

## Response Parsing

MAM response format:
```json
{ "data": [ { "id", "name", "author_info", "size", "added", "seeders", "leechers", "free", "fl_vip", "main_cat", "category", "catname", ... } ], "total": N }
```

Field mappings to `ReleaseInfo`:

| MAM field | ReleaseInfo field | Notes |
|---|---|---|
| `id` | `Guid` = `"MAM-{id}"` | |
| `name` | `Title` | As-is |
| `author_info` | `Author` | JSON dict `{"id": "name"}` — first value |
| `size` | `Size` | `string → long` |
| `added` | `PublishDate` | UTC |
| `seeders` | `Seeders` | |
| `seeders + leechers` | `Peers` | |
| `free` or `fl_vip` | `IndexerFlags.Freeleech` | |
| `id` | `DownloadUrl` = `/tor/download.php?tid={id}` | Cookie attached at grab time |
| `id` | `InfoUrl` = `https://www.myanonamouse.net/t/{id}` | |

**Error handling:** If response body contains `{"error": "..."}`, throw `IndexerException` with the message. If HTTP status is not 200, throw `IndexerException`.

## Settings

| # | Label | Type | Privacy | Default | Advanced |
|---|---|---|---|---|---|
| 0 | Session Cookie | Textbox | ApiKey | — | No |
| 1 | Search Type | Select (all/active/inactive) | — | active | Yes |
| 2 | Early Download Limit | Number (days) | — | — | Yes |
| 3 | Minimum Seeders | Number | — | 1 | Yes |
| 4 | Seed Criteria | SeedCriteriaSettings | — | — | — |
| 5 | Reject Blocklisted Torrent Hashes While Grabbing | Checkbox | — | false | Yes |

- `BaseUrl` is hardcoded to `https://www.myanonamouse.net` — no user-configurable URL.
- Validator: `Cookie` must be non-empty.
- HelpLink for Cookie field points to MAM privacy preferences page where users can find session info.

## Indexer Properties

```csharp
Name        => "MyAnonamouse"
Protocol    => DownloadProtocol.Torrent
SupportsRss => true
SupportsSearch => true
PageSize    => 100
```

## Out of Scope

- Category sub-filtering (e.g., filtering to only "Audiobooks - Fantasy") — can be added later
- Musicology (15) and Radio (16) categories
- Freeleech token usage
- Narrator/series search fields (can be added as advanced settings later)
- Pagination beyond first page (MAM returns up to 1000 per request; 100 is sufficient for most use cases)
