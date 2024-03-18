using Microsoft.WindowsAzure.Storage.Table;

namespace CPS_API.Models
{
    public class ToBePublishedEntity : TableEntity
    {
        public string ObjectId { get; set; }

        public DateTimeOffset PublicationDate { get; set; }

        public ToBePublishedEntity()
        {

        }

        public ToBePublishedEntity(string partitionKey, string objectId, DateTimeOffset publicationDate)
        {
            PartitionKey = partitionKey;
            RowKey = objectId;
            ObjectId = objectId;
            PublicationDate = publicationDate;
        }
    }
}