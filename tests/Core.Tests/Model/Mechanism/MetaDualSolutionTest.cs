#nullable enable

using System;
using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Model.Mechanism
{
    public class MetaDualSolutionTest
    {
        [Fact]
        public void Create_FromLinearDualMap_ShouldPartitionConstraintsAndSymbols()
        {
            var variable = new RealVar("x");
            var mathConstraint = new TestMathConstraint();
            var tokenTable = new Fuookami.Ospf.Core.Token.ConcurrentAutoTokenTable<Flt64>(
                LinearCategory.Instance, new List<IIntermediateSymbol>());
            tokenTable.Add(variable);

            var monomials = new List<LinearMonomial<Flt64>> { new(Flt64.One, variable) };
            var flattenData = new Fuookami.Ospf.Core.Symbol.Flatten.LinearFlattenData<Flt64>(monomials, Flt64.Zero);
            var relation = new LinearRelationImpl<Flt64>(flattenData, Comparison.LE, "c1");
            var createResult = LinearConstraintImpl<Flt64>.Create(relation, tokenTable,
                new IdentityFlt64Converter(), name: "c1", origin: mathConstraint);
            var constraint = ((Result<LinearConstraintImpl<Flt64>, ErrorCode, Error<ErrorCode>>)createResult).Value;

            var dualMap = new Dictionary<IConstraint<Flt64, LinearCategory>, Flt64>
            {
                { constraint, Flt64.One }
            };

            // MetaDualSolution.Create is a PUBLIC factory (no reflection!)
            var result = MetaDualSolution.Create(dualMap);

            result.Should().NotBeNull();
            result.Constraints.Should().ContainKey(mathConstraint);
            result.Constraints[mathConstraint].Should().Be(Flt64.One);
        }

        [Fact]
        public void Create_EmptyDualMap_ShouldReturnEmptySolution()
        {
            var dualMap = new Dictionary<IConstraint<Flt64, LinearCategory>, Flt64>();

            var result = MetaDualSolution.Create(dualMap);

            result.Should().NotBeNull();
            result.Constraints.Should().BeEmpty();
            result.Symbols.Should().BeEmpty();
        }

        [Fact]
        public void Empty_ShouldReturnSingleton()
        {
            var result = MetaDualSolution.Empty;

            result.Should().NotBeNull();
            result.Constraints.Should().BeEmpty();
            result.Symbols.Should().BeEmpty();
        }

        [Fact]
        public void ToMeta_Extension_ShouldDelegateToCreate()
        {
            var dualMap = new Dictionary<IConstraint<Flt64, LinearCategory>, Flt64>();

            var result = dualMap.ToMeta();

            result.Should().NotBeNull();
            result.Constraints.Should().BeEmpty();
        }

        private class TestMathConstraint : MathConstraint
        {
            public IMetaConstraintGroup? Group => null;
            public bool Lazy => false;
            public object? Args => null;
            public int Priority => 0;
            public string Name => "test";
            public string? DisplayName => null;
        }

        private class IdentityFlt64Converter : IFlt64ValueConverter<Flt64>
        {
            public Flt64 Zero => Flt64.Zero;
            public Flt64 One => Flt64.One;
            public Flt64 IntoValue(Flt64 value) => value;
            public Flt64 FromValue(Flt64 value) => value;
        }
    }
}
