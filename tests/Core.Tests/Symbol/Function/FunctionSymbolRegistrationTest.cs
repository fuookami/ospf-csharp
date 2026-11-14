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
using Fuookami.Ospf.Math.Algebra.ValueRange;
using Fuookami.Ospf.Math.Geometry;
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
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
using UInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;
namespace Fuookami.Ospf.Core.Tests.Symbol.Function;

/// <summary>
/// 函数符号注册测试 / Function symbol registration tests.
/// <para>验证函数接口与注册边界，包括未实现函数的失败。 / Verify function interfaces and registration boundaries, including failures for unimplemented functions.</para>
/// </summary>
public class FunctionSymbolRegistrationTest {
    /// <summary>构造生命周期夹具的约束输入 / Build the constraint input used by lifecycle fixtures.</summary>
    private static IReadOnlyList<LinearConstraintInput<Flt64>> ConstraintInputs() {
        LinearPolynomial<Flt64> lhs = EmptyPolynomial();
        Result<ValueRange<Flt64>, ErrorCode, Error<ErrorCode>> range =
            ValueRange<Flt64>.Of(new Flt64(-1000.0), new Flt64(1000.0));
        Result<LinearConstraintInput<Flt64>, ErrorCode, Error<ErrorCode>> input =
            LinearConstraintInput<Flt64>.From(
                new LinearInequality<Flt64>(lhs, EmptyPolynomial(), Comparison.LE),
                ((Ok<ValueRange<Flt64>, ErrorCode, Error<ErrorCode>>)range).Value,
                Flt64.Zero);
        return new[] { ((Ok<LinearConstraintInput<Flt64>, ErrorCode, Error<ErrorCode>>)input).Value };
    }

    /// <summary>构造满足数量夹具的不等式 / Build the inequality used by satisfied-amount fixtures.</summary>
    private static LinearInequality<Flt64> SatisfiedInequality(Comparison comparison) => new(
        EmptyPolynomial(),
        EmptyPolynomial(),
        comparison
    );

    private static LinearPolynomial<Flt64> EmptyPolynomial() => new(
        Array.Empty<LinearMonomial<Flt64>>(),
        Flt64.Zero
    );

    public static IEnumerable<object[]> AllFunctionTypes() {
        yield return new object[] {
            new AbsFunction<Flt64>(
                polynomial: EmptyPolynomial(),
                converter: IIntoValue<Flt64>.Identity
            ),
        };
        yield return new object[] { new AndFunction(new[] { EmptyPolynomial() }) };
        yield return new object[] { new OrFunction(new[] { EmptyPolynomial() }) };
        yield return new object[] { new NotFunction(EmptyPolynomial()) };
        yield return new object[] { new XorFunction(new[] { EmptyPolynomial() }) };
        yield return new object[] {
            new BalanceTernaryzationFunction(EmptyPolynomial()),
        };
        yield return new object[] { new BigMFunction() };
        yield return new object[] { new BinaryzationFunction(EmptyPolynomial()) };
        yield return new object[] {
            BivariateLinearPiecewiseFunction<Flt64>.Create(
                x: EmptyPolynomial(),
                y: EmptyPolynomial(),
                triangles: new[] { new Triangle<Dim3, Flt64>(
                    P1: Point<Dim3, Flt64>.Point3(
                        x: Flt64.Zero,
                        y: Flt64.Zero,
                        z: Flt64.Zero
                    ),
                    P2: Point<Dim3, Flt64>.Point3(
                        x: Flt64.One,
                        y: Flt64.Zero,
                        z: Flt64.One
                    ),
                    P3: Point<Dim3, Flt64>.Point3(
                        x: Flt64.Zero,
                        y: Flt64.One,
                        z: Flt64.One
                    )
                ) },
                converter: IIntoValue<Flt64>.Identity
            ).Value,
        };
        yield return new object[] {
            new CeilingFunction<Flt64>(EmptyPolynomial(), IIntoValue<Flt64>.Identity),
        };
        yield return new object[] { CosFunction<Flt64>.Create(EmptyPolynomial(), IIntoValue<Flt64>.Identity).Value };
        yield return new object[] { new FirstFunction() };
        yield return new object[] {
            new FloorFunction<Flt64>(EmptyPolynomial(), IIntoValue<Flt64>.Identity),
        };
        yield return new object[] {
            IfFunction<Flt64>.Create(
                condition: EmptyPolynomial(),
                converter: IIntoValue<Flt64>.Identity,
                strictBoundary: Flt64.One
            ).Value,
        };
        yield return new object[] {
            IfInFunction<Flt64>.Create(
                x: EmptyPolynomial(),
                lower: Flt64.Zero,
                upper: Flt64.One,
                converter: IIntoValue<Flt64>.Identity,
                strictBoundary: Flt64.One
            ).Value,
        };
        yield return new object[] {
            IfThenFunction<Flt64>.Create(
                condition: EmptyPolynomial(),
                then: EmptyPolynomial(),
                converter: IIntoValue<Flt64>.Identity,
                strictBoundary: Flt64.One
            ).Value,
        };
        yield return new object[] {
            ImplyFunction<Flt64>.Create(
                antecedent: EmptyPolynomial(),
                consequent: EmptyPolynomial(),
                converter: IIntoValue<Flt64>.Identity,
                strictBoundary: Flt64.One
            ).Value,
        };
        yield return new object[] {
            new InequalityFunction<Flt64>(
                lhs: EmptyPolynomial(),
                rhs: Flt64.Zero,
                sign: Comparison.LE,
                converter: IIntoValue<Flt64>.Identity
            ),
        };
        yield return new object[] {
            new InStepRangeFunction(
                lowerBound: EmptyPolynomial(),
                upperBound: new LinearPolynomial<Flt64>(
                    Array.Empty<LinearMonomial<Flt64>>(),
                    new Flt64(7.0)
                ),
                step: new Flt64(3.0)
            ),
        };
        yield return new object[] {
            new MaskingFunction<Flt64>(
                input: EmptyPolynomial(),
                mask: new BinVar("mask"),
                converter: IIntoValue<Flt64>.Identity,
                bigM: Flt64.One,
                name: "masking"
            ),
        };
        yield return new object[] {
            new MaskingWithPolyMaskFunction<Flt64>(
                input: EmptyPolynomial(),
                maskPolynomial: EmptyPolynomial(),
                converter: IIntoValue<Flt64>.Identity,
                bigM: Flt64.One,
                name: "maskingWithPolyMask"
            ),
        };
        yield return new object[] { new MaskingRangeFunction() };
        yield return new object[] { new MaxFunction<Flt64>(new[] { EmptyPolynomial() }, IIntoValue<Flt64>.Identity) };
        yield return new object[] { new MinFunction<Flt64>(new[] { EmptyPolynomial() }, IIntoValue<Flt64>.Identity) };
        yield return new object[] { new MinMaxFunction<Flt64>(new[] { EmptyPolynomial() }, IIntoValue<Flt64>.Identity) };
        yield return new object[] { new MaxMinFunction<Flt64>(new[] { EmptyPolynomial() }, IIntoValue<Flt64>.Identity) };
        yield return new object[] {
            new ModFunction(
                polynomial: EmptyPolynomial(),
                divisor: new Flt64(2.0)
            ),
        };
        yield return new object[] {
            new OneOfFunction(new[] { EmptyPolynomial() }),
        };
        yield return new object[] { PositivePartFunction<Flt64>.Create(EmptyPolynomial(), IIntoValue<Flt64>.Identity).Value };
        yield return new object[] {
            new QuadraticInStepRangeFunction(
                x: new QuadraticPolynomial<Flt64>(
                    new[] { QuadraticMonomial<Flt64>.Linear(Flt64.One, new RealVar("registration-step")) },
                    Flt64.Zero
                ),
                lower: Flt64.Zero,
                upper: Flt64.One,
                converter: IIntoValue<Flt64>.Identity
            ),
        };
        yield return new object[] {
            new QuadraticLinearFunction(
                polynomial: new QuadraticPolynomial<Flt64>(
                    new[] { QuadraticMonomial<Flt64>.Linear(Flt64.One, new RealVar("registration-linear")) },
                    Flt64.Zero
                ),
                converter: IIntoValue<Flt64>.Identity
            ),
        };
        yield return new object[] {
            new QuadraticMinFunction(
                polynomials: new[] {
                    new QuadraticPolynomial<Flt64>(
                        Array.Empty<QuadraticMonomial<Flt64>>(),
                        Flt64.Zero
                    ),
                },
                converter: IIntoValue<Flt64>.Identity
            ),
        };
        yield return new object[] {
            new RoundingFunction<Flt64>(EmptyPolynomial(), IIntoValue<Flt64>.Identity),
        };
        yield return new object[] {
            new SameAsFunction(
                inequalities: new[] {
                    new LinearInequality<Flt64>(
                        EmptyPolynomial(),
                        EmptyPolynomial(),
                        Comparison.EQ
                    ),
                },
                epsilon: Flt64.One
            ),
        };
        yield return new object[] {
            new SatisfiedAmountFunction(new[] { SatisfiedInequality(Comparison.LE) }, Flt64.One),
        };
        yield return new object[] { new SatisfiedAmountInequalityFunction(ConstraintInputs()) };
        yield return new object[] { new AnyFunction(ConstraintInputs()) };
        yield return new object[] { new AllFunction(ConstraintInputs()) };
        yield return new object[] {
            new AtLeastInequalityFunction(ConstraintInputs(), new UInt64(1)),
        };
        yield return new object[] { new NotAllFunction(ConstraintInputs()) };
        yield return new object[] {
            new NumerableFunction(
                ConstraintInputs(),
                SatisfiedAmountRange.CountRange(0, 1)),
        };
        yield return new object[] { new SemiFunction() };
        yield return new object[] {
            SigmoidFunction<Flt64>.Create(
                condition: EmptyPolynomial(),
                converter: IIntoValue<Flt64>.Identity,
                strictBoundary: Flt64.One
            ).Value,
        };
        yield return new object[] { SinFunction<Flt64>.Create(EmptyPolynomial(), IIntoValue<Flt64>.Identity).Value };
        yield return new object[] {
            new SlackFunction<Flt64>(
                x: EmptyPolynomial(),
                y: EmptyPolynomial(),
                converter: IIntoValue<Flt64>.Identity,
                name: "slack"
            ),
        };
        yield return new object[] {
            SlackRangeFunction<Flt64>.Create(
                input: EmptyPolynomial(),
                lower: Flt64.Zero,
                upper: Flt64.One,
                converter: IIntoValue<Flt64>.Identity
            ).Value,
        };
        yield return new object[] {
            UnivariateLinearPiecewiseFunction<Flt64>.Create(
                input: EmptyPolynomial(),
                breakpoints: new[] { Flt64.Zero, Flt64.One },
                slopes: new[] { Flt64.One },
                intercepts: new[] { Flt64.Zero },
                converter: IIntoValue<Flt64>.Identity
            ).Value,
        };
    }

    [Theory]
    [MemberData(nameof(AllFunctionTypes))]
    public void FunctionSymbol_ImplementsInterface(IMathFunctionSymbol<Flt64> function) {
        function.Should().NotBeNull();
        function.Name.Should().NotBeNullOrEmpty();
    }

    [Theory]
    [MemberData(nameof(AllFunctionTypes))]
    public async Task FunctionSymbol_RegisterConstraints_RequiresDeclaredInputs(IMathFunctionSymbol<Flt64> function) {
        using var metaModel = new LinearMetaModel<Flt64>("function-registration");
        var adapter = new LinearFunctionSymbolAdapter<Flt64>(function);
        Try added = metaModel.Add(adapter);
        added.IsOk.Should().BeTrue();

        Try result = (await LinearMechanismModel<Flt64>.InvokeAsync(metaModel))
            .Map(_ => Results.SuccessInstance);
        if (function.InputSymbols is null) {
            result.Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>()
                .Which.Code.Should().Be(ErrorCode.IllegalArgument);
        }
        else {
            result.IsOk.Should().BeTrue();
        }
    }

    [Fact]
    public async Task LinearAdapter_ShouldRegisterHelpersAndInvokeFunctionConstraints() {
        var helper = new RealVar("adapter-helper");
        var function = new TestFunction("adapter-function", helper);
        using var metaModel = new LinearMetaModel<Flt64>("adapter");
        var adapter = new LinearFunctionSymbolAdapter<Flt64>(function);

        metaModel.Add(adapter).IsOk.Should().BeTrue();
        metaModel.Tokens.Find(helper).Should().NotBeNull();

        Try result = (await LinearMechanismModel<Flt64>.InvokeAsync(metaModel))
            .Map(_ => Results.SuccessInstance);
        result.IsOk.Should().BeTrue();
        function.AuxiliaryRegistrationCalls.Should().Be(2);
        function.LinearRegistrationCalls.Should().Be(1);
    }

    [Fact]
    public void Add_WhenAuxiliaryRegistrationFails_ShouldRollbackSymbolAndHelpers() {
        var helper = new RealVar("failed-helper");
        var function = new TestFunction("failed-function", helper, failAuxiliary: true);
        using var metaModel = new LinearMetaModel<Flt64>("rollback");

        Try result = metaModel.Add(new LinearFunctionSymbolAdapter<Flt64>(function));

        result.IsFailed.Should().BeTrue();
        metaModel.Tokens.Symbols.Should().BeEmpty();
        metaModel.Tokens.Find(helper).Should().BeNull();
    }

    [Fact]
    public void Add_WhenAuxiliaryRegistrationIsFatal_ShouldRollbackAndPropagateFatal() {
        var helper = new RealVar("fatal-helper");
        var function = new TestFunction(
            name: "fatal-function",
            helper: helper,
            fatalAuxiliary: true
        );
        using var metaModel = new LinearMetaModel<Flt64>("fatal-rollback");

        Try result = metaModel.Add(new LinearFunctionSymbolAdapter<Flt64>(function));

        result.Should().BeOfType<Fatal<Success, ErrorCode, Error<ErrorCode>>>();
        ((Fatal<Success, ErrorCode, Error<ErrorCode>>)result).Errors
            .Should().ContainSingle()
            .Which.Code.Should().Be(ErrorCode.IllegalArgument);
        metaModel.Tokens.Symbols.Should().BeEmpty();
        metaModel.Tokens.Find(helper).Should().BeNull();
    }

    [Fact]
    public void Register_WhenAuxiliaryRegistrationIsFatal_ShouldRollbackAndPropagateFatal() {
        var helper = new RealVar("fatal-token-registration-helper");
        var function = new TestFunction(
            name: "fatal-token-registration",
            helper: helper,
            fatalAuxiliary: true
        );
        var adapter = new LinearFunctionSymbolAdapter<Flt64>(function);
        using var table = new ConcurrentAutoTokenTable<Flt64>(
            LinearCategory.Instance,
            new List<IIntermediateSymbol>()
        );

        Try result = new IIntermediateSymbol[] { adapter }.Register(table);

        result.Should().BeOfType<Fatal<Success, ErrorCode, Error<ErrorCode>>>();
        table.TokenList.Find(helper).Should().BeNull();
        table.Symbols.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Add_WhenAuxiliaryRegistrationFails_ShouldRestoreAutomaticIndex(bool fatal) {
        using var metaModel = new LinearMetaModel<Flt64>(
            configuration: new MetaModelConfiguration(ManualTokenAddition: false)
        );
        var existing = new RealVar("meta-index-existing");
        var removed = new RealVar("meta-index-removed");
        var helper = new RealVar("meta-index-helper");
        metaModel.Add(existing).IsOk.Should().BeTrue();
        metaModel.Add(removed).IsOk.Should().BeTrue();
        metaModel.Remove(removed);
        Token<Flt64> token = metaModel.Tokens.Find(existing)!;
        int callbacks = token.RefreshCallbacks.Count;
        var function = new TestFunction(
            name: "meta-index-failure",
            helper: helper,
            failAuxiliary: !fatal,
            fatalAuxiliary: fatal
        );

        Try result = metaModel.Add(new LinearFunctionSymbolAdapter<Flt64>(function));

        result.IsFailed.Should().BeTrue();
        (result is Fatal<Success, ErrorCode, Error<ErrorCode>>).Should().Be(fatal);
        metaModel.Tokens.Symbols.Should().BeEmpty();
        metaModel.Tokens.Find(helper).Should().BeNull();
        metaModel.Tokens.Find(existing).Should().BeSameAs(token);
        token.RefreshCallbacks.Should().HaveCount(callbacks);
        var next = new RealVar("meta-index-next");
        metaModel.Add(next).IsOk.Should().BeTrue();
        metaModel.Tokens.Find(next)!.SolverIndex.Should().Be(2);
    }

    [Fact]
    public void Add_WhenAuxiliaryRegistrationSucceeds_ShouldKeepAutomaticIndex() {
        using var metaModel = new LinearMetaModel<Flt64>(
            configuration: new MetaModelConfiguration(ManualTokenAddition: false)
        );
        var existing = new RealVar("meta-success-existing");
        var helper = new RealVar("meta-success-helper");
        metaModel.Add(existing).IsOk.Should().BeTrue();
        metaModel.Add(new LinearFunctionSymbolAdapter<Flt64>(
            new TestFunction("meta-success-function", helper)
        )).IsOk.Should().BeTrue();
        var next = new RealVar("meta-success-next");
        metaModel.Add(next).IsOk.Should().BeTrue();

        metaModel.Tokens.Find(helper)!.SolverIndex.Should().Be(1);
        metaModel.Tokens.Find(next)!.SolverIndex.Should().Be(2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Register_WhenProgressFails_ShouldRestoreAutomaticIndex(bool fatal) {
        using var table = new ConcurrentAutoTokenTable<Flt64>(
            LinearCategory.Instance,
            new List<IIntermediateSymbol>()
        );
        var existing = new RealVar("registration-index-existing");
        var removed = new RealVar("registration-index-removed");
        var helper = new RealVar("registration-index-helper");
        table.Add(existing).IsOk.Should().BeTrue();
        table.Add(removed).IsOk.Should().BeTrue();
        table.Remove(removed);
        Token<Flt64> token = table.TokenList.Find(existing)!;
        int callbacks = token.RefreshCallbacks.Count;
        var function = new LinearFunctionSymbolAdapter<Flt64>(
            new TestFunction("registration-index-failure", helper)
        );

        Try result = new IIntermediateSymbol[] { function }.Register(
            tokenTable: table,
            callBack: status => status.ReadySymbolAmount.ToULong() == 0
                ? Results.OkInstance
                : fatal
                    ? new Fatal<Success, ErrorCode, Error<ErrorCode>>(
                        new Err<ErrorCode>(ErrorCode.ApplicationStopped, "stopped"))
                    : Results.Failed<Success>(
                        new Err<ErrorCode>(ErrorCode.ApplicationStopped, "stopped"))
        );

        result.IsFailed.Should().BeTrue();
        (result is Fatal<Success, ErrorCode, Error<ErrorCode>>).Should().Be(fatal);
        table.Symbols.Should().BeEmpty();
        table.TokenList.Find(helper).Should().BeNull();
        table.TokenList.Find(existing).Should().BeSameAs(token);
        token.RefreshCallbacks.Should().HaveCount(callbacks);
        var next = new RealVar("registration-index-next");
        table.Add(next).IsOk.Should().BeTrue();
        table.TokenList.Find(next)!.SolverIndex.Should().Be(2);
    }

    [Fact]
    public void Register_WhenExistingTokenIsReplaced_ShouldRestoreItsIdentityAndIndex() {
        using var table = new ConcurrentAutoTokenTable<Flt64>(
            LinearCategory.Instance,
            new List<IIntermediateSymbol>()
        );
        var existing = new RealVar("registration-replaced-existing");
        var helper = new RealVar("registration-replaced-helper");
        table.Add(existing).IsOk.Should().BeTrue();
        Token<Flt64> original = table.TokenList.Find(existing)!;
        var function = new LinearFunctionSymbolAdapter<Flt64>(
            new TestFunction("registration-replaced-failure", helper)
        );

        Try result = new IIntermediateSymbol[] { function }.Register(
            tokenTable: table,
            callBack: status => {
                if (status.ReadySymbolAmount.ToULong() == 0) {
                    return Results.OkInstance;
                }
                table.Remove(existing);
                table.Add(existing).IsOk.Should().BeTrue();
                return Results.Failed<Success>(
                    new Err<ErrorCode>(ErrorCode.ApplicationStopped, "stopped"));
            }
        );

        result.IsFailed.Should().BeTrue();
        table.TokenList.Find(helper).Should().BeNull();
        table.TokenList.Find(existing).Should().BeSameAs(original);
        table.TokenList.Find(existing)!.SolverIndex.Should().Be(0);
        var next = new RealVar("registration-replaced-next");
        table.Add(next).IsOk.Should().BeTrue();
        table.TokenList.Find(next)!.SolverIndex.Should().Be(1);
    }

    [Fact]
    public void Add_WhenSymbolNameIsRepeated_ShouldRejectBeforeRegisteringHelpers() {
        var firstHelper = new RealVar("first-helper");
        var secondHelper = new RealVar("second-helper");
        var first = new TestFunction("repeated-function", firstHelper);
        var second = new TestFunction("repeated-function", secondHelper);
        using var metaModel = new LinearMetaModel<Flt64>("duplicate");

        metaModel.Add(new LinearFunctionSymbolAdapter<Flt64>(first)).IsOk.Should().BeTrue();
        Try result = metaModel.Add(new LinearFunctionSymbolAdapter<Flt64>(second));

        result.Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
        ((Failed<Success, ErrorCode, Error<ErrorCode>>)result).Code
            .Should().Be(ErrorCode.SymbolRepetitive);
        metaModel.Tokens.Symbols.Should().ContainSingle();
        metaModel.Tokens.Find(secondHelper).Should().BeNull();
    }

    [Fact]
    public async Task LinearMechanismModel_WhenFunctionRegistrationFails_ShouldReturnFailure() {
        var function = new TestFunction("failing-function", failLinear: true);
        using var metaModel = new LinearMetaModel<Flt64>("failing");
        metaModel.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk.Should().BeTrue();

        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> result =
            await LinearMechanismModel<Flt64>.InvokeAsync(metaModel);

        result.Should().BeOfType<Failed<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>>();
        ((Failed<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>)result).Code
            .Should().Be(ErrorCode.OREngineSolvingException);
        function.LinearRegistrationCalls.Should().Be(1);
    }

    [Fact]
    public async Task LinearMechanismModel_WhenFunctionRegistrationIsFatal_ShouldReturnFatal() {
        var function = new TestFunction("fatal-function", fatalLinear: true);
        using var metaModel = new LinearMetaModel<Flt64>("fatal-function-registration");
        metaModel.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk.Should().BeTrue();

        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> result =
            await LinearMechanismModel<Flt64>.InvokeAsync(metaModel);

        result.Should().BeOfType<Fatal<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>>();
        ((Fatal<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>)result).Errors
            .Should().ContainSingle()
            .Which.Code.Should().Be(ErrorCode.OREngineSolvingException);
        function.LinearRegistrationCalls.Should().Be(1);
    }

    [Fact]
    public async Task QuadraticMechanismModel_ShouldPreferQuadraticRegistration() {
        var function = new DualRegistrationFunction("dual-function");
        using var metaModel = new QuadraticMetaModel<Flt64>("quadratic");
        metaModel.Add(function).IsOk.Should().BeTrue();

        Result<QuadraticMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> result =
            await QuadraticMechanismModel<Flt64>.InvokeAsync(metaModel);

        result.IsOk.Should().BeTrue();
        function.QuadraticRegistrationCalls.Should().Be(1);
        function.LinearRegistrationCalls.Should().Be(0);
    }

    [Fact]
    public async Task QuadraticMechanismModel_WhenFunctionRegistrationIsFatal_ShouldReturnFatal() {
        var function = new DualRegistrationFunction(
            name: "fatal-quadratic-function",
            fatalQuadratic: true
        );
        using var metaModel = new QuadraticMetaModel<Flt64>("fatal-quadratic");
        metaModel.Add(function).IsOk.Should().BeTrue();

        Result<QuadraticMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> result =
            await QuadraticMechanismModel<Flt64>.InvokeAsync(metaModel);

        result.Should().BeOfType<Fatal<QuadraticMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>>();
        ((Fatal<QuadraticMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>)result).Errors
            .Should().ContainSingle()
            .Which.Code.Should().Be(ErrorCode.OREngineSolvingException);
        function.QuadraticRegistrationCalls.Should().Be(1);
        function.LinearRegistrationCalls.Should().Be(0);
    }

    [Fact]
    public void LinearAdapter_ShouldExposeStablePolynomialDependenciesAndEvaluation() {
        var x = new RealVar("adapter-x");
        var dependency = LinearExpressionSymbol.FromVariable(x);
        var polynomial = new LinearPolynomial<Flt64>(
            new List<LinearMonomial<Flt64>> { new(Flt64.One, dependency) },
            Flt64.One);
        var function = new TestFunction("polynomial-function", polynomial: polynomial);
        var adapter = new LinearFunctionSymbolAdapter<Flt64>(function);
        var values = new Dictionary<ISymbol, Flt64> { [dependency] = new Flt64(3) };
        var converter = new IdentityFlt64Converter();
        UInt64 identifier = adapter.Identifier;

        adapter.Polynomial.Should().BeSameAs(polynomial);
        adapter.Identifier.Should().Be(identifier);
        adapter.Dependencies.Should().ContainSingle().Which.Should().BeSameAs(dependency);
        polynomial.Monomials[0].Symbol.Should().BeSameAs(dependency);
        values.ContainsKey(polynomial.Monomials[0].Symbol).Should().BeTrue();
        adapter.Evaluate(values, null, converter).Should().Be(new Flt64(4));
        adapter.Evaluate(values).Should().Be(new Flt64(4));
    }

    [Fact]
    public void FunctionSymbol_Count_AtLeast37() {
        var functionTypes = typeof(AbsFunction<>).Assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && t.GetInterfaces().Any(contract =>
                contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(IMathFunctionSymbol<>)))
            .ToList();
        functionTypes.Count.Should().BeGreaterThanOrEqualTo(37);
    }

    [Fact]
    public void LinearPolynomialBounds_CanBeCreated() {
        var bounds = new LinearPolynomialBounds<Flt64>(
            LowerBound: Flt64.Zero,
            UpperBound: Flt64.One
        );
        bounds.LowerBound.Should().Be(Flt64.Zero);
        bounds.UpperBound.Should().Be(Flt64.One);
    }

    private sealed class TestFunction : IMathFunctionSymbol<Flt64>, IHasResultPolynomial<Flt64> {
        private readonly bool _failAuxiliary;
        private readonly bool _failLinear;
        private readonly bool _fatalAuxiliary;
        private readonly bool _fatalLinear;
        private readonly LinearPolynomial<Flt64> _polynomial;

        public string Name { get; set; }
        public string? DisplayName { get; set; }
        public IReadOnlyList<IVariableItem> HelperVariables { get; }
        public LinearPolynomial<Flt64> ResultPolynomial => _polynomial;
        public IReadOnlyCollection<ISymbol> InputSymbols => _polynomial.Monomials.Select(term => term.Symbol).ToArray();
        public int AuxiliaryRegistrationCalls { get; private set; }
        public int LinearRegistrationCalls { get; private set; }

        public TestFunction(
            string name,
            IVariableItem? helper = null,
            LinearPolynomial<Flt64>? polynomial = null,
            bool failAuxiliary = false,
            bool failLinear = false,
            bool fatalAuxiliary = false,
            bool fatalLinear = false) {
            Name = name;
            HelperVariables = helper is null ? Array.Empty<IVariableItem>() : new[] { helper };
            _polynomial = polynomial ?? new LinearPolynomial<Flt64>(
                Array.Empty<LinearMonomial<Flt64>>(), Flt64.Zero);
            _failAuxiliary = failAuxiliary;
            _failLinear = failLinear;
            _fatalAuxiliary = fatalAuxiliary;
            _fatalLinear = fatalLinear;
        }

        public Flt64? Evaluate(IReadOnlyDictionary<ISymbol, Flt64> values) =>
            _polynomial.Monomials.Count == 1 &&
            values.TryGetValue(_polynomial.Monomials[0].Symbol, out Flt64 value)
                ? value.Plus(_polynomial.Constant)
                : _polynomial.Constant;

        public Try RegisterAuxiliaryTokens(IAddableTokenCollection<Flt64> tokens) {
            AuxiliaryRegistrationCalls++;
            foreach (IVariableItem helper in HelperVariables) {
                Try added = tokens.Add(helper);
                if (added.IsFailed) {
                    return added;
                }
            }
            if (_fatalAuxiliary) {
                return new Fatal<Success, ErrorCode, Error<ErrorCode>>(
                    new Err<ErrorCode>(ErrorCode.IllegalArgument, "auxiliary fatal failure")
                );
            }
            return _failAuxiliary
                ? Results.Failed<Success>(
                    new Err<ErrorCode>(ErrorCode.IllegalArgument, "auxiliary failure"))
                : Results.Ok(Results.SuccessInstance);
        }

        public Try RegisterConstraints(IAbstractLinearMechanismModel<Flt64> model) {
            LinearRegistrationCalls++;
            if (_fatalLinear) {
                return new Fatal<Success, ErrorCode, Error<ErrorCode>>(
                    new Err<ErrorCode>(ErrorCode.OREngineSolvingException, "linear fatal failure")
                );
            }
            return _failLinear
                ? Results.Failed<Success>(
                    new Err<ErrorCode>(ErrorCode.OREngineSolvingException, "linear failure"))
                : Results.Ok(Results.SuccessInstance);
        }
    }

    private sealed class DualRegistrationFunction :
        IIntermediateSymbol<Flt64>,
        IMathFunctionSymbol<Flt64>,
        IQuadraticMathFunctionSymbolBase<Flt64> {
        private readonly UInt64 _identifier = IdentifierGenerator.Gen();

        public string Name { get; set; }
        public string? DisplayName { get; set; }
        public UInt64 Identifier => _identifier;
        public int Index => 0;
        public IIntermediateSymbol? Parent => null;
        public ExpressionRange<Flt64> Range => ExpressionRange<Flt64>.Create();
        public Category Category => QuadraticCategory.Instance;
        public bool Cached => false;
        public IReadOnlySet<IIntermediateSymbol> Dependencies =>
            new HashSet<IIntermediateSymbol>();
        public IReadOnlyList<IVariableItem> HelperVariables => Array.Empty<IVariableItem>();
        public int QuadraticRegistrationCalls { get; private set; }
        public int LinearRegistrationCalls { get; private set; }
        private readonly bool _fatalQuadratic;

        public DualRegistrationFunction(string name, bool fatalQuadratic = false) {
            Name = name;
            _fatalQuadratic = fatalQuadratic;
        }

        public Flt64? Prepare(
            IReadOnlyDictionary<ISymbol, Flt64>? values,
            IAbstractTokenTable<Flt64> tokenTable,
            IFlt64ValueConverter<Flt64> converter) => null;

        public void PrepareAndCache(
            IReadOnlyDictionary<ISymbol, Flt64>? values,
            IAbstractTokenTable<Flt64> tokenTable,
            IFlt64ValueConverter<Flt64> converter) { }

        public Flt64? Evaluate(
            IAbstractTokenTable<Flt64> tokenTable,
            IFlt64ValueConverter<Flt64> converter,
            bool zeroIfNone = false) => null;

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

        public Flt64? Evaluate(IReadOnlyDictionary<ISymbol, Flt64> values) => null;

        public Try RegisterAuxiliaryTokens(IAddableTokenCollection<Flt64> tokens) =>
            Results.Ok(Results.SuccessInstance);

        public Try RegisterConstraints(IAbstractLinearMechanismModel<Flt64> model) {
            LinearRegistrationCalls++;
            return Results.Ok(Results.SuccessInstance);
        }

        public Try RegisterConstraints(IAbstractQuadraticMechanismModel<Flt64> model) {
            QuadraticRegistrationCalls++;
            return _fatalQuadratic
                ? new Fatal<Success, ErrorCode, Error<ErrorCode>>(
                    new Err<ErrorCode>(ErrorCode.OREngineSolvingException, "quadratic fatal failure"))
                : Results.Ok(Results.SuccessInstance);
        }

        public void Flush(bool force = false) { }

        public string ToRawString(UInt64 unfold) => DisplayName ?? Name;
    }

    private sealed class IdentityFlt64Converter : IFlt64ValueConverter<Flt64> {
        public Flt64 Zero => Flt64.Zero;
        public Flt64 One => Flt64.One;
        public Flt64 IntoValue(Flt64 value) => value;
        public Flt64 FromValue(Flt64 value) => value;
    }
}
