

namespace NatsSimpleClient.Test
{
    using System;
    using System.Threading.Tasks;
    using System.Linq;
    using Xunit;
    using Xunit.Abstractions;
    public class PublishTest
    {
        ITestOutputHelper m_Outputter;
        public PublishTest(ITestOutputHelper outputter)
        {
            m_Outputter = outputter;
        }
        [Fact]
        public async Task PubSub()
        {
            var opt = NatsConnectOption.CreateDefault();
            opt.verbose = false;
            using (var con = NatsConnection.Create("127.0.0.1", 4222, opt, true))
            {
                const string subject = "natscsharpp";
                var sid = con.Subscribe(subject, null);
                con.Flush();
                var dat = new byte[] { 0x32, 0x32 };
                const int loopCount = 1000;
                var sw = new System.Diagnostics.Stopwatch();
                sw.Start();
                await Task.WhenAll(
                    Task.Run(() =>
                    {
                        m_Outputter.WriteLine("begin subscription");
                        for (int i = 0; i < loopCount; i++)
                        {
                            while (true)
                            {
                                try
                                {
                                    var ret = con.WaitMessage();
                                    if (ret.Kind != NatsServerResponseId.None)
                                    {
                                        Assert.Equal(NatsServerResponseId.Msg, ret.Kind);
                                        Assert.Equal(i, Util.btoi32(ret.Msg.Data));
                                        break;
                                    }
                                }
                                catch (Exception e)
                                {
                                    m_Outputter.WriteLine($"{i},{e}");
                                    throw;
                                }
                            }
                        }
                    })
                    ,
                    Task.Run(() =>
                    {
                        m_Outputter.WriteLine($"begin publish");
                        using (var producer = NatsConnection.Create("127.0.0.1", 4222, opt, true))
                        {
                            var buf = new byte[4];
                            for (int i = 0; i < loopCount; i++)
                            {
                                Util.i32tob(i, buf);
                                producer.Publish(subject, null, buf);
                            }
                            producer.Flush();
                            m_Outputter.WriteLine($"publish done");
                        }
                    })
                );
                m_Outputter.WriteLine($"finished: {loopCount},{sw.Elapsed},rps={loopCount * 1000 / sw.ElapsedMilliseconds}");
            }
        }
    }

}