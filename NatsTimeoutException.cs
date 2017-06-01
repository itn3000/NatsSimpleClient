using System;
using System.Collections.Generic;
using System.Text;

namespace nats_simple_client
{
    class NatsTimeoutException : Exception
    {
        public NatsTimeoutException()
            : base()
        {

        }
    }
}
