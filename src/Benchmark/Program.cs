#nullable enable

using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;
using System;
using System.Linq;

namespace Fuookami.Ospf.Benchmark;
/// <summary>
/// Benchmark 运行入口 / Benchmark runner entry.
/// <para>
/// 用法 / Usage:
/// <code>dotnet run -c Release --project src/Benchmark -- --filter '*'</code>
/// </para>
/// <para>
/// 可选参数 / Optional arguments:
/// <list type="bullet">
///   <item><description><c>--filter &lt;pattern&gt;</c> — benchmark name glob filter</description></item>
///   <item><description><c>--dry</c> — list matched benchmarks without running</description></item>
/// </list>
/// </para>
/// </summary>
public static class Program {
    /// <summary>程序入口 / Entry point.</summary>
    public static void Main(string[] args) {
        var switcher = BenchmarkSwitcher.FromTypes(new[]
        {
            typeof(CoreHotPathBenchmark),
            typeof(CorePluginDumpBenchmark),
            typeof(SymbolCombineBenchmark),
            typeof(MultiArrayHotPathBenchmark)
        });

        IConfig config = DefaultConfig.Instance;
        switcher.Run(args, config);
    }
}
