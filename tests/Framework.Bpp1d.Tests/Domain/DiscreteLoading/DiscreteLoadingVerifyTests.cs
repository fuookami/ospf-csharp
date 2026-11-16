// 离散装载解复核判据 / Criteria for solver-independent discrete-loading verification.

using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading;
using Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading.Model;
using Fuookami.Ospf.Framework.Bpp.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;
using PositionConflict = Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading.Model.PositionConflict;
using Try = Fuookami.Ospf.Utils.Functional.Result<
    Fuookami.Ospf.Utils.Functional.Success,
    Fuookami.Ospf.Utils.Error.ErrorCode,
    Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.Bpp1d.Tests.Domain.DiscreteLoading;

/// <summary>
/// **离散装载解复核** / Solver-independent discrete-loading verification.
/// </summary>
/// <remarks>
/// 对应基线 `DiscreteLoadingContext.verify` 的九类复核。**这一块是本模块最值得移植的**：
/// 不依赖求解器、不依赖元模型，只用问题定义就能把"求解器说可行"独立复核一遍。
/// 没有它，求解器或其适配层的任何一处错误都会**直接变成对外结论**。
///
/// 判据按九条规则分组，每条都有一个**必败**用例与一个**必过**用例——
/// 只写必过会让"永远返回 Ok"的实现通过，只写必败会让"永远返回 Failed"的实现通过。
///
/// Mirrors the baseline's nine checks. Each rule gets **one failing and one passing** case: only-passing would let "always Ok" survive, only-failing would let
/// "always Failed" survive.
/// </remarks>
public class DiscreteLoadingVerifyTests {
    /// <summary>**合法解必须通过** / A legal solution must pass.</summary>
    [Fact]
    public void Verify_ShouldAcceptALegalSolution() {
        DiscreteLoadingProblem<Flt64> problem = MakeProblem(
            items: [("I1", 1000.0, false, null)],
            positions: [("P1", 5UL, 5000.0)]);
        var validator = new DiscreteLoadingValidator<Flt64>(problem, Flt64PackingValueAdapter.Instance);
        var solution = new DiscreteLoadingSolution(
            Assignments: new Dictionary<string, string> { ["I1"] = "P1" },
            OccupiedPositions: new HashSet<string> { "P1" },
            SelectedLayouts: new Dictionary<string, string>());

        Try result = validator.Verify(solution);

        result.IsOk.Should().BeTrue(result.ToString());
    }

    /// <summary>**必装货物未装必须被拒** / A missing mandatory cargo must be rejected.</summary>
    [Fact]
    public void Verify_ShouldRejectAMissingRequiredCargo() {
        DiscreteLoadingProblem<Flt64> problem = MakeProblem(
            items: [("I1", 1000.0, true, null)],
            positions: [("P1", 5UL, 5000.0)]);
        var validator = new DiscreteLoadingValidator<Flt64>(problem, Flt64PackingValueAdapter.Instance);
        var solution = new DiscreteLoadingSolution(
            Assignments: new Dictionary<string, string>(),
            OccupiedPositions: new HashSet<string>(),
            SelectedLayouts: new Dictionary<string, string>());

        Try result = validator.Verify(solution);

        result.IsFailed.Should().BeTrue("I1 is required but never assigned");
        ((Failed<Success, ErrorCode, Error<ErrorCode>>)result).Error.Code
            .Should().Be(ErrorCode.ApplicationError, "a bad solution is an application error, not an argument error");
    }

    /// <summary>**固定货物装错位置必须被拒** / A fixed cargo in the wrong position must be rejected.</summary>
    [Fact]
    public void Verify_ShouldRejectAFixedCargoInTheWrongPosition() {
        DiscreteLoadingProblem<Flt64> problem = MakeProblem(
            items: [("I1", 1000.0, false, "P1")],
            positions: [("P1", 5UL, 5000.0), ("P2", 5UL, 5000.0)]);
        var validator = new DiscreteLoadingValidator<Flt64>(problem, Flt64PackingValueAdapter.Instance);
        var solution = new DiscreteLoadingSolution(
            Assignments: new Dictionary<string, string> { ["I1"] = "P2" },
            OccupiedPositions: new HashSet<string> { "P2" },
            SelectedLayouts: new Dictionary<string, string>());

        Try result = validator.Verify(solution);

        result.IsFailed.Should().BeTrue("I1 is fixed to P1 but the solution puts it on P2");
    }

    /// <summary>**未声明的合法组合必须被拒** / An undeclared assignment must be rejected.</summary>
    [Fact]
    public void Verify_ShouldRejectAnIllegalAssignment() {
        DiscreteLoadingProblem<Flt64> problem = MakeProblem(
            items: [("I1", 1000.0, false, null)],
            positions: [("P1", 5UL, 5000.0), ("P2", 5UL, 5000.0)],
            allowed: [("I1", "P1")]);
        var validator = new DiscreteLoadingValidator<Flt64>(problem, Flt64PackingValueAdapter.Instance);
        var solution = new DiscreteLoadingSolution(
            Assignments: new Dictionary<string, string> { ["I1"] = "P2" },
            OccupiedPositions: new HashSet<string> { "P2" },
            SelectedLayouts: new Dictionary<string, string>());

        Try result = validator.Verify(solution);

        result.IsFailed.Should().BeTrue("only I1→P1 was declared legal");
    }

    /// <summary>**冲突舱位同时占用必须被拒** / Two occupied conflicting positions must be rejected.</summary>
    [Fact]
    public void Verify_ShouldRejectOverlappingOccupancy() {
        DiscreteLoadingProblem<Flt64> problem = MakeProblem(
            items: [("I1", 1000.0, false, null), ("I2", 1000.0, false, null)],
            positions: [("P1", 5UL, 5000.0), ("P2", 5UL, 5000.0)],
            conflicts: [("P1", "P2")]);
        var validator = new DiscreteLoadingValidator<Flt64>(problem, Flt64PackingValueAdapter.Instance);
        var solution = new DiscreteLoadingSolution(
            Assignments: new Dictionary<string, string> { ["I1"] = "P1", ["I2"] = "P2" },
            OccupiedPositions: new HashSet<string> { "P1", "P2" },
            SelectedLayouts: new Dictionary<string, string>());

        Try result = validator.Verify(solution);

        result.IsFailed.Should().BeTrue("P1 and P2 are declared mutually exclusive");
    }

    /// <summary>**附加重量必须非负且有限** / Additional weight must be non-negative and finite.</summary>
    /// <remarks>
    /// **负附加重量是最危险的一条**：它会在容量求和里**抵消**真实载荷，
    /// 让一个超载方案看起来合规。判据用 `-1000` 克附加重量。
    ///
    /// **A negative additional weight is the most dangerous case**: it **cancels** real load in the capacity sum, making an overloaded plan look legal. The criterion uses
    /// `-1000` grams.
    /// </remarks>
    [Fact]
    public void Verify_ShouldRejectANegativeAdditionalWeight() {
        DiscreteLoadingProblem<Flt64> problem = MakeProblem(
            items: [("I1", 1000.0, false, null)],
            positions: [("P1", 5UL, 5000.0)]);
        var validator = new DiscreteLoadingValidator<Flt64>(problem, Flt64PackingValueAdapter.Instance);
        var solution = new DiscreteLoadingSolution(
            Assignments: new Dictionary<string, string> { ["I1"] = "P1" },
            OccupiedPositions: new HashSet<string> { "P1" },
            SelectedLayouts: new Dictionary<string, string>());
        var additional = new Dictionary<string, Flt64> { ["P1"] = new Flt64(-1000.0) };

        Try result = validator.Verify(solution, additional);

        result.IsFailed.Should().BeTrue("a negative additional weight would cancel the real cargo in the capacity sum");
    }

    /// <summary>**超容量必须被拒** / Over-capacity must be rejected.</summary>
    [Fact]
    public void Verify_ShouldRejectAnOverloadedPosition() {
        DiscreteLoadingProblem<Flt64> problem = MakeProblem(
            items: [("I1", 3000.0, false, null), ("I2", 3000.0, false, null)],
            positions: [("P1", 5UL, 5000.0)]);
        var validator = new DiscreteLoadingValidator<Flt64>(problem, Flt64PackingValueAdapter.Instance);
        var solution = new DiscreteLoadingSolution(
            Assignments: new Dictionary<string, string> { ["I1"] = "P1", ["I2"] = "P1" },
            OccupiedPositions: new HashSet<string> { "P1" },
            SelectedLayouts: new Dictionary<string, string>());

        Try result = validator.Verify(solution);

        result.IsFailed.Should().BeTrue("6000 g exceeds the 5000 g position");
    }

    /// <summary>**件数超限必须被拒** / Exceeding the max count must be rejected.</summary>
    [Fact]
    public void Verify_ShouldRejectTooManyItemsForTheMaxCount() {
        DiscreteLoadingProblem<Flt64> problem = MakeProblem(
            items: [("I1", 100.0, false, null), ("I2", 100.0, false, null)],
            positions: [("P1", 1UL, 5000.0)]);
        var validator = new DiscreteLoadingValidator<Flt64>(problem, Flt64PackingValueAdapter.Instance);
        var solution = new DiscreteLoadingSolution(
            Assignments: new Dictionary<string, string> { ["I1"] = "P1", ["I2"] = "P1" },
            OccupiedPositions: new HashSet<string> { "P1" },
            SelectedLayouts: new Dictionary<string, string>());

        Try result = validator.Verify(solution);

        result.IsFailed.Should().BeTrue("P1 holds at most one item but two are assigned");
    }

    /// <summary>**有内容却未标记占用必须被拒** / Content without an occupancy flag must be rejected.</summary>
    /// <remarks>
    /// 这条容易被忽略：装了货但没把舱位标成占用，会让**下游的占用统计与实际内容脱节**。
    ///
    /// Easy to miss: cargo loaded but the position never flagged occupied makes **downstream occupancy statistics disagree with the actual content**.
    /// </remarks>
    [Fact]
    public void Verify_ShouldRejectContentWithoutOccupancy() {
        DiscreteLoadingProblem<Flt64> problem = MakeProblem(
            items: [("I1", 1000.0, false, null)],
            positions: [("P1", 5UL, 5000.0)]);
        var validator = new DiscreteLoadingValidator<Flt64>(problem, Flt64PackingValueAdapter.Instance);
        var solution = new DiscreteLoadingSolution(
            Assignments: new Dictionary<string, string> { ["I1"] = "P1" },
            OccupiedPositions: new HashSet<string>(),
            SelectedLayouts: new Dictionary<string, string>());

        Try result = validator.Verify(solution);

        result.IsFailed.Should().BeTrue("P1 carries cargo but is not marked occupied");
    }

    /// <summary>**禁用舱位不得被随意占用** / A disabled position must not be occupied freely.</summary>
    [Fact]
    public void Verify_ShouldRejectOccupationOfADisabledPosition() {
        DiscreteLoadingProblem<Flt64> problem = MakeProblem(
            items: [("I1", 1000.0, false, null)],
            positions: [("P1", 5UL, 5000.0)],
            disabled: ["P1"]);
        var validator = new DiscreteLoadingValidator<Flt64>(problem, Flt64PackingValueAdapter.Instance);
        var solution = new DiscreteLoadingSolution(
            Assignments: new Dictionary<string, string> { ["I1"] = "P1" },
            OccupiedPositions: new HashSet<string> { "P1" },
            SelectedLayouts: new Dictionary<string, string>());

        Try result = validator.Verify(solution);

        result.IsFailed.Should().BeTrue("P1 is disabled and nothing is fixed to it");
    }

    /// <summary>**选择组必须恰选一种且布局覆盖占用** / Exactly one layout per group and it must cover the occupancy.</summary>
    [Fact]
    public void Verify_ShouldRejectAMissingLayoutSelection() {
        DiscreteLoadingProblem<Flt64> problem = MakeProblem(
            items: [("I1", 1000.0, false, null)],
            positions: [("P1", 5UL, 5000.0), ("P2", 5UL, 5000.0)],
            layoutGroups: [
                new LayoutSelectionGroup(
                    "G1",
                    new HashSet<string> { "P1", "P2" },
                    new List<LoadingLayout> {
                        new("L1", new HashSet<string> { "P1" }),
                        new("L2", new HashSet<string> { "P2" }),
                    }),
            ]);
        var validator = new DiscreteLoadingValidator<Flt64>(problem, Flt64PackingValueAdapter.Instance);
        var solution = new DiscreteLoadingSolution(
            Assignments: new Dictionary<string, string> { ["I1"] = "P1" },
            OccupiedPositions: new HashSet<string> { "P1" },
            SelectedLayouts: new Dictionary<string, string>());

        Try result = validator.Verify(solution);

        result.IsFailed.Should().BeTrue("group G1 exists but no layout was selected for it");
    }

    /// <summary>**合法布局选择必须通过** / A legal layout selection must pass.</summary>
    [Fact]
    public void Verify_ShouldAcceptALegalLayoutSelection() {
        DiscreteLoadingProblem<Flt64> problem = MakeProblem(
            items: [("I1", 1000.0, false, null)],
            positions: [("P1", 5UL, 5000.0), ("P2", 5UL, 5000.0)],
            layoutGroups: [
                new LayoutSelectionGroup(
                    "G1",
                    new HashSet<string> { "P1", "P2" },
                    new List<LoadingLayout> {
                        new("L1", new HashSet<string> { "P1" }),
                        new("L2", new HashSet<string> { "P2" }),
                    }),
            ]);
        var validator = new DiscreteLoadingValidator<Flt64>(problem, Flt64PackingValueAdapter.Instance);
        var solution = new DiscreteLoadingSolution(
            Assignments: new Dictionary<string, string> { ["I1"] = "P1" },
            OccupiedPositions: new HashSet<string> { "P1" },
            SelectedLayouts: new Dictionary<string, string> { ["G1"] = "L1" });

        Try result = validator.Verify(solution);

        result.IsOk.Should().BeTrue(result.ToString());
    }

    /// <summary>**选中的布局未覆盖占用舱位必须被拒** / A selected layout that misses an occupied position must be rejected.</summary>
    [Fact]
    public void Verify_ShouldRejectALayoutThatMissesAnOccupiedPosition() {
        DiscreteLoadingProblem<Flt64> problem = MakeProblem(
            items: [("I1", 1000.0, false, null)],
            positions: [("P1", 5UL, 5000.0), ("P2", 5UL, 5000.0)],
            layoutGroups: [
                new LayoutSelectionGroup(
                    "G1",
                    new HashSet<string> { "P1", "P2" },
                    new List<LoadingLayout> {
                        new("L1", new HashSet<string> { "P1" }),
                        new("L2", new HashSet<string> { "P2" }),
                    }),
            ]);
        var validator = new DiscreteLoadingValidator<Flt64>(problem, Flt64PackingValueAdapter.Instance);
        var solution = new DiscreteLoadingSolution(
            Assignments: new Dictionary<string, string> { ["I1"] = "P1" },
            OccupiedPositions: new HashSet<string> { "P1" },
            SelectedLayouts: new Dictionary<string, string> { ["G1"] = "L2" });

        Try result = validator.Verify(solution);

        result.IsFailed.Should().BeTrue("G1's scope includes P1, which is occupied, but L2 does not cover it");
    }

    /// <summary>**未知舱位必须被拒** / An unknown position must be rejected.</summary>
    [Fact]
    public void Verify_ShouldRejectAnUnknownOccupiedPosition() {
        DiscreteLoadingProblem<Flt64> problem = MakeProblem(
            items: [("I1", 1000.0, false, null)],
            positions: [("P1", 5UL, 5000.0)]);
        var validator = new DiscreteLoadingValidator<Flt64>(problem, Flt64PackingValueAdapter.Instance);
        var solution = new DiscreteLoadingSolution(
            Assignments: new Dictionary<string, string>(),
            OccupiedPositions: new HashSet<string> { "NOPE" },
            SelectedLayouts: new Dictionary<string, string>());

        Try result = validator.Verify(solution);

        result.IsFailed.Should().BeTrue("NOPE is not a position");
    }

    /// <summary>**容量比较留有 1e-6 容差，否则浮点误差会把正常解判成超载** / The capacity comparison keeps a 1e-6 tolerance, or floating noise flags a correct solution as overloaded.</summary>
    /// <remarks>
    /// **这条判据是补的。** 原来的判据用 1000 克对 5000 克容量——差值大得离谱，
    /// **容差去不掉**，变异存活。
    ///
    /// 现在用**刚好超出一点点**的输入：容量 1 克，两件 0.5000004 克，合计 `1.0000008`，
    /// 超 8e-7（**小于 1e-6**）⇒ 正确实现应通过；严格比较会拒。
    ///
    /// **This criterion was added later.** The original used 1000 g against a 5000 g capacity — the gap was enormous, so **removing the tolerance changed nothing** and
    /// the mutant survived. Now an input that **just barely** exceeds: capacity 1 g, two pieces of 0.5000004 g summing to `1.0000008`, over by 8e-7 (**below 1e-6**) ⇒
    /// correct code passes, a strict comparison rejects.
    /// </remarks>
    [Fact]
    public void Verify_ShouldTolerateAFloatingPointOvershootBelow1e6() {
        DiscreteLoadingProblem<Flt64> problem = MakeProblem(
            items: [("I1", 0.5000004, false, null), ("I2", 0.5000004, false, null)],
            positions: [("P1", 5UL, 1.0)]);
        var validator = new DiscreteLoadingValidator<Flt64>(problem, Flt64PackingValueAdapter.Instance);
        var solution = new DiscreteLoadingSolution(
            Assignments: new Dictionary<string, string> { ["I1"] = "P1", ["I2"] = "P1" },
            OccupiedPositions: new HashSet<string> { "P1" },
            SelectedLayouts: new Dictionary<string, string>());

        Try result = validator.Verify(solution);

        result.IsOk.Should().BeTrue(
            "the total 1.0000008 exceeds the 1 g capacity by 8e-7, inside the 1e-6 tolerance: " + result.ToString());
    }

    /// <summary>**超出容差仍然必须被拒** / An overshoot beyond the tolerance must still be rejected.</summary>
    /// <remarks>
    /// 反向对照：若容差写成 1e-3 或干脆取消容量比较，上一条仍会绿。这条把容差**钉在上界**。
    ///
    /// Reverse control: a tolerance of 1e-3, or dropping the comparison entirely, would still pass the previous case. This one pins the **upper bound**.
    /// </remarks>
    [Fact]
    public void Verify_ShouldStillRejectAnOvershootBeyondTheTolerance() {
        DiscreteLoadingProblem<Flt64> problem = MakeProblem(
            items: [("I1", 0.5000020, false, null), ("I2", 0.5000020, false, null)],
            positions: [("P1", 5UL, 1.0)]);
        var validator = new DiscreteLoadingValidator<Flt64>(problem, Flt64PackingValueAdapter.Instance);
        var solution = new DiscreteLoadingSolution(
            Assignments: new Dictionary<string, string> { ["I1"] = "P1", ["I2"] = "P1" },
            OccupiedPositions: new HashSet<string> { "P1" },
            SelectedLayouts: new Dictionary<string, string>());

        Try result = validator.Verify(solution);

        result.IsFailed.Should().BeTrue("the total 1.0000040 is 4e-6 over, beyond the 1e-6 tolerance");
    }

    /// <summary>构造问题 / Build a problem.</summary>
    /// <param name="items">货物：(标识, 克重, 必装, 固定舱位) / Items: (identity, grams, required, fixed position).</param>
    /// <param name="positions">舱位：(标识, 最大件数, 克容量) / Positions: (identity, max count, gram capacity).</param>
    /// <param name="allowed">合法分配；缺省为全部组合 / Legal assignments; all pairs by default.</param>
    /// <param name="conflicts">互斥对 / Conflicting pairs.</param>
    /// <param name="layoutGroups">布局选择组 / Layout selection groups.</param>
    /// <param name="disabled">被禁用的舱位 / Disabled positions.</param>
    /// <returns>问题 / The problem.</returns>
    private static DiscreteLoadingProblem<Flt64> MakeProblem(
        (string Id, double Weight, bool Required, string? Fixed)[] items,
        (string Id, ulong MaxCount, double Capacity)[] positions,
        (string Item, string Position)[]? allowed = null,
        (string First, string Second)[]? conflicts = null,
        IReadOnlyList<LayoutSelectionGroup>? layoutGroups = null,
        string[]? disabled = null) {
        var disabledIds = new HashSet<string>(disabled ?? []);

        var itemList = new List<LoadingItem<Flt64>>(items.Length);
        foreach ((string id, double weight, bool required, string? fixedPosition) in items) {
            itemList.Add(new LoadingItem<Flt64>(
                id, Grams(weight), Required: required, FixedPositionId: fixedPosition));
        }
        var positionList = new List<LoadingPosition<Flt64>>(positions.Length);
        foreach ((string id, ulong maxCount, double capacity) in positions) {
            positionList.Add(new LoadingPosition<Flt64>(
                id, maxCount, Grams(capacity), Enabled: !disabledIds.Contains(id)));
        }

        var assignments = new HashSet<AssignmentKey>();
        if (allowed is null) {
            foreach (LoadingItem<Flt64> item in itemList) {
                foreach (LoadingPosition<Flt64> position in positionList) {
                    assignments.Add(new AssignmentKey(item.Id, position.Id));
                }
            }
        }
        else {
            foreach ((string item, string position) in allowed) {
                assignments.Add(new AssignmentKey(item, position));
            }
        }

        var conflictSet = new HashSet<PositionConflict>();
        if (conflicts is not null) {
            foreach ((string first, string second) in conflicts) {
                conflictSet.Add(new PositionConflict(first, second));
            }
        }

        return new DiscreteLoadingProblem<Flt64>(
            Items: itemList,
            Positions: positionList,
            AllowedAssignments: assignments,
            Conflicts: conflictSet,
            LayoutGroups: layoutGroups ?? new List<LayoutSelectionGroup>(),
            WeightUnit: MassUnits.Gram);
    }

    /// <summary>构造克量 / Build a gram quantity.</summary>
    /// <param name="value">数值 / Value.</param>
    /// <returns>带单位的量 / The unit-carrying quantity.</returns>
    private static Quantity<Flt64> Grams(double value) =>
        new(new Flt64(value), MassUnits.Gram);
}
