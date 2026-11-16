#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Math.Algebra.ValueRange;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Model.Intermediate;

/// <summary>
/// 延迟结构的集合与待处理过滤 / The deferred-structure collection and the pending filter.
/// </summary>
/// <remarks>
/// 补三样缺失状态里的第二样：`DeferredFunctionStructures`，以及四段函数的第二段。
///
/// **这一段只实现了三个条件中的两个，第三个依赖仍缺的 `DeferredFunctionConstraintRegions`。**
/// **而"缺一个条件"的正确写法与"缺三个条件"不同：** **前者要给一个能被看到的开关。**
///
/// **Two of its three conditions are implemented; the third needs the still-missing
/// `DeferredFunctionConstraintRegions`.** **And "one condition missing" must be written differently from "three
/// missing": the former needs a visible switch.**
/// </remarks>
public class DeferredFunctionPendingFilterTests
{
    private static LinearMechanismModel<Flt64> Model(
        IReadOnlyList<DeferredFunctionStructure>? structures = null) {
        var parent = new LinearMetaModel<Flt64>();
        return new LinearMechanismModel<Flt64>(
            parent: parent,
            name: parent.Name,
            constraints: Array.Empty<LinearConstraintImpl<Flt64>>(),
            objectFunction: new SingleObject(parent.ObjectCategory, Array.Empty<object>()),
            tokens: new ConcurrentTokenTable<Flt64>(parent.Tokens),
            functionExpansionPolicy: FunctionExpansionPolicy.DeferredNativeFirst,
            deferredFunctionStructures: structures);
    }

    /// <summary>
    /// **没有延迟结构时集合是空的，而不是 `null`** /
    /// With no deferred structures the collection is empty, not `null`.
    /// </summary>
    /// <remarks>
    /// **`null` 与空集合不是一回事：** **`null` 表示"调用方没提供"，空集合表示"提供了，是空的"。**
    /// **构造参数接受 `null` 是为了让旧调用点不必改，而属性永远返回非 `null`——读的一方不该被迫区分这两件事。**
    ///
    /// **`null` and empty are not the same: `null` means "the caller supplied nothing", empty means "supplied and empty".
    /// The constructor accepts `null` so old call sites need not change, while the property always returns non-`null` — a
    /// reader should not be forced to tell them apart.**
    /// </remarks>
    [Fact]
    public void WithNoStructures_TheCollectionIsEmptyNotNull() {
        LinearMechanismModel<Flt64> model = Model();

        model.DeferredFunctionStructures.Should().NotBeNull("the property never exposes a null");
        model.DeferredFunctionStructures.Should().BeEmpty("a model with no deferred structures has an empty list");
    }

    /// <summary>
    /// **待处理过滤按原顺序保留结构** / The pending filter keeps structures in their original order.
    /// </summary>
    /// <remarks>
    /// **顺序是行为的一部分：物化会按这个顺序<b>追加约束行</b>，于是它决定了最终模型里行的次序。**
    /// **一个"只是过滤"的函数如果顺手换了顺序，会让同一份输入产生两份不同的模型。**
    ///
    /// **Order is part of the behaviour: materialisation appends constraint rows in this order, so it decides the row order
    /// of the resulting model.** **A function that "only filters" but reorders would turn one input into two different
    /// models.**
    /// </remarks>
    [Fact]
    public void ThePendingFilter_ShouldKeepTheOriginalOrder() {
        IVariableItem first = new RealVar("first");
        IVariableItem second = new RealVar("second");
        AbsStructure a = Abs(first, "a");
        AbsStructure b = Abs(second, "b");
        LinearMechanismModel<Flt64> model = Model([a, b]);

        IReadOnlyList<DeferredFunctionStructure> pending = DeferredFunctionMaterializationGate.Pending(
            model,
            new HashSet<VariableItemKey>());

        pending.Should().HaveCount(2);
        pending[0].Should().BeSameAs(a, "the original order must survive the filter");
        pending[1].Should().BeSameAs(b);
    }

    /// <summary>
    /// **原生建模的键被排除** / Keys the caller builds natively are excluded.
    /// </summary>
    /// <remarks>
    /// **这一条挡的是"同一个函数被建了两遍"：** **调用方说"这个结果我自己原生建"，
    /// 而物化又把它的 fallback 建了一遍——于是模型里同时存在两条描述同一个 `y` 的约束。**
    ///
    /// **This blocks "one function built twice": the caller says "I build this result natively", and materialisation also
    /// builds its fallback — leaving two constraint sets describing the same `y`.**
    /// </remarks>
    [Fact]
    public void AKeyBuiltNatively_ShouldBeExcluded() {
        IVariableItem kept = new RealVar("kept");
        IVariableItem excluded = new RealVar("excluded");
        LinearMechanismModel<Flt64> model = Model([Abs(kept, "kept"), Abs(excluded, "excluded")]);

        IReadOnlyList<DeferredFunctionStructure> pending = DeferredFunctionMaterializationGate.Pending(
            model,
            new HashSet<VariableItemKey> { excluded.Key });

        pending.Should().ContainSingle();
        pending[0].Should().BeSameAs(model.DeferredFunctionStructures[0], "the other structure must survive");
    }

    /// <summary>
    /// **已物化过的结构不再进入待处理集合** / An already-materialised structure never re-enters the pending set.
    /// </summary>
    /// <remarks>
    /// **这是第三个过滤条件，也是这里真正补上的东西。**
    ///
    /// **缺它的后果不是"少做一件事"，而是<b>多做一次</b>：** **同一个结构会被物化两次，
    /// 于是模型里出现两条描述同一个 `y` 的约束。**
    ///
    /// **而求解器不会报错：** **两条都满足的解是存在的（就是本来那个解），
    /// 所以求解"成功"，只是模型比应有的多了一段。**
    ///
    /// **这类缺陷的发现方式是"模型的约束数不对"，而不是"求解失败"——
    /// 所以判据必须数<b>待处理集合的成员</b>，而不是等求解报错。**
    ///
    /// **This is the third filter condition, and the thing this layer actually adds.**
    ///
    /// **Its absence does not leave one thing undone — it does one thing <b>twice</b>: the same structure is materialised
    /// again, leaving two constraint sets describing the same `y`.**
    ///
    /// **The solver does not complain: a solution satisfying both exists (the original one), so the solve "succeeds" and
    /// the model merely carries an extra slice.**
    ///
    /// **Such a defect surfaces as "the constraint count is wrong", not as a failed solve — so the criterion must count
    /// <b>members of the pending set</b> rather than wait for an error.**
    /// </remarks>
    [Fact]
    public void AnAlreadyMaterialisedStructure_ShouldBeExcluded() {
        IVariableItem done = new RealVar("done");
        IVariableItem fresh = new RealVar("fresh");
        AbsStructure alreadyMaterialised = Abs(done, "done");
        AbsStructure pendingOne = Abs(fresh, "fresh");

        var parent = new LinearMetaModel<Flt64>();
        LinearMechanismModel<Flt64> model = Rebuild(
            parent,
            [alreadyMaterialised, pendingOne],
            [new DeferredFunctionConstraintRegion(alreadyMaterialised, 0, 3)]);

        IReadOnlyList<DeferredFunctionStructure> pending = DeferredFunctionMaterializationGate.Pending(
            model,
            new HashSet<VariableItemKey>());

        pending.Should().ContainSingle("only the structure without a recorded region is still pending");
        pending[0].Should().BeSameAs(pendingOne);
        model.DeferredFunctionConstraintRegions.Should().ContainSingle(
            "the region record is what makes the other structure already-done");
    }

    /// <summary>
    /// **那个临时开关应该已经不存在** / The temporary switch should be gone.
    /// </summary>
    /// <remarks>
    /// **曾经有一个 `skipBoundKeyFilter` 参数，它的用途是"让缺口在被使用的那一刻出现"。**
    /// **缺口补上之后，开关如果留着，就变成了一个<b>可以合法地物化两次</b>的入口——
    /// 那比没有开关更糟，因为它让错误看起来是被允许的。**
    ///
    /// **这条判据钉的是"后半部分已经履行"。** **只做前半部分的临时开关会沉淀成永久缺陷。**
    ///
    /// **A `skipBoundKeyFilter` parameter used to exist whose purpose was "make the gap appear at the moment it is
    /// used".** **Once the gap is closed, keeping it turns the switch into an entry point where materialising twice is
    /// <b>legal</b> — worse than no switch, because it makes the error look permitted.**
    ///
    /// **This criterion pins that the second half was honoured.** **A switch that only gets the first half hardens into a
    /// permanent defect.**
    /// </remarks>
    [Fact]
    public void TheTemporarySwitch_ShouldBeGone() {
        string root = RepositoryRoot();
        string driver = System.IO.File.ReadAllText(
            System.IO.Path.Combine(root, "src/Core/Model/Intermediate/DeferredFunctionFallbackDriver.cs"));

        driver.Should().NotContain("skipBoundKeyFilter", "the gap is closed, so the switch must not survive");
    }

    /// <summary>
    /// **三样状态现在都在** / All three pieces are now present.
    /// </summary>
    /// <remarks>
    /// **这条断言经过三次改写：从"两个集合都不在"到"只剩最后一样"，再到"三样都在"。**
    ///
    /// **三次改动都是判据<b>变红</b>逼出来的，而不是有人想起来去更新它。**
    /// **这正是缺口判据的用途：** **它让"缺口被补上"这件事无法静默通过——
    /// 补的人必须回来把这句话也改掉，于是文档与代码不会分家。**
    ///
    /// **而它现在还可以再测一件事：** **三样都在之后，这个判据不再有"后续会变红"的预期，
    /// 所以它从此变成了一条普通的结构判据——它的生命周期结束了。**
    /// **一条缺口判据的终点，就是它不再会因为"变好"而变红。**
    ///
    /// **This assertion went through three versions: "neither collection is present", "only the last one is left", and now
    /// "all three are present".**
    ///
    /// **All three edits were forced by the criterion going <b>red</b>, not by someone remembering to update it.** **That is
    /// the point of a gap criterion: it makes closing a gap impossible to pass silently, since the closer must come back and
    /// change this sentence too, so docs and code cannot drift apart.**
    ///
    /// **And it can now assert one more thing: with all three present it no longer expects to go red later, so it
    /// becomes an ordinary structural criterion — its lifecycle is over.** **A gap criterion ends the day it can no longer
    /// go red by things getting better.**
    /// </remarks>
    [Fact]
    public void AllThreePieces_ShouldNowBePresent() {
        string root = RepositoryRoot();
        string model = System.IO.File.ReadAllText(
            System.IO.Path.Combine(root, "src/Core/Model/Mechanism/MechanismModel.cs"));

        // **只看代码行，不看注释**：注释里解释过这些名字，扫全文件会命中自己的说明。
        // **Only code lines, not comments**: these names appear in the explanations, and scanning the whole file would hit
        // them.
        string[] code = model.Split('\n')
            .Where(static line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal))
            .ToArray();

        code.Should().Contain(line => line.Contains("FunctionExpansionPolicy", StringComparison.Ordinal),
            "the policy is part of the build path");
        code.Should().Contain(line => line.Contains("DeferredFunctionStructures", StringComparison.Ordinal),
            "the structure snapshots are part of the build path");
        code.Should().Contain(line => line.Contains("DeferredFunctionConstraintRegions", StringComparison.Ordinal),
            "the region records are part of the build path, so the third filter condition can be applied");
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

    /// <summary>建一个带区域记录的模型 / Build a model carrying region records.</summary>
    private static LinearMechanismModel<Flt64> Rebuild(
        LinearMetaModel<Flt64> parent,
        IReadOnlyList<DeferredFunctionStructure> structures,
        IReadOnlyList<DeferredFunctionConstraintRegion> regions) =>
        new(
            parent: parent,
            name: parent.Name,
            constraints: Array.Empty<LinearConstraintImpl<Flt64>>(),
            objectFunction: new SingleObject(parent.ObjectCategory, Array.Empty<object>()),
            tokens: new ConcurrentTokenTable<Flt64>(parent.Tokens),
            functionExpansionPolicy: FunctionExpansionPolicy.DeferredNativeFirst,
            deferredFunctionStructures: structures,
            deferredFunctionConstraintRegions: regions);

    /// <summary>
    /// **造一个真的 `AbsStructure`** / Build a real `AbsStructure`.
    /// </summary>
    /// <remarks>
    /// **用真结构而不是假结构，是因为 `SupportsDeferredFallback` 是一次<b>类型分派</b>：
    /// 它只认七个具体类型，别的都返回 `false`。**
    /// **所以一个假结构走不到"被过滤"那一步——它会被第一个条件直接排除，
    /// 而那条判据就变成了在测分派表，不是在测过滤。**
    ///
    /// **A real structure rather than a fake, because `SupportsDeferredFallback` is a <b>type dispatch</b>: it recognises
    /// seven concrete types and returns `false` for everything else.** **A fake never reaches the filtering step — the first
    /// condition drops it, and the criterion would be testing the dispatch table instead of the filter.**
    /// </remarks>
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
}
