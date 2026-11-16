#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using System;
using System.IO;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Model.Intermediate;

/// <summary>
/// 真实函数符号投影成延迟结构 / A real function symbol projected into a deferred structure.
/// </summary>
/// <remarks>
/// 已有的是"真实函数符号 → 结构快照"这条路。**在此之前，此前的所有判据都<b>手工构造</b>
/// `DeferredFunctionStructure`，而生产路径上没有任何地方构造它。**
///
/// **而"手工造的结构能物化"与"用户写的函数能被物化"是两件事：**
/// **前者证明物化器对不对，后者才证明这条路接没接上用户的写法。**
///
/// The "real function symbol → structure snapshot" road starts here. **Before it, every criterion built
/// `DeferredFunctionStructure` by hand while nothing on the production path built one.**
///
/// **And "a hand-built structure materialises" is not "a function the user wrote gets materialised": the former proves the
/// materialiser right, the latter proves the road is joined to how users write models.**
/// </remarks>
public class DeferredFunctionStructureProjectionTests
{
    /// <summary>
    /// **投影保留函数符号自己的那些变量，一个都不新造** /
    /// The projection keeps the function symbol's own variables and invents none.
    /// </summary>
    /// <remarks>
    /// **这条判据钉的是"同一个变量"而不是"相等的变量"：** **用 `BeSameAs` 而不是 `Be`。**
    ///
    /// **理由很具体：** **`AbsFunction` 在构造时就把 `ResultVar`/`PositiveVar`/`NegativeVar`/`SignVar`
    /// 建好、设好范围、注册进了大 M 推导。** **如果投影<b>新造</b>一套变量，
    /// 那么物化出来的行会引用一套从未出现在模型里的变量——**
 /// **而 `AddConstraint` 会因此报 "variable... is not registered"。**
    ///
    /// **"相等的变量"与"同一个变量"在值语义下无法区分，而在注册表里差别是决定性的。**
    ///
    /// **This pins "the same variable", not "an equal variable": `BeSameAs` rather than `Be`.**
    ///
    /// **The reason is concrete: `AbsFunction` builds `ResultVar`/`PositiveVar`/`NegativeVar`/`SignVar`, sets their ranges
    /// and registers them during Big-M inference.** **If the projection <b>invented</b> its own variables, the materialised
    /// rows would reference variables that never entered the model — and `AddConstraint` would report
    /// "variable... is not registered".**
    ///
    /// **"An equal variable" and "the same variable" are indistinguishable by value and decisive in a registry.**
    /// </remarks>
    [Fact]
    public void TheProjection_ShouldReuseTheFunctionSymbolsOwnVariables() {
        var x = new RealVar("proj-x");
        var function = new AbsFunction<Flt64>(
            polynomial: Linear(x),
            converter: IIntoValue<Flt64>.Identity,
            name: "proj");

        AbsStructure structure = function.ToDeferredStructure(IIntoValue<Flt64>.Identity);

        structure.ResultVariable.Should().BeSameAs(function.ResultVar);
        structure.PositiveVariable.Should().BeSameAs(function.PositiveVar);
        structure.NegativeVariable.Should().BeSameAs(function.NegativeVar);
        structure.SignVariable.Should().BeSameAs(function.SignVar);
        structure.PositiveBigM.Should().Be(function.PositiveBigM);
        structure.NegativeBigM.Should().Be(function.NegativeBigM);
        structure.Name.Should().Be(function.Name);
    }

    /// <summary>
    /// **投影出来的结构被同一套分派认作"支持延迟展开"** /
    /// The projected structure is recognised by the same dispatch as supporting deferred expansion.
    /// </summary>
    /// <remarks>
    /// **如果不认，它就会被待处理过滤器直接丢掉——而丢掉是<b>静默</b>的：
    /// 模型仍然能建、能解，只是少了那几行。**
    ///
    /// **这与"看起来对、实际没接上"是同一个形状：
    /// 一条投影写对了，而它在分派表里对不上号。**
    ///
    /// **If it were not recognised, the pending filter would drop it — and dropping is <b>silent</b>: the model still
    /// builds, still solves, and merely lacks those rows.**
    ///
    /// **Same shape as "looks right, not connected": the projection is correct and it does not match the
    /// dispatch table.**
    /// </remarks>
    [Fact]
    public void TheProjectedStructure_ShouldBeRecognisedAsDeferrable() {
        var x = new RealVar("proj2-x");
        var function = new AbsFunction<Flt64>(Linear(x), IIntoValue<Flt64>.Identity, name: "proj2");

        AbsStructure structure = function.ToDeferredStructure(IIntoValue<Flt64>.Identity);

        structure.SupportsDeferredFallback().Should().BeTrue(
            "the pending filter keys off this dispatch; a mismatch drops the structure silently");
        structure.ResultVariableOrNull().Should().BeSameAs(function.ResultVar);
    }

    /// <summary>
    /// **构建路径现在自己派生结构，并且跳过急切注册** /
    /// The build path now derives structures itself and skips eager registration.
    /// </summary>
    /// <remarks>
    /// **这条判据原本说"构建路径仍然不派生"，现在反过来——而它是<b>自己变红逼着改的</b>。**
    ///
    /// **这是缺口判据第七次生效。** **而这次它还有个特别之处：**
    /// **它当初把"为什么不能直接接"写成了三条可执行的事实（投影不存在于构建路径、
    /// 投影本身存在、`RegisterFunctionConstraints` 仍在），所以修改它的人必须同时面对那第三条。**
    ///
    /// **如果当初只写"后续把它接上"，那这一段就不会有"跳过"这半边——
    /// 而缺了跳过，模型里会有两套描述同一个 `abs` 的行，且求解成功。**
    ///
    /// **It used to say "the build path still does not derive"; now it turns around — and it was <b>the criterion
    /// going red that forced the edit</b>.**
    ///
    /// **Seventh time a gap criterion has done its job.** **And this one is special: it had written "why it cannot be wired
    /// yet" as three executable facts (no projection call in the build path, the projection exists, `RegisterFunctionConstraints`
    /// is still there), so whoever edits it must confront the third.**
    ///
    /// **Had it merely said "wire it next batch", this round would have had no "skip" half — and without the skip the model
    /// carries two row sets describing the same `abs`, and still solves.**
    /// </remarks>
    [Fact]
    public void TheBuildPath_ShouldNowDeriveAndSkip() {
        string root = RepositoryRoot();

        string model = File.ReadAllText(System.IO.Path.Combine(root, "src/Core/Model/Mechanism/MechanismModel.cs"));
        model.Should().Contain("ToDeferredStructure(",
            "the build path must derive snapshots from the function symbols");
        model.Should().Contain("deferredSymbols.Contains(symbol)",
            "deriving without skipping leaves two row sets for the same function");
        model.Should().Contain("RegisterFunctionConstraints(model, tokens.Symbols, deferredSymbols)",
            "the skip set has to reach the registration step, or it does nothing");
    }

    private static LinearPolynomial<Flt64> Linear(IVariableItem variable) =>
        new(new[] { new LinearMonomial<Flt64>(Flt64.One, variable) }, Flt64.Zero);

    private static string RepositoryRoot() {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null) {
            if (File.Exists(System.IO.Path.Combine(current.FullName, "ospf-csharp.sln"))) {
                return current.FullName;
            }
            current = current.Parent;
        }
        throw new InvalidOperationException("ospf-csharp.sln not found");
    }
}
