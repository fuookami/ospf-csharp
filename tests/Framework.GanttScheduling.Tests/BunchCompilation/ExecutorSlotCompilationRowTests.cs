// 执行器-时隙编译约束真的加行/ The executor-slot compilation constraint really adds rows.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.BunchCompilation.Service.Limits;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Service.Limits;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Framework.GanttScheduling.Tests.BunchCompilation;
using Fuookami.Ospf.Framework.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.MultiArray;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;
using LinearCategory = Fuookami.Ospf.Math.Symbol.LinearCategory;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.BunchCompilation;

/// <summary>
/// **执行器-时隙编译约束真的加行** / The executor-slot compilation constraint really adds rows.
/// </summary>
/// <remarks>
/// 与 同一缺陷类，只是换了一条流水线：`Invoke` 以前**只校验、不加任何行**，`Extractor` 恒返回零，`Refresh` 是空 `Ok`。
/// `invoke` 是 `addConstraint(expression eq 1, args = ExecutorSlotCompilationShadowPriceKey(executor, slot))`——
/// "每个（执行器, 时隙）恰好编译一次"这条要求本身。少了它，那个执行器在那个时隙上可以一个任务都不接，排程看起来完全正常。
///
/// The same defect class as on a different pipeline: `Invoke` used to **validate only and add no row**, `Extractor` always returned zero, and `Refresh` was an empty `Ok`. Its
/// `invoke` is `addConstraint(expression eq 1, args = ExecutorSlotCompilationShadowPriceKey(executor, slot))` — the requirement itself, that **each (executor, slot) is compiled exactly once**.
/// Without it that executor can take no task in that slot while the schedule looks entirely normal.
/// </remarks>
public class ExecutorSlotCompilationRowTests {
    private static readonly DateTimeOffset Origin = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static TimeRange Slot(int from, int to) =>
        new(Origin.AddHours(from), Origin.AddHours(to));

    private static IReadOnlyList<LinearExpressionSymbol> Symbols(string name, int count) {
        var combination = new SymbolCombination<LinearExpressionSymbol, Shape1>(
            name, Shape1.Invoke(count), (_, _) => new LinearExpressionSymbol(
                new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero)));
        var symbols = new List<LinearExpressionSymbol>(count);
        for (int index = 0; index < count; ++index) {
            symbols.Add(combination[index]);
        }
        return symbols;
    }

    private static IReadOnlyList<MathConstraint> Rows(LinearMetaModel<Flt64> model, string prefix) =>
        ((IMetaModel<Flt64>)model).Constraints
            .Where(row => row.Name.StartsWith(prefix, StringComparison.Ordinal))
            .ToList();

    /// <summary>
    /// **每个（执行器, 时隙）一行 `x = 1`，且行上带键** / One `x = 1` row per (executor, slot), keyed.
    /// </summary>
    /// <remarks>
    /// 五点成对：
    ///
    /// 1. 行数**等于组合数**——少一行就有组合可以不被编译。
    /// 2. 每行左端**只有一个单项式、系数为 1**，符号就是该组合的表达式。
    /// 3. 比较方向是 **EQ**，右端常数 **1**。
    /// 4. 行名里带执行器与时隙——两维都得在，只带一维会让同名行互相覆盖。
    /// 5. 每行都带 `ExecutorSlotCompilationShadowPriceKey`，键里的执行器与时隙就是该行的组合。
    ///
    /// Five paired points: the **row count equals the pair count**, since a missing row lets a pair go uncompiled; each row's left side holds **one monomial with coefficient 1**, that symbol being
    /// the pair's expression; the comparison is **EQ** with right side **1**; the row name carries both the executor and the slot, and dropping either dimension would let same-named rows
    /// overwrite each other; and every row carries an `ExecutorSlotCompilationShadowPriceKey` naming that very pair.
    /// </remarks>
    [Fact]
    public void Invoke_ShouldAddOneEqualityRowPerPair() {
        var executors = new List<TestExecutor> { new("E1", "E1"), new("E2", "E2") };
        var slots = new List<TimeRange> { Slot(0, 8), Slot(8, 16) };
        IReadOnlyList<LinearExpressionSymbol> symbols = Symbols("executor_slot_x", executors.Count * slots.Count);
        var expressions = new Dictionary<(TestExecutor, TimeRange), object>();
        int index = 0;
        foreach (TestExecutor executor in executors) {
            foreach (TimeRange slot in slots) {
                expressions[(executor, slot)] = symbols[index++];
            }
        }

        var constraint = new ExecutorSlotCompilationConstraint<TestExecutor, TestAssignmentPolicy>(
            executors, slots, expressions);
        using var model = new LinearMetaModel<Flt64>();

        constraint.Invoke(model).IsOk.Should().BeTrue();

        IReadOnlyList<MathConstraint> rows = Rows(model, "executor_slot_compilation");
        rows.Should().HaveCount(executors.Count * slots.Count, "a pair without its row may go uncompiled");
        rows.Select(row => row.Name).Should().OnlyHaveUniqueItems("both dimensions belong in the name");

        foreach (MathConstraint row in rows) {
            var inequality = (LinearInequalityConstraint<Flt64>)row;
            LinearPolynomial<Flt64> left = inequality.Inequality.Lhs;
            left.Monomials.Should().ContainSingle("the row is `x = 1`, not a sum");
            left.Monomials[0].Coefficient.ToDouble().Should().Be(1.0);
            inequality.Inequality.Comparison.Should().Be(Comparison.EQ);
            inequality.Inequality.Rhs.Constant.ToDouble().Should().Be(1.0);

            row.Args.Should().BeOfType<ExecutorSlotCompilationShadowPriceKey<TestExecutor>>();
            var key = (ExecutorSlotCompilationShadowPriceKey<TestExecutor>)row.Args!;
            expressions.Should().ContainKey((key.Executor, key.Slot),
                "the key must name a real pair of this constraint");
        }

        // **每一行都用各自的符号**：全都用第一个会让所有组合的编译量绑在同一个变量上。
        // **Each row uses its own symbol**: using the first for all would tie every pair's compiled quantity to one variable.
        var used = rows
            .Select(row => (Fuookami.Ospf.Math.Symbol.ISymbol)((LinearInequalityConstraint<Flt64>)row).Inequality.Lhs.Monomials[0].Symbol)
            .ToList();
        used.Should().OnlyHaveUniqueItems();
    }

    /// <summary>
    /// **占位表达式不加行，但也不算失败** / A placeholder adds no row and is not a failure either.
    /// </summary>
    /// <remarks>
    /// 三点成对：表里是 `new object` 时返回 `Ok`（占位是 `CompilationTable.Build` 的当前正常状态）/ 一行都不加 /
    /// 换成真符号后同一个调用就建出真行。
    ///
    /// Three paired points: with `new object` in the table the call returns `Ok` (a placeholder is `CompilationTable.Build`'s normal state today), no row is added, and switching to real
    /// symbols makes the same call build real rows.
    /// </remarks>
    [Fact]
    public void Invoke_ShouldSkipAPlaceholderWithoutFailing() {
        var executors = new List<TestExecutor> { new("E1", "E1") };
        var slots = new List<TimeRange> { Slot(0, 8) };
        var placeholder = new Dictionary<(TestExecutor, TimeRange), object> {
            [(executors[0], slots[0])] = new object(),
        };
        var constraint = new ExecutorSlotCompilationConstraint<TestExecutor, TestAssignmentPolicy>(
            executors, slots, placeholder);
        using var model = new LinearMetaModel<Flt64>();

        constraint.Invoke(model).IsOk.Should().BeTrue("a placeholder is the normal state today");
        Rows(model, "executor_slot_compilation").Should().BeEmpty();

        var real = new Dictionary<(TestExecutor, TimeRange), object> {
            [(executors[0], slots[0])] = Symbols("executor_slot_x", 1)[0],
        };
        new ExecutorSlotCompilationConstraint<TestExecutor, TestAssignmentPolicy>(executors, slots, real)
            .Invoke(model).IsOk.Should().BeTrue();
        Rows(model, "executor_slot_compilation").Should().ContainSingle("real symbols do build the row");
    }

    /// <summary>
    /// **`Refresh` 把对偶按键写进映射** / `Refresh` writes the duals into the map by key.
    /// </summary>
    /// <remarks>
    /// 四点成对：
    ///
    /// 1. 本管线那一行给 5.5 → 映射里出现该键、值 5.5。
    /// 2. 别的键类型（`ExecutorCompilationShadowPriceKey`）给 99 → **不进**这张表。
    /// 3. 表里只有一条。
    /// 4. 再喂一条本管线的另一行 → 两条都在，前一条没被覆盖。**用不同的价**，同价被"认领"也看不出差别。
    ///
    /// Four paired points: giving this pipeline's row 5.5 puts that key in the map at 5.5; a foreign key type (`ExecutorCompilationShadowPriceKey`) at 99 does **not** enter this table; the map
    /// holds exactly one entry; and feeding a second row of this pipeline leaves both, without overwriting the first. **Use different prices** — a same-price claim would be invisible.
    /// </remarks>
    [Fact]
    public void Refresh_ShouldCollectDualsByKey() {
        var executors = new List<TestExecutor> { new("E1", "E1") };
        var slots = new List<TimeRange> { Slot(0, 8), Slot(8, 16) };
        IReadOnlyList<LinearExpressionSymbol> symbols = Symbols("executor_slot_x", 2);
        var expressions = new Dictionary<(TestExecutor, TimeRange), object> {
            [(executors[0], slots[0])] = symbols[0],
            [(executors[0], slots[1])] = symbols[1],
        };
        var constraint = new ExecutorSlotCompilationConstraint<TestExecutor, TestAssignmentPolicy>(
            executors, slots, expressions);
        using var model = new LinearMetaModel<Flt64>();
        constraint.Invoke(model).IsOk.Should().BeTrue();
        IReadOnlyList<MathConstraint> rows = Rows(model, "executor_slot_compilation");
        rows.Should().HaveCount(2);

        var foreignRow = new LinearInequalityConstraint<Flt64>(
            new LinearInequality<Flt64>(
                new LinearPolynomial<Flt64>(Array.Empty<Fuookami.Ospf.Math.Symbol.Monomial.LinearMonomial<Flt64>>(), Flt64.Zero),
                new LinearPolynomial<Flt64>(Array.Empty<Fuookami.Ospf.Math.Symbol.Monomial.LinearMonomial<Flt64>>(), Flt64.Zero),
                Comparison.LE,
                "foreign_row"),
            args: new ExecutorCompilationShadowPriceKey<TestExecutor>(executors[0]));

        var map = new GanttSchedulingShadowPriceMap<TestExecutor, TestAssignmentPolicy>();
        constraint.Refresh(map, model, Dual((rows[0], 5.5), (foreignRow, 99.0))).IsOk.Should().BeTrue();

        map.Map.Should().HaveCount(1, "a foreign key type is not this pipeline's business");
        map.Map[(ShadowPriceKey)rows[0].Args!].Price.ToDouble().Should().BeApproximately(5.5, 1e-9);

        constraint.Refresh(map, model, Dual((rows[1], 1.25))).IsOk.Should().BeTrue();
        map.Map.Should().HaveCount(2);
        map.Map[(ShadowPriceKey)rows[0].Args!].Price.ToDouble().Should().BeApproximately(5.5, 1e-9,
            "the first price must survive the second refresh untouched");
        map.Map[(ShadowPriceKey)rows[1].Args!].Price.ToDouble().Should().BeApproximately(1.25, 1e-9);
    }

    private static MetaDualSolution Dual(params (MathConstraint Row, double Price)[] rows) =>
        new(
            rows.ToDictionary(entry => entry.Row, entry => new Flt64(entry.Price)),
            new Dictionary<IIntermediateSymbol, IReadOnlyList<(IConstraint<Flt64, LinearCategory> Constraint, Flt64 Price)>>());
}
