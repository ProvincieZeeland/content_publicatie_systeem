namespace CPS_API.Models
{
    public class ExportResponse
    {
        public string NewNextTokens { get; set; }

        public List<DeltaDriveItem> FailedItems { get; set; }

        public int NumberOfSucceededItems { get; set; }

        public ExportResponse(string newNextTokens, List<DeltaDriveItem>? failedItems, int numberOfSucceededItems)
        {
            NewNextTokens = newNextTokens;
            FailedItems = failedItems ?? new List<DeltaDriveItem>();
            NumberOfSucceededItems = numberOfSucceededItems;
        }
    }
}
