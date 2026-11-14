#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
using Integer = Fuookami.Ospf.Math.Algebra.Number.Int64;
using SymbolFlatten = Fuookami.Ospf.Core.Symbol.Flatten;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
using UInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;
namespace Fuookami.Ospf.Core.Tests.Symbol.Function;

/// <summary>泛型二次乘积契约测试 / Generic quadratic product contract tests.</summary>
public class ProductFunctionTest {
    [Fact]
    public void InputsAndPolynomial_ShouldBeSnapshotsAndEvaluateFromAllSources() {
        var x = new RealVar("product-x");
        var y = new RealVar("product-y");
        LinearMonomial<Flt64>[] left = [new(new Flt64(2), x)];
        LinearMonomial<Flt64>[] right = [new(new Flt64(-1), y)];
        ProductFunction<Flt64> product = ProductFunction<Flt64>.Create(
            left: new LinearPolynomial<Flt64>(left, new Flt64(3)),
            right: new LinearPolynomial<Flt64>(right, new Flt64(4)),
            converter: IIntoValue<Flt64>.Identity
        ).Value;
        left[0] = new LinearMonomial<Flt64>(new Flt64(99), y);
        right[0] = new LinearMonomial<Flt64>(new Flt64(99), x);
        product.AsMutable().SetConstant(new Flt64(99));
        product.Polynomial.Constant.Should().Be(new Flt64(12));
        product.Polynomial.Monomials.Select(term => term.Coefficient)
            .Should().Equal(new Flt64(-2), new Flt64(8), new Flt64(-3));
        var values = new Dictionary<ISymbol, Flt64> { [x] = Flt64.One, [y] = new Flt64(2) };
        product.TryEvaluate(values).Value.Should().Be(new Flt64(10));
        product.TryEvaluate(new Dictionary<ISymbol, Flt64> { [x] = Flt64.One }).Value.Should().BeNull();

        using var table = new ConcurrentManualTokenTable<Flt64>(QuadraticCategory.Instance, []);
        IQuadraticMathFunctionSymbol<Flt64> contract = product;
        contract.RegisterAuxiliaryTokens(table).IsOk.Should().BeTrue();
        table.Add(x).IsOk.Should().BeTrue();
        table.Add(y).IsOk.Should().BeTrue();
        table.TokenList.Find(x)!.SetResult(Flt64.One);
        table.TokenList.Find(y)!.SetResult(new Flt64(2));
        product.Evaluate(table, IIntoValue<Flt64>.Identity).Should().Be(new Flt64(10));
        product.Evaluate(
            results: table.TokenList.TokensInSolver.Select(token => values[token.Variable]).ToArray(),
            tokenTable: table,
            converter: IIntoValue<Flt64>.Identity
        ).Should().Be(new Flt64(10));
        product.Evaluate(
            values: new Dictionary<ISymbol, Flt64> { [x] = new Flt64(2) },
            tokenTable: table,
            converter: IIntoValue<Flt64>.Identity
        ).Should().Be(new Flt64(14));
        product.Evaluate(
            results: Array.Empty<Flt64>(),
            tokenTable: table,
            converter: IIntoValue<Flt64>.Identity,
            zeroIfNone: true
        ).Should().Be(new Flt64(12));
        product.Prepare(
            values: new Dictionary<ISymbol, Flt64>(),
            tokenTable: table,
            converter: IIntoValue<Flt64>.Identity
        ).Should().Be(new Flt64(10));
        product.Prepare(
            values: new Dictionary<ISymbol, Flt64> { [x] = new Flt64(2) },
            tokenTable: table,
            converter: IIntoValue<Flt64>.Identity
        ).Should().Be(new Flt64(14));
        product.PrepareAndCache(
            values: values,
            tokenTable: table,
            converter: IIntoValue<Flt64>.Identity
        );
        table.CachedValue(product, values).Should().Be(new Flt64(10));
        table.CachedValue(product, (IReadOnlyList<Flt64>?)null).Should().BeNull();
        product.PrepareAndCache(
            values: null,
            tokenTable: table,
            converter: IIntoValue<Flt64>.Identity
        );
        table.CachedValue(product, (IReadOnlyList<Flt64>?)null).Should().Be(new Flt64(10));
    }

    [Fact]
    public void RepresentativeNumericTypes_ShouldUseNativeArithmetic() {
        var x = new RealVar("product-native-x");
        var integerConverter = new TestNumberConverter<Integer>(value => value.ToInt64());
        ProductFunction<Integer> integer = ProductFunction<Integer>.Create(
            left: new LinearPolynomial<Integer>([new LinearMonomial<Integer>(new Integer(3), x)], Integer.One),
            right: new LinearPolynomial<Integer>([], new Integer(2)),
            converter: integerConverter
        ).Value;
        integerConverter.ConversionCalls.Should().Be(0);
        integer.TryEvaluate(new Dictionary<ISymbol, Integer> { [x] = new Integer(9007199254740993L) })
            .Value.Should().Be(new Integer(54043195528445960L));
        integerConverter.ConversionCalls.Should().Be(0);

        ProductFunction<FltX> decimalProduct = ProductFunction<FltX>.Create(
            left: new LinearPolynomial<FltX>([new LinearMonomial<FltX>(new FltX(0.1m), x)], new FltX(0.2m)),
            right: new LinearPolynomial<FltX>([], new FltX(3m)),
            converter: FltX.Zero
        ).Value;
        decimalProduct.TryEvaluate(new Dictionary<ISymbol, FltX> { [x] = new FltX(9007199254740993m) })
            .Value.Should().Be(new FltX(2702159776422298.5m));
        decimalProduct.Polynomial.Monomials.Single().Coefficient.Should().Be(new FltX(0.3m));

        ProductFunction<RtnX> rational = ProductFunction<RtnX>.Create(
            left: new LinearPolynomial<RtnX>([new LinearMonomial<RtnX>(new RtnX(1, 3), x)], new RtnX(1, 2)),
            right: new LinearPolynomial<RtnX>([], new RtnX(2, 5)),
            converter: RtnX.Zero
        ).Value;
        rational.TryEvaluate(new Dictionary<ISymbol, RtnX> { [x] = new RtnX(3, 1) })
            .Value!.Value.Eq(new RtnX(3, 5)).Should().BeTrue();
        rational.Polynomial.Monomials.Single().Coefficient.Eq(new RtnX(2, 15)).Should().BeTrue();

        ProductFunction<Flt32> single = ProductFunction<Flt32>.Create(
            left: new LinearPolynomial<Flt32>([new LinearMonomial<Flt32>(new Flt32(1), x)], new Flt32(1)),
            right: new LinearPolynomial<Flt32>([], new Flt32(2)),
            converter: new TestNumberConverter<Flt32>(value => value.ToFlt32())
        ).Value;
        single.TryEvaluate(new Dictionary<ISymbol, Flt32> { [x] = new Flt32(3) }).Value.Should().Be(new Flt32(8));
    }

    [Fact]
    public async Task GenericProducts_ShouldReachTypedQuadraticObjectives() {
        await VerifyTypedObjective(new TestNumberConverter<Flt32>(value => value.ToFlt32()));
        await VerifyTypedObjective(new TestNumberConverter<Integer>(value => value.ToInt64()));
        await VerifyTypedObjective(new TestNumberConverter<FltX>(value => value.ToFltX()));
        await VerifyTypedObjective(new TestNumberConverter<RtnX>(value => new RtnX(value.ToDouble(), 1)));
    }

    [Fact]
    public void InvalidExpansionAndEvaluation_ShouldReturnFailures() {
        var x = new RealVar("product-invalid-x");
        var integerConverter = new TestNumberConverter<Integer>(value => value.ToInt64());
        ProductFunction<Integer>.Create(
            left: new LinearPolynomial<Integer>([new LinearMonomial<Integer>(new Integer(long.MaxValue), x)], Integer.Zero),
            right: new LinearPolynomial<Integer>([], new Integer(2)),
            converter: integerConverter
        ).IsFailed.Should().BeTrue();
        ProductFunction<Flt64>.Create(
            left: new LinearPolynomial<Flt64>([new LinearMonomial<Flt64>(new Flt64(double.Epsilon), x)], Flt64.Zero),
            right: new LinearPolynomial<Flt64>([], new Flt64(0.5)),
            converter: IIntoValue<Flt64>.Identity
        ).IsFailed.Should().BeTrue();
        ProductFunction<Flt64>.Create(
            left: new LinearPolynomial<Flt64>([], new Flt64(double.NaN)),
            right: new LinearPolynomial<Flt64>([], Flt64.Zero),
            converter: IIntoValue<Flt64>.Identity
        ).IsFailed.Should().BeTrue();
        ProductFunction<RtnX>.Create(
            left: new LinearPolynomial<RtnX>([], new RtnX(1, 1e200)),
            right: new LinearPolynomial<RtnX>([], new RtnX(1, 1e200)),
            converter: RtnX.Zero
        ).IsFailed.Should().BeTrue();
        ProductFunction<Integer> product = ProductFunction<Integer>.Create(
            left: new LinearPolynomial<Integer>([new LinearMonomial<Integer>(Integer.One, x)], Integer.Zero),
            right: new LinearPolynomial<Integer>([], new Integer(2)),
            converter: integerConverter
        ).Value;
        product.TryEvaluate(new Dictionary<ISymbol, Integer> { [x] = new Integer(long.MaxValue) })
            .Should().BeOfType<Failed<Integer?, ErrorCode, Error<ErrorCode>>>();
        product.TryEvaluate(new Dictionary<ISymbol, Integer>()).IsOk.Should().BeTrue();
        product.TryEvaluate(new Dictionary<ISymbol, Integer>()).Value.Should().BeNull();
        ProductFunction<Flt64> floating = ProductFunction<Flt64>.Create(
            left: Linear(0, [(1, x)]),
            right: Linear(0.5, []),
            converter: IIntoValue<Flt64>.Identity
        ).Value;
        floating.TryEvaluate(new Dictionary<ISymbol, Flt64> { [x] = new Flt64(double.Epsilon) })
            .IsFailed.Should().BeTrue();
        floating.TryEvaluate(new Dictionary<ISymbol, Flt64> { [x] = new Flt64(double.PositiveInfinity) })
            .IsFailed.Should().BeTrue();
        ProductFunction<Flt64>.Create(
            left: null,
            right: Linear(1, []),
            converter: IIntoValue<Flt64>.Identity
        ).IsFailed.Should().BeTrue();
        var quadratic = new QuadraticExpressionSymbol(new MutableQuadraticPolynomial<Flt64>(constant: Flt64.Zero));
        ProductFunction<Flt64>.Create(
            left: Linear(0, [(1, quadratic)]),
            right: Linear(1, []),
            converter: IIntoValue<Flt64>.Identity
        ).IsFailed.Should().BeTrue();
    }

    [Fact]
    public void SharedPolynomialEvaluation_ShouldRejectNullValues() {
        var x = new RealVar("product-shared-null-x");
        var function = new AbsFunction<Flt64>(
            polynomial: Linear(0, [(1, x)]),
            converter: IIntoValue<Flt64>.Identity
        );
        function.TryEvaluate(null!).IsFailed.Should().BeTrue();
    }

    [Fact]
    public async Task UnderflowInsideAnInput_ShouldFailInsteadOfDisagreeingWithExpandedModel() {
        var x = new RealVar("product-input-underflow");
        ProductFunction<Flt64> product = ProductFunction<Flt64>.Create(
            left: Linear(0, [(0.5, x)]),
            right: Linear(2, []),
            converter: IIntoValue<Flt64>.Identity
        ).Value;
        var values = new Dictionary<ISymbol, Flt64> { [x] = new Flt64(double.Epsilon) };
        product.Polynomial.Monomials.Single().Coefficient.Should().Be(Flt64.One);
        product.TryEvaluate(values).IsFailed.Should().BeTrue();
        using var parent = new QuadraticMetaModel<Flt64>();
        parent.Add(x).IsOk.Should().BeTrue();
        parent.Add(product).IsOk.Should().BeTrue();
        Result<QuadraticMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built =
            await QuadraticMechanismModel<Flt64>.InvokeAsync(
                metaModel: parent,
                fixedVariables: new Dictionary<IVariableItem, Flt64> { [x] = new Flt64(double.Epsilon) }
            );
        built.IsFailed.Should().BeTrue();
    }

    [Fact]
    public void InvalidLeftInput_ShouldNotBeHiddenByMissingRightInput() {
        var x = new RealVar("product-overflow-before-missing-x");
        var y = new RealVar("product-overflow-before-missing-y");
        var converter = new TestNumberConverter<Integer>(value => value.ToInt64());
        ProductFunction<Integer> product = ProductFunction<Integer>.Create(
            left: new LinearPolynomial<Integer>([new LinearMonomial<Integer>(new Integer(2), x)], Integer.Zero),
            right: new LinearPolynomial<Integer>([new LinearMonomial<Integer>(Integer.One, y)], Integer.Zero),
            converter: converter
        ).Value;
        product.TryEvaluate(new Dictionary<ISymbol, Integer> { [x] = new Integer(long.MaxValue) })
            .IsFailed.Should().BeTrue();
    }

    [Fact]
    public void InputTerms_ShouldBeEvaluatedInOrderBeforeMissingValues() {
        var x = new RealVar("product-ordered-x");
        var y = new RealVar("product-ordered-y");
        var converter = new TestNumberConverter<Integer>(value => value.ToInt64());
        var overflow = new LinearMonomial<Integer>(new Integer(2), x);
        var missing = new LinearMonomial<Integer>(Integer.One, y);
        ProductFunction<Integer> first = ProductFunction<Integer>.Create(
            left: new LinearPolynomial<Integer>([overflow, missing], Integer.Zero),
            right: new LinearPolynomial<Integer>([], Integer.One),
            converter: converter
        ).Value;
        ProductFunction<Integer> second = ProductFunction<Integer>.Create(
            left: new LinearPolynomial<Integer>([missing, overflow], Integer.Zero),
            right: new LinearPolynomial<Integer>([], Integer.One),
            converter: converter
        ).Value;
        ProductFunction<Integer> right = ProductFunction<Integer>.Create(
            left: new LinearPolynomial<Integer>([], Integer.One),
            right: new LinearPolynomial<Integer>([overflow, missing], Integer.Zero),
            converter: converter
        ).Value;
        ProductFunction<Integer> missingLeft = ProductFunction<Integer>.Create(
            left: new LinearPolynomial<Integer>([missing], Integer.Zero),
            right: new LinearPolynomial<Integer>([overflow], Integer.Zero),
            converter: converter
        ).Value;
        ProductFunction<Integer> accumulation = ProductFunction<Integer>.Create(
            left: new LinearPolynomial<Integer>([new LinearMonomial<Integer>(Integer.One, x),
                new LinearMonomial<Integer>(Integer.One, x), missing], Integer.Zero),
            right: new LinearPolynomial<Integer>([], Integer.One),
            converter: converter
        ).Value;
        var values = new Dictionary<ISymbol, Integer> { [x] = new Integer(long.MaxValue) };
        first.TryEvaluate(values).IsFailed.Should().BeTrue();
        second.TryEvaluate(values).IsOk.Should().BeTrue();
        second.TryEvaluate(values).Value.Should().BeNull();
        right.TryEvaluate(values).IsFailed.Should().BeTrue();
        missingLeft.TryEvaluate(values).IsOk.Should().BeTrue();
        missingLeft.TryEvaluate(values).Value.Should().BeNull();
        accumulation.TryEvaluate(values).IsFailed.Should().BeTrue();
    }

    [Fact]
    public async Task InvalidPartialPreparation_ShouldFailWithoutPublishingProduct() {
        var x = new RealVar("product-partial-overflow-x");
        var y = new RealVar("product-partial-missing-y");
        var converter = new TestNumberConverter<Integer>(value => value.ToInt64());
        ProductFunction<Integer> product = ProductFunction<Integer>.Create(
            left: new LinearPolynomial<Integer>([new LinearMonomial<Integer>(new Integer(2), x)], Integer.Zero),
            right: new LinearPolynomial<Integer>([new LinearMonomial<Integer>(Integer.One, y)], Integer.Zero),
            converter: converter
        ).Value;
        using var parent = new QuadraticMetaModel<Integer>(converter: converter);
        parent.Add(x).IsOk.Should().BeTrue();
        parent.Add(y).IsOk.Should().BeTrue();
        parent.Add(product).IsOk.Should().BeTrue();
        int callbacks = parent.Tokens.Find(x)!.RefreshCallbacks.Count;
        Result<QuadraticMechanismModel<Integer>, ErrorCode, Error<ErrorCode>> result =
            await QuadraticMechanismModel<Integer>.InvokeAsync(
                metaModel: parent,
                fixedVariables: new Dictionary<IVariableItem, Integer> { [x] = new Integer(long.MaxValue) }
            );
        result.IsFailed.Should().BeTrue();
        parent.Tokens.Find(x)!.RefreshCallbacks.Should().HaveCount(callbacks);
        parent.Tokens.CachedValue(product, (IReadOnlyList<Integer>?)null).Should().BeNull();
        parent.Tokens.Symbols.Should().ContainSingle().Which.Should().BeSameAs(product);
    }

    [Fact]
    public async Task Product_ShouldReachQuadraticRowsObjectivesAndMatrixWithoutExtraRowsOrTokens() {
        var x = new RealVar("product-row-x");
        var y = new RealVar("product-row-y");
        ProductFunction<Flt64> product = ProductFunction<Flt64>.Create(
            left: Linear(3, [(2, x)]),
            right: Linear(4, [(-1, y)]),
            converter: IIntoValue<Flt64>.Identity
        ).Value;
        var expression = new QuadraticPolynomial<Flt64>([new QuadraticMonomial<Flt64>(Flt64.One, product)], Flt64.Zero);
        using var parent = new QuadraticMetaModel<Flt64>();
        parent.Add(x).IsOk.Should().BeTrue();
        parent.Add(y).IsOk.Should().BeTrue();
        parent.Add(product).IsOk.Should().BeTrue();
        parent.AddConstraint(new QuadraticInequalityOf<Flt64>(
            Lhs: expression,
            Rhs: new QuadraticPolynomial<Flt64>([], new Flt64(11)),
            Comparison: Comparison.LE,
            Name: "product-row"
        ), null).IsOk.Should().BeTrue();
        parent.AddObject(
            category: ObjectCategory.Minimum,
            flattenData: new SymbolFlatten.QuadraticFlattenData<Flt64>(expression.Monomials, expression.Constant),
            name: "product-objective",
            displayName: null
        ).IsOk.Should().BeTrue();

        Result<QuadraticMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built =
            await QuadraticMechanismModel<Flt64>.InvokeAsync(parent);
        built.IsOk.Should().BeTrue();
        using QuadraticMechanismModel<Flt64> model = built.Value;
        model.Tokens.Tokens.Should().HaveCount(2);
        model.Tokens.Symbols.Should().ContainSingle().Which.Should().BeSameAs(product);
        IReadOnlyList<IConstraint<Flt64, QuadraticCategory>> rows = ((IAbstractQuadraticMechanismModel<Flt64>)model).Constraints;
        rows.Should().ContainSingle().Which.Name.Should().Be("product-row");
        var values = new Dictionary<IVariableItem, Flt64> { [x] = Flt64.One, [y] = new Flt64(2) };
        Flt64[] solution = model.Tokens.TokensInSolver.Select(token => values[token.Variable]).ToArray();
        rows[0].IsTrue(solution).Should().BeTrue();
        model.ObjectFunction.GetSubObjects<Flt64>().OfType<QuadraticSubObject<Flt64>>().Single()
            .Evaluate(solution).Should().Be(new Flt64(10));
        using QuadraticTetradModel matrix = model.ToQuadraticTetradModel().Value;
        int xi = model.Tokens.IndexOf(x)!.Value;
        int yi = model.Tokens.IndexOf(y)!.Value;
        matrix.Constraints.Lhs[0].Single(cell => cell.Col2 >= 0).Coefficient.Should().Be(new Flt64(-2));
        matrix.Constraints.Lhs[0].Single(cell => cell.Col1 == xi && cell.Col2 == -1).Coefficient.Should().Be(new Flt64(8));
        matrix.Constraints.Lhs[0].Single(cell => cell.Col1 == yi && cell.Col2 == -1).Coefficient.Should().Be(new Flt64(-3));
        matrix.Constraints.Rhs[0].Should().Be(new Flt64(-1));
        matrix.Objective.Constant.Should().Be(new Flt64(12));
    }

    [Fact]
    public async Task NestedSymbols_ShouldRequireFunctionRegistrationAndRetainTheirRows() {
        var x = new RealVar("product-nested-x");
        var absolute = new AbsFunction<Flt64>(
            polynomial: Linear(0, [(1, x)]),
            converter: IIntoValue<Flt64>.Identity,
            bigM: new Flt64(10)
        );
        var adapter = new LinearFunctionSymbolAdapter<Flt64>(absolute);
        ProductFunction<Flt64> product = ProductFunction<Flt64>.Create(
            left: Linear(1, [(1, adapter)]),
            right: Linear(2, [(1, x)]),
            converter: IIntoValue<Flt64>.Identity
        ).Value;
        using var parent = new QuadraticMetaModel<Flt64>();
        parent.Add(x).IsOk.Should().BeTrue();
        parent.Add(product).IsOk.Should().BeTrue();
        product.Dependencies.Should().ContainSingle().Which.Should().BeSameAs(adapter);
        (await QuadraticMechanismModel<Flt64>.InvokeAsync(parent)).IsFailed.Should().BeTrue();
        parent.Add(adapter).IsOk.Should().BeTrue();
        parent.AddObject(
            category: ObjectCategory.Minimum,
            flattenData: new SymbolFlatten.QuadraticFlattenData<Flt64>([new QuadraticMonomial<Flt64>(Flt64.One, product)], Flt64.Zero),
            name: "nested-product",
            displayName: null
        ).IsOk.Should().BeTrue();
        using QuadraticMechanismModel<Flt64> model = (await QuadraticMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        ((IAbstractQuadraticMechanismModel<Flt64>)model).Constraints.Should().HaveCount(4);
        var values = new Dictionary<IVariableItem, Flt64> {
            [x] = new Flt64(-2),
            [absolute.ResultVar] = new Flt64(2),
            [absolute.PositiveVar] = Flt64.Zero,
            [absolute.NegativeVar] = new Flt64(2),
            [absolute.SignVar] = Flt64.Zero,
        };
        Flt64[] solution = model.Tokens.TokensInSolver.Select(token => values[token.Variable]).ToArray();
        model.ObjectFunction.GetSubObjects<Flt64>().OfType<QuadraticSubObject<Flt64>>().Single()
            .Evaluate(solution).Should().Be(Flt64.Zero);

        using var missing = new QuadraticMetaModel<Flt64>();
        missing.Add(x).IsOk.Should().BeTrue();
        missing.Add(adapter).IsOk.Should().BeTrue();
        missing.AddObject(
            category: ObjectCategory.Minimum,
            flattenData: new SymbolFlatten.QuadraticFlattenData<Flt64>([new QuadraticMonomial<Flt64>(Flt64.One, product)], Flt64.Zero),
            name: "unregistered-product",
            displayName: null
        ).IsOk.Should().BeTrue();
        (await QuadraticMechanismModel<Flt64>.InvokeAsync(missing)).IsFailed.Should().BeTrue();
    }

    [Fact]
    public async Task NestedLinearInput_ShouldPrepareAndRebuildWithoutChangingOldModel() {
        var x = new RealVar("product-expression-x");
        var inner = LinearExpressionSymbol.FromVariable(x, "product-inner");
        inner.AsMutable().SetConstant(new Flt64(2));
        ProductFunction<Flt64> product = ProductFunction<Flt64>.Create(
            left: Linear(0, [(1, inner)]),
            right: Linear(1, [(1, x)]),
            converter: IIntoValue<Flt64>.Identity
        ).Value;
        var inputs = new Dictionary<ISymbol, Flt64> { [x] = new Flt64(2) };
        product.TryEvaluate(inputs).Value.Should().BeNull();
        product.Evaluate(
            values: inputs,
            tokenTable: null,
            converter: IIntoValue<Flt64>.Identity
        ).Should().Be(new Flt64(12));
        using var parent = new QuadraticMetaModel<Flt64>();
        parent.Add(x).IsOk.Should().BeTrue();
        parent.Add(product).IsOk.Should().BeTrue();
        parent.AddObject(
            category: ObjectCategory.Minimum,
            flattenData: new SymbolFlatten.QuadraticFlattenData<Flt64>(
                [new QuadraticMonomial<Flt64>(Flt64.One, product)], Flt64.Zero),
            name: "nested-linear-product",
            displayName: null
        ).IsOk.Should().BeTrue();
        using QuadraticMechanismModel<Flt64> first = (await QuadraticMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        QuadraticSubObject<Flt64> objective = first.ObjectFunction.GetSubObjects<Flt64>()
            .OfType<QuadraticSubObject<Flt64>>().Single();
        objective.Evaluate([new Flt64(2)]).Should().Be(new Flt64(12));
        inner.AsMutable().SetConstant(new Flt64(3));
        using QuadraticMechanismModel<Flt64> second = (await QuadraticMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        second.ObjectFunction.GetSubObjects<Flt64>().OfType<QuadraticSubObject<Flt64>>().Single()
            .Evaluate([new Flt64(2)]).Should().Be(new Flt64(15));
        objective.Evaluate([new Flt64(2)]).Should().Be(new Flt64(12));
    }

    [Fact]
    public async Task CyclicOrMissingInput_ShouldFailModelBuildWithoutLeakingCallbacks() {
        var x = new RealVar("product-invalid-dependency-x");
        var absent = new RealVar("product-absent-x");
        var cycle = LinearExpressionSymbol.FromVariable(x, "product-cycle");
        cycle.AsMutable().AddMonomial(new LinearMonomial<Flt64>(Flt64.One, cycle));
        foreach (ISymbol input in new ISymbol[] { cycle, absent }) {
            ProductFunction<Flt64> product = ProductFunction<Flt64>.Create(
                left: Linear(0, [(1, input)]),
                right: Linear(1, [(1, x)]),
                converter: IIntoValue<Flt64>.Identity
            ).Value;
            using var parent = new QuadraticMetaModel<Flt64>();
            parent.Add(x).IsOk.Should().BeTrue();
            parent.Add(product).IsOk.Should().BeTrue();
            parent.AddObject(
                category: ObjectCategory.Minimum,
                flattenData: new SymbolFlatten.QuadraticFlattenData<Flt64>(
                    [new QuadraticMonomial<Flt64>(Flt64.One, product)], Flt64.Zero),
                name: "invalid-product",
                displayName: null
            ).IsOk.Should().BeTrue();
            int callbacks = parent.Tokens.Find(x)!.RefreshCallbacks.Count;
            (await QuadraticMechanismModel<Flt64>.InvokeAsync(parent)).IsFailed.Should().BeTrue();
            parent.Tokens.Find(x)!.RefreshCallbacks.Should().HaveCount(callbacks);
            parent.Tokens.Symbols.Should().ContainSingle().Which.Should().BeSameAs(product);
        }
    }

    [Fact]
    public async Task FailedPreparation_ShouldNotPublishCachesOrMutateParentTokens() {
        var x = new RealVar("product-overflow-x");
        var converter = new TestNumberConverter<Integer>(value => value.ToInt64());
        ProductFunction<Integer> product = ProductFunction<Integer>.Create(
            left: new LinearPolynomial<Integer>([new LinearMonomial<Integer>(Integer.One, x)], Integer.Zero),
            right: new LinearPolynomial<Integer>([], new Integer(2)),
            converter: converter
        ).Value;
        using var parent = new QuadraticMetaModel<Integer>(converter: converter);
        parent.Add(x).IsOk.Should().BeTrue();
        parent.Add(product).IsOk.Should().BeTrue();
        int callbacks = parent.Tokens.Find(x)!.RefreshCallbacks.Count;

        Result<QuadraticMechanismModel<Integer>, ErrorCode, Error<ErrorCode>> result =
            await QuadraticMechanismModel<Integer>.InvokeAsync(
                metaModel: parent,
                fixedVariables: new Dictionary<IVariableItem, Integer> { [x] = new Integer(long.MaxValue) }
            );
        result.Should().BeOfType<Failed<QuadraticMechanismModel<Integer>, ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.IllegalArgument);
        parent.Tokens.Find(x)!.RefreshCallbacks.Should().HaveCount(callbacks);
        parent.Tokens.CachedValue(product, (IReadOnlyList<Integer>?)null).Should().BeNull();
        parent.Tokens.Symbols.Should().ContainSingle().Which.Should().BeSameAs(product);
    }

    [Fact]
    public void RegistrationWithUnrelatedFixedValues_ShouldNotUseStaleTokenResults() {
        var x = new RealVar("product-stale-x");
        var unrelated = new RealVar("product-stale-other");
        ProductFunction<Flt64> product = ProductFunction<Flt64>.Create(
            left: Linear(0, [(1, x)]),
            right: Linear(2, []),
            converter: IIntoValue<Flt64>.Identity
        ).Value;
        using var table = new ConcurrentAutoTokenTable<Flt64>(QuadraticCategory.Instance, []);
        table.Add(x).IsOk.Should().BeTrue();
        table.TokenList.Find(x)!.SetResult(new Flt64(7));
        table.Cache(
            cacheKey: product,
            solution: (IReadOnlyList<Flt64>?)null,
            value: new Flt64(42)
        );
        new IIntermediateSymbol[] { product }.Register(
            tokenTable: table,
            fixedValues: new Dictionary<ISymbol, Flt64> { [unrelated] = Flt64.One }
        ).IsOk.Should().BeTrue();
        table.CachedValue(product, (IReadOnlyList<Flt64>?)null).Should().BeNull();
    }

    [Fact]
    public void Flush_ShouldReachSharedIntermediateDependencyOnce() {
        var x = new RealVar("product-flush-x");
        var dependency = new TrackingLinear(LinearExpressionSymbol.FromVariable(x, "product-flush-inner"));
        ProductFunction<Flt64> product = ProductFunction<Flt64>.Create(
            left: Linear(0, [(1, dependency)]),
            right: Linear(1, [(1, dependency)]),
            converter: IIntoValue<Flt64>.Identity
        ).Value;
        product.Flush(force: true);
        dependency.Calls.Should().Be(1);
        dependency.Forced.Should().BeTrue();
    }

    [Fact]
    public void Evaluation_ShouldResolveSharedIntermediateOnce() {
        var x = new RealVar("product-resolve-x");
        var dependency = new TrackingLinear(LinearExpressionSymbol.FromVariable(x, "product-resolve-inner"));
        ProductFunction<Flt64> product = ProductFunction<Flt64>.Create(
            left: Linear(0, [(1, dependency), (1, dependency)]),
            right: Linear(0, [(1, dependency)]),
            converter: IIntoValue<Flt64>.Identity
        ).Value;
        product.Evaluate(
            values: new Dictionary<ISymbol, Flt64>(),
            tokenTable: null,
            converter: IIntoValue<Flt64>.Identity
        ).Should().Be(new Flt64(2));
        dependency.EvaluationCalls.Should().Be(1);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("undeclared")]
    [InlineData("fatal")]
    public void QuadraticFunctionRegistration_ShouldValidateInputsAndRestoreCaches(string failure) {
        var x = new RealVar($"quadratic-contract-{failure}-x");
        var hidden = new RealVar($"quadratic-contract-{failure}-hidden");
        ProductFunction<Flt64> product = ProductFunction<Flt64>.Create(
            left: Linear(0, [(1, x)]),
            right: Linear(1, []),
            converter: IIntoValue<Flt64>.Identity
        ).Value;
        var function = new QuadraticTestFunction(
            product: product,
            hidden: hidden,
            failure: failure
        );
        using var table = new ConcurrentAutoTokenTable<Flt64>(QuadraticCategory.Instance, []);
        table.Add(x).IsOk.Should().BeTrue();
        table.Cache(
            cacheKey: function,
            solution: (IReadOnlyList<Flt64>?)null,
            value: new Flt64(42)
        );
        Try result = new IIntermediateSymbol[] { function }.Register(
            tokenTable: table,
            fixedValues: new Dictionary<ISymbol, Flt64> { [x] = Flt64.One }
        );
        result.IsFailed.Should().BeTrue();
        if (failure == "fatal") {
            result.Should().BeOfType<Fatal<Success, ErrorCode, Error<ErrorCode>>>()
                .Which.Errors.Should().HaveCount(2);
        }
        else {
            result.Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>()
                .Which.Code.Should().Be(ErrorCode.IllegalArgument);
        }
        table.Symbols.Should().BeEmpty();
        table.TokenList.Tokens.Should().ContainSingle().Which.Variable.Should().BeSameAs(x);
        table.CachedValue(function, (IReadOnlyList<Flt64>?)null).Should().Be(new Flt64(42));
    }

    private static LinearPolynomial<Flt64> Linear(double constant, (double Coefficient, ISymbol Symbol)[] terms) =>
        new(terms.Select(term => new LinearMonomial<Flt64>(new Flt64(term.Coefficient), term.Symbol)).ToArray(), new Flt64(constant));

    private static async Task VerifyTypedObjective<V>(TestNumberConverter<V> converter)
        where V : struct, IRealNumber<V>, INumberField<V> {
        V Number(double value) => converter.IntoValue(new Flt64(value));
        var x = new RealVar($"product-{typeof(V).Name}-x");
        var y = new RealVar($"product-{typeof(V).Name}-y");
        ProductFunction<V> product = ProductFunction<V>.Create(
            left: new LinearPolynomial<V>([new LinearMonomial<V>(Number(2), x)], Number(1)),
            right: new LinearPolynomial<V>([new LinearMonomial<V>(Number(3), y)], Number(2)),
            converter: converter
        ).Value;
        using var parent = new QuadraticMetaModel<V>(converter: converter);
        parent.Add(x).IsOk.Should().BeTrue();
        parent.Add(y).IsOk.Should().BeTrue();
        parent.Add(product).IsOk.Should().BeTrue();
        parent.AddObject(
            category: ObjectCategory.Minimum,
            flattenData: new SymbolFlatten.QuadraticFlattenData<V>(
                [new QuadraticMonomial<V>(converter.One, product)], converter.Zero),
            name: "typed-product",
            displayName: null
        ).IsOk.Should().BeTrue();
        using QuadraticMechanismModel<V> model = (await QuadraticMechanismModel<V>.InvokeAsync(
            metaModel: parent,
            fixedVariables: new Dictionary<IVariableItem, V> { [x] = Number(2), [y] = Number(3) }
        )).Value;
        model.Tokens.CachedValue(product, (IReadOnlyList<V>?)null)!.Value.Eq(Number(55)).Should().BeTrue();
        QuadraticSubObject<V> objective = model.ObjectFunction.GetSubObjects<V>().OfType<QuadraticSubObject<V>>().Single();
        objective.QuadraticTerms().Single(term => term.Symbol2 is not null).Coefficient.Eq(Number(6)).Should().BeTrue();
        V[] solution = model.Tokens.TokensInSolver.Select(token => token.Variable == x ? Number(2) : Number(3)).ToArray();
        objective.Evaluate(solution)!.Value.Eq(Number(55)).Should().BeTrue();
    }

    private sealed class QuadraticTestFunction(ProductFunction<Flt64> product, ISymbol hidden, string failure) :
        IQuadraticMathFunctionSymbol<Flt64> {
        public IReadOnlyCollection<ISymbol>? InputSymbols => failure == "unknown" ? null : product.InputSymbols;
        public QuadraticPolynomial<Flt64> Polynomial => product.Polynomial;
        public UInt64 Identifier => product.Identifier;
        public int Index => product.Index;
        public IIntermediateSymbol? Parent => null;
        public string Name { get => product.Name; set => product.Name = value; }
        public string? DisplayName { get => product.DisplayName; set => product.DisplayName = value; }
        public Category Category => product.Category;
        public ExpressionRange<Flt64> Range => product.Range;
        public bool Cached => false;
        public IReadOnlySet<IIntermediateSymbol> Dependencies => product.Dependencies;
        public MutableQuadraticPolynomial<Flt64> AsMutable() => product.AsMutable();
        public Result<Flt64?, ErrorCode, Error<ErrorCode>> TryEvaluate(IReadOnlyDictionary<ISymbol, Flt64> values) {
            if (failure == "fatal") {
                return new Fatal<Flt64?, ErrorCode, Error<ErrorCode>>(
                    new Err<ErrorCode>(ErrorCode.ApplicationStopped, "stopped"),
                    new Err<ErrorCode>(ErrorCode.ApplicationFailed, "failed")
                );
            }
            values.TryGetValue(hidden, out _);
            return Results.Ok<Flt64?>(Flt64.Zero);
        }
        public Flt64? Prepare(
            IReadOnlyDictionary<ISymbol, Flt64>? values,
            IAbstractTokenTable<Flt64> tokenTable,
            IFlt64ValueConverter<Flt64> converter) => null;
        public void PrepareAndCache(
            IReadOnlyDictionary<ISymbol, Flt64>? values,
            IAbstractTokenTable<Flt64> tokenTable,
            IFlt64ValueConverter<Flt64> converter) { }
        public Flt64? Evaluate(IAbstractTokenTable<Flt64> tokenTable, IFlt64ValueConverter<Flt64> converter, bool zeroIfNone = false) => null;
        public Flt64? Evaluate(
            IReadOnlyList<Flt64> results,
            IAbstractTokenTable<Flt64> tokenTable,
            IFlt64ValueConverter<Flt64> converter,
            bool zeroIfNone = false) => null;
        public Flt64? Evaluate(
            IReadOnlyDictionary<ISymbol, Flt64> values,
            IAbstractTokenTable<Flt64>? tokenTable,
            IFlt64ValueConverter<Flt64> converter,
            bool zeroIfNone = false) => null;
        public Try RegisterAuxiliaryTokens(IAddableTokenCollection<Flt64> tokens) => Results.OkInstance;
        public Try RegisterConstraints(IAbstractQuadraticMechanismModel<Flt64> model) => Results.OkInstance;
        public void Flush(bool force = false) { }
        public string ToRawString(UInt64 unfold) => Name;
    }

    private sealed class TrackingLinear(LinearExpressionSymbol inner) : ILinearIntermediateSymbol<Flt64> {
        public int Calls { get; private set; }
        public int EvaluationCalls { get; private set; }
        public bool Forced { get; private set; }
        public LinearPolynomial<Flt64> Polynomial => inner.Polynomial;
        public UInt64 Identifier => inner.Identifier;
        public int Index => inner.Index;
        public IIntermediateSymbol? Parent => null;
        public string Name { get => inner.Name; set => inner.Name = value; }
        public string? DisplayName { get => inner.DisplayName; set => inner.DisplayName = value; }
        public Category Category => inner.Category;
        public ExpressionRange<Flt64> Range => inner.Range;
        public bool Cached => false;
        public IReadOnlySet<IIntermediateSymbol> Dependencies => inner.Dependencies;
        public MutableLinearPolynomial<Flt64> AsMutable() => inner.AsMutable();
        public Flt64? Prepare(
            IReadOnlyDictionary<ISymbol, Flt64>? values,
            IAbstractTokenTable<Flt64> tokenTable,
            IFlt64ValueConverter<Flt64> converter) => null;
        public void PrepareAndCache(
            IReadOnlyDictionary<ISymbol, Flt64>? values,
            IAbstractTokenTable<Flt64> tokenTable,
            IFlt64ValueConverter<Flt64> converter) { }
        public Flt64? Evaluate(IAbstractTokenTable<Flt64> tokenTable, IFlt64ValueConverter<Flt64> converter, bool zeroIfNone = false) => null;
        public Flt64? Evaluate(
            IReadOnlyList<Flt64> results,
            IAbstractTokenTable<Flt64> tokenTable,
            IFlt64ValueConverter<Flt64> converter,
            bool zeroIfNone = false) => null;
        public Flt64? Evaluate(
            IReadOnlyDictionary<ISymbol, Flt64> values,
            IAbstractTokenTable<Flt64>? tokenTable,
            IFlt64ValueConverter<Flt64> converter,
            bool zeroIfNone = false) {
            ++EvaluationCalls;
            return Flt64.One;
        }
        public void Flush(bool force = false) {
            ++Calls;
            Forced = force;
        }
        public string ToRawString(UInt64 unfold) => inner.ToRawString(unfold);
    }
}
