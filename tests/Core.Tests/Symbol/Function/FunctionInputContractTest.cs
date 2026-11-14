#nullable enable

using System;
using System.Collections.Generic;
using FluentAssertions;
using Xunit;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Algebra.ValueRange;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
using SymbolFlatten = Fuookami.Ospf.Core.Symbol.Flatten;
using UInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;
namespace Fuookami.Ospf.Core.Tests.Symbol.Function;

public class FunctionInputContractTest {
    [Theory]
    [MemberData(nameof(FunctionSymbolRegistrationTest.AllFunctionTypes), MemberType = typeof(FunctionSymbolRegistrationTest))]
    public void ImplementedFunctions_ShouldDeclareInputsAndPlaceholders_ShouldRemainUnknown(IMathFunctionSymbol<Flt64> function) {
        if (function is IHasResultPolynomial<Flt64>) {
            function.InputSymbols.Should().NotBeNull();
        }
        else {
            function.InputSymbols.Should().BeNull();
        }
    }

    [Fact]
    public void FunctionFamilies_ShouldExposeAllEvaluationInputsAsDependencies() {
        var first = LinearExpressionSymbol.FromConstant(Flt64.One, "metadata-first");
        var second = LinearExpressionSymbol.FromConstant(new Flt64(2), "metadata-second");
        LinearPolynomial<Flt64> left = Polynomial(first);
        LinearPolynomial<Flt64> right = Polynomial(second);
        LinearPolynomial<Flt64>[] both = { left, right };
        var inequality = new LinearInequality<Flt64>(
            Lhs: left,
            Rhs: right,
            Comparison: Comparison.LE
        );
        ValueRange<Flt64> range = ValueRange<Flt64>.Of(new Flt64(-10), new Flt64(10)).Value;
        var input = new LinearConstraintInput<Flt64>(
            FlattenData: new SymbolFlatten.LinearFlattenData<Flt64>(
                new[] {
                    new LinearMonomial<Flt64>(Flt64.One, first),
                    new LinearMonomial<Flt64>(Flt64.One, second),
                },
                Flt64.Zero
            ),
            Sign: Comparison.LE,
            LhsRange: range,
            Name: "metadata-input",
            DisplayName: null,
            RhsConstant: Flt64.Zero
        );
        LinearConstraintInput<Flt64>[] inputs = { input };
        IIntoValue<Flt64> converter = IIntoValue<Flt64>.Identity;
        IMathFunctionSymbol<Flt64>[] singleInput = {
            new AbsFunction<Flt64>(left, converter),
            new FloorFunction<Flt64>(left, converter),
            new CeilingFunction<Flt64>(left, converter),
            new RoundingFunction<Flt64>(left, converter),
            new InequalityFunction<Flt64>(
                lhs: left,
                rhs: Flt64.Zero,
                sign: Comparison.LE,
                converter: converter
            ),
            new BinaryzationFunction(left),
            new BalanceTernaryzationFunction(left),
            new NotFunction(left),
            new ModFunction(left, Flt64.One),
        };
        IMathFunctionSymbol<Flt64>[] twoInputs = {
            ImplyFunction<Flt64>.Create(
                antecedent: left,
                consequent: right,
                converter: converter,
                strictBoundary: Flt64.One
            ).Value,
            new MaxFunction<Flt64>(both, converter),
            new MinFunction<Flt64>(both, converter),
            new MinMaxFunction<Flt64>(both, converter),
            new MaxMinFunction<Flt64>(both, converter),
            new AndFunction(both),
            new OrFunction(both),
            new XorFunction(both),
            new OneOfFunction(both),
            new InStepRangeFunction(
                lowerBound: left,
                upperBound: right,
                step: Flt64.One
            ),
            new SameAsFunction(new[] { inequality }, Flt64.One),
            new SatisfiedAmountFunction(new[] { inequality }, Flt64.One),
            new SatisfiedAmountInequalityFunction(inputs),
            new AnyFunction(inputs),
            new AllFunction(inputs),
            new AtLeastInequalityFunction(inputs, new UInt64(1)),
            new NotAllFunction(inputs),
            new NumerableFunction(inputs, ValueRange<UInt64>.Of(UInt64.Zero, UInt64.One).Value),
        };

        foreach (IMathFunctionSymbol<Flt64> function in singleInput) {
            function.InputSymbols.Should().BeEquivalentTo(new ISymbol[] { first }, function.Name);
            new LinearFunctionSymbolAdapter<Flt64>(function).Dependencies
                .Should().BeEquivalentTo(new IIntermediateSymbol[] { first }, function.Name);
        }
        foreach (IMathFunctionSymbol<Flt64> function in twoInputs) {
            function.InputSymbols.Should().BeEquivalentTo(new ISymbol[] { first, second }, function.Name);
            new LinearFunctionSymbolAdapter<Flt64>(function).Dependencies
                .Should().BeEquivalentTo(new IIntermediateSymbol[] { first, second }, function.Name);
        }

        var semi = new SemiFunction();
        semi.InputSymbols.Should().BeEquivalentTo(new ISymbol[] { semi.IndicatorVar, semi.ResultVar });
    }

    [Fact]
    public void GenericAdapter_ShouldUseItsConverterOnlyForTokenResults() {
        var converter = new TestNumberConverter<FltX>(value => value.ToFltX());
        var x = new RealVar("generic-input");
        var function = new AbsFunction<FltX>(
            polynomial: new LinearPolynomial<FltX>(
                new[] { new LinearMonomial<FltX>(FltX.One, x) },
                FltX.Zero
            ),
            converter: converter
        );
        var adapter = new LinearFunctionSymbolAdapter<FltX>(function);
        using var table = new ConcurrentAutoTokenTable<FltX>(
            LinearCategory.Instance,
            new List<IIntermediateSymbol>()
        );
        table.Add(x).IsOk.Should().BeTrue();
        table.TokenList.Find(x)!.SetResult(new FltX(-4.25m));
        int calls = converter.ConversionCalls;

        function.InputSymbols.Should().ContainSingle().Which.Should().BeSameAs(x);
        adapter.Prepare(
            values: new Dictionary<ISymbol, FltX> { [x] = new FltX(-9007199254740993.125m) },
            tokenTable: table,
            converter: converter
        ).Should().Be(new FltX(9007199254740993.125m));
        converter.ConversionCalls.Should().Be(calls);
        adapter.Prepare(
            values: null,
            tokenTable: table,
            converter: converter
        ).Should().Be(new FltX(4.25m));
        converter.ConversionCalls.Should().BeGreaterThan(calls);
    }

    private static LinearPolynomial<Flt64> Polynomial(ISymbol symbol) =>
        new(new[] { new LinearMonomial<Flt64>(Flt64.One, symbol) }, Flt64.Zero);
}
