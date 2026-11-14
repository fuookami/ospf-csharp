#nullable enable

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Framework.Solver;

namespace Fuookami.Ospf.Framework.Tests.Solver;

public class LinearBendersCutsTest {
    [Fact]
    public async Task OptimalCut_ShouldIncludeObjectiveConstantsAndFixedCosts() {
        var x = new BinVar("x");
        var y = new URealVar("y");
        y.Range.Leq(new Flt64(2)).Should().BeTrue();
        using var parent = new LinearMetaModel<Flt64>(name: "optimal-cut");
        parent.Add(x).IsOk.Should().BeTrue();
        parent.Add(y).IsOk.Should().BeTrue();
        parent.AddConstraint(
            relation: Recourse(x, y, new Flt64(3)),
            group: null,
            name: "recourse"
        ).IsOk.Should().BeTrue();

        using LinearMechanismModel<Flt64> mechanism =
            (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        IConstraint<Flt64, LinearCategory> row =
            ((IMechanismModel<Flt64>)mechanism).Constraints.Single();
        var dual = new Dictionary<IConstraint<Flt64, LinearCategory>, Flt64>
        {
            [row] = Flt64.One,
        };
        var objective = new LinearPolynomial<Flt64>(
            new[]
            {
                new LinearMonomial<Flt64>(new Flt64(2), x),
                new LinearMonomial<Flt64>(Flt64.One, y),
            },
            new Flt64(5)
        );

        Result<LinearInequality<Flt64>, ErrorCode, Error<ErrorCode>> result = LinearBendersCuts.Optimal(
            theta: new URealVar("theta"),
            fixedVariables: new Dictionary<IVariableItem, Flt64> { [x] = Flt64.Zero },
            dual: dual,
            objective: objective,
            value: new Flt64(8)
        );

        result.IsOk.Should().BeTrue(result.ToString());
        Evaluate(result.Value.Rhs, x, Flt64.Zero).Should().Be(new Flt64(8));
        Evaluate(result.Value.Rhs, x, Flt64.One).Should().Be(new Flt64(9));
    }

    [Fact]
    public async Task FeasibleCut_ShouldUseNativeBoundsAndRejectInvalidRaySigns() {
        var x = new BinVar("x");
        var y = new URealVar("y");
        y.Range.Leq(new Flt64(2)).Should().BeTrue();
        using var parent = new LinearMetaModel<Flt64>(name: "farkas-cut");
        parent.Add(x).IsOk.Should().BeTrue();
        parent.Add(y).IsOk.Should().BeTrue();
        parent.AddConstraint(
            relation: Recourse(x, y, new Flt64(3)),
            group: null,
            name: "recourse"
        ).IsOk.Should().BeTrue();

        using LinearMechanismModel<Flt64> mechanism =
            (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        IConstraint<Flt64, LinearCategory> row =
            ((IMechanismModel<Flt64>)mechanism).Constraints.Single();
        var fixedVariables = new Dictionary<IVariableItem, Flt64> { [x] = Flt64.Zero };
        var validDual = new Dictionary<IConstraint<Flt64, LinearCategory>, Flt64>
        {
            [row] = -Flt64.One,
        };
        var invalidDual = new Dictionary<IConstraint<Flt64, LinearCategory>, Flt64>
        {
            [row] = Flt64.One,
        };

        Result<LinearInequality<Flt64>, ErrorCode, Error<ErrorCode>> cutResult = LinearBendersCuts.Feasible(
            fixedVariables: fixedVariables,
            dual: validDual
        );
        cutResult.IsOk.Should().BeTrue(cutResult.ToString());
        Evaluate(cutResult.Value.Lhs, x, Flt64.Zero)
            .Should().BeGreaterThan(Evaluate(cutResult.Value.Rhs, x, Flt64.Zero));
        Evaluate(cutResult.Value.Lhs, x, Flt64.One)
            .Should().BeLessThanOrEqualTo(Evaluate(cutResult.Value.Rhs, x, Flt64.One));

        LinearBendersCuts.Feasible(
            fixedVariables: fixedVariables,
            dual: invalidDual
        ).IsFailed.Should().BeTrue();
    }

    [Fact]
    public async Task FeasibleCut_ShouldRejectMissingSupportingBounds() {
        var x = new BinVar("x");
        var y = new URealVar("y");
        using var parent = new LinearMetaModel<Flt64>(name: "missing-bound");
        parent.Add(x).IsOk.Should().BeTrue();
        parent.Add(y).IsOk.Should().BeTrue();
        parent.AddConstraint(
            relation: Recourse(x, y, new Flt64(3)),
            group: null,
            name: "recourse"
        ).IsOk.Should().BeTrue();

        using LinearMechanismModel<Flt64> mechanism =
            (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        IConstraint<Flt64, LinearCategory> row =
            ((IMechanismModel<Flt64>)mechanism).Constraints.Single();
        Result<LinearInequality<Flt64>, ErrorCode, Error<ErrorCode>> result = LinearBendersCuts.Feasible(
            fixedVariables: new Dictionary<IVariableItem, Flt64> { [x] = Flt64.Zero },
            dual: new Dictionary<IConstraint<Flt64, LinearCategory>, Flt64>
            {
                [row] = -Flt64.One,
            }
        );

        result.IsFailed.Should().BeTrue();
    }

    private static Flt64 Evaluate(
        LinearPolynomial<Flt64> polynomial,
        IVariableItem variable,
        Flt64 value) {
        Flt64 result = polynomial.Constant;
        foreach (LinearMonomial<Flt64> monomial in polynomial.Monomials) {
            if (monomial.Symbol == variable) {
                result += monomial.Coefficient * value;
            }
        }
        return result;
    }

    private static LinearInequality<Flt64> Recourse(
        IVariableItem x,
        IVariableItem y,
        Flt64 rhs) {
        var lhs = new LinearPolynomial<Flt64>(
            new[]
            {
                new LinearMonomial<Flt64>(Flt64.One, x),
                new LinearMonomial<Flt64>(Flt64.One, y),
            },
            Flt64.Zero
        );
        return lhs.Ge(rhs);
    }
}
