namespace CPS_API.Models.Exceptions
{
    public class CpsException : Exception
    {
        public CpsException(string? message) : base(message)
        {
        }

        public CpsException(string? message, Exception? innerException) : base(message, innerException)
        {
        }
    }
}
