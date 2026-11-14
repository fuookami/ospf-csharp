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
using Fuookami.Ospf.Core.Model.Callback;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
using MathUInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;
using SymbolFlatten = Fuookami.Ospf.Core.Symbol.Flatten;
namespace Fuookami.Ospf.Core.Tests.Model.Mechanism;

public class ObjectiveEvaluationTest {
    [Fact]
    public async Task MixedLinearObjectives_ShouldRetainMetadataOrderAndCombineValues() {
        using var parent = new LinearMetaModel<Flt64>();
        var x = new RealVar("mixed-linear-x");
        parent.Add(x).IsOk.Should().BeTrue();
        parent.AddObject(
            category: ObjectCategory.Maximum,
            flattenData: new SymbolFlatten.LinearFlattenData<Flt64>(
                new[] { new LinearMonomial<Flt64>(new Flt64(2), x) }, new Flt64(3)),
            name: "flat-max",
            displayName: "Flat maximum"
        ).IsOk.Should().BeTrue();
        parent.AddObject(
            category: ObjectCategory.Minimum,
            polynomial: new LinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(new Flt64(3), x) }, new Flt64(4)),
            name: "polynomial-min",
            displayName: "Polynomial minimum"
        ).IsOk.Should().BeTrue();

        LinearModelSnapshot<Flt64> initial = (await parent.State.RefreshAsync()).Value;
        parent.AddObject(
            category: ObjectCategory.Minimum,
            flattenData: new SymbolFlatten.LinearFlattenData<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), new Flt64(2)),
            name: "flat-constant",
            displayName: null
        ).IsOk.Should().BeTrue();
        LinearModelSnapshot<Flt64> updated = (await parent.State.RefreshAsync()).Value;
        updated.Revision.Should().NotBe(initial.Revision);
        (await parent.State.RequireCurrentAsync(initial.Revision)).IsFailed.Should().BeTrue();
        updated.Objective[x].Should().Be(Flt64.One);
        updated.ObjectiveConstant.Should().Be(new Flt64(3));

        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        IReadOnlyList<SubObject<Flt64>> parts = model.ObjectFunction.GetSubObjects<Flt64>();
        parts.Select(part => part.Name).Should().Equal("flat-max", "polynomial-min", "flat-constant");
        parts.Select(part => part.Category).Should().Equal(ObjectCategory.Maximum, ObjectCategory.Minimum, ObjectCategory.Minimum);
        parts.Select(part => part.DisplayName).Should().Equal("Flat maximum", "Polynomial minimum", null);
        parent.MetaSubObjects.Select(part => part.Name).Should().Equal("flat-max", "polynomial-min", "flat-constant");
        model.GetObjectiveValue().Should().BeNull();
        parent.Tokens.Find(x)!.SetResult(new Flt64(2));
        model.GetObjectiveValue().Should().Be(new Flt64(5));
    }

    [Theory]
    [InlineData(ObjectCategory.Minimum, ObjectCategory.Maximum)]
    [InlineData(ObjectCategory.Maximum, ObjectCategory.Minimum)]
    public async Task SingleObjective_ShouldUseItsOwnDirectionForValue(ObjectCategory parentDirection, ObjectCategory direction) {
        using var parent = new LinearMetaModel<Flt64>(objectCategory: parentDirection);
        parent.AddObject(
            category: direction,
            flattenData: new SymbolFlatten.LinearFlattenData<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), new Flt64(7)),
            name: "single",
            displayName: "Single"
        ).IsOk.Should().BeTrue();
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        SubObject<Flt64> part = model.ObjectFunction.GetSubObjects<Flt64>().Single();
        part.Category.Should().Be(direction);
        part.Name.Should().Be("single");
        part.DisplayName.Should().Be("Single");
        model.GetObjectiveValue().Should().Be(new Flt64(7));
    }

    [Theory]
    [InlineData(ObjectCategory.Minimum, -8)]
    [InlineData(ObjectCategory.Maximum, 8)]
    public async Task QuadraticObjectives_ShouldCombineOppositeDirections(ObjectCategory direction, int expected) {
        using var parent = new QuadraticMetaModel<Flt64>(objectCategory: direction);
        var x = new RealVar("mixed-quadratic-x");
        var y = new RealVar("mixed-quadratic-y");
        parent.Add(x).IsOk.Should().BeTrue();
        parent.Add(y).IsOk.Should().BeTrue();
        parent.AddObject(
            category: ObjectCategory.Maximum,
            flattenData: new SymbolFlatten.QuadraticFlattenData<Flt64>(
                new[] { new QuadraticMonomial<Flt64>(new Flt64(2), x, y) }, new Flt64(4)),
            name: "quadratic-max",
            displayName: "Quadratic maximum"
        ).IsOk.Should().BeTrue();
        parent.AddObject(
            category: ObjectCategory.Minimum,
            flattenData: new SymbolFlatten.LinearFlattenData<Flt64>(
                new[] { new LinearMonomial<Flt64>(new Flt64(3), x) }, new Flt64(2)),
            name: "linear-min",
            displayName: "Linear minimum"
        ).IsOk.Should().BeTrue();
        using QuadraticMechanismModel<Flt64> model = (await QuadraticMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        IReadOnlyList<SubObject<Flt64>> parts = model.ObjectFunction.GetSubObjects<Flt64>();
        parts.Select(part => part.Name).Should().Equal("quadratic-max", "linear-min");
        parts.Select(part => part.DisplayName).Should().Equal("Quadratic maximum", "Linear minimum");
        model.GetObjectiveValue().Should().BeNull();
        parent.Tokens.Find(x)!.SetResult(new Flt64(2));
        model.GetObjectiveValue().Should().BeNull();
        parent.Tokens.Find(y)!.SetResult(new Flt64(3));
        model.GetObjectiveValue().Should().Be(new Flt64(expected));
    }

    [Fact]
    public async Task EmptyAndRationalCombinedObjectives_ShouldUseNumericZero() {
        IFlt64ValueConverter<RtnX> converter = RtnX.Zero;
        using var parent = new LinearMetaModel<RtnX>(converter: converter);
        using LinearMechanismModel<RtnX> empty = (await LinearMechanismModel<RtnX>.InvokeAsync(parent)).Value;
        empty.GetObjectiveValue()!.Value.ToFlt64().ToDouble().Should().Be(0);
        parent.AddObject(
            category: ObjectCategory.Minimum,
            flattenData: new SymbolFlatten.LinearFlattenData<RtnX>(Array.Empty<LinearMonomial<RtnX>>(), new RtnX(2, 3)),
            name: "positive",
            displayName: null
        ).IsOk.Should().BeTrue();
        parent.AddObject(
            category: ObjectCategory.Maximum,
            flattenData: new SymbolFlatten.LinearFlattenData<RtnX>(Array.Empty<LinearMonomial<RtnX>>(), new RtnX(1, 3)),
            name: "negative",
            displayName: null
        ).IsOk.Should().BeTrue();
        using LinearMechanismModel<RtnX> combined = (await LinearMechanismModel<RtnX>.InvokeAsync(parent)).Value;
        combined.GetObjectiveValue()!.Value.ToFlt64().ToDouble().Should().BeApproximately(1.0 / 3.0, 1e-15);
        empty.GetObjectiveValue()!.Value.ToFlt64().ToDouble().Should().Be(0);
    }

    [Fact]
    public void SingleObject_ShouldCopyItsPartList() {
        var source = new List<object> { "part" };
        var objective = new SingleObject(ObjectCategory.Minimum, source);
        source.Clear();
        objective.SubObjects.Should().ContainSingle().Which.Should().Be("part");
        Action mutate = () => ((IList<object>)objective.SubObjects).Clear();
        mutate.Should().Throw<NotSupportedException>();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LinearObjective_ShouldUseResolvedCellsAndSolverOrder(bool flattened) {
        using var parent = new LinearMetaModel<Flt64>();
        var x = new RealVar("objective-x");
        var y = new RealVar("objective-y");
        parent.Add(x).IsOk.Should().BeTrue();
        parent.Add(y).IsOk.Should().BeTrue();
        var terms = new List<LinearMonomial<Flt64>> {
            new(new Flt64(3), y),
            new(new Flt64(2), x),
        };
        if (flattened) {
            parent.AddObject(
                category: ObjectCategory.Minimum,
                flattenData: new SymbolFlatten.LinearFlattenData<Flt64>(terms, new Flt64(7)),
                name: "objective",
                displayName: null
            ).IsOk.Should().BeTrue();
        }
        else {
            parent.AddObject(
                category: ObjectCategory.Minimum,
                polynomial: new LinearPolynomial<Flt64>(terms, new Flt64(7)),
                name: "objective",
                displayName: null
            ).IsOk.Should().BeTrue();
        }

        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        LinearSubObject<Flt64> objective = model.ObjectFunction.GetSubObjects<Flt64>().OfType<LinearSubObject<Flt64>>().Single();
        terms.Clear();
        objective.LinearTerms().Should().HaveCount(2);
        objective.Cells.Should().OnlyContain(cell => cell is ILinearCell<Flt64>);
        objective.Evaluate().Should().BeNull();
        objective.Evaluate(Array.Empty<Flt64>()).Should().BeNull();

        var values = new Dictionary<IVariableItem, Flt64> {
            [x] = new Flt64(2),
            [y] = new Flt64(3),
        };
        Flt64[] solution = model.Tokens.TokensInSolver.Select(token => values[token.Variable]).ToArray();
        objective.Evaluate(solution).Should().Be(new Flt64(20));
        objective.Evaluate(solution.Take(1).ToArray()).Should().BeNull();
        parent.Tokens.Find(x)!.SetResult(values[x]);
        objective.Evaluate().Should().BeNull();
        parent.Tokens.Find(y)!.SetResult(values[y]);
        objective.Evaluate().Should().Be(new Flt64(20));

        parent.Remove(x);
        parent.Close();
        objective.Evaluate().Should().Be(new Flt64(20));
        objective.Evaluate(solution).Should().Be(new Flt64(20));
    }

    [Fact]
    public async Task QuadraticObjective_ShouldEvaluateBothFactorsAndPromotedLinearTerm() {
        using var parent = new QuadraticMetaModel<Flt64>();
        var x = new RealVar("quadratic-x");
        var y = new RealVar("quadratic-y");
        parent.Add(x).IsOk.Should().BeTrue();
        parent.Add(y).IsOk.Should().BeTrue();
        var terms = new List<QuadraticMonomial<Flt64>> {
            new(
                Coefficient: new Flt64(2),
                Symbol1: x,
                Symbol2: y
            ),
            new(
                Coefficient: new Flt64(-1),
                Symbol1: x,
                Symbol2: x
            ),
            new(new Flt64(4), y),
        };
        parent.AddObject(
            category: ObjectCategory.Maximum,
            flattenData: new SymbolFlatten.QuadraticFlattenData<Flt64>(terms, new Flt64(7)),
            name: "quadratic",
            displayName: null
        ).IsOk.Should().BeTrue();
        parent.AddObject(
            category: ObjectCategory.Minimum,
            flattenData: new SymbolFlatten.LinearFlattenData<Flt64>(new[] { new LinearMonomial<Flt64>(new Flt64(3), x) }, new Flt64(5)),
            name: "promoted",
            displayName: null
        ).IsOk.Should().BeTrue();

        using QuadraticMechanismModel<Flt64> model = (await QuadraticMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        var objectives = model.ObjectFunction.GetSubObjects<Flt64>().OfType<QuadraticSubObject<Flt64>>().ToList();
        QuadraticSubObject<Flt64> quadratic = objectives.Single(part => part.Name == "quadratic");
        QuadraticSubObject<Flt64> promoted = objectives.Single(part => part.Name == "promoted");
        terms.Clear();
        quadratic.QuadraticTerms().Should().HaveCount(3);
        quadratic.Cells.Should().OnlyContain(cell => cell is IQuadraticCell<Flt64>);
        quadratic.Evaluate().Should().BeNull();

        var values = new Dictionary<IVariableItem, Flt64> {
            [x] = new Flt64(2),
            [y] = new Flt64(3),
        };
        Flt64[] solution = model.Tokens.TokensInSolver.Select(token => values[token.Variable]).ToArray();
        quadratic.Evaluate(solution).Should().Be(new Flt64(27));
        promoted.Evaluate(solution).Should().Be(new Flt64(11));
        quadratic.Evaluate(solution.Take(1).ToArray()).Should().BeNull();
        parent.Tokens.Find(x)!.SetResult(values[x]);
        quadratic.Evaluate().Should().BeNull();
        parent.Tokens.Find(y)!.SetResult(values[y]);
        quadratic.Evaluate().Should().Be(new Flt64(27));
        promoted.Evaluate().Should().Be(new Flt64(11));
        parent.Close();
        quadratic.Evaluate(solution).Should().Be(new Flt64(27));
    }

    [Fact]
    public async Task ConstantObjectives_ShouldNotRequireASolution() {
        using var linear = new LinearMetaModel<Flt64>();
        linear.AddObject(
            category: ObjectCategory.Minimum,
            polynomial: new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), new Flt64(7)),
            name: "constant",
            displayName: null
        ).IsOk.Should().BeTrue();
        linear.MetaSubObjects.Single().Evaluate().Should().Be(new Flt64(7));
        using LinearMechanismModel<Flt64> linearModel = (await LinearMechanismModel<Flt64>.InvokeAsync(linear)).Value;
        SubObject<Flt64> linearObjective = linearModel.ObjectFunction.GetSubObjects<Flt64>().Single();
        linearObjective.Evaluate().Should().Be(new Flt64(7));
        linearObjective.Evaluate(Array.Empty<Flt64>()).Should().Be(new Flt64(7));

        using var quadratic = new QuadraticMetaModel<Flt64>();
        quadratic.AddObject(
            category: ObjectCategory.Minimum,
            flattenData: new SymbolFlatten.QuadraticFlattenData<Flt64>(Array.Empty<QuadraticMonomial<Flt64>>(), new Flt64(9)),
            name: "constant",
            displayName: null
        ).IsOk.Should().BeTrue();
        using QuadraticMechanismModel<Flt64> quadraticModel = (await QuadraticMechanismModel<Flt64>.InvokeAsync(quadratic)).Value;
        SubObject<Flt64> quadraticObjective = quadraticModel.ObjectFunction.GetSubObjects<Flt64>().Single();
        quadraticObjective.Evaluate().Should().Be(new Flt64(9));
        quadraticObjective.Evaluate(Array.Empty<Flt64>()).Should().Be(new Flt64(9));
    }

    [Fact]
    public void MetaObjective_ShouldIncludeConstantAndReturnWholeZeroForUnknownValue() {
        using var parent = new LinearMetaModel<Flt64>();
        var x = new RealVar("meta-x");
        var y = new RealVar("meta-y");
        parent.Add(x).IsOk.Should().BeTrue();
        parent.Add(y).IsOk.Should().BeTrue();
        parent.AddObject(
            category: ObjectCategory.Minimum,
            polynomial: new LinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(new Flt64(2), x), new LinearMonomial<Flt64>(new Flt64(3), y) },
                new Flt64(7)),
            name: "meta",
            displayName: null
        ).IsOk.Should().BeTrue();
        MetaSubObject<Flt64> objective = parent.MetaSubObjects.Single();
        objective.Evaluate().Should().BeNull();
        objective.Evaluate(zeroIfNone: true).Should().Be(Flt64.Zero);
        objective.Evaluate(Array.Empty<Flt64>()).Should().BeNull();
        objective.Evaluate(Array.Empty<Flt64>(), zeroIfNone: true).Should().Be(Flt64.Zero);
        parent.Tokens.Find(x)!.SetResult(new Flt64(2));
        objective.Evaluate().Should().BeNull();
        objective.Evaluate(zeroIfNone: true).Should().Be(Flt64.Zero);
        parent.Tokens.Find(y)!.SetResult(new Flt64(3));
        objective.Evaluate().Should().Be(new Flt64(20));
        var values = new Dictionary<IVariableItem, Flt64> { [x] = new Flt64(2), [y] = new Flt64(3) };
        Flt64[] solution = parent.Tokens.TokensInSolver.Select(token => values[token.Variable]).ToArray();
        objective.Evaluate(solution).Should().Be(new Flt64(20));
        objective.Evaluate(solution.Take(1).ToArray()).Should().BeNull();
        using var other = new LinearMetaModel<Flt64>();
        other.Add(x).IsOk.Should().BeTrue();
        other.Tokens.Find(x)!.SetResult(new Flt64(2));
        objective.Evaluate(other.Tokens).Should().BeNull();
        objective.Evaluate(other.Tokens, zeroIfNone: true).Should().Be(Flt64.Zero);
        objective.Evaluate(new[] { new Flt64(2) }, other.Tokens).Should().BeNull();
        var unresolved = new MetaSubObject<Flt64>(
            parent: parent,
            category: ObjectCategory.Minimum,
            name: "unresolved",
            displayName: null,
            polynomial: new LinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(Flt64.One, new TestSymbol("unexpanded")) },
                Flt64.One)
        );
        unresolved.Evaluate().Should().BeNull();
        unresolved.Evaluate(zeroIfNone: true).Should().Be(Flt64.Zero);
        unresolved.Evaluate(solution).Should().BeNull();
        unresolved.Evaluate(solution, zeroIfNone: true).Should().Be(Flt64.Zero);
    }

    [Fact]
    public async Task NativeDecimalVector_ShouldNotRoundThroughFlt64() {
        IFlt64ValueConverter<FltX> converter = FltX.Zero;
        using var parent = new LinearMetaModel<FltX>(converter: converter);
        var x = new RealVar("decimal-x");
        parent.Add(x).IsOk.Should().BeTrue();
        parent.AddObject(
            category: ObjectCategory.Minimum,
            polynomial: new LinearPolynomial<FltX>(new[] { new LinearMonomial<FltX>(new FltX(3m), x) }, new FltX(1m)),
            name: "decimal",
            displayName: null
        ).IsOk.Should().BeTrue();
        using LinearMechanismModel<FltX> model = (await LinearMechanismModel<FltX>.InvokeAsync(parent)).Value;
        FltX[] solution = { new FltX(9007199254740993m) };
        var expected = new FltX(27021597764222980m);
        parent.MetaSubObjects.Single().Evaluate(solution).Should().Be(expected);
        model.ObjectFunction.GetSubObjects<FltX>().Single().Evaluate(solution).Should().Be(expected);

        using var quadraticParent = new QuadraticMetaModel<FltX>(converter: converter);
        quadraticParent.Add(x).IsOk.Should().BeTrue();
        quadraticParent.AddObject(
            category: ObjectCategory.Minimum,
            flattenData: new SymbolFlatten.QuadraticFlattenData<FltX>(
                new[] {
                    new QuadraticMonomial<FltX>(
                        Coefficient: new FltX(3m),
                        Symbol1: x,
                        Symbol2: x
                    ),
                },
                new FltX(1m)),
            name: "decimal-quadratic",
            displayName: null
        ).IsOk.Should().BeTrue();
        using QuadraticMechanismModel<FltX> quadratic = (await QuadraticMechanismModel<FltX>.InvokeAsync(quadraticParent)).Value;
        quadratic.ObjectFunction.GetSubObjects<FltX>().Single().Evaluate(new[] { new FltX(0.1m) })
            .Should().Be(new FltX(1.03m));
    }

    [Fact]
    public async Task RationalObjective_ShouldUseNativeArithmeticAndAnActualZero() {
        IFlt64ValueConverter<RtnX> converter = RtnX.Zero;
        using var parent = new LinearMetaModel<RtnX>(converter: converter);
        var x = new RealVar("rational-x");
        parent.Add(x).IsOk.Should().BeTrue();
        parent.AddObject(
            category: ObjectCategory.Minimum,
            polynomial: new LinearPolynomial<RtnX>(new[] { new LinearMonomial<RtnX>(new RtnX(1, 3), x) }, new RtnX(1, 2)),
            name: "rational",
            displayName: null
        ).IsOk.Should().BeTrue();
        MetaSubObject<RtnX> meta = parent.MetaSubObjects.Single();
        meta.Evaluate().Should().BeNull();
        meta.Evaluate(zeroIfNone: true)!.Value.ToFlt64().ToDouble().Should().Be(0);
        RtnX[] solution = { new RtnX(3, 2) };
        meta.Evaluate(solution)!.Value.ToFlt64().ToDouble().Should().Be(1);
        using LinearMechanismModel<RtnX> model = (await LinearMechanismModel<RtnX>.InvokeAsync(parent)).Value;
        model.ObjectFunction.GetSubObjects<RtnX>().Single().Evaluate(solution)!.Value.ToFlt64().ToDouble().Should().Be(1);
    }

    [Fact]
    public async Task RationalState_ShouldNegateDynamicRowsAndOppositeObjectivesWithoutDefaultValues() {
        IFlt64ValueConverter<RtnX> converter = RtnX.Zero;
        using var parent = new LinearMetaModel<RtnX>(converter: converter);
        var x = new RealVar("rational-state-x");
        parent.Add(x).IsOk.Should().BeTrue();
        var polynomial = new LinearPolynomial<RtnX>(
            new[] { new LinearMonomial<RtnX>(new RtnX(1, 3), x) },
            new RtnX(1, 2));
        parent.AddObject(
            category: ObjectCategory.Maximum,
            polynomial: polynomial,
            name: "opposite",
            displayName: null
        ).IsOk.Should().BeTrue();
        var row = new LinearInequality<RtnX>(
            Lhs: polynomial,
            Rhs: new LinearPolynomial<RtnX>(Array.Empty<LinearMonomial<RtnX>>(), RtnX.Zero),
            Comparison: Comparison.LE
        );
        parent.State.RegisterRow(new ConstraintId("rational-row"), row).IsOk.Should().BeTrue();
        Result<LinearModelSnapshot<RtnX>, ErrorCode, Error<ErrorCode>> result = await parent.State.RefreshAsync();
        result.IsOk.Should().BeTrue();
        LinearModelSnapshot<RtnX> snapshot = result.Value;
        snapshot.Rows.Single().Rhs.ToFlt64().ToDouble().Should().Be(-0.5);
        snapshot.Objective[x].ToFlt64().ToDouble().Should().BeApproximately(-1.0 / 3.0, 1e-15);
        snapshot.ObjectiveConstant.ToFlt64().ToDouble().Should().Be(-0.5);
    }

    [Fact]
    public void RationalCallback_ShouldNegateAnOppositeDirectionObjective() {
        IFlt64ValueConverter<RtnX> converter = RtnX.Zero;
        using var model = CallBackModel<RtnX>.Create(ObjectCategory.Minimum, converter);
        model.Maximize(_ => new RtnX(2, 3)).IsOk.Should().BeTrue();
        RtnX result = ((ICallBackModelInterface<RtnX>)model).Objective(Array.Empty<RtnX>());
        result.ToFlt64().ToDouble().Should().BeApproximately(-2.0 / 3.0, 1e-15);
    }

    [Fact]
    public void SubObjectiveFactories_ShouldValidateAndCopyTerms() {
        using var parent = new QuadraticMetaModel<Flt64>();
        var x = new RealVar("factory-x");
        var missing = new RealVar("factory-missing");
        parent.Add(x).IsOk.Should().BeTrue();
        var terms = new List<LinearMonomial<Flt64>> { new(new Flt64(2), x), new(Flt64.Zero, missing) };
        Result<LinearSubObject<Flt64>, ErrorCode, Error<ErrorCode>> linear = LinearSubObject<Flt64>.Create(
            flattenData: new SymbolFlatten.LinearFlattenData<Flt64>(terms, Flt64.One),
            tokens: parent.Tokens,
            converter: IIntoValue<Flt64>.Identity,
            category: ObjectCategory.Minimum,
            name: "linear"
        );
        linear.IsOk.Should().BeTrue();
        terms.Clear();
        linear.Value.LinearTerms().Should().ContainSingle();
        linear.Value.Cells.Should().ContainSingle();
        linear.Value.Evaluate(new[] { new Flt64(3) }).Should().Be(new Flt64(7));
        Action mutateTerms = () => ((IList<LinearMonomial<Flt64>>)linear.Value.LinearTerms()).Clear();
        Action mutateCells = () => ((IList<ILinearCell<Flt64>>)linear.Value.Cells).Clear();
        mutateTerms.Should().Throw<NotSupportedException>();
        mutateCells.Should().Throw<NotSupportedException>();
        LinearSubObject<Flt64>.Create(
            flattenData: new SymbolFlatten.LinearFlattenData<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, missing) }, Flt64.Zero),
            tokens: parent.Tokens,
            converter: IIntoValue<Flt64>.Identity,
            category: ObjectCategory.Minimum
        ).Should().BeOfType<Failed<LinearSubObject<Flt64>, ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.IllegalArgument);
        LinearSubObject<Flt64>.Create(
            flattenData: new SymbolFlatten.LinearFlattenData<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, new TestSymbol("unexpanded")) }, Flt64.Zero),
            tokens: parent.Tokens,
            converter: IIntoValue<Flt64>.Identity,
            category: ObjectCategory.Minimum
        ).Should().BeOfType<Failed<LinearSubObject<Flt64>, ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.IllegalArgument);
        var quadraticTerms = new List<QuadraticMonomial<Flt64>> {
            new(
                Coefficient: new Flt64(2),
                Symbol1: x,
                Symbol2: x
            ),
            new(
                Coefficient: Flt64.Zero,
                Symbol1: x,
                Symbol2: missing
            ),
        };
        Result<QuadraticSubObject<Flt64>, ErrorCode, Error<ErrorCode>> quadratic = QuadraticSubObject<Flt64>.Create(
            flattenData: new SymbolFlatten.QuadraticFlattenData<Flt64>(quadraticTerms, Flt64.One),
            tokens: parent.Tokens,
            converter: IIntoValue<Flt64>.Identity,
            category: ObjectCategory.Minimum
        );
        quadratic.IsOk.Should().BeTrue();
        quadraticTerms.Clear();
        quadratic.Value.QuadraticTerms().Should().ContainSingle();
        quadratic.Value.Cells.Should().ContainSingle();
        quadratic.Value.Evaluate(new[] { new Flt64(3) }).Should().Be(new Flt64(19));
        Action mutateQuadratic = () => ((IList<IQuadraticCell<Flt64>>)quadratic.Value.Cells).Clear();
        mutateQuadratic.Should().Throw<NotSupportedException>();
        QuadraticSubObject<Flt64>.Create(
            flattenData: new SymbolFlatten.QuadraticFlattenData<Flt64>(
                new[] {
                    new QuadraticMonomial<Flt64>(
                        Coefficient: Flt64.One,
                        Symbol1: x,
                        Symbol2: missing
                    ),
                },
                Flt64.Zero),
            tokens: parent.Tokens,
            converter: IIntoValue<Flt64>.Identity,
            category: ObjectCategory.Minimum
        ).Should().BeOfType<Failed<QuadraticSubObject<Flt64>, ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.IllegalArgument);
    }

    [Fact]
    public void MissingTokenResults_ShouldPropagateThroughCellsAndConstraints() {
        using var parent = new QuadraticMetaModel<Flt64>();
        var x = new RealVar("missing-x");
        var y = new RealVar("missing-y");
        parent.Add(x).IsOk.Should().BeTrue();
        parent.Add(y).IsOk.Should().BeTrue();
        Token<Flt64> first = parent.Tokens.Find(x)!;
        Token<Flt64> second = parent.Tokens.Find(y)!;
        first.Result.Should().BeNull();
        first.ResultWith(IIntoValue<Flt64>.Identity).Should().BeNull();
        var linear = new LinearCellImpl<Flt64>(
            tokenTable: parent.Tokens,
            coefficient: new Flt64(2),
            token: first
        );
        var quadratic = new QuadraticCellImpl<Flt64>(
            tokenTable: parent.Tokens,
            coefficient: new Flt64(2),
            token1: first,
            token2: second
        );
        var empty = new Dictionary<VariableItemKey, Flt64>();
        linear.Evaluate().Should().BeNull();
        linear.Evaluate(Array.Empty<Flt64>()).Should().BeNull();
        linear.Evaluate(empty).Should().BeNull();
        quadratic.Evaluate().Should().BeNull();
        quadratic.Evaluate(Array.Empty<Flt64>()).Should().BeNull();
        quadratic.Evaluate(empty).Should().BeNull();
        LinearConstraintImpl<Flt64>.Create(
            relation: new LinearRelationImpl<Flt64>(
                new SymbolFlatten.LinearFlattenData<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, x) }, Flt64.Zero),
                Comparison.LE),
            tokens: parent.Tokens,
            converter: IIntoValue<Flt64>.Identity
        ).Value.IsTrue().Should().BeNull();
        QuadraticConstraintImpl<Flt64>.Create(
            relation: new QuadraticRelationImpl<Flt64>(
                new SymbolFlatten.QuadraticFlattenData<Flt64>(
                    new[] {
                        new QuadraticMonomial<Flt64>(
                            Coefficient: Flt64.One,
                            Symbol1: x,
                            Symbol2: y
                        ),
                    },
                    Flt64.Zero),
                Comparison.LE),
            tokens: parent.Tokens,
            converter: IIntoValue<Flt64>.Identity
        ).Value.IsTrue().Should().BeNull();
        first.SetResult(Flt64.One);
        quadratic.Evaluate().Should().BeNull();
        quadratic.Evaluate(new Dictionary<VariableItemKey, Flt64> { [first.Key] = Flt64.One }).Should().BeNull();
        second.SetResult(new Flt64(3));
        linear.Evaluate().Should().Be(new Flt64(2));
        quadratic.Evaluate().Should().Be(new Flt64(6));
        parent.ClearSolution();
        first.Result.Should().BeNull();
        first.ResultWith(IIntoValue<Flt64>.Identity).Should().BeNull();
    }

    [Fact]
    public void FunctionAdapter_ShouldNotTreatAnUnsolvedTokenAsZero() {
        using var parent = new LinearMetaModel<Flt64>();
        var x = new RealVar("adapter-input");
        parent.Add(x).IsOk.Should().BeTrue();
        var function = new AbsFunction<Flt64>(
            new LinearPolynomial<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, x) }, Flt64.Zero),
            IIntoValue<Flt64>.Identity);
        var adapter = new LinearFunctionSymbolAdapter<Flt64>(function);
        parent.Add(adapter).IsOk.Should().BeTrue();
        adapter.Evaluate(parent.Tokens, IIntoValue<Flt64>.Identity).Should().BeNull();
        adapter.Evaluate(
            tokenTable: parent.Tokens,
            converter: IIntoValue<Flt64>.Identity,
            zeroIfNone: true
        ).Should().Be(Flt64.Zero);
        parent.Tokens.Find(function.ResultVar)!.SetResult(new Flt64(4));
        adapter.Evaluate(parent.Tokens, IIntoValue<Flt64>.Identity).Should().Be(new Flt64(4));
    }

    [Fact]
    public void UnboundedToken_ShouldNotConvertMissingBoundsToZero() {
        var variable = new UnboundedVariable(new RealVar("unbounded"));
        var token = new Token<Flt64>(
            variable: variable,
            solverIndex: 0,
            refreshCallbacks: new Dictionary<object, Action<bool>>()
        );
        token.LowerBoundV(IIntoValue<Flt64>.Identity).Should().BeNull();
        token.UpperBoundV(IIntoValue<Flt64>.Identity).Should().BeNull();
    }

    [Fact]
    public async Task ClosedParents_ShouldRejectNewModelsWithoutInvalidatingExistingSnapshots() {
        var linear = new LinearMetaModel<Flt64>();
        var x = new RealVar("closed-linear");
        linear.Add(x).IsOk.Should().BeTrue();
        using LinearMechanismModel<Flt64> existingLinear = (await LinearMechanismModel<Flt64>.InvokeAsync(linear)).Value;
        Token<Flt64> linearToken = linear.Tokens.Find(x)!;
        linear.Close();
        int linearCallbacks = linearToken.RefreshCallbacks.Count;
        (await LinearMechanismModel<Flt64>.InvokeAsync(linear))
            .Should().BeOfType<Failed<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.ApplicationStopped);
        linearToken.RefreshCallbacks.Should().HaveCount(linearCallbacks);
        existingLinear.Tokens.Find(x).Should().BeSameAs(linearToken);

        var quadratic = new QuadraticMetaModel<Flt64>();
        var y = new RealVar("closed-quadratic");
        quadratic.Add(y).IsOk.Should().BeTrue();
        using QuadraticMechanismModel<Flt64> existingQuadratic = (await QuadraticMechanismModel<Flt64>.InvokeAsync(quadratic)).Value;
        Token<Flt64> quadraticToken = quadratic.Tokens.Find(y)!;
        quadratic.Close();
        int quadraticCallbacks = quadraticToken.RefreshCallbacks.Count;
        (await QuadraticMechanismModel<Flt64>.InvokeAsync(quadratic))
            .Should().BeOfType<Failed<QuadraticMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.ApplicationStopped);
        quadraticToken.RefreshCallbacks.Should().HaveCount(quadraticCallbacks);
        existingQuadratic.Tokens.Find(y).Should().BeSameAs(quadraticToken);
    }

    private sealed record TestSymbol(string Name) : ISymbol {
        public string? DisplayName => null;
    }

    private sealed class UnboundedVariable(IVariableItem inner) : IVariableItem {
        public MathUInt64 Identifier => inner.Identifier;
        public int Index => inner.Index;
        public VariableItemKey Key => inner.Key;
        public string Name => inner.Name;
        public string? DisplayName => inner.DisplayName;
        public IVariableTypeKind TypeKind => inner.TypeKind;
        public Bound<Flt64>? LowerBound => null;
        public Bound<Flt64>? UpperBound => null;
        public bool BelongsTo(IVariableItem item) => inner.BelongsTo(item);
        public bool BelongsTo(IVariableCombination combination) => inner.BelongsTo(combination);
    }
}
