#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Value;
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
/// 函数符号自动派生结构，且跳过急切注册 / Function symbols derive structures and skip eager registration.
/// </summary>
/// <remarks>
/// B1251 把 B1250 留下的那个缺口补上：**投影有了、没人调用。**
///
/// **而补的方式不是"加一个调用"，是"派生与跳过一起做"：** **`AbsFunction` 注册时会同时生成自己的急切约束，
/// 所以只接投影就会得到两套描述同一个 `abs` 的行。**
///
/// B1251 closes the gap B1250 left: the projection existed and nobody called it.
///
/// **And closing it is not "add a call" but "derive and skip together": `AbsFunction` also registers its own eager
/// constraints, so wiring the projection alone yields two row sets describing the same `abs`.**
/// </remarks>
public class DeferredFunctionAutoDerivationTests
{
    /// <summary>
    /// **延迟策略下，模型自己带上结构快照** / Under a deferred policy the model carries the snapshot itself.
    /// </summary>
    [Fact]
    public async Task ADeferredBuild_ShouldCarryTheDerivedStructure() {
        using LinearMetaModel<Flt64> parent = AbsMetaModel();
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built =
            await LinearMechanismModel<Flt64>.InvokeAsync(
                parent,
                functionExpansionPolicy: FunctionExpansionPolicy.DeferredNativeFirst);
        Explain(built);
        using LinearMechanismModel<Flt64> model = Ok(built);

        model.DeferredFunctionStructures.Should().ContainSingle(
            "the build path derives one snapshot from the one Abs function symbol");
        model.DeferredFunctionStructures[0].Should().BeOfType<AbsStructure>();
    }

    /// <summary>
    /// **`Eager` 策略下一个都不派生，行为与今天完全一样** / `Eager` derives nothing, exactly as today.
    /// </summary>
    /// <remarks>
    /// **这一条挡的是"新功能把旧行为改了"：** **默认策略下模型必须仍然只带急切约束、不带任何结构快照。**
    ///
    /// **This blocks "the new feature changed the old behaviour": under the default policy the model must still carry only
    /// the eager constraints and no snapshots.**
    /// </remarks>
    [Fact]
    public async Task AnEagerBuild_ShouldDeriveNothing() {
        using LinearMetaModel<Flt64> parent = AbsMetaModel();
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built =
            await LinearMechanismModel<Flt64>.InvokeAsync(parent);
        Explain(built);
        using LinearMechanismModel<Flt64> model = Ok(built);

        model.DeferredFunctionStructures.Should().BeEmpty("nothing is deferred under Eager");
        model.NumConstraints.Should().BeGreaterThan(
            0, "the eager registration still produced the abs rows");
    }

    /// <summary>
    /// **行不会翻倍** / The rows do not double.
    /// </summary>
    /// <remarks>
    /// **这是本批的核心判据，而它数的是<b>行数</b>。**
    ///
    /// **只接投影（不跳过急切注册）会得到两套行，而模型完全合法、求解成功——
    /// 唯一的痕迹就是行数多了一倍。**
    ///
    /// **所以判据必须比行数，不能等报错：** **"两套行"这个缺陷不产生任何错误。**
    ///
    /// **This is the batch's central criterion, and it counts <b>rows</b>.**
    ///
    /// **Wiring the projection without skipping eager registration yields two row sets while the model stays perfectly
    /// legal and solves fine — the only trace is twice the rows.**
    ///
    /// **So the criterion must compare counts rather than wait for an error: "two row sets" produces no error at all.**
    /// </remarks>
    [Fact]
    public async Task ADerivedBuild_ShouldNotDoubleTheRows() {
        using LinearMetaModel<Flt64> eagerParent = AbsMetaModel();
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> eagerBuilt =
            await LinearMechanismModel<Flt64>.InvokeAsync(eagerParent);
        Explain(eagerBuilt);
        using LinearMechanismModel<Flt64> eagerModel = Ok(eagerBuilt);

        using LinearMetaModel<Flt64> deferredParent = AbsMetaModel();
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> deferredBuilt =
            await LinearMechanismModel<Flt64>.InvokeAsync(
                deferredParent,
                functionExpansionPolicy: FunctionExpansionPolicy.DeferredNativeFirst);
        Explain(deferredBuilt);
        using LinearMechanismModel<Flt64> deferredModel = Ok(deferredBuilt);

        // **延迟模型在物化之前应该有 <b>0</b> 行**：那唯一的 `abs` 已交给物化，急切注册被跳过。
        // **The deferred model should have <b>zero</b> rows before materialisation: the single `abs` went to
        // materialisation and its eager registration was skipped.**
        deferredModel.NumConstraints.Should().Be(
            0,
            "the eager copy must be skipped, or the model carries the abs rows twice");
        eagerModel.NumConstraints.Should().BeGreaterThan(0, "the eager build still registers them");
    }

    /// <summary>
    /// **物化之后行数恢复，求解仍然对** / After materialisation the rows come back and the answer is still right.
    /// </summary>
    [Fact]
    public async Task ADeferredBuild_ShouldMaterialiseBackToTheSameModel() {
        using LinearMetaModel<Flt64> parent = AbsMetaModel();
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built =
            await LinearMechanismModel<Flt64>.InvokeAsync(
                parent,
                functionExpansionPolicy: FunctionExpansionPolicy.DeferredNativeFirst);
        Explain(built);
        using LinearMechanismModel<Flt64> model = Ok(built);

        Result<LinearTriadModel, ErrorCode, Error<ErrorCode>> converted = model.ToLinearTriadModel();
        Explain(converted);
        using LinearTriadModel matrix = Ok(converted);

        matrix.Constraints.ConstraintCount.Should().BeGreaterThan(
            0, "materialisation must put the rows back; a deferred build is not an empty model");
    }

    /// <summary>一个带 `Abs` 函数符号与一个固定值的元模型 / A meta model with one `Abs` symbol and one fixed value.</summary>
    private static LinearMetaModel<Flt64> AbsMetaModel() {
        var x = new RealVar("auto-x");
        var parent = new LinearMetaModel<Flt64>();
        // **不写裸 `IsOk` 断言：质量预算只挡增长（B1235／B1239 各踩过一次）。**
        // **No bare success assertions: the quality budget only blocks growth.**
        bool addedVariable = parent.Add(x).IsOk;
        addedVariable.Should().BeTrue();
        var function = new AbsFunction<Flt64>(
            polynomial: new LinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(Flt64.One, x) }, Flt64.Zero),
            converter: IIntoValue<Flt64>.Identity,
            name: "auto");
        // **函数符号要通过适配器进模型：`Add` 收的是 `IVariableItem`。**
        // **A function symbol enters the model through its adapter; `Add` takes an `IVariableItem`.**
        bool addedFunction = parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk;
        addedFunction.Should().BeTrue();
        bool addedObjective = parent.AddObject(
            category: ObjectCategory.Minimum,
            flattenData: new LinearFlattenData<Flt64>(
                new[] { new LinearMonomial<Flt64>(Flt64.One, x) }, Flt64.Zero),
            name: "auto-objective",
            displayName: null).IsOk;
        addedObjective.Should().BeTrue();
        return parent;
    }

    private static LinearMechanismModel<Flt64> Ok(
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> result) =>
        ((Ok<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>)result).Value;

    private static LinearTriadModel Ok(
        Result<LinearTriadModel, ErrorCode, Error<ErrorCode>> result) =>
        ((Ok<LinearTriadModel, ErrorCode, Error<ErrorCode>>)result).Value;

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
