namespace CPS_API.Models
{
    public class GlobalSettings
    {
        public string StorageTableConnectionstring { get; set; } = string.Empty;

        public string FileStorageConnectionstring { get; set; } = string.Empty;

        public string ObjectIdentifiersTableName { get; set; } = string.Empty;

        public string SettingsTableName { get; set; } = string.Empty;

        public string SettingsPartitionKey { get; set; } = string.Empty;

        public string SettingsSequenceRowKey { get; set; } = string.Empty;

        public string SettingsLastSynchronisationNewRowKey { get; set; } = string.Empty;

        public string SettingsLastSynchronisationChangedRowKey { get; set; } = string.Empty;

        public string SettingsLastTokenForNewRowKey { get; set; } = string.Empty;

        public string SettingsLastTokenForChangedRowKey { get; set; } = string.Empty;

        public string SettingsLastTokenForDeletedRowKey { get; set; } = string.Empty;

        public string SettingsIsNewSynchronisationRunningRowKey { get; set; } = string.Empty;

        public string SettingsIsChangedSynchronisationRunningRowKey { get; set; } = string.Empty;

        public string SettingsIsDeletedSynchronisationRunningRowKey { get; set; } = string.Empty;

        public string ContentContainerName { get; set; } = string.Empty;

        public string MetadataContainerName { get; set; } = string.Empty;

        public string CallbackUrl { get; set; } = string.Empty;

        public string CallbackAccessToken { get; set; } = string.Empty;

        public LoggingLevel LoggingLevel { get; set; }

        public IEnumerable<FieldMapping> MetadataMapping { get; set; }

        public List<FieldMapping> ExternalReferencesMapping { get; set; }

        public List<LocationMapping> LocationMapping { get; set; }

        public string RootSiteUrl { get; set; } = string.Empty;

        public string ClientId { get; set; } = string.Empty;

        public string ClientSecret { get; set; } = string.Empty;

        public string TenantId { get; set; } = string.Empty;

        public string CertificateThumbprint { get; set; } = string.Empty;

        public List<string> PublicDriveIds { get; set; } = new List<string>();
    }

    public class AppSettings
    {
        public int SequenceNumber { get; set; } = 0;

        public DateTime LastSynchronisationNew { get; set; } = DateTime.MinValue;

        public DateTime LastSynchronisationChanged { get; set; } = DateTime.MinValue;

        public DateTime LastSynchronisationDeleted { get; set; } = DateTime.MinValue;
    }

    public class FieldMapping
    {
        public string FieldName { get; set; } = string.Empty;

        public string SpoColumnName { get; set; } = string.Empty;

        public object? DefaultValue { get; set; }

        public bool Required { get; set; }

        public bool ReadOnly { get; set; }
    }

    public class LocationMapping
    {
        public string Classification { get; set; }

        public string Source { get; set; }

        // Afhankelijk van SPO inrichting > wordt mogelijk anders
        public string SiteId { get; set; }

        public string ListId { get; set; }

        public string ExternalReferenceListId { get; set; }

        // optioneel
        public string folderName { get; set; } = string.Empty;
    }
}