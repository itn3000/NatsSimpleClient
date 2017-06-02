using System;

namespace nats_simple_client
{
    using System.Threading.Tasks;
    using System.Net;
    using System.Net.Sockets;
    using System.Text;
    using System.Linq;
    class Program
    {
        static void i32tob(int i, byte[] buf)
        {
            buf[0] = (byte)(i & 0xff);
            buf[1] = (byte)((i >> 8) & 0xff);
            buf[2] = (byte)((i >> 16) & 0xff);
            buf[3] = (byte)((i >> 24) & 0xff);
        }
        static int btoi32(byte[] b)
        {
            return (int)(b[0])
                + ((int)b[1] << 8)
                + ((int)b[2] << 16)
                + ((int)b[3] << 24)
                ;
        }
        static void PubSub()
        {
            var opt = NatsConnectOption.CreateDefault();
            opt.verbose = false;
            using (var con = NatsConnection.Create("127.0.0.1", 4222, opt, true))
            {
                const string subject = "natscsharpp";
                var sid = con.Subscribe(subject, null);
                con.Flush();
                var dat = new byte[] { 0x32, 0x32 };
                const int loopCount = int.MaxValue/1000;
                var sw = new System.Diagnostics.Stopwatch();
                sw.Start();
                Task.WhenAll(
                    Task.Run(() =>
                    {
                        Console.WriteLine($"begin subscription");
                        NatsResponse lastResponse = default(NatsResponse);
                        for (int i = 0; i < loopCount; i++)
                        {
                            try
                            {
                                while (true)
                                {
                                    var ret = con.WaitMessage();
                                    if (ret.Kind == NatsServerMessageId.Msg)
                                    {
                                        lastResponse = ret;
                                        if (!string.IsNullOrEmpty(ret.Msg.Reply))
                                        {
                                            con.Publish(ret.Msg.Reply, null, ret.Msg.Data.ToArray());
                                        }
                                        if (i % (loopCount / 10) == (loopCount / 10 - 1))
                                        {
                                            Console.WriteLine($"msg: {btoi32(ret.Msg.Data)},{i}");
                                        }
                                        break;
                                    }
                                }
                            }
                            catch (Exception e)
                            {
                                Console.WriteLine($"{i},{btoi32(lastResponse.Msg.Data)},{e}");
                                throw;
                            }
                        }
                    })
                    ,
                    Task.Run(() =>
                    {
                        Console.WriteLine($"begin publish");
                        using (var producer = NatsConnection.Create("127.0.0.1", 4222, opt, true))
                        {
                            var buf = new byte[4];
                            for (int i = 0; i < loopCount; i++)
                            {
                                i32tob(i, buf);
                                producer.Publish(subject, null, buf);
                            }
                            producer.Flush();
                            //await Task.Delay(1000).ConfigureAwait(false);
                            Console.WriteLine($"publish done");
                        }
                    })
                ).Wait();
                Console.WriteLine($"finished: {loopCount},{sw.Elapsed},rps={loopCount * 1000 / sw.ElapsedMilliseconds}");
            }
        }
        static void ReqRep()
        {
            var opt = NatsConnectOption.CreateDefault();
            opt.verbose = false;
            using (var con = NatsConnection.Create("127.0.0.1", 4222, NatsConnectOption.CreateDefault()))
            {
                const string subject = "natscsharp";
                const string replySubject = "replyto";
                var sid = con.Subscribe(subject, null);
                var dat = new byte[] { 0x32, 0x32 };
                const int loopCount = 100000;
                var sw = new System.Diagnostics.Stopwatch();
                sw.Start();
                Task.WhenAll(
                    Task.Run(() =>
                    {
                        Console.WriteLine($"begin subscription");
                        for (int i = 0; i < loopCount; i++)
                        {
                            while (true)
                            {
                                var ret = con.WaitMessage();
                                if (ret.Kind == NatsServerMessageId.Msg)
                                {
                                    if (!string.IsNullOrEmpty(ret.Msg.Reply))
                                    {
                                        con.Publish(ret.Msg.Reply, null, ret.Msg.Data.ToArray());
                                    }
                                    break;
                                }
                            }
                        }
                    })
                    ,
                    Task.Run(async () =>
                    {
                        Console.WriteLine($"begin publish");
                        using (var producer = NatsConnection.Create("127.0.0.1", 4222, NatsConnectOption.CreateDefault(), true))
                        {
                            for (int i = 0; i < loopCount; i++)
                            {
                                var reqsid = producer.Subscribe(replySubject, null);
                                producer.Unsubscribe(reqsid, 1);
                                producer.Publish(subject, replySubject, dat);
                                producer.Flush();
                                for (int j = 0; j < 5; j++)
                                {
                                    var res = producer.WaitMessage();
                                    if (res.Kind == NatsServerMessageId.Msg)
                                    {
                                        if (i % (loopCount / 10) == (loopCount / 10 - 1))
                                        {
                                            Console.WriteLine($"reply:{i},{sw.Elapsed},{Encoding.UTF8.GetString(res.Msg.Data)}");
                                        }
                                        break;
                                    }
                                }
                            }
                            await Task.Delay(1000).ConfigureAwait(false);
                        }
                    })
                ).Wait();
                Console.WriteLine($"finished: {loopCount},{sw.Elapsed},rps={loopCount * 1000 / sw.ElapsedMilliseconds}");
                // con.WaitMessage().Wait();
                // System.Threading.Thread.Sleep(3000);
            }
        }
        static void Main(string[] args)
        {
            //ReqRep();
            PubSub();
        }
    }
}
