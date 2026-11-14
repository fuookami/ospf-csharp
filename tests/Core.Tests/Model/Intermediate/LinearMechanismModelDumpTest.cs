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
using Fuookami.Ospf.Math.Symbol;
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

public class LinearMechanismModelDumpTest {
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task RemovedOrReplacedToken_ShouldKeepCompactMatrixAndInitialValueOrder(bool manual, bool replace) {
        using var parent = new LinearMetaModel<Flt64>(
            configuration: new MetaModelConfiguration(ManualTokenAddition: manual)
        );
        var x = new RealVar("matrix-first");
        var removed = new RealVar("matrix-removed");
        var y = new RealVar("matrix-second");
        var z = new RealVar("matrix-third");
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
            flattenData: new SymbolFlatten.LinearFlattenData<Flt64>(new[] {
                new LinearMonomial<Flt64>(new Flt64(2), x),
                new LinearMonomial<Flt64>(new Flt64(3), y),
                new LinearMonomial<Flt64>(new Flt64(4), z),
            }, Flt64.Zero),
            name: "ordered-objective",
            displayName: null
        ).IsOk.Should().BeTrue();
        var row = new LinearInequality<Flt64>(
            Lhs: new LinearPolynomial<Flt64>(new[] {
                new LinearMonomial<Flt64>(Flt64.One, y),
                new LinearMonomial<Flt64>(new Flt64(2), z),
            }, Flt64.Zero),
            Rhs: new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), new Flt64(8)),
            Comparison: Comparison.LE
        );
        parent.AddConstraint(relation: row, group: null).IsOk.Should().BeTrue();

        using LinearMechanismModel<Flt64> mechanism = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        using LinearTriadModel matrix = mechanism.ToLinearTriadModel().Value;
        matrix.Variables.Select(variable => variable.Name).Should().Equal(replace
            ? new[] { x.Name, z.Name, y.Name } : new[] { x.Name, y.Name, z.Name });
        matrix.Variables.Select(variable => variable.InitialResult).Should().Equal(replace
            ? new Flt64?[] { new(2), new(4), new(3) } : new Flt64?[] { new(2), new(3), new(4) });
        matrix.Objective.Cells.Select(cell => (cell.ColIndex, cell.Coefficient)).Should().Equal(replace
            ? new[] { (0, new Flt64(2)), (1, new Flt64(4)), (2, new Flt64(3)) }
            : new[] { (0, new Flt64(2)), (1, new Flt64(3)), (2, new Flt64(4)) });
        matrix.Constraints.Lhs[0].Select(cell => (cell.ColIndex, cell.Coefficient)).Should().Equal(replace
            ? new[] { (1, new Flt64(2)), (2, Flt64.One) }
            : new[] { (1, Flt64.One), (2, new Flt64(2)) });
        mechanism.GetObjectiveValue().Should().Be(new Flt64(29));
    }

    [Theory]
    [InlineData(ObjectCategory.Minimum, 3, 3)]
    [InlineData(ObjectCategory.Maximum, -3, -3)]
    public async Task MixedObjectives_ShouldCombineCoefficientsAndConstants(ObjectCategory direction, int coefficient, int constant) {
        using var parent = new LinearMetaModel<Flt64>(objectCategory: direction);
        var x = new RealVar("objective-x");
        parent.Add(x).IsOk.Should().BeTrue();
        AddObjective(
            model: parent,
            category: ObjectCategory.Maximum,
            variable: x,
            coefficient: 2,
            constant: 3
        );
        AddObjective(
            model: parent,
            category: ObjectCategory.Minimum,
            variable: x,
            coefficient: 5,
            constant: 4
        );
        parent.AddObject(
            category: ObjectCategory.Minimum,
            flattenData: new SymbolFlatten.LinearFlattenData<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), new Flt64(2)),
            name: "constant",
            displayName: null
        ).IsOk.Should().BeTrue();
        using LinearMechanismModel<Flt64> mechanism = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        parent.Tokens.Find(x)!.SetResult(new Flt64(2));

        Result<LinearTriadModel, ErrorCode, Error<ErrorCode>> result = mechanism.ToLinearTriadModel();
        result.IsOk.Should().BeTrue();
        using LinearTriadModel model = result.Value;
        model.Objective.Category.Should().Be(direction);
        model.Objective.Constant.Should().Be(new Flt64(constant));
        model.Objective.Cells.Should().ContainSingle().Which.Coefficient.Should().Be(new Flt64(coefficient));
        model.Variables.Should().ContainSingle().Which.InitialResult.Should().Be(new Flt64(2));
        mechanism.GetObjectiveValue().Should().Be(new Flt64(coefficient * 2 + constant));
    }

    [Theory]
    [InlineData(ObjectCategory.Minimum, ObjectCategory.Maximum)]
    [InlineData(ObjectCategory.Maximum, ObjectCategory.Minimum)]
    public async Task SingleObjective_ShouldUseItsOwnDirection(ObjectCategory parentDirection, ObjectCategory direction) {
        using var parent = new LinearMetaModel<Flt64>(objectCategory: parentDirection);
        var x = new RealVar("single-x");
        parent.Add(x).IsOk.Should().BeTrue();
        AddObjective(
            model: parent,
            category: direction,
            variable: x,
            coefficient: 2,
            constant: 3
        );
        using LinearMechanismModel<Flt64> mechanism = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        using LinearTriadModel model = mechanism.ToLinearTriadModel().Value;
        model.Objective.Category.Should().Be(direction);
        model.Objective.Cells.Should().ContainSingle().Which.Coefficient.Should().Be(new Flt64(2));
        model.Objective.Constant.Should().Be(new Flt64(3));
    }

    [Fact]
    public async Task EmptyModel_ShouldProduceAnEmptyObjective() {
        using var parent = new LinearMetaModel<Flt64>(objectCategory: ObjectCategory.Maximum);
        using LinearMechanismModel<Flt64> mechanism = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        using LinearTriadModel model = mechanism.ToLinearTriadModel().Value;
        model.Variables.Should().BeEmpty();
        model.Constraints.Size.Should().Be(0);
        model.Objective.Category.Should().Be(ObjectCategory.Maximum);
        model.Objective.Cells.Should().BeEmpty();
        model.Objective.Constant.Should().Be(Flt64.Zero);
    }

    [Fact]
    public void Rows_ShouldRetainOrderAndMergeRepeatedColumns() {
        using var parent = new LinearMetaModel<Flt64>(name: "rows");
        var x = new RealVar("row-x");
        var y = new RealVar("row-y");
        parent.Add(x).IsOk.Should().BeTrue();
        parent.Add(y).IsOk.Should().BeTrue();
        Token<Flt64> xt = parent.Tokens.Find(x)!;
        Token<Flt64> yt = parent.Tokens.Find(y)!;
        LinearConstraintImpl<Flt64> first = Row(
            name: "first",
            rhs: 8,
            cells: new[] {
                Cell(
                    table: parent.Tokens,
                    token: yt,
                    coefficient: 4
                ),
                Cell(
                    table: parent.Tokens,
                    token: xt,
                    coefficient: 2
                ),
                Cell(
                    table: parent.Tokens,
                    token: yt,
                    coefficient: -1
                ),
            }
        );
        LinearConstraintImpl<Flt64> second = Row(
            name: "second",
            rhs: 9,
            cells: Array.Empty<ILinearCell<Flt64>>()
        );
        using LinearMechanismModel<Flt64> mechanism = MakeModel(parent, new[] { first, second });
        using LinearTriadModel model = mechanism.ToLinearTriadModel().Value;
        model.Name.Should().Be("rows");
        model.Variables.Select(variable => variable.Name)
            .Should().Equal(parent.Tokens.TokensInSolver.Select(token => token.Name));
        model.Constraints.Names.Should().Equal("first", "second");
        model.Constraints.Rhs.Should().Equal(new Flt64(8), new Flt64(9));
        model.Constraints.Sources.Should().Equal(ConstraintSource.Origin, ConstraintSource.Origin);
        model.Constraints.Lhs[0].Should().HaveCount(2);
        int xIndex = model.Variables.Single(variable => variable.Name == x.Name).Index;
        int yIndex = model.Variables.Single(variable => variable.Name == y.Name).Index;
        model.Constraints.Lhs[0].Single(cell => cell.ColIndex == xIndex).Coefficient.Should().Be(new Flt64(2));
        model.Constraints.Lhs[0].Single(cell => cell.ColIndex == yIndex).Coefficient.Should().Be(new Flt64(3));
        model.Constraints.Lhs[1].Should().BeEmpty();
    }

    [Fact]
    public void InvalidRows_ShouldNotProduceAPartialModel() {
        using var parent = new LinearMetaModel<Flt64>();
        using var other = new LinearMetaModel<Flt64>();
        var x = new RealVar("registered");
        var missing = new RealVar("missing");
        parent.Add(x).IsOk.Should().BeTrue();
        other.Add(missing).IsOk.Should().BeTrue();
        Token<Flt64> xt = parent.Tokens.Find(x)!;
        Token<Flt64> missingToken = other.Tokens.Find(missing)!;
        foreach (LinearConstraintImpl<Flt64> row in new[] {
            Row(
                name: "missing",
                rhs: 1,
                cells: new[] {
                    Cell(
                        table: other.Tokens,
                        token: missingToken,
                        coefficient: 1
                    ),
                }
            ),
            Row(
                name: "nan",
                rhs: 1,
                cells: new[] {
                    Cell(
                        table: parent.Tokens,
                        token: xt,
                        coefficient: double.NaN
                    ),
                }
            ),
            Row(
                name: "infinite-rhs",
                rhs: double.PositiveInfinity,
                cells: new[] {
                    Cell(
                        table: parent.Tokens,
                        token: xt,
                        coefficient: 1
                    ),
                }
            ),
            Row(
                name: "overflow",
                rhs: 1,
                cells: new[] {
                    Cell(
                        table: parent.Tokens,
                        token: xt,
                        coefficient: double.MaxValue
                    ),
                    Cell(
                        table: parent.Tokens,
                        token: xt,
                        coefficient: double.MaxValue
                    ),
                }
            ),
            Row(
                name: "lazy",
                rhs: 1,
                cells: Array.Empty<ILinearCell<Flt64>>(),
                lazy: true
            ),
        }) {
            using LinearMechanismModel<Flt64> mechanism = MakeModel(parent, new[] { row });
            mechanism.ToLinearTriadModel().Should().BeOfType<Failed<LinearTriadModel, ErrorCode, Error<ErrorCode>>>()
                .Which.Code.Should().Be(ErrorCode.IllegalArgument);
        }
    }

    [Fact]
    public async Task InvalidObjectives_ShouldReturnFailures() {
        foreach (double coefficient in new[] { double.NaN, double.PositiveInfinity }) {
            using var parent = new LinearMetaModel<Flt64>();
            var x = new RealVar("invalid-objective");
            parent.Add(x).IsOk.Should().BeTrue();
            AddObjective(
                model: parent,
                category: ObjectCategory.Minimum,
                variable: x,
                coefficient: coefficient,
                constant: 0
            );
            using LinearMechanismModel<Flt64> mechanism = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
            mechanism.ToLinearTriadModel().Should().BeOfType<Failed<LinearTriadModel, ErrorCode, Error<ErrorCode>>>();
        }
        using var overflow = new LinearMetaModel<Flt64>();
        var y = new RealVar("overflow-objective");
        overflow.Add(y).IsOk.Should().BeTrue();
        AddObjective(
            model: overflow,
            category: ObjectCategory.Minimum,
            variable: y,
            coefficient: double.MaxValue,
            constant: 0
        );
        AddObjective(
            model: overflow,
            category: ObjectCategory.Minimum,
            variable: y,
            coefficient: double.MaxValue,
            constant: 0
        );
        using LinearMechanismModel<Flt64> overflowingModel = (await LinearMechanismModel<Flt64>.InvokeAsync(overflow)).Value;
        overflowingModel.ToLinearTriadModel().IsFailed.Should().BeTrue();
        using LinearMechanismModel<Flt64> wrongPart = MakeModel(
            parent: overflow,
            constraints: Array.Empty<LinearConstraintImpl<Flt64>>(),
            objectives: new object[] { "unexpected" }
        );
        wrongPart.ToLinearTriadModel().IsFailed.Should().BeTrue();

        using var source = new LinearMetaModel<Flt64>();
        var missing = new RealVar("missing-objective-token");
        source.Add(missing).IsOk.Should().BeTrue();
        AddObjective(
            model: source,
            category: ObjectCategory.Minimum,
            variable: missing,
            coefficient: 1,
            constant: 0
        );
        using LinearMechanismModel<Flt64> sourceModel = (await LinearMechanismModel<Flt64>.InvokeAsync(source)).Value;
        using LinearMechanismModel<Flt64> unresolved = MakeModel(
            parent: overflow,
            constraints: Array.Empty<LinearConstraintImpl<Flt64>>(),
            objectives: sourceModel.ObjectFunction.SubObjects
        );
        unresolved.ToLinearTriadModel().IsFailed.Should().BeTrue();

        using var invalidConstant = new LinearMetaModel<Flt64>();
        var z = new RealVar("invalid-constant");
        invalidConstant.Add(z).IsOk.Should().BeTrue();
        AddObjective(
            model: invalidConstant,
            category: ObjectCategory.Minimum,
            variable: z,
            coefficient: 1,
            constant: double.NaN
        );
        using LinearMechanismModel<Flt64> constantModel = (await LinearMechanismModel<Flt64>.InvokeAsync(invalidConstant)).Value;
        constantModel.ToLinearTriadModel().IsFailed.Should().BeTrue();
    }

    [Fact]
    public async Task OpenOrEmptyBoundsAndNonfiniteStart_ShouldReturnFailures() {
        using var parent = new LinearMetaModel<Flt64>();
        var x = new RealVar("bounds");
        x.Range.SetRange(ValueRange<Flt64>.Of(
            lb: new Flt64(0),
            ub: new Flt64(2),
            lbInterval: new Interval.Open(),
            ubInterval: new Interval.Closed()
        ).Value);
        parent.Add(x).IsOk.Should().BeTrue();
        using LinearMechanismModel<Flt64> mechanism = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        mechanism.ToLinearTriadModel().IsFailed.Should().BeTrue();
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(0), new Flt64(2)).Value);
        parent.Tokens.Find(x)!.SetResult(new Flt64(double.PositiveInfinity));
        mechanism.ToLinearTriadModel().IsFailed.Should().BeTrue();
        parent.ClearSolution();
        x.Range.Geq(new InvariantFlt64Wrapper(new Flt64(3))).Should().BeFalse();
        mechanism.ToLinearTriadModel().IsFailed.Should().BeTrue();
    }

    [Fact]
    public async Task InfiniteBounds_ShouldRetainTheirDirections() {
        using var parent = new LinearMetaModel<Flt64>();
        var x = new RealVar("infinite-bounds");
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(double.NegativeInfinity), new Flt64(double.PositiveInfinity)).Value);
        parent.Add(x).IsOk.Should().BeTrue();
        using LinearMechanismModel<Flt64> mechanism = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        Result<LinearTriadModel, ErrorCode, Error<ErrorCode>> result = mechanism.ToLinearTriadModel();
        result.IsOk.Should().BeTrue();
        using LinearTriadModel model = result.Value;
        double.IsNegativeInfinity(model.Variables.Single().LowerBound.ToDouble()).Should().BeTrue();
        double.IsPositiveInfinity(model.Variables.Single().UpperBound.ToDouble()).Should().BeTrue();
    }

    [Fact]
    public async Task FiniteLargeCoefficients_ShouldNotBeClamped() {
        using var parent = new LinearMetaModel<Flt64>();
        var x = new RealVar("large-coefficient");
        parent.Add(x).IsOk.Should().BeTrue();
        AddObjective(
            model: parent,
            category: ObjectCategory.Minimum,
            variable: x,
            coefficient: 1e20,
            constant: 0
        );
        using LinearMechanismModel<Flt64> objective = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        LinearConstraintImpl<Flt64> row = Row(
            name: "large-row",
            rhs: 1,
            cells: new[] {
                Cell(
                    table: parent.Tokens,
                    token: parent.Tokens.Find(x)!,
                    coefficient: -1e20
                ),
            }
        );
        using LinearMechanismModel<Flt64> mechanism = MakeModel(
            parent: parent,
            constraints: new[] { row },
            objectives: objective.ObjectFunction.SubObjects
        );
        Result<LinearTriadModel, ErrorCode, Error<ErrorCode>> result = mechanism.ToLinearTriadModel();
        result.IsOk.Should().BeTrue();
        using LinearTriadModel model = result.Value;
        model.Objective.Cells.Should().ContainSingle().Which.Coefficient.Should().Be(new Flt64(1e20));
        model.Constraints.Lhs.Single().Should().ContainSingle().Which.Coefficient.Should().Be(new Flt64(-1e20));
    }

    [Fact]
    public async Task Cancellation_ShouldReturnStopped() {
        using var parent = new LinearMetaModel<Flt64>();
        using LinearMechanismModel<Flt64> mechanism = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        mechanism.ToLinearTriadModel(cancellationToken: cancellation.Token)
            .Should().BeOfType<Failed<LinearTriadModel, ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.ApplicationStopped);
    }

    [Fact]
    public void CancellationDuringRow_ShouldNotReturnAModel() {
        using var parent = new LinearMetaModel<Flt64>();
        var x = new RealVar("cancel-during-row");
        parent.Add(x).IsOk.Should().BeTrue();
        using var cancellation = new CancellationTokenSource();
        LinearConstraintImpl<Flt64> row = Row(
            name: "cancel-row",
            rhs: 1,
            cells: new[] { new CancellingCell(parent.Tokens.Find(x)!, cancellation) }
        );
        using LinearMechanismModel<Flt64> mechanism = MakeModel(parent, new[] { row });
        mechanism.ToLinearTriadModel(cancellationToken: cancellation.Token)
            .Should().BeOfType<Failed<LinearTriadModel, ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.ApplicationStopped);
    }

    [Fact]
    public async Task MechanismFactories_ShouldPrepareFixedVariablesWithoutEliminatingTokens() {
        var x = new RealVar("fixed-variable");
        var fixedVariables = new Dictionary<IVariableItem, Flt64> { [x] = Flt64.One };
        using var linear = new LinearMetaModel<Flt64>();
        linear.Add(x).IsOk.Should().BeTrue();
        Token<Flt64> linearToken = linear.Tokens.Find(x)!;
        int linearCallbacks = linearToken.RefreshCallbacks.Count;
        using (LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(
                   linear, fixedVariables: fixedVariables)).Value) {
            model.Tokens.Find(x).Should().BeSameAs(linearToken);
        }
        linearToken.RefreshCallbacks.Should().HaveCount(linearCallbacks);
        using LinearMechanismModel<Flt64> emptyLinear = (await LinearMechanismModel<Flt64>.InvokeAsync(
            linear,
            fixedVariables: new Dictionary<IVariableItem, Flt64>()
        )).Value;

        using var quadratic = new QuadraticMetaModel<Flt64>();
        quadratic.Add(x).IsOk.Should().BeTrue();
        Token<Flt64> quadraticToken = quadratic.Tokens.Find(x)!;
        int quadraticCallbacks = quadraticToken.RefreshCallbacks.Count;
        using (QuadraticMechanismModel<Flt64> model = (await QuadraticMechanismModel<Flt64>.InvokeAsync(
                   quadratic, fixedVariables: fixedVariables)).Value) {
            model.Tokens.Find(x).Should().BeSameAs(quadraticToken);
        }
        quadraticToken.RefreshCallbacks.Should().HaveCount(quadraticCallbacks);
        using QuadraticMechanismModel<Flt64> emptyQuadratic = (await QuadraticMechanismModel<Flt64>.InvokeAsync(
            quadratic,
            fixedVariables: new Dictionary<IVariableItem, Flt64>()
        )).Value;
    }

    private static void AddObjective(
        LinearMetaModel<Flt64> model,
        ObjectCategory category,
        RealVar variable,
        double coefficient,
        double constant) {
        model.AddObject(
            category: category,
            flattenData: new SymbolFlatten.LinearFlattenData<Flt64>(
                new[] { new LinearMonomial<Flt64>(new Flt64(coefficient), variable) },
                new Flt64(constant)),
            name: "part",
            displayName: null
        ).IsOk.Should().BeTrue();
    }

    private static ILinearCell<Flt64> Cell(
        IAbstractTokenTable<Flt64> table,
        Token<Flt64> token,
        double coefficient) =>
        new LinearCellImpl<Flt64>(
            tokenTable: table,
            coefficient: new Flt64(coefficient),
            token: token
        );

    private static LinearConstraintImpl<Flt64> Row(
        string name,
        double rhs,
        IReadOnlyList<ILinearCell<Flt64>> cells,
        bool lazy = false) =>
        new(
            linearCells: cells,
            sign: ConstraintRelation.LessEqual,
            rhs: new Flt64(rhs),
            lazy: lazy,
            name: name,
            origin: null,
            from: null
        );

    private static LinearMechanismModel<Flt64> MakeModel(
        LinearMetaModel<Flt64> parent,
        IReadOnlyList<LinearConstraintImpl<Flt64>> constraints,
        IReadOnlyList<object>? objectives = null) {
        var model = new LinearMechanismModel<Flt64>(
            parent: parent,
            name: parent.Name,
            constraints: constraints,
            objectFunction: new SingleObject(parent.ObjectCategory, objectives ?? Array.Empty<object>()),
            tokens: new ConcurrentTokenTable<Flt64>(parent.Tokens)
        );
        model.CaptureSourceSnapshot(new MechanismModelSourceSnapshot<Flt64>(parent));
        return model;
    }

    private sealed class CancellingCell(Token<Flt64> token, CancellationTokenSource cancellation) : ILinearCell<Flt64> {
        public Flt64 Coefficient {
            get {
                cancellation.Cancel();
                return Flt64.One;
            }
        }
        public Token<Flt64> Token => token;
        public Flt64? Evaluate() => null;
        public Flt64? Evaluate(IReadOnlyList<Flt64> solution) => null;
        public Flt64? Evaluate(IReadOnlyDictionary<VariableItemKey, Flt64> solution) => null;
    }
}
