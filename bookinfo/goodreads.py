from __future__ import annotations

import asyncio
import binascii
import json
import logging
import time
import xml.etree.ElementTree as ET
from typing import Any, Callable, Coroutine, Optional

import httpx

from models import classify_edition, dedup_editions

logger = logging.getLogger(__name__)

GRAPHQL_URL = binascii.unhexlify(
    "68747470733a2f2f6b7862776d716f76366a676733646161616d62373434796375342e61707073796e"
    "632d6170692e75732d656173742d312e616d617a6f6e6177732e636f6d2f6772617068716c"
).decode()
GRAPHQL_KEY = binascii.unhexlify(
    "6461322d787067736479646b627265676a68707236656a7a716468757779"
).decode()
XML_BASE = "https://www.goodreads.com"
XML_KEY = binascii.unhexlify(
    "543772537858796441735a6730645533504a7a466877"
).decode()

# ---------------------------------------------------------------------------
# GraphQL query strings (replicated exactly from rreading-glasses)
# ---------------------------------------------------------------------------

_BOOK_FRAGMENT = """
fragment BookInfo on Book {
  id legacyId description(stripped: true)
  bookGenres { genre { name } }
  bookSeries { series { id title webUrl } seriesPlacement }
  details {
    asin isbn13 format numPages
    language { name }
    officialUrl publisher publicationTime
  }
  imageUrl
  primaryContributorEdge { node { id name legacyId webUrl profileImageUrl description } }
  stats { averageRating ratingsCount ratingsSum }
  title titlePrimary webUrl
}
"""

_GET_BOOK_QUERY = (
    _BOOK_FRAGMENT
    + """
query GetBook($legacyId: Int!) {
  getBookByLegacyId(legacyId: $legacyId) {
    ...BookInfo
    work {
      id legacyId
      details { webUrl publicationTime }
      bestBook {
        legacyId titlePrimary
        primaryContributorEdge { role node { legacyId } }
      }
      editions { edges { node { ...BookInfo } } }
    }
  }
}
"""
)

_GET_AUTHOR_WORKS_QUERY = """
query GetAuthorWorks(
  $getWorksByContributorInput: GetWorksByContributorInput!
  $pagination: PaginationInput!
) {
  getWorksByContributor(
    getWorksByContributorInput: $getWorksByContributorInput
    pagination: $pagination
  ) {
    edges {
      node {
        id
        bestBook {
          legacyId
          primaryContributorEdge { role node { legacyId } }
          secondaryContributorEdges { role }
        }
      }
    }
    pageInfo { hasNextPage nextPageToken }
  }
}
"""

_GET_EDITIONS_QUERY = """
query GetEditions($workId: ID!, $pagination: PaginationInput!) {
  getEditions(id: $workId, pagination: $pagination) {
    edges { node { id legacyId } }
    pageInfo { hasNextPage nextPageToken }
  }
}
"""

_SEARCH_QUERY = """
query Search($query: String!) {
  getSearchSuggestions(query: $query) {
    totalCount
    edges {
      ... on SearchBookEdge {
        node {
          work { legacyId bestBook { primaryContributorEdge { node { name legacyId } } } }
          title legacyId
        }
      }
    }
  }
}
"""

# Inline alias template used in batch_graphql
_BATCH_BOOK_ALIAS = (
    "{alias}: getBookByLegacyId(legacyId: {lid}) {{ ...BookInfo "
    "work {{ id legacyId details {{ webUrl publicationTime }} "
    "bestBook {{ legacyId titlePrimary "
    "primaryContributorEdge {{ role node {{ legacyId }} }} }} "
    "editions {{ edges {{ node {{ ...BookInfo }} }} }} }} }}"
)


# ---------------------------------------------------------------------------
# Module-level mapping helpers
# ---------------------------------------------------------------------------

def _parse_date(publication_time) -> tuple[Optional[str], Optional[str]]:
    """Parse Goodreads publication time into (ReleaseDate, ReleaseDateRaw).

    Returns (None, None) when no date is available — Readarr's BookResource
    and WorkResource both declare ReleaseDate as DateTime? so null is correct.

    Goodreads may return an ISO string ("2014-04-01T...") or a Unix timestamp
    (integer milliseconds or seconds as a float/int).
    """
    import datetime
    if not publication_time:
        return None, None
    if isinstance(publication_time, (int, float)):
        # Unix timestamp; Goodreads uses milliseconds (abs value > 1e10)
        ts = publication_time / 1000.0 if abs(publication_time) > 1e10 else float(publication_time)
        raw = datetime.datetime.utcfromtimestamp(ts).strftime("%Y-%m-%d")
    else:
        raw = str(publication_time)[:10]  # "2014-04-01"
    return f"{raw}T07:00:00Z", raw


def map_book(gql_book: dict, author_foreign_id: int) -> dict:
    """Map a Goodreads GraphQL book node to a BookResource-shaped dict.

    Sets IsEbook from classify_edition() (Bug 2 fix).
    Guarantees Contributors is non-empty (prevents Readarr deletion).
    """
    details = gql_book.get("details") or {}
    lang_obj = details.get("language") or {}
    fmt = details.get("format", "")
    is_ebook, _ = classify_edition(fmt)

    contrib_edge = gql_book.get("primaryContributorEdge") or {}
    contrib_node = contrib_edge.get("node") or {}
    contrib_id = contrib_node.get("legacyId") or author_foreign_id

    release_date, release_date_raw = _parse_date(details.get("publicationTime"))
    stats = gql_book.get("stats") or {}

    return {
        "ForeignId": gql_book.get("legacyId", 0),
        "KCA": gql_book.get("id", ""),
        "Asin": details.get("asin") or "",
        "Isbn13": details.get("isbn13") or "",
        "Title": gql_book.get("titlePrimary") or gql_book.get("title") or "",
        "FullTitle": gql_book.get("titlePrimary") or gql_book.get("title") or "",
        "ShortTitle": gql_book.get("title") or "",
        "Language": lang_obj.get("name", ""),
        "Format": fmt,
        "IsEbook": is_ebook,
        "Publisher": details.get("publisher") or "",
        "NumPages": details.get("numPages"),
        "RatingCount": stats.get("ratingsCount", 0),
        "RatingSum": stats.get("ratingsSum", 0),
        "AverageRating": stats.get("averageRating", 0.0),
        "ImageUrl": gql_book.get("imageUrl") or "",
        "Url": gql_book.get("webUrl") or "",
        "ReleaseDate": release_date,
        "ReleaseDateRaw": release_date_raw,
        "EditionInformation": "",
        "Description": gql_book.get("description") or "",
        "Contributors": [{"ForeignId": contrib_id, "Role": "Author"}],
    }


def map_work(gql_book: dict, editions: list[dict], author_foreign_id: int) -> dict:
    """Map a GetBook response (which wraps a work) to a WorkResource-shaped dict.

    Deduplicates editions via dedup_editions() (Bug 1 fix).
    """
    work = gql_book.get("work") or {}
    best_book = work.get("bestBook") or {}
    work_details = work.get("details") or {}

    genres = [
        g["genre"]["name"]
        for g in (gql_book.get("bookGenres") or [])
        if g.get("genre")
    ]

    series_links = []
    for s in gql_book.get("bookSeries") or []:
        sr = s.get("series") or {}
        series_links.append(
            {
                "ForeignId": 0,  # Series ForeignId not available at this level
                "Title": sr.get("title", ""),
                "Url": sr.get("webUrl", ""),
                "PositionInSeries": s.get("seriesPlacement", ""),
            }
        )

    release_date, release_date_raw = _parse_date(work_details.get("publicationTime"))

    return {
        "ForeignId": work.get("legacyId", 0),
        "KCA": work.get("id", ""),
        "Title": best_book.get("titlePrimary") or gql_book.get("titlePrimary") or "",
        "FullTitle": best_book.get("titlePrimary") or gql_book.get("titlePrimary") or "",
        "ShortTitle": best_book.get("titlePrimary") or gql_book.get("titlePrimary") or "",
        "Url": work_details.get("webUrl") or "",
        "ReleaseDate": release_date,
        "ReleaseDateRaw": release_date_raw,
        "Genres": genres,
        "Series": series_links,
        "RelatedWorks": [],
        "Authors": [{"ForeignId": author_foreign_id, "Name": "", "KCA": ""}],
        "Books": dedup_editions(editions),
    }


# ---------------------------------------------------------------------------
# Rate limiter
# ---------------------------------------------------------------------------

class RateLimiter:
    """Serialises all requests with a minimum inter-request interval.

    Uses a lock so concurrent coroutines queue up rather than all sleeping
    by the same amount. Rate applies globally across all callers.
    """

    def __init__(self, rate: float):
        self._min_interval = 1.0 / rate
        self._last: float = 0.0
        self._lock = asyncio.Lock()

    async def acquire(self) -> None:
        async with self._lock:
            now = time.monotonic()
            wait = self._last + self._min_interval - now
            if wait > 0:
                await asyncio.sleep(wait)
            self._last = time.monotonic()


# ---------------------------------------------------------------------------
# Goodreads client
# ---------------------------------------------------------------------------

class GoodreadsClient:
    def __init__(self, rate: float = 3.0, batch_size: int = 20):
        self._rate_limiter = RateLimiter(rate)
        self._batch_size = batch_size
        self._client = httpx.AsyncClient(timeout=30.0)

    async def close(self) -> None:
        await self._client.aclose()

    # ------------------------------------------------------------------ #
    # Internal HTTP helpers
    # ------------------------------------------------------------------ #

    async def _graphql_raw(self, body: dict) -> dict:
        await self._rate_limiter.acquire()
        response = await self._client.post(
            GRAPHQL_URL,
            json=body,
            headers={
                "x-api-key": GRAPHQL_KEY,
                "content-type": "application/json",
            },
        )
        response.raise_for_status()
        payload = response.json()
        if "errors" in payload:
            if all(e.get("errorType") == "RESOURCE_NOT_FOUND" for e in payload["errors"]):
                raise LookupError("Author not found in Goodreads")
            raise RuntimeError(f"GraphQL errors: {payload['errors']}")
        return payload.get("data", {})

    async def graphql_query(self, query: str, variables: dict) -> dict:
        return await self._graphql_raw({"query": query, "variables": variables})

    async def batch_graphql(self, legacy_ids: list[int]) -> list[dict]:
        """Fetch multiple books in batched aliased GraphQL requests.

        Sends at most batch_size queries per HTTP request.
        Returns a flat list of book-level dicts (with embedded .work).
        """
        results: list[dict] = []
        for chunk_start in range(0, len(legacy_ids), self._batch_size):
            chunk = legacy_ids[chunk_start : chunk_start + self._batch_size]
            aliases = "\n".join(
                _BATCH_BOOK_ALIAS.format(alias=f"q{i}", lid=lid)
                for i, lid in enumerate(chunk)
            )
            batch_query = _BOOK_FRAGMENT + "{\n" + aliases + "\n}"
            data = await self._graphql_raw({"query": batch_query})
            for i in range(len(chunk)):
                book_data = data.get(f"q{i}")
                if book_data:
                    results.append(book_data)
        return results

    async def resolve_author_xml(self, author_id: int) -> tuple[str, str, str, str]:
        """Resolve Goodreads legacy author integer ID via XML API.

        Returns (kca, name, image_url, about) — all from a single XML request.

        The KCA is NOT at <author><uri>; it lives inside each <book>'s <authors>
        list. We scan each book's contributors until we find one whose name
        matches the top-level author name, then return that contributor's URI.
        This mirrors rreading-glasses' legacyAuthorIDtoKCA() logic.
        """
        await self._rate_limiter.acquire()
        url = f"{XML_BASE}/author/show/{author_id}.xml?key={XML_KEY}"
        response = await self._client.get(url)
        if response.status_code == 404:
            return "", "", "", ""
        response.raise_for_status()
        try:
            root = ET.fromstring(response.text)
            author_el = root.find(".//author")
            if author_el is None:
                return "", "", "", ""
            name = (author_el.findtext("name") or "").strip()
            image_url = (author_el.findtext("image_url") or "").strip()
            about = (author_el.findtext("about") or "").strip()

            # KCA lives inside the books list, not the top-level author element
            kca = ""
            for book_el in author_el.findall("books/book"):
                for contrib_el in book_el.findall("authors/author"):
                    contrib_name = (contrib_el.findtext("name") or "").strip()
                    if contrib_name == name:
                        candidate = (contrib_el.findtext("uri") or "").strip()
                        if candidate:
                            kca = candidate
                            break
                if kca:
                    break

            return kca, name, image_url, about
        except ET.ParseError:
            logger.warning("KCA XML parse error for author %d", author_id)
            return "", "", "", ""

    async def get_author_works_page(
        self, kca: str, author_id: int, token: Optional[str]
    ) -> tuple[list[dict], Optional[str]]:
        """Fetch one page of an author's works.

        Filters to works where the target author has role "Author".
        Returns (works, next_page_token). Each work dict has legacyId and workKca.
        """
        pagination: dict[str, Any] = {"limit": 20}
        if token:
            pagination["after"] = token

        data = await self.graphql_query(
            _GET_AUTHOR_WORKS_QUERY,
            {
                "getWorksByContributorInput": {"id": kca},
                "pagination": pagination,
            },
        )
        contributor_data = data.get("getWorksByContributor", {})
        edges = contributor_data.get("edges", [])
        page_info = contributor_data.get("pageInfo", {})

        works: list[dict] = []
        for edge in edges:
            node = edge.get("node", {})
            best_book = node.get("bestBook", {})
            contributor_edge = best_book.get("primaryContributorEdge", {})
            role = contributor_edge.get("role", "")
            contrib_node = contributor_edge.get("node", {})
            if role != "Author" or contrib_node.get("legacyId") != author_id:
                continue
            works.append(
                {"legacyId": best_book.get("legacyId"), "workKca": node.get("id")}
            )

        next_token = (
            page_info.get("nextPageToken") if page_info.get("hasNextPage") else None
        )
        return works, next_token

    async def get_series(self, series_id: int) -> dict:
        """Fetch series data via the Goodreads XML API."""
        await self._rate_limiter.acquire()
        url = f"{XML_BASE}/series/show/{series_id}.xml?key={XML_KEY}"
        response = await self._client.get(url)
        response.raise_for_status()

        root = ET.fromstring(response.text)
        series_el = root.find("series")
        if series_el is None:
            return {}

        title = (series_el.findtext("title") or "").strip()
        description = (series_el.findtext("description") or "").strip()
        link_items: list[dict] = []
        for i, sw in enumerate(series_el.findall(".//series_work")):
            work_el = sw.find("work")
            work_id_text = work_el.findtext("id") if work_el is not None else None
            position = (sw.findtext("user_position") or "").strip()
            if not work_id_text:
                continue
            try:
                link_items.append(
                    {
                        "ForeignWorkId": int(work_id_text),
                        "PositionInSeries": position,
                        "SeriesPosition": i + 1,
                        "Primary": False,
                    }
                )
            except ValueError:
                pass

        return {
            "ForeignId": series_id,
            "Title": title,
            "Description": description,
            "LinkItems": link_items,
        }

    async def get_editions_page(
        self, work_kca: str, token: Optional[str]
    ) -> tuple[list[int], Optional[str]]:
        """Fetch one page of edition legacy IDs for a work via GetEditions query.

        Returns (edition_legacy_ids, next_page_token).
        Used by background completion to catch editions truncated in GetBook inline response.
        """
        pagination: dict[str, Any] = {"limit": 20}
        if token:
            pagination["after"] = token

        data = await self.graphql_query(
            _GET_EDITIONS_QUERY,
            {"workId": work_kca, "pagination": pagination},
        )
        editions_data = data.get("getEditions", {})
        edges = editions_data.get("edges", [])
        page_info = editions_data.get("pageInfo", {})

        edition_ids = [
            e["node"]["legacyId"]
            for e in edges
            if e.get("node", {}).get("legacyId")
        ]
        next_token = (
            page_info.get("nextPageToken") if page_info.get("hasNextPage") else None
        )
        return edition_ids, next_token

    async def fetch_author_fast_path(
        self,
        author_id: int,
        author_name: str,
        author_kca: str,
        author_image_url: str = "",
        author_description: str = "",
        batch_books_fn=None,
    ) -> tuple[dict, Optional[str]]:
        """Run fast path: fetch page 1 of works.

        Returns (partial_author_dict, first_page_next_token).
        The caller passes first_page_next_token to complete_author_background
        so background completion can start from page 2 without re-fetching page 1.

        batch_books_fn is injectable for testing; defaults to self.batch_graphql.
        """
        if batch_books_fn is None:
            batch_books_fn = self.batch_graphql

        works_page, first_page_next_token = await self.get_author_works_page(
            kca=author_kca, author_id=author_id, token=None
        )
        legacy_ids = [w["legacyId"] for w in works_page if w.get("legacyId")]
        book_results = await batch_books_fn(legacy_ids) if legacy_ids else []

        # If author_kca is unknown, extract it from the contributor node.id in the
        # first GraphQL result — primaryContributorEdge.node.id IS the KCA.
        if not author_kca and book_results:
            first = book_results[0]
            contrib_node = (first.get("primaryContributorEdge") or {}).get("node") or {}
            extracted_kca = contrib_node.get("id", "")
            if extracted_kca.startswith("kca://"):
                author_kca = extracted_kca

        # If author_name or author_image_url is unknown (e.g. KCA was cached,
        # XML API unavailable), extract from contributor nodes in the GraphQL response.
        if not author_name or not author_image_url:
            for gql_book in book_results:
                contrib_node = (gql_book.get("primaryContributorEdge") or {}).get("node") or {}
                if contrib_node.get("legacyId") == author_id:
                    if not author_name and contrib_node.get("name"):
                        author_name = contrib_node["name"]
                    if not author_image_url and contrib_node.get("profileImageUrl"):
                        author_image_url = contrib_node["profileImageUrl"]
                if author_name and author_image_url:
                    break

        works: list[dict] = []
        for gql_book in book_results:
            work = gql_book.get("work") or {}
            inline_editions = [
                e["node"]
                for e in (work.get("editions") or {}).get("edges", [])
                if e.get("node")
            ]
            all_editions = [map_book(gql_book, author_id)] + [
                map_book(e, author_id) for e in inline_editions
            ]
            works.append(map_work(gql_book, all_editions, author_id))

        author_dict = {
            "ForeignId": author_id,
            "Kca": author_kca,
            "Name": author_name,
            "Description": author_description,
            "ImageUrl": author_image_url,
            "Url": f"https://www.goodreads.com/author/show/{author_id}",
            "Works": works,
            "Series": [],
        }
        return author_dict, first_page_next_token

    async def search(self, query: str) -> list[dict]:
        """Search Goodreads via GraphQL getSearchSuggestions."""
        data = await self.graphql_query(_SEARCH_QUERY, {"query": query})
        suggestions = data.get("getSearchSuggestions", {})
        results: list[dict] = []
        for edge in suggestions.get("edges", []):
            node = edge.get("node")
            if not node:
                continue
            work = node.get("work") or {}
            best_book = work.get("bestBook") or {}
            author_edge = best_book.get("primaryContributorEdge") or {}
            author_node = author_edge.get("node") or {}
            results.append(
                {
                    "BookId": node.get("legacyId", 0),
                    "WorkId": work.get("legacyId", 0),
                    "Author": {"Id": author_node.get("legacyId", 0)},
                }
            )
        return results

    async def complete_author_background(
        self,
        author_id: int,
        partial_data: dict,
        kca: str,
        first_page_next_token: Optional[str] = None,
        google_supplement_fn: Optional[Callable] = None,
        on_progress: Optional[Callable] = None,
    ) -> dict:
        """Fetch pages 2+ of works, complete edition data, run Google Books supplement.

        `first_page_next_token` is the token returned by fetch_author_fast_path.
        If None, page 1 was the only page and no further work pagination occurs.
        For each new work (pages 2+), GetEditions is called to catch editions
        truncated in the inline GetBook response (spec step 8).

        Fixes Bug 3: rreading-glasses' first response is always incomplete.

        Returns the completed author dict.
        """
        works_by_id: dict[int, dict] = {
            w["ForeignId"]: w for w in partial_data.get("Works", [])
        }

        # Fetch remaining pages (2+) starting from the token the fast path returned.
        # This avoids re-fetching page 1 against the rate limiter.
        next_token = first_page_next_token
        while next_token:
            try:
                works_page, next_token = await self.get_author_works_page(
                    kca=kca, author_id=author_id, token=next_token
                )
            except Exception:
                logger.warning("getWorksByContributor page failed for author %d, completing with %d works", author_id, len(works_by_id))
                break
            new_ids = [
                w["legacyId"]
                for w in works_page
                if w.get("legacyId") and w["legacyId"] not in works_by_id
            ]
            if new_ids:
                book_results = await self.batch_graphql(new_ids)
                for gql_book in book_results:
                    work = gql_book.get("work") or {}
                    work_kca = work.get("id", "")
                    inline_editions = [
                        e["node"]
                        for e in (work.get("editions") or {}).get("edges", [])
                        if e.get("node")
                    ]
                    all_editions = [map_book(gql_book, author_id)] + [
                        map_book(e, author_id) for e in inline_editions
                    ]

                    # Fetch additional editions via GetEditions (spec step 8):
                    # catches editions truncated in the inline response.
                    # Best-effort: a Lambda error on getEditions skips supplement for this work.
                    if work_kca:
                        try:
                            inline_ids: set[int] = {
                                e["ForeignId"] for e in all_editions if e.get("ForeignId")
                            }
                            ed_token: Optional[str] = None
                            while True:
                                extra_ids, ed_token = await self.get_editions_page(
                                    work_kca, ed_token
                                )
                                extra_to_fetch = [i for i in extra_ids if i not in inline_ids]
                                if extra_to_fetch:
                                    extra_books = await self.batch_graphql(extra_to_fetch)
                                    all_editions.extend(
                                        map_book(b, author_id) for b in extra_books
                                    )
                                    inline_ids.update(extra_to_fetch)
                                if not ed_token:
                                    break
                        except Exception:
                            logger.debug("getEditions failed for work %s, using inline editions", work_kca)

                    work_dict = map_work(gql_book, all_editions, author_id)
                    works_by_id[work_dict["ForeignId"]] = work_dict

            if on_progress:
                on_progress(works_by_id)

        # Google Books supplement for works with no ebook editions
        if google_supplement_fn:
            author_name = partial_data.get("Name", "")
            for work_id, work in works_by_id.items():
                has_ebook = any(e.get("IsEbook") for e in work.get("Books", []))
                if not has_ebook and work.get("Title"):
                    synthetic = await google_supplement_fn(
                        title=work["Title"],
                        author=author_name,
                        work_id=work_id,
                        author_foreign_id=author_id,
                    )
                    if synthetic:
                        work["Books"].append(synthetic)

        updated = {**partial_data, "Works": list(works_by_id.values())}
        return updated
