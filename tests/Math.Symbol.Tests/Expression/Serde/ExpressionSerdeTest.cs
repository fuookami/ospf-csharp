#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Math;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Expression;
using Fuookami.Ospf.Math.Symbol.Expression.Serde;
using Xunit;

namespace Fuookami.Ospf.Math.Symbol.Expression.Tests
{
    /// <summary>
    /// 标量表达式序列化测试 / Scalar Expression Serde Tests.
    /// </summary>
    public class ScalarExpressionSerdeTests
    {
        [Fact]
        public void ConstantRoundTrip_shouldPreserve()
        {
            var expr = (ScalarExpression<object>)new ScalarConstant<object>(42);
            var json = ExpressionSerde.ToJsonString(expr);
            var restored = ExpressionSerde.ScalarFromJson(json);
            Assert.IsType<ScalarConstant<object>>(restored);
            // JSON numbers may deserialize as long or double depending on representation
            var restoredValue = ((ScalarConstant<object>)restored).Value!;
            Assert.Equal(42.0, Convert.ToDouble(restoredValue));
        }

        [Fact]
        public void StringConstantRoundTrip_shouldPreserve()
        {
            var expr = (ScalarExpression<object>)new ScalarConstant<object>("hello");
            var json = ExpressionSerde.ToJsonString(expr);
            var restored = ExpressionSerde.ScalarFromJson(json);
            Assert.Equal("hello", ((ScalarConstant<object>)restored).Value);
        }

        [Fact]
        public void ReferenceRoundTrip_shouldPreserve()
        {
            var expr = (ScalarExpression<object>)new ScalarReference<object>(PropertyPath.Parse("user.age"));
            var json = ExpressionSerde.ToJsonString(expr);
            var restored = ExpressionSerde.ScalarFromJson(json);
            Assert.IsType<ScalarReference<object>>(restored);
            Assert.Equal("user.age", ((ScalarReference<object>)restored).Path.Value);
        }

        [Fact]
        public void BinaryRoundTrip_shouldPreserve()
        {
            var left = (ScalarExpression<object>)new ScalarConstant<object>(1);
            var right = (ScalarExpression<object>)new ScalarConstant<object>(2);
            var expr = (ScalarExpression<object>)new ScalarBinary<object>(BinaryOperator.Add, left, right);
            var json = ExpressionSerde.ToJsonString(expr);
            var restored = ExpressionSerde.ScalarFromJson(json);
            Assert.IsType<ScalarBinary<object>>(restored);
            Assert.Equal(BinaryOperator.Add, ((ScalarBinary<object>)restored).Operator);
        }

        [Fact]
        public void FunctionRoundTrip_shouldPreserve()
        {
            var arg = (ScalarExpression<object>)new ScalarConstant<object>(42);
            var expr = (ScalarExpression<object>)new ScalarFunction<object>("abs", new[] { arg });
            var json = ExpressionSerde.ToJsonString(expr);
            var restored = ExpressionSerde.ScalarFromJson(json);
            Assert.IsType<ScalarFunction<object>>(restored);
            Assert.Equal("abs", ((ScalarFunction<object>)restored).Name);
        }

        [Fact]
        public void CustomScalarRoundTrip_shouldPreservePayload()
        {
            var expr = (ScalarExpression<object>)new ScalarCustom<object>("payload", "desc");
            var json = ExpressionSerde.ToJsonString(expr);
            var restored = ExpressionSerde.ScalarFromJson(json);
            Assert.IsType<ScalarCustom<object>>(restored);
        }
    }

    /// <summary>
    /// 布尔表达式序列化测试 / Boolean Expression Serde Tests.
    /// </summary>
    public class BooleanExpressionSerdeTests
    {
        [Fact]
        public void BooleanConstantRoundTrip_shouldPreserve()
        {
            var expr = (BooleanExpression)BooleanConstant.True();
            var json = ExpressionSerde.ToJsonString(expr);
            var restored = ExpressionSerde.FromJson(json);
            Assert.IsType<BooleanConstant>(restored);
            Assert.True(((BooleanConstant)restored).IsTrue);
        }

        [Fact]
        public void ComparisonRoundTrip_shouldPreserve()
        {
            var expr = (BooleanExpression)new Comparison<object>(ComparisonOperator.Gt,
                new ScalarReference<object>(PropertyPath.Parse("age")),
                new ScalarConstant<object>(18));
            var json = ExpressionSerde.ToJsonString(expr);
            var restored = ExpressionSerde.FromJson(json);
            Assert.IsType<Comparison<object>>(restored);
            Assert.Equal(ComparisonOperator.Gt, ((Comparison<object>)restored).Operator);
        }

        [Fact]
        public void AndRoundTrip_shouldPreserve()
        {
            var a = (BooleanExpression)BooleanConstant.True();
            var b = (BooleanExpression)BooleanConstant.False();
            var expr = new AndExpression(new[] { a, b });
            var json = ExpressionSerde.ToJsonString(expr);
            var restored = ExpressionSerde.FromJson(json);
            Assert.IsType<AndExpression>(restored);
            Assert.Equal(2, ((AndExpression)restored).Operands.Count);
        }

        [Fact]
        public void OrRoundTrip_shouldPreserve()
        {
            var a = (BooleanExpression)BooleanConstant.True();
            var b = (BooleanExpression)BooleanConstant.False();
            var expr = new OrExpression(new[] { a, b });
            var json = ExpressionSerde.ToJsonString(expr);
            var restored = ExpressionSerde.FromJson(json);
            Assert.IsType<OrExpression>(restored);
        }

        [Fact]
        public void NotRoundTrip_shouldPreserve()
        {
            var expr = new NotExpression(BooleanConstant.True());
            var json = ExpressionSerde.ToJsonString(expr);
            var restored = ExpressionSerde.FromJson(json);
            Assert.IsType<NotExpression>(restored);
        }

        [Fact]
        public void InRoundTrip_shouldPreserve()
        {
            var value = (ScalarExpression<object>)new ScalarReference<object>(PropertyPath.Parse("status"));
            var candidates = new List<ScalarExpression<object>>
            {
                new ScalarConstant<object>("active"),
                new ScalarConstant<object>("pending"),
            };
            var expr = (BooleanExpression)new InExpression<object>(value, candidates);
            var json = ExpressionSerde.ToJsonString(expr);
            var restored = ExpressionSerde.FromJson(json);
            Assert.IsType<InExpression<object>>(restored);
        }

        [Fact]
        public void NullCheckRoundTrip_shouldPreserve()
        {
            var expr = (BooleanExpression)new NullCheck(PropertyPath.Parse("email"), NullCheckType.IsNull);
            var json = ExpressionSerde.ToJsonString(expr);
            var restored = ExpressionSerde.FromJson(json);
            Assert.IsType<NullCheck>(restored);
            Assert.True(((NullCheck)restored).IsNull);
        }

        [Fact]
        public void PathWithDotsRoundTrip_shouldSurvive()
        {
            var expr = (BooleanExpression)new NullCheck(PropertyPath.Parse("user.address.city"), NullCheckType.IsNotNull);
            var json = ExpressionSerde.ToJsonString(expr);
            var restored = ExpressionSerde.FromJson(json);
            Assert.Equal("user.address.city", ((NullCheck)restored).Path.Value);
        }

        [Fact]
        public void CustomBooleanRoundTrip_shouldPreservePayload()
        {
            var expr = (BooleanExpression)new BooleanCustom("payload", "desc");
            var json = ExpressionSerde.ToJsonString(expr);
            var restored = ExpressionSerde.FromJson(json);
            Assert.IsType<BooleanCustom>(restored);
        }
    }

    /// <summary>
    /// 错误处理测试 / Error Handling Tests.
    /// </summary>
    public class SerdeErrorHandlingTests
    {
        [Fact]
        public void InvalidJson_shouldReturnNull()
        {
            Assert.Null(ExpressionSerde.FromJsonOrNull("not json"));
            Assert.Null(ExpressionSerde.ScalarFromJsonOrNull("{invalid}"));
        }
    }
}
