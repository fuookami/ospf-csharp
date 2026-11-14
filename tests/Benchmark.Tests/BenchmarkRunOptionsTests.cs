// 基准运行选项测试 / Tests for the benchmark run options.

using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Benchmark;
using Xunit;

namespace Fuookami.Ospf.Benchmark.Tests;

/// <summary>
/// 基准运行选项 / The benchmark run options.
/// </summary>
public sealed class BenchmarkRunOptionsTests {
    /// <summary>**无参数时退回全部基准 + small + 单轮** / With no arguments it falls back to all benchmarks, `small`, and one round.</summary>
    [Fact]
    public void TryParse_ShouldFallBackToTheDefaults() {
        bool parsed = BenchmarkRunOptions.TryParse(
            new List<string>(), out BenchmarkRunOptions options, out string? error);

        parsed.Should().BeTrue(error);
        options.IncludeRegex.Should().Be(".*");
        options.Dataset.Should().Be("small");
        options.Forks.Should().Be(1);
        options.WarmupIterations.Should().Be(1);
        options.MeasurementIterations.Should().Be(1);
        options.ResultFormat.Should().Be(BenchmarkResultFormat.Json);
    }

    /// <summary>**给了非法轮数直接失败，而不是退回默认值** / An invalid iteration count fails instead of falling back.</summary>
    /// <remarks>
    /// 静默退回会把"十轮测量"跑成一轮，而**报告里看不出来**——所以非法值必须让命令失败。
    ///
    /// A silent fallback turns a ten-iteration measurement into one round and **nothing in the report shows it** — so an invalid value must fail the
    /// command.
    /// </remarks>
    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("abc")]
    [InlineData("1.5")]
    public void TryParse_ShouldRejectInvalidIterationCounts(string raw) {
        bool parsed = BenchmarkRunOptions.TryParse(
            new List<string> { ".*", "small", "1", raw, "1" },
            out _,
            out string? error);

        parsed.Should().BeFalse();
        error.Should().Contain(raw,
            "错误必须指名那个非法取值 / the error must name the offending value");
    }

    /// <summary>**每个已知格式都映射到各自的值，大小写与空白不敏感** / Each known format maps to its own value, ignoring case and whitespace.</summary>
    /// <remarks>
    /// **每个取值都要单独断言**：写成"结果属于已知格式集合"时，`csv` 被解析成 `json` 也照样通过。
    ///
    /// **Every value needs its own assertion**: phrased as "the result is one of the known formats", parsing `csv` as `json` would still pass.
    /// </remarks>
    [Theory]
    [InlineData("json", BenchmarkResultFormat.Json)]
    [InlineData("JSON", BenchmarkResultFormat.Json)]
    [InlineData(" csv ", BenchmarkResultFormat.Csv)]
    [InlineData("Csv", BenchmarkResultFormat.Csv)]
    [InlineData("markdown", BenchmarkResultFormat.Markdown)]
    [InlineData("md", BenchmarkResultFormat.Markdown)]
    [InlineData("html", BenchmarkResultFormat.Html)]
    public void ParseResultFormat_ShouldAcceptKnownFormats(
        string raw,
        BenchmarkResultFormat expected) =>
        BenchmarkRunOptions.ParseResultFormat(raw).Should().Be(expected);

    /// <summary>**未知格式回退到 JSON，且回退值确实是 JSON** / An unknown format falls back to JSON, and the fallback really is JSON.</summary>
    /// <remarks>
    /// 上一条断言写成"是已知格式之一"时对 `json` 恒真，因此**必须单独钉住未知值的回退方向**。
    ///
    /// The assertion above is trivially true for `json` when phrased as "one of the known formats", so **the fallback direction for unknown values needs
    /// its own assertion**.
    /// </remarks>
    [Theory]
    [InlineData("yaml")]
    [InlineData("")]
    [InlineData(null)]
    public void ParseResultFormat_ShouldFallBackToJson(string? raw) =>
        BenchmarkRunOptions.ParseResultFormat(raw).Should().Be(BenchmarkResultFormat.Json);

    /// <summary>**过滤正则被清成文件名安全串** / The filter regex is sanitized into a file-name-safe string.</summary>
    [Theory]
    [InlineData(".*", ".")]
    [InlineData("*NetworkIndexBuild*", "NetworkIndexBuild")]
    [InlineData("Fuookami.Ospf.Benchmark.NetworkIndexBuildBenchmark", "Fuookami.Ospf.Benchmark.NetworkIndexBuildBenchmark")]
    public void SanitizeForFileName_ShouldProduceStableNames(string raw, string expected) =>
        BenchmarkRunOptions.SanitizeForFileName(raw).Should().Be(expected);

    /// <summary>**清空后为空串时退回 `all`，而 `.*` 得到 `"."`** / An empty result falls back to `all`, while `.*` yields `"."`.</summary>
    /// <remarks>
    /// **`.*` 得到的不是 `all`**：`.` 属于保留字符、`*` 被替换成 `_`，修剪下划线后剩 `.`。这与基线
    /// `raw.replace(Regex("[^A-Za-z0-9._-]"), "_").trim('_')` 的结果一致——**测试最初写成 `all` 是抄错了期望，不是实现错**。
    ///
    /// **`.*` does not yield `all`**: `.` is a retained character and `*` becomes `_`, leaving `.` after trimming underscores. This matches the baseline's
    /// `raw.replace(Regex("[^A-Za-z0-9._-]"), "_").trim('_')` — **the test originally expected `all`, which was a copied expectation, not an
    /// implementation bug**.
    /// </remarks>
    [Fact]
    public void SanitizeForFileName_ShouldFallBackToAll() =>
        BenchmarkRunOptions.SanitizeForFileName("***").Should().Be("all");

    /// <summary>**命令行参数带上每一项设置** / The command line carries every setting.</summary>
    /// <remarks>
    /// **每一项都要单独钉住**：只断言"包含 `--filter`"时，轮数与格式被丢掉也照样通过。
    ///
    /// **Every setting needs its own assertion**: checking only "contains `--filter`" passes even when the iteration counts and the format are dropped.
    /// </remarks>
    [Fact]
    public void ToCommandLine_ShouldCarryEverySetting() {
        var options = new BenchmarkRunOptions(
            IncludeRegex: "*NetworkIndexBuild*",
            Dataset: "medium",
            Forks: 2,
            WarmupIterations: 3,
            MeasurementIterations: 5,
            ResultFormat: BenchmarkResultFormat.Csv);

        IReadOnlyList<string> line = options.ToCommandLine();

        string joined = string.Join(' ', line);
        joined.Should().Contain("--filter *NetworkIndexBuild*");
        joined.Should().Contain("--launchCount 2");
        joined.Should().Contain("--warmupCount 3");
        joined.Should().Contain("--iterationCount 5");
        joined.Should().Contain("--exporters csv");
        joined.Should().Contain("--artifacts");
        line.Should().HaveCount(12);
    }

    /// <summary>
    /// **`IncludeRegex` 的语义是 glob，不是正则**——名字与文档都在误导 / `IncludeRegex` means a glob, not a regex — the name and docs both mislead.
    /// </summary>
    /// <remarks>
    /// **B406 实跑基准时踩到的。** 传 `SymbolCombine`（当正则理解是合法的子串匹配）
    /// 结果是 **0 个基准执行**，BenchmarkDotNet 只打印可用列表。
    /// 换成 `*SymbolCombine*` 才跑起来——18 个基准、179 秒。
    ///
    /// 原因：`ToCommandLine` 把 `IncludeRegex` **原样**塞进 BenchmarkDotNet 的 `--filter`，
    /// 而那个参数是 **glob 模式**（`*` 通配），**不认 `|`、`+`、`\d` 这类正则语法**。
    ///
    /// | 传入 | 结果 |
    /// | --- | --- |
    /// | `SymbolCombine` | **0 个基准**（无通配符 ⇒ 要求全名精确相等） |
    /// | `*SymbolCombine*` | 6 个方法 × 3 数据集 = **18 个基准** |
    /// | `Symbol|Other`（正则交替） | **0 个基准**（glob 不认 `|`） |
    ///
    /// 判据钉住**参数被原样透传**：这样"名字叫正则、实际按 glob 用"这个坑不会被悄悄抹平。
    /// 若要修，正确做法是把属性**改名**为 `IncludeGlob` 并同步文档——那会让这条判据变红，正是提醒。
    ///
    /// **Hit this while actually running benchmarks in B406.** Passing `SymbolCombine` (a valid substring match if read as a regex) executed **zero benchmarks**;
    /// BenchmarkDotNet merely printed the available list. `*SymbolCombine*` ran **18 benchmarks in 179 s**. The cause: `ToCommandLine` drops `IncludeRegex` **verbatim**
    /// into BenchmarkDotNet's `--filter`, which is a **glob** pattern (`*`) and knows nothing of `|`, `+` or `\d` (table above). The criterion pins the **verbatim
    /// pass-through** so the trap cannot be papered over. Fixing it properly means **renaming** the property to `IncludeGlob` plus a docs update — which turns this
    /// criterion red, exactly the intended reminder.
    /// </remarks>
    [Fact]
    public void IncludePattern_ShouldBePassedThroughAsAGlobNotTranslated() {
        var options = new BenchmarkRunOptions(
            IncludeRegex: "Symbol|Core",
            Dataset: "small",
            Forks: 1,
            WarmupIterations: 1,
            MeasurementIterations: 1,
            ResultFormat: BenchmarkResultFormat.Json);

        IReadOnlyList<string> line = options.ToCommandLine();

        line[1].Should().Be(
            "Symbol|Core",
            "the pattern is not translated; a real regex alternation reaches BenchmarkDotNet unchanged and therefore matches nothing, which is the trap this pins");
    }

    /// <summary>**默认结果文件名带数据集标签与格式后缀** / The default result file name carries the dataset label and format suffix.</summary>
    [Fact]
    public void DefaultResultFile_ShouldEmbedDatasetAndFormat() {
        string file = BenchmarkRunOptions.DefaultResultFile(
            "*Pile*", "large", BenchmarkResultFormat.Markdown);

        file.Should().Contain("Pile");
        file.Should().Contain("-large.");
        file.Should().EndWith(".markdown");
    }

    /// <summary>**显式结果文件覆盖默认值** / An explicit result file overrides the default.</summary>
    [Fact]
    public void EffectiveResultFile_ShouldHonourTheExplicitPath() {
        var options = new BenchmarkRunOptions(ResultFile: "out/custom.json");

        options.EffectiveResultFile.Should().Be("out/custom.json");
        options.ToCommandLine().Should().Contain("out");
    }
}
