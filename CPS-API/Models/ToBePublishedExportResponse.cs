namespace CPS_API.Models
{
    public class ToBePublishedExportResponse
    {
        public int NumberOfSucceededItems { get; set; }

        public List<ToBePublishedEntity> FailedItems { get; set; }

        public ToBePublishedExportResponse(int numberOfSucceededItems, List<ToBePublishedEntity> failedItems)
        {
            NumberOfSucceededItems = numberOfSucceededItems;
            FailedItems = failedItems ?? new List<ToBePublishedEntity>();
        }
    }
}
