#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using CoreBendersSolver = Fuookami.Ospf.Core.Solver.IBendersDecompositionSolver;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;

/// <summary>
/// Benders 迭代快照。Per-iteration snapshot of Benders decomposition.
/// </summary>
/// <param name="MasterObj">主问题目标值 / Master problem objective value</param>
/// <param name="Gap">当前间隙 / Current gap</param>
public sealed record BendersIterationSnapshot(double MasterObj, double Gap);

/// <summary>
/// Benders 运行时指标。Runtime metrics for Benders decomposition execution.
/// </summary>
/// <param name="ExecutedIterations">已执行迭代数 / Executed iteration count</param>
/// <param name="TotalCuts">总切割数 / Total cuts generated</param>
/// <param name="IterationSnapshots">迭代快照列表 / Iteration snapshot list</param>
public sealed record BendersRuntimeMetrics(
    int ExecutedIterations,
    int TotalCuts,
    IReadOnlyList<BendersIterationSnapshot> IterationSnapshots);

/// <summary>
/// Benders 求解结果。Result of Benders decomposition solving.
/// </summary>
/// <param name="Obj">最终目标值 / Final objective value</param>
/// <param name="Solution">解向量 / Solution vector</param>
/// <param name="Gap">最终间隙 / Final gap</param>
/// <param name="TimeMs">耗时（毫秒）/ Elapsed time in milliseconds</param>
/// <param name="BendersIterations">Benders 迭代次数 / Benders iteration count</param>
/// <param name="RuntimeMetrics">运行时指标（可选）/ Runtime metrics (optional)</param>
public sealed record BendersResult(
    double Obj,
    double[] Solution,
    double Gap,
    long TimeMs,
    int BendersIterations,
    BendersRuntimeMetrics? RuntimeMetrics);

/// <summary>
/// Benders 分解求解器。Orchestrates the Benders decomposition iteration loop.
///
/// Port of Kotlin BendersSolver: master/sub solve, cut generation, convergence check.
/// </summary>
public static class BendersSolver
{
    /// <summary>
    /// 验证主问题输出可行性。Validate master problem output is feasible.
    /// </summary>
    private static Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> RequireFeasibleMasterOutput(SolverOutput output)
    {
        if (output is FeasibleSolverOutput<Flt64> feasible)
        {
            return Results.Ok(feasible);
        }

        return Results.Failed<FeasibleSolverOutput<Flt64>>(
            new Err<ErrorCode>(ErrorCode.ORModelInfeasible, "Master solver returned infeasible output."));
    }

    /// <summary>
    /// 执行 Benders 分解求解。Execute Benders decomposition iteration loop.
    /// </summary>
    /// <param name="solver">Benders 分解求解器 / Benders decomposition solver</param>
    /// <param name="masterModel">主问题模型 / Master problem model</param>
    /// <param name="subModel">子问题模型 / Sub problem model</param>
    /// <param name="fixedVariables">固定变量映射 / Fixed variables mapping</param>
    /// <param name="objectVariable">目标变量 / Objective variable (theta)</param>
    /// <param name="config">有效 Benders 自适应配置 / Effective Benders adaptive config</param>
    /// <param name="notes">备注列表 / Notes list (mutable)</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>Benders 求解结果 / Benders solve result</returns>
    public static async Task<Result<BendersResult, ErrorCode, Error<ErrorCode>>> SolveAsync(
        CoreBendersSolver solver,
        LinearMetaModel<Flt64> masterModel,
        LinearMetaModel<Flt64> subModel,
        IReadOnlyDictionary<IVariableItem, Flt64> fixedVariables,
        IVariableItem objectVariable,
        EffectiveBendersAdaptiveConfig config,
        List<string> notes,
        CancellationToken cancellationToken = default)
    {
        long startTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var snapshots = new List<BendersIterationSnapshot>();
        int totalCuts = 0;
        int maxIterations = config.MaxIterations;
        double tolerance = config.Tolerance;
        int stallWindow = config.MaxStallIterations ?? maxIterations;
        int objStallWindow = config.ObjectiveStallIterations ?? maxIterations;

        double? bestObj = null;
        int stallCount = 0;
        int objStallCount = 0;

        for (int iteration = 1; iteration <= maxIterations; iteration++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // 1. Solve master problem (one iteration at a time)
            var masterResult = await solver.SolveMasterAsync(
                $"benders_master_{iteration}",
                masterModel,
                toLogModel: false,
                cancellationToken: cancellationToken);

            if (masterResult is Failed<SolverOutput, ErrorCode, Error<ErrorCode>> masterFailed)
            {
                return Results.Failed<BendersResult>(masterFailed.Error);
            }
            if (masterResult is not Ok<SolverOutput, ErrorCode, Error<ErrorCode>> masterOk)
            {
                return Results.Failed<BendersResult>(
                    new Err<ErrorCode>(ErrorCode.ApplicationError, "Master solver returned unexpected result type."));
            }

            var masterOutput = masterOk.Value;
            var feasibleResult = RequireFeasibleMasterOutput(masterOutput);
            if (feasibleResult is Failed<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> feasibleFailed)
            {
                return Results.Failed<BendersResult>(feasibleFailed.Error);
            }
            var masterFeasible = ((Ok<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>)feasibleResult).Value;

            double masterObj = masterFeasible.Obj.ToDouble();
            snapshots.Add(new BendersIterationSnapshot(masterObj, masterFeasible.Gap.ToDouble()));

            if (bestObj == null || masterObj > bestObj)
            {
                if (bestObj != null)
                {
                    stallCount = 0;
                    objStallCount = 0;
                }
                bestObj = masterObj;
            }
            else
            {
                stallCount++;
                objStallCount++;
            }

            // 2. Solve sub problem
            var subResult = await solver.SolveSubAsync(
                $"benders_sub_{iteration}",
                subModel,
                objectVariable,
                fixedVariables,
                toLogModel: false,
                cancellationToken: cancellationToken);

            if (subResult is Failed<CoreBendersSolver.LinearSubResult, ErrorCode, Error<ErrorCode>> subFailed)
            {
                return Results.Failed<BendersResult>(subFailed.Error);
            }
            if (subResult is not Ok<CoreBendersSolver.LinearSubResult, ErrorCode, Error<ErrorCode>> subOk)
            {
                return Results.Failed<BendersResult>(
                    new Err<ErrorCode>(ErrorCode.ApplicationError, "Sub solver returned unexpected result type."));
            }

            var subOutput = subOk.Value;

            switch (subOutput)
            {
                case CoreBendersSolver.LinearFeasibleResult feasibleSub:
                {
                    // Add optimality cuts
                    var cuts = feasibleSub.Cuts;
                    if (cuts != null)
                    {
                        totalCuts += cuts.Count;
                        foreach (var cut in cuts)
                        {
                            var addResult = masterModel.AddConstraint(
                                cut, group: null, name: $"benders_opt_cut_{iteration}_{totalCuts}");
                            if (addResult is Failed<Success, ErrorCode, Error<ErrorCode>> addFailed)
                            {
                                return Results.Failed<BendersResult>(addFailed.Error);
                            }
                        }
                    }

                    double subObj = feasibleSub.Obj.ToDouble();
                    double currentGap = global::System.Math.Abs(masterObj) > 1e-12
                        ? global::System.Math.Abs(masterObj - subObj) / global::System.Math.Abs(masterObj)
                        : global::System.Math.Abs(masterObj - subObj);

                    if (currentGap <= tolerance)
                    {
                        long timeMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - startTime;
                        int solLen = masterFeasible.Solution.Length;
                        double[] solution = new double[solLen];
                        for (int i = 0; i < solLen; i++)
                        {
                            solution[i] = masterFeasible.Solution[i].ToDouble();
                        }

                        return Results.Ok(new BendersResult(
                            Obj: masterObj,
                            Solution: solution,
                            Gap: currentGap,
                            TimeMs: timeMs,
                            BendersIterations: iteration,
                            RuntimeMetrics: new BendersRuntimeMetrics(
                                ExecutedIterations: iteration,
                                TotalCuts: totalCuts,
                                IterationSnapshots: snapshots)));
                    }

                    break;
                }

                case CoreBendersSolver.LinearInfeasibleResult infeasibleSub:
                {
                    // Add feasibility cuts (Farkas cuts)
                    var cuts = infeasibleSub.Cuts;
                    if (cuts != null)
                    {
                        totalCuts += cuts.Count;
                        foreach (var cut in cuts)
                        {
                            var addResult = masterModel.AddConstraint(
                                cut, group: null, name: $"benders_feas_cut_{iteration}_{totalCuts}");
                            if (addResult is Failed<Success, ErrorCode, Error<ErrorCode>> addFailed)
                            {
                                return Results.Failed<BendersResult>(addFailed.Error);
                            }
                        }
                    }

                    break;
                }
            }

            // 3. Check stall conditions
            if (stallCount >= stallWindow || objStallCount >= objStallWindow)
            {
                notes.Add($"Benders stalled after {iteration} iterations (stall={stallCount}, objStall={objStallCount})");
                break;
            }
        }

        // Return best found solution (may not have converged)
        long finalTimeMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - startTime;
        double finalObj = bestObj ?? 0.0;
        double finalGap = snapshots.Count > 0 ? snapshots[snapshots.Count - 1].Gap : 1.0;

        return Results.Ok(new BendersResult(
            Obj: finalObj,
            Solution: Array.Empty<double>(),
            Gap: finalGap,
            TimeMs: finalTimeMs,
            BendersIterations: snapshots.Count,
            RuntimeMetrics: new BendersRuntimeMetrics(
                ExecutedIterations: snapshots.Count,
                TotalCuts: totalCuts,
                IterationSnapshots: snapshots)));
    }
}
