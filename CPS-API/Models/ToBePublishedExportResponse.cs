namespace CPS_API.Models
{
    public class ToBePublishedExportResponse
    {
        public int NumberOfSucceededItems { get; set; }

        public List<ToBePublished> FailedItems { get; set; }

        public ToBePublishedExportResponse(int numberOfSucceededItems, List<ToBePublished> failedItems)
        {
            NumberOfSucceededItems = numberOfSucceededItems;
            FailedItems = failedItems ?? new List<ToBePublished>();
        }
    }
}