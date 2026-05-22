namespace CPS_API.Models
{
    public static class Constants
    {
        public const string CacheKeyTermsId = "CacheKeyTermsId";
        public const string DateTimeNow = "DateTime.Now";
        public const string ObjectIdSpoColumnName = "ObjectID";
        public const string DropOffMetadataStatusProcessed = "Verwerkt";
        public const string DispositionTypeFormData = "form-data";
        public const string ItemPropertyInfoName = "Item";
        public const string ErrorMessagePropertiesFormatString = "{ErrorMessage} | {Properties}";
        public const string NewDocumentsSynchronisationError = "Error while synchronising new documents";
        public const string ErrorView = "Error";

        public static class Webhook
        {
            public const string Queue = "sharepointlistwebhooknotifications";
        }

        public static class Selectors
        {
            public const string SharePointIds = "sharepointids";
            public const string Fields = "Fields";
            public const string Id = "id";
            public const string WebUrl = "webUrl";
        }

        public static class ChangeTokenErrors
        {
            public const int InvalidErrorCode = -2146233086;
            public const int InvalidTimeErrorCode = -2130575172;
            public const int InvalidWrongObjectErrorCode = -2130575173;
            public const int FormatErrorCode = -1;
            public const int InvalidOperationCode = -1;
            public const string InvalidServerErrorTypeName = "System.ArgumentOutofRangeException";
            public const string FormatServerErrorTypeName = "System.FormatException";
            public const string InvalidOperationServerErrorTypeName = "System.InvalidOperationException";
            public const string InvalidTimeErrorMessageDutch = "Het changeToken verwijst naar een tijdstip vóór het begin van het huidige wijzigingenlogboek.";
            public const string InvalidTimeErrorMessageEnglish = "The changeToken refers to a time before the start of the current change log.";
            public const string InvalidWrondObjectErrorMessageDutch = "U kunt het changeToken van het ene object niet voor het andere object gebruiken.";
            public const string InvalidWrongObjectErrorMessageEnglish = "Cannot use the changeToken from one object against a different object";
        }

        public static class ODataErrors
        {
            public const string NameAlreadyExists = "nameAlreadyExists";
            public const string InvalidHostnameForThisTenancy = "Invalid hostname for this tenancy";
            public const string ProvidedDriveIdMalformed = "The provided drive id appears to be malformed, or does not represent a valid drive.";
        }
    }
}