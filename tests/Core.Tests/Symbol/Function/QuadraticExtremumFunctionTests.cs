#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Symbol.Function;

/// <summary>
/// `max min` 与 `min max` 不是同一个量 / `max min` and `min max` are different quantities.
/// </summary>
/// <remarks>
/// 对齐基线 `QuadraticMax.kt`、`QuadraticMaxMin.kt`、`QuadraticMinMax.kt` 补三个类。
///
/// **三者与 `QuadraticIf` 系列同形**：**桥接每个二次多项式，再交给已有的线性极值函数。**
/// **极值编码本身没有任何新增。**
///
/// **Same shape as the earlier `QuadraticIf` family**: **bridge each quadratic polynomial and hand them to the existing
/// linear extremum functions.** **No extremum encoding is new.**
/// </remarks>
public class QuadraticExtremumFunctionTests
{
    /// <summary>**`max` 取最大** / `max` takes the largest.</summary>
    [Fact]
    public void TheMax_ShouldTakeTheLargest()
    {
        QuadraticMaxFunction function = BuildMax();
        RealVar x = InputVariable(function);

        // 两个输入是 x 与 −x，所以 max(x, −x) = |x|。
        // The two inputs are x and -x, so max(x, -x) = |x|.
        function.Evaluate(Values(x, 3.0)).Should().Be(new Flt64(3.0), "max(3, -3) is 3");
        function.Evaluate(Values(x, -3.0)).Should().Be(new Flt64(3.0), "max(-3, 3) is 3");
        function.Evaluate(Values(x, 0.0)).Should().Be(Flt64.Zero, "both sides are zero at the origin");
    }

    /// <summary>
    /// **`max min` 与 `min max` 给出不同的值** / The two give different values.
    /// </summary>
    /// <remarks>
    /// **这是唯一能分开两者的判据**：**如果两者被写成同一个量，只测其中一个的实现会通过。**
    ///
    /// **两者的定义**：**`max min` 先取最小再取最大、`min max` 相反。**
    /// **对两个输入 `x` 与 `−x`**：
    /// **`min(x, −x) = −|x|`、`max(x, −x) = |x|`**，
    /// **所以 `max min = max(−|x|) = −|x|`、`min max = min(|x|) = |x|`。**
    /// **两者互为相反数——这个差别是符号级的，不会被舍入吞掉。**
    ///
    /// **This is the one criterion that separates them**: **if both were written as the same quantity, testing either
    /// one alone would pass.**
    ///
    /// **Definitions**: **`max min` minimises then maximises, `min max` the other way round.**
    /// **For the two inputs `x` and `−x`**: **`min(x, −x) = −|x|` and `max(x, −x) = |x|`**,
    /// **so `max min = −|x|` and `min max = |x|`.** **They are each other's negation — a sign-level difference that
    /// rounding cannot swallow.**
    /// </remarks>
    [Fact]
    public void TheMaxMinAndMinMax_ShouldDifferInSign()
    {
        QuadraticMaxMinFunction maxMin = BuildMaxMin();
        QuadraticMinMaxFunction minMax = BuildMinMax();
        RealVar x = InputVariable(maxMin);

        // x = 3：max min 给 −3，min max 给 3。
        // At x = 3: max min gives -3 and min max gives 3.
        maxMin.Evaluate(Values(x, 3.0)).Should().Be(
            new Flt64(-3.0), "max min of x and minus x is minus the absolute value");
        minMax.Evaluate(Values(x, 3.0)).Should().Be(
            new Flt64(3.0), "min max of x and minus x is the absolute value");
        maxMin.Evaluate(Values(x, 3.0)).Should().NotBe(
            minMax.Evaluate(Values(x, 3.0)),
            "a swap between the two would make them equal");
    }

    /// <summary>**桥接行真的被写进模型** / The bridge rows really reach the model.</summary>
    /// <remarks>
    /// **按行名逐条核对，而不是数总数**——**教训：`BeGreaterThan` 会被别的行满足。**
    ///
    /// **Check each row by name rather than counting** — **the earlier lesson: a `BeGreaterThan` is satisfied by other
    /// rows.**
    /// </remarks>
    [Fact]
    public async Task TheBridgeRows_ShouldReachTheModel()
    {
        QuadraticMaxFunction function = BuildMax();

        using var parent = new QuadraticMetaModel<Flt64>("quadratic-max");
        var declared = new List<IVariableItem>();
        foreach (ISymbol symbol in function.InputSymbols) {
            declared.Add((IVariableItem)symbol);
        }

        declared.AddRange(function.HelperVariables);
        parent.Add(declared);

        QuadraticMechanismModel<Flt64> model =
            (await QuadraticMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        function.RegisterConstraints(model);

        var names = new List<string>();
        foreach (QuadraticConstraintImpl<Flt64> row in
            (IList<QuadraticConstraintImpl<Flt64>>)((IAbstractQuadraticMechanismModel<Flt64>)model).Constraints) {
            names.Add(row.Name);
        }

        names.Should().Contain(
            n => n.EndsWith("_bridge_row_0", StringComparison.Ordinal),
            "the first bridge equality must be present");
        names.Should().Contain(
            n => n.EndsWith("_bridge_row_1", StringComparison.Ordinal),
            "the second bridge equality must be present");
    }

    /// <summary>
    /// **空输入补一个零，而不是拒绝** / An empty input gets a zero rather than a rejection.
    /// </summary>
    /// <remarks>
    /// **基线的 `inputs.ifEmpty { listOf(LinearPolynomial(emptyList, zero)) }`。**
    /// **代价是"空输入"与"输入一个常数 0"无法区分**——
    /// **而如果这里改成抛异常，基线的行为就变了，所以照抄。**
    ///
    /// **The baseline substitutes a zero.** **The cost is that "empty input" cannot be told from "one constant
    /// zero"** — **and throwing here would change the baseline's behaviour, so it is copied as is.**
    /// </remarks>
    [Fact]
    public void TheMax_ShouldSubstituteAZeroForAnEmptyInput()
    {
        var empty = new QuadraticMaxFunction(
            polynomials: Array.Empty<QuadraticPolynomial<Flt64>>(),
            converter: IIntoValue<Flt64>.Identity,
            name: "empty_max");

        empty.Bridges.Should().HaveCount(1, "the substituted zero needs a bridge too");
        empty.BridgeRows.Should().HaveCount(1);
        empty.Evaluate(new Dictionary<ISymbol, Flt64>()).Should().Be(
            Flt64.Zero, "a max over a substituted zero is zero");
    }

    /// <summary>**三个类各自的委托目标是不同的线性类** / Each delegates to a different linear class.</summary>
    /// <remarks>
    /// **`Inner` 的类型就是"委托给谁"的可观察证据**：
    /// **三者都是 `IMathFunctionSymbol`，所以只有具体类型能区分它们。**
    ///
    /// **The `Inner` type is the observable evidence of "delegated to what"**:
    /// **all three are `IMathFunctionSymbol`, so only the concrete type tells them apart.**
    /// </remarks>
    [Fact]
    public void TheThreeAdapters_ShouldDelegateToDifferentLinearClasses()
    {
        BuildMax().Inner.Should().BeOfType<MaxFunction<Flt64>>();
        BuildMaxMin().Inner.Should().BeOfType<MaxMinFunction<Flt64>>();
        BuildMinMax().Inner.Should().BeOfType<MinMaxFunction<Flt64>>();
    }

    /// <summary>**桥接变量是无界 `RealVar`** / The bridges are unbounded real variables.</summary>
    [Fact]
    public void TheBridges_ShouldBeUnboundedRealVariables()
    {
        QuadraticMaxFunction function = BuildMax();

        function.Bridges.Should().HaveCount(2);
        foreach (RealVar bridge in function.Bridges) {
            bridge.Should().NotBeOfType<URealVar>(
                "a bounded-below-zero bridge would clip the negative half of the input");
        }
    }

    private static QuadraticMaxFunction BuildMax() =>
        new(
            polynomials: Inputs(),
            converter: IIntoValue<Flt64>.Identity,
            name: "qmax");

    private static QuadraticMaxMinFunction BuildMaxMin() =>
        new(
            polynomials: Inputs(),
            converter: IIntoValue<Flt64>.Identity,
            name: "qmaxmin");

    private static QuadraticMinMaxFunction BuildMinMax() =>
        new(
            polynomials: Inputs(),
            converter: IIntoValue<Flt64>.Identity,
            name: "qminmax");

    /// <summary>两个输入：`x` 与 `−x` / Two inputs: `x` and `−x`.</summary>
    private static IReadOnlyList<QuadraticPolynomial<Flt64>> Inputs() => new[] {
        new QuadraticPolynomial<Flt64>(
            new[] { QuadraticMonomial<Flt64>.Linear(Flt64.One, SharedVariable) }, Flt64.Zero),
        new QuadraticPolynomial<Flt64>(
            new[] { QuadraticMonomial<Flt64>.Linear(Flt64.One.Negate(), SharedVariable) }, Flt64.Zero),
    };

    /// <summary>共享的变量实例 / The shared variable instance.</summary>
    /// <remarks>
    /// **必须是同一个实例**：**每次新建 `RealVar` 会让多项式与取值字典的键不是同一个对象，求值恒返回 `null`。**
    /// **这是本方向第四次强调这一点。**
    ///
    /// **One instance only**: **a fresh `RealVar` per call leaves the polynomial and the value dictionary keyed by
    /// different objects, so evaluation returns `null`.** **The fourth time this has been spelled out.**
    /// </remarks>
    private static readonly RealVar SharedVariable = CreateSharedVariable();

    private static RealVar CreateSharedVariable() {
        var variable = new RealVar("qext-x");
        variable.Range.Geq(new Flt64(-10.0));
        variable.Range.Leq(new Flt64(10.0));
        return variable;
    }

    private static RealVar InputVariable(QuadraticMaxFunction function) =>
        (RealVar)function.Polynomials[0].Monomials[0].Symbol1;

    private static RealVar InputVariable(QuadraticMaxMinFunction function) =>
        (RealVar)function.Polynomials[0].Monomials[0].Symbol1;

    private static IReadOnlyDictionary<ISymbol, Flt64> Values(RealVar variable, double value) =>
        new Dictionary<ISymbol, Flt64> { [variable] = new Flt64(value) };
}
