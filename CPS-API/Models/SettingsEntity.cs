using Microsoft.WindowsAzure.Storage.Table;

namespace CPS_API.Models
{
    public class SettingsEntity : TableEntity
    {
        public long? SequenceNumber { get; set; }

        public DateTime? LastSynchronisationNew { get; set; }

        public DateTime? LastSynchronisationChanged { get; set; }

        public string LastTokenForNew { get; set; }

        public string LastTokenForChanged { get; set; }

        public string LastTokenForDeleted { get; set; }

        public bool? IsNewSynchronisationRunning { get; set; }

        public bool? IsChangedSynchronisationRunning { get; set; }

        public bool? IsDeletedSynchronisationRunning { get; set; }

        public string DropOffSubscriptionExpirationDateTime { get; set; }

        public string DropOffSubscriptionId { get; set; }

        public string DropOffLastChangeToken { get; set; }

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