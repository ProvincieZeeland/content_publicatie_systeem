namespace CPS_API.Models
{
    public static class Constants
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
        public const string CacheKeyTermsId = "CacheKeyTermsId";
        public const string DropOffSubscriptionExpirationDateTime = "DropOffSubscriptionExpirationDateTime";
        public const string DropOffSubscriptionId = "DropOffSubscriptionId";
        public const string DropOffLastChangeToken = "DropOffLastChangeToken";
        public const string DropOffSubscriptionExpirationDateTimeFinancial = "DropOffSubscriptionExpirationDateTimeFinancial";
        public const string DropOffSubscriptionIdFinancial = "DropOffSubscriptionIdFinancial";
        public const string DropOffLastChangeTokenFinancial = "DropOffLastChangeTokenFinancial";
        public const int InvalidChangeTokenErrorCode = -2146233086;
        public const int InvalidChangeTokenTimeErrorCode = -2130575172;
        public const int InvalidChangeTokenWrongObjectErrorCode = -2130575173;
        public const int FormatChangeTokenErrorCode = -1;
        public const int InvalidOperationChangeTokenErrorCode = -1;
        public const string InvalidChangeTokenServerErrorTypeName = "System.ArgumentOutofRangeException";
        public const string FormatChangeTokenServerErrorTypeName = "System.FormatException";
        public const string InvalidOperationChangeTokenServerErrorTypeName = "System.InvalidOperationException";
        public const string InvalidChangeTokenTimeErrorMessageDutch = "Het changeToken verwijst naar een tijdstip vóór het begin van het huidige wijzigingenlogboek.";
        public const string InvalidChangeTokenTimeErrorMessageEnglish = "The changeToken refers to a time before the start of the current change log.";
        public const string InvalidChangeTokenWrondObjectErrorMessageDutch = "U kunt het changeToken van het ene object niet voor het andere object gebruiken.";
        public const string InvalidChangeTokenWrongObjectErrorMessageEnglish = "Cannot use the changeToken from one object against a different object";
        public const string DateTimeNow = "DateTime.Now";
        public const string NameAlreadyExistsErrorCode = "nameAlreadyExists";
        public const string ObjectIdSpoColumnName = "ObjectID";
        public const string InvalidHostnameForThisTenancyErrorMessage = "Invalid hostname for this tenancy";
        public const string ItemNotFoundErrorMessage = "Item not found";
        public const string ProvidedDriveIdMalformedErrorMessage = "The provided drive id appears to be malformed, or does not represent a valid drive.";
        public const string DropOffMetadataStatusProcessed = "Verwerkt";
        public const string DispositionTypeFormData = "form-data";
        public const string ItemPropertyInfoName = "Item";
        public const string SelectSharePointIds = "sharepointids";
        public const string SelectFields = "Fields";
        public const string SelectId = "id";
        public const string SelectWebUrl = "webUrl";
        public static readonly TimeSpan RegexMatchTimeout = TimeSpan.FromSeconds(2);
    }
}