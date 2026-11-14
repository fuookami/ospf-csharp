#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Variable;
using Ret = Fuookami.Ospf.Utils.Functional.Result<System.Collections.Generic.IReadOnlyList<Fuookami.Ospf.Math.Algebra.Number.Flt64>, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
namespace Fuookami.Ospf.Core.Tests.Model.Intermediate;

public class FixedVariableSolutionTest {
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task RestoreAndApply_ShouldPreserveInterleavedFixedValues(bool quadratic, bool manual) {
        using AbstractMetaModel<Flt64> parent = Parent(quadratic, manual);
        var x = new RealVar("fixed-x");
        var y = new RealVar("free-y");
        var z = new RealVar("fixed-z");
        var w = new RealVar("free-w");
        parent.Add(new IVariableItem[] { x, y, z, w }).IsOk.Should().BeTrue();
        using IMechanismModel<Flt64> mechanism = await Build(parent);
        var fixedValues = new Dictionary<IVariableItem, Flt64> { [x] = new(1), [z] = new(3) };
        using IDisposable matrix = Dump(mechanism, fixedValues);
        fixedValues[x] = new Flt64(100);
        fixedValues.Clear();

        Ret restored = Restore(
            model: mechanism,
            matrix: matrix,
            solution: new[] { new Flt64(2), new Flt64(4) }
        );
        restored.IsOk.Should().BeTrue();
        restored.Value.Should().Equal(new Flt64[] { new(1), new(2), new(3), new(4) });
        mechanism.Tokens.Find(x)!.Result.Should().BeNull();
        Restore(
            model: mechanism,
            matrix: matrix,
            solution: new[] { new Flt64(7), new Flt64(8) }
        ).Value
            .Should().Equal(new Flt64[] { new(1), new(7), new(3), new(8) });

        object key = new();
        parent.Tokens.Cache(
            cacheKey: key,
            solution: (IReadOnlyList<Flt64>?)null,
            value: Flt64.One
        );
        mechanism.Tokens.Cache(
            cacheKey: key,
            solution: (IReadOnlyList<Flt64>?)null,
            value: Flt64.One
        );
        Apply(
            model: mechanism,
            matrix: matrix,
            solution: new[] { new Flt64(2), new Flt64(4) }
        ).IsOk.Should().BeTrue();
        parent.Tokens.TokensInSolver.Select(token => token.Result)
            .Should().Equal(new Flt64?[] { new(1), new(2), new(3), new(4) });
        mechanism.Tokens.TokensInSolver.Select(token => token.Result)
            .Should().Equal(new Flt64?[] { new(1), new(2), new(3), new(4) });
        parent.Tokens.Cached(key, (IReadOnlyList<Flt64>?)null).Should().BeFalse();
        mechanism.Tokens.Cached(key, (IReadOnlyList<Flt64>?)null).Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Restore_ShouldHandleNoFixedAndAllFixedColumns(bool quadratic) {
        using AbstractMetaModel<Flt64> parent = Parent(quadratic, manual: false);
        var x = new RealVar("only-x");
        parent.Add(x).IsOk.Should().BeTrue();
        using IMechanismModel<Flt64> mechanism = await Build(parent);
        using IDisposable ordinary = Dump(mechanism, null);
        Restore(
            model: mechanism,
            matrix: ordinary,
            solution: new[] { new Flt64(9) }
        ).Value.Should().Equal(new Flt64(9));
        using IDisposable allFixed = Dump(mechanism, new Dictionary<IVariableItem, Flt64> { [x] = new(-3) });
        Restore(
            model: mechanism,
            matrix: allFixed,
            solution: Array.Empty<Flt64>()
        ).Value.Should().Equal(new Flt64(-3));
        Apply(
            model: mechanism,
            matrix: allFixed,
            solution: Array.Empty<Flt64>()
        ).IsOk.Should().BeTrue();
        parent.Tokens.Find(x)!.Result.Should().Be(new Flt64(-3));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Restore_ShouldPreserveNegativeZeroAndEmptyModels(bool quadratic) {
        using AbstractMetaModel<Flt64> parent = Parent(quadratic, manual: false);
        var x = new RealVar("negative-zero-fixed");
        var y = new RealVar("negative-zero-free");
        parent.Add(new IVariableItem[] { x, y }).IsOk.Should().BeTrue();
        using IMechanismModel<Flt64> mechanism = await Build(parent);
        using IDisposable matrix = Dump(mechanism, new Dictionary<IVariableItem, Flt64> { [x] = new(-0.0) });
        Ret restored = Restore(
            model: mechanism,
            matrix: matrix,
            solution: new[] { new Flt64(-0.0) }
        );
        restored.IsOk.Should().BeTrue();
        restored.Value.Select(value => BitConverter.DoubleToInt64Bits(value.ToDouble()))
            .Should().Equal(long.MinValue, long.MinValue);

        using AbstractMetaModel<Flt64> emptyParent = Parent(quadratic, manual: false);
        using IMechanismModel<Flt64> empty = await Build(emptyParent);
        using IDisposable emptyMatrix = Dump(empty, null);
        Restore(
            model: empty,
            matrix: emptyMatrix,
            solution: Array.Empty<Flt64>()
        ).Value.Should().BeEmpty();
        Apply(
            model: empty,
            matrix: emptyMatrix,
            solution: Array.Empty<Flt64>()
        ).IsOk.Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Restore_ShouldRejectInvalidVectorsWithoutWriting(bool quadratic) {
        using AbstractMetaModel<Flt64> parent = Parent(quadratic, manual: false);
        var x = new RealVar("invalid-fixed-x");
        var y = new RealVar("invalid-free-y");
        parent.Add(new IVariableItem[] { x, y }).IsOk.Should().BeTrue();
        parent.Tokens.SetSolverSolution(new[] { new Flt64(10), new Flt64(20) });
        using IMechanismModel<Flt64> mechanism = await Build(parent);
        using IDisposable matrix = Dump(mechanism, new Dictionary<IVariableItem, Flt64> { [x] = Flt64.One });
        IReadOnlyList<Flt64>?[] invalid = {
            null,
            Array.Empty<Flt64>(),
            new[] { Flt64.One, Flt64.One },
            new[] { new Flt64(double.NaN) },
            new[] { new Flt64(double.PositiveInfinity) },
            new[] { new Flt64(double.NegativeInfinity) },
            new CallbackSolution(() => throw new InvalidOperationException("unreadable")),
            new GrowingSolution(),
        };
        foreach (IReadOnlyList<Flt64>? values in invalid) {
            Restore(
                model: mechanism,
                matrix: matrix,
                solution: values
            ).IsFailed.Should().BeTrue();
            Apply(
                model: mechanism,
                matrix: matrix,
                solution: values
            ).IsFailed.Should().BeTrue();
            parent.Tokens.Find(x)!.Result.Should().Be(new Flt64(10));
            parent.Tokens.Find(y)!.Result.Should().Be(new Flt64(20));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Restore_ShouldRejectWrongSourceAndProtectColumns(bool quadratic) {
        using AbstractMetaModel<Flt64> first = Parent(quadratic, manual: false);
        using AbstractMetaModel<Flt64> second = Parent(quadratic, manual: false);
        var x = new RealVar("source-x");
        var y = new RealVar("source-y");
        first.Add(new IVariableItem[] { x, y }).IsOk.Should().BeTrue();
        second.Add(new IVariableItem[] { x, y }).IsOk.Should().BeTrue();
        using IMechanismModel<Flt64> source = await Build(first);
        using IMechanismModel<Flt64> other = await Build(second);
        using IDisposable matrix = Dump(source, null);
        using IDisposable manual = CopyWithoutProvenance(matrix);
        Flt64[] values = { Flt64.One, new(2) };

        Restore(
            model: other,
            matrix: matrix,
            solution: values
        ).IsFailed.Should().BeTrue();
        Restore(
            model: source,
            matrix: manual,
            solution: values
        ).IsFailed.Should().BeTrue();
        IReadOnlyList<ModelViewVariable> columns = Columns(matrix);
        Mapping(matrix).Validate(
            source: ((BasicMechanismModel<Flt64>)source).SolutionIdentity,
            variables: new[] { columns[1], columns[0] }
        ).IsFailed.Should().BeTrue();
        Action replace = () => ((IList<ModelViewVariable>)columns)[0] = columns[1];
        replace.Should().Throw<NotSupportedException>();
        Restore(
            model: source,
            matrix: matrix,
            solution: values
        ).IsOk.Should().BeTrue();
        first.Tokens.Find(x)!.Result.Should().BeNull();
        first.Tokens.Find(y)!.Result.Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Restore_ShouldValidateSourceAndColumnsBeforeReadingInput(bool quadratic) {
        using AbstractMetaModel<Flt64> first = Parent(quadratic, manual: false);
        using AbstractMetaModel<Flt64> second = Parent(quadratic, manual: false);
        var x = new RealVar("input-validation-x");
        first.Add(x).IsOk.Should().BeTrue();
        second.Add(x).IsOk.Should().BeTrue();
        using IMechanismModel<Flt64> source = await Build(first);
        using IMechanismModel<Flt64> other = await Build(second);
        using IDisposable matrix = Dump(source, null);
        bool read = false;
        var solution = new CallbackSolution(() => read = true);

        Restore(
            model: other,
            matrix: matrix,
            solution: solution
        ).IsFailed.Should().BeTrue();
        read.Should().BeFalse();

        IReadOnlyList<ModelViewVariable> columns = Columns(matrix);
        Mapping(matrix).Validate(
            source: ((BasicMechanismModel<Flt64>)source).SolutionIdentity,
            variables: new[] { columns[0].Copy() }
        ).IsFailed.Should().BeTrue();
        Action replace = () => ((IList<ModelViewVariable>)columns)[0] = columns[0].Copy();
        replace.Should().Throw<NotSupportedException>();
        Restore(
            model: source,
            matrix: matrix,
            solution: solution
        ).IsOk.Should().BeTrue();
        read.Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Restore_ShouldRejectColumnReplacementDuringInputRead(bool quadratic) {
        using AbstractMetaModel<Flt64> parent = Parent(quadratic, manual: false);
        var x = new RealVar("read-columns-x");
        parent.Add(x).IsOk.Should().BeTrue();
        using IMechanismModel<Flt64> mechanism = await Build(parent);
        using IDisposable matrix = Dump(mechanism, null);
        IReadOnlyList<ModelViewVariable> columns = Columns(matrix);
        var solution = new CallbackSolution(() => ((IList<ModelViewVariable>)columns)[0] = columns[0].Copy());

        Restore(
            model: mechanism,
            matrix: matrix,
            solution: solution
        ).IsFailed.Should().BeTrue();
        parent.Tokens.Find(x)!.Result.Should().BeNull();
        ValidateSnapshot(mechanism, matrix).IsOk.Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Restore_ShouldRejectReplacementDuringInputRead(bool quadratic) {
        using AbstractMetaModel<Flt64> parent = Parent(quadratic, manual: false);
        var x = new RealVar("changed-x");
        var y = new RealVar("changed-y");
        parent.Add(new IVariableItem[] { x, y }).IsOk.Should().BeTrue();
        using IMechanismModel<Flt64> mechanism = await Build(parent);
        using IDisposable matrix = Dump(mechanism, new Dictionary<IVariableItem, Flt64> { [x] = Flt64.One });
        var values = new CallbackSolution(() => parent.Add(y).IsOk.Should().BeTrue());

        Restore(
            model: mechanism,
            matrix: matrix,
            solution: values
        ).IsFailed.Should().BeTrue();
        parent.Tokens.Find(x)!.Result.Should().BeNull();
        parent.Tokens.Find(y)!.Result.Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TokenSnapshot_ShouldValidateWithoutAnIncumbent(bool quadratic) {
        using AbstractMetaModel<Flt64> parent = Parent(quadratic, manual: false);
        using AbstractMetaModel<Flt64> otherParent = Parent(quadratic, manual: false);
        var x = new RealVar("snapshot-fixed-x");
        var y = new RealVar("snapshot-free-y");
        parent.Add(new IVariableItem[] { x, y }).IsOk.Should().BeTrue();
        otherParent.Add(new IVariableItem[] { x, y }).IsOk.Should().BeTrue();
        parent.Tokens.SetSolverSolution(new[] { Flt64.One, new Flt64(2) });
        using IMechanismModel<Flt64> mechanism = await Build(parent);
        using IMechanismModel<Flt64> other = await Build(otherParent);
        using IDisposable matrix = Dump(mechanism, new Dictionary<IVariableItem, Flt64> { [x] = Flt64.One });
        using IDisposable copied = CopyWithoutProvenance(matrix);

        ValidateSnapshot(mechanism, matrix).IsOk.Should().BeTrue();
        ValidateSnapshot(other, matrix).IsFailed.Should().BeTrue();
        ValidateSnapshot(mechanism, copied).IsFailed.Should().BeTrue();
        IReadOnlyList<ModelViewVariable> columns = Columns(matrix);
        Action replace = () => ((IList<ModelViewVariable>)columns)[0] = columns[0].Copy();
        replace.Should().Throw<NotSupportedException>();
        ValidateSnapshot(mechanism, matrix).IsOk.Should().BeTrue();
        parent.Add(x).IsOk.Should().BeTrue();
        ValidateSnapshot(mechanism, matrix).Should()
            .BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.IllegalArgument);
        parent.Tokens.Find(x)!.Result.Should().BeNull();
        parent.Tokens.Find(y)!.Result.Should().Be(new Flt64(2));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TokenSnapshot_ShouldRejectClosedSourceWithoutAnIncumbent(bool quadratic) {
        using AbstractMetaModel<Flt64> parent = Parent(quadratic, manual: false);
        var x = new RealVar("snapshot-close-x");
        parent.Add(x).IsOk.Should().BeTrue();
        IMechanismModel<Flt64> mechanism = await Build(parent);
        using IDisposable matrix = Dump(mechanism, null);
        mechanism.Dispose();

        ValidateSnapshot(mechanism, matrix).Should()
            .BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.ApplicationStopped);

        using AbstractMetaModel<Flt64> otherParent = Parent(quadratic, manual: false);
        otherParent.Add(x).IsOk.Should().BeTrue();
        using IMechanismModel<Flt64> other = await Build(otherParent);
        using IDisposable otherMatrix = Dump(other, null);
        otherParent.Close();
        ValidateSnapshot(other, otherMatrix).Should()
            .BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.ApplicationStopped);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Restore_ShouldRejectClosedSourceOrParent(bool quadratic) {
        var x = new RealVar("closed-x");
        using AbstractMetaModel<Flt64> first = Parent(quadratic, manual: false);
        first.Add(x).IsOk.Should().BeTrue();
        IMechanismModel<Flt64> disposed = await Build(first);
        using IDisposable oldMatrix = Dump(disposed, null);
        disposed.Dispose();
        Restore(
            model: disposed,
            matrix: oldMatrix,
            solution: new[] { Flt64.One }
        ).Should()
            .BeOfType<Failed<IReadOnlyList<Flt64>, ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.ApplicationStopped);

        using AbstractMetaModel<Flt64> second = Parent(quadratic, manual: false);
        second.Add(x).IsOk.Should().BeTrue();
        using IMechanismModel<Flt64> mechanism = await Build(second);
        using IDisposable matrix = Dump(mechanism, null);
        second.Close();
        Restore(
            model: mechanism,
            matrix: matrix,
            solution: new[] { Flt64.One }
        ).Should()
            .BeOfType<Failed<IReadOnlyList<Flt64>, ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.ApplicationStopped);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Restore_ShouldObserveClosureDuringInputRead(bool quadratic) {
        using AbstractMetaModel<Flt64> parent = Parent(quadratic, manual: false);
        var x = new RealVar("read-close-x");
        parent.Add(x).IsOk.Should().BeTrue();
        using IMechanismModel<Flt64> mechanism = await Build(parent);
        using IDisposable matrix = Dump(mechanism, null);
        Restore(
            model: mechanism,
            matrix: matrix,
            solution: new CallbackSolution(parent.Close)
        ).Should()
            .BeOfType<Failed<IReadOnlyList<Flt64>, ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.ApplicationStopped);
        mechanism.Tokens.Find(x)!.Result.Should().BeNull();
    }

    [Fact]
    public async Task Restore_ShouldRejectNullSourcesAndMatrices() {
        LinearMechanismModel<Flt64>? missingLinear = null;
        QuadraticMechanismModel<Flt64>? missingQuadratic = null;
        missingLinear.RestoreSolverSolution(null, null).IsFailed.Should().BeTrue();
        missingQuadratic.RestoreSolverSolution(null, null).IsFailed.Should().BeTrue();
        missingLinear.ValidateSolverTokenSnapshot(null).IsFailed.Should().BeTrue();
        missingQuadratic.ValidateSolverTokenSnapshot(null).IsFailed.Should().BeTrue();
        missingLinear.ApplySolverSolution(null, null).IsFailed.Should().BeTrue();
        missingQuadratic.ApplySolverSolution(null, null).IsFailed.Should().BeTrue();
        using var linearParent = new LinearMetaModel<Flt64>();
        using var quadraticParent = new QuadraticMetaModel<Flt64>();
        using LinearMechanismModel<Flt64> linear = (await LinearMechanismModel<Flt64>.InvokeAsync(linearParent)).Value;
        using QuadraticMechanismModel<Flt64> quadratic = (await QuadraticMechanismModel<Flt64>.InvokeAsync(quadraticParent)).Value;
        linear.RestoreSolverSolution(null, Array.Empty<Flt64>()).IsFailed.Should().BeTrue();
        quadratic.RestoreSolverSolution(null, Array.Empty<Flt64>()).IsFailed.Should().BeTrue();
        linear.ValidateSolverTokenSnapshot(null).IsFailed.Should().BeTrue();
        quadratic.ValidateSolverTokenSnapshot(null).IsFailed.Should().BeTrue();
    }

    [Fact]
    public void DumpedMatrix_ShouldNotKeepSourceModelOrTokensAlive() {
        (IDisposable matrix, WeakReference source, WeakReference parent, WeakReference token) = CreateOrphanedMatrix();
        using (matrix) {
            for (int i = 0; i < 3 && (source.IsAlive || parent.IsAlive || token.IsAlive); i++) {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
            source.IsAlive.Should().BeFalse();
            parent.IsAlive.Should().BeFalse();
            token.IsAlive.Should().BeFalse();
            GC.KeepAlive(matrix);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (IDisposable Matrix, WeakReference Source, WeakReference Parent, WeakReference Token) CreateOrphanedMatrix() {
        var parent = new LinearMetaModel<Flt64>();
        var x = new RealVar("orphan-x");
        parent.Add(x).IsOk.Should().BeTrue();
        LinearMechanismModel<Flt64> mechanism = Task.Run(async () =>
            (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value).GetAwaiter().GetResult();
        LinearTriadModel matrix = mechanism.ToLinearTriadModel(
            new Dictionary<IVariableItem, Flt64> { [x] = Flt64.One }
        ).Value;
        return (matrix, new WeakReference(mechanism), new WeakReference(parent), new WeakReference(parent.Tokens.Find(x)!));
    }

    private static AbstractMetaModel<Flt64> Parent(bool quadratic, bool manual) => quadratic
        ? new QuadraticMetaModel<Flt64>(configuration: new MetaModelConfiguration(ManualTokenAddition: manual))
        : new LinearMetaModel<Flt64>(configuration: new MetaModelConfiguration(ManualTokenAddition: manual));

    private static async Task<IMechanismModel<Flt64>> Build(AbstractMetaModel<Flt64> parent) =>
        parent is LinearMetaModel<Flt64> linear
            ? (await LinearMechanismModel<Flt64>.InvokeAsync(linear)).Value
            : (await QuadraticMechanismModel<Flt64>.InvokeAsync((QuadraticMetaModel<Flt64>)parent)).Value;

    private static IDisposable Dump(IMechanismModel<Flt64> model, IReadOnlyDictionary<IVariableItem, Flt64>? fixedValues) => model switch {
        LinearMechanismModel<Flt64> linear => linear.ToLinearTriadModel(fixedValues).Value,
        QuadraticMechanismModel<Flt64> quadratic => quadratic.ToQuadraticTetradModel(fixedValues).Value,
        _ => throw new InvalidOperationException("unexpected mechanism model"),
    };

    private static Ret Restore(
        IMechanismModel<Flt64> model,
        IDisposable matrix,
        IReadOnlyList<Flt64>? solution) => (model, matrix) switch {
        (LinearMechanismModel<Flt64> linear, LinearTriadModel triad) => linear.RestoreSolverSolution(triad, solution),
        (QuadraticMechanismModel<Flt64> quadratic, QuadraticTetradModel tetrad) => quadratic.RestoreSolverSolution(tetrad, solution),
        _ => throw new InvalidOperationException("unexpected mechanism or matrix model"),
    };

    private static Try ValidateSnapshot(IMechanismModel<Flt64> model, IDisposable matrix) => (model, matrix) switch {
        (LinearMechanismModel<Flt64> linear, LinearTriadModel triad) => linear.ValidateSolverTokenSnapshot(triad),
        (QuadraticMechanismModel<Flt64> quadratic, QuadraticTetradModel tetrad) => quadratic.ValidateSolverTokenSnapshot(tetrad),
        _ => throw new InvalidOperationException("unexpected mechanism or matrix model"),
    };

    private static Try Apply(
        IMechanismModel<Flt64> model,
        IDisposable matrix,
        IReadOnlyList<Flt64>? solution) => (model, matrix) switch {
        (LinearMechanismModel<Flt64> linear, LinearTriadModel triad) => linear.ApplySolverSolution(
            matrix: triad,
            solution: solution
        ),
        (QuadraticMechanismModel<Flt64> quadratic, QuadraticTetradModel tetrad) => quadratic.ApplySolverSolution(
            matrix: tetrad,
            solution: solution
        ),
        _ => throw new InvalidOperationException("unexpected mechanism or matrix model"),
    };

    private static IReadOnlyList<ModelViewVariable> Columns(IDisposable matrix) => matrix switch {
        LinearTriadModel triad => triad.Variables,
        QuadraticTetradModel tetrad => tetrad.Variables,
        _ => throw new InvalidOperationException("unexpected matrix model"),
    };

    private static MechanismModelSolutionMapping Mapping(IDisposable matrix) => matrix switch {
        LinearTriadModel triad => triad.SolutionMapping ?? throw new InvalidOperationException("missing mapping"),
        QuadraticTetradModel tetrad => tetrad.SolutionMapping ?? throw new InvalidOperationException("missing mapping"),
        _ => throw new InvalidOperationException("unexpected matrix model"),
    };

    private static IDisposable CopyWithoutProvenance(IDisposable matrix) => matrix switch {
        LinearTriadModel triad => new LinearTriadModel(
            variables: triad.Variables,
            constraints: (LinearConstraintBatch)triad.Constraints.Copy(),
            objective: triad.Objective,
            name: triad.Name
        ),
        QuadraticTetradModel tetrad => new QuadraticTetradModel(
            variables: tetrad.Variables,
            constraints: (QuadraticConstraintBatch)tetrad.Constraints.Copy(),
            objective: tetrad.Objective,
            name: tetrad.Name
        ),
        _ => throw new InvalidOperationException("unexpected matrix model"),
    };

    private sealed class CallbackSolution(Action callback) : IReadOnlyList<Flt64> {
        public int Count => 1;
        public Flt64 this[int index] {
            get {
                callback();
                return Flt64.One;
            }
        }
        public IEnumerator<Flt64> GetEnumerator() => ((IEnumerable<Flt64>)new[] { Flt64.One }).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class GrowingSolution : IReadOnlyList<Flt64> {
        private int _reads;
        public int Count => ++_reads == 1 ? 1 : 2;
        public Flt64 this[int index] => Flt64.One;
        public IEnumerator<Flt64> GetEnumerator() => ((IEnumerable<Flt64>)new[] { Flt64.One }).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
