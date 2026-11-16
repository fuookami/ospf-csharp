#nullable enable

using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Core.Solver.Progress;

/// <summary>
/// 求解阶段标识 / Solver stage identifier.
/// </summary>
/// <param name="Value">稳定阶段标识 / Stable stage identifier.</param>
public readonly record struct SolverStageId(string Value);

/// <summary>
/// 求解阶段类型 / Solver stage kind.
/// </summary>
public enum StageKind {
    /// <summary>模型构建 / Model building.</summary>
    ModelBuilding,
    /// <summary>模型注册 / Registration.</summary>
    Registration,
    /// <summary>预求解 / Presolve.</summary>
    Presolve,
    /// <summary>列生成 / Column generation.</summary>
    ColumnGeneration,
    /// <summary>分支定价 / Branch and price.</summary>
    BranchAndPrice,
    /// <summary>启发式求解 / Heuristic.</summary>
    Heuristic,
    /// <summary>主问题求解 / Master LP.</summary>
    MasterLP,
    /// <summary>整数求解 / MILP.</summary>
    Milp,
    /// <summary>结果处理 / Post processing.</summary>
    PostProcessing,
}

/// <summary>
/// 求解阶段 / Solver stage.
/// </summary>
/// <param name="Id">稳定阶段标识 / Stable stage identifier.</param>
/// <param name="DefaultLabel">简体中文默认标签 / Simplified Chinese default label.</param>
/// <param name="MessageKey">国际化键 / Localization key.</param>
/// <param name="Kind">阶段类型 / Stage kind.</param>
/// <param name="LabelOverride">调用方标签覆盖 / Caller-provided label override.</param>
public sealed record SolverStage(
    SolverStageId Id,
    string DefaultLabel,
    string MessageKey,
    StageKind Kind,
    string? LabelOverride = null) {
    /// <summary>回退标签 / Fallback label.</summary>
    public string FallbackLabel => LabelOverride ?? DefaultLabel;
}

/// <summary>
/// 求解子阶段 / Solver sub-stage.
/// </summary>
/// <param name="Key">稳定子阶段键 / Stable sub-stage key.</param>
/// <param name="DefaultTemplate">简体中文默认模板 / Simplified Chinese default template.</param>
/// <param name="MessageKey">国际化键 / Localization key.</param>
/// <param name="Args">模板参数 / Template arguments.</param>
public sealed record SolverSubStage(
    string Key,
    string DefaultTemplate,
    string MessageKey,
    IReadOnlyDictionary<string, string>? Args = null) {
    /// <summary>使用参数渲染指定模板 / Render the supplied template with arguments.</summary>
    /// <param name="template">模板文本 / Template text.</param>
    /// <returns>渲染结果 / The rendered text.</returns>
    /// <remarks>
    /// **必须逐键顺序替换，且用 `{key}` 形式**。
    /// `args.entries.fold(template) { text, (key, value) -> text.replace("{$key}", value) }`。
    ///
    /// **Keys must be substituted in order using the `{key}` form**, matching the code.
    /// </remarks>
    public string Render(string template) {
        string text = template;
        foreach (KeyValuePair<string, string> pair in Args ?? EmptyArgs) {
            text = text.Replace("{" + pair.Key + "}", pair.Value, StringComparison.Ordinal);
        }
        return text;
    }

    /// <summary>使用参数渲染默认模板 / Render the default template with arguments.</summary>
    /// <returns>渲染结果 / The rendered text.</returns>
    public string RenderDefault() => Render(DefaultTemplate);

    private static readonly IReadOnlyDictionary<string, string> EmptyArgs =
        new Dictionary<string, string>();
}

/// <summary>
/// 求解进度快照 / Solver progress snapshot.
/// </summary>
/// <param name="Stage">当前一级阶段 / Current top-level stage.</param>
/// <param name="ProgressInStage">阶段内百分比，范围 0..100 / Percentage within the stage, in 0..100.</param>
/// <param name="OverallProgress">总百分比，范围 0..100 / Overall percentage, in 0..100.</param>
/// <param name="SubStage">当前二级阶段 / Current sub-stage.</param>
/// <param name="Diagnostics">结构化诊断字段 / Structured diagnostic fields.</param>
/// <param name="ResolvedStageLabel">已解析阶段标签 / Resolved stage label.</param>
/// <param name="ResolvedSubStageLabel">已解析子阶段标签 / Resolved sub-stage label.</param>
public sealed record SolverProgressSnapshot(
    SolverStage Stage,
    int ProgressInStage,
    int OverallProgress,
    SolverSubStage? SubStage = null,
    IReadOnlyDictionary<string, string>? Diagnostics = null,
    string? ResolvedStageLabel = null,
    string? ResolvedSubStageLabel = null) {
    /// <summary>将百分比收敛到合法范围 / Clamp percentages to the valid range.</summary>
    /// <returns>收敛后的快照 / The clamped snapshot.</returns>
    /// <remarks>
    /// `ResolvedStageLabel` / `ResolvedSubStageLabel` 为 null 时**按默认值补上**：
    /// 基线这两个属性的默认值是 `stage.fallbackLabel` 与 `subStage?.renderDefault`，
    /// 而 C# 的可选参数不能引用其它参数，所以在属性访问处补。
    ///
    /// When null, **the code defaults are supplied**: the code defaults these to `stage.fallbackLabel` and `subStage?.renderDefault`, but C# optional
    /// parameters cannot reference other parameters, so it is done at the property accessor instead.
    /// </remarks>
    public SolverProgressSnapshot Normalized() => this with {
        ProgressInStage = global::System.Math.Clamp(ProgressInStage, 0, 100),
        OverallProgress = global::System.Math.Clamp(OverallProgress, 0, 100),
    };

    /// <summary>生效的阶段标签 / The effective stage label.</summary>
    public string EffectiveStageLabel => ResolvedStageLabel ?? Stage.FallbackLabel;

    /// <summary>生效的子阶段标签 / The effective sub-stage label.</summary>
    public string? EffectiveSubStageLabel => ResolvedSubStageLabel ?? SubStage?.RenderDefault();
}

/// <summary>
/// 进度标签解析器，由上层本地化系统实现 / Progress label resolver implemented by the caller localization system.
/// </summary>
public interface IProgressLabelResolver {
    /// <summary>解析指定语言的文本 / Resolve text for the requested locale.</summary>
    /// <param name="key">稳定国际化键 / Stable localization key.</param>
    /// <param name="defaultText">简体中文默认文本 / Simplified Chinese default text.</param>
    /// <param name="locale">语言标签 / Locale tag.</param>
    /// <returns>已解析文本 / The resolved text.</returns>
    string Resolve(string key, string defaultText, string locale);
}

/// <summary>
/// 求解进度报告器 / Solver progress reporter.
/// </summary>
public interface IProgressReporter {
    /// <summary>上报进度快照 / Report a progress snapshot.</summary>
    /// <param name="snapshot">进度快照 / The progress snapshot.</param>
    /// <returns>成功或错误 / Success or an error.</returns>
    Result<Success, ErrorCode, Error<ErrorCode>> Report(SolverProgressSnapshot snapshot);
}

/// <summary>
/// 进度上报上下文 / Progress reporting context.
/// </summary>
/// <remarks>
/// **`SolverProgressContext` 与 `MonotonicProgressState` 是两个类型**（后者是 private class），
/// C# 侧合并成一个类：单调状态是纯实现细节，拆开只会多一个文件而没有调用方能用到它。
/// 行为逐条对齐——见 `Report` 的注释。
///
/// **`SolverProgressContext` and `MonotonicProgressState` are two types** (the latter a private class). The C# side merges them: the monotonic state
/// is a pure implementation detail, and splitting it out would add a file nobody can call. Behaviour is aligned item by item — see the notes on `Report`.
/// </remarks>
public sealed class SolverProgressContext {
    private readonly object _gate = new();
    private readonly Dictionary<string, int> _stageProgress = new(StringComparer.Ordinal);
    private int _lastOverallProgress;

    /// <summary>
    /// 构造进度上报上下文 / Construct a progress reporting context.
    /// </summary>
    /// <param name="reporter">报告器 / Reporter.</param>
    /// <param name="labelResolver">标签解析器 / Label resolver.</param>
    /// <param name="locale">语言标签 / Locale tag.</param>
    /// <param name="isCancelled">协作式取消判定 / Cooperative cancellation predicate.</param>
    public SolverProgressContext(
        IProgressReporter reporter,
        IProgressLabelResolver? labelResolver = null,
        string locale = "zh-CN",
        Func<bool>? isCancelled = null) {
        Reporter = reporter;
        LabelResolver = labelResolver;
        Locale = locale;
        IsCancelled = isCancelled ?? (static () => false);
    }

    /// <summary>报告器 / Reporter.</summary>
    public IProgressReporter Reporter { get; }

    /// <summary>标签解析器 / Label resolver.</summary>
    public IProgressLabelResolver? LabelResolver { get; }

    /// <summary>语言标签 / Locale tag.</summary>
    public string Locale { get; }

    /// <summary>协作式取消判定 / Cooperative cancellation predicate.</summary>
    public Func<bool> IsCancelled { get; }

    /// <summary>
    /// 在可中断边界检查取消状态 / Check cancellation state at an interruptible boundary.
    /// </summary>
    /// <remarks>
    /// 基线抛 `CancellationException`；C# 的对应物是 `OperationCanceledException`，
    /// 它也是 `CancellationToken.ThrowIfCancellationRequested` 抛出的同一类型——
    /// **这样两套取消机制对调用方是同一种异常**。
    ///
    /// The code throws `CancellationException`; the C# counterpart is `OperationCanceledException`, the same type
    /// `CancellationToken.ThrowIfCancellationRequested` throws — **so both cancellation mechanisms surface as one exception type to callers**.
    /// </remarks>
    public void ThrowIfCancelled() {
        if (IsCancelled()) {
            throw new OperationCanceledException("Solver execution cancelled");
        }
    }

    /// <summary>
    /// 解析文本并上报快照 / Resolve labels and report the snapshot.
    /// </summary>
    /// <param name="snapshot">进度快照 / The progress snapshot.</param>
    /// <returns>成功或错误 / Success or an error.</returns>
    /// <remarks>
    /// **顺序必须**：先 `throwIfCancelled`，再解析标签，最后过单调状态后上报。
    ///
    /// 三处细节逐条对齐：
    /// 1. **取消检查在最前**——已取消时**一次都不上报**，也不做本地化。
    /// 2. 无解析器时**直接 `Normalized`**，不清空已解析标签。
    /// 3. 有解析器时**覆盖**两个已解析标签，然后 `Normalized`。
    ///
    /// **The order must match the code**: `ThrowIfCancelled` first, then label resolution, then through the monotonic state before reporting.
    ///
    /// Three details aligned item by item: (1) **the cancellation check comes first** — when cancelled, **nothing is reported** and no localization happens;
    /// (2) without a resolver, call `Normalized` directly and do not clear the already-resolved labels; (3) with a resolver, **overwrite** both resolved labels and
    /// then `Normalized`.
    /// </remarks>
    public Result<Success, ErrorCode, Error<ErrorCode>> Report(SolverProgressSnapshot snapshot) {
        ThrowIfCancelled();
        SolverProgressSnapshot resolved;
        if (LabelResolver is null) {
            resolved = snapshot.Normalized();
        } else {
            string stageLabel = LabelResolver.Resolve(
                snapshot.Stage.MessageKey,
                snapshot.Stage.FallbackLabel,
                Locale);
            string? subStageLabel = null;
            if (snapshot.SubStage is not null) {
                string template = LabelResolver.Resolve(
                    snapshot.SubStage.MessageKey,
                    snapshot.SubStage.DefaultTemplate,
                    Locale);
                subStageLabel = snapshot.SubStage.Render(template);
            }
            resolved = (snapshot with {
                ResolvedStageLabel = stageLabel,
                ResolvedSubStageLabel = subStageLabel,
            }).Normalized();
        }
        return Reporter.Report(AcceptMonotonic(resolved));
    }

    /// <summary>
    /// 在同一求解上下文内保持阶段内及总体进度单调 / Keep in-stage and overall progress monotonic within one solve context.
    /// </summary>
    /// <param name="snapshot">进度快照 / The progress snapshot.</param>
    /// <returns>单调化后的快照 / The monotonised snapshot.</returns>
    /// <remarks>
    /// **进度只增不减**：阶段内取 `max(上次该阶段值, 本次值)`，总体取 `max(上次总体, 本次)`，
    /// 都收敛到 `0..100`。`@Synchronized`，C# 用 `lock`。
    ///
    /// **Progress never decreases**: in-stage takes `max(last for that stage, current)`, overall takes `max(last overall, current)`, both clamped to `0..100`.
    /// The code uses `@Synchronized`; C# uses `lock`.
    /// </remarks>
    private SolverProgressSnapshot AcceptMonotonic(SolverProgressSnapshot snapshot) {
        lock (_gate) {
            string stageId = snapshot.Stage.Id.Value;
            int previousInStage = _stageProgress.TryGetValue(stageId, out int stored) ? stored : 0;
            int inStage = global::System.Math.Clamp(global::System.Math.Max(previousInStage, snapshot.ProgressInStage), 0, 100);
            int overall = global::System.Math.Clamp(global::System.Math.Max(_lastOverallProgress, snapshot.OverallProgress), 0, 100);
            _stageProgress[stageId] = inStage;
            _lastOverallProgress = overall;
            return snapshot with {
                ProgressInStage = inStage,
                OverallProgress = overall,
            };
        }
    }
}

/// <summary>
/// OSPF 内置求解阶段目录 / Built-in OSPF solver stage catalog.
/// </summary>
public static class SolverStages {
    /// <summary>模型构建 / Model building.</summary>
    public static readonly SolverStage ModelBuilding = Make("MODEL_BUILDING", "模型构建", StageKind.ModelBuilding);
    /// <summary>模型注册 / Registration.</summary>
    public static readonly SolverStage Registration = Make("REGISTRATION", "模型注册", StageKind.Registration);
    /// <summary>预求解 / Presolve.</summary>
    public static readonly SolverStage Presolve = Make("PRESOLVE", "预求解", StageKind.Presolve);
    /// <summary>列生成 / Column generation.</summary>
    public static readonly SolverStage ColumnGeneration = Make("COLUMN_GENERATION", "列生成", StageKind.ColumnGeneration);
    /// <summary>分支定价 / Branch and price.</summary>
    public static readonly SolverStage BranchAndPrice = Make("BRANCH_AND_PRICE", "分支定价", StageKind.BranchAndPrice);
    /// <summary>启发式求解 / Heuristic.</summary>
    public static readonly SolverStage Heuristic = Make("HEURISTIC", "启发式求解", StageKind.Heuristic);
    /// <summary>主问题求解 / Master LP.</summary>
    public static readonly SolverStage MasterLp = Make("MASTER_LP", "主问题求解", StageKind.MasterLP);
    /// <summary>整数求解 / MILP.</summary>
    public static readonly SolverStage Milp = Make("MILP", "整数求解", StageKind.Milp);
    /// <summary>结果处理 / Post processing.</summary>
    public static readonly SolverStage PostProcessing = Make("POST_PROCESSING", "结果处理", StageKind.PostProcessing);

    /// <summary>全部内置阶段 / All built-in stages.</summary>
    public static IReadOnlyList<SolverStage> Entries { get; } = new List<SolverStage> {
        ModelBuilding,
        Registration,
        Presolve,
        ColumnGeneration,
        BranchAndPrice,
        Heuristic,
        MasterLp,
        Milp,
        PostProcessing,
    };

    private static SolverStage Make(string id, string label, StageKind kind) =>
        new(
            Id: new SolverStageId(id),
            DefaultLabel: label,
            MessageKey: "i18n.ospf.stage." + id,
            Kind: kind);
}
