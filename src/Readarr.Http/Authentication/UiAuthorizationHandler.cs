using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Configuration.Events;
using NzbDrone.Core.Messaging.Events;
using Readarr.Http.Extensions;

namespace NzbDrone.Http.Authentication
{
    public class UiAuthorizationHandler : AuthorizationHandler<BypassableDenyAnonymousAuthorizationRequirement>, IAuthorizationRequirement, IHandle<ConfigSavedEvent>
    {
        // Backups and log files can contain API keys, tracker passkeys and download client
        // credentials, so they are never served through the "disabled for local addresses"
        // convenience bypass. A reverse proxy that does not set X-Forwarded-For makes every
        // request look local, which would otherwise publish these to anyone who can reach
        // the instance.
        private static readonly string[] AlwaysAuthenticatedPaths =
        {
            "/logfile/",
            "/updatelogfile/",
            "/backup/"
        };

        private readonly IConfigFileProvider _configService;
        private static AuthenticationRequiredType _authenticationRequired;

        public UiAuthorizationHandler(IConfigFileProvider configService)
        {
            _configService = configService;
            _authenticationRequired = configService.AuthenticationRequired;
        }

        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, BypassableDenyAnonymousAuthorizationRequirement requirement)
        {
            if (_authenticationRequired == AuthenticationRequiredType.DisabledForLocalAddresses)
            {
                if (context.Resource is HttpContext httpContext &&
                    !IsAlwaysAuthenticated(httpContext) &&
                    IPAddress.TryParse(httpContext.GetRemoteIP(), out var ipAddress))
                {
                    if (ipAddress.IsLocalAddress() ||
                        (_configService.TrustCgnatIpAddresses && ipAddress.IsCgnatIpAddress()))
                    {
                        context.Succeed(requirement);
                    }
                }
            }

            return Task.CompletedTask;
        }

        private static bool IsAlwaysAuthenticated(HttpContext httpContext)
        {
            var path = httpContext.Request.Path;

            return path.HasValue &&
                   AlwaysAuthenticatedPaths.Any(p => path.Value.StartsWith(p, StringComparison.OrdinalIgnoreCase));
        }

        public void Handle(ConfigSavedEvent message)
        {
            _authenticationRequired = _configService.AuthenticationRequired;
        }
    }
}
