from __future__ import annotations

import json
import time

import httpx
import pytest
import respx

from goodreads import GRAPHQL_KEY, GRAPHQL_URL, GoodreadsClient, RateLimiter


class TestRateLimiter:
    async def test_first_request_is_immediate(self):
        rl = RateLimiter(rate=100)  # 100/s — first call should not wait
        start = time.monotonic()
        await rl.acquire()
        assert time.monotonic() - start < 0.05

    async def test_second_request_waits_for_interval(self):
        rl = RateLimiter(rate=10)  # 10/s = 0.1s interval
        await rl.acquire()
        start = time.monotonic()
        await rl.acquire()
        assert time.monotonic() - start >= 0.09


class TestGraphQLClient:
    @respx.mock
    async def test_sends_correct_headers(self):
        respx.post(GRAPHQL_URL).mock(
            return_value=httpx.Response(200, json={"data": {}})
        )
        client = GoodreadsClient()
        await client.graphql_query("query { stub }", {})
        request = respx.calls[0].request
        assert request.headers["x-api-key"] == GRAPHQL_KEY
        assert "application/json" in request.headers["content-type"]
        await client.close()

    @respx.mock
    async def test_returns_data_field(self):
        respx.post(GRAPHQL_URL).mock(
            return_value=httpx.Response(200, json={"data": {"result": "value"}})
        )
        client = GoodreadsClient()
        result = await client.graphql_query("query { stub }", {})
        assert result == {"result": "value"}
        await client.close()

    @respx.mock
    async def test_batch_graphql_sends_aliases(self):
        captured: dict = {}

        def capture(request):
            captured["body"] = json.loads(request.content)
            return httpx.Response(
                200, json={"data": {"q0": {"legacyId": 1, "work": {}}, "q1": {"legacyId": 2, "work": {}}}}
            )

        respx.post(GRAPHQL_URL).mock(side_effect=capture)
        client = GoodreadsClient()
        results = await client.batch_graphql([1, 2])
        assert "q0" in captured["body"]["query"]
        assert "q1" in captured["body"]["query"]
        assert len(results) == 2
        await client.close()

    @respx.mock
    async def test_batch_graphql_chunks_at_batch_size(self):
        """batch_size=2 with 3 IDs should produce 2 HTTP requests."""
        call_count = 0

        def respond(request):
            nonlocal call_count
            call_count += 1
            body = json.loads(request.content)
            # Return one result per alias found in query
            data = {}
            for i in range(body["query"].count("q") - body["query"].count("query")):
                data[f"q{i}"] = {"legacyId": i, "work": {}}
            return httpx.Response(200, json={"data": data})

        respx.post(GRAPHQL_URL).mock(side_effect=respond)
        client = GoodreadsClient(batch_size=2)
        await client.batch_graphql([1, 2, 3])
        assert call_count == 2
        await client.close()


from goodreads import XML_KEY


class TestAuthorWorksPage:
    @respx.mock
    async def test_returns_filtered_works_list(self):
        respx.post(GRAPHQL_URL).mock(
            return_value=httpx.Response(
                200,
                json={
                    "data": {
                        "getWorksByContributor": {
                            "edges": [
                                {
                                    "node": {
                                        "id": "kca://work/v1.W1",
                                        "bestBook": {
                                            "legacyId": 17910048,
                                            "primaryContributorEdge": {
                                                "role": "Author",
                                                "node": {"legacyId": 6949698},
                                            },
                                            "secondaryContributorEdges": [],
                                        },
                                    }
                                }
                            ],
                            "pageInfo": {"hasNextPage": False, "nextPageToken": None},
                        }
                    }
                },
            )
        )
        client = GoodreadsClient()
        works, next_token = await client.get_author_works_page(
            kca="kca://author/v1.A1", author_id=6949698, token=None
        )
        assert len(works) == 1
        assert works[0]["legacyId"] == 17910048
        assert next_token is None
        await client.close()

    @respx.mock
    async def test_excludes_translator_role(self):
        respx.post(GRAPHQL_URL).mock(
            return_value=httpx.Response(
                200,
                json={
                    "data": {
                        "getWorksByContributor": {
                            "edges": [
                                {
                                    "node": {
                                        "id": "kca://work/v1.W1",
                                        "bestBook": {
                                            "legacyId": 99,
                                            "primaryContributorEdge": {
                                                "role": "Translator",
                                                "node": {"legacyId": 6949698},
                                            },
                                            "secondaryContributorEdges": [],
                                        },
                                    }
                                }
                            ],
                            "pageInfo": {"hasNextPage": False, "nextPageToken": None},
                        }
                    }
                },
            )
        )
        client = GoodreadsClient()
        works, _ = await client.get_author_works_page(
            kca="kca://author/v1.A1", author_id=6949698, token=None
        )
        assert len(works) == 0
        await client.close()


class TestSeriesFetch:
    @respx.mock
    async def test_get_series_parses_xml(self):
        xml_body = """<?xml version="1.0"?>
<GoodreadsResponse>
  <series>
    <id>330959</id>
    <title>The Chronicles of Osreth</title>
    <description>A series.</description>
    <series_works>
      <series_work>
        <user_position>1</user_position>
        <work><id>24241248</id></work>
      </series_work>
    </series_works>
  </series>
</GoodreadsResponse>"""
        respx.get(
            f"https://www.goodreads.com/series/show/330959.xml?key={XML_KEY}"
        ).mock(return_value=httpx.Response(200, text=xml_body))

        client = GoodreadsClient()
        series = await client.get_series(330959)
        assert series["ForeignId"] == 330959
        assert series["Title"] == "The Chronicles of Osreth"
        assert len(series["LinkItems"]) == 1
        assert series["LinkItems"][0]["ForeignWorkId"] == 24241248
        assert series["LinkItems"][0]["PositionInSeries"] == "1"
        await client.close()


class TestGetEditionsPage:
    @respx.mock
    async def test_returns_edition_ids_and_token(self):
        respx.post(GRAPHQL_URL).mock(
            return_value=httpx.Response(
                200,
                json={
                    "data": {
                        "getEditions": {
                            "edges": [
                                {"node": {"id": "kca://book/v1.B1", "legacyId": 17910048}},
                                {"node": {"id": "kca://book/v1.B2", "legacyId": 42640737}},
                            ],
                            "pageInfo": {"hasNextPage": False, "nextPageToken": None},
                        }
                    }
                },
            )
        )
        client = GoodreadsClient()
        edition_ids, next_token = await client.get_editions_page("kca://work/v1.W1", token=None)
        assert edition_ids == [17910048, 42640737]
        assert next_token is None
        await client.close()


from unittest.mock import AsyncMock
from goodreads import map_book, map_work

_GR_BOOK = {
    "id": "kca://book/v1.B1",
    "legacyId": 42640737,
    "title": "The Goblin Emperor",
    "titlePrimary": "The Goblin Emperor",
    "description": "A fantasy novel.",
    "imageUrl": "https://example.com/cover.jpg",
    "webUrl": "https://www.goodreads.com/book/show/42640737",
    "details": {
        "asin": "B00KLJ5OFQ",
        "isbn13": "9780765326997",
        "format": "Kindle Edition",
        "numPages": 446,
        "language": {"name": "English"},
        "publisher": "Tor Books",
        "publicationTime": "2014-04-01T07:00:00.000Z",
        "officialUrl": None,
    },
    "stats": {"ratingsCount": 42175, "ratingsSum": 170056, "averageRating": 4.03},
    "primaryContributorEdge": {
        "node": {
            "id": "kca://author/v1.A1",
            "legacyId": 6949698,
            "name": "Katherine Addison",
            "webUrl": "https://www.goodreads.com/author/show/6949698",
        }
    },
    "bookGenres": [{"genre": {"name": "Fantasy"}}],
    "bookSeries": [],
    "work": {
        "id": "kca://work/v1.W1",
        "legacyId": 24241248,
        "details": {"webUrl": "https://www.goodreads.com/work/editions/24241248", "publicationTime": "2014-04-01T07:00:00.000Z"},
        "bestBook": {
            "legacyId": 42640737,
            "titlePrimary": "The Goblin Emperor",
            "primaryContributorEdge": {"role": "Author", "node": {"legacyId": 6949698}},
        },
        "editions": {"edges": []},
    },
}


class TestMapBook:
    def test_is_ebook_set_from_classify_edition(self):
        result = map_book(_GR_BOOK, author_foreign_id=6949698)
        assert result["IsEbook"] is True
        assert result["Format"] == "Kindle Edition"

    def test_foreign_id_set(self):
        result = map_book(_GR_BOOK, author_foreign_id=6949698)
        assert result["ForeignId"] == 42640737

    def test_contributors_non_empty_with_author_id(self):
        result = map_book(_GR_BOOK, author_foreign_id=6949698)
        assert len(result["Contributors"]) == 1
        assert result["Contributors"][0]["ForeignId"] == 6949698
        assert result["Contributors"][0]["Role"] == "Author"

    def test_hardcover_is_not_ebook(self):
        book = {
            **_GR_BOOK,
            "details": {**_GR_BOOK["details"], "format": "Hardcover"},
        }
        result = map_book(book, author_foreign_id=6949698)
        assert result["IsEbook"] is False

    def test_release_date_parsed(self):
        result = map_book(_GR_BOOK, author_foreign_id=6949698)
        assert result["ReleaseDateRaw"] == "2014-04-01"
        assert "2014-04-01" in result["ReleaseDate"]


class TestFetchAuthorFastPath:
    @respx.mock
    async def test_returns_author_dict_with_works(self):
        # Mock GetAuthorWorks page 1
        respx.post(GRAPHQL_URL).mock(
            return_value=httpx.Response(
                200,
                json={
                    "data": {
                        "getWorksByContributor": {
                            "edges": [
                                {
                                    "node": {
                                        "id": "kca://work/v1.W1",
                                        "bestBook": {
                                            "legacyId": 42640737,
                                            "primaryContributorEdge": {
                                                "role": "Author",
                                                "node": {"legacyId": 6949698},
                                            },
                                            "secondaryContributorEdges": [],
                                        },
                                    }
                                }
                            ],
                            "pageInfo": {"hasNextPage": False, "nextPageToken": None},
                        }
                    }
                },
            )
        )
        batch_mock = AsyncMock(return_value=[_GR_BOOK])

        client = GoodreadsClient()
        author, next_token = await client.fetch_author_fast_path(
            author_id=6949698,
            author_name="Katherine Addison",
            author_kca="kca://author/v1.A1",
            batch_books_fn=batch_mock,
        )
        assert author["ForeignId"] == 6949698
        assert author["Name"] == "Katherine Addison"
        assert len(author["Works"]) == 1
        assert author["Works"][0]["ForeignId"] == 24241248
        assert next_token is None  # pageInfo.hasNextPage was False in the mock
        await client.close()


class TestSearch:
    @respx.mock
    async def test_search_returns_search_resources(self):
        respx.post(GRAPHQL_URL).mock(
            return_value=httpx.Response(
                200,
                json={
                    "data": {
                        "getSearchSuggestions": {
                            "totalCount": 1,
                            "edges": [
                                {
                                    "node": {
                                        "legacyId": 17910048,
                                        "title": "The Goblin Emperor",
                                        "work": {
                                            "legacyId": 24241248,
                                            "bestBook": {
                                                "primaryContributorEdge": {
                                                    "node": {
                                                        "name": "Katherine Addison",
                                                        "legacyId": 6949698,
                                                    }
                                                }
                                            },
                                        },
                                    }
                                }
                            ],
                        }
                    }
                },
            )
        )
        client = GoodreadsClient()
        results = await client.search("Goblin Emperor")
        assert len(results) == 1
        assert results[0]["BookId"] == 17910048
        assert results[0]["WorkId"] == 24241248
        assert results[0]["Author"]["Id"] == 6949698
        await client.close()

    @respx.mock
    async def test_search_returns_empty_list_on_no_results(self):
        respx.post(GRAPHQL_URL).mock(
            return_value=httpx.Response(
                200,
                json={"data": {"getSearchSuggestions": {"totalCount": 0, "edges": []}}},
            )
        )
        client = GoodreadsClient()
        results = await client.search("xyzzy nonexistent")
        assert results == []
        await client.close()


class TestCompleteAuthorBackground:
    async def test_returns_dict_with_foreign_id_and_works(self):
        """complete_author_background should return a dict with ForeignId and Works."""
        client = GoodreadsClient()
        client.get_author_works_page = AsyncMock(return_value=([], None))

        partial = {
            "ForeignId": 6949698,
            "Name": "Test Author",
            "Works": [
                {
                    "ForeignId": 1,
                    "Title": "Some Book",
                    "Books": [{"IsEbook": True, "ForeignId": 100}],
                }
            ],
        }

        result = await client.complete_author_background(
            author_id=6949698,
            partial_data=partial,
            kca="kca://author/v1.A1",
            first_page_next_token=None,
            google_supplement_fn=None,
        )

        assert isinstance(result, dict)
        assert result["ForeignId"] == 6949698
        assert "Works" in result

    async def test_returns_dict_not_none(self):
        """complete_author_background must return a dict (not None)."""
        client = GoodreadsClient()
        client.get_author_works_page = AsyncMock(return_value=([], None))

        partial = {"ForeignId": 6949698, "Name": "Test Author", "Works": []}

        result = await client.complete_author_background(
            author_id=6949698,
            partial_data=partial,
            kca="kca://author/v1.A1",
            first_page_next_token=None,
            google_supplement_fn=None,
        )

        assert result is not None
        assert isinstance(result, dict)

    async def test_runs_google_supplement_for_works_without_ebook(self):
        """Works with no ebook editions should trigger google_supplement_fn."""
        supplement_calls = []

        async def fake_supplement(**kwargs):
            supplement_calls.append(kwargs)
            return None

        client = GoodreadsClient()
        client.get_author_works_page = AsyncMock(return_value=([], None))

        partial = {
            "ForeignId": 6949698,
            "Name": "Test Author",
            "Works": [
                {
                    "ForeignId": 1,
                    "Title": "No Ebook Here",
                    "Books": [{"IsEbook": False, "ForeignId": 100}],
                }
            ],
        }

        result = await client.complete_author_background(
            author_id=6949698,
            partial_data=partial,
            kca="kca://author/v1.A1",
            first_page_next_token=None,
            google_supplement_fn=fake_supplement,
        )

        assert len(supplement_calls) == 1
        assert supplement_calls[0]["title"] == "No Ebook Here"
        assert isinstance(result, dict)


class TestFetchAuthorFastPathKca:
    @respx.mock
    async def test_extracts_kca_from_graphql_contributor_node_when_empty(self):
        """fetch_author_fast_path should extract KCA from primaryContributorEdge.node.id
        when author_kca is empty and include it in the returned partial dict as 'Kca'."""
        # Mock GetAuthorWorks page 1 — must pass a kca even if empty
        respx.post(GRAPHQL_URL).mock(
            return_value=httpx.Response(
                200,
                json={
                    "data": {
                        "getWorksByContributor": {
                            "edges": [
                                {
                                    "node": {
                                        "id": "kca://work/v1.W1",
                                        "bestBook": {
                                            "legacyId": 42640737,
                                            "primaryContributorEdge": {
                                                "role": "Author",
                                                "node": {"legacyId": 6949698},
                                            },
                                            "secondaryContributorEdges": [],
                                        },
                                    }
                                }
                            ],
                            "pageInfo": {"hasNextPage": False, "nextPageToken": None},
                        }
                    }
                },
            )
        )

        # _GR_BOOK has primaryContributorEdge.node.id = "kca://author/v1.A1"
        batch_mock = AsyncMock(return_value=[_GR_BOOK])

        client = GoodreadsClient()
        author, next_token = await client.fetch_author_fast_path(
            author_id=6949698,
            author_name="Katherine Addison",
            author_kca="",  # empty — should be extracted from GraphQL response
            batch_books_fn=batch_mock,
        )
        assert author["Kca"] == "kca://author/v1.A1"
        await client.close()

    @respx.mock
    async def test_does_not_override_kca_when_already_set(self):
        """When author_kca is already set, it should not be overwritten."""
        respx.post(GRAPHQL_URL).mock(
            return_value=httpx.Response(
                200,
                json={
                    "data": {
                        "getWorksByContributor": {
                            "edges": [
                                {
                                    "node": {
                                        "id": "kca://work/v1.W1",
                                        "bestBook": {
                                            "legacyId": 42640737,
                                            "primaryContributorEdge": {
                                                "role": "Author",
                                                "node": {"legacyId": 6949698},
                                            },
                                            "secondaryContributorEdges": [],
                                        },
                                    }
                                }
                            ],
                            "pageInfo": {"hasNextPage": False, "nextPageToken": None},
                        }
                    }
                },
            )
        )

        batch_mock = AsyncMock(return_value=[_GR_BOOK])

        client = GoodreadsClient()
        author, _ = await client.fetch_author_fast_path(
            author_id=6949698,
            author_name="Katherine Addison",
            author_kca="kca://author/v1.EXISTING",
            batch_books_fn=batch_mock,
        )
        # Should keep the original, not overwrite with GraphQL result
        assert author["Kca"] == "kca://author/v1.EXISTING"
        await client.close()


class TestApiKeySelfHealing:
    """Goodreads rotated the AppSync key on 2026-08-29 and every instance broke for days.

    The client now rediscovers the key from a public Goodreads page when a call is
    rejected, so a rotation costs one retry rather than an outage.
    """

    NEW_KEY = "da2-aaaaaaaaaaaaaaaaaaaaaaaaaa"

    def _page(self, key: str) -> str:
        return f'<html><body><script>window.cfg={{"apiKey":"{key}"}}</script></body></html>'

    @respx.mock
    async def test_refreshes_key_and_retries_on_401(self):
        from goodreads import KEY_DISCOVERY_URLS

        calls = []

        def graphql_responder(request):
            calls.append(request.headers["x-api-key"])
            if request.headers["x-api-key"] == self.NEW_KEY:
                return httpx.Response(200, json={"data": {"ok": True}})
            return httpx.Response(401, json={"errors": [{"errorType": "UnauthorizedException"}]})

        respx.post(GRAPHQL_URL).mock(side_effect=graphql_responder)
        respx.get(KEY_DISCOVERY_URLS[0]).mock(
            return_value=httpx.Response(200, text=self._page(self.NEW_KEY))
        )

        client = GoodreadsClient(rate=1000)
        try:
            result = await client._graphql_raw({"query": "query{__typename}"})
        finally:
            await client.close()

        assert result == {"ok": True}  # _graphql_raw unwraps payload["data"]
        assert calls == [GRAPHQL_KEY, self.NEW_KEY], "should retry once with the new key"
        assert client._api_key == self.NEW_KEY

    @respx.mock
    async def test_does_not_retry_forever_when_page_key_is_also_rejected(self):
        from goodreads import KEY_DISCOVERY_URLS

        route = respx.post(GRAPHQL_URL).mock(return_value=httpx.Response(401))
        # Page still publishes the same key that was just rejected.
        respx.get(KEY_DISCOVERY_URLS[0]).mock(
            return_value=httpx.Response(200, text=self._page(GRAPHQL_KEY))
        )

        client = GoodreadsClient(rate=1000)
        try:
            with pytest.raises(httpx.HTTPStatusError):
                await client._graphql_raw({"query": "query{__typename}"})
        finally:
            await client.close()

        assert route.call_count == 1, "must not retry when the discovered key is unchanged"

    @respx.mock
    async def test_surfaces_original_error_when_discovery_page_fails(self):
        from goodreads import KEY_DISCOVERY_URLS

        respx.post(GRAPHQL_URL).mock(return_value=httpx.Response(401))
        respx.get(KEY_DISCOVERY_URLS[0]).mock(return_value=httpx.Response(503))

        client = GoodreadsClient(rate=1000)
        try:
            with pytest.raises(httpx.HTTPStatusError) as exc:
                await client._graphql_raw({"query": "query{__typename}"})
        finally:
            await client.close()

        assert exc.value.response.status_code == 401

    @respx.mock
    async def test_non_auth_errors_do_not_trigger_a_key_refresh(self):
        from goodreads import KEY_DISCOVERY_URLS

        respx.post(GRAPHQL_URL).mock(return_value=httpx.Response(500))
        discovery = respx.get(KEY_DISCOVERY_URLS[0]).mock(
            return_value=httpx.Response(200, text=self._page(self.NEW_KEY))
        )

        client = GoodreadsClient(rate=1000)
        try:
            with pytest.raises(httpx.HTTPStatusError):
                await client._graphql_raw({"query": "query{__typename}"})
        finally:
            await client.close()

        assert not discovery.called, "a 500 is not an auth problem"

    @respx.mock
    async def test_falls_back_to_next_page_when_goodreads_throttles(self):
        """Goodreads answers 202 with an empty body when mitigating bots."""
        from goodreads import KEY_DISCOVERY_URLS

        def graphql_responder(request):
            if request.headers["x-api-key"] == self.NEW_KEY:
                return httpx.Response(200, json={"data": {"ok": True}})
            return httpx.Response(401)

        respx.post(GRAPHQL_URL).mock(side_effect=graphql_responder)
        respx.get(KEY_DISCOVERY_URLS[0]).mock(return_value=httpx.Response(202, text=""))
        second = respx.get(KEY_DISCOVERY_URLS[1]).mock(
            return_value=httpx.Response(200, text=self._page(self.NEW_KEY))
        )

        client = GoodreadsClient(rate=1000)
        try:
            result = await client._graphql_raw({"query": "query{__typename}"})
        finally:
            await client.close()

        assert second.called, "should try the next candidate page"
        assert result == {"ok": True}
        assert client._api_key == self.NEW_KEY

    @respx.mock
    async def test_waf_403_does_not_trigger_a_key_refresh(self):
        """403 is WAFForbiddenException (IP rate-limited), not a rotated key."""
        from goodreads import KEY_DISCOVERY_URLS

        respx.post(GRAPHQL_URL).mock(
            return_value=httpx.Response(
                403, json={"errors": [{"errorType": "WAFForbiddenException"}]}
            )
        )
        discovery = respx.get(KEY_DISCOVERY_URLS[0]).mock(
            return_value=httpx.Response(200, text=self._page(self.NEW_KEY))
        )

        client = GoodreadsClient(rate=1000)
        try:
            with pytest.raises(httpx.HTTPStatusError) as exc:
                await client._graphql_raw({"query": "query{__typename}"})
        finally:
            await client.close()

        assert exc.value.response.status_code == 403
        assert not discovery.called, "a WAF block is not a key rotation"
