#nullable enable
#if GUROBI_INTEGRATION

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
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
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Symbol.Flatten;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Variable;
namespace Fuookami.Ospf.Core.Plugin.Gurobi.Tests;

public class GenericMechanismGurobiIntegrationTest {
    public static IEnumerable<object[]> Cases() {
        foreach (bool quadratic in new[] { false, true }) {
            foreach (bool cosine in new[] { false, true }) {
                foreach (bool fixedColumn in new[] { false, true }) {
                    foreach (double input in new[] { -0.5, 0.0, 0.5, 4.0 }) {
                        yield return new object[] { quadratic, cosine, fixedColumn, input };
                    }
                }
            }
        }
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task DecimalTrigonometricGraph_ShouldSolveAndApplyDirectly(
        bool quadratic,
        bool cosine,
        bool fixedColumn,
        double input) {
        var converter = new DecimalConverter();
        var x = new RealVar("generic-trig-x");
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-4), new Flt64(4)).Value);
        var polynomial = new LinearPolynomial<FltX>(new[] { new LinearMonomial<FltX>(FltX.One, x) }, FltX.Zero);
        Result<IMathFunctionSymbol<FltX>, ErrorCode, Error<ErrorCode>> created = cosine
            ? CosFunction<FltX>.Create(polynomial, converter).Map<IMathFunctionSymbol<FltX>>(function => function)
            : SinFunction<FltX>.Create(polynomial, converter).Map<IMathFunctionSymbol<FltX>>(function => function);
        Assert.True(created.IsOk, created.ToString());
        IMathFunctionSymbol<FltX> function = created.Value;
        var adapter = new LinearFunctionSymbolAdapter<FltX>(function);
        using AbstractMetaModel<FltX> parent = quadratic
            ? new QuadraticMetaModel<FltX>(converter: converter)
            : new LinearMetaModel<FltX>(converter: converter);
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(adapter).IsOk);
        var value = new FltX(input);
        var relation = new LinearInequality<FltX>(
            Lhs: polynomial,
            Rhs: new LinearPolynomial<FltX>(Array.Empty<LinearMonomial<FltX>>(), value),
            Comparison: Comparison.EQ
        );
        var linearParent = (IAbstractLinearMetaModel<FltX>)parent;
        Assert.True(linearParent.AddConstraint(relation: relation, group: null).IsOk);
        Assert.True(linearParent.AddObject(
            category: ObjectCategory.Minimum,
            flattenData: new LinearFlattenData<FltX>(new[] { new LinearMonomial<FltX>(FltX.One, adapter) }, FltX.Zero),
            name: "generic-trig-objective",
            displayName: null
        ).IsOk);
        IReadOnlyDictionary<IVariableItem, FltX>? fixedValues = fixedColumn
            ? new Dictionary<IVariableItem, FltX> { [x] = value }
            : null;
        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> solved;
        if (parent is LinearMetaModel<FltX> linear) {
            Result<(LinearMechanismModel<FltX> Model, LinearTriadModel Matrix), ErrorCode, Error<ErrorCode>> built =
                await linear.BuildLinearTriadModelAsync(fixedValues);
            Assert.True(built.IsOk, built.ToString());
            using LinearMechanismModel<FltX> model = built.Value.Model;
            using LinearTriadModel matrix = built.Value.Matrix;
            solved = await new GurobiLinearSolver().InvokeAsync((ILinearTriadModelView)matrix);
            if (solved.IsOk) {
                Assert.True(model.ApplySolverSolution(matrix, solved.Value.Solution.Values).IsOk);
            }
        }
        else {
            Result<(QuadraticMechanismModel<FltX> Model, QuadraticTetradModel Matrix), ErrorCode, Error<ErrorCode>> built =
                await ((QuadraticMetaModel<FltX>)parent).BuildQuadraticTetradModelAsync(fixedValues);
            Assert.True(built.IsOk, built.ToString());
            using QuadraticMechanismModel<FltX> model = built.Value.Model;
            using QuadraticTetradModel matrix = built.Value.Matrix;
            solved = await new GurobiQuadraticSolver().InvokeAsync((IQuadraticTetradModelView)matrix);
            if (solved.IsOk) {
                Assert.True(model.ApplySolverSolution(matrix, solved.Value.Solution.Values).IsOk);
            }
        }
        FltX? expected = function.Evaluate(new Dictionary<ISymbol, FltX> { [x] = value });
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
        Result<SolveReport<FltX>, ErrorCode, Error<ErrorCode>> report = solved.Value.ToSolveReport().ConvertTo(converter);
        Assert.True(report.IsOk, report.ToString());
        Assert.Equal(solved.Value.Solution.Length, report.Value.Solution!.Length);
        Assert.InRange(
            actual: report.Value.Solution.Objective!.Value.ToFlt64().ToDouble(),
            low: expected.Value.ToFlt64().ToDouble() - 1e-7,
            high: expected.Value.ToFlt64().ToDouble() + 1e-7
        );
        Assert.Equal(value, parent.Tokens.Find(x)!.Result);
    }

    private sealed class DecimalConverter : IIntoValue<FltX> {
        public FltX Zero => FltX.Zero;
        public FltX One => FltX.One;
        public FltX IntoValue(Flt64 value) => value.ToFltX();
        public Flt64 FromValue(FltX value) => value.ToFlt64();
    }
}
#endif
