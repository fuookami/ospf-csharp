#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Fuookami.Ospf.Math.Algebra.Law;
using Fuookami.Ospf.Math.Algebra.Number;

namespace Fuookami.Ospf.Math.Tests.Algebra.Law
{
    public class GroupLawTest
    {
        private static readonly IReadOnlyCollection<long> Samples =
            Enumerable.Range(-5, 11).Select(i => (long)i).ToList();

        [Fact]
        public void Int64_Addition_GroupLaw_Validate()
        {
            var law = new GroupLaw<long>(
                Samples,
                add: (a, b) => a + b,
                zero: 0L,
                negate: a => -a,
                equal: (a, b) => a == b);
            Assert.True(law.Validate());
        }

        [Fact]
        public void Int64_Addition_CheckAssociative()
        {
            var law = new GroupLaw<long>(
                Samples,
                add: (a, b) => a + b,
                zero: 0L,
                negate: a => -a,
                equal: (a, b) => a == b);
            Assert.True(law.CheckAssociative());
        }

        [Fact]
        public void Int64_Addition_CheckIdentity()
        {
            var law = new GroupLaw<long>(
                Samples,
                add: (a, b) => a + b,
                zero: 0L,
                negate: a => -a,
                equal: (a, b) => a == b);
            Assert.True(law.CheckIdentity());
        }

        [Fact]
        public void Int64_Addition_CheckInverse()
        {
            var law = new GroupLaw<long>(
                Samples,
                add: (a, b) => a + b,
                zero: 0L,
                negate: a => -a,
                equal: (a, b) => a == b);
            Assert.True(law.CheckInverse());
        }

        [Fact]
        public void NonAssociative_Operation_FailsCheckAssociative()
        {
            // Subtraction is not associative: (a - b) - c != a - (b - c)
            var smallSamples = new long[] { 0, 1, 2 };
            var law = new GroupLaw<long>(
                smallSamples,
                add: (a, b) => a - b,
                zero: 0L,
                negate: a => -a,
                equal: (a, b) => a == b);
            Assert.False(law.CheckAssociative());
        }

        [Fact]
        public void WrongIdentity_FailsCheckIdentity()
        {
            var law = new GroupLaw<long>(
                Samples,
                add: (a, b) => a + b,
                zero: 1L, // wrong identity
                negate: a => -a,
                equal: (a, b) => a == b);
            Assert.False(law.CheckIdentity());
        }
    }
}
