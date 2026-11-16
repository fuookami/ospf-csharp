// 三种装载模式的统一门面 / The facade over the three loading modes.

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Core.Solver;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<
    Fuookami.Ospf.Utils.Functional.Success,
    Fuookami.Ospf.Utils.Error.ErrorCode,
    Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Application;

/// <summary>
/// 三条装载路径的统一门面 / One facade in front of the three loading paths.
/// </summary>
/// <remarks>
/// **基线把三条 `Execute` 收在这个门面后面**（`Demo2ApplicationService.solve`），本仓的 只把三条路径分别接了线——
/// 三条各自能跑，**合起来却没有统一入口**。调用方按 `StowageMode` 分派，不该自己 `switch` 三个应用类。
///
/// **这里只做分派**，不搬基线的编排：心跳、列生成、Benders 质量守卫、`render` 都不在这里。
/// 基线那份是**一个 130 行的编排器**，硬把它的空壳搬过来只会得到"每个方法都返回未实现"的空架子——**空壳是最糟的缺口**。
/// 分派先站住，其余按基线逐条补，补一条加一条判据。
///
/// 基线里"分区列生成与 Benders 不能同时启用"这条校验**搬不了**：本仓的 `SolvePolicy` 没有 `PartitionColumnGeneration`
/// 字段（这是 `infrastructure/dto` 整域缺口的一部分）。**校验依赖的输入都不存在，写一个假校验只会骗人。**
///
/// **The baseline puts all three `Execute` paths behind this facade** (`Demo2ApplicationService.solve`); and wired them separately — each runs,
/// **but together they have no single entry point**. Callers dispatch on `StowageMode` instead of switching over three application classes themselves.
///
/// ** only dispatches**; it does not move the baseline's orchestration. Heartbeats, column generation, the Benders quality guard and `render` stay
/// out. That baseline method is **a 130-line orchestrator**, and dragging its shell over here would only produce an empty frame where every method returns
/// "not implemented" — **an empty shell is the worst kind of gap**. Dispatch first; the rest lands per baseline item, one criterion each.
///
/// The baseline's "partition column generation and Benders cannot both be enabled" check **cannot move**: this port's `SolvePolicy` has no
/// `PartitionColumnGeneration` field (part of the whole-domain `infrastructure/dto` gap). **The input the check needs does not exist, and writing a fake check
/// would only lie.**
/// </remarks>
public static class Demo2ApplicationService {
    /// <summary>
    /// 按装载模式求解 / Solve in the given loading mode.
    /// </summary>
    /// <param name="request">请求 DTO / Request DTO</param>
    /// <param name="mode">装载模式 / The stowage mode</param>
    /// <param name="milpSolver">直连 MILP 求解器（可选）/ Direct MILP solver (optional)</param>
    /// <param name="bendersSolver">Benders 求解器（可选）/ Benders solver (optional)</param>
    /// <param name="columnGenerationSolver">分区列生成求解器（可选；`partitionColumnGeneration` 为真时必须给）/
    /// Partition column-generation solver (optional; required when `partitionColumnGeneration` is set)</param>
    /// <param name="running">运行中心跳回调（可选）/ Running heartbeat callback (optional)</param>
    /// <param name="finished">收尾心跳回调（可选，成功与失败都会调用）/ Finishing heartbeat callback (optional; called on success and on failure)</param>
    /// <returns>响应 DTO 和渲染 DTO / Response DTO and render DTO</returns>
    /// <remarks>
    /// **未知模式必须明确报错，不能默认落到全配载**：默认落到某一条路径意味着"传错了模式"和"就是要那条路径"看起来一样，
    /// 而两者的后果差得很远。**默认值是最容易埋雷的地方。**
    ///
    /// **An unknown mode must fail explicitly rather than fall through to full load**: falling through makes "the caller passed the wrong mode" and "the caller
    /// wanted that path" indistinguishable, and the consequences differ a lot. **Defaults are where mines get buried.**
    /// </remarks>
    public static (ResponseDTO Response, RenderDTO? Render) Solve(
        RequestDTO request,
        StowageMode mode,
        ILinearSolver? milpSolver = null,
        Fuookami.Ospf.Framework.Solver.IBendersDecompositionSolver? bendersSolver = null,
        Fuookami.Ospf.Framework.Solver.IColumnGenerationSolver? columnGenerationSolver = null,
        Action<RunningHeartBeatDTO>? running = null,
        Action<FinnishHeartBeatDTO>? finished = null) {
        ArgumentNullException.ThrowIfNull(request);

        // **心跳回调不是装饰**：基线用它把"还在跑"与"已经结束"分开报，长跑求解没有它，调用方只能等或者超时。
        // 这里在分派前后各发一次，**与基线一致**：开跑前一次 `RunningHeartBeat`，收尾一次 `FinnishHeartBeat`。
        //
        // **The heartbeat callbacks are not decoration**: the baseline uses them to report "still running" separately from "finished", and without them a caller of
        // a long solve can only wait or time out. One is emitted before dispatch and one at the end, **matching the baseline**.
        DateTime start = DateTime.UtcNow;
        running?.Invoke(new RunningHeartBeatDTO(
            request.Id,
            TimeSpan.Zero,
            TimeSpan.Zero,
            Flt64.Zero));

        // **两条策略校验放在分派之前**：它们是"请求本身互相矛盾"，与走哪条装载路径无关。
        // 位置与基线一致（`Demo2ApplicationService.solve` 在心跳之前校验）。
        //
        // **Both policy checks run before dispatch**: they are "the request contradicts itself", independent of which loading path is taken. The position matches
        // the baseline, which checks before the running heartbeat.
        // **机型守卫在策略校验之前**：基线的顺序就是 `supportedAircraft` → 策略互斥 → 心跳。顺序不是装饰——
        // 机型不支持时"策略互斥"这件事根本轮不到讨论，先报会让人去改策略，改完还是不支持。
        //
        // **The aircraft guard comes before the policy checks**: the baseline's order is `supportedAircraft` then policy then heartbeat. The order is not
        // decoration — when the aircraft is unsupported there is no point discussing strategy exclusion: reporting that first sends the caller to change a policy that
        // will not help.
        if (!BendersStrategy.SupportedAircraft(request.AircraftType))
        {
            string path = mode switch {
                StowageMode.FullLoad => "full-load",
                StowageMode.Predistribution => "predistribution",
                _ => "weight-recommendation",
            };
            // 措辞照抄基线的 `unsupportedAircraftResponse`（中英各一句、带机型）。
            // The wording copies the baseline's `unsupportedAircraftResponse` — one sentence per language, carrying the aircraft type.
            (ResponseDTO Response, RenderDTO? Render) unsupported = (
                ResponseDTO.NoSolution(
                    "UnsupportedAircraft",
                    [$"不支持的{mode}机型：{request.AircraftType} / Unsupported aircraft type for {path} path: {request.AircraftType}"]),
                null);
            return Finish(request, unsupported, start, finished);
        }

        SolvePolicy policy = request.SolvePolicy ?? new SolvePolicy();
        if (policy.PartitionColumnGeneration && policy.PreferBenders)
        {
            // 基线的原话照抄：这不是"不支持"，是"两种策略互斥"。
            // The baseline's own wording: this is not "unsupported", the two strategies are mutually exclusive.
            (ResponseDTO Response, RenderDTO? Render) conflict = (
                ResponseDTO.NoSolution(
                    "PolicyConflict",
                    [
                        "分区列生成与 Benders 是独立策略，不能同时启用 / Partition CG and Benders are independent strategies and cannot be enabled together",
                        "partitionColumnGeneration=true,preferBenders=true",
                    ]),
                null);
            return Finish(request, conflict, start, finished);
        }
        if (policy.PartitionColumnGeneration)
        {
            // **接上了**： 那句"注册与分区配置没接起来"两半都不成立——注册一直都在，
            // 分区配置在 落地，`PartitionColumnGenerationRunner` 只是把两块交给框架层的循环。
            //
            // **仍然明确拒绝，但理由换了**：分派现在缺的是**求解器**。不给后端却跑一条列生成，只能得到一个空结果，
            // 而"没后端"与"列生成没结果"是两件事——把后者的措辞用在前者上，下一个人会去查模型。
            //
            // **Wired**: the earlier "the registration and the partition configuration are not wired" was wrong on both halves — the registration was always there and
            // the configuration landed; `PartitionColumnGenerationRunner` only hands the two to the framework loop.
            // **Still an explicit refusal, with a different reason**: what the dispatch lacks now is a **solver**. Running column generation with no backend can only
            // produce an empty result, and "no backend" and "column generation produced nothing" are different stories — using the latter's wording for the former sends
            // the next person to inspect the model.
            if (columnGenerationSolver is null)
            {
                (ResponseDTO Response, RenderDTO? Render) noSolver = (
                    ResponseDTO.NoSolution(
                        "ColumnGenerationSolverNotSupplied",
                        [
                            "分区列生成需要 IColumnGenerationSolver，本次调用没有提供 / "
                            + "partition column generation needs an IColumnGenerationSolver; this call supplied none",
                            "partitionColumnGeneration=true",
                        ]),
                    null);
                return Finish(request, noSolver, start, finished);
            }
            var columnGeneration = new FullLoadApplication();
            Try initialised = columnGeneration.Init(request);
            if (!initialised.IsOk)
            {
                (ResponseDTO Response, RenderDTO? Render) initFailed = (
                    ResponseDTO.NoSolution(
                        "InitFailed",
                        ["列生成路径的装配失败 / the column-generation path failed to assemble: " + initialised]),
                    null);
                return Finish(request, initFailed, start, finished);
            }
            var notes = new List<string>();
            (ResponseDTO Response, RenderDTO? Render) cg = columnGeneration.SolvePartitionColumnGeneration(
                columnGenerationSolver, notes, request.ResolvedParameter);
            return Finish(request, cg, start, finished);
        }
        (ResponseDTO Response, RenderDTO? Render) result = mode switch {
            StowageMode.FullLoad => new FullLoadApplication().Execute(request, bendersSolver, milpSolver),
            StowageMode.Predistribution => new PredistributionApplication().Execute(request, milpSolver),
            StowageMode.WeightRecommendation => new WeightRecommendationApplication().Execute(request, milpSolver),
            _ => (
                ResponseDTO.NoSolution(
                    "UnknownStowageMode",
                    [$"mode={mode}", "未知装载模式 / Unknown loading mode"]),
                null),
        };

        return Finish(request, result, start, finished);
    }

    /// <summary>
    /// 发收尾心跳并返回结果 / Emit the finishing heartbeat and return the outcome.
    /// </summary>
    /// <param name="request">请求 DTO / The request DTO.</param>
    /// <param name="result">响应与渲染 / The response and render.</param>
    /// <param name="start">开跑时刻 / When the run started.</param>
    /// <param name="finished">收尾回调 / The finishing callback.</param>
    /// <returns>原样返回结果 / The outcome, unchanged.</returns>
    /// <remarks>
    /// **抽出来是因为策略校验的两条提前返回也要发心跳**：校验失败和列生成未移植都是"结束了，而且失败了"，
    /// 而"只报成功的心跳等于没有心跳"。
    ///
    /// **Extracted because the two early returns need the beat too**: a policy conflict and an unported path are both "it ended, and it failed", and
    /// "heartbeats that only report success are no heartbeats at all".
    /// </remarks>
    private static (ResponseDTO Response, RenderDTO? Render) Finish(
        RequestDTO request,
        (ResponseDTO Response, RenderDTO? Render) result,
        DateTime start,
        Action<FinnishHeartBeatDTO>? finished) {
        TimeSpan elapsed = DateTime.UtcNow - start;
        if (finished is not null) {
            finished.Invoke(result.Response.Succeed
                ? FinnishHeartBeatDTO.Finished(request.Id, elapsed)
                : FinnishHeartBeatDTO.Failed(request.Id, elapsed, new Err<ErrorCode>(CodeOf(result.Response.Status), result.Response.Status)));
        }
        return result;
    }

    /// <summary>
    /// 把响应状态字符串映射回错误码 / Map a response status string back to an error code.
    /// </summary>
    /// <param name="status">响应状态 / The response status</param>
    /// <returns>错误码 / The error code</returns>
    /// <remarks>
    /// **这是一处有损映射，必须写明白**：基线在失败路径上手里有 `Error&lt;ErrorCode&gt;`，而本仓的 `ResponseDTO` 只带一个状态**字符串**——
    /// 错误码在 `Execute` 里就被折成字符串了。所以这里只能按字符串反推，认不出来的统一报 `ApplicationFailed`。
    /// 要恢复无损，得让 `ResponseDTO` 带上错误码本身（那是 `infrastructure/dto` 整域缺口的一部分）。
    ///
    /// **This is a lossy mapping and must be said out loud**: the baseline holds an `Error&lt;ErrorCode&gt;` on the failure path, while this port's `ResponseDTO` carries
    /// only a status **string** — the code is flattened inside `Execute`. So it is reverse-engineered here, and anything unrecognised reports `ApplicationFailed`.
    /// Restoring losslessness means putting the error code on `ResponseDTO` itself (part of the whole-domain `infrastructure/dto` gap).
    /// </remarks>
    private static ErrorCode CodeOf(string? status) => status switch {
        "UnknownStowageMode" or "InitFailed" or "UnsupportedAircraft" => ErrorCode.IllegalArgument,
        // **基线 `fail` 只产出三个状态**：`ORModelInfeasible` → `"Infeasible"`、
        // `OREngineSolvingException` → `"NotConverged"`、**其余 → `"Error"`**。
        // **本仓曾经自造 `DirectSolveFailed`/`DomainRegisterFailed`/`ModelBuildFailed` 三个，
        // 而基线里搜不到它们——全部产出点已改回基线的三个状态。**
        // **留着旧名字的映射会让"没人产出的字符串"看起来像一条真实分支。**
        //
        // **The baseline `fail` produces exactly three statuses**: `ORModelInfeasible` → `"Infeasible"`,
        // `OREngineSolvingException` → `"NotConverged"`, **everything else → `"Error"`**.
        // **This port once invented `DirectSolveFailed`/`DomainRegisterFailed`/`ModelBuildFailed`, which appear nowhere in the baseline —
        // Every producer now uses the baseline's three.** **Keeping the old names would make strings nobody emits look like real branches.**
        "NotConverged" => ErrorCode.OREngineSolvingException,
        _ => ErrorCode.ApplicationFailed,
    };
}
