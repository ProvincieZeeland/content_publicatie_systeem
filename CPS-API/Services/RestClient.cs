using System.Net;
using System.Net.Http.Headers;

namespace IExperts.SocialIntranet.Services
{
    public interface IRestClient
    {
        Task<HttpResponseMessage> HttpRequestAsync(HttpRequestMessage request, bool throwExceptionIfUnsuccessfull = true);
    }

    public class RestClient : IRestClient
    {
        private readonly IHttpClientFactory _httpClientFactory;

        public RestClient(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

        public async Task<HttpResponseMessage> HttpRequestAsync(HttpRequestMessage request, bool throwExceptionIfUnsuccessfull = true)
        {
            return await HttpRequestAsync(
                request,
                CancellationToken.None,
                throwExceptionIfUnsuccessfull);
        }

        public async Task<HttpResponseMessage> HttpRequestAsync(HttpRequestMessage request, CancellationToken cancellationToken, bool throwExceptionIfUnsuccessfull = true)
        {
            request.Headers.Accept.Clear();
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Version = HttpVersion.Version20;

            var client = _httpClientFactory.CreateClient("restClient");
            var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (throwExceptionIfUnsuccessfull)
                response.EnsureSuccessStatusCode();

            return response;
        }
    }
}
