using System.Collections.Generic;
using NzbDrone.Common.Http;
using NzbDrone.Core.IndexerSearch.Definitions;

namespace NzbDrone.Core.Indexers.MyAnonamouse
{
    public class MyAnonamouseRequestGenerator : IIndexerRequestGenerator
    {
        public MyAnonamouseSettings Settings { get; set; }

        public IndexerPageableRequestChain GetRecentRequests()
        {
            var pageableRequests = new IndexerPageableRequestChain();
            pageableRequests.Add(GetRequest());
            return pageableRequests;
        }

        public IndexerPageableRequestChain GetSearchRequests(BookSearchCriteria searchCriteria)
        {
            var pageableRequests = new IndexerPageableRequestChain();
            pageableRequests.Add(GetRequest());
            return pageableRequests;
        }

        public IndexerPageableRequestChain GetSearchRequests(AuthorSearchCriteria searchCriteria)
        {
            var pageableRequests = new IndexerPageableRequestChain();
            pageableRequests.Add(GetRequest());
            return pageableRequests;
        }

        private IEnumerable<IndexerRequest> GetRequest()
        {
            var request = new IndexerRequest("https://www.myanonamouse.net/tor/js/loadSearchJSONbasic.php", HttpAccept.Json);
            request.HttpRequest.Cookies["mam_id"] = Settings.Cookie;
            yield return request;
        }
    }
}
