using System.Text.Json.Serialization;

namespace CPS_API.Models
{
    /// <summary>
    /// generic class used to hold a collection of objects
    /// </summary>
    /// <typeparam name="T">Type of object</typeparam>
    public class ResponseModel<T>
    {
        [JsonPropertyName("value")]
        public List<T> Value { get; set; } = [];
    }
}