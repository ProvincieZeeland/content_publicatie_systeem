using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CPS_API.Models;
using CPS_API.Models.Exceptions;
using Microsoft.ApplicationInsights;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace CPS_API.Repositories
{
    public interface ICallbackRepository
    {
        Task CallCallbackAsync(string objectId, SynchronisationType synchronisationType);
    }

    public class CallbackRepository : ICallbackRepository
    {
        private readonly IFilesRepository _filesRepository;
        private readonly GlobalSettings _globalSettings;
        private readonly TelemetryClient _telemetryClient;

        public CallbackRepository(
            IFilesRepository filesRepository,
            IOptions<GlobalSettings> settings,
            TelemetryClient telemetryClient)
        {
            _filesRepository = filesRepository;
            _globalSettings = settings.Value;
            _telemetryClient = telemetryClient;
        }

        public async Task CallCallbackAsync(string objectId, SynchronisationType synchronisationType)
        {
            if (_globalSettings.CallbackUrl.IsNullOrEmpty())
            {
                return;
            }

            var body = string.Empty;
            if (synchronisationType != SynchronisationType.delete)
            {
                var fileInfo = await _filesRepository.GetFileAsync(objectId);
                var callbackFileInfo = new CallbackCpsFile(fileInfo);
                body = JsonSerializer.Serialize(callbackFileInfo);
            }
            var callbackUrl = _globalSettings.CallbackUrl + $"/{synchronisationType.GetLabel()}/{objectId}";

            await CallCallbackUrlAsync(callbackUrl, body);
        }

        private async Task CallCallbackUrlAsync(string url, string body = "")
        {
            using var client = new HttpClient();
            HttpRequestMessage request;
            HttpResponseMessage response;
            try
            {
                request = GetCallbackRequest(url, body);
                response = await client.SendAsync(request);
            }
            catch (Exception ex)
            {
                // Log error, otherwise ignore it
                await TrackCallbackExceptionAsync(ex, body);
                return;
            }
            if (!response.IsSuccessStatusCode)
            {
                await TrackCallbackExceptionAsync(new CpsException("Callback failed"), body, request, response);
            }
        }

        private HttpRequestMessage GetCallbackRequest(string url, string body)
        {
            var method = body.IsNullOrEmpty() ? HttpMethod.Get : HttpMethod.Post;
            var request = new HttpRequestMessage(method, url);
            request.Headers.Accept.Clear();
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _globalSettings.CallbackAccessToken);
            if (!body.IsNullOrEmpty())
            {
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            }
            return request;
        }

        private async Task TrackCallbackExceptionAsync(Exception exception, string body, HttpRequestMessage? request = null, HttpResponseMessage? response = null)
        {
            var properties = new Dictionary<string, string>
            {
                ["Body"] = body
            };
            if (request != null)
            {
                properties.Add("Request", request.ToString());
            }
            if (response != null)
            {
                var responseContent = await GetCallbackResponseContentAsync(response);
                properties.Add("Response", response.ToString());
                properties.Add("ResponseBody", responseContent);
            }
            _telemetryClient.TrackException(exception, properties);
        }

        private static async Task<string> GetCallbackResponseContentAsync(HttpResponseMessage response)
        {
            if (response.Content == null)
            {
                return "";
            }
            try
            {
                return await response.Content.ReadAsStringAsync();
            }
            catch
            {
                // Try to read the content of the response.
                return "";
            }
        }
    }
}