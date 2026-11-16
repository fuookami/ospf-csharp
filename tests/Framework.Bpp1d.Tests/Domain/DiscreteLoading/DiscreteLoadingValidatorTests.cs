// 离散装载输入校验判据 / Criteria for discrete-loading input validation.

using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading;
using Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading.Model;
using Fuookami.Ospf.Framework.Bpp.Infrastructure;
// 两个命名空间各有 `PositionConflict`：几何扫描的有向边 vs 业务互斥的无序对。
// Both namespaces define `PositionConflict`: the geometry-sweep directed edge vs the business unordered pair.
using PositionConflict = Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading.Model.PositionConflict;
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

namespace Fuookami.Ospf.Framework.Bpp1d.Tests.Domain.DiscreteLoading;

/// <summary>
/// **离散装载输入校验** / Discrete-loading input validation.
/// </summary>
/// <remarks>
/// 对应基线 `DiscreteLoadingContext.validate` 的十一类校验。判据挑出**最隐蔽的三条**：
///
/// | # | 校验 | 为什么隐蔽 |
/// | --- | --- | --- |
/// | 8 | 货物重量非负 | 负重量在 `Σ weight &lt;= maxWeight` 里**抵消**其他货物 |
/// | 9 | 固定装载是合法分配 | 固定到不允许的舱位，模型静默少一项 |
/// | 11 | 固定装载不超容量 | 固定装载本身就超重，求解器只会说"不可行" |
///
/// 's eleven checks; the criteria single out the **three subtlest** (table above).
/// </remarks>
public class DiscreteLoadingValidatorTests {
    /// <summary>**合法输入必须通过，且容量与重量被归一化** / A legal input passes and normalises capacities and weights.</summary>
    [Fact]
    public void Validate_ShouldAcceptALegalProblemAndNormaliseValues() {
        DiscreteLoadingProblem<Flt64> problem = MakeProblem(
            items: [("I1", 1000.0, null)],
            positions: [("P1", 5UL, 5000.0)]);

        var validator = new DiscreteLoadingValidator<Flt64>(problem, Flt64PackingValueAdapter.Instance);
        Try result = validator.Validate();

        result.IsOk.Should().BeTrue(result.ToString());
        // **归一化的目标单位是 `WeightUnit`（这里是克），不是千克。**
        // 最初写成 5.0（千克），实测 5000.0 —— **判据写错了，不是代码错了**。
        //
        // **The normalisation target is `WeightUnit` (grams here), not kilograms.** The first version expected 5.0 (kilograms) and measured 5000.0 — **the criterion was
        // wrong, not the code**.
        validator.Capacities["P1"].ToDouble().Should().BeApproximately(
            5000.0, 1e-9, "the problem's weight unit is grams, so the capacity stays 5000");
        validator.ItemWeights["I1"].ToDouble().Should().BeApproximately(
            1000.0, 1e-9, "the problem's weight unit is grams, so the weight stays 1000");
    }

    /// <summary>**负重量必须被拒——它会在容量约束里抵消其他货物** / A negative weight must be rejected: it cancels other cargo in the capacity constraint.</summary>
    /// <remarks>
    /// 这是最隐蔽的一条。若允许负重量，`Σ weight &lt;= maxWeight` 会**抵消**其他货物，
    /// 让一个实际超载的方案"看起来"满足容量。**求解器不会报错**，它只会返回那个错的方案。
    ///
    /// The subtlest check. Allowing a negative weight lets `Σ weight &lt;= maxWeight` **cancel** other cargo, making an actually overloaded plan look feasible.
    /// **The solver never complains** — it just returns that wrong plan.
    /// </remarks>
    [Fact]
    public void Validate_ShouldRejectANegativeItemWeight() {
        DiscreteLoadingProblem<Flt64> problem = MakeProblem(
            items: [("I1", -100.0, null)],
            positions: [("P1", 5UL, 5000.0)]);

        var validator = new DiscreteLoadingValidator<Flt64>(problem, Flt64PackingValueAdapter.Instance);
        Try result = validator.Validate();

        result.IsFailed.Should().BeTrue(
            "a negative weight would cancel other cargo in the capacity constraint");
    }

    /// <summary>**负容量必须被拒——且是容量检查本身拒的** / A negative capacity must be rejected, and by the capacity check itself.</summary>
    /// <remarks>
    /// **第一版判据太弱，变异存活。** 原来的输入带一件**未固定**的货物，
    /// 于是负容量同时让"固定装载不超容量"那条也失败——**两条检查任意一条就能拦住**，
    /// 把容量检查改成只查件数，判据仍然绿。
    ///
    /// 现在**不给任何货物**：没有固定装载，`fixedCount = 0`、`fixedWeight = 0`，
    /// 固定装载检查**不可能**失败，**只有容量检查能拦住**。
    ///
    /// **The first criterion was too weak and the mutant survived.** It carried an **unfixed** item, so a negative capacity also failed the "fixed load within capacity"
    /// check — **either check sufficed**, and disabling the capacity check alone kept the criterion green. Now **no items at all**: with `fixedCount = 0` and
    /// `fixedWeight = 0` the fixed-load check cannot fail, so **only the capacity check can catch it**.
    /// </remarks>
    [Fact]
    public void Validate_ShouldRejectANegativePositionCapacity() {
        DiscreteLoadingProblem<Flt64> problem = MakeProblem(
            items: [],
            positions: [("P1", 5UL, -1.0)]);

        var validator = new DiscreteLoadingValidator<Flt64>(problem, Flt64PackingValueAdapter.Instance);
        Try result = validator.Validate();

        result.IsFailed.Should().BeTrue("a negative capacity makes every loading infeasible: " + result.ToString());
    }

    /// <summary>**零件数容量必须被拒** / A zero max count must be rejected.</summary>
    [Fact]
    public void Validate_ShouldRejectAZeroMaxCount() {
        DiscreteLoadingProblem<Flt64> problem = MakeProblem(
            items: [("I1", 100.0, null)],
            positions: [("P1", 0UL, 5000.0)]);

        var validator = new DiscreteLoadingValidator<Flt64>(problem, Flt64PackingValueAdapter.Instance);
        Try result = validator.Validate();

        result.IsFailed.Should().BeTrue("a position that holds nothing should not be offered at all");
    }

    /// <summary>**固定装载到不允许的舱位必须被拒** / A fixed load into a forbidden position must be rejected.</summary>
    [Fact]
    public void Validate_ShouldRejectAFixedAssignmentThatIsNotAllowed() {
        DiscreteLoadingProblem<Flt64> problem = MakeProblem(
            items: [("I1", 100.0, "P2")],
            positions: [("P1", 5UL, 5000.0), ("P2", 5UL, 5000.0)],
            allowed: [("I1", "P1")]);

        var validator = new DiscreteLoadingValidator<Flt64>(problem, Flt64PackingValueAdapter.Instance);
        Try result = validator.Validate();

        result.IsFailed.Should().BeTrue(
            "I1 is fixed to P2, but only I1→P1 is listed as a legal assignment");
    }

    /// <summary>**固定装载超容量必须被拒** / A fixed load that exceeds the position capacity must be rejected.</summary>
    /// <remarks>
    /// 两个 3000 克的货物固定在同一个 5000 克舱位：总量 6000 克超容量。
    /// 若不查这一条，模型会在求解阶段返回"不可行"——**离根因很远**。
    ///
    /// Two 3000 g items fixed to one 5000 g position total 6000 g. Without this check the model would report "infeasible" at solve time — **far from the root cause**.
    /// </remarks>
    [Fact]
    public void Validate_ShouldRejectAFixedLoadThatExceedsTheCapacity() {
        DiscreteLoadingProblem<Flt64> problem = MakeProblem(
            items: [("I1", 3000.0, "P1"), ("I2", 3000.0, "P1")],
            positions: [("P1", 5UL, 5000.0)]);

        var validator = new DiscreteLoadingValidator<Flt64>(problem, Flt64PackingValueAdapter.Instance);
        Try result = validator.Validate();

        result.IsFailed.Should().BeTrue("the two fixed items total 6000 g against a 5000 g position");
    }

    /// <summary>**两条固定占用互相冲突必须被拒** / Two conflicting fixed occupancies must be rejected.</summary>
    [Fact]
    public void Validate_ShouldRejectConflictingFixedOccupancy() {
        DiscreteLoadingProblem<Flt64> problem = MakeProblem(
            items: [("I1", 100.0, "P1"), ("I2", 100.0, "P2")],
            positions: [("P1", 5UL, 5000.0), ("P2", 5UL, 5000.0)],
            conflicts: [("P1", "P2")]);

        var validator = new DiscreteLoadingValidator<Flt64>(problem, Flt64PackingValueAdapter.Instance);
        Try result = validator.Validate();

        result.IsFailed.Should().BeTrue("P1 and P2 conflict, yet both are fixed to be occupied");
    }

    /// <summary>**分配引用不存在的舱位必须被拒** / An assignment referencing an unknown position must be rejected.</summary>
    [Fact]
    public void Validate_ShouldRejectAnAssignmentWithAnUnknownReference() {
        DiscreteLoadingProblem<Flt64> problem = MakeProblem(
            items: [("I1", 100.0, null)],
            positions: [("P1", 5UL, 5000.0)],
            allowed: [("I1", "NOPE")]);

        var validator = new DiscreteLoadingValidator<Flt64>(problem, Flt64PackingValueAdapter.Instance);
        Try result = validator.Validate();

        result.IsFailed.Should().BeTrue("NOPE is not a position");
    }

    /// <summary>**重复货物标识必须被拒** / Duplicate item identities must be rejected.</summary>
    [Fact]
    public void Validate_ShouldRejectDuplicateItemIdentities() {
        DiscreteLoadingProblem<Flt64> problem = MakeProblem(
            items: [("I1", 100.0, null), ("I1", 200.0, null)],
            positions: [("P1", 5UL, 5000.0)]);

        var validator = new DiscreteLoadingValidator<Flt64>(problem, Flt64PackingValueAdapter.Instance);
        Try result = validator.Validate();

        result.IsFailed.Should().BeTrue("two cargo items share the identity I1");
    }

    /// <summary>**自冲突必须被拒** / A self-conflict must be rejected.</summary>
    [Fact]
    public void Validate_ShouldRejectASelfConflict() {
        DiscreteLoadingProblem<Flt64> problem = MakeProblem(
            items: [("I1", 100.0, null)],
            positions: [("P1", 5UL, 5000.0)],
            conflicts: [("P1", "P1")]);

        var validator = new DiscreteLoadingValidator<Flt64>(problem, Flt64PackingValueAdapter.Instance);
        Try result = validator.Validate();

        result.IsFailed.Should().BeTrue("a position conflicting with itself makes the model trivially infeasible");
    }

    /// <summary>构造问题 / Build a problem.</summary>
    /// <param name="items">货物：(标识, 克重, 固定舱位) / Items: (identity, grams, fixed position).</param>
    /// <param name="positions">舱位：(标识, 最大件数, 克容量) / Positions: (identity, max count, gram capacity).</param>
    /// <param name="allowed">合法分配；缺省为全部组合 / Legal assignments; all pairs by default.</param>
    /// <param name="conflicts">互斥对 / Conflicting pairs.</param>
    /// <returns>问题 / The problem.</returns>
    private static DiscreteLoadingProblem<Flt64> MakeProblem(
        (string Id, double Weight, string? Fixed)[] items,
        (string Id, ulong MaxCount, double Capacity)[] positions,
        (string Item, string Position)[]? allowed = null,
        (string First, string Second)[]? conflicts = null) {
        var itemList = new List<LoadingItem<Flt64>>(items.Length);
        foreach ((string id, double weight, string? fixedPosition) in items) {
            itemList.Add(new LoadingItem<Flt64>(id, Grams(weight), Required: false, FixedPositionId: fixedPosition));
        }
        var positionList = new List<LoadingPosition<Flt64>>(positions.Length);
        foreach ((string id, ulong maxCount, double capacity) in positions) {
            positionList.Add(new LoadingPosition<Flt64>(id, maxCount, Grams(capacity)));
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
            LayoutGroups: new List<LayoutSelectionGroup>(),
            WeightUnit: MassUnits.Gram);
    }

    /// <summary>构造克量 / Build a gram quantity.</summary>
    /// <param name="value">数值 / Value.</param>
    /// <returns>带单位的量 / The unit-carrying quantity.</returns>
    private static Quantity<Flt64> Grams(double value) =>
        new(new Flt64(value), MassUnits.Gram);
}
