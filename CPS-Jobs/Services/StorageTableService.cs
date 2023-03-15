using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;

namespace CPS_Jobs.Helpers
{
    public interface IStorageTableService
    {
        CloudTable? GetTable(string tableName);

        Task SaveAsync(CloudTable table, ITableEntity entity);
    }

    public class StorageTableService : IStorageTableService
    {
        private readonly IConfiguration _configuration;

        public StorageTableService(IConfiguration configuration)
        {
            this._configuration = configuration;
        }

        private string GetConnectionstring()
        {
            return _configuration.GetValue<string>("Settings:StorageTableConnectionstring");
        }

        private CloudTableClient? GetCloudTableClient()
        {
            var connectionString = this.GetConnectionstring();
            var storageAccount = CloudStorageAccount.Parse(connectionString);
            return storageAccount.CreateCloudTableClient();
        }

        public CloudTable? GetTable(string tableName)
        {
            var tableClient = this.GetCloudTableClient();
            return tableClient?.GetTableReference(tableName);
        }

        public async Task SaveAsync(CloudTable table, ITableEntity entity)
        {
            var insertop = TableOperation.InsertOrReplace(entity);
            await table.ExecuteAsync(insertop);
        }
    }
}
