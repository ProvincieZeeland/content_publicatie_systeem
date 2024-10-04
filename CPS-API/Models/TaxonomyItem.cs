namespace CPS_API.Models
{
    [Serializable]
    public class TaxonomyItemDto
    {
        public string Label { get; set; } = string.Empty;

        public string TermGuid { get; set; } = string.Empty;

        public int WssId { get; set; }

        public TaxonomyItemDto() { }
    }
}