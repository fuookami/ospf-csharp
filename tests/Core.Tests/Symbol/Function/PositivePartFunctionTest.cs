#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Algebra.ValueRange;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Variable;
using NInt64 = Fuookami.Ospf.Math.Algebra.Number.Int64;
namespace Fuookami.Ospf.Core.Tests.Symbol.Function;

public class PositivePartFunctionTest {
    [Fact]
    public async Task NumericTypes_ShouldEnforceTheGraphWithoutAnObjective() {
        await VerifyGraph(IIntoValue<Flt64>.Identity);
        await VerifyGraph(new TestNumberConverter<Flt32>(value => value.ToFlt32()));
        await VerifyGraph(new TestNumberConverter<FltX>(value => value.ToFltX()));
        await VerifyGraph(new TestNumberConverter<NInt64>(value => value.ToInt64()));
        await VerifyGraph(new TestNumberConverter<RtnX>(value => new RtnX(value.ToDouble(), 1)));
    }

    [Fact]
    public async Task AffineExpression_ShouldUseAllTermsAndItsConstant() {
        RealVar x = Bounded();
        var y = new RealVar("positive-y");
        y.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-2), new Flt64(2)).Value);
        var polynomial = new LinearPolynomial<Flt64>(new[] {
            new LinearMonomial<Flt64>(Flt64.One, x),
            new LinearMonomial<Flt64>(Flt64.One, x),
            new LinearMonomial<Flt64>(new Flt64(-1), y),
        }, Flt64.One);
        PositivePartFunction<Flt64> function = Create(polynomial, IIntoValue<Flt64>.Identity);
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(y).IsOk);
        Assert.True(parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk);
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        using LinearTriadModel matrix = model.ToLinearTriadModel().Value;
        foreach ((double first, double second) in new[] { (-4.0, 2.0), (0.0, 1.0), (2.0, -1.0) }) {
            double expected = System.Math.Max((2 * first) - second + 1, 0);
            Assert.Equal(new Flt64(expected), function.TryEvaluate(new Dictionary<ISymbol, Flt64> {
                [x] = new Flt64(first), [y] = new Flt64(second),
            }).Value);
            Assert.True(Feasible(expected));
            Assert.False(Feasible(expected + 1));

            bool Feasible(double result) => Enumerable.Range(0, 2).Any(selected => {
                var values = new Dictionary<IVariableItem, Flt64> {
                    [x] = new Flt64(first),
                    [y] = new Flt64(second),
                    [function.ResultVar] = new Flt64(result),
                    [function.SelectorVars[0]] = new Flt64(selected == 0 ? 1 : 0),
                    [function.SelectorVars[1]] = new Flt64(selected == 1 ? 1 : 0),
                };
                return SatisfiedAmountResidualTest.ExactFeasible(model, values) &&
                    SatisfiedAmountResidualTest.ExactFeasible(matrix, values);
            });
        }
    }

    [Theory]
    [InlineData(-3)]
    [InlineData(0)]
    [InlineData(4)]
    public void ConstantInput_ShouldReturnItsPositivePart(int input) {
        var polynomial = new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), new Flt64(input));
        PositivePartFunction<Flt64> function = Create(polynomial, IIntoValue<Flt64>.Identity);
        Assert.Equal(new Flt64(System.Math.Max(input, 0)), function.TryEvaluate(new Dictionary<ISymbol, Flt64>()).Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NestedInput_ShouldRejectChangedExpansionAndRestoreAfterRebuild(bool quadratic) {
        RealVar x = Bounded();
        var nested = new LinearExpressionSymbol(
            name: "nested-positive-input",
            polynomial: new MutableLinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(Flt64.One, x) }, Flt64.Zero)
        );
        PositivePartFunction<Flt64> function = Create(Polynomial(nested), IIntoValue<Flt64>.Identity);
        using AbstractMetaModel<Flt64> parent = quadratic ? new QuadraticMetaModel<Flt64>() : new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(nested).IsOk);
        Assert.True(parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk);
        using IMechanismModel<Flt64> model = await Build(parent);
        using IDisposable matrix = Dump(model);

        nested.AsMutable().SetConstant(Flt64.One);
        Assert.True(DumpFails(model));
        Assert.True(ValidateFails(model, matrix));
        Assert.True(ApplyFails(model, matrix));
        nested.AsMutable().SetConstant(Flt64.Zero);
        using IDisposable restored = Dump(model);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ChangedConverter_ShouldRejectDumpAndSolution(bool quadratic, bool modelConverter) {
        RealVar x = Bounded();
        var functionConverter = new SwitchingConverter();
        SwitchingConverter parentConverter = modelConverter ? new SwitchingConverter() : functionConverter;
        PositivePartFunction<Flt64> function = Create(Polynomial(x), functionConverter);
        using AbstractMetaModel<Flt64> parent = quadratic
            ? new QuadraticMetaModel<Flt64>(converter: parentConverter)
            : new LinearMetaModel<Flt64>(converter: parentConverter);
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk);
        using IMechanismModel<Flt64> model = await Build(parent);
        using IDisposable matrix = Dump(model);

        (modelConverter ? parentConverter : functionConverter).CorruptValue = -4;
        Assert.True(DumpFails(model));
        Assert.True(ValidateFails(model, matrix));
        Assert.True(ApplyFails(model, matrix));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task IncompatibleModelConverter_ShouldFailBeforeCommit(bool quadratic, bool inputConversion) {
        RealVar x = Bounded();
        PositivePartFunction<Flt64> function = Create(Polynomial(x), IIntoValue<Flt64>.Identity);
        var converter = new SwitchingConverter {
            CorruptValue = inputConversion ? null : -4,
            CorruptInput = inputConversion ? -4 : null,
        };
        using AbstractMetaModel<Flt64> parent = quadratic
            ? new QuadraticMetaModel<Flt64>(converter: converter)
            : new LinearMetaModel<Flt64>(converter: converter);
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Tokens.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk);
        Result<IMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> result = quadratic
            ? (await QuadraticMechanismModel<Flt64>.InvokeAsync((QuadraticMetaModel<Flt64>)parent)).Map<IMechanismModel<Flt64>>(model => model)
            : (await LinearMechanismModel<Flt64>.InvokeAsync((LinearMetaModel<Flt64>)parent)).Map<IMechanismModel<Flt64>>(model => model);
        if (result.IsOk) {
            result.Value.Dispose();
        }
        Assert.True(result.IsFailed);
        Assert.All(function.HelperVariables, helper => Assert.Null(parent.Tokens.Find(helper)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChangedHelperBeforeCommit_ShouldRollBackTokens(bool quadratic) {
        RealVar x = Bounded();
        PositivePartFunction<Flt64> function = Create(Polynomial(x), IIntoValue<Flt64>.Identity);
        using AbstractMetaModel<Flt64> parent = quadratic ? new QuadraticMetaModel<Flt64>() : new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Tokens.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk);
        bool called = false;
        Result<IMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built = quadratic
            ? (await QuadraticMechanismModel<Flt64>.InvokeBeforeCommitAsync(
                metaModel: (QuadraticMetaModel<Flt64>)parent,
                fixedVariables: null,
                registrationStatusCallBack: null,
                beforeCommit: _ => Change()
            )).Map<IMechanismModel<Flt64>>(model => model)
            : (await LinearMechanismModel<Flt64>.InvokeBeforeCommitAsync(
                metaModel: (LinearMetaModel<Flt64>)parent,
                fixedVariables: null,
                registrationStatusCallBack: null,
                beforeCommit: _ => Change()
            )).Map<IMechanismModel<Flt64>>(model => model);
        if (built.IsOk) {
            built.Value.Dispose();
        }
        Assert.True(called);
        Assert.True(built.IsFailed);
        Assert.All(function.HelperVariables, helper => Assert.Null(parent.Tokens.Find(helper)));

        Result<Success, ErrorCode, Error<ErrorCode>> Change() {
            called = true;
            function.ResultVar.Range.Leq(Flt64.Zero);
            return Results.OkInstance;
        }
    }

    [Fact]
    public async Task Preparation_ShouldNotPublishUnverifiedHelperValues() {
        RealVar x = Bounded();
        PositivePartFunction<Flt64> function = Create(Polynomial(x), IIntoValue<Flt64>.Identity);
        var adapter = new LinearFunctionSymbolAdapter<Flt64>(function);
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Tokens.Add(adapter).IsOk);
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built = await LinearMechanismModel<Flt64>.InvokeAsync(
            metaModel: parent,
            fixedVariables: new Dictionary<IVariableItem, Flt64> { [x] = new Flt64(2) }
        );
        Assert.True(built.IsOk, built.ToString());
        using LinearMechanismModel<Flt64> model = built.Value;
        Assert.Null(model.Tokens.CachedValue(adapter, (IReadOnlyList<Flt64>?)null));
        Assert.Equal(new Flt64(2), function.TryEvaluate(new Dictionary<ISymbol, Flt64> { [x] = new Flt64(2) }).Value);
    }

    [Fact]
    public void Create_ShouldValidateFiniteInputsAndSnapshotTerms() {
        RealVar x = Bounded();
        var terms = new List<LinearMonomial<Flt64>> { new(Flt64.One, x) };
        PositivePartFunction<Flt64> function = Create(new LinearPolynomial<Flt64>(terms, Flt64.Zero), IIntoValue<Flt64>.Identity);
        terms.Clear();
        Assert.Single(function.Input.Monomials);
        Assert.Throws<NotSupportedException>(() => ((IList<LinearMonomial<Flt64>>)function.Input.Monomials).Clear());
        Assert.Null(function.TryEvaluate(new Dictionary<ISymbol, Flt64>()).Value);
        Assert.True(function.TryEvaluate(new Dictionary<ISymbol, Flt64> { [x] = new Flt64(double.NaN) }).IsFailed);
        Assert.True(PositivePartFunction<Flt64>.Create(null, IIntoValue<Flt64>.Identity).IsFailed);
        Assert.True(PositivePartFunction<Flt64>.Create(Polynomial(x), null).IsFailed);
        var unbounded = new RealVar("unbounded");
        unbounded.Range.SetRange(ValueRange<Flt64>.Full());
        Assert.True(PositivePartFunction<Flt64>.Create(Polynomial(unbounded), IIntoValue<Flt64>.Identity).IsFailed);
        Assert.True(PositivePartFunction<Flt64>.Create(
            new LinearPolynomial<Flt64>(new[] { new LinearMonomial<Flt64>(new Flt64(double.NaN), x) }, Flt64.Zero),
            IIntoValue<Flt64>.Identity
        ).IsFailed);
        var decimalConverter = new TestNumberConverter<FltX>(value => value.ToFltX());
        Assert.True(PositivePartFunction<FltX>.Create(
            new LinearPolynomial<FltX>(new[] { new LinearMonomial<FltX>(new FltX(0.1m), x) }, FltX.Zero),
            decimalConverter
        ).IsFailed);
        Assert.True(PositivePartFunction<Flt64>.Create(
            Polynomial(x), new SwitchingConverter { CorruptValue = -4 }
        ).IsFailed);
        x.Range.Leq(new Flt64(7));
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(function.RegisterAuxiliaryTokens(parent.Tokens).IsFailed);
        Assert.All(function.HelperVariables, helper => Assert.Null(parent.Tokens.Find(helper)));
    }

    private static async Task VerifyGraph<V>(IIntoValue<V> converter)
        where V : struct, IRealNumber<V>, INumberField<V> {
        V Number(double value) => converter.IntoValue(new Flt64(value));
        RealVar x = Bounded();
        var polynomial = new LinearPolynomial<V>(new[] { new LinearMonomial<V>(converter.One, x) }, converter.Zero);
        Result<PositivePartFunction<V>, ErrorCode, Error<ErrorCode>> created = PositivePartFunction<V>.Create(polynomial, converter);
        Assert.True(created.IsOk, created.ToString());
        PositivePartFunction<V> function = created.Value;
        using var parent = new LinearMetaModel<V>(converter: converter);
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(new LinearFunctionSymbolAdapter<V>(function)).IsOk);
        Result<LinearMechanismModel<V>, ErrorCode, Error<ErrorCode>> built = await LinearMechanismModel<V>.InvokeAsync(parent);
        Assert.True(built.IsOk, built.ToString());
        using LinearMechanismModel<V> model = built.Value;
        IReadOnlyList<IConstraint<V, LinearCategory>> rows = ((IMechanismModel<V>)model).Constraints;
        Assert.Equal(5, rows.Count);
        foreach (double input in new[] { -4.0, -1.0, 0.0, 2.0, 6.0 }) {
            double expected = System.Math.Max(input, 0);
            V? evaluated = function.TryEvaluate(new Dictionary<ISymbol, V> { [x] = Number(input) }).Value;
            Assert.True(evaluated.HasValue && evaluated.Value.Eq(Number(expected)));
            Assert.True(Feasible(expected));
            Assert.False(Feasible(expected + 1));

            bool Feasible(double result) => Enumerable.Range(0, 2).Any(selected => {
                var values = new Dictionary<IVariableItem, V> {
                    [x] = Number(input),
                    [function.ResultVar] = Number(result),
                    [function.SelectorVars[0]] = Number(selected == 0 ? 1 : 0),
                    [function.SelectorVars[1]] = Number(selected == 1 ? 1 : 0),
                };
                V[] solution = model.Tokens.TokensInSolver.Select(token => values[token.Variable]).ToArray();
                return rows.All(row => row.IsTrue(solution) == true);
            });
        }
    }

    private static RealVar Bounded() {
        var x = new RealVar("positive-x");
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-4), new Flt64(6)).Value);
        return x;
    }

    private static LinearPolynomial<Flt64> Polynomial(ISymbol symbol) => new(
        new[] { new LinearMonomial<Flt64>(Flt64.One, symbol) }, Flt64.Zero);

    private static PositivePartFunction<Flt64> Create(LinearPolynomial<Flt64> input, IIntoValue<Flt64> converter) {
        Result<PositivePartFunction<Flt64>, ErrorCode, Error<ErrorCode>> result = PositivePartFunction<Flt64>.Create(input, converter);
        Assert.True(result.IsOk, result.ToString());
        return result.Value;
    }

    private static async Task<IMechanismModel<Flt64>> Build(AbstractMetaModel<Flt64> parent) {
        Result<IMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> result = parent is QuadraticMetaModel<Flt64> quadratic
            ? (await QuadraticMechanismModel<Flt64>.InvokeAsync(quadratic)).Map<IMechanismModel<Flt64>>(model => model)
            : (await LinearMechanismModel<Flt64>.InvokeAsync((LinearMetaModel<Flt64>)parent)).Map<IMechanismModel<Flt64>>(model => model);
        Assert.True(result.IsOk, result.ToString());
        return result.Value;
    }

    private static IDisposable Dump(IMechanismModel<Flt64> model) => model switch {
        LinearMechanismModel<Flt64> linear => linear.ToLinearTriadModel().Value,
        QuadraticMechanismModel<Flt64> quadratic => quadratic.ToQuadraticTetradModel().Value,
        _ => throw new InvalidOperationException(),
    };

    private static bool DumpFails(IMechanismModel<Flt64> model) => model switch {
        LinearMechanismModel<Flt64> linear => linear.ToLinearTriadModel().IsFailed,
        QuadraticMechanismModel<Flt64> quadratic => quadratic.ToQuadraticTetradModel().IsFailed,
        _ => false,
    };

    private static bool ValidateFails(IMechanismModel<Flt64> model, IDisposable matrix) => (model, matrix) switch {
        (LinearMechanismModel<Flt64> linear, LinearTriadModel triad) => linear.ValidateSolverTokenSnapshot(triad).IsFailed,
        (QuadraticMechanismModel<Flt64> quadratic, QuadraticTetradModel tetrad) => quadratic.ValidateSolverTokenSnapshot(tetrad).IsFailed,
        _ => false,
    };

    private static bool ApplyFails(IMechanismModel<Flt64> model, IDisposable matrix) => (model, matrix) switch {
        (LinearMechanismModel<Flt64> linear, LinearTriadModel triad) => linear.ApplySolverSolution(triad, new Flt64[linear.Tokens.TokensInSolver.Count]).IsFailed,
        (QuadraticMechanismModel<Flt64> quadratic, QuadraticTetradModel tetrad) => quadratic.ApplySolverSolution(tetrad, new Flt64[quadratic.Tokens.TokensInSolver.Count]).IsFailed,
        _ => false,
    };

    private sealed class SwitchingConverter : IIntoValue<Flt64> {
        public double? CorruptValue { get; set; }
        public double? CorruptInput { get; set; }
        public Flt64 Zero => Flt64.Zero;
        public Flt64 One => Flt64.One;
        public Flt64 IntoValue(Flt64 value) => value.ToDouble() == CorruptInput ? new Flt64(value.ToDouble() + 0.125) : value;
        public Flt64 FromValue(Flt64 value) => value.ToDouble() == CorruptValue ? new Flt64(value.ToDouble() + 0.125) : value;
    }
}
