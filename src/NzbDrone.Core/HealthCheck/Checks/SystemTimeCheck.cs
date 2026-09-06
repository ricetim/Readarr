using System;
using NLog;
using NzbDrone.Common.Cloud;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Core.Localization;

namespace NzbDrone.Core.HealthCheck.Checks
{
    public class SystemTimeCheck : HealthCheckBase
    {
        private readonly IHttpClient _client;
        private readonly IHttpRequestBuilderFactory _cloudRequestBuilder;
        private readonly Logger _logger;

        public SystemTimeCheck(IHttpClient client, IReadarrCloudRequestBuilder cloudRequestBuilder, ILocalizationService localizationService, Logger logger)
            : base(localizationService)
        {
            _client = client;
            _cloudRequestBuilder = cloudRequestBuilder.Services;
            _logger = logger;
        }

        public override HealthCheck Check()
        {
            // The whole check is guarded: without this, no internet connection meant an
            // unhandled exception here aborted every other health check in the run.
            try
            {
                // Upstream read a JSON /time endpoint from the Readarr cloud service, which
                // retired with the project. Static hosting cannot serve the current time, so
                // the server's own Date response header is used instead - it is part of every
                // HTTP response and accurate far beyond the one-day tolerance below.
                var request = _cloudRequestBuilder.Create()
                                                  .Resource("/update/{branch}.json")
                                                  .SetSegment("branch", "develop")
                                                  .Build();

                var response = _client.Execute(request);
                var serverTimeHeader = response.Headers.GetSingleValue("Date");

                if (serverTimeHeader.IsNullOrWhiteSpace())
                {
                    _logger.Debug("No Date header returned, cannot verify system time");
                    return new HealthCheck(GetType());
                }

                var serverTime = HttpHeader.ParseDateTime(serverTimeHeader);
                var systemTime = DateTime.UtcNow;

                // +/- more than 1 day
                if (Math.Abs(serverTime.Subtract(systemTime).TotalDays) >= 1)
                {
                    _logger.Error("System time mismatch. SystemTime: {0} Expected Time: {1}. Update system time", systemTime, serverTime);
                    return new HealthCheck(GetType(), HealthCheckResult.Error, _localizationService.GetLocalizedString("SystemTimeCheckMessage"), "#system-time-off");
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Unable to verify system time");
            }

            return new HealthCheck(GetType());
        }
    }
}
