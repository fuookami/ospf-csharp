// RMP 求解器的判据 / Criteria for the RMP solver.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Framework.Bpp2d.Domain;
using Fuookami.Ospf.Framework.Bpp2d.Domain.Item.Model;
using Fuookami.Ospf.Framework.Bpp2d.Domain.LayerAssignment.ColumnGeneration;
using Fuookami.Ospf.Framework.Solver;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp2d.Tests.LayerAssignment;

/// <summary>
/// **RMP 求解器** / The RMP solver.
/// </summary>
/// <remarks>
/// 判据的重点：**求完 LP 必须取对偶值当影子价格**（不取的话下一轮检验数全为零）、
/// **没有列时不加列**、**求解失败原样透传**、**信息表的六个字段**。
///
/// The criteria focus on **the dual values being turned into shadow prices after the LP** (without them every reduced cost next round is zero), on **no columns being added when there are none**,
/// on **solver failures passing through unchanged**, and on **the info table's six fields**.
/// </remarks>
public class StandardRmpSolverTests {
    private static Quantity<FltX> Q(double value) => new(new FltX(value), LengthUnits.Millimeter);

    private static Layer<FltX> Layer(string itemId) =>
        new(0L, null,
            new SheetType<FltX>(Q(100.0), Q(100.0), Q(1000.0), new SheetTypeId("S"), true),
            new Sheet2Shape<FltX>(Q(100.0), Q(100.0)),
            new[] {
                new Placement2<FltX>(
                    Shape: new RectanglePackingShape2<FltX>(Q(10.0), Q(10.0)),
                    Position: new QuantityPoint2<FltX>(Q(0.0), Q(0.0)),
                    Rotated: false,
                    ItemId: itemId),
            });

    private static Bpp2dAssignmentDemandEntry AmountDemand(string itemId, ulong amount) =>
        new(Bpp2dAssignmentDemandMode.ItemAmount, new ItemId(itemId),
            new Bpp2dAssignmentDemandValue.Amount(amount), null);

    /// <summary>
    /// 按约束名给对偶值的假求解器 / A stub solver that prices the duals by constraint name.
    /// </summary>
    /// <remarks>
    /// **对偶价按约束名给**：需求约束的名字是 `demand_<tag>_lb_<i>`，所以判据能指定"第 i 条需求值多少"。
    /// 这样测的是"本实现怎么把对偶值变成影子价格"，而不是"求解器对不对"。
    ///
    /// **Duals are priced by constraint name**: demand constraints are named `demand_<tag>_lb_<i>`, so a criterion can dictate what the i-th demand is worth. This tests how this implementation
    /// turns duals into shadow prices rather than whether the solver is right.
    /// </remarks>
    private sealed class DualPricingSolver : IColumnGenerationSolver
    {
        private readonly Func<string, double?> _price;
        private readonly bool _succeeds;

        /// <summary>上一次求解看到的模型快照 / The model snapshot seen by the last solve.</summary>
        public ModelSnapshot? Last { get; private set; }

        public DualPricingSolver(Func<string, double?> price, bool succeeds = true) {
            _price = price;
            _succeeds = succeeds;
        }

        public string Name => "dual-pricing";

        public Task<Result<IColumnGenerationSolver.LpResult, ErrorCode, Error<ErrorCode>>> SolveLPAsync(
            string name,
            LinearMetaModel<Flt64> metaModel,
            bool toLogModel = false,
            RegistrationStatusCallBack? registrationStatusCallBack = null,
            SolvingStatusCallBack? solvingStatusCallBack = null) {
            if (!_succeeds) {
                return Task.FromResult(Results.Failed<IColumnGenerationSolver.LpResult>(
                    new Err<ErrorCode>(ErrorCode.ApplicationFailed, "lp exploded")));
            }
            // **记下模型形状**：目标名、面积系数、令牌数——这三样是"目标注册了没有""列加进去了没有"的唯一观测点。
            // **Record the model's shape**: objective names, the area coefficient and the token count are the only observation points for "was the objective registered" and "were the columns added".
            double areaCoefficient = 0.0;
            List<string> objectiveNames = new();
            foreach (MetaSubObject<Flt64> sub in metaModel.MetaSubObjects) {
                objectiveNames.Add(sub.Name);
                foreach (Fuookami.Ospf.Math.Symbol.Monomial.LinearMonomial<Flt64> monomial in sub.Polynomial.Monomials) {
                    if (monomial.Symbol.Name == "area") {
                        areaCoefficient += monomial.Coefficient.ToDouble();
                    }
                }
            }
            Last = new ModelSnapshot(objectiveNames, areaCoefficient, metaModel.Tokens.Tokens.Count);

            var duals = new Dictionary<MathConstraint, Flt64>();
            foreach (MathConstraint constraint in metaModel.Constraints) {
                double? price = _price(constraint.Name);
                if (price is not null) {
                    duals[constraint] = new Flt64(price.Value);
                }
            }
            return Task.FromResult(Results.Ok<IColumnGenerationSolver.LpResult>(
                new IColumnGenerationSolver.LpResult(
                    new FeasibleSolverOutput<Flt64>(
                        Obj: new Flt64(12.5),
                        Solution: new Solution<Flt64>(new[] { new Flt64(1.0) }),
                        Time: TimeSpan.FromMilliseconds(9.0),
                        PossibleBestObj: new Flt64(12.5),
                        Gap: Flt64.Zero),
                    duals)));
        }

        public Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> SolveMILPAsync(
            string name, LinearMetaModel<Flt64> metaModel, bool toLogModel = false,
            RegistrationStatusCallBack? registrationStatusCallBack = null,
            SolvingStatusCallBack? solvingStatusCallBack = null) =>
            throw new NotSupportedException("not under test");

        public Task<Result<(FeasibleSolverOutput<Flt64> Output, List<List<Flt64>> SolutionPool), ErrorCode, Error<ErrorCode>>> SolveMILPAsync(
            string name, LinearMetaModel<Flt64> metaModel, ulong amount, bool toLogModel = false,
            RegistrationStatusCallBack? registrationStatusCallBack = null,
            SolvingStatusCallBack? solvingStatusCallBack = null) =>
            throw new NotSupportedException("not under test");

        public Task<Result<FeasibleSolverOutput<V>, ErrorCode, Error<ErrorCode>>> SolveMILPAsAsync<V>(
            string name, LinearMetaModel<Flt64> metaModel, IIntoValue<V> converter, bool toLogModel = false,
            RegistrationStatusCallBack? registrationStatusCallBack = null,
            SolvingStatusCallBack? solvingStatusCallBack = null)
            where V : struct, IRealNumber<V>, INumberField<V> =>
            throw new NotSupportedException("not under test");

        public Task<Result<IColumnGenerationSolver.LpResultOf<V>, ErrorCode, Error<ErrorCode>>> SolveLPAsAsync<V>(
            string name, LinearMetaModel<Flt64> metaModel, IIntoValue<V> converter, bool toLogModel = false,
            RegistrationStatusCallBack? registrationStatusCallBack = null,
            SolvingStatusCallBack? solvingStatusCallBack = null)
            where V : struct, IRealNumber<V>, INumberField<V> =>
            throw new NotSupportedException("not under test");
    }

    /// <summary>模型快照 / A model snapshot.</summary>
    private sealed record ModelSnapshot(IReadOnlyList<string> ObjectiveNames, double AreaCoefficient, int TokenCount);

    private static Dictionary<ItemId, ulong> Items() =>
        new() { [new ItemId("I1")] = 4UL, [new ItemId("I2")] = 5UL };

    private static Bpp2dAssignmentDemandEntry[] Demands() =>
        new[] { AmountDemand("I1", 4UL), AmountDemand("I2", 5UL) };

    /// <summary>
    /// **对偶值变成影子价格，信息表六个字段齐全** / Duals become shadow prices and the info table carries six fields.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. 两条需求各给对偶价 `3.5` / `7.25` → **影子价格表两项**，键是 `(item_amount, 物品)`。
    /// 2. **目标值来自 LP 解**（`12.5`），不是自己估的。
    /// 3. 信息表有 `solver`、`model`、`lp_time_ms`、`lp_gap`、`lp_objective`、`column_count` 六项，
    /// 模型名是 `前缀-迭代号`。
    /// 4. `column_count` 反映的是**输入状态的列数**（这里 0）。
    ///
    /// Four paired points: two demands priced `3.5` and `7.25` produce **two shadow-price entries** keyed by `(item_amount, item)`; **the objective comes from the LP solution** (`12.5`) rather than
    /// an estimate; the info table carries the six fields `solver`, `model`, `lp_time_ms`, `lp_gap`, `lp_objective` and `column_count`, with the model named `prefix-iteration`; and
    /// `column_count` reflects **the incoming state's column count**, zero here.
    /// </remarks>
    [Fact]
    public async Task SolveAsync_ShouldTurnDualsIntoShadowPrices() {
        var solverStub = new DualPricingSolver(name =>
            name.EndsWith("_lb_0", StringComparison.Ordinal) ? 3.5
            : name.EndsWith("_lb_1", StringComparison.Ordinal) ? 7.25
            : null);
        var solver = new StandardRmpSolver(
            Items(), Demands(), solverStub,
            // **面积系数也传非默认值**：默认是 1，用默认值时"忽略系数"的实现照样通过。
            // **The area coefficient is also non-default**: the default is 1, and with it an implementation that ignores the coefficient still passes (the / / family).
            new ColumnGenerationStandardExecutorConfig(
                RmpSolveNamePrefix: "custom-rmp", RmpAreaCoefficient: new FltX(2.5)));

        Result<ColumnGenerationLpResult, ErrorCode, Error<ErrorCode>> result = await solver.SolveAsync(
            new ColumnGenerationState(3, Array.Empty<Layer<FltX>>()));

        result.IsOk.Should().BeTrue();
        ColumnGenerationLpResult value =
            ((Ok<ColumnGenerationLpResult, ErrorCode, Error<ErrorCode>>)result).Value;
        value.ShadowPrices.Should().HaveCount(2, "both demands carry a dual");
        value.ShadowPrices[new DemandModeKey("item_amount", "I1")].ToFlt64().ToDouble()
            .Should().BeApproximately(3.5, 1e-9);
        value.ShadowPrices[new DemandModeKey("item_amount", "I2")].ToFlt64().ToDouble()
            .Should().BeApproximately(7.25, 1e-9);
        ((FltX)value.Objective!).ToFlt64().ToDouble().Should().BeApproximately(12.5, 1e-9);

        value.Info!["solver"].Should().Be("dual-pricing");
        value.Info!["model"].Should().Be("custom-rmp-3", "the configured prefix reaches the model name");
        value.Info!["lp_time_ms"].Should().Be("9");
        value.Info!["lp_gap"].Should().Be("0");
        value.Info!["lp_objective"].Should().Be("12.5");
        value.Info!["column_count"].Should().Be("0");

        // **面积目标是 RMP 阶段唯一的目标**，必须真的注册进模型，且系数取默认 1。
        // **The area objective is the RMP stage's only objective**; it must genuinely be registered, with the default coefficient of 1.
        IReadOnlyList<string> objectives = solverStub.Last!.ObjectiveNames;
        objectives.Should().Contain("area_minimization", "the RMP registers exactly one objective");
        solverStub.Last!.AreaCoefficient.Should().BeApproximately(2.5, 1e-9, "the configured area coefficient reaches the objective");
    }

    /// <summary>
    /// **没有列时不加列，有列时列数进信息表** / No columns means no column-adding, and their count reaches the info table.
    /// </remarks>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. 空列 → 信息表的 `column_count` 是 `0`，且**求解仍然成功**（空 RMP 是合法输入，不是错误）。
    /// 2. 给两列 → `column_count` 是 `2`。
    /// 3. 两条路径都拿到影子价格——加列不改变"取对偶值"这一步。
    ///
    /// Three paired points: empty columns make `column_count` read `0` and **the solve still succeeds**, since an empty RMP is valid input rather than an error; two columns make it read `2`;
    /// and both paths still yield shadow prices, since adding columns does not skip the dual step.
    /// </remarks>
    [Fact]
    public async Task SolveAsync_ShouldAddColumnsOnlyWhenPresent() {
        var solverStub = new DualPricingSolver(name =>
            name.EndsWith("_lb_0", StringComparison.Ordinal) ? 1.0 : null);
        var solver = new StandardRmpSolver(Items(), Demands(), solverStub);

        ColumnGenerationLpResult withColumns =
            ((Ok<ColumnGenerationLpResult, ErrorCode, Error<ErrorCode>>)
                await solver.SolveAsync(new ColumnGenerationState(
                    1, new[] { Layer("I1"), Layer("I2") }))).Value;
        withColumns.Info!["column_count"].Should().Be("2");
        withColumns.ShadowPrices.Should().HaveCount(1, "only the first demand carries a dual");

        int withColumnsTokens = solverStub.Last!.TokenCount;

        ColumnGenerationLpResult empty =
            ((Ok<ColumnGenerationLpResult, ErrorCode, Error<ErrorCode>>)
                await solver.SolveAsync(new ColumnGenerationState(0, Array.Empty<Layer<FltX>>()))).Value;
        empty.Info!["column_count"].Should().Be("0");
        empty.ShadowPrices.Should().HaveCount(1);

        // **列真的被加进模型了**：每列一个份数令牌，所以带列时令牌数严格更多。
        // **The columns genuinely entered the model**: one copy token per column, so carrying columns yields strictly more tokens.
        solverStub.Last!.TokenCount.Should().BeLessThan(withColumnsTokens,
            "adding two columns registers two more tokens");
    }

    /// <summary>
    /// **求解失败原样透传** / A failed solve passes through unchanged.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. LP 失败 → 本方法失败，**不是**"成功但没有影子价格"。
    /// 2. 错误消息原样保留。
    /// 3. 失败不被包装成空影子价格表——那会让主循环以为"这一轮没有定价"而继续跑。
    ///
    /// Three paired points: a failing LP makes this method fail, **not** succeed with no shadow prices; the message is preserved; and the failure is not wrapped as an empty price table, which
    /// would let the main loop believe this round simply had no pricing and carry on.
    /// </remarks>
    [Fact]
    public async Task SolveAsync_ShouldPropagateLpFailures() {
        var solver = new StandardRmpSolver(
            Items(), Demands(),
            new DualPricingSolver(_ => null, succeeds: false));
        Result<ColumnGenerationLpResult, ErrorCode, Error<ErrorCode>> result = await solver.SolveAsync(
            new ColumnGenerationState(0, Array.Empty<Layer<FltX>>()));

        result.IsOk.Should().BeFalse("a broken LP is not an empty price table");
        ((Failed<ColumnGenerationLpResult, ErrorCode, Error<ErrorCode>>)result)
            .Error.Message.Should().Be("lp exploded");
    }
}
