using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Web;

namespace CPS_Jobs.Helpers
{
    public interface IAppService
    {
        Task callService(string baseUrl, string scope, string url, ILogger log);
    }

    public class AppService : IAppService
    {
        private readonly ITokenAcquisition _tokenAcquisition;

        public AppService(ITokenAcquisition tokenAcquisition)
        {
            _tokenAcquisition = tokenAcquisition;
        }

        public async Task callService(string baseUrl, string scope, string url, ILogger log)
        {
            try
            {
                string token = await _tokenAcquisition.GetAccessTokenForAppAsync(scope);
                using var client = new HttpClient();

                var method = HttpMethod.Get;
                var request = new HttpRequestMessage(method, baseUrl + url);
                request.Headers.Accept.Clear();
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

                await client.SendAsync(request);
            }
            catch
            {
                log.LogError("Could not start sync for url " + url);
                throw;
            }
        }
    }
}
