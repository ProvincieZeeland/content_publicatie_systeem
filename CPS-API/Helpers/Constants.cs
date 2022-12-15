namespace CPS_API.Helpers
{
    public static class Constants
    {
        public const string ObjectIdentifiersTableName = "ObjectIdentifiers";

        public const string SettingsTableName = "Settings";
        public const string SettingsPartitionKey = "397ad8b3-569b-4b96-89b4-2544e5b54510";
        public const string SettingsSequenceRowKey = "SequenceNumber";
        public const string SettingsLastSynchronisationRowKey = "LastSynchronisation";

        public const string ContentContainerName = "content";
        public const string MetadataContainerName = "metadata";
    }
}