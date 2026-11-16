// 掩码函数的判据 / Criteria for the masking function.

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
/// **掩码函数从空壳变成真函数** / The masking function goes from an empty stub to a real function.
/// </summary>
/// <remarks>
/// 原来的 `MaskingFunction` / `MaskingWithPolyMaskFunction` 与 `SlackFunction` 同一个病：
/// `Evaluate` 恒 `null`、`RegisterConstraints` 恒 `Ok`、`HelperVariables` 为空数组，而它们出现在
/// `FunctionSymbolRegistrationTest` 的列表里，于是"注册通过"这条判据一直绿着。
///
/// 三种静默失效：
/// <list type="number">
/// <item>**四行少一行**：`y` 在某个 `mask` 值下自由浮动，目标会把它推到边界上，读回来"像样"；</item>
/// <item>**上下界取反**：`mask = 0` 时把 `y` 夹到 `[-upper, -lower]` 而不是 `[0, 0]`，解照样可行；</item>
/// <item>**先套掩码行再定义 `m`**：`m` 那时还没有定义，求解器给它任意值，掩码行形同虚设。</item>
/// </list>
///
/// The old `MaskingFunction` / `MaskingWithPolyMaskFunction` share the earlier `SlackFunction` disease: `Evaluate` always null, `RegisterConstraints` always Ok, empty
/// `HelperVariables`, and they sit in `FunctionSymbolRegistrationTest`'s list so "registration passes" stays green. Three silent failures: (1) **one row short** leaves `y` free at
/// some mask value and the objective pushes it to a bound, reading back plausibly; (2) **inverted bounds** clamp `y` to `[-upper, -lower]` instead of `[0, 0]` when `mask = 0` and
/// the solution is still feasible; (3) **masking rows before defining `m`** leaves `m` undefined at that point, the solver sets it freely and the rows are decorative.
/// </remarks>
public class MaskingFunctionTests {
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

    private static List<string> BuildConstraintNames(IMathFunctionSymbol<Flt64> function, params IVariableItem[] inputs) {
        using var parent = new LinearMetaModel<Flt64>();
        foreach (IVariableItem input in inputs) {
            parent.Add(input);
        }
        parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk.Should().BeTrue();
        Fuookami.Ospf.Utils.Functional.Result<LinearMechanismModel<Flt64>, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>> builtResult =
            LinearMechanismModel<Flt64>.InvokeAsync(parent).GetAwaiter().GetResult();
        builtResult.IsOk.Should().BeTrue("the model must build: " + builtResult);
        using LinearMechanismModel<Flt64> built = builtResult.Value;
        return ((IMechanismModel<Flt64>)built).Constraints.Select(constraint => constraint.Name).ToList();
    }

    // ===== 求值 =====

    /// <summary>**`mask = 1` → 输入值；`mask = 0` → 零** / Mask 1 gives the input, mask 0 gives zero.</summary>
    /// <remarks>
    /// 只测 `mask = 1` 会让"永远返回输入"通过；只测 `mask = 0` 会让"永远返回零"通过。两边都要。
    ///
    /// Testing only `mask = 1` lets "always return the input" through; only `mask = 0` lets "always return zero" through. Both sides are needed.
    /// </remarks>
    [Fact]
    public void Evaluate_ShouldFollowTheMask() {
        var input = new RealVar("x");
        var mask = new BinVar("m");
        var function = new MaskingFunction<Flt64>(
            Polynomial((1.0, input)), mask, IIntoValue<Flt64>.Identity, bigM: new Flt64(10));

        function.Evaluate(new Dictionary<ISymbol, Flt64> { [input] = new Flt64(7), [mask] = Flt64.One })!
            .Value.ToDouble().Should().BeApproximately(7.0, 1e-9);
        function.Evaluate(new Dictionary<ISymbol, Flt64> { [input] = new Flt64(7), [mask] = Flt64.Zero })!
            .Value.ToDouble().Should().BeApproximately(0.0, 1e-9);
    }

    /// <summary>**`mask` 读不到 → 零**（不是 `null`） / A missing mask yields zero, not null.</summary>
    /// <remarks>
    /// 把"掩码未知"变成"结果未知"会让调用方以为还要再算一轮，而掩码是决策变量——它在求值阶段读不到是正常的。
    ///
    /// Turning "mask unknown" into "result unknown" makes the caller think another round is needed, while the mask is a decision variable — being unreadable at evaluation time is
    /// normal.
    /// </remarks>
    [Fact]
    public void Evaluate_ShouldBeZeroWhenTheMaskIsMissing() {
        var input = new RealVar("x");
        var mask = new BinVar("m");
        var function = new MaskingFunction<Flt64>(
            Polynomial((1.0, input)), mask, IIntoValue<Flt64>.Identity, bigM: new Flt64(10));

        function.Evaluate(new Dictionary<ISymbol, Flt64> { [input] = new Flt64(7) })!
            .Value.ToDouble().Should().BeApproximately(0.0, 1e-9);
    }

    // ===== 辅助变量 =====

    /// <summary>**辅助变量是结果变量 + （多项式掩码时）内部的二值 `m`** / Helpers are the result variable, plus the internal binary `m` for the polynomial mask.</summary>
    /// <remarks>
    /// 多项式掩码必须**自己建一个二值变量**，不能直接拿多项式进 Big-M 行——那一行会失去整数性，
    /// 而模型仍然可解，只是解里 `mask = 0.5`。
    ///
    /// A polynomial mask needs **its own binary variable**; putting the polynomial straight into the Big-M rows loses integrality and the model still solves, only with `mask = 0.5`.
    /// </remarks>
    [Fact]
    public void HelperVariables_ShouldIncludeTheInternalMaskForAPolynomialMask() {
        var input = new RealVar("x");
        var mask = new BinVar("m");
        var simple = new MaskingFunction<Flt64>(
            Polynomial((1.0, input)), mask, IIntoValue<Flt64>.Identity, bigM: new Flt64(10), name: "simple");
        var polynomial = new MaskingWithPolyMaskFunction<Flt64>(
            Polynomial((1.0, input)), Polynomial((1.0, mask)), IIntoValue<Flt64>.Identity,
            bigM: new Flt64(10), name: "poly");

        simple.HelperVariables.Should().ContainSingle();
        simple.HelperVariables[0].Name.Should().Be("simple_masking");

        polynomial.HelperVariables.Should().HaveCount(2);
        polynomial.HelperVariables.Select(item => item.Name)
            .Should().BeEquivalentTo(new[] { "poly_mask_var", "poly_masking" });
    }

    // ===== 约束 =====

    /// <summary>**简单掩码 → 四行** / A simple mask produces four rows.</summary>
    /// <remarks>
    /// 只写两行"mask=0 时 y=0"会让 `mask=1` 时 `y` 自由；只写两行"mask=1 时 y=x"会让 `mask=0` 时 `y` 自由。
    /// 四行缺一不可，所以判据要求**四行且点名**。
    ///
    /// Writing only the two "y = 0 when mask = 0" rows leaves `y` free at `mask = 1`; only the two "y = x when mask = 1" rows leaves it free at `mask = 0`. All four are required,
    /// so the criterion demands **four, by name**.
    /// </remarks>
    [Fact]
    public void RegisterConstraints_ShouldAddFourRowsForASimpleMask() {
        var input = new RealVar("x");
        var mask = new BinVar("m");
        var function = new MaskingFunction<Flt64>(
            Polynomial((1.0, input)), mask, IIntoValue<Flt64>.Identity,
            bigM: new Flt64(10), name: "s");

        List<string> names = BuildConstraintNames(function, input, mask);

        names.Should().Contain("s_masking_zero_ub");
        names.Should().Contain("s_masking_zero_lb");
        names.Should().Contain("s_masking_eq_ub");
        names.Should().Contain("s_masking_eq_lb");
        names.Should().HaveCount(4, "the masking formulation needs exactly four rows");
    }

    /// <summary>**多项式掩码 → 一行定义 + 四行掩码** / A polynomial mask gives one definition row plus the four masking rows.</summary>
    /// <remarks>
    /// 定义行必须在前面：后面的掩码行里出现的 `m` 若还没被定义，求解器可以给它任意值。
    /// 判据既查**数量**（5 行）也查**顺序**（定义行第一）。
    ///
    /// The definition row must come first: an undefined `m` in the masking rows lets the solver choose it freely. The criterion checks both the **count** (five rows) and the
    /// **order** (the definition row first).
    /// </remarks>
    [Fact]
    public void RegisterConstraints_ShouldDefineTheMaskBeforeMasking() {
        var input = new RealVar("x");
        var mask = new BinVar("m");
        var function = new MaskingWithPolyMaskFunction<Flt64>(
            Polynomial((1.0, input)), Polynomial((1.0, mask)), IIntoValue<Flt64>.Identity,
            bigM: new Flt64(10), name: "p");

        List<string> names = BuildConstraintNames(function, input, mask);

        names.Should().HaveCount(5);
        names[0].Should().Be("p_mask_definition", "the mask must be defined before it is used");
        names.Should().Contain("p_masking_zero_ub");
        names.Should().Contain("p_masking_eq_lb");
    }

    /// <summary>**边界取自输入的有限值域；`bigM` 只是兜底** / The bounds come from the input's finite range; `bigM` is only the fallback.</summary>
    /// <remarks>
    /// 顺序是 `bounds?.upper ?: bigM`——**有限值域优先**。判据给输入变量设一个明确的 `[-3, 3]`，
    /// 于是即使传 `bigM = 5`，边界也必须收成 3。反过来只测"没传 bigM 时用 1e6"会让"永远用 bigM"通过，
    /// 而那条路会让 `mask = 1` 时的 `y = x` 约束在 `y` 超出 `bigM` 时**不可行**——
    /// 报出来是"无解"，看起来像排不下而不是掩码写错。
    ///
    /// The code order is `bounds?.upper ?: bigM` — **finite range first**. The criterion gives the input an explicit `[-3, 3]`, so even with `bigM = 5` the bounds must shrink to 3.
    /// Testing only "1e6 when no bigM is passed" would let "always use bigM" pass, and that path makes "y = x when mask = 1" **infeasible** once `y` exceeds `bigM` — reported as
    /// "no solution", looking like an unschedulable plan rather than a wrong mask.
    /// </remarks>
    [Fact]
    public void Bounds_ShouldPreferTheInputRangeOverTheExplicitBigM() {
        var input = new RealVar("x");
        input.Range.Geq(new Flt64(-3.0));
        input.Range.Leq(new Flt64(3.0));
        var mask = new BinVar("m");
        var function = new MaskingFunction<Flt64>(
            Polynomial((1.0, input)), mask, IIntoValue<Flt64>.Identity, bigM: new Flt64(5), name: "bounded");
        // 另起一个**没设过值域**的变量：同一个变量上面已经被夹到 `[-3, 3]` 了。
        // A **fresh** variable with no range set: the one above is already clamped to `[-3, 3]`.
        var openInput = new RealVar("y");
        var unbounded = new MaskingFunction<Flt64>(
            Polynomial((1.0, openInput)), new BinVar("m2"), IIntoValue<Flt64>.Identity, name: "unbounded");

        function.Bounds.Lower.ToDouble().Should().BeApproximately(-3.0, 1e-9,
            "the input's own range wins over the explicit Big-M");
        function.Bounds.Upper.ToDouble().Should().BeApproximately(3.0, 1e-9);
        // 一个无界的输入推导不出有限边界，于是回退到1e6——**不是 1**。
        // An unbounded input yields no finite bounds, so it falls back to its 1e6 — **not 1**.
        (unbounded.Bounds.Upper.ToDouble() > 1e5).Should().BeTrue(
            "the fallback is the baseline's 1e6, not a constant 1: " + unbounded.Bounds.Upper.ToDouble());
    }

    /// <summary>**`InputSymbols` 含输入与掩码** / `InputSymbols` covers the input and the mask.</summary>
    /// <remarks>
    /// 漏掉掩码会让注册阶段不去检查它，于是掩码变量没注册也能构建模型——解里它取任意值。
    ///
    /// Omitting the mask lets registration skip checking it, so the model builds without the mask variable registered — and the mask takes any value in the solution.
    /// </remarks>
    [Fact]
    public void InputSymbols_ShouldCoverTheMask() {
        var input = new RealVar("x");
        var mask = new BinVar("m");
        var function = new MaskingFunction<Flt64>(
            Polynomial((1.0, input)), mask, IIntoValue<Flt64>.Identity, bigM: new Flt64(10));

        function.InputSymbols.Should().NotBeNull();
        function.InputSymbols!.Should().Contain(input);
        function.InputSymbols!.Should().Contain(mask);
    }
}
