#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
using Integer = Fuookami.Ospf.Math.Algebra.Number.Int64;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
namespace Fuookami.Ospf.Core.Tests.Model.Mechanism;

public class LinearModelStateFunctionTest {
    [Fact]
    public async Task RegisteredAbs_ShouldSurviveFrozenRowsAndRequireItsDefiningFunction() {
        using var parent = new LinearMetaModel<Flt64>();
        var x = new RealVar("abs-input");
        parent.Add(x).IsOk.Should().BeTrue();
        var abs = new AbsFunction<Flt64>(
            polynomial: Polynomial(0, (1, x)),
            converter: IIntoValue<Flt64>.Identity,
            bigM: new Flt64(10)
        );
        var adapter = new LinearFunctionSymbolAdapter<Flt64>(abs);
        parent.Add(adapter).IsOk.Should().BeTrue();
        var wrapper = new LinearExpressionSymbol(
            polynomial: new MutableLinearPolynomial<Flt64>(
                [new LinearMonomial<Flt64>(new Flt64(2), adapter)], new Flt64(3)),
            name: "wrapper"
        );
        LinearInequality<Flt64> relation = Row(wrapper, 5);
        var id = new ConstraintId("abs-row");

        parent.State.RegisterRow(id, relation).IsOk.Should().BeTrue();
        parent.State.RegisterRow(id, relation).IsOk.Should().BeTrue();
        LinearModelSnapshot<Flt64> snapshot = parent.State.Refresh().Value;
        LinearModelRow<Flt64> row = snapshot.Rows.Single(item => item.Id == id.Value);
        row.Coefficients.Should().ContainSingle().Which.Key.Should().BeSameAs(abs.ResultVar);
        row.Coefficients[abs.ResultVar].Should().Be(new Flt64(2));
        row.Rhs.Should().Be(new Flt64(2));

        using LinearMechanismModel<Flt64> built = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        var values = new Dictionary<IVariableItem, Flt64> {
            [x] = new Flt64(-1),
            [abs.ResultVar] = Flt64.One,
            [abs.PositiveVar] = Flt64.Zero,
            [abs.NegativeVar] = Flt64.One,
            [abs.SignVar] = Flt64.Zero,
        };
        IReadOnlyList<Flt64> solution = built.Tokens.TokensInSolver.Select(token => values[token.Variable]).ToList();
        ((IMechanismModel<Flt64>)built).Constraints.Should().HaveCount(5)
            .And.OnlyContain(item => item.IsTrue(solution) == true);
        using LinearTriadModel matrix = built.ToLinearTriadModel().Value;
        matrix.Constraints.Rhs.Should().Contain(new Flt64(2));

        wrapper.AsMutable().SetConstant(new Flt64(42));
        parent.State.Refresh().Value.Should().Be(snapshot);
        parent.State.RegisterRow(id, relation).IsFailed.Should().BeTrue();
        parent.Remove(adapter);
        parent.Tokens.Find(abs.ResultVar).Should().NotBeNull();
        int callbacks = parent.Tokens.Find(x)!.RefreshCallbacks.Count;
        (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).IsFailed.Should().BeTrue();
        parent.State.Refresh().IsFailed.Should().BeTrue();
        parent.Tokens.Find(x)!.RefreshCallbacks.Should().HaveCount(callbacks);
        ((IMechanismModel<Flt64>)built).Constraints.Should().HaveCount(5)
            .And.OnlyContain(item => item.IsTrue(solution) == true);

        parent.Add(adapter).IsOk.Should().BeTrue();
        parent.State.Refresh().Value.Should().Be(snapshot);
        using LinearMechanismModel<Flt64> restored = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        ((IMechanismModel<Flt64>)restored).Constraints.Should().HaveCount(5);
    }

    [Fact]
    public async Task CancelledFunctionsAndEquivalentRows_ShouldRetainReferenceDependencies() {
        using var parent = new LinearMetaModel<Flt64>();
        var x = new RealVar("shared-result");
        parent.Add(x).IsOk.Should().BeTrue();
        LinearFunctionSymbolAdapter<Flt64> first = Adapter("first", [new LinearMonomial<Flt64>(Flt64.One, x)]);
        LinearFunctionSymbolAdapter<Flt64> second = Adapter("second", [new LinearMonomial<Flt64>(Flt64.One, x)]);
        parent.Add(first).IsOk.Should().BeTrue();
        parent.Add(second).IsOk.Should().BeTrue();
        var id = new ConstraintId("dependent");
        parent.State.RegisterRow(id, Row(first, 1)).IsOk.Should().BeTrue();
        parent.State.RegisterRow(id, Row(first, 1)).IsOk.Should().BeTrue();
        parent.State.RegisterRow(id, Row(second, 1)).IsFailed.Should().BeTrue();
        parent.State.RegisterRow(id, Row(x, 1)).IsFailed.Should().BeTrue();

        var cancelled = new LinearInequality<Flt64>(
            Lhs: Polynomial(0, (1, first)),
            Rhs: Polynomial(0, (1, first)),
            Comparison: Comparison.EQ
        );
        var cancelledId = new ConstraintId("cancelled-function");
        parent.State.RegisterRow(cancelledId, cancelled).IsOk.Should().BeTrue();
        parent.State.RegisterRow(
            cancelledId,
            new LinearInequality<Flt64>(
                Lhs: Polynomial(0),
                Rhs: Polynomial(0),
                Comparison: Comparison.EQ
            )
        ).IsFailed.Should().BeTrue();
        parent.Remove(first);
        parent.Tokens.Find(x).Should().NotBeNull();
        (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).IsFailed.Should().BeTrue();
        parent.State.Refresh().IsFailed.Should().BeTrue();
        parent.Add(first).IsOk.Should().BeTrue();
        using LinearMechanismModel<Flt64> restored = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        ((IMechanismModel<Flt64>)restored).Constraints.Should().HaveCount(2);
    }

    [Fact]
    public async Task NestedFunctionResultChanges_ShouldInvalidateDirectBuildsAndRetries() {
        using var parent = new LinearMetaModel<Flt64>();
        var x = new RealVar("nested-x");
        var y = new RealVar("nested-y");
        parent.Add(x).IsOk.Should().BeTrue();
        parent.Add(y).IsOk.Should().BeTrue();
        var innerTerms = new List<LinearMonomial<Flt64>> { new(Flt64.One, x) };
        LinearFunctionSymbolAdapter<Flt64> inner = Adapter("inner", innerTerms);
        LinearFunctionSymbolAdapter<Flt64> replacement = Adapter("replacement", [new LinearMonomial<Flt64>(Flt64.One, x)]);
        var outerTerms = new List<LinearMonomial<Flt64>> { new(Flt64.One, inner) };
        LinearFunctionSymbolAdapter<Flt64> outer = Adapter("outer", outerTerms);
        parent.Add(inner).IsOk.Should().BeTrue();
        parent.Add(replacement).IsOk.Should().BeTrue();
        parent.Add(outer).IsOk.Should().BeTrue();
        var id = new ConstraintId("nested-functions");
        LinearInequality<Flt64> relation = Row(outer, 1);
        parent.State.RegisterRow(id, relation).IsOk.Should().BeTrue();
        LinearModelSnapshot<Flt64> snapshot = parent.State.Refresh().Value;
        int callbacks = parent.Tokens.Find(x)!.RefreshCallbacks.Count;

        outerTerms[0] = new LinearMonomial<Flt64>(Flt64.One, replacement);
        (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).IsFailed.Should().BeTrue();
        parent.State.RegisterRow(id, relation).IsFailed.Should().BeTrue();
        parent.Tokens.Find(x)!.RefreshCallbacks.Should().HaveCount(callbacks);
        outerTerms[0] = new LinearMonomial<Flt64>(Flt64.One, inner);
        innerTerms[0] = new LinearMonomial<Flt64>(Flt64.One, y);
        (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).IsFailed.Should().BeTrue();
        parent.State.Refresh().IsFailed.Should().BeTrue();
        innerTerms[0] = new LinearMonomial<Flt64>(Flt64.One, x);
        parent.Remove(inner);
        (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).IsFailed.Should().BeTrue();
        parent.Add(inner).IsOk.Should().BeTrue();
        parent.State.Refresh().Value.Should().Be(snapshot);
    }

    [Fact]
    public async Task DeepFunctionResults_ShouldPreserveDependenciesWithoutRecursion() {
        using var parent = new LinearMetaModel<Flt64>();
        var x = new RealVar("deep-function-x");
        parent.Add(x).IsOk.Should().BeTrue();
        ISymbol current = x;
        var chain = new List<LinearFunctionSymbolAdapter<Flt64>>();
        for (int i = 0; i < 128; ++i) {
            LinearFunctionSymbolAdapter<Flt64> next = Adapter(
                $"deep-function-{i}",
                [new LinearMonomial<Flt64>(Flt64.One, current)]
            );
            parent.Add(next).IsOk.Should().BeTrue();
            chain.Add(next);
            current = next;
        }
        parent.State.RegisterRow(new ConstraintId("deep-function-row"), Row(current, 1)).IsOk.Should().BeTrue();
        LinearModelSnapshot<Flt64> snapshot = parent.State.Refresh().Value;
        snapshot.Rows.Single().Coefficients[x].Should().Be(Flt64.One);
        parent.Remove(chain[64]);
        (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).IsFailed.Should().BeTrue();
        parent.Add(chain[64]).IsOk.Should().BeTrue();
        parent.State.Refresh().Value.Should().Be(snapshot);
    }

    [Fact]
    public async Task FunctionResultMutationDuringRowRegistration_ShouldAbortTheBuild() {
        using var parent = new LinearMetaModel<Flt64>();
        var x = new RealVar("callback-x");
        var y = new RealVar("callback-y");
        parent.Add(x).IsOk.Should().BeTrue();
        parent.Add(y).IsOk.Should().BeTrue();
        var terms = new List<LinearMonomial<Flt64>> { new(Flt64.One, x) };
        var function = new StructuralFunction("callback", terms);
        var adapter = new LinearFunctionSymbolAdapter<Flt64>(function);
        parent.Add(adapter).IsOk.Should().BeTrue();
        parent.State.RegisterRow(new ConstraintId("callback-row"), Row(adapter, 1)).IsOk.Should().BeTrue();
        int callbacks = parent.Tokens.Find(x)!.RefreshCallbacks.Count;
        function.OnRegister = () => terms[0] = new LinearMonomial<Flt64>(Flt64.One, y);
        (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).IsFailed.Should().BeTrue();
        parent.Tokens.Find(x)!.RefreshCallbacks.Should().HaveCount(callbacks);
        function.OnRegister = null;
        terms[0] = new LinearMonomial<Flt64>(Flt64.One, x);
        using LinearMechanismModel<Flt64> restored = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        ((IMechanismModel<Flt64>)restored).Constraints.Should().ContainSingle();
    }

    [Fact]
    public void FunctionResultChangingDuringRowCapture_ShouldNotPublishAMismatchedRow() {
        using var parent = new LinearMetaModel<Flt64>();
        var x = new RealVar("capture-x");
        var y = new RealVar("capture-y");
        parent.Add(x).IsOk.Should().BeTrue();
        parent.Add(y).IsOk.Should().BeTrue();
        var terms = new SwitchingTerms(x, y);
        var adapter = new LinearFunctionSymbolAdapter<Flt64>(new StructuralFunction("unstable", terms));
        parent.Add(adapter).IsOk.Should().BeTrue();
        var id = new ConstraintId("unstable-row");
        parent.State.RegisterRow(id, Row(adapter, 1)).IsFailed.Should().BeTrue();
        parent.Constraints.Should().BeEmpty();
        parent.State.RegisterRow(id, Row(adapter, 1)).IsOk.Should().BeTrue();
        parent.State.Refresh().Value.Rows.Single().Coefficients.Should().ContainKey(y);
    }

    [Fact]
    public async Task FunctionRowFailures_ShouldRollbackIdentityAndPreserveFatalErrors() {
        var identities = new ModelElementIdentityRegistry();
        using var parent = new LinearMetaModel<Flt64>(identityRegistry: identities);
        var x = new RealVar("failure-x");
        parent.Add(x).IsOk.Should().BeTrue();
        parent.AddConstraint(Row(x, 1), null).IsOk.Should().BeTrue();
        identities.RegisterConstraint(parent.Constraints.Single(), new ConstraintId("occupied")).IsOk.Should().BeTrue();
        var function = new StructuralFunction("failing", [new LinearMonomial<Flt64>(Flt64.One, x)]);
        var adapter = new LinearFunctionSymbolAdapter<Flt64>(function);
        parent.Add(adapter).IsOk.Should().BeTrue();
        parent.State.RegisterRow(new ConstraintId("occupied"), Row(adapter, 1)).IsFailed.Should().BeTrue();
        parent.Constraints.Should().ContainSingle();
        parent.State.RegisterRow(new ConstraintId("available"), Row(adapter, 1)).IsOk.Should().BeTrue();
        function.RegistrationResult = new Fatal<Success, ErrorCode, Error<ErrorCode>>(
            new Err<ErrorCode>(ErrorCode.IllegalArgument, "first"),
            new Err<ErrorCode>(ErrorCode.ApplicationFailed, "second")
        );
        int callbacks = parent.Tokens.Find(x)!.RefreshCallbacks.Count;
        (await LinearMechanismModel<Flt64>.InvokeAsync(parent))
            .Should().BeOfType<Fatal<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>>()
            .Which.Errors.Should().HaveCount(2);
        parent.State.Refresh()
            .Should().BeOfType<Fatal<LinearModelSnapshot<Flt64>, ErrorCode, Error<ErrorCode>>>()
            .Which.Errors.Should().HaveCount(2);
        parent.Tokens.Find(x)!.RefreshCallbacks.Should().HaveCount(callbacks);
        function.RegistrationResult = null;
        using LinearMechanismModel<Flt64> restored = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        ((IMechanismModel<Flt64>)restored).Constraints.Should().HaveCount(2);
    }

    [Fact]
    public async Task NestedAbsInputs_ShouldKeepCapturedInputAndRejectMissingFunctions() {
        using var parent = new LinearMetaModel<Flt64>();
        var x = new RealVar("input-x");
        var y = new RealVar("input-y");
        parent.Add(x).IsOk.Should().BeTrue();
        parent.Add(y).IsOk.Should().BeTrue();
        var input = new List<LinearMonomial<Flt64>> { new(Flt64.One, x) };
        var innerFunction = new AbsFunction<Flt64>(
            polynomial: new LinearPolynomial<Flt64>(input, Flt64.Zero),
            converter: IIntoValue<Flt64>.Identity,
            bigM: new Flt64(10),
            name: "inner-abs"
        );
        var inner = new LinearFunctionSymbolAdapter<Flt64>(innerFunction);
        var outerFunction = new AbsFunction<Flt64>(
            polynomial: Polynomial(-1, (2, inner)),
            converter: IIntoValue<Flt64>.Identity,
            bigM: new Flt64(10),
            name: "outer-abs"
        );
        var outer = new LinearFunctionSymbolAdapter<Flt64>(outerFunction);
        parent.Add(inner).IsOk.Should().BeTrue();
        parent.Add(outer).IsOk.Should().BeTrue();
        parent.State.RegisterRow(new ConstraintId("outer-abs-row"), Row(outer, 1)).IsOk.Should().BeTrue();
        LinearModelSnapshot<Flt64> initial = parent.State.Refresh().Value;
        using LinearMechanismModel<Flt64> built = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        var values = new Dictionary<IVariableItem, Flt64> {
            [x] = new Flt64(-1),
            [y] = Flt64.Zero,
            [innerFunction.ResultVar] = Flt64.One,
            [innerFunction.PositiveVar] = Flt64.Zero,
            [innerFunction.NegativeVar] = Flt64.One,
            [innerFunction.SignVar] = Flt64.Zero,
            [outerFunction.ResultVar] = Flt64.One,
            [outerFunction.PositiveVar] = Flt64.One,
            [outerFunction.NegativeVar] = Flt64.Zero,
            [outerFunction.SignVar] = Flt64.One,
        };
        IReadOnlyList<Flt64> solution = built.Tokens.TokensInSolver.Select(token => values[token.Variable]).ToList();
        ((IMechanismModel<Flt64>)built).Constraints.Should().HaveCount(9)
            .And.OnlyContain(item => item.IsTrue(solution) == true);

        input[0] = new LinearMonomial<Flt64>(Flt64.One, y);
        LinearModelSnapshot<Flt64> changed = parent.State.Refresh().Value;
        changed.Revision.Sequence.Should().Be(initial.Revision.Sequence);
        innerFunction.InputSymbols.Should().Contain(x).And.NotContain(y);
        changed.Rows.Single(item => item.Id == "outer-abs-row").Coefficients[outerFunction.ResultVar]
            .Should().Be(Flt64.One);
        parent.Remove(inner);
        int callbacks = parent.Tokens.Find(x)!.RefreshCallbacks.Count;
        (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).IsFailed.Should().BeTrue();
        parent.Tokens.Find(x)!.RefreshCallbacks.Should().HaveCount(callbacks);
        parent.Add(inner).IsOk.Should().BeTrue();
        LinearModelSnapshot<Flt64> restored = parent.State.Refresh().Value;
        restored.Revision.Sequence.Should().Be(changed.Revision.Sequence + 1);
        restored.Rows.Single(item => item.Id == "outer-abs-row").Coefficients[outerFunction.ResultVar]
            .Should().Be(Flt64.One);
    }

    [Fact]
    public void DecimalAndRationalFunctions_ShouldPreserveNativeCoefficients() {
        var x = new RealVar("generic-function-x");
        IFlt64ValueConverter<FltX> decimalConverter = FltX.Zero;
        using var decimalModel = new LinearMetaModel<FltX>(converter: decimalConverter);
        decimalModel.Add(x).IsOk.Should().BeTrue();
        var decimalTerms = new List<LinearMonomial<FltX>> {
            new(new FltX(9007199254740993.1m), x),
        };
        var decimalFunction = new LinearFunctionSymbolAdapter<FltX>(new StructuralFunction<FltX>(
            name: "decimal-function",
            polynomial: new LinearPolynomial<FltX>(decimalTerms, new FltX(0.2m))
        ));
        decimalModel.Add(decimalFunction).IsOk.Should().BeTrue();
        var decimalRow = new LinearInequality<FltX>(
            Lhs: new LinearPolynomial<FltX>([new LinearMonomial<FltX>(FltX.One, decimalFunction)], FltX.Zero),
            Rhs: new LinearPolynomial<FltX>([], new FltX(0.1m)),
            Comparison: Comparison.LE
        );
        decimalModel.State.RegisterRow(new ConstraintId("decimal-function-row"), decimalRow).IsOk.Should().BeTrue();
        LinearModelRow<FltX> decimalSnapshot = decimalModel.State.Refresh().Value.Rows.Single();
        decimalSnapshot.Coefficients[x].Should().Be(new FltX(9007199254740993.1m));
        decimalSnapshot.Rhs.Should().Be(new FltX(-0.1m));
        decimalTerms[0] = new LinearMonomial<FltX>(FltX.One, x);
        decimalModel.State.Refresh().IsFailed.Should().BeTrue();

        IFlt64ValueConverter<RtnX> rationalConverter = RtnX.Zero;
        using var rationalModel = new LinearMetaModel<RtnX>(converter: rationalConverter);
        rationalModel.Add(x).IsOk.Should().BeTrue();
        var rationalFunction = new LinearFunctionSymbolAdapter<RtnX>(new StructuralFunction<RtnX>(
            name: "rational-function",
            polynomial: new LinearPolynomial<RtnX>(
                [new LinearMonomial<RtnX>(new RtnX(1, 3), x)], new RtnX(1, 4))
        ));
        rationalModel.Add(rationalFunction).IsOk.Should().BeTrue();
        var rationalRow = new LinearInequality<RtnX>(
            Lhs: new LinearPolynomial<RtnX>([new LinearMonomial<RtnX>(new RtnX(3, 2), rationalFunction)], RtnX.Zero),
            Rhs: new LinearPolynomial<RtnX>([], RtnX.One),
            Comparison: Comparison.LE
        );
        rationalModel.State.RegisterRow(new ConstraintId("rational-function-row"), rationalRow).IsOk.Should().BeTrue();
        LinearModelRow<RtnX> rationalSnapshot = rationalModel.State.Refresh().Value.Rows.Single();
        rationalSnapshot.Coefficients[x].Eq(new RtnX(1, 2)).Should().BeTrue();
        rationalSnapshot.Rhs.Eq(new RtnX(5, 8)).Should().BeTrue();
    }

    [Fact]
    public void RationalCrossProductOverflow_ShouldNotHideFunctionOrRevisionChanges() {
        var x = new RealVar("large-rational-x");
        var terms = new List<LinearMonomial<RtnX>> {
            new(new RtnX(double.MaxValue, double.MaxValue), x),
        };
        IFlt64ValueConverter<RtnX> converter = RtnX.Zero;
        using var functionModel = new LinearMetaModel<RtnX>(converter: converter);
        functionModel.Add(x).IsOk.Should().BeTrue();
        var function = new LinearFunctionSymbolAdapter<RtnX>(new StructuralFunction<RtnX>(
            name: "large-rational-function",
            polynomial: new LinearPolynomial<RtnX>(terms, RtnX.Zero)
        ));
        functionModel.Add(function).IsOk.Should().BeTrue();
        var row = new LinearInequality<RtnX>(
            Lhs: new LinearPolynomial<RtnX>([new LinearMonomial<RtnX>(RtnX.One, function)], RtnX.Zero),
            Rhs: new LinearPolynomial<RtnX>([], RtnX.Zero),
            Comparison: Comparison.LE
        );
        functionModel.State.RegisterRow(new ConstraintId("large-rational-row"), row).IsOk.Should().BeTrue();
        LinearModelSnapshot<RtnX> functionSnapshot = functionModel.State.Refresh().Value;
        terms[0] = new LinearMonomial<RtnX>(new RtnX(double.MaxValue / 2, double.MaxValue), x);
        functionModel.State.Refresh().IsFailed.Should().BeTrue();
        functionModel.State.RegisterRow(new ConstraintId("large-rational-row"), row).IsFailed.Should().BeTrue();
        terms[0] = new LinearMonomial<RtnX>(new RtnX(double.MaxValue, double.MaxValue), x);
        functionModel.State.Refresh().Value.Should().Be(functionSnapshot);

        using var objectiveModel = new LinearMetaModel<RtnX>(converter: converter);
        objectiveModel.Add(x).IsOk.Should().BeTrue();
        var objectiveTerms = new List<LinearMonomial<RtnX>> {
            new(new RtnX(double.MaxValue, double.MaxValue), x),
        };
        objectiveModel.AddObject(
            category: ObjectCategory.Minimum,
            polynomial: new LinearPolynomial<RtnX>(objectiveTerms, RtnX.Zero),
            name: "large-rational-objective",
            displayName: null
        ).IsOk.Should().BeTrue();
        LinearModelRevision revision = objectiveModel.State.Refresh().Value.Revision;
        objectiveTerms[0] = new LinearMonomial<RtnX>(new RtnX(double.MaxValue / 2, double.MaxValue), x);
        objectiveModel.State.RequireCurrent(revision).IsFailed.Should().BeTrue();
        LinearModelRevision changed = objectiveModel.State.Refresh().Value.Revision;
        changed.Sequence.Should().Be(revision.Sequence + 1);
        objectiveTerms[0] = new LinearMonomial<RtnX>(new RtnX(double.MaxValue, double.MaxValue), x);
        objectiveModel.State.Refresh().Value.Revision.Sequence.Should().Be(changed.Sequence + 1);
    }

    [Fact]
    public void RationalChangesDuringRowCapture_ShouldNotReserveTheIdentity() {
        var x = new RealVar("switching-rational-x");
        IFlt64ValueConverter<RtnX> converter = RtnX.Zero;
        var identities = new ModelElementIdentityRegistry();
        using var model = new LinearMetaModel<RtnX>(
            converter: converter,
            identityRegistry: identities
        );
        model.Add(x).IsOk.Should().BeTrue();
        var function = new LinearFunctionSymbolAdapter<RtnX>(new StructuralFunction<RtnX>(
            name: "switching-rational-function",
            polynomial: new LinearPolynomial<RtnX>(new SwitchingRationalTerms(x), RtnX.Zero)
        ));
        model.Add(function).IsOk.Should().BeTrue();
        var row = new LinearInequality<RtnX>(
            Lhs: new LinearPolynomial<RtnX>([new LinearMonomial<RtnX>(RtnX.One, function)], RtnX.Zero),
            Rhs: new LinearPolynomial<RtnX>([], RtnX.Zero),
            Comparison: Comparison.LE
        );
        var id = new ConstraintId("switching-rational-row");
        model.State.RegisterRow(id, row).IsFailed.Should().BeTrue();
        model.Constraints.Should().BeEmpty();
        model.State.RegisterRow(id, row).IsOk.Should().BeTrue();
        model.State.Refresh().IsOk.Should().BeTrue();
    }

    [Fact]
    public void RationalDynamicRowRetry_ShouldCompareStoredCoefficientsConservatively() {
        var x = new RealVar("rational-retry-x");
        IFlt64ValueConverter<RtnX> converter = RtnX.Zero;
        using var model = new LinearMetaModel<RtnX>(converter: converter);
        model.Add(x).IsOk.Should().BeTrue();
        var terms = new List<LinearMonomial<RtnX>> {
            new(new RtnX(double.MaxValue, double.MaxValue), x),
        };
        var row = new LinearInequality<RtnX>(
            Lhs: new LinearPolynomial<RtnX>(terms, RtnX.Zero),
            Rhs: new LinearPolynomial<RtnX>([], RtnX.Zero),
            Comparison: Comparison.LE
        );
        var id = new ConstraintId("rational-retry-row");
        model.State.RegisterRow(id, row).IsOk.Should().BeTrue();
        LinearModelSnapshot<RtnX> initial = model.State.Refresh().Value;
        terms[0] = new LinearMonomial<RtnX>(new RtnX(double.MaxValue / 2, double.MaxValue), x);
        model.State.RegisterRow(id, row).IsFailed.Should().BeTrue();
        model.Constraints.Should().ContainSingle();
        model.State.Refresh().Value.Should().Be(initial);
        terms[0] = new LinearMonomial<RtnX>(new RtnX(double.MaxValue, double.MaxValue), x);
        model.State.RegisterRow(id, row).IsOk.Should().BeTrue();
    }

    [Fact]
    public void IntegerFunctionOverflow_ShouldFailWithoutReservingTheRowIdentity() {
        using var parent = new LinearMetaModel<Integer>(converter: new IntegerConverter());
        var x = new RealVar("integer-function-x");
        parent.Add(x).IsOk.Should().BeTrue();
        var function = new LinearFunctionSymbolAdapter<Integer>(new StructuralFunction<Integer>(
            name: "integer-function",
            polynomial: new LinearPolynomial<Integer>(
                [new LinearMonomial<Integer>(new Integer(long.MaxValue), x)], Integer.Zero)
        ));
        parent.Add(function).IsOk.Should().BeTrue();
        var lhs = new List<LinearMonomial<Integer>> { new(new Integer(2), function) };
        var relation = new LinearInequality<Integer>(
            Lhs: new LinearPolynomial<Integer>(lhs, Integer.Zero),
            Rhs: new LinearPolynomial<Integer>([], Integer.Zero),
            Comparison: Comparison.LE
        );
        var id = new ConstraintId("integer-function-row");
        parent.State.RegisterRow(id, relation).IsFailed.Should().BeTrue();
        parent.Constraints.Should().BeEmpty();
        lhs[0] = new LinearMonomial<Integer>(Integer.One, function);
        parent.State.RegisterRow(id, relation).IsOk.Should().BeTrue();
        parent.State.Refresh().Value.Rows.Single().Coefficients[x].Should().Be(new Integer(long.MaxValue));
    }

    private static LinearFunctionSymbolAdapter<Flt64> Adapter(string name, IReadOnlyList<LinearMonomial<Flt64>> terms) =>
        new(new StructuralFunction(name, terms));

    private static LinearInequality<Flt64> Row(ISymbol symbol, double rhs) => new(
        Lhs: Polynomial(0, (1, symbol)),
        Rhs: Polynomial(rhs),
        Comparison: Comparison.LE
    );

    private static LinearPolynomial<Flt64> Polynomial(double constant, params (double Coefficient, ISymbol Symbol)[] terms) =>
        new(terms.Select(term => new LinearMonomial<Flt64>(new Flt64(term.Coefficient), term.Symbol)).ToList(), new Flt64(constant));

    private sealed class StructuralFunction(string name, IReadOnlyList<LinearMonomial<Flt64>> terms) :
        IMathFunctionSymbol<Flt64>, IHasResultPolynomial<Flt64> {
        public string Name { get; set; } = name;
        public string? DisplayName { get; set; }
        public Action? OnRegister { get; set; }
        public Try? RegistrationResult { get; set; }
        public IReadOnlyList<IVariableItem> HelperVariables => Array.Empty<IVariableItem>();
        public IReadOnlyCollection<ISymbol> InputSymbols => Array.Empty<ISymbol>();
        public LinearPolynomial<Flt64> ResultPolynomial { get; } = new(terms, Flt64.Zero);
        public Flt64? Evaluate(IReadOnlyDictionary<ISymbol, Flt64> values) => null;
        public Try RegisterAuxiliaryTokens(IAddableTokenCollection<Flt64> tokens) => Results.OkInstance;
        public Try RegisterConstraints(IAbstractLinearMechanismModel<Flt64> model) {
            OnRegister?.Invoke();
            return RegistrationResult ?? Results.OkInstance;
        }
    }

    private sealed class StructuralFunction<V>(string name, LinearPolynomial<V> polynomial) :
        IMathFunctionSymbol<V>, IHasResultPolynomial<V>
        where V : struct, IRealNumber<V>, INumberField<V> {
        public string Name { get; set; } = name;
        public string? DisplayName { get; set; }
        public IReadOnlyList<IVariableItem> HelperVariables => Array.Empty<IVariableItem>();
        public IReadOnlyCollection<ISymbol> InputSymbols => Array.Empty<ISymbol>();
        public LinearPolynomial<V> ResultPolynomial { get; } = polynomial;
        public V? Evaluate(IReadOnlyDictionary<ISymbol, V> values) => null;
        public Try RegisterAuxiliaryTokens(IAddableTokenCollection<V> tokens) => Results.OkInstance;
        public Try RegisterConstraints(IAbstractLinearMechanismModel<V> model) => Results.OkInstance;
    }

    private sealed class IntegerConverter : IFlt64ValueConverter<Integer> {
        public Integer Zero => Integer.Zero;
        public Integer One => Integer.One;
        public Integer IntoValue(Flt64 value) => value.ToInt64();
        public Flt64 FromValue(Integer value) => value.ToFlt64();
    }

    private sealed class SwitchingTerms(IVariableItem first, IVariableItem second) : IReadOnlyList<LinearMonomial<Flt64>> {
        private int _reads;
        public int Count => 1;
        public LinearMonomial<Flt64> this[int index] => new(Flt64.One, _reads++ == 0 ? first : second);
        public IEnumerator<LinearMonomial<Flt64>> GetEnumerator() {
            yield return this[0];
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class SwitchingRationalTerms(IVariableItem variable) : IReadOnlyList<LinearMonomial<RtnX>> {
        private int _reads;
        public int Count => 1;
        public LinearMonomial<RtnX> this[int index] => new(
            new RtnX(_reads++ == 0 ? double.MaxValue : double.MaxValue / 2, double.MaxValue), variable);
        public IEnumerator<LinearMonomial<RtnX>> GetEnumerator() {
            yield return this[0];
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
