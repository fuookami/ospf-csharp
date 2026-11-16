#nullable enable

using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.Core.Solver.Progress;

/// <summary>
/// 进度适配器：把各求解器回调适配为统一进度上报，并保留原回调。
/// Progress adapters: adapt solver callbacks to unified progress reporting while preserving the original callbacks.
/// </summary>
/// <remarks>
/// **这是 移植核心类型时漏掉的另一半**：核心类型（`SolverProgressContext` 等）只提供"怎么上报"，
/// 适配器提供"从哪里接"。没有适配器，进度上报**存在但无人调用**。
///
/// 三个适配器对应三个函数，**全部保留原回调**：适配后仍会调用 `delegate`，
/// 所以接入进度上报**不会让既有的回调失效**。
///
/// **This is the half missed when implemented the core types**: the core types only provide "how to report", while the adapters provide "where to hook in". Without
/// them progress reporting **exists but nothing calls it**.
///
/// The three adapters correspond to its three functions and **all preserve the original callback**: after adapting, `delegate` is still invoked, so
/// wiring in progress reporting **does not disable any existing callback**.
/// </remarks>
public static class SolverProgressAdapters {
    /// <summary>
    /// 将模型构建回调适配为统一进度，并保留原回调 / Adapt the model-building callback to unified progress while preserving the delegate.
    /// </summary>
    /// <param name="context">进度上下文 / Progress context.</param>
    /// <param name="delegate">原回调 / Original callback.</param>
    /// <returns>组合回调 / The composed callback.</returns>
    public static ModelBuildingStatusCallBack ModelBuildingCallback(
        this SolverProgressContext context,
        ModelBuildingStatusCallBack? @delegate = null) =>
        status => {
            int progress = Percentage(status.Ready, status.Total);
            string stageName = status.Stage.ToString();
            Result<Success, ErrorCode, Error<ErrorCode>> reported = context.Report(
                new SolverProgressSnapshot(
                    Stage: SolverStages.ModelBuilding,
                    ProgressInStage: progress,
                    OverallProgress: progress / 10,
                    SubStage: new SolverSubStage(
                        Key: stageName.ToLowerInvariant(),
                        DefaultTemplate: "模型构建：{stage}",
                        MessageKey: "i18n.ospf.substage.model_building",
                        Args: new Dictionary<string, string> { ["stage"] = stageName }),
                    Diagnostics: new Dictionary<string, string> {
                        ["model"] = status.ModelName,
                        ["ready"] = status.Ready.ToString(),
                        ["total"] = status.Total.ToString(),
                    }));

            return reported.Then(() => @delegate is null ? Results.Ok(Results.SuccessInstance) : @delegate(status));
        };

    /// <summary>
    /// 将注册回调适配为统一进度，并保留原回调 / Adapt the registration callback to unified progress while preserving the delegate.
    /// </summary>
    /// <param name="context">进度上下文 / Progress context.</param>
    /// <param name="delegate">原回调 / Original callback.</param>
    /// <returns>组合回调 / The composed callback.</returns>
    public static RegistrationStatusCallBack RegistrationCallback(
        this SolverProgressContext context,
        RegistrationStatusCallBack? @delegate = null) =>
        status => {
            int progress = Percentage(status.ReadySymbolAmount, status.TotalSymbolAmount);
            Result<Success, ErrorCode, Error<ErrorCode>> reported = context.Report(
                new SolverProgressSnapshot(
                    Stage: SolverStages.Registration,
                    ProgressInStage: progress,
                    OverallProgress: 10 + (progress / 5),
                    Diagnostics: new Dictionary<string, string> {
                        ["ready"] = status.ReadySymbolAmount.ToString(),
                        ["total"] = status.TotalSymbolAmount.ToString(),
                        ["empty"] = status.EmptySymbolAmount.ToString(),
                    }));

            return reported.Then(() => @delegate is null ? Results.Ok(Results.SuccessInstance) : @delegate(status));
        };

    /// <summary>
    /// 将求解回调适配为统一进度，并保留原回调 / Adapt the solving callback to unified progress while preserving the delegate.
    /// </summary>
    /// <param name="context">进度上下文 / Progress context.</param>
    /// <param name="stage">阶段 / Stage.</param>
    /// <param name="delegate">原回调 / Original callback.</param>
    /// <returns>组合回调 / The composed callback.</returns>
    /// <remarks>
    /// **gap 解析失败时进度取 0**（`gap.toString.toDoubleOrNull` 为 null 则 `progress = 0`）。
    /// 总体进度按 `stage.kind` 分四档，**四档的系数必须逐字对齐**——它们决定进度条在各阶段的区间。
    ///
    /// **When the gap cannot be parsed, progress is 0**, matching the code. Overall progress has four branches by `stage.kind`, and **the coefficients must match
    /// exactly** — they decide which band of the progress bar each stage occupies.
    /// </remarks>
    public static SolvingStatusCallBack SolvingCallback(
        this SolverProgressContext context,
        SolverStage? stage = null,
        SolvingStatusCallBack? @delegate = null) {
        SolverStage resolvedStage = stage ?? SolverStages.Milp;
        return status => {
            double? gapValue = ParseGap(status.Gap);
            int progress = gapValue is null
                ? 0
                : (int)((1.0 - global::System.Math.Clamp(gapValue.Value, 0.0, 1.0)) * 99.0);
            int overallProgress = resolvedStage.Kind switch {
                StageKind.MasterLP => 30 + (progress * 4 / 10),
                StageKind.ColumnGeneration => 60 + (progress * 2 / 10),
                StageKind.BranchAndPrice => 70 + (progress * 2 / 10),
                _ => 30 + (progress * 7 / 10),
            };
            Result<Success, ErrorCode, Error<ErrorCode>> reported = context.Report(
                new SolverProgressSnapshot(
                    Stage: resolvedStage,
                    ProgressInStage: progress,
                    OverallProgress: overallProgress,
                    SubStage: new SolverSubStage(
                        Key: "incumbent",
                        DefaultTemplate: "求解中（gap {gap}）",
                        MessageKey: "i18n.ospf.substage.incumbent",
                        Args: new Dictionary<string, string> { ["gap"] = status.Gap.ToString() }),
                    Diagnostics: new Dictionary<string, string> {
                        ["solver"] = status.Solver,
                        ["objective"] = status.Obj.ToString(),
                        ["bestBound"] = status.BestBound?.ToString() ?? "",
                        ["gap"] = status.Gap.ToString(),
                        ["iterations"] = status.Iterations?.ToString() ?? "",
                        ["nodes"] = status.NodeCount?.ToString() ?? "",
                    }));

            return reported.Then(() => @delegate is null ? Results.Ok(Results.SuccessInstance) : @delegate(status));
        };
    }

    /// <summary>
    /// 解析 gap 数值 / Parse the gap value.
    /// </summary>
    /// <param name="gap">gap 数值 / Gap value.</param>
    /// <returns>解析值或 null / The parsed value, or null.</returns>
    /// <remarks>
    /// **`Flt64` 有 `ToDouble`，不需要文本往返。** 首版写成"先 `ToString` 再 `TryParse`"，
    /// 那是照搬`toString.toDoubleOrNull`——但**么写是因为 Kotlin 的 `Flt64` 没有直接的
    /// double 转换**，C# 有。这里直接取值，**语义相同、少一次解析、也不会因格式设置出错**。
    ///
    /// **`Flt64` has `ToDouble`, so no text round-trip is needed.** The first version did "`ToString` then `TryParse`", copying its
    /// `toString.toDoubleOrNull` — but **the code does that because Kotlin's `Flt64` has no direct double conversion**; C# does. Taking the value directly
    /// is **semantically identical, skips a parse, and cannot break on format settings**.
    /// </remarks>
    private static double? ParseGap(Fuookami.Ospf.Math.Algebra.Number.Flt64 gap) => gap.ToDouble();

    /// <summary>
    /// 计算百分比 / Compute the percentage.
    /// </summary>
    /// <param name="ready">已完成文本 / Ready text.</param>
    /// <param name="total">总量文本 / Total text.</param>
    /// <returns>百分比 0..100 / Percentage 0..100.</returns>
    /// <remarks>
    /// **`total == 0` 时返回 100**——总量为零意味着"已经全部完成"，
    /// 而不是"进度为 0"。这一条在初始化瞬间会触发，容易写成 0 而让进度条从 0 开始跳。
    ///
    /// **Returns 100 when `total == 0`**, matching the code — a zero total means "everything is done", not "progress is zero". This fires at the first instant
    /// of initialisation and is easy to write as 0, which makes the progress bar jump from zero.
    /// </remarks>
    private static int Percentage(object? ready, object? total) {
        if (!double.TryParse(ready?.ToString(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double readyValue)) {
            return 0;
        }
        if (!double.TryParse(total?.ToString(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double totalValue)) {
            return 0;
        }
        if (totalValue == 0.0) {
            return 100;
        }
        return global::System.Math.Clamp((int)(readyValue * 100.0 / totalValue), 0, 100);
    }
}

/// <summary>
/// 进度带重映射辅助 / Progress-band remapping helpers.
/// </summary>
public static class SolverProgressBands {
    /// <summary>
    /// 进度带重映射：把子上下文的阶段内进度挤压进父级的 `[start, end]` 区间 /
    /// Progress-band remapping: squeeze a sub-context's in-stage progress into the parent's `[start, end]`.
    /// </summary>
    /// <remarks>
    /// 对齐`SolverProgressContext.phase(start, end)`：用一个新的上报器包装父上下文，
    /// 把子快照的 `overallProgress` 改写成 `start + progressInStage * (end - start) / 100`。
    ///
    /// **关键点一：重映射用的是 `progressInStage`，不是子上下文的 `overallProgress`。**
    /// 子求解器的阶段内进度（0—100）**线性映射**到父级的一个带，父级其它带不受影响。
    /// 若误用 `overallProgress`，子上下文的 0—100 会被二次挤压，结果仍然"看起来合理"却**落错带**。
    ///
    /// **关键点二：新的子上下文不传 `isCancelled`。** 转发 `reporter` 与 `locale`，
    /// 取消判定**留在父上下文**——子上下文取消时**不会自己抛**，依赖父级检查点。
    /// 这里照做，并记为**已知偏离风险**：若将来子求解器依赖 `IsCancelled`，需要在 `Phase` 里转发。
    ///
    /// Aligns with its `SolverProgressContext.phase(start, end)`: wraps the parent context with a reporter that rewrites the sub-snapshot's
    /// `overallProgress` as `start + progressInStage * (end - start) / 100`.
    ///
    /// **Key point one: the remap uses `progressInStage`, not the sub-context's `overallProgress`.** The sub-solver's in-stage progress (0—100) is **linearly mapped**
    /// onto one parent band, leaving the parent's other bands untouched. Using `overallProgress` instead would squash it twice — the result would still **look
    /// plausible** while **landing in the wrong band**.
    ///
    /// **Key point two: the new sub-context does not receive `isCancelled`.** The code forwards only `reporter` and `locale`, leaving cancellation **with the
    /// parent** — a cancelled sub-context **does not throw on its own**; that relies on the parent's checkpoints. Copied as-is and recorded as a **known divergence
    /// risk**: if a future sub-solver relies on `IsCancelled`, `Phase` must forward it.
    /// </remarks>
    /// <param name="context">父上下文 / Parent context.</param>
    /// <param name="start">区间起点（总体进度）/ Band start (overall progress).</param>
    /// <param name="end">区间终点（总体进度）/ Band end (overall progress).</param>
    /// <returns>子上下文 / The sub-context.</returns>
    public static SolverProgressContext Phase(this SolverProgressContext context, int start, int end) =>
        new(new BandRemappingReporter(context, start, end), locale: context.Locale);
}

/// <summary>
/// 把子上下文的阶段内进度重映射到父级区间的上报器 / A reporter that remaps a sub-context's in-stage progress into a parent band.
/// </summary>
internal sealed class BandRemappingReporter : IProgressReporter {
    private readonly SolverProgressContext _parent;
    private readonly int _start;
    private readonly int _end;

    /// <summary>构造 / Construct.</summary>
    /// <param name="parent">父上下文 / Parent context.</param>
    /// <param name="start">区间起点 / Band start.</param>
    /// <param name="end">区间终点 / Band end.</param>
    public BandRemappingReporter(SolverProgressContext parent, int start, int end) {
        _parent = parent;
        _start = start;
        _end = end;
    }

    /// <inheritdoc/>
    public Result<Success, ErrorCode, Error<ErrorCode>> Report(SolverProgressSnapshot snapshot) =>
        _parent.Report(snapshot with {
            OverallProgress = _start
                + (global::System.Math.Clamp(snapshot.ProgressInStage, 0, 100) * (_end - _start) / 100),
        });
}

/// <summary>
/// `Try` 的串联辅助 / A `Try` chaining helper.
/// </summary>
internal static class TryChaining {
    /// <summary>
    /// 成功时执行下一步，失败时原样返回 / Run the next step on success, return the failure unchanged.
    /// </summary>
    /// <param name="current">当前结果 / The current result.</param>
    /// <param name="next">下一步 / The next step.</param>
    /// <returns>结果 / The result.</returns>
    /// <remarks>
    /// 对齐基线 `Try.then`：**`Ok` 才执行 `next`**，`Failed` / `Fatal` 原样传播。
    /// 这条语义保证"进度上报失败时**不会**继续调用原回调"——否则一次上报失败会让下游回调
    /// 在错误的时机被调用。
    ///
    /// Aligns with the code `Try.then`: **only `Ok` runs `next`**; `Failed` / `Fatal` propagate unchanged. This semantics guarantees "when progress reporting
    /// fails, the original callback is **not** invoked" — otherwise a failed report would fire downstream callbacks at the wrong moment.
    /// </remarks>
    public static Result<Success, ErrorCode, Error<ErrorCode>> Then(
        this Result<Success, ErrorCode, Error<ErrorCode>> current,
        Func<Result<Success, ErrorCode, Error<ErrorCode>>> next) =>
        current.IsOk ? next() : current;
}
