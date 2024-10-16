using Microsoft.Identity.Web;
using Microsoft.Kiota.Abstractions.Authentication;

namespace CPS_API.Models
{
    public class UserOnlyAuthenticationProvider : IAccessTokenProvider
    {
        private readonly string _tenantId;
        private readonly ITokenAcquisition _tokenAcquisition;

        public UserOnlyAuthenticationProvider(ITokenAcquisition tokenAcquisition, string tenantId)
        {
            AllowedHostsValidator = new AllowedHostsValidator();
            _tokenAcquisition = tokenAcquisition;
            _tenantId = tenantId;
        }

        public async Task<string> GetAuthorizationTokenAsync(Uri uri, Dictionary<string, object>? additionalAuthenticationContext = default,
            CancellationToken cancellationToken = default)
        {
            var token = await _tokenAcquisition.GetAccessTokenForUserAsync(new[] { "https://graph.microsoft.com/.default" }, tenantId: _tenantId);
            return token;
        }

        public AllowedHostsValidator AllowedHostsValidator { get; }
    }
}
