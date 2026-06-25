#nullable enable

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
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Model.Mechanism;

public class MechanismModelTest {
    [Fact]
    public async Task LinearMechanismModel_InvokeAsync_ShouldBuildFromMetaModel() {
        var metaModel = new LinearMetaModel<Flt64>("test", ObjectCategory.Minimum);
        var x = new RealVar("x");
        var y = new RealVar("y");
        metaModel.Add(x);
        metaModel.Add(y);

        // Add constraint: x + y <= 10
        var lhs = new LinearPolynomial<Flt64>(
            new List<LinearMonomial<Flt64>>
            {
                new(Flt64.One, x),
                new(Flt64.One, y)
            },
            Flt64.Zero);
        var rhs = new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), new Flt64(10));
        metaModel.AddConstraint(new LinearInequality<Flt64>(lhs, rhs, Comparison.LE), null, name: "c1");

        // Add objective: minimize x
        var objPoly = new LinearPolynomial<Flt64>(
            new List<LinearMonomial<Flt64>> { new(Flt64.One, x) },
            Flt64.Zero);
        metaModel.AddObject(ObjectCategory.Minimum, objPoly, "obj", null);

        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> result = await LinearMechanismModel<Flt64>.InvokeAsync(metaModel);

        result.Should().NotBeNull();
        LinearMechanismModel<Flt64> model = ((Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>)result).Value;
        model.Name.Should().Be("test");
        model.NumVariables.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task QuadraticMechanismModel_InvokeAsync_ShouldBuildFromMetaModel() {
        var metaModel = new QuadraticMetaModel<Flt64>("test", ObjectCategory.Minimum);
        var x = new RealVar("x");
        metaModel.Add(x);

        // Add quadratic constraint: x^2 <= 4
        var qLhs = new QuadraticPolynomial<Flt64>(
            new List<QuadraticMonomial<Flt64>> { new(Flt64.One, x, x) },
            Flt64.Zero);
        var qRhs = new QuadraticPolynomial<Flt64>(Array.Empty<QuadraticMonomial<Flt64>>(), new Flt64(4));
        metaModel.AddConstraint(new QuadraticInequalityOf<Flt64>(qLhs, qRhs, Comparison.LE), null, name: "q1");

        Result<QuadraticMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> result = await QuadraticMechanismModel<Flt64>.InvokeAsync(metaModel);

        result.Should().NotBeNull();
        QuadraticMechanismModel<Flt64> model = ((Result<QuadraticMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>)result).Value;
        model.Name.Should().Be("test");
    }

    [Fact]
    public async Task LinearMechanismModel_Dispose_ShouldNotThrow() {
        var metaModel = new LinearMetaModel<Flt64>("test", ObjectCategory.Minimum);
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> buildResult = await LinearMechanismModel<Flt64>.InvokeAsync(metaModel);
        LinearMechanismModel<Flt64> model = ((Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>)buildResult).Value;

        model.Invoking(m => m.Dispose()).Should().NotThrow();
    }
}
