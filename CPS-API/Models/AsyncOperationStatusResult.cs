using Microsoft.Graph;
using Newtonsoft.Json;

namespace CPS_API.Models
{
    public class AsyncOperationStatusResult : AsyncOperationStatus
    {
        //
        // Summary:
        //     Gets or sets status.
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore, PropertyName = "resourceId", Required = Required.Default)]
        public string ResourceId { get; set; }
    }
}