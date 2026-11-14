// 长度约束管线的判据 / Criteria for the length constraint pipeline.

using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.Csp1d.Domain.LengthAssignment;
using Fuookami.Ospf.Framework.Csp1d.Domain.LengthAssignment.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.LengthAssignment.Service.Pipeline;
using Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.Produce.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Test.Framework.Csp1d.Domain.LengthAssignment;

/// <summary>
/// **长度约束管线的四条约束** / The length constraint pipeline's four constraints.
/// </summary>
/// <remarks>
/// **B380 记的缺口**："`LengthConstraintPipeline` 的其余约束（超长上限等）没有判据"——
/// 上一批只间接看到了下界/上界两条。
///
/// 四条约束各有各的**方向**与**系数**，写错的后果都是**静默的**：
///
/// | 约束 | 形式 | 写错的后果 |
/// | --- | --- | --- |
/// | 下界 | `assigned >= lower` | 方向反了 ⇒ 卷长可以任意小 |
/// | 上界 | `assigned <= upper` | 方向反了 ⇒ 卷长可以任意大 |
/// | 超长上限 | `over <= overBound` | 同上 |
/// | **关联** | `assigned - over <= maxOver` | **系数写成 `+1` ⇒ 关联彻底失效** |
///
/// 最后一行是本批的重点：`-1` 写成 `1` 时约束仍然"存在"、名字仍然对，
/// **只有系数检查能发现**。
///
/// **B380's gap**: "the pipeline's other constraints (over-length bound and the rest) have no criteria" — the previous batch only saw the lower and upper bounds
/// indirectly. Each of the four has its own **direction** and **coefficient**, and every mistake fails **silently** (table above). The last row is this batch's focus:
/// turning `-1` into `1` leaves the constraint present and correctly named — **only a coefficient check catches it**.
/// </remarks>
public class LengthConstraintPipelineTests {
    private static Quantity<Flt64> Millimetres(double value) =>
        new(new Flt64(value), LengthUnits.Millimeter);

    private static Product<Flt64> MakeProduct() =>
        new(
            id: "A",
            name: "product-a",
            width: new List<Quantity<Flt64>> { Millimetres(60.0) },
            maxOverProduceLength: Millimetres(500.0));

    private static (LengthAggregation Length, LinearMetaModel<Flt64> Model) Build(
        LengthAssignmentModelingConfig<Flt64> config) {
        var demands = new List<ProductDemand<Flt64>> {
            new(MakeProduct(), Millimetres(60.0)),
        };
        var length = new LengthAggregation(config, demands);
        var model = new LinearMetaModel<Flt64>();
        length.Register(model).IsOk.Should().BeTrue();
        new LengthConstraintPipeline<Flt64>(length, config, demands).Invoke(model).IsOk.Should().BeTrue();
        return (length, model);
    }

    private static LinearInequalityConstraint<Flt64> Constraint(LinearMetaModel<Flt64> model, string name) =>
        model.Constraints
            .OfType<LinearInequalityConstraint<Flt64>>()
            .Single(c => c.Name == name);

    /// <summary>**四条约束都必须存在，方向与右端值正确** / All four constraints must exist with the right direction and right-hand value.</summary>
    /// <remarks>
    /// 配置给齐四个触发条件，于是四条约束都该被建出来。
    ///
    /// 断言**比较符 + 右端常数**，而不只是"存在"：
    /// `GE` 与 `LE` 互换的实现会让"卷长 ≥ 下界"变成"卷长 ≤ 下界"，
    /// **约束还在、名字还对，只是方向反了**。
    ///
    /// All four triggers are supplied, so all four constraints should be created. The assertions check **the comparison and the right-hand constant**, not just
    /// existence: swapping `GE` and `LE` turns "assigned ≥ lower" into "assigned ≤ lower" — **the constraint is still there and still named right, only reversed**.
    /// </remarks>
    [Fact]
    public void Invoke_ShouldEmitAllFourConstraintsWithTheRightDirection() {
        var config = new LengthAssignmentModelingConfig<Flt64>(
            DynamicProductIds: new HashSet<string> { "A" },
            AssignedLengthLowerBound: new Dictionary<string, Flt64> { ["A"] = new Flt64(10.0) },
            AssignedLengthUpperBound: new Dictionary<string, Flt64> { ["A"] = new Flt64(900.0) },
            OverLengthPenalty: new Dictionary<string, Flt64> { ["A"] = new Flt64(1.0) },
            OverLengthUpperBound: new Dictionary<string, Flt64> { ["A"] = new Flt64(300.0) });
        (_, LinearMetaModel<Flt64> model) = Build(config);

        LinearInequalityConstraint<Flt64> lower = Constraint(model, "assigned_length_lower_bound_0");
        lower.Inequality.Comparison.Should().Be(Comparison.GE, "the lower bound is a >= constraint");
        lower.Inequality.Rhs.Constant.ToDouble().Should().BeApproximately(10.0, 1e-9);

        LinearInequalityConstraint<Flt64> upper = Constraint(model, "assigned_length_upper_bound_0");
        upper.Inequality.Comparison.Should().Be(Comparison.LE, "the upper bound is a <= constraint");
        upper.Inequality.Rhs.Constant.ToDouble().Should().BeApproximately(900.0, 1e-9);

        LinearInequalityConstraint<Flt64> overBound = Constraint(model, "over_length_bound_0");
        overBound.Inequality.Comparison.Should().Be(Comparison.LE);
        overBound.Inequality.Rhs.Constant.ToDouble().Should().BeApproximately(300.0, 1e-9);

        LinearInequalityConstraint<Flt64> link = Constraint(model, "assigned_over_length_link_0");
        link.Inequality.Comparison.Should().Be(Comparison.LE);
        link.Inequality.Rhs.Constant.ToDouble().Should().BeApproximately(500.0, 1e-9);
    }

    /// <summary>**关联约束的系数必须是 `+1` 与 `-1`** / The link constraint's coefficients must be +1 and -1.</summary>
    /// <remarks>
    /// 这条**只钉系数**，因为方向与右端已由上一条覆盖。
    ///
    /// `assigned - over <= maxOver` 的 `-1` 若写成 `+1`，约束变成
    /// `assigned + over <= maxOver`——**名字对、方向对、右端对，语义完全相反**：
    /// 它会把"超长量"变成"额外消耗"，**越超长越不可行**。
    ///
    /// 这是本批唯一能钉住它的判据。
    ///
    /// This pins **only the coefficients**, since the direction and right-hand side are covered above. Turning the `-1` in `assigned - over <= maxOver` into `+1` gives
    /// `assigned + over <= maxOver` — **right name, right direction, right right-hand side, opposite meaning**: it makes the over-length amount an extra cost, so the
    /// more you over-produce the less feasible it becomes. This is the only criterion that catches it.
    /// </remarks>
    [Fact]
    public void Invoke_ShouldEmitTheLinkConstraintWithPlusAndMinusOneCoefficients() {
        var config = new LengthAssignmentModelingConfig<Flt64>(
            DynamicProductIds: new HashSet<string> { "A" },
            OverLengthPenalty: new Dictionary<string, Flt64> { ["A"] = new Flt64(1.0) });
        (LengthAggregation length, LinearMetaModel<Flt64> model) = Build(config);

        LinearInequalityConstraint<Flt64> link = Constraint(model, "assigned_over_length_link_0");

        link.Inequality.Lhs.Monomials.Should().HaveCount(2);
        double assignedCoefficient = link.Inequality.Lhs.Monomials
            .Single(m => Equals(m.Symbol, length.AssignedLength[0])).Coefficient.ToDouble();
        double overCoefficient = link.Inequality.Lhs.Monomials
            .Single(m => Equals(m.Symbol, length.OverLength[0])).Coefficient.ToDouble();
        assignedCoefficient.Should().BeApproximately(
            1.0,
            1e-9,
            "the assigned length enters with a +1 coefficient");
        overCoefficient.Should().BeApproximately(
            -1.0,
            1e-9,
            "the over length enters with a -1 coefficient; a +1 would make over-production penalise feasibility instead of relaxing it");
    }

    /// <summary>**非动态产品不产生任何长度约束**——记录一致性，同时说明它是等价变异 / A non-dynamic product produces no length constraints — records consistency and names an equivalent mutant.</summary>
    /// <remarks>
    /// 少了这条，"**无条件给所有产品加约束**"的实现也会绿。
    ///
    /// **但实测发现：把 `if (!DynamicProductIds.Contains(...)) continue;` 整个删掉，
    /// 这条判据仍然全绿。** 原因是**双重保护**：非动态产品在 `LengthAggregation`
    /// 里就**没有变量**（`assignedVar` / `overVar` 都是 null），而四条约束
    /// **各自都要求对应的变量非 null**。所以守卫被删掉也不会多出任何约束。
    ///
    /// **这是等价变异，不是判据的疏漏。** 判据保留，用于记录"双重保护"这个事实——
    /// 它防止下一个人同时放宽两处（既让非动态产品有变量、又删掉守卫）。
    ///
    /// Without this, an implementation that **adds constraints for every product unconditionally** would also pass. **But measuring showed that deleting the
    /// `if (!DynamicProductIds.Contains(...)) continue;` guard entirely leaves this criterion green.** The reason is **double protection**: a non-dynamic product has
    /// **no variables** in `LengthAggregation` (`assignedVar` / `overVar` are both null), and each of the four constraints **requires its own variable to be non-null**.
    /// So removing the guard adds nothing. **This is an equivalent mutant, not a gap.** The criterion stays to record the double protection — it stops someone from
    /// relaxing both places at once.
    /// </remarks>
    [Fact]
    public void Invoke_ShouldEmitNothingForANonDynamicProduct() {
        var config = new LengthAssignmentModelingConfig<Flt64>(
            DynamicProductIds: new HashSet<string>(),
            AssignedLengthLowerBound: new Dictionary<string, Flt64> { ["A"] = new Flt64(10.0) },
            OverLengthPenalty: new Dictionary<string, Flt64> { ["A"] = new Flt64(1.0) });
        (_, LinearMetaModel<Flt64> model) = Build(config);

        model.Constraints
            .Select(c => c.Name)
            .Should().NotContain(
                name => name.Contains("assigned_length") || name.Contains("over_length"),
                "the product is not dynamic, so the pipeline skips it entirely");
    }
}
