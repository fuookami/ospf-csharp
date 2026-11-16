#nullable enable
#if CPLEX_INTEGRATION

using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Plugin.Cplex;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Symbol.Flatten;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Algebra.ValueRange;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Threading.Tasks;
using Xunit;

namespace Fuookami.Ospf.Core.Plugin.Cplex.Tests;

/// <summary>
/// 延迟物化之后的模型真的求解一次 / A materialised model is really solved once.
/// </summary>
/// <remarks>
/// 补这条线上最大的空白：**此前所有判据都止于"转换成三元模型"，真实求解一次都没跑过。**
///
/// **这条判据的设计要点是"行承重"：** **目标是最小化 `y`，约束是 `x = −3`，唯一把 `y` 压到 3 的东西
/// 就是 `abs` 的 fallback 行。**
///
/// **所以如果 fallback 行没被物化进去，`y` 就<b>没有下界</b>——求解会给出无界或一个远小于 3 的值，
/// 而<b>不会报错</b>。**
///
/// **"断言目标值等于 3"因此不是"顺便看看解对不对"，而是<b>行是否存在的唯一证据</b>：
/// 少那几行，模型依然合法、依然能解，只是答案不对。**
///
/// Fills the biggest blank on this line: every criterion so far stopped at "converted to a triad model", and no real
/// solve had ever run.
///
/// **The design point is that the rows carry the answer: the objective minimises `y`, the constraint fixes `x = −3`, and
/// the only thing pushing `y` down to 3 is the `abs` fallback rows.**
///
/// **So if those rows were not materialised, `y` has <b>no lower bound</b> — the solve returns unbounded or something far
/// below 3, and <b>does not fail</b>.**
///
/// **Asserting the objective equals 3 is therefore not "checking the answer while we are here" but the <b>only evidence the
/// rows exist</b>: without them the model is still legal, still solves, and merely answers wrong.**
/// </remarks>
public class DeferredMaterialisationSolveCplexIntegrationTest
{
    /// <summary>
    /// **物化后的 `abs` 行真的把解压到 3** / The materialised `abs` rows really pin the answer at 3.
    /// </summary>
    [Fact]
    public async Task TheMaterialisedAbsRows_ShouldPinTheAnswer() {
        var x = new RealVar("deferred-x");
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-4), new Flt64(6)).Value);
        var y = new RealVar("deferred-y");
        var positive = new RealVar("deferred-y-pos");
        var negative = new RealVar("deferred-y-neg");
        // **fallback 行不约束这两个辅助变量非负，它们依赖变量自己的范围——
        // 不给下界，`y = pos + neg` 就可以一路走到 −∞（这里实测：解出 −4.5e15）。**
        // **The fallback rows do not force these helpers non-negative; they rely on the variables' own ranges. Without a
        // lower bound, `y = pos + neg` walks off to −∞ (measured here: −4.5e15).**
        positive.Range.SetRange(ValueRange<Flt64>.Of(Flt64.Zero, new Flt64(100)).Value);
        negative.Range.SetRange(ValueRange<Flt64>.Of(Flt64.Zero, new Flt64(100)).Value);
        var sign = new BinVar("deferred-y-sign");

        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(y).IsOk);
        // **物化产出的约束引用了这三个辅助变量，它们必须在 token 表里，
 // 否则 `AddConstraint` 解析不到。**
        // **The materialised rows reference these three helpers, so they must be in the token table or `AddConstraint`
        // cannot resolve them — one earlier attempt hit exactly that.**
        Assert.True(parent.Add(positive).IsOk);
        Assert.True(parent.Add(negative).IsOk);
        Assert.True(parent.Add(sign).IsOk);

        // **x = −3。**
        Assert.True(parent.AddConstraint(relation: Fixed(x, -3.0), group: null).IsOk);
        // **最小化 y。**
        Assert.True(parent.AddObject(
            category: ObjectCategory.Minimum,
            flattenData: new LinearFlattenData<Flt64>(
                new[] { new LinearMonomial<Flt64>(Flt64.One, y) },
                Flt64.Zero),
            name: "deferred-objective",
            displayName: null
        ).IsOk);

        var structure = new AbsStructure(
            Input: new LinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(Flt64.One, x) }, Flt64.Zero),
            ResultVariable: y,
            PositiveVariable: positive,
            NegativeVariable: negative,
            SignVariable: sign,
            PositiveBigM: new Flt64(100.0),
            NegativeBigM: new Flt64(100.0),
            Converter: IIntoValue<Flt64>.Identity,
            Name: "deferred-abs");

        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built =
            await LinearMechanismModel<Flt64>.InvokeAsync(
                parent,
                functionExpansionPolicy: FunctionExpansionPolicy.DeferredNativeFirst,
                deferredFunctionStructures: [structure]);
        Assert.True(built.IsOk, built.ToString());
        using LinearMechanismModel<Flt64> model = built.Value;

 // **物化发生在转换里面，所以这一步之后三元模型才带上 fallback 行。**
 // **Materialisation happens inside the conversion, so only after this does the triad model carry
        // the fallback rows.**
        Result<LinearTriadModel, ErrorCode, Error<ErrorCode>> converted = model.ToLinearTriadModel();
        Assert.True(converted.IsOk, converted.ToString());
        using LinearTriadModel matrix = converted.Value;

        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> solved =
            await new CplexLinearSolver().InvokeAsync((ILinearTriadModelView)matrix);
        Assert.True(solved.IsOk, solved.ToString());

        // **|−3| = 3。** **若 fallback 行缺失，y 无下界，这里会是无界或一个远离 3 的值。**
        // **|−3| = 3.** **With the fallback rows missing, y is unbounded and this is either unbounded or far from 3.**
        Assert.InRange(
            actual: solved.Value.Obj.ToDouble(),
            low: 3.0 - 1e-7,
            high: 3.0 + 1e-7
        );
    }

    private static LinearInequality<Flt64> Fixed(ISymbol symbol, double value) => new(
        Lhs: new LinearPolynomial<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, symbol) }, Flt64.Zero),
        Rhs: new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), new Flt64(value)),
        Comparison: Comparison.EQ
    );
}
#endif
