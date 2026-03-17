using System.Net.Http;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.Books.Events;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.MetadataSource.BookInfo
{
    public class BookInfoCacheService : IHandle<AuthorDeletedEvent>
    {
        private readonly IHttpClient _httpClient;
        private readonly IMetadataRequestBuilder _requestBuilder;
        private readonly Logger _logger;

        public BookInfoCacheService(IHttpClient httpClient,
                                    IMetadataRequestBuilder requestBuilder,
                                    Logger logger)
        {
            _httpClient = httpClient;
            _requestBuilder = requestBuilder;
            _logger = logger;
        }

        public void Handle(AuthorDeletedEvent message)
        {
            var foreignAuthorId = message.Author.ForeignAuthorId;
            if (string.IsNullOrWhiteSpace(foreignAuthorId))
            {
                return;
            }

            try
            {
                var httpRequest = _requestBuilder.GetRequestBuilder().Create()
                    .SetSegment("route", $"cache/author/{foreignAuthorId}")
                    .Build();

                httpRequest.Method = HttpMethod.Delete;
                httpRequest.SuppressHttpError = true;

                _httpClient.Execute(httpRequest);
                _logger.Debug("Invalidated bookinfo cache for author {0}", foreignAuthorId);
            }
            catch (NzbDrone.Common.Http.HttpException ex)
            {
                _logger.Warn(ex, "Failed to invalidate bookinfo cache for author {0}", foreignAuthorId);
            }
        }
    }
}
