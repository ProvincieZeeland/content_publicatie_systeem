using System;

namespace CPS_Jobs.Models
{
    public class CpsException : Exception//NOSONAR
    {
        public CpsException(string message) : base(message)
        {
        }

        public CpsException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}