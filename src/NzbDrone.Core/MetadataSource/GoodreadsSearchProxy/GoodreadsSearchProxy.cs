using System;
using System.Collections.Generic;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.Http;

namespace NzbDrone.Core.MetadataSource.Goodreads
{
    public interface IGoodreadsSearchProxy
    {
        public List<SearchJsonResource> Search(string query);
    }

    public class GoodreadsSearchProxy : IGoodreadsSearchProxy
    {
        private readonly ICachedHttpResponseService _cachedHttpClient;
        private readonly IMetadataRequestBuilder _requestBuilder;
        private readonly Logger _logger;

        public GoodreadsSearchProxy(ICachedHttpResponseService cachedHttpClient,
            IMetadataRequestBuilder requestBuilder,
            Logger logger)
        {
            _cachedHttpClient = cachedHttpClient;
            _requestBuilder = requestBuilder;
            _logger = logger;
        }

        public List<SearchJsonResource> Search(string query)
        {
            try
            {
                var httpRequest = _requestBuilder.GetRequestBuilder().Create()
                    .SetSegment("route", "search")
                    .AddQueryParam("q", query)
                    .Build();

                var response = _cachedHttpClient.Get<List<SearchJsonResource>>(httpRequest, true, TimeSpan.FromDays(5));
                var resources = response.Resource ?? new List<SearchJsonResource>();

                // Don't return stale empty cache — re-fetch if empty so a previously-failed
                // search (e.g. before bookinfo was running) doesn't stay broken for 5 days.
                if (resources.Count == 0)
                {
                    response = _cachedHttpClient.Get<List<SearchJsonResource>>(httpRequest, false, TimeSpan.FromDays(5));
                    resources = response.Resource ?? new List<SearchJsonResource>();
                }

                return resources;
            }
            catch (HttpException ex)
            {
                _logger.Warn(ex);
                throw new GoodreadsException("Search for '{0}' failed. Unable to communicate with metadata service.", ex, query);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex);
                throw new GoodreadsException("Search for '{0}' failed. Invalid response received from metadata service.", ex, query);
            }
        }
    }
}
