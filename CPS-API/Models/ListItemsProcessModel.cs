namespace CPS_API.Models
{
    public class ListItemsProcessModel
    {
        public List<string> processedItemIds { get; set; } = [];

        public List<string> notProcessedItemIds { get; set; } = [];
    }
}
