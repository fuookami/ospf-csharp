#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Xunit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Algebra.ValueRange;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Variable;
using NInt64 = Fuookami.Ospf.Math.Algebra.Number.Int64;
using SymbolFlatten = Fuookami.Ospf.Core.Symbol.Flatten;
namespace Fuookami.Ospf.Core.Tests.Model.Intermediate;

public class GenericMechanismModelDumpTest {
    [Fact]
    public async Task RepresentativeNumericTypes_ShouldDumpBothModelKinds() {
        await VerifyNumericType(new NumericConverter<Flt32>(value => value.ToFlt32()));
        await VerifyNumericType(new NumericConverter<NInt64>(value => value.ToInt64()));
        await VerifyNumericType(new NumericConverter<RtnX>(value => new RtnX(value.ToDouble(), 1)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DecimalModel_ShouldDumpRestoreAndApplyFixedColumns(bool quadratic) {
        using AbstractMetaModel<FltX> parent = Parent(quadratic);
        (RealVar x, RealVar y) = AddModel(parent);
        var fixedValues = new Dictionary<IVariableItem, FltX> { [y] = new FltX(2m) };
        if (parent is LinearMetaModel<FltX> linear) {
            using LinearMechanismModel<FltX> model = (await LinearMechanismModel<FltX>.InvokeAsync(linear)).Value;
            using LinearTriadModel matrix = model.ToLinearTriadModel(fixedValues).Value;
            Assert.Single(matrix.Variables);
            Assert.Equal(x.Name, matrix.Variables[0].Name);
            Assert.Equal(0.5, matrix.Constraints.Lhs[0].Single().Coefficient.ToDouble(), 12);
            Assert.Equal(1.6, matrix.Constraints.Rhs[0].ToDouble(), 12);
            Assert.Equal(0.1, matrix.Objective.Cells.Single().Coefficient.ToDouble(), 12);
            Assert.Equal(0.7, matrix.Objective.Constant.ToDouble(), 12);
            Assert.True(model.ValidateSolverTokenSnapshot(matrix).IsOk);
            Assert.Equal(new[] { new Flt64(1), new Flt64(2) }, model.RestoreSolverSolution(matrix, new[] { Flt64.One }).Value);
            Assert.True(model.ApplySolverSolution(matrix, new[] { Flt64.One }).IsOk);
        }
        else {
            using QuadraticMechanismModel<FltX> model = (await QuadraticMechanismModel<FltX>.InvokeAsync((QuadraticMetaModel<FltX>)parent)).Value;
            using QuadraticTetradModel matrix = model.ToQuadraticTetradModel(fixedValues).Value;
            Assert.Single(matrix.Variables);
            Assert.Equal(x.Name, matrix.Variables[0].Name);
            Assert.Equal(1.0, matrix.Constraints.Lhs[0].Single().Coefficient.ToDouble(), 12);
            Assert.Equal(1.6, matrix.Constraints.Rhs[0].ToDouble(), 12);
            Assert.Equal(0.6, matrix.Objective.Cells.Single().Coefficient.ToDouble(), 12);
            Assert.Equal(0.7, matrix.Objective.Constant.ToDouble(), 12);
            Assert.True(model.ValidateSolverTokenSnapshot(matrix).IsOk);
            Assert.Equal(new[] { new Flt64(1), new Flt64(2) }, model.RestoreSolverSolution(matrix, new[] { Flt64.One }).Value);
            Assert.True(model.ApplySolverSolution(matrix, new[] { Flt64.One }).IsOk);
        }
        Assert.Equal(new FltX(1m), parent.Tokens.Find(x)!.Result);
        Assert.Equal(new FltX(2m), parent.Tokens.Find(y)!.Result);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DecimalModel_ShouldRejectStaleMatrixAfterVariableChanges(bool quadratic) {
        using AbstractMetaModel<FltX> parent = Parent(quadratic);
        (RealVar x, _) = AddModel(parent);
        if (parent is LinearMetaModel<FltX> linear) {
            using LinearMechanismModel<FltX> model = (await LinearMechanismModel<FltX>.InvokeAsync(linear)).Value;
            using LinearTriadModel matrix = model.ToLinearTriadModel().Value;
            x.Range.Leq(new Flt64(3));
            Assert.True(model.ValidateSolverTokenSnapshot(matrix).IsFailed);
            Assert.True(model.ApplySolverSolution(matrix, new[] { Flt64.One, Flt64.One }).IsFailed);
        }
        else {
            using QuadraticMechanismModel<FltX> model = (await QuadraticMechanismModel<FltX>.InvokeAsync((QuadraticMetaModel<FltX>)parent)).Value;
            using QuadraticTetradModel matrix = model.ToQuadraticTetradModel().Value;
            x.Range.Leq(new Flt64(3));
            Assert.True(model.ValidateSolverTokenSnapshot(matrix).IsFailed);
            Assert.True(model.ApplySolverSolution(matrix, new[] { Flt64.One, Flt64.One }).IsFailed);
        }
        Assert.Null(parent.Tokens.Find(x)!.Result);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DecimalFixedValue_ShouldRejectLossySolverRoundTrip(bool quadratic) {
        using AbstractMetaModel<FltX> parent = Parent(quadratic);
        var x = new RealVar("large-fixed");
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-1e16), new Flt64(1e16)).Value);
        Assert.True(parent.Add(x).IsOk);
        var fixedValues = new Dictionary<IVariableItem, FltX> { [x] = new FltX(9007199254740993m) };
        if (parent is LinearMetaModel<FltX> linear) {
            using LinearMechanismModel<FltX> model = (await LinearMechanismModel<FltX>.InvokeAsync(linear)).Value;
            Assert.True(model.ToLinearTriadModel(fixedValues).IsFailed);
        }
        else {
            using QuadraticMechanismModel<FltX> model = (await QuadraticMechanismModel<FltX>.InvokeAsync((QuadraticMetaModel<FltX>)parent)).Value;
            Assert.True(model.ToQuadraticTetradModel(fixedValues).IsFailed);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DecimalFixedValue_ShouldAllowRoundTripThroughBinarySolverValue(bool quadratic) {
        using AbstractMetaModel<FltX> parent = Parent(quadratic);
        var x = new RealVar("decimal-fixed");
        Assert.True(parent.Add(x).IsOk);
        var fixedValues = new Dictionary<IVariableItem, FltX> { [x] = new FltX(0.1m) };
        if (parent is LinearMetaModel<FltX> linear) {
            using LinearMechanismModel<FltX> model = (await LinearMechanismModel<FltX>.InvokeAsync(linear)).Value;
            using LinearTriadModel matrix = model.ToLinearTriadModel(fixedValues).Value;
            Assert.True(model.ApplySolverSolution(matrix, Array.Empty<Flt64>()).IsOk);
        }
        else {
            using QuadraticMechanismModel<FltX> model = (await QuadraticMechanismModel<FltX>.InvokeAsync((QuadraticMetaModel<FltX>)parent)).Value;
            using QuadraticTetradModel matrix = model.ToQuadraticTetradModel(fixedValues).Value;
            Assert.True(model.ApplySolverSolution(matrix, Array.Empty<Flt64>()).IsOk);
        }
        Assert.Equal(new FltX(0.1m), parent.Tokens.Find(x)!.Result);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DecimalFixedValue_ShouldRejectConverterChangesBeforeWriteback(bool quadratic) {
        var converter = new ChangingDecimalConverter();
        using AbstractMetaModel<FltX> parent = quadratic
            ? new QuadraticMetaModel<FltX>(converter: converter)
            : new LinearMetaModel<FltX>(converter: converter);
        (RealVar x, RealVar y) = AddModel(parent);
        var fixedValues = new Dictionary<IVariableItem, FltX> { [y] = new FltX(2m) };
        if (parent is LinearMetaModel<FltX> linear) {
            using LinearMechanismModel<FltX> model = (await LinearMechanismModel<FltX>.InvokeAsync(linear)).Value;
            using LinearTriadModel matrix = model.ToLinearTriadModel(fixedValues).Value;
            converter.Changed = true;
            Assert.True(model.ValidateSolverTokenSnapshot(matrix).IsFailed);
            Assert.True(model.ApplySolverSolution(matrix, new[] { Flt64.One }).IsFailed);
        }
        else {
            using QuadraticMechanismModel<FltX> model = (await QuadraticMechanismModel<FltX>.InvokeAsync((QuadraticMetaModel<FltX>)parent)).Value;
            using QuadraticTetradModel matrix = model.ToQuadraticTetradModel(fixedValues).Value;
            converter.Changed = true;
            Assert.True(model.ValidateSolverTokenSnapshot(matrix).IsFailed);
            Assert.True(model.ApplySolverSolution(matrix, new[] { Flt64.One }).IsFailed);
        }
        Assert.Null(parent.Tokens.Find(x)!.Result);
        Assert.Null(parent.Tokens.Find(y)!.Result);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DecimalSolution_ShouldValidateConversionBeforeMutatingTokens(bool quadratic) {
        var converter = new ChangingDecimalConverter();
        using AbstractMetaModel<FltX> parent = quadratic
            ? new QuadraticMetaModel<FltX>(converter: converter)
            : new LinearMetaModel<FltX>(converter: converter);
        (RealVar x, RealVar y) = AddModel(parent);
        parent.Tokens.SetSolverSolution(new[] { Flt64.One, new Flt64(2) });
        if (parent is LinearMetaModel<FltX> linear) {
            using LinearMechanismModel<FltX> model = (await LinearMechanismModel<FltX>.InvokeAsync(linear)).Value;
            using LinearTriadModel matrix = model.ToLinearTriadModel().Value;
            converter.InvalidResult = 3.5;
            Assert.True(model.ApplySolverSolution(matrix, new[] { new Flt64(3), new Flt64(3.5) }).IsFailed);
        }
        else {
            using QuadraticMechanismModel<FltX> model = (await QuadraticMechanismModel<FltX>.InvokeAsync((QuadraticMetaModel<FltX>)parent)).Value;
            using QuadraticTetradModel matrix = model.ToQuadraticTetradModel().Value;
            converter.InvalidResult = 3.5;
            Assert.True(model.ApplySolverSolution(matrix, new[] { new Flt64(3), new Flt64(3.5) }).IsFailed);
        }
        Assert.Equal(new FltX(1m), parent.Tokens.Find(x)!.Result);
        Assert.Equal(new FltX(2m), parent.Tokens.Find(y)!.Result);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DecimalModel_ShouldRejectChangedConverterWithoutFixedColumns(bool quadratic) {
        var converter = new ChangingDecimalConverter();
        using AbstractMetaModel<FltX> parent = quadratic
            ? new QuadraticMetaModel<FltX>(converter: converter)
            : new LinearMetaModel<FltX>(converter: converter);
        (RealVar x, _) = AddModel(parent);
        if (parent is LinearMetaModel<FltX> linear) {
            using LinearMechanismModel<FltX> model = (await LinearMechanismModel<FltX>.InvokeAsync(linear)).Value;
            using LinearTriadModel matrix = model.ToLinearTriadModel().Value;
            converter.Changed = true;
            Assert.True(model.ValidateSolverTokenSnapshot(matrix).IsFailed);
            Assert.True(model.ApplySolverSolution(matrix, new[] { Flt64.One, Flt64.One }).IsFailed);
        }
        else {
            using QuadraticMechanismModel<FltX> model = (await QuadraticMechanismModel<FltX>.InvokeAsync((QuadraticMetaModel<FltX>)parent)).Value;
            using QuadraticTetradModel matrix = model.ToQuadraticTetradModel().Value;
            converter.Changed = true;
            Assert.True(model.ValidateSolverTokenSnapshot(matrix).IsFailed);
            Assert.True(model.ApplySolverSolution(matrix, new[] { Flt64.One, Flt64.One }).IsFailed);
        }
        Assert.Null(parent.Tokens.Find(x)!.Result);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IntegerModel_ShouldRejectFractionalSolverValues(bool quadratic) {
        var converter = new IntegerConverter();
        using AbstractMetaModel<NInt64> parent = quadratic
            ? new QuadraticMetaModel<NInt64>(converter: converter)
            : new LinearMetaModel<NInt64>(converter: converter);
        var x = new RealVar("integer-solver-value");
        Assert.True(parent.Add(x).IsOk);
        if (parent is LinearMetaModel<NInt64> linear) {
            using LinearMechanismModel<NInt64> model = (await LinearMechanismModel<NInt64>.InvokeAsync(linear)).Value;
            using LinearTriadModel matrix = model.ToLinearTriadModel().Value;
            Assert.True(model.ApplySolverSolution(matrix, new[] { new Flt64(1.5) }).IsFailed);
            Assert.True(model.ApplySolverSolution(matrix, new[] { new Flt64(2) }).IsOk);
        }
        else {
            using QuadraticMechanismModel<NInt64> model = (await QuadraticMechanismModel<NInt64>.InvokeAsync((QuadraticMetaModel<NInt64>)parent)).Value;
            using QuadraticTetradModel matrix = model.ToQuadraticTetradModel().Value;
            Assert.True(model.ApplySolverSolution(matrix, new[] { new Flt64(1.5) }).IsFailed);
            Assert.True(model.ApplySolverSolution(matrix, new[] { new Flt64(2) }).IsOk);
        }
        Assert.Equal(new NInt64(2), parent.Tokens.Find(x)!.Result);
    }

    [Theory]
    [InlineData(false, 9007199254740992d)]
    [InlineData(false, -9007199254740992d)]
    [InlineData(true, 9007199254740992d)]
    [InlineData(true, -9007199254740992d)]
    public async Task IntegerModel_ShouldRejectLossyConversionBeforeWritingTokens(bool quadratic, double value) {
        var converter = new LargeIntegerConverter();
        using AbstractMetaModel<IntX> parent = quadratic
            ? new QuadraticMetaModel<IntX>(converter: converter)
            : new LinearMetaModel<IntX>(converter: converter);
        var x = new RealVar("large-integer-solver-value");
        Assert.True(parent.Add(x).IsOk);
        if (parent is LinearMetaModel<IntX> linear) {
            using LinearMechanismModel<IntX> model = (await LinearMechanismModel<IntX>.InvokeAsync(linear)).Value;
            using LinearTriadModel matrix = model.ToLinearTriadModel().Value;
            Assert.True(model.ApplySolverSolution(matrix, new[] { new Flt64(value) }).IsOk);
            Assert.Equal(new IntX(new BigInteger(value)), parent.Tokens.Find(x)!.Result);
            Assert.True(model.ApplySolverSolution(matrix, new[] { new Flt64(2) }).IsOk);
            converter.Biased = true;
            Assert.True(model.ApplySolverSolution(matrix, new[] { new Flt64(value) }).IsFailed);
        }
        else {
            using QuadraticMechanismModel<IntX> model = (await QuadraticMechanismModel<IntX>.InvokeAsync((QuadraticMetaModel<IntX>)parent)).Value;
            using QuadraticTetradModel matrix = model.ToQuadraticTetradModel().Value;
            Assert.True(model.ApplySolverSolution(matrix, new[] { new Flt64(value) }).IsOk);
            Assert.Equal(new IntX(new BigInteger(value)), parent.Tokens.Find(x)!.Result);
            Assert.True(model.ApplySolverSolution(matrix, new[] { new Flt64(2) }).IsOk);
            converter.Biased = true;
            Assert.True(model.ApplySolverSolution(matrix, new[] { new Flt64(value) }).IsFailed);
        }
        Assert.Equal(new IntX(2), parent.Tokens.Find(x)!.Result);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DecimalAtomicBuild_ShouldReturnUsableModelAndMatrix(bool quadratic) {
        using AbstractMetaModel<FltX> parent = Parent(quadratic);
        (RealVar x, RealVar y) = AddModel(parent);
        var fixedValues = new Dictionary<IVariableItem, FltX> { [y] = new FltX(2m) };
        if (parent is LinearMetaModel<FltX> linear) {
            Result<(LinearMechanismModel<FltX> Model, LinearTriadModel Matrix), ErrorCode, Error<ErrorCode>> built =
                await linear.BuildLinearTriadModelAsync(fixedValues);
            Assert.True(built.IsOk, built.ToString());
            using LinearMechanismModel<FltX> model = built.Value.Model;
            using LinearTriadModel matrix = built.Value.Matrix;
            Assert.True(model.ApplySolverSolution(matrix, new[] { Flt64.One }).IsOk);
        }
        else {
            Result<(QuadraticMechanismModel<FltX> Model, QuadraticTetradModel Matrix), ErrorCode, Error<ErrorCode>> built =
                await ((QuadraticMetaModel<FltX>)parent).BuildQuadraticTetradModelAsync(fixedValues);
            Assert.True(built.IsOk, built.ToString());
            using QuadraticMechanismModel<FltX> model = built.Value.Model;
            using QuadraticTetradModel matrix = built.Value.Matrix;
            Assert.True(model.ApplySolverSolution(matrix, new[] { Flt64.One }).IsOk);
        }
        Assert.Equal(new FltX(1m), parent.Tokens.Find(x)!.Result);
        Assert.Equal(new FltX(2m), parent.Tokens.Find(y)!.Result);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DecimalAtomicBuild_ShouldRollBackHelpersWhenFixedConversionFails(bool quadratic) {
        var converter = new ChangingDecimalConverter();
        using AbstractMetaModel<FltX> parent = quadratic
            ? new QuadraticMetaModel<FltX>(converter: converter)
            : new LinearMetaModel<FltX>(converter: converter);
        var x = new RealVar("atomic-input");
        var y = new RealVar("atomic-fixed");
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-4), new Flt64(4)).Value);
        y.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-1e16), new Flt64(1e16)).Value);
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(y).IsOk);
        var input = new LinearPolynomial<FltX>(new[] { new LinearMonomial<FltX>(FltX.One, x) }, FltX.Zero);
        PositivePartFunction<FltX> function = PositivePartFunction<FltX>.Create(input, converter).Value;
        Assert.True(parent.Tokens.Add(new LinearFunctionSymbolAdapter<FltX>(function)).IsOk);
        Assert.All(function.HelperVariables, helper => Assert.Null(parent.Tokens.Find(helper)));
        var fixedValues = new Dictionary<IVariableItem, FltX> { [y] = new FltX(9007199254740993m) };
        if (parent is LinearMetaModel<FltX> linear) {
            Assert.True((await linear.BuildLinearTriadModelAsync(fixedValues)).IsFailed);
        }
        else {
            Assert.True((await ((QuadraticMetaModel<FltX>)parent).BuildQuadraticTetradModelAsync(fixedValues)).IsFailed);
        }
        Assert.All(function.HelperVariables, helper => Assert.Null(parent.Tokens.Find(helper)));
    }

    private static AbstractMetaModel<FltX> Parent(bool quadratic) => quadratic
        ? new QuadraticMetaModel<FltX>(converter: FltX.Zero)
        : new LinearMetaModel<FltX>(converter: FltX.Zero);

    private static (RealVar X, RealVar Y) AddModel(AbstractMetaModel<FltX> parent) {
        var x = new RealVar("generic-x");
        var y = new RealVar("generic-y");
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-5), new Flt64(5)).Value);
        y.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-5), new Flt64(5)).Value);
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(y).IsOk);
        if (parent is LinearMetaModel<FltX> linear) {
            var row = new LinearInequality<FltX>(
                Lhs: new LinearPolynomial<FltX>(new[] {
                    new LinearMonomial<FltX>(new FltX(0.5m), x),
                    new LinearMonomial<FltX>(new FltX(0.2m), y),
                }, FltX.Zero),
                Rhs: new LinearPolynomial<FltX>(Array.Empty<LinearMonomial<FltX>>(), new FltX(2m)),
                Comparison: Comparison.LE
            );
            Assert.True(linear.AddConstraint(relation: row, group: null).IsOk);
            Assert.True(linear.AddObject(
                category: ObjectCategory.Minimum,
                flattenData: new SymbolFlatten.LinearFlattenData<FltX>(new[] {
                    new LinearMonomial<FltX>(new FltX(0.1m), x),
                    new LinearMonomial<FltX>(new FltX(0.2m), y),
                }, new FltX(0.3m)),
                name: "generic-objective",
                displayName: null
            ).IsOk);
        }
        else {
            var model = (QuadraticMetaModel<FltX>)parent;
            var row = new QuadraticInequalityOf<FltX>(
                Lhs: new QuadraticPolynomial<FltX>(new[] {
                    new QuadraticMonomial<FltX>(new FltX(0.5m), x),
                    new QuadraticMonomial<FltX>(new FltX(0.2m), y),
                    new QuadraticMonomial<FltX>(new FltX(0.25m), x, y),
                }, FltX.Zero),
                Rhs: new QuadraticPolynomial<FltX>(Array.Empty<QuadraticMonomial<FltX>>(), new FltX(2m)),
                Comparison: Comparison.LE
            );
            Assert.True(model.AddConstraint(relation: row, group: null).IsOk);
            Assert.True(model.AddObject(
                category: ObjectCategory.Minimum,
                flattenData: new SymbolFlatten.QuadraticFlattenData<FltX>(new[] {
                    new QuadraticMonomial<FltX>(new FltX(0.1m), x),
                    new QuadraticMonomial<FltX>(new FltX(0.2m), y),
                    new QuadraticMonomial<FltX>(new FltX(0.25m), x, y),
                }, new FltX(0.3m)),
                name: "generic-objective",
                displayName: null
            ).IsOk);
        }
        return (x, y);
    }

    private static async Task VerifyNumericType<V>(IIntoValue<V> converter)
        where V : struct, IRealNumber<V>, INumberField<V> {
        foreach (bool quadratic in new[] { false, true }) {
            using AbstractMetaModel<V> parent = quadratic
                ? new QuadraticMetaModel<V>(converter: converter)
                : new LinearMetaModel<V>(converter: converter);
            var x = new RealVar("numeric-x");
            x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-5), new Flt64(5)).Value);
            Assert.True(parent.Add(x).IsOk);
            V two = converter.IntoValue(new Flt64(2));
            V four = converter.IntoValue(new Flt64(4));
            var row = new LinearInequality<V>(
                Lhs: new LinearPolynomial<V>(new[] { new LinearMonomial<V>(two, x) }, converter.Zero),
                Rhs: new LinearPolynomial<V>(Array.Empty<LinearMonomial<V>>(), four),
                Comparison: Comparison.LE
            );
            var linearParent = (IAbstractLinearMetaModel<V>)parent;
            Assert.True(linearParent.AddConstraint(relation: row, group: null).IsOk);
            Assert.True(linearParent.AddObject(
                category: ObjectCategory.Minimum,
                flattenData: new SymbolFlatten.LinearFlattenData<V>(
                    new[] { new LinearMonomial<V>(two, x) }, converter.One),
                name: "numeric-objective",
                displayName: null
            ).IsOk);
            if (parent is LinearMetaModel<V> linear) {
                using LinearMechanismModel<V> model = (await LinearMechanismModel<V>.InvokeAsync(linear)).Value;
                using LinearTriadModel matrix = model.ToLinearTriadModel().Value;
                Assert.Equal(2, matrix.Constraints.Lhs[0].Single().Coefficient.ToDouble());
                Assert.Equal(4, matrix.Constraints.Rhs[0].ToDouble());
                Assert.True(model.ApplySolverSolution(matrix, new[] { new Flt64(2) }).IsOk);
            }
            else {
                using QuadraticMechanismModel<V> model = (await QuadraticMechanismModel<V>.InvokeAsync((QuadraticMetaModel<V>)parent)).Value;
                using QuadraticTetradModel matrix = model.ToQuadraticTetradModel().Value;
                Assert.Equal(2, matrix.Constraints.Lhs[0].Single().Coefficient.ToDouble());
                Assert.Equal(4, matrix.Constraints.Rhs[0].ToDouble());
                Assert.True(model.ApplySolverSolution(matrix, new[] { new Flt64(2) }).IsOk);
            }
            Assert.Equal(two, parent.Tokens.Find(x)!.Result);
        }
    }

    private sealed class ChangingDecimalConverter : IIntoValue<FltX> {
        public bool Changed { get; set; }
        public double? InvalidResult { get; set; }
        public FltX Zero => FltX.Zero;
        public FltX One => FltX.One;
        public FltX IntoValue(Flt64 value) => value.ToDouble() == InvalidResult
            ? throw new InvalidOperationException("invalid conversion")
            : new(value.ToDouble() + (Changed ? 1 : 0));
        public Flt64 FromValue(FltX value) => value.ToFlt64();
    }

    private sealed class IntegerConverter : IIntoValue<NInt64> {
        public NInt64 Zero => NInt64.Zero;
        public NInt64 One => NInt64.One;
        public NInt64 IntoValue(Flt64 value) => value.ToInt64();
        public Flt64 FromValue(NInt64 value) => value.ToFlt64();
    }

    private sealed class LargeIntegerConverter : IIntoValue<IntX> {
        public bool Biased { get; set; }
        public IntX Zero => IntX.Zero;
        public IntX One => IntX.One;
        public IntX IntoValue(Flt64 value) {
            double number = value.ToDouble();
            BigInteger result = new(number);
            return new IntX(Biased && System.Math.Abs(number) == 9007199254740992d
                ? result + System.Math.Sign(number)
                : result);
        }
        public Flt64 FromValue(IntX value) => value.ToFlt64();
    }

    private sealed class NumericConverter<V>(Func<Flt64, V> convert) : IIntoValue<V>
        where V : struct, IRealNumber<V> {
        public V Zero => convert(Flt64.Zero);
        public V One => convert(Flt64.One);
        public V IntoValue(Flt64 value) => convert(value);
        public Flt64 FromValue(V value) => value.ToFlt64();
    }
}
