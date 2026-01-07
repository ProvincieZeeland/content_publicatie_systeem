using Microsoft.Extensions.Logging;
using Microsoft.Identity.Web;
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CPS_Jobs.Helpers
{
    public interface IAppService
    {
        Task<HttpResponseMessage> GetAsync(string baseUrl, string scope, string url);

        Task<HttpResponseMessage> PutAsync(string baseUrl, string scope, string url, string body);
    }

    public class AppService : IAppService
    {
        private readonly ILogger<HandleWebHookQueue> _logger;
        private readonly ITokenAcquisition _tokenAcquisition;

        public AppService(ILogger<HandleWebHookQueue> logger, ITokenAcquisition tokenAcquisition)
        {
            _logger = logger;
            _tokenAcquisition = tokenAcquisition;
        }

        public async Task<HttpResponseMessage> GetAsync(string baseUrl, string scope, string url)
        {
            return await CallAsync(HttpMethod.Get, baseUrl, scope, url);
        }

        public async Task<HttpResponseMessage> PutAsync(string baseUrl, string scope, string url, string body)
        {
            return await CallAsync(HttpMethod.Put, baseUrl, scope, url, body);
        }

        private async Task<HttpResponseMessage> CallAsync(HttpMethod method, string baseUrl, string scope, string url, string? body = null)
        {
            try
            {
                string token = await _tokenAcquisition.GetAccessTokenForAppAsync(scope);
                using var client = new HttpClient();

                var request = new HttpRequestMessage(method, baseUrl + url);
                request.Headers.Accept.Clear();
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                if (!string.IsNullOrEmpty(body))
                {
                    request.Content = new StringContent(body, Encoding.UTF8, "application/json");
                }

                // Make sure long calls don't timeout easily
                client.Timeout = Timeout.InfiniteTimeSpan;
                var cts = new CancellationTokenSource(TimeSpan.FromMinutes(15));
                HttpResponseMessage response = await client.SendAsync(request, cts.Token);
                cts.Dispose();
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not start sync for url {Url}", url);
                throw;
            }
        }
    }
}
