#nullable enable

using BenchmarkDotNet.Running;
using Fuookami.Ospf.Benchmark;
using System;
using System.Linq;
using System.Reflection;

namespace Fuookami.Ospf.Benchmark;

/// <summary>
/// Benchmark 运行入口 / Benchmark runner entry.
/// </summary>
/// <remarks>
/// 用法 / Usage:
/// <code>
/// dotnet run -c Release --project src/Benchmark -- \
///   '.*' small 1 1 1 json BenchmarkDotNet.Artifacts/results/run.json
/// </code>
///
/// 位置参数依次为：包含正则、数据集标签、fork 数、预热轮数、测量轮数、结果格式、结果文件。
/// **不给参数时退回全部基准 + `small` + 单轮**。
///
/// The positional arguments are: inclusion regex, dataset label, fork count, warmup iterations, measurement iterations, result format, result file.
/// **With no arguments it falls back to all benchmarks + `small` + a single round**, matching the code defaults.
/// </remarks>
public static class Program {
    /// <summary>入口 / Entry point.</summary>
    /// <param name="args">命令行参数 / Command-line arguments.</param>
    public static void Main(string[] args) {
        // **`--dump` 走跨版本文本转储，不计时。** **`CrossVersionSemanticDump` 是一个独立 main，
        // 本仓的基准项目只有一个入口，所以在这里分叉；** **它必须在参数解析之前，否则 `--dump` 会被当成非法参数。**
        //
        // **`--dump` runs the cross-version text dump instead of timing.** **dump is a separate main; this benchmark
        // project has a single entry point, so the fork lives here — and it must sit before argument parsing, or `--dump` would be rejected as
        // an invalid argument.**
        if (args.Length == 1 && string.Equals(args[0], "--dump", StringComparison.Ordinal)) {
            Console.Out.Write(CrossVersionSemanticDump.Run());
            return;
        }

        if (!BenchmarkRunOptions.TryParse(args, out BenchmarkRunOptions options, out string? error)) {
            // **非法参数直接失败退出**：静默退回默认值会让"十轮测量"跑成一轮，而报告里看不出来。
            // **Invalid arguments fail hard**: silently falling back to defaults turns a ten-iteration measurement into one round, and nothing in the
            // report shows it.
            Console.Error.WriteLine($"invalid benchmark arguments: {error}");
            Environment.ExitCode = 2;
            return;
        }
        BenchmarkSwitcher
            .FromAssembly(Assembly.GetExecutingAssembly())
            .Run(options.ToCommandLine().ToArray());
    }
}
