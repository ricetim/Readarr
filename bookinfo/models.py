from __future__ import annotations

from typing import Optional

EBOOK_FORMATS = {
    "kindle edition", "ebook", "e-book", "epub", "digital",
    "pdf", "nook", "kobo", "digital edition", "ibooks",
    "electronic", "e-book edition",
}
AUDIOBOOK_FORMATS = {
    "audible audio", "audiobook", "audio cd", "mp3 cd",
    "audio", "unabridged", "abridged", "audio cassette",
}


def classify_edition(format_str: Optional[str]) -> tuple[bool, bool]:
    """Return (is_ebook, is_audiobook) for a Goodreads format string."""
    fmt = (format_str or "").strip().lower()
    return fmt in EBOOK_FORMATS, fmt in AUDIOBOOK_FORMATS


def dedup_editions(editions: list[dict]) -> list[dict]:
    """Deduplicate editions by (format_category, language).

    Bug 1 fix: key on format category (ebook/audiobook/physical), NOT on title.
    This preserves both a Kindle Edition AND a Hardcover for the same language.

    Within each (category, language) bucket, keeps the edition with the highest
    RatingCount (most popular).
    """
    editions = sorted(
        editions, key=lambda e: e.get("RatingCount") or 0, reverse=True
    )
    seen: dict[tuple, dict] = {}
    for edition in editions:
        is_ebook, is_audio = classify_edition(edition.get("Format", ""))
        if is_audio:
            category = "audiobook"
        elif is_ebook:
            category = "ebook"
        else:
            category = "physical"
        lang = (edition.get("Language") or "").lower()
        key = (category, lang)
        if key not in seen:
            seen[key] = edition
    return list(seen.values())
