using Microsoft.WindowsAzure.Storage.Table;
using System.Text.Json;

namespace CPS_API.Models
{
    public class DocumentIdsEntity : TableEntity
    {
        public string ContentIds { get; set; }

        public DocumentIdsEntity()
        {

        }

        public ContentIds GetContentIds()
        {
            return JsonSerializer.Deserialize<ContentIds>(ContentIds);
        }

        public DocumentIdsEntity(string contentId, ContentIds ids)
        {
            this.PartitionKey = contentId;
            this.RowKey = ids.SiteId + ids.WebId + ids.ListId + ids.ListItemId;
            this.ContentIds = JsonSerializer.Serialize(ids);
        }
    }
}
