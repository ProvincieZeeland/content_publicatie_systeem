namespace CPS_API.Models
{
    public class DeltaResponse
    {
        public List<DeltaDriveItem> Items { get; set; }

        public Dictionary<string, string> NextTokens { get; set; }

        public DeltaResponse(List<DeltaDriveItem> items, Dictionary<string, string> nextTokens)
        {
            Items = items;
            NextTokens = nextTokens;
        }
    }
}