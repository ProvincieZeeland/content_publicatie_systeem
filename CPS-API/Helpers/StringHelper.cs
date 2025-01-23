namespace CPS_API.Helpers
{
    public static class StringHelper
    {
        public static string? GetStringValueOrDefault(IDictionary<string, object> AdditionalData, string key)
        {
            if (AdditionalData == null || !AdditionalData.TryGetValue(key, out var value))
            {
                return string.Empty;
            }
            return value.ToString();
        }
    }
}
