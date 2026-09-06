using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using NzbDrone.Common.Cloud;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Http;
using NzbDrone.Core.Analytics;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Update
{
    public interface IUpdatePackageProvider
    {
        UpdatePackage GetLatestUpdate(string branch, Version currentVersion);
        List<UpdatePackage> GetRecentUpdates(string branch, Version currentVersion, Version previousVersion = null);
    }

    public class UpdatePackageProvider : IUpdatePackageProvider
    {
        private readonly IHttpClient _httpClient;
        private readonly IHttpRequestBuilderFactory _requestBuilder;
        private readonly IPlatformInfo _platformInfo;
        private readonly IAnalyticsService _analyticsService;
        private readonly IMainDatabase _mainDatabase;

        public UpdatePackageProvider(IHttpClient httpClient, IReadarrCloudRequestBuilder requestBuilder, IAnalyticsService analyticsService, IPlatformInfo platformInfo, IMainDatabase mainDatabase)
        {
            _platformInfo = platformInfo;
            _analyticsService = analyticsService;
            _requestBuilder = requestBuilder.Services;
            _httpClient = httpClient;
            _mainDatabase = mainDatabase;
        }

        public UpdatePackage GetLatestUpdate(string branch, Version currentVersion)
        {
            var request = _requestBuilder.Create()
                                         .Resource("/update/{branch}.json")
                                         .AddQueryParam("version", currentVersion)
                                         .AddQueryParam("os", OsInfo.Os.ToString().ToLowerInvariant())
                                         .AddQueryParam("arch", RuntimeInformation.OSArchitecture)
                                         .AddQueryParam("runtime", "netcore")
                                         .AddQueryParam("runtimeVer", _platformInfo.Version)
                                         .AddQueryParam("dbType", _mainDatabase.DatabaseType)
                                         .AddQueryParam("includeMajorVersion", true)
                                         .SetSegment("branch", branch);

            if (_analyticsService.IsEnabled)
            {
                // Send if the system is active so we know which versions to deprecate/ignore
                request.AddQueryParam("active", _analyticsService.InstallIsActive.ToString().ToLower());
            }

            var update = _httpClient.Get<UpdatePackageAvailable>(request.Build()).Resource;

            if (update?.UpdatePackage == null || !update.Available)
            {
                return null;
            }

            // Upstream's service compared against the version query param and answered
            // Available accordingly. A static feed cannot, so it always reports the newest
            // release and the comparison happens here. Without this the health check would
            // warn that an update exists on every install once the build is 14 days old,
            // and InstallUpdateService would reinstall the running version.
            if (update.UpdatePackage.Version <= currentVersion)
            {
                return null;
            }

            return update.UpdatePackage;
        }

        public List<UpdatePackage> GetRecentUpdates(string branch, Version currentVersion, Version previousVersion)
        {
            var request = _requestBuilder.Create()
                                         .Resource("/update/{branch}/changes.json")
                                         .AddQueryParam("version", currentVersion)
                                         .AddQueryParam("os", OsInfo.Os.ToString().ToLowerInvariant())
                                         .AddQueryParam("arch", RuntimeInformation.OSArchitecture)
                                         .AddQueryParam("runtime", "netcore")
                                         .AddQueryParam("runtimeVer", _platformInfo.Version)
                                         .SetSegment("branch", branch);

            if (previousVersion != null && previousVersion != currentVersion)
            {
                request.AddQueryParam("prevVersion", previousVersion);
            }

            if (_analyticsService.IsEnabled)
            {
                // Send if the system is active so we know which versions to deprecate/ignore
                request.AddQueryParam("active", _analyticsService.InstallIsActive.ToString().ToLower());
            }

            var updates = _httpClient.Get<List<UpdatePackage>>(request.Build());

            return updates.Resource;
        }
    }
}
