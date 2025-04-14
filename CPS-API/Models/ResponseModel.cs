using Newtonsoft.Json;

namespace CPS_API.Models
{
    /// <summary>
    /// generic class used to hold a collection of objects
    /// </summary>
    /// <typeparam name="T">Type of object</typeparam>
    public class ResponseModel<T>
    {
        [JsonProperty(PropertyName = "value")]
        public List<T> Value { get; set; } = [];
    }
}