namespace NatsSimpleClient
{
    static class NatsServerResponseString
    {
        public const string Info = "INFO";
        public const string Msg = "MSG";
        public const string Ok = "+OK";
        public const string Err = "-ERR";
        public const string Ping = "PING";
    }
    public enum NatsServerResponseId
    {
        Info,
        Msg,
        Ok,
        Err,
        Ping,
        Timeout,
        None,
    }
    static class NatsClientMessageString
    {
        public const string Connect = "CONNECT";
        public const string Pub = "PUB";
        public const string Sub = "SUB";
        public const string Unsub = "UNSUB";
    }
    static class NatsBothMessageString
    {
        public const string Ping = "PING";
        public const string Pong = "PONG";
    }
}