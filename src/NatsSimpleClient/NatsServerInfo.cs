namespace NatsSimpleClient
{
    /// <summary>information sent from NATS Server</summary>
    /// <seealso>https://nats.io/documentation/internals/nats-protocol/</summary>
    public struct ServerInfo
    {
        /// <summary>unique identifier of NATS Server</summary>
        public string server_id { get; set; }
        /// <summary>NATS Server version</summary>
        public string version { get; set; }
        /// <summary>The version of golang the NATS server was built with</summary>
        public string go { get; set; }
        /// <summary>The IP address of the NATS server host</summary>
        public string host { get; set; }
        /// <summary>The port number the NATS server is configured to listen on</summary>
        public int port { get; set; }
        /// <summary>If this is set, then the client should try to authenticate upon connect.</summary>
        public bool auth_required { get; set; }
        /// <summary>If this is set, then the client must authenticate using SSL.</summary>
        public bool ssl_required { get; set; }
        /// <summary>Maximum payload size that the server will accept from the client.</summary>
        public long max_payload { get; set; }
    }
}