﻿namespace CPS_API.Models.Exceptions
{
    public class ObjectIdAlreadyExistsException : Exception
    {
        public ObjectIdAlreadyExistsException()
        {
        }

        public ObjectIdAlreadyExistsException(string message)
            : base(message)
        {
        }

        public ObjectIdAlreadyExistsException(string message, Exception inner)
            : base(message, inner)
        {
        }
    }
}