#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using System;
using System.Linq;
using System.Reflection;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Model.Intermediate;

/// <summary>
/// 延迟物化的入口判定 / The entry decision for deferred materialisation.
/// </summary>
/// <remarks>
/// B1236 补 `FunctionExpansionPolicy` 与它的第一道守卫。
///
/// **这一批只做基线那个四段函数的第一段，因为它是唯一只需要一个只读属性的一段。**
/// **把能单独成立的一段先做出来，是为了让<b>剩余缺口保持清晰</b>——
/// 现在缺的是两个集合（`DeferredFunctionStructures`、`DeferredFunctionConstraintRegions`），
/// 而不是"整个函数"。**
///
/// adds
///
/// **Only the first of the baseline's four parts is done, because it is the only one that needs a single read-only
/// property.** **Doing the part that stands alone first keeps the <b>remaining gap sharp</b>: what is missing is two
/// collections, not "the whole function".**
/// </remarks>
public class DeferredFunctionMaterializationGateTests
{
    /// <summary>建一个空模型，可指定策略 / Build an empty model with an optional policy.</summary>
    private static LinearMechanismModel<Flt64> Model(FunctionExpansionPolicy? policy = null) {
        var parent = new LinearMetaModel<Flt64>();
        return policy is null
            ? new LinearMechanismModel<Flt64>(
                parent: parent,
                name: parent.Name,
                constraints: Array.Empty<LinearConstraintImpl<Flt64>>(),
                objectFunction: new SingleObject(parent.ObjectCategory, Array.Empty<object>()),
                tokens: new ConcurrentTokenTable<Flt64>(parent.Tokens))
            : new LinearMechanismModel<Flt64>(
                parent: parent,
                name: parent.Name,
                constraints: Array.Empty<LinearConstraintImpl<Flt64>>(),
                objectFunction: new SingleObject(parent.ObjectCategory, Array.Empty<object>()),
                tokens: new ConcurrentTokenTable<Flt64>(parent.Tokens),
                functionExpansionPolicy: policy.Value);
    }

    /// <summary>
    /// **默认策略是 `Eager`，于是默认跳过物化** /
    /// The default policy is `Eager`, so materialisation is skipped by default.
    /// </summary>
    /// <remarks>
    /// **这条判据真正的对象是<b>默认值的方向</b>，不是那个布尔值。**
    ///
    /// **一个没配置过策略的模型必须是保守的：** **`Eager` 的意思是"什么都不延迟"，
    /// 于是这个模型永远走捷径，不会在没人要求的情况下走进延迟物化那条更复杂的路径。**
    ///
    /// **The real subject here is <b>which way the default points</b>, not the boolean.**
    ///
    /// **A model that was never configured must be conservative: `Eager` means "defer nothing", so the model always takes
    /// the short path and never drifts into the more complicated route unasked.**
    /// </remarks>
    [Fact]
    public void AnUnconfiguredModel_ShouldSkipMaterialisation() {
        LinearMechanismModel<Flt64> model = Model();

        model.FunctionExpansionPolicy.Should().Be(
            FunctionExpansionPolicy.Eager,
            "the default must be the conservative one, so an unconfigured model never defers");
        DeferredFunctionMaterializationGate.ShouldSkip(model).Should().BeTrue(
            "an Eager model is returned unchanged, which is the baseline's first guard");
    }

    /// <summary>
    /// **非 `Eager` 的策略不跳过** / A non-`Eager` policy does not skip.
    /// </summary>
    /// <remarks>
    /// **这一条是上一条的反面。** **没有它，把 `ShouldSkip` 写成恒返回 `true` 也能让上一条绿——
    /// 而那会让延迟物化永远不发生。**
    ///
    /// **This is the counterpart to the criterion above.** **Without it, a `ShouldSkip` that always returns `true` would
    /// keep that one green — and it would make materialisation never happen.**
    /// </remarks>
    [Theory]
    [InlineData(FunctionExpansionPolicy.DeferredNativeFirst)]
    [InlineData(FunctionExpansionPolicy.Auto)]
    public void ANonEagerPolicy_ShouldNotSkip(FunctionExpansionPolicy policy) {
        LinearMechanismModel<Flt64> model = Model(policy);

        model.FunctionExpansionPolicy.Should().Be(policy, "the constructor must carry the value through");
        DeferredFunctionMaterializationGate.ShouldSkip(model).Should().BeFalse(
            "only Eager short-circuits; the other two must reach the remaining guards");
    }

    /// <summary>
    /// **策略是只读的** / The policy is read-only.
    /// </summary>
    /// <remarks>
    /// **它是"决定这个模型怎么展开"的输入，不是"记录展开到哪一步"的状态。**
    /// **让它可写，就会出现"物化到一半改策略"这种没有定义的行为。**
    ///
    /// **It is an input deciding how this model expands, not state recording how far expansion got.**
    /// **Making it writable creates the undefined behaviour of "change the policy halfway through materialisation".**
    /// </remarks>
    [Fact]
    public void ThePolicy_ShouldHaveNoSetter() {
        PropertyInfo? property = typeof(LinearMechanismModel<Flt64>).GetProperty("FunctionExpansionPolicy");

        property.Should().NotBeNull("the policy must be a property on the model");
        property!.CanWrite.Should().BeFalse("the policy is an input, not progress state");
    }

    /// <summary>
    /// **入口判定的三段现在都建立在真实状态上** / All three parts of the entry decision now rest on real state.
    /// </summary>
    /// <remarks>
    /// **B1236 这条断言"两个集合都不在"，B1237 改成"只剩最后一样"，B1238 改成"三样都在"。**
    ///
    /// **它现在测的是另一件事：** **`ShouldSkip` 与 `Pending` 都只读模型自己的状态，
    /// <b>不再有任何一段依赖"模型上没有的东西"</b>。**
    ///
    /// **这比"三样都在"更有意义：** **三样都在是<b>数据的完备性</b>，
    /// 而"没有一段在等一个不存在的字段"是<b>接线的完备性</b>。**
    /// **前者可以做而后者不做，于是代码看似齐全却仍然跑不起来——
    /// B1235 的"能力齐备、没有调用方"就是这一类。**
    ///
    /// **B1236 asserted "both collections are absent", B1237 narrowed it to "only one left", and B1238 makes it unnecessary.
    /// It now tests something else: `ShouldSkip` and `Pending` read only the model's own state, with <b>no part waiting on
    /// something the model does not have</b>.**
    ///
    /// **That is more meaningful than "all three are present": presence is <b>data completeness</b>, while "no part awaits a
    /// missing field" is <b>wiring completeness</b>.** **The former can exist without the latter, leaving code that looks
    /// complete and still cannot run — which is exactly B1235's "everything implemented, nothing calls it".**
    /// </remarks>
    [Fact]
    public void TheEntryDecision_ShouldRestOnRealStateOnly() {
        string root = RepositoryRoot();
        string driver = System.IO.File.ReadAllText(
            System.IO.Path.Combine(root, "src/Core/Model/Intermediate/DeferredFunctionFallbackDriver.cs"));

        // **三样状态都被读过，而没有一个"还没做的开关"。**
        // **All three pieces are read, and no "not yet done" switch survives.**
        driver.Should().Contain("model.FunctionExpansionPolicy", "the first guard reads the policy");
        driver.Should().Contain("model.DeferredFunctionStructures", "the filter reads the structure snapshots");
        driver.Should().Contain("model.DeferredFunctionConstraintRegions", "the filter reads the region records");
        driver.Should().NotContain("skipBoundKeyFilter", "the temporary switch must not outlive the gap it marked");
        driver.Should().NotContain("NotSupportedException", "nothing is left refusing to run for a missing field");
    }

    private static string RepositoryRoot() {
        System.IO.DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null) {
            if (System.IO.File.Exists(System.IO.Path.Combine(current.FullName, "ospf-csharp.sln"))) {
                return current.FullName;
            }
            current = current.Parent;
        }
        throw new InvalidOperationException("ospf-csharp.sln not found");
    }
}
