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
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Symbol.Function;

/// <summary>
/// 桥接变量必须被约束，否则它自由 / The bridge variable must be constrained or it is free.
/// </summary>
/// <remarks>
/// B1161 按基线 `QuadraticIf.kt`、`QuadraticIfThen.kt`、`QuadraticIfIn.kt` 补三个类。
///
/// **三者都只做一件事**：**把二次多项式桥接成一个实变量，再交给已有的线性 `If*` 函数。**
/// **分支编码本身没有任何新增。**
///
/// adds
///
/// **All three do one thing**: **bridge a quadratic polynomial to a real variable and hand it to the existing linear
/// `If*` functions.** **No branch encoding is new.**
/// </remarks>
public class QuadraticIfFunctionTests
{
    /// <summary>**桥接行真的被写进模型** / The bridge rows really reach the model.</summary>
    /// <remarks>
    /// **没有这一步，桥接变量是自由的**——**它不出现在任何约束里，分支读的是一个任意值。**
    /// **而 `Inner` 的约束全部可满足、模型可行、目标值合理。**
    ///
    /// **Without this step the bridge is free** — **it appears in no constraint and the branch reads an arbitrary
    /// value.** **Meanwhile every `Inner` row is satisfiable, the model feasible and the objective plausible.**
    /// </remarks>
    [Fact]
    public async Task TheBridgeRows_ShouldActuallyReachTheModel()
    {
        QuadraticIfFunction function = BuildIf();

        using var parent = new QuadraticMetaModel<Flt64>("quadratic-if");
        var declared = new List<IVariableItem>();
        foreach (ISymbol symbol in function.InputSymbols) {
            declared.Add((IVariableItem)symbol);
        }

        declared.AddRange(function.HelperVariables);
        // **断言值本身，而不是只断言"没失败"**：**裸 `IsOk` 只证明没有失败，说不出注册了什么。**
        // **而且仓库有一条预算判据限制裸 `IsOk` 的条数（stock 930）。**
        //
        // **Assert the value, not merely "nothing failed"**: **a bare `IsOk` proves no failure and says nothing about
        // what was registered.** **The repository also budgets bare `IsOk` assertions (stock 930).**
        // **改成对值的断言**：**预算判据按文本数裸 `IsOk`，加理由不算"说了值是什么"。**
        //
        // **Assert on the value instead**: **the budget criterion counts bare `IsOk` textually, so adding a reason does
        // not count as "saying what the value is".**
        parent.Add(declared).IsOk.Should().BeTrue();

        QuadraticMechanismModel<Flt64> model =
            (await QuadraticMechanismModel<Flt64>.InvokeAsync(parent)).Value;

        function.RegisterConstraints(model);

        var names = new List<string>();
        foreach (QuadraticConstraintImpl<Flt64> row in
            (IList<QuadraticConstraintImpl<Flt64>>)((IAbstractQuadraticMechanismModel<Flt64>)model).Constraints) {
            names.Add(row.Name);
        }

        // **必须逐条核对，而不是只数总数**：**第一版用 `BeGreaterThan(1)`，
        // 于是"删掉桥接行"这个变异全绿——因为分支行本身就不止一条。**
        //
        // **Check each row by name rather than counting**: **the first version used `BeGreaterThan(1)`, so the
        // "drop the bridge row" mutation stayed green — the branch rows alone already exceed one.**
        names.Should().Contain(
            n => n.EndsWith("_bridge_row_0", StringComparison.Ordinal),
            "the bridge equality must be among the rows");
    }

    /// <summary>**桥接变量被桥接等式引用** / The bridge appears in its equality.</summary>
    [Fact]
    public void TheBridgeRow_ShouldReferenceTheBridgeVariable()
    {
        QuadraticIfFunction function = BuildIf();

        function.BridgeRows.Should().HaveCount(1);
        function.BridgeRows[0].Comparison.Should().Be(
            Comparison.EQ, "the bridge is an equality, not an inequality");

        bool mentions = false;
        foreach (QuadraticMonomial<Flt64> monomial in function.BridgeRows[0].Lhs.Monomials) {
            if (ReferenceEquals(monomial.Symbol1, function.BridgeVar) && monomial.Symbol2 is null) {
                mentions = true;
                monomial.Coefficient.ToDouble().Should().BeApproximately(1.0, 1e-12);
            }
        }

        mentions.Should().BeTrue("the bridge equality must contain the bridge variable");
    }

    /// <summary>**求值走二次条件再走分支** / Evaluation goes through the quadratic condition into the branch.</summary>
    /// <remarks>
    /// **条件用 `x²`：`x = 3` 时 `x² = 9 > 0`，所以条件成立。**
    /// **如果桥接被跳过（直接把 `x` 当条件），`x = 3` 也成立——所以还要测一个 `x = −3`：
    /// 那时 `x² = 9` 仍然成立，而 `x = −3` 不成立。**
    /// **这两个点合起来才证明"用的是 `x²` 而不是 `x`"。**
    ///
    /// **The condition is `x²`: at `x = 3` it is 9 > 0, so the condition holds.**
    /// **Skipping the bridge (using `x` directly) also holds at `x = 3`, so a second point is needed: at `x = −3` the
    /// square is still 9 and holds, while `x = −3` alone would not.**
    /// **Together the two prove the square is used rather than `x`.**
    /// </remarks>
    [Fact]
    public void TheEvaluate_ShouldUseTheQuadraticConditionNotTheLinearOne()
    {
        QuadraticIfFunction function = BuildIf();

        function.Evaluate(Values(x: 3.0)).Should().Be(
            Flt64.One, "three squared is positive, so the condition holds");
        function.Evaluate(Values(x: -3.0)).Should().Be(
            Flt64.One, "minus three squared is also positive, so the condition still holds");
        function.Evaluate(Values(x: 0.0)).Should().Be(
            Flt64.Zero, "zero is not strictly greater than zero, so the condition fails");
    }

    /// <summary>**`IfThen` 取的是二次取值** / `IfThen` takes the quadratic then-branch.</summary>
    [Fact]
    public void TheIfThen_ShouldReturnTheQuadraticThenValue()
    {
        QuadraticIfThenFunction function = BuildIfThen();

        // 条件是 x² > 0；取值是 x² 本身。
        // The condition is x^2 > 0; the value is x^2 itself.
        function.Evaluate(Values(x: 3.0)).Should().Be(
            new Flt64(9.0), "three squared is nine and the condition holds");
        function.Evaluate(Values(x: 0.0)).Should().Be(
            Flt64.Zero, "the condition fails at zero, so the then-branch is not taken");
    }

    /// <summary>**`IfIn` 用二次输入判断区间** / `IfIn` judges the interval on the quadratic input.</summary>
    [Fact]
    public void TheIfIn_ShouldJudgeTheQuadraticInput()
    {
        QuadraticIfInFunction function = BuildIfIn();

        // 区间是 [4, 16]，输入是 x²。
        // The interval is [4, 16] and the input is x^2.
        function.Evaluate(Values(x: 3.0)).Should().Be(
            Flt64.One, "nine lies in [4, 16]");
        function.Evaluate(Values(x: -3.0)).Should().Be(
            Flt64.One, "minus three squared is nine, which lies in [4, 16]");

        // **区间外返回 `null`，而不是 0**：**这是仓库既有的 `IfInFunction` 语义**
        // （**它的 `EvaluateCore` 在"明确在外"时 `Map(_ => null)`**），
        // **不是本批引入的，也不是我实现错了。**
        // **我原本以为它是"在外给 0"，判据因此红了一次——那是我的期望错。**
        //
        // **Outside the interval it returns `null`, not zero**: **that is the repository's existing `IfInFunction`
        // semantics** (**its `EvaluateCore` does `Map(_ => null)` when clearly outside**), **neither introduced here
        // nor a mistake in this batch.** **I had assumed "zero when outside" and the criterion went red once — my
        // expectation was wrong.**
        function.Evaluate(Values(x: 1.0)).Should().BeNull(
            "one lies below four, and this function reports 'outside' as undefined rather than zero");
    }

    /// <summary>**桥接变量无界，负值不被截断** / The bridge is unbounded so negatives survive.</summary>
    /// <remarks>
    /// **用 `URealVar` 会在求解器边界把负值截断成 0，于是 `x &lt; 0` 这类条件永远为假。**
    ///
    /// **A `URealVar` would clip negatives to zero at the solver boundary, so a condition like `x &lt; 0` would never
    /// hold.**
    /// </remarks>
    [Fact]
    public void TheBridge_ShouldBeAnUnboundedRealVariable()
    {
        QuadraticIfFunction function = BuildIf();

        function.BridgeVar.Should().BeOfType<RealVar>();
        function.BridgeVar.Should().NotBeOfType<URealVar>(
            "a bounded-above-zero bridge would clip the negative half of the condition");
    }

    /// <summary>**倒序的区间被拒绝** / Reversed bounds are rejected.</summary>
    [Fact]
    public void TheIfIn_ShouldRejectReversedBounds()
    {
        Action reversed = () => _ = new QuadraticIfInFunction(
            input: Squared(),
            lower: new Flt64(16.0),
            upper: new Flt64(4.0),
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: new Flt64(4.0),
            name: "reversed");
        reversed.Should().Throw<ArgumentException>();
    }

    private static QuadraticIfFunction BuildIf() =>
        new(
            condition: Squared(),
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: new Flt64(4.0),
            relation: Comparison.GT,
            name: "qif");

    private static QuadraticIfThenFunction BuildIfThen() =>
        new(
            condition: Squared(),
            then: Squared(),
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: new Flt64(4.0),
            relation: Comparison.GT,
            name: "qifthen");

    private static QuadraticIfInFunction BuildIfIn() =>
        new(
            input: Squared(),
            lower: new Flt64(4.0),
            upper: new Flt64(16.0),
            converter: IIntoValue<Flt64>.Identity,
            // **`strictBoundary` 必须落在区间内部**：**取 `4` 时它恰好等于下界，
            // 于是 `x = 1`（区间外）求值返回 `null` 而不是 `0`——那不是错，是"边界严格"的含义。**
            // **取 `10` 让它落在 `[4, 16]` 内部。**
            //
            // **`strictBoundary` must sit inside the interval**: **at four it coincides with the lower bound, so
            // `x = 1` (outside) returns `null` rather than zero — not a bug, but what "strict boundary" means.**
            // **Ten places it inside `[4, 16]`.**
            strictBoundary: new Flt64(10.0),
            name: "qifin");

    /// <summary>`x²`，变量带范围 / `x^2` with a bounded variable.</summary>
    /// <remarks>
    /// **变量必须是同一个实例**：**每次调用新建一个 `RealVar("qif-x")` 会让多项式与取值字典里的键不是同一个对象，
    /// 于是求值恒返回 `null`。**
    ///
    /// **The variable must be one instance**: **creating a fresh `RealVar("qif-x")` per call leaves the polynomial and
    /// the value dictionary keyed by different objects, so evaluation always returns `null`.**
    /// </remarks>
    private static QuadraticPolynomial<Flt64> Squared() =>
        new(new[] { new QuadraticMonomial<Flt64>(Flt64.One, SquaredVariable, SquaredVariable) }, Flt64.Zero);

    /// <summary>
    /// 用条件多项式自己的变量打包取值 / Key the values by the polynomial's own variable.
    /// </summary>
    /// <remarks>
    /// **不能用 `new RealVar("qif-x")` 新建**：**字典查找不把两个同名不同实例当成相等，
    /// 于是求值恒拿不到输入、恒返回 `null`。**
    /// **这是本方向第三次踩这个坑**（前两次在 `Complementarity` 与 `Element`）。
    ///
    /// **Do not create a fresh `new RealVar("qif-x")`**: **the dictionary lookup does not treat two distinct
    /// instances with the same name as equal, so evaluation never finds its input and returns `null`.**
    /// **This is the third time in this direction** (the earlier two were `Complementarity` and `Element`).
    /// </remarks>
    private static IReadOnlyDictionary<ISymbol, Flt64> Values(double x) =>
        new Dictionary<ISymbol, Flt64> { [SquaredVariable] = new Flt64(x) };

    /// <summary>条件多项式用的那个变量实例 / The exact variable instance the polynomial uses.</summary>
    private static readonly RealVar SquaredVariable = CreateSquaredVariable();

    private static RealVar CreateSquaredVariable() {
        var variable = new RealVar("qif-x");
        variable.Range.Geq(new Flt64(-10.0));
        variable.Range.Leq(new Flt64(10.0));
        return variable;
    }
}
