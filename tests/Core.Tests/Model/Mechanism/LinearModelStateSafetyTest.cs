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
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Core.Model.Basic;
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

public class LinearModelStateSafetyTest {
    [Fact]
    public async Task DynamicRows_ShouldFreezeNestedExpressionsAndRejectConflictingRetries() {
        using var model = new LinearMetaModel<Flt64>();
        var x = new RealVar("state-x");
        var y = new RealVar("state-y");
        model.Add(x).IsOk.Should().BeTrue();
        model.Add(y).IsOk.Should().BeTrue();
        LinearExpressionSymbol inner = Expression(
            name: "inner",
            constant: 3,
            terms: [(2, x)]
        );
        LinearExpressionSymbol outer = Expression(
            name: "outer",
            constant: 5,
            terms: [(4, inner), (1, y)]
        );
        var relation = new LinearInequality<Flt64>(
            Lhs: Polynomial(constant: 1, terms: [(3, outer), (2, x)]),
            Rhs: Polynomial(constant: 4, terms: [(1, inner), (2, y)]),
            Comparison: Comparison.LE
        );
        var id = new ConstraintId("nested-row");

        model.State.RegisterRow(id, relation).IsOk.Should().BeTrue();
        model.State.RegisterRow(id, relation).IsOk.Should().BeTrue();
        var equivalent = new LinearInequality<Flt64>(
            Lhs: Polynomial(constant: 0, terms: [(24, x), (1, y)]),
            Rhs: Polynomial(-45),
            Comparison: Comparison.LE
        );
        model.State.RegisterRow(id, equivalent).IsOk.Should().BeTrue();
        model.Constraints.Should().ContainSingle();
        LinearModelSnapshot<Flt64> before = model.State.Refresh().Value;
        before.Rows.Single().Coefficients.Should().BeEquivalentTo(new Dictionary<IVariableItem, Flt64> {
            [x] = new Flt64(24),
            [y] = Flt64.One,
        });
        before.Rows.Single().Rhs.Should().Be(new Flt64(-45));
        outer.AsMutable().SetConstant(new Flt64(6));
        model.State.RegisterRow(id, relation).IsFailed.Should().BeTrue();
        model.State.Refresh().Value.Should().Be(before);
        using LinearMechanismModel<Flt64> built = (await LinearMechanismModel<Flt64>.InvokeAsync(model)).Value;
        ((IMechanismModel<Flt64>)built).Constraints.Single().Rhs.Should().Be(new Flt64(-45));

        model.Close();
        model.State.RegisterRow(id, relation)
            .Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.ApplicationStopped);
        model.State.Refresh()
            .Should().BeOfType<Failed<LinearModelSnapshot<Flt64>, ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.ApplicationStopped);
        model.State.RequireCurrent(before.Revision)
            .Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.ApplicationStopped);
    }

    [Fact]
    public async Task DynamicRows_ShouldRejectMissingCyclesAndUnregisteredFunctionsBeforeCancellation() {
        using var model = new LinearMetaModel<Flt64>();
        var x = new RealVar("input");
        var missing = new RealVar("missing");
        model.Add(x).IsOk.Should().BeTrue();
        LinearExpressionSymbol cycle = Expression("cycle", 0);
        cycle.AsMutable().AddMonomial(new LinearMonomial<Flt64>(Flt64.One, cycle));
        var function = new AbsFunction<Flt64>(
            polynomial: Polynomial(0, (1, x)),
            converter: IIntoValue<Flt64>.Identity,
            bigM: new Flt64(10)
        );
        var adapter = new LinearFunctionSymbolAdapter<Flt64>(function);
        model.Add(adapter).IsOk.Should().BeTrue();

        foreach (ISymbol symbol in new ISymbol[] { missing, cycle }) {
            var cancelled = new LinearInequality<Flt64>(
                Lhs: Polynomial(0, (1, symbol)),
                Rhs: Polynomial(0, (1, symbol)),
                Comparison: Comparison.LE
            );
            Try result = model.State.RegisterRow(new ConstraintId($"invalid-{symbol.Name}"), cancelled);
            result.IsFailed.Should().BeTrue();
        }
        model.Remove(adapter);
        LinearExpressionSymbol wrapper = Expression(
            name: "wrapper",
            constant: 0,
            terms: [(1, adapter)]
        );
        var unregistered = new LinearInequality<Flt64>(
            Lhs: Polynomial(0, (1, wrapper)),
            Rhs: Polynomial(0),
            Comparison: Comparison.LE
        );
        model.State.RegisterRow(new ConstraintId("unregistered"), unregistered).IsFailed.Should().BeTrue();
        using LinearMechanismModel<Flt64> built = (await LinearMechanismModel<Flt64>.InvokeAsync(model)).Value;
        built.AddConstraint(unregistered, from: (IIntermediateSymbol?)null)
            .Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>()
            .Which.Message.Should().Contain("Function symbol is not registered");
        model.Constraints.Should().BeEmpty();
        var zero = new LinearInequality<Flt64>(
            Lhs: Polynomial(constant: 0, terms: [(0, missing), (0, cycle), (0, adapter)]),
            Rhs: Polynomial(0),
            Comparison: Comparison.LE
        );
        model.State.RegisterRow(new ConstraintId("zero"), zero).IsOk.Should().BeTrue();
    }

    [Fact]
    public void DynamicRows_ShouldExpandDeepExpressionChainsWithoutRecursion() {
        using var model = new LinearMetaModel<Flt64>();
        var x = new RealVar("deep");
        model.Add(x).IsOk.Should().BeTrue();
        ISymbol current = x;
        for (int i = 0; i < 2048; ++i) {
            current = Expression(
                name: $"level-{i}",
                constant: 0,
                terms: [(1, current)]
            );
        }
        var relation = new LinearInequality<Flt64>(
            Lhs: Polynomial(0, (1, current)),
            Rhs: Polynomial(3),
            Comparison: Comparison.LE
        );
        model.State.RegisterRow(new ConstraintId("deep-row"), relation).IsOk.Should().BeTrue();
        model.State.Refresh().Value.Rows.Single().Coefficients[x].Should().Be(Flt64.One);
    }

    [Fact]
    public void DynamicRows_ShouldRejectIntegerAndDecimalOverflowWithoutAddingRows() {
        var x = new RealVar("overflow");
        using var integer = new LinearMetaModel<Integer>(converter: new IntegerConverter());
        integer.Add(x).IsOk.Should().BeTrue();
        var zero = new LinearPolynomial<Integer>([], Integer.Zero);
        LinearInequality<Integer>[] invalid = [
            new(
                Lhs: new LinearPolynomial<Integer>([
                    new LinearMonomial<Integer>(new Integer(long.MaxValue), x),
                    new LinearMonomial<Integer>(Integer.One, x),
                ], Integer.Zero),
                Rhs: zero,
                Comparison: Comparison.LE
            ),
            new(
                Lhs: zero,
                Rhs: new LinearPolynomial<Integer>([
                    new LinearMonomial<Integer>(new Integer(long.MinValue), x),
                ], Integer.Zero),
                Comparison: Comparison.LE
            ),
            new(
                Lhs: new LinearPolynomial<Integer>([], new Integer(long.MinValue)),
                Rhs: zero,
                Comparison: Comparison.LE
            ),
        ];
        foreach (LinearInequality<Integer> relation in invalid) {
            integer.State.RegisterRow(new ConstraintId("integer-overflow"), relation).IsFailed.Should().BeTrue();
        }
        integer.Constraints.Should().BeEmpty();
        integer.State.RegisterRow(
            new ConstraintId("integer-overflow"),
            new LinearInequality<Integer>(
                Lhs: zero,
                Rhs: zero,
                Comparison: Comparison.EQ
            )
        ).IsOk.Should().BeTrue();

        IFlt64ValueConverter<FltX> converter = FltX.Zero;
        using var decimalModel = new LinearMetaModel<FltX>(converter: converter);
        decimalModel.Add(x).IsOk.Should().BeTrue();
        var decimalRelation = new LinearInequality<FltX>(
            Lhs: new LinearPolynomial<FltX>([
                new LinearMonomial<FltX>(new FltX(decimal.MaxValue), x),
                new LinearMonomial<FltX>(new FltX(1m), x),
            ], FltX.Zero),
            Rhs: new LinearPolynomial<FltX>([], FltX.Zero),
            Comparison: Comparison.LE
        );
        decimalModel.State.RegisterRow(new ConstraintId("decimal-overflow"), decimalRelation)
            .IsFailed.Should().BeTrue();
        decimalModel.Constraints.Should().BeEmpty();
    }

    [Fact]
    public void Refresh_ShouldRejectObjectiveOverflowWithoutAdvancingRevision() {
        using var model = new LinearMetaModel<Integer>(converter: new IntegerConverter());
        var x = new RealVar("objective-overflow");
        model.Add(x).IsOk.Should().BeTrue();
        var terms = new List<LinearMonomial<Integer>> {
            new(new Integer(long.MaxValue), x),
        };
        model.AddObject(
            category: ObjectCategory.Minimum,
            polynomial: new LinearPolynomial<Integer>(terms, Integer.Zero),
            name: "objective",
            displayName: null
        ).IsOk.Should().BeTrue();
        LinearModelSnapshot<Integer> initial = model.State.Refresh().Value;
        int callbacks = model.Tokens.Find(x)!.RefreshCallbacks.Count;
        terms.Add(new LinearMonomial<Integer>(Integer.One, x));
        model.State.Refresh().IsFailed.Should().BeTrue();
        model.State.RequireCurrent(initial.Revision).IsFailed.Should().BeTrue();
        model.Tokens.Find(x)!.RefreshCallbacks.Should().HaveCount(callbacks);
        terms[1] = new LinearMonomial<Integer>(new Integer(-1), x);
        LinearModelSnapshot<Integer> recovered = model.State.Refresh().Value;
        recovered.Revision.Sequence.Should().Be(initial.Revision.Sequence + 1);
        recovered.Objective[x].Should().Be(new Integer(long.MaxValue - 1));

        using var negative = new LinearMetaModel<Integer>(converter: new IntegerConverter());
        negative.AddObject(
            category: ObjectCategory.Maximum,
            polynomial: new LinearPolynomial<Integer>([], new Integer(long.MinValue)),
            name: "negative-overflow",
            displayName: null
        ).IsOk.Should().BeTrue();
        negative.State.Refresh().IsFailed.Should().BeTrue();

        using var negativeCoefficient = new LinearMetaModel<Integer>(converter: new IntegerConverter());
        negativeCoefficient.Add(x).IsOk.Should().BeTrue();
        negativeCoefficient.AddObject(
            category: ObjectCategory.Maximum,
            polynomial: new LinearPolynomial<Integer>([
                new LinearMonomial<Integer>(new Integer(long.MinValue), x),
            ], Integer.Zero),
            name: "negative-coefficient-overflow",
            displayName: null
        ).IsOk.Should().BeTrue();
        negativeCoefficient.State.Refresh().IsFailed.Should().BeTrue();

        IFlt64ValueConverter<FltX> converter = FltX.Zero;
        using var decimalModel = new LinearMetaModel<FltX>(converter: converter);
        decimalModel.AddObject(
            category: ObjectCategory.Minimum,
            polynomial: new LinearPolynomial<FltX>([], new FltX(decimal.MaxValue)),
            name: "maximum",
            displayName: null
        ).IsOk.Should().BeTrue();
        decimalModel.AddObject(
            category: ObjectCategory.Minimum,
            polynomial: new LinearPolynomial<FltX>([], new FltX(1m)),
            name: "extra",
            displayName: null
        ).IsOk.Should().BeTrue();
        decimalModel.State.Refresh().IsFailed.Should().BeTrue();

        using var floating = new LinearMetaModel<Flt64>();
        floating.Add(x).IsOk.Should().BeTrue();
        floating.AddObject(
            category: ObjectCategory.Minimum,
            polynomial: Polynomial(constant: 0, terms: [(double.MaxValue, x), (double.MaxValue, x)]),
            name: "floating-overflow",
            displayName: null
        ).IsOk.Should().BeTrue();
        floating.State.Refresh().IsFailed.Should().BeTrue();
    }

    [Fact]
    public void DecimalDynamicRows_ShouldRetainNativePrecision() {
        IFlt64ValueConverter<FltX> converter = FltX.Zero;
        using var model = new LinearMetaModel<FltX>(converter: converter);
        var x = new RealVar("decimal");
        model.Add(x).IsOk.Should().BeTrue();
        var relation = new LinearInequality<FltX>(
            Lhs: new LinearPolynomial<FltX>([
                new LinearMonomial<FltX>(new FltX(9007199254740993.1m), x),
            ], new FltX(1.2m)),
            Rhs: new LinearPolynomial<FltX>([], new FltX(0.1m)),
            Comparison: Comparison.GE
        );
        model.State.RegisterRow(new ConstraintId("decimal"), relation).IsOk.Should().BeTrue();
        LinearModelRow<FltX> row = model.State.Refresh().Value.Rows.Single();
        row.Coefficients[x].Should().Be(new FltX(9007199254740993.1m));
        row.Rhs.Should().Be(new FltX(-1.1m));
    }

    [Fact]
    public void RationalSnapshotComparison_ShouldDetectChangesInEveryStoredValue() {
        var x = new RealVar("rational-snapshot");
        var large = new RtnX(double.MaxValue, double.MaxValue);
        var half = new RtnX(double.MaxValue / 2, double.MaxValue);

        LinearModelSnapshot<RtnX> Snapshot(int field, RtnX value) {
            RtnX At(int index) => field == index ? value : RtnX.One;
            return new LinearModelSnapshot<RtnX>(
                revision: new LinearModelRevision(1, 1),
                variables: [new LinearVariableState<RtnX>(x, At(0), At(1))],
                rows: [new LinearModelRow<RtnX>(
                    id: "row",
                    sourceName: "row",
                    coefficients: new Dictionary<IVariableItem, RtnX> { [x] = At(2) },
                    rhs: At(3),
                    comparison: Comparison.LE
                )],
                objective: new Dictionary<IVariableItem, RtnX> { [x] = At(4) },
                objectiveConstant: At(5),
                objectCategory: ObjectCategory.Minimum
            );
        }

        for (int i = 0; i < 6; ++i) {
            LinearModelSnapshot<RtnX> before = Snapshot(i, large);
            before.SameStructure(Snapshot(i, large)).Should().BeTrue();
            before.SameStructure(Snapshot(i, half)).Should().BeFalse();
        }
        Snapshot(2, new RtnX(1, 1)).SameStructure(Snapshot(2, new RtnX(2, 2))).Should().BeFalse();
    }

    [Fact]
    public async Task RefreshAndCurrentValidation_ShouldPreserveFatalErrors() {
        using var model = new LinearMetaModel<Flt64>();
        LinearModelRevision revision = model.State.Refresh().Value.Revision;
        var adapter = new LinearFunctionSymbolAdapter<Flt64>(new FatalFunction());
        model.Add(adapter).IsOk.Should().BeTrue();
        Result<LinearModelSnapshot<Flt64>, ErrorCode, Error<ErrorCode>> refreshed = await model.State.RefreshAsync();
        refreshed.Should().BeOfType<Fatal<LinearModelSnapshot<Flt64>, ErrorCode, Error<ErrorCode>>>()
            .Which.Errors.Should().HaveCount(2);
        (await model.State.RequireCurrentAsync(revision))
            .Should().BeOfType<Fatal<Success, ErrorCode, Error<ErrorCode>>>()
            .Which.Errors.Should().HaveCount(2);
    }

    private static LinearExpressionSymbol Expression(
        string name,
        double constant,
        params (double Coefficient, ISymbol Symbol)[] terms) =>
        new(
            polynomial: new MutableLinearPolynomial<Flt64>(
                terms.Select(term => new LinearMonomial<Flt64>(new Flt64(term.Coefficient), term.Symbol)),
                new Flt64(constant)),
            name: name
        );

    private static LinearPolynomial<Flt64> Polynomial(double constant, params (double Coefficient, ISymbol Symbol)[] terms) =>
        new(terms.Select(term => new LinearMonomial<Flt64>(new Flt64(term.Coefficient), term.Symbol)).ToList(), new Flt64(constant));

    private sealed class IntegerConverter : IFlt64ValueConverter<Integer> {
        public Integer Zero => Integer.Zero;
        public Integer One => Integer.One;
        public Integer IntoValue(Flt64 value) => value.ToInt64();
        public Flt64 FromValue(Integer value) => value.ToFlt64();
    }

    private sealed class FatalFunction : IMathFunctionSymbol<Flt64>, IHasResultPolynomial<Flt64> {
        public string Name { get; set; } = "fatal";
        public string? DisplayName { get; set; }
        public IReadOnlyList<IVariableItem> HelperVariables => Array.Empty<IVariableItem>();
        public IReadOnlyCollection<ISymbol> InputSymbols => Array.Empty<ISymbol>();
        public LinearPolynomial<Flt64> ResultPolynomial => new([], Flt64.Zero);
        public Flt64? Evaluate(IReadOnlyDictionary<ISymbol, Flt64> values) => null;
        public Try RegisterAuxiliaryTokens(IAddableTokenCollection<Flt64> tokens) => Results.OkInstance;
        public Try RegisterConstraints(IAbstractLinearMechanismModel<Flt64> model) => new Fatal<Success, ErrorCode, Error<ErrorCode>>(
            new Err<ErrorCode>(ErrorCode.IllegalArgument, "first"),
            new Err<ErrorCode>(ErrorCode.ApplicationFailed, "second")
        );
    }
}
