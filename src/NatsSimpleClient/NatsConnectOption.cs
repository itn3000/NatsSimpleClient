namespace NatsSimpleClient
{
    /// <summary>NATS Connection options</summary>
    /// <seealso>https://nats.io/documentation/internals/nats-protocol/</seealso>
    public struct NatsConnectOption
    {
        /// <summary>creating connection option with default parameter</summary>
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
        /// <summary>if true, you will receive '+OK' after publish, subscribe, unsubscribe</summary>
        /// <remarks>default false</remarks>
        public bool verbose {get; set;}
        /// <summary>turns on additional strict format checking</summary>
        /// <remarks>default false</remarks>
        public bool pedantic {get; set;}
        /// <summary>indicates whether the client requires TLS</summary>
        /// <remarks>default false</remarks>
        public bool ssl_required{get; set;}
        /// <summary>client authorization token</summary>
        /// <remarks>default null</remarks>
        public string auth_token {get;set;}
        /// <summary>user name</summary>
        /// <remarks>default null</remarks>
        public string user {get;set;}
        /// <summary>password</summary>
        /// <remarks>default null</remarks>
        public string pass {get;set;}
        /// <summary>client application name(this is useful for logging)</summary>
        /// <remarks>default 'SimpleNatsClient'</remarks>
        public string name {get;set;}
        /// <summary>client program language(this is useful for logging)</summary>
        /// <remarks>default 'CSharp'</remarks>
        public string lang{get;set;}
        /// <summary>client program version(this is useful for logging)</summary>
        /// <remarks>default '0.0.1'</remarks>
        public string version{get;set;}
    }
}