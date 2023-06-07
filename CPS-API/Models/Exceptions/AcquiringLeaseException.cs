namespace CPS_API.Models.Exceptions
{
    public class AcquiringLeaseException : Exception
    {
        public AcquiringLeaseException()
        {
        }

        public AcquiringLeaseException(string message)
            : base(message)
        {
        }

        public AcquiringLeaseException(string message, Exception inner)
            : base(message, inner)
        {
        }
    }
}