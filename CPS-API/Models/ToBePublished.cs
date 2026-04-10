namespace CPS_API.Models
{
    public partial class ToBePublished
    {
        public Guid Id { get; set; }

        public string ObjectId { get; set; } = string.Empty;

        public DateTime PublicationDate { get; set; }
    }
}
