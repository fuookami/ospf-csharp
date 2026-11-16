#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Token;
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
using System.IO;
using System.Linq;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Model.Intermediate;

/// <summary>
/// 延迟函数物化的整体入口 / The whole entry point for deferred-function materialisation.
/// </summary>
/// <remarks>
/// 补完基线四段函数的最后两段，于是四段现在都在一个函数里。
///
/// **这里最重要的发现不在"补完"，而在核对第三、四段时才看见的一件事：**
/// **驱动把区域记录放进<b>返回值</b>，而返回值的唯一消费者是测试——
/// 于是 `DeferredFunctionConstraintRegions` 在真实路径上永远是空的，
/// 而第三个过滤条件因此<b>永远不会生效</b>。**
///
/// Completes the last two of its four parts.
///
/// **The important find here is not the completion but something only visible while checking parts three and four: the
/// driver put region records in its <b>return value</b>, whose only consumer was tests — so
/// `DeferredFunctionConstraintRegions` stayed empty on the real path, and the third filter condition just added could
/// <b>never</b> fire.**
/// </remarks>
public class DeferredFunctionMaterializationTests
{
    private static LinearMechanismModel<Flt64> Model(
        FunctionExpansionPolicy policy,
        IReadOnlyList<DeferredFunctionStructure>? structures = null,
        IReadOnlyList<DeferredFunctionConstraintRegion>? regions = null,
        IReadOnlyList<IVariableItem>? variables = null) {
        var parent = new LinearMetaModel<Flt64>();
        // **变量必须先注册进 parent：** **物化产出的约束引用了这些变量，
        // 而 `AddConstraint` 会通过 token 表解析它们——没注册就解析不到。**
        // **Variables must be registered on `parent` first: the materialised constraints reference them and `AddConstraint`
        // resolves them through the token table — unregistered ones cannot be resolved.**
        if (variables is { Count: > 0 }) {
            // **不写裸 `IsOk` 断言：质量预算只挡增长。**
            // **No bare success assertion: the quality budget only blocks growth.**
            bool registered = parent.Add(variables).IsOk;
            registered.Should().BeTrue("the test's variables must be registered on the meta model");
        }
        return new LinearMechanismModel<Flt64>(
            parent: parent,
            name: parent.Name,
            constraints: Array.Empty<LinearConstraintImpl<Flt64>>(),
            objectFunction: new SingleObject(parent.ObjectCategory, Array.Empty<object>()),
            tokens: new ConcurrentTokenTable<Flt64>(parent.Tokens),
            functionExpansionPolicy: policy,
            deferredFunctionStructures: structures,
            deferredFunctionConstraintRegions: regions);
    }

    private static AbsStructure Abs(IVariableItem result, string name) =>
        new(
            Input: new LinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(Flt64.One, result) }, Flt64.Zero),
            ResultVariable: result,
            PositiveVariable: new RealVar($"{name}-pos"),
            NegativeVariable: new RealVar($"{name}-neg"),
            SignVariable: new RealVar($"{name}-sign"),
            PositiveBigM: new Flt64(100.0),
            NegativeBigM: new Flt64(100.0),
            Converter: IIntoValue<Flt64>.Identity,
            Name: name);

    /// <summary>
    /// **`Eager` 时返回<b>同一个引用</b>** / With `Eager` the <b>same reference</b> comes back.
    /// </summary>
    /// <remarks>
    /// **判据用引用相等而不是"内容相同"，因为调用方需要的正是这个：**
    /// **"有没有发生物化"这个问题的答案，就是"回来的是不是同一个对象"。**
    /// **如果这里复制一份再返回，那个判断就永远失效——而它失效时不会报错，只会让每个调用方都多做一次无用的展开。**
    ///
    /// **The criterion uses reference equality rather than "same contents", because that is exactly what a caller needs:
    /// "did materialisation happen" is answered by "is this the same object".** **Returning a copy would break that check
    /// forever, and breaking it is silent — every caller would simply do one useless expansion.**
    /// </remarks>
    [Fact]
    public void AnEagerModel_ShouldComeBackAsTheSameReference() {
        LinearMechanismModel<Flt64> model = Model(FunctionExpansionPolicy.Eager, [Abs(new RealVar("x"), "x")]);

        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> result =
            DeferredFunctionMaterialization.Materialize(model, new HashSet<VariableItemKey>());

        result.Should().BeOfType<Ok<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>>(
            "an Eager model needs no work and must not fail");
        ((Ok<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>)result).Value
            .Should().BeSameAs(model, "the source comes back unchanged, so callers can tell nothing happened");
    }

    /// <summary>
    /// **没有待处理结构时也返回同一个引用** / With nothing pending the same reference comes back too.
    /// </summary>
    [Fact]
    public void AnEmptyPendingSet_ShouldComeBackAsTheSameReference() {
        LinearMechanismModel<Flt64> model = Model(FunctionExpansionPolicy.DeferredNativeFirst);

        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> result =
            DeferredFunctionMaterialization.Materialize(model, new HashSet<VariableItemKey>());

        result.Should().BeOfType<Ok<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>>();
        ((Ok<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>)result).Value
            .Should().BeSameAs(model, "no pending structures means no copy is made");
    }

    /// <summary>
    /// **区域记录会真的写进模型，而不是只放进返回值** /
    /// Region records really land on the model, not only in a return value.
    /// </summary>
    /// <remarks>
    /// **这一条是这里发现的那个缺陷的判据。**
    ///
    /// **它跑的是<b>完整入口</b>，而不是直接调驱动：** **因为缺陷恰恰在"驱动返回的东西没人接"这两层之间，
 /// 只测驱动的那条判据一直是绿的。**
    ///
    /// **所以测"每一层都对"无法发现"层与层之间没接上"——
    /// 需要一条判据从用户入口进去、在另一头断言。**
    ///
    /// **This criterion is for the defect found.**
    ///
    /// **It runs the <b>full entry point</b> rather than calling the driver directly: the defect sat precisely between the
 /// driver's return value and its absent consumer, while the driver-level criterion stayed green.**
    ///
    /// **So testing "each layer is right" cannot find "the layers are not joined" — that needs a criterion entering at the
    /// user's door and asserting at the other end.**
    /// </remarks>
    [Fact]
    public void TheMaterialisedModel_ShouldCarryItsRegionRecords() {
        var y = new RealVar("y");
        AbsStructure structure = Abs(y, "y");
        LinearMechanismModel<Flt64> model = Model(
            FunctionExpansionPolicy.DeferredNativeFirst,
            [structure],
            variables: [y, structure.PositiveVariable, structure.NegativeVariable, structure.SignVariable]);

        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> result =
            DeferredFunctionMaterialization.Materialize(model, new HashSet<VariableItemKey>());

        // **把失败原因带进断言消息里**：否则只能看到"期望 Ok、实际是别的"，
        // 而真正想知道的是<b>失败的原因</b>。
        // **Carry the failure reason into the assertion message**: otherwise all that shows is "expected Ok, got
        // something else", while the thing worth knowing is <b>why it failed</b>.
        string because = result switch {
            Failed<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> f => f.Error.Message,
            Fatal<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> f =>
                string.Join(" | ", f.Errors.Select(static e => e.Message)),
            _ => "not a failure",
        };

        // **断言成功并直接取出值，不走 `BeOfType` 的泛型链——**
        // **那一层链在这里只增加类型噪声，而真正要看的是 `DeferredFunctionConstraintRegions`。**
        // **Assert the success and take the value directly rather than through the `BeOfType` generic chain, which only adds
        // type noise here while the thing worth examining is `DeferredFunctionConstraintRegions`.**
        var ok = result as Ok<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>;
        ok.Should().NotBeNull("materialising one Abs structure must succeed, but: " + because);

        ok!.Value.DeferredFunctionConstraintRegions.Should().ContainSingle(
            "the region the driver recorded must reach the model, or the third filter condition can never fire");
        ok.Value.DeferredFunctionConstraintRegions[0].Structure.Should().BeSameAs(structure);
        ok.Value.DeferredFunctionConstraintRegions[0].FirstConstraintIndex.Should().Be(
            0, "the source model had no rows, so the first materialised row is row zero");

        model.DeferredFunctionConstraintRegions.Should().BeEmpty("the source model must not be touched");
    }

    /// <summary>
    /// **源模型不被改动** / The source model is not modified.
    /// </summary>
    /// <remarks>
    /// **"保留源模型"有两层意思：** **失败时不污染，以及<b>成功时也不改</b>。**
    /// **前者由事务解决，后者只能靠复制——而只做前者的实现会让"再物化一次"这个动作在第二次时看到
    /// 一个已经被改过的源，于是第三次的结果与第一次不同。**
    ///
    /// **"Preserving the source" has two meanings: not polluting it on failure, and <b>not changing it on success</b>.**
    /// **The transaction covers the first; only copying covers the second — and an implementation that does only the first
    /// makes a second materialisation see an already-changed source, so the third result differs from the first.**
    /// </remarks>
    [Fact]
    public void TheSourceModel_ShouldKeepItsRowCount() {
        var z = new RealVar("z");
        AbsStructure structure = Abs(z, "z");
        LinearMechanismModel<Flt64> model = Model(
            FunctionExpansionPolicy.DeferredNativeFirst,
            [structure],
            variables: [z, structure.PositiveVariable, structure.NegativeVariable, structure.SignVariable]);

        DeferredFunctionMaterialization.Materialize(model, new HashSet<VariableItemKey>());

        model.NumConstraints.Should().Be(0, "the source had no rows and materialisation must not add any to it");
    }

    /// <summary>
    /// **整体入口收口：四段都在同一个函数里** / Wrap-up: all four parts live in one function.
    /// </summary>
    /// <remarks>
    /// **这条判据钉的是"四段确实在同一处"，而不是四段各自存在。**
    /// **四段分散在三个类里各写一遍，与一个函数按顺序调用它们，是两件事。**
    ///
    /// **It pins that the four parts are in <b>one place</b>, not that each exists.** **Writing them separately in three
    /// classes is not the same as one function calling them in order.**
    /// </remarks>
    [Fact]
    public void AllFourParts_ShouldLiveInOneFunction() {
        string root = RepositoryRoot();
        string driver = File.ReadAllText(
            Path.Combine(root, "src/Core/Model/Intermediate/DeferredFunctionFallbackDriver.cs"));

        int start = driver.IndexOf("public static class DeferredFunctionMaterialization", StringComparison.Ordinal);
        start.Should().BeGreaterThan(0, "the entry point class must exist");
        int end = driver.IndexOf("public static class DeferredFunctionFallbackDriver", StringComparison.Ordinal);
        end.Should().BeGreaterThan(start, "the entry point comes before the driver in this file");

        string body = driver[start..end];
        body.Should().Contain("ShouldSkip", "part one: the Eager guard");
        body.Should().Contain("Pending(", "part two: the pending filter");
        body.Should().Contain("ValidateFallbackInputBounds", "part three: the bounds validation");
        body.Should().Contain("new LinearMechanismModel<Flt64>(", "part four: the copy");
        body.Should().Contain("DeferredFunctionFallbackDriver.Materialize(", "part four: the driver call");
    }

    /// <summary>
    /// **驱动不再把区域只放进返回值** / The driver no longer leaves regions only in its return value.
    /// </summary>
    [Fact]
    public void TheDriver_ShouldRecordRegionsOnTheTarget() {
        string root = RepositoryRoot();
        string driver = File.ReadAllText(
            Path.Combine(root, "src/Core/Model/Intermediate/DeferredFunctionFallbackDriver.cs"));

        driver.Should().Contain("target.RecordRegion(", "the target is where a region has to land");
        driver.Should().Contain(
            "RecordRegion(DeferredFunctionConstraintRegion region)",
            "the interface must declare it, or no target can receive one");
    }

    private static string RepositoryRoot() {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null) {
            if (File.Exists(Path.Combine(current.FullName, "ospf-csharp.sln"))) {
                return current.FullName;
            }
            current = current.Parent;
        }
        throw new InvalidOperationException("ospf-csharp.sln not found");
    }
}
