#nullable enable

using Fuookami.Ospf.Core.Model.ConstraintProgramming;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;

namespace Fuookami.Ospf.Core.Analysis;

/// <summary>
/// 冲突分析的前置门控 / The preflight gates for conflict analysis.
/// </summary>
/// <remarks>
/// **这是 `ConflictAnalyzer.analyze(session, target, options)` 前三道门控的移植。**
///
/// 三道门的顺序是**刻意**的：
///
/// | 序 | 门 | 为什么排在这里 |
/// | --- | --- | --- |
/// | 1 | 会话本身无效 | 身份都错了，后面每一步都无意义 |
/// | 2 | **会话已关闭** | **必须在读缓存之前** |
/// | 3 | 选项非法 | 必须在动任何求解之前 |
///
/// **第 2 道的位置是本文件的全部价值。** `session.cached(...)` **之前**查 `isClosed`。
/// 把缓存读放在关闭检查之前，一个已关闭的会话**仍然会返回上一次的结论**——
/// **于是"关闭"这件事对调用方完全不生效**，而它本该是"不要再给我东西了"。
///
/// **The position of gate 2 is the whole point of this file.** The code checks `isClosed` **before** `session.cached(...)`. Reading the cache first means a closed session **still returns the previous
/// conclusion** — **making "closed" a no-op for the caller**, when it should mean "stop giving me things".
/// </remarks>
public static class ConflictAnalysisPreflight
{
    /// <summary>
    /// 判断能否继续 / Decide whether the analysis may proceed.
    /// </summary>
    /// <param name="session">分析会话 / The analysis session.</param>
    /// <param name="options">选项 / The options.</param>
    /// <returns>拒绝时的错误，放行时为 <c>null</c> / The refusal, or <c>null</c> to proceed.</returns>
    public static Err<ErrorCode>? Check(
        CriticalConstraintAnalysisSession session,
        ConflictAnalysisOptions options) {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(options);

        if (!session.Validation.IsOk) {
            return new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "冲突分析会话无效 / Conflict analysis session is invalid");
        }

        // **关闭检查必须在读缓存之前。** / **The closed check has to come before any cache read.**
        if (session.IsClosed) {
            return new Err<ErrorCode>(
                ErrorCode.ApplicationStopped,
                "冲突分析会话已关闭 / Conflict analysis session is closed");
        }

        if (options.Validate() is { } problem) {
            return new Err<ErrorCode>(ErrorCode.IllegalArgument, problem);
        }

        return null;
    }

    /// <summary>
    /// 把前置门控的结果转成结果类型 / Turn a preflight verdict into the result type.
    /// </summary>
    /// <typeparam name="T">成功时的值类型 / The success value type.</typeparam>
    /// <param name="refusal">前置门控的拒绝，可为 <c>null</c> / The refusal, or <c>null</c>.</param>
    /// <returns>拒绝结果，或 <c>null</c> 表示继续 / The refusal result, or <c>null</c> to continue.</returns>
    /// <remarks>
    /// **"已关闭"与"选项非法"必须给出不同的错误码。** 调用方对它们的处置不同：
    /// 前者该换个会话，后者该改选项。**合成一个 `IllegalArgument` 会让前者被当成后者。**
    ///
    /// **"Closed" and "invalid options" must carry different error codes.** The caller reacts differently: the first needs another session, the second needs different options. **Merging them into one
    /// `IllegalArgument` makes the first look like the second.**
    /// </remarks>
    public static Result<T, ErrorCode, Error<ErrorCode>>? Refuse<T>(Err<ErrorCode>? refusal) =>
        refusal is null
            ? null
            : Results.Failed<T>(refusal);

    /// <summary>
    /// 快照级的前置门控 / The snapshot-level preflight.
    /// </summary>
    /// <param name="snapshot">模型快照 / The model snapshot.</param>
    /// <param name="options">选项 / The options.</param>
    /// <param name="matrix">能力矩阵 / The capability matrix.</param>
    /// <returns>拒绝的原因，或可用的抽取层次 / The refusal, or the usable extraction tier.</returns>
    /// <remarks>
    /// **这是 `ConflictAnalyzer.analyze(snapshot, target, options, capabilityMatrix)` 前三道门控的移植。**
    ///
    /// **顺序不能换：先查选项，再查快照，最后才看能力层次。**
    ///
    /// | 顺序 | 一个坏快照 + 一个不可用的后端会报 | 调用方去修 |
    /// | --- | --- | --- |
    /// | 选项 → 快照 → 层次（基线） | **`IllegalArgument`（快照坏了）** | 修快照 |
    /// | 层次先行 | `Unsupported`（后端不支持） | **换一个根本不是问题的后端** |
    ///
    /// **The order cannot change: options, then snapshot, then the tier.** A broken snapshot with an unavailable backend reports `IllegalArgument` under the code order and `Unsupported` under a
    /// tier-first order — **and the second one sends the caller to swap a backend that was never the problem.**
    ///
    /// **"层次不可用"是三种结果里唯一不返回错误的**：它返回一份 `Unsupported` 解释，
    /// **因为"这个求解器读不出冲突"本身就是一个合法的分析结论。**
    ///
    /// **"Tier unavailable" is the one outcome that is not an error**: it yields an `Unsupported` explanation, **because "this solver cannot read out conflicts" is itself a legitimate analysis result.**
    /// </remarks>
    public static ConflictPreflightResult SnapshotPreflight(
        ConstraintProgrammingModelSnapshot snapshot,
        ConflictAnalysisOptions options,
        CapabilityMatrix matrix) {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(matrix);

        if (options.Validate() is { } problem) {
            return ConflictPreflightResult.Refused(new Err<ErrorCode>(ErrorCode.IllegalArgument, problem));
        }

        if (!snapshot.Validate().IsOk) {
            return ConflictPreflightResult.Refused(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "CP snapshot 身份或目标语义无效 / CP snapshot identity or objective semantics is invalid"));
        }

        return ConflictPreflightResult.WithTier(ConflictExtractionTiers.Select(matrix));
    }
}

/// <summary>
/// 快照级前置门控的结果 / The outcome of the snapshot-level preflight.
/// </summary>
/// <param name="Refusal">拒绝的原因，放行时为 <c>null</c> / The refusal, or <c>null</c>.</param>
/// <param name="Tier">可用的抽取层次，拒绝时为 <c>null</c> / The usable tier, or <c>null</c>.</param>
public readonly record struct ConflictPreflightResult(
    Err<ErrorCode>? Refusal,
    ConflictExtractionTier? Tier)
{
    /// <summary>构造一个拒绝 / Build a refusal.</summary>
    /// <param name="refusal">原因 / The reason.</param>
    /// <returns>拒绝结果 / A refusal outcome.</returns>
    public static ConflictPreflightResult Refused(Err<ErrorCode> refusal) => new(refusal, null);

    /// <summary>构造一个放行 / Build a pass.</summary>
    /// <param name="tier">选中的层次 / The selected tier.</param>
    /// <returns>放行结果 / A passing outcome.</returns>
    public static ConflictPreflightResult WithTier(ConflictExtractionTier tier) => new(null, tier);

    /// <summary>
    /// 是否因为这个求解器读不出冲突而放行 / Whether it passed because this solver cannot read conflicts.
    /// </summary>
    /// <remarks>
    /// **这是一个"放行"而不是"拒绝"**：调用方应当拿到一份 `Unsupported` 解释，**而不是一个错误。**
    ///
    /// **This is a pass, not a refusal**: the caller should get an `Unsupported` explanation, **not an error.**
    /// </remarks>
    public bool TierUnavailable => Refusal is null && Tier is { } tier && !tier.IsAvailable();
}
