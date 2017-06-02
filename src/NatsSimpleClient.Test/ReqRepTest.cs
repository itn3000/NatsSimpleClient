namespace NatsSimpleClient.Test
{
    using System;
    using System.Threading.Tasks;
    using System.Linq;
    using Xunit;
    using Xunit.Abstractions;
    using System.Text;
    public class ReqRepTest
    {
        ITestOutputHelper m_Outputter;
        public ReqRepTest(ITestOutputHelper outputter)
        {
            m_Outputter = outputter;
        }
        [Fact]
        public async Task ReqRep()
        {
            var opt = NatsConnectOption.CreateDefault();
            opt.verbose = false;
            using (var con = NatsConnection.Create("127.0.0.1", 4222, NatsConnectOption.CreateDefault()))
            {
                const string subject = "natscsharp";
                const string replySubject = "replyto";
                var sid = con.Subscribe(subject, null);
                var dat = new byte[] { 0x32, 0x32 };
                const int loopCount = 100;
                var sw = new System.Diagnostics.Stopwatch();
                sw.Start();
                await Task.WhenAll(
                    Task.Run(() =>
                    {
                        m_Outputter.WriteLine($"begin subscription");
                        for (int i = 0; i < loopCount; i++)
                        {
                            while (true)
                            {
                                var ret = con.WaitMessage();
                                if (ret.Kind == NatsServerResponseId.Msg)
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
                        m_Outputter.WriteLine($"begin publish");
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
                                    if (res.Kind == NatsServerResponseId.Msg)
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
                );
                m_Outputter.WriteLine($"finished: {loopCount},{sw.Elapsed},rps={loopCount * 1000 / sw.ElapsedMilliseconds}");
                // con.WaitMessage().Wait();
                // System.Threading.Thread.Sleep(3000);
            }
        }

    }
}