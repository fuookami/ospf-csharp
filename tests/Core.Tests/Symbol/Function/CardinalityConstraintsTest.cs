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
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Variable;
using NInt8 = Fuookami.Ospf.Math.Algebra.Number.Int8;
using NInt64 = Fuookami.Ospf.Math.Algebra.Number.Int64;
namespace Fuookami.Ospf.Core.Tests.Symbol.Function;

public class CardinalityConstraintsTest {
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RowsAndMatrix_ShouldMatchEverySmallBinaryAssignment(bool exactly) {
        for (int count = 0; count <= 4; ++count) {
            for (int amount = 0; amount <= count; ++amount) {
                BinVar[] indicators = Enumerable.Range(0, count).Select(index => new BinVar($"x{index}")).ToArray();
                Result<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>> created = Build(
                    indicators: indicators,
                    amount: amount,
                    exactly: exactly
                );
                Assert.True(created.IsOk, created.ToString());
                Assert.Single(created.Value);
                using var parent = new LinearMetaModel<Flt64>();
                foreach (BinVar indicator in indicators) {
                    Assert.True(parent.Add(indicator).IsOk);
                }
                Assert.True(parent.AddConstraint(created.Value[0], group: null).IsOk);
                Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built =
                    await LinearMechanismModel<Flt64>.InvokeAsync(parent);
                Assert.True(built.IsOk, built.ToString());
                using LinearMechanismModel<Flt64> model = built.Value;
                Result<LinearTriadModel, ErrorCode, Error<ErrorCode>> dumped = model.ToLinearTriadModel();
                Assert.True(dumped.IsOk, dumped.ToString());
                using LinearTriadModel matrix = dumped.Value;
                Assert.Single(((IMechanismModel<Flt64>)model).Constraints);
                Assert.Equal(count, matrix.Variables.Count);
                for (int mask = 0; mask < 1 << count; ++mask) {
                    var values = indicators.Select((variable, index) => (variable, value: (mask >> index) & 1))
                        .ToDictionary(item => (IVariableItem)item.variable, item => new Flt64(item.value));
                    int ones = values.Values.Count(value => value == Flt64.One);
                    bool expected = exactly ? ones == amount : ones <= amount;
                    Assert.Equal(expected, SatisfiedAmountResidualTest.ExactFeasible(model, values));
                    Assert.Equal(expected, SatisfiedAmountResidualTest.ExactFeasible(matrix, values));
                }
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RepeatedInputs_ShouldCountEveryOccurrenceAndSnapshotTheList(bool exactly) {
        var x = new BinVar("x");
        var y = new BinVar("y");
        var inputs = new List<IVariableItem> { x, y, x };
        Result<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>> created = Build(
            indicators: inputs,
            amount: 2,
            exactly: exactly
        );
        Assert.True(created.IsOk, created.ToString());
        inputs.Clear();
        Assert.Equal(3, created.Value[0].Lhs.Monomials.Count);
        Assert.Throws<NotSupportedException>(() =>
            ((IList<LinearMonomial<Flt64>>)created.Value[0].Lhs.Monomials).Clear());
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(y).IsOk);
        Assert.True(parent.AddConstraint(created.Value[0], group: null).IsOk);
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        using LinearTriadModel matrix = model.ToLinearTriadModel().Value;
        for (int first = 0; first <= 1; ++first) {
            for (int second = 0; second <= 1; ++second) {
                var values = new Dictionary<IVariableItem, Flt64> {
                    [x] = new Flt64(first),
                    [y] = new Flt64(second),
                };
                bool expected = exactly ? (2 * first) + second == 2 : (2 * first) + second <= 2;
                Assert.Equal(expected, SatisfiedAmountResidualTest.ExactFeasible(model, values));
                Assert.Equal(expected, SatisfiedAmountResidualTest.ExactFeasible(matrix, values));
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Rows_ShouldAlsoRegisterInQuadraticModels(bool exactly) {
        var x = new BinVar("x");
        var y = new BinVar("y");
        BinVar[] indicators = { x, y };
        LinearInequality<Flt64> row = Build(
            indicators: indicators,
            amount: 1,
            exactly: exactly
        ).Value[0];
        using var parent = new QuadraticMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(y).IsOk);
        Assert.True(parent.AddConstraint(row, group: null).IsOk);
        Result<QuadraticMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built =
            await QuadraticMechanismModel<Flt64>.InvokeAsync(parent);
        Assert.True(built.IsOk, built.ToString());
        using QuadraticMechanismModel<Flt64> model = built.Value;
        using QuadraticTetradModel matrix = model.ToQuadraticTetradModel().Value;
        Assert.Single(((IAbstractQuadraticMechanismModel<Flt64>)model).Constraints);
        Assert.Equal(1, matrix.Constraints.Size);
        Assert.Equal(2, matrix.Variables.Count);
        foreach (int first in new[] { 0, 1 }) {
            foreach (int second in new[] { 0, 1 }) {
                var values = new Dictionary<IVariableItem, Flt64> {
                    [x] = new Flt64(first),
                    [y] = new Flt64(second),
                };
                Flt64[] solution = model.Tokens.TokensInSolver.Select(token => values[token.Variable]).ToArray();
                bool expected = exactly ? first + second == 1 : first + second <= 1;
                Assert.Equal(expected, ((IAbstractQuadraticMechanismModel<Flt64>)model).Constraints[0].IsTrue(solution));
                double sum = 0;
                foreach (QuadraticConstraintCell cell in matrix.Constraints.Lhs[0]) {
                    Assert.Equal(-1, cell.Col2);
                    IVariableItem variable = Assert.IsAssignableFrom<IVariableItem>(matrix.Variables[cell.Col1].Origin);
                    sum += cell.Coefficient.ToDouble() * values[variable].ToDouble();
                }
                Assert.Equal(expected, exactly ? sum == matrix.Constraints.Rhs[0].ToDouble() : sum <= matrix.Constraints.Rhs[0].ToDouble());
            }
        }
    }

    [Fact]
    public async Task RepresentativeNumericTypes_ShouldKeepTheCountAndComparison() {
        await VerifyNumeric(new TestNumberConverter<Flt32>(value => value.ToFlt32()));
        await VerifyNumeric(new TestNumberConverter<FltX>(value => value.ToFltX()));
        await VerifyNumeric(new TestNumberConverter<NInt64>(value => value.ToInt64()));
        await VerifyNumeric(new TestNumberConverter<RtnX>(value => new RtnX(value.ToDouble(), 1)));
    }

    [Fact]
    public void InvalidInputsAndInexactConversion_ShouldReturnFailure() {
        var x = new BinVar("x");
        IVariableItem[] two = { x, x };
        Assert.True(CardinalityConstraints.AtMost<Flt64>(
            indicators: null,
            limit: 0,
            converter: IIntoValue<Flt64>.Identity
        ).IsFailed);
        Assert.True(CardinalityConstraints.AtMost<Flt64>(
            indicators: two,
            limit: -1,
            converter: IIntoValue<Flt64>.Identity
        ).IsFailed);
        Assert.True(CardinalityConstraints.Exactly<Flt64>(
            indicators: two,
            amount: 3,
            converter: IIntoValue<Flt64>.Identity
        ).IsFailed);
        Assert.True(CardinalityConstraints.AtMost<Flt64>(
            indicators: new IVariableItem[] { new RealVar("real") },
            limit: 0,
            converter: IIntoValue<Flt64>.Identity
        ).IsFailed);
        Assert.True(CardinalityConstraints.Exactly<Flt64>(
            indicators: new IVariableItem[] { null! },
            amount: 0,
            converter: IIntoValue<Flt64>.Identity
        ).IsFailed);
        Assert.True(CardinalityConstraints.Exactly<Flt64>(
            indicators: two,
            amount: 1,
            converter: null
        ).IsFailed);
        var dropping = new TestNumberConverter<Flt64>(value => value == new Flt64(2) ? Flt64.One : value);
        Assert.True(CardinalityConstraints.AtMost(
            indicators: two,
            limit: 1,
            converter: dropping
        ).IsFailed);
        Assert.True(CardinalityConstraints.Exactly(
            indicators: two,
            amount: 1,
            converter: new WrongNegativeConverter()
        ).IsFailed);
        var y = new BinVar("y");
        var z = new BinVar("z");
        Assert.True(CardinalityConstraints.AtMost(
            indicators: new IVariableItem[] { x, x, y, z },
            limit: 1,
            converter: new WrongDuplicateConverter()
        ).IsFailed);
        var narrow = new TestNumberConverter<NInt8>(value => new NInt8(unchecked((sbyte)value.ToDouble())));
        Assert.True(CardinalityConstraints.AtMost(
            indicators: Enumerable.Repeat<IVariableItem>(x, 128).ToArray(),
            limit: 1,
            converter: narrow
        ).IsFailed);
    }

    private static async Task VerifyNumeric<V>(IIntoValue<V> converter)
        where V : struct, IRealNumber<V>, INumberField<V> {
        var x = new BinVar("x");
        var y = new BinVar("y");
        foreach (bool exactly in new[] { false, true }) {
            Result<IReadOnlyList<LinearInequality<V>>, ErrorCode, Error<ErrorCode>> created = exactly
                ? CardinalityConstraints.Exactly(
                    indicators: new[] { x, y },
                    amount: 1,
                    converter: converter
                )
                : CardinalityConstraints.AtMost(
                    indicators: new[] { x, y },
                    limit: 1,
                    converter: converter
                );
            Assert.True(created.IsOk, created.ToString());
            using var parent = new LinearMetaModel<V>(converter: converter);
            Assert.True(parent.Add(x).IsOk);
            Assert.True(parent.Add(y).IsOk);
            Assert.True(parent.AddConstraint(created.Value[0], group: null).IsOk);
            Result<LinearMechanismModel<V>, ErrorCode, Error<ErrorCode>> built = await LinearMechanismModel<V>.InvokeAsync(parent);
            Assert.True(built.IsOk, built.ToString());
            using LinearMechanismModel<V> model = built.Value;
            IConstraint<V, LinearCategory> row = Assert.Single(((IMechanismModel<V>)model).Constraints);
            foreach (int first in new[] { 0, 1 }) {
                foreach (int second in new[] { 0, 1 }) {
                    var values = new Dictionary<IVariableItem, V> {
                        [x] = converter.IntoValue(new Flt64(first)),
                        [y] = converter.IntoValue(new Flt64(second)),
                    };
                    V[] solution = model.Tokens.TokensInSolver.Select(token => values[token.Variable]).ToArray();
                    Assert.Equal(exactly ? first + second == 1 : first + second <= 1, row.IsTrue(solution));
                }
            }
        }
    }

    private static Result<IReadOnlyList<LinearInequality<Flt64>>, ErrorCode, Error<ErrorCode>> Build(
        IReadOnlyList<IVariableItem> indicators,
        int amount,
        bool exactly) => exactly
        ? CardinalityConstraints.Exactly(
            indicators: indicators,
            amount: amount,
            converter: IIntoValue<Flt64>.Identity
        )
        : CardinalityConstraints.AtMost(
            indicators: indicators,
            limit: amount,
            converter: IIntoValue<Flt64>.Identity
        );

    private sealed class WrongNegativeConverter : IIntoValue<Flt64> {
        public Flt64 Zero => Flt64.Zero;
        public Flt64 One => Flt64.One;
        public Flt64 IntoValue(Flt64 value) => value;
        public Flt64 FromValue(Flt64 value) => value < Flt64.Zero ? Flt64.Zero : value;
    }

    private sealed class WrongDuplicateConverter : IIntoValue<Flt64> {
        public Flt64 Zero => Flt64.Zero;
        public Flt64 One => Flt64.One;
        public Flt64 IntoValue(Flt64 value) => value;
        public Flt64 FromValue(Flt64 value) => value == new Flt64(2) ? Flt64.One : value;
    }
}
