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
using Fuookami.Ospf.Core.Tests.Symbol.Function;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
using Integer = Fuookami.Ospf.Math.Algebra.Number.Int64;
using SymbolFlatten = Fuookami.Ospf.Core.Symbol.Flatten;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
namespace Fuookami.Ospf.Core.Tests.Model.Mechanism;

[CollectionDefinition("LinearSymbolFlatten", DisableParallelization = true)]
public sealed class LinearSymbolFlattenCollection { }

[Collection("LinearSymbolFlatten")]
public class LinearSymbolFlattenTest {
    [Fact]
    public async Task NestedExpressions_ShouldReachRowsObjectivesAndMatrixAsSnapshots() {
        using var parent = new LinearMetaModel<Flt64>();
        var x = new RealVar("nested-x");
        var y = new RealVar("nested-y");
        parent.Add(x).IsOk.Should().BeTrue();
        parent.Add(y).IsOk.Should().BeTrue();
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
        parent.AddConstraint(relation, null).IsOk.Should().BeTrue();
        parent.AddObject(
            category: ObjectCategory.Minimum,
            polynomial: Polynomial(constant: 7, terms: [(2, outer), (1, inner)]),
            name: "nested-objective",
            displayName: null
        ).IsOk.Should().BeTrue();

        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built =
            await LinearMechanismModel<Flt64>.InvokeAsync(parent);
        built.IsOk.Should().BeTrue();
        using LinearMechanismModel<Flt64> model = built.Value;
        IConstraint<Flt64, LinearCategory> row = ((IMechanismModel<Flt64>)model).Constraints.Single();
        row.Lhs.Cast<ILinearCell<Flt64>>().ToDictionary(cell => cell.Token.Variable, cell => cell.Coefficient)
            .Should().BeEquivalentTo(new Dictionary<IVariableItem, Flt64> { [x] = new(24), [y] = Flt64.One });
        row.Rhs.Should().Be(new Flt64(-45));
        LinearSubObject<Flt64> objective = model.ObjectFunction.GetSubObjects<Flt64>()
            .OfType<LinearSubObject<Flt64>>().Single();
        objective.Constant.Should().Be(new Flt64(44));
        objective.Evaluate(new[] { new Flt64(2), new Flt64(3) }).Should().Be(new Flt64(86));

        using LinearTriadModel matrix = model.ToLinearTriadModel().Value;
        matrix.Objective.Constant.Should().Be(new Flt64(44));
        matrix.Objective.Cells.Select(cell => cell.Coefficient).Should().Equal(new Flt64(18), new Flt64(2));
        matrix.Constraints.Lhs.Single().Select(cell => cell.Coefficient).Should().Equal(new Flt64(24), Flt64.One);
        matrix.Constraints.Rhs.Single().Should().Be(new Flt64(-45));

        LinearModelSnapshot<Flt64> before = parent.State.Refresh().Value;
        outer.AsMutable().SetConstant(new Flt64(6));
        LinearModelSnapshot<Flt64> after = parent.State.Refresh().Value;
        after.Revision.Should().NotBe(before.Revision);
        after.ObjectiveConstant.Should().Be(new Flt64(46));
        objective.Constant.Should().Be(new Flt64(44));
        row.Rhs.Should().Be(new Flt64(-45));
    }

    [Fact]
    public async Task FunctionAdapter_ShouldExpandItsStructuralResultAndRegisterItsRows() {
        using var parent = new LinearMetaModel<Flt64>();
        var x = new RealVar("absolute-input");
        parent.Add(x).IsOk.Should().BeTrue();
        LinearExpressionSymbol inner = Expression(
            name: "absolute-inner",
            constant: 1,
            terms: [(2, x)]
        );
        var function = new AbsFunction<Flt64>(
            polynomial: Polynomial(0, (1, inner)),
            converter: IIntoValue<Flt64>.Identity,
            bigM: new Flt64(10)
        );
        var adapter = new LinearFunctionSymbolAdapter<Flt64>(function);
        parent.Add(adapter).IsOk.Should().BeTrue();
        parent.AddConstraint(
            new LinearInequality<Flt64>(
                Lhs: Polynomial(0, (1, adapter)),
                Rhs: Polynomial(2),
                Comparison: Comparison.LE
            ),
            null
        ).IsOk.Should().BeTrue();
        parent.AddObject(
            category: ObjectCategory.Minimum,
            flattenData: new SymbolFlatten.LinearFlattenData<Flt64>(
                new[] { new LinearMonomial<Flt64>(new Flt64(2), adapter) }, new Flt64(3)),
            name: "absolute-objective",
            displayName: null
        ).IsOk.Should().BeTrue();

        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built =
            await LinearMechanismModel<Flt64>.InvokeAsync(parent);
        built.IsOk.Should().BeTrue();
        using LinearMechanismModel<Flt64> model = built.Value;
        var values = new Dictionary<IVariableItem, Flt64> {
            [x] = new Flt64(-1),
            [function.ResultVar] = Flt64.One,
            [function.PositiveVar] = Flt64.Zero,
            [function.NegativeVar] = Flt64.One,
            [function.SignVar] = Flt64.Zero,
        };
        IReadOnlyList<Flt64> solution = model.Tokens.TokensInSolver.Select(token => values[token.Variable]).ToList();
        IReadOnlyList<IConstraint<Flt64, LinearCategory>> rows = ((IMechanismModel<Flt64>)model).Constraints;
        rows.Should().HaveCount(5).And.OnlyContain(row => row.IsTrue(solution) == true);
        IConstraint<Flt64, LinearCategory> decomposition = rows.Single(row => row.Name == "abs_abs_decompose");
        decomposition.Lhs.Cast<ILinearCell<Flt64>>().ToDictionary(cell => cell.Token.Variable, cell => cell.Coefficient)
            .Should().BeEquivalentTo(new Dictionary<IVariableItem, Flt64> {
                [x] = new Flt64(2),
                [function.PositiveVar] = new Flt64(-1),
                [function.NegativeVar] = Flt64.One,
            });
        decomposition.Rhs.Should().Be(new Flt64(-1));
        values[function.ResultVar] = new Flt64(2);
        IReadOnlyList<Flt64> invalid = model.Tokens.TokensInSolver.Select(token => values[token.Variable]).ToList();
        rows.Should().Contain(row => row.IsTrue(invalid) == false);
        LinearSubObject<Flt64> objective = model.ObjectFunction.GetSubObjects<Flt64>()
            .OfType<LinearSubObject<Flt64>>().Single();
        objective.LinearTerms().Should().ContainSingle().Which.Symbol.Should().BeSameAs(function.ResultVar);
        objective.Evaluate(solution).Should().Be(new Flt64(5));
        using LinearTriadModel matrix = model.ToLinearTriadModel().Value;
        matrix.Objective.Cells.Should().ContainSingle().Which.Coefficient.Should().Be(new Flt64(2));
    }

    [Fact]
    public async Task CyclesMissingVariablesAndUnknownResults_ShouldFailWithoutLeakingSnapshots() {
        var x = new RealVar("cycle-x");
        var missing = new RealVar("missing-x");
        LinearExpressionSymbol cycle = Expression("cycle", 1);
        cycle.AsMutable().AddMonomial(new LinearMonomial<Flt64>(Flt64.One, cycle));
        LinearExpressionSymbol absent = Expression(
            name: "absent",
            constant: 0,
            terms: [(1, missing)]
        );
        var unknown = new LinearFunctionSymbolAdapter<Flt64>(new UnknownFunctionStub());
        foreach (ISymbol invalid in new ISymbol[] { cycle, absent, unknown, new TestSymbol("unknown") }) {
            using var parent = new LinearMetaModel<Flt64>();
            parent.Add(x).IsOk.Should().BeTrue();
            parent.AddObject(
                category: ObjectCategory.Minimum,
                polynomial: Polynomial(0, (1, invalid)),
                name: "invalid",
                displayName: null
            ).IsOk.Should().BeTrue();
            int callbacks = parent.Tokens.Find(x)!.RefreshCallbacks.Count;
            (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).IsFailed.Should().BeTrue();
            parent.Tokens.Find(x)!.RefreshCallbacks.Should().HaveCount(callbacks);
        }

        using var invalidRows = new LinearMetaModel<Flt64>();
        invalidRows.Add(x).IsOk.Should().BeTrue();
        using LinearMechanismModel<Flt64> rowModel = (await LinearMechanismModel<Flt64>.InvokeAsync(invalidRows)).Value;
        var cyclicRow = new LinearInequality<Flt64>(
            Lhs: Polynomial(0, (1, cycle)),
            Rhs: Polynomial(0),
            Comparison: Comparison.LE
        );
        rowModel.AddConstraint(cyclicRow, from: (IIntermediateSymbol?)null).IsFailed.Should().BeTrue();
        ((IMechanismModel<Flt64>)rowModel).Constraints.Should().BeEmpty();
        var nullRow = new LinearInequality<Flt64>(
            Lhs: new LinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(Flt64.One, null!) }, Flt64.Zero),
            Rhs: Polynomial(0),
            Comparison: Comparison.LE
        );
        rowModel.AddConstraint(nullRow, from: (IIntermediateSymbol?)null).IsFailed.Should().BeTrue();
        ((IMechanismModel<Flt64>)rowModel).Constraints.Should().BeEmpty();
        invalidRows.AddConstraint(cyclicRow, null).IsOk.Should().BeTrue();
        int rowCallbacks = invalidRows.Tokens.Find(x)!.RefreshCallbacks.Count;
        (await LinearMechanismModel<Flt64>.InvokeAsync(invalidRows)).IsFailed.Should().BeTrue();
        invalidRows.Tokens.Find(x)!.RefreshCallbacks.Should().HaveCount(rowCallbacks);

        using var valid = new LinearMetaModel<Flt64>();
        valid.AddObject(
            category: ObjectCategory.Minimum,
            polynomial: Polynomial(constant: 5, terms: [(0, cycle), (0, unknown)]),
            name: "zero",
            displayName: null
        ).IsOk.Should().BeTrue();
        using LinearMechanismModel<Flt64> zero = (await LinearMechanismModel<Flt64>.InvokeAsync(valid)).Value;
        zero.ObjectFunction.GetSubObjects<Flt64>().OfType<LinearSubObject<Flt64>>().Single()
            .Constant.Should().Be(new Flt64(5));
    }

    [Fact]
    public async Task DeepChainAndRepeatedBranches_ShouldNotDependOnCallStackOrTreatSharingAsCycle() {
        var x = new RealVar("deep-x");
        ISymbol current = x;
        for (int i = 0; i < 2048; ++i) {
            current = Expression(
                name: $"level-{i}",
                constant: 0,
                terms: [(1, current)]
            );
        }
        LinearExpressionSymbol shared = Expression(
            name: "shared",
            constant: 1,
            terms: [(1, current), (1, current)]
        );
        using var parent = new LinearMetaModel<Flt64>();
        parent.Add(x).IsOk.Should().BeTrue();
        parent.AddConstraint(
            new LinearInequality<Flt64>(
                Lhs: Polynomial(0, (1, shared)),
                Rhs: Polynomial(7),
                Comparison: Comparison.LE
            ),
            null
        ).IsOk.Should().BeTrue();
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built =
            await LinearMechanismModel<Flt64>.InvokeAsync(parent);
        built.IsOk.Should().BeTrue();
        using LinearMechanismModel<Flt64> model = built.Value;
        IConstraint<Flt64, LinearCategory> row = ((IMechanismModel<Flt64>)model).Constraints.Single();
        row.Lhs.Cast<ILinearCell<Flt64>>().Should().ContainSingle().Which.Coefficient.Should().Be(new Flt64(2));
        row.Rhs.Should().Be(new Flt64(6));
    }

    [Fact]
    public async Task UnregisteredFunction_ShouldNotBecomeAnUnconstrainedHelperVariable() {
        var x = new RealVar("unregistered-function-input");
        var function = new AbsFunction<Flt64>(
            polynomial: Polynomial(0, (1, x)),
            converter: IIntoValue<Flt64>.Identity,
            bigM: new Flt64(10)
        );
        var adapter = new LinearFunctionSymbolAdapter<Flt64>(function);
        using var parent = new LinearMetaModel<Flt64>();
        parent.Add(x).IsOk.Should().BeTrue();
        parent.Add(function.ResultVar).IsOk.Should().BeTrue();
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        var relation = new LinearInequality<Flt64>(
            Lhs: Polynomial(0, (1, adapter)),
            Rhs: Polynomial(1),
            Comparison: Comparison.LE
        );
        model.AddConstraint(relation, from: (IIntermediateSymbol?)null).IsFailed.Should().BeTrue();
        ((IMechanismModel<Flt64>)model).Constraints.Should().BeEmpty();
        parent.AddConstraint(relation, null).IsOk.Should().BeTrue();
        int callbacks = parent.Tokens.Find(x)!.RefreshCallbacks.Count;
        (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).IsFailed.Should().BeTrue();
        parent.Tokens.Find(x)!.RefreshCallbacks.Should().HaveCount(callbacks);

        using var objectiveParent = new LinearMetaModel<Flt64>();
        objectiveParent.Add(function.ResultVar).IsOk.Should().BeTrue();
        objectiveParent.AddObject(
            category: ObjectCategory.Minimum,
            polynomial: Polynomial(0, (1, adapter)),
            name: "unregistered-function",
            displayName: null
        ).IsOk.Should().BeTrue();
        (await LinearMechanismModel<Flt64>.InvokeAsync(objectiveParent)).IsFailed.Should().BeTrue();
    }

    [Fact]
    public void ConstraintInput_ShouldExpandExpressionsButNotDiscardFunctionRegistration() {
        var x = new RealVar("constraint-input-x");
        LinearExpressionSymbol inner = Expression(
            name: "constraint-input-inner",
            constant: 3,
            terms: [(2, x)]
        );
        ValueRange<Flt64> range = ValueRange<Flt64>.Of(new Flt64(-10), new Flt64(10)).Value;
        var relation = new LinearInequality<Flt64>(
            Lhs: Polynomial(1, (4, inner)),
            Rhs: Polynomial(6),
            Comparison: Comparison.LE
        );
        Result<LinearConstraintInput<Flt64>, ErrorCode, Error<ErrorCode>> input = LinearConstraintInput<Flt64>.From(
            relation: relation,
            lhsRange: range,
            rhsConstant: new Flt64(6)
        );
        input.IsOk.Should().BeTrue();
        input.Value.FlattenData.Monomials.Should().ContainSingle().Which.Coefficient.Should().Be(new Flt64(8));
        input.Value.FlattenData.Constant.Should().Be(new Flt64(7));

        var function = new AbsFunction<Flt64>(
            polynomial: Polynomial(0, (1, x)),
            converter: IIntoValue<Flt64>.Identity,
            bigM: new Flt64(10)
        );
        var adapter = new LinearFunctionSymbolAdapter<Flt64>(function);
        LinearExpressionSymbol wrapper = Expression(
            name: "constraint-input-wrapper",
            constant: 0,
            terms: [(1, adapter)]
        );
        LinearConstraintInput<Flt64>.From(
            relation: new LinearInequality<Flt64>(
                Lhs: Polynomial(0, (1, wrapper)),
                Rhs: Polynomial(0),
                Comparison: Comparison.LE
            ),
            lhsRange: range,
            rhsConstant: Flt64.Zero
        ).IsFailed.Should().BeTrue();
    }

    [Fact]
    public async Task LinearConstraintsOnQuadraticMechanism_ShouldUseSharedExpansion() {
        var x = new RealVar("quadratic-linear-input");
        LinearExpressionSymbol inner = Expression(
            name: "quadratic-linear-inner",
            constant: 3,
            terms: [(2, x)]
        );
        using var parent = new QuadraticMetaModel<Flt64>();
        parent.Add(x).IsOk.Should().BeTrue();
        using QuadraticMechanismModel<Flt64> model = (await QuadraticMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        model.AddConstraint(
            relation: new LinearInequality<Flt64>(
                Lhs: Polynomial(1, (4, inner)),
                Rhs: Polynomial(6),
                Comparison: Comparison.LE
            ),
            name: "promoted",
            from: (IIntermediateSymbol?)null
        ).IsOk.Should().BeTrue();
        IConstraint<Flt64, QuadraticCategory> row = ((IAbstractQuadraticMechanismModel<Flt64>)model).Constraints.Single();
        row.Lhs.Cast<IQuadraticCell<Flt64>>().Should().ContainSingle().Which.Coefficient.Should().Be(new Flt64(8));
        row.Rhs.Should().Be(new Flt64(-7));
    }

    [Fact]
    public void GenericExpansion_ShouldRetainDecimalAndRationalCoefficients() {
        var x = new RealVar("generic-expansion");
        LinearFunctionSymbolAdapter<FltX> decimalAdapter = Structural(new LinearPolynomial<FltX>(
            new[] { new LinearMonomial<FltX>(new FltX(9007199254740993.1m), x) },
            new FltX(0.2m)
        ));
        Result<SymbolFlatten.LinearFlattenData<FltX>, ErrorCode, Error<ErrorCode>> decimalResult =
            new SymbolFlatten.LinearFlattenData<FltX>(
                new[] { new LinearMonomial<FltX>(new FltX(3m), decimalAdapter) }, new FltX(0.1m)
            ).ExpandIntermediateSymbols();
        decimalResult.IsOk.Should().BeTrue();
        decimalResult.Value.Monomials.Single().Coefficient.Eq(new FltX(27021597764222979.3m)).Should().BeTrue();
        decimalResult.Value.Constant.Eq(new FltX(0.7m)).Should().BeTrue();

        LinearFunctionSymbolAdapter<RtnX> rationalAdapter = Structural(new LinearPolynomial<RtnX>(
            new[] { new LinearMonomial<RtnX>(new RtnX(1, 3), x) }, new RtnX(1, 4)
        ));
        Result<SymbolFlatten.LinearFlattenData<RtnX>, ErrorCode, Error<ErrorCode>> rationalResult =
            new SymbolFlatten.LinearFlattenData<RtnX>(
                new[] { new LinearMonomial<RtnX>(new RtnX(3, 2), rationalAdapter) }, new RtnX(1, 8)
            ).ExpandIntermediateSymbols();
        rationalResult.IsOk.Should().BeTrue();
        rationalResult.Value.Monomials.Single().Coefficient.Eq(new RtnX(1, 2)).Should().BeTrue();
        rationalResult.Value.Constant.Eq(new RtnX(1, 2)).Should().BeTrue();
    }

    [Fact]
    public void Expansion_ShouldAccumulateConstantsWithinEachNestedSymbolInSourceOrder() {
        LinearExpressionSymbol negative = Expression("negative", -1e16);
        LinearExpressionSymbol one = Expression("one", 1);
        LinearExpressionSymbol nested = Expression(
            name: "nested-order",
            constant: 1e16,
            terms: [(1, negative), (1, one)]
        );
        Result<SymbolFlatten.LinearFlattenData<Flt64>, ErrorCode, Error<ErrorCode>> result =
            new SymbolFlatten.LinearFlattenData<Flt64>(
                new[] { new LinearMonomial<Flt64>(Flt64.One, nested) }, new Flt64(3)
            ).ExpandIntermediateSymbols();
        result.IsOk.Should().BeTrue();
        result.Value.Constant.Should().Be(new Flt64(4));
    }

    [Fact]
    public void ExpansionAndNormalization_ShouldRejectOverflowAndUnderflow() {
        var x = new RealVar("overflow-expansion");
        foreach ((long scale, long coefficient) in new[] {
            (long.MaxValue, 2L),
            (long.MinValue, -1L),
            (4294967296L, 4294967296L),
        }) {
            LinearFunctionSymbolAdapter<Integer> adapter = Structural(new LinearPolynomial<Integer>(
                new[] { new LinearMonomial<Integer>(new Integer(coefficient), x) }, Integer.Zero
            ));
            new SymbolFlatten.LinearFlattenData<Integer>(
                new[] { new LinearMonomial<Integer>(new Integer(scale), adapter) }, Integer.Zero
            ).ExpandIntermediateSymbols().IsFailed.Should().BeTrue();
        }
        LinearFunctionSymbolAdapter<Integer> constantAdapter = Structural(new LinearPolynomial<Integer>(Array.Empty<LinearMonomial<Integer>>(), Integer.One));
        new SymbolFlatten.LinearFlattenData<Integer>(
            new[] { new LinearMonomial<Integer>(Integer.One, constantAdapter) }, new Integer(long.MaxValue)
        ).ExpandIntermediateSymbols().IsFailed.Should().BeTrue();
        var repeated = new LinearPolynomial<Integer>(
            new[] { new LinearMonomial<Integer>(new Integer(long.MaxValue), x), new LinearMonomial<Integer>(Integer.One, x) },
            Integer.Zero
        );
        var zero = new LinearPolynomial<Integer>(Array.Empty<LinearMonomial<Integer>>(), Integer.Zero);
        new LinearInequality<Integer>(
            Lhs: repeated,
            Rhs: zero,
            Comparison: Comparison.LE
        ).ToLinearFlattenData().IsFailed.Should().BeTrue();
        new LinearInequality<Integer>(
            Lhs: new LinearPolynomial<Integer>(Array.Empty<LinearMonomial<Integer>>(), new Integer(long.MinValue)),
            Rhs: zero,
            Comparison: Comparison.LE
        ).ToLinearFlattenData().IsFailed.Should().BeTrue();

        foreach ((double scale, double coefficient) in new[] { (double.MaxValue, 2.0), (double.Epsilon, 0.5) }) {
            LinearFunctionSymbolAdapter<Flt64> adapter = Structural(new LinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(new Flt64(coefficient), x) }, Flt64.Zero
            ));
            new SymbolFlatten.LinearFlattenData<Flt64>(
                new[] { new LinearMonomial<Flt64>(new Flt64(scale), adapter) }, Flt64.Zero
            ).ExpandIntermediateSymbols().IsFailed.Should().BeTrue();
        }
    }

    private static LinearExpressionSymbol Expression(
        string name,
        double constant,
        params (double Coefficient, ISymbol Symbol)[] terms) =>
        new(
            polynomial: new MutableLinearPolynomial<Flt64>(
                terms.Select(term => new LinearMonomial<Flt64>(new Flt64(term.Coefficient), term.Symbol)),
                new Flt64(constant)
            ),
            name: name
        );

    private static LinearPolynomial<Flt64> Polynomial(double constant, params (double Coefficient, ISymbol Symbol)[] terms) =>
        new(terms.Select(term => new LinearMonomial<Flt64>(new Flt64(term.Coefficient), term.Symbol)).ToList(), new Flt64(constant));

    private static LinearFunctionSymbolAdapter<V> Structural<V>(LinearPolynomial<V> polynomial)
        where V : struct, IRealNumber<V>, INumberField<V> => new(new StructuralFunction<V>(polynomial));

    private sealed class StructuralFunction<V> : IMathFunctionSymbol<V>, IHasResultPolynomial<V>
        where V : struct, IRealNumber<V>, INumberField<V> {
        public string Name { get; set; } = "structural";
        public string? DisplayName { get; set; }
        public IReadOnlyList<IVariableItem> HelperVariables => Array.Empty<IVariableItem>();
        public LinearPolynomial<V> ResultPolynomial { get; }

        public StructuralFunction(LinearPolynomial<V> polynomial) => ResultPolynomial = polynomial;
        public V? Evaluate(IReadOnlyDictionary<ISymbol, V> values) => null;
        public Try RegisterAuxiliaryTokens(IAddableTokenCollection<V> tokens) => Results.Ok(Results.SuccessInstance);
        public Try RegisterConstraints(IAbstractLinearMechanismModel<V> model) => Results.Ok(Results.SuccessInstance);
    }

    private sealed record TestSymbol(string Name) : ISymbol {
        public string? DisplayName => null;
    }
}
