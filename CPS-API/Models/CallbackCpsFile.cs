namespace CPS_API.Models
{
    public class CallbackCpsFile
    {
        public string ObjectId { get; set; }

        public CallbackFileInformation Metadata { get; set; }

        public CallbackCpsFile()
        {
            ObjectId = string.Empty;
            Metadata = new CallbackFileInformation();
        }

        public CallbackCpsFile(CpsFile file)
        {
            ObjectId = file.Metadata?.Ids?.ObjectId ?? string.Empty;
            Metadata = file.Metadata == null ? new CallbackFileInformation() : new CallbackFileInformation(file.Metadata);
        }
    }
}