namespace CPS_API.Models
{
    public class NameAlreadyExistsException : Exception
    {
        public NameAlreadyExistsException()
        {
        }

        public NameAlreadyExistsException(string message)
            : base(message)
        {
        }

        public NameAlreadyExistsException(string message, Exception inner)
            : base(message, inner)
        {
        }
    }
}
