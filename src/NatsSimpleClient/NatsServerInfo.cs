namespace NatsSimpleClient
{
    public struct ServerInfo
    {
        public string server_id { get; set; }
        public string version { get; set; }
        public string go { get; set; }
        public string host { get; set; }
        public int port { get; set; }
        public bool auth_required { get; set; }
        public bool ssl_required { get; set; }
        public long max_payload { get; set; }
    }
}