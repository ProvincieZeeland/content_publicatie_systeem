namespace CPS_API.Models
{
    public class ExportResponse
    {
        public string SiteId { get; set; }

        public string ListId { get; set; }

        public string NewChangeToken { get; set; }

        public List<int> FailedItemIds { get; set; }

        public List<int> AddedItemIds { get; set; }

        public List<int> UpdatedItemIds { get; set; }

        public List<int> DeletedItemIds { get; set; }

        public ExportResponse(
            string siteId,
            string listId,
            string newNextTokens,
            List<int>? failedItemIds,
            List<int>? addedItemIds,
            List<int>? updatedItemIds,
            List<int>? deletedItemIds)
        {
            SiteId = siteId;
            ListId = listId;
            NewChangeToken = newNextTokens;
            FailedItemIds = failedItemIds ?? new List<int>();
            AddedItemIds = addedItemIds ?? new List<int>();
            UpdatedItemIds = updatedItemIds ?? new List<int>();
            DeletedItemIds = deletedItemIds ?? new List<int>();
        }
    }
}