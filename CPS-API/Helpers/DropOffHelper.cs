using CPS_API.Models;

namespace CPS_API.Helpers
{
    public static class DropOffHelper
    {
        public static string GetDropOffSubscriptionExpirationDateTime(this DropOffType dropOffType)
        {
            if (dropOffType == DropOffType.Financial) return Constants.DropOffSubscriptionExpirationDateTimeFinancial;
            else return Constants.DropOffSubscriptionExpirationDateTime;
        }

        public static string GetDropOffSubscriptionId(this DropOffType dropOffType)
        {
            if (dropOffType == DropOffType.Financial) return Constants.DropOffSubscriptionIdFinancial;
            else return Constants.DropOffSubscriptionId;
        }

        public static string GetDropOffLastChangeToken(this DropOffType dropOffType)
        {
            if (dropOffType == DropOffType.Financial) return Constants.DropOffLastChangeTokenFinancial;
            else return Constants.DropOffLastChangeToken;
        }

        public static string? GetDropOffSiteId(this DropOffType dropOffType, List<WebHookList> webHookLists)
        {
            var dropOffList = webHookLists.Find(item => item.DropOffType == dropOffType);
            return dropOffList?.SiteId;
        }

        public static string? GetDropOffListId(this DropOffType dropOffType, List<WebHookList> webHookLists)
        {
            var dropOffList = webHookLists.Find(item => item.DropOffType == dropOffType);
            return dropOffList?.ListId;
        }
    }
}
