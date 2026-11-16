// 尾箱重排判据 / Criteria for the tail-bin optimiser.

using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Fuookami.Ospf.Framework.Bpp.Infrastructure;
using Fuookami.Ospf.Framework.Bpp1d.Application;
using Fuookami.Ospf.Framework.Bpp1d.Domain.Item.Model;
using Fuookami.Ospf.Framework.Bpp1d.Domain.Packing;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp1d.Tests.Application;

/// <summary>
/// **尾箱重排** / Tail-bin rearrangement.
/// </summary>
/// <remarks>
/// 对应。判据分两层：
///
/// - **打分口径**不需要求解器，直接打在 `Score` 上——**单位换算是本类里最容易错的一段**；
/// - **重排主流程**需要真实求解器（它要重求局部模型）。
///
/// Mirrors. The criteria come in two layers: the **scoring metrics** need no solver and hit `Score` directly (**unit conversion is the most
/// error-prone part**), while the **rearrangement flow** needs a real solver because it re-solves a local model.
/// </remarks>
public class TailBinOptimizerTests {
    /// <summary>**箱数目标的分数就是箱数** / The bin-count score is the number of bins.</summary>
    [Fact]
    public void Score_ShouldCountBinsForTheBinCountObjective() {
        PackingProblem<Flt64> problem = MakeProblem(PackingObjective.BinCount);
        PackingResult<Flt64> result = MakeResult(
            MillimetreType("B1"), MillimetreType("B1"), MillimetreType("B2"));

        Result<Flt64, ErrorCode, Error<ErrorCode>> score = TailBinOptimizer<Flt64>.Score(problem, result);

        score.IsOk.Should().BeTrue(score.ToString());
        ((Ok<Flt64, ErrorCode, Error<ErrorCode>>)score).Value.ToDouble().Should().BeApproximately(3.0, 1e-12);
    }

    /// <summary>**成本目标的分数是逐箱成本之和** / The cost score is the sum of the bin costs.</summary>
    [Fact]
    public void Score_ShouldSumBinCostsForTheCostObjective() {
        PackingProblem<Flt64> problem = MakeProblem(PackingObjective.Cost);
        var cheap = new BinType<Flt64>("B1", Millimetres(500.0), Kilograms(10.0), new Flt64(2.0), null);
        var pricey = new BinType<Flt64>("B2", Millimetres(500.0), Kilograms(10.0), new Flt64(7.0), null);
        PackingResult<Flt64> result = MakeResult(cheap, pricey);

        Result<Flt64, ErrorCode, Error<ErrorCode>> score = TailBinOptimizer<Flt64>.Score(problem, result);

        score.IsOk.Should().BeTrue(score.ToString());
        ((Ok<Flt64, ErrorCode, Error<ErrorCode>>)score).Value.ToDouble().Should().BeApproximately(9.0, 1e-12);
    }

    /// <summary>**卷长目标的分数必须换算到问题自己的单位** / The consumed-length score must be converted into the problem's own unit.</summary>
    /// <remarks>
    /// **这里最要紧的一条。** 问题里的基准箱是**米**（0.5 m），被判分的箱是**毫米**（500 mm）。
    /// 不换算会把 `500` 直接当成 `0.5`——**差 1000 倍，而比较结果会因此毫无意义，错得还很安静**。
    /// 判据断言分数是 `0.5`，不是 `500`。
    ///
    /// **The most important criterion in.** The problem's reference bin is in **metres** (0.5 m) while the scored bin is in **millimetres** (500 mm).
    /// Without conversion `500` is treated as `0.5` — **a factor of 1000 that makes the comparison meaningless, silently**. The criterion asserts 0.5, not 500.
    /// </remarks>
    [Fact]
    public void Score_ShouldConvertBinLengthsIntoTheProblemUnitForConsumedLength() {
        PackingProblem<Flt64> problem = MakeProblem(PackingObjective.ConsumedLength);
        PackingResult<Flt64> result = MakeResult(MillimetreType("B1"), MillimetreType("B1"));

        Result<Flt64, ErrorCode, Error<ErrorCode>> score = TailBinOptimizer<Flt64>.Score(problem, result);

        score.IsOk.Should().BeTrue(score.ToString());
        ((Ok<Flt64, ErrorCode, Error<ErrorCode>>)score).Value.ToDouble().Should().BeApproximately(
            1.0, 1e-9, "two 500 mm bins are 0.5 m each, so 1.0 m in the problem's metre unit");
    }

    /// <summary>**分数与箱数成正比，不随目标之外的字段漂移** / The score tracks bins only, independent of other fields.</summary>
    [Fact]
    public void Score_ShouldBeEmptyForAnEmptyResult() {
        PackingProblem<Flt64> problem = MakeProblem(PackingObjective.BinCount);

        Result<Flt64, ErrorCode, Error<ErrorCode>> score =
            TailBinOptimizer<Flt64>.Score(problem, MakeResult());

        score.IsOk.Should().BeTrue(score.ToString());
        ((Ok<Flt64, ErrorCode, Error<ErrorCode>>)score).Value.ToDouble().Should().BeApproximately(0.0, 1e-12);
    }

#if SCIP_INTEGRATION
    /// <summary>**窗口非正时在建模之前就被拒** / A non-positive window is rejected before any model is built.</summary>
    [Fact]
    public async Task OptimizeAsync_ShouldRejectANonPositiveWindow() {
        PackingProblem<Flt64> problem = MakeProblem(PackingObjective.BinCount);
        TailBinOptimizer<Flt64> optimiser = MakeOptimiser();

        Result<PackingResult<Flt64>, ErrorCode, Error<ErrorCode>> result = await optimiser.OptimizeAsync(
            problem, MakeResult(MetreType(), MetreType(), MetreType()), window: 0);

        result.IsFailed.Should().BeTrue("a non-positive window is invalid");
        ((Failed<PackingResult<Flt64>, ErrorCode, Error<ErrorCode>>)result).Error.Code
            .Should().Be(ErrorCode.IllegalArgument);
    }

    /// <summary>**同分时候选仍会走到 `accept`** / On a tie the candidate still reaches `accept`.</summary>
    /// <remarks>
    /// `if (score(candidate) > score(current)) return Ok(current)`——**严格大于**才保留原结果。
    /// 改成 `>=` 时，同分（正常情形）就不再调用 `accept`：**外部业务策略被静默跳过**。
    /// 判据用一个"总是拒绝"的 `accept`：严格大于下它必然被调用 ⇒ 结果失败。
    ///
 /// This implementation keeps the original only on **strictly greater**. With `&gt;=`, a tie — the normal case — never reaches `accept`, so the **external business policy is
    /// silently skipped**. The criterion passes an always-rejecting `accept`: under strict-greater it is called and the result must fail.
    /// </remarks>
    [Fact]
    public async Task OptimizeAsync_ShouldReachAcceptOnATie() {
        PackingProblem<Flt64> problem = MakeProblem(PackingObjective.BinCount);
        TailBinOptimizer<Flt64> optimiser = MakeOptimiser();
        PackingResult<Flt64> current =
            MakeResult(MetreType(), MetreType(), MetreType());

        Result<PackingResult<Flt64>, ErrorCode, Error<ErrorCode>> result = await optimiser.OptimizeAsync(
            problem,
            current,
            window: 1,
            accept: _ => Results.Failed<Success>(new Err<ErrorCode>(ErrorCode.ApplicationError, "rejected")));

        result.IsFailed.Should().BeTrue(
            "a tie must still consult the caller's policy, so an always-rejecting policy must be heard");
    }

    /// <summary>**带扩展时被拒** / Extensions are rejected, since they belong to a full-result policy.</summary>
    [Fact]
    public async Task OptimizeAsync_ShouldRejectExtensions() {
        PackingProblem<Flt64> problem = MakeProblem(PackingObjective.BinCount);
        TailBinOptimizer<Flt64> optimiser = MakeOptimiser();
        // **必须给一个非空的扩展。** 第一版传的是空表——`Count == 0` 自然不触发守卫，
        // 判据于是"根本没测到守卫"。**空集合不是"有扩展"。**
        //
        // **The list must be non-empty.** The first version passed an empty list, so `Count == 0` never triggered the guard and the criterion **never tested it**.
        var config = new Bpp1dSolveConfig<Flt64>(
            Extensions: new List<Fuookami.Ospf.Framework.Bpp1d.Domain.PatternAssignment.IPatternModelingExtension<Flt64>> {
                new NoopExtension(),
            });

        Result<PackingResult<Flt64>, ErrorCode, Error<ErrorCode>> result = await optimiser.OptimizeAsync(
            problem, MakeResult(MetreType(), MetreType(), MetreType()), window: 1, config: config);

        result.IsFailed.Should().BeTrue("a non-empty extension list must not reach a local model");
        ((Failed<PackingResult<Flt64>, ErrorCode, Error<ErrorCode>>)result).Error.Code
            .Should().Be(ErrorCode.IllegalArgument);
    }
#endif

    /// <summary>不做任何事的扩展，只用来让扩展表非空 / A do-nothing extension, present only to make the extension list non-empty.</summary>
    private sealed class NoopExtension : Fuookami.Ospf.Framework.Bpp1d.Domain.PatternAssignment.IPatternModelingExtension<Flt64> {
        /// <inheritdoc/>
        public Flt64 ReducedCost(
            PackingPattern<Flt64> pattern,
            Fuookami.Ospf.Framework.Bpp1d.Domain.PatternAssignment.PatternAssignmentContext<Flt64> context,
            Fuookami.Ospf.Core.Model.Mechanism.MetaDualSolution dual) {
            _ = pattern;
            _ = context;
            _ = dual;
            return Flt64.Zero;
        }
    }

    /// <summary>构造优化器 / Build the optimiser.</summary>
    /// <returns>优化器 / The optimiser.</returns>
    private static TailBinOptimizer<Flt64> MakeOptimiser() =>
        new(new Bpp1dMilp<Flt64>(
            new Fuookami.Ospf.Core.Plugin.Scip.ScipColumnGenerationSolver(),
            Flt64PackingValueAdapter.Instance));

    /// <summary>构造问题 / Build a problem.</summary>
    /// <param name="objective">目标 / The objective.</param>
    /// <returns>问题 / The problem.</returns>
    private static PackingProblem<Flt64> MakeProblem(PackingObjective objective) =>
        new(
            Items: new List<Item<Flt64>> { new("I1", Millimetres(100.0), Kilograms(1.0), 3UL) },
            // **基准箱的箱长用米**：判分时的换算目标就是它。
            // **The reference bin length is in metres**: it is the conversion target for scoring.
            BinTypes: new List<BinType<Flt64>> {
                new("B0", Metres(0.5), Kilograms(10.0), Flt64.One, null),
            },
            Objective: objective,
            Spacing: Millimetres(0.0));

    /// <summary>构造结果 / Build a result.</summary>
    /// <param name="types">逐箱箱型 / Bin types, one per plan.</param>
    /// <returns>结果 / The result.</returns>
    private static PackingResult<Flt64> MakeResult(params BinType<Flt64>[] types) {
        // **每个方案只放一件。** 需求 3 件的算例需要**三个方案**才合法，
        // 所以 gated 判据要传三个箱型——只传一个会让完整复核先报错，
        // 而那个错误恰好也是 `IllegalArgument`，**判据就变成"因为别的原因失败"**（这是反复踩到的形态）。
        //
        // **Each plan holds one piece.** A demand of 3 needs **three plans** to be legal, so the gated criteria pass three bin types; passing one makes the full
        // re-verification fail first with a code that is **also** `IllegalArgument`, turning the criterion into "fails for some other reason".
        var plans = new List<PackingPlan<Flt64>>();
        for (int index = 0; index < types.Length; ++index) {
            plans.Add(new PackingPlan<Flt64>(
                new Bin<Flt64>($"bin_{index}", types[index]),
                new List<Placement<Flt64>> {
                    new("I1", Millimetres(0.0), Millimetres(100.0)),
                }));
        }
        return new PackingResult<Flt64>(
            Plans: plans,
            Remnants: new List<Remnant<Flt64>>(),
            Materials: new Dictionary<string, ulong>(),
            Utilization: new Flt64(0.5));
    }

    /// <summary>构造毫米箱型 / Build a millimetre bin type.</summary>
    /// <param name="id">箱型标识 / Bin type id.</param>
    /// <returns>箱型 / The bin type.</returns>
    /// <summary>构造问题里**真的存在**的基准箱型 / Build the bin type that the problem actually declares.</summary>
    /// <returns>箱型 / The bin type.</returns>
    /// <remarks>
    /// **gated 判据必须用这个，不能用毫米箱型。** 第一版用 `MillimetreType("B1")`，而问题只声明了 `B0`——
    /// 完整复核先以 `IllegalArgument` 拒掉未知箱型，**判据于是"因为别的原因失败"**，
    /// 去掉守卫的变异照样存活。这是第七次撞上"判据失败的原因不是被测的守卫"。
    ///
    /// **The gated criteria must use this, not the millimetre type.** The first version used `MillimetreType("B1")` while the problem only declares `B0`, so the full
    /// re-verification rejected an unknown bin type with **`IllegalArgument`** first and the criterion **failed for some other reason**, letting the guard mutants survive.
    /// </remarks>
    private static BinType<Flt64> MetreType() =>
        new("B0", Metres(0.5), Kilograms(10.0), Flt64.One, null);

    /// <summary>构造毫米箱型 / Build a millimetre bin type.</summary>
    /// <param name="id">箱型标识 / Bin type id.</param>
    /// <returns>箱型 / The bin type.</returns>
    private static BinType<Flt64> MillimetreType(string id) =>
        new(id, Millimetres(500.0), Kilograms(10.0), Flt64.One, null);

    /// <summary>构造毫米量 / Build a millimetre quantity.</summary>
    /// <param name="value">数值 / Value.</param>
    /// <returns>带单位的量 / The unit-carrying quantity.</returns>
    private static Quantity<Flt64> Millimetres(double value) =>
        new(new Flt64(value), LengthUnits.Millimeter);

    /// <summary>构造米量 / Build a metre quantity.</summary>
    /// <param name="value">数值 / Value.</param>
    /// <returns>带单位的量 / The unit-carrying quantity.</returns>
    private static Quantity<Flt64> Metres(double value) =>
        new(new Flt64(value), SIBaseUnits.Meter);

    /// <summary>构造千克量 / Build a kilogram quantity.</summary>
    /// <param name="value">数值 / Value.</param>
    /// <returns>带单位的量 / The unit-carrying quantity.</returns>
    private static Quantity<Flt64> Kilograms(double value) =>
        new(new Flt64(value), SIBaseUnits.Kilogram);
}
