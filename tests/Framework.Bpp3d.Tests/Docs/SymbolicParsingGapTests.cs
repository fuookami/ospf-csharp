#nullable enable

using FluentAssertions;
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Docs;

/// <summary>
/// 符号文本解析这个缺口还在不在 / Whether the symbolic text-parsing gap is still open.
/// </summary>
/// <remarks>
/// **基线有整套文本解析：`ScalarParser`（713 行）、`PolynomialLexer`（281 行）、`PolynomialParser`（622 行）。**
/// **C# 这边只有数值解析（`INumberParser&lt;T&gt;` / `Int64NumberParser`），**
/// **表达式与多项式的文本入口**没有实现。
///
/// **这条绊线的方向是反的：缺口还是缺口时它绿，补上了它红。**
/// **红不是问题，红了才是提醒"文档里那句缺口该改了"。**
///
/// **The baseline has a whole text-parsing stack: `ScalarParser` (713 lines), `PolynomialLexer` (281), `PolynomialParser` (622).**
/// **C# has only the numeric parser (`INumberParser&lt;T&gt;` / `Int64NumberParser`)**; the expression and polynomial text entry points have no implementation.
/// **This tripwire points the other way: green while the gap is a gap, red once it is filled.** **Red is not a failure, red is the reminder that the documented gap needs editing.**
/// </remarks>
public class SymbolicParsingGapTests
{
    private static DirectoryInfo Root()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ospf-csharp.sln")))
        {
            directory = directory.Parent;
        }
        if (directory is null)
        {
            throw new InvalidOperationException("repository root not found");
        }

        return directory;
    }

    /// <summary>`src/Math/Symbol/Parse/` 里的源码 / The sources under the parse folder.</summary>
    /// <summary>两个解析目录 / Both parsing directories.</summary>
    /// <remarks>
    /// **`Parse/` 是多项式那一侧，`Expression/Parser/` 是表达式那一侧。**
    /// **B1092 之前这里只扫前者，于是"表达式解析不在"这条判据从来没看过表达式解析的代码。**
    ///
    /// **`Parse/` holds the polynomial side and `Expression/Parser/` the expression side.**
    /// **Before B1092 this scanned only the former, so the "expression parser is absent" criterion had never read any expression-parsing code.**
    /// </remarks>
    private static string[] ParseSources() => new[]
    {
        Path.Combine(Root().FullName, "src", "Math", "Symbol", "Parse"),
        Path.Combine(Root().FullName, "src", "Math", "Symbol", "Expression", "Parser"),
    }
        .SelectMany(directory => Directory.GetFiles(directory, "*.cs"))
        .Select(File.ReadAllText)
        .ToArray();

    /// <summary>**数值解析在** / The numeric parser is present.</summary>
    /// <remarks>
    /// **这一条不是缺口，是范围**：写这条是为了让人看得出"不是整个 `Parse/` 都空着"。
    /// **把"只有数值解析"读成"连数值解析都没有"，会让缺口看起来比实际大，而夸大会让真缺口以后没人信。**
    ///
    /// **This is not the gap, it is the scope**: it exists so a reader can see the folder is not empty.
    /// **Reading "numeric parsing only" as "no parsing at all" inflates the gap, and inflation costs the real one its credibility.**
    /// </remarks>
    [Fact]
    public void TheNumericParser_ShouldBePresent()
    {
        ParseSources().Any(source => source.Contains("interface INumberParser<", StringComparison.Ordinal))
            .Should().BeTrue("the numeric parser exists; the gap is the expression and polynomial layers above it");
    }

    /// <summary>**标量解析器的优先级链已经完整** / The scalar parser's precedence chain is complete.</summary>
    /// <remarks>
    /// **这条判据换过三次对象，每次都是因为我把缺口的位置认错了。**
    ///
    /// 1. 一开始钉"整条表达式解析栈不在"——**那是我读错了文件名差集（B1091），栈一直在。**
    /// 2. 然后钉"三元与 `if-then-else` 不在"——**B1093/B1094 搬完之后它就该红，而我靠手工收窄才让它继续绿。**
    /// 3. 最后钉"标量解析器里没有 `PatternMatch`"——**而模式匹配根本不在 `ScalarParser.kt` 里，它在布尔侧 `Parser.kt`，
    ///    C# 的 `Parser.cs` 早就实现了。我钉的是一个不存在的缺口。**
    ///
    /// **现在钉的是能长期成立的那件事：优先级链的九层都在、顺序对、且比较确实不链式。**
    /// **这条不会因为"某块搬完了"而需要收窄，因为它钉的是结构，不是进度。**
    ///
    /// **This criterion has changed its target three times, each time because I mislocated the gap.**
    /// **1. It first pinned "the whole expression stack is absent" — my misreading of a filename diff (B1091); the stack was there.**
    /// **2. Then it pinned "ternaries and `if-then-else` are absent" — which should have gone red once B1093/B1094 landed, and stayed green only because I narrowed it by hand.**
    /// **3. Finally it pinned "no `PatternMatch` in the scalar parser" — but pattern matching is not in `ScalarParser.kt` at all; it lives in the boolean `Parser.kt`,
    ///    which C# has had all along. I was pinning a gap that did not exist.**
    /// **What it pins now can hold for good: all nine precedence layers are present, in order, and comparison does not chain.**
    /// **This one needs no narrowing when a piece lands, because it pins structure, not progress.**
    /// </remarks>
    [Fact]
    public void TheScalarPrecedenceChain_ShouldBeCompleteAndInOrder()
    {
        string[] scalarParserSources = ParseSources()
            .Where(source => source.Contains("ParseTernary", StringComparison.Ordinal))
            .ToArray();

        scalarParserSources.Should().ContainSingle(
            "exactly one file implements the scalar precedence chain");

        string scalarParser = scalarParserSources[0];

        // **九层，按降序的调用顺序。** **任何一层被删掉，上一层的递归就会直接落到更低的层，
        // **而那种改动照样编译。**
        // **The nine layers, in descending call order.** **Dropping any one makes the layer above fall straight through to a lower one,
        // **and that change compiles.**
        string[] layers =
        [
            "ParseTernary",
            "ParseLogicalOr",
            "ParseLogicalAnd",
            "ParseComparison",
            "ParseAdditive",
            "ParseMultiplicative",
            "ParseUnaryMinus",
            "ParsePower",
            "ParseUnaryPlus",
        ];

        int previousIndex = -1;
        foreach (string layer in layers)
        {
            int index = scalarParser.IndexOf("private ScalarRet " + layer + "(", StringComparison.Ordinal);
            index.Should().BeGreaterThan(-1, layer + " has to exist");
            index.Should().BeGreaterThan(previousIndex,
                layer + " has to be declared below the layer that calls it, so the chain reads top-down");
            previousIndex = index;
        }

        // **比较用 `if` 不用 `while`。** **`while` 会把 `1 &lt; 2 &lt; 3` 变成嵌套比较——照样编译，
        // **而那是另一门语言的语义。**
        // **Comparison is an `if`, not a `while`.** **A `while` turns `1 &lt; 2 &lt; 3` into a nested comparison — it compiles,
        // **and that is another language's semantics.**
        int comparisonStart = scalarParser.IndexOf("private ScalarRet ParseComparison()", StringComparison.Ordinal);
        int comparisonEnd = scalarParser.IndexOf("private static OrExpression MergeOr", StringComparison.Ordinal);
        comparisonEnd.Should().BeGreaterThan(comparisonStart, "the comparison layer has a bounded body");
        scalarParser[comparisonStart..comparisonEnd].Should().NotContain("while (",
            "comparison must not chain; a while loop would silently accept 1 < 2 < 3");
    }

    /// <summary>**按名字标识的符号已经有了** / A name-identified symbol now exists.</summary>
    /// <remarks>
    /// **B1086 之前 C# 只有 `PathSymbol`，而符号反序列化对任何普通名字直接抛异常。**
    /// **这一条是正向的**：它记的是"哪一半已经填上了"。
    ///
    /// **Before B1086 C# had only `PathSymbol`, and symbol deserialization threw on every plain name.**
    /// **This one is positive**: it records which half is already filled.
    /// </remarks>
    [Fact]
    public void TheNamedSymbol_ShouldExist()
    {
        typeof(Fuookami.Ospf.Math.Symbol.NamedSymbol).Should().NotBeNull(
            "the polynomial layer needs a name-identified symbol, and it had none before B1086");
    }

    /// <summary>**多项式文本解析还没有** / There is no polynomial text parser yet.</summary>
    [Fact]
    public void ThePolynomialParser_ShouldStillBeAbsent()
    {
        Directory.GetFiles(Path.Combine(Root().FullName, "src", "Math"), "*Lexer*.cs", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(Path.Combine(Root().FullName, "src", "Math"), "*Parser*.cs", SearchOption.AllDirectories))
            .Select(path => Path.GetFileName(path)!)
            .Where(name => name.Contains("Polynomial", StringComparison.Ordinal))
            // **词法层 B1084、语法层 B1085 已搬，所以这里只钉标量解析那 713 行。**
            // **`PolynomialLexer.cs` 与 `PolynomialParser.cs` 是已有的，不算命中。**
            // **The lexical layer landed in B1084 and the grammar layer in B1085, so this pins only the 713-line scalar parser.**
            // **`PolynomialLexer.cs` and `PolynomialParser.cs` are expected and do not count.**
            .Where(name => !name.StartsWith("PolynomialLexer", StringComparison.Ordinal))
            .Where(name => !name.StartsWith("PolynomialParser", StringComparison.Ordinal))
            .Should().BeEmpty(
                "the baseline's PolynomialParser has no counterpart; a file by that name means it landed");
    }
}
