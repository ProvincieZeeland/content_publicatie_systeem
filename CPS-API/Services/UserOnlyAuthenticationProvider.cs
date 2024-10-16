using Microsoft.Identity.Web;
using Microsoft.Kiota.Abstractions.Authentication;

namespace CPS_API.Models
{
    public class UserOnlyAuthenticationProvider : IAccessTokenProvider
    {
        private readonly ITokenAcquisition _tokenAcquisition;

        public UserOnlyAuthenticationProvider(ITokenAcquisition tokenAcquisition)
        {
            AllowedHostsValidator = new AllowedHostsValidator();
            _tokenAcquisition = tokenAcquisition;
        }

        public async Task<string> GetAuthorizationTokenAsync(Uri uri, Dictionary<string, object>? additionalAuthenticationContext = default,
            CancellationToken cancellationToken = default)
        {
            var token = await _tokenAcquisition.GetAccessTokenForUserAsync(new[] { "Sites.Read.All", "Files.Read.All" });
            return token;
        }

        public AllowedHostsValidator AllowedHostsValidator { get; }
    }
}
