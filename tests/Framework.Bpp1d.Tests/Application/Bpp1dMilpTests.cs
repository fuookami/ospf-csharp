// 一维 MILP 入口判据 / Criteria for the ordinary BPP1D MILP entry.

using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Fuookami.Ospf.Framework.Bpp.Infrastructure;
using Fuookami.Ospf.Framework.Bpp1d.Application;
using Fuookami.Ospf.Framework.Bpp1d.Domain.Item.Model;
using Fuookami.Ospf.Framework.Bpp1d.Domain.PatternGeneration;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp1d.Tests.Application;

/// <summary>
/// **一维普通 MILP 入口** / The ordinary BPP1D MILP entry.
/// </summary>
/// <remarks>
/// 对应基线 `Bpp1dSolver.kt` 的 `Bpp1dMilp`。本批只做“生成候选 → 最终整数模型”这一条路径，
/// 带 LP 定价循环的 `Bpp1dColumnGeneration` 留到下一批。
///
/// **判据分两层，而第一层不需要任何求解器。** 本批真正新增的能力是那条**错误翻译**判断
/// （“受限候选池无解不等于原问题不可行”），它与求解器实现无关，所以直接打在 `TranslateFinalFailure` 上。
///
/// **曾经写过一整套桩求解器去端到端测它**——为了测一个纯函数而实现五个 `throw` 的接口成员，
/// 那是用 40 行 fixture 换 5 行判据。**把判断抽成函数，判据就只剩一行。**
///
/// Mirrors `Bpp1dMilp`. The criteria come in two layers, and the first needs no solver at all: the new capability is the **error translation**, which is solver
/// independent and therefore hit directly on `TranslateFinalFailure`. A whole stub solver was written first to test it end to end — five `throw` members to test one
/// pure function, which is 40 fixture lines bought for a 5-line criterion.
/// </remarks>
public class Bpp1dMilpTests {
    /// <summary>**受限候选池无解必须改报求解异常** / An infeasible restricted pool must surface as a solving exception.</summary>
    /// <remarks>
    /// **本批最要紧的一条。** 候选**不完整**时求解器报 `ORModelInfeasible`，只说明**这一池**装不下；
    /// 直接透出会让调用方**放弃一个其实有解的问题**。
    ///
    /// **The most important criterion in this batch.** An `ORModelInfeasible` from an **incomplete** pool only says *this pool* cannot pack the problem; passing it
    /// through would make callers **abandon a problem that is actually solvable**.
    /// </remarks>
    [Fact]
    public void TranslateFinalFailure_ShouldTranslateAPoolInfeasibilityIntoSolvingException() {
        Bpp1dMilp<Flt64>.TranslateFinalFailure(ErrorCode.ORModelInfeasible, complete: false)
            .Should().Be(ErrorCode.OREngineSolvingException);
    }

    /// <summary>**候选完整时“不可行”原样透出** / With complete candidates, "infeasible" passes through.</summary>
    /// <remarks>
    /// **反向对照，也是这条判断的另一半。** 候选**完整**时的不可行是**真的不可行**——那是求解器的证书，
    /// 不该被翻译掉。少了这一条，实现只要“见不可行就翻”也能通过上一条。
    ///
    /// **The reverse control, and the other half of the judgement.** With **complete** candidates, infeasibility is **real infeasibility** — a solver certificate that
    /// must not be translated away. Without this case, an implementation that translates *any* infeasibility would still pass the previous one.
    /// </remarks>
    [Fact]
    public void TranslateFinalFailure_ShouldPreserveInfeasibilityWhenCandidatesAreComplete() {
        Bpp1dMilp<Flt64>.TranslateFinalFailure(ErrorCode.ORModelInfeasible, complete: true)
            .Should().Be(ErrorCode.ORModelInfeasible);
    }

    /// <summary>**其它错误码一律原样透出** / Every other code passes through untouched.</summary>
    /// <remarks>
    /// 翻译是**有条件的**：只有“候选不完整 + 不可行”这一种组合会被改写。
    ///
    /// The translation is **conditional**: only the combination "incomplete candidates plus infeasible" is rewritten.
    /// </remarks>
    /// <param name="code">错误码 / The error code.</param>
    [Theory]
    [InlineData(ErrorCode.OREngineSolvingException)]
    [InlineData(ErrorCode.ApplicationError)]
    public void TranslateFinalFailure_ShouldPassThroughUnrelatedCodes(ErrorCode code) {
        Bpp1dMilp<Flt64>.TranslateFinalFailure(code, complete: false).Should().Be(code);
        Bpp1dMilp<Flt64>.TranslateFinalFailure(code, complete: true).Should().Be(code);
    }

    /// <summary>**配置缺省值稳定** / The configuration defaults are stable.</summary>
    /// <remarks>
    /// 缺省的迭代上限与每轮列数直接决定定价循环的开销；它们静默变成 `0` 会让列生成**一轮都不跑**，
    /// 而调用方看到的只是“结果差一点”。
    ///
    /// The default iteration limit and per-iteration column count drive the pricing loop's cost; a silent drop to `0` would make column generation **run no
    /// iterations at all**, while the caller only sees "a slightly worse answer".
    /// </remarks>
    [Fact]
    public void SolveConfig_ShouldExposeStableDefaults() {
        var config = new Bpp1dSolveConfig<Flt64>();

        config.IterationLimit.Should().Be(32);
        config.ColumnsPerIteration.Should().Be(32);
        config.ExtensionsOrDefault.Should().BeEmpty();
        config.GenerationOrDefault.MaxPatterns.Should().BeGreaterThan(0);
    }

    /// <summary>**配置可被显式覆盖** / The configuration can be overridden explicitly.</summary>
    [Fact]
    public void SolveConfig_ShouldHonourExplicitOverrides() {
        var config = new Bpp1dSolveConfig<Flt64>(
            Generation: new PatternGenerationConfig(MaxPatterns: 7, MaxNodes: 11),
            IterationLimit: 3,
            ColumnsPerIteration: 2);

        config.GenerationOrDefault.MaxPatterns.Should().Be(7);
        config.GenerationOrDefault.MaxNodes.Should().Be(11);
        config.IterationLimit.Should().Be(3);
        config.ColumnsPerIteration.Should().Be(2);
    }

#if SCIP_INTEGRATION
    /// <summary>**真实 SCIP 求解一个可装下的小问题** / A small packable problem solves with real SCIP.</summary>
    /// <remarks>
    /// 需求 4 件、每件 100 mm、箱长 500 mm ⇒ 每箱 5 件 ⇒ **一箱足够**；目标按箱数 ⇒ 目标值 **1**。
    /// **顺带量一件事**：C# 的 `FeasibleSolverOutput` **没有状态字段**，实现只能从 `Gap` 推 `Optimal`。
    /// 这条判据在真实求解器上跑一次，用来确认那个推断站得住。
    ///
    /// 4 pieces at 100 mm in a 500 mm bin (5 per bin) ⇒ **one bin suffices**, and the bin-count objective is **1**. The C# `FeasibleSolverOutput` has no status
    /// field and the implementation infers `Optimal` from `Gap`; running on a real solver confirms that inference.
    /// </remarks>
    [Fact]
    public async Task SolveAsync_ShouldSolveASmallProblemWithRealScip() {
        PackingProblem<Flt64> problem = MakeProblem();
        Bpp1dMilp<Flt64> entry = new(
            new Fuookami.Ospf.Core.Plugin.Scip.ScipColumnGenerationSolver(),
            Flt64PackingValueAdapter.Instance);

        Result<Bpp1dSolution<Flt64>, ErrorCode, Error<ErrorCode>> result = await entry.SolveAsync(problem);

        result.IsOk.Should().BeTrue(result.ToString());
        Bpp1dSolution<Flt64> solution = ((Ok<Bpp1dSolution<Flt64>, ErrorCode, Error<ErrorCode>>)result).Value;
        solution.Objective.ToDouble().Should().BeApproximately(
            1.0, 1e-9, "four pieces at 100 mm fit one 500 mm bin and the objective counts bins");
        solution.Packing.Plans.Should().NotBeEmpty("the physical packing must be reconstructed");
        solution.ColumnCount.Should().BeGreaterThan(0);
    }

    /// <summary>**真实候选池不足时不能被当成“原问题不可行”** / A real insufficient pool must not read as "problem infeasible".</summary>
    /// <remarks>
    /// 预算压到 `MaxPatterns: 1`、`MaxNodes: 1`：候选**远不完整**，需求行无法满足。
    /// **这是那条翻译判断的真实端到端版本**——桩求解器只能给出一个“意见”，这里是求解器真报不可行。
    ///
    /// Budget squeezed to `MaxPatterns: 1, MaxNodes: 1`: candidates are **far from complete** and the demand row cannot be satisfied. **This is the real end-to-end
    /// version of the translation judgement** — a stub only offers an opinion, while here the solver genuinely reports infeasibility.
    /// </remarks>
    [Fact]
    public async Task SolveAsync_ShouldNotReportARealPoolInfeasibilityAsProblemInfeasibility() {
        PackingProblem<Flt64> problem = MakeProblem();
        Bpp1dMilp<Flt64> entry = new(
            new Fuookami.Ospf.Core.Plugin.Scip.ScipColumnGenerationSolver(),
            Flt64PackingValueAdapter.Instance);
        var config = new Bpp1dSolveConfig<Flt64>(
            Generation: new PatternGenerationConfig(MaxPatterns: 1, MaxNodes: 1));

        Result<Bpp1dSolution<Flt64>, ErrorCode, Error<ErrorCode>> result =
            await entry.SolveAsync(problem, config);

        result.IsFailed.Should().BeTrue("one candidate cannot cover the demand");
        ((Failed<Bpp1dSolution<Flt64>, ErrorCode, Error<ErrorCode>>)result).Error.Code
            .Should().NotBe(ErrorCode.ORModelInfeasible,
                "an incomplete pool never proves the original problem infeasible");
    }
#endif

    /// <summary>构造问题 / Build a problem.</summary>
    /// <returns>问题 / The problem.</returns>
    private static PackingProblem<Flt64> MakeProblem() =>
        new(
            Items: new List<Item<Flt64>> { new("I1", Millimetres(100.0), Kilograms(1.0), 4UL) },
            // **箱长 500 mm、每件 100 mm ⇒ 一箱装 5 件**，需求 4 件时一箱够。
            // **A 500 mm bin holds 5 pieces of 100 mm**, so one bin serves 4 pieces.
            BinTypes: new List<BinType<Flt64>> {
                new("B1", Millimetres(500.0), Kilograms(10.0), Flt64.One, null),
            },
            Objective: PackingObjective.BinCount,
            Spacing: Millimetres(0.0));

    /// <summary>构造毫米量 / Build a millimetre quantity.</summary>
    /// <param name="value">数值 / Value.</param>
    /// <returns>带单位的量 / The unit-carrying quantity.</returns>
    private static Quantity<Flt64> Millimetres(double value) =>
        new(new Flt64(value), LengthUnits.Millimeter);

    /// <summary>构造千克量 / Build a kilogram quantity.</summary>
    /// <param name="value">数值 / Value.</param>
    /// <returns>带单位的量 / The unit-carrying quantity.</returns>
    private static Quantity<Flt64> Kilograms(double value) =>
        new(new Flt64(value), SIBaseUnits.Kilogram);
}
