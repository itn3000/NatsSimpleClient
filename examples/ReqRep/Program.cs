using System;

namespace ReqRep
{
    using NatsSimpleClient;
    class Program
    {
        static void ProcessMessage(NatsConnection con, NatsMessage msg)
        {
            Console.WriteLine($"{msg.Subject},{msg.Sid},{msg.Data.Length}");
            if (!string.IsNullOrEmpty(msg.Reply))
            {
                // if reply subject is set, you must send publish to reply subject
                con.Publish(msg.Reply, null, new byte[1]);
            }
        }
        static void WaitAndProcess(NatsConnection con)
        {
            while (true)
            {
                var res = con.WaitMessage();
                switch (res.Kind)
                {
                    case NatsServerResponseId.Msg:
                        // subscribed message has come from NATS Server
                        // you must publish to *replyto subject* if replyto is not null and not empty string
                        ProcessMessage(con, res.Msg);
                        return;
                    default:
                        // wait for msg
                        break;
                }
            }
        }
        static void Main(string[] args)
        {
            const string subject = "natscsharp.example.reqrep.subject";
            const string replyto = "natscsharp.example.reqrep.reply";
            var opt = NatsConnectOption.CreateDefault();
            using (var con = NatsConnection.Create("127.0.0.1", 4222, opt, true))
            {
                var sid = con.Subscribe(subject, null);
                var repsid = con.Subscribe(replyto, null);
                // auto unsubscribe after reply is received.
                con.Unsubscribe(repsid, 1);
                // Publish message with reply subject
                con.Publish(subject, replyto, new byte[1]);
                // process publish
                WaitAndProcess(con);
                // process reply subject
                WaitAndProcess(con);
                con.Unsubscribe(sid);
            }
        }
    }
}
