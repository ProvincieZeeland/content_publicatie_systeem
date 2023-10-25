using System.Collections.Generic;
using Newtonsoft.Json;

namespace CPS_Jobs.Models
{
    public class ResponseModel<T>
    {
        [JsonProperty(PropertyName = "value")]
        public List<T> Value { get; set; }
    }
}
