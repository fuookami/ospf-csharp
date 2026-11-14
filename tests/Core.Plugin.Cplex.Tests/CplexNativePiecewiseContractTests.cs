#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Solver;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace Fuookami.Ospf.Core.Plugin.Cplex.Tests;

/// <summary>
/// CPLEX 原生 PWL 的校验与写入 / CPLEX's native PWL validation and writing.
/// </summary>
/// <remarks>
/// B1233 按基线 `CplexNativePiecewise.kt` 补写侧，与 B1232 的 COPT 同形。
///
/// **这一批判据的重点不在"写了 SOS2"（那一层 B1232 已经钉过、本批结构相同），**
/// **而在<b>两条 CPLEX 特有的校验</b>：非有限值、断点严格递增。**
///
/// **它们存在的理由值得记：** **"两个后端的写入器结构相同"很容易被读成"校验也应该相同"。
/// 而每一条校验都在回答那个后端<b>特有的</b>失败方式——
/// CPLEX 会直接接受一个 NaN 断点，写出一行 NaN，而 COPT 侧不会发生这种失败（它有哨兵校验）。**
///
/// **所以这两条判据同时在验两件事：CPLEX 的防御到位，以及"合并两个写入器"这个念头是错的。**
///
/// B1233 adds the write side, shaped like B1232's COPT.
///
/// **The criteria here focus not on "the SOS2 is written" (B1232 pinned that, and the shape matches) but on the <b>two
/// CPLEX-specific checks</b>: non-finite values and strictly increasing breakpoints.**
///
/// **Why they matter: "the two writers have the same shape" reads easily as "the checks should match too", yet each check
/// answers a failure mode <b>peculiar</b> to that backend — CPLEX will accept a NaN breakpoint and write a NaN row, a
/// failure COPT cannot have.**
///
/// **So these criteria verify two things at once: CPLEX's defences are in place, and the idea of merging the two writers
/// is wrong.**
/// </remarks>
public class CplexNativePiecewiseContractTests
{
    /// <summary>建一个变量键 / Build a variable key.</summary>
    private static VariableItemKey Key(ulong identifier, int index = 0) =>
        new(new Fuookami.Ospf.Math.Algebra.Number.UInt64(identifier), index);

    /// <summary>一份合法的 PWL / A valid PWL.</summary>
    private static NativePiecewiseData Valid(VariableItemKey input, VariableItemKey result, string name = "pwl") =>
        new(input, result, [0.0, 1.0, 2.0], [0.0, 2.0, 0.0], name);

    /// <summary>跑一次校验 / Run one validation.</summary>
    private static Result<Success, ErrorCode, Error<ErrorCode>> Run(
        VariableItemKey input, VariableItemKey result, NativePiecewiseData datum) =>
        CplexNativePiecewise.Validate([input, result], [datum]);

    /// <summary>
    /// **非有限的断点必须被拒** / A non-finite breakpoint must be rejected.
    /// </summary>
    /// <remarks>
    /// **这是 CPLEX 独有的一条（COPT 侧没有）。**
    ///
    /// **为什么 CPLEX 特别需要它：** **`AddSOS2` 会直接接受一个 NaN 断点，
    /// 把它写进模型，然后在求解时给出一个无法解释的结果——或者干脆返回 NaN 的目标值。**
    ///
    /// **"求解器接受了它"不等于"它是合法的"**，而这条校验就是这两者之间的那一层。
    ///
    /// **This check is CPLEX's alone (COPT has none of it).**
    ///
    /// **Why CPLEX needs it especially: `AddSOS2` accepts a NaN breakpoint, writes it into the model, and then returns an
    /// unexplainable result — or a NaN objective.**
    ///
    /// **"The solver accepted it" is not "it is legal", and this check is the layer between the two.**
    /// </remarks>
    [Theory]
    [InlineData(double.NaN, 1.0)]
    [InlineData(1.0, double.NaN)]
    [InlineData(double.PositiveInfinity, 2.0)]
    [InlineData(1.0, double.NegativeInfinity)]
    public void ANonFiniteBreakpointOrValue_ShouldBeRejected(double middle, double rewritten) {
        VariableItemKey input = Key(1);
        VariableItemKey result = Key(2);
        // **断点数与函数值数量仍然一致，所以拒它的只能是"非有限"这一条。**
        // **The counts still match, so only the finiteness check can reject it.**
        var datum = new NativePiecewiseData(
            input, result, [0.0, middle, 2.0], [0.0, rewritten, 0.0], "nonfinite");

        Run(input, result, datum).Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>
    /// **断点必须严格递增（相等也拒）** / Breakpoints must be strictly increasing, so equality is rejected too.
    /// </summary>
    /// <remarks>
    /// **"严格"是关键：两个<b>相等</b>的相邻断点会让 SOS2 的"相邻"指代一个有零长度的区间，
    /// 而那一段上的插值没有定义——不是"退化成常数"，而是"落在一个不存在的区间里"。**
    ///
    /// **这也是 SOS2 的真正前提：** **SOS2 说"至多两个相邻 λ 非零"，而"相邻"由断点数组的<b>下标</b>定义；
    /// 一组乱序断点会让"相邻"指向错误的两个点，于是插值在错误的区间上进行——模型可解，折线是错的。**
    ///
    /// **"Strictly" is the point: two <b>equal</b> neighbouring breakpoints make SOS2's "neighbouring" name a zero-length
    /// interval, where interpolation is undefined — not "degenerates to a constant" but "lands in an interval that does
    /// not exist".**
    ///
    /// **It is also SOS2's real precondition: "neighbouring" is defined by the <b>indices</b> of the breakpoint array, so
    /// unordered breakpoints make it name the wrong pair and interpolation happens over the wrong interval — the model
    /// solves, the polyline is wrong.**
    /// </remarks>
    [Theory]
    [InlineData(1.0, 1.0)]      // 相等 / equal
    [InlineData(2.0, 1.0)]      // 递减 / decreasing
    public void NonIncreasingBreakpoints_ShouldBeRejected(double second, double third) {
        VariableItemKey input = Key(1);
        VariableItemKey result = Key(2);
        var datum = new NativePiecewiseData(
            input, result, [0.0, second, third], [0.0, 1.0, 0.0], "unordered");

        Run(input, result, datum).Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>
    /// **断点少于两个、或与函数值数量不等时必须被拒** / Fewer than two breakpoints, or a count mismatch, must fail.
    /// </summary>
    [Fact]
    public void ABadPointCount_ShouldBeRejected() {
        VariableItemKey input = Key(1);
        VariableItemKey result = Key(2);

        Run(input, result, new NativePiecewiseData(input, result, [0.0], [0.0], "single"))
            .Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
        Run(input, result, new NativePiecewiseData(input, result, [0.0, 1.0, 2.0], [0.0, 1.0], "mismatch"))
            .Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>
    /// **未映射的变量键、以及重复的结果键必须被拒** /
    /// Unmapped keys and duplicated result keys must be rejected.
    /// </summary>
    [Fact]
    public void AMissingMappingOrADuplicateResult_ShouldBeRejected() {
        VariableItemKey inputA = Key(1);
        VariableItemKey inputB = Key(2);
        VariableItemKey result = Key(3);

        // **未映射：只把 inputA 报上去，result 不在集合里。**
        // **Unmapped: only inputA is declared, result is not in the set.**
        CplexNativePiecewise.Validate([inputA], [Valid(inputA, result)])
            .Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();

        // **重复结果键：两条 PWL 抢同一个 y。**
        // **Duplicate result: two PWLs claim the same y.**
        CplexNativePiecewise.Validate(
                [inputA, inputB, result],
                [Valid(inputA, result, "a"), Valid(inputB, result, "b")])
            .Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>
    /// **一份合法的数据通过** / A valid datum passes.
    /// </summary>
    [Fact]
    public void AValidDatum_ShouldPass() {
        VariableItemKey input = Key(1);
        VariableItemKey result = Key(2);

        CplexNativePiecewise.Validate([input, result], [Valid(input, result)])
            .Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>
    /// **写入先校验，且写的是 `AddSOS2`** / Writing validates first and writes an `AddSOS2`.
    /// </summary>
    [Fact]
    public void TheWritePath_ShouldValidateFirstAndWriteAnSos2() {
        string path = Path.Combine(
            RepositoryRoot(),
            "src/Core.Plugin.Cplex/CplexNativePiecewise.cs");
        string text = File.ReadAllText(path);

        text.Should().Contain("internal static Try Validate(", "the up-front validation entry must exist");
        text.Should().Contain("internal static Try Add(", "the write entry must exist");
        text.Should().Contain("AddSOS2(", "the SOS2 call must exist");

        int validateCall = text.IndexOf("Validate(variables.Keys", StringComparison.Ordinal);
        int firstNumVar = text.IndexOf("model.NumVarArray(", StringComparison.Ordinal);
        validateCall.Should().BeGreaterThan(0, "Add must call Validate");
        firstNumVar.Should().BeGreaterThan(0, "Add must create variables");
        validateCall.Should().BeLessThan(firstNumVar, "validation must come before the model is touched");
    }

    /// <summary>
    /// **CPLEX 的校验比 COPT 多两条，且这不是疏忽** /
    /// CPLEX carries two more checks than COPT, and that is not an oversight.
    /// </summary>
    /// <remarks>
    /// **这一条判据钉的是<b>差别的存在</b>，而不是差别的实现——因为差别的实现上面已经钉过了。**
    ///
    /// **它防的是一种很自然的"整理"：** **有人看到两个写入器形状相同，把其中一个的校验拿去复用，
    /// 于是 CPLEX 失去非有限值检查、或者 COPT 多出一个它没有的哨兵参数。**
    ///
    /// **这条判据会让那种整理立刻变红，并指出原因：形状一样，防御不一样。**
    ///
    /// **This pins the <b>existence of the difference</b>, not its implementation, which the criteria above already
    /// pin.**
    ///
    /// **It guards against a very natural "cleanup": someone notices the two writers share a shape, reuses one's
    /// validation for the other, and CPLEX loses its finiteness check — or COPT grows a sentinel parameter it has no use
    /// for.**
    ///
    /// **The criterion turns that cleanup red immediately and names the reason: same shape, different defences.**
    /// </remarks>
    [Fact]
    public void TheCplexChecks_ShouldBeAPostropheOfCopt() {
        string root = RepositoryRoot();
        string cplex = File.ReadAllText(Path.Combine(root, "src/Core.Plugin.Cplex/CplexNativePiecewise.cs"));
        string copt = File.ReadAllText(Path.Combine(root, "src/Core.Plugin.Copt/CoptNativePiecewise.cs"));

        // **CPLEX 有、COPT 没有的两条。**
        // **Two checks CPLEX has and COPT does not.**
        cplex.Should().Contain("IsFinite", "CPLEX must reject non-finite breakpoints");
        cplex.Should().Contain("断点必须严格递增", "CPLEX must reject non-increasing breakpoints");

        // **COPT 有、CPLEX 没有的一条：无穷哨兵。**
        // **One check COPT has and CPLEX does not: the infinity sentinel.**
        copt.Should().Contain("infinity", "COPT derives its λ bounds from the sentinel");
        cplex.Should().NotContain("infinity", "CPLEX has no sentinel, and inventing one would be a fabricated parameter");
    }

    /// <summary>
    /// **仓库根目录从测试运行目录往上找** / Walk up to the repository root.
    /// </summary>
    private static string RepositoryRoot() {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null) {
            if (File.Exists(Path.Combine(current.FullName, "ospf-csharp.sln"))) {
                return current.FullName;
            }
            current = current.Parent;
        }
        throw new InvalidOperationException("ospf-csharp.sln not found");
    }
}
