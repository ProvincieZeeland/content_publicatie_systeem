namespace CPS_API.Models
{
    public enum SynchronisationType
    {
        create,
        update,
        delete
    }

    public static class SynchronisationTypeFunctions
    {
        public static string? GetLabel(this SynchronisationType synchronisationType)
        {
            switch (synchronisationType)
            {
                case SynchronisationType.create:
                    return "create";
                case SynchronisationType.update:
                    return "update";
                case SynchronisationType.delete:
                    return "delete";
            }
            return null;
        }
    }
}