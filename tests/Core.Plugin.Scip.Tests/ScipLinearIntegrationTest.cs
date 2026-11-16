#nullable enable
#if SCIP_INTEGRATION

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver;
using Fuookami.Ospf.Core.Solver.Config;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Symbol.Flatten;
using Fuookami.Ospf.Core.Variable;
namespace Fuookami.Ospf.Core.Plugin.Scip.Tests;

public class ScipLinearIntegrationTest {
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SolvesContinuousAndIntegerModelsWithObjectiveOffset(bool integer) {
        using LinearTriadModel model = MakeModel(
            variables: [
                new ModelViewVariable(
                    index: 0,
                    lowerBound: Flt64.Zero,
                    upperBound: new Flt64(10),
                    type: integer ? Integer.Instance : Continuous.Instance,
                    name: "x"),
                new ModelViewVariable(
                    index: 1,
                    lowerBound: Flt64.Zero,
                    upperBound: new Flt64(10),
                    type: integer ? Integer.Instance : Continuous.Instance,
                    name: "y"),
            ],
            rows: [
                new SparseVector([new SparseVectorEntry(0, Flt64.One), new SparseVectorEntry(1, Flt64.One)]),
                new SparseVector([new SparseVectorEntry(0, Flt64.One)]),
            ],
            relations: [ConstraintRelation.GreaterEqual, ConstraintRelation.LessEqual],
            right: [new Flt64(4), new Flt64(3)],
            objective: [new LinearObjectiveCell(0, new Flt64(2)), new LinearObjectiveCell(1, new Flt64(3))],
            category: ObjectCategory.Minimum,
            constant: 5);
        IAbstractLinearSolver solver = new ScipLinearSolver();
        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> result = await solver.InvokeReportAsync(model);
        Assert.True(result.IsOk, result.ToString());
        SolveReport<Flt64> report = result.Value;
        Assert.Equal(ProblemStatus.Feasible, report.ProblemStatus);
        Assert.Equal(TerminationReason.Completed, report.TerminationReason);
        Assert.Equal(SolutionPresence.Optimal, report.SolutionPresence);
        Assert.Equal(ProofStatus.Claimed, report.Proof.Status);
        Assert.Equal(
            expected: 14,
            actual: report.Solution!.Objective!.Value.ToDouble(),
            precision: 6);
        Assert.Equal(
            expected: 3,
            actual: report.Values[0].ToDouble(),
            precision: 6);
        Assert.Equal(
            expected: 1,
            actual: report.Values[1].ToDouble(),
            precision: 6);
        Assert.Equal(
            expected: 14,
            actual: report.Statistics.BestBound!.Value.ToDouble(),
            precision: 6);
        Assert.Equal("scip", report.Provenance!.Descriptor.SolverId);
        Assert.StartsWith("9.", report.Provenance.NativeVersion);
        // LP 对偶与 Farkas 乘子已由原生后端给出，能力声明必须如实为 true。 / LP duals and Farkas multipliers come from the native backend, so the capability claim is now true.
        Assert.True(report.Provenance.Descriptor.Capabilities.Dual);
        Assert.True(report.Provenance.Descriptor.Capabilities.Farkas);
        Assert.False(report.Provenance.Descriptor.Capabilities.NativeIis);
        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> legacy = await solver.InvokeAsync((ILinearTriadModelView)model);
        Assert.True(legacy.IsOk, legacy.ToString());
        Assert.Equal(
            expected: 14,
            actual: legacy.Value.Obj.ToDouble(),
            precision: 6);
    }

    [Fact]
    public async Task MaximizesBinaryModelAndAddsRepeatedCoefficients() {
        using LinearTriadModel model = MakeModel(
            variables: [
                new ModelViewVariable(
                    index: 0,
                    lowerBound: Flt64.Zero,
                    upperBound: Flt64.One,
                    type: Binary.Instance,
                    name: "x"),
                new ModelViewVariable(
                    index: 1,
                    lowerBound: Flt64.Zero,
                    upperBound: Flt64.One,
                    type: Binary.Instance,
                    name: "y"),
            ],
            rows: [new SparseVector([new SparseVectorEntry(0, Flt64.One), new SparseVectorEntry(1, Flt64.One)])],
            relations: [ConstraintRelation.LessEqual],
            right: [Flt64.One],
            objective: [new LinearObjectiveCell(0, Flt64.One), new LinearObjectiveCell(1, Flt64.One), new LinearObjectiveCell(1, Flt64.One)],
            category: ObjectCategory.Maximum,
            constant: 7);
        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> result = await new ScipLinearSolver().InvokeReportAsync(model);
        Assert.True(result.IsOk, result.ToString());
        Assert.Equal(
            expected: 9,
            actual: result.Value.Solution!.Objective!.Value.ToDouble(),
            precision: 6);
        Assert.Equal(
            expected: 0,
            actual: result.Value.Values[0].ToDouble(),
            precision: 6);
        Assert.Equal(
            expected: 1,
            actual: result.Value.Values[1].ToDouble(),
            precision: 6);
        Assert.Equal(
            expected: 9,
            actual: result.Value.Statistics.BestBound!.Value.ToDouble(),
            precision: 6);
    }

    [Fact]
    public async Task ReportsInfeasibleAndLegacyError() {
        using LinearTriadModel model = MakeModel(
            variables: [new ModelViewVariable(
                index: 0,
                lowerBound: Flt64.Zero,
                upperBound: Flt64.One,
                type: Continuous.Instance,
                name: "x")],
            rows: [new SparseVector([new SparseVectorEntry(0, Flt64.One)])],
            relations: [ConstraintRelation.GreaterEqual],
            right: [new Flt64(2)],
            objective: [],
            category: ObjectCategory.Minimum,
            constant: 0);
        IAbstractLinearSolver solver = new ScipLinearSolver();
        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> result = await solver.InvokeReportAsync(model);
        Assert.True(result.IsOk, result.ToString());
        Assert.Equal(ProblemStatus.Infeasible, result.Value.ProblemStatus);
        Assert.Equal(ProofStatus.Claimed, result.Value.Proof.Status);
        Assert.Null(result.Value.Solution);
        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> legacy = await solver.InvokeAsync((ILinearTriadModelView)model);
        Assert.Equal(ErrorCode.ORModelInfeasible, Assert.IsType<Failed<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>>(legacy).Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(8)]
    public async Task PoolReturnsAdditionalSolutionsAndKeepsIndependentCopies(ulong amount) {
        using LinearTriadModel model = MakeModel(
            variables: [
                new ModelViewVariable(
                    index: 0,
                    lowerBound: Flt64.Zero,
                    upperBound: Flt64.One,
                    type: Binary.Instance,
                    name: "x",
                    initialResult: Flt64.Zero),
                new ModelViewVariable(
                    index: 1,
                    lowerBound: Flt64.Zero,
                    upperBound: Flt64.One,
                    type: Binary.Instance,
                    name: "y",
                    initialResult: Flt64.Zero),
            ],
            rows: [],
            relations: [],
            right: [],
            objective: [new LinearObjectiveCell(0, Flt64.One), new LinearObjectiveCell(1, new Flt64(2))],
            category: ObjectCategory.Maximum,
            constant: 0);
        var solver = new ScipLinearSolver();
        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> result = await solver.InvokeReportAsync(model, amount);
        Assert.True(result.IsOk, result.ToString());
        Assert.True(result.Value.Provenance!.Descriptor.Capabilities.SolutionPool);
        Assert.Equal(SolutionPresence.Optimal, result.Value.SolutionPresence);
        SolveSolution<Flt64> solution = Assert.IsType<SolveSolution<Flt64>>(result.Value.Solution);
        Assert.Equal(new[] { Flt64.One, Flt64.One }, solution.Values.Values);
        if (amount <= 1) {
            Assert.Empty(solution.Pool);
            // 运行参数由统一 SolverConfig 始终记录，只是不附加解池参数。
            // The unified SolverConfig always records runtime parameters; only the pool parameter stays absent.
            Assert.DoesNotContain("heuristics/dins/solnum", result.Value.Provenance.EffectiveParameters.Keys);
            Assert.Equal("30", result.Value.Provenance.EffectiveParameters["time"]);
        }
        else {
            Assert.Equal(amount.ToString(), result.Value.Provenance.EffectiveParameters["heuristics/dins/solnum"]);
            Assert.NotEmpty(solution.Pool);
            Assert.True((ulong)solution.Pool.Count < amount);
            Assert.DoesNotContain(solution.Pool, item => item.Values.SequenceEqual(solution.Values.Values));
            Assert.Equal(solution.Pool.Count, solution.Pool.Select(item => string.Join(",", item.Values)).Distinct().Count());
        }
        Result<(FeasibleSolverOutput<Flt64> Output, List<List<Flt64>> Solutions), ErrorCode, Error<ErrorCode>> legacy =
            await solver.InvokeAsync(model, amount);
        Assert.True(legacy.IsOk, legacy.ToString());
        Assert.Equal(solution.Pool.Count, legacy.Value.Solutions.Count);
        IAbstractLinearSolver generic = solver;
        Result<(FeasibleSolverOutput<Flt64> Output, List<Solution<Flt64>> Solutions), ErrorCode, Error<ErrorCode>> converted =
            await generic.SolveAsync(
                model: model,
                solutionAmount: amount,
                converter: IIntoValue<Flt64>.Identity);
        Assert.True(converted.IsOk, converted.ToString());
        Assert.Equal(solution.Pool.Count, converted.Value.Solutions.Count);
        model.Dispose();
        Assert.Equal(new[] { Flt64.One, Flt64.One }, solution.Values.Values);
        foreach (Solution<Flt64> item in solution.Pool) {
            Assert.Equal(2, item.Values.Count);
            Assert.All(item.Values, value => Assert.Contains(value, new[] { Flt64.Zero, Flt64.One }));
            Assert.Throws<NotSupportedException>(() => ((IList<Flt64>)item.Values)[0] = Flt64.Zero);
        }
    }

    [Fact]
    public async Task PoolPreservesInfeasibleReportAndLegacyError() {
        using LinearTriadModel model = MakeModel(
            variables: [new ModelViewVariable(
                index: 0,
                lowerBound: Flt64.Zero,
                upperBound: Flt64.One,
                type: Binary.Instance,
                name: "x")],
            rows: [new SparseVector([new SparseVectorEntry(0, Flt64.One)])],
            relations: [ConstraintRelation.GreaterEqual],
            right: [new Flt64(2)],
            objective: [],
            category: ObjectCategory.Minimum,
            constant: 0);
        var solver = new ScipLinearSolver();
        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> report = await solver.InvokeReportAsync(model, 3);
        Assert.True(report.IsOk, report.ToString());
        Assert.Equal(ProblemStatus.Infeasible, report.Value.ProblemStatus);
        Assert.Null(report.Value.Solution);
        Result<(FeasibleSolverOutput<Flt64> Output, List<List<Flt64>> Solutions), ErrorCode, Error<ErrorCode>> legacy = await solver.InvokeAsync(model, 3);
        Assert.Equal(ErrorCode.ORModelInfeasible, Assert.IsType<Failed<(FeasibleSolverOutput<Flt64>, List<List<Flt64>>), ErrorCode, Error<ErrorCode>>>(legacy).Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(5)]
    public async Task FrameworkPoolPrependsAndAppliesTheMainSolution(ulong amount) {
        using var meta = new LinearMetaModel<Flt64>();
        var variable = new BinVar("x");
        Assert.True(meta.Add(variable).IsOk);
        Assert.True(meta.AddObject(
            category: ObjectCategory.Minimum,
            flattenData: new LinearFlattenData<Flt64>([new LinearMonomial<Flt64>(-Flt64.One, variable)], Flt64.Zero),
            name: "objective",
            displayName: null).IsOk);
        Result<(FeasibleSolverOutput<Flt64> Output, List<List<Flt64>> SolutionPool), ErrorCode, Error<ErrorCode>> result =
            await new ScipColumnGenerationSolver().SolveMILPAsync(
                name: "milp",
                metaModel: meta,
                amount: amount);
        Assert.True(result.IsOk, result.ToString());
        Assert.NotEmpty(result.Value.SolutionPool);
        Assert.Equal(result.Value.Output.Solution.Values, result.Value.SolutionPool[0]);
        Assert.Equal(Flt64.One, meta.Tokens.Find(variable)!.Result);
        result.Value.SolutionPool[0][0] = Flt64.Zero;
        Assert.Equal(Flt64.One, result.Value.Output.Solution.Values[0]);
        if (amount <= 1) {
            Assert.Single(result.Value.SolutionPool);
        }
        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> options = await new ScipColumnGenerationSolver().SolveMILPAsync(
            metaModel: meta,
            options: new Framework.Solver.FrameworkSolveOptions { SolutionAmount = amount });
        Assert.True(options.IsOk, options.ToString());
        Assert.Equal(Flt64.One, Assert.Single(options.Value.Solution.Values));
    }

    [Fact]
    public async Task FrameworkPoolRestoresAdditionalSolutionsInTokenOrder() {
        using var meta = new LinearMetaModel<Flt64>();
        var x = new BinVar("x");
        var y = new BinVar("y");
        Assert.True(meta.Add(x).IsOk);
        Assert.True(meta.Add(y).IsOk);
        Assert.True(meta.AddObject(
            category: ObjectCategory.Minimum,
            flattenData: new LinearFlattenData<Flt64>([
                new LinearMonomial<Flt64>(-Flt64.One, x),
                new LinearMonomial<Flt64>(new Flt64(-2), y),
            ], Flt64.Zero),
            name: "objective",
            displayName: null).IsOk);
        meta.Tokens.Find(x)!.SetResult(Flt64.Zero);
        meta.Tokens.Find(y)!.SetResult(Flt64.Zero);
        Result<(FeasibleSolverOutput<Flt64> Output, List<List<Flt64>> SolutionPool), ErrorCode, Error<ErrorCode>> result =
            await new ScipColumnGenerationSolver().SolveMILPAsync(
                name: "milp",
                metaModel: meta,
                amount: 8);
        Assert.True(result.IsOk, result.ToString());
        Assert.True(result.Value.SolutionPool.Count > 1);
        Assert.Equal(new[] { Flt64.One, Flt64.One }, result.Value.SolutionPool[0]);
        Assert.Contains(result.Value.SolutionPool.Skip(1), values => values.SequenceEqual(new[] { Flt64.Zero, Flt64.Zero }));
        Assert.Equal(Flt64.One, meta.Tokens.Find(x)!.Result);
        Assert.Equal(Flt64.One, meta.Tokens.Find(y)!.Result);
    }

    [Fact]
    public void OriginalProblemCandidatesAreNotReportedAsSolutions() {
        Result<ScipNative, ErrorCode, Error<ErrorCode>> loaded = ScipNative.Load();
        Assert.True(loaded.IsOk, loaded.ToString());
        using ScipNative native = loaded.Value;
        IntPtr scip = IntPtr.Zero;
        IntPtr variable = IntPtr.Zero;
        IntPtr initial = IntPtr.Zero;
        try {
            Assert.Equal(1, native.CreateScip(ref scip));
            native.SetQuiet(scip, 1);
            Assert.Equal(1, native.IncludePlugins(scip));
            Assert.Equal(1, native.CreateProblem(scip, "candidate"));
            Assert.Equal(1, native.CreateVariable(
                scip: scip,
                variable: ref variable,
                name: "x",
                lower: 0,
                upper: 1,
                objective: 0,
                type: 0));
            Assert.Equal(1, native.AddVariable(scip, variable));
            Result<Success, ErrorCode, Error<ErrorCode>> submitted = ScipLinearNativeSolver.SubmitInitialSolution(
                native: native,
                scip: scip,
                columns: [new ScipColumn(Lower: 0, Upper: 1, Type: 0, Initial: 2)],
                variables: [variable],
                solution: ref initial,
                cancellationToken: default,
                stored: out bool stored);
            Assert.True(submitted.IsOk, submitted.ToString());
            Assert.True(stored);
            Assert.Equal(1, native.GetStage(scip));
            Assert.NotEqual(IntPtr.Zero, native.GetBestSolution(scip));
            Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> report = ScipLinearNativeSolver.ReadReport(
                native: native,
                scip: scip,
                variables: [variable],
                infinity: native.Infinity(scip),
                interruptedByCancellation: false,
                config: new SolverConfig(),
                solutionAmount: 3);
            Assert.True(report.IsOk, report.ToString());
            Assert.Equal(ProblemStatus.Unknown, report.Value.ProblemStatus);
            Assert.Equal(SolutionPresence.None, report.Value.SolutionPresence);
            Assert.Null(report.Value.Solution);
            Assert.Null(report.Value.Statistics.BestBound);
            Assert.Null(report.Value.Statistics.Gap);
        }
        finally {
            if (scip != IntPtr.Zero) {
                if (initial != IntPtr.Zero) {
                    Assert.Equal(1, native.FreeSolution(scip, ref initial));
                }
                if (variable != IntPtr.Zero) {
                    Assert.Equal(1, native.ReleaseVariable(scip, ref variable));
                }
                Assert.Equal(1, native.FreeScip(ref scip));
            }
        }
    }

    [Fact]
    public async Task ReportsUnboundedModelWithoutPretendingOptimal() {
        using LinearTriadModel model = MakeModel(
            variables: [new ModelViewVariable(
                index: 0,
                lowerBound: Flt64.Zero,
                upperBound: new Flt64(double.PositiveInfinity),
                type: Continuous.Instance,
                name: "x")],
            rows: [],
            relations: [],
            right: [],
            objective: [new LinearObjectiveCell(0, Flt64.One)],
            category: ObjectCategory.Maximum,
            constant: 0);
        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> result = await new ScipLinearSolver().InvokeReportAsync(model);
        Assert.True(result.IsOk, result.ToString());
        Assert.Equal(ProblemStatus.Unbounded, result.Value.ProblemStatus);
        Assert.NotEqual(SolutionPresence.Optimal, result.Value.SolutionPresence);
        Assert.Equal(ProofStatus.Claimed, result.Value.Proof.Status);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SolvesZeroColumnModels(bool feasible) {
        using LinearTriadModel model = MakeModel(
            variables: [],
            rows: [new SparseVector()],
            relations: [ConstraintRelation.Equal],
            right: [new Flt64(feasible ? 0 : 1)],
            objective: [],
            category: ObjectCategory.Minimum,
            constant: 3);
        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> result = await new ScipLinearSolver().InvokeReportAsync(model);
        Assert.True(result.IsOk, result.ToString());
        Assert.Equal(feasible ? ProblemStatus.Feasible : ProblemStatus.Infeasible, result.Value.ProblemStatus);
        if (feasible) {
            Assert.Empty(result.Value.Values.Values);
            Assert.Equal(
                expected: 3,
                actual: result.Value.Solution!.Objective!.Value.ToDouble(),
                precision: 6);
        }
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(3, 1)]
    public async Task RespectsBuiltInVariableDomains(int type, double expected) {
        IVariableTypeKind kind = type switch {
            0 => Ternary.Instance,
            1 => BalancedTernary.Instance,
            2 => Percentage.Instance,
            _ => Binary.Instance,
        };
        using LinearTriadModel model = MakeModel(
            variables: [new ModelViewVariable(
                index: 0,
                lowerBound: new Flt64(-10),
                upperBound: new Flt64(10),
                type: kind,
                name: "x")],
            rows: [],
            relations: [],
            right: [],
            objective: [new LinearObjectiveCell(0, Flt64.One)],
            category: ObjectCategory.Maximum,
            constant: 0);
        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> result = await new ScipLinearSolver().InvokeReportAsync(model);
        Assert.True(result.IsOk, result.ToString());
        Assert.Equal(
            expected: expected,
            actual: result.Value.Values[0].ToDouble(),
            precision: 6);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FrameworkMasterAppliesNativeSolution(bool benders) {
        using var meta = new LinearMetaModel<Flt64>();
        var variable = new BinVar("x");
        Assert.True(meta.Add(variable).IsOk);
        Assert.True(meta.AddObject(
            category: ObjectCategory.Minimum,
            flattenData: new LinearFlattenData<Flt64>([new LinearMonomial<Flt64>(-Flt64.One, variable)], Flt64.Zero),
            name: "objective",
            displayName: null).IsOk);
        if (benders) {
            var solver = new ScipBendersDecompositionSolver();
            Result<SolverOutput, ErrorCode, Error<ErrorCode>> result = await solver.SolveMasterAsync("master", meta);
            Assert.True(result.IsOk, result.ToString());
            Assert.Equal(
                expected: -1,
                actual: Assert.IsType<FeasibleSolverOutput<Flt64>>(result.Value).Obj.ToDouble(),
                precision: 6);
        }
        else {
            var solver = new ScipColumnGenerationSolver();
            Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> result = await solver.SolveMILPAsync("milp", meta);
            Assert.True(result.IsOk, result.ToString());
            Assert.Equal(
                expected: -1,
                actual: result.Value.Obj.ToDouble(),
                precision: 6);
        }
        Assert.Equal(Flt64.One, meta.Tokens.Find(variable)!.Result);
        var matrixSolver = new ScipLinearSolver();
        Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> mechanismResult = await matrixSolver.DumpAsync(
            model: meta,
            rcb: null,
            dcb: null);
        Assert.True(mechanismResult.IsOk, mechanismResult.ToString());
        using (LinearMechanismModel<Flt64> mechanism = mechanismResult.Value) {
            Result<LinearTriadModel, ErrorCode, Error<ErrorCode>> matrixResult = await matrixSolver.DumpAsync(mechanism);
            Assert.True(matrixResult.IsOk, matrixResult.ToString());
            using LinearTriadModel matrix = matrixResult.Value;
            Assert.Equal(Flt64.One, Assert.Single(matrix.Variables).InitialResult);
        }
        if (benders) {
            Result<SolverOutput, ErrorCode, Error<ErrorCode>> repeated = await new ScipBendersDecompositionSolver().SolveMasterAsync("master", meta);
            Assert.True(repeated.IsOk, repeated.ToString());
        }
        else {
            Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> repeated = await new ScipColumnGenerationSolver().SolveMILPAsync("milp", meta);
            Assert.True(repeated.IsOk, repeated.ToString());
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InitialValuesAreSubmittedToNativeCandidateStorage(bool partial) {
        Result<ScipNative, ErrorCode, Error<ErrorCode>> loaded = ScipNative.Load();
        Assert.True(loaded.IsOk, loaded.ToString());
        using ScipNative native = loaded.Value;
        IntPtr scip = IntPtr.Zero;
        IntPtr initial = IntPtr.Zero;
        IntPtr[] variables = new IntPtr[2];
        try {
            Assert.Equal(1, native.CreateScip(ref scip));
            native.SetQuiet(scip, 1);
            Assert.Equal(1, native.IncludePlugins(scip));
            Assert.Equal(1, native.CreateProblem(scip, "initial"));
            for (int i = 0; i < variables.Length; i++) {
                Assert.Equal(1, native.CreateVariable(
                    scip: scip,
                    variable: ref variables[i],
                    name: $"x{i}",
                    lower: 0,
                    upper: 1,
                    objective: 0,
                    type: 0));
                Assert.Equal(1, native.AddVariable(scip, variables[i]));
            }
            ScipColumn[] columns = [
                new ScipColumn(
                    Lower: 0,
                    Upper: 1,
                    Type: 0,
                    Initial: 1),
                new ScipColumn(
                    Lower: 0,
                    Upper: 1,
                    Type: 0,
                    Initial: partial ? null : 0),
            ];
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            Result<Success, ErrorCode, Error<ErrorCode>> cancelled = ScipLinearNativeSolver.SubmitInitialSolution(
                native: native,
                scip: scip,
                columns: columns,
                variables: variables,
                solution: ref initial,
                cancellationToken: cancellation.Token,
                stored: out bool cancelledStored);
            Assert.Equal(ErrorCode.ApplicationStopped, Assert.IsType<Failed<Success, ErrorCode, Error<ErrorCode>>>(cancelled).Code);
            Assert.False(cancelledStored);
            Assert.Equal(IntPtr.Zero, initial);
            Result<Success, ErrorCode, Error<ErrorCode>> submitted = ScipLinearNativeSolver.SubmitInitialSolution(
                native: native,
                scip: scip,
                columns: columns,
                variables: variables,
                solution: ref initial,
                cancellationToken: default,
                stored: out bool stored);
            Assert.True(submitted.IsOk, submitted.ToString());
            Assert.True(stored);
            Assert.Equal(IntPtr.Zero, initial);
            IntPtr candidate;
            if (partial) {
                IntPtr candidates = native.Bind<ScipNative.BestSolution>("SCIPgetPartialSols")(scip);
                Assert.NotEqual(IntPtr.Zero, candidates);
                candidate = Marshal.ReadIntPtr(candidates);
            }
            else {
                candidate = native.GetBestSolution(scip);
            }
            Assert.NotEqual(IntPtr.Zero, candidate);
            Assert.Equal(1, native.GetSolutionValue(
                scip: scip,
                solution: candidate,
                variable: variables[0]));
            Assert.Equal(partial ? 1e98 : 0, native.GetSolutionValue(
                scip: scip,
                solution: candidate,
                variable: variables[1]));
        }
        finally {
            if (scip != IntPtr.Zero) {
                if (initial != IntPtr.Zero) {
                    Assert.Equal(1, native.FreeSolution(scip, ref initial));
                }
                for (int i = 0; i < variables.Length; i++) {
                    if (variables[i] != IntPtr.Zero) {
                        Assert.Equal(1, native.ReleaseVariable(scip, ref variables[i]));
                    }
                }
                Assert.Equal(1, native.FreeScip(ref scip));
            }
        }
    }

    [Theory]
    [InlineData(0.0, 1.0)]
    [InlineData(0.0, null)]
    [InlineData(0.0, 0.0)]
    [InlineData(2.0, 1.0)]
    [InlineData(0.5, 1.0)]
    [InlineData(2.0, null)]
    [InlineData(0.5, null)]
    public async Task InitialValuesAreHintsNotFixedOrProvenSolutions(double x, double? y) {
        using LinearTriadModel model = MakeModel(
            variables: [
                new ModelViewVariable(
                    index: 0,
                    lowerBound: Flt64.Zero,
                    upperBound: Flt64.One,
                    type: Binary.Instance,
                    name: "x",
                    initialResult: new Flt64(x)),
                new ModelViewVariable(
                    index: 1,
                    lowerBound: Flt64.Zero,
                    upperBound: Flt64.One,
                    type: Binary.Instance,
                    name: "y",
                    initialResult: y is { } value ? new Flt64(value) : null),
            ],
            rows: [new SparseVector([new SparseVectorEntry(0, Flt64.One), new SparseVectorEntry(1, Flt64.One)])],
            relations: [ConstraintRelation.GreaterEqual],
            right: [Flt64.One],
            objective: [new LinearObjectiveCell(0, Flt64.One), new LinearObjectiveCell(1, new Flt64(2))],
            category: ObjectCategory.Minimum,
            constant: 0);
        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> result = await new ScipLinearSolver().InvokeReportAsync(model);
        Assert.True(result.IsOk, result.ToString());
        Assert.Equal(SolutionPresence.Optimal, result.Value.SolutionPresence);
        Assert.Equal(
            expected: 1,
            actual: result.Value.Values[0].ToDouble(),
            precision: 6);
        Assert.Equal(
            expected: 0,
            actual: result.Value.Values[1].ToDouble(),
            precision: 6);
        Assert.True(result.Value.Provenance!.Descriptor.Capabilities.WarmStart);
    }

    [Fact]
    public async Task ContinuousModelCanImproveAnInitialValue() {
        using LinearTriadModel model = MakeModel(
            variables: [new ModelViewVariable(
                index: 0,
                lowerBound: Flt64.Zero,
                upperBound: new Flt64(10),
                type: Continuous.Instance,
                name: "x",
                initialResult: new Flt64(2))],
            rows: [],
            relations: [],
            right: [],
            objective: [new LinearObjectiveCell(0, Flt64.One)],
            category: ObjectCategory.Maximum,
            constant: 0);
        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> result = await new ScipLinearSolver().InvokeReportAsync(model);
        Assert.True(result.IsOk, result.ToString());
        Assert.Equal(SolutionPresence.Optimal, result.Value.SolutionPresence);
        Assert.Equal(
            expected: 10,
            actual: result.Value.Values[0].ToDouble(),
            precision: 6);
        Assert.True(result.Value.Provenance!.Descriptor.Capabilities.WarmStart);
    }

    [Fact]
    public async Task RepeatedSolvesReleaseNativeResourcesAndKeepPriorResults() {
        using LinearTriadModel model = MakeModel(
            variables: [new ModelViewVariable(
                index: 0,
                lowerBound: Flt64.Zero,
                upperBound: Flt64.One,
                type: Binary.Instance,
                name: "x")],
            rows: [],
            relations: [],
            right: [],
            objective: [new LinearObjectiveCell(0, Flt64.One)],
            category: ObjectCategory.Maximum,
            constant: 0);
        var solver = new ScipLinearSolver();
        SolveReport<Flt64>? first = null;
        for (int i = 0; i < 50; i++) {
            Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> result = await solver.InvokeReportAsync(model);
            Assert.True(result.IsOk, result.ToString());
            first ??= result.Value;
            Assert.Equal(
                expected: 1,
                actual: result.Value.Values[0].ToDouble(),
                precision: 6);
        }
        model.Dispose();
        Assert.Equal(
            expected: 1,
            actual: first!.Values[0].ToDouble(),
            precision: 6);
    }

    [Fact]
    public async Task LiveTokenPreservesCompletedResultAndIsSafeToCancelAfterRelease() {
        using LinearTriadModel model = MakeModel(
            variables: [new ModelViewVariable(
                index: 0,
                lowerBound: Flt64.Zero,
                upperBound: Flt64.One,
                type: Binary.Instance,
                name: "x")],
            rows: [],
            relations: [],
            right: [],
            objective: [new LinearObjectiveCell(0, Flt64.One)],
            category: ObjectCategory.Maximum,
            constant: 0);
        var solver = new ScipLinearSolver();
        for (int i = 0; i < 20; i++) {
            using var cancellation = new CancellationTokenSource();
            Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> result = await solver.InvokeReportAsync(
                model: model,
                cancellationToken: cancellation.Token);
            Assert.True(result.IsOk, result.ToString());
            cancellation.Cancel();
            Assert.Equal(TerminationReason.Completed, result.Value.TerminationReason);
            Assert.Equal(SolutionPresence.Optimal, result.Value.SolutionPresence);
            Assert.True(result.Value.Provenance!.Descriptor.Capabilities.Interrupt);
        }
    }

    [Fact]
    public void CancellationCanBeObservedDuringNativeTransformation() {
        Result<ScipNative, ErrorCode, Error<ErrorCode>> loaded = ScipNative.Load();
        Assert.True(loaded.IsOk, loaded.ToString());
        using ScipNative native = loaded.Value;
        using var cancellation = new CancellationTokenSource();
        var events = new ScipSolveEvents(
            native: native,
            token: cancellation.Token,
            config: new SolverConfig(),
            category: null,
            variables: Array.Empty<IntPtr>(),
            variableCount: 0,
            statusCallBack: null,
            sourceModel: null);
        IntPtr scip = IntPtr.Zero;
        try {
            Assert.Equal(1, native.CreateScip(ref scip));
            native.SetQuiet(scip, 1);
            Assert.Equal(1, native.IncludePlugins(scip));
            Assert.Equal(1, native.CreateProblem(scip, "cancellation"));
            Assert.Equal(1, events.Install(scip));
            cancellation.Cancel();
            Assert.Equal(1, native.Solve(scip));
            Assert.True(events.Interrupted);
            Assert.Contains(native.GetStatus(scip), new[] { 1, 11 });
            Assert.Null(events.CallbackError);
        }
        finally {
            if (scip != IntPtr.Zero) {
                Assert.Equal(1, native.FreeScip(ref scip));
            }
            GC.KeepAlive(events);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CancellationDuringTransformationProducesANativeReport(bool infeasibleStart) {
        const int pigeons = 12;
        const int holes = pigeons - 1;
        Result<ScipNative, ErrorCode, Error<ErrorCode>> loaded = ScipNative.Load();
        Assert.True(loaded.IsOk, loaded.ToString());
        using ScipNative native = loaded.Value;
        using var cancellation = new CancellationTokenSource();
        IntPtr scip = IntPtr.Zero;
        IntPtr initial = IntPtr.Zero;
        IntPtr[] variables = new IntPtr[pigeons * holes];
        var events = new ScipSolveEvents(
            native: native,
            token: cancellation.Token,
            config: new SolverConfig(),
            category: null,
            variables: variables,
            variableCount: variables.Length,
            statusCallBack: null,
            sourceModel: null);
        try {
            Assert.Equal(1, native.CreateScip(ref scip));
            native.SetQuiet(scip, 1);
            Assert.Equal(1, native.IncludePlugins(scip));
            Assert.Equal(1, native.CreateProblem(scip, "pigeonhole"));
            for (int i = 0; i < variables.Length; i++) {
                Assert.Equal(1, native.CreateVariable(
                    scip: scip,
                    variable: ref variables[i],
                    name: $"x{i}",
                    lower: 0,
                    upper: 1,
                    objective: 0,
                    type: 0));
                Assert.Equal(1, native.AddVariable(scip, variables[i]));
            }
            for (int i = 0; i < pigeons; i++) {
                IntPtr[] row = new IntPtr[holes];
                Array.Copy(
                    sourceArray: variables,
                    sourceIndex: i * holes,
                    destinationArray: row,
                    destinationIndex: 0,
                    length: holes);
                AddNativeRow(
                    native: native,
                    scip: scip,
                    variables: row,
                    lower: 1,
                    upper: 1);
            }
            double infinity = native.Infinity(scip);
            for (int j = 0; j < holes; j++) {
                IntPtr[] row = new IntPtr[pigeons];
                for (int i = 0; i < pigeons; i++) {
                    row[i] = variables[i * holes + j];
                }
                AddNativeRow(
                    native: native,
                    scip: scip,
                    variables: row,
                    lower: -infinity,
                    upper: 1);
            }
            if (infeasibleStart) {
                var columns = new ScipColumn[variables.Length];
                for (int i = 0; i < columns.Length; i++) {
                    columns[i] = new ScipColumn(
                        Lower: 0,
                        Upper: 1,
                        Type: 0,
                        Initial: 0);
                }
                Result<Success, ErrorCode, Error<ErrorCode>> submitted = ScipLinearNativeSolver.SubmitInitialSolution(
                    native: native,
                    scip: scip,
                    columns: columns,
                    variables: variables,
                    solution: ref initial,
                    cancellationToken: default,
                    stored: out bool stored);
                Assert.True(submitted.IsOk, submitted.ToString());
                Assert.True(stored);
            }
            Assert.Equal(1, events.Install(scip));
            cancellation.Cancel();
            Assert.Equal(1, native.Solve(scip));
            Assert.True(events.Interrupted);
            Assert.Equal(1, native.GetStatus(scip));
            Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> report = ScipLinearNativeSolver.ReadReport(
                native: native,
                scip: scip,
                variables: variables,
                infinity: infinity,
                interruptedByCancellation: events.Cancelled,
                config: new SolverConfig(),
                solutionAmount: 3);
            Assert.True(report.IsOk, report.ToString());
            Assert.Equal(TerminationReason.Cancelled, report.Value.TerminationReason);
            Assert.Equal(ProofStatus.None, report.Value.Proof.Status);
            Assert.Null(report.Value.Solution);
        }
        finally {
            if (scip != IntPtr.Zero) {
                if (initial != IntPtr.Zero) {
                    Assert.Equal(1, native.FreeSolution(scip, ref initial));
                }
                for (int i = 0; i < variables.Length; i++) {
                    if (variables[i] != IntPtr.Zero) {
                        Assert.Equal(1, native.ReleaseVariable(scip, ref variables[i]));
                    }
                }
                Assert.Equal(1, native.FreeScip(ref scip));
            }
            GC.KeepAlive(events);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancelsDuringNativeMipSearch(bool withPool) {
        const int vertexCount = 80;
        var variables = new List<ModelViewVariable>();
        var rows = new List<SparseVector>();
        var relations = new List<ConstraintRelation>();
        var right = new List<Flt64>();
        var objective = new List<LinearObjectiveCell>();
        for (int i = 0; i < vertexCount; i++) {
            variables.Add(new ModelViewVariable(
                index: i,
                lowerBound: Flt64.Zero,
                upperBound: Flt64.One,
                type: Binary.Instance,
                name: $"v{i}"));
        }
        var random = new Random(8675309);
        for (int i = 0; i < vertexCount; i++) {
            for (int j = i + 1; j < vertexCount; j++) {
                if (random.Next(2) == 0) {
                    continue;
                }
                int edge = variables.Count;
                variables.Add(new ModelViewVariable(
                    index: edge,
                    lowerBound: Flt64.Zero,
                    upperBound: Flt64.One,
                    type: Continuous.Instance,
                    name: $"e{edge}"));
                objective.Add(new LinearObjectiveCell(edge, Flt64.One));
                rows.Add(new SparseVector([
                    new SparseVectorEntry(edge, Flt64.One),
                    new SparseVectorEntry(i, -Flt64.One),
                    new SparseVectorEntry(j, -Flt64.One),
                ]));
                rows.Add(new SparseVector([
                    new SparseVectorEntry(edge, Flt64.One),
                    new SparseVectorEntry(i, Flt64.One),
                    new SparseVectorEntry(j, Flt64.One),
                ]));
                relations.Add(ConstraintRelation.LessEqual);
                relations.Add(ConstraintRelation.LessEqual);
                right.Add(Flt64.Zero);
                right.Add(new Flt64(2));
            }
        }
        LinearTriadModel model = MakeModel(
            variables: variables.ToArray(),
            rows: rows.ToArray(),
            relations: relations.ToArray(),
            right: right.ToArray(),
            objective: objective.ToArray(),
            category: ObjectCategory.Maximum,
            constant: 0);
        using var cancellation = new CancellationTokenSource();
        CancellationToken token = cancellation.Token;
        Task<Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>> task = Task.Run(async () => {
            using (model) {
                var solver = new ScipLinearSolver();
                return withPool ? await solver.InvokeReportAsync(
                    model: model,
                    solutionAmount: 3,
                    cancellationToken: token) : await solver.InvokeReportAsync(
                    model: model,
                    cancellationToken: token);
            }
        });
        cancellation.CancelAfter(TimeSpan.FromSeconds(1));
        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> result = await task.WaitAsync(TimeSpan.FromSeconds(25));
        Assert.True(result.IsOk, result.ToString());
        Assert.Equal(TerminationReason.Cancelled, result.Value.TerminationReason);
        Assert.NotNull(result.Value.Provenance);
        Assert.NotEqual(SolutionPresence.Optimal, result.Value.SolutionPresence);
        Assert.Equal(ProofStatus.None, result.Value.Proof.Status);
        Assert.Equal(result.Value.Solution is null ? SolutionPresence.None : SolutionPresence.Incumbent, result.Value.SolutionPresence);
        if (withPool && result.Value.Solution is { } solution) {
            Assert.All(solution.Pool, item => Assert.Equal(solution.Values.Length, item.Length));
            Assert.True(solution.Pool.Count <= 3);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public async Task RejectsFiniteValuesAtNativeInfinityBeforeBuildingTheProblem(int change) {
        using LinearTriadModel model = MakeModel(
            variables: [new ModelViewVariable(
                index: 0,
                lowerBound: Flt64.Zero,
                upperBound: new Flt64(change == 0 ? 1e20 : 1),
                type: Continuous.Instance,
                name: "x",
                initialResult: change == 4 ? new Flt64(1e20) : change == 5 ? new Flt64(-1e20) : null)],
            rows: [new SparseVector([new SparseVectorEntry(0, new Flt64(change == 1 ? 1e20 : 1))])],
            relations: [ConstraintRelation.LessEqual],
            right: [new Flt64(change == 2 ? 1e20 : 1)],
            objective: [],
            category: ObjectCategory.Minimum,
            constant: change == 3 ? 1e20 : 0);
        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> result = await new ScipLinearSolver().InvokeReportAsync(model);
        Assert.Equal(ErrorCode.IllegalArgument, Assert.IsType<Failed<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>>(result).Code);
    }

    [Fact]
    public async Task FractionalBinaryBoundsProduceAnInfeasibleReport() {
        using LinearTriadModel model = MakeModel(
            variables: [new ModelViewVariable(
                index: 0,
                lowerBound: new Flt64(0.2),
                upperBound: new Flt64(0.8),
                type: Binary.Instance,
                name: "x")],
            rows: [],
            relations: [],
            right: [],
            objective: [],
            category: ObjectCategory.Minimum,
            constant: 0);
        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> result = await new ScipLinearSolver().InvokeReportAsync(model);
        Assert.True(result.IsOk, result.ToString());
        Assert.Equal(ProblemStatus.Infeasible, result.Value.ProblemStatus);
    }

    [Fact]
    public async Task StatusCallbackObservesEveryNativeEvent() {
        using LinearTriadModel model = MaxCutModel(vertexCount: 60, seed: 8675309);
        var config = new SolverConfig { TimeLimit = TimeSpan.FromSeconds(20), ThreadCount = 1UL };
        var observations = new List<SolvingStatus>();
        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> result =
            await new ScipLinearSolver(config).InvokeReportAsync(
                model: model,
                solvingStatusCallBack: status => {
                    observations.Add(status);
                    return Results.OkInstance;
                });
        Assert.True(result.IsOk, result.ToString());
        Assert.NotEmpty(observations);
        Assert.All(observations, status => {
            Assert.Equal("scip", status.Solver);
            Assert.Equal(0, status.SolverIndex);
            Assert.Same(config, status.SolverConfig);
            Assert.Same(model, status.IntermediateModel);
            Assert.Equal(ObjectCategory.Maximum, status.ObjectCategory);
            Assert.True(double.IsFinite(status.Obj.ToDouble()));
            Assert.True(double.IsFinite(status.BestBound!.Value.ToDouble()));
            Assert.True(status.Time >= TimeSpan.Zero);
            Assert.NotNull(status.SolverCallBack);
            // 间隙用 `SolvingStatusSupport.Gap` 的公式，不额外归一化目标方向。
            // The gap uses the `SolvingStatusSupport.Gap` formula and is not renormalised for the objective direction.
            Assert.Equal(
                SolvingStatusSupport.Gap(status.Obj, status.PossibleBestObj).ToDouble(),
                status.Gap.ToDouble(),
                12);
            Assert.True(
                status.CurrentBestSolution is null ||
                status.CurrentBestSolution.Count == model.Variables.Count);
        });
        Assert.Equal(
            observations[0].InitialBestObj.ToDouble(),
            observations[^1].InitialBestObj.ToDouble(),
            12);
        TimeSpan previous = TimeSpan.Zero;
        foreach (SolvingStatus status in observations) {
            Assert.True(status.Time >= previous, "原生求解时间不能倒退 / native solving time must not go backwards");
            previous = status.Time;
        }
    }

    [Fact]
    public async Task FailedStatusCallbackInterruptsTheSolve() {
        using LinearTriadModel model = MaxCutModel(vertexCount: 80, seed: 8675309);
        var config = new SolverConfig { TimeLimit = TimeSpan.FromSeconds(20), ThreadCount = 1UL };
        int calls = 0;
        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> result =
            await new ScipLinearSolver(config).InvokeReportAsync(
                model: model,
                solvingStatusCallBack: _ => {
                    calls++;
                    return Results.Failed<Success>(new Err<ErrorCode>(
                        ErrorCode.ApplicationStopped,
                        "回调请求停止 / the callback requested a stop"));
                });
        Assert.True(result.IsOk, result.ToString());
        Assert.True(calls >= 1, "回调失败前应至少收到一次原生事件 / at least one native event precedes the failure");
        Assert.Equal(TerminationReason.Interrupted, result.Value.TerminationReason);
        Assert.Equal(ProofStatus.None, result.Value.Proof.Status);
        Assert.Equal(
            result.Value.Solution is null ? SolutionPresence.None : SolutionPresence.Incumbent,
            result.Value.SolutionPresence);
    }

    /// <summary>
    /// 静止窗口为 0 且改进阈值极大时，第二次事件即应中断，不依赖具体机器速度。
    /// With a zero stale window and an enormous improvement threshold the second event interrupts,
    /// independent of machine speed.
    /// </summary>
    [Fact]
    public async Task NoImprovementTimeInterruptsAStalledSearch() {
        using LinearTriadModel model = MaxCutModel(vertexCount: 80, seed: 8675309);
        var config = new SolverConfig {
            TimeLimit = TimeSpan.FromSeconds(20),
            ThreadCount = 1UL,
            NotImprovementTime = TimeSpan.Zero,
            ImproveThreshold = new Flt64(1e12),
        };
        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> result =
            await new ScipLinearSolver(config).InvokeReportAsync(model);
        Assert.True(result.IsOk, result.ToString());
        Assert.Equal(TerminationReason.Interrupted, result.Value.TerminationReason);
        Assert.Equal(ProofStatus.None, result.Value.Proof.Status);
        Assert.Equal("0", result.Value.Provenance!.EffectiveParameters["notImprovementTime"]);
    }

    /// <summary>
    /// 极小时间限制下的时限结论：不验证响应速度，只验证结论与证明强度不被伪造。
    /// A tiny time limit: this does not prove response latency, only that the conclusion and proof
    /// strength are not fabricated.
    /// </summary>
    [Fact]
    public async Task TinyTimeLimitIsRecordedWithoutAFakeProof() {
        using LinearTriadModel model = MaxCutModel(vertexCount: 120, seed: 8675309);
        var config = new SolverConfig { TimeLimit = TimeSpan.FromMilliseconds(1), ThreadCount = 1UL };
        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> result =
            await new ScipLinearSolver(config).InvokeReportAsync(model);
        Assert.True(result.IsOk, result.ToString());
        Assert.Equal("0.001", result.Value.Provenance!.EffectiveParameters["time"]);
        if (result.Value.TerminationReason == TerminationReason.TimeLimit) {
            Assert.NotEqual(ProofStatus.Verified, result.Value.Proof.Status);
            Assert.NotEqual(SolutionPresence.Optimal, result.Value.SolutionPresence);
        }
    }

    private static LinearTriadModel MakeModel(
        ModelViewVariable[] variables,
        SparseVector[] rows,
        ConstraintRelation[] relations,
        Flt64[] right,
        LinearObjectiveCell[] objective,
        ObjectCategory category,
        double constant) => new(
            variables: variables,
            constraints: new LinearConstraintBatch(
                lhs: new SparseMatrix(rows),
                signs: relations,
                rhs: right,
                names: Array.ConvertAll(rows, _ => "row"),
                sources: Array.ConvertAll(rows, _ => ConstraintSource.Origin)),
            objective: new Objective<LinearObjectiveCell>(
                category: category,
                cells: objective,
                constant: new Flt64(constant)),
            name: "test");

    /// <summary>
    /// 构造固定种子随机图上的最大割 MIP，保证搜索耗时足够观察事件。
    /// Builds a MaxCut MIP on a seeded random graph so the search runs long enough to observe events.
    /// </summary>
    private static LinearTriadModel MaxCutModel(int vertexCount, int seed) {
        var variables = new List<ModelViewVariable>();
        var rows = new List<SparseVector>();
        var relations = new List<ConstraintRelation>();
        var right = new List<Flt64>();
        var objective = new List<LinearObjectiveCell>();
        for (int i = 0; i < vertexCount; i++) {
            variables.Add(new ModelViewVariable(
                index: i,
                lowerBound: Flt64.Zero,
                upperBound: Flt64.One,
                type: Binary.Instance,
                name: $"v{i}"));
        }
        var random = new Random(seed);
        for (int i = 0; i < vertexCount; i++) {
            for (int j = i + 1; j < vertexCount; j++) {
                if (random.Next(2) == 0) {
                    continue;
                }
                int edge = variables.Count;
                variables.Add(new ModelViewVariable(
                    index: edge,
                    lowerBound: Flt64.Zero,
                    upperBound: Flt64.One,
                    type: Continuous.Instance,
                    name: $"e{edge}"));
                objective.Add(new LinearObjectiveCell(edge, Flt64.One));
                rows.Add(new SparseVector([
                    new SparseVectorEntry(edge, Flt64.One),
                    new SparseVectorEntry(i, -Flt64.One),
                    new SparseVectorEntry(j, -Flt64.One),
                ]));
                rows.Add(new SparseVector([
                    new SparseVectorEntry(edge, Flt64.One),
                    new SparseVectorEntry(i, Flt64.One),
                    new SparseVectorEntry(j, Flt64.One),
                ]));
                relations.Add(ConstraintRelation.LessEqual);
                relations.Add(ConstraintRelation.LessEqual);
                right.Add(Flt64.Zero);
                right.Add(new Flt64(2));
            }
        }
        return MakeModel(
            variables: variables.ToArray(),
            rows: rows.ToArray(),
            relations: relations.ToArray(),
            right: right.ToArray(),
            objective: objective.ToArray(),
            category: ObjectCategory.Maximum,
            constant: 0);
    }

    private static void AddNativeRow(ScipNative native, IntPtr scip, IntPtr[] variables, double lower, double upper) {
        double[] coefficients = new double[variables.Length];
        Array.Fill(coefficients, 1);
        IntPtr constraint = IntPtr.Zero;
        try {
            Assert.Equal(1, native.CreateLinearConstraint(
                scip: scip,
                constraint: ref constraint,
                name: "row",
                count: variables.Length,
                variables: variables,
                coefficients: coefficients,
                lower: lower,
                upper: upper));
            Assert.Equal(1, native.AddConstraint(scip, constraint));
        }
        finally {
            if (constraint != IntPtr.Zero) {
                Assert.Equal(1, native.ReleaseConstraint(scip, ref constraint));
            }
        }
    }
}
#endif
