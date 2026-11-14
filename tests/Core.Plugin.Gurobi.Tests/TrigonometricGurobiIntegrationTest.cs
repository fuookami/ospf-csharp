#nullable enable
#if GUROBI_INTEGRATION

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Algebra.ValueRange;
using Fuookami.Ospf.Math.Geometry;
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
namespace Fuookami.Ospf.Core.Plugin.Gurobi.Tests;

public class TrigonometricGurobiIntegrationTest {
    [Theory]
    [InlineData(false, -3.141592653589793, 0, true)]
    [InlineData(false, -1.5707963267948966, -1, true)]
    [InlineData(false, 0, 0, true)]
    [InlineData(false, 0.7853981633974483, 0.5, true)]
    [InlineData(false, 1.5707963267948966, 1, true)]
    [InlineData(false, 3.141592653589793, 0, true)]
    [InlineData(false, 0.7853981633974483, 0, false)]
    [InlineData(false, 4, 0, false)]
    [InlineData(true, -3.141592653589793, -1, true)]
    [InlineData(true, -1.5707963267948966, 0, true)]
    [InlineData(true, 0, 1, true)]
    [InlineData(true, 0.7853981633974483, 0.5, true)]
    [InlineData(true, 1.5707963267948966, 0, true)]
    [InlineData(true, 3.141592653589793, -1, true)]
    [InlineData(true, 0.7853981633974483, 0, false)]
    [InlineData(true, 4, 0, false)]
    public async Task DefaultGraph_ShouldMatchFixedInputAndResult(bool cosine, double input, double output, bool feasible) {
        RealVar x = Variable();
        IMathFunctionSymbol<Flt64> function = Create(x, cosine);
        var adapter = new LinearFunctionSymbolAdapter<Flt64>(function);
        ISymbol resultVariable = ((IHasResultPolynomial<Flt64>)function).ResultPolynomial.Monomials[0].Symbol;
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(adapter).IsOk);
        Assert.True(parent.AddConstraint(relation: Fixed(x, input), group: null).IsOk);
        Assert.True(parent.AddConstraint(relation: Fixed(resultVariable, output), group: null).IsOk);
        Assert.True(parent.AddObject(
            category: ObjectCategory.Minimum,
            flattenData: new LinearFlattenData<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, adapter) }, Flt64.Zero),
            name: "trig-objective",
            displayName: null
        ).IsOk);
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built = await LinearMechanismModel<Flt64>.InvokeAsync(parent);
        Assert.True(built.IsOk, built.ToString());
        using LinearMechanismModel<Flt64> model = built.Value;
        using LinearTriadModel matrix = model.ToLinearTriadModel().Value;
        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> solved =
            await new GurobiLinearSolver().InvokeAsync((ILinearTriadModelView)matrix);
        if (!feasible) {
            Assert.Equal(ErrorCode.ORModelInfeasible,
                Assert.IsType<Failed<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>>(solved).Code);
            return;
        }
        Assert.True(solved.IsOk, solved.ToString());
        Assert.InRange(
            actual: solved.Value.Obj.ToDouble(),
            low: output - 1e-7,
            high: output + 1e-7
        );
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CustomGraph_ShouldPublishEvidenceToDownstreamCondition(bool cosine) {
        RealVar x = Variable();
        Point<Dim2, Flt64>[] points = {
            Point<Dim2, Flt64>.Create(new Flt64(-1), Flt64.Zero),
            Point<Dim2, Flt64>.Create(Flt64.One, new Flt64(2)),
        };
        IMathFunctionSymbol<Flt64> function = Create(
            x: x,
            cosine: cosine,
            points: points
        );
        var inner = new LinearFunctionSymbolAdapter<Flt64>(function);
        var outer = new LinearFunctionSymbolAdapter<Flt64>(IfFunction<Flt64>.Create(
            condition: new LinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(new Flt64(2), inner) }, new Flt64(-1)),
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: Flt64.One
        ).Value);
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(inner).IsOk);
        Assert.True(parent.Add(outer).IsOk);
        Assert.True(parent.AddConstraint(relation: Fixed(x, 0), group: null).IsOk);
        Assert.True(parent.AddObject(
            category: ObjectCategory.Minimum,
            flattenData: new LinearFlattenData<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, outer) }, Flt64.Zero),
            name: "trig-nested-objective",
            displayName: null
        ).IsOk);
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built = await LinearMechanismModel<Flt64>.InvokeAsync(
            metaModel: parent,
            fixedVariables: new Dictionary<IVariableItem, Flt64> { [x] = Flt64.Zero }
        );
        Assert.True(built.IsOk, built.ToString());
        using LinearMechanismModel<Flt64> model = built.Value;
        Assert.Equal(Flt64.One, model.Tokens.CachedValue(inner, (IReadOnlyList<Flt64>?)null));
        Assert.Equal(Flt64.One, model.Tokens.CachedValue(outer, (IReadOnlyList<Flt64>?)null));
        using LinearTriadModel matrix = model.ToLinearTriadModel().Value;
        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> solved =
            await new GurobiLinearSolver().InvokeAsync((ILinearTriadModelView)matrix);
        Assert.True(solved.IsOk, solved.ToString());
        Assert.InRange(
            actual: solved.Value.Obj.ToDouble(),
            low: 1 - 1e-7,
            high: 1 + 1e-7
        );
    }

    [Theory]
    [InlineData(false, -0.5)]
    [InlineData(false, 0.0)]
    [InlineData(false, 0.5)]
    [InlineData(false, 4.0)]
    [InlineData(true, -0.5)]
    [InlineData(true, 0.0)]
    [InlineData(true, 0.5)]
    [InlineData(true, 4.0)]
    public async Task DecimalRowsCopiedToFlt64Fixture_ShouldSolveTheEncodedGraph(bool cosine, double input) {
        RealVar x = Variable();
        var converter = new DecimalConverter();
        var polynomial = new LinearPolynomial<FltX>(new[] { new LinearMonomial<FltX>(FltX.One, x) }, FltX.Zero);
        Result<IMathFunctionSymbol<FltX>, ErrorCode, Error<ErrorCode>> created = cosine
            ? CosFunction<FltX>.Create(polynomial, converter).Map<IMathFunctionSymbol<FltX>>(function => function)
            : SinFunction<FltX>.Create(polynomial, converter).Map<IMathFunctionSymbol<FltX>>(function => function);
        Assert.True(created.IsOk, created.ToString());
        IMathFunctionSymbol<FltX> function = created.Value;
        ISymbol resultVariable = ((IHasResultPolynomial<FltX>)function).ResultPolynomial.Monomials[0].Symbol;
        using var decimalParent = new LinearMetaModel<FltX>(converter: converter);
        Assert.True(decimalParent.Add(x).IsOk);
        Assert.True(decimalParent.Add(new LinearFunctionSymbolAdapter<FltX>(function)).IsOk);
        Result<LinearMechanismModel<FltX>, ErrorCode, Error<ErrorCode>> built = await LinearMechanismModel<FltX>.InvokeAsync(decimalParent);
        Assert.True(built.IsOk, built.ToString());
        using LinearMechanismModel<FltX> decimalModel = built.Value;
        Assert.Equal(22, ((IMechanismModel<FltX>)decimalModel).Constraints.Count);

        using var fixture = new LinearMetaModel<Flt64>();
        foreach (IVariableItem variable in decimalModel.Tokens.TokensInSolver.Select(token => token.Variable)) {
            Assert.True(fixture.Add(variable).IsOk);
        }
        foreach (IConstraint<FltX, LinearCategory> row in ((IMechanismModel<FltX>)decimalModel).Constraints) {
            LinearMonomial<Flt64>[] terms = row.Lhs.Cast<ILinearCell<FltX>>()
                .Select(cell => new LinearMonomial<Flt64>(converter.FromValue(cell.Coefficient), cell.Token.Variable))
                .ToArray();
            var relation = new LinearInequality<Flt64>(
                Lhs: new LinearPolynomial<Flt64>(terms, Flt64.Zero),
                Rhs: new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), converter.FromValue(row.Rhs)),
                Comparison: row.Sign.ToComparison()
            );
            Assert.True(fixture.AddConstraint(
                relation: relation,
                group: null,
                name: row.Name
            ).IsOk);
        }
        Assert.True(fixture.AddConstraint(relation: Fixed(x, input), group: null).IsOk);
        Assert.True(fixture.AddObject(
            category: ObjectCategory.Minimum,
            flattenData: new LinearFlattenData<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, resultVariable) }, Flt64.Zero),
            name: "decimal-pwl-objective",
            displayName: null
        ).IsOk);
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> encoded = await LinearMechanismModel<Flt64>.InvokeAsync(fixture);
        Assert.True(encoded.IsOk, encoded.ToString());
        using LinearMechanismModel<Flt64> model = encoded.Value;
        using LinearTriadModel matrix = model.ToLinearTriadModel().Value;
        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> solved =
            await new GurobiLinearSolver().InvokeAsync((ILinearTriadModelView)matrix);
        FltX? expected = function.Evaluate(new Dictionary<ISymbol, FltX> { [x] = new FltX(input) });
        if (expected is null) {
            Assert.Equal(ErrorCode.ORModelInfeasible,
                Assert.IsType<Failed<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>>(solved).Code);
            return;
        }
        Assert.True(solved.IsOk, solved.ToString());
        Assert.InRange(
            actual: solved.Value.Obj.ToDouble(),
            low: expected.Value.ToFlt64().ToDouble() - 1e-7,
            high: expected.Value.ToFlt64().ToDouble() + 1e-7
        );
    }

    private static IMathFunctionSymbol<Flt64> Create(RealVar x, bool cosine, IReadOnlyList<Point<Dim2, Flt64>>? points = null) {
        var input = new LinearPolynomial<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, x) }, Flt64.Zero);
        return cosine
            ? CosFunction<Flt64>.Create(
                input: input,
                converter: IIntoValue<Flt64>.Identity,
                samplingPoints: points
            ).Value
            : SinFunction<Flt64>.Create(
                input: input,
                converter: IIntoValue<Flt64>.Identity,
                samplingPoints: points
            ).Value;
    }

    private static RealVar Variable() {
        var x = new RealVar("trig-gurobi-x");
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-4), new Flt64(4)).Value);
        return x;
    }

    private static LinearInequality<Flt64> Fixed(ISymbol symbol, double value) => new(
        Lhs: new LinearPolynomial<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, symbol) }, Flt64.Zero),
        Rhs: new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), new Flt64(value)),
        Comparison: Comparison.EQ
    );

    private sealed class DecimalConverter : IIntoValue<FltX> {
        public FltX Zero => FltX.Zero;
        public FltX One => FltX.One;
        public FltX IntoValue(Flt64 value) => value.ToFltX();
        public Flt64 FromValue(FltX value) => value.ToFlt64();
    }
}
#endif
