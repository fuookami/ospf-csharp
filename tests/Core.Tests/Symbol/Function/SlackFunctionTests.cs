// 松弛变量函数的判据 / Criteria for the slack variable function.

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Symbol.Function;

/// <summary>
/// **松弛变量函数从空壳变成真函数** / The slack function goes from an empty stub to a real function.
/// </summary>
/// <remarks>
/// 原来的 `SlackFunction` 是个**空壳**：`Evaluate` 恒返回 `null`，`RegisterConstraints` 恒返回 `Ok`，
/// `HelperVariables` 是空数组。它出现在 `FunctionSymbolRegistrationTest` 的列表里，所以"注册通过"这条判据是绿的——
/// 绿在**什么都没做**上。这不是理论风险： 的 `estSlack` 正好要用它。
///
/// 三段结构各自能静默地错：
/// <list type="bullet">
/// <item>**两个方向都开**时只写两行下界 → 求解器可以把 `neg` 与 `pos` 一起抬高，约束全满足，
/// 事后读出的松弛是**假的**。必须用四行绝对值公式；</item>
/// <item>求值方向写反 → 仍然返回非负数，只查"非负"发现不了；</item>
/// <item>两个方向都关 → `require` 拒绝，这里抛 `ArgumentException`。</item>
/// </list>
///
/// The old `SlackFunction` was an **empty stub**: `Evaluate` always returned `null`, `RegisterConstraints` always `Ok`, `HelperVariables` empty. It appeared in
/// `FunctionSymbolRegistrationTest`'s list, so "registration passes" was green — green on **doing nothing**. That was not theoretical: the earlier `estSlack` needs exactly this.
/// </remarks>
public class SlackFunctionTests {
    private static LinearPolynomial<Flt64> Polynomial(params (double Coefficient, IVariableItem? Variable)[] terms) {
        LinearMonomial<Flt64>[] monomials = terms
            .Where(term => term.Variable is not null)
            .Select(term => new LinearMonomial<Flt64>(new Flt64(term.Coefficient), term.Variable!))
            .ToArray();
        double constant = terms.Where(term => term.Variable is null).Sum(term => term.Coefficient);
        return new LinearPolynomial<Flt64>(monomials, new Flt64(constant));
    }

    private static LinearPolynomial<Flt64> Constant(double value) =>
        new(Array.Empty<LinearMonomial<Flt64>>(), new Flt64(value));

    /// <summary>
    /// 把函数挂到元模型上并构建机制模型，返回约束名列表 / Attach the function to a meta model, build the mechanism model, and return the constraint names.
    /// </summary>
    /// <remarks>
    /// `RegisterConstraints` 要的是**机制模型**，不是元模型——直接传 `LinearMetaModel` 会编译失败，
    /// 这也正是"约束确实被写进去了"这件事必须走完整构建流程才看得见的原因。
    ///
    /// `RegisterConstraints` wants a **mechanism model**, not a meta model — passing a `LinearMetaModel` fails to compile, which is exactly why "the constraints really got written"
    /// is only visible through the full build.
    /// </remarks>
    private static List<string> BuildConstraintNames(SlackFunction<Flt64> function) {
        using var parent = new LinearMetaModel<Flt64>();
        foreach (LinearMonomial<Flt64> monomial in function.X.Monomials.Concat(function.Y.Monomials)) {
            if (monomial.Symbol is IVariableItem variable) {
                parent.Add(variable);
            }
        }
        parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk.Should().BeTrue();
        Fuookami.Ospf.Utils.Functional.Result<LinearMechanismModel<Flt64>, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>> builtResult =
            LinearMechanismModel<Flt64>.InvokeAsync(parent).GetAwaiter().GetResult();
        builtResult.IsOk.Should().BeTrue("the model must build: " + builtResult);
        using LinearMechanismModel<Flt64> built = builtResult.Value;
        return ((IMechanismModel<Flt64>)built).Constraints.Select(constraint => constraint.Name).ToList();
    }

    private static Dictionary<ISymbol, Flt64> Values(RealVar variable, double value) =>
        new() { [variable] = new Flt64(value) };

    // ===== 求值 =====

    /// <summary>**两个方向都开 → 绝对差** / Both directions → the absolute difference.</summary>
    /// <remarks>
    /// `x = 7, y = 3` → 4；`x = 3, y = 7` → 也是 4。第二个方向最容易漏测：只测一个方向的话，
    /// 把 `|x - y|` 写成 `x - y` 会给出 `-4`（能发现），但把 `Abs` 写成 `max(x-y, 0)` 会给出 `0`（**看起来像"没有偏差"**）。
    ///
    /// `x = 7, y = 3` → 4; `x = 3, y = 7` → also 4. The second direction is the one usually skipped: testing one direction would catch `x - y` giving `-4`, but writing
    /// `max(x - y, 0)` instead of `Abs` gives `0` — which **looks like "no deviation"**.
    /// </remarks>
    [Fact]
    public void Evaluate_ShouldBeTheAbsoluteDifferenceWhenBothDirectionsAreOpen() {
        var variable = new RealVar("x");
        var function = new SlackFunction<Flt64>(
            Polynomial((1.0, variable)), Constant(3.0), IIntoValue<Flt64>.Identity, name: "s");

        function.Evaluate(Values(variable, 7.0))!.Value.ToDouble().Should().BeApproximately(4.0, 1e-9);
        function.Evaluate(Values(variable, 3.0))!.Value.ToDouble().Should().BeApproximately(0.0, 1e-9);
        function.Evaluate(Values(variable, 0.0))!.Value.ToDouble().Should().BeApproximately(3.0, 1e-9,
            "x below y is still a 3-unit deviation");
    }

    /// <summary>**只有负松弛 → 只报"低于目标"的量** / Negative-only reports only the shortfall.</summary>
    /// <remarks>
    /// `x = 0, y = 3` → 3；`x = 7, y = 3` → **0**（超出目标不算负松弛）。
    /// 写反成 `max(x - y, 0)` 会让超出 4 单位，而 4 同样是个非负数。
    ///
    /// `x = 0, y = 3` → 3; `x = 7, y = 3` → **0** (exceeding the target is not negative slack). Inverted to `max(x - y, 0)` it reports 4 — also a non-negative number.
    /// </remarks>
    [Fact]
    public void Evaluate_ShouldReportOnlyTheShortfallWhenOnlyNegativeIsOpen() {
        var variable = new RealVar("x");
        var function = new SlackFunction<Flt64>(
            Polynomial((1.0, variable)), Constant(3.0), IIntoValue<Flt64>.Identity,
            withPositive: false, name: "s");

        function.Evaluate(Values(variable, 0.0))!.Value.ToDouble().Should().BeApproximately(3.0, 1e-9);
        function.Evaluate(Values(variable, 7.0))!.Value.ToDouble().Should().BeApproximately(0.0, 1e-9,
            "exceeding the target is not a negative slack");
    }

    /// <summary>**只有正松弛 → 只报"超出目标"的量** / Positive-only reports only the excess.</summary>
    [Fact]
    public void Evaluate_ShouldReportOnlyTheExcessWhenOnlyPositiveIsOpen() {
        var variable = new RealVar("x");
        var function = new SlackFunction<Flt64>(
            Polynomial((1.0, variable)), Constant(3.0), IIntoValue<Flt64>.Identity,
            withNegative: false, name: "s");

        function.Evaluate(Values(variable, 7.0))!.Value.ToDouble().Should().BeApproximately(4.0, 1e-9);
        function.Evaluate(Values(variable, 0.0))!.Value.ToDouble().Should().BeApproximately(0.0, 1e-9,
            "falling short of the target is not a positive slack");
    }

    /// <summary>**两个方向都关 → 拒绝** / Both directions off is rejected.</summary>
    [Fact]
    public void Construction_ShouldRejectASlackWithNoDirection() {
        var variable = new RealVar("x");

        Action act = () => _ = new SlackFunction<Flt64>(
            Polynomial((1.0, variable)), Constant(3.0), IIntoValue<Flt64>.Identity,
            withNegative: false, withPositive: false);

        act.Should().Throw<ArgumentException>(
            "a slack with no direction is a constant zero fake symbol");
    }

    // ===== 辅助变量 =====

    /// <summary>**辅助变量随方向增减** / The helper variables follow the directions.</summary>
    /// <remarks>
    /// 两个方向 → 三个（neg、pos、side）；一个方向 → 一个，**且没有 side**。
    /// 只数总数会让"两个方向也建了 side"或"单方向也建了 side"混过去——而 side 只属于四行公式。
    ///
    /// Both directions → three (neg, pos, side); one direction → one, **and no side**. Counting the total alone would let "side built for one direction too" through — and side
    /// belongs only to the four-row formulation.
    /// </remarks>
    [Fact]
    public void HelperVariables_ShouldDependOnTheDirections() {
        var variable = new RealVar("x");
        var both = new SlackFunction<Flt64>(
            Polynomial((1.0, variable)), Constant(3.0), IIntoValue<Flt64>.Identity, name: "both");
        var positive = new SlackFunction<Flt64>(
            Polynomial((1.0, variable)), Constant(3.0), IIntoValue<Flt64>.Identity,
            withNegative: false, name: "positive");

        both.HelperVariables.Should().HaveCount(3);
        both.HelperVariables.Select(item => item.Name)
            .Should().BeEquivalentTo(new[] { "both_neg", "both_pos", "both_side" });
        both.SideVar.Should().NotBeNull();

        positive.HelperVariables.Should().ContainSingle();
        positive.HelperVariables[0].Name.Should().Be("positive_pos");
        positive.SideVar.Should().BeNull("a one-directional slack needs no branch variable");
        positive.NegVar.Should().BeNull();
    }

    // ===== 约束 =====

    /// <summary>**两个方向都开 → 四行，且分支行带 Big-M** / Both directions → four rows, the branch rows carrying a Big-M.</summary>
    /// <remarks>
    /// 只写两行下界是这类公式最经典的空洞：`neg = pos = 5` 时 `neg + pos = 10 > |x - y| = 0`，
    /// 却满足 `result >= ±difference`。判据因此**要求四行**：两行下界 + 两行分支。
    ///
    /// Two lower rows only is the classic hole: with `neg = pos = 5`, `neg + pos = 10 > |x - y| = 0` while still satisfying `result >= ±difference`. The criterion therefore
    /// **requires four rows**: two lower bounds plus two branch rows.
    /// </remarks>
    [Fact]
    public void RegisterConstraints_ShouldAddFourRowsWhenBothDirectionsAreOpen() {
        var variable = new RealVar("x");
        var function = new SlackFunction<Flt64>(
            Polynomial((1.0, variable)), Constant(3.0), IIntoValue<Flt64>.Identity, name: "s");
        List<string> names = BuildConstraintNames(function);

        names.Should().Contain("s_abs_ge_difference");
        names.Should().Contain("s_abs_ge_negative_difference");
        names.Should().Contain("s_abs_branch_positive");
        names.Should().Contain("s_abs_branch_negative");
        names.Should().HaveCount(4, "the exact absolute-value formulation needs exactly four rows");
    }

    /// <summary>**只有一个方向且非阈值 → 一行等式** / One direction, non-threshold → a single equality.</summary>
    [Fact]
    public void RegisterConstraints_ShouldAddOneEqualityWhenOneDirectionIsOpen() {
        var variable = new RealVar("x");
        var function = new SlackFunction<Flt64>(
            Polynomial((1.0, variable)), Constant(3.0), IIntoValue<Flt64>.Identity,
            withNegative: false, name: "s");
        BuildConstraintNames(function).Should().ContainSingle("one direction needs one equality").Which.Should().Be("s");
    }

    /// <summary>**阈值模式 → 不加等式，只加单边行** / Threshold mode adds no equality, only one-sided rows.</summary>
    /// <remarks>
    /// 阈值模式下的等式会把松弛**钉死**成差值的绝对值，于是"偏差"不再是一个可选量。
    /// 判据看的是"没有等式行、只有单边行"。
    ///
    /// **分支顺序是绝对值公式优先**：两个方向都开时，即使 `threshold = true` 也走四行公式
    /// （`if (withNegative && withPositive && sideVar != null) … else if (!threshold) … else …`）。
    /// 沿用这个顺序，所以阈值模式只在**单方向**下才有区别——这条判据就用单方向。
    ///
    /// In threshold mode an equality would **pin** the slack to the absolute difference, making "deviation" a non-choice. The criterion looks for "no equality row, only one-sided rows".
 /// **The implementation checks the absolute-value branch first**: with both directions open, even `threshold = true` takes the four-row formulation. That order is copied, so
    /// threshold mode only differs for **one direction** — which is what this criterion uses.
    /// </remarks>
    [Fact]
    public void RegisterConstraints_ShouldAddOneSidedRowsInThresholdMode() {
        var variable = new RealVar("x");
        var function = new SlackFunction<Flt64>(
            Polynomial((1.0, variable)), Constant(3.0), IIntoValue<Flt64>.Identity,
            withNegative: false, threshold: true, name: "s");
        BuildConstraintNames(function).Should().ContainSingle("threshold mode adds one one-sided row per open direction")
            .Which.Should().Be("s_pos");
    }

    /// <summary>**两个方向都开时 `threshold` 不改变形状** / With both directions open, `threshold` changes nothing.</summary>
    /// <remarks>
    /// 这条钉的是**分支顺序**：写成"先判 `threshold`"会让两个方向 + 阈值时退化成两行单边约束，
    /// 于是 `neg` 与 `pos` 可以一起抬高——正是不该允许的那种松弛。
    ///
    /// This pins its **branch order**: checking `threshold` first would degrade "both directions + threshold" into two one-sided rows, letting `neg` and `pos` rise
    /// together — exactly the slack that must not be allowed.
    /// </remarks>
    [Fact]
    public void RegisterConstraints_ShouldIgnoreThresholdWhenBothDirectionsAreOpen() {
        var variable = new RealVar("x");
        var function = new SlackFunction<Flt64>(
            Polynomial((1.0, variable)), Constant(3.0), IIntoValue<Flt64>.Identity,
            threshold: true, name: "s");
        BuildConstraintNames(function).Should().HaveCount(4, "the absolute-value formulation wins over threshold mode");
    }

    /// <summary>**`constraint: false` → 不加任何约束** / `constraint: false` adds nothing.</summary>
    [Fact]
    public void RegisterConstraints_ShouldAddNothingWhenConstraintsAreOff() {
        var variable = new RealVar("x");
        var function = new SlackFunction<Flt64>(
            Polynomial((1.0, variable)), Constant(3.0), IIntoValue<Flt64>.Identity,
            constraint: false, name: "s");
        BuildConstraintNames(function).Should().BeEmpty();
    }
}
