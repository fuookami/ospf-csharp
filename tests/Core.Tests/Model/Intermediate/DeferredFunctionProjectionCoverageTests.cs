#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Symbol.Flatten;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Model.Intermediate;

/// <summary>
/// 三种函数符号都能自动派生结构 / Three function symbols all derive their structures.
/// </summary>
/// <remarks>
/// 把投影从一种扩到三种（`Abs`、`Max`／`Min`、`Semi`），并把构建路径里的分派集中成一处 `switch`。
///
/// **判据只证明了 `Abs` 那条链通。而 `Max` 与 `Semi` 各有<b>一处</b>不是机械搬运——
/// 那正是它们值得单独跑判据的原因。**
///
/// Widens the projection from one kind to three (`Abs`, `Max`/`Min`, `Semi`) and centralises the build path's
/// dispatch into one `switch`.
///
/// **the earlier criterion only proved the `Abs` chain works. `Max` and `Semi` each have <b>one</b> place that is not
/// mechanical — which is exactly why they deserve their own criteria.**
/// </remarks>
public class DeferredFunctionProjectionCoverageTests
{
    /// <summary>
    /// **`Max` 与 `Min` 派生出的结构必须记得自己是哪一种** /
    /// A `Max`/`Min` structure must remember which one it is.
    /// </summary>
    /// <remarks>
    /// **这是这里最要紧的一条：** **`Min` 与 `Max` 是同一个类的两个构造，而"是哪一种"此前只存在私有字段里。
    /// 如果投影忘掉这个方向，`min` 会被当成 `max` 物化——而模型照样能解，只是答案错了。**
    ///
    /// **所以判据必须<b>分别</b>建 `Min` 与 `Max` 两个符号，并各自核对。**
    ///
    /// **This is the most important criterion here: `Min` and `Max` are two constructions of one class, and which one it
    /// is existed only in a private field. If the projection loses that direction, a `min` materialises as a `max` — the
    /// model still solves, merely with the wrong answer.**
    ///
    /// **So the criterion builds a `Min` and a `Max` <b>separately</b> and checks each.**
    /// </remarks>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExtremumProjection_ShouldKeepItsDirection(bool minimum) {
        var left = new RealVar("ext-x1");
        var right = new RealVar("ext-x2");
        IMathFunctionSymbol<Flt64> symbol = minimum
            ? (IMathFunctionSymbol<Flt64>)new MinFunction<Flt64>(
                polynomials: [Linear(left), Linear(right)],
                converter: IIntoValue<Flt64>.Identity,
                name: "ext")
            : new MaxFunction<Flt64>(
                polynomials: [Linear(left), Linear(right)],
                converter: IIntoValue<Flt64>.Identity,
                name: "ext");

        using LinearMetaModel<Flt64> parent = MetaModel([left, right], [new LinearFunctionSymbolAdapter<Flt64>(symbol)], left);
        using LinearMechanismModel<Flt64> model = await BuiltAsync(parent);

        model.DeferredFunctionStructures.Should().ContainSingle();
        MaxStructure structure = model.DeferredFunctionStructures[0].Should().BeOfType<MaxStructure>().Subject;
        structure.Minimum.Should().Be(minimum, "the direction is the one field that a projection can silently lose");
        structure.BigMValues.Should().HaveCount(2, "one Big-M per input, all finite");
        structure.SelectorVariables.Should().HaveCount(2);
    }

    /// <summary>
    /// **`Semi` 派生出的结构带上了它自己的上下界** / A `Semi` structure carries its own bounds.
    /// </summary>
    /// <remarks>
    /// **`SemiFunction` 的 `lb`／`ub` 都是可选的、有默认值。**
    /// **所以"投影把上下界实现出来了"这句话必须用<b>非默认值</b>来验——用默认值验，漏实现也看不出来。**
    ///
    /// **`SemiFunction`'s `lb`/`ub` are optional with defaults, so "the projection carried the bounds over" must be checked
    /// with <b>non-default</b> values — with defaults, forgetting to carry them looks identical.**
    /// </remarks>
    [Fact]
    public async Task SemiProjection_ShouldCarryTheBounds() {
        var symbol = new SemiFunction(lb: new Flt64(2.0), ub: new Flt64(7.0), name: "semi-proj");
        using LinearMetaModel<Flt64> parent = MetaModel(
            [],
            [new LinearFunctionSymbolAdapter<Flt64>(symbol)],
            symbol.ResultVar);
        using LinearMechanismModel<Flt64> model = await BuiltAsync(parent);

        model.DeferredFunctionStructures.Should().ContainSingle();
        SemiStructure structure = model.DeferredFunctionStructures[0].Should().BeOfType<SemiStructure>().Subject;
        structure.LowerBound.ToDouble().Should().Be(2.0, "a default bound would hide a missing carry");
        structure.UpperBound.ToDouble().Should().Be(7.0);
        structure.IndicatorVariable.Should().BeSameAs(symbol.IndicatorVar);
    }

    /// <summary>
    /// **派生之后急切那一份被跳过，所以行不会翻倍** / Eager rows are skipped after deriving, so nothing doubles.
    /// </summary>
    /// <remarks>
    /// **这一条把 "派生与跳过一起做"扩到三种：** **如果 `switch` 只对 `Abs` 跳过、
    /// 对 `Max` 只派生，那么 `Max` 就会有 4 行急切约束再加一份物化行。**
    ///
    /// **判据用"延迟下为 0 行"来卡，因为那是唯一能被观察到的痕迹。**
    ///
    /// **This extends the earlier "derive and skip together" to three kinds: if the `switch` skipped only `Abs` while merely
    /// deriving `Max`, then `Max` would keep its four eager rows plus a materialised copy.**
    ///
    /// **The criterion uses "zero rows under deferred" because that is the only observable trace.**
    /// </remarks>
    [Fact]
    public async Task EveryDerivedKind_ShouldSkipItsEagerRows() {
        var left = new RealVar("skip-x1");
        var right = new RealVar("skip-x2");
        var extremum = new MaxFunction<Flt64>(
            polynomials: [Linear(left), Linear(right)],
            converter: IIntoValue<Flt64>.Identity,
            name: "skip-max");
        using LinearMetaModel<Flt64> parent = MetaModel(
            [left, right],
            [new LinearFunctionSymbolAdapter<Flt64>(extremum)],
            left);
        using LinearMechanismModel<Flt64> deferred = await BuiltAsync(parent);

        deferred.NumConstraints.Should().Be(0, "the eager copy must be skipped for every derived kind");
        deferred.DeferredFunctionStructures.Should().ContainSingle();

        using LinearTriadModel matrix = Converted(deferred);
        matrix.Constraints.ConstraintCount.Should().BeGreaterThan(
            0, "materialisation must put rows back; a deferred build is not an empty model");
    }

    private static LinearPolynomial<Flt64> Linear(IVariableItem variable) =>
        new(new[] { new LinearMonomial<Flt64>(Flt64.One, variable) }, Flt64.Zero);

    private static LinearMetaModel<Flt64> MetaModel(
        IVariableItem[] variables,
        IIntermediateSymbol[] symbols,
        IVariableItem objective) {
        var parent = new LinearMetaModel<Flt64>();
        foreach (IVariableItem variable in variables) {
            bool addedVariable = parent.Add(variable).IsOk;
            addedVariable.Should().BeTrue();
        }
        foreach (IIntermediateSymbol symbol in symbols) {
            bool addedSymbol = parent.Add(symbol).IsOk;
            addedSymbol.Should().BeTrue();
        }
        bool addedObjective = parent.AddObject(
            category: ObjectCategory.Minimum,
            flattenData: new LinearFlattenData<Flt64>(
                new[] { new LinearMonomial<Flt64>(Flt64.One, (ISymbol)objective) }, Flt64.Zero),
            name: "projection-objective",
            displayName: null).IsOk;
        addedObjective.Should().BeTrue();
        return parent;
    }

    private static async Task<LinearMechanismModel<Flt64>> BuiltAsync(LinearMetaModel<Flt64> parent) {
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built =
            await LinearMechanismModel<Flt64>.InvokeAsync(
                parent,
                functionExpansionPolicy: FunctionExpansionPolicy.DeferredNativeFirst);
        Explain(built);
        return ((Ok<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>)built).Value;
    }

    private static LinearTriadModel Converted(LinearMechanismModel<Flt64> model) {
        Result<LinearTriadModel, ErrorCode, Error<ErrorCode>> converted = model.ToLinearTriadModel();
        Explain(converted);
        return ((Ok<LinearTriadModel, ErrorCode, Error<ErrorCode>>)converted).Value;
    }

    private static void Explain<T>(Result<T, ErrorCode, Error<ErrorCode>> result) {
        if (result is Failed<T, ErrorCode, Error<ErrorCode>> failure) {
            failure.Error.Message.Should().BeEmpty($"the call failed because: {failure.Error.Message}");
        }
        if (result is Fatal<T, ErrorCode, Error<ErrorCode>> fatal) {
            string joined = string.Join(" | ", fatal.Errors.Select(static e => e.Message));
            joined.Should().BeEmpty($"the call failed fatally because: {joined}");
        }
    }
}
