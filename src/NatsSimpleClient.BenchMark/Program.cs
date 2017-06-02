using System;
using BenchmarkDotNet.Running;

namespace NatsSimpleClient.BenchMark
{
    class Program
    {
        static void Main(string[] args)
        {
            var summary = BenchmarkRunner.Run<PubSubBench>();
            foreach(var report in summary.Reports)
            {
                Console.WriteLine($"{report.ToString()}");
            }
        }
    }
}
