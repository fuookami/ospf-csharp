#nullable enable

using Fuookami.Ospf.Math;
using Fuookami.Ospf.Math.Symbol.Expression;
using Fuookami.Ospf.Math.Symbol.Expression.Operation;
using Xunit;

namespace Fuookami.Ospf.Math.Symbol.Expression.Tests;

public class NormalizeTest {
    [Fact]
    public void FlattenNestedAnd_shouldMerge() {
        var a = (BooleanExpression)BooleanConstant.True();
        var b = (BooleanExpression)BooleanConstant.False();
        var inner = new AndExpression(new[] { a, b });
        var outer = new AndExpression(new[] { inner, a });
        BooleanExpression result = Normalize.Flatten(outer);
        Assert.IsType<AndExpression>(result);
        Assert.Equal(3, ((AndExpression)result).Operands.Count);
    }

    [Fact]
    public void FlattenNestedOr_shouldMerge() {
        var a = (BooleanExpression)BooleanConstant.True();
        var b = (BooleanExpression)BooleanConstant.False();
        var inner = new OrExpression(new[] { a, b });
        var outer = new OrExpression(new[] { inner, a });
        BooleanExpression result = Normalize.Flatten(outer);
        Assert.IsType<OrExpression>(result);
        Assert.Equal(3, ((OrExpression)result).Operands.Count);
    }

    [Fact]
    public void FlattenSingleOperand_shouldSimplify() {
        var a = (BooleanExpression)BooleanConstant.True();
        var single = new AndExpression(new[] { a });
        BooleanExpression result = Normalize.Flatten(single);
        Assert.IsType<BooleanConstant>(result);
    }

    [Fact]
    public void ConstantFold_andTrue_shouldKeep() {
        var a = (BooleanExpression)new NullCheck(PropertyPath.Parse("x"), NullCheckType.IsNull);
        var expr = new AndExpression(new[] { a, BooleanConstant.True() });
        BooleanExpression result = Normalize.ConstantFold(expr);
        Assert.Equal(a, result);
    }

    [Fact]
    public void ConstantFold_andFalse_shouldReturnFalse() {
        var a = (BooleanExpression)new NullCheck(PropertyPath.Parse("x"), NullCheckType.IsNull);
        var expr = new AndExpression(new[] { a, BooleanConstant.False() });
        BooleanExpression result = Normalize.ConstantFold(expr);
        Assert.True(result is BooleanConstant bc && bc.IsFalse);
    }

    [Fact]
    public void ConstantFold_notTrue_shouldReturnFalse() {
        BooleanExpression result = Normalize.ConstantFold(new NotExpression(BooleanConstant.True()));
        Assert.True(result is BooleanConstant bc && bc.IsFalse);
    }

    [Fact]
    public void Deduplicate_shouldRemoveDuplicates() {
        var a = (BooleanExpression)new NullCheck(PropertyPath.Parse("x"), NullCheckType.IsNull);
        var expr = new AndExpression(new[] { a, a, a });
        BooleanExpression result = Normalize.Deduplicate(expr);
        Assert.Single(((AndExpression)result).Operands);
    }

    [Fact]
    public void DoubleNegation_shouldEliminate() {
        var inner = (BooleanExpression)BooleanConstant.True();
        var expr = new NotExpression(new NotExpression(inner));
        BooleanExpression result = Normalize.EliminateDoubleNegation(expr);
        Assert.Equal(inner, result);
    }

    [Fact]
    public void TripleNegation_shouldLeaveOne() {
        var inner = (BooleanExpression)BooleanConstant.True();
        var expr = new NotExpression(new NotExpression(new NotExpression(inner)));
        BooleanExpression result = Normalize.EliminateDoubleNegation(expr);
        Assert.IsType<NotExpression>(result);
    }

    [Fact]
    public void DeMorgan_andToOr() {
        var a = (BooleanExpression)BooleanConstant.True();
        var b = (BooleanExpression)BooleanConstant.False();
        var inner = new AndExpression(new[] { a, b });
        var expr = new NotExpression(inner);
        BooleanExpression result = Normalize.ApplyDeMorgan(expr);
        Assert.IsType<OrExpression>(result);
    }

    [Fact]
    public void DeMorgan_orToAnd() {
        var a = (BooleanExpression)BooleanConstant.True();
        var b = (BooleanExpression)BooleanConstant.False();
        var inner = new OrExpression(new[] { a, b });
        var expr = new NotExpression(inner);
        BooleanExpression result = Normalize.ApplyDeMorgan(expr);
        Assert.IsType<AndExpression>(result);
    }

    [Fact]
    public void FullNormalize_shouldApplyAll() {
        var a = (BooleanExpression)new NullCheck(PropertyPath.Parse("x"), NullCheckType.IsNull);
        var b = (BooleanExpression)BooleanConstant.True();
        var expr = new AndExpression(new[] { a, b, a });
        BooleanExpression result = Normalize.Apply(expr);
        // After constant fold (remove true) and dedup (remove duplicate a), should be just a
        Assert.Equal(a, result);
    }
}
