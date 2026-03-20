using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;

namespace NzbDrone.Core.DecisionEngine.Specifications
{
    public class RepackSpecification : IDecisionEngineSpecification
    {
        private readonly IMediaFileService _mediaFileService;
        private readonly UpgradableSpecification _upgradableSpecification;
        private readonly IConfigService _configService;
        private readonly Logger _logger;

        public RepackSpecification(IMediaFileService mediaFileService, UpgradableSpecification upgradableSpecification, IConfigService configService, Logger logger)
        {
            _mediaFileService = mediaFileService;
            _upgradableSpecification = upgradableSpecification;
            _configService = configService;
            _logger = logger;
        }

        public SpecificationPriority Priority => SpecificationPriority.Database;
        public RejectionType Type => RejectionType.Permanent;

        public Decision IsSatisfiedBy(RemoteBook subject, SearchCriteriaBase searchCriteria)
        {
            if (!subject.ParsedBookInfo.Quality.Revision.IsRepack)
            {
                return Decision.Accept();
            }

            var downloadPropersAndRepacks = _configService.DownloadPropersAndRepacks;

            if (downloadPropersAndRepacks == ProperDownloadTypes.DoNotPrefer)
            {
                _logger.Debug("Repacks are not preferred, skipping check");
                return Decision.Accept();
            }

            foreach (var book in subject.Books)
            {
                var bookFiles = _mediaFileService.GetFilesByBook(book.Id);

                foreach (var file in bookFiles)
                {
                    if (_upgradableSpecification.IsRevisionUpgrade(file.Quality, subject.ParsedBookInfo.Quality))
                    {
                        if (downloadPropersAndRepacks == ProperDownloadTypes.DoNotUpgrade)
                        {
                            _logger.Debug("Auto downloading of repacks is disabled");
                            return Decision.Reject("Repack downloading is disabled");
                        }
                    }
                }
            }

            return Decision.Accept();
        }
    }
}
