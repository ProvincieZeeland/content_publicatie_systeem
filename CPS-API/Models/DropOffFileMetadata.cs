using System.Text.Json.Serialization;
using CPS_API.Helpers;

namespace CPS_API.Models
{
    public class DropOffFileMetadata
    {
        public bool IsComplete { get; set; }

        public string Status { get; set; } = "";

        public string? ObjectId { get; set; }

        public DropOffFileMetadata()
        {

        }

        public DropOffFileMetadata(bool isComplete, string status, string? objectId)
        {
            IsComplete = isComplete;
            Status = status ?? string.Empty;
            ObjectId = objectId;
        }

        [JsonIgnore]
        public object? this[string fieldname]
        {
            get
            {
                var property = this.GetType().GetProperty(fieldname);
                if (property != null)
                    return property.GetValue(this);
                else
                    throw new ArgumentException("Unknown property " + fieldname);
            }

            set
            {
                FieldPropertyHelper.SetFieldValue(this, fieldname, value);
            }
        }
    }
}