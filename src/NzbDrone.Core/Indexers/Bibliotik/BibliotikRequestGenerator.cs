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

            // Send the cookie value exactly as copied from the browser — do NOT UrlDecode it.
            // .NET's Cookie class wraps values containing literal '=' in double-quotes, which
            // Bibliotik does not accept. The raw cookie value uses %3D instead of '=', avoiding
            // the quoting behaviour.
            request.Cookies["id"] = Settings.Cookie;

            yield return new IndexerRequest(request);
        }
    }
}
