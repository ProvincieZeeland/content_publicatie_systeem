namespace CPS_API.Models
{
    public class GlobalSettings
    {
        public string StorageTableConnectionstring { get; set; } = string.Empty;
        public string FileStorageConnectionstring { get; set; } = string.Empty;

        public string ObjectIdentifiersTable { get; set; } = string.Empty;
        public string AppSettingsTable { get; set; } = string.Empty;

        public string CallbackUrl { get; set; } = string.Empty;
        public LoggingLevel LoggingLevel { get; set; }

        public IEnumerable<FieldMapping> MetadataSettings { get; set; }

        public List<FieldMapping> ExternalReferencesMapping { get; set; }

        public List<LocationMapping> LocationMapping { get; set; }
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
    }

    public class LocationMapping
    {
        public Classification Classification { get; set; }

        public Source Source { get; set; }

        // Afhankelijk van SPO inrichting > wordt mogelijk anders
        public string SiteId { get; set; }

        public string ListId { get; set; }

        public string ExternalReferenceListId { get; set; }

        // optioneel
        public string folderName { get; set; } = string.Empty;
    }
}