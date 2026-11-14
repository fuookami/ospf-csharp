#nullable enable

using System;
using System.Collections.Generic;
using Xunit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Algebra.ValueRange;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Variable;
using Integer = Fuookami.Ospf.Math.Algebra.Number.Int64;
namespace Fuookami.Ospf.Core.Tests.Model.Mechanism;

public class LinearConstraintInputPrecisionTest {
    [Fact]
    public void From_ShouldRejectRoundedCoefficientCancellation() {
        var x = new RealVar("constraint-precision-x");
        LinearPolynomial<Flt64> lhs = Polynomial(0, (9007199254740992.0, x), (1, x));
        LinearPolynomial<Flt64> rhs = Polynomial(0, (9007199254740992.0, x));
        Assert.True(From(new LinearInequality<Flt64>(lhs, rhs, Comparison.GT)).IsFailed);
        Assert.True(From(new LinearInequality<Flt64>(
            Lhs: Polynomial(0),
            Rhs: Polynomial(0, (9007199254740992.0, x), (1, x), (-9007199254740992.0, x)),
            Comparison: Comparison.GT
        )).IsFailed);
    }

    [Fact]
    public void From_ShouldRejectRoundedConstantAndNestedExpansion() {
        var x = new RealVar("constraint-precision-nested-x");
        var nested = new LinearExpressionSymbol(
            polynomial: new MutableLinearPolynomial<Flt64>(
                [new LinearMonomial<Flt64>(new Flt64(0.1), x)], Flt64.One),
            name: "constraint-precision-nested"
        );
        Assert.True(From(new LinearInequality<Flt64>(
            Lhs: Polynomial(9007199254740992.0),
            Rhs: Polynomial(-1),
            Comparison: Comparison.LE
        )).IsFailed);
        Assert.True(From(new LinearInequality<Flt64>(
            Lhs: Polynomial(0, (0.1, nested)),
            Rhs: Polynomial(0),
            Comparison: Comparison.LE
        )).IsFailed);
        Assert.True(From(new LinearInequality<Flt64>(
            Lhs: Polynomial(9007199254740992.0, (1, nested)),
            Rhs: Polynomial(0),
            Comparison: Comparison.LE
        )).IsFailed);
    }

    [Fact]
    public void From_ShouldKeepExactNativeDecimalsWithoutSolverConversion() {
        var x = new RealVar("constraint-precision-decimal-x");
        var lhs = new LinearPolynomial<FltX>(
            [new LinearMonomial<FltX>(new FltX(0.1m), x)], new FltX(0.3m)
        );
        var rhs = new LinearPolynomial<FltX>(
            [new LinearMonomial<FltX>(new FltX(0.2m), x)], new FltX(0.1m)
        );
        Result<LinearConstraintInput<FltX>, ErrorCode, Error<ErrorCode>> result = LinearConstraintInput<FltX>.From(
            relation: new LinearInequality<FltX>(lhs, rhs, Comparison.GE),
            lhsRange: ValueRange<FltX>.Full(),
            rhsConstant: new FltX(0.1m)
        );
        Assert.True(result.IsOk, result.ToString());
        Assert.Equal(new FltX(-0.1m), Assert.Single(result.Value.FlattenData.Monomials).Coefficient);
        Assert.Equal(new FltX(0.2m), result.Value.FlattenData.Constant);
    }

    [Fact]
    public void From_ShouldAcceptExactNestedExpansion() {
        var x = new RealVar("constraint-exact-nested-x");
        var nested = new LinearExpressionSymbol(
            polynomial: new MutableLinearPolynomial<Flt64>(
                [new LinearMonomial<Flt64>(new Flt64(2), x)], Flt64.One),
            name: "constraint-exact-nested"
        );
        Result<LinearConstraintInput<Flt64>, ErrorCode, Error<ErrorCode>> result = From(new LinearInequality<Flt64>(
            Lhs: Polynomial(1, (2, nested)),
            Rhs: Polynomial(2, (1, x)),
            Comparison: Comparison.GE
        ));
        Assert.True(result.IsOk, result.ToString());
        Assert.Equal(new Flt64(3), Assert.Single(result.Value.FlattenData.Monomials).Coefficient);
        Assert.Equal(Flt64.One, result.Value.FlattenData.Constant);
    }

    [Fact]
    public void From_ShouldSupportRepresentativeNativeNumbersAndRejectUnprovenType() {
        VerifyNumeric(Flt64.One, Flt64.Zero);
        VerifyNumeric(new Flt32(1), new Flt32(0));
        VerifyNumeric(Integer.One, Integer.Zero);
        VerifyNumeric(FltX.One, FltX.Zero);
        VerifyNumeric(RtnX.One, RtnX.Zero);
        var x = new RealVar("constraint-unsupported-rational");
        var polynomial = new LinearPolynomial<URtn8>(
            [new LinearMonomial<URtn8>(URtn8.One, x)], URtn8.Zero
        );
        Assert.True(LinearConstraintInput<URtn8>.From(
            relation: new LinearInequality<URtn8>(polynomial, polynomial, Comparison.EQ),
            lhsRange: ValueRange<URtn8>.Full(),
            rhsConstant: URtn8.Zero
        ).IsFailed);
    }

    [Fact]
    public void From_ShouldRejectMissingInputAndAcceptDeepExpansionWithoutRecursion() {
        Assert.True(LinearConstraintInput<Flt64>.From(
            relation: null,
            lhsRange: ValueRange<Flt64>.Full(),
            rhsConstant: Flt64.Zero
        ).IsFailed);
        ISymbol current = new RealVar("constraint-deep-x");
        for (int index = 0; index < 1024; ++index) {
            current = new LinearExpressionSymbol(
                polynomial: new MutableLinearPolynomial<Flt64>(
                    [new LinearMonomial<Flt64>(Flt64.One, current)], Flt64.Zero),
                name: $"constraint-deep-{index}"
            );
        }
        Assert.True(From(new LinearInequality<Flt64>(
            Lhs: Polynomial(0, (1, current)),
            Rhs: Polynomial(0),
            Comparison: Comparison.LE
        )).IsOk);
    }

    private static void VerifyNumeric<V>(V one, V zero) where V : struct, IRealNumber<V>, INumberField<V> {
        var x = new RealVar($"constraint-number-{typeof(V).Name}");
        var lhs = new LinearPolynomial<V>([new LinearMonomial<V>(one, x)], one);
        var rhs = new LinearPolynomial<V>(Array.Empty<LinearMonomial<V>>(), zero);
        Result<LinearConstraintInput<V>, ErrorCode, Error<ErrorCode>> result = LinearConstraintInput<V>.From(
            relation: new LinearInequality<V>(lhs, rhs, Comparison.GE),
            lhsRange: ValueRange<V>.Full(),
            rhsConstant: zero
        );
        Assert.True(result.IsOk, result.ToString());
        Assert.True(result.Value.FlattenData.Constant.Eq(one));
        Assert.True(Assert.Single(result.Value.FlattenData.Monomials).Coefficient.Eq(one));
    }

    private static Result<LinearConstraintInput<Flt64>, ErrorCode, Error<ErrorCode>> From(
        LinearInequality<Flt64> inequality) => LinearConstraintInput<Flt64>.From(
        relation: inequality,
        lhsRange: ValueRange<Flt64>.Full(),
        rhsConstant: Flt64.Zero
    );

    private static LinearPolynomial<Flt64> Polynomial(double constant, params (double Coefficient, ISymbol Symbol)[] terms) {
        var monomials = new List<LinearMonomial<Flt64>>();
        foreach ((double coefficient, ISymbol symbol) in terms) {
            monomials.Add(new LinearMonomial<Flt64>(new Flt64(coefficient), symbol));
        }
        return new LinearPolynomial<Flt64>(monomials, new Flt64(constant));
    }
}
