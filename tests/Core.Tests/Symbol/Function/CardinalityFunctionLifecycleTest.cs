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
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
using NUInt8 = Fuookami.Ospf.Math.Algebra.Number.UInt8;
namespace Fuookami.Ospf.Core.Tests.Symbol.Function;

public class CardinalityFunctionLifecycleTest {
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InexactIntermediateCoefficient_ShouldRejectConstruction(bool exactly) {
        IVariableItem[] inputs = Enumerable.Range(0, 5).Select(i => new BinVar($"x{i}")).ToArray();
        var converter = new SwitchingConverter { CorruptInput = 3 };
        bool failed = exactly
            ? ExactlyFunction<Flt64>.Create(
                indicators: inputs,
                amount: 2,
                converter: converter
            ).IsFailed
            : AtMostFunction<Flt64>.Create(
                indicators: inputs,
                limit: 2,
                converter: converter
            ).IsFailed;
        Assert.True(failed);
    }

    [Fact]
    public void DuplicateHelper_ShouldNotPartiallyRegister() {
        var x = new BinVar("duplicate-x");
        ExactlyFunction<Flt64> function = ExactlyFunction<Flt64>.Create(
            indicators: new[] { x, x },
            amount: 1,
            converter: IIntoValue<Flt64>.Identity
        ).Value;
        using var parent = new LinearMetaModel<Flt64>(configuration: new MetaModelConfiguration(CheckTokenExists: true));
        IVariableItem existing = function.HelperVariables[1];
        Assert.True(parent.Add(existing).IsOk);
        Token<Flt64>? original = parent.Tokens.Find(existing);
        Assert.True(function.RegisterAuxiliaryTokens(parent.Tokens).IsFailed);
        Assert.Same(original, parent.Tokens.Find(existing));
        Assert.Null(parent.Tokens.Find(function.ResultVar));
        Assert.Null(parent.Tokens.Find(function.HelperVariables[2]));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public async Task FixedInputOutsideCapturedRange_ShouldNotPublishEvidence(int fixedValue, bool valid) {
        var x = new BinVar("bounded-x");
        x.Range.SetRange(ValueRange<NUInt8>.Of(new NUInt8(0), new NUInt8(0)).Value);
        AtMostFunction<Flt64> function = AtMostFunction<Flt64>.Create(
            indicators: new[] { x },
            limit: 0,
            converter: IIntoValue<Flt64>.Identity
        ).Value;
        var adapter = new LinearFunctionSymbolAdapter<Flt64>(function);
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Tokens.Add(adapter).IsOk);
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built = await LinearMechanismModel<Flt64>.InvokeAsync(
            metaModel: parent,
            fixedVariables: new Dictionary<IVariableItem, Flt64> { [x] = new Flt64(fixedValue) }
        );
        Assert.True(built.IsOk, built.ToString());
        using LinearMechanismModel<Flt64> model = built.Value;
        Assert.Equal(valid ? Flt64.One : null, model.Tokens.CachedValue(adapter, (IReadOnlyList<Flt64>?)null));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DifferentModelConverter_ShouldRejectBuildAndPreparation(bool quadratic) {
        var x = new BinVar("incompatible-converter-x");
        AtMostFunction<Flt64> function = AtMostFunction<Flt64>.Create(
            indicators: new[] { x, x },
            limit: 1,
            converter: IIntoValue<Flt64>.Identity
        ).Value;
        var adapter = new LinearFunctionSymbolAdapter<Flt64>(function);
        var converter = new SwitchingConverter { CorruptOutput = -2 };
        var values = new Dictionary<ISymbol, Flt64> { [x] = Flt64.One };
        Assert.Null(adapter.TryPrepare(
            inputs: values,
            modelValues: values,
            converter: converter
        ).Value);
        using AbstractMetaModel<Flt64> parent = quadratic
            ? new QuadraticMetaModel<Flt64>(converter: converter)
            : new LinearMetaModel<Flt64>(converter: converter);
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Tokens.Add(adapter).IsOk);
        Result<IMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built = quadratic
            ? (await QuadraticMechanismModel<Flt64>.InvokeAsync((QuadraticMetaModel<Flt64>)parent)).Map<IMechanismModel<Flt64>>(model => model)
            : (await LinearMechanismModel<Flt64>.InvokeAsync((LinearMetaModel<Flt64>)parent)).Map<IMechanismModel<Flt64>>(model => model);
        if (built.IsOk) {
            built.Value.Dispose();
        }
        Assert.True(built.IsFailed);
        Assert.All(function.HelperVariables, helper => Assert.Null(parent.Tokens.Find(helper)));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ChangedConverterAfterBuild_ShouldRejectDumpAndSolution(bool quadratic, bool modelConverter) {
        var input = new BinVar("converter-x");
        var functionConverter = new SwitchingConverter();
        SwitchingConverter parentConverter = modelConverter ? new SwitchingConverter() : functionConverter;
        AtMostFunction<Flt64> function = AtMostFunction<Flt64>.Create(
            indicators: new[] { input, input, input },
            limit: 1,
            converter: functionConverter
        ).Value;
        using AbstractMetaModel<Flt64> parent = quadratic
            ? new QuadraticMetaModel<Flt64>(converter: parentConverter)
            : new LinearMetaModel<Flt64>(converter: parentConverter);
        Assert.True(parent.Add(input).IsOk);
        Assert.True(parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk);
        if (quadratic) {
            using QuadraticMechanismModel<Flt64> model = (await QuadraticMechanismModel<Flt64>.InvokeAsync((QuadraticMetaModel<Flt64>)parent)).Value;
            using QuadraticTetradModel matrix = model.ToQuadraticTetradModel().Value;
            (modelConverter ? parentConverter : functionConverter).CorruptOutput = -3;
            Assert.True(model.ToQuadraticTetradModel().IsFailed);
            Assert.True(model.ValidateSolverTokenSnapshot(matrix).IsFailed);
            Assert.True(model.ApplySolverSolution(matrix, new Flt64[model.Tokens.TokensInSolver.Count]).IsFailed);
        }
        else {
            using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync((LinearMetaModel<Flt64>)parent)).Value;
            using LinearTriadModel matrix = model.ToLinearTriadModel().Value;
            (modelConverter ? parentConverter : functionConverter).CorruptOutput = -3;
            Assert.True(model.ToLinearTriadModel().IsFailed);
            Assert.True(model.ValidateSolverTokenSnapshot(matrix).IsFailed);
            Assert.True(model.ApplySolverSolution(matrix, new Flt64[model.Tokens.TokensInSolver.Count]).IsFailed);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChangedHelperBeforeCommit_ShouldRollBackPreparedHelpers(bool quadratic) {
        var x = new BinVar("commit-x");
        ExactlyFunction<Flt64> function = ExactlyFunction<Flt64>.Create(
            indicators: new[] { x, x },
            amount: 1,
            converter: IIntoValue<Flt64>.Identity
        ).Value;
        using AbstractMetaModel<Flt64> parent = quadratic ? new QuadraticMetaModel<Flt64>() : new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Tokens.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk);
        Assert.All(function.HelperVariables, helper => Assert.Null(parent.Tokens.Find(helper)));
        bool called = false;
        Result<IMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built = quadratic
            ? (await QuadraticMechanismModel<Flt64>.InvokeBeforeCommitAsync(
                metaModel: (QuadraticMetaModel<Flt64>)parent,
                fixedVariables: null,
                registrationStatusCallBack: null,
                beforeCommit: _ => Change()
            )).Map<IMechanismModel<Flt64>>(model => model)
            : (await LinearMechanismModel<Flt64>.InvokeBeforeCommitAsync(
                metaModel: (LinearMetaModel<Flt64>)parent,
                fixedVariables: null,
                registrationStatusCallBack: null,
                beforeCommit: _ => Change()
            )).Map<IMechanismModel<Flt64>>(model => model);
        if (built.IsOk) {
            built.Value.Dispose();
        }
        Assert.True(called);
        Assert.True(built.IsFailed);
        Assert.All(function.HelperVariables, helper => Assert.Null(parent.Tokens.Find(helper)));

        Result<Success, ErrorCode, Error<ErrorCode>> Change() {
            called = true;
            function.ResultVar.Range.SetRange(ValueRange<NUInt8>.Of(new NUInt8(0), new NUInt8(0)).Value);
            return Results.OkInstance;
        }
    }

    private sealed class SwitchingConverter : IIntoValue<Flt64> {
        public double? CorruptInput { get; set; }
        public double? CorruptOutput { get; set; }
        public Flt64 Zero => Flt64.Zero;
        public Flt64 One => Flt64.One;
        public Flt64 IntoValue(Flt64 value) => value.ToDouble() == CorruptInput ? Flt64.One : value;
        public Flt64 FromValue(Flt64 value) => value.ToDouble() == CorruptOutput ? Flt64.Zero : value;
    }
}
