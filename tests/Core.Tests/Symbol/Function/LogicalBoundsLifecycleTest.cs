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
using MathUInt8 = Fuookami.Ospf.Math.Algebra.Number.UInt8;
using MathUInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;
namespace Fuookami.Ospf.Core.Tests.Symbol.Function;

public class LogicalBoundsLifecycleTest {
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public async Task AutomaticBounds_ShouldInvalidateLinearAndQuadraticDumps(int kind) {
        RealVar x = Bounded("tracked", 1);
        RealVar unrelated = Bounded("unrelated", 1);
        IMathFunctionSymbol<Flt64> function = Create(kind, Polynomial(x));
        using var linearParent = new LinearMetaModel<Flt64>();
        using var quadraticParent = new QuadraticMetaModel<Flt64>();
        linearParent.Add(x).IsOk.Should().BeTrue();
        linearParent.Add(unrelated).IsOk.Should().BeTrue();
        linearParent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk.Should().BeTrue();
        quadraticParent.Add(x).IsOk.Should().BeTrue();
        quadraticParent.Add(unrelated).IsOk.Should().BeTrue();
        quadraticParent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk.Should().BeTrue();
        using LinearMechanismModel<Flt64> linear = (await LinearMechanismModel<Flt64>.InvokeAsync(linearParent)).Value;
        using QuadraticMechanismModel<Flt64> quadratic = (await QuadraticMechanismModel<Flt64>.InvokeAsync(quadraticParent)).Value;
        using LinearTriadModel original = linear.ToLinearTriadModel().Value;

        unrelated.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-2), new Flt64(2)).Value);
        using (LinearTriadModel changed = linear.ToLinearTriadModel().Value) {
            changed.Variables.Single(variable => variable.Origin == unrelated).UpperBound.Should().Be(new Flt64(2));
        }
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-4), new Flt64(4)).Value);
        linear.ToLinearTriadModel()
            .Should().BeOfType<Failed<LinearTriadModel, ErrorCode, Error<ErrorCode>>>()
            .Which.Error.Code.Should().Be(ErrorCode.IllegalArgument);
        quadratic.ToQuadraticTetradModel()
            .Should().BeOfType<Failed<QuadraticTetradModel, ErrorCode, Error<ErrorCode>>>()
            .Which.Error.Code.Should().Be(ErrorCode.IllegalArgument);
        original.Variables.Single(variable => variable.Origin == x).UpperBound.Should().Be(Flt64.One);
        using LinearMechanismModel<Flt64> rebuilt = (await LinearMechanismModel<Flt64>.InvokeAsync(linearParent)).Value;
        using LinearTriadModel matrix = rebuilt.ToLinearTriadModel().Value;
        matrix.Variables.Single(variable => variable.Origin == x).UpperBound.Should().Be(new Flt64(4));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public async Task ExplicitBigM_ShouldNotDependOnInputBounds(int kind) {
        RealVar x = Bounded("explicit", 1);
        IMathFunctionSymbol<Flt64> function = Create(
            kind: kind,
            input: Polynomial(x),
            bigM: new Flt64(10)
        );
        using var parent = new LinearMetaModel<Flt64>();
        parent.Add(x).IsOk.Should().BeTrue();
        parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk.Should().BeTrue();
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-4), new Flt64(4)).Value);
        model.ValidateBoundDependencies().IsOk.Should().BeTrue();
        using LinearTriadModel matrix = model.ToLinearTriadModel().Value;
        matrix.Variables.Single(variable => variable.Origin == x).UpperBound.Should().Be(new Flt64(4));
    }

    [Theory]
    [InlineData(0, 4)]
    [InlineData(1, 4)]
    [InlineData(2, 4)]
    [InlineData(3, 4)]
    [InlineData(4, 4)]
    [InlineData(5, 4)]
    [InlineData(0, -4)]
    [InlineData(1, -4)]
    [InlineData(2, -4)]
    [InlineData(3, -4)]
    [InlineData(4, -4)]
    [InlineData(5, -4)]
    public async Task NestedAutomaticBounds_ShouldUseExpandedVariablesRatherThanDeclaredRange(int kind, double value) {
        RealVar x = Bounded("nested", 4);
        var nested = new RangedLinearSymbol(x);
        IMathFunctionSymbol<Flt64> function = Create(kind, Polynomial(nested));
        using var parent = new LinearMetaModel<Flt64>();
        parent.Add(x).IsOk.Should().BeTrue();
        parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk.Should().BeTrue();
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        IReadOnlyList<IConstraint<Flt64, LinearCategory>> rows = ((IMechanismModel<Flt64>)model).Constraints;
        BinVar[] binaryHelpers = function.HelperVariables.OfType<BinVar>().ToArray();
        bool feasible = Enumerable.Range(0, 1 << binaryHelpers.Length).Any(mask => {
            var values = new Dictionary<IVariableItem, Flt64> { [x] = new Flt64(value) };
            for (int index = 0; index < binaryHelpers.Length; ++index) {
                values[binaryHelpers[index]] = new Flt64((mask >> index) & 1);
            }
            if (function is SameAsFunction sameAs) {
                foreach (RealVar residual in sameAs.ResidualVars.OfType<RealVar>()) {
                    values[residual] = new Flt64(value);
                }
            }
            var solution = model.Tokens.TokensInSolver.Select(token => values[token.Variable]).ToList();
            return rows.All(row => row.IsTrue(solution) == true);
        });
        feasible.Should().BeTrue();
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-8), new Flt64(8)).Value);
        model.ToLinearTriadModel().IsFailed.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public async Task NestedAutomaticBounds_ShouldRejectUnboundedUnderlyingVariables(int kind) {
        var x = new RealVar("unbounded-under-declared-range");
        x.Range.SetRange(ValueRange<Flt64>.Full());
        IMathFunctionSymbol<Flt64> function = Create(kind, Polynomial(new RangedLinearSymbol(x)));
        using var parent = new LinearMetaModel<Flt64>();
        parent.Add(x).IsOk.Should().BeTrue();
        foreach (IVariableItem helper in function.HelperVariables) {
            parent.Add(helper).IsOk.Should().BeTrue();
        }
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        function.RegisterConstraints(model).IsFailed.Should().BeTrue();
        ((IMechanismModel<Flt64>)model).Constraints.Should().BeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public async Task MutableInputTerms_ShouldNotChangeFunctionOrBinaryFastPath(int kind) {
        var first = new BinVar("original-input");
        var replacement = new BinVar("replacement-input");
        var terms = new List<LinearMonomial<Flt64>> { new(Flt64.One, first) };
        IMathFunctionSymbol<Flt64> function = Create(
            kind: kind,
            input: new LinearPolynomial<Flt64>(terms, Flt64.Zero),
            bigM: new Flt64(10)
        );
        terms[0] = new LinearMonomial<Flt64>(Flt64.One, replacement);
        function.InputSymbols.Should().ContainSingle().Which.Should().BeSameAs(first);
        if (function is AndFunction and) {
            and.BinaryInputs.Should().BeAssignableTo<IList<BinVar>>().Which.IsReadOnly.Should().BeTrue();
        }
        if (function is OrFunction or) {
            or.BinaryInputs.Should().BeAssignableTo<IList<BinVar>>().Which.IsReadOnly.Should().BeTrue();
        }
        using var parent = new LinearMetaModel<Flt64>();
        parent.Add(first).IsOk.Should().BeTrue();
        parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk.Should().BeTrue();
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        using LinearTriadModel matrix = model.ToLinearTriadModel().Value;
        matrix.Variables.Select(variable => variable.Origin).Should().Contain(first).And.NotContain(replacement);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(4, false)]
    [InlineData(5, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    [InlineData(4, true)]
    [InlineData(5, true)]
    public async Task ManualRegistration_ShouldRejectMissingVariablesBeforeAnyRows(int kind, bool missingHelper) {
        RealVar x = Bounded("missing-input", 1);
        IMathFunctionSymbol<Flt64> function = Create(kind, Polynomial(x));
        using var parent = new LinearMetaModel<Flt64>();
        if (missingHelper) {
            parent.Add(x).IsOk.Should().BeTrue();
        }
        foreach (IVariableItem helper in missingHelper ? function.HelperVariables.SkipLast(1) : function.HelperVariables) {
            parent.Add(helper).IsOk.Should().BeTrue();
        }
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;

        function.RegisterConstraints(model).IsFailed.Should().BeTrue();
        ((IMechanismModel<Flt64>)model).Constraints.Should().BeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public async Task ManualRegistration_ShouldTrackAutomaticBounds(int kind) {
        RealVar x = Bounded("manual-input", 1);
        IMathFunctionSymbol<Flt64> function = Create(kind, Polynomial(x));
        using var parent = new LinearMetaModel<Flt64>();
        parent.Add(x).IsOk.Should().BeTrue();
        foreach (IVariableItem helper in function.HelperVariables) {
            parent.Add(helper).IsOk.Should().BeTrue();
        }
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        function.RegisterConstraints(model).IsOk.Should().BeTrue();

        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-2), new Flt64(2)).Value);
        model.ValidateBoundDependencies().IsFailed.Should().BeTrue();
        model.ToLinearTriadModel().IsFailed.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public async Task LaterInputChangingEarlierBounds_ShouldFailBeforeAnyRows(int kind) {
        RealVar first = Bounded("first-input", 1);
        RealVar second = Bounded("second-input", 1);
        var nested = new RangedLinearSymbol(second) {
            OnRead = () => first.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-2), new Flt64(2)).Value),
        };
        LinearPolynomial<Flt64> left = Polynomial(first);
        LinearPolynomial<Flt64> right = Polynomial(nested);
        IMathFunctionSymbol<Flt64> function = kind switch {
            0 => new AndFunction(new[] { left, right }),
            1 => new OrFunction(new[] { left, right }),
            3 => new XorFunction(new[] { left, right }),
            4 => new OneOfFunction(new[] { left, right }),
            5 => new SameAsFunction(new[] { left.Le(Flt64.Zero), right.Le(Flt64.Zero) }, new Flt64(0.125)),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        using var parent = new LinearMetaModel<Flt64>();
        parent.Add(first).IsOk.Should().BeTrue();
        parent.Add(second).IsOk.Should().BeTrue();
        foreach (IVariableItem helper in function.HelperVariables) {
            parent.Add(helper).IsOk.Should().BeTrue();
        }
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;

        function.RegisterConstraints(model).IsFailed.Should().BeTrue();
        ((IMechanismModel<Flt64>)model).Constraints.Should().BeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task BinaryFastPath_ShouldNotTrackUnusedBigMBounds(int kind) {
        var x = new BinVar("binary-fast-input");
        IMathFunctionSymbol<Flt64> function = Create(kind, Polynomial(x));
        using var parent = new LinearMetaModel<Flt64>();
        parent.Add(x).IsOk.Should().BeTrue();
        parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk.Should().BeTrue();
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;

        x.Range.SetRange(ValueRange<MathUInt8>.Of(MathUInt8.Zero, MathUInt8.Zero).Value);
        model.ValidateBoundDependencies().IsOk.Should().BeTrue();
        using LinearTriadModel matrix = model.ToLinearTriadModel().Value;
        matrix.Variables.Single(variable => variable.Origin == x).UpperBound.Should().Be(Flt64.Zero);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    public async Task BinaryFastPath_ShouldRejectMissingVariablesBeforeAnyRows(int kind, bool missingHelper) {
        var first = new BinVar("binary-first");
        var second = new BinVar("binary-second");
        IMathFunctionSymbol<Flt64> function = kind switch {
            0 => new AndFunction(new[] { Polynomial(first), Polynomial(second) }),
            1 => new OrFunction(new[] { Polynomial(first), Polynomial(second) }),
            2 => new NotFunction(Polynomial(first)),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        using var parent = new LinearMetaModel<Flt64>();
        if (missingHelper || kind != 2) {
            parent.Add(first).IsOk.Should().BeTrue();
        }
        if (missingHelper && kind != 2) {
            parent.Add(second).IsOk.Should().BeTrue();
        }
        if (!missingHelper) {
            foreach (IVariableItem helper in function.HelperVariables) {
                parent.Add(helper).IsOk.Should().BeTrue();
            }
        }
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;

        function.RegisterConstraints(model).IsFailed.Should().BeTrue();
        ((IMechanismModel<Flt64>)model).Constraints.Should().BeEmpty();
    }

    [Fact]
    public async Task ContinuousAnd_ShouldForceResultForEveryInputCombination() {
        RealVar first = Bounded("and-first", 1);
        RealVar second = Bounded("and-second", 1);
        var function = new AndFunction(
            polynomials: new[] { Polynomial(first), Polynomial(second) },
            tolerance: new Flt64(0.125),
            strictBoundary: new Flt64(0.25)
        );
        using var parent = new LinearMetaModel<Flt64>();
        parent.Add(first).IsOk.Should().BeTrue();
        parent.Add(second).IsOk.Should().BeTrue();
        parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk.Should().BeTrue();
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        IVariableItem[] indicators = function.IndicatorVars.Cast<IVariableItem>().Concat(function.SideVars).ToArray();

        foreach (int firstValue in new[] { -1, 0, 1 }) {
            foreach (int secondValue in new[] { -1, 0, 1 }) {
                foreach (int result in new[] { 0, 1 }) {
                    bool feasible = Enumerable.Range(0, 1 << indicators.Length).Any(mask => {
                        var values = new Dictionary<IVariableItem, Flt64> {
                            [first] = new Flt64(firstValue),
                            [second] = new Flt64(secondValue),
                            [function.ResultVar] = new Flt64(result),
                        };
                        for (int index = 0; index < indicators.Length; ++index) {
                            values[indicators[index]] = new Flt64((mask >> index) & 1);
                        }
                        var solution = model.Tokens.TokensInSolver.Select(token => values[token.Variable]).ToList();
                        return ((IMechanismModel<Flt64>)model).Constraints.All(row => row.IsTrue(solution) == true);
                    });
                    bool expected = result == (firstValue != 0 && secondValue != 0 ? 1 : 0);
                    feasible.Should().Be(expected, $"inputs ({firstValue}, {secondValue}) and result {result}");
                }
            }
        }
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(4, false)]
    [InlineData(5, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    [InlineData(4, true)]
    [InlineData(5, true)]
    public async Task EqualOrNarrowerReplacement_ShouldInvalidateAutomaticBounds(int kind, bool narrower) {
        RealVar x = Bounded("replaced-input", 1);
        ValueRange<Flt64> original = x.Range.Range ?? throw new InvalidOperationException("Missing range.");
        IMathFunctionSymbol<Flt64> function = Create(kind, Polynomial(x));
        using var parent = new LinearMetaModel<Flt64>();
        parent.Add(x).IsOk.Should().BeTrue();
        parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk.Should().BeTrue();
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;

        double limit = narrower ? 0.5 : 1;
        x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-limit), new Flt64(limit)).Value);
        model.ToLinearTriadModel().IsFailed.Should().BeTrue();
        x.Range.SetRange(original);
        model.ValidateBoundDependencies().IsOk.Should().BeTrue();
    }

    [Theory]
    [InlineData(0, 4)]
    [InlineData(1, 4)]
    [InlineData(2, 4)]
    [InlineData(3, 4)]
    [InlineData(4, 4)]
    [InlineData(5, 4)]
    [InlineData(0, 5)]
    [InlineData(1, 5)]
    [InlineData(2, 5)]
    [InlineData(3, 5)]
    [InlineData(4, 5)]
    [InlineData(5, 5)]
    public async Task RangeChangeDuringRegistration_ShouldFailWithoutLeavingRows(int kind, int triggerRead) {
        RealVar x = Bounded("registration-input", 1);
        var alias = new ChangingRangeVariable(
            inner: x,
            triggerRead: triggerRead,
            change: () => x.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-2), new Flt64(2)).Value)
        );
        IMathFunctionSymbol<Flt64> function = Create(kind, Polynomial(alias));
        using var parent = new LinearMetaModel<Flt64>();
        parent.Add(x).IsOk.Should().BeTrue();
        foreach (IVariableItem helper in function.HelperVariables) {
            parent.Add(helper).IsOk.Should().BeTrue();
        }
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;

        function.RegisterConstraints(model).IsFailed.Should().BeTrue();
        alias.Triggered.Should().BeTrue();
        ((IMechanismModel<Flt64>)model).Constraints.Should().BeEmpty();
        model.ValidateBoundDependencies().IsOk.Should().BeTrue();
        Result<LinearTriadModel, ErrorCode, Error<ErrorCode>> dumped = model.ToLinearTriadModel();
        dumped.IsOk.Should().BeTrue();
        dumped.Value.Dispose();
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(4, false)]
    [InlineData(5, false)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    [InlineData(4, true)]
    [InlineData(5, true)]
    public async Task NestedCycleOrSelfReference_ShouldFailBeforeAnyRows(int kind, bool cycle) {
        RealVar x = Bounded("nested-cycle", 1);
        var nested = new RangedLinearSymbol(x);
        IMathFunctionSymbol<Flt64> function = Create(kind, Polynomial(nested));
        nested.Override = Polynomial(cycle ? nested : function.HelperVariables[0]);
        using var parent = new LinearMetaModel<Flt64>();
        parent.Add(x).IsOk.Should().BeTrue();
        foreach (IVariableItem helper in function.HelperVariables) {
            parent.Add(helper).IsOk.Should().BeTrue();
        }
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;

        function.RegisterConstraints(model).IsFailed.Should().BeTrue();
        ((IMechanismModel<Flt64>)model).Constraints.Should().BeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public async Task NestedFunction_ShouldRequireRegistrationAndTrackItsResultBounds(int kind) {
        var x = new BinVar("nested-function-input");
        var nested = new NotFunction(Polynomial(x), name: "nested-not");
        var adapter = new LinearFunctionSymbolAdapter<Flt64>(nested);
        IMathFunctionSymbol<Flt64> function = Create(kind, Polynomial(adapter));
        using var missingParent = new LinearMetaModel<Flt64>();
        missingParent.Add(x).IsOk.Should().BeTrue();
        foreach (IVariableItem helper in function.HelperVariables) {
            missingParent.Add(helper).IsOk.Should().BeTrue();
        }
        using LinearMechanismModel<Flt64> missingModel = (await LinearMechanismModel<Flt64>.InvokeAsync(missingParent)).Value;
        function.RegisterConstraints(missingModel).IsFailed.Should().BeTrue();
        ((IMechanismModel<Flt64>)missingModel).Constraints.Should().BeEmpty();

        using var parent = new LinearMetaModel<Flt64>();
        parent.Add(x).IsOk.Should().BeTrue();
        parent.Add(adapter).IsOk.Should().BeTrue();
        parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk.Should().BeTrue();
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        nested.ResultVar.Range.SetRange(ValueRange<MathUInt8>.Of(MathUInt8.Zero, MathUInt8.Zero).Value);
        model.ToLinearTriadModel().IsFailed.Should().BeTrue();
    }

    [Fact]
    public void SameAs_ShouldFreezeBothSidesAndPreserveInequalityMetadata() {
        var first = new BinVar("lhs-original");
        var second = new BinVar("rhs-original");
        var replacement = new BinVar("replacement");
        var left = new List<LinearMonomial<Flt64>> { new(Flt64.One, first) };
        var right = new List<LinearMonomial<Flt64>> { new(Flt64.One, second) };
        var inequality = new LinearInequality<Flt64>(
            Lhs: new LinearPolynomial<Flt64>(left, Flt64.Zero),
            Rhs: new LinearPolynomial<Flt64>(right, Flt64.One),
            Comparison: Comparison.GE,
            Name: "original-name",
            DisplayName: "original-display"
        );
        var function = new SameAsFunction(new[] { inequality }, Flt64.One);
        left[0] = new LinearMonomial<Flt64>(Flt64.One, replacement);
        right[0] = new LinearMonomial<Flt64>(Flt64.One, replacement);

        function.InputSymbols.Should().BeEquivalentTo(new ISymbol[] { first, second });
        function.Inequalities[0].Name.Should().Be("original-name");
        function.Inequalities[0].DisplayName.Should().Be("original-display");
        function.Inequalities[0].Comparison.Should().Be(Comparison.GE);
        function.Inequalities[0].Rhs.Constant.Should().Be(Flt64.One);
    }

    private static RealVar Bounded(string name, double limit) {
        var variable = new RealVar(name);
        variable.Range.SetRange(ValueRange<Flt64>.Of(new Flt64(-limit), new Flt64(limit)).Value);
        return variable;
    }

    private static LinearPolynomial<Flt64> Polynomial(ISymbol symbol) => new(
        new[] { new LinearMonomial<Flt64>(Flt64.One, symbol) },
        Flt64.Zero
    );

    private static IMathFunctionSymbol<Flt64> Create(
        int kind,
        LinearPolynomial<Flt64> input,
        Flt64? bigM = null) => kind switch {
        0 => new AndFunction(
            polynomials: new[] { input },
            bigM: bigM,
            tolerance: new Flt64(0.125),
            strictBoundary: new Flt64(0.25)
        ),
        1 => new OrFunction(
            polynomials: new[] { input },
            bigM: bigM,
            tolerance: new Flt64(0.125),
            strictBoundary: new Flt64(0.25)
        ),
        2 => new NotFunction(
            polynomial: input,
            bigM: bigM,
            tolerance: new Flt64(0.125),
            strictBoundary: new Flt64(0.25)
        ),
        3 => new XorFunction(
            polynomials: new[] { input },
            bigM: bigM,
            tolerance: new Flt64(0.125),
            strictBoundary: new Flt64(0.25)
        ),
        4 => new OneOfFunction(
            polynomials: new[] { input },
            bigM: bigM,
            tolerance: new Flt64(0.125),
            strictBoundary: new Flt64(0.25)
        ),
        5 => new SameAsFunction(
            inequalities: new[] { input.Le(Flt64.Zero) },
            epsilon: new Flt64(0.125),
            constraint: false,
            bigM: bigM
        ),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private sealed class RangedLinearSymbol(IVariableItem variable) : ILinearIntermediateSymbol<Flt64> {
        private readonly LinearExpressionSymbol _inner = LinearExpressionSymbol.FromVariable(variable, "declared-range");
        public string Name { get => _inner.Name; set => _inner.Name = value; }
        public string? DisplayName { get => _inner.DisplayName; set => _inner.DisplayName = value; }
        public MathUInt64 Identifier => _inner.Identifier;
        public int Index => _inner.Index;
        public IIntermediateSymbol? Parent => null;
        public Category Category => LinearCategory.Instance;
        public bool Cached => false;
        public ExpressionRange<Flt64> Range => ExpressionRange<Flt64>.Create(ValueRange<Flt64>.Of(new Flt64(-1), Flt64.One).Value);
        public IReadOnlySet<IIntermediateSymbol> Dependencies => _inner.Dependencies;
        public Action? OnRead { get; set; }
        public LinearPolynomial<Flt64>? Override { get; set; }
        public LinearPolynomial<Flt64> Polynomial {
            get {
                OnRead?.Invoke();
                return Override ?? _inner.Polynomial;
            }
        }
        public MutableLinearPolynomial<Flt64> AsMutable() => _inner.AsMutable();
        public Flt64? Prepare(
            IReadOnlyDictionary<ISymbol, Flt64>? values,
            IAbstractTokenTable<Flt64> tokenTable,
            IFlt64ValueConverter<Flt64> converter) => _inner.Prepare(
                values: values,
                tokenTable: tokenTable,
                converter: converter
            );
        public void PrepareAndCache(
            IReadOnlyDictionary<ISymbol, Flt64>? values,
            IAbstractTokenTable<Flt64> tokenTable,
            IFlt64ValueConverter<Flt64> converter) => _inner.PrepareAndCache(
                values: values,
                tokenTable: tokenTable,
                converter: converter
            );
        public Flt64? Evaluate(
            IAbstractTokenTable<Flt64> tokenTable,
            IFlt64ValueConverter<Flt64> converter,
            bool zeroIfNone = false) => _inner.Evaluate(
                tokenTable: tokenTable,
                converter: converter,
                zeroIfNone: zeroIfNone
            );
        public Flt64? Evaluate(
            IReadOnlyList<Flt64> results,
            IAbstractTokenTable<Flt64> tokenTable,
            IFlt64ValueConverter<Flt64> converter,
            bool zeroIfNone = false) => _inner.Evaluate(
                results: results,
                tokenTable: tokenTable,
                converter: converter,
                zeroIfNone: zeroIfNone
            );
        public Flt64? Evaluate(
            IReadOnlyDictionary<ISymbol, Flt64> values,
            IAbstractTokenTable<Flt64>? tokenTable,
            IFlt64ValueConverter<Flt64> converter,
            bool zeroIfNone = false) => _inner.Evaluate(
                values: values,
                tokenTable: tokenTable,
                converter: converter,
                zeroIfNone: zeroIfNone
            );
        public void Flush(bool force = false) => _inner.Flush(force);
        public string ToRawString(MathUInt64 unfold) => _inner.ToRawString(unfold);
    }

    private sealed class ChangingRangeVariable(IVariableItem inner, int triggerRead, Action change) :
        IVariableItem, IVariableRangeSnapshot {
        private int _reads;
        public bool Triggered { get; private set; }
        public MathUInt64 Identifier => inner.Identifier;
        public int Index => inner.Index;
        public VariableItemKey Key => inner.Key;
        public string Name => inner.Name;
        public string? DisplayName => inner.DisplayName;
        public IVariableTypeKind TypeKind => inner.TypeKind;
        public Bound<Flt64>? LowerBound => inner.LowerBound;
        public Bound<Flt64>? UpperBound => inner.UpperBound;
        public object? CurrentRange {
            get {
                if (++_reads == triggerRead) {
                    Triggered = true;
                    change();
                }
                return ((IVariableRangeSnapshot)inner).CurrentRange;
            }
        }
        public bool BelongsTo(IVariableItem item) => inner.BelongsTo(item);
        public bool BelongsTo(IVariableCombination combination) => inner.BelongsTo(combination);
    }
}
