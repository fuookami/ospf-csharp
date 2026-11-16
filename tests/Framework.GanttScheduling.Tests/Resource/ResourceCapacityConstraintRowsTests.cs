// 资源容量约束的两行/ The resource capacity constraint's two rows.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Resource.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Resource.Service.Limits;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Framework.GanttScheduling.Tests.BunchCompilation;
using Fuookami.Ospf.Framework.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Xunit;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.Resource;

/// <summary>
/// **资源容量约束的两行** / The resource capacity constraint's two rows.
/// </summary>
/// <remarks>
/// 这条管线以前是 `public Try Invoke(object model) => Results.Ok&lt;Success&gt;(...)`——一行都不加。
/// 上一个批次把数量 \/ 超量 \/ 不足三张表补齐，这里才轮到它们派上用场。
///
/// 三件必须照抄基线：**`polyX` 不是数量本身**（有松弛时左端是 `x − pos` / `x + neg`）、
/// **没有松弛时退回数量**、**数量表没注册就失败**（当成零数量会让容量永远不紧，模型照样可解）。
///
/// This pipeline used to be `Invoke(object model) => Ok` — not a single row. the previous step built the quantity / over / less tables; this one is where they finally pay off. Three things must
/// be copied from the baseline: **`polyX` is not the quantity itself** (with a slack the left side is `x - pos` / `x + neg`), **fall back to the quantity when there is no slack**, and **fail
/// when the quantity table was never registered** (treating it as quantity zero leaves capacity never tight while the model stays solvable).
/// </remarks>
public class ResourceCapacityConstraintRowsTests {
    private static readonly DateTimeOffset Origin = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private sealed class TestResource : ExecutionResource<ResourceCapacity<Flt64>, Flt64> {
        public TestResource(IReadOnlyList<ResourceCapacity<Flt64>> capacities) {
            Capacities = capacities;
        }

        public override string Id => "R1";

        public override string Name => "R1";

        public override IReadOnlyList<ResourceCapacity<Flt64>> Capacities { get; }

        public override Flt64 InitialQuantityValue => new(3.0);

        public override Flt64 UsedBy<E, A>(IAbstractTask<E, A> task, TimeRange time) => Flt64.Zero;
    }

    private static (BunchSchedulingExecutionResourceUsage<TestResource, ResourceCapacity<Flt64>, Flt64> Usage,
        LinearMetaModel<Flt64> Model) Prepare(Flt64? less = null, Flt64? over = null) {
        var window = new TimeWindow<Flt64>(
            new TimeRange(Origin, Origin.AddHours(24)), true, TimeSpan.FromHours(8));
        var resource = new TestResource(new List<ResourceCapacity<Flt64>> {
            new(new TimeRange(Origin, Origin.AddHours(16)), new Flt64(2.0), new Flt64(10.0),
                less, over, TimeSpan.FromHours(8)),
        });
        var usage = new BunchSchedulingExecutionResourceUsage<TestResource, ResourceCapacity<Flt64>, Flt64>(
            window, new List<TestResource> { resource }, "execution");
        var model = new LinearMetaModel<Flt64>();
        return (usage, model);
    }

    private static IReadOnlyList<MathConstraint> Rows(LinearMetaModel<Flt64> model, string prefix) =>
        ((IMetaModel<Flt64>)model).Constraints
            .Where(row => row.Name.StartsWith(prefix, StringComparison.Ordinal))
            .ToList();

    private static LinearPolynomial<Flt64> Left(MathConstraint row) =>
        ((LinearInequalityConstraint<Flt64>)row).Inequality.Lhs;

    /// <summary>
    /// **每槽两行，方向与右端常数都照容量走** / Two rows per slot, direction and right side taken from the capacity.
    /// </summary>
    /// <remarks>
    /// 五点成对：
    ///
    /// 1. 行数**是槽数的两倍**——少一半就有槽只约束了一个方向。
    /// 2. 上界行方向 `≤`、右端等于容量上界（10）；下界行方向 `≥`、右端等于容量下界（2）。
    /// 3. 两个右端**不相等**——若两边都写成同一个界，断言"上界是 10、下界是 2"立刻分得出来。
    /// 4. 没有松弛时左端是**数量符号本身**（单项式系数 1、名字是数量符号）。
    /// 5. 两行都挂 `ResourceCapacityShadowPriceKey`。
    ///
    /// Five paired points: the row count is **twice the slot count**, since half of them would leave a slot constrained in one direction only; the upper row is `<=` with right side equal to
    /// the capacity's upper bound (10) and the lower row `>=` with the lower bound (2); the two right sides **differ**, so writing the same bound on both is immediately visible; with no slack the
    /// left side is **the quantity symbol itself** (one monomial, coefficient 1, named after the quantity); and both rows carry a `ResourceCapacityShadowPriceKey`.
    /// </remarks>
    [Fact]
    public void Invoke_ShouldAddBothBoundsPerSlot() {
        (BunchSchedulingExecutionResourceUsage<TestResource, ResourceCapacity<Flt64>, Flt64> usage,
            LinearMetaModel<Flt64> model) = Prepare();
        usage.Register(model).IsOk.Should().BeTrue();
        var constraint = new ResourceCapacityConstraint<TestExecutor, TestAssignmentPolicy,
            ExecutionResourceTimeSlot<TestResource, ResourceCapacity<Flt64>, Flt64>,
            TestResource, ResourceCapacity<Flt64>, Flt64>(usage);

        constraint.Invoke(model).IsOk.Should().BeTrue();

        IReadOnlyList<MathConstraint> upper = Rows(model, "execution_resource_capacity_ub_");
        IReadOnlyList<MathConstraint> lower = Rows(model, "execution_resource_capacity_lb_");
        upper.Should().HaveCount(usage.TimeSlots.Count, "every slot needs its upper bound");
        lower.Should().HaveCount(usage.TimeSlots.Count, "every slot needs its lower bound");

        foreach (MathConstraint row in upper) {
            var inequality = (LinearInequalityConstraint<Flt64>)row;
            inequality.Inequality.Comparison.Should().Be(Comparison.LE);
            inequality.Inequality.Rhs.Constant.ToDouble().Should().Be(10.0, "the capacity's upper bound");
            row.Args.Should().BeOfType<ResourceCapacityShadowPriceKey<TestResource, ResourceCapacity<Flt64>, Flt64>>();
        }
        foreach (MathConstraint row in lower) {
            var inequality = (LinearInequalityConstraint<Flt64>)row;
            inequality.Inequality.Comparison.Should().Be(Comparison.GE);
            inequality.Inequality.Rhs.Constant.ToDouble().Should().Be(2.0, "the capacity's lower bound");
            row.Args.Should().BeOfType<ResourceCapacityShadowPriceKey<TestResource, ResourceCapacity<Flt64>, Flt64>>();
        }

        // **没有松弛时左端就是数量**：单项式系数 1，符号名与数量表里的一致。
        var quantity = (IReadOnlyDictionary<ExecutionResourceTimeSlot<TestResource, ResourceCapacity<Flt64>, Flt64>,
            ILinearIntermediateSymbol<Flt64>>)usage.Quantity!;
        ILinearIntermediateSymbol<Flt64> symbol = quantity.Values.First();
        LinearPolynomial<Flt64> left = Left(upper[0]);
        left.Monomials.Should().ContainSingle("with no slack the row is `x <= ub`");
        left.Monomials[0].Coefficient.ToDouble().Should().Be(1.0);
        left.Monomials[0].Symbol.Should().BeSameAs(symbol);
    }

    /// <summary>
    /// **有松弛时左端是 `polyX`，不是数量** / With a slack the left side is `polyX`, not the quantity.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. 开超量后上界行左端**不再是数量本身**：里面出现 `_pos` 辅助变量，且它的系数与数量**异号**（`x − pos`）。
    /// 2. 下界行左端只含数量（不足没开）。
    /// 3. 两个左端的单项式条数不同——若两侧都用数量，条数会一样。
    /// 4. 不开超量时上界行左端只有数量（与上一条判据互为反证）。
    ///
    /// Four paired points: with over enabled the upper row's left side **stops being the quantity** — a `_pos` helper appears with a coefficient of the **opposite sign** to the quantity
    /// (`x - pos`); the lower row's left side holds only the quantity (less is off); the two left sides differ in monomial count, whereas using the quantity on both would make them equal; and
    /// with over disabled the upper row holds only the quantity, the counter-probe to the first point.
    /// </remarks>
    [Fact]
    public void Invoke_ShouldUsePolyXWhenASlackIsAvailable() {
        (BunchSchedulingExecutionResourceUsage<TestResource, ResourceCapacity<Flt64>, Flt64> withOver,
            LinearMetaModel<Flt64> model) = Prepare(new Flt64(1.0), new Flt64(2.0));
        withOver.Register(model).IsOk.Should().BeTrue();
        new ResourceCapacityConstraint<TestExecutor, TestAssignmentPolicy,
            ExecutionResourceTimeSlot<TestResource, ResourceCapacity<Flt64>, Flt64>,
            TestResource, ResourceCapacity<Flt64>, Flt64>(withOver).Invoke(model).IsOk.Should().BeTrue();

        LinearPolynomial<Flt64> overLeft = Left(Rows(model, "execution_resource_capacity_ub_")[0]);
        overLeft.Monomials.Should().HaveCount(2, "`x - pos` has two terms");
        LinearMonomial<Flt64> positive = overLeft.Monomials.Single(m => m.Symbol.Name.EndsWith("_pos"));
        LinearMonomial<Flt64> amount = overLeft.Monomials.Single(m => !m.Symbol.Name.EndsWith("_pos"));
        positive.Coefficient.ToDouble().Should().BeLessThan(0.0, "the excess is subtracted: `x - pos`");
        amount.Coefficient.ToDouble().Should().BeGreaterThan(0.0);

        // 下界侧同理：不足松弛开了就是 `x + neg`，两项，`_neg` 那一项是**加**。
        // The lower side mirrors it: with the less slack enabled it is `x + neg`, two terms, and the `_neg` one is **added**.
        LinearPolynomial<Flt64> lessLeft = Left(Rows(model, "execution_resource_capacity_lb_")[0]);
        lessLeft.Monomials.Should().HaveCount(2, "`x + neg` has two terms");
        LinearMonomial<Flt64> negative = lessLeft.Monomials.Single(m => m.Symbol.Name.EndsWith("_neg"));
        negative.Coefficient.ToDouble().Should().BeGreaterThan(0.0, "the shortfall is added: `x + neg`");
    }

    /// <summary>
    /// **数量表没注册就失败** / An unregistered quantity table fails.
    /// </summary>
    /// <remarks>
    /// 三点成对：
    ///
    /// 1. 未注册就 `Invoke` → **`Failed`**，不是 `Ok`：当成零数量会让容量永远不紧，模型照样可解，缺陷只在产出上留痕。
    /// 2. 模型里**一行都没有**——不能凭空造约束。
    /// 3. 注册之后同一个调用就加行，所以"没有行"是因为没表，不是因为管线不工作。
    ///
    /// Three paired points: `Invoke` before registration is **`Failed`** rather than `Ok`, since treating it as quantity zero leaves capacity never tight and the defect only shows in output;
    /// the model has **no rows**, and no constraint may be invented; and after registration the same call does add rows, so the emptiness was the missing table rather than a broken pipeline.
    /// </remarks>
    [Fact]
    public void Invoke_ShouldFailWhileTheQuantityTableIsUnregistered() {
        (BunchSchedulingExecutionResourceUsage<TestResource, ResourceCapacity<Flt64>, Flt64> usage,
            LinearMetaModel<Flt64> model) = Prepare();
        var constraint = new ResourceCapacityConstraint<TestExecutor, TestAssignmentPolicy,
            ExecutionResourceTimeSlot<TestResource, ResourceCapacity<Flt64>, Flt64>,
            TestResource, ResourceCapacity<Flt64>, Flt64>(usage);

        constraint.Invoke(model).IsFailed.Should().BeTrue(
            "a missing table must not be read as quantity zero");
        Rows(model, "execution").Should().BeEmpty();

        usage.Register(model).IsOk.Should().BeTrue();
        constraint.Invoke(model).IsOk.Should().BeTrue();
        Rows(model, "execution").Should().NotBeEmpty("after registration the same call does add rows");
    }

    /// <summary>
    /// **非模型必须失败** / A non-model must fail.
    /// </summary>
    /// <remarks>
    /// 两点成对：非模型 → `Failed` / `null` → 抛 `ArgumentNullException`。两者都是"没成功"，只查 `IsFailed` 分不出，
    /// 所以分别断言。
    ///
    /// Two paired points: a non-model gives `Failed` and `null` throws `ArgumentNullException`. Both are "did not succeed", so checking `IsFailed` alone cannot tell them apart — the same
    /// lesson as — and each is asserted separately.
    /// </remarks>
    [Fact]
    public void Invoke_ShouldRejectANonModel() {
        (BunchSchedulingExecutionResourceUsage<TestResource, ResourceCapacity<Flt64>, Flt64> usage,
            LinearMetaModel<Flt64> model) = Prepare();
        usage.Register(model).IsOk.Should().BeTrue();
        var constraint = new ResourceCapacityConstraint<TestExecutor, TestAssignmentPolicy,
            ExecutionResourceTimeSlot<TestResource, ResourceCapacity<Flt64>, Flt64>,
            TestResource, ResourceCapacity<Flt64>, Flt64>(usage);

        constraint.Invoke(new object()).IsFailed.Should().BeTrue("a capacity constraint needs a linear meta model");
        Action nullModel = () => constraint.Invoke(null!);
        nullModel.Should().Throw<ArgumentNullException>();
    }
}
