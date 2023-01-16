namespace CPS_API.Models
{
    public class CallbackCpsFile
    {
        public string ObjectId { get; set; }

        public CallbackFileInformation Metadata { get; set; }

        public CallbackCpsFile()
        {
            this.ObjectId = string.Empty;
            this.Metadata = new CallbackFileInformation();
        }

        public CallbackCpsFile(CpsFile file)
        {
            this.ObjectId = file.Metadata?.Ids?.ObjectId ?? string.Empty;
            this.Metadata = new CallbackFileInformation(file.Metadata);
        }
    }
}