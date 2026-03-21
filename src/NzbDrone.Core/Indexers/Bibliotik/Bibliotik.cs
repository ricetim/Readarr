using NLog;
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

        public Bibliotik(IHttpClient httpClient,
                         IIndexerStatusService indexerStatusService,
                         IConfigService configService,
                         IParsingService parsingService,
                         Logger logger)
            : base(httpClient, indexerStatusService, configService, parsingService, logger)
        {
        }

        public override IIndexerRequestGenerator GetRequestGenerator()
        {
            return new BibliotikRequestGenerator { Settings = Settings };
        }

        public override IParseIndexerResponse GetParser()
        {
            return new BibliotikParser(Settings.BaseUrl);
        }
    }
}
