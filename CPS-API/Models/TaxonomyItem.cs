namespace CPS_API.Models
{
    [Serializable]
    public class TaxonomyItemDto
    {
        public string Label { get; set; }

        public string TermGuid { get; set; }

        public int WssId { get; set; }

        public TaxonomyItemDto() { }
    }
}