using System;

namespace PubSub
{
    using NatsSimpleClient;
    class Program
    {
        static void ProcessMessage(NatsConnection con, NatsMessage msg)
        {
            Console.WriteLine($"{msg.Subject},{msg.Sid},{msg.Data.Length}");
            if(!string.IsNullOrEmpty(msg.Reply))
            {
                // if reply subject is set, you must send publish to reply subject
                con.Publish(msg.Reply, null, new byte[1]);
            }
        }
        static void Main(string[] args)
        {
            const string subject = "natscsharp.example.pubsub";
            var opt = NatsConnectOption.CreateDefault();
            using (var con = NatsConnection.Create("127.0.0.1", 4222, opt, true))
            {
                var sid = con.Subscribe(subject, null);
                con.Publish(subject, null, new byte[1]);
                bool done = false;
                while (!done)
                {
                    var res = con.WaitMessage();
                    switch (res.Kind)
                    {
                        case NatsServerResponseId.Msg:
                            // subscribed message has come from NATS Server
                            // you must publish to *replyto subject* if replyto is not null and not empty string
                            ProcessMessage(con, res.Msg);
                            done = true;
                            break;
                        case NatsServerResponseId.Ok:
                            // NATS Server returns +OK(come when verbose=true)
                            // you can ignore this response
                            break;
                        case NatsServerResponseId.Err:
                            // NATS Server error message has come
                            Console.WriteLine($"server error:{res.Error.ErrorString}");
                            break;
                        case NatsServerResponseId.Ping:
                            // ping(keepalive) message has come from server.
                            // you must send "PONG" to server.
                            con.SendPong();
                            break;
                        case NatsServerResponseId.Timeout:
                            // socket wait timeout.
                            break;
                        default:
                            throw new NotSupportedException("unknown response");
                    }
                }
                con.Unsubscribe(sid);
            }
        }
    }
}
