#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Algebra.ValueRange;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
namespace Fuookami.Ospf.Core.Tests.Symbol.Function;

public class UnivariatePiecewiseLifecycleTest {
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChangedNestedInputAfterBuild_ShouldRejectDumpAndSolution(bool quadratic) {
        (AbstractMetaModel<Flt64> parent, _, LinearExpressionSymbol nested, _, RealVar x) = Setup(quadratic);
        using (parent) {
            parent.Tokens.Find(x)!.SetResult(Flt64.One);
            using IMechanismModel<Flt64> model = await Build(parent);
            using IDisposable matrix = Dump(model);

            nested.AsMutable().SetConstant(Flt64.One);

            Assert.True(DumpFails(model));
            Assert.True(ValidateFails(model, matrix));
            Assert.True(ApplyFails(model, matrix));
            Assert.Equal(Flt64.One, parent.Tokens.Find(x)!.Result);

            nested.AsMutable().SetConstant(Flt64.Zero);
            Assert.False(ValidateFails(model, matrix));
        }
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(false, -1)]
    [InlineData(false, 2)]
    [InlineData(false, -2)]
    [InlineData(false, 5)]
    [InlineData(false, -5)]
    [InlineData(false, 3)]
    [InlineData(false, -3)]
    [InlineData(false, 4)]
    [InlineData(false, -4)]
    [InlineData(false, 7)]
    [InlineData(false, -7)]
    [InlineData(false, 11)]
    [InlineData(false, -11)]
    [InlineData(false, 23)]
    [InlineData(false, -23)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    [InlineData(true, -1)]
    [InlineData(true, 2)]
    [InlineData(true, -2)]
    [InlineData(true, 5)]
    [InlineData(true, -5)]
    [InlineData(true, 3)]
    [InlineData(true, -3)]
    [InlineData(true, 4)]
    [InlineData(true, -4)]
    [InlineData(true, 7)]
    [InlineData(true, -7)]
    [InlineData(true, 11)]
    [InlineData(true, -11)]
    [InlineData(true, 23)]
    [InlineData(true, -23)]
    public async Task ChangedRowValueConversionAfterBuild_ShouldRejectDumpAndSolution(bool quadratic, double changedValue) {
        (AbstractMetaModel<Flt64> parent, UnivariateLinearPiecewiseFunction<Flt64> function,
            _, SwitchingConverter converter, RealVar x) = Setup(quadratic);
        using (parent) {
            parent.Tokens.Find(x)!.SetResult(Flt64.One);
            using IMechanismModel<Flt64> model = await Build(parent);
            using IDisposable matrix = Dump(model);

            converter.CorruptValue = changedValue;

            Assert.True(DumpFails(model));
            Assert.True(ValidateFails(model, matrix));
            Assert.True(ApplyFails(model, matrix));
            Assert.Equal(Flt64.One, parent.Tokens.Find(x)!.Result);
            using var tokens = new ManualTokenList<Flt64>();
            Assert.True(function.RegisterAuxiliaryTokens(tokens).IsFailed);
            Assert.Empty(tokens.Tokens);

            converter.CorruptValue = null;
            Assert.False(ValidateFails(model, matrix));
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ChangedConverterIdentityAfterBuild_ShouldRejectDumpAndSolution(bool quadratic, bool zero) {
        (AbstractMetaModel<Flt64> parent, _, _, SwitchingConverter converter, RealVar x) = Setup(quadratic);
        using (parent) {
            parent.Tokens.Find(x)!.SetResult(Flt64.One);
            using IMechanismModel<Flt64> model = await Build(parent);
            using IDisposable matrix = Dump(model);

            converter.CorruptZero = zero;
            converter.CorruptOne = !zero;

            Assert.True(DumpFails(model));
            Assert.True(ValidateFails(model, matrix));
            Assert.True(ApplyFails(model, matrix));
            Assert.Equal(Flt64.One, parent.Tokens.Find(x)!.Result);

            converter.CorruptZero = false;
            converter.CorruptOne = false;
            Assert.False(ValidateFails(model, matrix));
        }
    }

    [Fact]
    public async Task ChangedAutomaticBigMConversionAfterBuild_ShouldRejectOldMatrix() {
        (AbstractMetaModel<Flt64> parent, UnivariateLinearPiecewiseFunction<Flt64> function,
            _, SwitchingConverter converter, _) = Setup(
                quadratic: false,
                registry: null,
                automaticBigM: true
            );
        using (parent) {
            using IMechanismModel<Flt64> model = await Build(parent);
            using IDisposable matrix = Dump(model);
            double bigM = model.Constraints.Single(row => row.Name == $"{function.Name}_seg_0_eq_ub").Lhs
                .Cast<ILinearCell<Flt64>>()
                .Single(cell => cell.Token.Variable.Key == function.InactiveVars[0].Key).Coefficient.Abs().ToDouble();
            Assert.DoesNotContain(bigM, new[] { 0.0, 1.0, 2.0, 3.0, 4.0, 5.0, 7.0, 11.0, 23.0 });

            converter.CorruptValue = -bigM;

            Assert.True(DumpFails(model));
            Assert.True(ValidateFails(model, matrix));
            Assert.True(ApplyFails(model, matrix));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChangedMergedInputCoefficientConversion_ShouldRejectOldMatrix(bool quadratic) {
        var x = new RealVar("pwl-merged-coefficient-x");
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-2), new Flt64(6)).Value);
        var converter = new SwitchingConverter();
        UnivariateLinearPiecewiseFunction<Flt64> function = UnivariateLinearPiecewiseFunction<Flt64>.Create(
            input: new LinearPolynomial<Flt64>(new[] {
                new LinearMonomial<Flt64>(new Flt64(2), x),
                new LinearMonomial<Flt64>(new Flt64(7), x),
            }, Flt64.Zero),
            breakpoints: new[] { Flt64.Zero, new Flt64(2), new Flt64(5) },
            slopes: new[] { new Flt64(3), new Flt64(-4) },
            intercepts: new[] { new Flt64(7), new Flt64(11) },
            converter: converter,
            bigM: new Flt64(23)
        ).Value;
        using AbstractMetaModel<Flt64> parent = quadratic
            ? new QuadraticMetaModel<Flt64>(converter: converter)
            : new LinearMetaModel<Flt64>(converter: converter);
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk);
        using IMechanismModel<Flt64> model = await Build(parent);
        using IDisposable matrix = Dump(model);

        converter.CorruptValue = -9;

        Assert.True(DumpFails(model));
        Assert.True(ValidateFails(model, matrix));
        Assert.True(ApplyFails(model, matrix));
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    public async Task ChangedDependencyBeforeCommit_ShouldRollBackHelpersAndIdentities(bool quadratic, int change) {
        var registry = new ModelElementIdentityRegistry();
        (AbstractMetaModel<Flt64> parent, UnivariateLinearPiecewiseFunction<Flt64> function,
            LinearExpressionSymbol nested, SwitchingConverter converter, RealVar x) = Setup(quadratic, registry);
        using (parent) {
            ValueRange<Flt64> originalRange = function.ResultVar.Range.Range!;
            Token<Flt64> original = parent.Tokens.Find(x)!;
            Assert.All(function.HelperVariables, helper => Assert.Null(parent.Tokens.Find(helper)));
            bool callbackRan = false;

            Result<Success, ErrorCode, Error<ErrorCode>> Change<TMatrix>(Result<TMatrix, ErrorCode, Error<ErrorCode>> dumped)
                where TMatrix : class, IDisposable {
                Assert.True(dumped.IsOk, dumped.ToString());
                using TMatrix matrix = dumped.Value;
                Assert.NotEmpty(registry.Entries());
                callbackRan = true;
                if (change == 0) {
                    nested.AsMutable().SetConstant(Flt64.One);
                }
                else if (change == 1) {
                    converter.CorruptValue = -23;
                }
                else {
                    function.ResultVar.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-20), new Flt64(30)).Value);
                }
                return Results.OkInstance;
            }

            Result<IMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> result = quadratic
                ? (await QuadraticMechanismModel<Flt64>.InvokeBeforeCommitAsync(
                    metaModel: (QuadraticMetaModel<Flt64>)parent,
                    fixedVariables: null,
                    registrationStatusCallBack: null,
                    beforeCommit: model => Change(model.ToQuadraticTetradModel())
                )).Map<IMechanismModel<Flt64>>(model => model)
                : (await LinearMechanismModel<Flt64>.InvokeBeforeCommitAsync(
                    metaModel: (LinearMetaModel<Flt64>)parent,
                    fixedVariables: null,
                    registrationStatusCallBack: null,
                    beforeCommit: model => Change(model.ToLinearTriadModel())
                )).Map<IMechanismModel<Flt64>>(model => model);
            if (result.IsOk) {
                result.Value.Dispose();
            }
            Assert.True(callbackRan);
            Assert.True(result.IsFailed);
            Assert.Same(original, parent.Tokens.Find(x));
            Assert.All(function.HelperVariables, helper => Assert.Null(parent.Tokens.Find(helper)));
            Assert.Empty(registry.Entries());

            nested.AsMutable().SetConstant(Flt64.Zero);
            converter.CorruptValue = null;
            function.ResultVar.Range.SetRange(originalRange);
            using IMechanismModel<Flt64> retry = await Build(parent);
            Assert.All(function.HelperVariables, helper => Assert.NotNull(parent.Tokens.Find(helper)));
        }
    }

    private static (AbstractMetaModel<Flt64>, UnivariateLinearPiecewiseFunction<Flt64>, LinearExpressionSymbol, SwitchingConverter, RealVar)
        Setup(bool quadratic, ModelElementIdentityRegistry? registry = null, bool automaticBigM = false) {
        var x = new RealVar("pwl-lifecycle-x");
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-2), new Flt64(6)).Value);
        var nested = new LinearExpressionSymbol(
            name: "pwl-lifecycle-input",
            polynomial: new MutableLinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(Flt64.One, x) }, Flt64.Zero)
        );
        var converter = new SwitchingConverter();
        Result<UnivariateLinearPiecewiseFunction<Flt64>, ErrorCode, Error<ErrorCode>> created = UnivariateLinearPiecewiseFunction<Flt64>.Create(
            input: new LinearPolynomial<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, nested) }, Flt64.Zero),
            breakpoints: new[] { Flt64.Zero, new Flt64(2), new Flt64(5) },
            slopes: new[] { new Flt64(3), new Flt64(-4) },
            intercepts: new[] { new Flt64(7), new Flt64(11) },
            converter: converter,
            bigM: automaticBigM ? null : new Flt64(23)
        );
        Assert.True(created.IsOk, created.ToString());
        AbstractMetaModel<Flt64> parent = quadratic
            ? new QuadraticMetaModel<Flt64>(identityRegistry: registry)
            : new LinearMetaModel<Flt64>(identityRegistry: registry);
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(nested).IsOk);
        Assert.True(parent.Tokens.Add(new LinearFunctionSymbolAdapter<Flt64>(created.Value)).IsOk);
        return (parent, created.Value, nested, converter, x);
    }

    private static async Task<IMechanismModel<Flt64>> Build(AbstractMetaModel<Flt64> parent) {
        if (parent is QuadraticMetaModel<Flt64> quadratic) {
            Result<QuadraticMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> result = await QuadraticMechanismModel<Flt64>.InvokeAsync(quadratic);
            Assert.True(result.IsOk, result.ToString());
            return result.Value;
        }
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> linear = await LinearMechanismModel<Flt64>.InvokeAsync((LinearMetaModel<Flt64>)parent);
        Assert.True(linear.IsOk, linear.ToString());
        return linear.Value;
    }

    private static IDisposable Dump(IMechanismModel<Flt64> model) => model switch {
        QuadraticMechanismModel<Flt64> q => q.ToQuadraticTetradModel().Value,
        LinearMechanismModel<Flt64> l => l.ToLinearTriadModel().Value,
        _ => throw new InvalidOperationException(),
    };

    private static bool DumpFails(IMechanismModel<Flt64> model) => model switch {
        QuadraticMechanismModel<Flt64> q => DisposeOrFail(q.ToQuadraticTetradModel()),
        LinearMechanismModel<Flt64> l => DisposeOrFail(l.ToLinearTriadModel()),
        _ => false,
    };

    private static bool DisposeOrFail<T>(Result<T, ErrorCode, Error<ErrorCode>> result) where T : IDisposable {
        if (result.IsOk) {
            result.Value.Dispose();
        }
        return result.IsFailed;
    }

    private static bool ValidateFails(IMechanismModel<Flt64> model, IDisposable matrix) => (model, matrix) switch {
        (QuadraticMechanismModel<Flt64> q, QuadraticTetradModel t) => q.ValidateSolverTokenSnapshot(t).IsFailed,
        (LinearMechanismModel<Flt64> l, LinearTriadModel t) => l.ValidateSolverTokenSnapshot(t).IsFailed,
        _ => false,
    };

    private static bool ApplyFails(IMechanismModel<Flt64> model, IDisposable matrix) => (model, matrix) switch {
        (QuadraticMechanismModel<Flt64> q, QuadraticTetradModel t) => q.ApplySolverSolution(t, new Flt64[q.Tokens.TokensInSolver.Count]).IsFailed,
        (LinearMechanismModel<Flt64> l, LinearTriadModel t) => l.ApplySolverSolution(t, new Flt64[l.Tokens.TokensInSolver.Count]).IsFailed,
        _ => false,
    };

    private sealed class SwitchingConverter : IIntoValue<Flt64> {
        public double? CorruptValue { get; set; }
        public bool CorruptZero { get; set; }
        public bool CorruptOne { get; set; }
        public Flt64 Zero => CorruptZero ? Flt64.One : Flt64.Zero;
        public Flt64 One => CorruptOne ? Flt64.Zero : Flt64.One;
        public Flt64 IntoValue(Flt64 value) => value;
        public Flt64 FromValue(Flt64 value) => value.ToDouble() == CorruptValue ? new Flt64(value.ToDouble() + 0.125) : value;
    }
}
