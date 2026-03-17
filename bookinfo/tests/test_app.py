"""Test the FastAPI app routes (stateless version).

Tests focus on routing behavior (correct status codes, response structure)
and mocked GoodreadsClient. No SQLite or cache_module involved.
"""
from __future__ import annotations

import importlib
import time
from unittest.mock import AsyncMock, MagicMock, patch

import pytest
import pytest_asyncio
from httpx import ASGITransport, AsyncClient


@pytest_asyncio.fixture
async def app_and_client():
    mock_gr = MagicMock()
    mock_gr.resolve_author_xml = AsyncMock(
        return_value=("kca://author/amzn1.gr.author.v1.Test", "C.S. Lewis", "", "")
    )
    mock_gr.fetch_author_fast_path = AsyncMock(
        return_value=(
            {
                "ForeignId": 3389,
                "Name": "C.S. Lewis",
                "Kca": "kca://author/amzn1.gr.author.v1.Test",
                "Works": [{"ForeignId": 111, "Books": [], "Title": "Narnia"}],
                "Series": [],
            },
            "next_token_abc",
        )
    )
    mock_gr.complete_author_background = AsyncMock(
        return_value={
            "ForeignId": 3389,
            "Name": "C.S. Lewis",
            "Kca": "kca://author/amzn1.gr.author.v1.Test",
            "Works": [
                {"ForeignId": 111, "Books": [], "Title": "Narnia"},
                {"ForeignId": 222, "Books": [], "Title": "Perelandra"},
            ],
            "Series": [],
        }
    )
    mock_gr.batch_graphql = AsyncMock(return_value=[])
    mock_gr.search = AsyncMock(return_value=[])
    mock_gr.get_series = AsyncMock(return_value=None)
    mock_gr.close = AsyncMock()

    # Reload app module for clean state, then inject the mock directly.
    # We bypass the lifespan (which would open real connections) by setting
    # goodreads_client directly on the module after reload.
    import app as app_module
    importlib.reload(app_module)
    app_module.goodreads_client = mock_gr
    app_module._pending_complete.clear()

    transport = ASGITransport(app=app_module.app)
    async with AsyncClient(transport=transport, base_url="http://test") as client:
        yield client, mock_gr

    # Cleanup after test
    app_module._pending_complete.clear()


@pytest.mark.asyncio
async def test_get_author_returns_partial_immediately(app_and_client):
    client, mock_gr = app_and_client
    response = await client.get("/author/3389")
    assert response.status_code == 200
    data = response.json()
    assert data["ForeignId"] == 3389
    assert data["Kca"] == "kca://author/amzn1.gr.author.v1.Test"


@pytest.mark.asyncio
async def test_get_author_passes_kca_query_param(app_and_client):
    client, mock_gr = app_and_client
    kca = "kca://author/amzn1.gr.author.v1.Test"
    response = await client.get(f"/author/3389?kca={kca}")
    assert response.status_code == 200
    call_kwargs = mock_gr.fetch_author_fast_path.call_args
    assert call_kwargs.kwargs.get("author_kca") == kca or (
        len(call_kwargs.args) >= 3 and call_kwargs.args[2] == kca
    )


@pytest.mark.asyncio
async def test_get_author_skips_xml_when_kca_provided(app_and_client):
    client, mock_gr = app_and_client
    response = await client.get("/author/3389?kca=kca://author/amzn1.gr.author.v1.Test")
    assert response.status_code == 200
    mock_gr.resolve_author_xml.assert_not_called()


@pytest.mark.asyncio
async def test_get_author_calls_xml_when_no_kca(app_and_client):
    client, mock_gr = app_and_client
    response = await client.get("/author/3389")
    assert response.status_code == 200
    mock_gr.resolve_author_xml.assert_called_once_with(3389)


@pytest.mark.asyncio
async def test_get_author_complete_available_via_pending(app_and_client):
    client, mock_gr = app_and_client
    import app as app_module

    complete_data = {
        "ForeignId": 3389,
        "Name": "C.S. Lewis",
        "Kca": "kca://author/amzn1.gr.author.v1.Test",
        "Works": [
            {"ForeignId": 111, "Books": [], "Title": "Narnia"},
            {"ForeignId": 222, "Books": [], "Title": "Perelandra"},
        ],
        "Series": [],
    }
    app_module._pending_complete[3389] = (complete_data, time.monotonic() + 3600)

    response = await client.get("/author/3389")
    assert response.status_code == 200
    data = response.json()
    # Should return complete data (2 works) from _pending_complete
    assert len(data["Works"]) == 2
    assert data["Works"][1]["Title"] == "Perelandra"
    # Entry should be popped (one-time pickup)
    assert 3389 not in app_module._pending_complete


@pytest.mark.asyncio
async def test_author_changed_returns_limited(app_and_client):
    client, mock_gr = app_and_client
    response = await client.get("/author/changed")
    assert response.status_code == 200
    assert response.json() == {"Limited": True, "Ids": []}


@pytest.mark.asyncio
async def test_work_returns_404(app_and_client):
    client, mock_gr = app_and_client
    response = await client.get("/work/12345")
    assert response.status_code == 404


@pytest.mark.asyncio
async def test_recommended_returns_empty(app_and_client):
    client, mock_gr = app_and_client
    response = await client.get("/recommended")
    assert response.status_code == 200
    assert response.json() == {"WorkIds": []}


@pytest.mark.asyncio
async def test_search_delegates_to_goodreads_client(app_and_client):
    client, mock_gr = app_and_client
    mock_gr.search = AsyncMock(
        return_value=[{"BookId": 17910048, "WorkId": 24241248, "Author": {"Id": 3389}}]
    )
    response = await client.get("/search?q=Narnia")
    assert response.status_code == 200
    results = response.json()
    assert len(results) == 1
    assert results[0]["BookId"] == 17910048


@pytest.mark.asyncio
async def test_series_not_found_returns_404(app_and_client):
    client, mock_gr = app_and_client
    # mock_gr.get_series already returns None by default
    response = await client.get("/series/99999")
    assert response.status_code == 404


@pytest.mark.asyncio
async def test_series_found_returns_data(app_and_client):
    client, mock_gr = app_and_client
    mock_gr.get_series = AsyncMock(
        return_value={
            "ForeignId": 330959,
            "Title": "The Chronicles of Narnia",
            "Description": "",
            "LinkItems": [],
        }
    )
    response = await client.get("/series/330959")
    assert response.status_code == 200
    assert response.json()["Title"] == "The Chronicles of Narnia"


@pytest.mark.asyncio
async def test_book_edition_not_found_returns_404(app_and_client):
    client, mock_gr = app_and_client
    # batch_graphql returns [] by default
    response = await client.get("/book/99999")
    assert response.status_code == 404


@pytest.mark.asyncio
async def test_book_edition_found_redirects_to_author(app_and_client):
    client, mock_gr = app_and_client
    mock_gr.batch_graphql = AsyncMock(
        return_value=[
            {
                "primaryContributorEdge": {
                    "node": {"legacyId": 3389, "name": "C.S. Lewis"}
                }
            }
        ]
    )
    response = await client.get("/book/42640737", follow_redirects=False)
    assert response.status_code == 302
    assert "/author/3389" in response.headers["location"]
