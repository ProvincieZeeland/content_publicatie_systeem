using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;

namespace CPS_API.Helpers
{
    public static class ApiHelper
    {
        public static IConfigurationRoot? getConfiguration()
        {
            var builder = new ConfigurationBuilder()
            .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
            .AddJsonFile("appsettings.json");
            var configuration = builder.Build();
            return configuration;
        }

        public static CloudTableClient? getCloudTableClientFromStorageAccount()
        {
            // Configuratie bepalen.
            var configuration = getConfiguration();

            // Storageaccount definiëren.
            var connectionString = configuration.GetConnectionString("CloudStorageAccount");
            var storageAccount = CloudStorageAccount.Parse(connectionString);

            var tableClient = storageAccount.CreateCloudTableClient();
            return tableClient;
        }
    }
}
