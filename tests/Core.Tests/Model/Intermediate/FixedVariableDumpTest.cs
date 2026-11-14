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
using Fuookami.Ospf.Core.Variable;
using MathInt64 = Fuookami.Ospf.Math.Algebra.Number.Int64;
using MathInt8 = Fuookami.Ospf.Math.Algebra.Number.Int8;
using MathUInt8 = Fuookami.Ospf.Math.Algebra.Number.UInt8;
using SymbolFlatten = Fuookami.Ospf.Core.Symbol.Flatten;
namespace Fuookami.Ospf.Core.Tests.Model.Intermediate;

public class FixedVariableDumpTest {
    [Theory]
    [InlineData(ObjectCategory.Minimum, 1)]
    [InlineData(ObjectCategory.Maximum, -1)]
    public async Task LinearSubstitution_ShouldPreserveRowsObjectivesAndSolutionMapping(ObjectCategory direction, int sign) {
        using var parent = new LinearMetaModel<Flt64>(objectCategory: direction);
        var x = new RealVar("linear-fixed-x");
        var y = new RealVar("linear-free-y");
        var z = new RealVar("linear-fixed-z");
        parent.Add(new IVariableItem[] { x, y, z }).IsOk.Should().BeTrue();
        AddLinearObjective(
            model: parent,
            category: ObjectCategory.Maximum,
            terms: new[] { Linear(2, x), Linear(3, y) },
            constant: 5
        );
        AddLinearObjective(
            model: parent,
            category: ObjectCategory.Minimum,
            terms: new[] { Linear(5, x), Linear(-1, y), Linear(2, z) },
            constant: 7
        );
        AddLinearRow(
            model: parent,
            name: "mixed",
            terms: new[] { Linear(2, x), Linear(3, y), Linear(-4, z) },
            rhs: 10
        );
        AddLinearRow(
            model: parent,
            name: "infeasible-after-fixing",
            terms: new[] { Linear(2, x), Linear(-1, z) },
            rhs: 4
        );
        using LinearMechanismModel<Flt64> mechanism = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        var fixedVariables = new Dictionary<IVariableItem, Flt64> { [x] = new Flt64(2), [z] = new Flt64(-1) };
        Result<LinearTriadModel, ErrorCode, Error<ErrorCode>> result = mechanism.ToLinearTriadModel(fixedVariables);
        result.IsOk.Should().BeTrue();
        using LinearTriadModel model = result.Value;
        ModelViewVariable free = model.Variables.Should().ContainSingle().Which;
        free.Index.Should().Be(0);
        free.Origin.Should().BeSameAs(y);
        free.Copy().Origin.Should().BeSameAs(y);
        model.Constraints.Names.Should().Equal("mixed", "infeasible-after-fixing");
        model.Constraints.Lhs[0].Should().ContainSingle().Which.Coefficient.Should().Be(new Flt64(3));
        model.Constraints.Rhs[0].Should().Be(new Flt64(2));
        model.Constraints.Lhs[1].Should().BeEmpty();
        model.Constraints.Rhs[1].Should().Be(new Flt64(-1));
        model.Objective.Category.Should().Be(direction);
        model.Objective.Cells.Should().ContainSingle().Which.Coefficient.Should().Be(new Flt64(-4 * sign));
        model.Objective.Constant.Should().Be(new Flt64(6 * sign));
        mechanism.Tokens.Find(x)!.Result.Should().BeNull();
        mechanism.Tokens.Find(z)!.Result.Should().BeNull();
        fixedVariables[free.Origin!] = new Flt64(2);
        mechanism.Tokens.SetSolution(fixedVariables);
        mechanism.GetObjectiveValue().Should().Be(new Flt64(-2 * sign));
    }

    [Theory]
    [InlineData(ObjectCategory.Minimum, 1)]
    [InlineData(ObjectCategory.Maximum, -1)]
    public async Task QuadraticSubstitution_ShouldReduceBothFactorsAndPreserveObjective(ObjectCategory direction, int sign) {
        using var parent = new QuadraticMetaModel<Flt64>(objectCategory: direction);
        var x = new RealVar("quadratic-fixed-x");
        var y = new RealVar("quadratic-free-y");
        var z = new RealVar("quadratic-fixed-z");
        parent.Add(new IVariableItem[] { x, y, z }).IsOk.Should().BeTrue();
        AddQuadraticObjective(
            model: parent,
            category: ObjectCategory.Maximum,
            terms: new[] {
                Quadratic(
                    coefficient: 2,
                    first: x,
                    second: y
                ),
                Quadratic(
                    coefficient: 3,
                    first: x,
                    second: z
                ),
                Quadratic(
                    coefficient: 4,
                    first: x,
                    second: x
                ),
                Quadratic(5, z),
            },
            constant: 6
        );
        AddQuadraticObjective(
            model: parent,
            category: ObjectCategory.Minimum,
            terms: new[] {
                Quadratic(
                    coefficient: 5,
                    first: x,
                    second: y
                ),
                Quadratic(
                    coefficient: 1,
                    first: y,
                    second: y
                ),
                Quadratic(
                    coefficient: 2,
                    first: y,
                    second: z
                ),
                Quadratic(3, x),
            },
            constant: 10
        );
        var row = new QuadraticInequalityOf<Flt64>(
            Lhs: new QuadraticPolynomial<Flt64>(
                new[] {
                    Quadratic(
                        coefficient: 2,
                        first: x,
                        second: y
                    ),
                    Quadratic(
                        coefficient: 3,
                        first: x,
                        second: z
                    ),
                    Quadratic(
                        coefficient: 4,
                        first: x,
                        second: x
                    ),
                    Quadratic(
                        coefficient: 5,
                        first: y,
                        second: z
                    ),
                    Quadratic(6, y),
                    Quadratic(7, z),
                    Quadratic(
                        coefficient: 8,
                        first: y,
                        second: y
                    ),
                },
                Flt64.Zero),
            Rhs: new QuadraticPolynomial<Flt64>(Array.Empty<QuadraticMonomial<Flt64>>(), new Flt64(100)),
            Comparison: Comparison.LE
        );
        parent.AddConstraint(
            relation: row,
            group: null,
            name: "mixed"
        ).IsOk.Should().BeTrue();
        using QuadraticMechanismModel<Flt64> mechanism = (await QuadraticMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        var fixedVariables = new Dictionary<IVariableItem, Flt64> { [x] = new Flt64(2), [z] = new Flt64(3) };
        Result<QuadraticTetradModel, ErrorCode, Error<ErrorCode>> result = mechanism.ToQuadraticTetradModel(fixedVariables);
        result.IsOk.Should().BeTrue();
        using QuadraticTetradModel model = result.Value;
        ModelViewVariable free = model.Variables.Should().ContainSingle().Which;
        free.Origin.Should().BeSameAs(y);
        model.Constraints.Lhs[0].Single(cell => cell.Col2 == -1).Coefficient.Should().Be(new Flt64(25));
        model.Constraints.Lhs[0].Single(cell => cell.Col2 == 0).Coefficient.Should().Be(new Flt64(8));
        model.Constraints.Rhs[0].Should().Be(new Flt64(45));
        model.Objective.Cells.Single(cell => cell.Col2 == -1).Coefficient.Should().Be(new Flt64(12 * sign));
        model.Objective.Cells.Single(cell => cell.Col2 == 0).Coefficient.Should().Be(new Flt64(sign));
        model.Objective.Constant.Should().Be(new Flt64(-39 * sign));
        fixedVariables[free.Origin!] = new Flt64(2);
        mechanism.Tokens.SetSolution(fixedVariables);
        mechanism.GetObjectiveValue().Should().Be(new Flt64(-11 * sign));
    }

    [Fact]
    public async Task AllFixedVariables_ShouldProduceConstantObjectivesAndRows() {
        using var linearParent = new LinearMetaModel<Flt64>();
        var x = new RealVar("all-fixed-x");
        linearParent.Add(x).IsOk.Should().BeTrue();
        AddLinearObjective(
            model: linearParent,
            category: ObjectCategory.Maximum,
            terms: new[] { Linear(3, x) },
            constant: 1
        );
        AddLinearRow(
            model: linearParent,
            name: "constant",
            terms: new[] { Linear(2, x) },
            rhs: 5
        );
        using LinearMechanismModel<Flt64> linear = (await LinearMechanismModel<Flt64>.InvokeAsync(linearParent)).Value;
        var fixedVariables = new Dictionary<IVariableItem, Flt64> { [x] = new Flt64(2) };
        using LinearTriadModel triad = linear.ToLinearTriadModel(fixedVariables).Value;
        triad.Variables.Should().BeEmpty();
        triad.Objective.Category.Should().Be(ObjectCategory.Maximum);
        triad.Objective.Constant.Should().Be(new Flt64(7));
        triad.Objective.Cells.Should().BeEmpty();
        triad.Constraints.Lhs[0].Should().BeEmpty();
        triad.Constraints.Rhs[0].Should().Be(Flt64.One);

        using var quadraticParent = new QuadraticMetaModel<Flt64>();
        quadraticParent.Add(x).IsOk.Should().BeTrue();
        AddQuadraticObjective(
            model: quadraticParent,
            category: ObjectCategory.Minimum,
            terms: new[] {
                Quadratic(
                    coefficient: 3,
                    first: x,
                    second: x
                ),
                Quadratic(-2, x),
            },
            constant: 1
        );
        using QuadraticMechanismModel<Flt64> quadratic = (await QuadraticMechanismModel<Flt64>.InvokeAsync(quadraticParent)).Value;
        using QuadraticTetradModel tetrad = quadratic.ToQuadraticTetradModel(fixedVariables).Value;
        tetrad.Variables.Should().BeEmpty();
        tetrad.Objective.Cells.Should().BeEmpty();
        tetrad.Objective.Constant.Should().Be(new Flt64(9));
    }

    [Fact]
    public async Task InterleavedFixedVariables_ShouldPreserveFreeColumnMapping() {
        using var parent = new QuadraticMetaModel<Flt64>();
        var x = new RealVar("free-x");
        var y = new RealVar("fixed-y");
        var z = new RealVar("free-z");
        var w = new RealVar("fixed-w");
        parent.Add(new IVariableItem[] { x, y, z, w }).IsOk.Should().BeTrue();
        AddQuadraticObjective(
            model: parent,
            category: ObjectCategory.Minimum,
            terms: new[] {
                Quadratic(
                    coefficient: 3,
                    first: x,
                    second: z
                ),
                Quadratic(
                    coefficient: 2,
                    first: z,
                    second: x
                ),
                Quadratic(
                    coefficient: 4,
                    first: y,
                    second: x
                ),
                Quadratic(
                    coefficient: 5,
                    first: z,
                    second: w
                ),
                Quadratic(
                    coefficient: 6,
                    first: y,
                    second: w
                ),
                Quadratic(7, z),
            },
            constant: 1
        );
        using QuadraticMechanismModel<Flt64> mechanism = (await QuadraticMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        var fixedVariables = new Dictionary<IVariableItem, Flt64> { [y] = new Flt64(2), [w] = new Flt64(-1) };
        using QuadraticTetradModel model = mechanism.ToQuadraticTetradModel(fixedVariables).Value;
        model.Variables.Select(variable => variable.Origin).Should().Equal(x, z);
        model.Variables.Select(variable => variable.Index).Should().Equal(0, 1);
        model.Objective.Cells.Single(cell => cell.Col1 == 0 && cell.Col2 == 1).Coefficient.Should().Be(new Flt64(5));
        model.Objective.Cells.Single(cell => cell.Col1 == 0 && cell.Col2 == -1).Coefficient.Should().Be(new Flt64(8));
        model.Objective.Cells.Single(cell => cell.Col1 == 1 && cell.Col2 == -1).Coefficient.Should().Be(new Flt64(2));
        model.Objective.Constant.Should().Be(new Flt64(-11));

        Flt64[] reducedSolution = { new Flt64(3), new Flt64(4) };
        foreach (ModelViewVariable variable in model.Variables) {
            fixedVariables[variable.Origin!] = reducedSolution[variable.Index];
        }
        mechanism.Tokens.SetSolution(fixedVariables);
        mechanism.Tokens.Find(x)!.Result.Should().Be(new Flt64(3));
        mechanism.Tokens.Find(y)!.Result.Should().Be(new Flt64(2));
        mechanism.Tokens.Find(z)!.Result.Should().Be(new Flt64(4));
        mechanism.Tokens.Find(w)!.Result.Should().Be(new Flt64(-1));
        mechanism.GetObjectiveValue().Should().Be(new Flt64(81));
    }

    [Fact]
    public async Task DuplicateFixedVariableKeys_ShouldFail() {
        using var parent = new LinearMetaModel<Flt64>();
        var x = new RealVar("duplicate");
        parent.Add(x).IsOk.Should().BeTrue();
        using LinearMechanismModel<Flt64> mechanism = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        var fixedVariables = new Dictionary<IVariableItem, Flt64>(ReferenceEqualityComparer.Instance) {
            [x] = Flt64.Zero,
            [new VariableAlias(x)] = Flt64.One,
        };
        mechanism.ToLinearTriadModel(fixedVariables)
            .Should().BeOfType<Failed<LinearTriadModel, ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.IllegalArgument);
        mechanism.Tokens.Find(x)!.Result.Should().BeNull();
    }

    [Fact]
    public async Task InvalidFixedValues_ShouldFailWithoutMutatingModels() {
        using var linearParent = new LinearMetaModel<Flt64>();
        using var quadraticParent = new QuadraticMetaModel<Flt64>();
        var integer = new IntVar("integer");
        var binary = new BinVar("binary");
        var missing = new RealVar("missing");
        linearParent.Add(new IVariableItem[] { integer, binary }).IsOk.Should().BeTrue();
        quadraticParent.Add(new IVariableItem[] { integer, binary }).IsOk.Should().BeTrue();
        using LinearMechanismModel<Flt64> linear = (await LinearMechanismModel<Flt64>.InvokeAsync(linearParent)).Value;
        using QuadraticMechanismModel<Flt64> quadratic = (await QuadraticMechanismModel<Flt64>.InvokeAsync(quadraticParent)).Value;
        foreach (Dictionary<IVariableItem, Flt64> fixedVariables in new[] {
            new Dictionary<IVariableItem, Flt64> { [integer] = new Flt64(double.NaN) },
            new Dictionary<IVariableItem, Flt64> { [integer] = new Flt64(double.PositiveInfinity) },
            new Dictionary<IVariableItem, Flt64> { [integer] = new Flt64(0.5) },
            new Dictionary<IVariableItem, Flt64> { [binary] = new Flt64(2) },
            new Dictionary<IVariableItem, Flt64> { [missing] = Flt64.Zero },
        }) {
            linear.ToLinearTriadModel(fixedVariables)
                .Should().BeOfType<Failed<LinearTriadModel, ErrorCode, Error<ErrorCode>>>()
                .Which.Code.Should().Be(ErrorCode.IllegalArgument);
            quadratic.ToQuadraticTetradModel(fixedVariables)
                .Should().BeOfType<Failed<QuadraticTetradModel, ErrorCode, Error<ErrorCode>>>()
                .Which.Code.Should().Be(ErrorCode.IllegalArgument);
        }
        linear.Tokens.Find(integer)!.Result.Should().BeNull();
        quadratic.Tokens.Find(binary)!.Result.Should().BeNull();
    }

    [Fact]
    public async Task FixedOpenBound_ShouldRequireStrictMembership() {
        using var parent = new LinearMetaModel<Flt64>();
        var x = new RealVar("open-fixed");
        x.Range.SetRange(ValueRange<Flt64>.Of(
            lb: Flt64.Zero,
            ub: new Flt64(2),
            lbInterval: new Interval.Open(),
            ubInterval: new Interval.Open()
        ).Value);
        parent.Add(x).IsOk.Should().BeTrue();
        using LinearMechanismModel<Flt64> mechanism = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        mechanism.ToLinearTriadModel(new Dictionary<IVariableItem, Flt64> { [x] = Flt64.Zero }).IsFailed.Should().BeTrue();
        mechanism.ToLinearTriadModel(new Dictionary<IVariableItem, Flt64> { [x] = new Flt64(2) }).IsFailed.Should().BeTrue();
        using LinearTriadModel model = mechanism.ToLinearTriadModel(new Dictionary<IVariableItem, Flt64> { [x] = Flt64.One }).Value;
        model.Variables.Should().BeEmpty();
        mechanism.ToLinearTriadModel().IsFailed.Should().BeTrue();
    }

    [Fact]
    public async Task IntegerFixedValues_ShouldUseExactOriginalBoundsAndTypeDomain() {
        using var linearParent = new LinearMetaModel<Flt64>();
        using var quadraticParent = new QuadraticMetaModel<Flt64>();
        var signed = new IntVar("signed");
        var unsigned = new UIntVar("unsigned");
        var lower = new IntVar("rounded-lower");
        var upper = new IntVar("rounded-open-upper");
        var binary = new BinVar("expanded-binary");
        var ternary = new TerVar("expanded-ternary");
        var balanced = new BTerVar("expanded-balanced");
        var combination = new IntVariable1("combination", 1);
        lower.Range.SetRange(ValueRange<MathInt64>.Of(
            new MathInt64(9007199254740993L),
            new MathInt64(9007199254740998L)
        ).Value);
        upper.Range.SetRange(ValueRange<MathInt64>.Of(
            lb: new MathInt64(0),
            ub: new MathInt64(9007199254740993L),
            lbInterval: new Interval.Closed(),
            ubInterval: new Interval.Open()
        ).Value);
        binary.Range.SetRange(ValueRange<MathUInt8>.Of(new MathUInt8(0), new MathUInt8(2)).Value);
        ternary.Range.SetRange(ValueRange<MathUInt8>.Of(new MathUInt8(0), new MathUInt8(4)).Value);
        balanced.Range.SetRange(ValueRange<MathInt8>.Of(new MathInt8(-2), new MathInt8(2)).Value);
        combination[0].Range.SetRange(ValueRange<MathInt64>.Of(
            new MathInt64(9007199254740993L),
            new MathInt64(9007199254740998L)
        ).Value);
        IVariableItem[] variables = { signed, unsigned, lower, upper, binary, ternary, balanced, combination[0] };
        linearParent.Add(variables).IsOk.Should().BeTrue();
        quadraticParent.Add(variables).IsOk.Should().BeTrue();
        using LinearMechanismModel<Flt64> linear = (await LinearMechanismModel<Flt64>.InvokeAsync(linearParent)).Value;
        using QuadraticMechanismModel<Flt64> quadratic = (await QuadraticMechanismModel<Flt64>.InvokeAsync(quadraticParent)).Value;

        var valid = new Dictionary<IVariableItem, Flt64> {
            [signed] = new Flt64(-9223372036854775808d),
            [unsigned] = new Flt64(9223372036854775808d),
            [lower] = new Flt64(9007199254740994d),
            [upper] = new Flt64(9007199254740992d),
            [binary] = Flt64.One,
            [ternary] = new Flt64(2),
            [balanced] = new Flt64(-1),
            [combination[0]] = new Flt64(9007199254740994d),
        };
        Result<LinearTriadModel, ErrorCode, Error<ErrorCode>> triadResult = linear.ToLinearTriadModel(valid);
        Result<QuadraticTetradModel, ErrorCode, Error<ErrorCode>> tetradResult = quadratic.ToQuadraticTetradModel(valid);
        triadResult.IsOk.Should().BeTrue();
        tetradResult.IsOk.Should().BeTrue();
        using LinearTriadModel triad = triadResult.Value;
        using QuadraticTetradModel tetrad = tetradResult.Value;
        triad.Variables.Should().BeEmpty();
        tetrad.Variables.Should().BeEmpty();

        foreach ((IVariableItem variable, double value) in new (IVariableItem, double)[] {
            (signed, 9223372036854775808d),
            (unsigned, 18446744073709551616d),
            (lower, 9007199254740992d),
            (binary, 2),
            (ternary, 3),
            (balanced, -2),
            (combination[0], 9007199254740992d),
        }) {
            var invalid = new Dictionary<IVariableItem, Flt64>(valid) { [variable] = new Flt64(value) };
            linear.ToLinearTriadModel(invalid).IsFailed.Should().BeTrue();
            quadratic.ToQuadraticTetradModel(invalid).IsFailed.Should().BeTrue();
        }
    }

    [Fact]
    public async Task CustomIntegerVariables_ShouldRejectAmbiguousLargeBounds() {
        using var parent = new LinearMetaModel<Flt64>();
        var variable = new VariableAlias(new IntVar("custom-integer"));
        parent.Add(variable).IsOk.Should().BeTrue();
        using LinearMechanismModel<Flt64> mechanism = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        var fixedVariables = new Dictionary<IVariableItem, Flt64> { [variable] = new Flt64(9007199254740992d) };
        mechanism.ToLinearTriadModel(fixedVariables).IsFailed.Should().BeTrue();
        fixedVariables[variable] = new Flt64(9007199254740991d);
        Result<LinearTriadModel, ErrorCode, Error<ErrorCode>> result = mechanism.ToLinearTriadModel(fixedVariables);
        result.IsOk.Should().BeTrue();
        using LinearTriadModel model = result.Value;
        model.Variables.Should().BeEmpty();
    }

    [Fact]
    public async Task FixedTerms_ShouldRejectOverflow() {
        var x = new RealVar("overflow-fixed");
        using var linearParent = new LinearMetaModel<Flt64>();
        linearParent.Add(x).IsOk.Should().BeTrue();
        AddLinearObjective(
            model: linearParent,
            category: ObjectCategory.Minimum,
            terms: new[] { Linear(double.MaxValue, x) },
            constant: 0
        );
        using LinearMechanismModel<Flt64> linear = (await LinearMechanismModel<Flt64>.InvokeAsync(linearParent)).Value;
        var fixedVariables = new Dictionary<IVariableItem, Flt64> { [x] = new Flt64(2) };
        linear.ToLinearTriadModel(fixedVariables).IsFailed.Should().BeTrue();

        using var quadraticParent = new QuadraticMetaModel<Flt64>();
        quadraticParent.Add(x).IsOk.Should().BeTrue();
        AddQuadraticObjective(
            model: quadraticParent,
            category: ObjectCategory.Minimum,
            terms: new[] {
                Quadratic(
                    coefficient: double.MaxValue,
                    first: x,
                    second: x
                ),
            },
            constant: 0
        );
        using QuadraticMechanismModel<Flt64> quadratic = (await QuadraticMechanismModel<Flt64>.InvokeAsync(quadraticParent)).Value;
        quadratic.ToQuadraticTetradModel(fixedVariables).IsFailed.Should().BeTrue();
    }

    [Fact]
    public async Task FixedRowTerms_ShouldRejectOverflow() {
        var x = new RealVar("overflow-row-fixed");
        var fixedVariables = new Dictionary<IVariableItem, Flt64> { [x] = new Flt64(2) };
        using var linearParent = new LinearMetaModel<Flt64>();
        linearParent.Add(x).IsOk.Should().BeTrue();
        AddLinearRow(
            model: linearParent,
            name: "overflow-row",
            terms: new[] { Linear(double.MaxValue, x) },
            rhs: 0
        );
        using LinearMechanismModel<Flt64> linear = (await LinearMechanismModel<Flt64>.InvokeAsync(linearParent)).Value;
        linear.ToLinearTriadModel(fixedVariables).IsFailed.Should().BeTrue();

        using var quadraticParent = new QuadraticMetaModel<Flt64>();
        quadraticParent.Add(x).IsOk.Should().BeTrue();
        var row = new QuadraticInequalityOf<Flt64>(
            Lhs: new QuadraticPolynomial<Flt64>(
                new[] {
                    Quadratic(
                        coefficient: double.MaxValue,
                        first: x,
                        second: x
                    ),
                },
                Flt64.Zero),
            Rhs: new QuadraticPolynomial<Flt64>(Array.Empty<QuadraticMonomial<Flt64>>(), Flt64.Zero),
            Comparison: Comparison.LE
        );
        quadraticParent.AddConstraint(
            relation: row,
            group: null,
            name: "overflow-row"
        ).IsOk.Should().BeTrue();
        using QuadraticMechanismModel<Flt64> quadratic = (await QuadraticMechanismModel<Flt64>.InvokeAsync(quadraticParent)).Value;
        quadratic.ToQuadraticTetradModel(fixedVariables).IsFailed.Should().BeTrue();
    }

    [Fact]
    public async Task Cancellation_WithFixedValues_ShouldReturnStopped() {
        using var parent = new LinearMetaModel<Flt64>();
        var x = new RealVar("cancel-fixed");
        parent.Add(x).IsOk.Should().BeTrue();
        using LinearMechanismModel<Flt64> mechanism = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        mechanism.ToLinearTriadModel(
            fixedVariables: new Dictionary<IVariableItem, Flt64> { [x] = Flt64.Zero },
            cancellationToken: cancellation.Token
        ).Should().BeOfType<Failed<LinearTriadModel, ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.ApplicationStopped);
    }

    private static LinearMonomial<Flt64> Linear(double coefficient, IVariableItem variable) =>
        new(new Flt64(coefficient), variable);

    private static QuadraticMonomial<Flt64> Quadratic(double coefficient, IVariableItem first, IVariableItem? second = null) =>
        new(
            Coefficient: new Flt64(coefficient),
            Symbol1: first,
            Symbol2: second
        );

    private static void AddLinearObjective(
        LinearMetaModel<Flt64> model,
        ObjectCategory category,
        IReadOnlyList<LinearMonomial<Flt64>> terms,
        double constant) =>
        model.AddObject(
            category: category,
            flattenData: new SymbolFlatten.LinearFlattenData<Flt64>(terms, new Flt64(constant)),
            name: "objective",
            displayName: null
        ).IsOk.Should().BeTrue();

    private static void AddQuadraticObjective(
        QuadraticMetaModel<Flt64> model,
        ObjectCategory category,
        IReadOnlyList<QuadraticMonomial<Flt64>> terms,
        double constant) =>
        model.AddObject(
            category: category,
            flattenData: new SymbolFlatten.QuadraticFlattenData<Flt64>(terms, new Flt64(constant)),
            name: "objective",
            displayName: null
        ).IsOk.Should().BeTrue();

    private static void AddLinearRow(
        LinearMetaModel<Flt64> model,
        string name,
        IReadOnlyList<LinearMonomial<Flt64>> terms,
        double rhs) =>
        model.AddConstraint(
            relation: new LinearInequality<Flt64>(
                Lhs: new LinearPolynomial<Flt64>(terms, Flt64.Zero),
                Rhs: new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), new Flt64(rhs)),
                Comparison: Comparison.LE
            ),
            group: null,
            name: name
        ).IsOk.Should().BeTrue();

    private sealed class VariableAlias(IVariableItem inner) : IVariableItem {
        public Fuookami.Ospf.Math.Algebra.Number.UInt64 Identifier => inner.Identifier;
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
}
