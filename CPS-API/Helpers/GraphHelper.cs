using Microsoft.Graph;
using Microsoft.Identity.Client;
using System.Net.Http.Headers;

namespace CPS_API.Helpers
{

    static class GraphHelper
    {

        // Client configured with user authentication
        private static GraphServiceClient? _graphClient;

        public async static Task SignInUserAndInitializeGraphUsingMSAL()
        {
            // Using appsettings.json for our configuration settings
            var builder = new ConfigurationBuilder()
                .SetBasePath(System.IO.Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json");

            var configuration = builder.Build();
            var appConfiguration = configuration
                .Get<PublicClientApplicationOptions>();

            var scopes = new[] { "user.read" };
            _graphClient = await SignInAndInitializeGraphServiceClient(appConfiguration, scopes);
        }

        private static async Task<string> SignInUserAndGetTokenUsingMSAL(PublicClientApplicationOptions configuration, string[] scopes)
        {
            var authority = string.Concat(configuration.Instance, configuration.TenantId);

            // Initialize the MSAL library by building a public client application
            var application = PublicClientApplicationBuilder.Create(configuration.ClientId)
                                                    .WithAuthority(authority)
                                                    .WithRedirectUri(configuration.RedirectUri)
                                                    .Build();

            AuthenticationResult result;
            try
            {
                var accounts = await application.GetAccountsAsync();
                result = await application.AcquireTokenSilent(scopes, accounts.FirstOrDefault())
                 .ExecuteAsync();
            }
            catch (MsalUiRequiredException ex)
            {
                result = await application.AcquireTokenInteractive(scopes)
                 .WithClaims(ex.Claims)
                 .ExecuteAsync();
            }

            return result.AccessToken;
        }

        /// <summary>
        /// Sign in user using MSAL and obtain a token for MS Graph
        /// </summary>
        /// <returns></returns>
        private async static Task<GraphServiceClient> SignInAndInitializeGraphServiceClient(PublicClientApplicationOptions configuration, string[] scopes)
        {
            var graphClient = new GraphServiceClient("https://graph.microsoft.com/v1.0/",
                new DelegateAuthenticationProvider(async (requestMessage) =>
                {
                    string? accessToken;
                    try
                    {
                        accessToken = await SignInUserAndGetTokenUsingMSAL(configuration, scopes);
                    }
                    catch (Exception ex)
                    {
                        throw new UnauthorizedException("Error while getting access token");
                    }
                    requestMessage.Headers.Authorization = new AuthenticationHeaderValue("bearer", accessToken);
                }));

            return await Task.FromResult(graphClient);
        }

        public static async Task<Drive?> GetDriveAsync(string siteId)
        {
            _ = _graphClient ?? throw new NullReferenceException("Graph has not been initialized");

            return await _graphClient.Sites[siteId].Drive.Request().GetAsync();
        }

        public static async Task<DriveItem> GetDriveItemAsync(string siteId, string listId, string listItemId)
        {
            _ = _graphClient ?? throw new NullReferenceException("Graph has not been initialized");

            return await _graphClient.Sites[siteId].Lists[listId].Items[listItemId].DriveItem.Request().GetAsync();
        }

        public static async Task<string> GetFileUrlAsync(string siteId, string driveItemId)
        {
            _ = _graphClient ?? throw new NullReferenceException("Graph has not been initialized");

            var permission = await _graphClient.Sites[siteId].Drive.Items[driveItemId].CreateLink("view").Request().PostAsync();
            if (permission == null)
            {
                return null;
            }
            return permission.Link.WebUrl;
        }

        public static async Task<ListItem?> GetLisItemAsync(string siteId, string listId, string listItemId)
        {
            _ = _graphClient ?? throw new NullReferenceException("Graph has not been initialized");

            var queryOptions = new List<QueryOption>()
            {
                new QueryOption("expand", "fields")
            };
            return await _graphClient.Sites[siteId].Lists[listId].Items[listItemId].Request(queryOptions).GetAsync();
        }

        public static async Task<DriveItem?> PutFileAsync(string siteId, string driveItemId, MemoryStream stream)
        {
            _ = _graphClient ?? throw new NullReferenceException("Graph has not been initialized");

            return await _graphClient.Sites[siteId].Drive.Items[driveItemId].Content.Request().PutAsync<DriveItem>(stream);
        }

        public static async void DeleteFileAsync(string siteId, string driveItemId)
        {
            _ = _graphClient ?? throw new NullReferenceException("Graph has not been initialized");

            await _graphClient.Sites[siteId].Drive.Items[driveItemId].Request().DeleteAsync();
        }

        public class UnauthorizedException : Exception
        {
            public UnauthorizedException(string message)
               : base(message)
            {
            }
        }
    }
}
