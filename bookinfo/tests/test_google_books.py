from __future__ import annotations

import httpx
import pytest
import respx

from google_books import _synthetic_foreign_id, supplement_ebook_edition

_VOLUMES_URL = "https://www.googleapis.com/books/v1/volumes"


class TestSyntheticForeignId:
    def test_is_positive_and_deterministic(self):
        id1 = _synthetic_foreign_id(24241248)
        id2 = _synthetic_foreign_id(24241248)
        assert id1 == id2
        assert id1 > 0

    def test_different_work_ids_produce_different_foreign_ids(self):
        assert _synthetic_foreign_id(1) != _synthetic_foreign_id(2)

    def test_uses_sha256_not_builtin_hash(self):
        """Verifies we use SHA-256 (stable) not hash() (PYTHONHASHSEED-randomized)."""
        import hashlib
        digest = hashlib.sha256(b"gbooks:24241248").digest()
        expected = int.from_bytes(digest[:4], "big") % (2**31 - 1) + 1
        assert _synthetic_foreign_id(24241248) == expected


class TestSupplementEbookEdition:
    @respx.mock
    async def test_returns_synthetic_edition_when_ebook_found(self):
        respx.get(_VOLUMES_URL).mock(
            return_value=httpx.Response(
                200,
                json={
                    "items": [
                        {
                            "volumeInfo": {
                                "title": "The Goblin Emperor",
                                "language": "en",
                            },
                            "saleInfo": {"isEbook": True},
                        }
                    ]
                },
            )
        )
        result = await supplement_ebook_edition(
            title="The Goblin Emperor",
            author="Katherine Addison",
            work_id=24241248,
            author_foreign_id=6949698,
        )
        assert result is not None
        assert result["IsEbook"] is True
        assert result["Format"] == "Kindle Edition"
        assert result["Language"] == "eng"
        assert result["Contributors"] == [{"ForeignId": 6949698, "Role": "Author"}]
        assert result["ForeignId"] == _synthetic_foreign_id(24241248)

    @respx.mock
    async def test_returns_none_when_not_for_sale_as_ebook(self):
        respx.get(_VOLUMES_URL).mock(
            return_value=httpx.Response(
                200,
                json={
                    "items": [
                        {
                            "volumeInfo": {"title": "The Goblin Emperor", "language": "en"},
                            "saleInfo": {"isEbook": False},
                        }
                    ]
                },
            )
        )
        result = await supplement_ebook_edition(
            title="The Goblin Emperor",
            author="Katherine Addison",
            work_id=24241248,
            author_foreign_id=6949698,
        )
        assert result is None

    @respx.mock
    async def test_returns_none_when_results_empty(self):
        respx.get(_VOLUMES_URL).mock(
            return_value=httpx.Response(200, json={})
        )
        result = await supplement_ebook_edition(
            title="Unknown Book", author="Nobody", work_id=999, author_foreign_id=1
        )
        assert result is None

    @respx.mock
    async def test_returns_none_for_non_english_result(self):
        respx.get(_VOLUMES_URL).mock(
            return_value=httpx.Response(
                200,
                json={
                    "items": [
                        {
                            "volumeInfo": {"title": "Der Koboldkaiser", "language": "de"},
                            "saleInfo": {"isEbook": True},
                        }
                    ]
                },
            )
        )
        result = await supplement_ebook_edition(
            title="The Goblin Emperor",
            author="Katherine Addison",
            work_id=24241248,
            author_foreign_id=6949698,
        )
        assert result is None
