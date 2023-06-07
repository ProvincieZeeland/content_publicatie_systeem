namespace CPS_API.Models.Exceptions
{
    public class FieldRequiredException : Exception
    {
        public FieldRequiredException()
        {
        }

        public FieldRequiredException(string message)
            : base(message)
        {
        }

        public FieldRequiredException(string message, Exception inner)
            : base(message, inner)
        {
        }
    }
}
