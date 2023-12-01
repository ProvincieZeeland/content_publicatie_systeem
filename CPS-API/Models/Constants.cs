namespace CPS_API.Models
{
    public class Constants
    {
        public const string SettingsSequenceField = "SequenceNumber";
        public const string SettingsLastSynchronisationNewField = "LastSynchronisationNew";
        public const string SettingsLastSynchronisationChangedField = "LastSynchronisationChanged";
        public const string SettingsLastTokenForNewField = "LastTokenForNew";
        public const string SettingsLastTokenForChangedField = "LastTokenForChanged";
        public const string SettingsLastTokenForDeletedField = "LastTokenForDeleted";
        public const string SettingsIsNewSynchronisationRunningField = "IsNewSynchronisationRunning";
        public const string SettingsIsChangedSynchronisationRunningField = "IsChangedSynchronisationRunning";
        public const string SettingsIsDeletedSynchronisationRunningField = "IsDeletedSynchronisationRunning";
        public const string CacheKeyTermGroup = "CacheKeyTermGroup";
        public const string DropOffSubscriptionExpirationDateTime = "DropOffSubscriptionExpirationDateTime";
        public const string DropOffSubscriptionId = "DropOffSubscriptionId";
        public const string DropOffLastChangeToken = "DropOffLastChangeToken";
        public const int ERROR_CODE_INVALID_CHANGE_TOKEN = -2146233086;
        public const int ERROR_CODE_INVALID_CHANGE_TOKEN_TIME = -2130575172;
        public const int ERROR_CODE_INVALID_CHANGE_TOKEN_WRONG_OBJECT = -2130575173;
        public const int ERROR_CODE_FORMAT_CHANGE_TOKEN = -1;
        public const int ERROR_CODE_INVALID_OPERATION_CHANGE_TOKEN = -1;
    }
}