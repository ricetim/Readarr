# Single-DB Metadata Pipeline Implementation Plan

> **For agentic workers:** REQUIRED: Use superpowers:subagent-driven-development (if subagents available) or superpowers:executing-plans to implement this plan. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Eliminate all 6 intermediate caches; make bookinfo stateless (no SQLite); store all persistent state (KCA, author/book metadata) exclusively in Readarr's `readarr.db`.

**Architecture:** bookinfo becomes a pure Goodreads HTTP adapter — it fetches data on demand, holds in-flight background results in a `_pending_complete` in-memory dict (single worker, TTL-cleaned), and returns them once via a one-time-pickup webhook flow. Readarr stores the KCA in `AuthorMetadata.Kca` (new column, migration 041), passes it as `?kca=` on every `/author/{id}` request, and reads from the direct `IHttpClient` (bypassing the `CachedHttpResponseService` and `LazyCache`).

**Tech Stack:** Python 3.11 / FastAPI / httpx / pytest-asyncio; C# / .NET 6 / NUnit / Dapper / SQLite; NLog

---

## Chunk 1: bookinfo — make `complete_author_background` return a dict + KCA extraction

### Task 1: Modify `goodreads.py` — background completion returns dict; KCA from GraphQL

**Files:**
- Modify: `bookinfo/goodreads.py`
- Modify: `bookinfo/tests/test_goodreads.py` (if it exists; create if not)

- [ ] **Step 1.1: Read current `complete_author_background` and `fetch_author_fast_path`**

Read `bookinfo/goodreads.py` and find:
1. `complete_author_background` — currently ends with `await cache_set_fn(updated, status="complete")`
2. `fetch_author_fast_path` — need to see where KCA is resolved from the GraphQL response
3. `_BOOK_FRAGMENT` — confirm `primaryContributorEdge { node { id legacyId name ... } }`

- [ ] **Step 1.2: Write failing test for `complete_author_background` returning dict**

In `bookinfo/tests/test_goodreads.py`:

```python
import pytest
from unittest.mock import AsyncMock, patch
from goodreads import GoodreadsClient

@pytest.mark.asyncio
async def test_complete_author_background_returns_dict():
    client = GoodreadsClient(rate=100, batch_size=20)
    partial = {"ForeignId": 3389, "Name": "C.S. Lewis", "Works": []}
    # Mock _paginate_works to return no additional works (first_page_next_token=None)
    with patch.object(client, '_paginate_works', new_callable=AsyncMock, return_value=[]):
        result = await client.complete_author_background(
            author_id=3389,
            partial_data=partial,
            kca="kca://author/amzn1.gr.author.v1.test",
            first_page_next_token=None,
            google_supplement_fn=AsyncMock(return_value=None),
        )
    assert isinstance(result, dict)
    assert result["ForeignId"] == 3389
    assert "Works" in result
```

Run: `cd bookinfo && python -m pytest tests/test_goodreads.py::test_complete_author_background_returns_dict -v`

Expected: FAIL — `complete_author_background` currently doesn't return a dict, calls `cache_set_fn`

- [ ] **Step 1.3: Change `complete_author_background` signature to return dict**

Old signature:
```python
async def complete_author_background(
    self, author_id, partial_data, kca, conn, cache_set_fn, first_page_next_token, google_supplement_fn
) -> None:
```

New signature (remove `conn` and `cache_set_fn`, add return type):
```python
async def complete_author_background(
    self, author_id, partial_data, kca, first_page_next_token, google_supplement_fn
) -> dict:
```

Replace the final lines that call `cache_set_fn` with `return updated`:

Old ending pattern (approximately):
```python
    await cache_set_fn(updated, status="complete")
```

New ending:
```python
    return updated
```

Also remove any `import cache as cache_module` and `await cache_module.set_edition_work_map(...)` calls from this method.

- [ ] **Step 1.4: Run test to verify it passes**

Run: `cd bookinfo && python -m pytest tests/test_goodreads.py::test_complete_author_background_returns_dict -v`
Expected: PASS

- [ ] **Step 1.5: Write failing test for KCA extraction from GraphQL contributor node**

```python
@pytest.mark.asyncio
async def test_fetch_author_fast_path_extracts_kca_from_graphql():
    """When author_kca is empty, kca should be extracted from contributor node.id in GraphQL response."""
    client = GoodreadsClient(rate=100, batch_size=20)
    # primaryContributorEdge.node.id IS the KCA (GRN format)
    mock_gql_result = [{
        "primaryContributorEdge": {
            "node": {
                "id": "kca://author/amzn1.gr.author.v1.AbCdEf",
                "legacyId": 3389,
                "name": "C.S. Lewis",
            }
        },
        "work": {"legacyId": 12345, "editions": {"edges": []}},
        "legacyId": 99999,
        "title": "The Lion, the Witch and the Wardrobe",
        "description": {"html": ""},
        "imageUrl": "",
        "webUrl": "",
        "stats": {"averageRating": 4.2, "ratingsCount": 1000},
        "primaryContributorEdge": {
            "role": "Author",
            "node": {
                "id": "kca://author/amzn1.gr.author.v1.AbCdEf",
                "legacyId": 3389,
                "name": "C.S. Lewis",
                "description": {"html": ""},
                "webUrl": "",
                "profileImageUrl": "",
                "followers": {"totalCount": 0},
            }
        },
        "secondaryContributorEdges": [],
        "bookSeries": [],
    }]
    with patch.object(client, 'batch_graphql', new_callable=AsyncMock, return_value=mock_gql_result):
        partial, next_token = await client.fetch_author_fast_path(
            author_id=3389,
            author_name="C.S. Lewis",
            author_kca="",  # empty — should be extracted from GraphQL
        )
    assert partial.get("Kca") == "kca://author/amzn1.gr.author.v1.AbCdEf"
```

Run: `cd bookinfo && python -m pytest tests/test_goodreads.py::test_fetch_author_fast_path_extracts_kca_from_graphql -v`
Expected: FAIL — `fetch_author_fast_path` doesn't currently return `Kca` in the partial dict

- [ ] **Step 1.6: Add KCA extraction to `fetch_author_fast_path`**

After the `batch_graphql` call inside `fetch_author_fast_path`, extract the KCA from the first result's `primaryContributorEdge.node.id` if `author_kca` is empty:

```python
# After: gql_results = await self.batch_graphql([...])
# Add:
if not author_kca and gql_results:
    first = gql_results[0]
    contrib_node = (first.get("primaryContributorEdge") or {}).get("node") or {}
    extracted_kca = contrib_node.get("id", "")
    if extracted_kca.startswith("kca://"):
        author_kca = extracted_kca

# Then in the partial dict being built, include:
partial["Kca"] = author_kca
```

- [ ] **Step 1.7: Verify `map_author` / partial dict building includes `Kca`**

The `partial` dict returned from `fetch_author_fast_path` needs a `"Kca"` key. Verify it's included in the dict construction. If `partial` is built via a helper (e.g., `map_author_from_gql`), add `"Kca": author_kca` to that helper's output.

- [ ] **Step 1.8: Run test to verify it passes**

Run: `cd bookinfo && python -m pytest tests/test_goodreads.py::test_fetch_author_fast_path_extracts_kca_from_graphql -v`
Expected: PASS

- [ ] **Step 1.9: Run all goodreads tests**

Run: `cd bookinfo && python -m pytest tests/test_goodreads.py -v`
Expected: All PASS

- [ ] **Step 1.10: Commit**

```bash
git add bookinfo/goodreads.py bookinfo/tests/test_goodreads.py
git commit -m "feat(bookinfo): complete_author_background returns dict; KCA extracted from GraphQL node.id"
```

---

## Chunk 2: bookinfo — stateless app.py

### Task 2: Rewrite `bookinfo/app.py` to be stateless (no SQLite, `_pending_complete` dict)

**Files:**
- Modify: `bookinfo/app.py`
- Modify: `bookinfo/tests/test_app.py`

- [ ] **Step 2.1: Read current `app.py` fully**

Read `bookinfo/app.py` and note every route, every `cache_module.*` call, every `_db_conn` usage.

- [ ] **Step 2.2: Read current `test_app.py` fully**

Read `bookinfo/tests/test_app.py` to understand the test harness before rewriting it.

- [ ] **Step 2.3: Write failing tests for new stateless `app.py`**

These tests go in `bookinfo/tests/test_app.py`. The new test harness patches `GoodreadsClient` only — no SQLite.

```python
import asyncio
import pytest
import pytest_asyncio
from unittest.mock import AsyncMock, MagicMock, patch
from httpx import AsyncClient, ASGITransport


MOCK_PARTIAL = {
    "ForeignId": 3389,
    "Name": "C.S. Lewis",
    "Kca": "kca://author/amzn1.gr.author.v1.Test",
    "Works": [{"ForeignId": 111, "Books": [], "Title": "Narnia"}],
    "Series": [],
}
MOCK_COMPLETE = {**MOCK_PARTIAL, "Works": MOCK_PARTIAL["Works"] + [{"ForeignId": 222, "Books": [], "Title": "Perelandra"}]}


@pytest_asyncio.fixture
async def app_and_client():
    mock_gr = MagicMock()
    mock_gr.resolve_author_xml = AsyncMock(
        return_value=("kca://author/amzn1.gr.author.v1.Test", "C.S. Lewis", "", "")
    )
    mock_gr.fetch_author_fast_path = AsyncMock(return_value=(MOCK_PARTIAL, "next_token_abc"))
    mock_gr.complete_author_background = AsyncMock(return_value=MOCK_COMPLETE)
    mock_gr.batch_graphql = AsyncMock(return_value=[])
    mock_gr.search = AsyncMock(return_value=[])
    mock_gr.get_series = AsyncMock(return_value=None)
    mock_gr.close = AsyncMock()

    with patch("app.GoodreadsClient", return_value=mock_gr):
        from app import app
        transport = ASGITransport(app=app)
        async with AsyncClient(transport=transport, base_url="http://test") as client:
            yield client, mock_gr

    # Reset module state between tests
    import app as app_module
    app_module._pending_complete.clear()


@pytest.mark.asyncio
async def test_get_author_returns_partial_immediately(app_and_client):
    client, mock_gr = app_and_client
    resp = await client.get("/author/3389")
    assert resp.status_code == 200
    data = resp.json()
    assert data["ForeignId"] == 3389
    assert data["Kca"] == "kca://author/amzn1.gr.author.v1.Test"


@pytest.mark.asyncio
async def test_get_author_passes_kca_query_param(app_and_client):
    client, mock_gr = app_and_client
    resp = await client.get("/author/3389?kca=kca://author/amzn1.gr.author.v1.FromReadarr")
    assert resp.status_code == 200
    # fetch_author_fast_path should have been called with the provided KCA
    mock_gr.fetch_author_fast_path.assert_called_once()
    call_kwargs = mock_gr.fetch_author_fast_path.call_args.kwargs
    assert call_kwargs.get("author_kca") == "kca://author/amzn1.gr.author.v1.FromReadarr"


@pytest.mark.asyncio
async def test_get_author_skips_xml_when_kca_provided(app_and_client):
    client, mock_gr = app_and_client
    resp = await client.get("/author/3389?kca=kca://author/amzn1.gr.author.v1.Known")
    assert resp.status_code == 200
    mock_gr.resolve_author_xml.assert_not_called()


@pytest.mark.asyncio
async def test_get_author_complete_available_via_pending(app_and_client):
    """If _pending_complete has an entry, GET /author/{id} returns the complete data."""
    client, mock_gr = app_and_client
    import app as app_module
    app_module._pending_complete[3389] = (MOCK_COMPLETE, asyncio.get_event_loop().time() + 7200)
    resp = await client.get("/author/3389")
    assert resp.status_code == 200
    data = resp.json()
    assert len(data["Works"]) == 2  # complete has 2 works
    # Should be popped after pickup
    assert 3389 not in app_module._pending_complete


@pytest.mark.asyncio
async def test_author_changed_returns_limited(app_and_client):
    client, _ = app_and_client
    resp = await client.get("/author/changed")
    assert resp.status_code == 200
    assert resp.json() == {"Limited": True, "Ids": []}


@pytest.mark.asyncio
async def test_work_returns_404(app_and_client):
    client, _ = app_and_client
    resp = await client.get("/work/12345")
    assert resp.status_code == 404


@pytest.mark.asyncio
async def test_recommended_returns_empty(app_and_client):
    client, _ = app_and_client
    resp = await client.get("/recommended")
    assert resp.status_code == 200
    assert resp.json() == {"WorkIds": []}
```

Run: `cd bookinfo && python -m pytest tests/test_app.py -v`
Expected: Several FAIL — routes don't exist yet in new form / `_pending_complete` not present

- [ ] **Step 2.4: Rewrite `app.py` to be stateless**

Complete replacement of `app.py`. Key changes:
1. Remove all `aiosqlite`, `cache as cache_module` imports
2. Remove `_db_conn` global; add `_pending_complete: dict[int, tuple[dict, float]] = {}`
3. Remove `DB_PATH` env var; keep `LOG_DIR`, `LOG_KEEP`, `READARR_URL`, `READARR_API_KEY`, `GR_RATE`, `BATCH_SIZE`
4. Simplify `lifespan`: no DB init, no scheduler
5. Add `_cleanup_pending_complete()` background helper (TTL eviction)
6. Rewrite `GET /author/{author_id}` — accept `?kca=` query param, check `_pending_complete` first, then fast-path
7. `GET /work/{work_id}` → always returns 404
8. `GET /book/{edition_id}` → uses `batch_graphql` to find author_id, redirects to `/author/{author_id}`
9. Remove `GET /book/bulk`, `POST /book/bulk`, `DELETE /cache/author/{id}`

New `app.py`:

```python
from __future__ import annotations

import asyncio
import glob
import logging
import os
import time
from contextlib import asynccontextmanager
from datetime import datetime
from typing import Optional

import httpx
from fastapi import BackgroundTasks, FastAPI, HTTPException, Query
from fastapi.responses import RedirectResponse, Response

import google_books as gb_module
from goodreads import GoodreadsClient

logger = logging.getLogger(__name__)

LOG_DIR = os.getenv("BOOKINFO_LOG_DIR", "/logs")
LOG_KEEP = int(os.getenv("BOOKINFO_LOG_KEEP", "10"))
GR_RATE = float(os.getenv("BOOKINFO_GR_RATE", "3"))
BATCH_SIZE = int(os.getenv("BOOKINFO_BATCH_SIZE", "20"))
READARR_URL = os.getenv("READARR_URL", "").rstrip("/")
READARR_API_KEY = os.getenv("READARR_API_KEY", "")
PENDING_TTL = 2 * 3600  # seconds — how long to keep completed data before giving up

_pending_complete: dict[int, tuple[dict, float]] = {}
goodreads_client: Optional[GoodreadsClient] = None


def _author_ttl_deadline() -> float:
    return time.monotonic() + PENDING_TTL


def _setup_file_logging() -> None:
    try:
        os.makedirs(LOG_DIR, exist_ok=True)
    except OSError:
        logger.warning("Could not create log directory %s, file logging disabled", LOG_DIR)
        return
    timestamp = datetime.utcnow().strftime("%Y-%m-%dT%H-%M-%S")
    log_path = os.path.join(LOG_DIR, f"bookinfo-{timestamp}.log")
    handler = logging.FileHandler(log_path, encoding="utf-8")
    handler.setFormatter(logging.Formatter(
        "%(asctime)s|%(levelname)s|%(name)s|%(message)s",
        datefmt="%Y-%m-%d %H:%M:%S",
    ))
    logging.getLogger().addHandler(handler)
    logger.info("Logging to %s", log_path)
    pattern = os.path.join(LOG_DIR, "bookinfo-*.log")
    old_files = sorted(glob.glob(pattern))
    for old in old_files[:-LOG_KEEP]:
        try:
            os.remove(old)
        except OSError:
            pass


async def _notify_readarr(author_id: int) -> None:
    if not READARR_URL or not READARR_API_KEY:
        return
    payload = {"name": "RefreshAuthor", "foreignAuthorId": str(author_id)}
    headers = {"X-Api-Key": READARR_API_KEY, "Content-Type": "application/json"}
    delays = [5, 30, 300]
    async with httpx.AsyncClient() as client:
        for attempt, delay in enumerate(delays, 1):
            try:
                r = await client.post(
                    f"{READARR_URL}/api/v1/command",
                    json=payload,
                    headers=headers,
                    timeout=10,
                )
                if r.status_code < 500:
                    logger.debug("Notified Readarr to refresh author %d (HTTP %d)", author_id, r.status_code)
                    return
                logger.warning("Readarr returned %d for author %d (attempt %d/%d)", r.status_code, author_id, attempt, len(delays))
            except Exception as exc:
                logger.warning("Failed to notify Readarr for author %d (attempt %d/%d): %s", author_id, attempt, len(delays), exc)
            if attempt < len(delays):
                await asyncio.sleep(delay)
    logger.warning("Gave up notifying Readarr for author %d after %d attempts", author_id, len(delays))


@asynccontextmanager
async def lifespan(app: FastAPI):
    _setup_file_logging()
    global goodreads_client
    goodreads_client = GoodreadsClient(rate=GR_RATE, batch_size=BATCH_SIZE)
    if READARR_URL and READARR_API_KEY:
        logger.info("Readarr webhook enabled: %s", READARR_URL)
    else:
        logger.info("Readarr webhook disabled (READARR_URL/READARR_API_KEY not set)")
    try:
        yield
    finally:
        await goodreads_client.close()


app = FastAPI(lifespan=lifespan)


# ---------------------------------------------------------------------------
# Routes
# ---------------------------------------------------------------------------

@app.get("/author/changed")
async def author_changed():
    return {"Limited": True, "Ids": []}


@app.get("/author/{author_id}")
async def get_author(author_id: int, kca: str = Query(default=""), background_tasks: BackgroundTasks = None):
    # One-time pickup: if background has already completed, return the complete data
    if author_id in _pending_complete:
        complete_data, _deadline = _pending_complete.pop(author_id)
        return complete_data

    # Resolve KCA if not provided
    author_name = ""
    author_image_url = ""
    author_description = ""
    if not kca:
        kca, author_name, author_image_url, author_description = (
            await goodreads_client.resolve_author_xml(author_id)
        )

    partial, first_page_next_token = await goodreads_client.fetch_author_fast_path(
        author_id=author_id,
        author_name=author_name,
        author_kca=kca,
        author_image_url=author_image_url,
        author_description=author_description,
    )

    # Extract name from GraphQL contributor data if still empty
    if not partial.get("Name"):
        for work in partial.get("Works", []):
            for book in work.get("Books", []):
                for contrib in book.get("Contributors", []):
                    if contrib.get("ForeignId") == author_id and contrib.get("Name"):
                        partial["Name"] = contrib["Name"]
                        break
                if partial.get("Name"):
                    break
            if partial.get("Name"):
                break

    async def _complete_and_store():
        try:
            complete = await goodreads_client.complete_author_background(
                author_id=author_id,
                partial_data=partial,
                kca=kca,
                first_page_next_token=first_page_next_token,
                google_supplement_fn=gb_module.supplement_ebook_edition,
            )
            _pending_complete[author_id] = (complete, _author_ttl_deadline())
            # Evict stale entries
            now = time.monotonic()
            stale = [k for k, (_, deadline) in _pending_complete.items() if deadline < now]
            for k in stale:
                del _pending_complete[k]
        except Exception as exc:
            logger.warning("Background completion failed for author %d: %s", author_id, exc)
            return
        await _notify_readarr(author_id)

    background_tasks.add_task(_complete_and_store)
    return partial


@app.get("/work/{work_id}")
async def get_work(work_id: int):
    raise HTTPException(status_code=404, detail="Work not found")


@app.get("/book/{edition_id}")
async def get_book(edition_id: int):
    book_results = await goodreads_client.batch_graphql([edition_id])
    if book_results:
        contrib_edge = book_results[0].get("primaryContributorEdge") or {}
        author_id = (contrib_edge.get("node") or {}).get("legacyId")
        if author_id:
            return RedirectResponse(url=f"/author/{author_id}", status_code=302)
    raise HTTPException(status_code=404, detail="Edition not found")


@app.get("/series/{series_id}")
async def get_series(series_id: int):
    series = await goodreads_client.get_series(series_id)
    if not series:
        raise HTTPException(status_code=404, detail="Series not found")
    return series


@app.get("/search")
async def search(q: str):
    try:
        return await goodreads_client.search(q)
    except Exception:
        logger.warning("Search failed for %r", q)
        return []


@app.get("/recommended")
async def recommended():
    return {"WorkIds": []}
```

- [ ] **Step 2.5: Run the new tests**

Run: `cd bookinfo && python -m pytest tests/test_app.py -v`
Expected: All PASS

- [ ] **Step 2.6: Run all bookinfo tests**

Run: `cd bookinfo && python -m pytest -v`
Expected: All PASS (some test_cache.py tests may fail — will be deleted in Task 3)

- [ ] **Step 2.7: Commit**

```bash
git add bookinfo/app.py bookinfo/tests/test_app.py
git commit -m "feat(bookinfo): rewrite app.py as stateless — _pending_complete replaces SQLite cache"
```

---

## Chunk 3: bookinfo — delete SQLite layer

### Task 3: Delete `cache.py`, `test_cache.py`; remove `aiosqlite`; add `--workers 1`

**Files:**
- Delete: `bookinfo/cache.py`
- Delete: `bookinfo/tests/test_cache.py`
- Modify: `bookinfo/requirements.txt`
- Modify: `bookinfo/Dockerfile`

- [ ] **Step 3.1: Read `bookinfo/requirements.txt` and `bookinfo/Dockerfile`**

Verify:
- `requirements.txt` has `aiosqlite` entry
- `Dockerfile` CMD doesn't have `--workers 1`

- [ ] **Step 3.2: Write test to confirm `aiosqlite` is not imported in app.py**

This is a static check — just verify the import was removed in Step 2.4. Search `app.py`:

```bash
grep -n "aiosqlite\|cache_module\|cache as" bookinfo/app.py
```

Expected: no matches

- [ ] **Step 3.3: Delete `cache.py`**

```bash
rm bookinfo/cache.py
```

- [ ] **Step 3.4: Delete `test_cache.py`**

```bash
rm bookinfo/tests/test_cache.py
```

- [ ] **Step 3.5: Remove `aiosqlite` from `requirements.txt`**

Edit `bookinfo/requirements.txt` — delete the `aiosqlite==...` line.

- [ ] **Step 3.6: Add `--workers 1` to `bookinfo/Dockerfile` CMD**

Find the CMD line:
```
CMD ["uvicorn", "app:app", "--host", "0.0.0.0", "--port", "28202"]
```

Change to:
```
CMD ["uvicorn", "app:app", "--host", "0.0.0.0", "--port", "28202", "--workers", "1"]
```

This is critical: `_pending_complete` is per-process; multiple workers would lose completions.

- [ ] **Step 3.7: Run all bookinfo tests**

Run: `cd bookinfo && python -m pytest -v`
Expected: All PASS; `test_cache.py` tests no longer present

- [ ] **Step 3.8: Commit**

```bash
git add bookinfo/requirements.txt bookinfo/Dockerfile
git rm bookinfo/cache.py bookinfo/tests/test_cache.py
git commit -m "chore(bookinfo): remove SQLite layer — delete cache.py, aiosqlite, test_cache.py; add --workers 1"
```

---

## Chunk 4: Readarr — `AuthorMetadata.Kca` + migration 041

### Task 4: Add `Kca` column to `AuthorMetadata`; create migration 041

**Files:**
- Modify: `src/NzbDrone.Core/Books/Model/AuthorMetadata.cs`
- Modify: `src/NzbDrone.Core/MetadataSource/BookInfo/BookInfoResource/AuthorResource.cs`
- Create: `src/NzbDrone.Core/Datastore/Migration/041_add_kca_to_author_metadata.cs`

- [ ] **Step 4.1: Read `AuthorMetadata.cs` and `AuthorResource.cs`**

Find:
- `UseMetadataFrom()` method in `AuthorMetadata.cs`
- All properties in `AuthorResource.cs`

- [ ] **Step 4.2: Write failing build test (schema check)**

Add `Kca` property to `AuthorMetadata` model and verify the build compiles. The "test" here is:

```bash
dotnet build src/Readarr.sln -p:Configuration=Debug -p:Platform=Posix --no-restore 2>&1 | grep -E "error|warning" | head -20
```

But first, actually add the properties.

- [ ] **Step 4.3: Add `Kca` to `AuthorMetadata.cs`**

Add property:
```csharp
public string Kca { get; set; }
```

In `UseMetadataFrom()`, add (preserve existing Kca if new value is empty):
```csharp
Kca = other.Kca.IsNullOrWhiteSpace() ? Kca : other.Kca;
```

- [ ] **Step 4.4: Add `Kca` to `AuthorResource.cs`**

Add property:
```csharp
public string Kca { get; set; }
```

- [ ] **Step 4.5: Create migration `041_add_kca_to_author_metadata.cs`**

Read the most recent migration file (040) to confirm the pattern, then create:

```csharp
using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(041)]
    public class add_kca_to_author_metadata : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("AuthorMetadata").AddColumn("Kca").AsString().Nullable();
            Delete.FromTable("HttpResponse").AllRows();
        }
    }
}
```

Note: `Delete.FromTable("HttpResponse").AllRows()` purges the old HTTP response cache entries. This forces Readarr to re-fetch all author data from bookinfo on next refresh, which is correct — the old cache is stale and useless in the new design.

- [ ] **Step 4.6: Build the solution**

Run:
```bash
dotnet build src/Readarr.sln -p:Configuration=Debug -p:Platform=Posix --no-restore
```

Expected: Build succeeds with 0 errors

- [ ] **Step 4.7: Commit**

```bash
git add src/NzbDrone.Core/Books/Model/AuthorMetadata.cs
git add src/NzbDrone.Core/MetadataSource/BookInfo/BookInfoResource/AuthorResource.cs
git add src/NzbDrone.Core/Datastore/Migration/041_add_kca_to_author_metadata.cs
git commit -m "feat: add Kca column to AuthorMetadata (migration 041); clear HttpResponse cache on upgrade"
```

---

## Chunk 5: Readarr — `AuthorMetadataService.FindById`

### Task 5: Add `FindById(string)` to `IAuthorMetadataService` and `AuthorMetadataService`

**Files:**
- Modify: `src/NzbDrone.Core/Books/Services/AuthorMetadataService.cs`

- [ ] **Step 5.1: Read `AuthorMetadataService.cs` and its interface**

Identify:
- The `IAuthorMetadataService` interface (may be in same file or separate)
- How `_authorMetadataRepository.FindById(List<string>)` is called
- Existing method signatures

- [ ] **Step 5.2: Write a unit test for `FindById(string)`**

In `src/NzbDrone.Core.Test/Books/Services/` (create if needed), add a test file `AuthorMetadataServiceFixture.cs`:

```csharp
using System.Collections.Generic;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Books.Services
{
    [TestFixture]
    public class AuthorMetadataServiceFixture : CoreTest<AuthorMetadataService>
    {
        [Test]
        public void find_by_id_returns_null_when_not_found()
        {
            Mocker.GetMock<IAuthorMetadataRepository>()
                  .Setup(x => x.FindById(It.IsAny<List<string>>()))
                  .Returns(new List<AuthorMetadata>());

            var result = Subject.FindById("9999");

            result.Should().BeNull();
        }

        [Test]
        public void find_by_id_returns_metadata_when_found()
        {
            var meta = new AuthorMetadata { ForeignAuthorId = "3389", Name = "C.S. Lewis", Kca = "kca://test" };
            Mocker.GetMock<IAuthorMetadataRepository>()
                  .Setup(x => x.FindById(It.IsAny<List<string>>()))
                  .Returns(new List<AuthorMetadata> { meta });

            var result = Subject.FindById("3389");

            result.Should().NotBeNull();
            result.ForeignAuthorId.Should().Be("3389");
            result.Kca.Should().Be("kca://test");
        }
    }
}
```

Run:
```bash
dotnet build src/Readarr.sln -p:Configuration=Debug -p:Platform=Posix --no-restore && \
dotnet test src/NzbDrone.Core.Test/Readarr.Core.Test.csproj \
  --filter "FullyQualifiedName~AuthorMetadataServiceFixture" \
  -p:Platform=Posix --no-build
```

Expected: FAIL — `FindById(string)` doesn't exist yet

- [ ] **Step 5.3: Add `FindById(string)` to interface and implementation**

In `IAuthorMetadataService`:
```csharp
AuthorMetadata FindById(string foreignAuthorId);
```

In `AuthorMetadataService`:
```csharp
public AuthorMetadata FindById(string foreignAuthorId)
{
    return _authorMetadataRepository.FindById(new List<string> { foreignAuthorId }).FirstOrDefault();
}
```

Ensure `using System.Linq;` is present.

- [ ] **Step 5.4: Build and run the test**

```bash
dotnet build src/Readarr.sln -p:Configuration=Debug -p:Platform=Posix --no-restore && \
dotnet test src/NzbDrone.Core.Test/Readarr.Core.Test.csproj \
  --filter "FullyQualifiedName~AuthorMetadataServiceFixture" \
  -p:Platform=Posix --no-build
```

Expected: PASS (2 tests)

- [ ] **Step 5.5: Commit**

```bash
git add src/NzbDrone.Core/Books/Services/AuthorMetadataService.cs
git add src/NzbDrone.Core.Test/Books/Services/AuthorMetadataServiceFixture.cs
git commit -m "feat: add AuthorMetadataService.FindById(string) for KCA lookup"
```

---

## Chunk 6: Readarr — `BookInfoProxy` refactor

### Task 6: Remove LazyCache + `CachedHttpClient`; add KCA passthrough to `BookInfoProxy`

**Files:**
- Modify: `src/NzbDrone.Core/MetadataSource/BookInfo/BookInfoProxy.cs`
- Modify: `src/NzbDrone.Core/MetadataSource/BookInfo/BookInfoCacheService.cs` (delete or gut)
- Create: `src/NzbDrone.Core.Test/MetadataSource/BookInfoProxyUncachedFixture.cs`

- [ ] **Step 6.1: Read `BookInfoProxy.cs` fully**

Identify:
- All constructor parameters
- `PollAuthor()` vs `PollAuthorUncached()` methods
- Where `_cachedHttpClient.Get(...)` is called
- Where `_authorCache` (LazyCache) is used
- How `MapAuthorMetadata()` builds the metadata object

- [ ] **Step 6.2: Read `BookInfoCacheService.cs`**

This service provides the LazyCache. Read to understand if it's used anywhere else or only in `BookInfoProxy`.

- [ ] **Step 6.3: Write failing tests for the new `BookInfoProxy`**

Create `src/NzbDrone.Core.Test/MetadataSource/BookInfoProxyUncachedFixture.cs`:

```csharp
using System.Net;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Books;
using NzbDrone.Core.MetadataSource.BookInfo;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MetadataSource
{
    [TestFixture]
    public class BookInfoProxyUncachedFixture : CoreTest<BookInfoProxy>
    {
        [Test]
        public void should_pass_kca_as_query_param_when_available()
        {
            var meta = new AuthorMetadata { ForeignAuthorId = "3389", Kca = "kca://author/amzn1.gr.author.v1.Test" };
            Mocker.GetMock<IAuthorMetadataService>()
                  .Setup(x => x.FindById("3389"))
                  .Returns(meta);

            // Capture the outgoing HTTP request
            HttpRequest capturedRequest = null;
            Mocker.GetMock<IHttpClient>()
                  .Setup(x => x.Get(It.IsAny<HttpRequest>()))
                  .Callback<HttpRequest>(r => capturedRequest = r)
                  .Returns(new HttpResponse<string>(new HttpRequest("http://test/author/3389"), new HttpHeader(), "{}") { StatusCode = HttpStatusCode.OK });

            // Act (will throw because response is invalid — that's fine, we just want the request)
            try { Subject.GetAuthorInfo("3389"); } catch { }

            Assert.IsNotNull(capturedRequest);
            StringAssert.Contains("kca=kca%3A%2F%2Fauthor", capturedRequest.Url.ToString());
        }

        [Test]
        public void should_not_use_cached_http_client()
        {
            // ICachedHttpResponseService should NOT be called in the new design
            Mocker.GetMock<IAuthorMetadataService>()
                  .Setup(x => x.FindById(It.IsAny<string>()))
                  .Returns((AuthorMetadata)null);

            Mocker.GetMock<IHttpClient>()
                  .Setup(x => x.Get(It.IsAny<HttpRequest>()))
                  .Returns(new HttpResponse<string>(new HttpRequest("http://test/author/0"), new HttpHeader(), "{}") { StatusCode = HttpStatusCode.NotFound });

            try { Subject.GetAuthorInfo("0"); } catch { }

            // CachedHttpResponseService.Get should never be called
            Mocker.GetMock<ICachedHttpResponseService>()
                  .Verify(x => x.Get(It.IsAny<HttpRequest>(), It.IsAny<bool>(), It.IsAny<System.TimeSpan>()), Times.Never);
        }
    }
}
```

Run:
```bash
dotnet build src/Readarr.sln -p:Configuration=Debug -p:Platform=Posix --no-restore && \
dotnet test src/NzbDrone.Core.Test/Readarr.Core.Test.csproj \
  --filter "FullyQualifiedName~BookInfoProxyUncachedFixture" \
  -p:Platform=Posix --no-build
```

Expected: FAIL — proxy still uses cached client and LazyCache

- [ ] **Step 6.4: Refactor `BookInfoProxy.cs`**

Key changes:

**Remove from constructor:**
- `ICachedHttpResponseService cachedHttpClient` parameter
- `CachingService` / LazyCache initialization (`_authorCache = ...`)

**Add to constructor:**
- `IAuthorMetadataService authorMetadataService` parameter
- Store as `private readonly IAuthorMetadataService _authorMetadataService;`

**Remove:**
- `_authorCache` field
- `_cachedHttpClient` field
- `PollAuthor()` method (the one with 60-retry loop and LazyCache)
- `LazyCache`, `LazyCache.Providers`, `Microsoft.Extensions.Caching.Memory` usings
- `BookInfoCacheService` usage

**Rewrite `PollAuthorUncached()`:**

```csharp
private Author PollAuthorUncached(string foreignAuthorId)
{
    var kca = _authorMetadataService.FindById(foreignAuthorId)?.Kca ?? string.Empty;

    while (true)
    {
        var httpRequest = _requestBuilder.GetRequestBuilder()
            .Create()
            .SetSegment("route", $"author/{foreignAuthorId}")
            .AddQueryParam("kca", kca)
            .Build();

        httpRequest.AllowAutoRedirect = true;
        httpRequest.SuppressHttpError = true;

        var httpResponse = _httpClient.Get(httpRequest);

        if (httpResponse.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
        {
            WaitUntilRetry(httpResponse);
            continue;
        }

        if (httpResponse.HasHttpError)
        {
            if (httpResponse.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                throw new AuthorNotFoundException(foreignAuthorId);
            }

            if (httpResponse.StatusCode == System.Net.HttpStatusCode.BadRequest)
            {
                throw new BadRequestException(foreignAuthorId);
            }

            throw new BookInfoException("Unexpected error fetching author data from bookinfo");
        }

        var resource = JsonSerializer.Deserialize<AuthorResource>(httpResponse.Content, SerializerSettings);

        if (resource?.Works == null)
        {
            throw new BookInfoException($"Failed to get works for {foreignAuthorId}");
        }

        resource.Works = SanitizeWorks(resource.Works, _logger);
        resource.Series ??= new List<SeriesResource>();

        return MapAuthor(resource);
    }
}
```

**Replace all callers of `PollAuthor()` with `PollAuthorUncached()`.**

**In `MapAuthorMetadata()`**, add KCA mapping:
```csharp
metadata.Kca = resource.Kca ?? string.Empty;
```

- [ ] **Step 6.5: Build**

```bash
dotnet build src/Readarr.sln -p:Configuration=Debug -p:Platform=Posix --no-restore
```

Expected: Build succeeds; fix any compilation errors before proceeding.

- [ ] **Step 6.6: Run the new tests**

```bash
dotnet test src/NzbDrone.Core.Test/Readarr.Core.Test.csproj \
  --filter "FullyQualifiedName~BookInfoProxyUncachedFixture" \
  -p:Platform=Posix --no-build
```

Expected: PASS (2 tests)

- [ ] **Step 6.7: Run existing BookInfoProxy tests (should still pass/be ignored)**

```bash
dotnet test src/NzbDrone.Core.Test/Readarr.Core.Test.csproj \
  --filter "FullyQualifiedName~BookInfoProxy" \
  -p:Platform=Posix --no-build
```

Expected: Existing `[Ignore]`-tagged tests are skipped; no regressions

- [ ] **Step 6.8: Commit**

```bash
git add src/NzbDrone.Core/MetadataSource/BookInfo/BookInfoProxy.cs
git add src/NzbDrone.Core.Test/MetadataSource/BookInfoProxyUncachedFixture.cs
git commit -m "feat: BookInfoProxy removes LazyCache and CachedHttpClient; adds KCA passthrough via IAuthorMetadataService"
```

---

## Chunk 7: Final build, integration checks, and docker-compose

### Task 7: Full build verification, docker-compose `--workers 1` note, and smoke test

**Files:**
- Modify: `docker-compose.yml` (if CMD override needed; usually handled by Dockerfile)

- [ ] **Step 7.1: Full backend build**

```bash
dotnet build src/Readarr.sln -p:Configuration=Debug -p:Platform=Posix --no-restore
```

Expected: 0 errors, 0 warnings (StyleCop treats warnings as errors)

- [ ] **Step 7.2: Run all Core unit tests**

```bash
dotnet test src/NzbDrone.Core.Test/Readarr.Core.Test.csproj \
  -p:Platform=Posix --no-build 2>&1 | tail -20
```

Expected: All tests pass (may be slow; watch for failures)

- [ ] **Step 7.3: Run all bookinfo tests**

```bash
cd bookinfo && python -m pytest -v
```

Expected: All PASS; no references to `aiosqlite`, `cache_module`

- [ ] **Step 7.4: Verify `bookinfo/goodreads.py` has no `cache_module` imports**

```bash
grep -n "cache_module\|aiosqlite\|import cache" bookinfo/goodreads.py
```

Expected: no matches

- [ ] **Step 7.5: Verify `docker-compose.yml` is consistent with stateless design**

Read `docker-compose.yml`. Confirm:
- `bookinfo-data` volume is removed (no more SQLite DB)
- `bookinfo-logs` volume is present
- Any `BOOKINFO_DB_PATH` env var is removed

Update `docker-compose.yml` to remove the `bookinfo-data` volume:

Remove:
```yaml
      - bookinfo-data:/data
```

Remove from volumes section:
```yaml
  bookinfo-data:
```

- [ ] **Step 7.6: Verify `READARR_METADATA_URL` format in docker-compose**

The `{route}` placeholder in `READARR_METADATA_URL=http://bookinfo:28202/{route}` must match how `MetadataRequestBuilder.cs` uses it. Confirm the URL template is correct.

- [ ] **Step 7.7: Final commit**

```bash
git add docker-compose.yml
git commit -m "chore: remove bookinfo-data volume from docker-compose (stateless design, no SQLite DB)"
```

- [ ] **Step 7.8: Tag the feature complete**

```bash
git log --oneline -10
```

Verify all commits are present in a logical sequence.

---

## Reference: Key Architectural Invariants

These must hold after all tasks complete:

1. **No `aiosqlite` in bookinfo** — `grep -r "aiosqlite" bookinfo/` returns nothing
2. **No `cache_module` in `app.py` or `goodreads.py`** — no SQLite calls in bookinfo routes
3. **`complete_author_background` returns `dict`** — no `cache_set_fn` parameter
4. **`_pending_complete` is the only state in bookinfo** — and it's in-memory, per-process
5. **`--workers 1` in bookinfo Dockerfile** — ensures `_pending_complete` consistency
6. **`AuthorMetadata.Kca` exists** — migration 041 has run, column is in DB
7. **`BookInfoProxy` uses `_httpClient.Get()`** — NOT `_cachedHttpClient.Get()`
8. **`BookInfoProxy` reads KCA from `_authorMetadataService.FindById()`** — NOT from any cache
9. **`GET /work/{work_id}` → 404** — expected; book redirect goes to `/author/{id}` instead
10. **Webhook fires on background complete** — `_notify_readarr(author_id)` after `_pending_complete` entry is stored

## Reference: Data Flow After This Change

```
Readarr /author/{id} request
        │
        ├─ DB lookup: AuthorMetadata.Kca for foreignAuthorId
        │
        ▼
GET bookinfo/author/{id}?kca=<kca_or_empty>
        │
        ├─ kca present? skip XML API → go straight to GraphQL
        ├─ kca absent? XML API → resolve kca → GraphQL
        │
        ├─ Returns partial immediately (first page ~20 works)
        │    └─ Includes "Kca" field in response
        │
        ├─ Spawns background task → paginates remaining pages
        │    └─ Stores complete dict in _pending_complete[author_id]
        │    └─ POSTs RefreshAuthor webhook to Readarr
        │
        ▼
Readarr receives partial → stores Kca in AuthorMetadata.Kca → done
        │
        ▼ (on webhook)
Readarr GET bookinfo/author/{id}?kca=<stored_kca>
        │
        ├─ _pending_complete[author_id] exists → pop and return complete
        ▼
Readarr stores complete author/book data in readarr.db → single source of truth
```
