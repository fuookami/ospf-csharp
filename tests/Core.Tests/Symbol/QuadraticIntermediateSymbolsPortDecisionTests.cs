#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.MultiArray;
using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Symbol;

/// <summary>
/// **`QuadraticIntermediateSymbols` 这个工厂对象不移植，理由与同族另一项不同** /
/// The `QuadraticIntermediateSymbols` factory object is not implemented, for a different reason than the sibling decision.
/// </summary>
/// <remarks>
/// 逐项对照的对象是。
///
/// **先数叶子：** **`IAbstractSymbolCombination`、`SymbolCombination`、
/// `QuantitySymbolCombination` 本仓<b>全都有</b>**（`src/Core/Symbol/SymbolCombination.cs`）。
///
/// **缺的只有 `QuadraticIntermediateSymbols`——一个 `data object`，十个 `operator fun invoke` 重载，
/// 每个都是"按形状造一个装满空二次表达式符号的组合"。**
///
/// **而它在<b>零生产调用方</b>：** **`git grep` 只命中它自己的文件与一个测试
///。**
///
/// **这与理由不同，值得分清：**
///
/// | 批次 | 不移植的理由 |
/// | --- | --- |
/// | — | 基类的三条内容在本仓<b>要么已有、要么不可观测、要么是空检查</b> |
/// | — | **功能可以造出来，只是<b>没有使用者</b>** |
///
/// **还有一层更精确的理由：** **那十个重载的<b>返回类型是 typealias</b>
/// （`typealias QuadraticExpressionSymbols1<V> = SymbolCombination<QuadraticExpressionSymbol<V>, Shape1>`），
/// **而 C# 用结构化类型表达它——`SymbolCombination<QuadraticExpressionSymbol, Shape1>` 本来就写得出来，
/// **不需要为它造一个名字。所以工厂缺的不只是"使用者"，还缺"一个 C# 需要的类型"。**
///
/// **本仓已经为同一件事写过一次：** **`Framework.Bpp2d` 里 `Capacity` 的注释说
/// "`LinearIntermediateSymbols1` 是 C# 无法表达的 Kotlin typealias，所以改用 `IReadOnlyList<...>`"。**
/// **这里是那条先例的第二次应用。**
///
/// **One more, sharper reason: the ten overloads' <b>return types are typealiases</b>, and C# expresses those
/// structurally — `SymbolCombination<QuadraticExpressionSymbol, Shape1>` is already writable without inventing a name.**
/// **So the factory lacks not only a caller but also a type C# would need.** **This repo already wrote that down in
/// `Capacity`: `LinearIntermediateSymbols1` is a Kotlin typealias C# cannot express.** **This is that precedent's second
/// application.**
/// **"没人用"与"做了也没意义"是两句话。** **前者会变（有一天有人要），后者不会。**
/// **所以这里的判据必须能区分它们：** **它不证明"做不到"，而证明"现在做得到、只是没做"。**
///
/// The survey item is its (843 lines).
///
/// **Counting the leaves first: `IAbstractSymbolCombination`, `SymbolCombination` and
/// `QuantitySymbolCombination` all exist here.** Only the `QuadraticIntermediateSymbols` factory object is missing — ten
/// `invoke` overloads, each building a shape's worth of empty quadratic expression symbols.
///
/// **And it has <b>zero production callers</b> in the code: `git grep` hits only its own file and one test.**
///
/// **That reason differs from the sibling decision and the two must be kept apart: there the base class was "already had, unobservable, or
/// an empty check", while this is "buildable, merely unused".** **"Nobody uses it" changes; "it would be pointless" does
/// not.** **So the criterion below must distinguish them: it does not prove "cannot be done", only "can be done and is not
/// done".**
/// </remarks>
public class QuadraticIntermediateSymbolsPortDecisionTests
{
    /// <summary>
    /// **空二次表达式符号组合现在就能构造出来，工厂只是语法糖** /
    /// An empty quadratic symbol combination is constructible today; the factory is only sugar.
    /// </summary>
    /// <remarks>
    /// **这是这里<b>唯一有实证价值</b>的断言：** **如果"空二次组合"造不出来，那么缺的就是能力；
    /// 造得出来，缺的就只是十个便捷重载。**
    ///
    /// **判据同时核对 `GroupIdentifier` 与 `IndexInGroup`：** **那是 `SymbolCombination` 构造时做的事，
    /// 也是十个工厂方法<b>唯一</b>依赖的既有行为。**
    /// **如果连这个都不成立，那么"工厂只是糖"这句话就是错的——糖下面还得有东西。**
    ///
    /// **This is the batch's <b>one assertion with evidential value</b>: if an empty quadratic combination could not be built,
    /// the gap would be capability; since it can, the gap is ten convenience overloads.**
    ///
    /// **It also checks `GroupIdentifier` and `IndexInGroup`, which is what the `SymbolCombination` constructor does and the
    /// <b>only</b> existing behaviour those ten factories depend on.** **Were that untrue, "the factory is only sugar" would
    /// be false — sugar still needs something under it.**
    /// </remarks>
    [Fact]
    public void AnEmptyQuadraticCombination_ShouldAlreadyBeConstructible() {
        var combination = new SymbolCombination<QuadraticExpressionSymbol, Shape1>(
            name: "quadratic-empty",
            shape: Shape1.Invoke(3),
            ctor: static (_, _) => new QuadraticExpressionSymbol(
                new MutableQuadraticPolynomial<Flt64>()));

        combination.Name.Should().Be("quadratic-empty");
        combination.Should().HaveCount(3, "Shape1(3) means three empty quadratic symbols");

        int index = 0;
        foreach (QuadraticExpressionSymbol symbol in combination) {
            symbol.GroupIdentifier.Should().Be(
                combination.Identifier,
                "the constructor stamps the group; the ten factories rely on exactly this");
            symbol.IndexInGroup.Should().Be(index, "the constructor numbers them in order");
            ++index;
        }

        // **两个组合的组标识必须不同：** **否则同一形状的两批符号会在下游被当成同一组，
        // 而那种冲突不会报错，只会让两个本来独立的组合混在一起。**
        // **Two combinations must get different group identifiers: otherwise two independent batches of the same shape read
        // as one group downstream, and such a collision does not error — it merely mixes two combinations that should stay
        // apart.**
        var other = new SymbolCombination<QuadraticExpressionSymbol, Shape1>(
            name: "quadratic-empty-2",
            shape: Shape1.Invoke(3),
            ctor: static (_, _) => new QuadraticExpressionSymbol(new MutableQuadraticPolynomial<Flt64>()));
        other.Identifier.Should().NotBe(
            combination.Identifier, "each combination gets its own group identity");
    }

    /// <summary>
    /// **工厂对象仍然不存在** / The factory object still does not exist.
    /// </summary>
    /// <remarks>
    /// **缺口判据：** **有人在 C# 里加这个工厂对象时它会变红，而那时该做的是把理由从
    /// "没有使用者"改成具体的那个使用者。**
    ///
    /// **注意它<b>不</b>说"不该有"：** **它说的是"现在没有，而这条判据会在有了之后提醒改写理由"。**
    ///
    /// **The gap criterion: it goes red when someone adds the factory here, and then the reason must change from "no caller" to
    /// naming that caller.** **Note it does <b>not</b> say "it should not exist" — it says "it does not exist now, and this
    /// criterion will demand the reason be rewritten once it does".**
    /// </remarks>
    [Fact]
    public void TheFactoryObject_ShouldStillBeAbsent() {
        string root = RepositoryRoot();
        string source = ReadAllSources(System.IO.Path.Combine(root, "src"));

        source.Should().NotContain(
            "QuadraticIntermediateSymbols",
            "if this factory appears, the reason for its absence changed from 'no caller' to 'this caller'");
    }

    private static string ReadAllSources(string root) {
        var text = new System.Text.StringBuilder();
        foreach (string path in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)) {
            text.Append(File.ReadAllText(path)).Append('\n');
        }
        return text.ToString();
    }

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
