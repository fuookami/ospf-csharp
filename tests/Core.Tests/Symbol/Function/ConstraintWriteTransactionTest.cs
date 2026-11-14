#nullable enable

using System;
using System.Collections.Generic;
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
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
using Integer = Fuookami.Ospf.Math.Algebra.Number.Int64;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
namespace Fuookami.Ospf.Core.Tests.Symbol.Function;

public class ConstraintWriteTransactionTest {
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MiddleFailure_ShouldPreserveExistingRows(bool quadratic) {
        using var linearParent = new LinearMetaModel<Flt64>();
        using var quadraticParent = new QuadraticMetaModel<Flt64>();
        using IAbstractLinearMechanismModel<Flt64> model = quadratic
            ? (await QuadraticMechanismModel<Flt64>.InvokeAsync(quadraticParent)).Value
            : (await LinearMechanismModel<Flt64>.InvokeAsync(linearParent)).Value;
        Assert.True(model.AddConstraint(Row("existing"), from: (Fuookami.Ospf.Core.Symbol.IIntermediateSymbol?)null).IsOk);
        object original = quadratic
            ? ((IAbstractQuadraticMechanismModel<Flt64>)model).Constraints[0]
            : model.Constraints[0];

        Try result = IndicatorSupport.AddConstraints(model, new[] {
            Row("first"), Row("invalid", Comparison.NE), Row("unwritten"),
        });

        Assert.True(result.IsFailed);
        if (quadratic) {
            IReadOnlyList<IConstraint<Flt64, QuadraticCategory>> rows = ((IAbstractQuadraticMechanismModel<Flt64>)model).Constraints;
            Assert.Single(rows);
            Assert.Same(original, rows[0]);
        }
        else {
            Assert.Single(model.Constraints);
            Assert.Same(original, model.Constraints[0]);
        }
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    public async Task WriterFailureAfterAppending_ShouldRollBack(bool quadratic, int failureKind) {
        using var linearParent = new LinearMetaModel<Flt64>();
        using var quadraticParent = new QuadraticMetaModel<Flt64>();
        using IAbstractLinearMechanismModel<Flt64> model = quadratic
            ? (await QuadraticMechanismModel<Flt64>.InvokeAsync(quadraticParent)).Value
            : (await LinearMechanismModel<Flt64>.InvokeAsync(linearParent)).Value;
        Assert.True(model.AddConstraint(Row("existing"), from: (IIntermediateSymbol?)null).IsOk);
        object original = FirstRow(model, quadratic);
        using var writer = new FaultingWriter(model) { FailureKind = failureKind, FailAt = 2 };

        Try result = IndicatorSupport.AddConstraints(writer, new[] { Row("first"), Row("second"), Row("last") });

        Assert.True(result.IsFailed);
        Assert.Equal(2, writer.Writes);
        Assert.Equal(1, RowCount(model, quadratic));
        Assert.Same(original, FirstRow(model, quadratic));
        if (failureKind == 1) {
            Assert.IsType<Fatal<Success, ErrorCode, Error<ErrorCode>>>(result);
        }
        else {
            Assert.IsType<Failed<Success, ErrorCode, Error<ErrorCode>>>(result);
        }
    }

    [Theory]
    [InlineData(0, 1, 2)]
    [InlineData(0, 2, 2)]
    [InlineData(1, 3, 4)]
    public async Task RollbackFailure_ShouldReportBothErrors(int failureKind, int rollbackFailure, int errorCount) {
        using var parent = new LinearMetaModel<Flt64>();
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        using var writer = new FaultingWriter(model) {
            FailAt = 2,
            FailureKind = failureKind,
            RollbackFailure = rollbackFailure,
        };

        Try result = IndicatorSupport.AddConstraints(writer, new[] { Row("first"), Row("second") });

        Fatal<Success, ErrorCode, Error<ErrorCode>> fatal = Assert.IsType<Fatal<Success, ErrorCode, Error<ErrorCode>>>(result);
        Assert.Equal(errorCount, fatal.Errors.Count);
        Assert.Contains("write failed", fatal.Errors[0].Message);
        Assert.Contains("rollback failed", fatal.Errors[errorCount - 1].Message);
        Assert.Equal(2, RowCount(model, quadratic: false));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CommitFailure_ShouldRollBack(bool throws) {
        using var parent = new LinearMetaModel<Flt64>();
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        Assert.True(model.AddConstraint(Row("existing"), from: (IIntermediateSymbol?)null).IsOk);
        object original = FirstRow(model, quadratic: false);
        using var writer = new FaultingWriter(model) { CommitFailure = throws ? 2 : 1 };

        Try result = IndicatorSupport.AddConstraints(writer, new[] { Row("first"), Row("second") });

        Assert.True(result.IsFailed);
        Assert.Equal(1, RowCount(model, quadratic: false));
        Assert.Same(original, FirstRow(model, quadratic: false));
    }

    [Fact]
    public async Task IteratorFailure_ShouldRollBack() {
        using var parent = new LinearMetaModel<Flt64>();
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        Try result = IndicatorSupport.AddConstraints(model, BrokenRows());
        Assert.True(result.IsFailed);
        Assert.Empty(((IMechanismModel<Flt64>)model).Constraints);
    }

    [Fact]
    public async Task RoundingRows_ShouldUseTheSameTransaction() {
        using var parent = new LinearMetaModel<Flt64>();
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        using var writer = new FaultingWriter(model) { FailAt = 2 };
        Try result = RoundingSupport.AddConstraints(writer, new[] { Row("first"), Row("second") });
        Assert.True(result.IsFailed);
        Assert.Empty(((IMechanismModel<Flt64>)model).Constraints);
    }

    [Theory]
    [InlineData(false, false, 0)]
    [InlineData(false, false, 1)]
    [InlineData(false, true, 2)]
    [InlineData(true, false, 0)]
    [InlineData(true, false, 1)]
    [InlineData(true, true, 2)]
    public async Task CardinalityWriterFailure_ShouldRollBack(bool exactly, bool quadratic, int failureKind) {
        var x = new BinVar("cardinality-transaction-x");
        IMathFunctionSymbol<Flt64> function = exactly
            ? ExactlyFunction<Flt64>.Create(
                indicators: new[] { x, x },
                amount: 1,
                converter: IIntoValue<Flt64>.Identity
            ).Value
            : AtMostFunction<Flt64>.Create(
                indicators: new[] { x, x },
                limit: 1,
                converter: IIntoValue<Flt64>.Identity
            ).Value;
        using var linearParent = new LinearMetaModel<Flt64>();
        using var quadraticParent = new QuadraticMetaModel<Flt64>();
        AbstractMetaModel<Flt64> parent = quadratic ? quadraticParent : linearParent;
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(function.HelperVariables).IsOk);
        using IAbstractLinearMechanismModel<Flt64> model = quadratic
            ? (await QuadraticMechanismModel<Flt64>.InvokeAsync(quadraticParent)).Value
            : (await LinearMechanismModel<Flt64>.InvokeAsync(linearParent)).Value;
        Assert.True(model.AddConstraint(Row("existing"), from: (IIntermediateSymbol?)null).IsOk);
        object original = FirstRow(model, quadratic);
        using var writer = new FaultingWriter(model) { FailAt = 2, FailureKind = failureKind };

        Try result = function.RegisterConstraints(writer);

        Assert.True(result.IsFailed);
        Assert.Equal(failureKind == 1, result is Fatal<Success, ErrorCode, Error<ErrorCode>>);
        Assert.Equal(1, RowCount(model, quadratic));
        Assert.Same(original, FirstRow(model, quadratic));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CardinalityChangedConverterDuringWrite_ShouldRollBack(bool quadratic) {
        var x = new BinVar("cardinality-changing-x");
        var converter = new PiecewiseSwitchingConverter();
        ExactlyFunction<Flt64> function = ExactlyFunction<Flt64>.Create(
            indicators: new[] { x, x, x },
            amount: 1,
            converter: converter
        ).Value;
        using var linearParent = new LinearMetaModel<Flt64>();
        using var quadraticParent = new QuadraticMetaModel<Flt64>();
        AbstractMetaModel<Flt64> parent = quadratic ? quadraticParent : linearParent;
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(function.HelperVariables).IsOk);
        using IAbstractLinearMechanismModel<Flt64> model = quadratic
            ? (await QuadraticMechanismModel<Flt64>.InvokeAsync(quadraticParent)).Value
            : (await LinearMechanismModel<Flt64>.InvokeAsync(linearParent)).Value;
        Assert.True(model.AddConstraint(Row("existing"), from: (IIntermediateSymbol?)null).IsOk);
        object original = FirstRow(model, quadratic);
        using var writer = new FaultingWriter(model) { OnFirstWrite = () => converter.Corrupt = true };

        Try result = function.RegisterConstraints(writer);

        Assert.True(result.IsFailed);
        Assert.True(writer.Writes > 1);
        Assert.Equal(1, RowCount(model, quadratic));
        Assert.Same(original, FirstRow(model, quadratic));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task PositivePartWriteFailureOrChangedBounds_ShouldRollBack(bool quadratic, bool changeBounds) {
        var x = new RealVar("positive-transaction-x");
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-4), new Flt64(6)).Value);
        PositivePartFunction<Flt64> function = PositivePartFunction<Flt64>.Create(
            input: new LinearPolynomial<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, x) }, Flt64.Zero),
            converter: IIntoValue<Flt64>.Identity
        ).Value;
        using var linearParent = new LinearMetaModel<Flt64>();
        using var quadraticParent = new QuadraticMetaModel<Flt64>();
        AbstractMetaModel<Flt64> parent = quadratic ? quadraticParent : linearParent;
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(function.HelperVariables).IsOk);
        using IAbstractLinearMechanismModel<Flt64> model = quadratic
            ? (await QuadraticMechanismModel<Flt64>.InvokeAsync(quadraticParent)).Value
            : (await LinearMechanismModel<Flt64>.InvokeAsync(linearParent)).Value;
        Assert.True(model.AddConstraint(Row("existing"), from: (IIntermediateSymbol?)null).IsOk);
        object original = FirstRow(model, quadratic);
        using var writer = new FaultingWriter(model) {
            FailAt = changeBounds ? 0 : 2,
            OnFirstWrite = changeBounds
                ? () => x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-5), new Flt64(6)).Value)
                : null,
        };

        Try result = function.RegisterConstraints(writer);

        Assert.True(result.IsFailed);
        Assert.True(writer.Writes > 1);
        Assert.Equal(1, RowCount(model, quadratic));
        Assert.Same(original, FirstRow(model, quadratic));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PositivePartChangedConverterDuringWrite_ShouldRollBack(bool quadratic) {
        var x = new RealVar("positive-converter-transaction-x");
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-3), new Flt64(4)).Value);
        var converter = new PiecewiseSwitchingConverter();
        PositivePartFunction<Flt64> function = PositivePartFunction<Flt64>.Create(
            input: new LinearPolynomial<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, x) }, Flt64.Zero),
            converter: converter
        ).Value;
        using var linearParent = new LinearMetaModel<Flt64>();
        using var quadraticParent = new QuadraticMetaModel<Flt64>();
        AbstractMetaModel<Flt64> parent = quadratic ? quadraticParent : linearParent;
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(function.HelperVariables).IsOk);
        using IAbstractLinearMechanismModel<Flt64> model = quadratic
            ? (await QuadraticMechanismModel<Flt64>.InvokeAsync(quadraticParent)).Value
            : (await LinearMechanismModel<Flt64>.InvokeAsync(linearParent)).Value;
        Assert.True(model.AddConstraint(Row("existing"), from: (IIntermediateSymbol?)null).IsOk);
        object original = FirstRow(model, quadratic);
        using var writer = new FaultingWriter(model) { OnFirstWrite = () => converter.Corrupt = true };

        Try result = function.RegisterConstraints(writer);

        Assert.True(result.IsFailed);
        Assert.True(writer.Writes > 1);
        Assert.Equal(1, RowCount(model, quadratic));
        Assert.Same(original, FirstRow(model, quadratic));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(4, false)]
    [InlineData(5, false)]
    [InlineData(6, false)]
    [InlineData(7, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    [InlineData(4, true)]
    [InlineData(5, true)]
    [InlineData(6, true)]
    [InlineData(7, true)]
    public async Task ChangedBoundsAfterWrite_ShouldRollBackFunctionRows(int kind, bool quadratic) {
        var x = new RealVar("transaction-x");
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-2), new Flt64(2)).Value);
        var polynomial = new LinearPolynomial<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, x) }, Flt64.Zero);
        IMathFunctionSymbol<Flt64> function = kind switch {
            0 => IfFunction<Flt64>.Create(
                condition: polynomial,
                converter: IIntoValue<Flt64>.Identity,
                strictBoundary: Flt64.One
            ).Value,
            1 => IfInFunction<Flt64>.Create(
                x: polynomial,
                lower: new Flt64(-1),
                upper: Flt64.One,
                converter: IIntoValue<Flt64>.Identity,
                strictBoundary: Flt64.One
            ).Value,
            2 => IfThenFunction<Flt64>.Create(
                condition: polynomial,
                then: polynomial,
                converter: IIntoValue<Flt64>.Identity,
                strictBoundary: Flt64.One
            ).Value,
            3 => ImplyFunction<Flt64>.Create(
                antecedent: polynomial,
                consequent: polynomial,
                converter: IIntoValue<Flt64>.Identity,
                strictBoundary: Flt64.One
            ).Value,
            4 => new AbsFunction<Flt64>(polynomial, IIntoValue<Flt64>.Identity),
            5 => new MaxFunction<Flt64>(new[] { polynomial, polynomial }, IIntoValue<Flt64>.Identity),
            6 => new MinFunction<Flt64>(new[] { polynomial, polynomial }, IIntoValue<Flt64>.Identity),
            _ => SigmoidFunction<Flt64>.Create(
                condition: polynomial,
                converter: IIntoValue<Flt64>.Identity,
                strictBoundary: Flt64.One
            ).Value,
        };
        Assert.NotNull(function);
        using var linearParent = new LinearMetaModel<Flt64>();
        using var quadraticParent = new QuadraticMetaModel<Flt64>();
        if (quadratic) {
            Assert.True(quadraticParent.Add(x).IsOk);
            Assert.True(quadraticParent.Add(function.HelperVariables).IsOk);
        }
        else {
            Assert.True(linearParent.Add(x).IsOk);
            Assert.True(linearParent.Add(function.HelperVariables).IsOk);
        }
        using IAbstractLinearMechanismModel<Flt64> model = quadratic
            ? (await QuadraticMechanismModel<Flt64>.InvokeAsync(quadraticParent)).Value
            : (await LinearMechanismModel<Flt64>.InvokeAsync(linearParent)).Value;
        Assert.True(model.AddConstraint(Row("existing"), from: (IIntermediateSymbol?)null).IsOk);
        object original = FirstRow(model, quadratic);
        using var writer = new FaultingWriter(model) {
            OnFirstWrite = () => x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-3), new Flt64(3)).Value),
        };

        Try result = function.RegisterConstraints(writer);

        Assert.True(result.IsFailed);
        Assert.True(writer.Writes > 1);
        Assert.Equal(1, RowCount(model, quadratic));
        Assert.Same(original, FirstRow(model, quadratic));
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    public async Task SigmoidWriterFailure_ShouldRollBack(bool quadratic, int failureKind) {
        var x = new RealVar("sigmoid-transaction-x");
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-2), new Flt64(2)).Value);
        var polynomial = new LinearPolynomial<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, x) }, Flt64.Zero);
        SigmoidFunction<Flt64> function = SigmoidFunction<Flt64>.Create(
            condition: polynomial,
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: Flt64.One
        ).Value;
        using var linearParent = new LinearMetaModel<Flt64>();
        using var quadraticParent = new QuadraticMetaModel<Flt64>();
        if (quadratic) {
            Assert.True(quadraticParent.Add(x).IsOk);
            Assert.True(quadraticParent.Add(function.HelperVariables).IsOk);
        }
        else {
            Assert.True(linearParent.Add(x).IsOk);
            Assert.True(linearParent.Add(function.HelperVariables).IsOk);
        }
        using IAbstractLinearMechanismModel<Flt64> model = quadratic
            ? (await QuadraticMechanismModel<Flt64>.InvokeAsync(quadraticParent)).Value
            : (await LinearMechanismModel<Flt64>.InvokeAsync(linearParent)).Value;
        Assert.True(model.AddConstraint(Row("existing"), from: (IIntermediateSymbol?)null).IsOk);
        object original = FirstRow(model, quadratic);
        using var writer = new FaultingWriter(model) { FailAt = 2, FailureKind = failureKind };

        Try result = function.RegisterConstraints(writer);

        Assert.True(result.IsFailed);
        Assert.Equal(failureKind == 1, result is Fatal<Success, ErrorCode, Error<ErrorCode>>);
        Assert.Equal(2, writer.Writes);
        Assert.Equal(1, RowCount(model, quadratic));
        Assert.Same(original, FirstRow(model, quadratic));
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    public async Task PiecewiseWriterFailure_ShouldRollBack(bool quadratic, int failureKind) {
        var x = new RealVar("pwl-transaction-x");
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-2), new Flt64(3)).Value);
        Result<UnivariateLinearPiecewiseFunction<Flt64>, ErrorCode, Error<ErrorCode>> created = UnivariateLinearPiecewiseFunction<Flt64>.Create(
            input: new LinearPolynomial<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, x) }, Flt64.Zero),
            breakpoints: new[] { Flt64.Zero, Flt64.One, new Flt64(2) },
            slopes: new[] { new Flt64(2), new Flt64(-2) },
            intercepts: new[] { Flt64.Zero, new Flt64(4) },
            converter: IIntoValue<Flt64>.Identity
        );
        Assert.True(created.IsOk, created.ToString());
        using var linearParent = new LinearMetaModel<Flt64>();
        using var quadraticParent = new QuadraticMetaModel<Flt64>();
        if (quadratic) {
            Assert.True(quadraticParent.Add(x).IsOk);
            Assert.True(quadraticParent.Add(created.Value.HelperVariables).IsOk);
        }
        else {
            Assert.True(linearParent.Add(x).IsOk);
            Assert.True(linearParent.Add(created.Value.HelperVariables).IsOk);
        }
        using IAbstractLinearMechanismModel<Flt64> model = quadratic
            ? (await QuadraticMechanismModel<Flt64>.InvokeAsync(quadraticParent)).Value
            : (await LinearMechanismModel<Flt64>.InvokeAsync(linearParent)).Value;
        Assert.True(model.AddConstraint(Row("existing"), from: (IIntermediateSymbol?)null).IsOk);
        object original = FirstRow(model, quadratic);
        using var writer = new FaultingWriter(model) { FailAt = 4, FailureKind = failureKind };

        Try result = created.Value.RegisterConstraints(writer);

        Assert.True(result.IsFailed);
        Assert.Equal(failureKind == 1, result is Fatal<Success, ErrorCode, Error<ErrorCode>>);
        Assert.Equal(4, writer.Writes);
        Assert.Equal(1, RowCount(model, quadratic));
        Assert.Same(original, FirstRow(model, quadratic));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PiecewiseChangedHelperDuringWrite_ShouldRollBack(bool quadratic) {
        var x = new RealVar("pwl-changing-x");
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-2), new Flt64(3)).Value);
        UnivariateLinearPiecewiseFunction<Flt64> function = UnivariateLinearPiecewiseFunction<Flt64>.Create(
            input: new LinearPolynomial<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, x) }, Flt64.Zero),
            breakpoints: new[] { Flt64.Zero, Flt64.One },
            slopes: new[] { Flt64.One },
            intercepts: new[] { Flt64.Zero },
            converter: IIntoValue<Flt64>.Identity
        ).Value;
        using var linearParent = new LinearMetaModel<Flt64>();
        using var quadraticParent = new QuadraticMetaModel<Flt64>();
        if (quadratic) {
            Assert.True(quadraticParent.Add(x).IsOk);
            Assert.True(quadraticParent.Add(function.HelperVariables).IsOk);
        }
        else {
            Assert.True(linearParent.Add(x).IsOk);
            Assert.True(linearParent.Add(function.HelperVariables).IsOk);
        }
        using IAbstractLinearMechanismModel<Flt64> model = quadratic
            ? (await QuadraticMechanismModel<Flt64>.InvokeAsync(quadraticParent)).Value
            : (await LinearMechanismModel<Flt64>.InvokeAsync(linearParent)).Value;
        Assert.True(model.AddConstraint(Row("existing"), from: (IIntermediateSymbol?)null).IsOk);
        object original = FirstRow(model, quadratic);
        using var writer = new FaultingWriter(model) {
            OnFirstWrite = () => function.ResultVar.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-1), Flt64.One).Value),
        };

        Try result = function.RegisterConstraints(writer);

        Assert.True(result.IsFailed);
        Assert.True(writer.Writes > 1);
        Assert.Equal(1, RowCount(model, quadratic));
        Assert.Same(original, FirstRow(model, quadratic));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PiecewiseChangedConverterDuringWrite_ShouldRollBack(bool quadratic) {
        var x = new RealVar("pwl-changing-converter-x");
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-2), new Flt64(3)).Value);
        var converter = new PiecewiseSwitchingConverter();
        UnivariateLinearPiecewiseFunction<Flt64> function = UnivariateLinearPiecewiseFunction<Flt64>.Create(
            input: new LinearPolynomial<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, x) }, Flt64.Zero),
            breakpoints: new[] { Flt64.Zero, new Flt64(2) },
            slopes: new[] { new Flt64(3) },
            intercepts: new[] { new Flt64(7) },
            converter: converter,
            bigM: new Flt64(13)
        ).Value;
        using var linearParent = new LinearMetaModel<Flt64>();
        using var quadraticParent = new QuadraticMetaModel<Flt64>();
        if (quadratic) {
            Assert.True(quadraticParent.Add(x).IsOk);
            Assert.True(quadraticParent.Add(function.HelperVariables).IsOk);
        }
        else {
            Assert.True(linearParent.Add(x).IsOk);
            Assert.True(linearParent.Add(function.HelperVariables).IsOk);
        }
        using IAbstractLinearMechanismModel<Flt64> model = quadratic
            ? (await QuadraticMechanismModel<Flt64>.InvokeAsync(quadraticParent)).Value
            : (await LinearMechanismModel<Flt64>.InvokeAsync(linearParent)).Value;
        Assert.True(model.AddConstraint(Row("existing"), from: (IIntermediateSymbol?)null).IsOk);
        object original = FirstRow(model, quadratic);
        using var writer = new FaultingWriter(model) { OnFirstWrite = () => converter.Corrupt = true };

        Try result = function.RegisterConstraints(writer);

        Assert.True(result.IsFailed);
        Assert.True(writer.Writes > 1);
        Assert.Equal(1, RowCount(model, quadratic));
        Assert.Same(original, FirstRow(model, quadratic));
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    public async Task BivariateWriterFailure_ShouldRollBack(bool quadratic, int failureKind) {
        var x = new RealVar("surface-transaction-x");
        BivariateLinearPiecewiseFunction<Flt64> function = Surface(x);
        using var linearParent = new LinearMetaModel<Flt64>();
        using var quadraticParent = new QuadraticMetaModel<Flt64>();
        if (quadratic) {
            Assert.True(quadraticParent.Add(x).IsOk);
            Assert.True(quadraticParent.Add(function.HelperVariables).IsOk);
        }
        else {
            Assert.True(linearParent.Add(x).IsOk);
            Assert.True(linearParent.Add(function.HelperVariables).IsOk);
        }
        using IAbstractLinearMechanismModel<Flt64> model = quadratic
            ? (await QuadraticMechanismModel<Flt64>.InvokeAsync(quadraticParent)).Value
            : (await LinearMechanismModel<Flt64>.InvokeAsync(linearParent)).Value;
        Assert.True(model.AddConstraint(Row("existing"), from: (IIntermediateSymbol?)null).IsOk);
        object original = FirstRow(model, quadratic);
        using var writer = new FaultingWriter(model) { FailAt = 3, FailureKind = failureKind };

        Try result = function.RegisterConstraints(writer);

        Assert.True(result.IsFailed);
        Assert.Equal(failureKind == 1, result is Fatal<Success, ErrorCode, Error<ErrorCode>>);
        Assert.Equal(3, writer.Writes);
        Assert.Equal(1, RowCount(model, quadratic));
        Assert.Same(original, FirstRow(model, quadratic));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BivariateChangedHelperDuringWrite_ShouldRollBack(bool quadratic) {
        var x = new RealVar("surface-changing-x");
        BivariateLinearPiecewiseFunction<Flt64> function = Surface(x);
        using var linearParent = new LinearMetaModel<Flt64>();
        using var quadraticParent = new QuadraticMetaModel<Flt64>();
        if (quadratic) {
            Assert.True(quadraticParent.Add(x).IsOk);
            Assert.True(quadraticParent.Add(function.HelperVariables).IsOk);
        }
        else {
            Assert.True(linearParent.Add(x).IsOk);
            Assert.True(linearParent.Add(function.HelperVariables).IsOk);
        }
        using IAbstractLinearMechanismModel<Flt64> model = quadratic
            ? (await QuadraticMechanismModel<Flt64>.InvokeAsync(quadraticParent)).Value
            : (await LinearMechanismModel<Flt64>.InvokeAsync(linearParent)).Value;
        Assert.True(model.AddConstraint(Row("existing"), from: (IIntermediateSymbol?)null).IsOk);
        object original = FirstRow(model, quadratic);
        using var writer = new FaultingWriter(model) {
            OnFirstWrite = () => function.LambdaVars[0][0].Range.SetRange(ValueRange<Flt64>.Of(Flt64.Zero, new Flt64(0.5)).Value),
        };

        Try result = function.RegisterConstraints(writer);

        Assert.True(result.IsFailed);
        Assert.True(writer.Writes > 1);
        Assert.Equal(1, RowCount(model, quadratic));
        Assert.Same(original, FirstRow(model, quadratic));
    }

    private static BivariateLinearPiecewiseFunction<Flt64> Surface(RealVar x) => BivariateLinearPiecewiseFunction<Flt64>.Create(
        x: new LinearPolynomial<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, x) }, Flt64.Zero),
        y: new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), Flt64.Zero),
        triangles: new[] { new Triangle<Dim3, Flt64>(
            P1: Point<Dim3, Flt64>.Point3(
                x: Flt64.Zero,
                y: Flt64.Zero,
                z: Flt64.Zero
            ),
            P2: Point<Dim3, Flt64>.Point3(
                x: Flt64.One,
                y: Flt64.Zero,
                z: Flt64.One
            ),
            P3: Point<Dim3, Flt64>.Point3(
                x: Flt64.Zero,
                y: Flt64.One,
                z: Flt64.One
            )
        ) },
        converter: IIntoValue<Flt64>.Identity
    ).Value;

    [Fact]
    public async Task FailedPostWriteValidation_ShouldRemoveNewBoundDependencies() {
        var x = new RealVar("transaction-bounds");
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-2), new Flt64(2)).Value);
        var polynomial = new LinearPolynomial<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, x) }, Flt64.Zero);
        var dependency = new BoundDependencySnapshot<Flt64>(new[] { polynomial });
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;

        Try result = IndicatorSupport.AddConstraints(
            model: model,
            constraints: new[] { Row("temporary") },
            afterWrite: () => {
                model.TrackBoundDependencies(dependency);
                x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-3), new Flt64(3)).Value);
                return Results.Failed<Success>(new Err<ErrorCode>(ErrorCode.IllegalArgument, "changed"));
            }
        );

        Assert.True(result.IsFailed);
        Assert.Empty(((IMechanismModel<Flt64>)model).Constraints);
        Assert.True(model.ValidateBoundDependencies().IsOk);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedPostWriteValidation_ShouldRemoveNewModelDependencies(bool quadratic) {
        using var linear = new LinearMetaModel<Flt64>();
        using var quadraticModel = new QuadraticMetaModel<Flt64>();
        using IAbstractLinearMechanismModel<Flt64> model = quadratic
            ? (await QuadraticMechanismModel<Flt64>.InvokeAsync(quadraticModel)).Value
            : (await LinearMechanismModel<Flt64>.InvokeAsync(linear)).Value;
        var mechanism = (BasicMechanismModel<Flt64>)model;
        bool current = true;

        Try result = IndicatorSupport.AddConstraints(
            model: model,
            constraints: new[] { Row("temporary-model-dependency") },
            afterWrite: () => {
                mechanism.TrackModelDependency(() => current);
                current = false;
                return Results.Failed<Success>(new Err<ErrorCode>(ErrorCode.IllegalArgument, "changed"));
            }
        );

        Assert.True(result.IsFailed);
        Assert.Empty(((IMechanismModel<Flt64>)model).Constraints);
        Assert.True(mechanism.ValidateBoundDependencies().IsOk);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChangedBoundsAfterWrite_ShouldRollBackIntegerProduct(bool quadratic) {
        var integer = new IntVar("transaction-integer");
        integer.Range.SetRange(ValueRange<Integer>.Of(new Integer(0), new Integer(2)).Value);
        var x = new RealVar("transaction-product-x");
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-2), new Flt64(2)).Value);
        var polynomial = new LinearPolynomial<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, x) }, Flt64.Zero);
        Result<IntegerProductFunction<Flt64>, ErrorCode, Error<ErrorCode>> created = IntegerProductFunction<Flt64>.Create(
            integer: integer,
            input: polynomial,
            converter: IIntoValue<Flt64>.Identity
        );
        Assert.True(created.IsOk, created.ToString());
        using var linearParent = new LinearMetaModel<Flt64>();
        using var quadraticParent = new QuadraticMetaModel<Flt64>();
        if (quadratic) {
            Assert.True(quadraticParent.Add(integer).IsOk);
            Assert.True(quadraticParent.Add(x).IsOk);
            Assert.True(quadraticParent.Add(created.Value.HelperVariables).IsOk);
        }
        else {
            Assert.True(linearParent.Add(integer).IsOk);
            Assert.True(linearParent.Add(x).IsOk);
            Assert.True(linearParent.Add(created.Value.HelperVariables).IsOk);
        }
        using IAbstractLinearMechanismModel<Flt64> model = quadratic
            ? (await QuadraticMechanismModel<Flt64>.InvokeAsync(quadraticParent)).Value
            : (await LinearMechanismModel<Flt64>.InvokeAsync(linearParent)).Value;
        Assert.True(model.AddConstraint(Row("existing"), from: (IIntermediateSymbol?)null).IsOk);
        object original = FirstRow(model, quadratic);
        using var writer = new FaultingWriter(model) {
            OnFirstWrite = () => x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-3), new Flt64(3)).Value),
        };

        Try result = created.Value.RegisterConstraints(writer);

        Assert.True(result.IsFailed);
        Assert.True(writer.Writes > 1);
        Assert.Equal(1, RowCount(model, quadratic));
        Assert.Same(original, FirstRow(model, quadratic));
    }

    private static int RowCount(IAbstractLinearMechanismModel<Flt64> model, bool quadratic) => quadratic
        ? ((IAbstractQuadraticMechanismModel<Flt64>)model).Constraints.Count
        : model.Constraints.Count;

    private static object FirstRow(IAbstractLinearMechanismModel<Flt64> model, bool quadratic) => quadratic
        ? ((IAbstractQuadraticMechanismModel<Flt64>)model).Constraints[0]
        : model.Constraints[0];

    private static IEnumerable<LinearInequality<Flt64>> BrokenRows() {
        yield return Row("first");
        throw new InvalidOperationException("iterator failed");
    }

    private sealed class PiecewiseSwitchingConverter : IIntoValue<Flt64> {
        public bool Corrupt { get; set; }
        public Flt64 Zero => Flt64.Zero;
        public Flt64 One => Flt64.One;
        public Flt64 IntoValue(Flt64 value) => value;
        public Flt64 FromValue(Flt64 value) => Corrupt && value.ToDouble() == -3 ? Flt64.Zero : value;
    }

    private sealed class FaultingWriter(IAbstractLinearMechanismModel<Flt64> model) : IAbstractLinearMechanismModel<Flt64> {
        public int Writes { get; private set; }
        public int FailAt { get; init; }
        public int FailureKind { get; init; }
        public int CommitFailure { get; init; }
        public int RollbackFailure { get; init; }
        public Action? OnFirstWrite { get; init; }
        public string Name => model.Name;
        public IReadOnlyList<IConstraint<Flt64, LinearCategory>> Constraints => model.Constraints;
        public IObject ObjectFunction => model.ObjectFunction;
        public IAbstractTokenTable<Flt64> Tokens => model.Tokens;
        public void Dispose() { }

        public Try AddConstraint(
            LinearInequality<Flt64> relation,
            string? name = null,
            (IIntermediateSymbol Symbol, bool Direction)? from = null) {
            Try added = model.AddConstraint(
                relation: relation,
                name: name,
                from: from
            );
            ++Writes;
            if (Writes == 1) {
                OnFirstWrite?.Invoke();
            }
            if (Writes != FailAt || !added.IsOk) {
                return added;
            }
            if (FailureKind == 2) {
                throw new InvalidOperationException("write failed");
            }
            var error = new Err<ErrorCode>(ErrorCode.ApplicationFailed, "write failed");
            return FailureKind == 1
                ? new Fatal<Success, ErrorCode, Error<ErrorCode>>(
                    error,
                    new Err<ErrorCode>(ErrorCode.ApplicationStopped, "write also failed")
                )
                : Results.Failed<Success>(error);
        }

        public Try AddConstraint(
            LinearInequality<Flt64> relation,
            string? name = null,
            IIntermediateSymbol? from = null) => AddConstraint(
                relation: relation,
                name: name,
                from: from is null ? null : (from, false)
            );

        public Result<IConstraintWriteTransaction, ErrorCode, Error<ErrorCode>> BeginConstraintWrite() =>
            model.BeginConstraintWrite().Map<IConstraintWriteTransaction>(transaction => new FaultingTransaction(
                inner: transaction,
                commitFailure: CommitFailure,
                rollbackFailure: RollbackFailure
            ));
    }

    private sealed class FaultingTransaction(
        IConstraintWriteTransaction inner,
        int commitFailure,
        int rollbackFailure) : IConstraintWriteTransaction {
        public Try Commit() => commitFailure switch {
            1 => Results.Failed<Success>(new Err<ErrorCode>(ErrorCode.ApplicationFailed, "commit failed")),
            2 => throw new InvalidOperationException("commit failed"),
            _ => inner.Commit(),
        };

        public Try Rollback() => rollbackFailure switch {
            1 => Results.Failed<Success>(new Err<ErrorCode>(ErrorCode.ApplicationFailed, "rollback failed")),
            2 => throw new InvalidOperationException("rollback failed"),
            3 => new Fatal<Success, ErrorCode, Error<ErrorCode>>(
                new Err<ErrorCode>(ErrorCode.ApplicationFailed, "rollback failed"),
                new Err<ErrorCode>(ErrorCode.ApplicationError, "rollback failed again")
            ),
            _ => inner.Rollback(),
        };
    }

    private static LinearInequality<Flt64> Row(string name, Comparison comparison = Comparison.LE) =>
        IndicatorSupport.Row(
            monomials: Array.Empty<LinearMonomial<Flt64>>(),
            constant: Flt64.Zero,
            comparison: comparison,
            rhs: Flt64.Zero,
            name: name
        );
}
