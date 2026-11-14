#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Variable;
using NInt64 = Fuookami.Ospf.Math.Algebra.Number.Int64;
namespace Fuookami.Ospf.Core.Tests.Symbol.Function;

public class CardinalityFunctionTest {
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task AllSmallAssignments_ShouldExactlyDetermineTheIndicator(bool exactly, bool quadratic) {
        for (int count = 0; count <= 3; ++count) {
            for (int amount = 0; amount <= count; ++amount) {
                BinVar[] inputs = Enumerable.Range(0, count).Select(i => new BinVar($"x{i}")).ToArray();
                IMathFunctionSymbol<Flt64> function = Create(
                    exactly: exactly,
                    inputs: inputs,
                    amount: amount
                );
                BinVar result = ResultVariable(function);
                using AbstractMetaModel<Flt64> parent = quadratic
                    ? new QuadraticMetaModel<Flt64>() : new LinearMetaModel<Flt64>();
                foreach (BinVar input in inputs) {
                    Assert.True(parent.Add(input).IsOk);
                }
                Assert.True(parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk);
                using IAbstractLinearMechanismModel<Flt64> model = quadratic
                    ? (await QuadraticMechanismModel<Flt64>.InvokeAsync((QuadraticMetaModel<Flt64>)parent)).Value
                    : (await LinearMechanismModel<Flt64>.InvokeAsync((LinearMetaModel<Flt64>)parent)).Value;
                using IDisposable matrix = quadratic
                    ? ((QuadraticMechanismModel<Flt64>)model).ToQuadraticTetradModel().Value
                    : ((LinearMechanismModel<Flt64>)model).ToLinearTriadModel().Value;
                for (int mask = 0; mask < 1 << count; ++mask) {
                    var values = inputs.Select((input, index) => (input, value: (mask >> index) & 1))
                        .ToDictionary(item => (IVariableItem)item.input, item => new Flt64(item.value));
                    int ones = values.Values.Count(value => value == Flt64.One);
                    bool expected = exactly ? ones == amount : ones <= amount;
                    var evaluation = values.ToDictionary(pair => (ISymbol)pair.Key, pair => pair.Value);
                    Assert.Equal(expected ? Flt64.One : Flt64.Zero, function.Evaluate(evaluation));
                    for (int output = 0; output <= 1; ++output) {
                        bool feasible = false;
                        int auxiliaryCount = function.HelperVariables.Count - 1;
                        for (int branches = 0; branches < 1 << auxiliaryCount; ++branches) {
                            values[result] = new Flt64(output);
                            for (int i = 0; i < auxiliaryCount; ++i) {
                                values[function.HelperVariables[i + 1]] = new Flt64((branches >> i) & 1);
                            }
                            Flt64[] solution = model.Tokens.TokensInSolver.Select(token => values[token.Variable]).ToArray();
                            bool matches = quadratic
                                ? ((IAbstractQuadraticMechanismModel<Flt64>)model).Constraints.All(row => row.IsTrue(solution) == true)
                                : model.Constraints.All(row => row.IsTrue(solution) == true);
                            if (!quadratic) {
                                Assert.Equal(matches, SatisfiedAmountResidualTest.ExactFeasible((LinearTriadModel)matrix, values));
                            }
                            feasible |= matches;
                        }
                        Assert.Equal(output == (expected ? 1 : 0), feasible);
                    }
                }
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RepeatedInputs_ShouldCountOccurrencesAndKeepASnapshot(bool exactly) {
        var x = new BinVar("x");
        var y = new BinVar("y");
        var inputs = new List<IVariableItem> { x, y, x };
        IMathFunctionSymbol<Flt64> function = Create(
            exactly: exactly,
            inputs: inputs,
            amount: 2
        );
        inputs.Clear();
        IReadOnlyList<IVariableItem> snapshot = function switch {
            AtMostFunction<Flt64> atMost => atMost.Indicators,
            ExactlyFunction<Flt64> exact => exact.Indicators,
            _ => throw new InvalidOperationException(),
        };
        Assert.Equal(3, snapshot.Count);
        Assert.Throws<NotSupportedException>(() => ((IList<IVariableItem>)snapshot).Clear());
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(y).IsOk);
        Assert.True(parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk);
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        using LinearTriadModel matrix = model.ToLinearTriadModel().Value;
        for (int first = 0; first <= 1; ++first) {
            for (int second = 0; second <= 1; ++second) {
                var values = new Dictionary<ISymbol, Flt64> { [x] = new(first), [y] = new(second) };
                bool expected = exactly ? (2 * first) + second == 2 : (2 * first) + second <= 2;
                Assert.Equal(expected ? Flt64.One : Flt64.Zero, function.Evaluate(values));
                for (int output = 0; output <= 1; ++output) {
                    bool feasible = false;
                    for (int branches = 0; branches < 1 << (function.HelperVariables.Count - 1); ++branches) {
                        var assignment = new Dictionary<IVariableItem, Flt64> {
                            [x] = new(first),
                            [y] = new(second),
                            [ResultVariable(function)] = new(output),
                        };
                        for (int i = 1; i < function.HelperVariables.Count; ++i) {
                            assignment[function.HelperVariables[i]] = new Flt64((branches >> (i - 1)) & 1);
                        }
                        bool actual = SatisfiedAmountResidualTest.ExactFeasible(model, assignment);
                        Assert.Equal(actual, SatisfiedAmountResidualTest.ExactFeasible(matrix, assignment));
                        feasible |= actual;
                    }
                    Assert.Equal(output == (expected ? 1 : 0), feasible);
                }
            }
        }
        Assert.Contains(((IMechanismModel<Flt64>)model).Constraints, row => row.Lhs
            .Cast<ILinearCell<Flt64>>().Any(cell => cell.Token.Variable.Key == x.Key && cell.Coefficient == new Flt64(2)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingNonBinaryAndInvalidInputs_ShouldNotProduceAnIndicator(bool exactly) {
        var x = new BinVar("x");
        IMathFunctionSymbol<Flt64> function = Create(
            exactly: exactly,
            inputs: new[] { x },
            amount: 0
        );
        Assert.Null(function.Evaluate(new Dictionary<ISymbol, Flt64>()));
        Assert.Null(function.Evaluate(new Dictionary<ISymbol, Flt64> { [x] = new Flt64(0.5) }));
        Assert.Null(function.Evaluate(new Dictionary<ISymbol, Flt64> { [x] = new Flt64(double.NaN) }));
        Assert.True(AtMostFunction<Flt64>.Create(
            indicators: null,
            limit: 0,
            converter: IIntoValue<Flt64>.Identity
        ).IsFailed);
        Assert.True(ExactlyFunction<Flt64>.Create(
            indicators: new[] { x },
            amount: 2,
            converter: IIntoValue<Flt64>.Identity
        ).IsFailed);
        Assert.True(AtMostFunction<Flt64>.Create(
            indicators: new[] { new RealVar() },
            limit: 0,
            converter: IIntoValue<Flt64>.Identity
        ).IsFailed);
        Assert.True(ExactlyFunction<Flt64>.Create(
            indicators: new[] { x },
            amount: 0,
            converter: null
        ).IsFailed);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ConflictingFixedHelpers_ShouldNotCacheAResult(bool exactly, bool conflict) {
        var x = new BinVar("x");
        IMathFunctionSymbol<Flt64> function = Create(
            exactly: exactly,
            inputs: new[] { x },
            amount: 0
        );
        var adapter = new LinearFunctionSymbolAdapter<Flt64>(function);
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Tokens.Add(adapter).IsOk);
        var fixedValues = new Dictionary<IVariableItem, Flt64> { [x] = Flt64.One };
        if (conflict) {
            fixedValues[ResultVariable(function)] = Flt64.One;
        }
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built = await LinearMechanismModel<Flt64>.InvokeAsync(
            metaModel: parent,
            fixedVariables: fixedValues
        );
        Assert.True(built.IsOk, built.ToString());
        using LinearMechanismModel<Flt64> model = built.Value;
        Assert.Equal(conflict ? null : Flt64.Zero, model.Tokens.CachedValue(adapter, (IReadOnlyList<Flt64>?)null));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NestedCondition_ShouldReceiveOnlyProvenCardinalityEvidence(bool conflict) {
        var x = new BinVar("evidence-x");
        var y = new BinVar("evidence-y");
        ExactlyFunction<Flt64> function = ExactlyFunction<Flt64>.Create(
            indicators: new[] { x, y },
            amount: 1,
            converter: IIntoValue<Flt64>.Identity
        ).Value;
        var producer = new LinearFunctionSymbolAdapter<Flt64>(function);
        IfFunction<Flt64> condition = IfFunction<Flt64>.Create(
            condition: new LinearPolynomial<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, producer) }, Flt64.Zero),
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: Flt64.One
        ).Value;
        var consumer = new LinearFunctionSymbolAdapter<Flt64>(condition);
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(y).IsOk);
        Assert.True(parent.Add(producer).IsOk);
        Assert.True(parent.Add(consumer).IsOk);
        var values = new Dictionary<IVariableItem, Flt64> { [x] = Flt64.One, [y] = Flt64.Zero };
        if (conflict) {
            values[function.HelperVariables[1]] = Flt64.One;
        }
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built = await LinearMechanismModel<Flt64>.InvokeAsync(
            metaModel: parent,
            fixedVariables: values
        );
        Assert.True(built.IsOk, built.ToString());
        using LinearMechanismModel<Flt64> model = built.Value;
        Assert.Equal(conflict ? null : Flt64.One, model.Tokens.CachedValue(producer, (IReadOnlyList<Flt64>?)null));
        Assert.Equal(conflict ? null : Flt64.One, model.Tokens.CachedValue(consumer, (IReadOnlyList<Flt64>?)null));
    }

    [Fact]
    public async Task RepresentativeNumericTypes_ShouldRegisterBothFunctions() {
        await CheckNumeric(new TestNumberConverter<Flt32>(value => value.ToFlt32()));
        await CheckNumeric(new TestNumberConverter<FltX>(value => value.ToFltX()));
        await CheckNumeric(new TestNumberConverter<NInt64>(value => value.ToInt64()));
        await CheckNumeric(new TestNumberConverter<RtnX>(value => new RtnX(value.ToDouble(), 1)));
    }

    private static async Task CheckNumeric<V>(IIntoValue<V> converter)
        where V : struct, IRealNumber<V>, INumberField<V> {
        foreach (bool exactly in new[] { false, true }) {
            var x = new BinVar("x");
            var y = new BinVar("y");
            IMathFunctionSymbol<V> function = exactly
                ? ExactlyFunction<V>.Create(
                    indicators: new[] { x, y, x },
                    amount: 2,
                    converter: converter
                ).Value
                : AtMostFunction<V>.Create(
                    indicators: new[] { x, y, x },
                    limit: 2,
                    converter: converter
                ).Value;
            using var parent = new LinearMetaModel<V>(converter: converter);
            Assert.True(parent.Add(x).IsOk);
            Assert.True(parent.Add(y).IsOk);
            Assert.True(parent.Add(new LinearFunctionSymbolAdapter<V>(function)).IsOk);
            Result<LinearMechanismModel<V>, ErrorCode, Error<ErrorCode>> built = await LinearMechanismModel<V>.InvokeAsync(parent);
            Assert.True(built.IsOk, built.ToString());
            using LinearMechanismModel<V> model = built.Value;
            for (int first = 0; first <= 1; ++first) {
                for (int second = 0; second <= 1; ++second) {
                    bool expected = exactly ? (2 * first) + second == 2 : (2 * first) + second <= 2;
                    var inputs = new Dictionary<ISymbol, V> {
                        [x] = converter.IntoValue(new Flt64(first)),
                        [y] = converter.IntoValue(new Flt64(second)),
                    };
                    V? evaluation = function.Evaluate(inputs);
                    Assert.True(evaluation.HasValue);
                    Assert.Equal(expected ? 1.0 : 0.0, converter.FromValue(evaluation.Value).ToDouble());
                    bool found = false;
                    for (int helpers = 0; helpers < 1 << function.HelperVariables.Count; ++helpers) {
                        var values = new Dictionary<IVariableItem, V> {
                            [x] = inputs[x],
                            [y] = inputs[y],
                        };
                        for (int i = 0; i < function.HelperVariables.Count; ++i) {
                            values[function.HelperVariables[i]] = converter.IntoValue(new Flt64((helpers >> i) & 1));
                        }
                        V[] solution = model.Tokens.TokensInSolver.Select(token => values[token.Variable]).ToArray();
                        bool feasible = ((IMechanismModel<V>)model).Constraints.All(row => row.IsTrue(solution) == true);
                        if (feasible) {
                            Assert.Equal(expected ? 1 : 0, helpers & 1);
                            found = true;
                        }
                    }
                    Assert.True(found);
                }
            }
        }
    }

    private static IMathFunctionSymbol<Flt64> Create(bool exactly, IReadOnlyList<IVariableItem> inputs, int amount) => exactly
        ? ExactlyFunction<Flt64>.Create(
            indicators: inputs,
            amount: amount,
            converter: IIntoValue<Flt64>.Identity
        ).Value
        : AtMostFunction<Flt64>.Create(
            indicators: inputs,
            limit: amount,
            converter: IIntoValue<Flt64>.Identity
        ).Value;

    private static BinVar ResultVariable(IMathFunctionSymbol<Flt64> function) => function switch {
        ExactlyFunction<Flt64> exact => exact.ResultVar,
        AtMostFunction<Flt64> atMost => atMost.ResultVar,
        _ => throw new InvalidOperationException(),
    };
}
