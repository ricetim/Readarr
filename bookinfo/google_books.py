from __future__ import annotations

import hashlib
import logging
import os
from typing import Optional

import httpx

logger = logging.getLogger(__name__)

_VOLUMES_URL = "https://www.googleapis.com/books/v1/volumes"
_API_KEY = os.getenv("GOOGLE_BOOKS_API_KEY", "")


def _synthetic_foreign_id(work_id: int) -> int:
    """Return a stable positive int for a synthetic Google Books edition.

    Uses SHA-256 (not Python's built-in hash) so the result is stable
    across interpreter restarts regardless of PYTHONHASHSEED.
    """
    digest = hashlib.sha256(f"gbooks:{work_id}".encode()).digest()
    return int.from_bytes(digest[:4], "big") % (2**31 - 1) + 1


async def supplement_ebook_edition(
    title: str,
    author: str,
    work_id: int,
    author_foreign_id: int,
    client: Optional[httpx.AsyncClient] = None,
) -> Optional[dict]:
    """Query Google Books for an English ebook edition of this work.

    Returns a synthetic BookResource-shaped dict if found, else None.
    Only called during background completion (not the fast path).
    """
    params = {
        "q": f'intitle:"{title}" inauthor:"{author}"',
        "langRestrict": "en",
        "maxResults": 5,
    }
    if _API_KEY:
        params["key"] = _API_KEY

    should_close = client is None
    if client is None:
        client = httpx.AsyncClient(timeout=15.0)
    try:
        response = await client.get(_VOLUMES_URL, params=params)
        response.raise_for_status()
        data = response.json()
    except Exception:
        logger.warning("Google Books query failed for '%s' by '%s'", title, author)
        return None
    finally:
        if should_close:
            await client.aclose()

    for item in data.get("items", []):
        volume_info = item.get("volumeInfo") or {}
        sale_info = item.get("saleInfo") or {}
        if sale_info.get("isEbook") and volume_info.get("language") == "en":
            return {
                "ForeignId": _synthetic_foreign_id(work_id),
                "KCA": "",
                "Asin": "",
                "Isbn13": "",
                "Title": title,
                "FullTitle": title,
                "ShortTitle": title,
                "Language": "eng",
                "Format": "Kindle Edition",
                "IsEbook": True,
                "Publisher": "",
                "NumPages": None,
                "RatingCount": 0,
                "RatingSum": 0,
                "AverageRating": 0.0,
                "ImageUrl": "",
                "Url": "",
                "ReleaseDate": "",
                "ReleaseDateRaw": "",
                "EditionInformation": "",
                "Description": "",
                "Contributors": [{"ForeignId": author_foreign_id, "Role": "Author"}],
            }

    return None
