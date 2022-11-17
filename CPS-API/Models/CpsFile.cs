namespace CPS_API.Models
{
    public class CpsFile
    {
        public byte[] Content { get; set; }

        public FileInformation Metadata { get; set; }
    }
}
