#nullable enable

using System;
using System.Numerics;
using Xunit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Core.Symbol.Function;
using Integer = Fuookami.Ospf.Math.Algebra.Number.Int64;
using Unsigned = Fuookami.Ospf.Math.Algebra.Number.UInt64;
namespace Fuookami.Ospf.Core.Tests.Symbol.Function;

/// <summary>条件三值判定契约 / Three-valued condition classification contracts.</summary>
public class ConditionClassificationTest {
    [Theory]
    [InlineData(Comparison.GT, -1, TruthValue.False)]
    [InlineData(Comparison.GT, 0, TruthValue.False)]
    [InlineData(Comparison.GT, 0.5, TruthValue.Undefined)]
    [InlineData(Comparison.GT, 1, TruthValue.True)]
    [InlineData(Comparison.GE, -1, TruthValue.False)]
    [InlineData(Comparison.GE, -0.5, TruthValue.Undefined)]
    [InlineData(Comparison.GE, 0, TruthValue.True)]
    [InlineData(Comparison.GE, 1, TruthValue.True)]
    [InlineData(Comparison.LT, -1, TruthValue.True)]
    [InlineData(Comparison.LT, -0.5, TruthValue.Undefined)]
    [InlineData(Comparison.LT, 0, TruthValue.False)]
    [InlineData(Comparison.LT, 1, TruthValue.False)]
    [InlineData(Comparison.LE, -1, TruthValue.True)]
    [InlineData(Comparison.LE, 0, TruthValue.True)]
    [InlineData(Comparison.LE, 0.5, TruthValue.Undefined)]
    [InlineData(Comparison.LE, 1, TruthValue.False)]
    public void Relations_ShouldClassifyEndpointsAndGaps(Comparison relation, double value, TruthValue expected) {
        var difference = new Flt64(value);
        Flt64 boundary = Flt64.One;
        Result<TruthValue, ErrorCode, Error<ErrorCode>> continuous = ConditionClassification.Classify(
            difference: difference,
            relation: relation,
            strictBoundary: boundary
        );
        Result<TruthValue, ErrorCode, Error<ErrorCode>> discrete = ConditionClassification.ClassifyDiscrete(
            difference: difference,
            relation: relation,
            delta: new Flt64(2),
            strictBoundary: boundary
        );
        Assert.True(continuous.IsOk, continuous.ToString());
        Assert.True(discrete.IsOk, discrete.ToString());
        Assert.Equal(expected, continuous.Value);
        Assert.Equal(expected, discrete.Value);
    }

    [Fact]
    public void NativeValues_ShouldNotCollapseToTheirDoubleEncodings() {
        var integerGap = new Integer(9007199254740993L);
        AssertClassified(TruthValue.Undefined, ConditionClassification.Classify(
            difference: new Integer(9007199254740992L),
            relation: Comparison.GT,
            strictBoundary: integerGap
        ));
        AssertClassified(TruthValue.False, ConditionClassification.Classify(
            difference: new Integer(long.MinValue),
            relation: Comparison.GT,
            strictBoundary: integerGap
        ));
        AssertClassified(TruthValue.Undefined, ConditionClassification.Classify(
            difference: new FltX(9007199254740993.09m),
            relation: Comparison.GT,
            strictBoundary: new FltX(9007199254740993.1m)
        ));
        AssertClassified(TruthValue.Undefined, ConditionClassification.Classify(
            difference: new RtnX(6004799503160661, 18014398509481984),
            relation: Comparison.GT,
            strictBoundary: new RtnX(1, 3)
        ));
        AssertClassified(TruthValue.Undefined, ConditionClassification.Classify(
            difference: new Unsigned(9007199254740992UL),
            relation: Comparison.GT,
            strictBoundary: new Unsigned(9007199254740993UL)
        ));
    }

    [Fact]
    public void AdjacentFloatValuesAndNegativeZero_ShouldKeepTheirBranches() {
        var boundary = new Flt64(double.Epsilon);
        AssertClassified(TruthValue.True, ConditionClassification.Classify(
            difference: boundary,
            relation: Comparison.GT,
            strictBoundary: boundary
        ));
        AssertClassified(TruthValue.False, ConditionClassification.Classify(
            difference: new Flt64(-0.0),
            relation: Comparison.GT,
            strictBoundary: boundary
        ));
        AssertClassified(TruthValue.True, ConditionClassification.Classify(
            difference: new Flt32(-float.Epsilon),
            relation: Comparison.LT,
            strictBoundary: new Flt32(float.Epsilon)
        ));
        AssertClassified(TruthValue.Undefined, ConditionClassification.Classify(
            difference: boundary,
            relation: Comparison.GT,
            strictBoundary: new Flt64(double.BitIncrement(double.Epsilon))
        ));
        AssertClassified(TruthValue.Undefined, ConditionClassification.Classify(
            difference: new Flt64(double.BitIncrement(-0.125)),
            relation: Comparison.GE,
            strictBoundary: new Flt64(0.125)
        ));
        AssertClassified(TruthValue.False, ConditionClassification.Classify(
            difference: new Flt64(double.BitDecrement(-0.125)),
            relation: Comparison.GE,
            strictBoundary: new Flt64(0.125)
        ));
    }

    [Fact]
    public void RepresentativeNumberTypes_ShouldMatchTheRelationMatrix() {
        VerifyRelations(
            negative: new Flt32(-1),
            zero: new Flt32(0),
            positive: new Flt32(1)
        );
        VerifyRelations(
            negative: new FltX(-1),
            zero: FltX.Zero,
            positive: FltX.One
        );
        VerifyRelations(
            negative: new RtnX(-1, 1),
            zero: RtnX.Zero,
            positive: RtnX.One
        );
        VerifyRelations(
            negative: new Integer(-1),
            zero: Integer.Zero,
            positive: Integer.One
        );
        VerifyRelations(
            negative: new IntX(-1),
            zero: IntX.Zero,
            positive: IntX.One
        );
    }

    [Fact]
    public void InvalidRelationsAndNumericValues_ShouldReturnFailures() {
        foreach (Comparison relation in new[] { Comparison.EQ, Comparison.NE, (Comparison)100 }) {
            Assert.Equal(ErrorCode.IllegalArgument, Assert.IsType<Failed<TruthValue, ErrorCode, Error<ErrorCode>>>(ConditionClassification.Classify(
                difference: Flt64.Zero,
                relation: relation,
                strictBoundary: Flt64.One
            )).Code);
        }
        foreach (double value in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, double.MaxValue, -double.MaxValue }) {
            Assert.True(ConditionClassification.Classify(
                difference: new Flt64(value),
                relation: Comparison.GT,
                strictBoundary: Flt64.One
            ).IsFailed);
        }
        foreach (double gap in new[] { 0, -1, double.NaN, double.PositiveInfinity, double.MaxValue }) {
            Assert.True(ConditionClassification.Classify(
                difference: Flt64.Zero,
                relation: Comparison.GT,
                strictBoundary: new Flt64(gap)
            ).IsFailed);
        }
        Assert.True(ConditionClassification.Classify(
            difference: new RtnX(1, -2),
            relation: Comparison.GT,
            strictBoundary: RtnX.One
        ).IsFailed);
        Assert.True(ConditionClassification.Classify(
            difference: default(RtnX),
            relation: Comparison.GT,
            strictBoundary: RtnX.One
        ).IsFailed);
        Assert.True(ConditionClassification.Classify(
            difference: new RtnX(double.Epsilon, double.MaxValue),
            relation: Comparison.GT,
            strictBoundary: RtnX.One
        ).IsFailed);
        Assert.True(ConditionClassification.Classify(
            difference: URtn8.One,
            relation: Comparison.GT,
            strictBoundary: URtn8.One
        ).IsFailed);
        Assert.True(ConditionClassification.Classify(
            difference: new IntX(BigInteger.Pow(10, 400)),
            relation: Comparison.GT,
            strictBoundary: IntX.One
        ).IsFailed);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-1, 1)]
    [InlineData(1, 0)]
    [InlineData(1, -1)]
    [InlineData(1, 2)]
    [InlineData(double.NaN, 1)]
    [InlineData(1, double.PositiveInfinity)]
    [InlineData(double.MaxValue, 1)]
    public void InvalidDiscreteParameters_ShouldFail(double delta, double strictBoundary) {
        var step = new Flt64(delta);
        var boundary = new Flt64(strictBoundary);
        Assert.True(ConditionClassification.ValidateDiscreteParameters(step, boundary).IsFailed);
        Assert.True(ConditionClassification.ClassifyDiscrete(
            difference: Flt64.Zero,
            relation: Comparison.GT,
            delta: step,
            strictBoundary: boundary
        ).IsFailed);
    }

    [Fact]
    public void DiscreteParameters_ShouldBeComparedBeforeDoubleConversion() {
        var smaller = new Integer(9007199254740992L);
        var larger = new Integer(9007199254740993L);
        Assert.True(ConditionClassification.ValidateDiscreteParameters(smaller, larger).IsFailed);
        Assert.True(ConditionClassification.ValidateDiscreteParameters(larger, smaller).IsOk);
        Assert.True(ConditionClassification.ValidateDiscreteParameters(larger, larger).IsOk);
    }

    private static void AssertClassified(
        TruthValue expected,
        Result<TruthValue, ErrorCode, Error<ErrorCode>> actual) {
        Assert.True(actual.IsOk, actual.ToString());
        Assert.Equal(expected, actual.Value);
    }

    private static void VerifyRelations<V>(V negative, V zero, V positive)
        where V : struct, IRealNumber<V>, INumberField<V> {
        foreach ((Comparison relation, TruthValue[] expected) in new[] {
            (Comparison.GT, new[] { TruthValue.False, TruthValue.False, TruthValue.True }),
            (Comparison.GE, new[] { TruthValue.False, TruthValue.True, TruthValue.True }),
            (Comparison.LT, new[] { TruthValue.True, TruthValue.False, TruthValue.False }),
            (Comparison.LE, new[] { TruthValue.True, TruthValue.True, TruthValue.False }),
        }) {
            V[] values = { negative, zero, positive };
            for (int index = 0; index < values.Length; ++index) {
                AssertClassified(expected[index], ConditionClassification.ClassifyDiscrete(
                    difference: values[index],
                    relation: relation,
                    delta: positive,
                    strictBoundary: positive
                ));
            }
        }
    }
}
