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
from fastapi import BackgroundTasks, FastAPI, HTTPException, Query, Response
from fastapi.responses import RedirectResponse

import google_books as gb_module
from goodreads import GoodreadsClient

logger = logging.getLogger(__name__)

LOG_DIR = os.getenv("BOOKINFO_LOG_DIR", "/logs")
LOG_KEEP = int(os.getenv("BOOKINFO_LOG_KEEP", "10"))
GR_RATE = float(os.getenv("BOOKINFO_GR_RATE", "3"))
BATCH_SIZE = int(os.getenv("BOOKINFO_BATCH_SIZE", "20"))
READARR_URL = os.getenv("READARR_URL", "").rstrip("/")
READARR_API_KEY = os.getenv("READARR_API_KEY", "")

PENDING_TTL = 2 * 3600  # 2 hours in seconds

goodreads_client: Optional[GoodreadsClient] = None
_pending_complete: dict[int, tuple[dict, float]] = {}


async def _notify_readarr(author_id: int) -> None:
    """Notify Readarr to refresh an author after bookinfo background completion.

    Best-effort: retries on 5xx or network errors with 5s / 30s / 5m backoff.
    Silently skips if READARR_URL or READARR_API_KEY are not configured.
    """
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
                    logger.debug(
                        "Notified Readarr to refresh author %d (HTTP %d)", author_id, r.status_code
                    )
                    return
                logger.warning(
                    "Readarr returned %d for author %d (attempt %d/%d)",
                    r.status_code, author_id, attempt, len(delays),
                )
            except Exception as exc:
                logger.warning(
                    "Failed to notify Readarr for author %d (attempt %d/%d): %s",
                    author_id, attempt, len(delays), exc,
                )
            if attempt < len(delays):
                await asyncio.sleep(delay)
    logger.warning(
        "Gave up notifying Readarr for author %d after %d attempts", author_id, len(delays)
    )


def _setup_file_logging() -> None:
    """Write logs to a timestamped file in LOG_DIR, keeping the last LOG_KEEP files."""
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

    # Prune old log files beyond LOG_KEEP
    pattern = os.path.join(LOG_DIR, "bookinfo-*.log")
    old_files = sorted(glob.glob(pattern))
    for old in old_files[:-LOG_KEEP]:
        try:
            os.remove(old)
        except OSError:
            pass


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
    """Tell Readarr no global author changes are available (use its own schedule)."""
    return {"Limited": True, "Ids": []}


@app.get("/author/{author_id}")
async def get_author(author_id: int, background_tasks: BackgroundTasks, kca: str = Query(default="")):
    # Check _pending_complete first — one-time pickup of completed background data
    if author_id in _pending_complete:
        complete, _deadline = _pending_complete.pop(author_id)
        return complete

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

    # If name is still empty, extract it from contributor data in works GraphQL response.
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
        complete = await goodreads_client.complete_author_background(
            author_id=author_id,
            partial_data=partial,
            kca=kca,
            first_page_next_token=first_page_next_token,
            google_supplement_fn=gb_module.supplement_ebook_edition,
        )
        now = time.monotonic()
        _pending_complete[author_id] = (complete, now + PENDING_TTL)
        # Evict stale entries
        stale = [aid for aid, (_data, deadline) in _pending_complete.items() if deadline < now]
        for aid in stale:
            _pending_complete.pop(aid, None)
        await _notify_readarr(author_id)

    background_tasks.add_task(_complete_and_store)
    return partial


@app.get("/work/{work_id}")
async def get_work(work_id: int):
    raise HTTPException(status_code=404, detail="Work not found")


@app.get("/book/{edition_id}")
async def get_book(edition_id: int):
    """Redirect to the author that has this edition."""
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
