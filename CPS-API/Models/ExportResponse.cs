namespace CPS_API.Models
{
    public class ExportResponse
    {
        public string NewNextTokens { get; set; }

        public List<DeltaDriveItem> FailedItems { get; set; }

        public int NumberOfSucceededItems { get; set; }

        public List<string> FailedToBePublishedIObjectIds { get; set; }

        public ExportResponse(string newNextTokens, int numberOfSucceededItems, List<DeltaDriveItem>? failedItems = null, List<string>? failedToBePublishedItems = null)
        {
            NewNextTokens = newNextTokens;
            FailedItems = failedItems ?? new List<DeltaDriveItem>();
            NumberOfSucceededItems = numberOfSucceededItems;
            FailedToBePublishedIObjectIds = failedToBePublishedItems ?? new List<string>();
        }
    }
}
