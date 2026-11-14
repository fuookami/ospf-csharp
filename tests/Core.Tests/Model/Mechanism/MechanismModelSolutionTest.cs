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
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
namespace Fuookami.Ospf.Core.Tests.Model.Mechanism;

public class MechanismModelSolutionTest {
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Apply_ShouldWriteCompleteSolutionAndFlushBothTables(bool quadratic, bool manual) {
        using AbstractMetaModel<Flt64> parent = Parent(quadratic, manual);
        var x = new RealVar("solution-x");
        var y = new RealVar("solution-y");
        parent.Add(new IVariableItem[] { x, y }).IsOk.Should().BeTrue();
        using IMechanismModel<Flt64> model = await Build(parent);
        object key = new();
        parent.Tokens.Cache(
            cacheKey: key,
            solution: (IReadOnlyList<Flt64>?)null,
            value: Flt64.One
        );
        model.Tokens.Cache(
            cacheKey: key,
            solution: (IReadOnlyList<Flt64>?)null,
            value: Flt64.One
        );

        Apply(model, new[] { new Flt64(2), new Flt64(3) }).IsOk.Should().BeTrue();

        parent.Tokens.Find(x)!.Result.Should().Be(new Flt64(2));
        parent.Tokens.Find(y)!.Result.Should().Be(new Flt64(3));
        model.Tokens.Find(x)!.Result.Should().Be(new Flt64(2));
        model.Tokens.Find(y)!.Result.Should().Be(new Flt64(3));
        parent.Tokens.Cached(key, (IReadOnlyList<Flt64>?)null).Should().BeFalse();
        model.Tokens.Cached(key, (IReadOnlyList<Flt64>?)null).Should().BeFalse();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Apply_ShouldRejectReplacedTokenEvenWhenOrderDoesNotChange(bool quadratic, bool manual) {
        using AbstractMetaModel<Flt64> parent = Parent(quadratic, manual);
        var x = new RealVar("replaced-x");
        var y = new RealVar("replaced-y");
        parent.Add(new IVariableItem[] { x, y }).IsOk.Should().BeTrue();
        parent.Tokens.SetSolverSolution(new[] { new Flt64(1), new Flt64(2) });
        Token<Flt64> original = parent.Tokens.Find(y)!;
        using IMechanismModel<Flt64> model = await Build(parent);
        parent.Add(y).IsOk.Should().BeTrue();
        object key = new();
        parent.Tokens.Cache(
            cacheKey: key,
            solution: (IReadOnlyList<Flt64>?)null,
            value: Flt64.One
        );
        model.Tokens.Cache(
            cacheKey: key,
            solution: (IReadOnlyList<Flt64>?)null,
            value: Flt64.One
        );

        Apply(model, new[] { new Flt64(11), new Flt64(22) }).IsFailed.Should().BeTrue();

        parent.Tokens.Find(x)!.Result.Should().Be(Flt64.One);
        parent.Tokens.Find(y)!.Result.Should().BeNull();
        original.Result.Should().Be(new Flt64(2));
        parent.Tokens.Cached(key, (IReadOnlyList<Flt64>?)null).Should().BeTrue();
        model.Tokens.Cached(key, (IReadOnlyList<Flt64>?)null).Should().BeTrue();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Apply_ShouldRejectAddedOrRemovedColumns(bool quadratic, bool remove) {
        using AbstractMetaModel<Flt64> parent = Parent(quadratic, manual: false);
        var x = new RealVar("count-x");
        var y = new RealVar("count-y");
        parent.Add(new IVariableItem[] { x, y }).IsOk.Should().BeTrue();
        parent.Tokens.SetSolverSolution(new[] { Flt64.One, new Flt64(2) });
        using IMechanismModel<Flt64> model = await Build(parent);
        if (remove) {
            parent.Remove(y);
        }
        else {
            parent.Add(new RealVar("count-z")).IsOk.Should().BeTrue();
        }

        Apply(model, new[] { new Flt64(11), new Flt64(22) }).IsFailed.Should().BeTrue();
        model.Tokens.Find(x)!.Result.Should().Be(Flt64.One);
        model.Tokens.Find(y)!.Result.Should().Be(new Flt64(2));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Apply_ShouldRejectInvalidLengthAndNonfiniteValuesWithoutPartialWrites(bool quadratic) {
        using AbstractMetaModel<Flt64> parent = Parent(quadratic, manual: false);
        var x = new RealVar("invalid-x");
        var y = new RealVar("invalid-y");
        parent.Add(new IVariableItem[] { x, y }).IsOk.Should().BeTrue();
        parent.Tokens.SetSolverSolution(new[] { new Flt64(1), new Flt64(2) });
        using IMechanismModel<Flt64> model = await Build(parent);
        object key = new();
        parent.Tokens.Cache(
            cacheKey: key,
            solution: (IReadOnlyList<Flt64>?)null,
            value: Flt64.One
        );
        model.Tokens.Cache(
            cacheKey: key,
            solution: (IReadOnlyList<Flt64>?)null,
            value: Flt64.One
        );

        IReadOnlyList<Flt64>[] invalid = {
            new[] { new Flt64(10) },
            new[] { new Flt64(10), new Flt64(20), new Flt64(30) },
            new[] { new Flt64(10), new Flt64(double.NaN) },
            new[] { new Flt64(10), new Flt64(double.PositiveInfinity) },
        };
        foreach (IReadOnlyList<Flt64> values in invalid) {
            Apply(model, values).IsFailed.Should().BeTrue();
            parent.Tokens.Find(x)!.Result.Should().Be(Flt64.One);
            parent.Tokens.Find(y)!.Result.Should().Be(new Flt64(2));
            parent.Tokens.Cached(key, (IReadOnlyList<Flt64>?)null).Should().BeTrue();
            model.Tokens.Cached(key, (IReadOnlyList<Flt64>?)null).Should().BeTrue();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Apply_ShouldRejectDisposedSourceOrParent(bool quadratic) {
        var x = new RealVar("closed-x");
        using AbstractMetaModel<Flt64> first = Parent(quadratic, manual: false);
        first.Add(x).IsOk.Should().BeTrue();
        IMechanismModel<Flt64> disposed = await Build(first);
        disposed.Dispose();
        Apply(disposed, new[] { Flt64.One }).Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.ApplicationStopped);
        first.Tokens.Find(x)!.Result.Should().BeNull();

        using AbstractMetaModel<Flt64> second = Parent(quadratic, manual: false);
        second.Add(x).IsOk.Should().BeTrue();
        using IMechanismModel<Flt64> model = await Build(second);
        second.Close();
        Apply(model, new[] { Flt64.One }).Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.ApplicationStopped);
        model.Tokens.Find(x)!.Result.Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Apply_ShouldRejectUnreadableSolutionOrClosureDuringEnumeration(bool quadratic) {
        using AbstractMetaModel<Flt64> first = Parent(quadratic, manual: false);
        var x = new RealVar("input-x");
        first.Add(x).IsOk.Should().BeTrue();
        using IMechanismModel<Flt64> model = await Build(first);
        Apply(model, new CallbackList(() => throw new InvalidOperationException("unreadable")))
            .Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.IllegalArgument);
        model.Tokens.Find(x)!.Result.Should().BeNull();

        Apply(model, new CallbackList(first.Close))
            .Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.ApplicationStopped);
        model.Tokens.Find(x)!.Result.Should().BeNull();
    }

    [Fact]
    public async Task Apply_WhenAResultCallbackThrows_ShouldReturnFatal() {
        using var parent = new LinearMetaModel<Flt64>();
        var x = new RealVar("callback-x");
        parent.Add(x).IsOk.Should().BeTrue();
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        object owner = new();
        parent.Tokens.Find(x)!.RefreshCallbacks.Add(owner, _ => throw new InvalidOperationException("callback"));

        model.ApplySolverSolution(new[] { Flt64.One })
            .Should().BeOfType<Fatal<Success, ErrorCode, Error<ErrorCode>>>()
            .Which.Errors.Should().ContainSingle().Which.Message.Should().Contain("rebuild");
        parent.Tokens.Find(x)!.RefreshCallbacks.Remove(owner);
    }

    [Fact]
    public async Task Apply_ShouldRejectNullArguments() {
        LinearMechanismModel<Flt64>? missing = null;
        missing.ApplySolverSolution(new[] { Flt64.One }).IsFailed.Should().BeTrue();
        using var parent = new LinearMetaModel<Flt64>();
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        model.ApplySolverSolution(null).IsFailed.Should().BeTrue();
    }

    private static AbstractMetaModel<Flt64> Parent(bool quadratic, bool manual) => quadratic
        ? new QuadraticMetaModel<Flt64>(configuration: new MetaModelConfiguration(ManualTokenAddition: manual))
        : new LinearMetaModel<Flt64>(configuration: new MetaModelConfiguration(ManualTokenAddition: manual));

    private static async Task<IMechanismModel<Flt64>> Build(AbstractMetaModel<Flt64> parent) =>
        parent is LinearMetaModel<Flt64> linear
            ? (await LinearMechanismModel<Flt64>.InvokeAsync(linear)).Value
            : (await QuadraticMechanismModel<Flt64>.InvokeAsync((QuadraticMetaModel<Flt64>)parent)).Value;

    private static Try Apply(IMechanismModel<Flt64> model, IReadOnlyList<Flt64> values) => model switch {
        LinearMechanismModel<Flt64> linear => linear.ApplySolverSolution(values),
        QuadraticMechanismModel<Flt64> quadratic => quadratic.ApplySolverSolution(values),
        _ => Results.Failed<Success>(new Err<ErrorCode>(ErrorCode.IllegalArgument, "unexpected model")),
    };

    private sealed class CallbackList(Action callback) : IReadOnlyList<Flt64> {
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
}
