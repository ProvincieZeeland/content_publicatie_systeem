using Microsoft.WindowsAzure.Storage.Table;

namespace CPS_API.Models
{
    public class ToBePublishedEntity : TableEntity
    {
        public string ObjectId { get; set; }

        public ToBePublishedEntity()
        {

        }

        public ToBePublishedEntity(string partitionKey, string objectId)
        {
            PartitionKey = partitionKey;
            RowKey = objectId;
            ObjectId = objectId;
        }
    }
}