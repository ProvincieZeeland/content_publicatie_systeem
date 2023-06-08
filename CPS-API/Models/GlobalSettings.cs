namespace CPS_API.Models
{
    public class GlobalSettings
    {
        public string StorageTableConnectionstring { get; set; } = string.Empty;

        public string FileStorageConnectionstring { get; set; } = string.Empty;

        public string ObjectIdentifiersTableName { get; set; } = string.Empty;

        public string SettingsTableName { get; set; } = string.Empty;

        public string SettingsPartitionKey { get; set; } = string.Empty;

        public string SettingsRowKey { get; set; } = string.Empty;

        public string ContentContainerName { get; set; } = string.Empty;

        public string MetadataContainerName { get; set; } = string.Empty;

        public string CallbackUrl { get; set; } = string.Empty;

        public string CallbackAccessToken { get; set; } = string.Empty;

        public LoggingLevel LoggingLevel { get; set; }

        public List<FieldMapping> MetadataMapping { get; set; } = new List<FieldMapping>();

        public List<FieldMapping> ExternalReferencesMapping { get; set; } = new List<FieldMapping>();

        public List<LocationMapping> LocationMapping { get; set; } = new List<LocationMapping>();

        public string RootSiteUrl { get; set; } = string.Empty;

        public string ClientId { get; set; } = string.Empty;

        public string ClientSecret { get; set; } = string.Empty;

        public string TenantId { get; set; } = string.Empty;

        public string CertificateThumbprint { get; set; } = string.Empty;

        public List<string> PublicDriveIds { get; set; } = new List<string>();

        public List<string> AdditionalObjectIds { get; set; } = new List<string>();

        public string TermStoreName { get; set; } = string.Empty;
    }
    public class FieldMapping
    {
        public string FieldName { get; set; } = string.Empty;

        public string SpoColumnName { get; set; } = string.Empty;

        public string TermsetName { get; set; } = string.Empty;

        public object? DefaultValue { get; set; }

        public bool Required { get; set; }

        public bool ReadOnly { get; set; }
    }

    public class LocationMapping
    {
        public string Classification { get; set; } = string.Empty;

        public string Source { get; set; } = string.Empty;

        public string SiteId { get; set; } = string.Empty;

        public string ListId { get; set; } = string.Empty;

        public string ExternalReferenceListId { get; set; } = string.Empty;

        // optioneel
        public string folderName { get; set; } = string.Empty;
    }
}