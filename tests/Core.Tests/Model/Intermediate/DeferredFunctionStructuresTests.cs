#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using System;
using System.Collections.Generic;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Model.Intermediate;

/// <summary>
/// 标记接口的分派表：漏加分支不会编译失败 / A missing dispatch branch does not fail the build.
/// </summary>
/// <remarks>
/// 对齐基线 `FunctionExpansion.kt` 的五个 `DeferredFunctionStructure` 扩展函数与 `FunctionExpansionPolicy`。
///
/// **标记接口没有成员，所以所有具体信息都要靠类型分派取出来——代价就在这里。**
///
/// adds
///
/// **The marker has no members, so every concrete fact comes out of a type dispatch — and that is where the cost is.**
/// </remarks>
public class DeferredFunctionStructuresTests
{
    /// <summary>
    /// **未登记的结构返回 `null`/`false`，而不是抛异常** / An unregistered structure reports absent, not an error.
    /// </summary>
    /// <remarks>
    /// **这是本批的核心判据，也是本文件唯一的真实危险。**
    ///
    /// **给标记接口写一个新实现、却忘了在分派表里加分支，
    /// 不会编译失败：那个结构会静默地报"没有结果列"。**
    ///
    /// **调用方拿到 `null` 后通常走"不需要保留"的分支，于是模型少了一列——
    /// 而模型仍然可行、求解成功。**
    ///
    /// **判据用一个本批临时定义的结构去证实这个行为：**
    /// **它<b>不是</b>回归保护（漏加分支时它仍然全绿），而是把"这就是当前语义"写进测试，
    /// 让下一个加结构的人看见它还欠一个分支。**
    ///
    /// **This is the batch's core criterion and this file's one real hazard.**
    ///
    /// **Writing a new implementation of the marker and forgetting the dispatch branch does not fail the build: the
    /// structure silently reports "no result column".**
    ///
    /// **The caller takes the "no retention needed" path and the model ends up short one column — while staying feasible
    /// and solving successfully.**
    ///
    /// **The criterion uses a structure defined here to record the behaviour: it is <b>not</b> a regression guard (it
    /// stays green when a branch is missed), it puts "this is the current semantics" into a test so whoever adds a
    /// structure sees the branch still owed.**
    /// </remarks>
    [Fact]
    public void TheUnregisteredStructure_ShouldReportAbsent()
    {
        var unregistered = new UnregisteredStructure();

        unregistered.ResultVariableOrNull().Should().BeNull();
        unregistered.InputOrNull().Should().BeNull();
        unregistered.InputPolynomialsOrEmpty().Should().BeEmpty();
        unregistered.HelperVariablesOrEmpty().Should().BeEmpty();
        unregistered.SupportsDeferredFallback().Should().BeFalse();
    }

    /// <summary>**`IndicatorStructure` 报出自己的全部输入** / An indicator reports every input it carries.</summary>
    /// <remarks>
    /// **只报主输入的话，附加条件的变量范围就没人收集，而自动 Big-M 依赖这份清单——
    /// 于是 M 偏小，行会切掉合法解。**
    ///
    /// **Reporting only the main input leaves the attached conditions' ranges uncollected, and the automatic Big-M
    /// depends on this list — so M comes out too small and the rows cut off legal solutions.**
    /// </remarks>
    [Fact]
    public void TheIndicator_ShouldReportEveryAttachedInput()
    {
        IndicatorStructure plain = Structure("plain");

        plain.InputPolynomialsOrEmpty().Should().HaveCount(1);
        plain.InputPolynomialsOrEmpty()[0].Should().BeSameAs(plain.Input);

        IndicatorStructure rich = plain with {
            ConditionalValue = new ConditionalValue(
                Input: SecondInput, ResultVariable: ValueResult,
                Bounds: new ConditionBounds<Flt64>(new Flt64(-1.0), new Flt64(1.0)),
                Name: "cv"),
            ImpliedCondition = new ImpliedCondition(
                ThirdInput, ImpliedIndicator,
                new ConditionBounds<Flt64>(new Flt64(-1.0), new Flt64(1.0)), "ic"),
        };

        IReadOnlyList<LinearPolynomial<Flt64>> inputs = rich.InputPolynomialsOrEmpty();

        inputs.Should().HaveCount(3, "the main input plus the conditional value and the implication");
        inputs[0].Should().BeSameAs(plain.Input);
        inputs[1].Should().BeSameAs(SecondInput);
        inputs[2].Should().BeSameAs(ThirdInput);
    }

    /// <summary>
    /// **合取与差值的条件输入也要报出来** / The conjunction and difference condition inputs are included too.
    /// </summary>
    [Fact]
    public void TheConjunctionAndDifference_ShouldContributeTheirInputs()
    {
        IndicatorStructure plain = Structure("plain");
        IndicatorStructure left = Structure("left");
        IndicatorStructure right = Structure("right");

        IndicatorStructure conjoined = plain with {
            Conjunction = new ConjoinedIndicator(left, ConjunctionResult, "and"),
        };

        conjoined.InputPolynomialsOrEmpty().Should().HaveCount(2);
        conjoined.InputPolynomialsOrEmpty()[1].Should().BeSameAs(left.Input);

        IndicatorStructure differenced = plain with {
            Difference = new DifferenceIndicator(right, DifferenceResult, "diff"),
        };

        differenced.InputPolynomialsOrEmpty().Should().HaveCount(2);
        differenced.InputPolynomialsOrEmpty()[1].Should().BeSameAs(right.Input);
    }

    /// <summary>**指示结构报出结果列与延迟展开能力** / An indicator reports its result and deferred support.</summary>
    [Fact]
    public void TheIndicator_ShouldReportItsResultAndDeferredSupport()
    {
        IndicatorStructure plain = Structure("plain");

        plain.ResultVariableOrNull().Should().BeSameAs(plain.ResultVariable);
        plain.InputOrNull().Should().BeSameAs(plain.Input);
        plain.SupportsDeferredFallback().Should().BeTrue();
    }

    /// <summary>
    /// **`HelperVariablesOrEmpty` 对指示结构为空，避免与保留列重复** /
    /// The helper list is empty for an indicator, so it does not duplicate the retained columns.
    /// </summary>
    /// <remarks>
    /// **零带方向列、等价列、合取/差值结果都已经由 `RetainedResultVariables` 报出。**
    /// **如果这里再报一遍，同一个列会被登记两次——而"登记两次"在多数调用方那里
    /// 表现为重复行，不是错误。**
    ///
    /// **The zero-band side, the equivalents and the conjunction/difference results are already reported by
    /// `RetainedResultVariables`.** **Reporting them again registers one column twice — and most callers see that as
    /// duplicate rows rather than an error.**
    /// </remarks>
    [Fact]
    public void TheIndicator_ShouldNotRepeatItsRetainedColumnsAsHelpers()
    {
        IndicatorStructure rich = Structure("rich") with {
            EquivalentResults = new[] { Equivalent },
            ZeroBand = new ZeroBand<Flt64>(new Flt64(1.0), Side),
        };

        rich.HelperVariablesOrEmpty().Should().BeEmpty(
            "those columns already appear in the retained list");

        rich.RetainedResultVariables.Should().HaveCount(3);
    }

    /// <summary>**策略的默认值是立刻展开** / The policy defaults to eager.</summary>
    /// <remarks>
    /// **`default(FunctionExpansionPolicy)` 必须是 `Eager`。**
    /// **如果哪天有人把 `DeferredNativeFirst` 挪到首位，所有没显式赋值的调用点都会静默改成"先试原生"——
    /// 而那需要每个结构都有一份"我能不能原生"的判断，正是最容易写错的地方。**
    ///
    /// **`default(FunctionExpansionPolicy)` must be `Eager`.** **If someone moves `DeferredNativeFirst` to the front,
    /// every call site that does not assign explicitly silently switches to "try native first" — which needs a "can I go
    /// native" judgement per structure, exactly where things go wrong.**
    /// </remarks>
    [Fact]
    public void ThePolicyDefault_ShouldBeEager()
    {
        default(FunctionExpansionPolicy).Should().Be(FunctionExpansionPolicy.Eager);
        ((int)FunctionExpansionPolicy.Eager).Should().Be(0);
        Enum.GetValues<FunctionExpansionPolicy>().Should().HaveCount(3);
    }

    private static IndicatorStructure Structure(string name) =>
        new(Input: new LinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(Flt64.One, new RealVar($"x-{name}")) }, Flt64.Zero),
            ResultVariable: new BinVar($"r-{name}"), BigM: new Flt64(100.0),
            Tolerance: new Flt64(2.0), Converter: IIntoValue<Flt64>.Identity, Name: name);

    private static readonly LinearPolynomial<Flt64> SecondInput =
        new(new[] { new LinearMonomial<Flt64>(Flt64.One, new RealVar("x2")) }, Flt64.One);

    private static readonly LinearPolynomial<Flt64> ThirdInput =
        new(new[] { new LinearMonomial<Flt64>(Flt64.One, new RealVar("x3")) }, Flt64.Zero);

    private static readonly RealVar ValueResult = new("cv-value");

    private static readonly BinVar ImpliedIndicator = new("ic-ind");

    private static readonly BinVar Equivalent = new("eq");

    private static readonly BinVar Side = new("side");

    private static readonly BinVar ConjunctionResult = new("and-result");

    private static readonly BinVar DifferenceResult = new("diff-result");

    /// <summary>本批临时定义的结构，用来证实"未登记"的语义 / A structure defined here to prove the unregistered case.</summary>
    private sealed record UnregisteredStructure : DeferredFunctionStructure;
}
