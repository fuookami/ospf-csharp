#nullable enable
#if GUROBI_INTEGRATION

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Gurobi;
using Xunit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Algebra.ValueRange;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Variable;
namespace Fuookami.Ospf.Core.Plugin.Gurobi.Tests;

public class LogicalBoundsGurobiIntegrationTest {
    public static IEnumerable<object[]> Cases() {
        for (int kind = 0; kind < 6; ++kind) {
            foreach (double input in new[] { -4.0, 4.0 }) {
                int expected = kind == 2 ? 0 : 1;
                yield return new object[] { kind, input, expected, true };
                yield return new object[] { kind, input, 1 - expected, false };
            }
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task NestedAutomaticBounds_ShouldSolveSelectedEndpoints(
        int kind,
        double input,
        int flag,
        bool expectedFeasible) {
        var x = new RealVar("logical-nested-x");
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-4), new Flt64(4)).Value);
        var nested = new LinearExpressionSymbol(
            polynomial: new MutableLinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(Flt64.One, x) },
                Flt64.Zero
            ),
            name: "logical-nested"
        );
        var polynomial = new LinearPolynomial<Flt64>(
            new[] { new LinearMonomial<Flt64>(Flt64.One, nested) },
            Flt64.Zero
        );
        IMathFunctionSymbol<Flt64> function = kind switch {
            0 => new AndFunction(
                polynomials: new[] { polynomial },
                tolerance: new Flt64(0.125),
                strictBoundary: new Flt64(0.25)
            ),
            1 => new OrFunction(
                polynomials: new[] { polynomial },
                tolerance: new Flt64(0.125),
                strictBoundary: new Flt64(0.25)
            ),
            2 => new NotFunction(
                polynomial: polynomial,
                tolerance: new Flt64(0.125),
                strictBoundary: new Flt64(0.25)
            ),
            3 => new XorFunction(
                polynomials: new[] { polynomial },
                tolerance: new Flt64(0.125),
                strictBoundary: new Flt64(0.25)
            ),
            4 => new OneOfFunction(
                polynomials: new[] { polynomial },
                tolerance: new Flt64(0.125),
                strictBoundary: new Flt64(0.25)
            ),
            5 => new SameAsFunction(
                inequalities: new[] { polynomial.Le(Flt64.Zero) },
                epsilon: new Flt64(0.125),
                constraint: false
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        BinVar resultVar = function switch {
            AndFunction and => and.ResultVar,
            OrFunction or => or.ResultVar,
            NotFunction not => not.ResultVar,
            XorFunction xor => xor.ResultVar,
            OneOfFunction oneOf => oneOf.ResultVar,
            SameAsFunction sameAs => sameAs.ResultVar,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        if (flag == 1) {
            resultVar.Range.SetTrue();
        }
        else {
            resultVar.Range.SetFalse();
        }
        using var parent = new LinearMetaModel<Flt64>();
        parent.Add(x).IsOk.Should().BeTrue();
        parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk.Should().BeTrue();
        parent.AddConstraint(
            new LinearInequality<Flt64>(
                Lhs: new LinearPolynomial<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, x) }, Flt64.Zero),
                Rhs: new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), new Flt64(input)),
                Comparison: Comparison.EQ
            ),
            null
        ).IsOk.Should().BeTrue();
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built =
            await LinearMechanismModel<Flt64>.InvokeAsync(parent);
        built.IsOk.Should().BeTrue($"kind={kind}, input={input}, flag={flag}: {built}");
        using LinearMechanismModel<Flt64> mechanism = built.Value;
        using LinearTriadModel matrix = mechanism.ToLinearTriadModel().Value;
        bool configured = false;
        GurobiLinearSolverCallBack callback = new GurobiLinearSolverCallBack().Configuration((_, native, _, _) => {
            native.Set(GRB.DoubleParam.FeasibilityTol, 1e-9);
            native.Set(GRB.IntParam.DualReductions, 0);
            configured = true;
            return Task.FromResult(Results.OkInstance);
        });
        var solver = new GurobiLinearSolver(callBack: callback);

        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> result =
            await solver.InvokeAsync((ILinearTriadModelView)matrix);
        configured.Should().BeTrue();
        if (expectedFeasible) {
            result.IsOk.Should().BeTrue($"kind={kind}, input={input}, flag={flag}: {result}");
        }
        else {
            result.Should().BeOfType<Failed<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>>()
                .Which.Code.Should().Be(ErrorCode.ORModelInfeasible);
        }
    }
}
#endif
