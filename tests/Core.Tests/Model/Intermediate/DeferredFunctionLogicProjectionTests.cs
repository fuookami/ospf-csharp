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
/// 逻辑函数符号的投影 / The projections of the logic function symbols.
/// </summary>
/// <remarks>
/// 把投影从三种扩到六种（加上 `And`／`Or`／`Not`）。
///
/// **`Xor` 仍然没有投影，而那是一个<b>具体发现</b>而不是遗漏：** **`XorFunction` 只存多项式、结果列、
/// 指示列、副列、容差与严格边界，<b>没存二元输入</b>；而 `And`／`Or`／`Not` 都存了。**
/// **所以这条路在符号那一侧就断了——不是投影没写，是符号没保留物化需要的东西。**
///
/// Widens the projection to six kinds (adding `And`/`Or`/`Not`).
///
/// **`Xor` still has no projection, and that is a <b>finding</b> rather than an omission: `XorFunction` stores the
/// polynomials, result, indicators, side variables, tolerance and strict boundary but <b>not the binary inputs</b>, while
/// `And`/`Or`/`Not` all do.** **So the road is cut on the symbol side: the projection is not missing, the symbol did not
/// keep what materialisation needs.**
/// </remarks>
public class DeferredFunctionLogicProjectionTests
{
    /// <summary>
    /// **三种逻辑符号各自把运算方向带进结构** / Each logic symbol carries its operation into the structure.
    /// </summary>
    /// <remarks>
    /// **与 `Min`／`Max` 是同一个陷阱的第二个实例：** **`And`／`Or`／`Not` 是三个<b>互不相关</b>的类，
    /// 所以"投影用了哪个 body"决定了运算方向。**
    ///
    /// **如果三个扩展都误调到同一个 helper 分支，模型照样能解，只是把 `and` 当成了 `or`。**
    ///
    /// **所以判据必须三种各建一个符号，分别核对 `Operation`。**
    ///
    /// **The second instance of the same trap as `Min`/`Max`: `And`/`Or`/`Not` are three <b>unrelated</b> classes, so which
    /// body the projection reaches decides the operation.**
    ///
    /// **If all three extensions reached the same branch, the model would still solve — it would merely treat `and` as
    /// `or`.**
    ///
    /// **So the criterion builds all three and checks `Operation` on each.**
    /// </remarks>
    [Theory]
    [InlineData("and")]
    [InlineData("or")]
    [InlineData("not")]
    public async Task EachLogicSymbol_ShouldKeepItsOperation(string kind) {
        var first = new BinVar($"{kind}-a");
        var second = new BinVar($"{kind}-b");
        IMathFunctionSymbol<Flt64> symbol = kind switch {
            "and" => new AndFunction(
                polynomials: [Linear(first), Linear(second)],
                name: "logic"),
            "or" => new OrFunction(
                polynomials: [Linear(first), Linear(second)],
                name: "logic"),
            _ => new NotFunction(
                polynomial: Linear(first),
                name: "logic"),
        };
        BinaryLogicOperation expected = kind switch {
            "and" => BinaryLogicOperation.And,
            "or" => BinaryLogicOperation.Or,
            _ => BinaryLogicOperation.Not,
        };

        using LinearMetaModel<Flt64> parent = MetaModel([first, second], symbol, objective: first);
        using LinearMechanismModel<Flt64> model = await BuiltAsync(parent);

        model.DeferredFunctionStructures.Should().ContainSingle();
        BinaryLogicStructure structure =
            model.DeferredFunctionStructures[0].Should().BeOfType<BinaryLogicStructure>().Subject;
        structure.Operation.Should().Be(expected, "the operation is the field a shared body can silently lose");
        structure.Inputs.Should().HaveCount(kind == "not" ? 1 : 2);
        model.NumConstraints.Should().Be(0, "the derived kind must skip its eager rows");
    }

    /// <summary>
    /// **输入不是二元变量时不派生，急切注册照旧** /
    /// Non-binary inputs derive nothing and eager registration stays.
    /// </summary>
    /// <remarks>
    /// **这是"返回 `null` 而不是抛"那条决定的判据。**
    ///
    /// **当输入是实变量时，`AndFunction` 把 `BinaryInputs` 置为 null**，于是投影返回 null、
    /// 构建路径不派生、该符号继续走急切注册。
    ///
    /// **而如果当初选择抛异常，那么"用户写了一个输入不是二元的 `and`"会在<b>构建</b>时炸——
    /// 而它在此之前一直是能建的。**
    ///
    /// **这条判据钉的就是"新旧两条路都还在"：派生为空，而急切行数 > 0。**
    ///
    /// **This is the criterion for the "return null rather than throw" decision.**
    ///
    /// **With a real input `AndFunction` sets `BinaryInputs` to null, so the projection returns null, the build path derives
    /// nothing and eager registration continues.**
    ///
    /// **Had it thrown instead, "the user wrote an `and` whose input is not binary" would explode at <b>build</b> time —
    /// while it had always built fine before.**
    ///
    /// **So the criterion pins "both roads still exist": nothing derived, and the eager rows still there.**
    /// </remarks>
    [Fact]
    public async Task NonBinaryInputs_ShouldDeriveNothingAndKeepTheEagerRows() {
        var real = new RealVar("logic-real");
        var binary = new BinVar("logic-bin");
        var symbol = new AndFunction(
            polynomials: [Linear(real), Linear(binary)],
            name: "logic-nonbinary");

        using LinearMetaModel<Flt64> parent = MetaModel([real, binary], symbol, objective: binary);
        using LinearMechanismModel<Flt64> model = await BuiltAsync(parent);

        model.DeferredFunctionStructures.Should().BeEmpty(
            "a structure that only describes binary inputs cannot represent this symbol");
        model.NumConstraints.Should().BeGreaterThan(
            0, "the eager road must stay open; refusing to derive is not refusing to build");
    }

    private static LinearPolynomial<Flt64> Linear(IVariableItem variable) =>
        new(new[] { new LinearMonomial<Flt64>(Flt64.One, variable) }, Flt64.Zero);

    private static LinearMetaModel<Flt64> MetaModel(
        IVariableItem[] variables,
        IMathFunctionSymbol<Flt64> symbol,
        IVariableItem objective) {
        var parent = new LinearMetaModel<Flt64>();
        foreach (IVariableItem variable in variables) {
            bool addedVariable = parent.Add(variable).IsOk;
            addedVariable.Should().BeTrue();
        }
        bool addedSymbol = parent.Add(new LinearFunctionSymbolAdapter<Flt64>(symbol)).IsOk;
        addedSymbol.Should().BeTrue();
        bool addedObjective = parent.AddObject(
            category: ObjectCategory.Minimum,
            flattenData: new LinearFlattenData<Flt64>(
                new[] { new LinearMonomial<Flt64>(Flt64.One, (ISymbol)objective) }, Flt64.Zero),
            name: "logic-objective",
            displayName: null).IsOk;
        addedObjective.Should().BeTrue();
        return parent;
    }

    private static async Task<LinearMechanismModel<Flt64>> BuiltAsync(LinearMetaModel<Flt64> parent) {
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built =
            await LinearMechanismModel<Flt64>.InvokeAsync(
                parent,
                functionExpansionPolicy: FunctionExpansionPolicy.DeferredNativeFirst);
        if (built is Failed<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> failure) {
            failure.Error.Message.Should().BeEmpty($"the build failed because: {failure.Error.Message}");
        }
        if (built is Fatal<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> fatal) {
            string joined = string.Join(" | ", fatal.Errors.Select(static e => e.Message));
            joined.Should().BeEmpty($"the build failed fatally because: {joined}");
        }
        return ((Ok<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>)built).Value;
    }
}
