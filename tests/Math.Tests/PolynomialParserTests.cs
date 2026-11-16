#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Parse;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Math.Symbol.Serde;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Fuookami.Ospf.Math.Tests;

/// <summary>
/// 多项式语法核心 / The polynomial grammar core.
/// </summary>
/// <remarks>
/// **实现自固定的语法层。**
/// 五条判据对着五个"能编译但语义丢了"的点：优先级、`*` 左结合、`x^0`、负指数、多余输入。
///
/// **Ported from the grammar layer of.** Five criteria for five places where the code can compile with the meaning gone: precedence, left-associative `*`, `x^0`, negative exponents, trailing input.
/// </remarks>
public class PolynomialParserTests
{
    private static ParsedPolynomial<Int64> Parse(string input)
    {
        Result<ParsedPolynomial<Int64>, ErrorCode, Error<ErrorCode>> result =
            PolynomialParser<Int64>.Parse(input, Int64NumberParser.Instance, Int64.Zero, Int64.One);

        result.Value.Should().NotBeNull("the input has to parse: " + input);
        return result.Value;
    }

    private static bool Rejects(string input) =>
        !PolynomialParser<Int64>.Parse(input, Int64NumberParser.Instance, Int64.Zero, Int64.One).IsOk;

    /// <summary>某一项的系数 / The coefficient of one term.</summary>
    /// <remarks>
    /// **`Int64.Value` 是 internal，测试读不到**，所以这里返回结构本身，
    /// **由调用方与 `new Int64(n)` 比较——`Int64` 有值相等。**
    ///
    /// **`Int64.Value` is internal and unreadable from tests**, so this returns the struct itself
    /// **and the caller compares against `new Int64(n)`; `Int64` has value equality.**
    /// </remarks>
    private static Int64 Coefficient(ParsedPolynomial<Int64> polynomial, params (string Name, int Power)[] powers)
    {
        Dictionary<string, int> wanted = powers.ToDictionary(pair => pair.Name, pair => pair.Power);

        ParsedTerm<Int64> match = polynomial.Terms.Single(term =>
            term.Powers.Count == wanted.Count
            && wanted.All(pair => term.Powers.TryGetValue(pair.Key, out int actual) && actual == pair.Value));

        return match.Coefficient;
    }

    /// <summary>**乘号比加减号先算** / Multiplication binds tighter than addition.</summary>
    /// <remarks>
    /// **"先算哪个"在数值上会给出不同答案，所以这一条看得出来**：`1+2*x` 若先加后乘，x 的系数是 3。
    ///
    /// **Which one runs first changes the numbers, so this can see it**: if `1+2*x` added first, x's coefficient would be 3.
    /// </remarks>
    [Fact]
    public void TheMultiplication_ShouldBindTighterThanAddition()
    {
        ParsedPolynomial<Int64> polynomial = Parse("1 + 2*x");

        polynomial.Constant.Should().Be(new Int64(1));
        Coefficient(polynomial, ("x", 1)).Should().Be(new Int64(2), "2*x is a term; adding first would give coefficient 3");
    }

    /// <summary>**`*` 左结合，幂次相加** / `*` is left-associative and powers add.</summary>
    [Fact]
    public void TheProduct_ShouldAddPowers()
    {
        ParsedPolynomial<Int64> polynomial = Parse("x * x * x");

        polynomial.Terms.Should().ContainSingle("three x's collapse into one term");
        Coefficient(polynomial, ("x", 3)).Should().Be(new Int64(1));
    }

    /// <summary>**`^0` 是常数 1，不是"没有这一项"** / `x^0` is the constant one, not the absence of a term.</summary>
    /// <remarks>
    /// **这一条最容易被写成"乘零次就是空"**，那样 `x^0` 会读成 0，而数学上它是 1。
    ///
    /// **The tempting shortcut is "multiplying zero times means nothing"**, which reads `x^0` as 0 when it is 1.
    /// </remarks>
    [Fact]
    public void TheZeroExponent_ShouldBeTheConstantOne()
    {
        ParsedPolynomial<Int64> polynomial = Parse("x^0");

        polynomial.Terms.Should().BeEmpty("there is no x term");
        polynomial.Constant.Should().Be(new Int64(1), "x^0 is one, not zero");
    }

    /// <summary>**负指数被拒绝** / A negative exponent is rejected.</summary>
    [Fact]
    // **`x^-1` 的词法就把 `-1` 切成两个单元，所以这里走的是"`^` 后不是整数"那条门。**
    // **The lexer splits `-1` into two tokens, so this takes the "not an integer after ^" path.**
    public void TheNegativeExponent_ShouldBeRejected() =>
        Rejects("x^-1").Should().BeTrue("a negative exponent is out of scope for polynomials");

    /// <summary>**多余输入是错误** / Trailing input is an error.</summary>
    /// <remarks>
    /// **不检查这一条，`"1 2"` 会安静地解析成 `1`**——**而调用方拿到的多项式与它写下的文本不是一回事。**
    ///
    /// **Without this check `"1 2"` quietly parses as `1`** — **and the caller's polynomial is not what it wrote.**
    /// </remarks>
    [Fact]
    public void TheTrailingInput_ShouldBeRejected()
    {
        Rejects("1 2").Should().BeTrue("two numbers in a row is not a polynomial");
        Rejects("1 +").Should().BeTrue("a dangling operator is not a polynomial");
        Rejects("(1 + 2").Should().BeTrue("an unclosed parenthesis is not a polynomial");
    }

    /// <summary>**一元负号与括号** / Unary minus and parentheses.</summary>
    [Fact]
    public void TheUnaryMinusAndParens_ShouldWork()
    {
        ParsedPolynomial<Int64> polynomial = Parse("-(2*x) + 3");

        polynomial.Constant.Should().Be(new Int64(3));
        Coefficient(polynomial, ("x", 1)).Should().Be(new Int64(-2), "the minus distributes over the parenthesised term");
    }
}

/// <summary>
/// 解析结果落到规范多项式 / Lowering a parse result onto the canonical polynomial.
/// </summary>
/// <remarks>
/// **此前这条路根本走不通**：`SymbolOfSerializedIdentifier` 对任何不带 `path:` 前缀的标识符
/// 直接抛异常，**而解析出来的记号全是这种**。**缺的是最简单的那个回退，不是 hex/JSON 那几段。**
///
/// **This path did not work at all**: `SymbolOfSerializedIdentifier` threw for any identifier without a `path:` prefix,
/// **and every parsed name is one of those**. **What was missing was the simplest fallback, not the hex/JSON segments.**
/// </remarks>
public class ParsedPolynomialLoweringTests
{
    /// <summary>**解析结果能落到 `CanonicalPolynomial`** / A parse result lowers onto `CanonicalPolynomial`.</summary>
    [Fact]
    public void TheParsedPolynomial_ShouldLowerOntoCanonical()
    {
        ParsedPolynomial<Int64> parsed = PolynomialParser<Int64>
            .Parse("2*x^2 + 3*y - 1", Int64NumberParser.Instance, Int64.Zero, Int64.One)
            .Value;

        var canonical = parsed.ToCanonicalPolynomial();

        canonical.Monomials.Should().HaveCount(2, "x^2 and y are two monomials");
        canonical.Constant.Should().Be(new Int64(-1));
        canonical.Monomials.Select(monomial => monomial.Powers.Count).Should().AllBeEquivalentTo(1);
    }

    /// <summary>**按名字建符号，名字就是身份** / Names become symbols, and the name is the identity.</summary>
    [Fact]
    public void TheMonomialSymbols_ShouldCarryTheParsedNames()
    {
        var canonical = PolynomialParser<Int64>
            .Parse("2*x^2 + 3*y", Int64NumberParser.Instance, Int64.Zero, Int64.One)
            .Value
            .ToCanonicalPolynomial();

        canonical.Monomials
            .SelectMany(monomial => monomial.Powers.Keys)
            .Select(symbol => symbol.Name)
            .Should().BeEquivalentTo(["x", "y"]);
    }

    /// <summary>**`SymbolOfSerializedIdentifier` 对普通名字不再抛** / The identifier factory no longer throws on plain names.</summary>
    /// <remarks>
    /// **这一条直接钉那个异常。** 判据看的是**它返回什么**，不是"它没抛"——
    /// **"没抛"单独立着说明不了任何事。**
    ///
    /// **This pins the exception that was removed.** It looks at **what comes back**, not at "it did not throw" — **"did not throw" on its own states nothing.**
    /// </remarks>
    [Fact]
    public void TheIdentifierFactory_ShouldReturnANamedSymbol()
    {
        ISymbol symbol = SymbolIdentitySerde.SymbolOfSerializedIdentifier("z_1");

        symbol.Name.Should().Be("z_1");
        symbol.DisplayName.Should().BeNull("no display name was specified, and falling back to the name would erase that");
    }
}

/// <summary>
/// 不等式的语法层 / The inequality grammar.
/// </summary>
/// <remarks>
/// **两边各自是完整表达式，解析阶段不做移项。** 移项属于 `LinearInequality` 那一步，
/// **混进来的话"解析"与"化简"就再也分不开了。**
///
/// **Each side is a full expression, and parsing does not rearrange.** Rearranging belongs to the `LinearInequality` step,
/// **and mixing it in would make "parse" and "simplify" inseparable for good.**
/// </remarks>
public class PolynomialInequalityParseTests
{
    private static ParsedInequality<Int64> Parse(string input) =>
        PolynomialParser<Int64>
            .ParseInequality(input, Int64NumberParser.Instance, Int64.Zero, Int64.One)
            .Value;

    private static bool Rejects(string input) =>
        !PolynomialParser<Int64>
            .ParseInequality(input, Int64NumberParser.Instance, Int64.Zero, Int64.One).IsOk;

    /// <summary>**六个比较符都认得** / All six operators are recognised.</summary>
    [Theory]
    [InlineData("a < b", "LT")]
    [InlineData("a <= b", "LE")]
    [InlineData("a = b", "EQ")]
    [InlineData("a != b", "NE")]
    [InlineData("a >= b", "GE")]
    [InlineData("a > b", "GT")]
    public void TheComparison_ShouldBeRecognised(string input, string expected)
    {
        Parse(input).Comparison.Should().Be(
            (Fuookami.Ospf.Math.Symbol.Inequality.Comparison)System.Enum.Parse(typeof(Fuookami.Ospf.Math.Symbol.Inequality.Comparison), expected));
    }

    /// <summary>**两边各自成式，不移项** / Each side stands alone; nothing is moved across.</summary>
    /// <remarks>
    /// **`2*x &lt;= 3*y + 1` 的右边必须仍然是两项加常数**，而不是被挪成左边的一堆负项。
    ///
    /// **The right side of `2*x &lt;= 3*y + 1` has to stay two terms plus a constant**, not be moved left as a pile of negatives.
    /// </remarks>
    [Fact]
    public void TheTwoSides_ShouldStaySeparate()
    {
        ParsedInequality<Int64> inequality = Parse("2*x <= 3*y + 1");

        inequality.Lhs.Terms.Should().ContainSingle("x alone on the left");
        inequality.Lhs.Constant.Should().Be(Int64.Zero);

        inequality.Rhs.Terms.Should().ContainSingle("y alone on the right");
        inequality.Rhs.Constant.Should().Be(new Int64(1), "the constant stays on the side it was written");
    }

    /// <summary>**没有比较符是错误** / A missing operator is an error.</summary>
    [Fact]
    public void TheMissingOperator_ShouldBeRejected() =>
        Rejects("a + b").Should().BeTrue("a bare expression is not an inequality");

    /// <summary>**右边之后的多余输入同样是错误** / Trailing input after the right side is an error too.</summary>
    [Fact]
    public void TheTrailingInputAfterTheRightSide_ShouldBeRejected() =>
        Rejects("a < b c").Should().BeTrue("two expressions after the operator is not an inequality");
}

/// <summary>
/// 带度数约束的入口 / The degree-constrained entry points.
/// </summary>
/// <remarks>
/// **这两个入口的全部价值就是"度数超了就拒绝"**——**否则它们与 `Parse` 没有区别，只是多一层。**
/// 所以判据全部围绕"什么时候必须拒绝"。
///
/// **All these entry points are worth is refusing an out-of-range degree** — **otherwise they are `Parse` with a wrapper.**
/// So every criterion here is about when they must refuse.
/// </remarks>
public class PolynomialDegreeEntryPointTests
{
    private static bool Linear(string input) =>
        PolynomialParser<Int64>.ParseLinear(input, Int64NumberParser.Instance, Int64.Zero, Int64.One).IsOk;

    private static bool Quadratic(string input) =>
        PolynomialParser<Int64>.ParseQuadratic(input, Int64NumberParser.Instance, Int64.Zero, Int64.One).IsOk;

    /// <summary>**一次以内算线性** / Degree one or less is linear.</summary>
    [Fact]
    public void TheLinearEntry_ShouldAcceptDegreeOne()
    {
        Linear("2*x + 1").Should().BeTrue();
        Linear("5").Should().BeTrue("a constant has degree zero, which is within one");
    }

    /// <summary>**线性看总度数，不是看每个变量出现几次** / Linear means total degree, not how often each variable appears.</summary>
    /// <remarks>
    /// **`x*y` 里每个变量只出现一次，而它的总度数是 2，所以它不是线性的。**
    /// **这条判据是我自己先写错的地方**：第一版把它断言成"线性"，
    /// 注释里还留着"这值得问一下"——**那说明写的时候就知道自己没把握，却还是先写了断言。**
    ///
    /// **In `x*y` each variable appears once and its total degree is two, so it is not linear.**
    /// **This is the criterion I got wrong first**: the initial version asserted "linear", and its comment still said "the question worth asking" —
    /// **which means I knew I was unsure and wrote the assertion anyway.**
    /// </remarks>
    [Fact]
    public void TheLinearEntry_ShouldRefuseAProductOfTwoVariables() =>
        Linear("x*y").Should().BeFalse("x*y has total degree two, so it is not linear");

    /// <summary>**超过一次被拒绝** / Above degree one is refused.</summary>
    [Fact]
    public void TheLinearEntry_ShouldRefuseDegreeTwo()
    {
        Linear("x^2").Should().BeFalse("a squared term is degree two");
        Linear("x^3 + x").Should().BeFalse("a cubed term exceeds one");
    }

    /// <summary>**二次入口接受二次、拒绝三次** / The quadratic entry takes two and refuses three.</summary>
    [Fact]
    public void TheQuadraticEntry_ShouldDrawTheLineAtTwo()
    {
        Quadratic("x^2 + 2*x + 1").Should().BeTrue();
        Quadratic("2*x + 1").Should().BeTrue("lower degree is still valid for this entry");
        Quadratic("x^3").Should().BeFalse("a cubed term exceeds two");
    }
}

/// <summary>
/// 线性不等式的降低 / Lowering a linear inequality.
/// </summary>
/// <remarks>
/// **这一层仍然不移项。** `2*x &lt;= 3*y + 1` 降低之后，左边是 `2x`、右边是 `3y + 1`。
/// **移项会把比较符方向带进来**（乘负号要翻转），**那是求解那一侧的事；在这里做就会有两处各自翻转。**
///
/// **This layer still does not rearrange.** After lowering `2*x &lt;= 3*y + 1`, the left is `2x` and the right is `3y + 1`.
/// **Rearranging drags the operator direction in** (a negative multiplier flips it), **and that belongs to the solving side; doing it here would flip in two places.**
/// </remarks>
public class LinearInequalityLoweringTests
{
    private static LinearInequality<Int64> Parse(string input) =>
        PolynomialParser<Int64>
            .ParseLinearInequality(input, Int64NumberParser.Instance, Int64.Zero, Int64.One)
            .Value;

    private static bool Rejects(string input) =>
        !PolynomialParser<Int64>
            .ParseLinearInequality(input, Int64NumberParser.Instance, Int64.Zero, Int64.One).IsOk;

    /// <summary>**两边各自保留，符号不搬** / Both sides survive as written; nothing moves.</summary>
    /// <remarks>
    /// **第一版写的是 `Lhs.ToString != Rhs.ToString`，那什么也没证明**：
    /// **即使真的移了项，两边仍然不一样。** 改成**与单独解析 `2*x`、`3*y + 1` 的结果结构相等**——
    /// **移了项这颗钉子必红。**
    ///
    /// **The first version asserted `Lhs.ToString != Rhs.ToString`, which proves nothing**.
    /// **even after rearranging the two sides would still differ.** This compares against parsing `2*x` and `3*y + 1` on their own instead —
    /// **and a rearranged result cannot pass.**
    /// </remarks>
    [Fact]
    public void TheSides_ShouldNotBeRearranged()
    {
        LinearInequality<Int64> inequality = Parse("2*x <= 3*y + 1");

        inequality.Comparison.Should().Be(Comparison.LE,
            "the operator stays as written; rearranging would have to flip it");
        // **`LinearPolynomial` 是 record，但它含集合成员，所以逐值相等退化成引用相等。**
        // **所以这里断的是可观察内容：项数、符号名、常数在哪一边。**
        // **`LinearPolynomial` is a record, but it holds a collection, so value equality degrades to reference equality.**
        // **So this asserts the observable content: how many terms, which symbol, and where the constant sits.**
        inequality.Lhs.Monomials.Should().ContainSingle("2x is one term");
        inequality.Lhs.Monomials[0].Symbol.Name.Should().Be("x", "the left side is x, not y");
        inequality.Lhs.Constant.Should().Be(Int64.Zero, "no constant was moved into the left side");

        inequality.Rhs.Monomials.Should().ContainSingle("3y is one term");
        inequality.Rhs.Monomials[0].Symbol.Name.Should().Be("y", "the right side is y, not x");
        inequality.Rhs.Constant.Should().Be(new Int64(1), "the constant stays on the side it was written");
    }

    /// <summary>**有一边超过一次就拒绝** / One side above degree one refuses the whole thing.</summary>
    [Fact]
    public void TheNonlinearSide_ShouldBeRefused()
    {
        Rejects("x^2 <= 1").Should().BeTrue("the left side is quadratic");
        Rejects("1 <= y^2").Should().BeTrue("the right side is quadratic");
    }

    /// <summary>**线性不等式能解析** / A linear inequality parses.</summary>
    [Fact]
    public void TheLinearInequality_ShouldParse()
    {
        LinearInequality<Int64> inequality = Parse("x + 1 >= 2*y - 3");

        inequality.Comparison.Should().Be(Comparison.GE);
    }
}

/// <summary>
/// 二次不等式的降低 / Lowering a quadratic inequality.
/// </summary>
/// <remarks>
/// **与线性版同一条规矩：不移项。** 多出来的一条是**两边都要过二次这一关**。
///
/// **Same rule as the linear version: no rearranging.** The extra one is **both sides have to pass the quadratic gate**.
/// </remarks>
public class QuadraticInequalityLoweringTests
{
    private static QuadraticInequalityOf<Int64> Parse(string input) =>
        PolynomialParser<Int64>
            .ParseQuadraticInequality(input, Int64NumberParser.Instance, Int64.Zero, Int64.One)
            .Value;

    private static bool Rejects(string input) =>
        !PolynomialParser<Int64>
            .ParseQuadraticInequality(input, Int64NumberParser.Instance, Int64.Zero, Int64.One).IsOk;

    /// <summary>**二次不等式能解析** / A quadratic inequality parses.</summary>
    [Fact]
    public void TheQuadraticInequality_ShouldParse()
    {
        QuadraticInequalityOf<Int64> inequality = Parse("x^2 + 2*x <= 3");

        inequality.Comparison.Should().Be(Comparison.LE);
        inequality.Rhs.Monomials.Should().BeEmpty("the right side is the constant 3");
        inequality.Rhs.Constant.Should().Be(new Int64(3));
    }

    /// <summary>**三次及以上被拒绝，两边都查** / Degree three and above is refused, on either side.</summary>
    /// <remarks>
    /// **只查左边会让 `x &lt;= y^3` 混进来**——**而那个不等式在线性侧与二次侧都装不下，它会一路走到求解器才炸。**
    ///
    /// **Checking only the left lets `x &lt;= y^3` through** — **and it fits neither the linear nor the quadratic shape, so it would blow up in the solver instead.**
    /// </remarks>
    [Fact]
    public void TheCubicSide_ShouldBeRefused()
    {
        Rejects("x^3 <= 1").Should().BeTrue("the left side is cubic");
        Rejects("x <= y^3").Should().BeTrue("the right side is cubic, and one unchecked side is enough to let it through");
    }
}
