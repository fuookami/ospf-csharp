// 分区装载判据 / Criteria for partition loading.

using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading;
using Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading.Model;
using Fuookami.Ospf.Framework.Bpp.Domain.PartitionLoading;
using Fuookami.Ospf.Framework.Bpp.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;
using Try = Fuookami.Ospf.Utils.Functional.Result<
    Fuookami.Ospf.Utils.Functional.Success,
    Fuookami.Ospf.Utils.Error.ErrorCode,
    Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.Bpp1d.Tests.Domain.PartitionLoading;

/// <summary>
/// **分区装载的装配与加列** / Assembly and column addition for partition loading.
/// </summary>
/// <remarks>
/// 对应基线 `PartitionLoadingContext.kt` 的 `create` / `columnId` / `decisionId` / 边界与整数性复核。
///
/// **判据先打纯函数**：分区覆盖的五条规则、身份格式、整数类型识别、范围复核，都不需要求解器也不需要模型。
/// 装配本身（`CreateAsync`）另有一条，用真实的离散上下文与已注册模型。
///
/// Mirrors the baseline's `create`, `columnId`, `decisionId`, and the bounds/integrality re-checks. **The criteria hit the pure functions first** — the five coverage
/// rules, the identity formats, integer-type detection and bounds checking need neither solver nor model; assembly itself gets one case against a real discrete
/// context and a registered model.
/// </remarks>
public class PartitionLoadingContextTests {
    /// <summary>**分区必须唯一且完整地覆盖舱位** / Partitions must uniquely and exhaustively own the positions.</summary>
    /// <remarks>
    /// 五条规则各有一个算例。**最要紧的是"重复归属"**：一个舱位被两个分区同时列上，
    /// 两条归属单独看都合法，合并起来才错——而错的方式是**这个舱位在两条列里都可变**。
    ///
    /// One case per rule. **"Duplicate ownership" matters most**: a position listed by two partitions leaves each ownership looking legitimate on its own, and the
    /// error only appears when combined — the position ends up variable in both partitions' columns.
    /// </remarks>
    [Fact]
    public void PartitionCoverage_ShouldRequireUniqueAndExhaustiveOwnership() {
        var ids = new HashSet<string> { "P1", "P2" };

        // 合法：两个分区各占一个舱位。
        // Legal: two partitions owning one position each.
        PartitionLoadingContext<Flt64>.PartitionCoverageIsValid(
            new List<LoadingPartition> {
                new("A", new HashSet<string> { "P1" }),
                new("B", new HashSet<string> { "P2" }),
            },
            new List<(string, string)> { ("P1", "A"), ("P2", "B") },
            ids).Should().BeTrue();

        // 空分区表。
        // An empty partition list.
        PartitionLoadingContext<Flt64>.PartitionCoverageIsValid(
            new List<LoadingPartition>(), new List<(string, string)>(), ids).Should().BeFalse();

        // 分区标识为空。
        // A blank partition identity.
        PartitionLoadingContext<Flt64>.PartitionCoverageIsValid(
            new List<LoadingPartition> { new("", new HashSet<string> { "P1", "P2" }) },
            new List<(string, string)> { ("P1", ""), ("P2", "") },
            ids).Should().BeFalse();

        // 分区标识重复。
        // A duplicated partition identity.
        PartitionLoadingContext<Flt64>.PartitionCoverageIsValid(
            new List<LoadingPartition> {
                new("A", new HashSet<string> { "P1" }),
                new("A", new HashSet<string> { "P2" }),
            },
            new List<(string, string)> { ("P1", "A"), ("P2", "A") },
            ids).Should().BeFalse();

        // 覆盖不全。
        // Incomplete coverage.
        PartitionLoadingContext<Flt64>.PartitionCoverageIsValid(
            new List<LoadingPartition> { new("A", new HashSet<string> { "P1" }) },
            new List<(string, string)> { ("P1", "A") },
            ids).Should().BeFalse();

        // **同一舱位被两个分区都列了**。
        // **The same position is listed by two partitions.**
        PartitionLoadingContext<Flt64>.PartitionCoverageIsValid(
            new List<LoadingPartition> {
                new("A", new HashSet<string> { "P1", "P2" }),
                new("B", new HashSet<string> { "P1" }),
            },
            new List<(string, string)> { ("P1", "A"), ("P2", "A"), ("P1", "B") },
            ids).Should().BeFalse();
    }

    /// <summary>**决策身份带长度前缀，不会被拼接混淆** / Decision identities carry length prefixes so concatenation cannot collide.</summary>
    /// <remarks>
    /// `assignment:a:bc` 与 `assignment:ab:c` 直接拼成字符串会撞在一起。**带长度前缀后不会。**
    ///
    /// `assignment:a:bc` and `assignment:ab:c` collide if simply concatenated; **with length prefixes they do not.**
    /// </remarks>
    [Fact]
    public void DecisionId_ShouldNotCollideAcrossComponentBoundaries() {
        string left = PartitionLoadingContext<Flt64>.DecisionId("assignment", "a", "bc");
        string right = PartitionLoadingContext<Flt64>.DecisionId("assignment", "ab", "c");

        left.Should().NotBe(right);
        left.Should().Be("assignment:1:a:2:bc");
    }

    /// <summary>**列标识按本地顺序稳定** / The column identity is stable regardless of local ordering.</summary>
    /// <remarks>
    /// 两条列只要局部取值相同就必须**同标识**——否则同一个方案会被加进池两次，目标里重复计费。
    ///
    /// Two columns with equal local values must share **one identity**, or the same configuration enters the pool twice and is charged twice in the objective.
    /// </remarks>
    [Fact]
    public void ColumnId_ShouldBeStableAcrossKeyOrdering() {
        var first = new Dictionary<string, Flt64> { ["b"] = new(1.0), ["a"] = new(2.0) };
        var second = new Dictionary<string, Flt64> { ["a"] = new(2.0), ["b"] = new(1.0) };

        PartitionLoadingContext<Flt64>.ColumnId("blk", first).Should().Be(
            PartitionLoadingContext<Flt64>.ColumnId("blk", second));
        PartitionLoadingContext<Flt64>.ColumnId("blk", first).Should().NotBe(
            PartitionLoadingContext<Flt64>.ColumnId("blk2", first));
    }

    /// <summary>**整数类型必须被识别出来** / Integer-typed variables must be recognised.</summary>
    /// <remarks>
    /// **连续资源不能被当成整数**：把预估值四舍五入到整数，会**抹掉一个正重量**。
    ///
    /// **A continuous resource must not be treated as an integer**: rounding an estimate to a whole number **erases a positive weight**.
    /// </remarks>
    [Fact]
    public void IsIntegerType_ShouldSeparateIntegersFromContinuousResources() {
        PartitionLoadingContext<Flt64>.IsIntegerType(new UIntVar("n")).Should().BeTrue();
        PartitionLoadingContext<Flt64>.IsIntegerType(new IntVar("i")).Should().BeTrue();
        PartitionLoadingContext<Flt64>.IsIntegerType(new BinVar("b")).Should().BeTrue();
        PartitionLoadingContext<Flt64>.IsIntegerType(new RealVar("r")).Should().BeFalse(
            "a continuous resource must never be rounded to an integer");
        PartitionLoadingContext<Flt64>.IsIntegerType(new URealVar("u")).Should().BeFalse();
    }

    /// <summary>**范围与整数性复核** / Bounds and integrality checking.</summary>
    [Fact]
    public void ViolatesBounds_ShouldRejectOutOfRangeAndNonIntegerValues() {
        var binary = new BinVar("b");
        var continuous = new RealVar("r");

        PartitionLoadingContext<Flt64>.ViolatesBounds(binary, new Flt64(0.5)).Should().BeTrue(
            "0.5 is not an integer decision");
        PartitionLoadingContext<Flt64>.ViolatesBounds(binary, new Flt64(2.0)).Should().BeTrue(
            "2 exceeds the binary upper bound");
        PartitionLoadingContext<Flt64>.ViolatesBounds(binary, new Flt64(1.0)).Should().BeFalse();
        PartitionLoadingContext<Flt64>.ViolatesBounds(continuous, new Flt64(0.5)).Should().BeFalse();
    }

    /// <summary>**装配产出完整生命周期对象** / Assembly yields a complete lifecycle object.</summary>
    /// <remarks>
    /// 用真实的离散上下文与已注册模型：分区覆盖两个舱位，决策表包含分配与占位。
    /// 这是这里唯一覆盖"装配"的判据——**其余判据都打在纯函数上**。
    ///
    /// A real discrete context and a registered model: the partition covers both positions and the decision table holds assignments and occupancies. This is the batch's
    /// only case covering **assembly**; every other criterion hits a pure function.
    /// </remarks>
    [Fact]
    public async Task CreateAsync_ShouldAssembleTheLifecycleContext() {
        DiscreteLoadingProblem<Flt64> problem = MakeProblem();
        var loading = new DiscreteLoadingContext<Flt64>(problem, Flt64PackingValueAdapter.Instance);
        LinearMetaModel<Flt64> model = new();
        Try registered = loading.Register(model);
        registered.IsOk.Should().BeTrue(registered.ToString());

        var partitions = new List<LoadingPartition> {
            new("A", new HashSet<string> { "P1" }),
            new("B", new HashSet<string> { "P2" }),
        };

        Result<PartitionLoadingContext<Flt64>, ErrorCode, Error<ErrorCode>> result =
            await PartitionLoadingContext<Flt64>.CreateAsync(loading, model, partitions);

        result.IsOk.Should().BeTrue(result.ToString());
        PartitionLoadingContext<Flt64> context =
            ((Ok<PartitionLoadingContext<Flt64>, ErrorCode, Error<ErrorCode>>)result).Value;
        context.Partitions.Should().HaveCount(2);
        context.Decisions.Should().NotBeEmpty("assignments and occupancies both bind to partitions");
        context.ResourceDecisions.Should().BeEmpty("the fixture declares no downstream resource decisions");
    }

    /// <summary>**分区覆盖不全时装配必须失败** / Assembly must fail on incomplete coverage.</summary>
    [Fact]
    public async Task CreateAsync_ShouldRejectIncompleteCoverage() {
        DiscreteLoadingProblem<Flt64> problem = MakeProblem();
        var loading = new DiscreteLoadingContext<Flt64>(problem, Flt64PackingValueAdapter.Instance);
        LinearMetaModel<Flt64> model = new();
        loading.Register(model).IsOk.Should().BeTrue();

        // 只声明 P1 的分区，P2 无人归属。
        // Only P1 is owned; P2 is unowned.
        Result<PartitionLoadingContext<Flt64>, ErrorCode, Error<ErrorCode>> result =
            await PartitionLoadingContext<Flt64>.CreateAsync(
                loading, model, new List<LoadingPartition> { new("A", new HashSet<string> { "P1" }) });

        result.IsFailed.Should().BeTrue();
        ((Failed<PartitionLoadingContext<Flt64>, ErrorCode, Error<ErrorCode>>)result).Error.Code
            .Should().Be(ErrorCode.IllegalArgument);
    }

    /// <summary>**未知分区必须在碰模型之前就拒掉** / An unknown partition must be refused before the model is touched.</summary>
    /// <remarks>
    /// 定价请求指向一个不存在的分区。**判据只看错误码**：分区检查排在建模型之前，所以一个无效版本号也不会被用到。
    ///
    /// The pricing request points at a partition that does not exist. **The criterion only checks the error code**: the partition check runs before the model is built,
    /// so even an invalid revision is never used.
    /// </remarks>
    [Fact]
    public async Task PricingModelAsync_ShouldRejectAnUnknownPartition() {
        DiscreteLoadingProblem<Flt64> problem = MakeProblem();
        var loading = new DiscreteLoadingContext<Flt64>(problem, Flt64PackingValueAdapter.Instance);
        LinearMetaModel<Flt64> model = new();
        loading.Register(model).IsOk.Should().BeTrue();

        Result<PartitionLoadingContext<Flt64>, ErrorCode, Error<ErrorCode>> created =
            await PartitionLoadingContext<Flt64>.CreateAsync(
                loading,
                model,
                new List<LoadingPartition> {
                    new("A", new HashSet<string> { "P1" }),
                    new("B", new HashSet<string> { "P2" }),
                });
        created.IsOk.Should().BeTrue(created.ToString());
        PartitionLoadingContext<Flt64> context =
            ((Ok<PartitionLoadingContext<Flt64>, ErrorCode, Error<ErrorCode>>)created).Value;

        var request = new Fuookami.Ospf.Framework.Solver.ColumnPricingRequest<Flt64>(
            blockId: "NOPE",
            coefficients: new Dictionary<string, Flt64>(),
            convexityPrice: Flt64.Zero,
            rowPrices: new Dictionary<string, Flt64>(),
            revision: new Fuookami.Ospf.Framework.Solver.ColumnProjectionRevision(default, 0L, 0L));

        Result<LinearMetaModel<Flt64>, ErrorCode, Error<ErrorCode>> priced = await context.PricingModelAsync(request);

        priced.IsFailed.Should().BeTrue();
        ((Failed<LinearMetaModel<Flt64>, ErrorCode, Error<ErrorCode>>)priced).Error.Code
            .Should().Be(ErrorCode.IllegalArgument);
    }

    /// <summary>构造两舱位的问题 / Build a two-position problem.</summary>
    /// <returns>问题 / The problem.</returns>
    private static DiscreteLoadingProblem<Flt64> MakeProblem() =>
        new(
            Items: new List<LoadingItem<Flt64>> {
                new("I1", Kilograms(1.0), Required: true, FixedPositionId: null),
            },
            Positions: new List<LoadingPosition<Flt64>> {
                new("P1", MaxCount: 5UL, MaxWeight: Kilograms(5.0), FixedOccupied: false, Enabled: true),
                new("P2", MaxCount: 5UL, MaxWeight: Kilograms(5.0), FixedOccupied: false, Enabled: true),
            },
            AllowedAssignments: new HashSet<AssignmentKey> { new("I1", "P1"), new("I1", "P2") },
            Conflicts: new HashSet<Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading.Model.PositionConflict>(),
            LayoutGroups: new List<LayoutSelectionGroup>(),
            WeightUnit: SIBaseUnits.Kilogram);

    /// <summary>构造千克量 / Build a kilogram quantity.</summary>
    /// <param name="value">数值 / Value.</param>
    /// <returns>带单位的量 / The unit-carrying quantity.</returns>
    private static Quantity<Flt64> Kilograms(double value) =>
        new(new Flt64(value), SIBaseUnits.Kilogram);
}
