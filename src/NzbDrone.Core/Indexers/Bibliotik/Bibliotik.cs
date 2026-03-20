using System.Collections.Generic;
using System.Threading.Tasks;
using FluentValidation.Results;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Parser;

namespace NzbDrone.Core.Indexers.Bibliotik
{
    public class Bibliotik : HttpIndexerBase<BibliotikSettings>
    {
        public override string Name => "Bibliotik";
        public override DownloadProtocol Protocol => DownloadProtocol.Torrent;
        public override bool SupportsRss => true;
        public override bool SupportsSearch => true;
        public override int PageSize => 50;

        private readonly ICached<Dictionary<string, string>> _authCookieCache;

        public Bibliotik(IHttpClient httpClient,
                         ICacheManager cacheManager,
                         IIndexerStatusService indexerStatusService,
                         IConfigService configService,
                         IParsingService parsingService,
                         Logger logger)
            : base(httpClient, indexerStatusService, configService, parsingService, logger)
        {
            _authCookieCache = cacheManager.GetCache<Dictionary<string, string>>(GetType(), "authCookies");
        }

        public override IIndexerRequestGenerator GetRequestGenerator()
        {
            return new BibliotikRequestGenerator
            {
                Settings = Settings,
                HttpClient = _httpClient,
                Logger = _logger,
                AuthCookieCache = _authCookieCache
            };
        }

        public override IParseIndexerResponse GetParser()
        {
            return new BibliotikParser(Settings.BaseUrl);
        }

        protected override async Task Test(List<ValidationFailure> failures)
        {
            _authCookieCache.Remove(Settings.BaseUrl.Trim().TrimEnd('/'));
            await base.Test(failures);
        }
    }
}
