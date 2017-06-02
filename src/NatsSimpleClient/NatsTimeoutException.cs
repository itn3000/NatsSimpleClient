using System;
using System.Collections.Generic;
using System.Text;

namespace NatsSimpleClient
{
    public class NatsTimeoutException : Exception
    {
        public NatsTimeoutException()
            : base()
        {

        }
    }
}
