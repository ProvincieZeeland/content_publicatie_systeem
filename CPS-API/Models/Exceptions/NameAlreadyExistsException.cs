﻿namespace CPS_API.Models.Exceptions
{
    public class NameAlreadyExistsException : Exception//NOSONAR
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