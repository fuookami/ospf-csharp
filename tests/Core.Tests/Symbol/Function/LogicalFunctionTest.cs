#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Algebra.ValueRange;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Symbol.Function;

/// <summary>逻辑指示函数契约测试 / Logical-indicator function contract tests.</summary>
public class LogicalFunctionTest {
    [Fact]
    public async Task Binaryzation_ShouldEvaluateAndRegisterPositiveIndicatorRows() {
        var x = new RealVar("binaryzation-x");
        x.Range.Geq(new Flt64(-2.0));
        x.Range.Leq(new Flt64(2.0));
        var function = new BinaryzationFunction(
            polynomial: Polynomial(x),
            bigM: new Flt64(2.0),
            name: "contract-bin",
            tolerance: new Flt64(0.1)
        );

        function.Evaluate(Values(x, 0.1)).Should().Be(Flt64.One);
        function.Evaluate(Values(x, 0.0)).Should().Be(Flt64.Zero);
        function.Evaluate(Values(x, 0.05)).Should().BeNull();

        LinearMechanismModel<Flt64> model = await BuildModel(function, x);
        var mechanism = (IMechanismModel<Flt64>)model;
        mechanism.Constraints.Select(constraint => constraint.Name).Should().Equal(
            "contract-bin_bin_positive_ub",
            "contract-bin_bin_positive_lb"
        );
        IsFeasible(model, new Dictionary<IVariableItem, Flt64> {
            [x] = new(0.1),
            [function.ResultVar] = Flt64.One,
        }).Should().BeTrue();
        IsFeasible(model, new Dictionary<IVariableItem, Flt64> {
            [x] = new(0.0),
            [function.ResultVar] = Flt64.Zero,
        }).Should().BeTrue();
        IsFeasible(model, new Dictionary<IVariableItem, Flt64> {
            [x] = new(0.05),
            [function.ResultVar] = Flt64.One,
        }).Should().BeFalse();
        IsFeasible(model, new Dictionary<IVariableItem, Flt64> {
            [x] = new(0.05),
            [function.ResultVar] = Flt64.Zero,
        }).Should().BeFalse();
    }

    [Fact]
    public async Task Binaryzation_WhenInputBoundsExpandBeyondProof_ShouldFail() {
        var x = new RealVar("binaryzation-proof-x");
        x.Range.Geq(new Flt64(-2.0));
        x.Range.Leq(new Flt64(2.0));
        var function = new BinaryzationFunction(
            polynomial: Polynomial(x),
            name: "contract-bin-proof"
        );
        x.Range.SetRange(ValueRange<Flt64>.Of(
            new Flt64(-2.0),
            new Flt64(3.0)
        ).Value!);
        using var metaModel = new LinearMetaModel<Flt64>(function.Name);
        metaModel.Add(x).IsOk.Should().BeTrue();
        metaModel.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk.Should().BeTrue();

        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> result =
            await LinearMechanismModel<Flt64>.InvokeAsync(metaModel);

        result.Should().BeOfType<Failed<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>>();
        ((Failed<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>)result).Code
            .Should().Be(ErrorCode.IllegalArgument);
    }

    [Fact]
    public async Task AndAndOr_WhenInputsAreBinary_ShouldUseExactFastPath() {
        var first = new BinVar("binary-logic-first");
        var second = new BinVar("binary-logic-second");
        var and = new AndFunction(
            new[] { Polynomial(first), Polynomial(second) },
            name: "binary-and"
        );
        var or = new OrFunction(
            new[] { Polynomial(first), Polynomial(second) },
            name: "binary-or"
        );

        and.HelperVariables.Should().ContainSingle().Which.Should().BeSameAs(and.ResultVar);
        or.HelperVariables.Should().ContainSingle().Which.Should().BeSameAs(or.ResultVar);
        LinearMechanismModel<Flt64> andModel = await BuildModel(and, first, second);
        LinearMechanismModel<Flt64> orModel = await BuildModel(or, first, second);
        ((IMechanismModel<Flt64>)andModel).Constraints.Should().HaveCount(3);
        ((IMechanismModel<Flt64>)orModel).Constraints.Should().HaveCount(3);

        IsFeasible(andModel, new Dictionary<IVariableItem, Flt64> {
            [first] = Flt64.One,
            [second] = Flt64.One,
            [and.ResultVar] = Flt64.Zero,
        }).Should().BeFalse();
        IsFeasible(andModel, new Dictionary<IVariableItem, Flt64> {
            [first] = Flt64.Zero,
            [second] = Flt64.One,
            [and.ResultVar] = Flt64.One,
        }).Should().BeFalse();
        IsFeasible(orModel, new Dictionary<IVariableItem, Flt64> {
            [first] = Flt64.Zero,
            [second] = Flt64.Zero,
            [or.ResultVar] = Flt64.One,
        }).Should().BeFalse();
        IsFeasible(andModel, new Dictionary<IVariableItem, Flt64> {
            [first] = Flt64.One,
            [second] = Flt64.One,
            [and.ResultVar] = Flt64.One,
        }).Should().BeTrue();
        IsFeasible(orModel, new Dictionary<IVariableItem, Flt64> {
            [first] = Flt64.Zero,
            [second] = Flt64.One,
            [or.ResultVar] = Flt64.One,
        }).Should().BeTrue();
    }

    [Fact]
    public async Task AndOrNot_WhenInputsAreContinuous_ShouldRegisterIndicatorRows() {
        var x = new RealVar("continuous-logic-x");
        var y = new RealVar("continuous-logic-y");
        x.Range.Geq(new Flt64(-2.0));
        x.Range.Leq(new Flt64(2.0));
        y.Range.Geq(new Flt64(-2.0));
        y.Range.Leq(new Flt64(2.0));
        var and = new AndFunction(
            new[] { Polynomial(x), Polynomial(y) },
            bigM: new Flt64(2.0),
            name: "rows-and"
        );
        var or = new OrFunction(
            new[] { Polynomial(x), Polynomial(y) },
            bigM: new Flt64(2.0),
            name: "rows-or"
        );
        var not = new NotFunction(
            polynomial: Polynomial(x),
            bigM: new Flt64(2.0),
            name: "rows-not"
        );

        LinearMechanismModel<Flt64> andModel = await BuildModel(and, x, y);
        LinearMechanismModel<Flt64> orModel = await BuildModel(or, x, y);
        LinearMechanismModel<Flt64> notModel = await BuildModel(not, x);
        ((IMechanismModel<Flt64>)andModel).Constraints.Should().HaveCount(11);
        ((IMechanismModel<Flt64>)orModel).Constraints.Should().HaveCount(11);
        ((IMechanismModel<Flt64>)notModel).Constraints.Should().HaveCount(5);

        and.Evaluate(Values(x, 2.0, y, 0.0)).Should().Be(Flt64.Zero);
        and.Evaluate(Values(x, 2.0, y, -1.0)).Should().Be(Flt64.One);
        or.Evaluate(Values(x, 0.0, y, -1.0)).Should().Be(Flt64.One);
        not.Evaluate(Values(x, 0.0)).Should().Be(Flt64.One);
        not.Evaluate(Values(x, 0.1)).Should().Be(Flt64.Zero);
    }

    [Fact]
    public async Task Xor_ShouldEvaluateZeroBandAndRegisterExactRows() {
        var zero = new LinearPolynomial<Flt64>(
            Array.Empty<LinearMonomial<Flt64>>(),
            Flt64.Zero
        );
        var one = new LinearPolynomial<Flt64>(
            Array.Empty<LinearMonomial<Flt64>>(),
            Flt64.One
        );
        var function = new XorFunction(
            new[] { one, zero, zero },
            bigM: new Flt64(42.0),
            name: "contract-xor"
        );

        function.Evaluate(Values()).Should().Be(Flt64.One);
        new XorFunction(
            polynomials: new[] {
                new LinearPolynomial<Flt64>(
                    Array.Empty<LinearMonomial<Flt64>>(),
                    new Flt64(0.1)
                ),
            },
            tolerance: new Flt64(0.1),
            strictBoundary: new Flt64(0.2),
            name: "xor-band"
        ).Evaluate(Values()).Should().Be(Flt64.Zero);
        new XorFunction(
            polynomials: new[] {
                new LinearPolynomial<Flt64>(
                    Array.Empty<LinearMonomial<Flt64>>(),
                    new Flt64(0.15)
                ),
            },
            tolerance: new Flt64(0.1),
            strictBoundary: new Flt64(0.2),
            name: "xor-gap"
        ).Evaluate(Values()).Should().BeNull();
        new XorFunction(
            polynomials: new[] {
                new LinearPolynomial<Flt64>(
                    Array.Empty<LinearMonomial<Flt64>>(),
                    new Flt64(0.2)
                ),
            },
            tolerance: new Flt64(0.1),
            strictBoundary: new Flt64(0.2),
            name: "xor-boundary"
        ).Evaluate(Values()).Should().Be(Flt64.One);

        LinearMechanismModel<Flt64> model = await BuildModel(function);
        var mechanism = (IMechanismModel<Flt64>)model;
        mechanism.Constraints.Should().HaveCount(19);
        mechanism.Constraints.Select(constraint => constraint.Name).Should().Contain(new[] {
            "contract-xor_xor_nz_0_band_ub",
            "contract-xor_xor_nz_2_out_ub",
            "contract-xor_xor_sum_ub",
            "contract-xor_xor_single_1",
            "contract-xor_xor_pair_1_2",
        });
        var values = new Dictionary<IVariableItem, Flt64> {
            [function.ResultVar] = Flt64.One,
        };
        for (int index = 0; index < function.IndicatorVars.Count; ++index) {
            values[function.IndicatorVars[index]] = index == 0 ? Flt64.One : Flt64.Zero;
            values[function.SideVars[index]] = index == 0 ? Flt64.One : Flt64.Zero;
        }
        IsFeasible(model, values).Should().BeTrue();
        values[function.ResultVar] = Flt64.Zero;
        IsFeasible(model, values).Should().BeFalse();
    }

    [Fact]
    public void Xor_WhenStrictBoundaryDoesNotExceedTolerance_ShouldRejectConstruction() {
        var zero = new LinearPolynomial<Flt64>(
            Array.Empty<LinearMonomial<Flt64>>(),
            Flt64.Zero
        );
        Action create = () => new XorFunction(
            polynomials: new[] { zero },
            tolerance: new Flt64(0.2),
            strictBoundary: new Flt64(0.2),
            name: "invalid-xor"
        );

        create.Should().Throw<ArgumentException>();
    }

    private static async Task<LinearMechanismModel<Flt64>> BuildModel(
        IMathFunctionSymbol<Flt64> function,
        params IVariableItem[] variables) {
        using var metaModel = new LinearMetaModel<Flt64>(function.Name);
        foreach (IVariableItem variable in variables) {
            metaModel.Add(variable).IsOk.Should().BeTrue();
        }
        metaModel.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk.Should().BeTrue();

        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> result =
            await LinearMechanismModel<Flt64>.InvokeAsync(metaModel);
        result.IsOk.Should().BeTrue();
        return ((Ok<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>)result).Value;
    }

    private static bool IsFeasible(
        LinearMechanismModel<Flt64> model,
        IReadOnlyDictionary<IVariableItem, Flt64> values) {
        var solution = model.Tokens.TokensInSolver
            .Select(token => values[token.Variable])
            .ToList();
        return ((IMechanismModel<Flt64>)model).Constraints
            .All(constraint => constraint.IsTrue(solution) == true);
    }

    private static LinearPolynomial<Flt64> Polynomial(IVariableItem variable) => new(
        new[] { new LinearMonomial<Flt64>(Flt64.One, variable) },
        Flt64.Zero
    );

    private static IReadOnlyDictionary<ISymbol, Flt64> Values(
        RealVar? first = null,
        double firstValue = 0.0,
        RealVar? second = null,
        double secondValue = 0.0) {
        var values = new Dictionary<ISymbol, Flt64>();
        if (first is not null) {
            values[first] = new(firstValue);
        }
        if (second is not null) {
            values[second] = new(secondValue);
        }
        return values;
    }
}
