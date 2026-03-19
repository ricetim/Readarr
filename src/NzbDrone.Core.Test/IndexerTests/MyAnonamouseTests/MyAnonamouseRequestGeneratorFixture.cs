using System;
using System.Linq;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Indexers.MyAnonamouse;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.IndexerTests.MyAnonamouseTests
{
    [TestFixture]
    public class MyAnonamouseRequestGeneratorFixture : CoreTest<MyAnonamouseRequestGenerator>
    {
        private BookSearchCriteria _bookSearchCriteria;
        private AuthorSearchCriteria _authorSearchCriteria;

        [SetUp]
        public void SetUp()
        {
            Subject.Settings = new MyAnonamouseSettings
            {
                Cookie = "test_cookie",
                SearchType = (int)MyAnonamouseSearchType.All
            };

            _bookSearchCriteria = new BookSearchCriteria
            {
                Author = new Author { Name = "Patrick Rothfuss" },
                BookTitle = "The Name of the Wind",
                BookIsbn = "9780756404079"
            };

            _authorSearchCriteria = new AuthorSearchCriteria
            {
                Author = new Author { Name = "Brandon Sanderson" },
                Books = new System.Collections.Generic.List<Book>()
            };
        }

        private JObject GetRequestBody(NzbDrone.Common.Http.HttpRequest request)
        {
            var body = System.Text.Encoding.UTF8.GetString(request.ContentData);
            return JObject.Parse(body);
        }

        [Test]
        public void recent_request_should_have_correct_categories()
        {
            var results = Subject.GetRecentRequests();
            var request = results.GetAllTiers().First().First().HttpRequest;
            var body = GetRequestBody(request);

            var mainCat = body["tor"]["main_cat"].ToObject<int[]>();
            mainCat.Should().BeEquivalentTo(new[] { 13, 14 });
        }

        [Test]
        public void recent_request_should_sort_by_date_descending()
        {
            var results = Subject.GetRecentRequests();
            var request = results.GetAllTiers().First().First().HttpRequest;
            var body = GetRequestBody(request);

            body["tor"]["sortType"].Value<string>().Should().Be("dateDesc");
        }

        [Test]
        public void recent_request_should_not_include_start_date_when_last_sync_is_null()
        {
            Subject.LastRssSyncDate = null;

            var results = Subject.GetRecentRequests();
            var request = results.GetAllTiers().First().First().HttpRequest;
            var body = GetRequestBody(request);

            body["tor"]["startDate"].Should().BeNull();
        }

        [Test]
        public void recent_request_should_include_start_date_when_last_sync_is_set()
        {
            var syncDate = new DateTime(2024, 6, 15, 10, 30, 0, DateTimeKind.Utc);
            Subject.LastRssSyncDate = syncDate;

            var results = Subject.GetRecentRequests();
            var request = results.GetAllTiers().First().First().HttpRequest;
            var body = GetRequestBody(request);

            body["tor"]["startDate"].Value<string>().Should().Be("2024-06-15 10:30:00");
        }

        [Test]
        public void recent_request_should_set_cookie()
        {
            var results = Subject.GetRecentRequests();
            var request = results.GetAllTiers().First().First().HttpRequest;

            request.Cookies["mam_id"].Should().Be("test_cookie");
        }

        [Test]
        public void book_search_should_include_text_and_search_in_title_and_author()
        {
            var results = Subject.GetSearchRequests(_bookSearchCriteria);
            var request = results.GetAllTiers().First().First().HttpRequest;
            var body = GetRequestBody(request);

            var srchIn = body["tor"]["srchIn"].ToObject<string[]>();
            srchIn.Should().Contain("title");
            srchIn.Should().Contain("author");
            body["tor"]["text"].Should().NotBeNull();
        }

        [Test]
        public void book_search_text_should_include_author_name_to_narrow_results()
        {
            var results = Subject.GetSearchRequests(_bookSearchCriteria);
            var request = results.GetAllTiers().First().First().HttpRequest;
            var body = GetRequestBody(request);

            var text = body["tor"]["text"].Value<string>();
            text.Should().Contain("Patrick Rothfuss");
            text.Should().Contain("Name of the Wind");
        }

        [Test]
        public void book_search_with_isbn_should_produce_two_requests()
        {
            var results = Subject.GetSearchRequests(_bookSearchCriteria);
            var tier = results.GetTier(0).ToList();
            var allRequests = tier.SelectMany(r => r).ToList();

            allRequests.Should().HaveCount(2);
        }

        [Test]
        public void book_search_isbn_request_should_include_isbn()
        {
            var results = Subject.GetSearchRequests(_bookSearchCriteria);
            var tier = results.GetTier(0).ToList();
            var requests = tier.SelectMany(r => r).ToList();

            var isbnRequest = requests[1].HttpRequest;
            var body = GetRequestBody(isbnRequest);

            body["isbn"].Value<string>().Should().Be("9780756404079");
        }

        [Test]
        public void book_search_without_isbn_should_produce_one_request()
        {
            _bookSearchCriteria.BookIsbn = null;

            var results = Subject.GetSearchRequests(_bookSearchCriteria);
            var allRequests = results.GetAllTiers().SelectMany<IndexerPageableRequest, IndexerRequest>(r => r).ToList();

            allRequests.Should().HaveCount(1);
        }

        [Test]
        public void author_search_should_restrict_to_author_field_only()
        {
            var results = Subject.GetSearchRequests(_authorSearchCriteria);
            var request = results.GetAllTiers().First().First().HttpRequest;
            var body = GetRequestBody(request);

            var srchIn = body["tor"]["srchIn"].ToObject<string[]>();
            srchIn.Should().BeEquivalentTo(new[] { "author" });
        }

        [Test]
        public void search_type_active_should_map_to_active()
        {
            Subject.Settings = new MyAnonamouseSettings
            {
                Cookie = "test_cookie",
                SearchType = (int)MyAnonamouseSearchType.Active
            };

            var results = Subject.GetSearchRequests(_bookSearchCriteria);
            var request = results.GetAllTiers().First().First().HttpRequest;
            var body = GetRequestBody(request);

            body["tor"]["searchType"].Value<string>().Should().Be("active");
        }

        [Test]
        public void search_type_inactive_should_map_to_inactive()
        {
            Subject.Settings = new MyAnonamouseSettings
            {
                Cookie = "test_cookie",
                SearchType = (int)MyAnonamouseSearchType.Inactive
            };

            var results = Subject.GetSearchRequests(_bookSearchCriteria);
            var request = results.GetAllTiers().First().First().HttpRequest;
            var body = GetRequestBody(request);

            body["tor"]["searchType"].Value<string>().Should().Be("inactive");
        }

        [Test]
        public void search_type_all_should_map_to_all()
        {
            var results = Subject.GetSearchRequests(_bookSearchCriteria);
            var request = results.GetAllTiers().First().First().HttpRequest;
            var body = GetRequestBody(request);

            body["tor"]["searchType"].Value<string>().Should().Be("all");
        }
    }
}
