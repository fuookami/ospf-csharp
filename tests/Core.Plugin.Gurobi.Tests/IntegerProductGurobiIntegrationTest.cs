#nullable enable
#if GUROBI_INTEGRATION

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Gurobi;
using Xunit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Algebra.ValueRange;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Symbol.Flatten;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Variable;
using Integer = Fuookami.Ospf.Math.Algebra.Number.Int64;
namespace Fuookami.Ospf.Core.Plugin.Gurobi.Tests;

/// <summary>整数乘积原生求解回归 / Native solver regressions for integer products.</summary>
public class IntegerProductGurobiIntegrationTest {
    public static IEnumerable<object[]> Cases() {
        foreach (int integer in new[] { -2, 0, 2 }) {
            foreach (double input in new[] { -3.0, 0.0, 4.0 }) {
                yield return new object[] { integer, input, true };
                yield return new object[] { integer, input, false };
            }
        }
    }

    [Fact]
    public async Task IntegerProduct_ShouldOptimizeWithoutFixedInputs() {
        var integer = new IntVar("integer-product-optimize-integer");
        integer.Range.SetRange(ValueRange<Integer>.Of(new Integer(-2), new Integer(2)).Value);
        var input = new RealVar("integer-product-optimize-input");
        input.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-3), new Flt64(4)).Value);
        IntegerProductFunction<Flt64> function = IntegerProductFunction<Flt64>.Create(
            integer: integer,
            input: new LinearPolynomial<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, input) }, Flt64.Zero),
            converter: IIntoValue<Flt64>.Identity
        ).Value;
        var adapter = new LinearFunctionSymbolAdapter<Flt64>(function);
        using var parent = new LinearMetaModel<Flt64>();
        parent.Add(integer).IsOk.Should().BeTrue();
        parent.Add(input).IsOk.Should().BeTrue();
        parent.Add(adapter).IsOk.Should().BeTrue();
        parent.AddObject(
            category: ObjectCategory.Minimum,
            flattenData: new LinearFlattenData<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, adapter) }, Flt64.Zero),
            name: "integer-product-minimum",
            displayName: null
        ).IsOk.Should().BeTrue();
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        using LinearTriadModel matrix = model.ToLinearTriadModel().Value;
        var solver = new GurobiLinearSolver();
        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> solved =
            await solver.InvokeAsync((ILinearTriadModelView)matrix);
        solved.IsOk.Should().BeTrue(solved.ToString());
        solved.Value.Obj.ToDouble().Should().BeApproximately(-8, 1e-7);
        solved.Value.Solution[matrix.Variables.Single(column => column.Origin == integer).Index]
            .ToDouble().Should().BeApproximately(-2, 1e-7);
        solved.Value.Solution[matrix.Variables.Single(column => column.Origin == input).Index]
            .ToDouble().Should().BeApproximately(4, 1e-7);
    }

    [Fact]
    public async Task IntegerProduct_ShouldSolveWithFixedColumnSubstitution() {
        var integer = new IntVar("integer-product-fixed-integer");
        integer.Range.SetRange(ValueRange<Integer>.Of(new Integer(-2), new Integer(2)).Value);
        var input = new RealVar("integer-product-fixed-input");
        input.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-3), new Flt64(4)).Value);
        IntegerProductFunction<Flt64> function = IntegerProductFunction<Flt64>.Create(
            integer: integer,
            input: new LinearPolynomial<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, input) }, Flt64.Zero),
            converter: IIntoValue<Flt64>.Identity
        ).Value;
        var adapter = new LinearFunctionSymbolAdapter<Flt64>(function);
        using var parent = new LinearMetaModel<Flt64>();
        parent.Add(integer).IsOk.Should().BeTrue();
        parent.Add(input).IsOk.Should().BeTrue();
        parent.Add(adapter).IsOk.Should().BeTrue();
        parent.AddObject(
            category: ObjectCategory.Minimum,
            flattenData: new LinearFlattenData<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, adapter) }, Flt64.Zero),
            name: "integer-product-fixed-objective",
            displayName: null
        ).IsOk.Should().BeTrue();
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        Result<LinearTriadModel, ErrorCode, Error<ErrorCode>> dumped = model.ToLinearTriadModel(
            fixedVariables: new Dictionary<IVariableItem, Flt64> { [integer] = new Flt64(-2), [input] = new Flt64(4) }
        );
        dumped.IsOk.Should().BeTrue();
        using LinearTriadModel matrix = dumped.Value;
        var solver = new GurobiLinearSolver();
        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> solved =
            await solver.InvokeAsync((ILinearTriadModelView)matrix);
        solved.IsOk.Should().BeTrue(solved.ToString());
        solved.Value.Obj.ToDouble().Should().BeApproximately(-8, 1e-7);
        solved.Value.Solution[matrix.Variables.Single(column => column.Origin == function.ResultVar).Index]
            .ToDouble().Should().BeApproximately(-8, 1e-7);
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task IntegerProduct_ShouldSolveOrRejectSelectedAssignments(int integerValue, double inputValue, bool correct) {
        var integer = new IntVar("integer-product-gurobi-integer");
        integer.Range.SetRange(ValueRange<Integer>.Of(new Integer(-2), new Integer(2)).Value);
        var input = new RealVar("integer-product-gurobi-input");
        input.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-3), new Flt64(4)).Value);
        var expression = new LinearPolynomial<Flt64>(
            new[] { new LinearMonomial<Flt64>(Flt64.One, input) },
            new Flt64(0.5)
        );
        Result<IntegerProductFunction<Flt64>, ErrorCode, Error<ErrorCode>> created = IntegerProductFunction<Flt64>.Create(
            integer: integer,
            input: expression,
            converter: IIntoValue<Flt64>.Identity
        );
        created.IsOk.Should().BeTrue();
        IntegerProductFunction<Flt64> function = created.Value;
        var adapter = new LinearFunctionSymbolAdapter<Flt64>(function);
        double expected = integerValue * (inputValue + 0.5);
        using var parent = new LinearMetaModel<Flt64>();
        parent.Add(integer).IsOk.Should().BeTrue();
        parent.Add(input).IsOk.Should().BeTrue();
        parent.Add(adapter).IsOk.Should().BeTrue();
        parent.AddConstraint(relation: Fixed(integer, integerValue), group: null).IsOk.Should().BeTrue();
        parent.AddConstraint(relation: Fixed(input, inputValue), group: null).IsOk.Should().BeTrue();
        parent.AddConstraint(relation: Fixed(function.ResultVar, expected + (correct ? 0 : 1)), group: null).IsOk.Should().BeTrue();
        parent.AddObject(
            category: ObjectCategory.Minimum,
            flattenData: new LinearFlattenData<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, adapter) }, Flt64.Zero),
            name: "integer-product-objective",
            displayName: null
        ).IsOk.Should().BeTrue();
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built = await LinearMechanismModel<Flt64>.InvokeAsync(parent);
        built.IsOk.Should().BeTrue();
        using LinearMechanismModel<Flt64> model = built.Value;
        using LinearTriadModel matrix = model.ToLinearTriadModel().Value;
        bool configured = false;
        GurobiLinearSolverCallBack callback = new GurobiLinearSolverCallBack().Configuration((_, native, _, _) => {
            native.Set(GRB.DoubleParam.FeasibilityTol, 1e-9);
            native.Set(GRB.IntParam.DualReductions, 0);
            configured = true;
            return Task.FromResult(Results.OkInstance);
        });
        var solver = new GurobiLinearSolver(callBack: callback);
        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> solved =
            await solver.InvokeAsync((ILinearTriadModelView)matrix);
        configured.Should().BeTrue();
        if (!correct) {
            solved.Should().BeOfType<Failed<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>>()
                .Which.Code.Should().Be(ErrorCode.ORModelInfeasible);
            return;
        }
        solved.IsOk.Should().BeTrue(solved.ToString());
        solved.Value.Obj.ToDouble().Should().BeApproximately(expected, 1e-7);
        Value(function.InputVar).Should().BeApproximately(inputValue + 0.5, 1e-7);
        Value(function.ResultVar).Should().BeApproximately(expected, 1e-7);
        for (int index = 0; index < function.BitVars.Count; ++index) {
            int bit = ((integerValue + 2) >> index) & 1;
            Value(function.BitVars[index]).Should().BeApproximately(bit, 1e-7);
            Value(function.MaskedVars[index]).Should().BeApproximately(bit * (inputValue + 0.5), 1e-7);
        }
        double Value(IVariableItem variable) => solved.Value.Solution[
            matrix.Variables.Single(column => column.Origin == variable).Index
        ].ToDouble();
    }

    private static LinearInequality<Flt64> Fixed(ISymbol symbol, double value) => new(
        Lhs: new LinearPolynomial<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, symbol) }, Flt64.Zero),
        Rhs: new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), new Flt64(value)),
        Comparison: Comparison.EQ
    );
}
#endif
