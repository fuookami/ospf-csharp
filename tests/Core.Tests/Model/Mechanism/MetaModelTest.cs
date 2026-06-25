#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
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
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Model.Mechanism
{
    public class MetaModelTest
    {
        [Fact]
        public void LinearMetaModel_AddVariable_ShouldSucceed()
        {
            var model = new LinearMetaModel<Flt64>("test", ObjectCategory.Minimum);
            var variable = new RealVar("x");

            var result = model.Add(variable);

            result.Should().NotBeNull();
            model.Tokens.Find(variable).Should().NotBeNull();
        }

        [Fact]
        public void LinearMetaModel_RemoveVariable_ShouldSucceed()
        {
            var model = new LinearMetaModel<Flt64>("test", ObjectCategory.Minimum);
            var variable = new RealVar("x");
            model.Add(variable);

            model.Remove(variable);

            model.Tokens.Find(variable).Should().BeNull();
        }

        [Fact]
        public void LinearMetaModel_AddConstraint_ShouldAddToConstraints()
        {
            var model = new LinearMetaModel<Flt64>("test", ObjectCategory.Minimum);
            var variable = new RealVar("x");
            model.Add(variable);

            var lhs = new LinearPolynomial<Flt64>(
                new List<LinearMonomial<Flt64>> { new(Flt64.One, variable) },
                Flt64.Zero);
            var rhs = new LinearPolynomial<Flt64>(
                Array.Empty<LinearMonomial<Flt64>>(),
                Flt64.One);
            var inequality = new LinearInequality<Flt64>(lhs, rhs, Comparison.LE);

            var result = model.AddConstraint(inequality, null, name: "c1");

            result.Should().NotBeNull();
            model.Constraints.Should().HaveCount(1);
        }

        [Fact]
        public void LinearMetaModel_AddObject_ShouldSucceed()
        {
            var model = new LinearMetaModel<Flt64>("test", ObjectCategory.Minimum);
            var variable = new RealVar("x");
            model.Add(variable);

            var polynomial = new LinearPolynomial<Flt64>(
                new List<LinearMonomial<Flt64>> { new(Flt64.One, variable) },
                Flt64.Zero);

            var result = model.AddObject(ObjectCategory.Minimum, polynomial, "obj", null);

            result.Should().NotBeNull();
        }

        [Fact]
        public void QuadraticMetaModel_AddQuadraticConstraint_ShouldSucceed()
        {
            var model = new QuadraticMetaModel<Flt64>("test", ObjectCategory.Minimum);
            var variable = new RealVar("x");
            model.Add(variable);

            var qLhs = new QuadraticPolynomial<Flt64>(
                new List<QuadraticMonomial<Flt64>> { new(Flt64.One, variable, variable) },
                Flt64.Zero);
            var qRhs = new QuadraticPolynomial<Flt64>(
                Array.Empty<QuadraticMonomial<Flt64>>(),
                Flt64.One);
            var inequality = new QuadraticInequalityOf<Flt64>(qLhs, qRhs, Comparison.LE);

            var result = model.AddConstraint(inequality, null, name: "q1");

            result.Should().NotBeNull();
            model.Constraints.Should().HaveCount(1);
        }

        [Fact]
        public void LinearMetaModel_RegisterConstraintGroup_ShouldTrackIndices()
        {
            var model = new LinearMetaModel<Flt64>("test", ObjectCategory.Minimum);
            var group = new TestConstraintGroup("group1");
            var variable = new RealVar("x");
            model.Add(variable);

            model.RegisterConstraintGroup(group);
            var lhs = new LinearPolynomial<Flt64>(
                new List<LinearMonomial<Flt64>> { new(Flt64.One, variable) },
                Flt64.Zero);
            var rhs = new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), Flt64.One);
            model.AddConstraint(new LinearInequality<Flt64>(lhs, rhs, Comparison.LE), group, name: "c1");
            model.AddConstraint(new LinearInequality<Flt64>(lhs, rhs, Comparison.LE), group, name: "c2");

            var indices = model.IndicesOfConstraintGroup(group);
            indices.Should().NotBeNull();
            model.ConstraintsOfGroup(group).Should().HaveCount(2);
        }

        [Fact]
        public void LinearMetaModel_Flush_ShouldNotThrow()
        {
            var model = new LinearMetaModel<Flt64>("test", ObjectCategory.Minimum);

            model.Invoking(m => m.Flush()).Should().NotThrow();
            model.Invoking(m => m.Flush(force: true)).Should().NotThrow();
        }

        [Fact]
        public void LinearMetaModel_Dispose_ShouldNotThrow()
        {
            var model = new LinearMetaModel<Flt64>("test", ObjectCategory.Minimum);

            model.Invoking(m => m.Dispose()).Should().NotThrow();
        }

        private class TestConstraintGroup : IMetaConstraintGroup
        {
            public bool Lazy => false;
            public string Name { get; }
            public TestConstraintGroup(string name) => Name = name;
        }
    }
}
