namespace CPS_API.Models
{
    public class DeltaResponse
    {
        public List<DeltaDriveItem> Items { get; set; }

        public Dictionary<string, string> DeltaLinks { get; set; }

        public DeltaResponse(List<DeltaDriveItem> items, Dictionary<string, string> deltaLinks)
        {
            Items = items;
            DeltaLinks = deltaLinks;
        }
    }
}