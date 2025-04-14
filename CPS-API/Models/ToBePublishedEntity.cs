using Microsoft.WindowsAzure.Storage.Table;

namespace CPS_API.Models
{
    public class ToBePublishedEntity : TableEntity
    {
        public string ObjectId { get; set; } = string.Empty;

        public DateTime PublicationDate { get; set; }

        public ToBePublishedEntity()
        {

        }

        public ToBePublishedEntity(string partitionKey, string objectId, DateTime publicationDate)
        {
            PartitionKey = partitionKey;
            RowKey = objectId;
            ObjectId = objectId;
            PublicationDate = publicationDate;
        }
    }
}