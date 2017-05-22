namespace nats_simple_client
{
    struct NatsConnectOption
    {
        public static NatsConnectOption CreateDefault()
        {
            return new NatsConnectOption()
            {
                verbose = false
                , pedantic = false
                , ssl_required = false
                , auth_token = null
                , user = null
                , pass = null
                , name = "SimpleNatsClient"
                , lang = "CSharp"
                , version = "0.0.1"
            };
        }
        public bool verbose {get; set;}
        public bool pedantic {get; set;}
        public bool ssl_required{get; set;}
        public string auth_token {get;set;}
        public string user {get;set;}
        public string pass {get;set;}
        public string name {get;set;}
        public string lang{get;set;}
        public string version{get;set;}
    }
}