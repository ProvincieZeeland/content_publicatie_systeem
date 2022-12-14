using CPS_API.Helpers;
using Microsoft.WindowsAzure.Storage.Table;

namespace CPS_API.Models
{
    public class SettingsEntity : TableEntity
    {
        public long SequenceNumber { get; set; }

        public DateTime LastSynchronisation { get; set; }

        public SettingsEntity()
        {

        }

        public SettingsEntity(long sequenceNumber)
        {
            this.PartitionKey = Constants.SettingsPartitionKey;
            this.RowKey = Constants.SettingsSequenceRowKey;
            this.SequenceNumber = sequenceNumber;
        }

        public SettingsEntity(DateTime lastSynchronisation)
        {
            this.PartitionKey = Constants.SettingsPartitionKey;
            this.RowKey = Constants.SettingsLastSynchronisationRowKey;
            this.LastSynchronisation = lastSynchronisation;
        }
    }
}