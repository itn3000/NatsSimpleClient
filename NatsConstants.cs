namespace nats_simple_client
{
    static class NatsConstants
    {
    }
    static class NatsServerMessageKind
    {
        public const string Info = "INFO";
        public const string Msg = "MSG";
        public const string Ok = "+OK";
        public const string Err = "-ERR";
    }
    static class NatsClientMessageKind
    {
        public const string Connect = "CONNECT";
        public const string Pub = "PUB";
        public const string Sub = "SUB";
        public const string Unsub = "UNSUB";
    }
    static class NatsBothMessageKind
    {
        public const string Ping = "PING";
        public const string Pong = "PONG";
    }
}