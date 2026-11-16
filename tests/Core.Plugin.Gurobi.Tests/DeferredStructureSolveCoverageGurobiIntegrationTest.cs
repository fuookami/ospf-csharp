#nullable enable
#if GUROBI_INTEGRATION

using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Symbol.Flatten;
using Fuookami.Ospf.Core.Symbol.Function;
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
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace Fuookami.Ospf.Core.Plugin.Gurobi.Tests;

/// <summary>
/// 其余六种延迟结构各真实求解一次 / Each of the remaining six deferred structures solved once for real.
/// </summary>
/// <remarks>
/// 把求解验证从一种扩到七种。**只有证明了 `abs` 能解，**
/// **而"一种结构能解"推不出"每种结构能解"——七种的行结构各不相同。**
///
/// **每一条的共同设计是"行承重"：** **目标函数的最优值<b>只能</b>由 fallback 行压出来。**
/// **少那几行，模型不是"报错"，而是给出另一个值或不给下界。**
///
/// Widens solve verification from one kind to seven. **Only proved `abs` solves**, and "one kind
/// solves" does not imply "every kind solves" — the seven have different row structures.**
///
/// **Every case shares one design: the rows carry the answer.** **The optimum can <b>only</b> be forced out by the fallback
/// rows.** **Without them the model does not fail — it answers something else, or loses its bound.**
/// </remarks>
public class DeferredStructureSolveCoverageGurobiIntegrationTest
{
    /// <summary>**`max`：最大化后应得 3（取 1 与 3 的较大者）** / `max`: the maximum is 1 or 3, so 3.</summary>
    [Fact]
    public async Task MaxRows_ShouldPinTheAnswer() {
        var left = new RealVar("max-x1");
        var right = new RealVar("max-x2");
        var result = new RealVar("max-y");
        // **选择子与 Big-M 的数量必须与输入数量一致**（两个输入 → 两个选择子、两个 Big-M）。
        // **Selector and Big-M counts must match the input count.**
        var selectorA = new BinVar("max-sel-a");
        var selectorB = new BinVar("max-sel-b");
        var structure = new MaxStructure(
            Inputs: [Linear(left), Linear(right)],
            ResultVariable: result,
            SelectorVariables: [selectorA, selectorB],
            BigMValues: [new Flt64(100.0), new Flt64(100.0)],
            Converter: IIntoValue<Flt64>.Identity,
            Name: "max");

        double value = await SolveAsync(
            structure,
            [left, right, result, selectorA, selectorB],
            result,
            ObjectCategory.Maximum,
            [(left, 1.0), (right, 3.0)]);

        Assert.InRange(value, 3.0 - 1e-7, 3.0 + 1e-7);
    }

    /// <summary>
    /// **`min` 方向：最小化后应得 1（取 1 与 3 的较小者）** /
    /// The `min` direction: minimising gives 1, the smaller of 1 and 3.
    /// </summary>
    /// <remarks>
    /// **这条判据，因为 `Minimum` 这个属性此前<b>没有任何地方读它</b>：**
 /// **声明了、投影也在填，而生成逻辑无论方向都产 MAX 行。**
    /// **于是 `min` 会被当成 MAX 物化——模型合法、能解，只是答案是最大值。**
    ///
    /// **只做结构判据是抓不到它的：** **两组行都"存在"，只是符号反了。**
    /// **所以这一条必须是真实求解：固定 x₁ = 1、x₂ = 3，最小化 y，正确答案是 1；
    /// 若按 MAX 物化，y 会被压到 3。**
    ///
 /// **adds this because `Minimum` was <b>read by nothing</b> — declared, filled by the projection, and the
    /// generation produced MAX rows either way.** **So a min materialised as a MAX: legal, solvable, answering with the
    /// maximum.**
    ///
    /// **A structural criterion cannot catch it: both row families "exist", only the signs are wrong.** **So this must be a
    /// real solve: pin x₁ = 1 and x₂ = 3, minimise y, right answer 1; materialised as a MAX it would be pushed to 3.**
    /// </remarks>
    [Fact]
    public async Task MinRows_ShouldPinTheAnswer() {
        var left = new RealVar("min-x1");
        var right = new RealVar("min-x2");
        var result = new RealVar("min-y");
        var selectorA = new BinVar("min-sel-a");
        var selectorB = new BinVar("min-sel-b");
        var structure = new MaxStructure(
            Inputs: [Linear(left), Linear(right)],
            ResultVariable: result,
            SelectorVariables: [selectorA, selectorB],
            BigMValues: [new Flt64(100.0), new Flt64(100.0)],
            Converter: IIntoValue<Flt64>.Identity,
            Name: "min") {
            Minimum = true,
        };

        double value = await SolveAsync(
            structure,
            [left, right, result, selectorA, selectorB],
            result,
            ObjectCategory.Minimum,
            [(left, 1.0), (right, 3.0)]);

        Assert.InRange(value, 1.0 - 1e-7, 1.0 + 1e-7);
    }

    /// <summary>**`piecewise`：斜率 1、截距 0，故 x = 5 时最小值为 5** / `piecewise`: slope 1 through the origin, so 5.</summary>
    [Fact]
    public async Task PiecewiseRows_ShouldPinTheAnswer() {
        var input = new RealVar("pwl-x");
        input.Range.SetRange(ValueRange<Flt64>.Of(Flt64.Zero, new Flt64(10)).Value);
        var result = new RealVar("pwl-y");
        var selector = new RealVar("pwl-sel");
        var structure = new UnivariateLinearPiecewiseStructure(
            Input: Linear(input),
            Breakpoints: [Flt64.Zero, new Flt64(10)],
            Slopes: [Flt64.One],
            Intercepts: [Flt64.Zero],
            ResultVariable: result,
            SelectorVariables: [selector]) {
            Converter = IIntoValue<Flt64>.Identity,
            CapturedInputBounds = new Ok<LinearPolynomialBounds<Flt64>, ErrorCode, Error<ErrorCode>>(
                new LinearPolynomialBounds<Flt64>(Flt64.Zero, new Flt64(10))),
        };

        double value = await SolveAsync(
            structure,
            [input, result, selector],
            result,
            ObjectCategory.Minimum,
            [(input, 5.0)]);

        Assert.InRange(value, 5.0 - 1e-7, 5.0 + 1e-7);
    }

    /// <summary>**`binary-logic` AND：两个输入都为 1 时结果为 1** / AND: with both inputs 1 the result is 1.</summary>
    [Fact]
    public async Task BinaryLogicRows_ShouldPinTheAnswer() {
        var left = new BinVar("logic-a");
        var right = new BinVar("logic-b");
        var result = new BinVar("logic-y");
        var structure = new BinaryLogicStructure(
            Operation: BinaryLogicOperation.And,
            Inputs: [left, right],
            ResultVariable: result,
            Converter: IIntoValue<Flt64>.Identity,
            Name: "logic");

        double value = await SolveAsync(
            structure,
            [left, right, result],
            result,
            ObjectCategory.Maximum,
            [(left, 1.0), (right, 1.0)]);

        Assert.InRange(value, 1.0 - 1e-7, 1.0 + 1e-7);
    }

    /// <summary>**`semi`：指标被定为 1 时，最小值就是下界 2** / `semi`: with the indicator at 1, the minimum is the lower bound 2.</summary>
    [Fact]
    public async Task SemiRows_ShouldPinTheAnswer() {
        var result = new RealVar("semi-y");
        var indicator = new BinVar("semi-i");
        var structure = new SemiStructure(
            LowerBound: new Flt64(2.0),
            UpperBound: new Flt64(10.0),
            ResultVariable: result,
            IndicatorVariable: indicator,
            Converter: IIntoValue<Flt64>.Identity,
            Name: "semi");

        double value = await SolveAsync(
            structure,
            [result, indicator],
            result,
            ObjectCategory.Minimum,
            [(indicator, 1.0)]);

        Assert.InRange(value, 2.0 - 1e-7, 2.0 + 1e-7);
    }

    /// <summary>**`indicator`：输入为 5（不小于容差）时结果为 1** / `indicator`: with the input at 5 the result is 1.</summary>
    [Fact]
    public async Task IndicatorRows_ShouldPinTheAnswer() {
        var input = new RealVar("ind-x");
        input.Range.SetRange(ValueRange<Flt64>.Of(Flt64.Zero, new Flt64(10)).Value);
        var result = new BinVar("ind-y");
        var structure = new IndicatorStructure(
            Input: Linear(input),
            ResultVariable: result,
            BigM: new Flt64(100.0),
            Tolerance: new Flt64(1e-6),
            Converter: IIntoValue<Flt64>.Identity,
            Name: "ind") {
            CapturedInputBounds = new LinearPolynomialBounds<Flt64>(Flt64.Zero, new Flt64(10)),
        };

        double value = await SolveAsync(
            structure,
            [input, result],
            result,
            ObjectCategory.Maximum,
            [(input, 5.0)]);

        Assert.InRange(value, 1.0 - 1e-7, 1.0 + 1e-7);
    }

    /// <summary>**`masking`：mask 为 1 时结果等于输入 4** / `masking`: with the mask at 1 the result equals the input 4.</summary>
    [Fact]
    public async Task MaskingRows_ShouldPinTheAnswer() {
        var input = new RealVar("mask-x");
        input.Range.SetRange(ValueRange<Flt64>.Of(Flt64.Zero, new Flt64(10)).Value);
        var result = new RealVar("mask-y");
        var mask = new BinVar("mask-i");
        var structure = new MaskingStructure(
            Value: new ConditionalValue(
                Input: Linear(input),
                ResultVariable: result,
                Bounds: new ConditionBounds<Flt64>(Flt64.Zero, new Flt64(10)),
                Name: "mask"),
            Mask: mask,
            Converter: IIntoValue<Flt64>.Identity);

        double value = await SolveAsync(
            structure,
            [input, result, mask],
            result,
            ObjectCategory.Maximum,
            [(input, 4.0), (mask, 1.0)]);

        Assert.InRange(value, 4.0 - 1e-7, 4.0 + 1e-7);
    }

    /// <summary>建模型、物化、转换、求解，返回目标值 / Build, materialise, convert, solve, return the objective.</summary>
    private static async Task<double> SolveAsync(
        DeferredFunctionStructure structure,
        IVariableItem[] variables,
        IVariableItem objective,
        ObjectCategory category,
        (IVariableItem Variable, double Value)[] fixedPairs) {
        using var parent = new LinearMetaModel<Flt64>();
        foreach (IVariableItem variable in variables) {
            Assert.True(parent.Add(variable).IsOk, $"could not register {variable.Name}");
        }
        foreach ((IVariableItem variable, double value) in fixedPairs) {
            Assert.True(parent.AddConstraint(relation: Fixed((ISymbol)variable, value), group: null).IsOk);
        }
        Assert.True(parent.AddObject(
            category: category,
            flattenData: new LinearFlattenData<Flt64>(
                new[] { new LinearMonomial<Flt64>(Flt64.One, (ISymbol)objective) },
                Flt64.Zero),
            name: "deferred-objective",
            displayName: null
        ).IsOk);

        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built =
            await LinearMechanismModel<Flt64>.InvokeAsync(
                parent,
                functionExpansionPolicy: FunctionExpansionPolicy.DeferredNativeFirst,
                deferredFunctionStructures: [structure]);
        Assert.True(built.IsOk, built.ToString());
        using LinearMechanismModel<Flt64> model = built.Value;

        Result<LinearTriadModel, ErrorCode, Error<ErrorCode>> converted = model.ToLinearTriadModel();
        Assert.True(converted.IsOk, converted.ToString());
        using LinearTriadModel matrix = converted.Value;

        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> solved =
            await new GurobiLinearSolver().InvokeAsync((ILinearTriadModelView)matrix);
        Assert.True(solved.IsOk, solved.ToString());
        return solved.Value.Obj.ToDouble();
    }

    private static LinearPolynomial<Flt64> Linear(IVariableItem variable) =>
        new(new[] { new LinearMonomial<Flt64>(Flt64.One, (ISymbol)variable) }, Flt64.Zero);

    private static LinearInequality<Flt64> Fixed(ISymbol symbol, double value) => new(
        Lhs: new LinearPolynomial<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, symbol) }, Flt64.Zero),
        Rhs: new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), new Flt64(value)),
        Comparison: Comparison.EQ
    );
}
#endif
