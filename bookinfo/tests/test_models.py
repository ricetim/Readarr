from __future__ import annotations

import pytest

from models import classify_edition, dedup_editions


class TestClassifyEdition:
    def test_kindle_is_ebook(self):
        is_ebook, is_audio = classify_edition("Kindle Edition")
        assert is_ebook is True
        assert is_audio is False

    def test_epub_is_ebook(self):
        is_ebook, _ = classify_edition("epub")
        assert is_ebook is True

    def test_hardcover_is_physical(self):
        is_ebook, is_audio = classify_edition("Hardcover")
        assert is_ebook is False
        assert is_audio is False

    def test_audiobook_is_audio(self):
        _, is_audio = classify_edition("Audiobook")
        assert is_audio is True

    def test_audible_is_audio(self):
        _, is_audio = classify_edition("Audible Audio")
        assert is_audio is True

    def test_paperback_is_physical(self):
        is_ebook, is_audio = classify_edition("Paperback")
        assert is_ebook is False
        assert is_audio is False

    def test_empty_string_is_physical(self):
        is_ebook, is_audio = classify_edition("")
        assert is_ebook is False
        assert is_audio is False

    def test_none_is_physical(self):
        is_ebook, is_audio = classify_edition(None)
        assert is_ebook is False
        assert is_audio is False

    def test_case_insensitive(self):
        is_ebook, _ = classify_edition("KINDLE EDITION")
        assert is_ebook is True

    def test_digital_is_ebook(self):
        is_ebook, _ = classify_edition("Digital")
        assert is_ebook is True

    def test_mp3_cd_is_audio(self):
        _, is_audio = classify_edition("MP3 CD")
        assert is_audio is True

    def test_pdf_is_ebook(self):
        is_ebook, _ = classify_edition("pdf")
        assert is_ebook is True


class TestDedupEditions:
    def _make_edition(self, fmt, lang, rating_count=0):
        return {
            "Format": fmt,
            "Language": lang,
            "RatingCount": rating_count,
            "ForeignId": hash((fmt, lang, rating_count)) % 100000,
        }

    def test_dedup_keeps_both_kindle_and_hardcover(self):
        """Bug 1 fix: same language, different format category → both kept."""
        editions = [
            self._make_edition("Kindle Edition", "English"),
            self._make_edition("Hardcover", "English"),
        ]
        result = dedup_editions(editions)
        assert len(result) == 2

    def test_dedup_removes_duplicate_kindle(self):
        editions = [
            self._make_edition("Kindle Edition", "English", rating_count=100),
            self._make_edition("Kindle Edition", "English", rating_count=50),
        ]
        result = dedup_editions(editions)
        assert len(result) == 1

    def test_dedup_keeps_highest_rated_when_duplicate(self):
        editions = [
            self._make_edition("Kindle Edition", "English", rating_count=50),
            self._make_edition("Kindle Edition", "English", rating_count=100),
        ]
        result = dedup_editions(editions)
        assert result[0]["RatingCount"] == 100

    def test_dedup_keeps_different_languages(self):
        editions = [
            self._make_edition("Kindle Edition", "English"),
            self._make_edition("Kindle Edition", "French"),
        ]
        result = dedup_editions(editions)
        assert len(result) == 2

    def test_dedup_audio_and_ebook_both_kept(self):
        editions = [
            self._make_edition("Kindle Edition", "English"),
            self._make_edition("Audiobook", "English"),
        ]
        result = dedup_editions(editions)
        assert len(result) == 2

    def test_dedup_empty_returns_empty(self):
        assert dedup_editions([]) == []
