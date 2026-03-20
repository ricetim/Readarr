using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Core.IndexerSearch.Definitions;

namespace NzbDrone.Core.Indexers.Bibliotik
{
    public class BibliotikRequestGenerator : IIndexerRequestGenerator
    {
        public BibliotikSettings Settings { get; set; }
        public ICached<Dictionary<string, string>> AuthCookieCache { get; set; }
        public IHttpClient HttpClient { get; set; }
        public Logger Logger { get; set; }

        public IndexerPageableRequestChain GetRecentRequests()
        {
            var pageableRequests = new IndexerPageableRequestChain();
            pageableRequests.Add(GetSearchRequests(searchQuery: null, categories: new[] { 3, 5 }));
            return pageableRequests;
        }

        public IndexerPageableRequestChain GetSearchRequests(BookSearchCriteria searchCriteria)
        {
            var pageableRequests = new IndexerPageableRequestChain();
            var query = BuildSearchQuery(authorName: searchCriteria.AuthorQuery, bookTitle: searchCriteria.BookQuery);
            pageableRequests.Add(GetSearchRequests(searchQuery: query, categories: new[] { 3, 5 }));
            return pageableRequests;
        }

        public IndexerPageableRequestChain GetSearchRequests(AuthorSearchCriteria searchCriteria)
        {
            var pageableRequests = new IndexerPageableRequestChain();
            var query = BuildSearchQuery(authorName: searchCriteria.AuthorQuery, bookTitle: null);
            pageableRequests.Add(GetSearchRequests(searchQuery: query, categories: new[] { 3, 5 }));
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

        private IEnumerable<IndexerRequest> GetSearchRequests(string searchQuery, int[] categories)
        {
            Authenticate().GetAwaiter().GetResult();

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

            var cookies = AuthCookieCache.Find(baseUrl);
            if (cookies != null)
            {
                requestBuilder.SetCookies(cookies);
            }

            yield return new IndexerRequest(requestBuilder.Build());
        }

        private async Task Authenticate()
        {
            var baseUrl = Settings.BaseUrl.Trim().TrimEnd('/');
            var cookies = AuthCookieCache.Find(baseUrl);

            if (cookies != null)
            {
                return;
            }

            Logger.Debug("Authenticating with Bibliotik");

            var requestBuilder = new HttpRequestBuilder(baseUrl + "/")
            {
                LogResponseContent = true,
                Method = HttpMethod.Post
            };

            requestBuilder.PostProcess += r => r.RequestTimeout = TimeSpan.FromSeconds(15);

            var loginRequest = requestBuilder
                .AddFormParameter("username", Settings.Username)
                .AddFormParameter("password", Settings.Password)
                .AddFormParameter("keeplogged", "1")
                .AddFormParameter("login", "Log In!")
                .Build();

            var response = await HttpClient.ExecuteAsync(loginRequest);

            if (response.Content.Contains("<center>"))
            {
                Logger.Warn("Bibliotik login failed — check username and password.");
                throw new Exception("Bibliotik authentication failed. Verify username and password.");
            }

            var sessionCookies = response.GetCookies();
            AuthCookieCache.Set(baseUrl, sessionCookies);

            Logger.Debug("Bibliotik authentication succeeded.");
        }
    }
}
