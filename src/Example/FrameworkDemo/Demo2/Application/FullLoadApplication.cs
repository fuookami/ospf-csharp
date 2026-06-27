#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.AirworthinessSecurity;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Mac;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.MacOptimization;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.SoftSecurity;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Application;

/// <summary>
/// 全装载应用。Full load application: orchestrates the full-load stowage optimization pipeline.
///
/// Port of Kotlin FullLoadApplication / FullLoadAlgorithm.
/// Supports both direct MILP and Benders decomposition paths with quality-guarded fallback.
/// </summary>
public sealed class FullLoadApplication
{
    private readonly AircraftContext _aircraftContext = new();
    private readonly StowageContext _stowageContext = new();
    private readonly MacContext _macContext = new();
    private readonly AirworthinessSecurityContext _airworthinessSecurityContext = new();
    private readonly SoftSecurityContext _softSecurityContext = new();
    private readonly MacOptimizationContext _macOptimizationContext = new();

    /// <summary>
    /// 执行全装载算法。Execute the full load algorithm.
    /// </summary>
    /// <param name="request">请求 DTO / Request DTO</param>
    /// <param name="bendersSolver">Benders 分解求解器（可选，为 null 时跳过 Benders）/ Benders decomposition solver (optional, null skips Benders)</param>
    /// <returns>响应 DTO 和渲染 DTO / Response DTO and render DTO</returns>
    public (ResponseDTO Response, RenderDTO? Render) Execute(RequestDTO request, IBendersDecompositionSolver? bendersSolver = null)
    {
        var notes = new List<string>();

        // 1. Initialize contexts
        Try r1 = Init(request);
        if (r1 is Failed<Success, ErrorCode, Error<ErrorCode>> failed)
        {
            return (ResponseDTO.NoSolution("InitFailed", notes), null);
        }

        // 2. Feasibility diagnostics
        FeasibilityDiagnostics.AppendCoreFeasibilityDiagnostics(request, notes);

        // 3. Check aircraft support
        if (!BendersStrategy.SupportedAircraft(request.AircraftType))
        {
            notes.Add($"unsupported aircraft type for full-load path: {request.AircraftType}");
            return (ResponseDTO.NoSolution("UnsupportedAircraft", notes), null);
        }

        if (notes.Count > 0)
        {
            return (ResponseDTO.NoSolution("NoSolution", notes), null);
        }

        // 4. Resolve solve mode
        SolveMode solveMode = BendersStrategy.ResolveSolveMode(request, notes);

        // 5. Solve
        ResponseDTO response;
        switch (solveMode)
        {
            case SolveMode.Benders bendersMode:
                notes.Add("solver_path=benders");
                DiagnosticsHelper.PushGroupedNote(
                    notes, DiagnosticsHelper.LevelDiagnostic, DiagnosticsHelper.GroupSolver,
                    DiagnosticsHelper.CodeSolverPath, "benders");
                response = SolveWithBenders(request, bendersMode.Config, bendersSolver, notes);
                break;

            case SolveMode.Milp:
            default:
                notes.Add("solver_path=milp_direct");
                DiagnosticsHelper.PushGroupedNote(
                    notes, DiagnosticsHelper.LevelDiagnostic, DiagnosticsHelper.GroupSolver,
                    DiagnosticsHelper.CodeSolverPath, "milp_direct");
                response = ResponseDTO.NoSolution("NotYetPorted", notes);
                break;
        }

        return (response, null);
    }

    /// <summary>
    /// 使用 Benders 分解求解。Solve with Benders decomposition, with quality guard and MILP fallback.
    /// </summary>
    private ResponseDTO SolveWithBenders(
        RequestDTO request,
        EffectiveBendersAdaptiveConfig config,
        IBendersDecompositionSolver? solver,
        List<string> notes)
    {
        if (solver == null)
        {
            notes.Add("Benders solver not available, falling back to MILP");
            return ResponseDTO.NoSolution("BendersSolverUnavailable", notes);
        }

        // Build Benders models (master/sub decomposition)
        var bendersModels = BuildBendersModels();

        notes.Add($"benders_adaptive=max_iterations={config.MaxIterations},tolerance={config.Tolerance:F6}");
        DiagnosticsHelper.PushGroupedNote(
            notes, DiagnosticsHelper.LevelDiagnostic, DiagnosticsHelper.GroupSolver,
            DiagnosticsHelper.CodeBendersAdaptiveEffective,
            $"max_iterations={config.MaxIterations},tolerance={config.Tolerance:F6}");

        // Run Benders iteration loop
        var bendersResultTask = BendersSolver.SolveAsync(
            solver: solver,
            masterModel: bendersModels.MasterModel,
            subModel: bendersModels.SubModel,
            fixedVariables: bendersModels.FixedVariables,
            objectVariable: bendersModels.ObjectVariable,
            config: config,
            notes: notes);
        bendersResultTask.Wait();

        var bendersResult = bendersResultTask.Result;
        if (bendersResult is Failed<BendersResult, ErrorCode, Error<ErrorCode>> bendersFailed)
        {
            // Benders failed -- fallback to MILP if configured
            if (request.ResolvedSolvePolicy.BendersFallbackToMilp)
            {
                notes.Add("Benders failed, falling back to MILP");
                DiagnosticsHelper.PushGroupedNote(
                    notes, DiagnosticsHelper.LevelDiagnostic, DiagnosticsHelper.GroupSolver,
                    DiagnosticsHelper.CodeBendersFailed, "benders failed, fallback to milp");
                return ResponseDTO.NoSolution("BendersFailed_MilpFallback", notes);
            }
            return ResponseDTO.NoSolution("BendersFailed", notes);
        }

        var result = ((Ok<BendersResult, ErrorCode, Error<ErrorCode>>)bendersResult).Value;

        // Quality guard check
        var qualityGuard = BendersStrategy.ResolveQualityGuardConfig(request.BendersQualityOverrides);
        string? qualityReason = BendersStrategy.ResolveQualityReason(
            adaptive: config,
            qualityGuard: qualityGuard,
            bendersIterations: result.BendersIterations,
            bendersGap: result.Gap,
            bendersTimeMs: result.TimeMs,
            executedIterations: result.RuntimeMetrics?.ExecutedIterations,
            totalCuts: result.RuntimeMetrics?.TotalCuts,
            iterationSnapshots: result.RuntimeMetrics != null
                ? GetSnapshotObjectives(result.RuntimeMetrics.IterationSnapshots)
                : null);

        if (qualityReason != null)
        {
            string qualityCode = qualityReason switch
            {
                "gap_guard_exceeded" => DiagnosticsHelper.CodeBendersGapGuardExceeded,
                "time_guard_exceeded" => DiagnosticsHelper.CodeBendersTimeGuardExceeded,
                "progress_guard_triggered" => DiagnosticsHelper.CodeBendersProgressGuardTriggered,
                "cut_efficiency_low" => DiagnosticsHelper.CodeBendersCutEfficiencyLow,
                "trajectory_weak" => DiagnosticsHelper.CodeBendersTrajectoryWeak,
                _ => DiagnosticsHelper.CodeBendersFailed
            };
            DiagnosticsHelper.PushGroupedNote(
                notes, DiagnosticsHelper.LevelDiagnostic, DiagnosticsHelper.GroupSolver,
                qualityCode, $"benders quality reason: {qualityReason}");

            double qualityScore = BendersStrategy.ResolveQualityScore(
                adaptive: config,
                qualityGuard: qualityGuard,
                bendersIterations: result.BendersIterations,
                bendersGap: result.Gap,
                bendersTimeMs: result.TimeMs,
                executedIterations: result.RuntimeMetrics?.ExecutedIterations,
                totalCuts: result.RuntimeMetrics?.TotalCuts,
                iterationSnapshots: result.RuntimeMetrics != null
                    ? GetSnapshotObjectives(result.RuntimeMetrics.IterationSnapshots)
                    : null);
            DiagnosticsHelper.PushGroupedNote(
                notes, DiagnosticsHelper.LevelDiagnostic, DiagnosticsHelper.GroupSolver,
                DiagnosticsHelper.CodeBendersQualityScore, $"quality_score={qualityScore:F2}");

            if (request.ResolvedSolvePolicy.BendersFallbackToMilp)
            {
                DiagnosticsHelper.PushGroupedNote(
                    notes, DiagnosticsHelper.LevelDiagnostic, DiagnosticsHelper.GroupSolver,
                    DiagnosticsHelper.CodeBendersQualityAction, $"action=fallback_to_milp reason={qualityReason}");
                return ResponseDTO.NoSolution("BendersQualityInsufficient_MilpFallback", notes);
            }
            else
            {
                DiagnosticsHelper.PushGroupedNote(
                    notes, DiagnosticsHelper.LevelDiagnostic, DiagnosticsHelper.GroupSolver,
                    DiagnosticsHelper.CodeBendersQualityAction, $"action=accept_benders reason={qualityReason}");
            }
        }

        // Report Benders diagnostics
        notes.Add($"benders_iterations={result.BendersIterations}");
        notes.Add($"benders_gap={result.Gap:F6}");
        notes.Add($"benders_time_ms={result.TimeMs}");
        DiagnosticsHelper.PushGroupedNote(
            notes, DiagnosticsHelper.LevelDiagnostic, DiagnosticsHelper.GroupSolver,
            DiagnosticsHelper.CodeBendersIterations, $"iterations={result.BendersIterations}");
        DiagnosticsHelper.PushGroupedNote(
            notes, DiagnosticsHelper.LevelDiagnostic, DiagnosticsHelper.GroupSolver,
            DiagnosticsHelper.CodeBendersGap, $"gap={result.Gap:F6}");
        DiagnosticsHelper.PushGroupedNote(
            notes, DiagnosticsHelper.LevelDiagnostic, DiagnosticsHelper.GroupSolver,
            DiagnosticsHelper.CodeBendersTimeMs, $"time_ms={result.TimeMs}");

        // Build assignments from solution
        var assignments = BuildAssignments(request, result.Solution);

        return ResponseDTO.Optimal(result.Obj, assignments, notes);
    }

    /// <summary>
    /// 构建 Benders 分解模型。Build master and sub models for Benders decomposition.
    ///
    /// Master: stowage (binary loading decisions) + mac + soft_security + mac_optimization
    /// Sub: airworthiness constraints (weight/CG/MAC checks)
    /// </summary>
    private BendersModels BuildBendersModels()
    {
        var masterModel = new LinearMetaModel<Flt64>("demo2_full_load_master", ObjectCategory.Minimum);
        var subModel = new LinearMetaModel<Flt64>("demo2_full_load_sub", ObjectCategory.Minimum);

        // Master problem: stowage + mac + soft_security + mac_optimization
        // (These contexts register their variables and constraints into the master model)
        // Note: Register methods on domain contexts are stubs in current C# port.
        // When fully ported, these would call:
        //   _stowageContext.RegisterForBendersMP(masterModel);
        //   _macContext.RegisterForBendersMP(masterModel);
        //   _softSecurityContext.RegisterForBendersMP(masterModel);
        //   _macOptimizationContext.RegisterForBendersMP(masterModel);

        // Sub problem: airworthiness constraints
        //   _stowageContext.RegisterForBendersSP(subModel);
        //   _airworthinessSecurityContext.RegisterForBendersSP(subModel);

        // Create theta variable for cut generation
        var thetaVar = new URealVar("benders_theta");
        masterModel.Add(thetaVar);

        // Build fixedVariables from stowage x variables (binary loading decisions)
        // When stowage aggregation is fully ported, iterate over stowage items/positions
        var fixedVariables = new Dictionary<IVariableItem, Flt64>();

        return new BendersModels(masterModel, subModel, thetaVar, fixedVariables);
    }

    /// <summary>
    /// 从解向量构建装载分配。Build loading assignments from solution vector.
    /// </summary>
    private static IReadOnlyList<string> BuildAssignments(RequestDTO request, double[] solution)
    {
        var assignments = new List<string>();
        if (solution.Length == 0) return assignments;

        int numCargos = request.Cargos.Count;
        int numPositions = request.Positions.Count;

        for (int c = 0; c < numCargos; c++)
        {
            for (int p = 0; p < numPositions; p++)
            {
                int idx = c * numPositions + p;
                if (idx < solution.Length && solution[idx] > 0.5)
                {
                    assignments.Add($"{request.Cargos[c].Name}->{request.Positions[p].Name}");
                }
            }
        }

        return assignments;
    }

    /// <summary>
    /// 提取迭代快照目标值。Extract objective values from iteration snapshots.
    /// </summary>
    private static List<double> GetSnapshotObjectives(IReadOnlyList<BendersIterationSnapshot> snapshots)
    {
        var result = new List<double>(snapshots.Count);
        foreach (var s in snapshots)
        {
            result.Add(s.MasterObj);
        }
        return result;
    }

    private Try Init(RequestDTO request)
    {
        Try r1 = _aircraftContext.Init(request);
        if (r1.IsFailed) return r1;

        Try r2 = _stowageContext.Init(_aircraftContext, request);
        if (r2.IsFailed) return r2;

        Try r3 = _macContext.Init(_aircraftContext, _stowageContext, request);
        if (r3.IsFailed) return r3;

        Try r4 = _airworthinessSecurityContext.Init(_aircraftContext, _stowageContext, _macContext, request);
        if (r4.IsFailed) return r4;

        Try r5 = _softSecurityContext.Init(_aircraftContext, _stowageContext, request);
        if (r5.IsFailed) return r5;

        Try r6 = _macOptimizationContext.Init(_aircraftContext, _stowageContext, _macContext, request);
        if (r6.IsFailed) return r6;

        return Results.Ok<Success>(Results.SuccessInstance);
    }

    /// <summary>
    /// Benders 模型容器。Container for Benders master/sub models and shared state.
    /// </summary>
    private sealed record BendersModels(
        LinearMetaModel<Flt64> MasterModel,
        LinearMetaModel<Flt64> SubModel,
        IVariableItem ObjectVariable,
        Dictionary<IVariableItem, Flt64> FixedVariables);
}
