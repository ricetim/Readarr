using System.Linq;
using NLog;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.DecisionEngine.Specifications
{
    public class LanguageAllowedByProfileSpecification : IDecisionEngineSpecification
    {
        private readonly Logger _logger;

        public LanguageAllowedByProfileSpecification(Logger logger)
        {
            _logger = logger;
        }

        public SpecificationPriority Priority => SpecificationPriority.Default;
        public RejectionType Type => RejectionType.Permanent;

        public virtual Decision IsSatisfiedBy(RemoteBook subject, SearchCriteriaBase searchCriteria)
        {
            var allowedLanguages = subject.Author.QualityProfile.Value.AllowedLanguages;
            var releaseLanguages = subject.Release.Languages;

            if (!allowedLanguages.Any())
            {
                _logger.Debug("Profile has no language restrictions, accepting");
                return Decision.Accept();
            }

            if (!releaseLanguages.Any())
            {
                _logger.Debug("Release has no language information, accepting");
                return Decision.Accept();
            }

            if (!releaseLanguages.Any(l => allowedLanguages.Contains(l)))
            {
                var names = string.Join(", ", releaseLanguages.Select(l => l.Name));
                _logger.Debug("Release language(s) [{0}] not in profile allowed languages", names);
                return Decision.Reject("Language {0} is not allowed by profile", names);
            }

            return Decision.Accept();
        }
    }
}
