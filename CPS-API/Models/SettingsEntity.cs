using CPS_API.Helpers;
using Microsoft.WindowsAzure.Storage.Table;

namespace CPS_API.Models
{
    public class SettingsEntity : TableEntity
    {
        public long SequenceNumber { get; set; }

        public SettingsEntity()
        {

        }

        public SettingsEntity(long sequenceNumber)
        {
            this.PartitionKey = Constants.SettingsPartitionKey;
            this.RowKey = Constants.SettingsRowKey;
            this.SequenceNumber = sequenceNumber;
        }
    }
}