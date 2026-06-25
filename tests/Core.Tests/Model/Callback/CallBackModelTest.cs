#nullable enable

using System;
using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Callback;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Model.Callback
{
    public class CallBackModelTest
    {
        [Fact]
        public void CallBackModel_Create_ShouldSucceed()
        {
            var model = CallBackModel<Flt64>.Create(
                ObjectCategory.Minimum,
                new IdentityFlt64Converter());

            model.Should().NotBeNull();
            model.ObjectCategory.Should().Be(ObjectCategory.Minimum);
        }

        [Fact]
        public void CallBackModel_AddConstraint_ShouldSucceed()
        {
            var model = CallBackModel<Flt64>.Create(
                ObjectCategory.Minimum,
                new IdentityFlt64Converter());

            var result = model.AddConstraint(solution => true, "c1");

            result.Should().NotBeNull();
            model.Constraints.Should().HaveCount(1);
        }

        [Fact]
        public void CallBackModel_Minimize_ShouldSucceed()
        {
            var model = CallBackModel<Flt64>.Create(
                ObjectCategory.Minimum,
                new IdentityFlt64Converter());

            var result = model.Minimize(solution => Flt64.One, "obj1");

            result.Should().NotBeNull();
            model.ObjectiveFunctions.Should().HaveCount(1);
        }

        [Fact]
        public void CallBackModel_Maximize_ShouldSucceed()
        {
            var model = CallBackModel<Flt64>.Create(
                ObjectCategory.Maximum,
                new IdentityFlt64Converter());

            var result = model.Maximize(solution => Flt64.One, "obj1");

            result.Should().NotBeNull();
            model.ObjectiveFunctions.Should().HaveCount(1);
        }

        [Fact]
        public void CallBackModel_ConstraintSatisfied_WithNoViolations_ShouldReturnTrue()
        {
            var model = CallBackModel<Flt64>.Create(
                ObjectCategory.Minimum,
                new IdentityFlt64Converter());
            model.AddConstraint(solution => true, "c1");

            var result = model.ConstraintSatisfied(new List<Flt64> { Flt64.One });

            result.Should().BeTrue();
        }

        [Fact]
        public void CallBackModel_ConstraintSatisfied_WithViolation_ShouldReturnFalse()
        {
            var model = CallBackModel<Flt64>.Create(
                ObjectCategory.Minimum,
                new IdentityFlt64Converter());
            model.AddConstraint(solution => false, "c1");

            var result = model.ConstraintSatisfied(new List<Flt64> { Flt64.One });

            result.Should().BeFalse();
        }

        [Fact]
        public void CallBackModel_Flush_ShouldNotThrow()
        {
            var model = CallBackModel<Flt64>.Create(
                ObjectCategory.Minimum,
                new IdentityFlt64Converter());

            model.Invoking(m => m.Flush()).Should().NotThrow();
        }

        [Fact]
        public void CallBackModel_Dispose_ShouldNotThrow()
        {
            var model = CallBackModel<Flt64>.Create(
                ObjectCategory.Minimum,
                new IdentityFlt64Converter());

            model.Invoking(m => m.Dispose()).Should().NotThrow();
        }

        [Fact]
        public void MultiObjectCallBackModel_Create_ShouldSucceed()
        {
            var locations = new List<MultiObjectLocation<Flt64>>
            {
                new(Fuookami.Ospf.Math.Algebra.Number.UInt64.One, Flt64.One),
                new(new Fuookami.Ospf.Math.Algebra.Number.UInt64(2), Flt64.One)
            };

            var model = MultiObjectCallBackModel<Flt64>.Create(
                ObjectCategory.Minimum,
                locations,
                new IdentityFlt64Converter());

            model.Should().NotBeNull();
            model.ObjectiveLocation.Should().HaveCount(2);
            IMultiObjectiveModelInterface<Flt64> iface = model;
            iface.ObjectiveSize.Should().Be(2);
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
