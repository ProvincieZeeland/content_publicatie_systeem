using System;
using Microsoft.WindowsAzure.Storage.Table;

namespace CPS_Jobs.Models
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

        public SettingsEntity(string partitionKey, string rowKey)
        {
            this.PartitionKey = partitionKey;
            this.RowKey = rowKey;
        }
    }
}