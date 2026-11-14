#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
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
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Variable;
using SymbolFlatten = Fuookami.Ospf.Core.Symbol.Flatten;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
namespace Fuookami.Ospf.Core.Tests.Model.Mechanism;

public class MechanismModelStructureSafetyTest {
    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(false, 2)]
    [InlineData(false, 3)]
    [InlineData(false, 4)]
    [InlineData(false, 5)]
    [InlineData(false, 6)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    [InlineData(true, 3)]
    [InlineData(true, 4)]
    [InlineData(true, 5)]
    [InlineData(true, 6)]
    public async Task ParentChanges_ShouldInvalidateOldSolutionAndEvidence(bool quadratic, int change) {
        using AbstractMetaModel<Flt64> parent = Parent(quadratic);
        var x = new RealVar("structure-x");
        x.Range.SetRange(ValueRange<Flt64>.Of(Flt64.Zero, new Flt64(10)).Value);
        parent.Add(x).IsOk.Should().BeTrue();
        parent.Tokens.Find(x)!.SetResult(Flt64.One);
        var linearRow = new List<LinearMonomial<Flt64>> { new(Flt64.One, x) };
        var linearObjective = new List<LinearMonomial<Flt64>> { new(Flt64.One, x) };
        var quadraticRow = new List<QuadraticMonomial<Flt64>> { new(
            Coefficient: Flt64.One,
            Symbol1: x,
            Symbol2: x
        ) };
        var quadraticObjective = new List<QuadraticMonomial<Flt64>> { new(
            Coefficient: Flt64.One,
            Symbol1: x,
            Symbol2: x
        ) };
        var symbol = LinearExpressionSymbol.FromVariable(x, "structure-symbol");
        if (change == 6) {
            parent.Add(symbol).IsOk.Should().BeTrue();
        }
        AddInitialStructure(
            parent: parent,
            linearRow: linearRow,
            linearObjective: linearObjective,
            quadraticRow: quadraticRow,
            quadraticObjective: quadraticObjective
        );
        using IMechanismModel<Flt64> mechanism = await Build(parent);
        using IDisposable matrix = Dump(mechanism);
        Validate(mechanism, matrix).IsOk.Should().BeTrue();

        switch (change) {
            case 0:
                AddConstraint(parent, x);
                break;
            case 1:
                AddObjective(parent, x);
                break;
            case 2:
                if (quadratic) {
                    quadraticRow[0] = new QuadraticMonomial<Flt64>(
                        Coefficient: new Flt64(2),
                        Symbol1: x,
                        Symbol2: x
                    );
                }
                else {
                    linearRow[0] = new LinearMonomial<Flt64>(new Flt64(2), x);
                }
                break;
            case 3:
                if (quadratic) {
                    quadraticObjective[0] = new QuadraticMonomial<Flt64>(
                        Coefficient: new Flt64(2),
                        Symbol1: x,
                        Symbol2: x
                    );
                }
                else {
                    linearObjective[0] = new LinearMonomial<Flt64>(new Flt64(2), x);
                }
                break;
            case 4:
                x.Range.SetRange(ValueRange<Flt64>.Of(Flt64.Zero, Flt64.One).Value);
                break;
            case 5:
                parent.Add(symbol).IsOk.Should().BeTrue();
                break;
            default:
                parent.Remove(symbol);
                break;
        }

        Validate(mechanism, matrix).IsFailed.Should().BeTrue();
        int reads = 0;
        var solution = new ObservedSolution(() => reads++);
        Restore(
            model: mechanism,
            matrix: matrix,
            solution: solution
        ).IsFailed.Should().BeTrue();
        Apply(mechanism, solution).IsFailed.Should().BeTrue();
        Apply(
            model: mechanism,
            matrix: matrix,
            solution: solution
        ).IsFailed.Should().BeTrue();
        if (change != 4) {
            DumpFailed(mechanism).Should().BeTrue();
        }
        reads.Should().Be(0);
        parent.Tokens.Find(x)!.Result.Should().Be(Flt64.One);

        using IMechanismModel<Flt64> rebuilt = await Build(parent);
        Apply(rebuilt, new[] { Flt64.Zero }).IsOk.Should().BeTrue();
        parent.Tokens.Find(x)!.Result.Should().Be(Flt64.Zero);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    public async Task ChangeWhileReadingSolution_ShouldNotWriteOldResult(bool quadratic, int operation) {
        using AbstractMetaModel<Flt64> parent = Parent(quadratic);
        var x = new RealVar("structure-read-x");
        parent.Add(x).IsOk.Should().BeTrue();
        parent.Tokens.Find(x)!.SetResult(Flt64.One);
        using IMechanismModel<Flt64> mechanism = await Build(parent);
        using IDisposable matrix = Dump(mechanism);
        var solution = new ObservedSolution(() => AddObjective(parent, x));

        bool failed = operation switch {
            0 => Apply(mechanism, solution).IsFailed,
            1 => Restore(
                model: mechanism,
                matrix: matrix,
                solution: solution
            ).IsFailed,
            _ => Apply(
                model: mechanism,
                matrix: matrix,
                solution: solution
            ).IsFailed,
        };
        failed.Should().BeTrue();
        parent.Tokens.Find(x)!.Result.Should().Be(Flt64.One);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChangedEliminatedVariableRange_ShouldInvalidateMatrix(bool quadratic) {
        using AbstractMetaModel<Flt64> parent = Parent(quadratic);
        var x = new RealVar("fixed-range-x");
        ValueRange<Flt64> range = ValueRange<Flt64>.Of(Flt64.Zero, new Flt64(2)).Value;
        x.Range.SetRange(range);
        parent.Add(x).IsOk.Should().BeTrue();
        using IMechanismModel<Flt64> mechanism = await Build(parent);
        using IDisposable matrix = Dump(mechanism, new Dictionary<IVariableItem, Flt64> { [x] = Flt64.One });

        x.Range.SetRange(ValueRange<Flt64>.Of(Flt64.Zero, Flt64.One).Value);
        Validate(mechanism, matrix).IsFailed.Should().BeTrue();
        Restore(
            model: mechanism,
            matrix: matrix,
            solution: Array.Empty<Flt64>()
        ).IsFailed.Should().BeTrue();
        Apply(
            model: mechanism,
            matrix: matrix,
            solution: Array.Empty<Flt64>()
        ).IsFailed.Should().BeTrue();
        parent.Tokens.Find(x)!.Result.Should().BeNull();
        x.Range.SetRange(range);
        Apply(
            model: mechanism,
            matrix: matrix,
            solution: Array.Empty<Flt64>()
        ).IsOk.Should().BeTrue();
        parent.Tokens.Find(x)!.Result.Should().Be(Flt64.One);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RedumpAfterRangeChange_ShouldAcceptOnlyTheNewMatrix(bool quadratic) {
        using AbstractMetaModel<Flt64> parent = Parent(quadratic);
        var x = new RealVar("redump-range-x");
        x.Range.SetRange(ValueRange<Flt64>.Of(Flt64.Zero, new Flt64(2)).Value);
        parent.Add(x).IsOk.Should().BeTrue();
        using IMechanismModel<Flt64> mechanism = await Build(parent);
        using IDisposable oldMatrix = Dump(mechanism);

        x.Range.SetRange(ValueRange<Flt64>.Of(Flt64.Zero, Flt64.One).Value);
        using IDisposable newMatrix = Dump(mechanism);
        Validate(mechanism, oldMatrix).IsFailed.Should().BeTrue();
        Validate(mechanism, newMatrix).IsOk.Should().BeTrue();
        Apply(mechanism, new[] { Flt64.One }).IsFailed.Should().BeTrue();
        Restore(
            model: mechanism,
            matrix: newMatrix,
            solution: new[] { Flt64.One }
        ).IsOk.Should().BeTrue();
        Apply(
            model: mechanism,
            matrix: newMatrix,
            solution: new[] { Flt64.One }
        ).IsOk.Should().BeTrue();
        parent.Tokens.Find(x)!.Result.Should().Be(Flt64.One);
    }

    private static AbstractMetaModel<Flt64> Parent(bool quadratic) => quadratic
        ? new QuadraticMetaModel<Flt64>()
        : new LinearMetaModel<Flt64>();

    private static void AddInitialStructure(
        AbstractMetaModel<Flt64> parent,
        List<LinearMonomial<Flt64>> linearRow,
        List<LinearMonomial<Flt64>> linearObjective,
        List<QuadraticMonomial<Flt64>> quadraticRow,
        List<QuadraticMonomial<Flt64>> quadraticObjective) {
        if (parent is LinearMetaModel<Flt64> linear) {
            linear.AddConstraint(
                relation: new LinearInequality<Flt64>(
                    Lhs: new LinearPolynomial<Flt64>(linearRow, Flt64.Zero),
                    Rhs: new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), new Flt64(10)),
                    Comparison: Comparison.LE),
                group: null
            ).IsOk.Should().BeTrue();
            linear.AddObject(
                category: ObjectCategory.Minimum,
                polynomial: new LinearPolynomial<Flt64>(linearObjective, Flt64.Zero),
                name: "original",
                displayName: null
            ).IsOk.Should().BeTrue();
        }
        else {
            var quadratic = (QuadraticMetaModel<Flt64>)parent;
            quadratic.AddConstraint(
                relation: new QuadraticInequalityOf<Flt64>(
                    Lhs: new QuadraticPolynomial<Flt64>(quadraticRow, Flt64.Zero),
                    Rhs: new QuadraticPolynomial<Flt64>(Array.Empty<QuadraticMonomial<Flt64>>(), new Flt64(10)),
                    Comparison: Comparison.LE),
                group: null
            ).IsOk.Should().BeTrue();
            quadratic.AddObject(
                category: ObjectCategory.Minimum,
                flattenData: new SymbolFlatten.QuadraticFlattenData<Flt64>(quadraticObjective, Flt64.Zero),
                name: "original",
                displayName: null
            ).IsOk.Should().BeTrue();
        }
    }

    private static void AddConstraint(AbstractMetaModel<Flt64> parent, IVariableItem x) {
        if (parent is LinearMetaModel<Flt64> linear) {
            linear.AddConstraint(x, group: null).IsOk.Should().BeTrue();
        }
        else {
            ((QuadraticMetaModel<Flt64>)parent).AddConstraint(x, group: null).IsOk.Should().BeTrue();
        }
    }

    private static void AddObjective(AbstractMetaModel<Flt64> parent, IVariableItem x) {
        LinearMonomial<Flt64>[] terms = { new(Flt64.One, x) };
        if (parent is LinearMetaModel<Flt64> linear) {
            linear.AddObject(
                category: ObjectCategory.Minimum,
                flattenData: new SymbolFlatten.LinearFlattenData<Flt64>(terms, Flt64.Zero),
                name: "added",
                displayName: null
            ).IsOk.Should().BeTrue();
        }
        else {
            ((QuadraticMetaModel<Flt64>)parent).AddObject(
                category: ObjectCategory.Minimum,
                flattenData: new SymbolFlatten.LinearFlattenData<Flt64>(terms, Flt64.Zero),
                name: "added",
                displayName: null
            ).IsOk.Should().BeTrue();
        }
    }

    private static async Task<IMechanismModel<Flt64>> Build(AbstractMetaModel<Flt64> parent) =>
        parent is LinearMetaModel<Flt64> linear
            ? (await LinearMechanismModel<Flt64>.InvokeAsync(linear)).Value
            : (await QuadraticMechanismModel<Flt64>.InvokeAsync((QuadraticMetaModel<Flt64>)parent)).Value;

    private static IDisposable Dump(IMechanismModel<Flt64> model, IReadOnlyDictionary<IVariableItem, Flt64>? fixedValues = null) => model switch {
        LinearMechanismModel<Flt64> linear => linear.ToLinearTriadModel(fixedValues).Value,
        QuadraticMechanismModel<Flt64> quadratic => quadratic.ToQuadraticTetradModel(fixedValues).Value,
        _ => throw new InvalidOperationException(),
    };

    private static bool DumpFailed(IMechanismModel<Flt64> model) => model switch {
        LinearMechanismModel<Flt64> linear => linear.ToLinearTriadModel().IsFailed,
        QuadraticMechanismModel<Flt64> quadratic => quadratic.ToQuadraticTetradModel().IsFailed,
        _ => true,
    };

    private static Try Validate(IMechanismModel<Flt64> model, IDisposable matrix) => (model, matrix) switch {
        (LinearMechanismModel<Flt64> linear, LinearTriadModel triad) => linear.ValidateSolverTokenSnapshot(triad),
        (QuadraticMechanismModel<Flt64> quadratic, QuadraticTetradModel tetrad) => quadratic.ValidateSolverTokenSnapshot(tetrad),
        _ => Results.Failed<Success>(new Err<ErrorCode>(ErrorCode.IllegalArgument)),
    };

    private static Result<IReadOnlyList<Flt64>, ErrorCode, Error<ErrorCode>> Restore(
        IMechanismModel<Flt64> model,
        IDisposable matrix,
        IReadOnlyList<Flt64> solution) => (model, matrix) switch {
        (LinearMechanismModel<Flt64> linear, LinearTriadModel triad) => linear.RestoreSolverSolution(triad, solution),
        (QuadraticMechanismModel<Flt64> quadratic, QuadraticTetradModel tetrad) => quadratic.RestoreSolverSolution(tetrad, solution),
        _ => Results.Failed<IReadOnlyList<Flt64>>(new Err<ErrorCode>(ErrorCode.IllegalArgument)),
    };

    private static Try Apply(IMechanismModel<Flt64> model, IReadOnlyList<Flt64> solution) => model switch {
        LinearMechanismModel<Flt64> linear => linear.ApplySolverSolution(solution),
        QuadraticMechanismModel<Flt64> quadratic => quadratic.ApplySolverSolution(solution),
        _ => Results.Failed<Success>(new Err<ErrorCode>(ErrorCode.IllegalArgument)),
    };

    private static Try Apply(IMechanismModel<Flt64> model, IDisposable matrix, IReadOnlyList<Flt64> solution) => (model, matrix) switch {
        (LinearMechanismModel<Flt64> linear, LinearTriadModel triad) => linear.ApplySolverSolution(triad, solution),
        (QuadraticMechanismModel<Flt64> quadratic, QuadraticTetradModel tetrad) => quadratic.ApplySolverSolution(tetrad, solution),
        _ => Results.Failed<Success>(new Err<ErrorCode>(ErrorCode.IllegalArgument)),
    };

    private sealed class ObservedSolution(Action read) : IReadOnlyList<Flt64> {
        public int Count => 1;
        public Flt64 this[int index] {
            get {
                read();
                return Flt64.Zero;
            }
        }
        public IEnumerator<Flt64> GetEnumerator() => ((IEnumerable<Flt64>)new[] { Flt64.Zero }).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
