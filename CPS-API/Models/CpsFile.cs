using CPS_API.Helpers;
using System.Text.Json.Serialization;

namespace CPS_API.Models
{
    public class CpsFile
    {
        [JsonConverter(typeof(ByteArrayConverter))]
        public byte[] Content { get; set; }

        public FileInformation Metadata { get; set; }
    }
}