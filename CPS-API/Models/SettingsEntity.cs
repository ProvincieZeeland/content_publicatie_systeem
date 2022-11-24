using Microsoft.WindowsAzure.Storage.Table;

namespace CPS_API.Models
{
    public class SettingsEntity : TableEntity
    {
        public long Sequence { get; set; }

        public SettingsEntity()
        {

        }

        public SettingsEntity(long Sequence)
        {
            this.PartitionKey = "0";
            this.RowKey = "0";
            this.Sequence = Sequence;
        }
    }
}
