namespace CPS_API.Helpers
{
    public static class SharePointDateHelper
    {
        // TODO: Get TimeZone from Site?
        private const string WindowsTzNl = "W. Europe Standard Time";

        public static TimeZoneInfo GetNlSiteTimeZone()
        {
            return TimeZoneInfo.FindSystemTimeZoneById(WindowsTzNl);
        }

        public static bool IsDateInFuture(this DateTime sharePointDate)
        {
            var tz = GetNlSiteTimeZone();
            var itemLocal = TimeZoneInfo.ConvertTimeFromUtc(sharePointDate, tz);

            var nowLocal = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);

            var itemDate = itemLocal.Date;
            var today = nowLocal.Date;

            return (itemDate > today);
        }
    }
}
