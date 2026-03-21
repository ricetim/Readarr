using System.Collections.Generic;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Core.IndexerSearch.Definitions;

namespace NzbDrone.Core.Indexers.Bibliotik
{
    public class BibliotikRequestGenerator : IIndexerRequestGenerator
    {
        public BibliotikSettings Settings { get; set; }

        public IndexerPageableRequestChain GetRecentRequests()
        {
            var pageableRequests = new IndexerPageableRequestChain();
            pageableRequests.Add(BuildRequests(searchQuery: null, categories: new[] { 3, 5 }));
            return pageableRequests;
        }

        public IndexerPageableRequestChain GetSearchRequests(BookSearchCriteria searchCriteria)
        {
            var pageableRequests = new IndexerPageableRequestChain();
            var query = BuildSearchQuery(authorName: searchCriteria.AuthorQuery, bookTitle: searchCriteria.BookQuery);
            pageableRequests.Add(BuildRequests(searchQuery: query, categories: new[] { 3, 5 }));
            return pageableRequests;
        }

        public IndexerPageableRequestChain GetSearchRequests(AuthorSearchCriteria searchCriteria)
        {
            var pageableRequests = new IndexerPageableRequestChain();
            var query = BuildSearchQuery(authorName: searchCriteria.AuthorQuery, bookTitle: null);
            pageableRequests.Add(BuildRequests(searchQuery: query, categories: new[] { 3, 5 }));
            return pageableRequests;
        }

        private static string BuildSearchQuery(string authorName, string bookTitle)
        {
            var parts = new System.Text.StringBuilder();

            if (authorName.IsNotNullOrWhiteSpace())
            {
                parts.Append("@authors ").Append(authorName.Trim());
            }

            if (bookTitle.IsNotNullOrWhiteSpace())
            {
                if (parts.Length > 0)
                {
                    parts.Append(' ');
                }

                parts.Append("@title ").Append(bookTitle.Trim());
            }

            return parts.Length > 0 ? parts.ToString() : null;
        }

        private IEnumerable<IndexerRequest> BuildRequests(string searchQuery, int[] categories)
        {
            var baseUrl = Settings.BaseUrl.Trim().TrimEnd('/');
            var requestBuilder = new HttpRequestBuilder($"{baseUrl}/torrents/")
                .AddQueryParam("orderby", "added")
                .AddQueryParam("order", "DESC");

            if (searchQuery.IsNotNullOrWhiteSpace())
            {
                requestBuilder.AddQueryParam("search", searchQuery);
            }

            foreach (var cat in categories)
            {
                requestBuilder.AddQueryParam("cat[]", cat.ToString());
            }

            var request = requestBuilder.Build();

            // URL-decode the cookie value in case the user copied it from the browser's Network tab
            // (which shows percent-encoded values like %2B for + and %3D for =) rather than the
            // Application tab (which shows the raw decoded value).
            request.Cookies["session"] = System.Net.WebUtility.UrlDecode(Settings.Cookie);

            yield return new IndexerRequest(request);
        }
    }
}
