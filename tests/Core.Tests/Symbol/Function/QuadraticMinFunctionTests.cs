#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Symbol.Function;

/// <summary>
/// 空壳比缺失更危险 / A stub is more dangerous than an absence.
/// </summary>
/// <remarks>
/// B1157 把 `QuadraticMinFunction` 从空壳换成真实编码（基线 `QuadraticMin.kt`）。
///
/// **在此之前它是空壳**：**`Evaluate` 恒返回 `null`、`RegisterConstraints` 什么都不做。**
/// **它能编译、有名字、能进注册表，所以"类型存在"的扫描把它算作已覆盖——而它对任何模型都没有作用。**
///
/// B1157 replaces `QuadraticMinFunction`'s stub with the real encoding.
///
/// **Until now it was a stub**: **`Evaluate` always returned `null` and `RegisterConstraints` did nothing.**
/// **It compiled, had a name and could be registered, so a "does the type exist" scan counted it as covered — while
/// it had no effect on any model.**
/// </remarks>
public class QuadraticMinFunctionTests
{
    /// <summary>**求值真的取最小值** / Evaluation really takes the minimum.</summary>
    /// <remarks>
    /// **空壳恒返回 `null`，所以这条判据在空壳上就红**——**它是"空壳变实体"最直接的一条。**
    ///
    /// **The stub always returned `null`, so this criterion is red on the stub** — **it is the most direct check that
    /// the stub became real.**
    /// </remarks>
    [Fact]
    public void TheEvaluate_ShouldTakeTheMinimum()
    {
        QuadraticMinFunction function = Build();
        var x = (RealVar)function.Polynomials[0].Monomials[0].Symbol1;

        function.Evaluate(Values(x, 3.0)).Should().Be(
            new Flt64(3.0), "min(x, 10) at x = 3 is 3");
        function.Evaluate(Values(x, 15.0)).Should().Be(
            new Flt64(10.0), "min(x, 10) at x = 15 is 10");
        function.Evaluate(Values(x, 10.0)).Should().Be(
            new Flt64(10.0), "the tie gives the same value");
    }

    /// <summary>**`Σuᵢ = 1`** / The one-hot row sums the selectors to one.</summary>
    /// <remarks>
    /// **没有它，多个 `uᵢ` 可同时为 1，于是 `y ≥ pᵢ` 对所有 i 同时成立，`y` 被迫 ≥ max 而不是 min。**
    ///
    /// **Without it several `uᵢ` can be one at once, so `y ≥ pᵢ` holds for every i and `y` is forced to at least the
    /// max rather than the min.**
    /// </remarks>
    [Fact]
    public void TheOneHotRow_ShouldSumToExactlyOne()
    {
        QuadraticMinFunction function = Build();
        LinearInequality<Flt64> row = function.OneHotRow();

        row.Comparison.Should().Be(Comparison.EQ);
        row.Lhs.Monomials.Should().HaveCount(2, "one term per entry");
        row.Rhs.Constant.ToDouble().Should().BeApproximately(1.0, 1e-12);

        foreach (LinearMonomial<Flt64> monomial in row.Lhs.Monomials) {
            monomial.Coefficient.ToDouble().Should().BeApproximately(1.0, 1e-12);
        }
    }

    /// <summary>**下界行逐项存在，且 `result` 系数为 +1、该项系数取反** / The lower rows are per entry.</summary>
    [Fact]
    public void TheLowerRows_ShouldNegateEachEntryAndAddTheResult()
    {
        QuadraticMinFunction function = Build();
        IReadOnlyList<QuadraticInequalityOf<Flt64>> rows = function.LowerRows();

        rows.Should().HaveCount(2);
        rows[0].Comparison.Should().Be(Comparison.LE);
        rows[0].Name.Should().EndWith("lb_0");
        rows[1].Name.Should().EndWith("lb_1");

        // 第一项是 x：取反后系数 -1，再加上 result 的 +1。
        // The first entry is x: negated to -1, plus +1 for result.
        rows[0].Lhs.Monomials[0].Coefficient.ToDouble().Should().BeApproximately(-1.0, 1e-12,
            "the entry is negated");
        rows[0].Lhs.Monomials[1].Coefficient.ToDouble().Should().BeApproximately(1.0, 1e-12,
            "the result is added");

        // 第二项是常数 10：取反后常数项 -10。
        // The second entry is the constant 10: negated to -10.
        rows[1].Lhs.Constant.ToDouble().Should().BeApproximately(-10.0, 1e-12);
    }

    /// <summary>**非精确模式不产生上界行，也不产生 `u`** / The inexact mode has no upper rows and no selectors.</summary>
    /// <remarks>
    /// **非精确模式下 `Σuᵢ = 1` 没有对应物，于是 `u` 不该出现在辅助变量表里——**
    /// **否则它们被注册却永远不被约束，成为求解器里的自由变量。**
    ///
    /// **With `exact = false` there is no `Σuᵢ = 1` to pair them with, so the `u` must not appear in the helper list
    /// either** — **otherwise they are registered and never constrained, becoming free variables in the solver.**
    /// </remarks>
    [Fact]
    public void TheInexactMode_ShouldHaveNoUpperRowsAndNoSelectors()
    {
        QuadraticMinFunction exact = Build();
        QuadraticMinFunction inexact = Build(exact: false);

        exact.BinVars.Should().HaveCount(2);
        exact.UpperRows().Should().HaveCount(2);
        exact.HelperVariables.Should().HaveCount(3, "the result plus two selectors");

        inexact.BinVars.Should().BeEmpty("the inexact mode has no selectors");
        inexact.UpperRows().Should().BeEmpty("the inexact mode has no upper rows");
        inexact.HelperVariables.Should().HaveCount(1, "only the result remains");
    }

    /// <summary>**大 M 是紧的，不是全局常数** / The big-M is per entry, not one global constant.</summary>
    /// <remarks>
    /// **`Mᵢ = pᵢ.upper − minLower`。** **两项的 `M` 必须不同**——**如果相等，说明用了同一个常数，
    /// 而"全局常数"会让界宽的项上界约束失效。**
    ///
    /// **`Mᵢ = pᵢ.upper − minLower`.** **The two entries' `M` must differ** — **equal values mean one shared constant,
    /// which leaves the wider entry's upper row unable to bind.**
    /// </remarks>
    [Fact]
    public void TheBigM_ShouldDifferPerEntry()
    {
        QuadraticMinFunction function = Build();
        IReadOnlyList<QuadraticInequalityOf<Flt64>> rows = function.UpperRows();

        // **`minLower = min(x.lower = 0, 10) = 0`**——**不是 10**。
        // 所以 M₀ = x.upper − 0 = 20，M₁ = 10 − 0 = 10。
        //
        // **`minLower = min(x.lower = 0, 10) = 0`**, not ten.
        // So M0 = x.upper - 0 = 20 and M1 = 10 - 0 = 10.
        double firstM = global::System.Math.Abs(SelectorCoefficient(rows[0], function, 0));
        double secondM = global::System.Math.Abs(SelectorCoefficient(rows[1], function, 1));

        firstM.Should().BeApproximately(20.0, 1e-12,
            "x's upper bound of twenty above the smallest lower bound of zero");
        secondM.Should().BeApproximately(10.0, 1e-12,
            "the constant's value of ten above the smallest lower bound of zero");
        firstM.Should().NotBe(secondM, "a shared constant would make these equal");
    }

    /// <summary>**显式大 M 必须为正** / An explicit big-M must be positive.</summary>
    [Fact]
    public void TheExplicitBigM_ShouldBePositive()
    {
        Action negative = () => _ = Build(bigM: -1.0);
        negative.Should().Throw<ArgumentException>("a negative big-M is not a big-M");

        Action zero = () => _ = Build(bigM: 0.0);
        zero.Should().Throw<ArgumentException>("a zero big-M degenerates the upper rows");
    }

    /// <summary>**空表被拒** / An empty entry list is rejected.</summary>
    [Fact]
    public void TheMin_ShouldRejectAnEmptyEntryList()
    {
        Action empty = () => _ = new QuadraticMinFunction(
            polynomials: Array.Empty<QuadraticPolynomial<Flt64>>(),
            converter: IIntoValue<Flt64>.Identity);
        empty.Should().Throw<ArgumentException>();
    }

    /// <summary>
    /// **注册真的把约束写进模型** / Registration actually writes the rows into the model.
    /// </summary>
    /// <remarks>
    /// **这条是被一次变异逼出来的**：**删掉 `Σuᵢ = 1` 的注册后，7 条判据全绿**——
    /// **因为其余判据都只看 `OneHotRow()` 的<b>构造</b>，而不看它有没有被<b>注册</b>。**
    ///
    /// **"构造出一个正确的行"与"这个行被交给模型"是两件事**，
    /// **而空壳的特征正是后者缺失：它照样能构造、照样编译。**
    ///
    /// **This criterion was forced by a mutation**: **dropping the registration of `Σuᵢ = 1` left all seven
    /// criteria green**, **because every other criterion looks only at the <b>construction</b> of `OneHotRow()`, not at
    /// whether it is <b>registered</b>.**
    ///
    /// **"A correct row is built" and "that row reaches the model" are two different things**,
    /// **and the missing second one is exactly what a stub looks like: it still builds and still compiles.**
    /// </remarks>
    [Fact]
    public async Task TheRegisterConstraints_ShouldActuallyWriteTheRows()
    {
        QuadraticMinFunction function = Build();
        var parent = new QuadraticMetaModel<Flt64>();
        // **辅助变量也要先声明**：`RegisterConstraints` 只写约束，不建 token。
        // **The helpers must be declared too**: `RegisterConstraints` writes rows, it does not create tokens.
        var declared = new List<IVariableItem> {
            (IVariableItem)function.Polynomials[0].Monomials[0].Symbol1,
        };
        declared.AddRange(function.HelperVariables);
        parent.Add(declared).IsOk.Should().BeTrue();

        QuadraticMechanismModel<Flt64> model =
            (await QuadraticMechanismModel<Flt64>.InvokeAsync(parent)).Value;

        int before = ((IAbstractQuadraticMechanismModel<Flt64>)model).Constraints.Count;
        function.RegisterConstraints(model).IsOk.Should().BeTrue();

        // 两项：下界 2 条 + 上界 2 条 + one-hot 1 条 = 5。
        // Two entries: two lower rows, two upper rows, one one-hot row.
        int after = ((IAbstractQuadraticMechanismModel<Flt64>)model).Constraints.Count;
        (after - before).Should().Be(
            5, "two lower rows, two upper rows and the one-hot row must all reach the model");
    }

    /// <summary>造一个 `min(x, 10)`，`x ∈ [0, 20]` / Build min(x, 10) with x in [0, 20].</summary>
    private static QuadraticMinFunction Build(
        bool exact = true, double? bigM = null) {
        var x = new RealVar("qmin-x");
        x.Range.Geq(Flt64.Zero);
        x.Range.Leq(new Flt64(20.0));

        return new QuadraticMinFunction(
            polynomials: new[] {
                new QuadraticPolynomial<Flt64>(
                    new[] { QuadraticMonomial<Flt64>.Linear(Flt64.One, x) }, Flt64.Zero),
                new QuadraticPolynomial<Flt64>(
                    Array.Empty<QuadraticMonomial<Flt64>>(), new Flt64(10.0)),
            },
            converter: IIntoValue<Flt64>.Identity,
            exact: exact,
            bigM: bigM is { } value ? new Flt64(value) : null,
            name: "qmin");
    }

    /// <summary>读一行里第 `slot` 个 selector 的系数 / Read the slot-th selector's coefficient.</summary>
    private static double SelectorCoefficient(
        QuadraticInequalityOf<Flt64> row, QuadraticMinFunction function, int slot) {
        foreach (QuadraticMonomial<Flt64> monomial in row.Lhs.Monomials) {
            if (ReferenceEquals(monomial.Symbol1, function.BinVars[slot]) && monomial.Symbol2 is null) {
                return monomial.Coefficient.ToDouble();
            }
        }

        throw new InvalidOperationException($"row {row.Name} has no selector {slot}");
    }

    private static IReadOnlyDictionary<ISymbol, Flt64> Values(RealVar variable, double value) =>
        new Dictionary<ISymbol, Flt64> { [variable] = new Flt64(value) };
}
