#nullable enable
#if GUROBI_INTEGRATION

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
using SymbolFlatten = Fuookami.Ospf.Core.Symbol.Flatten;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
namespace Fuookami.Ospf.Core.Plugin.Gurobi.Tests;

public class SolutionSnapshotGurobiIntegrationTest {
    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 1)]
    [InlineData(0, 2)]
    [InlineData(1, 0)]
    [InlineData(1, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 0)]
    [InlineData(2, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 0)]
    [InlineData(3, 1)]
    [InlineData(3, 2)]
    [InlineData(4, 0)]
    [InlineData(4, 1)]
    [InlineData(4, 2)]
    public async Task FrameworkSolvers_ShouldApplyOnlyToUnchangedTokens(int operation, int replacement) {
        using var parent = new LinearMetaModel<Flt64>();
        var x = new BinVar("snapshot-x");
        var y = new BinVar("snapshot-y");
        Assert.True(parent.Add(new IVariableItem[] { x, y }).IsOk);
        parent.Tokens.SetSolverSolution(new[] { Flt64.Zero, Flt64.Zero });
        Assert.True(parent.AddObject(
            category: ObjectCategory.Minimum,
            flattenData: new SymbolFlatten.LinearFlattenData<Flt64>(new[] {
                new LinearMonomial<Flt64>(new Flt64(-1), x),
                new LinearMonomial<Flt64>(new Flt64(-2), y),
            }, Flt64.Zero),
            name: "objective",
            displayName: null
        ).IsOk);
        BinVar replaced = replacement == 2 ? y : x;
        BinVar unchanged = replacement == 2 ? x : y;
        Token<Flt64> old = parent.Tokens.Find(replaced)!;
        bool called = false;
        GurobiLinearSolverCallBack callback = new GurobiLinearSolverCallBack().AfterModeling((_, _, _, _) => {
            called = true;
            if (replacement != 0) {
                Assert.True(parent.Add(replaced).IsOk);
            }
            return Task.FromResult(Results.OkInstance);
        });
        var columnSolver = new GurobiColumnGenerationSolver(callBack: callback);
        var bendersSolver = new GurobiBendersDecompositionSolver(callBack: callback);

        ErrorCode? error = operation switch {
            0 => Error(await columnSolver.SolveMILPAsync("solution-test", parent)),
            1 => Error(await columnSolver.SolveMILPAsync(
                name: "solution-test",
                metaModel: parent,
                amount: 2
            )),
            2 => Error(await columnSolver.SolveLPAsync("solution-test", parent)),
            3 => Error(await bendersSolver.SolveMasterAsync("solution-test", parent)),
            _ => Error(await bendersSolver.SolveSubAsync(
                name: "solution-test",
                metaModel: parent,
                objectVariable: x,
                fixedVariables: new Dictionary<IVariableItem, Flt64>()
            )),
        };

        Assert.True(called);
        if (replacement == 0) {
            Assert.Null(error);
            Assert.Equal(Flt64.One, parent.Tokens.Find(x)!.Result);
            Assert.Equal(Flt64.One, parent.Tokens.Find(y)!.Result);
            Assert.Equal(Flt64.One, old.Result);
        }
        else {
            Assert.Equal(ErrorCode.IllegalArgument, error);
            Assert.Null(parent.Tokens.Find(replaced)!.Result);
            Assert.Equal(Flt64.Zero, parent.Tokens.Find(unchanged)!.Result);
            Assert.Equal(Flt64.Zero, old.Result);
        }
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 1)]
    [InlineData(0, 2)]
    [InlineData(1, 0)]
    [InlineData(1, 1)]
    [InlineData(1, 2)]
    [InlineData(2, 0)]
    [InlineData(2, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 0)]
    [InlineData(3, 1)]
    [InlineData(3, 2)]
    [InlineData(4, 0)]
    [InlineData(4, 1)]
    [InlineData(4, 2)]
    public async Task FrameworkSolvers_ShouldRejectParentStructureChanges(int operation, int change) {
        using var parent = new LinearMetaModel<Flt64>();
        var x = new BinVar("structure-solve-x");
        Assert.True(parent.Add(x).IsOk);
        parent.Tokens.Find(x)!.SetResult(Flt64.Zero);
        Assert.True(parent.AddObject(
            category: ObjectCategory.Minimum,
            flattenData: new SymbolFlatten.LinearFlattenData<Flt64>(
                new[] { new LinearMonomial<Flt64>(-Flt64.One, x) }, Flt64.Zero),
            name: "objective",
            displayName: null
        ).IsOk);
        bool called = false;
        GurobiLinearSolverCallBack callback = new GurobiLinearSolverCallBack().AfterModeling((_, _, _, _) => {
            called = true;
            if (change == 0) {
                Assert.True(parent.AddConstraint(x, group: null).IsOk);
            }
            else if (change == 1) {
                Assert.True(parent.AddObject(
                    category: ObjectCategory.Minimum,
                    flattenData: new SymbolFlatten.LinearFlattenData<Flt64>(
                        new[] { new LinearMonomial<Flt64>(Flt64.One, x) }, Flt64.Zero),
                    name: "added",
                    displayName: null
                ).IsOk);
            }
            else {
                Assert.True(x.Range.Ls(false));
            }
            return Task.FromResult(Results.OkInstance);
        });
        var columnSolver = new GurobiColumnGenerationSolver(callBack: callback);
        var bendersSolver = new GurobiBendersDecompositionSolver(callBack: callback);

        ErrorCode? error = operation switch {
            0 => Error(await columnSolver.SolveMILPAsync("structure-solve", parent)),
            1 => Error(await columnSolver.SolveMILPAsync(
                name: "structure-solve",
                metaModel: parent,
                amount: 2
            )),
            2 => Error(await columnSolver.SolveLPAsync("structure-solve", parent)),
            3 => Error(await bendersSolver.SolveMasterAsync("structure-solve", parent)),
            _ => Error(await bendersSolver.SolveSubAsync(
                name: "structure-solve",
                metaModel: parent,
                objectVariable: x,
                fixedVariables: new Dictionary<IVariableItem, Flt64>()
            )),
        };

        Assert.True(called);
        Assert.Equal(ErrorCode.IllegalArgument, error);
        Assert.Equal(Flt64.Zero, parent.Tokens.Find(x)!.Result);
    }

    [Fact]
    public async Task SolutionPoolBaseline_ShouldReturnTwoBinarySolutions() {
        using var parent = new LinearMetaModel<Flt64>();
        var x = new BinVar("structure-pool-control");
        Assert.True(parent.Add(x).IsOk);
        Assert.True(parent.AddObject(
            category: ObjectCategory.Minimum,
            flattenData: new SymbolFlatten.LinearFlattenData<Flt64>(
                new[] { new LinearMonomial<Flt64>(-Flt64.One, x) }, Flt64.Zero),
            name: "objective",
            displayName: null
        ).IsOk);

        Result<(FeasibleSolverOutput<Flt64> Output, List<List<Flt64>> SolutionPool), ErrorCode, Error<ErrorCode>> result =
            await new GurobiColumnGenerationSolver().SolveMILPAsync(
                name: "structure-pool-control",
                metaModel: parent,
                amount: 2
            );

        Assert.True(result.IsOk, result.ToString());
        Assert.Equal(2, result.Value.SolutionPool.Count);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    [InlineData(3, true)]
    [InlineData(4, false)]
    [InlineData(4, true)]
    public async Task FrameworkSolvers_ShouldRejectMechanismRowsAddedAfterDump(int operation, bool mutate) {
        using var parent = new LinearMetaModel<Flt64>();
        var x = new BinVar("mechanism-change-x");
        Assert.True(parent.Add(x).IsOk);
        parent.Tokens.Find(x)!.SetResult(Flt64.Zero);
        Assert.True(parent.AddObject(
            category: ObjectCategory.Minimum,
            flattenData: new SymbolFlatten.LinearFlattenData<Flt64>(
                new[] { new LinearMonomial<Flt64>(-Flt64.One, x) }, Flt64.Zero),
            name: "objective",
            displayName: null
        ).IsOk);
        var probe = new MechanismProbe();
        Assert.True(parent.Add(new LinearFunctionSymbolAdapter<Flt64>(probe)).IsOk);
        bool called = false;
        GurobiLinearSolverCallBack callback = new GurobiLinearSolverCallBack().AfterModeling((_, _, _, _) => {
            called = true;
            if (mutate) {
                Assert.NotNull(probe.Model);
                Assert.True(probe.Model!.AddConstraint(
                    relation: new LinearInequality<Flt64>(
                        Lhs: new LinearPolynomial<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, x) }, Flt64.Zero),
                        Rhs: new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), Flt64.Zero),
                        Comparison: Comparison.LE
                    ),
                    name: "late-row",
                    from: (Fuookami.Ospf.Core.Symbol.IIntermediateSymbol?)null
                ).IsOk);
            }
            return Task.FromResult(Results.OkInstance);
        });
        var columnSolver = new GurobiColumnGenerationSolver(callBack: callback);
        var bendersSolver = new GurobiBendersDecompositionSolver(callBack: callback);
        ErrorCode? error = operation switch {
            0 => Error(await columnSolver.SolveMILPAsync("mechanism-change", parent)),
            1 => Error(await columnSolver.SolveMILPAsync(
                name: "mechanism-change",
                metaModel: parent,
                amount: 2
            )),
            2 => Error(await columnSolver.SolveLPAsync("mechanism-change", parent)),
            3 => Error(await bendersSolver.SolveMasterAsync("mechanism-change", parent)),
            _ => Error(await bendersSolver.SolveSubAsync(
                name: "mechanism-change",
                metaModel: parent,
                objectVariable: x,
                fixedVariables: new Dictionary<IVariableItem, Flt64>()
            )),
        };

        Assert.True(called);
        Assert.Equal(mutate ? ErrorCode.IllegalArgument : null, error);
        Assert.Equal(mutate ? Flt64.Zero : Flt64.One, parent.Tokens.Find(x)!.Result);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BendersFarkas_ShouldRejectMechanismRowsAddedAfterDump(bool mutate) {
        using var parent = new LinearMetaModel<Flt64>();
        var x = new BinVar("mechanism-farkas-x");
        Assert.True(parent.Add(x).IsOk);
        parent.Tokens.Find(x)!.SetResult(Flt64.Zero);
        var zero = new LinearInequality<Flt64>(
            Lhs: new LinearPolynomial<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, x) }, Flt64.Zero),
            Rhs: new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), Flt64.Zero),
            Comparison: Comparison.LE
        );
        var one = new LinearInequality<Flt64>(
            Lhs: new LinearPolynomial<Flt64>(new[] { new LinearMonomial<Flt64>(Flt64.One, x) }, Flt64.Zero),
            Rhs: new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), Flt64.One),
            Comparison: Comparison.GE
        );
        Assert.True(parent.AddConstraint(relation: zero, group: null).IsOk);
        Assert.True(parent.AddConstraint(relation: one, group: null).IsOk);
        var probe = new MechanismProbe();
        Assert.True(parent.Add(new LinearFunctionSymbolAdapter<Flt64>(probe)).IsOk);
        bool called = false;
        GurobiLinearSolverCallBack callback = new GurobiLinearSolverCallBack().AfterFailure((_, _, _, _) => {
            called = true;
            if (mutate) {
                Assert.NotNull(probe.Model);
                Assert.True(probe.Model!.AddConstraint(
                    relation: zero,
                    name: "late-farkas-row",
                    from: (Fuookami.Ospf.Core.Symbol.IIntermediateSymbol?)null
                ).IsOk);
            }
            return Task.FromResult(Results.OkInstance);
        });

        Result<Framework.Solver.IBendersDecompositionSolver.LinearSubResult, ErrorCode, Error<ErrorCode>> result =
            await new GurobiBendersDecompositionSolver(callBack: callback).SolveSubAsync(
                name: "mechanism-farkas",
                metaModel: parent,
                objectVariable: x,
                fixedVariables: new Dictionary<IVariableItem, Flt64>()
            );
        Assert.True(called);
        if (mutate) {
            Assert.Equal(ErrorCode.IllegalArgument, Error(result));
        }
        else {
            Assert.True(result.IsOk, result.ToString());
            Assert.IsType<Framework.Solver.IBendersDecompositionSolver.LinearInfeasibleResult>(result.Value);
        }
        Assert.Equal(Flt64.Zero, parent.Tokens.Find(x)!.Result);
    }

    private sealed class MechanismProbe : IMathFunctionSymbol<Flt64> {
        public string Name { get; set; } = "mechanism-probe";
        public string? DisplayName { get; set; }
        public IAbstractLinearMechanismModel<Flt64>? Model { get; private set; }
        public IReadOnlyList<IVariableItem> HelperVariables => Array.Empty<IVariableItem>();
        public IReadOnlyCollection<ISymbol> InputSymbols => Array.Empty<ISymbol>();
        public Flt64? Evaluate(IReadOnlyDictionary<ISymbol, Flt64> values) => Flt64.Zero;
        public Try RegisterAuxiliaryTokens(IAddableTokenCollection<Flt64> tokens) => Results.OkInstance;
        public Try RegisterConstraints(IAbstractLinearMechanismModel<Flt64> model) {
            Model = model;
            return Results.OkInstance;
        }
    }

    private static ErrorCode? Error<T>(Result<T, ErrorCode, Error<ErrorCode>> result) => result.IsOk
        ? null
        : Assert.IsType<Failed<T, ErrorCode, Error<ErrorCode>>>(result).Code;
}
#endif
