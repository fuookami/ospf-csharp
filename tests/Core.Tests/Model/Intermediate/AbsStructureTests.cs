#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using System;
using System.Collections.Generic;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Model.Intermediate;

/// <summary>
/// ABS 结构：裸变量必须是"一个 monomial、系数 1、常数 0" /
/// A bare variable is "one monomial, coefficient one, constant zero".
/// </summary>
/// <remarks>
/// 对齐的 `AbsStructure` 与 `AbsHelperBoundsSnapshot`。
///
/// **三条中少任何一条都必须返回 `null`，不能"尽力而为"。**
///
/// **Missing any of the three must yield `null`, never a best effort.**
/// </remarks>
public class AbsStructureTests
{
    /// <summary>
    /// **系数不是 1 时不给键** / A coefficient other than one yields no key.
    /// </summary>
    /// <remarks>
    /// **这是这里的核心判据。**
    ///
    /// **这个方法的结果被用来"按输入变量索引 helper"。**
    /// **如果系数是 2，那个 helper 属于 `2x` 而不是 `x`，
    /// 而两个不同的结构会被索引到同一个键上——后写入的那个悄悄覆盖前一个。**
    ///
    /// **覆盖不是错误：字典写入永远成功。它只是让一个 helper 消失。**
    ///
    /// **This is the batch's core criterion.**
    ///
    /// **The result indexes helpers <b>by input variable</b>.**
    /// **With coefficient two the helper belongs to `2x` rather than `x`, two different structures get the same key, and
    /// the later write silently overwrites the earlier one.**
    ///
    /// **An overwrite is not an error — a dictionary write always succeeds. It merely makes one helper vanish.**
    /// </remarks>
    [Fact]
    public void TheNonUnitCoefficient_ShouldYieldNoKey()
    {
        Structure(Poly(2.0, 0.0)).InputVariableKeyOrNull().Should().BeNull(
            "a coefficient of two is not a bare variable");
        Structure(Poly(-1.0, 0.0)).InputVariableKeyOrNull().Should().BeNull(
            "a coefficient of minus one is not a bare variable either");

        // **对照：系数 1、常数 0 时给出 `X` 的键。**
        // **Contrast: coefficient one with constant zero yields X's key.**
        Structure(Poly(1.0, 0.0)).InputVariableKeyOrNull().Should().Be(X.Key);
    }

    /// <summary>**常数不是 0 时不给键** / A non-zero constant yields no key.</summary>
    [Fact]
    public void TheNonZeroConstant_ShouldYieldNoKey()
    {
        Structure(Poly(1.0, 0.0)).InputVariableKeyOrNull().Should().NotBeNull();

        Structure(Poly(1.0, 3.0)).InputVariableKeyOrNull().Should().BeNull(
            "x + 3 is a shift, not a bare variable");
        Structure(Poly(1.0, -3.0)).InputVariableKeyOrNull().Should().BeNull();
    }

    /// <summary>**monomial 不是恰好一个时不给键** / Not exactly one monomial yields no key.</summary>
    /// <remarks>
    /// **零个与两个都测**：**零个是退化（常数），两个是表达式。**
    /// **只测一个的话，"用 `FirstOrDefault` 代替 `SingleOrDefault`"这类错误会漏掉——
    /// 它在零个时返回 `null`（碰巧对），在两个时返回第一个（错）。**
    ///
    /// **Both zero and two are tested**: **zero is degenerate (a constant) and two is an expression.**
    /// **Testing only one would miss "`FirstOrDefault` instead of `SingleOrDefault`" — it returns `null` for zero
    /// (accidentally right) and the first for two (wrong).**
    /// </remarks>
    [Fact]
    public void TheWrongMonomialCount_ShouldYieldNoKey()
    {
        // 零个 monomial：纯常数。
        // Zero monomials: a pure constant.
        var constant = new AbsStructure(
            Input: new LinearPolynomial<Flt64>(
                Array.Empty<LinearMonomial<Flt64>>(), Flt64.One),
            ResultVariable: Result, PositiveVariable: Positive, NegativeVariable: Negative,
            SignVariable: Sign, PositiveBigM: new Flt64(100.0), NegativeBigM: new Flt64(100.0),
            Converter: IIntoValue<Flt64>.Identity);

        constant.InputVariableKeyOrNull().Should().BeNull("a constant has no input variable");

        // 两个 monomial：表达式。
        // Two monomials: an expression.
        var sum = new AbsStructure(
            Input: new LinearPolynomial<Flt64>(
                new[] {
                    new LinearMonomial<Flt64>(Flt64.One, X),
                    new LinearMonomial<Flt64>(Flt64.One, Y),
                },
                Flt64.Zero),
            ResultVariable: Result, PositiveVariable: Positive, NegativeVariable: Negative,
            SignVariable: Sign, PositiveBigM: new Flt64(100.0), NegativeBigM: new Flt64(100.0),
            Converter: IIntoValue<Flt64>.Identity);

        sum.InputVariableKeyOrNull().Should().BeNull("x + y is not a bare variable");
    }

    /// <summary>
    /// **符号不是变量时不给键** / A non-variable symbol yields no key.
    /// </summary>
    /// <remarks>
    /// **`|sin(x)|` 的输入里面那个符号不是变量列——它是一个函数。
    /// 硬转型会抛，而抛出去会让整个建模失败；这里返回 `null`，
    /// 因为"输入不是裸变量"本来就是可预期的结果。**
    ///
    /// **The symbol inside `|sin(x)|` is not a variable column but a function. A hard cast would throw, and throwing
    /// fails the whole build; returning `null` is right because "the input is not a bare variable" is an expected
    /// outcome.**
    /// </remarks>
    [Fact]
    public void TheNonVariableSymbol_ShouldYieldNoKey()
    {
        var wrapped = new AbsStructure(
            Input: new LinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(Flt64.One, NonVariable.Instance) },
                Flt64.Zero),
            ResultVariable: Result, PositiveVariable: Positive, NegativeVariable: Negative,
            SignVariable: Sign, PositiveBigM: new Flt64(100.0), NegativeBigM: new Flt64(100.0),
            Converter: IIntoValue<Flt64>.Identity);

        wrapped.InputVariableKeyOrNull().Should().BeNull("the monomial's symbol is not a column");
    }

    /// <summary>**三个辅助列的顺序固定，且不含结果列** / The helpers are ordered and exclude the result.</summary>
    [Fact]
    public void TheHelperVariables_ShouldBeOrderedAndExcludeTheResult()
    {
        AbsStructure structure = Structure(Poly(1.0, 0.0));

        structure.HelperVariables.Should().HaveCount(3);
        structure.HelperVariables[0].Should().BeSameAs(Positive);
        structure.HelperVariables[1].Should().BeSameAs(Negative);
        structure.HelperVariables[2].Should().BeSameAs(Sign);

        foreach (IVariableItem helper in structure.HelperVariables) {
            helper.Should().NotBeSameAs(
                structure.ResultVariable, "the result belongs to the retained list, not the helper list");
        }
    }

    /// <summary>
    /// **`null` 界表示"没有界"，不是"界为零"** / A `null` bound means "no bound", not "zero".
    /// </summary>
    /// <remarks>
    /// **把快照写成非空 `Flt64` 会让"没有界"变成"界为零"。**
    /// **那会让 fallback 证明认为辅助变量只可能在 0 上——那是错的，且不会报错。**
    ///
    /// **A non-nullable snapshot turns "no bound" into "bound zero", and the fallback proof then believes a helper can
    /// only be zero — wrong, and silent.**
    /// </remarks>
    [Fact]
    public void TheBoundsSnapshot_ShouldKeepNullDistinctFromZero()
    {
        var absent = new AbsHelperBoundsSnapshot(Lower: null, Upper: null);
        var zero = new AbsHelperBoundsSnapshot(Fl64(0.0), Fl64(0.0));

        absent.Lower.Should().BeNull();
        absent.Upper.Should().BeNull();
        zero.Lower.Should().NotBeNull();
        absent.Should().NotBe(zero, "absent and zero are different facts");
    }

    /// <summary>**两个 Big-M 都按顺序给出** / Both Big-M values come out in order.</summary>
    [Fact]
    public void TheSolverBigM_ShouldKeepTheOrder()
    {
        AbsStructure structure = Structure(Poly(1.0, 0.0)) with {
            PositiveBigM = new Flt64(7.0),
            NegativeBigM = new Flt64(9.0),
        };

        (Flt64 Positive, Flt64 Negative)? values = structure.SolverBigMOrNull();

        values.Should().NotBeNull();
        values!.Value.Positive.ToDouble().Should().Be(7.0);
        values.Value.Negative.ToDouble().Should().Be(9.0);
    }

    /// <summary>
    /// **ABS 已经接入分派表** / ABS is wired into the dispatch table.
    /// </summary>
    /// <remarks>
    /// **记录了这条危险：漏加分支不报错，结构会静默报"没有结果列"。**
    /// **这里落地 `AbsStructure` 时同步加了分支，这条判据钉住它。**
    ///
    /// **This hazard is recorded: a missing branch is silent and the structure reports no result column.**
 /// ** added the branches together with the structure, and this criterion pins them.**
    /// </remarks>
    [Fact]
    public void TheAbsStructure_ShouldBeDispatched()
    {
        AbsStructure structure = Structure(Poly(1.0, 0.0));

        structure.ResultVariableOrNull().Should().BeSameAs(Result);
        structure.InputOrNull().Should().BeSameAs(structure.Input);
        structure.InputPolynomialsOrEmpty().Should().HaveCount(1);
        structure.HelperVariablesOrEmpty().Should().HaveCount(3);
        structure.SupportsDeferredFallback().Should().BeTrue();
    }

    private static Flt64 Fl64(double value) => new(value);

    private static LinearPolynomial<Flt64> Poly(double coefficient, double constant) =>
        new(new[] { new LinearMonomial<Flt64>(new Flt64(coefficient), X) }, new Flt64(constant));

    private static AbsStructure Structure(LinearPolynomial<Flt64> input) =>
        new(Input: input, ResultVariable: Result, PositiveVariable: Positive,
            NegativeVariable: Negative, SignVariable: Sign,
            PositiveBigM: new Flt64(100.0), NegativeBigM: new Flt64(100.0),
            Converter: IIntoValue<Flt64>.Identity);

    private static readonly RealVar X = new("abs-x");

    private static readonly RealVar Y = new("abs-y");

    private static readonly RealVar Result = new("abs-result");

    private static readonly RealVar Positive = new("abs-pos");

    private static readonly RealVar Negative = new("abs-neg");

    private static readonly BinVar Sign = new("abs-sign");

    /// <summary>一个不是变量列的符号 / A symbol that is not a column.</summary>
    private sealed class NonVariable : ISymbol
    {
        public static readonly NonVariable Instance = new();

        public string Name => "non-variable";

        public string? DisplayName => null;
    }

    // ===== 行生成 =====

    /// <summary>
    /// **四行，两条等式加两条互补的上界** / Four rows: two equalities and two complementary caps.
    /// </summary>
    [Fact]
    public void TheRows_ShouldBeFourWithTheExpectedNames()
    {
        IReadOnlyList<LinearInequality<Flt64>> rows =
            Structure(Poly(1.0, 0.0)).GenerateConstraints().Value;

        rows.Should().HaveCount(4);
        rows[0].Name.Should().Be("abs_abs_result");
        rows[1].Name.Should().Be("abs_abs_decompose");
        rows[2].Name.Should().Be("abs_abs_pos_ub");
        rows[3].Name.Should().Be("abs_abs_neg_ub");

        rows[0].Comparison.Should().Be(Comparison.EQ);
        rows[1].Comparison.Should().Be(Comparison.EQ);
        rows[2].Comparison.Should().Be(Comparison.LE);
        rows[3].Comparison.Should().Be(Comparison.LE);
    }

    /// <summary>
    /// **`sign = 1` 时结果等于输入** / At sign one the result equals the input.
    /// </summary>
    /// <remarks>
    /// **`x = 5`、`sign = 1` 时的正确解：`pos = 5`、`neg = 0`、`result = 5`。**
    ///
    /// **等式行与上界行都要检查：一条等式满足而另一条不满足的情形是可能的。**
    ///
    /// **With `x = 5` and `sign = 1`: `pos = 5`, `neg = 0`, `result = 5`.**
    ///
    /// **Both the equalities and the caps are checked: one equality can hold while the other does not.**
    /// </remarks>
    [Fact]
    public void ThePositiveSide_ShouldReproduceTheInput()
    {
        IReadOnlyList<LinearInequality<Flt64>> rows =
            Structure(Poly(1.0, 0.0)).GenerateConstraints().Value;

        double[] values = Residuals(rows, x: 5.0, pos: 5.0, neg: 0.0, sign: 1.0, result: 5.0);

        // **两条等式在紧点必须<b>恰为 0</b>；两条上界只需 `≤ 0`——
        // 上界在非紧时残差本来就小于 0，要求它等于 0 是把"可行"错当成"紧"。**
        //
        // **The two equalities must be <b>exactly zero</b> at a tight point; the two caps only need `≤ 0` — a cap
        // that is not binding has a residual below zero, and demanding zero confuses "feasible" with "binding".**
        values[0].Should().BeApproximately(0.0, 1e-9, "the result equality is tight");
        values[1].Should().BeApproximately(0.0, 1e-9, "the decompose equality is tight");
        values[2].Should().BeLessThanOrEqualTo(1e-9, "the pos cap is slack at pos = 5 under posM = 100");
        values[3].Should().BeLessThanOrEqualTo(1e-9, "the neg cap is tight at neg = 0 under sign = 1");

        Residuals(rows, x: 101.0, pos: 101.0, neg: 0.0, sign: 1.0, result: 101.0)[2]
            .Should().BeGreaterThan(0.0, "the pos cap is posM");
    }

    /// <summary>
    /// **`sign = 0` 时结果等于输入的相反数** / At sign zero the result is the input negated.
    /// </summary>
    /// <remarks>
    /// **`_abs_neg_ub` 是 `neg + negM·sign ≤ negM`：`sign = 0` 给出 `neg ≤ negM`、
    /// `sign = 1` 给出 `neg ≤ 0`。**
    ///
    /// **如果把右侧写成 `0`，`sign = 0` 时也压成 `neg ≤ 0`，
    /// 于是负输入完全无法表示。**
    ///
    /// **`_abs_neg_ub` is `neg + negM·sign ≤ negM`: `sign = 0` gives `neg ≤ negM` and `sign = 1` gives `neg ≤ 0`.**
    ///
    /// **With `0` on the right, `sign = 0` would also force `neg ≤ 0`, and negative inputs would become
    /// unrepresentable.**
    /// </remarks>
    [Fact]
    public void TheNegativeSide_ShouldReproduceTheAbsValue()
    {
        IReadOnlyList<LinearInequality<Flt64>> rows =
            Structure(Poly(1.0, 0.0)).GenerateConstraints().Value;

        double[] negative = Residuals(rows, x: -5.0, pos: 0.0, neg: 5.0, sign: 0.0, result: 5.0);
        negative[0].Should().BeApproximately(0.0, 1e-9);
        negative[1].Should().BeApproximately(0.0, 1e-9);
        negative[2].Should().BeLessThanOrEqualTo(1e-9);
        negative[3].Should().BeLessThanOrEqualTo(1e-9);

        double[] atCap = Residuals(rows, x: -100.0, pos: 0.0, neg: 100.0, sign: 0.0, result: 100.0);
        atCap[0].Should().BeApproximately(0.0, 1e-9);
        atCap[1].Should().BeApproximately(0.0, 1e-9);
        atCap[3].Should().BeApproximately(0.0, 1e-9, "neg = negM makes the cap binding");

        Residuals(rows, x: -101.0, pos: 0.0, neg: 101.0, sign: 0.0, result: 101.0)[3]
            .Should().BeGreaterThan(0.0, "the neg cap is negM");
    }

    /// <summary>
    /// **"两侧非负"是调用方的前提，不是这四行给的** /
    /// Nonnegativity is a caller premise, not something the four rows give.
    /// </summary>
    /// <remarks>
    /// **这是这里最需要写下来的一件事：四行里没有一行给 `pos ≥ 0` 或 `neg ≥ 0`。**
    ///
    /// **取 `x = 5`、`result = 3`，则 `pos = 4`、`neg = −1`：
    /// `neg ≤ 0` 成立、`pos = 4 ≤ 100` 成立、两条等式都成立。**
    ///
    /// **于是 `result = 3 ≠ |5| = 5`，而四行全满足。**
    ///
    /// **`CapturedHelperBounds` 存在的理由正是这个：辅助变量的域由调用方声明，
    /// 而这一层拿不到它。判据断言这个漏洞存在，而不是假装不存在。**
    ///
    /// **The thing most worth writing down: none of the four rows gives `pos ≥ 0` or `neg ≥ 0`.**
    ///
    /// **Take `x = 5`, `result = 3`, hence `pos = 4` and `neg = −1`: `neg ≤ 0` holds, `pos = 4 ≤ 100` holds, and both
    /// equalities hold.**
    ///
    /// **So `result = 3 ≠ |5| = 5` with all four rows satisfied.**
    ///
    /// **That is why `CapturedHelperBounds` exists: the helper domains are declared by the caller and this layer cannot see
    /// them. The criterion asserts the hole rather than pretending otherwise.**
    /// </remarks>
    [Fact]
    public void TheNonnegativity_ShouldBeACallerPremise()
    {
        IReadOnlyList<LinearInequality<Flt64>> rows =
            Structure(Poly(1.0, 0.0)).GenerateConstraints().Value;

        double[] hole = Residuals(rows, x: 5.0, pos: 4.0, neg: -1.0, sign: 1.0, result: 3.0);
        hole[0].Should().BeApproximately(0.0, 1e-9, "result = pos + neg = 3");
        hole[1].Should().BeApproximately(0.0, 1e-9, "input = pos - neg = 5");
        hole[2].Should().BeLessThanOrEqualTo(1e-9, "pos = 4 is under posM");
        hole[3].Should().BeLessThanOrEqualTo(1e-9, "neg = -1 satisfies neg <= 0 at sign = 1");
    }

    /// <summary>**四个键必须互不相同** / The four keys must be distinct.</summary>
    /// <remarks>
    /// **同一列扮演两个角色时，两条等式仍然有解，只是那个解不是 `abs`。**
    ///
    /// **One column playing two roles keeps both equalities solvable, just not as `abs`.**
    /// </remarks>
    [Fact]
    public void TheDuplicateKeys_ShouldBeRejected()
    {
        var duplicated = new AbsStructure(
            Input: Poly(1.0, 0.0), ResultVariable: Result,
            PositiveVariable: Positive, NegativeVariable: Positive, SignVariable: Sign,
            PositiveBigM: new Flt64(100.0), NegativeBigM: new Flt64(100.0),
            Converter: IIntoValue<Flt64>.Identity, Name: "abs");

        Result<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>> result =
            duplicated.GenerateConstraints();

        result.Should().BeOfType<
            Failed<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>>();
        ((Failed<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>)result)
            .Error.Message.Should().Contain("distinct keys");
    }

    /// <summary>
    /// **负的 Big-M 被拒，零被接受** / A negative Big-M is rejected; zero is accepted.
    /// </summary>
    /// <remarks>
    /// **与 `PiecewiseBigM` 要求显式 M 为正不同：那里的 M 是松弛量，为零就没有松弛；
    /// 这里的 M 是域的上界，为零意味着"这一侧不可能非零"——那是一个声明，不是错误。**
    ///
    /// **两侧都测，否则一个"要求严格正"的实现能通过负值一例。**
    ///
    /// **Different from `PiecewiseBigM`, which demands a positive explicit M: there the M is slack, and zero means no
    /// slack; here it is a domain cap, and zero is a legal statement.**
    ///
    /// **Both sides are tested, otherwise a strictly-positive implementation would pass the negative case.**
    /// </remarks>
    [Fact]
    public void TheBigM_ShouldRejectNegativeAndAcceptZero()
    {
        BigM(positive: -1.0).GenerateConstraints().Should()
            .BeOfType<Failed<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>>();
        BigM(negative: -1.0).GenerateConstraints().Should()
            .BeOfType<Failed<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>>();

        BigM(positive: 0.0).GenerateConstraints().Should()
            .BeOfType<Ok<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>>(
                "zero says this side cannot be nonzero, which is legal");
    }

    /// <summary>**不可用的输入值被拒** / An unusable input value is rejected.</summary>
    [Fact]
    public void TheUnusableInput_ShouldBeRejected()
    {
        var broken = new AbsStructure(
            Input: new LinearPolynomial<Flt64>(
                Array.Empty<LinearMonomial<Flt64>>(), new Flt64(double.NaN)),
            ResultVariable: Result, PositiveVariable: Positive, NegativeVariable: Negative,
            SignVariable: Sign, PositiveBigM: new Flt64(100.0),
            NegativeBigM: new Flt64(100.0),
            Converter: IIntoValue<Flt64>.Identity, Name: "abs");

        broken.GenerateConstraints().Should()
            .BeOfType<Failed<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>
    /// **ABS 现在能真正物化了** / ABS can now actually materialize.
    /// </summary>
    /// <remarks>
    /// **它是"未实现"的三格之一；这里补上，所以 4/3 的分法变成 5/2。**
    ///
    /// **It was one of the three "not implemented" cells; filling it, so the 4/3 split becomes 5/2.**
    /// </remarks>
    [Fact]
    public void TheAbs_ShouldNowMaterialize()
    {
        Result<DeferredFunctionFallbackConstraints, ErrorCode, Error<ErrorCode>> result =
            DeferredFunctionFallbackMaterializers.Core.Materialize(Structure(Poly(1.0, 0.0)));

        result.Should().BeOfType<
            Ok<DeferredFunctionFallbackConstraints, ErrorCode, Error<ErrorCode>>>();
        result.Value.Constraints.Should().HaveCount(4);
    }

    private static AbsStructure BigM(double positive = 100.0, double negative = 100.0) =>
        new(Input: Poly(1.0, 0.0), ResultVariable: Result, PositiveVariable: Positive,
            NegativeVariable: Negative, SignVariable: Sign,
            PositiveBigM: new Flt64(positive), NegativeBigM: new Flt64(negative),
            Converter: IIntoValue<Flt64>.Identity, Name: "abs");

    /// <summary>四行的残差：按 `lhs − rhs` 计算 / The four residuals, as lhs − rhs.</summary>
    private static double[] Residuals(
        IReadOnlyList<LinearInequality<Flt64>> rows,
        double x, double pos, double neg, double sign, double result) {
        double[] values = new double[rows.Count];
        for (int index = 0; index < rows.Count; ++index) {
            LinearInequality<Flt64> row = rows[index];
            double total = row.Lhs.Constant.ToDouble() - row.Rhs.Constant.ToDouble();

            foreach (LinearMonomial<Flt64> monomial in row.Lhs.Monomials) {
                total += monomial.Coefficient.ToDouble()
                    * Lookup(monomial.Symbol, x, pos, neg, sign, result);
            }

            foreach (LinearMonomial<Flt64> monomial in row.Rhs.Monomials) {
                total -= monomial.Coefficient.ToDouble()
                    * Lookup(monomial.Symbol, x, pos, neg, sign, result);
            }

            values[index] = total;
        }

        return values;
    }

    private static double Lookup(
        ISymbol symbol, double x, double pos, double neg, double sign, double result) {
        if (symbol.Equals(X)) { return x; }
        if (symbol.Equals(Positive)) { return pos; }
        if (symbol.Equals(Negative)) { return neg; }
        if (symbol.Equals(Sign)) { return sign; }
        if (symbol.Equals(Result)) { return result; }
        return 0.0;
    }
}
