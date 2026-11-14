#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Algebra.ValueRange;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
using Integer = Fuookami.Ospf.Math.Algebra.Number.Int64;
using MathUInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;
namespace Fuookami.Ospf.Core.Tests.Symbol.Function;

/// <summary>构造时边界依赖与输入快照 / Construction-time bound dependencies and input snapshots.</summary>
public class CapturedBoundsLifecycleTest {
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
    public async Task BuiltModels_ShouldRejectChangedTrackedBoundsWithoutBlockingUnrelatedChanges(int kind, bool explicitBigM) {
        RealVar x = BoundedReal();
        var unrelated = new RealVar("unrelated-bounds");
        IMathFunctionSymbol<Flt64> function = Create(
            kind: kind,
            input: Polynomial(x),
            explicitBigM: explicitBigM
        );
        using var linearParent = new LinearMetaModel<Flt64>();
        using var quadraticParent = new QuadraticMetaModel<Flt64>();
        linearParent.Add(x).IsOk.Should().BeTrue();
        linearParent.Add(unrelated).IsOk.Should().BeTrue();
        linearParent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk.Should().BeTrue();
        quadraticParent.Add(x).IsOk.Should().BeTrue();
        quadraticParent.Add(unrelated).IsOk.Should().BeTrue();
        quadraticParent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk.Should().BeTrue();
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> linearBuild =
            await LinearMechanismModel<Flt64>.InvokeAsync(linearParent);
        Result<QuadraticMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> quadraticBuild =
            await QuadraticMechanismModel<Flt64>.InvokeAsync(quadraticParent);
        linearBuild.IsOk.Should().BeTrue($"linear {kind}: {linearBuild}");
        quadraticBuild.IsOk.Should().BeTrue($"quadratic {kind}: {quadraticBuild}");
        using LinearMechanismModel<Flt64> linear = linearBuild.Value;
        using QuadraticMechanismModel<Flt64> quadratic = quadraticBuild.Value;
        using LinearTriadModel original = linear.ToLinearTriadModel().Value;
        using QuadraticTetradModel originalQuadratic = quadratic.ToQuadraticTetradModel().Value;
        ValueRange<Flt64> initialRange = x.Range.Range ?? throw new InvalidOperationException("Missing initial range.");
        ValueRange<Flt64> initialUnrelatedRange = unrelated.Range.Range ?? throw new InvalidOperationException("Missing unrelated range.");

        unrelated.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-4), new Flt64(4)).Value);
        linear.ValidateSolverTokenSnapshot(original).IsFailed.Should().BeTrue();
        quadratic.ValidateSolverTokenSnapshot(originalQuadratic).IsFailed.Should().BeTrue();
        using (LinearTriadModel changedUnrelated = linear.ToLinearTriadModel().Value) {
            changedUnrelated.Variables.Single(variable => variable.Origin == unrelated).UpperBound.Should().Be(new Flt64(4));
            linear.ValidateSolverTokenSnapshot(changedUnrelated).IsOk.Should().BeTrue();
        }
        using (QuadraticTetradModel changedUnrelated = quadratic.ToQuadraticTetradModel().Value) {
            changedUnrelated.Variables.Single(variable => variable.Origin == unrelated).UpperBound.Should().Be(new Flt64(4));
            quadratic.ValidateSolverTokenSnapshot(changedUnrelated).IsOk.Should().BeTrue();
        }
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-2), new Flt64(2)).Value);
        linear.ValidateBoundDependencies().IsFailed.Should().BeTrue();
        quadratic.ValidateBoundDependencies().IsFailed.Should().BeTrue();
        linear.ValidateSolverTokenSnapshot(original).IsFailed.Should().BeTrue();
        quadratic.ValidateSolverTokenSnapshot(originalQuadratic).IsFailed.Should().BeTrue();
        linear.ToLinearTriadModel().IsFailed.Should().BeTrue();
        linear.ToLinearTriadModel(new Dictionary<IVariableItem, Flt64> { [x] = Flt64.Zero }).IsFailed.Should().BeTrue();
        quadratic.ToQuadraticTetradModel().IsFailed.Should().BeTrue();
        quadratic.ToQuadraticTetradModel(new Dictionary<IVariableItem, Flt64> { [x] = Flt64.Zero }).IsFailed.Should().BeTrue();
        original.Variables.Single(variable => variable.Origin == x).UpperBound.Should().Be(Flt64.One);
        x.Range.SetRange(initialRange);
        unrelated.Range.SetRange(initialUnrelatedRange);
        linear.ValidateSolverTokenSnapshot(original).IsOk.Should().BeTrue();
        quadratic.ValidateSolverTokenSnapshot(originalQuadratic).IsOk.Should().BeTrue();
        using LinearTriadModel restored = linear.ToLinearTriadModel().Value;
        restored.Variables.Single(variable => variable.Origin == x).UpperBound.Should().Be(Flt64.One);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BuiltModels_ShouldRejectEquivalentOrNarrowerReplacementBounds(bool narrower) {
        RealVar x = BoundedReal();
        ValueRange<Flt64> originalRange = x.Range.Range ?? throw new InvalidOperationException("Missing initial range.");
        IMathFunctionSymbol<Flt64> function = Create(kind: 7, input: Polynomial(x), explicitBigM: false);
        using var linearParent = new LinearMetaModel<Flt64>();
        using var quadraticParent = new QuadraticMetaModel<Flt64>();
        linearParent.Add(x).IsOk.Should().BeTrue();
        linearParent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk.Should().BeTrue();
        quadraticParent.Add(x).IsOk.Should().BeTrue();
        quadraticParent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk.Should().BeTrue();
        using LinearMechanismModel<Flt64> linear = (await LinearMechanismModel<Flt64>.InvokeAsync(linearParent)).Value;
        using QuadraticMechanismModel<Flt64> quadratic = (await QuadraticMechanismModel<Flt64>.InvokeAsync(quadraticParent)).Value;
        using LinearTriadModel originalLinear = linear.ToLinearTriadModel().Value;
        using QuadraticTetradModel originalQuadratic = quadratic.ToQuadraticTetradModel().Value;

        Flt64 limit = narrower ? new Flt64(0.5) : Flt64.One;
        Flt64 lower = narrower ? new Flt64(-0.5) : new Flt64(-1);
        x.Range.SetRange(ValueRange<Flt64>.Of(lower, limit).Value);
        linear.ToLinearTriadModel()
            .Should().BeOfType<Failed<LinearTriadModel, ErrorCode, Error<ErrorCode>>>()
            .Which.Error.Code.Should().Be(ErrorCode.IllegalArgument);
        quadratic.ToQuadraticTetradModel()
            .Should().BeOfType<Failed<QuadraticTetradModel, ErrorCode, Error<ErrorCode>>>()
            .Which.Error.Code.Should().Be(ErrorCode.IllegalArgument);
        originalLinear.Variables.Single(variable => variable.Origin == x).UpperBound.Should().Be(Flt64.One);
        originalQuadratic.Variables.Single(variable => variable.Origin == x).UpperBound.Should().Be(Flt64.One);
        x.Range.SetRange(originalRange);
        using LinearTriadModel restored = linear.ToLinearTriadModel().Value;
        restored.Variables.Single(variable => variable.Origin == x).UpperBound.Should().Be(Flt64.One);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public async Task ExplicitBigMWithoutCapturedBounds_ShouldLeaveBoundsUnderCallerControl(int kind) {
        RealVar x = BoundedReal();
        IMathFunctionSymbol<Flt64> function = Create(
            kind: kind,
            input: Polynomial(x),
            explicitBigM: true
        );
        using var parent = new LinearMetaModel<Flt64>();
        parent.Add(x).IsOk.Should().BeTrue();
        parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk.Should().BeTrue();
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-2), new Flt64(2)).Value);

        model.ValidateBoundDependencies().IsOk.Should().BeTrue();
        using LinearTriadModel matrix = model.ToLinearTriadModel().Value;
        matrix.Variables.Single(variable => variable.Origin == x).UpperBound.Should().Be(new Flt64(2));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public async Task ManualRegistration_ShouldTrackAutomaticBounds(int kind) {
        RealVar x = BoundedReal();
        IMathFunctionSymbol<Flt64> function = Create(
            kind: kind,
            input: Polynomial(x),
            explicitBigM: false
        );
        using var parent = new LinearMetaModel<Flt64>();
        parent.Add(x).IsOk.Should().BeTrue();
        foreach (IVariableItem helper in function.HelperVariables) {
            parent.Add(helper).IsOk.Should().BeTrue();
        }
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        function.RegisterConstraints(model).IsOk.Should().BeTrue();
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-2), new Flt64(2)).Value);

        model.ValidateBoundDependencies().IsFailed.Should().BeTrue();
        model.ToLinearTriadModel().IsFailed.Should().BeTrue();
    }

    [Fact]
    public async Task IntegerRangeChangeHiddenByDouble_ShouldInvalidateBuiltModel() {
        var x = new IntVar("model-bound-integer");
        var first = new Integer(9007199254740992L);
        var next = new Integer(9007199254740993L);
        x.Range.SetRange(ValueRange<Integer>.Of(first, first).Value);
        var input = new LinearPolynomial<Flt64>(
            new[] { new LinearMonomial<Flt64>(new Flt64(1e-15), x) },
            Flt64.Zero
        );
        IMathFunctionSymbol<Flt64> function = Create(
            kind: 7,
            input: input,
            explicitBigM: false
        );
        using var parent = new LinearMetaModel<Flt64>();
        parent.Add(x).IsOk.Should().BeTrue();
        parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk.Should().BeTrue();
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        Flt64 oldUpper = x.UpperBound!.Value.Unwrap();
        x.Range.SetRange(ValueRange<Integer>.Of(first, next).Value);
        x.UpperBound!.Value.Unwrap().Should().Be(oldUpper);

        model.ToLinearTriadModel().IsFailed.Should().BeTrue();
    }

    [Fact]
    public async Task BoundsChangedDuringDump_ShouldRejectBothMatrixTypes() {
        RealVar x = BoundedReal();
        ValueRange<Flt64> initialRange = x.Range.Range ?? throw new InvalidOperationException("Missing initial range.");
        var trigger = new BoundsChangingVariable(new RealVar("dump-trigger"), () =>
            x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-2), new Flt64(2)).Value));
        IMathFunctionSymbol<Flt64> function = Create(kind: 7, input: Polynomial(x), explicitBigM: false);
        using var linearParent = new LinearMetaModel<Flt64>();
        using var quadraticParent = new QuadraticMetaModel<Flt64>();
        linearParent.Add(x).IsOk.Should().BeTrue();
        linearParent.Add(trigger).IsOk.Should().BeTrue();
        linearParent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk.Should().BeTrue();
        quadraticParent.Add(x).IsOk.Should().BeTrue();
        quadraticParent.Add(trigger).IsOk.Should().BeTrue();
        quadraticParent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk.Should().BeTrue();
        using LinearMechanismModel<Flt64> linear = (await LinearMechanismModel<Flt64>.InvokeAsync(linearParent)).Value;
        using QuadraticMechanismModel<Flt64> quadratic = (await QuadraticMechanismModel<Flt64>.InvokeAsync(quadraticParent)).Value;

        trigger.Armed = true;
        linear.ToLinearTriadModel().IsFailed.Should().BeTrue();
        trigger.Armed.Should().BeFalse();
        x.Range.SetRange(initialRange);
        trigger.Armed = true;
        quadratic.ToQuadraticTetradModel().IsFailed.Should().BeTrue();
        trigger.Armed.Should().BeFalse();
    }

    [Fact]
    public async Task UntrackedBoundsChangedDuringDump_ShouldRejectBothMatrixTypes() {
        RealVar x = BoundedReal();
        ValueRange<Flt64> initialRange = x.Range.Range ?? throw new InvalidOperationException("Missing initial range.");
        var trigger = new BoundsChangingVariable(new RealVar("untracked-dump-trigger"), () =>
            x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-2), new Flt64(2)).Value));
        using var linearParent = new LinearMetaModel<Flt64>();
        using var quadraticParent = new QuadraticMetaModel<Flt64>();
        linearParent.Add(x).IsOk.Should().BeTrue();
        linearParent.Add(trigger).IsOk.Should().BeTrue();
        quadraticParent.Add(x).IsOk.Should().BeTrue();
        quadraticParent.Add(trigger).IsOk.Should().BeTrue();
        using LinearMechanismModel<Flt64> linear = (await LinearMechanismModel<Flt64>.InvokeAsync(linearParent)).Value;
        using QuadraticMechanismModel<Flt64> quadratic = (await QuadraticMechanismModel<Flt64>.InvokeAsync(quadraticParent)).Value;

        trigger.Armed = true;
        linear.ToLinearTriadModel().IsFailed.Should().BeTrue();
        trigger.Armed.Should().BeFalse();
        x.Range.SetRange(initialRange);
        trigger.Armed = true;
        quadratic.ToQuadraticTetradModel().IsFailed.Should().BeTrue();
        trigger.Armed.Should().BeFalse();
    }

    [Fact]
    public async Task UnreadableBoundsDuringDump_ShouldReturnFailure() {
        var trigger = new BoundsChangingVariable(new RealVar("unreadable-dump-trigger"),
            () => throw new InvalidOperationException("unreadable bounds"));
        using var linearParent = new LinearMetaModel<Flt64>();
        using var quadraticParent = new QuadraticMetaModel<Flt64>();
        linearParent.Add(trigger).IsOk.Should().BeTrue();
        quadraticParent.Add(trigger).IsOk.Should().BeTrue();
        using LinearMechanismModel<Flt64> linear = (await LinearMechanismModel<Flt64>.InvokeAsync(linearParent)).Value;
        using QuadraticMechanismModel<Flt64> quadratic = (await QuadraticMechanismModel<Flt64>.InvokeAsync(quadraticParent)).Value;

        trigger.Armed = true;
        linear.ToLinearTriadModel().IsFailed.Should().BeTrue();
        trigger.Armed = true;
        quadratic.ToQuadraticTetradModel().IsFailed.Should().BeTrue();
    }

    [Fact]
    public async Task IntegerRangeChangeHiddenByDouble_ShouldInvalidateOldMatrices() {
        var x = new IntVar("matrix-bound-integer");
        var first = new Integer(9007199254740992L);
        var next = new Integer(9007199254740993L);
        x.Range.SetRange(ValueRange<Integer>.Of(first, first).Value);
        using var linearParent = new LinearMetaModel<Flt64>();
        using var quadraticParent = new QuadraticMetaModel<Flt64>();
        linearParent.Add(x).IsOk.Should().BeTrue();
        quadraticParent.Add(x).IsOk.Should().BeTrue();
        using LinearMechanismModel<Flt64> linear = (await LinearMechanismModel<Flt64>.InvokeAsync(linearParent)).Value;
        using QuadraticMechanismModel<Flt64> quadratic = (await QuadraticMechanismModel<Flt64>.InvokeAsync(quadraticParent)).Value;
        using LinearTriadModel oldLinear = linear.ToLinearTriadModel().Value;
        using QuadraticTetradModel oldQuadratic = quadratic.ToQuadraticTetradModel().Value;
        Flt64 upper = x.UpperBound!.Value.Unwrap();

        x.Range.SetRange(ValueRange<Integer>.Of(first, next).Value);
        x.UpperBound!.Value.Unwrap().Should().Be(upper);
        linear.ValidateSolverTokenSnapshot(oldLinear).IsFailed.Should().BeTrue();
        quadratic.ValidateSolverTokenSnapshot(oldQuadratic).IsFailed.Should().BeTrue();
        using LinearTriadModel newLinear = linear.ToLinearTriadModel().Value;
        using QuadraticTetradModel newQuadratic = quadratic.ToQuadraticTetradModel().Value;
        linear.ValidateSolverTokenSnapshot(newLinear).IsOk.Should().BeTrue();
        quadratic.ValidateSolverTokenSnapshot(newQuadratic).IsOk.Should().BeTrue();
    }

    [Fact]
    public async Task BoundsChangedDuringStateRefresh_ShouldNotPublishSnapshotOrReserveRowIds() {
        RealVar x = BoundedReal();
        ValueRange<Flt64> initialRange = x.Range.Range ?? throw new InvalidOperationException("Missing initial range.");
        var trigger = new BoundsChangingVariable(new RealVar("state-trigger"), () =>
            x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-2), new Flt64(2)).Value));
        var function = new ArmBoundsChangingFunction(
            Create(kind: 7, input: Polynomial(x), explicitBigM: false),
            trigger
        );
        using var parent = new LinearMetaModel<Flt64>();
        parent.Add(x).IsOk.Should().BeTrue();
        parent.Add(trigger).IsOk.Should().BeTrue();
        parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk.Should().BeTrue();
        parent.AddConstraint(Polynomial(x).Le(Flt64.One), null, name: "original").IsOk.Should().BeTrue();
        LinearModelSnapshot<Flt64> original = (await parent.State.RefreshAsync()).Value;
        string originalId = original.Rows.Single(row => row.SourceName == "original").Id;

        parent.AddConstraint(Polynomial(x).Ge(Flt64.Zero), null, name: "temporary").IsOk.Should().BeTrue();
        function.ArmOnRegister = true;
        Result<LinearModelSnapshot<Flt64>, ErrorCode, Error<ErrorCode>> failed = await parent.State.RefreshAsync();
        trigger.Armed.Should().BeFalse();
        failed.Should().BeOfType<Failed<LinearModelSnapshot<Flt64>, ErrorCode, Error<ErrorCode>>>()
            .Which.Error.Code.Should().Be(ErrorCode.IllegalArgument);
        original.Variables.Single(variable => variable.Variable == x).UpperBound.Should().Be(Flt64.One);
        function.ArmOnRegister = false;
        x.Range.SetRange(initialRange);
        parent.RollbackConstraint(parent.RelationConstraints[^1]);
        (await parent.State.RefreshAsync()).Value.Should().Be(original);

        parent.AddConstraint(Polynomial(x).Ge(Flt64.Zero), null, name: "replacement").IsOk.Should().BeTrue();
        LinearModelSnapshot<Flt64> replacement = (await parent.State.RefreshAsync()).Value;
        replacement.Revision.Sequence.Should().Be(original.Revision.Sequence + 1);
        replacement.Rows.Single(row => row.SourceName == "original").Id.Should().Be(originalId);
        replacement.Rows.Single(row => row.SourceName == "replacement").Id
            .Should().Be($"model-local-row:{original.Revision.Owner}:1");
    }

    [Fact]
    public async Task NestedComparison_ShouldTrackExpandedVariableBounds() {
        RealVar x = BoundedReal();
        var nested = new LinearExpressionSymbol(
            polynomial: new MutableLinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(new Flt64(2), x) },
                new Flt64(0.5)
            ),
            name: "nested-tracked-bounds"
        );
        var function = new InequalityFunction<Flt64>(
            lhs: new LinearPolynomial<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, nested) }, Flt64.Zero),
            rhs: Flt64.Zero,
            sign: Comparison.GT,
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: new Flt64(0.25)
        );
        using var parent = new LinearMetaModel<Flt64>();
        parent.Add(x).IsOk.Should().BeTrue();
        parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk.Should().BeTrue();
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-2), new Flt64(2)).Value);

        model.ValidateBoundDependencies().IsFailed.Should().BeTrue();
        model.ToLinearTriadModel().IsFailed.Should().BeTrue();
    }

    [Fact]
    public async Task NestedSatisfiedCount_ShouldTrackFunctionResultBounds() {
        RealVar x = BoundedReal();
        var absolute = new AbsFunction<Flt64>(
            polynomial: Polynomial(x),
            converter: IIntoValue<Flt64>.Identity
        );
        var inner = new LinearFunctionSymbolAdapter<Flt64>(absolute);
        IMathFunctionSymbol<Flt64> function = Create(
            kind: 7,
            input: new LinearPolynomial<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, inner) }, Flt64.Zero),
            explicitBigM: false
        );
        using var parent = new LinearMetaModel<Flt64>();
        parent.Add(x).IsOk.Should().BeTrue();
        parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk.Should().BeTrue();
        parent.Add(inner).IsOk.Should().BeTrue();
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        absolute.ResultVar.Range.SetRange(ValueRange<Flt64>.Of(Flt64.Zero, new Flt64(10)).Value);

        model.ValidateBoundDependencies().IsFailed.Should().BeTrue();
        model.ToLinearTriadModel().IsFailed.Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BoundsChangedDuringRegistration_ShouldFailBeforeTokenCommit(bool quadratic) {
        RealVar x = BoundedReal();
        IMathFunctionSymbol<Flt64> inner = Create(kind: 7, input: Polynomial(x), explicitBigM: false);
        var function = new BoundsChangingFunction(inner, x);
        if (quadratic) {
            using var parent = new QuadraticMetaModel<Flt64>();
            parent.Add(x).IsOk.Should().BeTrue();
            parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk.Should().BeTrue();
            IVariableItem[] before = parent.Tokens.Tokens.Select(token => token.Variable).ToArray();
            (await QuadraticMechanismModel<Flt64>.InvokeAsync(parent)).IsFailed.Should().BeTrue();
            parent.Tokens.Tokens.Select(token => token.Variable).Should().Equal(before);
        }
        else {
            using var parent = new LinearMetaModel<Flt64>();
            parent.Add(x).IsOk.Should().BeTrue();
            parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk.Should().BeTrue();
            IVariableItem[] before = parent.Tokens.Tokens.Select(token => token.Variable).ToArray();
            (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).IsFailed.Should().BeTrue();
            parent.Tokens.Tokens.Select(token => token.Variable).Should().Equal(before);
        }
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    [InlineData(4, false)]
    [InlineData(5, false)]
    public async Task ChangedInputRange_ShouldFailBeforeWritingRows(int kind, bool explicitBigM) {
        RealVar x = BoundedReal();
        IMathFunctionSymbol<Flt64> function = Create(
            kind: kind,
            input: Polynomial(x),
            explicitBigM: explicitBigM
        );
        using var parent = new LinearMetaModel<Flt64>();
        parent.Add(x).IsOk.Should().BeTrue();
        foreach (IVariableItem helper in function.HelperVariables) {
            parent.Add(helper).IsOk.Should().BeTrue();
        }
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built =
            await LinearMechanismModel<Flt64>.InvokeAsync(parent);
        built.IsOk.Should().BeTrue();
        using LinearMechanismModel<Flt64> model = built.Value;

        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-2), new Flt64(2)).Value);
        function.RegisterConstraints(model).IsFailed.Should().BeTrue();
        ((IMechanismModel<Flt64>)model).Constraints.Should().BeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(5)]
    public async Task Factory_ShouldRejectNarrowedOrReplacedInputRange(int kind) {
        RealVar x = BoundedReal();
        IMathFunctionSymbol<Flt64> function = Create(
            kind: kind,
            input: Polynomial(x),
            explicitBigM: false
        );
        using var parent = new LinearMetaModel<Flt64>();
        parent.Add(x).IsOk.Should().BeTrue();
        parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk.Should().BeTrue();
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-0.5), new Flt64(0.5)).Value);

        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built =
            await LinearMechanismModel<Flt64>.InvokeAsync(parent);
        built.IsFailed.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(5)]
    public async Task IntegerRangeChangeHiddenByFlt64_ShouldStillBeRejected(int kind) {
        var x = new IntVar("range-snapshot-integer");
        var first = new Integer(9007199254740992L);
        var next = new Integer(9007199254740993L);
        x.Range.SetRange(ValueRange<Integer>.Of(first, first).Value);
        var polynomial = new LinearPolynomial<Flt64>(
            new[] { new LinearMonomial<Flt64>(new Flt64(1e-15), x) },
            Flt64.Zero
        );
        IMathFunctionSymbol<Flt64> function = Create(
            kind: kind,
            input: polynomial,
            explicitBigM: false
        );
        Flt64 previousUpper = x.UpperBound!.Value.Unwrap();
        x.Range.SetRange(ValueRange<Integer>.Of(first, next).Value);
        x.UpperBound!.Value.Unwrap().Should().Be(previousUpper);

        using var parent = new LinearMetaModel<Flt64>();
        parent.Add(x).IsOk.Should().BeTrue();
        parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk.Should().BeTrue();
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built =
            await LinearMechanismModel<Flt64>.InvokeAsync(parent);
        built.IsFailed.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(5)]
    public async Task MutableInputList_ShouldBeCapturedForEvaluationAndRegistration(int kind) {
        RealVar x = BoundedReal();
        var terms = new List<LinearMonomial<Flt64>> { new(Flt64.One, x) };
        var input = new LinearPolynomial<Flt64>(terms, Flt64.Zero);
        IMathFunctionSymbol<Flt64> function = Create(
            kind: kind,
            input: input,
            explicitBigM: false
        );
        terms[0] = new LinearMonomial<Flt64>(new Flt64(20), x);

        function.Evaluate(new Dictionary<ISymbol, Flt64> { [x] = new Flt64(0.5) })
            .Should().Be(kind == 5 ? Flt64.One : new Flt64(0.5));
        using var parent = new LinearMetaModel<Flt64>();
        parent.Add(x).IsOk.Should().BeTrue();
        parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk.Should().BeTrue();
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built =
            await LinearMechanismModel<Flt64>.InvokeAsync(parent);
        built.IsOk.Should().BeTrue();
        using LinearMechanismModel<Flt64> model = built.Value;
        var values = new Dictionary<IVariableItem, Flt64> { [x] = new Flt64(0.5) };
        foreach (IVariableItem helper in function.HelperVariables) {
            values[helper] = helper switch {
                BinVar => Flt64.One,
                _ => new Flt64(0.5),
            };
        }
        if (function is AbsFunction<Flt64> absolute) {
            values[absolute.NegativeVar] = Flt64.Zero;
        }
        var solution = model.Tokens.TokensInSolver.Select(token => values[token.Variable]).ToList();
        ((IMechanismModel<Flt64>)model).Constraints.Should().OnlyContain(row => row.IsTrue(solution) == true);
    }

    [Fact]
    public async Task BinaryzationWithExplicitBigM_DoesNotDependOnCapturedInputBounds() {
        RealVar x = BoundedReal();
        IMathFunctionSymbol<Flt64> function = Create(
            kind: 5,
            input: Polynomial(x),
            explicitBigM: true
        );
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-2), new Flt64(2)).Value);
        using var parent = new LinearMetaModel<Flt64>();
        parent.Add(x).IsOk.Should().BeTrue();
        parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk.Should().BeTrue();

        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built =
            await LinearMechanismModel<Flt64>.InvokeAsync(parent);
        built.IsOk.Should().BeTrue();
        using LinearMechanismModel<Flt64> model = built.Value;
        ((IMechanismModel<Flt64>)model).Constraints.Should().HaveCount(2);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(5)]
    public async Task UntrackableCustomVariable_ShouldFailBeforeWritingRows(int kind) {
        var alias = new VariableAlias(BoundedReal());
        IMathFunctionSymbol<Flt64> function = Create(
            kind: kind,
            input: Polynomial(alias),
            explicitBigM: false
        );
        using var parent = new LinearMetaModel<Flt64>();
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built =
            await LinearMechanismModel<Flt64>.InvokeAsync(parent);
        built.IsOk.Should().BeTrue();
        using LinearMechanismModel<Flt64> model = built.Value;

        function.RegisterConstraints(model).IsFailed.Should().BeTrue();
        ((IMechanismModel<Flt64>)model).Constraints.Should().BeEmpty();
    }

    [Fact]
    public void IntermediateRange_ShouldCompareRationalRepresentationAndPermitCopiedRanges() {
        var half = new RtnX(1, 2);
        var symbol = new RangedSymbol<RtnX>(half);
        var input = new LinearPolynomial<RtnX>(
            new[] { new LinearMonomial<RtnX>(RtnX.One, symbol) },
            RtnX.Zero
        );
        var snapshot = new BoundDependencySnapshot<RtnX>(new[] { input });
        snapshot.IsCurrent().Should().BeTrue();

        var equivalent = new RtnX(2, 4);
        half.Eq(equivalent).Should().BeTrue();
        symbol.StoredRange.SetRange(ValueRange<RtnX>.Of(equivalent, equivalent).Value);
        snapshot.IsCurrent().Should().BeFalse();
    }

    [Fact]
    public void IntermediateRangeFailure_ShouldInvalidateTheSnapshot() {
        var symbol = new RangedSymbol<Flt64>(Flt64.One);
        var input = new LinearPolynomial<Flt64>(
            new[] { new LinearMonomial<Flt64>(Flt64.One, symbol) },
            Flt64.Zero
        );
        var snapshot = new BoundDependencySnapshot<Flt64>(new[] { input });
        symbol.RejectRange = true;
        snapshot.IsCurrent().Should().BeFalse();
        new BoundDependencySnapshot<Flt64>(new[] { input }).IsCurrent().Should().BeFalse();
    }

    private static RealVar BoundedReal() {
        var x = new RealVar("range-snapshot-real");
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-1), new Flt64(1)).Value);
        return x;
    }

    private static LinearPolynomial<Flt64> Polynomial(IVariableItem variable) => new(
        new[] { new LinearMonomial<Flt64>(Flt64.One, variable) },
        Flt64.Zero
    );

    private static IMathFunctionSymbol<Flt64> Create(int kind, LinearPolynomial<Flt64> input, bool explicitBigM) {
        Flt64? bigM = explicitBigM ? new Flt64(10) : null;
        return kind switch {
            0 => new AbsFunction<Flt64>(
                polynomial: input,
                converter: IIntoValue<Flt64>.Identity,
                bigM: bigM
            ),
            1 => new MaxFunction<Flt64>(
                polynomials: new[] { input },
                converter: IIntoValue<Flt64>.Identity,
                bigM: bigM
            ),
            2 => new MinFunction<Flt64>(
                polynomials: new[] { input },
                converter: IIntoValue<Flt64>.Identity,
                bigM: bigM
            ),
            3 => new MinMaxFunction<Flt64>(
                polynomials: new[] { input },
                converter: IIntoValue<Flt64>.Identity,
                bigM: bigM
            ),
            4 => new MaxMinFunction<Flt64>(
                polynomials: new[] { input },
                converter: IIntoValue<Flt64>.Identity,
                bigM: bigM
            ),
            5 => new BinaryzationFunction(input, bigM),
            6 => new InequalityFunction<Flt64>(
                lhs: input,
                rhs: Flt64.Zero,
                sign: Comparison.GT,
                converter: IIntoValue<Flt64>.Identity,
                bigM: bigM,
                strictBoundary: new Flt64(0.25)
            ),
            7 => new SatisfiedAmountFunction(
                inequalities: new[] {
                    new LinearInequality<Flt64>(
                        Lhs: input,
                        Rhs: new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), Flt64.Zero),
                        Comparison: Comparison.LE
                    ),
                },
                epsilon: new Flt64(0.25),
                bigM: bigM
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    private sealed class VariableAlias(IVariableItem inner) : IVariableItem {
        public MathUInt64 Identifier => inner.Identifier;
        public int Index => inner.Index;
        public VariableItemKey Key => inner.Key;
        public string Name => inner.Name;
        public string? DisplayName => inner.DisplayName;
        public IVariableTypeKind TypeKind => inner.TypeKind;
        public Bound<Flt64>? LowerBound => inner.LowerBound;
        public Bound<Flt64>? UpperBound => inner.UpperBound;
        public bool BelongsTo(IVariableItem item) => inner.BelongsTo(item);
        public bool BelongsTo(IVariableCombination combination) => inner.BelongsTo(combination);
    }

    private sealed class BoundsChangingVariable(IVariableItem inner, Action change) : IVariableItem {
        public bool Armed { get; set; }
        public MathUInt64 Identifier => inner.Identifier;
        public int Index => inner.Index;
        public VariableItemKey Key => inner.Key;
        public string Name => inner.Name;
        public string? DisplayName => inner.DisplayName;
        public IVariableTypeKind TypeKind => inner.TypeKind;
        public Bound<Flt64>? LowerBound {
            get {
                if (Armed) {
                    Armed = false;
                    change();
                }
                return inner.LowerBound;
            }
        }
        public Bound<Flt64>? UpperBound => inner.UpperBound;
        public bool BelongsTo(IVariableItem item) => inner.BelongsTo(item);
        public bool BelongsTo(IVariableCombination combination) => inner.BelongsTo(combination);
    }

    private sealed class BoundsChangingFunction(IMathFunctionSymbol<Flt64> inner, RealVar input) :
        IMathFunctionSymbol<Flt64>, IHasResultPolynomial<Flt64> {
        public string Name { get => inner.Name; set => inner.Name = value; }
        public string? DisplayName { get => inner.DisplayName; set => inner.DisplayName = value; }
        public IReadOnlyList<IVariableItem> HelperVariables => inner.HelperVariables;
        public IReadOnlyCollection<ISymbol>? InputSymbols => inner.InputSymbols;
        public LinearPolynomial<Flt64> ResultPolynomial => ((IHasResultPolynomial<Flt64>)inner).ResultPolynomial;
        public Flt64? Evaluate(IReadOnlyDictionary<ISymbol, Flt64> values) => inner.Evaluate(values);
        public Result<Success, ErrorCode, Error<ErrorCode>> RegisterAuxiliaryTokens(IAddableTokenCollection<Flt64> tokens) =>
            inner.RegisterAuxiliaryTokens(tokens);
        public Result<Success, ErrorCode, Error<ErrorCode>> RegisterConstraints(IAbstractLinearMechanismModel<Flt64> model) {
            Result<Success, ErrorCode, Error<ErrorCode>> result = inner.RegisterConstraints(model);
            input.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-2), new Flt64(2)).Value);
            return result;
        }
    }

    private sealed class ArmBoundsChangingFunction(IMathFunctionSymbol<Flt64> inner, BoundsChangingVariable trigger) :
        IMathFunctionSymbol<Flt64>, IHasResultPolynomial<Flt64> {
        public bool ArmOnRegister { get; set; }
        public string Name { get => inner.Name; set => inner.Name = value; }
        public string? DisplayName { get => inner.DisplayName; set => inner.DisplayName = value; }
        public IReadOnlyList<IVariableItem> HelperVariables => inner.HelperVariables;
        public IReadOnlyCollection<ISymbol>? InputSymbols => inner.InputSymbols;
        public LinearPolynomial<Flt64> ResultPolynomial => ((IHasResultPolynomial<Flt64>)inner).ResultPolynomial;
        public Flt64? Evaluate(IReadOnlyDictionary<ISymbol, Flt64> values) => inner.Evaluate(values);
        public Result<Success, ErrorCode, Error<ErrorCode>> RegisterAuxiliaryTokens(IAddableTokenCollection<Flt64> tokens) =>
            inner.RegisterAuxiliaryTokens(tokens);
        public Result<Success, ErrorCode, Error<ErrorCode>> RegisterConstraints(IAbstractLinearMechanismModel<Flt64> model) {
            Result<Success, ErrorCode, Error<ErrorCode>> result = inner.RegisterConstraints(model);
            trigger.Armed = ArmOnRegister;
            return result;
        }
    }

    private sealed class RangedSymbol<V> : IIntermediateSymbol<V>
        where V : struct, IRealNumber<V>, INumberField<V> {
        public RangedSymbol(V initial) {
            StoredRange = ExpressionRange<V>.Create(ValueRange<V>.Of(initial, initial).Value);
        }

        public ExpressionRange<V> StoredRange { get; }
        public bool RejectRange { get; set; }
        public ExpressionRange<V> Range => RejectRange
            ? throw new InvalidOperationException("测试边界读取失败 / Test bound-read failure.")
            : ExpressionRange<V>.Create(StoredRange.Range!.Copy());
        public string Name { get; set; } = "range-snapshot-symbol";
        public string? DisplayName { get; set; }
        public MathUInt64 Identifier { get; } = IdentifierGenerator.Gen();
        public int Index => 0;
        public IIntermediateSymbol? Parent => null;
        public Category Category => LinearCategory.Instance;
        public bool Cached => false;
        public IReadOnlySet<IIntermediateSymbol> Dependencies => new HashSet<IIntermediateSymbol>();
        public V? Prepare(IReadOnlyDictionary<ISymbol, V>? values, IAbstractTokenTable<V> tokenTable, IFlt64ValueConverter<V> converter) => null;
        public void PrepareAndCache(IReadOnlyDictionary<ISymbol, V>? values, IAbstractTokenTable<V> tokenTable, IFlt64ValueConverter<V> converter) { }
        public V? Evaluate(IAbstractTokenTable<V> tokenTable, IFlt64ValueConverter<V> converter, bool zeroIfNone = false) => null;
        public V? Evaluate(IReadOnlyList<V> results, IAbstractTokenTable<V> tokenTable, IFlt64ValueConverter<V> converter, bool zeroIfNone = false) => null;
        public V? Evaluate(IReadOnlyDictionary<ISymbol, V> values, IAbstractTokenTable<V>? tokenTable, IFlt64ValueConverter<V> converter, bool zeroIfNone = false) => null;
        public void Flush(bool force = false) { }
        public string ToRawString(MathUInt64 unfold) => Name;
    }
}
