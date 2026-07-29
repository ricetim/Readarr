using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.RegularExpressions;
using NzbDrone.Common.Http;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Parser;

namespace NzbDrone.Core.Indexers.MyAnonamouse
{
    public class MyAnonamouseRequestGenerator : IIndexerRequestGenerator
    {
        private const string SearchUrl = "https://www.myanonamouse.net/tor/js/loadSearchJSONbasic.php";

        // MAM stores initialed names with spaces ("C J Cherryh", not "C.J. Cherryh"), so periods in the search text never match.
        private static readonly Regex MultiSpace = new Regex(@"\s+", RegexOptions.Compiled);

        public MyAnonamouseSettings Settings { get; set; }
        public DateTime? LastRssSyncDate { get; set; }

        public IndexerPageableRequestChain GetRecentRequests()
        {
            var pageableRequests = new IndexerPageableRequestChain();
            pageableRequests.Add(GetRecentRequest());
            return pageableRequests;
        }

        public IndexerPageableRequestChain GetSearchRequests(BookSearchCriteria searchCriteria)
        {
            var pageableRequests = new IndexerPageableRequestChain();
            pageableRequests.AddTier(GetBookSearchRequests(searchCriteria));
            return pageableRequests;
        }

        public IndexerPageableRequestChain GetSearchRequests(AuthorSearchCriteria searchCriteria)
        {
            var pageableRequests = new IndexerPageableRequestChain();
            pageableRequests.Add(GetAuthorSearchRequest(searchCriteria));
            return pageableRequests;
        }

        private IEnumerable<IndexerRequest> GetRecentRequest()
        {
            var torBody = new System.Collections.Generic.Dictionary<string, object>
            {
                { "main_cat", new[] { 13, 14 } },
                { "sortType", "dateDesc" },
                { "searchType", "all" },
                { "startNumber", 0 }
            };

            if (LastRssSyncDate.HasValue)
            {
                torBody["startDate"] = LastRssSyncDate.Value.ToString("yyyy-MM-dd HH:mm:ss");
            }

            var body = new Dictionary<string, object>
            {
                { "tor", torBody },
                { "perpage", 100 }
            };

            yield return BuildRequest(body);
        }

        private IEnumerable<IndexerRequest> GetBookSearchRequests(BookSearchCriteria searchCriteria)
        {
            var searchType = MapSearchType(Settings.SearchType);

            // Text search — include author name to narrow results (MAM titles are "Author - Title [FORMAT]")
            // Use raw title (not BookQuery, which URL-encodes spaces as '+')
            var bookTitle = searchCriteria.BookTitle.SplitBookTitle(searchCriteria.Author.Name).Item1;
            var searchText = NormalizeSearchText($"{searchCriteria.Author.Name} {bookTitle}");
            var torBody = new Dictionary<string, object>
            {
                { "main_cat", new[] { 13, 14 } },
                { "searchType", searchType },
                { "sortType", "default" },
                { "startNumber", 0 },
                { "text", searchText },
                { "srchIn", new[] { "title", "author" } }
            };

            var body = new Dictionary<string, object>
            {
                { "tor", torBody },
                { "perpage", 100 }
            };

            yield return BuildRequest(body);

            // ISBN search
            if (!string.IsNullOrEmpty(searchCriteria.BookIsbn))
            {
                var isbnTorBody = new Dictionary<string, object>
                {
                    { "main_cat", new[] { 13, 14 } },
                    { "searchType", searchType },
                    { "sortType", "default" },
                    { "startNumber", 0 }
                };

                var isbnBody = new Dictionary<string, object>
                {
                    { "tor", isbnTorBody },
                    { "perpage", 100 },
                    { "isbn", searchCriteria.BookIsbn }
                };

                yield return BuildRequest(isbnBody);
            }
        }

        private IEnumerable<IndexerRequest> GetAuthorSearchRequest(AuthorSearchCriteria searchCriteria)
        {
            var searchType = MapSearchType(Settings.SearchType);

            var torBody = new Dictionary<string, object>
            {
                { "main_cat", new[] { 13, 14 } },
                { "searchType", searchType },
                { "sortType", "default" },
                { "startNumber", 0 },
                { "text", NormalizeSearchText(searchCriteria.Author.Name) },
                { "srchIn", new[] { "author" } }
            };

            var body = new Dictionary<string, object>
            {
                { "tor", torBody },
                { "perpage", 100 }
            };

            yield return BuildRequest(body);
        }

        private IndexerRequest BuildRequest(Dictionary<string, object> body)
        {
            // MAM only returns descriptions when this flag is present on the request.
            // It is request-level, not per-release: all results carry it, or none do.
            // Set here rather than at each call site so no request can omit it.
            body["description"] = string.Empty;

            var httpRequest = new HttpRequestBuilder(SearchUrl)
                .Accept(HttpAccept.Json)
                .Build();

            httpRequest.Method = HttpMethod.Post;
            httpRequest.Headers.ContentType = "application/json";
            httpRequest.SetContent(body.ToJson());
            httpRequest.Cookies["mam_id"] = Settings.Cookie;

            return new IndexerRequest(httpRequest);
        }

        private static string NormalizeSearchText(string text)
        {
            return MultiSpace.Replace(text.Replace('.', ' '), " ").Trim();
        }

        private static string MapSearchType(int searchType)
        {
            switch (searchType)
            {
                case 0:
                    return "active";
                case 2:
                    return "inactive";
                default:
                    return "all";
            }
        }
    }
}
