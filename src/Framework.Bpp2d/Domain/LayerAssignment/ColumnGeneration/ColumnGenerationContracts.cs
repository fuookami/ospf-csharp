#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fuookami.Ospf.Framework.Bpp2d.Domain;
using Fuookami.Ospf.Framework.Bpp2d.Domain.Item.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.Bpp2d.Domain.LayerAssignment.ColumnGeneration;

/// <summary>
/// 受限主问题求解器 / The restricted-master-problem solver.
/// </summary>
/// <remarks>
/// 基线的 6 个 `fun interface` 是**挂起函数**，C# 侧对应 `Task`。它们不是"零实现的装饰"：
/// <see cref="ColumnGenerationAlgorithm"/> 是这里的确定性实现者，缺任何一个都会让主循环少一臂。
///
/// The baseline's six `fun interface`s are suspend functions, which map to `Task` here. They are not decoration: `ColumnGenerationAlgorithm` in is their concrete consumer, and
/// dropping any of them would leave the main loop one arm short.
/// </remarks>
public interface IColumnGenerationRmpSolver
{
    /// <summary>求解一轮 LP / Solve one LP.</summary>
    /// <param name="state">当前状态 / The current state.</param>
    /// <param name="cancellationToken">取消令牌 / The cancellation token.</param>
    /// <returns>LP 结果 / The LP result.</returns>
    Task<Result<ColumnGenerationLpResult, ErrorCode, Error<ErrorCode>>> SolveAsync(
        ColumnGenerationState state, CancellationToken cancellationToken = default);
}

/// <summary>
/// 最终 MILP 求解器 / The final-MILP solver.
/// </summary>
public interface IColumnGenerationFinalSolver
{
    /// <summary>求解最终 MILP / Solve the final MILP.</summary>
    /// <param name="state">当前状态 / The current state.</param>
    /// <param name="cancellationToken">取消令牌 / The cancellation token.</param>
    /// <returns>最终结果 / The final result.</returns>
    Task<Result<ColumnGenerationFinalResult, ErrorCode, Error<ErrorCode>>> SolveAsync(
        ColumnGenerationState state, CancellationToken cancellationToken = default);
}

/// <summary>
/// 解的分析器 / The solution analyser.
/// </summary>
public interface IColumnGenerationSolutionAnalyzer
{
    /// <summary>分析一个状态 / Analyse a state.</summary>
    /// <param name="state">状态 / The state.</param>
    /// <param name="cancellationToken">取消令牌 / The cancellation token.</param>
    /// <returns>执行结果 / Execution result.</returns>
    Task<Try> AnalyzeAsync(ColumnGenerationState state, CancellationToken cancellationToken = default);
}

/// <summary>
/// 失败分析器 / The failure analyser.
/// </summary>
public interface IColumnGenerationFailureAnalyzer
{
    /// <summary>分析一次失败 / Analyse a failure.</summary>
    /// <param name="failure">失败 / The failure.</param>
    /// <param name="cancellationToken">取消令牌 / The cancellation token.</param>
    /// <returns>执行结果 / Execution result.</returns>
    Task<Try> AnalyzeAsync(ColumnGenerationFailure failure, CancellationToken cancellationToken = default);
}

/// <summary>
/// 心跳 / The heartbeat.
/// </summary>
public interface IColumnGenerationHeartbeat
{
    /// <summary>每轮回调 / Called every iteration.</summary>
    /// <param name="state">当前状态 / The current state.</param>
    /// <param name="cancellationToken">取消令牌 / The cancellation token.</param>
    /// <returns>完成任务 / The completion task.</returns>
    Task InvokeAsync(ColumnGenerationState state, CancellationToken cancellationToken = default);
}

/// <summary>
/// 建列请求构造器 / The layer-request builder.
/// </summary>
public interface IColumnGenerationLayerRequestBuilder
{
    /// <summary>构造建列请求 / Build a layer-generation request.</summary>
    /// <param name="state">当前状态 / The current state.</param>
    /// <param name="config">配置 / The configuration.</param>
    /// <returns>建列请求 / The request.</returns>
    Bpp2dLayerGenerationRequest<FltX> Build(ColumnGenerationState state, ColumnGenerationConfig config);
}
