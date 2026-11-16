#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Math.Symbol.Expression;
using Fuookami.Ospf.Math.Symbol.Expression.Parser;
using Xunit;

namespace Fuookami.Ospf.Math.Symbol.Tests.Expression.Parser;

/// <summary>
/// 标量算术解析的优先级与结合性 / Precedence and associativity in the scalar arithmetic parser.
/// </summary>
/// <remarks>
/// **这一层的每个判据都能被"能编译但结合性错了"的实现骗过去**，所以每条都断**树形**，不断"解析成功"。
///
/// **Every criterion here can be fooled by an implementation that compiles but associates wrongly**, so each asserts **the tree**, not "it parsed".
/// </remarks>
public class ScalarExpressionParserTests
{
    private static ScalarExpression<object?> Parse(string input) =>
        ScalarExpressionParser.Parse(input).Value;

    private static bool Rejects(string input) => !ScalarExpressionParser.Parse(input).IsOk;

    /// <summary>**乘比加紧** / Multiplication binds tighter than addition.</summary>
    /// <remarks>
    /// **`1 + 2 * 3` 必须是 `Add(1, Multiply(2,3))`。** 反过来也能编译，而且**只在算术上错**。
    ///
    /// **`1 + 2 * 3` has to be `Add(1, Multiply(2,3))`.** The reverse compiles too and **is only wrong arithmetically**.
    /// </remarks>
    [Fact]
    public void TheMultiplication_ShouldBindTighterThanAddition()
    {
        ScalarBinary<object?> root = Parse("1 + 2 * 3").Should().BeOfType<ScalarBinary<object?>>().Subject;

        root.Operator.Should().Be(BinaryOperator.Add);
        root.Right.Should().BeOfType<ScalarBinary<object?>>()
            .Which.Operator.Should().Be(BinaryOperator.Multiply);
    }

    /// <summary>**加法左结合** / Addition is left-associative.</summary>
    [Fact]
    public void TheAddition_ShouldBeLeftAssociative()
    {
        ScalarBinary<object?> root = Parse("1 - 2 - 3").Should().BeOfType<ScalarBinary<object?>>().Subject;

        root.Operator.Should().Be(BinaryOperator.Subtract);
        root.Left.Should().BeOfType<ScalarBinary<object?>>()
            .Which.Operator.Should().Be(BinaryOperator.Subtract, "the left pair groups first");
        root.Right.Should().BeOfType<ScalarConstant<object?>>();
    }

    /// <summary>**幂是右结合** / Power is right-associative.</summary>
    /// <remarks>
    /// **`2^3^2` 是 `2^(3^2) = 512`，不是 `(2^3)^2 = 64`。**
    /// **写成循环会得到后者，而它照样编译、照样"解析成功"。**
    ///
    /// **`2^3^2` is `2^(3^2) = 512`, not `(2^3)^2 = 64`.** **A loop gives the latter, and it still compiles and still "parses".**
    /// </remarks>
    [Fact]
    public void ThePower_ShouldBeRightAssociative()
    {
        ScalarBinary<object?> root = Parse("2 ^ 3 ^ 2").Should().BeOfType<ScalarBinary<object?>>().Subject;

        root.Operator.Should().Be(BinaryOperator.Power);
        root.Left.Should().BeOfType<ScalarConstant<object?>>()
            .Which.Value.Should().Be(2.0, "the base is 2, so the right side groups first");
        root.Right.Should().BeOfType<ScalarBinary<object?>>()
            .Which.Operator.Should().Be(BinaryOperator.Power, "the exponent is itself a power");
    }

    /// <summary>**`**` 与 `^` 是同一个运算符** / `**` and `^` are the same operator.</summary>
    [Fact]
    public void TheDoubleStar_ShouldMeanPower()
    {
        ScalarBinary<object?> root = Parse("2 ** 3").Should().BeOfType<ScalarBinary<object?>>().Subject;

        root.Operator.Should().Be(BinaryOperator.Power);
    }

    /// <summary>**一元负号比幂松** / Unary minus binds looser than power.</summary>
    /// <remarks>
    /// **`-2^2` 是 `-(2^2) = -4`，不是 `(-2)^2 = 4`。** **这两者都能编译，只在数值上分道。**
    ///
    /// **`-2^2` is `-(2^2) = -4`, not `(-2)^2 = 4`.** **Both compile and only the value diverges.**
    /// </remarks>
    [Fact]
    public void TheUnaryMinus_ShouldBindLooserThanPower()
    {
        ScalarUnary<object?> root = Parse("-2 ^ 2").Should().BeOfType<ScalarUnary<object?>>().Subject;

        root.Operator.Should().Be(UnaryOperator.Negate);
        root.Operand.Should().BeOfType<ScalarBinary<object?>>()
            .Which.Operator.Should().Be(BinaryOperator.Power, "the power applies before the negation");
    }

    /// <summary>**括号改变分组** / Parentheses change the grouping.</summary>
    [Fact]
    public void TheParentheses_ShouldRegroup()
    {
        ScalarBinary<object?> root = Parse("(1 + 2) * 3").Should().BeOfType<ScalarBinary<object?>>().Subject;

        root.Operator.Should().Be(BinaryOperator.Multiply);
        root.Left.Should().BeOfType<ScalarBinary<object?>>()
            .Which.Operator.Should().Be(BinaryOperator.Add);
    }

    /// <summary>**标识符成为引用** / An identifier becomes a reference.</summary>
    [Fact]
    public void TheIdentifier_ShouldBecomeAReference() =>
        Parse("x").Should().BeOfType<ScalarReference<object?>>();

    /// <summary>**多余输入被拒绝** / Trailing input is refused.</summary>
    /// <remarks>
    /// **不查 `Eof` 的话 `"1 2"` 会安静地解析成 `1`**——**前半句是真的，后半句被丢掉了，没有任何提示。**
    ///
    /// **Without the `Eof` check `"1 2"` quietly parses as `1`** — **half the input is real, the other half is dropped, and nothing says so.**
    /// </remarks>
    [Fact]
    public void TheTrailingInput_ShouldBeRejected()
    {
        Rejects("1 2").Should().BeTrue("two numbers in a row is not one expression");
        Rejects("(1 + 2").Should().BeTrue("the parenthesis never closes");
        Rejects("1 +").Should().BeTrue("the operator has no right operand");
    }

    /// <summary>**空输入被拒绝，而不是当成零** / Empty input is refused rather than read as zero.</summary>
    /// <remarks>
    /// **"空"和"零"是两件事**：**把空读成零会让一个拼错的配置项安静地变成合法输入。**
    ///
    /// **"Empty" and "zero" are different things**: **reading empty as zero turns a mistyped configuration entry into valid input, quietly.**
    /// </remarks>
    [Fact]
    public void TheEmptyInput_ShouldBeRejected() => Rejects("").Should().BeTrue();

    /// <summary>**比较产生布尔，逻辑把布尔接起来** / Comparison yields a boolean, and logic joins booleans.</summary>
    [Fact]
    public void TheComparison_ShouldYieldAWrappedBoolean()
    {
        ScalarBoolean<object?> comparison = Parse("1 < 2").Should().BeOfType<ScalarBoolean<object?>>().Subject;

        comparison.Expr.Should().BeOfType<Comparison<object?>>()
            .Which.Operator.Should().Be(ComparisonOperator.Lt);
    }

    /// <summary>**比较不链式** / Comparison does not chain.</summary>
    /// <remarks>
    /// **`1 &lt; 2 &lt; 3` 必须被拒绝。** 用 `while` 写的版本会把它变成嵌套比较，**那照样编译、照样"解析成功"，而那是另一门语言的语义。**
    /// </remarks>
    [Fact]
    public void TheChainedComparison_ShouldBeRejected() =>
        Rejects("1 < 2 < 3").Should().BeTrue("comparison does not chain; a while loop would silently accept it");

    /// <summary>**逻辑运算接受布尔、拒绝数值** / Logic takes booleans and refuses numbers.</summary>
    [Fact]
    public void TheLogic_ShouldRefuseNonBooleanOperands()
    {
        Parse("1 < 2 and 3 < 4").Should().BeOfType<ScalarBoolean<object?>>()
            .Which.Expr.Should().BeOfType<AndExpression>();

        Rejects("1 and 2").Should().BeTrue("two numbers are not booleans");
        Rejects("1 < 2 or 3").Should().BeTrue("the right side of or must be boolean too");
    }

    /// <summary>**逻辑是扁平的，不是右倾嵌套** / Logic is flat, not right-leaning.</summary>
    /// <remarks>**`a and b and c` 三个操作数在同一层；嵌套写法在语义上等价，**但落盘与比较都会不一样。**</remarks>
    [Fact]
    public void TheLogic_ShouldBeFlat()
    {
        AndExpression and = Parse("1 < 2 and 3 < 4 and 5 < 6").Should().BeOfType<ScalarBoolean<object?>>()
            .Which.Expr.Should().BeOfType<AndExpression>().Subject;

        and.Operands.Should().HaveCount(3, "three comparisons on one flat level");
    }

    /// <summary>**三元两支各自递归，所以右结合** / Both ternary branches recurse, so it is right-associative.</summary>
    [Fact]
    public void TheTernary_ShouldBeRightAssociative()
    {
        ScalarConditional<object?> outer = Parse("1 < 2 ? 10 : 1 < 3 ? 20 : 30")
            .Should().BeOfType<ScalarConditional<object?>>().Subject;

        outer.Else.Should().BeOfType<ScalarConditional<object?>>(
            "the nested ternary sits in the else branch, not the then branch");
        outer.Then.Should().BeOfType<ScalarConstant<object?>>();
    }

    /// <summary>**`then` 那一支也递归回三元** / The `then` branch recurses into the ternary too.</summary>
    /// <remarks>
    /// **这一条是补上一个判据缺口**：原来那条只钉了 `else` 里的嵌套，
    /// **于是把 `thenBranch = ParseTernary` 改成 `ParseLogicalOr` 之后，判据照样全绿。**
    /// **两支都递归，所以两支都得钉。**
    ///
    /// **This fills a criterion gap**: the original one pinned only the nesting in `else`,
    /// **so replacing `thenBranch = ParseTernary` with `ParseLogicalOr` left everything green.**
 /// **The implementation recurses on both branches, so both have to be pinned.**
    /// </remarks>
    [Fact]
    public void TheThenBranch_ShouldAlsoAllowANestedTernary()
    {
        ScalarConditional<object?> outer = Parse("1 < 2 ? 3 < 4 ? 5 : 6 : 7")
            .Should().BeOfType<ScalarConditional<object?>>(
                "a ternary in the then position has to parse; only recursing on else refuses it").Subject;

        outer.Then.Should().BeOfType<ScalarConditional<object?>>(
            "the nested ternary sits in the then branch");
        outer.Else.Should().BeOfType<ScalarConstant<object?>>(
            "the final 7 lands in the outer else");
    }
    /// <summary>**三元条件是数值时被拒绝** / A numeric ternary condition is refused.</summary>
    /// <remarks>**`1 ? 2 : 3` 没有任何意义，而放宽条件会让它变成合法输入。**</remarks>
    [Fact]
    public void TheNumericCondition_ShouldBeRejected()
    {
        Rejects("1 ? 2 : 3").Should().BeTrue("a number is not a condition");
        Rejects("1 < 2 ? 3").Should().BeTrue("the colon is missing");
    }


    /// <summary>**`if-then-else-fi` 落到与三元同一个节点** / `if-then-else-fi` lowers onto the same node as the ternary.</summary>
    /// <remarks>
    /// **它们是同一个东西的两种写法。** 分成两个节点之后，下游每处判断都得写两遍，**而两遍迟早会不一致。**
    /// </remarks>
    [Fact]
    public void TheIfThenElse_ShouldLendOnScalarConditional()
    {
        ScalarConditional<object?> conditional = Parse("if 1 < 2 then 10 else 20 fi")
            .Should().BeOfType<ScalarConditional<object?>>().Subject;

        conditional.Condition.Should().BeOfType<Comparison<object?>>();
        conditional.Then.Should().BeOfType<ScalarConstant<object?>>().Which.Value.Should().Be(10.0);
        conditional.Else.Should().BeOfType<ScalarConstant<object?>>().Which.Value.Should().Be(20.0);
    }

    /// <summary>**`fi` 不能省，`then`/`else` 也不能省** / Neither `fi` nor `then` nor `else` may be omitted.</summary>
    /// <remarks>
    /// **`if` 可以嵌套，没有结束标记就没办法知道哪一层在哪结束。**
    /// **三个关键字各缺一次都拒**——**只查一个的话，另外两个的缺失会走成别处的报错，而那时位置已经不对了。**
    /// </remarks>
    [Fact]
    public void TheMissingKeywords_ShouldBeRejected()
    {
        Rejects("if 1 < 2 then 10 else 20").Should().BeTrue("fi is missing");
        Rejects("if 1 < 2 10 else 20 fi").Should().BeTrue("then is missing");
        Rejects("if 1 < 2 then 10 20 fi").Should().BeTrue("else is missing");
    }

    /// <summary>**`if` 的条件必须是布尔** / The `if` condition has to be boolean.</summary>
    [Fact]
    public void TheNumericIfCondition_ShouldBeRejected() =>
        Rejects("if 1 then 10 else 20 fi").Should().BeTrue("a number is not a condition");

    /// <summary>**`if` 与三元可以互相嵌套** / `if` and the ternary nest in each other.</summary>
    /// <remarks>**两支都递归回三元，而三元又能回头进 `if`**——**这才是可嵌套的实际含义。**</remarks>
    [Fact]
    public void TheIfAndTernary_ShouldNestBothWays()
    {
        Parse("if 1 < 2 then 1 < 3 ? 4 : 5 else 6 fi")
            .Should().BeOfType<ScalarConditional<object?>>()
            .Which.Then.Should().BeOfType<ScalarConditional<object?>?>(
                "a ternary in the then branch of an if");

        Parse("1 < 2 ? (if 1 < 3 then 4 else 5 fi) : 6")
            .Should().BeOfType<ScalarConditional<object?>>()
            .Which.Then.Should().BeOfType<ScalarConditional<object?>?>(
                "an if in the then branch of a ternary");
    }


    /// <summary>**函数调用带参数，名字剥掉 `math.` 前缀** / A call carries arguments, and its name loses the `math.` prefix.</summary>
    /// <remarks>
    /// **归一化只发生在一处**：**带前缀与不带前缀的两个名字，下游只该看到一个。**
    /// </remarks>
    [Fact]
    public void TheFunctionCall_ShouldStripTheMathPrefix()
    {
        ScalarFunction<object?> call = Parse("math.sqrt(2)")
            .Should().BeOfType<ScalarFunction<object?>>().Subject;

        call.Name.Should().Be("sqrt", "the prefix is stripped once, here");
        call.Arguments.Should().ContainSingle();
    }

    /// <summary>**多个参数按顺序，且零参数合法** / Arguments keep their order, and zero arguments are legal.</summary>
    [Fact]
    public void TheArguments_ShouldKeepTheirOrder()
    {
        Parse("f(1, 2, 3)").Should().BeOfType<ScalarFunction<object?>>()
            .Which.Arguments.Should().HaveCount(3, "three arguments were written");

        Parse("g()").Should().BeOfType<ScalarFunction<object?>>()
            .Which.Arguments.Should().BeEmpty("a call with no arguments is still a call");
    }

    /// <summary>**`math.PI` / `math.E` 是常量，不是引用也不是函数** / They are constants, not references or calls.</summary>
    /// <remarks>
    /// **词法上它们是标识符**，所以**如果不在这一处换掉，它们会变成一个名叫 `math.PI` 的引用，而那个引用永远求不出值。**
    /// </remarks>
    [Fact]
    public void TheMathConstants_ShouldBecomeNumbers()
    {
        Parse("math.PI").Should().BeOfType<ScalarConstant<object?>>()
            .Which.Value.Should().Be(global::System.Math.PI);
        Parse("math.E").Should().BeOfType<ScalarConstant<object?>>()
            .Which.Value.Should().Be(global::System.Math.E);
    }

    /// <summary>**参数本身可以是完整表达式** / An argument may itself be a full expression.</summary>
    /// <remarks>**参数调的是三元层**，所以 `f(a ? b : c)` 与 `f(if... fi)` 都该合法——**只调到加法层就会把它们拒掉。**</remarks>
    [Fact]
    public void TheArguments_ShouldAdmitFullExpressions()
    {
        Parse("f(1 < 2 ? 3 : 4)").Should().BeOfType<ScalarFunction<object?>>()
            .Which.Arguments[0].Should().BeOfType<ScalarConditional<object?>>();

        Parse("f(if 1 < 2 then 3 else 4 fi)").Should().BeOfType<ScalarFunction<object?>>()
            .Which.Arguments[0].Should().BeOfType<ScalarConditional<object?>>();
    }

    /// <summary>**缺右括号被拒绝** / A missing right parenthesis is refused.</summary>
    [Fact]
    public void TheUnclosedCall_ShouldBeRejected()
    {
        Rejects("f(1, 2").Should().BeTrue("the call never closes");
        Rejects("f(1,").Should().BeTrue("a trailing comma has no argument after it");
    }

}
