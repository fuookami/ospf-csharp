#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Framework.Plugin.Persistence.MongoDB;
using Fuookami.Ospf.Math.Symbol.Expression;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using MongoDB.Bson;
using Xunit;

// **不能叫 `...Persistence.MongoDB`**：`InterfaceVerificationTests` 里写着全限定的
// `MongoDB.Driver.IMongoDatabase`，而我这个命名空间会把它解析成 `...Tests.Persistence.MongoDB.Driver`，
// **于是"新加一个测试"让"一个已有测试"编译失败。** 编译错误实测：`CS0234 ... 不存在类型或命名空间名"Driver"`。
//
// **Not `...Persistence.MongoDB`**: `InterfaceVerificationTests` spells out `MongoDB.Driver.IMongoDatabase`,
// and this namespace would resolve it to `...Tests.Persistence.MongoDB.Driver` —
// **so adding a test broke an existing one.** The measured error: `CS0234 ... no type or namespace named "Driver"`.
namespace Fuookami.Ospf.Framework.Plugin.Tests.Persistence.MongoTranslators;

/// <summary>
/// **不支持的运算必须失败，不能"看起来没这回事"** / An unsupported operator has to fail, not quietly vanish.
/// </summary>
/// <remarks>
/// 基线 `MongoScalarTranslator`（**209 行**）把标量表达式翻成聚合管道里的值。
/// **B1117 查明 C# 侧四个 Mongo 翻译器一个都没有**（`src/` 下 `BsonDocument` 出现 0 次），
/// **而基线侧这四件共 18 条离线判据——全部不需要 Mongo 实例。** 这一批补第一个。
///
/// **为什么"失败"这件事本身要钉**：把不支持的运算返回 `null` 或原样，
/// **查询会返回一个错误的结果集，而调用方以为查对了**——
/// **这是最难发现的一类错误：它不抛异常，只是答错了。**
///
/// The baseline's `MongoScalarTranslator` (**209 lines**) turns scalar expressions into aggregation-pipeline values.
/// **B1117 found that none of the four C# Mongo translators existed** (`BsonDocument` appeared 0 times under `src/`),
/// **while the baseline carries 18 offline criteria across them — none needing a Mongo instance.** This batch adds the first.
///
/// **Why "fails" itself needs pinning**: returning `null` or the input for an unsupported operator
/// **makes the query return a wrong result set while the caller believes it succeeded** —
/// **the hardest kind of error to notice: it does not throw, it just answers wrongly.**
/// </remarks>
public class MongoScalarTranslatorTests
{
    /// <summary>**引用变成 `$字段`** / A reference becomes `$field`.</summary>
    /// <remarks>
    /// **聚合管道用 `$字段`，查询过滤器用裸字段名**——两种写法不能混。
    /// **写错了 Mongo 会把它当成一个字符串字面量**，于是"字段等于某个值"变成"字符串等于某个值"。
    ///
    /// **The aggregation pipeline spells it `$field`; query filters use the bare name** — the two must not be mixed.
    /// **Get it wrong and Mongo treats it as a string literal**, so "the field equals some value" becomes "a string equals some value".
    /// </remarks>
    [Fact]
    public void TheReference_ShouldBecomeADollarField()
    {
        var reference = new ScalarReference<object?>(new PropertyPath("Amount"));

        Result<BsonValue, ErrorCode, Error<ErrorCode>> result =
            MongoScalarTranslator.Translate(reference);

        result.IsOk.Should().BeTrue();
        result.Value.Should().Be(new BsonString("$Amount"));
    }

    /// <summary>**字段名解析交给调用方** / Field-name resolution belongs to the caller.</summary>
    /// <remarks>
    /// **文档字段名与模型属性名常常不同**（前缀、下划线转换）。
    /// **在这里写死属性名，等于假设两者永远相同**——而那个假设在第一个真实集合上就会破。
    ///
    /// **Document field names and model property names often differ** (prefixes, snake_case conversion).
    /// **Hard-coding the property name assumes the two are always identical** — an assumption that breaks on the first real collection.
    /// </remarks>
    [Fact]
    public void TheResolver_ShouldDecideTheFieldName()
    {
        var reference = new ScalarReference<object?>(new PropertyPath("Amount"));

        Result<BsonValue, ErrorCode, Error<ErrorCode>> result =
            MongoScalarTranslator.Translate(reference, name => "doc_" + name);

        result.Value.Should().Be(new BsonString("$doc_Amount"));
    }

    /// <summary>**二元运算映射到聚合操作符** / Binary operators map to aggregation operators.</summary>
    /// <remarks>
    /// **`+` 变 `$add`、`-` 变 `$subtract`**——**而 `%` 在 Mongo 里叫 `$mod` 不是 `$rem`**，
    /// 这种"名字不按直觉"的映射，写错了不报错、只是算错。
    ///
    /// **`+` becomes `$add` and `-` becomes `$subtract`** — **and `%` is `$mod` in Mongo, not `$rem`**;
    /// mappings whose names are not intuitive fail silently by computing the wrong thing.
    /// </remarks>
    [Theory]
    [InlineData(BinaryOperator.Add, "$add")]
    [InlineData(BinaryOperator.Subtract, "$subtract")]
    [InlineData(BinaryOperator.Multiply, "$multiply")]
    [InlineData(BinaryOperator.Divide, "$divide")]
    [InlineData(BinaryOperator.Modulo, "$mod")]
    public void TheBinaryOperator_ShouldUseTheMongoName(BinaryOperator op, string expected)
    {
        var binary = new ScalarBinary<object?>(
            Operator: op,
            Left: new ScalarReference<object?>(new PropertyPath("A")),
            Right: new ScalarReference<object?>(new PropertyPath("B")));

        Result<BsonValue, ErrorCode, Error<ErrorCode>> result = MongoScalarTranslator.Translate(binary);

        result.IsOk.Should().BeTrue();
        BsonDocument document = result.Value.Should().BeAssignableTo<BsonDocument>().Subject;
        document.Contains(expected).Should().BeTrue(
            $"the operator has to be written as {expected}, not as its C# name");
    }

    /// <summary>**`-x` 写成 `$multiply: [-1, x]`** / `-x` becomes `$multiply: [-1, x]`.</summary>
    /// <remarks>
    /// **聚合管道没有一元减号**——所以负号必须变成乘 -1。
    /// **而 `+x` 是恒等，包一层只会让管道多一步**，所以它原样返回。
    ///
    /// **The aggregation pipeline has no unary minus** — so a negation must become a multiply by -1.
    /// **And `+x` is identity, where wrapping only adds a pipeline stage**, so it passes through.
    /// </remarks>
    [Fact]
    public void TheNegation_ShouldBecomeMultiplyByMinusOne()
    {
        var negate = new ScalarUnary<object?>(
            Operator: UnaryOperator.Negate,
            Operand: new ScalarReference<object?>(new PropertyPath("A")));

        Result<BsonValue, ErrorCode, Error<ErrorCode>> result = MongoScalarTranslator.Translate(negate);

        BsonDocument document = result.Value.Should().BeAssignableTo<BsonDocument>().Subject;
        document.Contains("$multiply").Should().BeTrue();
        document["$multiply"].AsBsonArray[0].ToInt32().Should().Be(-1);
    }

    /// <summary>**不支持的运算必须失败，且给出理由** / An unsupported operator fails with a reason.</summary>
    /// <remarks>
    /// **`ABS` 与 `POWER` 在基线里都不支持**（Mongo 有 `$abs`/`$pow`，基线没接）。
    /// **这里跟着基线失败，不自作主张补上**——**多接一个就是多一个"本仓比基线多"的地方，而那不是同步。**
    ///
    /// **`ABS` and `POWER` are both unsupported in the baseline** (Mongo has `$abs`/`$pow`; the baseline never wired them).
    /// **This fails with the baseline rather than inventing support** — **wiring an extra one is a place where this repo
    /// does more than the baseline, and that is not synchronisation.**
    /// </remarks>
    [Fact]
    public void TheUnsupportedOperators_ShouldFailRatherThanVanish()
    {
        var abs = new ScalarUnary<object?>(
            Operator: UnaryOperator.Abs,
            Operand: new ScalarReference<object?>(new PropertyPath("A")));
        MongoScalarTranslator.Translate(abs).IsFailed.Should().BeTrue(
            "ABS is unsupported in the baseline, and returning the operand would silently drop the absolute value");

        var power = new ScalarBinary<object?>(
            Operator: BinaryOperator.Power,
            Left: new ScalarReference<object?>(new PropertyPath("A")),
            Right: new ScalarReference<object?>(new PropertyPath("B")));
        MongoScalarTranslator.Translate(power).IsFailed.Should().BeTrue(
            "POWER is unsupported in the baseline, and returning null would corrupt the pipeline");
    }

    /// <summary>**`null` 变成 `BsonNull`，不是省略** / `null` becomes `BsonNull`, not an omission.</summary>
    /// <remarks>
    /// **Mongo 里"字段不存在"与"字段是 null"是两件事**，而 CLR 的 `null` 只表达后者。
    /// **用 `BsonNull` 才能让后者被查出来**；省掉它，查询就变成"找不存在的字段"，一条也查不到。
    ///
    /// **In Mongo "the field is absent" and "the field is null" are two things**, and a CLR `null` only expresses the latter.
    /// **`BsonNull` is what makes the latter findable**; omitting it turns the query into "find an absent field", matching nothing.
    /// </remarks>
    [Fact]
    public void TheNullConstant_ShouldBecomeBsonNull()
    {
        var constant = new ScalarConstant<object?>(null);

        Result<BsonValue, ErrorCode, Error<ErrorCode>> result = MongoScalarTranslator.Translate(constant);

        result.Value.Should().Be(BsonNull.Value);
    }

    /// <summary>**深度护栏失败而不是返回原值** / The depth guard fails rather than returning the input.</summary>
    /// <remarks>
    /// **返回原值会让一个没翻译的子树混进管道**——**而管道会"成功"执行，只是结果不对。**
    ///
    /// **Returning the input would slip an untranslated subtree into the pipeline** — **and the pipeline would "succeed", just with the wrong answer.**
    /// </remarks>
    [Fact]
    public void TheDeepExpression_ShouldFailRatherThanLeak()
    {
        ScalarExpression<object?> expression = new ScalarReference<object?>(new PropertyPath("A"));
        for (int level = 0; level <= MongoScalarTranslator.MaxDepth; ++level)
        {
            expression = new ScalarUnary<object?>(UnaryOperator.Negate, expression);
        }

        MongoScalarTranslator.Translate(expression).IsFailed.Should().BeTrue(
            "an expression past the depth limit must fail, not pass an untranslated subtree through");
    }
}
