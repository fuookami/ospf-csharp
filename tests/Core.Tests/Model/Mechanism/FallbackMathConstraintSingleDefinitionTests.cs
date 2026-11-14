#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Model.Mechanism;

/// <summary>
/// 缺源占位键只有一份定义 / The no-origin placeholder key has exactly one definition.
/// </summary>
/// <remarks>
/// B1226 把六个后端各自的同一份 `FallbackMathConstraint` 合并到 `Core`。
///
/// **合并前的六份<b>逐字相同</b>，而其中四份用了四个不同的类名前缀**
/// （`Copt`／`Cplex`／`Mindopt`／`Hexaly`），另外两份都叫 `FallbackMathConstraint` 却在不同命名空间里。
///
/// **所以"它们其实一样"这件事在阅读时看不出来**——而这正是重复最贵的形态：
/// **六份相同的代码并不比一份贵六倍，它贵在"没人知道它们相同，于是没人敢改任何一份"。**
///
/// **判据的形状因此不是"行为对"（行为一直对），而是"定义只有一处"：
/// 源码树里 `record ... : MathConstraint` 形式的占位键声明必须恰好一个。**
///
/// **这是少见的"结构判据"而不是"行为判据"，而它在这件事上是唯一有效的形状：**
/// **行为判据在六份实现上都是绿的，因为六份都正确。**
///
/// B1226 merges six per-backend `FallbackMathConstraint` copies into `Core`.
///
/// **The six were <b>byte-identical</b>, and four used four different class-name prefixes, while the remaining two
/// shared a name across different namespaces.**
///
/// **So "they are actually the same" was invisible when reading — and that is the expensive form of duplication: six
/// identical copies do not cost six times one, they cost the confidence to change any of them.**
///
/// **The criterion is therefore not behavioural (behaviour was always right) but structural: the source tree must
/// contain exactly one placeholder-key declaration of the form `record ... : MathConstraint`.**
///
/// **This is a rare structural criterion rather than a behavioural one, and here it is the only shape that works: a
/// behavioural criterion is green on all six copies, because all six are correct.**
/// </remarks>
public class FallbackMathConstraintSingleDefinitionTests
{
    /// <summary>
    /// **全仓只有一个 `MathConstraint` 占位键声明** / Exactly one placeholder-key declaration exists.
    /// </summary>
    [Fact]
    public void ThereShouldBeExactlyOneDeclaration() {
        string root = RepositoryRoot();

        string[] declarations = Directory
            .EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => IsBuildOutput(path) is false)
            .SelectMany(path => File.ReadAllLines(path)
                .Where(static line => line.Contains(": MathConstraint", StringComparison.Ordinal))
                .Where(static line => line.Contains("record ", StringComparison.Ordinal))
                .Where(static line => line.Contains("FallbackMathConstraint", StringComparison.Ordinal))
                .Select(line => $"{Path.GetFileName(path)}: {line.Trim()}"))
            .ToArray();

        Assert.Single(declarations);
    }

    /// <summary>
    /// **共享实现落在 `Core`，不在任何插件里** / The shared implementation lives in `Core`, not in a plugin.
    /// </summary>
    /// <remarks>
    /// **这一条钉的是"合并到哪一层"。** **放在插件里也能编译（其余插件引用它），
    /// 但那会让 `Core` 依赖某个后端的存在，而这条依赖在只构建 `Core` 时不会暴露。**
    ///
    /// **This pins which layer the merge landed in.** **Putting it in a plugin would also compile (the others reference
    /// it), but it would make `Core` depend on one backend's existence — a dependency invisible when building `Core`
    /// alone.**
    /// </remarks>
    [Fact]
    public void TheSharedDeclaration_ShouldLiveInCore() {
        string root = RepositoryRoot();
        string declaration = Directory
            .EnumerateFiles(Path.Combine(root, "src", "Core"), "*.cs", SearchOption.AllDirectories)
            .Single(path => File.ReadAllText(path)
                .Contains("record FallbackMathConstraint(int Index) : MathConstraint", StringComparison.Ordinal));

        declaration.Should().BeEquivalentTo(Path.Combine(root, "src", "Core", "Model", "Mechanism", "MetaConstraint.cs"));
    }

    /// <summary>
    /// **六个插件都不再含有私有副本** / None of the six plugins still carries a private copy.
    /// </summary>
    [Theory]
    [InlineData("Core.Plugin.Copt")]
    [InlineData("Core.Plugin.Cplex")]
    [InlineData("Core.Plugin.Gurobi")]
    [InlineData("Core.Plugin.Hexaly")]
    [InlineData("Core.Plugin.Mindopt")]
    [InlineData("Core.Plugin.Scip")]
    public void ThePlugin_ShouldNotDeclareItsOwnCopy(string plugin) {
        string root = RepositoryRoot();
        string[] copies = Directory
            .EnumerateFiles(Path.Combine(root, "src", plugin), "*.cs", SearchOption.AllDirectories)
            .Where(path => IsBuildOutput(path) is false)
            .Where(path => File.ReadAllText(path).Contains(
                "record ", StringComparison.Ordinal)
                && File.ReadAllText(path).Contains(
                    "FallbackMathConstraint(int Index) : MathConstraint", StringComparison.Ordinal))
            .ToArray();

        Assert.Empty(copies);
    }

    /// <summary>
    /// **行号到来源键的解析也只有一个定义** / Row-to-origin key resolution also has exactly one definition.
    /// </summary>
    /// <remarks>
    /// B1227 与 B1226 同源：**占位键只剩一份定义之后，造占位键的那段三元表达式还在四个后端各写了一遍。**
    ///
    /// **判据同样是结构的，理由也一样：** **行为在四份实现上都是绿的，因为它们都正确。**
    ///
    /// **但这一批的合并没有把四份整文件合并，只合并了那个纯函数。**
    /// **原因写在这里，因为"为什么只合这一点"比"合了什么"更容易丢失：**
    /// **四个 `XxxDualSupport` 的其余部分依赖各自的 `LinearMechanismModel` 泛型特化与各自的后端结果类型，
    /// 整文件合并会牵动 18 个调用文件；而那个三元表达式不依赖任何后端类型。**
    ///
    /// **"同一个形状的重复"不等于"同样大的一步"，这两批的差别就是证据。**
    ///
    /// **B1227 is the same story as B1226: once the placeholder key had one definition, the ternary that builds it was
    /// still written out in four backends.**
    ///
    /// **The criterion is structural again, for the same reason: behaviour is green on all four because all four are
    /// correct.**
    ///
    /// **But this batch did not merge the four whole files, only that pure function. The reason is recorded here because
    /// "why only this much" is easier to lose than "what was merged":**
    ///
    /// **the rest of each `XxxDualSupport` depends on its own `LinearMechanismModel` generic specialisation and its own
    /// backend result types — a whole-file merge would touch 18 calling files, while the ternary depends on no backend
    /// type.**
    ///
    /// **"The same shape of duplication" does not mean "the same size of step"; the difference between these two batches
    /// is the evidence.**
    /// </remarks>
    [Fact]
    public void TheRowToOriginKeyResolution_ShouldHaveExactlyOneDefinition() {
        string root = RepositoryRoot();

        string[] declarations = Directory
            .EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => IsBuildOutput(path) is false)
            .SelectMany(path => File.ReadAllLines(path)
                .Where(static line => line.Contains("ResolveOriginKey", StringComparison.Ordinal))
                .Where(static line => line.Contains("public static MathConstraint", StringComparison.Ordinal))
                .Select(line => $"{Path.GetFileName(path)}: {line.Trim()}"))
            .ToArray();

        Assert.Single(declarations);
    }

    /// <summary>
    /// **四个后端都不再自己解析来源键** / None of the four backends resolves the origin key itself.
    /// </summary>
    [Theory]
    [InlineData("Core.Plugin.Copt")]
    [InlineData("Core.Plugin.Cplex")]
    [InlineData("Core.Plugin.Mindopt")]
    [InlineData("Core.Plugin.Scip")]
    public void ThePlugin_ShouldCallTheSharedResolver(string plugin) {
        string root = RepositoryRoot();
        string[] localDefinitions = Directory
            .EnumerateFiles(Path.Combine(root, "src", plugin), "*.cs", SearchOption.AllDirectories)
            .Where(path => IsBuildOutput(path) is false)
            .Where(path => File.ReadAllLines(path).Any(static line =>
                line.Contains("private static MathConstraint Key(", StringComparison.Ordinal)))
            .ToArray();

        Assert.Empty(localDefinitions);
    }

    /// <summary>
    /// **来源映射与对偶整理各只有一个定义** / The origin map and the dual tidy each have exactly one definition.
    /// </summary>
    /// <remarks>
    /// B1228 与 B1226／B1227 同一动机，但**这一批的可行性是上一批<b>先不合并</b>换来的**。
    ///
    /// **B1227 只合了那个纯函数，理由是"其余部分依赖各自的泛型特化"。**
    /// **本批核对下来，`OriginMap` 与 `ToDualSolution` 依赖的泛型特化<b>完全相同</b>——
    /// 四个插件都用同一个 `LinearMechanismModel<Flt64>` 与 `IConstraint<Flt64, LinearCategory>`。**
    ///
    /// **所以上一批那个理由对 `Key` 成立、对这两个函数不成立，而这恰恰验证了当时的判断方式：
    /// 不确定时先不合，核对之后再合，而不是先合再发现类型对不上。**
    ///
    /// **B1228 shares B1226/B1227's motive, but its feasibility was bought by the previous batch <b>declining</b> to
    /// merge.**
    ///
    /// **B1227 merged only the pure function, reasoning that "the rest depends on each backend's generic
    /// specialisation".** **This batch's check shows `OriginMap` and `ToDualSolution` depend on the <b>same</b>
    /// specialisation — all four plugins use one `LinearMechanismModel<Flt64>` and
    /// `IConstraint<Flt64, LinearCategory>`.**
    ///
    /// **So the earlier reason held for `Key` but not here, which is exactly what that way of judging was for: decline
    /// while unsure, merge after checking, rather than merge and then discover the mismatch.**
    /// </remarks>
    [Fact]
    public void TheOriginMapAndDualTidy_ShouldHaveExactlyOneDefinitionEach() {
        string root = RepositoryRoot();

        string[] declarations = Directory
            .EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => IsBuildOutput(path) is false)
            .SelectMany(path => File.ReadAllLines(path)
                .Where(static line =>
                    line.Contains("public static MathConstraint?[] OriginMap(", StringComparison.Ordinal)
                    || line.Contains("public static IReadOnlyDictionary<MathConstraint, Flt64> ToDualSolution(",
                        StringComparison.Ordinal))
                .Select(line => $"{Path.GetFileName(path)}: {line.Trim()}"))
            .ToArray();

        Assert.Equal(2, declarations.Length);
        Assert.Single(declarations, static line => line.Contains("OriginMap", StringComparison.Ordinal));
        Assert.Single(declarations, static line => line.Contains("ToDualSolution", StringComparison.Ordinal));
    }

    /// <summary>
    /// **四个后端都不再声明自己的来源映射** / None of the four backends declares its own origin map.
    /// </summary>
    [Theory]
    [InlineData("Core.Plugin.Copt")]
    [InlineData("Core.Plugin.Cplex")]
    [InlineData("Core.Plugin.Mindopt")]
    [InlineData("Core.Plugin.Scip")]
    public void ThePlugin_ShouldNotDeclareItsOwnOriginMap(string plugin) {
        string root = RepositoryRoot();
        string[] copies = Directory
            .EnumerateFiles(Path.Combine(root, "src", plugin), "*.cs", SearchOption.AllDirectories)
            .Where(path => IsBuildOutput(path) is false)
            .Where(path => File.ReadAllLines(path).Any(static line =>
                line.Contains("static MathConstraint?[] OriginMap(", StringComparison.Ordinal)
                || line.Contains("static IReadOnlyDictionary<MathConstraint, Flt64> ToDualSolution(",
                    StringComparison.Ordinal)))
            .ToArray();

        Assert.Empty(copies);
    }

    /// <summary>
    /// **Farkas 整理也只有一个定义** / The Farkas tidy also has exactly one definition.
    /// </summary>
    /// <remarks>
    /// B1229 与 B1226—B1228 同一动机，闭掉最后一份对偶辅助函数的重复。
    ///
    /// **它与 `ToDualSolution` 的差别只有两点，而判据必须能把这两点区分开，
    /// 否则"合并"会退化成"悄悄改变语义"：** **零值在这里被丢弃、全零在这里是失败。**
    /// 两条都由 `ScipLpDualSupportTest` 的既有判据覆盖，本批只是换了实现位置。
    ///
    /// **B1229 shares B1226–B1228's motive and closes the last duplicated dual helper.**
    ///
    /// **It differs from `ToDualSolution` in exactly two ways, and the criteria must separate them or the merge would
    /// degrade into a silent semantic change: zeros are dropped here, and all-zeros is a failure here.** **Both are
    /// covered by existing `ScipLpDualSupportTest` criteria; this batch only moved the implementation.**
    /// </remarks>
    [Fact]
    public void TheFarkasTidy_ShouldHaveExactlyOneDefinition() {
        string root = RepositoryRoot();

        string[] declarations = Directory
            .EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => IsBuildOutput(path) is false)
            .SelectMany(path => File.ReadAllLines(path)
                .Where(static line => line.Contains("ToFarkasSolution(", StringComparison.Ordinal))
                .Where(static line => line.Contains("public static Result", StringComparison.Ordinal))
                .Select(line => $"{Path.GetFileName(path)}: {line.Trim()}"))
            .ToArray();

        Assert.Single(declarations);
    }

    /// <summary>
    /// **四个后端都不再声明自己的 Farkas 整理** / None of the four backends declares its own Farkas tidy.
    /// </summary>
    [Theory]
    [InlineData("Core.Plugin.Copt")]
    [InlineData("Core.Plugin.Cplex")]
    [InlineData("Core.Plugin.Mindopt")]
    [InlineData("Core.Plugin.Scip")]
    public void ThePlugin_ShouldNotDeclareItsOwnFarkasTidy(string plugin) {
        string root = RepositoryRoot();
        string[] copies = Directory
            .EnumerateFiles(Path.Combine(root, "src", plugin), "*.cs", SearchOption.AllDirectories)
            .Where(path => IsBuildOutput(path) is false)
            .Where(path => File.ReadAllLines(path).Any(static line =>
                line.Contains("static Result<IReadOnlyDictionary<MathConstraint, Flt64>", StringComparison.Ordinal)
                && line.Contains("ToFarkasSolution(", StringComparison.Ordinal)))
            .ToArray();

        Assert.Empty(copies);
    }

    /// <summary>
    /// **占位键只在共享解析器里被构造** / The placeholder key is only ever constructed inside the shared resolver.
    /// </summary>
    /// <remarks>
    /// B1230 把最后三处"自己解析来源键"的地方也接上共享实现：Gurobi 的列生成、Gurobi 的 Benders 分解、
    /// 以及 Hexaly 一处内联的 `Origin ?? new FallbackMathConstraint(index)`。
    ///
    /// **最后那一处值得单独看：它连函数都没抽出来，只是一行 `??`。**
    /// **这种形态最不容易被"找重复实现"发现，因为它不是一个<b>声明</b>，而是一个<b>表达式</b>——
    /// 而前四批的结构判据找的都是"声明"。**
    ///
    /// **所以这一条的判据换了个锚：不数声明，数 `new FallbackMathConstraint(` 的出现次数。**
    /// **它必须恰好出现在共享解析器里一次——因为那是唯一知道"缺源时该造什么"的地方。**
    ///
    /// **判据的锚点决定了它能看见什么。** **前几批数"声明"，于是看不见内联表达式；
    /// 这一批数"构造"，于是看见了。**
    ///
    /// **B1230 wires the last three self-resolving sites onto the shared implementation: Gurobi's column generation,
    /// Gurobi's Benders decomposition, and one inline `Origin ?? new FallbackMathConstraint(index)` in Hexaly.**
    ///
    /// **That last one deserves its own note: it never even became a function, just a `??` in an expression.** **This
    /// shape is the hardest to find by "look for duplicate implementations", because it is not a <b>declaration</b> but
    /// an <b>expression</b> — and the previous four batches' criteria all looked at declarations.**
    ///
    /// **So this criterion re-anchors: instead of counting declarations it counts occurrences of
    /// `new FallbackMathConstraint(`.** **Exactly one, inside the shared resolver, because that is the only place that
    /// knows what to build when an origin is missing.**
    ///
    /// **The anchor decides what a criterion can see.** **Counting declarations could not see an inline expression;
    /// counting constructions can.**
    /// </remarks>
    [Fact]
    public void ThePlaceholderKey_ShouldOnlyBeConstructedInsideTheSharedResolver() {
        string root = RepositoryRoot();

        string[] constructions = Directory
            .EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => IsBuildOutput(path) is false)
            .SelectMany(path => File.ReadAllLines(path)
                .Where(static line => line.Contains("new FallbackMathConstraint(", StringComparison.Ordinal))
                .Select(line => $"{Path.GetFileName(path)}: {line.Trim()}"))
            .ToArray();

        Assert.Single(constructions);
        Assert.Contains("MetaConstraint.cs", constructions[0], StringComparison.Ordinal);
    }

    /// <summary>
    /// **六个后端都不再自己解析来源键（含内联表达式）** /
    /// None of the six backends resolves an origin key itself, inline expressions included.
    /// </summary>
    [Theory]
    [InlineData("Core.Plugin.Copt")]
    [InlineData("Core.Plugin.Cplex")]
    [InlineData("Core.Plugin.Gurobi")]
    [InlineData("Core.Plugin.Hexaly")]
    [InlineData("Core.Plugin.Mindopt")]
    [InlineData("Core.Plugin.Scip")]
    public void ThePlugin_ShouldNotResolveOriginKeysItself(string plugin) {
        string root = RepositoryRoot();
        string[] offenders = Directory
            .EnumerateFiles(Path.Combine(root, "src", plugin), "*.cs", SearchOption.AllDirectories)
            .Where(path => IsBuildOutput(path) is false)
            .Where(path => File.ReadAllLines(path).Any(static line =>
                line.Contains("new FallbackMathConstraint(", StringComparison.Ordinal)
                || line.Contains("GetMathConstraintKey(", StringComparison.Ordinal)))
            .ToArray();

        Assert.Empty(offenders);
    }

    private static bool IsBuildOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
        || path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    private static string RepositoryRoot() {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null) {
            if (File.Exists(Path.Combine(current.FullName, "ospf-csharp.sln"))) {
                return current.FullName;
            }
            current = current.Parent;
        }
        throw new InvalidOperationException("ospf-csharp.sln not found above the test output directory");
    }
}
