namespace NatsSimpleClient
{
    using System.Collections.Generic;
    public struct NatsMessage
    {
        public string Subject {get;set;}
        public string Reply {get;set;}
        public byte[] Data{get;set;}
        public long Sid{get;set;}
    }
}