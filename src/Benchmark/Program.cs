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
/// **不给参数时退回全部基准 + `small` + 单轮**，与基线默认一致。
///
/// The positional arguments are: inclusion regex, dataset label, fork count, warmup iterations, measurement iterations, result format, result file.
/// **With no arguments it falls back to all benchmarks + `small` + a single round**, matching the baseline defaults.
/// </remarks>
public static class Program {
    /// <summary>入口 / Entry point.</summary>
    /// <param name="args">命令行参数 / Command-line arguments.</param>
    public static void Main(string[] args) {
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
