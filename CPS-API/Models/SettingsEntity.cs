using CPS_API.Helpers;
using Microsoft.WindowsAzure.Storage.Table;

namespace CPS_API.Models
{
    public class SettingsEntity : TableEntity
    {
        public long? SequenceNumber { get; set; }

        public DateTime? LastSynchronisationNew { get; set; }

        public DateTime? LastSynchronisationChanged { get; set; }

        public DateTime? LastSynchronisationDeleted { get; set; }

        public SettingsEntity()
        {

        }

        public SettingsEntity(long sequenceNumber)
        {
            this.PartitionKey = Constants.SettingsPartitionKey;
            this.RowKey = Constants.SettingsSequenceRowKey;
            this.SequenceNumber = sequenceNumber;
        }

        public static SettingsEntity createForLastSynchronisationNew(DateTime lastSynchronisationNew)
        {
            var entity = new SettingsEntity();
            entity.PartitionKey = Constants.SettingsPartitionKey;
            entity.RowKey = Constants.SettingsLastSynchronisationNewRowKey;
            entity.LastSynchronisationNew = lastSynchronisationNew;
            return entity;
        }

        public static SettingsEntity createForLastSynchronisationChanged(DateTime lastSynchronisationChanged)
        {
            var entity = new SettingsEntity();
            entity.PartitionKey = Constants.SettingsPartitionKey;
            entity.RowKey = Constants.SettingsLastSynchronisationChangedRowKey;
            entity.LastSynchronisationChanged = lastSynchronisationChanged;
            return entity;
        }

        public static SettingsEntity createForLastSynchronisationDeleted(DateTime lastSynchronisationDeleted)
        {
            var entity = new SettingsEntity();
            entity.PartitionKey = Constants.SettingsPartitionKey;
            entity.RowKey = Constants.SettingsLastSynchronisationDeletedRowKey;
            entity.LastSynchronisationDeleted = lastSynchronisationDeleted;
            return entity;
        }
    }
}