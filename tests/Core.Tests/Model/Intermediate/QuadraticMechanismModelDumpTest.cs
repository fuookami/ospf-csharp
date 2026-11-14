#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Algebra.ValueRange;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
using SymbolFlatten = Fuookami.Ospf.Core.Symbol.Flatten;
namespace Fuookami.Ospf.Core.Tests.Model.Intermediate;

public class QuadraticMechanismModelDumpTest {
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task RemovedOrReplacedToken_ShouldKeepCompactMatrixAndInitialValueOrder(bool manual, bool replace) {
        using var parent = new QuadraticMetaModel<Flt64>(
            configuration: new MetaModelConfiguration(ManualTokenAddition: manual)
        );
        var x = new RealVar("quadratic-matrix-first");
        var removed = new RealVar("quadratic-matrix-removed");
        var y = new RealVar("quadratic-matrix-second");
        var z = new RealVar("quadratic-matrix-third");
        parent.Add(new IVariableItem[] { x, removed, y }).IsOk.Should().BeTrue();
        parent.Remove(removed);
        parent.Add(z).IsOk.Should().BeTrue();
        if (replace) {
            parent.Tokens.Find(y)!.SetResult(new Flt64(9));
            parent.Add(y).IsOk.Should().BeTrue();
            parent.Tokens.Find(y)!.Result.Should().BeNull();
        }
        parent.Tokens.SetSolverSolution(replace
            ? new[] { new Flt64(2), new Flt64(4), new Flt64(3) }
            : new[] { new Flt64(2), new Flt64(3), new Flt64(4) });
        parent.AddObject(
            category: ObjectCategory.Minimum,
            flattenData: new SymbolFlatten.QuadraticFlattenData<Flt64>(new[] {
                new QuadraticMonomial<Flt64>(new Flt64(2), x),
                new QuadraticMonomial<Flt64>(
                    Coefficient: new Flt64(3),
                    Symbol1: y,
                    Symbol2: z
                ),
            }, Flt64.Zero),
            name: "quadratic-ordered-objective",
            displayName: null
        ).IsOk.Should().BeTrue();
        var row = new QuadraticInequalityOf<Flt64>(
            Lhs: new QuadraticPolynomial<Flt64>(new[] {
                new QuadraticMonomial<Flt64>(new Flt64(2), x),
                new QuadraticMonomial<Flt64>(
                    Coefficient: Flt64.One,
                    Symbol1: y,
                    Symbol2: z
                ),
            }, Flt64.Zero),
            Rhs: new QuadraticPolynomial<Flt64>(Array.Empty<QuadraticMonomial<Flt64>>(), new Flt64(20)),
            Comparison: Comparison.LE
        );
        parent.AddConstraint(relation: row, group: null).IsOk.Should().BeTrue();

        using QuadraticMechanismModel<Flt64> mechanism = (await QuadraticMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        using QuadraticTetradModel matrix = mechanism.ToQuadraticTetradModel().Value;
        matrix.Variables.Select(variable => variable.Name).Should().Equal(replace
            ? new[] { x.Name, z.Name, y.Name } : new[] { x.Name, y.Name, z.Name });
        matrix.Variables.Select(variable => variable.InitialResult).Should().Equal(replace
            ? new Flt64?[] { new(2), new(4), new(3) } : new Flt64?[] { new(2), new(3), new(4) });
        matrix.Objective.Cells.Select(cell => (cell.Col1, cell.Col2, cell.Coefficient)).Should().Equal(new[] {
            (0, -1, new Flt64(2)), (1, 2, new Flt64(3)),
        });
        matrix.Constraints.Lhs[0].Select(cell => (cell.Col1, cell.Col2, cell.Coefficient)).Should().Equal(new[] {
            (0, -1, new Flt64(2)), (1, 2, Flt64.One),
        });
        mechanism.GetObjectiveValue().Should().Be(new Flt64(40));
    }

    [Theory]
    [InlineData(ObjectCategory.Minimum, 1)]
    [InlineData(ObjectCategory.Maximum, -1)]
    public async Task MixedObjectives_ShouldCombineDirectionsAndCommutativeTerms(ObjectCategory direction, int sign) {
        using var parent = new QuadraticMetaModel<Flt64>(objectCategory: direction);
        var x = new RealVar("objective-x");
        var y = new RealVar("objective-y");
        parent.Add(x).IsOk.Should().BeTrue();
        parent.Add(y).IsOk.Should().BeTrue();
        AddObjective(
            model: parent,
            category: ObjectCategory.Maximum,
            terms: new[] {
                new QuadraticMonomial<Flt64>(
                    Coefficient: new Flt64(2),
                    Symbol1: x,
                    Symbol2: y
                ),
                new QuadraticMonomial<Flt64>(
                    Coefficient: new Flt64(4),
                    Symbol1: x,
                    Symbol2: x
                ),
                new QuadraticMonomial<Flt64>(new Flt64(3), x),
            },
            constant: 5
        );
        AddObjective(
            model: parent,
            category: ObjectCategory.Minimum,
            terms: new[] {
                new QuadraticMonomial<Flt64>(
                    Coefficient: new Flt64(5),
                    Symbol1: x,
                    Symbol2: y
                ),
                new QuadraticMonomial<Flt64>(
                    Coefficient: Flt64.One,
                    Symbol1: y,
                    Symbol2: x
                ),
                new QuadraticMonomial<Flt64>(
                    Coefficient: new Flt64(-2),
                    Symbol1: x,
                    Symbol2: x
                ),
                new QuadraticMonomial<Flt64>(new Flt64(7), x),
            },
            constant: 6
        );
        parent.AddObject(
            category: ObjectCategory.Minimum,
            flattenData: new SymbolFlatten.LinearFlattenData<Flt64>(
                new[] { new LinearMonomial<Flt64>(new Flt64(2), y) }, new Flt64(4)),
            name: "promoted-linear",
            displayName: null
        ).IsOk.Should().BeTrue();
        using QuadraticMechanismModel<Flt64> mechanism = (await QuadraticMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        parent.Tokens.Find(x)!.SetResult(new Flt64(2));
        parent.Tokens.Find(y)!.SetResult(new Flt64(3));

        Result<QuadraticTetradModel, ErrorCode, Error<ErrorCode>> result = mechanism.ToQuadraticTetradModel();
        result.IsOk.Should().BeTrue();
        using QuadraticTetradModel model = result.Value;
        int xi = model.Variables.Single(variable => variable.Name == x.Name).Index;
        int yi = model.Variables.Single(variable => variable.Name == y.Name).Index;
        model.Objective.Category.Should().Be(direction);
        model.Objective.Constant.Should().Be(new Flt64(5 * sign));
        model.Objective.Cells.Should().HaveCount(4);
        model.Objective.Cells.Single(cell => cell.Col1 == xi && cell.Col2 == xi).Coefficient.Should().Be(new Flt64(-6 * sign));
        model.Objective.Cells.Single(cell => cell.Col1 == System.Math.Min(xi, yi) && cell.Col2 == System.Math.Max(xi, yi))
            .Coefficient.Should().Be(new Flt64(4 * sign));
        model.Objective.Cells.Single(cell => cell.Col1 == xi && cell.Col2 == -1).Coefficient.Should().Be(new Flt64(4 * sign));
        model.Objective.Cells.Single(cell => cell.Col1 == yi && cell.Col2 == -1).Coefficient.Should().Be(new Flt64(2 * sign));
        model.Variables.Single(variable => variable.Name == x.Name).InitialResult.Should().Be(new Flt64(2));
        mechanism.GetObjectiveValue().Should().Be(new Flt64(19 * sign));
    }

    [Theory]
    [InlineData(ObjectCategory.Minimum, ObjectCategory.Maximum)]
    [InlineData(ObjectCategory.Maximum, ObjectCategory.Minimum)]
    public async Task SingleObjective_ShouldUseItsOwnDirection(ObjectCategory parentDirection, ObjectCategory direction) {
        using var parent = new QuadraticMetaModel<Flt64>(objectCategory: parentDirection);
        AddObjective(
            model: parent,
            category: direction,
            terms: Array.Empty<QuadraticMonomial<Flt64>>(),
            constant: 7
        );
        using QuadraticMechanismModel<Flt64> mechanism = (await QuadraticMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        using QuadraticTetradModel model = mechanism.ToQuadraticTetradModel().Value;
        model.Objective.Category.Should().Be(direction);
        model.Objective.Constant.Should().Be(new Flt64(7));
    }

    [Fact]
    public async Task EmptyModel_ShouldProduceAnEmptyObjective() {
        using var parent = new QuadraticMetaModel<Flt64>(objectCategory: ObjectCategory.Maximum);
        using QuadraticMechanismModel<Flt64> mechanism = (await QuadraticMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        using QuadraticTetradModel model = mechanism.ToQuadraticTetradModel().Value;
        model.Variables.Should().BeEmpty();
        model.Constraints.Size.Should().Be(0);
        model.Objective.Category.Should().Be(ObjectCategory.Maximum);
        model.Objective.Cells.Should().BeEmpty();
        model.Objective.Constant.Should().Be(Flt64.Zero);
    }

    [Fact]
    public void Rows_ShouldRetainOrderAndMergeRepeatedTerms() {
        using var parent = new QuadraticMetaModel<Flt64>(name: "rows");
        var x = new RealVar("row-x");
        var y = new RealVar("row-y");
        parent.Add(x).IsOk.Should().BeTrue();
        parent.Add(y).IsOk.Should().BeTrue();
        Token<Flt64> xt = parent.Tokens.Find(x)!;
        Token<Flt64> yt = parent.Tokens.Find(y)!;
        QuadraticConstraintImpl<Flt64> first = Row(
            name: "first",
            rhs: 8,
            cells: new[] {
                Cell(
                    table: parent.Tokens,
                    token1: xt,
                    token2: yt,
                    coefficient: 4
                ),
                Cell(
                    table: parent.Tokens,
                    token1: yt,
                    token2: xt,
                    coefficient: -1
                ),
                Cell(
                    table: parent.Tokens,
                    token1: xt,
                    token2: null,
                    coefficient: 2
                ),
                Cell(
                    table: parent.Tokens,
                    token1: xt,
                    token2: null,
                    coefficient: -2
                ),
                Cell(
                    table: parent.Tokens,
                    token1: yt,
                    token2: yt,
                    coefficient: 5
                ),
            }
        );
        QuadraticConstraintImpl<Flt64> second = Row(
            name: "second",
            rhs: 9,
            cells: Array.Empty<IQuadraticCell<Flt64>>()
        );
        using QuadraticMechanismModel<Flt64> mechanism = MakeModel(parent, new[] { first, second });
        using QuadraticTetradModel model = mechanism.ToQuadraticTetradModel().Value;
        int xi = model.Variables.Single(variable => variable.Name == x.Name).Index;
        int yi = model.Variables.Single(variable => variable.Name == y.Name).Index;
        model.Name.Should().Be("rows");
        model.Variables.Select(variable => variable.Name)
            .Should().Equal(parent.Tokens.TokensInSolver.Select(token => token.Name));
        model.Constraints.Names.Should().Equal("first", "second");
        model.Constraints.Rhs.Should().Equal(new Flt64(8), new Flt64(9));
        model.Constraints.Sources.Should().Equal(ConstraintSource.Origin, ConstraintSource.Origin);
        model.Constraints.Lhs[0].Should().HaveCount(2);
        model.Constraints.Lhs[0].Single(cell => cell.Col1 == System.Math.Min(xi, yi) && cell.Col2 == System.Math.Max(xi, yi))
            .Coefficient.Should().Be(new Flt64(3));
        model.Constraints.Lhs[0].Single(cell => cell.Col1 == yi && cell.Col2 == yi).Coefficient.Should().Be(new Flt64(5));
        model.Constraints.Lhs[1].Should().BeEmpty();
    }

    [Fact]
    public async Task RegisteredRows_ShouldIncludePromotedLinearAndPreserveLargeFiniteCoefficients() {
        using var parent = new QuadraticMetaModel<Flt64>();
        var x = new RealVar("registered-x");
        parent.Add(x).IsOk.Should().BeTrue();
        var quadraticRow = new QuadraticInequalityOf<Flt64>(
            Lhs: new QuadraticPolynomial<Flt64>(
                new[] { new QuadraticMonomial<Flt64>(
                    Coefficient: new Flt64(1e20),
                    Symbol1: x,
                    Symbol2: x
                ) },
                Flt64.Zero),
            Rhs: new QuadraticPolynomial<Flt64>(Array.Empty<QuadraticMonomial<Flt64>>(), new Flt64(4)),
            Comparison: Comparison.LE
        );
        parent.AddConstraint(
            relation: quadraticRow,
            group: null,
            name: "quadratic"
        ).IsOk.Should().BeTrue();
        using QuadraticMechanismModel<Flt64> mechanism = (await QuadraticMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        var linearRow = new LinearInequality<Flt64>(
            Lhs: new LinearPolynomial<Flt64>(new[] { new LinearMonomial<Flt64>(new Flt64(-1e20), x) }, Flt64.Zero),
            Rhs: new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), new Flt64(3)),
            Comparison: Comparison.GE
        );
        mechanism.AddConstraint(
            relation: linearRow,
            name: "linear",
            from: (Fuookami.Ospf.Core.Symbol.IIntermediateSymbol?)null
        ).IsOk.Should().BeTrue();
        using QuadraticTetradModel model = mechanism.ToQuadraticTetradModel().Value;
        model.Constraints.Names.Should().Equal("quadratic", "linear");
        model.Constraints.Signs.Should().Equal(ConstraintRelation.LessEqual, ConstraintRelation.GreaterEqual);
        model.Constraints.Rhs.Should().Equal(new Flt64(4), new Flt64(3));
        model.Constraints.Lhs[0].Should().ContainSingle().Which.Coefficient.Should().Be(new Flt64(1e20));
        QuadraticConstraintCell linear = model.Constraints.Lhs[1].Should().ContainSingle().Which;
        linear.Col2.Should().Be(-1);
        linear.Coefficient.Should().Be(new Flt64(-1e20));
    }

    [Fact]
    public void InvalidRows_ShouldNotProduceAPartialModel() {
        using var parent = new QuadraticMetaModel<Flt64>();
        using var other = new QuadraticMetaModel<Flt64>();
        var x = new RealVar("registered");
        var missing = new RealVar("missing");
        parent.Add(x).IsOk.Should().BeTrue();
        other.Add(missing).IsOk.Should().BeTrue();
        Token<Flt64> xt = parent.Tokens.Find(x)!;
        Token<Flt64> mt = other.Tokens.Find(missing)!;
        foreach (QuadraticConstraintImpl<Flt64> row in new[] {
            Row(
                name: "missing-first",
                rhs: 1,
                cells: new[] { Cell(
                    table: other.Tokens,
                    token1: mt,
                    token2: xt,
                    coefficient: 1
                ) }
            ),
            Row(
                name: "missing-second",
                rhs: 1,
                cells: new[] { Cell(
                    table: other.Tokens,
                    token1: xt,
                    token2: mt,
                    coefficient: 1
                ) }
            ),
            Row(
                name: "nan",
                rhs: 1,
                cells: new[] { Cell(
                    table: parent.Tokens,
                    token1: xt,
                    token2: xt,
                    coefficient: double.NaN
                ) }
            ),
            Row(
                name: "infinite-rhs",
                rhs: double.PositiveInfinity,
                cells: new[] { Cell(
                    table: parent.Tokens,
                    token1: xt,
                    token2: null,
                    coefficient: 1
                ) }
            ),
            Row(
                name: "overflow",
                rhs: 1,
                cells: new[] {
                    Cell(
                        table: parent.Tokens,
                        token1: xt,
                        token2: xt,
                        coefficient: double.MaxValue
                    ),
                    Cell(
                        table: parent.Tokens,
                        token1: xt,
                        token2: xt,
                        coefficient: double.MaxValue
                    ),
                }
            ),
            Row(
                name: "lazy",
                rhs: 1,
                cells: Array.Empty<IQuadraticCell<Flt64>>(),
                lazy: true
            ),
            Row(
                name: "invalid-sign",
                rhs: 1,
                cells: Array.Empty<IQuadraticCell<Flt64>>(),
                sign: new InvalidRelation()
            ),
        }) {
            using QuadraticMechanismModel<Flt64> mechanism = MakeModel(parent, new[] { row });
            mechanism.ToQuadraticTetradModel().Should().BeOfType<Failed<QuadraticTetradModel, ErrorCode, Error<ErrorCode>>>()
                .Which.Code.Should().Be(ErrorCode.IllegalArgument);
        }
    }

    [Fact]
    public async Task InvalidObjectives_ShouldReturnFailures() {
        using var parent = new QuadraticMetaModel<Flt64>();
        var x = new RealVar("invalid-objective");
        parent.Add(x).IsOk.Should().BeTrue();
        foreach (double coefficient in new[] { double.NaN, double.PositiveInfinity }) {
            using var source = new QuadraticMetaModel<Flt64>();
            source.Add(x).IsOk.Should().BeTrue();
            AddObjective(
                model: source,
                category: ObjectCategory.Minimum,
                terms: new[] { new QuadraticMonomial<Flt64>(
                    Coefficient: new Flt64(coefficient),
                    Symbol1: x,
                    Symbol2: x
                ) },
                constant: 0
            );
            using QuadraticMechanismModel<Flt64> mechanism = (await QuadraticMechanismModel<Flt64>.InvokeAsync(source)).Value;
            mechanism.ToQuadraticTetradModel().IsFailed.Should().BeTrue();
        }
        AddObjective(
            model: parent,
            category: ObjectCategory.Minimum,
            terms: new[] { new QuadraticMonomial<Flt64>(
                Coefficient: new Flt64(double.MaxValue),
                Symbol1: x,
                Symbol2: x
            ) },
            constant: 0
        );
        AddObjective(
            model: parent,
            category: ObjectCategory.Minimum,
            terms: new[] { new QuadraticMonomial<Flt64>(
                Coefficient: new Flt64(double.MaxValue),
                Symbol1: x,
                Symbol2: x
            ) },
            constant: 0
        );
        using QuadraticMechanismModel<Flt64> overflow = (await QuadraticMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        overflow.ToQuadraticTetradModel().IsFailed.Should().BeTrue();
        using QuadraticMechanismModel<Flt64> wrongPart = MakeModel(
            parent: parent,
            constraints: Array.Empty<QuadraticConstraintImpl<Flt64>>(),
            objectives: new object[] { "unexpected" }
        );
        wrongPart.ToQuadraticTetradModel().IsFailed.Should().BeTrue();

        using var sourceParent = new QuadraticMetaModel<Flt64>();
        var missing = new RealVar("missing-objective-token");
        sourceParent.Add(x).IsOk.Should().BeTrue();
        sourceParent.Add(missing).IsOk.Should().BeTrue();
        AddObjective(
            model: sourceParent,
            category: ObjectCategory.Minimum,
            terms: new[] { new QuadraticMonomial<Flt64>(
                Coefficient: Flt64.One,
                Symbol1: x,
                Symbol2: missing
            ) },
            constant: 0
        );
        using QuadraticMechanismModel<Flt64> sourceModel = (await QuadraticMechanismModel<Flt64>.InvokeAsync(sourceParent)).Value;
        using QuadraticMechanismModel<Flt64> unresolved = MakeModel(
            parent: parent,
            constraints: Array.Empty<QuadraticConstraintImpl<Flt64>>(),
            objectives: sourceModel.ObjectFunction.SubObjects
        );
        unresolved.ToQuadraticTetradModel().IsFailed.Should().BeTrue();

        using var invalidConstant = new QuadraticMetaModel<Flt64>();
        AddObjective(
            model: invalidConstant,
            category: ObjectCategory.Minimum,
            terms: Array.Empty<QuadraticMonomial<Flt64>>(),
            constant: double.NaN
        );
        using QuadraticMechanismModel<Flt64> constantModel = (await QuadraticMechanismModel<Flt64>.InvokeAsync(invalidConstant)).Value;
        constantModel.ToQuadraticTetradModel().IsFailed.Should().BeTrue();
    }

    [Fact]
    public async Task BoundsAndInitialValues_ShouldUseSharedValidation() {
        using var parent = new QuadraticMetaModel<Flt64>();
        var x = new RealVar("bounds");
        x.Range.SetRange(ValueRange<Flt64>.Of(
            lb: Flt64.Zero,
            ub: new Flt64(2),
            lbInterval: new Interval.Open(),
            ubInterval: new Interval.Closed()
        ).Value);
        parent.Add(x).IsOk.Should().BeTrue();
        using QuadraticMechanismModel<Flt64> mechanism = (await QuadraticMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        mechanism.ToQuadraticTetradModel().IsFailed.Should().BeTrue();
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(double.NegativeInfinity), new Flt64(double.PositiveInfinity)).Value);
        parent.Tokens.Find(x)!.SetResult(new Flt64(double.PositiveInfinity));
        mechanism.ToQuadraticTetradModel().IsFailed.Should().BeTrue();
        parent.ClearSolution();
        using QuadraticTetradModel model = mechanism.ToQuadraticTetradModel().Value;
        double.IsNegativeInfinity(model.Variables.Single().LowerBound.ToDouble()).Should().BeTrue();
        double.IsPositiveInfinity(model.Variables.Single().UpperBound.ToDouble()).Should().BeTrue();
    }

    [Fact]
    public void Cancellation_ShouldReturnStoppedBeforeAndDuringConversion() {
        using var parent = new QuadraticMetaModel<Flt64>();
        var x = new RealVar("cancel-during-row");
        parent.Add(x).IsOk.Should().BeTrue();
        using var cancellation = new CancellationTokenSource();
        QuadraticConstraintImpl<Flt64> row = Row(
            name: "cancel-row",
            rhs: 1,
            cells: new[] { new CancellingCell(parent.Tokens.Find(x)!, cancellation) }
        );
        using QuadraticMechanismModel<Flt64> mechanism = MakeModel(parent, new[] { row });
        mechanism.ToQuadraticTetradModel(cancellationToken: cancellation.Token)
            .Should().BeOfType<Failed<QuadraticTetradModel, ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.ApplicationStopped);
        mechanism.ToQuadraticTetradModel(cancellationToken: cancellation.Token)
            .Should().BeOfType<Failed<QuadraticTetradModel, ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.ApplicationStopped);
    }

    private static void AddObjective(
        QuadraticMetaModel<Flt64> model,
        ObjectCategory category,
        IReadOnlyList<QuadraticMonomial<Flt64>> terms,
        double constant) {
        model.AddObject(
            category: category,
            flattenData: new SymbolFlatten.QuadraticFlattenData<Flt64>(terms, new Flt64(constant)),
            name: "part",
            displayName: null
        ).IsOk.Should().BeTrue();
    }

    private static IQuadraticCell<Flt64> Cell(
        IAbstractTokenTable<Flt64> table,
        Token<Flt64> token1,
        Token<Flt64>? token2,
        double coefficient) =>
        new QuadraticCellImpl<Flt64>(
            tokenTable: table,
            coefficient: new Flt64(coefficient),
            token1: token1,
            token2: token2
        );

    private static QuadraticConstraintImpl<Flt64> Row(
        string name,
        double rhs,
        IReadOnlyList<IQuadraticCell<Flt64>> cells,
        bool lazy = false,
        ConstraintRelation? sign = null) =>
        new(
            quadraticCells: cells,
            sign: sign ?? ConstraintRelation.LessEqual,
            rhs: new Flt64(rhs),
            lazy: lazy,
            name: name,
            origin: null,
            from: null
        );

    private static QuadraticMechanismModel<Flt64> MakeModel(
        QuadraticMetaModel<Flt64> parent,
        IReadOnlyList<QuadraticConstraintImpl<Flt64>> constraints,
        IReadOnlyList<object>? objectives = null) {
        var model = new QuadraticMechanismModel<Flt64>(
            parent: parent,
            name: parent.Name,
            constraints: constraints,
            objectFunction: new SingleObject(parent.ObjectCategory, objectives ?? Array.Empty<object>()),
            tokens: new ConcurrentTokenTable<Flt64>(parent.Tokens)
        );
        model.CaptureSourceSnapshot(new MechanismModelSourceSnapshot<Flt64>(parent));
        return model;
    }

    private sealed class CancellingCell(Token<Flt64> token, CancellationTokenSource cancellation) : IQuadraticCell<Flt64> {
        public Flt64 Coefficient {
            get {
                cancellation.Cancel();
                return Flt64.One;
            }
        }
        public Token<Flt64> Token1 => token;
        public Token<Flt64>? Token2 => null;
        public Flt64? Evaluate() => null;
        public Flt64? Evaluate(IReadOnlyList<Flt64> solution) => null;
        public Flt64? Evaluate(IReadOnlyDictionary<VariableItemKey, Flt64> solution) => null;
    }

    private sealed record InvalidRelation : ConstraintRelation {
        public override ConstraintRelation Reverse => this;
        public override Func<T, T, bool> Operator<T>() => (_, _) => false;
        public override Comparison ToComparison() => Comparison.NE;
    }
}
