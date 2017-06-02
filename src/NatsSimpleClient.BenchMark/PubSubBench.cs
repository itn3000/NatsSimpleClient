namespace NatsSimpleClient.BenchMark
{
    using System.Threading.Tasks;
    using BenchmarkDotNet.Attributes;
    public class PubSubBench
    {
        [Benchmark]
        public void PublishBench()
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
                Task.WhenAll(
                    Task.Run(() =>
                    {
                        for (int i = 0; i < loopCount; i++)
                        {
                            while (true)
                            {
                                    var ret = con.WaitMessage();
                                    if (ret.Kind != NatsServerResponseId.None)
                                    {
                                        break;
                                    }
                            }
                        }
                    })
                    ,
                    Task.Run(() =>
                    {
                        using (var producer = NatsConnection.Create("127.0.0.1", 4222, opt, true))
                        {
                            var buf = new byte[4];
                            for (int i = 0; i < loopCount; i++)
                            {
                                Util.i32tob(i, buf);
                                producer.Publish(subject, null, buf);
                            }
                            producer.Flush();
                        }
                    })
                ).Wait();
            }
        }
    }
}