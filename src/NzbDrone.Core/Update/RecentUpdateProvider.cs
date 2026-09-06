using System;
using System.Collections.Generic;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Update.History;

namespace NzbDrone.Core.Update
{
    public interface IRecentUpdateProvider
    {
        List<UpdatePackage> GetRecentUpdatePackages();
    }

    public class RecentUpdateProvider : IRecentUpdateProvider
    {
        private readonly IConfigFileProvider _configFileProvider;
        private readonly IUpdatePackageProvider _updatePackageProvider;
        private readonly IUpdateHistoryService _updateHistoryService;
        private readonly Logger _logger;

        public RecentUpdateProvider(IConfigFileProvider configFileProvider,
                                    IUpdatePackageProvider updatePackageProvider,
                                    IUpdateHistoryService updateHistoryService,
                                    Logger logger)
        {
            _logger = logger;
            _configFileProvider = configFileProvider;
            _updatePackageProvider = updatePackageProvider;
            _updateHistoryService = updateHistoryService;
        }

        public List<UpdatePackage> GetRecentUpdatePackages()
        {
            // Release notes come from the update feed generated from CHANGELOG.md. A feed
            // outage should leave the page empty rather than failing the request, since
            // this backs both System > Updates and the post-update dialog.
            try
            {
                var previous = _updateHistoryService.PreviouslyInstalled();

                return _updatePackageProvider.GetRecentUpdates(
                    _configFileProvider.Branch, BuildInfo.Version, previous);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Failed to fetch release notes from the update feed");
                return new List<UpdatePackage>();
            }
        }
    }
}
