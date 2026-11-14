#nullable enable

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Variable;
namespace Fuookami.Ospf.Core.Tests.Symbol.Function;

/// <summary>相同状态结果编码契约 / Same-status result encoding contract.</summary>
public class SameAsResultEncodingTest {
    [Theory]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    [InlineData(4, true)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(4, false)]
    public async Task ResultAndDifferences_ShouldMatchEveryBooleanAssignment(int count, bool constraint) {
        RealVar[] inputs = Enumerable.Range(0, count).Select(index => new RealVar($"input-{index}")).ToArray();
        foreach (RealVar input in inputs) {
            input.Range.Geq(new Flt64(-1));
            input.Range.Leq(Flt64.One);
        }
        var function = new SameAsFunction(
            inequalities: inputs.Select(input => Polynomial(input).Le(Flt64.Zero)).ToArray(),
            epsilon: new Flt64(0.125),
            constraint: constraint,
            bigM: new Flt64(4)
        );
        using var parent = new LinearMetaModel<Flt64>();
        foreach (RealVar input in inputs) {
            Assert.True(parent.Add(input).IsOk);
        }
        Assert.True(parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk);
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built =
            await LinearMechanismModel<Flt64>.InvokeAsync(parent);
        Assert.True(built.IsOk);
        using LinearMechanismModel<Flt64> model = built.Value;
        int combinations = 1 << count;
        int differenceCombinations = 1 << function.DiffVars.Count;
        for (int inputMask = 0; inputMask < combinations; ++inputMask) {
            bool allSame = inputMask == 0 || inputMask == combinations - 1;
            var evaluationValues = new Dictionary<ISymbol, Flt64>();
            var assigned = new Dictionary<IVariableItem, Flt64>();
            int expectedDifferences = 0;
            for (int index = 0; index < count; ++index) {
                bool satisfied = (inputMask & (1 << index)) != 0;
                Flt64 inputValue = satisfied ? new Flt64(-1) : Flt64.One;
                evaluationValues[inputs[index]] = inputValue;
                assigned[inputs[index]] = inputValue;
                if (index > 0 && satisfied != ((inputMask & 1) != 0)) {
                    expectedDifferences |= 1 << (index - 1);
                }
            }
            Assert.Equal(allSame ? Flt64.One : Flt64.Zero, function.Evaluate(evaluationValues));
            for (int flagMask = 0; flagMask < combinations; ++flagMask) {
                for (int index = 0; index < count; ++index) {
                    assigned[function.SatisfactionFlags[index]] = Bit(flagMask, index);
                    assigned[function.ViolationFlags[index]!] = Bit(~flagMask, index);
                }
                for (int differenceMask = 0; differenceMask < differenceCombinations; ++differenceMask) {
                    for (int index = 0; index < function.DiffVars.Count; ++index) {
                        assigned[function.DiffVars[index]] = Bit(differenceMask, index);
                    }
                    for (int result = 0; result <= 1; ++result) {
                        assigned[function.ResultVar] = Bit(result, 0);
                        bool expected = flagMask == inputMask &&
                            (constraint
                                ? allSame && result == 1
                                : differenceMask == expectedDifferences && result == (allSame ? 1 : 0));
                        var solution = model.Tokens.TokensInSolver
                            .Select(token => assigned[token.Variable]).ToList();
                        bool feasible = ((IMechanismModel<Flt64>)model).Constraints
                            .All(row => row.IsTrue(solution) == true);
                        Assert.True(expected == feasible,
                            $"constraint={constraint}, inputs={inputMask}, flags={flagMask}, " +
                            $"differences={differenceMask}, result={result}, expected={expected}");
                    }
                }
            }
        }
    }

    [Fact]
    public void Evaluation_ShouldReturnNullForMissingInputEvenAfterUnequalFlags() {
        RealVar[] inputs = Enumerable.Range(0, 3).Select(index => new RealVar($"input-{index}")).ToArray();
        var function = new SameAsFunction(
            inequalities: inputs.Select(input => Polynomial(input).Le(Flt64.Zero)).ToArray(),
            epsilon: new Flt64(0.125),
            constraint: false
        );
        var values = new Dictionary<ISymbol, Flt64> {
            [inputs[0]] = new Flt64(-1),
            [inputs[1]] = Flt64.One,
        };

        Assert.Null(function.Evaluate(values));
        values[inputs[2]] = Flt64.One;
        Assert.Equal(Flt64.Zero, function.Evaluate(values));
        values.Remove(inputs[0]);
        Assert.Null(function.Evaluate(values));
    }

    private static Flt64 Bit(int mask, int index) =>
        (mask & (1 << index)) != 0 ? Flt64.One : Flt64.Zero;

    private static LinearPolynomial<Flt64> Polynomial(IVariableItem variable) => new(
        new[] { new LinearMonomial<Flt64>(Flt64.One, variable) },
        Flt64.Zero
    );
}
