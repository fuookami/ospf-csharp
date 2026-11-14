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
using Fuookami.Ospf.Math.Algebra.ValueRange;
using Fuookami.Ospf.Math.Geometry;
using Fuookami.Ospf.Math.Symbol;
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
using Integer = Fuookami.Ospf.Math.Algebra.Number.Int64;
namespace Fuookami.Ospf.Core.Tests.Symbol.Function;

public class BivariateLinearPiecewiseFunctionTest {
    [Theory]
    [InlineData(0, 0, 2)]
    [InlineData(1, 0, 10)]
    [InlineData(0, 1, 20)]
    [InlineData(0.25, 0.25, 8.5)]
    [InlineData(0.5, 0.5, 15)]
    public void Evaluation_ShouldInterpolateTheCorrespondingVertexValues(double xValue, double yValue, double expected) {
        var x = new RealVar("surface-x");
        var y = new RealVar("surface-y");
        BivariateLinearPiecewiseFunction<Flt64> function = Create(x, y).Value;
        Result<Flt64?, ErrorCode, Error<ErrorCode>> result = function.TryEvaluate(new Dictionary<ISymbol, Flt64> {
            [x] = new Flt64(xValue),
            [y] = new Flt64(yValue),
        });
        Assert.True(result.IsOk, result.ToString());
        Assert.Equal(new Flt64(expected), result.Value);
        Assert.True(function.TryEvaluate(new Dictionary<ISymbol, Flt64> { [x] = Flt64.Zero }).IsOk);
        Assert.Null(function.Evaluate(new Dictionary<ISymbol, Flt64> { [x] = Flt64.Zero }));
    }

    [Fact]
    public void Evaluation_ShouldFollowTheBaselineBoundaryTolerance() {
        var x = new RealVar("surface-tolerance-x");
        var y = new RealVar("surface-tolerance-y");
        BivariateLinearPiecewiseFunction<Flt64> function = Create(x, y).Value;
        Assert.NotNull(function.Evaluate(new Dictionary<ISymbol, Flt64> { [x] = new Flt64(-0.5e-12), [y] = Flt64.Zero }));
        Assert.Null(function.Evaluate(new Dictionary<ISymbol, Flt64> { [x] = new Flt64(-2e-12), [y] = Flt64.Zero }));
        Assert.True(function.TryEvaluate(new Dictionary<ISymbol, Flt64> { [x] = new Flt64(double.NaN), [y] = Flt64.Zero }).IsFailed);
    }

    [Fact]
    public void Evaluation_ShouldNotOverflowAValidLargeTriangleDeterminant() {
        var x = new RealVar("surface-large-x");
        var y = new RealVar("surface-large-y");
        BivariateLinearPiecewiseFunction<Flt64> function = Create(
            x: x,
            y: y,
            triangles: new[] { new Triangle<Dim3, Flt64>(
                P1: Point(
                    x: 0,
                    y: 0,
                    z: 2
                ),
                P2: Point(
                    x: 1e200,
                    y: 0,
                    z: 10
                ),
                P3: Point(
                    x: 0,
                    y: 1e200,
                    z: 20
                )
            ) }
        ).Value;
        Result<Flt64?, ErrorCode, Error<ErrorCode>> evaluated = function.TryEvaluate(
            new Dictionary<ISymbol, Flt64> { [x] = Flt64.Zero, [y] = Flt64.Zero });
        Assert.True(evaluated.IsOk, evaluated.ToString());
        Assert.Equal(new Flt64(2), evaluated.Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Evaluation_ShouldRoundExactMidpointsToEvenInBothOrientations(bool reverse) {
        double next = global::System.Math.BitIncrement(1.0);
        double previous = global::System.Math.BitDecrement(-1.0);
        (double First, double Second, double Expected)[] cases = {
            (First: 1.0, Second: next, Expected: 1.0),
            (First: next, Second: global::System.Math.BitIncrement(next), Expected: global::System.Math.BitIncrement(next)),
            (First: -1.0, Second: previous, Expected: -1.0),
            (First: previous, Second: global::System.Math.BitDecrement(previous), Expected: global::System.Math.BitDecrement(previous)),
            (First: 0.0, Second: double.Epsilon, Expected: 0.0),
            (First: 0.0, Second: -double.Epsilon, Expected: -0.0),
        };
        foreach ((double first, double second, double expected) in cases) {
            var x = new RealVar("surface-round-x");
            var y = new RealVar("surface-round-y");
            Point<Dim3, Flt64> p1 = Point(
                x: 0,
                y: 0,
                z: first
            );
            Point<Dim3, Flt64> p2 = Point(
                x: 2,
                y: 0,
                z: second
            );
            Point<Dim3, Flt64> p3 = Point(
                x: 0,
                y: 2,
                z: first
            );
            BivariateLinearPiecewiseFunction<Flt64> function = Create(
                x: x,
                y: y,
                triangles: new[] { new Triangle<Dim3, Flt64>(
                    P1: p1,
                    P2: reverse ? p3 : p2,
                    P3: reverse ? p2 : p3
                ) }
            ).Value;
            Result<Flt64?, ErrorCode, Error<ErrorCode>> result = function.TryEvaluate(
                new Dictionary<ISymbol, Flt64> { [x] = Flt64.One, [y] = Flt64.Zero });
            Assert.True(result.IsOk, result.ToString());
            Assert.NotNull(result.Value);
            Assert.Equal(BitConverter.DoubleToInt64Bits(expected), BitConverter.DoubleToInt64Bits(result.Value!.Value.ToDouble()));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Factory_ShouldUseTheExactDegeneracyThresholdInBothOrientations(bool reverse) {
        var x = new RealVar("surface-threshold-x");
        var y = new RealVar("surface-threshold-y");
        double threshold = 1e-12;
        foreach ((double height, bool valid) in new[] {
            (global::System.Math.BitDecrement(threshold), false),
            (threshold, false),
            (global::System.Math.BitIncrement(threshold), true),
        }) {
            Point<Dim3, Flt64> p1 = Point(
                x: 0,
                y: 0,
                z: 0
            );
            Point<Dim3, Flt64> p2 = Point(
                x: 1,
                y: 0,
                z: 1
            );
            Point<Dim3, Flt64> p3 = Point(
                x: 0,
                y: height,
                z: 2
            );
            Assert.Equal(valid, Create(
                x: x,
                y: y,
                triangles: new[] { new Triangle<Dim3, Flt64>(
                    P1: p1,
                    P2: reverse ? p3 : p2,
                    P3: reverse ? p2 : p3
                ) }
            ).IsOk);
        }
    }

    [Fact]
    public void FactoryAndEvaluation_ShouldRejectLossyNumericConversions() {
        var x = new RealVar("surface-conversion-x");
        var y = new RealVar("surface-conversion-y");
        var single = new TestNumberConverter<Flt32>(value => value.ToFlt32());
        var integer = new TestNumberConverter<Integer>(value => value.ToInt64());
        Triangle<Dim3, Flt64> lossy = new(
            P1: Point(
                x: 0,
                y: 0,
                z: 0.1
            ),
            P2: Point(
                x: 1,
                y: 0,
                z: 1
            ),
            P3: Point(
                x: 0,
                y: 1,
                z: 2
            )
        );
        Assert.True(BivariateLinearPiecewiseFunction<Flt32>.Create(
            x: new LinearPolynomial<Flt32>(new[] { new LinearMonomial<Flt32>(single.One, x) }, single.Zero),
            y: new LinearPolynomial<Flt32>(new[] { new LinearMonomial<Flt32>(single.One, y) }, single.Zero),
            triangles: new[] { lossy },
            converter: single
        ).IsFailed);
        BivariateLinearPiecewiseFunction<Integer> function = BivariateLinearPiecewiseFunction<Integer>.Create(
            x: new LinearPolynomial<Integer>(new[] { new LinearMonomial<Integer>(integer.One, x) }, integer.Zero),
            y: new LinearPolynomial<Integer>(new[] { new LinearMonomial<Integer>(integer.One, y) }, integer.Zero),
            triangles: new[] { new Triangle<Dim3, Flt64>(
                P1: Point(
                    x: 0,
                    y: 0,
                    z: 0
                ),
                P2: Point(
                    x: 2,
                    y: 0,
                    z: 1
                ),
                P3: Point(
                    x: 0,
                    y: 2,
                    z: 1
                )
            ) },
            converter: integer
        ).Value;
        Assert.True(function.TryEvaluate(new Dictionary<ISymbol, Integer> { [x] = integer.One, [y] = integer.Zero }).IsFailed);
    }

    [Theory]
    [InlineData(1.0, 0.0, 0.0)]
    [InlineData(0.0, 1.0, 0.0)]
    [InlineData(0.0, 0.0, 1.0)]
    [InlineData(0.5, 0.25, 0.25)]
    public async Task Rows_ShouldMatchTheBarycentricSurface(double first, double second, double third) {
        var x = new RealVar("surface-row-x");
        var y = new RealVar("surface-row-y");
        BivariateLinearPiecewiseFunction<Flt64> function = Create(x, y).Value;
        var adapter = new LinearFunctionSymbolAdapter<Flt64>(function);
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(y).IsOk);
        Assert.True(parent.Add(adapter).IsOk);
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built = await LinearMechanismModel<Flt64>.InvokeAsync(parent);
        Assert.True(built.IsOk, built.ToString());
        using LinearMechanismModel<Flt64> model = built.Value;
        using LinearTriadModel matrix = model.ToLinearTriadModel().Value;
        Assert.Equal(5, ((IMechanismModel<Flt64>)model).Constraints.Count);
        var assignment = new Dictionary<IVariableItem, Flt64> {
            [x] = new Flt64(second),
            [y] = new Flt64(third),
            [function.LambdaVars[0][0]] = new Flt64(first),
            [function.LambdaVars[0][1]] = new Flt64(second),
            [function.LambdaVars[0][2]] = new Flt64(third),
            [function.SelectorVars[0]] = Flt64.One,
        };
        Assert.True(SatisfiedAmountResidualTest.ExactFeasible(model, assignment));
        Assert.True(SatisfiedAmountResidualTest.ExactFeasible(matrix, assignment));
        Flt64 expected = new((2 * first) + (10 * second) + (20 * third));
        Assert.Equal(expected, function.Evaluate(new Dictionary<ISymbol, Flt64> { [x] = new Flt64(second), [y] = new Flt64(third) }));
        Assert.Equal(expected, adapter.Evaluate(
            values: assignment.ToDictionary(pair => (ISymbol)pair.Key, pair => pair.Value),
            tokenTable: null,
            converter: IIntoValue<Flt64>.Identity
        ));
        assignment[function.SelectorVars[0]] = Flt64.Zero;
        Assert.False(SatisfiedAmountResidualTest.ExactFeasible(model, assignment));
        Assert.False(SatisfiedAmountResidualTest.ExactFeasible(matrix, assignment));
    }

    [Fact]
    public void Factory_ShouldRejectDegenerateAndNonFiniteTrianglesAndSnapshotCoordinates() {
        var x = new RealVar("surface-invalid-x");
        var y = new RealVar("surface-invalid-y");
        Flt64[] coordinates = { Flt64.Zero, Flt64.Zero, new Flt64(2) };
        var mutablePoint = new Point<Dim3, Flt64>(coordinates, Dim3.Instance);
        var triangle = new Triangle<Dim3, Flt64>(
            P1: mutablePoint,
            P2: Point(
                x: 1,
                y: 0,
                z: 10
            ),
            P3: Point(
                x: 0,
                y: 1,
                z: 20
            )
        );
        var triangles = new List<Triangle<Dim3, Flt64>> { triangle };
        BivariateLinearPiecewiseFunction<Flt64> function = Create(
            x: x,
            y: y,
            triangles: triangles
        ).Value;
        coordinates[2] = new Flt64(99);
        triangles.Clear();
        Assert.Equal(new Flt64(2), function.Evaluate(new Dictionary<ISymbol, Flt64> { [x] = Flt64.Zero, [y] = Flt64.Zero }));
        Assert.True(Create(
            x: x,
            y: y,
            triangles: Array.Empty<Triangle<Dim3, Flt64>>()
        ).IsFailed);
        Assert.True(Create(
            x: x,
            y: y,
            triangles: new[] { new Triangle<Dim3, Flt64>(
                P1: Point(
                    x: 0,
                    y: 0,
                    z: 0
                ),
                P2: Point(
                    x: 1,
                    y: 1,
                    z: 1
                ),
                P3: Point(
                    x: 2,
                    y: 2,
                    z: 2
                )
            ) }
        ).IsFailed);
        Assert.True(Create(
            x: x,
            y: y,
            triangles: new[] { new Triangle<Dim3, Flt64>(
                P1: Point(
                    x: 0,
                    y: 0,
                    z: 0
                ),
                P2: Point(
                    x: 1,
                    y: 0,
                    z: double.NaN
                ),
                P3: Point(
                    x: 0,
                    y: 1,
                    z: 1
                )
            ) }
        ).IsFailed);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Preparation_ShouldCacheOnlyAUniqueSurfaceWithoutConflictingHelpers(bool quadratic, bool conflict) {
        var x = new RealVar("surface-cache-x");
        var y = new RealVar("surface-cache-y");
        BivariateLinearPiecewiseFunction<Flt64> function = Create(x, y).Value;
        var adapter = new LinearFunctionSymbolAdapter<Flt64>(function);
        var values = new Dictionary<IVariableItem, Flt64> { [x] = new Flt64(0.25), [y] = new Flt64(0.25) };
        if (conflict) {
            values[function.LambdaVars[0][0]] = Flt64.Zero;
        }
        using var linear = new LinearMetaModel<Flt64>();
        using var quadraticModel = new QuadraticMetaModel<Flt64>();
        if (quadratic) {
            Assert.True(quadraticModel.Add(x).IsOk);
            Assert.True(quadraticModel.Add(y).IsOk);
            Assert.True(quadraticModel.Add(adapter).IsOk);
            Result<QuadraticMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built = await QuadraticMechanismModel<Flt64>.InvokeAsync(
                metaModel: quadraticModel,
                fixedVariables: values
            );
            Assert.True(built.IsOk, built.ToString());
            using QuadraticMechanismModel<Flt64> model = built.Value;
            Assert.Equal(conflict ? null : new Flt64(8.5), model.Tokens.CachedValue(adapter, (IReadOnlyList<Flt64>?)null));
        }
        else {
            Assert.True(linear.Add(x).IsOk);
            Assert.True(linear.Add(y).IsOk);
            Assert.True(linear.Add(adapter).IsOk);
            Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built = await LinearMechanismModel<Flt64>.InvokeAsync(
                metaModel: linear,
                fixedVariables: values
            );
            Assert.True(built.IsOk, built.ToString());
            using LinearMechanismModel<Flt64> model = built.Value;
            Assert.Equal(conflict ? null : new Flt64(8.5), model.Tokens.CachedValue(adapter, (IReadOnlyList<Flt64>?)null));
        }
    }

    [Fact]
    public async Task Preparation_ShouldNotCacheOverlappingOrToleranceOnlyInputs() {
        var x = new RealVar("surface-overlap-x");
        var y = new RealVar("surface-overlap-y");
        Triangle<Dim3, Flt64> first = new(
            P1: Point(
                x: 0,
                y: 0,
                z: 2
            ),
            P2: Point(
                x: 1,
                y: 0,
                z: 10
            ),
            P3: Point(
                x: 0,
                y: 1,
                z: 20
            )
        );
        Triangle<Dim3, Flt64> second = new(
            P1: Point(
                x: 0,
                y: 0,
                z: 4
            ),
            P2: Point(
                x: 1,
                y: 0,
                z: 12
            ),
            P3: Point(
                x: 0,
                y: 1,
                z: 22
            )
        );
        foreach (bool overlap in new[] { false, true }) {
            BivariateLinearPiecewiseFunction<Flt64> function = Create(
                x: x,
                y: y,
                triangles: overlap ? new[] { first, second } : new[] { first }
            ).Value;
            var adapter = new LinearFunctionSymbolAdapter<Flt64>(function);
            using var parent = new LinearMetaModel<Flt64>();
            Assert.True(parent.Add(x).IsOk);
            Assert.True(parent.Add(y).IsOk);
            Assert.True(parent.Add(adapter).IsOk);
            var values = new Dictionary<IVariableItem, Flt64> {
                [x] = overlap ? new Flt64(0.25) : new Flt64(-0.5e-12),
                [y] = overlap ? new Flt64(0.25) : Flt64.Zero,
            };
            Assert.NotNull(function.Evaluate(values.ToDictionary(pair => (ISymbol)pair.Key, pair => pair.Value)));
            Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built = await LinearMechanismModel<Flt64>.InvokeAsync(
                metaModel: parent,
                fixedVariables: values
            );
            Assert.True(built.IsOk, built.ToString());
            using LinearMechanismModel<Flt64> model = built.Value;
            Assert.Null(model.Tokens.CachedValue(adapter, (IReadOnlyList<Flt64>?)null));
        }
    }

    [Fact]
    public async Task Preparation_ShouldPropagateRevalidatedBarycentricEvidenceToNestedConditions() {
        var x = new RealVar("surface-nested-x");
        var y = new RealVar("surface-nested-y");
        var producer = new LinearFunctionSymbolAdapter<Flt64>(Create(x, y).Value);
        IfFunction<Flt64> condition = IfFunction<Flt64>.Create(
            condition: new LinearPolynomial<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, producer) }, new Flt64(-8)),
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: new Flt64(0.5)
        ).Value;
        var consumer = new LinearFunctionSymbolAdapter<Flt64>(condition);
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(y).IsOk);
        Assert.True(parent.Add(producer).IsOk);
        Assert.True(parent.Add(consumer).IsOk);
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built = await LinearMechanismModel<Flt64>.InvokeAsync(
            metaModel: parent,
            fixedVariables: new Dictionary<IVariableItem, Flt64> { [x] = new Flt64(0.25), [y] = new Flt64(0.25) }
        );
        Assert.True(built.IsOk, built.ToString());
        using LinearMechanismModel<Flt64> model = built.Value;
        Assert.Equal(new Flt64(8.5), model.Tokens.CachedValue(producer, (IReadOnlyList<Flt64>?)null));
        Assert.Equal(Flt64.One, model.Tokens.CachedValue(consumer, (IReadOnlyList<Flt64>?)null));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void Preparation_ShouldRevalidateEvidenceBeforeTheNestedConsumer(int change) {
        var x = new RealVar("surface-revalidate-x");
        var y = new RealVar("surface-revalidate-y");
        var converter = new SwitchingConverter();
        BivariateLinearPiecewiseFunction<Flt64> function = BivariateLinearPiecewiseFunction<Flt64>.Create(
            x: Polynomial(x),
            y: Polynomial(y),
            triangles: new[] { new Triangle<Dim3, Flt64>(
                P1: Point(
                    x: 0,
                    y: 0,
                    z: 2
                ),
                P2: Point(
                    x: 1,
                    y: 0,
                    z: 10
                ),
                P3: Point(
                    x: 0,
                    y: 1,
                    z: 20
                )
            ) },
            converter: converter
        ).Value;
        var producer = new LinearFunctionSymbolAdapter<Flt64>(function);
        var consumer = new LinearFunctionSymbolAdapter<Flt64>(IfFunction<Flt64>.Create(
            condition: new LinearPolynomial<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, producer) }, new Flt64(-8)),
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: new Flt64(0.5)
        ).Value);
        var values = new Dictionary<ISymbol, Flt64> { [x] = new Flt64(0.25), [y] = new Flt64(0.25) };
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(y).IsOk);
        Result<Success, ErrorCode, Error<ErrorCode>> registered = new IIntermediateSymbol[] { producer, consumer }.Register(
            tokenTable: parent.Tokens,
            fixedValues: values,
            callBack: status => {
                if (status.ReadySymbolAmount.ToULong() == 1) {
                    if (change == 0) {
                        values[x] = new Flt64(0.5);
                    }
                    else if (change == 1) {
                        values[function.LambdaVars[0][0]] = Flt64.Zero;
                    }
                    else if (change == 2) {
                        converter.CorruptNegativeTen = true;
                    }
                }
                return Results.OkInstance;
            }
        );
        Assert.True(registered.IsOk, registered.ToString());
        Assert.Equal(new Flt64(8.5), parent.Tokens.CachedValue(producer, (IReadOnlyList<Flt64>?)null));
        Assert.Equal(change == -1 ? Flt64.One : null, parent.Tokens.CachedValue(consumer, (IReadOnlyList<Flt64>?)null));
    }

    [Fact]
    public async Task Preparation_ShouldNotPublishInexactBarycentricHelpers() {
        var x = new RealVar("surface-third-x");
        var y = new RealVar("surface-third-y");
        BivariateLinearPiecewiseFunction<Flt64> function = Create(
            x: x,
            y: y,
            triangles: new[] { new Triangle<Dim3, Flt64>(
                P1: Point(
                    x: 0,
                    y: 0,
                    z: 2
                ),
                P2: Point(
                    x: 3,
                    y: 0,
                    z: 2
                ),
                P3: Point(
                    x: 0,
                    y: 3,
                    z: 2
                )
            ) }
        ).Value;
        var producer = new LinearFunctionSymbolAdapter<Flt64>(function);
        var consumer = new LinearFunctionSymbolAdapter<Flt64>(IfFunction<Flt64>.Create(
            condition: new LinearPolynomial<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, producer) }, Flt64.Zero),
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: Flt64.One
        ).Value);
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(y).IsOk);
        Assert.True(parent.Add(producer).IsOk);
        Assert.True(parent.Add(consumer).IsOk);
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> built = await LinearMechanismModel<Flt64>.InvokeAsync(
            metaModel: parent,
            fixedVariables: new Dictionary<IVariableItem, Flt64> { [x] = Flt64.One, [y] = Flt64.One }
        );
        Assert.True(built.IsOk, built.ToString());
        using LinearMechanismModel<Flt64> model = built.Value;
        Assert.Equal(new Flt64(2), model.Tokens.CachedValue(producer, (IReadOnlyList<Flt64>?)null));
        Assert.Null(model.Tokens.CachedValue(consumer, (IReadOnlyList<Flt64>?)null));
    }

    [Fact]
    public async Task ChangedHelperRange_ShouldInvalidateRegistrationAndDump() {
        var x = new RealVar("surface-stale-x");
        var y = new RealVar("surface-stale-y");
        BivariateLinearPiecewiseFunction<Flt64> function = Create(x, y).Value;
        using var parent = new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(y).IsOk);
        Assert.True(parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk);
        using LinearMechanismModel<Flt64> model = (await LinearMechanismModel<Flt64>.InvokeAsync(parent)).Value;
        function.LambdaVars[0][0].Range.SetRange(ValueRange<Flt64>.Of(Flt64.Zero, new Flt64(0.5)).Value);
        Assert.True(model.ToLinearTriadModel().IsFailed);
        using var tokens = new ManualTokenList<Flt64>();
        Assert.True(function.RegisterAuxiliaryTokens(tokens).IsFailed);
        Assert.Empty(tokens.Tokens);
    }

    [Fact]
    public void ChangedNestedInput_ShouldRejectRegistration() {
        var x = new RealVar("surface-source-x");
        var y = new RealVar("surface-source-y");
        var nested = new LinearExpressionSymbol(
            name: "surface-source",
            polynomial: new MutableLinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(Flt64.One, x) }, Flt64.Zero)
        );
        BivariateLinearPiecewiseFunction<Flt64> function = BivariateLinearPiecewiseFunction<Flt64>.Create(
            x: Polynomial(nested),
            y: Polynomial(y),
            triangles: new[] { new Triangle<Dim3, Flt64>(
                P1: Point(
                    x: 0,
                    y: 0,
                    z: 2
                ),
                P2: Point(
                    x: 1,
                    y: 0,
                    z: 10
                ),
                P3: Point(
                    x: 0,
                    y: 1,
                    z: 20
                )
            ) },
            converter: IIntoValue<Flt64>.Identity
        ).Value;
        nested.AsMutable().SetConstant(Flt64.One);
        using var tokens = new ManualTokenList<Flt64>();
        Assert.True(function.RegisterAuxiliaryTokens(tokens).IsFailed);
        Assert.Empty(tokens.Tokens);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ChangedNestedInputAfterBuild_ShouldInvalidateDumpAndOldSolution(bool quadratic, bool changeY) {
        var x = new RealVar("surface-built-x");
        var y = new RealVar("surface-built-y");
        var nested = new LinearExpressionSymbol(
            name: "surface-built-input",
            polynomial: new MutableLinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(Flt64.One, changeY ? y : x) }, Flt64.Zero)
        );
        BivariateLinearPiecewiseFunction<Flt64> function = BivariateLinearPiecewiseFunction<Flt64>.Create(
            x: Polynomial(changeY ? x : nested),
            y: Polynomial(changeY ? nested : y),
            triangles: new[] { new Triangle<Dim3, Flt64>(
                P1: Point(
                    x: 0,
                    y: 0,
                    z: 2
                ),
                P2: Point(
                    x: 1,
                    y: 0,
                    z: 10
                ),
                P3: Point(
                    x: 0,
                    y: 1,
                    z: 20
                )
            ) },
            converter: IIntoValue<Flt64>.Identity
        ).Value;
        using AbstractMetaModel<Flt64> parent = quadratic ? new QuadraticMetaModel<Flt64>() : new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(y).IsOk);
        Assert.True(parent.Add(nested).IsOk);
        Assert.True(parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk);
        parent.Tokens.Find(x)!.SetResult(Flt64.One);
        using IMechanismModel<Flt64> model = quadratic
            ? (await QuadraticMechanismModel<Flt64>.InvokeAsync((QuadraticMetaModel<Flt64>)parent)).Value
            : (await LinearMechanismModel<Flt64>.InvokeAsync((LinearMetaModel<Flt64>)parent)).Value;
        using IDisposable matrix = model switch {
            QuadraticMechanismModel<Flt64> q => q.ToQuadraticTetradModel().Value,
            LinearMechanismModel<Flt64> l => l.ToLinearTriadModel().Value,
            _ => throw new InvalidOperationException(),
        };

        nested.AsMutable().SetConstant(Flt64.One);

        Assert.True(model switch {
            QuadraticMechanismModel<Flt64> q => q.ToQuadraticTetradModel().IsFailed,
            LinearMechanismModel<Flt64> l => l.ToLinearTriadModel().IsFailed,
            _ => false,
        });
        Assert.True((model, matrix) switch {
            (QuadraticMechanismModel<Flt64> q, QuadraticTetradModel t) => q.ValidateSolverTokenSnapshot(t).IsFailed,
            (LinearMechanismModel<Flt64> l, LinearTriadModel t) => l.ValidateSolverTokenSnapshot(t).IsFailed,
            _ => false,
        });
        Assert.True(model switch {
            QuadraticMechanismModel<Flt64> q => q.ApplySolverSolution(new Flt64[q.Tokens.TokensInSolver.Count]).IsFailed,
            LinearMechanismModel<Flt64> l => l.ApplySolverSolution(new Flt64[l.Tokens.TokensInSolver.Count]).IsFailed,
            _ => false,
        });
        Assert.Equal(Flt64.One, parent.Tokens.Find(x)!.Result);

        nested.AsMutable().SetConstant(Flt64.Zero);
        Assert.True((model, matrix) switch {
            (QuadraticMechanismModel<Flt64> q, QuadraticTetradModel t) => q.ValidateSolverTokenSnapshot(t).IsOk,
            (LinearMechanismModel<Flt64> l, LinearTriadModel t) => l.ValidateSolverTokenSnapshot(t).IsOk,
            _ => false,
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChangedConverterAfterBuild_ShouldInvalidateDumpAndOldSolution(bool quadratic) {
        var x = new RealVar("surface-converter-x");
        var y = new RealVar("surface-converter-y");
        var converter = new SwitchingConverter();
        BivariateLinearPiecewiseFunction<Flt64> function = BivariateLinearPiecewiseFunction<Flt64>.Create(
            x: Polynomial(x),
            y: Polynomial(y),
            triangles: new[] { new Triangle<Dim3, Flt64>(
                P1: Point(
                    x: 0,
                    y: 0,
                    z: 2
                ),
                P2: Point(
                    x: 1,
                    y: 0,
                    z: 10
                ),
                P3: Point(
                    x: 0,
                    y: 1,
                    z: 20
                )
            ) },
            converter: converter
        ).Value;
        using AbstractMetaModel<Flt64> parent = quadratic ? new QuadraticMetaModel<Flt64>() : new LinearMetaModel<Flt64>();
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(y).IsOk);
        Assert.True(parent.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk);
        using IMechanismModel<Flt64> model = quadratic
            ? (await QuadraticMechanismModel<Flt64>.InvokeAsync((QuadraticMetaModel<Flt64>)parent)).Value
            : (await LinearMechanismModel<Flt64>.InvokeAsync((LinearMetaModel<Flt64>)parent)).Value;
        using IDisposable matrix = model switch {
            QuadraticMechanismModel<Flt64> q => q.ToQuadraticTetradModel().Value,
            LinearMechanismModel<Flt64> l => l.ToLinearTriadModel().Value,
            _ => throw new InvalidOperationException(),
        };

        converter.CorruptNegativeTen = true;

        Assert.True(model switch {
            QuadraticMechanismModel<Flt64> q => q.ToQuadraticTetradModel().IsFailed,
            LinearMechanismModel<Flt64> l => l.ToLinearTriadModel().IsFailed,
            _ => false,
        });
        Assert.True((model, matrix) switch {
            (QuadraticMechanismModel<Flt64> q, QuadraticTetradModel t) => q.ValidateSolverTokenSnapshot(t).IsFailed,
            (LinearMechanismModel<Flt64> l, LinearTriadModel t) => l.ValidateSolverTokenSnapshot(t).IsFailed,
            _ => false,
        });

        converter.CorruptNegativeTen = false;
        Assert.True((model, matrix) switch {
            (QuadraticMechanismModel<Flt64> q, QuadraticTetradModel t) => q.ValidateSolverTokenSnapshot(t).IsOk,
            (LinearMechanismModel<Flt64> l, LinearTriadModel t) => l.ValidateSolverTokenSnapshot(t).IsOk,
            _ => false,
        });
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    public async Task ChangedDependencyBeforeCommit_ShouldRollBackHelpersAndIdentities(bool quadratic, int change) {
        var x = new RealVar("surface-commit-x");
        var y = new RealVar("surface-commit-y");
        var nested = new LinearExpressionSymbol(
            name: "surface-commit-source",
            polynomial: new MutableLinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(Flt64.One, x) }, Flt64.Zero)
        );
        var converter = new SwitchingConverter();
        BivariateLinearPiecewiseFunction<Flt64> function = BivariateLinearPiecewiseFunction<Flt64>.Create(
            x: Polynomial(nested),
            y: Polynomial(y),
            triangles: new[] { new Triangle<Dim3, Flt64>(
                P1: Point(
                    x: 0,
                    y: 0,
                    z: 2
                ),
                P2: Point(
                    x: 1,
                    y: 0,
                    z: 10
                ),
                P3: Point(
                    x: 0,
                    y: 1,
                    z: 20
                )
            ) },
            converter: converter
        ).Value;
        ValueRange<Flt64> originalRange = function.LambdaVars[0][0].Range.Range!;
        var registry = new ModelElementIdentityRegistry();
        using AbstractMetaModel<Flt64> parent = quadratic
            ? new QuadraticMetaModel<Flt64>(identityRegistry: registry)
            : new LinearMetaModel<Flt64>(identityRegistry: registry);
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(y).IsOk);
        Assert.True(parent.Add(nested).IsOk);
        Assert.True(parent.Tokens.Add(new LinearFunctionSymbolAdapter<Flt64>(function)).IsOk);
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
                converter.CorruptNegativeTen = true;
            }
            else {
                function.LambdaVars[0][0].Range.SetRange(ValueRange<Flt64>.Of(Flt64.Zero, new Flt64(0.5)).Value);
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
        converter.CorruptNegativeTen = false;
        function.LambdaVars[0][0].Range.SetRange(originalRange);
        using IMechanismModel<Flt64> retry = quadratic
            ? (await QuadraticMechanismModel<Flt64>.InvokeAsync((QuadraticMetaModel<Flt64>)parent)).Value
            : (await LinearMechanismModel<Flt64>.InvokeAsync((LinearMetaModel<Flt64>)parent)).Value;
        Assert.All(function.HelperVariables, helper => Assert.NotNull(parent.Tokens.Find(helper)));
    }

    [Fact]
    public void DuplicateHelper_ShouldNotPartiallyRegister() {
        var x = new RealVar("surface-duplicate-x");
        var y = new RealVar("surface-duplicate-y");
        BivariateLinearPiecewiseFunction<Flt64> function = Create(x, y).Value;
        using var parent = new LinearMetaModel<Flt64>(configuration: new MetaModelConfiguration(CheckTokenExists: true));
        Assert.True(parent.Add(function.LambdaVars[0][1]).IsOk);
        Token<Flt64> original = parent.Tokens.Find(function.LambdaVars[0][1])!;
        Assert.True(function.RegisterAuxiliaryTokens(parent.Tokens).IsFailed);
        Assert.Same(original, parent.Tokens.Find(function.LambdaVars[0][1]));
        Assert.Null(parent.Tokens.Find(function.LambdaVars[0][0]));
        Assert.Null(parent.Tokens.Find(function.SelectorVars[0]));
    }

    [Fact]
    public async Task RepresentativeNumericTypes_ShouldEvaluateAndBuild() {
        await VerifyNumeric(IIntoValue<Flt64>.Identity);
        await VerifyNumeric(new TestNumberConverter<Flt32>(value => value.ToFlt32()));
        await VerifyNumeric(new TestNumberConverter<Integer>(value => value.ToInt64()));
        await VerifyNumeric(new TestNumberConverter<FltX>(value => value.ToFltX()));
        await VerifyNumeric(new TestNumberConverter<RtnX>(value => new RtnX(value.ToDouble(), 1)));
    }

    private static async Task VerifyNumeric<V>(IIntoValue<V> converter)
        where V : struct, IRealNumber<V>, INumberField<V> {
        var x = new RealVar($"surface-{typeof(V).Name}-x");
        var y = new RealVar($"surface-{typeof(V).Name}-y");
        Result<BivariateLinearPiecewiseFunction<V>, ErrorCode, Error<ErrorCode>> created = BivariateLinearPiecewiseFunction<V>.Create(
            x: new LinearPolynomial<V>(new[] { new LinearMonomial<V>(converter.One, x) }, converter.Zero),
            y: new LinearPolynomial<V>(new[] { new LinearMonomial<V>(converter.One, y) }, converter.Zero),
            triangles: new[] { new Triangle<Dim3, Flt64>(
                P1: Point(
                    x: 0,
                    y: 0,
                    z: 2
                ),
                P2: Point(
                    x: 1,
                    y: 0,
                    z: 10
                ),
                P3: Point(
                    x: 0,
                    y: 1,
                    z: 20
                )
            ) },
            converter: converter
        );
        Assert.True(created.IsOk, created.ToString());
        BivariateLinearPiecewiseFunction<V> function = created.Value;
        Assert.True(function.Evaluate(new Dictionary<ISymbol, V> { [x] = converter.Zero, [y] = converter.Zero })!.Value
            .Eq(converter.IntoValue(new Flt64(2))));
        using var parent = new LinearMetaModel<V>(converter: converter);
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.Add(y).IsOk);
        Assert.True(parent.Add(new LinearFunctionSymbolAdapter<V>(function)).IsOk);
        Result<LinearMechanismModel<V>, ErrorCode, Error<ErrorCode>> built = await LinearMechanismModel<V>.InvokeAsync(parent);
        Assert.True(built.IsOk, built.ToString());
        using LinearMechanismModel<V> model = built.Value;
        Assert.Equal(5, ((IMechanismModel<V>)model).Constraints.Count);
    }

    private sealed class SwitchingConverter : IIntoValue<Flt64> {
        public bool CorruptNegativeTen { get; set; }
        public Flt64 Zero => Flt64.Zero;
        public Flt64 One => Flt64.One;
        public Flt64 IntoValue(Flt64 value) => value;
        public Flt64 FromValue(Flt64 value) => CorruptNegativeTen && value.ToDouble() == -10
            ? Flt64.Zero : value;
    }

    private static Result<BivariateLinearPiecewiseFunction<Flt64>, ErrorCode, Error<ErrorCode>> Create(
        RealVar x,
        RealVar y,
        IReadOnlyList<Triangle<Dim3, Flt64>>? triangles = null) => BivariateLinearPiecewiseFunction<Flt64>.Create(
            x: Polynomial(x),
            y: Polynomial(y),
            triangles: triangles ?? new[] { new Triangle<Dim3, Flt64>(
                P1: Point(
                    x: 0,
                    y: 0,
                    z: 2
                ),
                P2: Point(
                    x: 1,
                    y: 0,
                    z: 10
                ),
                P3: Point(
                    x: 0,
                    y: 1,
                    z: 20
                )
            ) },
            converter: IIntoValue<Flt64>.Identity
        );

    private static LinearPolynomial<Flt64> Polynomial(ISymbol symbol) => new(
        new[] { new LinearMonomial<Flt64>(Flt64.One, symbol) }, Flt64.Zero
    );

    private static Point<Dim3, Flt64> Point(double x, double y, double z) => Point<Dim3, Flt64>.Point3(
        x: new Flt64(x),
        y: new Flt64(y),
        z: new Flt64(z)
    );
}
