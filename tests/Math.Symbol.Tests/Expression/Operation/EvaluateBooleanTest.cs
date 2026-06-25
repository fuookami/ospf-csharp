#nullable enable

using System.Collections.Generic;
using Fuookami.Ospf.Math;
using Fuookami.Ospf.Math.Symbol.Expression;
using Fuookami.Ospf.Math.Symbol.Expression.Operation;
using Xunit;

namespace Fuookami.Ospf.Math.Symbol.Expression.Tests
{
    public class EvaluateBooleanTest
    {
        private static readonly IEvaluationContext EmptyCtx = EmptyEvaluationContext.Instance;

        [Fact]
        public void EvaluateEquality_shouldWork()
        {
            var ctx = MapEvaluationContext.FromStringMap(new Dictionary<string, object?> { ["age"] = 18 });
            var expr = new Comparison<object>(ComparisonOperator.Eq,
                new ScalarReference<object>(PropertyPath.Parse("age")),
                new ScalarConstant<object>(18));
            var result = EvaluateBoolean.Evaluate(expr, ctx);
            Assert.True(result is Trivalent.True);
        }

        [Fact]
        public void EvaluateInequality_shouldWork()
        {
            var ctx = MapEvaluationContext.FromStringMap(new Dictionary<string, object?> { ["age"] = 18 });
            var expr = new Comparison<object>(ComparisonOperator.Ne,
                new ScalarReference<object>(PropertyPath.Parse("age")),
                new ScalarConstant<object>(20));
            var result = EvaluateBoolean.Evaluate(expr, ctx);
            Assert.True(result is Trivalent.True);
        }

        [Fact]
        public void EvaluateLessThan_shouldWork()
        {
            var ctx = MapEvaluationContext.FromStringMap(new Dictionary<string, object?> { ["age"] = 15 });
            var expr = new Comparison<object>(ComparisonOperator.Lt,
                new ScalarReference<object>(PropertyPath.Parse("age")),
                new ScalarConstant<object>(18));
            var result = EvaluateBoolean.Evaluate(expr, ctx);
            Assert.True(result is Trivalent.True);
        }

        [Fact]
        public void EvaluateMissingValue_shouldReturnUnknown()
        {
            var expr = new Comparison<object>(ComparisonOperator.Eq,
                new ScalarReference<object>(PropertyPath.Parse("missing")),
                new ScalarConstant<object>(42));
            var result = EvaluateBoolean.Evaluate(expr, EmptyCtx);
            Assert.True(result is Trivalent.Unknown);
        }

        [Fact]
        public void EvaluateAnd_shouldWork()
        {
            var trueExpr = (BooleanExpression)BooleanConstant.True();
            var falseExpr = (BooleanExpression)BooleanConstant.False();
            Assert.True(EvaluateBoolean.Evaluate(new AndExpression(new[] { trueExpr, trueExpr }), EmptyCtx) is Trivalent.True);
            Assert.True(EvaluateBoolean.Evaluate(new AndExpression(new[] { trueExpr, falseExpr }), EmptyCtx) is Trivalent.False);
        }

        [Fact]
        public void EvaluateOr_shouldWork()
        {
            var trueExpr = (BooleanExpression)BooleanConstant.True();
            var falseExpr = (BooleanExpression)BooleanConstant.False();
            Assert.True(EvaluateBoolean.Evaluate(new OrExpression(new[] { falseExpr, trueExpr }), EmptyCtx) is Trivalent.True);
            Assert.True(EvaluateBoolean.Evaluate(new OrExpression(new[] { falseExpr, falseExpr }), EmptyCtx) is Trivalent.False);
        }

        [Fact]
        public void EvaluateNot_shouldWork()
        {
            Assert.True(EvaluateBoolean.Evaluate(new NotExpression(BooleanConstant.True()), EmptyCtx) is Trivalent.False);
            Assert.True(EvaluateBoolean.Evaluate(new NotExpression(BooleanConstant.False()), EmptyCtx) is Trivalent.True);
        }

        [Fact]
        public void EvaluateIsNull_shouldWork()
        {
            var ctx = MapEvaluationContext.FromStringMap(new Dictionary<string, object?> { ["email"] = null });
            var expr = new NullCheck(PropertyPath.Parse("email"), NullCheckType.IsNull);
            Assert.True(EvaluateBoolean.Evaluate(expr, ctx) is Trivalent.True);
        }

        [Fact]
        public void EvaluateIsNotNull_withMissingPath_shouldReturnUnknown()
        {
            var expr = new NullCheck(PropertyPath.Parse("missing"), NullCheckType.IsNotNull);
            Assert.True(EvaluateBoolean.Evaluate(expr, EmptyCtx) is Trivalent.Unknown);
        }

        [Fact]
        public void EvaluateIn_shouldWork()
        {
            var ctx = MapEvaluationContext.FromStringMap(new Dictionary<string, object?> { ["status"] = "active" });
            var value = (ScalarExpression<object>)new ScalarReference<object>(PropertyPath.Parse("status"));
            var candidates = new List<ScalarExpression<object>>
            {
                new ScalarConstant<object>("active"),
                new ScalarConstant<object>("pending"),
            };
            var expr = new InExpression<object>(value, candidates);
            Assert.True(EvaluateBoolean.Evaluate(expr, ctx) is Trivalent.True);
        }

        [Fact]
        public void EvaluateWithMapExtension_shouldWork()
        {
            var expr = (BooleanExpression)new Comparison<object>(ComparisonOperator.Eq,
                new ScalarReference<object>(PropertyPath.Parse("x")),
                new ScalarConstant<object>(42));
            var result = expr.EvaluateWith(new Dictionary<string, object?> { ["x"] = 42 });
            Assert.True(result is Trivalent.True);
        }
    }
}
