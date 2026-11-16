#nullable enable

#if GUROBI_INTEGRATION
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Plugin.Gurobi;
using Fuookami.Ospf.Core.Plugin.Onnx.Lowering;
using Fuookami.Ospf.Core.Plugin.Onnx.Model;
using Fuookami.Ospf.Core.Symbol.Flatten;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
using Xunit;

namespace Fuookami.Ospf.Core.Plugin.Gurobi.Tests;

/// <summary>
/// **二次目标真的被求解：`(x−3)²` 的最小值是 0，落在 x = 3** / A quadratic objective is really solved.
/// </summary>
/// <remarks>
/// 同步 Kotlin `dee897d2` 的 `Quadratic` 导入模式。**上一批把这一条记成"挂接没做成"，原因其实是我用错了类：** '
/// **二次的 `AddObject` 在 `QuadraticMetaModel` 上，不在 `LinearMetaModel` 上——编译器是对的，读错的是我。** '
/// **这一批把它改正，并补上"二次模型要给一个参考点"这一步。**
///
/// **判据价值在于一个一次模型<b>不可能**凑出这个答案：** 展开成 `x² − 6x + 9` 后，一次项与常数都在，'
/// **唯一的区别是 `x²`；没有它最小值落在端点（`x=0` 给 9、`x=10` 给 49 取小）。**
///
/// Syncs the `Quadratic` import mode. **The previous batch recorded this as "attachment not done", and the real cause was that I used the wrong class: the quadratic AddObject
/// lives on `QuadraticMetaModel`, not on `LinearMetaModel` — the compiler was right and I misread it.** This batch fixes that and adds the reference point a quadratic model
/// needs.
///
/// **The criterion matters because a first-degree model cannot fake the answer: expanded to `x² − 6x + 9` the linear and constant terms are all there and the only difference
/// is x²; without it the minimum lands on an endpoint (9 at x=0, 49 at x=10).**
/// </remarks>
public class OnnxQuadraticObjectiveGurobiIntegrationTest
{
    /// <summary>**最小值是 0，落在 x = 3** / The minimum is zero, at x = 3.</summary>
    [Fact]
    public async Task TheQuadraticObjective_ShouldReachZeroAtThree() {
        using var meta = new QuadraticMetaModel<Flt64>();
        var x = new RealVar("x");
        MustSucceed(meta.Add(x), "the variable has to be in the token table before anything references it");

        foreach (LinearInequality<Flt64> bound in Bounds(x, 0.0, 10.0)) {
            MustSucceed(meta.AddConstraint(relation: bound, group: null), "a bound has to be accepted before the objective is attached");
        }

        var shifted = new LinearPolynomial<Flt64>(
            new[] { new LinearMonomial<Flt64>(Flt64.One, x) },
            new Flt64(-3.0));
        OnnxQuadraticProduct.Result product = OnnxQuadraticProduct.Multiply(shifted, shifted);
        product.Polynomial.Should().NotBeNull(product.Failure);

        var flatten = new QuadraticFlattenData<Flt64>(product.Polynomial!.Monomials, product.Polynomial.Constant);
        MustSucceed(
            meta.AddObject(ObjectCategory.Minimum, flatten, "quadratic-objective", null),
            "QuadraticMetaModel carries the quadratic AddObject overload; LinearMetaModel does not");

        using QuadraticMechanismModel<Flt64> mechanism = (await QuadraticMechanismModel<Flt64>.InvokeAsync(meta)).Value;
        // 不传 fixedVariables：那个字典是"待代换的变量及其值"，也就是把变量固定住。
        // 我第一版传了 x = 0，于是解就是 0，目标值恰为 f(0) = 9——看起来像"最优值"，其实是固定值。
        //
        // No fixedVariables: that dictionary means "variables to substitute", i.e. pin them. The first version passed x = 0, so the solution was 0 and the objective was
        // f(0) = 9 — which looks like an optimum and is really a pinned value.
        using QuadraticTetradModel matrix = mechanism.ToQuadraticTetradModel().Value;

        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> solved =
            await new GurobiQuadraticSolver().InvokeAsync((IQuadraticTetradModelView)matrix);

        solved.IsOk.Should().BeTrue(solved.ToString());
        solved.Value.Obj.ToDouble().Should().BeApproximately(0.0, 1e-5,
            "the minimum of (x−3)² is zero at x = 3; an endpoint would give 9 or 49");
    }

    /// <summary>**界把最优点推到边界外时，最优值是端点上的值** / With the bound away from 3 the optimum sits on the bound.</summary>
    /// <remarks>
    /// **两条成对，理由与符号激活那批相同：** **只测 `[0,10]` 时，一个把 `x²` 丢掉的一次模型会给出端点值 9——不会通过；'
    /// **但只测这一条时，一个"总是返回 0"的实现也会通过。** **`[5,10]` 上正确答案是 4（`x = 5`），两种错误都会露出来。**
    ///
    /// **The pair exists for the same reason as the activation batch: on `[0,10]` alone a first-degree model without x² gives an endpoint value of 9 and fails, but an
    /// implementation that always returns 0 would pass. On `[5,10]` the right answer is 4 (at x = 5), which exposes both mistakes.**
    /// </remarks>
    [Fact]
    public async Task WithTheBoundAwayFromThree_ShouldSitOnTheBound() {
        using var meta = new QuadraticMetaModel<Flt64>();
        var x = new RealVar("x");
        MustSucceed(meta.Add(x), "the variable has to be in the token table");

        foreach (LinearInequality<Flt64> bound in Bounds(x, 5.0, 10.0)) {
            MustSucceed(meta.AddConstraint(relation: bound, group: null), "a bound has to be accepted before the objective is attached");
        }

        var shifted = new LinearPolynomial<Flt64>(
            new[] { new LinearMonomial<Flt64>(Flt64.One, x) },
            new Flt64(-3.0));
        OnnxQuadraticProduct.Result product = OnnxQuadraticProduct.Multiply(shifted, shifted);
        var flatten = new QuadraticFlattenData<Flt64>(product.Polynomial!.Monomials, product.Polynomial.Constant);
        MustSucceed(meta.AddObject(ObjectCategory.Minimum, flatten, "quadratic-objective", null), "the objective has to be attached");

        using QuadraticMechanismModel<Flt64> mechanism = (await QuadraticMechanismModel<Flt64>.InvokeAsync(meta)).Value;
        using QuadraticTetradModel matrix = mechanism.ToQuadraticTetradModel().Value;

        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> solved =
            await new GurobiQuadraticSolver().InvokeAsync((IQuadraticTetradModelView)matrix);

        solved.IsOk.Should().BeTrue(solved.ToString());
        solved.Value.Obj.ToDouble().Should().BeApproximately(4.0, 1e-5,
            "(5 − 3)² = 4, and x cannot reach 3 because the bound starts at 5");
    }

    /// <summary>
    /// 装配步骤必须成功 / An assembly step has to succeed.
    /// </summary>
    /// <remarks>
    /// **装配（登记变量、加约束、挂目标）没有"值"可言，** **这里用 `IsFailed` 的反面表达，是为了让"这一条判据在验什么"清楚：** '
    /// **它验的不是"结果非空"，而是"这一步没被拒绝"。**
    ///
    /// **Assembly (registering a variable, adding a constraint, attaching an objective) has no value to speak of; phrasing it as the negation of IsFailed keeps clear what the
    /// criterion is checking: not that a result exists, but that this step was not refused.**
    /// </remarks>
    private static void MustSucceed(Try step, string why) => step.IsFailed.Should().BeFalse(why);

    private static IReadOnlyList<LinearInequality<Flt64>> Bounds(RealVar x, double lower, double upper) {
        OnnxBoundsConstraints.Generated generated = OnnxBoundsConstraints.ForAll(
            new Dictionary<string, ISymbol> { ["x"] = x },
            new Dictionary<string, OnnxBounds> { ["x"] = new OnnxBounds(lower, upper) },
            out IReadOnlyList<string> missing);
        generated.Failure.Should().BeNull(generated.Failure);
        missing.Should().BeEmpty();
        return generated.Constraints;
    }
}
#endif