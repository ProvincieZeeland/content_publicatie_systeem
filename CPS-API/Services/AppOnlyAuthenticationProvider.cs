using System.IdentityModel.Tokens.Jwt;
using CPS_API.Models;
using CPS_API.Models.Exceptions;
using Microsoft.Identity.Web;
using Microsoft.Kiota.Abstractions.Authentication;

namespace CPS_API.Services
{
    public class AppOnlyAuthenticationProvider : IAccessTokenProvider
    {
        private readonly GlobalSettings _globalSettings;
        private readonly ITokenAcquisition _tokenAcquisition;

        public AppOnlyAuthenticationProvider(ITokenAcquisition tokenAcquisition, Microsoft.Extensions.Options.IOptions<GlobalSettings> settings)
        {
            AllowedHostsValidator = new AllowedHostsValidator();
            _tokenAcquisition = tokenAcquisition;
            _globalSettings = settings.Value;
        }

        public AllowedHostsValidator AllowedHostsValidator { get; }

        public async Task<string> GetAuthorizationTokenAsync(Uri uri, Dictionary<string, object>? additionalAuthenticationContext = null, CancellationToken cancellationToken = default)
        {
            string token = await _tokenAcquisition.GetAccessTokenForAppAsync("https://graph.microsoft.com/.default", _globalSettings.TenantId, null);

            if (string.IsNullOrWhiteSpace(token)) return token;

            try
            {
                var handler = new JwtSecurityTokenHandler();
                var jwtToken = handler.ReadToken(token) as JwtSecurityToken;
                if (jwtToken == null) throw new CpsException("Error while getting access token");
                var tokenExpiryDate = jwtToken.ValidTo;

                // If token is less then 45 min valid, we need a new one for Teams Notifications so refresh it
                if (tokenExpiryDate.CompareTo(DateTime.Now.AddMinutes(45).ToUniversalTime()) <= 0)
                {
                    token = await _tokenAcquisition.GetAccessTokenForAppAsync("https://graph.microsoft.com/.default", _globalSettings.TenantId, new TokenAcquisitionOptions { ForceRefresh = true });
                }
            }
            catch
            {
                // Failed to refresh token; ignore error and return current one
            }

            return token;
        }
    }
}