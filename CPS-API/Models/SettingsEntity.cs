using Microsoft.WindowsAzure.Storage.Table;

namespace CPS_API.Models
{
    public class SettingsEntity : TableEntity
    {
        public long? SequenceNumber { get; set; }

        public DateTime? LastSynchronisationNew { get; set; }

        public DateTime? LastSynchronisationChanged { get; set; }

        public string LastTokenForNew { get; set; } = string.Empty;

        public string LastTokenForChanged { get; set; } = string.Empty;

        public string LastTokenForDeleted { get; set; } = string.Empty;

        public bool? IsNewSynchronisationRunning { get; set; }

        public bool? IsChangedSynchronisationRunning { get; set; }

        public bool? IsDeletedSynchronisationRunning { get; set; }

        public string DropOffSubscriptionExpirationDateTime { get; set; } = string.Empty;

        public string DropOffSubscriptionId { get; set; } = string.Empty;

        public string DropOffLastChangeToken { get; set; } = string.Empty;

        public string DropOffSubscriptionExpirationDateTimeFinancial { get; set; } = string.Empty;

        public string DropOffSubscriptionIdFinancial { get; set; } = string.Empty;

        public string DropOffLastChangeTokenFinancial { get; set; } = string.Empty;

        public SettingsEntity()
        {

        }

        public SettingsEntity(string partitionKey, string rowKey)
        {
            this.PartitionKey = partitionKey;
            this.RowKey = rowKey;
        }

        //todo: update code to Azure.Storage.Table, instead of old WindowsAzure.Storage
        //[IgnoreDataMember]
        //public object? this[string fieldname]
        //{
        //    get
        //    {
        //        var property = this.GetType().GetProperty(fieldname);
        //        if (property != null)
        //            return property.GetValue(this);
        //        else
        //            throw new ArgumentException("Unknown property " + fieldname);
        //    }

        //    set
        //    {
        //        var property = this.GetType().GetProperty(fieldname);
        //        if (property == null) throw new ArgumentException("Unknown property " + fieldname);

        //        FieldPropertyHelper.SetFieldValue(this, property, value);
        //    }
        //}
    }
}