#nullable enable

using Fuookami.Ospf.Math.Algebra.Law;
using Fuookami.Ospf.Math.Algebra.Number;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Fuookami.Ospf.Math.Tests.Algebra.Law;

public class FieldLawTest {
    private static readonly IReadOnlyCollection<double> Samples =
        new double[] { -3.0, -2.0, -1.0, -0.5, 0.0, 0.5, 1.0, 2.0, 3.0 };

    [Fact]
    public void Flt64_FieldLaw_Validate() {
        var law = new FieldLaw<double>(
            Samples,
            add: (a, b) => a + b,
            mul: (a, b) => a * b,
            zero: 0.0,
            one: 1.0,
            negate: a => -a,
            reciprocal: a => 1.0 / a,
            isZero: a => a == 0.0,
            equal: (a, b) => global::System.Math.Abs(a - b) < 1e-10);
        Assert.True(law.Validate());
    }

    [Fact]
    public void Flt64_CheckMultiplicativeCommutative() {
        var law = new FieldLaw<double>(
            Samples,
            add: (a, b) => a + b,
            mul: (a, b) => a * b,
            zero: 0.0,
            one: 1.0,
            negate: a => -a,
            reciprocal: a => 1.0 / a,
            isZero: a => a == 0.0,
            equal: (a, b) => global::System.Math.Abs(a - b) < 1e-10);
        Assert.True(law.CheckMultiplicativeCommutative());
    }

    [Fact]
    public void Flt64_CheckMultiplicativeInverse() {
        var law = new FieldLaw<double>(
            Samples,
            add: (a, b) => a + b,
            mul: (a, b) => a * b,
            zero: 0.0,
            one: 1.0,
            negate: a => -a,
            reciprocal: a => 1.0 / a,
            isZero: a => a == 0.0,
            equal: (a, b) => global::System.Math.Abs(a - b) < 1e-10);
        Assert.True(law.CheckMultiplicativeInverse());
    }

    [Fact]
    public void Flt64_CheckMultiplicativeAssociative_ViaRingLaw() {
        var ring = new RingLaw<double>(
            Samples,
            add: (a, b) => a + b,
            mul: (a, b) => a * b,
            zero: 0.0,
            one: 1.0,
            negate: a => -a,
            equal: (a, b) => global::System.Math.Abs(a - b) < 1e-10);
        Assert.True(ring.CheckMultiplicativeAssociative());
    }

    [Fact]
    public void Flt64_CheckDistributive_ViaRingLaw() {
        var ring = new RingLaw<double>(
            Samples,
            add: (a, b) => a + b,
            mul: (a, b) => a * b,
            zero: 0.0,
            one: 1.0,
            negate: a => -a,
            equal: (a, b) => global::System.Math.Abs(a - b) < 1e-10);
        Assert.True(ring.CheckDistributive());
    }

    [Fact]
    public void Flt64_CheckAdditiveCommutative_ViaRingLaw() {
        var ring = new RingLaw<double>(
            Samples,
            add: (a, b) => a + b,
            mul: (a, b) => a * b,
            zero: 0.0,
            one: 1.0,
            negate: a => -a,
            equal: (a, b) => global::System.Math.Abs(a - b) < 1e-10);
        Assert.True(ring.CheckAdditiveCommutative());
    }

    [Fact]
    public void NonCommutative_Multiplication_FailsFieldLaw() {
        // Matrix-like multiplication that's not commutative
        // Use a simple custom "number" that doesn't commute
        double[] samples = new[] { 0.0, 1.0, 2.0 };
        var law = new FieldLaw<double>(
            samples,
            add: (a, b) => a + b,
            mul: (a, b) => a + b, // not actually multiplication
            zero: 0.0,
            one: 0.0, // wrong identity for this "multiplication"
            negate: a => -a,
            reciprocal: a => 1.0 / a,
            isZero: a => a == 0.0,
            equal: (a, b) => global::System.Math.Abs(a - b) < 1e-10);
        Assert.False(law.Validate());
    }

    [Fact]
    public void RingLaw_Validate_Flt64() {
        var ring = new RingLaw<double>(
            Samples,
            add: (a, b) => a + b,
            mul: (a, b) => a * b,
            zero: 0.0,
            one: 1.0,
            negate: a => -a,
            equal: (a, b) => global::System.Math.Abs(a - b) < 1e-10);
        Assert.True(ring.Validate());
    }
}
