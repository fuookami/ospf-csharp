#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Fuookami.Ospf.Benchmark;

/// <summary>
/// 基准结果导出格式 / Benchmark result export format.
/// </summary>
public enum BenchmarkResultFormat {
    /// <summary>JSON</summary>
    Json,
    /// <summary>CSV</summary>
    Csv,
    /// <summary>Markdown</summary>
    Markdown,
    /// <summary>HTML</summary>
    Html,
}

/// <summary>
/// Benchmark 运行选项，对应位置参数序列 / Benchmark run options, mirroring its positional argument sequence.
/// </summary>
/// <remarks>
///
/// **位置参数一律有默认值，且越界项不吞掉前面的解析结果**。`args.getOrNull(n)?.toIntOrNull ?: 1`，
/// 于是 `"abc"` 会被当成"没给"；这里区分"没给"与"给了但非法"，**因为把非法值静默当成默认值时，一次本该 10 轮
/// 的测量会跑成 1 轮，而报告里看不出来**。
///
/// **Positional arguments all have defaults, and an out-of-range item never swallows the earlier parse results.** The code uses
/// `args.getOrNull(n)?.toIntOrNull ?: 1`, so `"abc"` reads as "not given"; this version distinguishes "absent" from "present but invalid", **because
/// silently treating an invalid value as the default turns a ten-iteration measurement into a single iteration, and nothing in the report shows it**.
///
/// **`Dataset` 只影响结果文件名，不影响规模**。BenchmarkDotNet 没有"从命令行覆盖 `[Params]`"的开关，
/// 所以规模仍由各基准自己的 `[Params]` 决定——**假装这里能选规模，会让 `medium` 的报告其实是 `small` 的数据**。
///
/// **`Dataset` only affects the result file name, not the scale.** BenchmarkDotNet has no switch for "override `[Params]` from the command line", so the
/// scale still comes from each benchmark's own `[Params]` — **pretending a scale can be chosen here would make a `medium` report contain `small` numbers**.
/// </remarks>
/// <param name="IncludeRegex">包含过滤正则 / Inclusion filter regex.</param>
/// <param name="Dataset">数据集标签（仅用于文件名）/ Dataset label (file name only).</param>
/// <param name="Forks">fork 数 / Fork count.</param>
/// <param name="WarmupIterations">预热轮数 / Warmup iterations.</param>
/// <param name="MeasurementIterations">测量轮数 / Measurement iterations.</param>
/// <param name="ResultFormat">结果格式 / Result format.</param>
/// <param name="ResultFile">结果文件路径 / Result file path.</param>
public sealed record BenchmarkRunOptions(
    string IncludeRegex = ".*",
    string Dataset = "small",
    int Forks = 1,
    int WarmupIterations = 1,
    int MeasurementIterations = 1,
    BenchmarkResultFormat ResultFormat = BenchmarkResultFormat.Json,
    string? ResultFile = null) {
    /// <summary>实际使用的结果文件路径 / The effective result file path.</summary>
    public string EffectiveResultFile => ResultFile ?? DefaultResultFile(IncludeRegex, Dataset, ResultFormat);

    /// <summary>
    /// 解析位置参数 / Parse the positional arguments.
    /// </summary>
    /// <param name="args">命令行参数 / Command-line arguments.</param>
    /// <param name="options">解析结果 / The parsed options.</param>
    /// <param name="error">非法参数说明 / A description of the invalid argument.</param>
    /// <returns>是否解析成功 / Whether parsing succeeded.</returns>
    public static bool TryParse(
        IReadOnlyList<string> args,
        out BenchmarkRunOptions options,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(false)] out string? error) {
        ArgumentNullException.ThrowIfNull(args);
        options = new BenchmarkRunOptions();
        string include = args.Count > 0 && args[0].Length > 0 ? args[0] : ".*";
        string dataset = args.Count > 1 && args[1].Length > 0 ? args[1] : "small";
        if (!Positive(args, 2, 1, "forks", out int forks, out error)) {
            return false;
        }
        if (!Positive(args, 3, 1, "warmup iterations", out int warmup, out error)) {
            return false;
        }
        if (!Positive(args, 4, 1, "measurement iterations", out int measurement, out error)) {
            return false;
        }
        options = new BenchmarkRunOptions(
            IncludeRegex: include,
            Dataset: dataset,
            Forks: forks,
            WarmupIterations: warmup,
            MeasurementIterations: measurement,
            ResultFormat: ParseResultFormat(args.Count > 5 ? args[5] : null),
            ResultFile: args.Count > 6 && args[6].Length > 0 ? args[6] : null);
        return true;
    }

    /// <summary>
    /// 解析结果格式，未知值回退到 JSON / Parse the result format, falling back to JSON for unknown values.
    /// </summary>
    /// <param name="raw">原始文本 / Raw text.</param>
    /// <returns>结果格式 / The result format.</returns>
    public static BenchmarkResultFormat ParseResultFormat(string? raw) =>
        raw?.Trim().ToLowerInvariant() switch {
            "json" => BenchmarkResultFormat.Json,
            "csv" => BenchmarkResultFormat.Csv,
            "markdown" or "md" => BenchmarkResultFormat.Markdown,
            "html" => BenchmarkResultFormat.Html,
            _ => BenchmarkResultFormat.Json,
        };

    /// <summary>
    /// 过滤正则转成稳定可读的文件名 / Sanitize the filter regex into a stable, readable file name.
    /// </summary>
    /// <param name="raw">原始正则 / Raw regex.</param>
    /// <returns>文件名安全字符串 / A file-name-safe string.</returns>
    public static string SanitizeForFileName(string raw) {
        ArgumentNullException.ThrowIfNull(raw);
        var builder = new System.Text.StringBuilder(raw.Length);
        foreach (char character in raw) {
            builder.Append(
                char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-'
                    ? character
                    : '_');
        }
        string trimmed = builder.ToString().Trim('_');
        return trimmed.Length == 0 ? "all" : trimmed;
    }

    /// <summary>默认结果文件路径 / The default result file path.</summary>
    /// <param name="include">包含过滤正则 / Inclusion filter regex.</param>
    /// <param name="dataset">数据集标签 / Dataset label.</param>
    /// <param name="format">结果格式 / Result format.</param>
    /// <returns>结果文件路径 / Result file path.</returns>
    public static string DefaultResultFile(
        string include,
        string dataset,
        BenchmarkResultFormat format) =>
        Path.Combine(
            "BenchmarkDotNet.Artifacts",
            "results",
            $"{SanitizeForFileName(include)}-{dataset}.{format.ToString().ToLowerInvariant()}");

    /// <summary>
    /// 转成 BenchmarkDotNet 命令行参数 / Convert into BenchmarkDotNet command-line arguments.
    /// </summary>
    /// <remarks>
    /// **`--artifacts` 指向结果文件所在目录**，而结果文件名本身由 BenchmarkDotNet 生成：它不接受"导出到这个确切的
    /// 文件名"。所以 `ResultFile` 的文件名部分只用于**报告与目录定位**，不参与导出的最终命名。
    ///
    /// **`--artifacts` points at the directory of the result file**, and the file name itself is produced by BenchmarkDotNet: it does not accept "export
    /// to exactly this file name". The file-name part of `ResultFile` therefore serves **reporting and directory location** only, not the final export name.
    /// </remarks>
    /// <returns>命令行参数 / Command-line arguments.</returns>
    public IReadOnlyList<string> ToCommandLine() {
        string artifacts = Path.GetDirectoryName(EffectiveResultFile) is { Length: > 0 } directory
            ? directory
            : "BenchmarkDotNet.Artifacts";
        return new[] {
            "--filter", IncludeRegex,
            "--launchCount", Forks.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--warmupCount",
            WarmupIterations.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--iterationCount",
            MeasurementIterations.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--exporters", ResultFormat.ToString().ToLowerInvariant(),
            "--artifacts", artifacts,
        };
    }

    /// <summary>
    /// 解析一个正整数位置参数 / Parse one positive-integer positional argument.
    /// </summary>
    /// <param name="args">命令行参数 / Command-line arguments.</param>
    /// <param name="index">位置 / Position.</param>
    /// <param name="fallback">缺省值 / Default.</param>
    /// <param name="name">参数名 / Argument name.</param>
    /// <param name="value">数值 / The value.</param>
    /// <param name="error">非法说明 / The invalid-argument description.</param>
    /// <returns>是否成功 / Whether it succeeded.</returns>
    private static bool Positive(
        IReadOnlyList<string> args,
        int index,
        int fallback,
        string name,
        out int value,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(false)] out string? error) {
        error = null;
        if (args.Count <= index || args[index].Length == 0) {
            value = fallback;
            return true;
        }
        if (int.TryParse(
                args[index],
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out value) && value > 0) {
            return true;
        }
        error = $"{name} must be a positive integer, got '{args[index]}'";
        return false;
    }
}
