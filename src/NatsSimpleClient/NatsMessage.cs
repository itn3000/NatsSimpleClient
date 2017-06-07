namespace NatsSimpleClient
{
    using System.Collections.Generic;
    public struct NatsMessage
    {
        /// <summary>subscribed subject</summary>
        public string Subject {get;set;}
        /// <summary>subject you should replyto if publisher set</summary>
        public string Reply {get;set;}
        /// <summary>published data</summary>
        public byte[] Data{get;set;}
        /// <summary>subscription ID(connection unique)</summary>
        public long Sid{get;set;}
    }
}