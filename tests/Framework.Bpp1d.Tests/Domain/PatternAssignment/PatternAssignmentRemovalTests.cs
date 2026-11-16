// 删列与取解判据 / Criteria for column removal and solution extraction.

using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Framework.Bpp.Infrastructure;
using Fuookami.Ospf.Framework.Bpp1d.Domain.Item.Model;
using Fuookami.Ospf.Framework.Bpp1d.Domain.PatternAssignment;
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

namespace Fuookami.Ospf.Framework.Bpp1d.Tests.Domain.PatternAssignment;

/// <summary>
/// **删列与取解** / Column removal and solution extraction.
/// </summary>
/// <remarks>
/// 对应`removeColumns` 与 `extractSolution`。判据点出两处要害：
///
/// | 点 | 错法 | 后果 |
/// | --- | --- | --- |
/// | **删列顺序** | 先删目标项再刷新 | 刷新会把已删列**写回**表达式 |
/// | **非整数用量** | 四舍五入 | 一个 LP 松弛值被当成整数用量报出去 |
///
/// And `extractSolution`; the two failure modes are in the table.
/// </remarks>
public class PatternAssignmentRemovalTests {
    /// <summary>**删列后目标项真的消失** / Removing a column really drops its objective term.</summary>
    /// <remarks>
    /// 删列依赖 `removeObjectiveTerms(variables)`。曾把"清完目标为空"**读成"什么都没清"**，
    /// 因而判定该 API 无效、把 `removeColumns` 记为缺口。**实测证明它有效**（一个目标里两项，移除一项后只剩另一项）。
    /// 这条判据把那个结论钉住。
    ///
    /// The code relies on `removeObjectiveTerms(variables)`. **read "objective empty after clearing" as "nothing was cleared"** and wrongly recorded
    /// `removeColumns` as blocked. **Measurement shows it works** (two terms in one objective; removing one leaves the other). This criterion pins that conclusion.
    /// </remarks>
    [Fact]
    public void RemoveColumns_ShouldDropTheObjectiveTermOfTheRemovedColumn() {
        PatternAssignmentContext<Flt64> context = MakeContext();
        LinearMetaModel<Flt64> model = new();
        context.Register(new List<PackingPattern<Flt64>>(), model).IsOk.Should().BeTrue();
        context.AddColumns(
            new List<PackingPattern<Flt64>> { Pattern("B1", ("I1", 1)), Pattern("B1", ("I1", 2)) }, model)
            .IsOk.Should().BeTrue();

        string removedKey = Pattern("B1", ("I1", 1)).Key;
        UIntVar removedVariable = context.Aggregation.Variables[removedKey];

        int before = context.Aggregation.Objective!.Polynomial.Monomials.Count;
        Try result = context.RemoveColumns(new HashSet<string> { removedKey }, model);

        result.IsOk.Should().BeTrue(result.ToString());
        // **目标是单一来源（聚合符号），所以"目标项消失"就是符号内容被重建。**
        // 把逐列目标项删掉之后，这里断言的是符号本身的单项式条数。
        //
        // **The objective is a single source (the aggregate symbol), so "the term is gone" means the symbol was rebuilt.** After dropped the per-column objective
        // terms, this asserts the symbol's own monomial count.
        context.Aggregation.Objective!.Polynomial.Monomials.Count.Should().BeLessThan(
            before, "the removed column's objective term must be gone");
        context.Aggregation.Pool.Should().HaveCount(1);
        context.Aggregation.Variables.Should().HaveCount(1);
        // **变量本身必须从模型里移除。** 只清目标项不移变量，token 里会留下一个
        // "在目标里已经没有项、但仍然存在"的变量——下游读 token 时仍会看到它。
        //
        // **The variable itself must leave the model.** Clearing only the objective terms leaves a variable in the token table that **has no objective term yet still
        // exists**, and downstream token readers still see it.
        model.Tokens.Find(removedVariable).Should().BeNull("the variable must be removed from the model, not merely unpriced");
    }

    /// <summary>**删列后需求表达式按剩余列重建** / After removal the demand expression is rebuilt from the remaining columns.</summary>
    /// <remarks>
    /// **顺序错了这里就会露出来**：若先清目标项再刷新，刷新会把已删列写回表达式，需求项数仍然是 2。
    ///
    /// **The wrong order shows up here**: clearing objective terms before refreshing lets the refresh write the deleted column back, leaving two demand terms.
    /// </remarks>
    [Fact]
    public void RemoveColumns_ShouldRebuildTheDemandExpressionFromTheRemainingColumns() {
        PatternAssignmentContext<Flt64> context = MakeContext();
        LinearMetaModel<Flt64> model = new();
        context.Register(new List<PackingPattern<Flt64>>(), model).IsOk.Should().BeTrue();
        context.AddColumns(
            new List<PackingPattern<Flt64>> { Pattern("B1", ("I1", 1)), Pattern("B1", ("I1", 2)) }, model)
            .IsOk.Should().BeTrue();

        context.RemoveColumns(new HashSet<string> { Pattern("B1", ("I1", 1)).Key }, model).IsOk.Should().BeTrue();

        LinearExpressionSymbol demand = context.Aggregation.Demands["I1"];
        demand.Polynomial.Monomials.Should().HaveCount(1, "only one column remains in the pool");
        demand.Polynomial.Monomials[0].Coefficient.ToDouble().Should().BeApproximately(
            2.0, 1e-12, "the surviving column carries two pieces");
    }

    /// <summary>**未知键静默跳过，不报错** / An unknown key is skipped silently.</summary>
    /// <remarks>
    /// 定价循环每轮都可能重发候选，把"本来就不在池里"当成错误会让**每轮都失败**。
    ///
    /// A pricing loop may resend candidates every round; treating "not in the pool" as an error would **fail every round**.
    /// </remarks>
    [Fact]
    public void RemoveColumns_ShouldSkipAnUnknownKeyWithoutFailing() {
        PatternAssignmentContext<Flt64> context = MakeContext();
        LinearMetaModel<Flt64> model = new();
        context.Register(new List<PackingPattern<Flt64>>(), model).IsOk.Should().BeTrue();

        context.RemoveColumns(new HashSet<string> { "not-a-key" }, model).IsOk.Should().BeTrue();
        context.Aggregation.Pool.Should().BeEmpty();
    }

    /// <summary>**未注册就删列必须被拒** / Removing columns before registering must be rejected.</summary>
    [Fact]
    public void RemoveColumns_ShouldRejectAnUnregisteredContext() {
        PatternAssignmentContext<Flt64> context = MakeContext();
        LinearMetaModel<Flt64> model = new();

        context.RemoveColumns(new HashSet<string> { "anything" }, model).IsFailed.Should().BeTrue();
    }

    /// <summary>**只有正用量的列被报出** / Only columns with a positive usage are reported.</summary>
    [Fact]
    public void ExtractSolution_ShouldReportOnlyPositiveUsages() {
        PatternAssignmentContext<Flt64> context = MakeContext();
        LinearMetaModel<Flt64> model = new();
        context.Register(new List<PackingPattern<Flt64>>(), model).IsOk.Should().BeTrue();
        context.AddColumns(
            new List<PackingPattern<Flt64>> { Pattern("B1", ("I1", 2)), Pattern("B1", ("I1", 1)) }, model)
            .IsOk.Should().BeTrue();
        SetUsage(model, context, "B1", 2UL, 2.0);
        SetUsage(model, context, "B1", 1UL, 0.0);

        Result<IReadOnlyList<(PackingPattern<Flt64> Pattern, ulong Usage)>, ErrorCode, Error<ErrorCode>> result =
            context.ExtractSolution(model);

        result.IsOk.Should().BeTrue(result.ToString());
        IReadOnlyList<(PackingPattern<Flt64> Pattern, ulong Usage)> usages =
            ((Ok<IReadOnlyList<(PackingPattern<Flt64> Pattern, ulong Usage)>, ErrorCode, Error<ErrorCode>>)result).Value;
        usages.Should().HaveCount(1, "the zero-usage column is not reported");
        usages[0].Usage.Should().Be(2UL);
    }

    /// <summary>**非整数用量必须被拒** / A non-integer usage must be rejected.</summary>
    /// <remarks>
    /// 一个 LP 松弛值（`1.5`）被四舍五入成 2，就会把松弛解当成整数解报出去。
    ///
    /// Rounding an LP relaxation value (`1.5`) up to 2 reports a relaxation as an integer solution.
    /// </remarks>
    [Fact]
    public void ExtractSolution_ShouldRejectANonIntegerUsage() {
        PatternAssignmentContext<Flt64> context = MakeContext();
        LinearMetaModel<Flt64> model = new();
        context.Register(new List<PackingPattern<Flt64>>(), model).IsOk.Should().BeTrue();
        context.AddColumns(new List<PackingPattern<Flt64>> { Pattern("B1", ("I1", 2)) }, model).IsOk.Should().BeTrue();
        SetUsage(model, context, "B1", 2UL, 1.5);

        context.ExtractSolution(model).IsFailed.Should().BeTrue("1.5 is not an integer usage");
    }

    /// <summary>**容差内的浮点误差仍算整数** / Floating noise inside the tolerance still counts as an integer.</summary>
    /// <remarks>
    /// 求解器返回 `1.9999999` 必须算 2，否则**所有正常解都会被拒**。
    ///
    /// A solver's `1.9999999` must count as 2, or **every normal solution is rejected**.
    /// </remarks>
    [Fact]
    public void ExtractSolution_ShouldTolerateFloatingNoiseBelowOnePerMillion() {
        PatternAssignmentContext<Flt64> context = MakeContext();
        LinearMetaModel<Flt64> model = new();
        context.Register(new List<PackingPattern<Flt64>>(), model).IsOk.Should().BeTrue();
        context.AddColumns(new List<PackingPattern<Flt64>> { Pattern("B1", ("I1", 2)) }, model).IsOk.Should().BeTrue();
        SetUsage(model, context, "B1", 2UL, 1.9999999);

        Result<IReadOnlyList<(PackingPattern<Flt64> Pattern, ulong Usage)>, ErrorCode, Error<ErrorCode>> result =
            context.ExtractSolution(model);

        result.IsOk.Should().BeTrue("1.9999999 is inside the 1e-6 tolerance: " + result.ToString());
        IReadOnlyList<(PackingPattern<Flt64> Pattern, ulong Usage)> usages =
            ((Ok<IReadOnlyList<(PackingPattern<Flt64> Pattern, ulong Usage)>, ErrorCode, Error<ErrorCode>>)result).Value;
        usages[0].Usage.Should().Be(2UL, "rounding keeps 1.9999999 at 2");
    }

    /// <summary>**超出容差必须被拒** / A deviation beyond the tolerance must be rejected.</summary>
    /// <remarks>
    /// 反向对照：容差写成 `1e-2` 时上一条仍会绿，这条把容差**钉在上界**。
    ///
    /// Reverse control: the previous case still passes with a `1e-2` tolerance; this one pins the **upper bound**.
    /// </remarks>
    [Fact]
    public void ExtractSolution_ShouldRejectADeviationBeyondTheTolerance() {
        PatternAssignmentContext<Flt64> context = MakeContext();
        LinearMetaModel<Flt64> model = new();
        context.Register(new List<PackingPattern<Flt64>>(), model).IsOk.Should().BeTrue();
        context.AddColumns(new List<PackingPattern<Flt64>> { Pattern("B1", ("I1", 2)) }, model).IsOk.Should().BeTrue();
        SetUsage(model, context, "B1", 2UL, 2.0001);

        context.ExtractSolution(model).IsFailed.Should().BeTrue("2.0001 is outside the 1e-6 tolerance");
    }

    /// <summary>**负用量必须被拒** / A negative usage must be rejected.</summary>
    [Fact]
    public void ExtractSolution_ShouldRejectANegativeUsage() {
        PatternAssignmentContext<Flt64> context = MakeContext();
        LinearMetaModel<Flt64> model = new();
        context.Register(new List<PackingPattern<Flt64>>(), model).IsOk.Should().BeTrue();
        context.AddColumns(new List<PackingPattern<Flt64>> { Pattern("B1", ("I1", 2)) }, model).IsOk.Should().BeTrue();
        SetUsage(model, context, "B1", 2UL, -1.0);

        context.ExtractSolution(model).IsFailed.Should().BeTrue("a negative usage would shrink the total");
    }

    /// <summary>**缺值返回 `DataNotFound`** / A missing value yields `DataNotFound`.</summary>
    [Fact]
    public void ExtractSolution_ShouldReportDataNotFoundWhenTheValueIsMissing() {
        PatternAssignmentContext<Flt64> context = MakeContext();
        LinearMetaModel<Flt64> model = new();
        context.Register(new List<PackingPattern<Flt64>>(), model).IsOk.Should().BeTrue();
        context.AddColumns(new List<PackingPattern<Flt64>> { Pattern("B1", ("I1", 2)) }, model).IsOk.Should().BeTrue();

        Result<IReadOnlyList<(PackingPattern<Flt64> Pattern, ulong Usage)>, ErrorCode, Error<ErrorCode>> result =
            context.ExtractSolution(model);

        result.IsFailed.Should().BeTrue();
        ((Failed<IReadOnlyList<(PackingPattern<Flt64> Pattern, ulong Usage)>, ErrorCode, Error<ErrorCode>>)result)
            .Error.Code.Should().Be(ErrorCode.DataNotFound);
    }

    /// <summary>写入一个列的用量 / Write one column's usage.</summary>
    /// <param name="model">元模型 / The meta model.</param>
    /// <param name="context">上下文 / The context.</param>
    /// <param name="binTypeId">箱型 / Bin type.</param>
    /// <param name="amount">件数 / Piece count.</param>
    /// <param name="value">用量 / The usage value.</param>
    private static void SetUsage(
        LinearMetaModel<Flt64> model,
        PatternAssignmentContext<Flt64> context,
        string binTypeId,
        ulong amount,
        double value) {
        UIntVar variable = context.Aggregation.Variables[Pattern(binTypeId, ("I1", amount)).Key];
        model.Tokens.Find(variable)!.SetResult(new Flt64(value));
    }

    /// <summary>构造上下文 / Build a context.</summary>
    /// <returns>上下文 / The context.</returns>
    private static PatternAssignmentContext<Flt64> MakeContext() {
        var problem = new PackingProblem<Flt64>(
            Items: new List<Item<Flt64>> { new("I1", Millimetres(100.0), Kilograms(1.0), 5UL) },
            BinTypes: new List<BinType<Flt64>> {
                new("B1", Millimetres(1000.0), Kilograms(10.0), Flt64.One, null),
            },
            Objective: PackingObjective.BinCount,
            Spacing: Millimetres(0.0));
        return new PatternAssignmentContext<Flt64>(problem, Flt64PackingValueAdapter.Instance);
    }

    /// <summary>构造方案 / Build a pattern.</summary>
    /// <param name="binTypeId">箱型 / Bin type.</param>
    /// <param name="amounts">件数 / Amounts.</param>
    /// <returns>方案 / The pattern.</returns>
    private static PackingPattern<Flt64> Pattern(string binTypeId, params (string Item, ulong Amount)[] amounts) {
        var map = new Dictionary<string, ulong>();
        foreach ((string item, ulong amount) in amounts) {
            map[item] = amount;
        }
        return new PackingPattern<Flt64>(binTypeId, map);
    }

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
