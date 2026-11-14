#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Core.Solver.Report;

/// <summary>
/// 批量实验场景 / Batch experiment case.
/// </summary>
/// <remarks>
/// **B1257 按基线 `OfflineExperiment.kt` 的 `SolveExperimentCase` 补。**
///
/// **这一块是本仓此前<b>整块缺失</b>的：基线有 `SolveExperiment*`／`BatchSolveExperiment*`，
/// 本仓一个文件都没有。** **而它依赖的 `SolveReport`／`SolveIssue`／`SolveIssueCategory`／
/// `AuditFingerprint`／`SolverProvenance` 全都在——所以缺的是这一层编排，不是底座。**
///
/// **B1257 adds it from the baseline's `SolveExperimentCase`.** **The whole block was absent here: the baseline has
/// `SolveExperiment*`/`BatchSolveExperiment*` and this repo had no file for either.** **Every dependency
/// (`SolveReport`, `SolveIssue`, `SolveIssueCategory`, `AuditFingerprint`, `SolverProvenance`) already exists — what was
/// missing is this orchestration layer, not the foundation.**
///
/// **`Seed`／`GeneratorVersion`／两个指纹都是可空的：** **它们是审计元数据，而不是运行输入；
/// 一个手工构造的场景没有种子是正常的。**
///
/// **`Seed`, `GeneratorVersion` and the two fingerprints are nullable because they are audit metadata, not run inputs: a
/// hand-built case legitimately has no seed.**
/// </remarks>
/// <typeparam name="I">场景输入类型 / The case input type.</typeparam>
/// <param name="Id">场景标识 / The case identifier.</param>
/// <param name="Input">场景输入 / The case input.</param>
/// <param name="Seed">随机种子 / The random seed.</param>
/// <param name="GeneratorVersion">生成器版本 / The generator version.</param>
/// <param name="ModelFingerprint">模型指纹 / The model fingerprint.</param>
/// <param name="ConfigurationFingerprint">配置指纹 / The configuration fingerprint.</param>
public sealed record SolveExperimentCase<I>(
    string Id,
    I Input,
    long? Seed = null,
    string? GeneratorVersion = null,
    AuditFingerprint? ModelFingerprint = null,
    AuditFingerprint? ConfigurationFingerprint = null);

/// <summary>
/// 单个实验结果 / A single experiment result.
/// </summary>
/// <remarks>
/// **成功与失败是<b>同一个记录的两个字段</b>，而不是两种结果类型。**
///
/// **理由很实际：** **批量报告要能完整列出所有场景——包括失败的。
/// 如果用 `Result` 包住整条记录，那么"失败"就无法与"没有这个场景"区分。**
///
/// **Success and failure are <b>two fields of one record</b> rather than two result types, because a batch report must list
/// every case including the failed ones.** **Wrapping the record in a `Result` would make "failed" indistinguishable from
/// "this case does not exist".**
/// </remarks>
/// <typeparam name="V">数值类型 / The numeric type.</typeparam>
/// <param name="CaseId">场景标识 / The case identifier.</param>
/// <param name="Report">求解报告，失败时为 <c>null</c> / The report, null on failure.</param>
/// <param name="Errors">失败原因 / Why it failed.</param>
public sealed record SolveExperimentResult<V>(
    string CaseId,
    SolveReport<V>? Report,
    IReadOnlyList<SolveIssue> Errors)
    where V : struct {
    /// <summary>是否成功 / Whether it succeeded.</summary>
    public bool IsSuccess => Report is not null;
}

/// <summary>
/// 批量实验报告 / The batch experiment report.
/// </summary>
/// <remarks>
/// **`ConcurrencyBudget` 存的是<b>实际生效</b>的那个值，不是调用方传进来的那个。**
///
/// **传 0 或负数会被抬到 1，而报告里记录的是抬过之后的数——**
/// **否则读报告的人会以为"这次真的并行跑了 0 个"。**
///
/// **`ConcurrencyBudget` holds the <b>effective</b> value, not the one the caller passed.** **A zero or negative budget is
/// raised to 1, and the report records the raised number — otherwise a reader would believe "this run really used 0
/// concurrent slots".**
/// </remarks>
/// <typeparam name="V">数值类型 / The numeric type.</typeparam>
/// <param name="SchemaVersion">报告 schema 版本 / The report schema version.</param>
/// <param name="ConcurrencyBudget">实际生效的并发上限 / The effective concurrency budget.</param>
/// <param name="Results">按场景标识排序的结果 / The results, ordered by case id.</param>
public sealed record BatchSolveExperimentReport<V>(
    string SchemaVersion,
    int ConcurrencyBudget,
    IReadOnlyList<SolveExperimentResult<V>> Results)
    where V : struct;

/// <summary>
/// 生成式 MILP 样本的审计元数据 / Audit metadata for a generated MILP sample.
/// </summary>
/// <typeparam name="V">数值类型 / The numeric type.</typeparam>
/// <param name="GeneratorId">生成器标识 / The generator identifier.</param>
/// <param name="GeneratorVersion">生成器版本 / The generator version.</param>
/// <param name="Seed">随机种子 / The random seed.</param>
/// <param name="ModelFingerprint">模型指纹 / The model fingerprint.</param>
/// <param name="ConfigurationFingerprint">配置指纹 / The configuration fingerprint.</param>
/// <param name="SolverFingerprint">求解器指纹 / The solver fingerprint.</param>
/// <param name="Provenance">来源信息 / The provenance.</param>
public sealed record GeneratedMilpSampleMetadata<V>(
    string GeneratorId,
    string GeneratorVersion,
    long Seed,
    AuditFingerprint ModelFingerprint,
    AuditFingerprint ConfigurationFingerprint,
    AuditFingerprint? SolverFingerprint = null,
    SolverProvenance? Provenance = null)
    where V : struct;

/// <summary>
/// 离线学习观测：不携带生产求解控制指令 / An offline learning observation, carrying no production solve controls.
/// </summary>
/// <remarks>
/// **"不携带生产求解控制指令"这句话是基线写在类型上的约束，不是一个偶然的性质。**
/// **它意味着这个类型只能拿来<b>看</b>，不能拿来<b>指挥</b>下一次求解——
/// 而一旦它被读进配置，那条边界就没了。**
///
/// **本仓没有任何地方消费它（B1257 只把它定义出来），所以这条约束现在是靠"没有消费者"成立的。
/// 而在有消费者那一天，必须先回答"它能不能影响配置"这个问题。**
///
/// **"Carrying no production solve controls" is a constraint the baseline states on the type, not an accident.** **It
/// means the type may only be <b>observed</b>, never used to <b>drive</b> another solve — and the moment it is read into a
/// configuration that boundary is gone.**
///
/// **Nothing in this repo consumes it yet (B1257 only defines it), so the constraint currently holds by "no consumer
/// exists". The day one appears, "may it influence configuration" must be answered first.**
/// </remarks>
/// <typeparam name="V">数值类型 / The numeric type.</typeparam>
/// <param name="Sample">样本元数据 / The sample metadata.</param>
/// <param name="ProblemStatus">问题状态 / The problem status.</param>
/// <param name="TerminationReason">终止原因 / The termination reason.</param>
/// <param name="Objective">目标值 / The objective value.</param>
/// <param name="BestBound">最好界 / The best bound.</param>
/// <param name="Gap">间隙 / The gap.</param>
/// <param name="Features">特征 / The features.</param>
/// <param name="Labels">标签 / The labels.</param>
public sealed record OfflineSolveObservation<V>(
    GeneratedMilpSampleMetadata<V> Sample,
    ProblemStatus ProblemStatus,
    TerminationReason TerminationReason,
    V? Objective = null,
    V? BestBound = null,
    V? Gap = null,
    IReadOnlyDictionary<string, string>? Features = null,
    IReadOnlyDictionary<string, string>? Labels = null)
    where V : struct;

/// <summary>
/// 纯输入输出的批量实验运行器 / A pure input-output batch experiment runner.
/// </summary>
/// <remarks>
/// B1257 按基线 `BatchSolveExperimentRunner` 补。
///
/// **两条确定性保证是这一块的全部价值，而它们都写在签名之外的约定里：**
///
/// **① 场景按 `Id` <b>排序后</b>调度**——于是输入列表的顺序不影响任何东西；
/// **② 结果按 `Id` <b>排序后</b>返回**——于是并发程度不影响报告的顺序。**
///
/// **第二条尤其重要：** **如果只排调度而不排返回，那么一份"按完成顺序"的报告在并发 > 1 时
/// 每次都不一样，而它看起来完全正常——只是一个不可复现的报告。**
///
/// **失败不抛：** **一个场景失败会变成一条带 `SolveIssue` 的结果，
/// 其余场景继续跑完。** **批量实验里"一个坏样本中断整批"是最坏的行为。**
///
/// B1257 adds it from the baseline's `BatchSolveExperimentRunner`.
///
/// **Two determinism guarantees are the whole point of this layer, and both live in conventions outside the signature: cases
/// are scheduled <b>after sorting by `Id`</b>, and results are returned <b>after sorting by `Id`</b>.**
///
/// **The second matters especially: sorting only the schedule yields a completion-ordered report that differs run to run
/// at concurrency &gt; 1, while looking perfectly normal — merely not reproducible.**
///
/// **Failures do not throw: one failing case becomes a result carrying a `SolveIssue` and the rest finish.** **In batch
/// experiments, "one bad sample aborts the batch" is the worst possible behaviour.**
/// </remarks>
public static class BatchSolveExperimentRunner
{
    /// <summary>当前报告 schema 版本 / The current report schema version.</summary>
    public const string CurrentSchemaVersion = "1.0";

    /// <summary>
    /// **运行一次确定性批量实验 / Run one deterministic batch experiment.**
    /// </summary>
    /// <typeparam name="I">场景输入类型 / The case input type.</typeparam>
    /// <typeparam name="V">数值类型 / The numeric type.</typeparam>
    /// <param name="cases">实验场景 / The experiment cases.</param>
    /// <param name="concurrencyBudget">最大并发数，小于 1 时按 1 处理 / Maximum concurrency; below 1 is treated as 1.</param>
    /// <param name="executor">无副作用场景执行器 / The side-effect-free case executor.</param>
    /// <param name="cancellationToken">取消令牌 / The cancellation token.</param>
    /// <returns>结构化实验报告 / The structured experiment report.</returns>
    public static async Task<BatchSolveExperimentReport<V>> RunAsync<I, V>(
        IReadOnlyList<SolveExperimentCase<I>> cases,
        int concurrencyBudget,
        Func<SolveExperimentCase<I>, CancellationToken, Task<Result<SolveReport<V>, ErrorCode, Error<ErrorCode>>>> executor,
        CancellationToken cancellationToken = default)
        where V : struct {
        ArgumentNullException.ThrowIfNull(cases);
        ArgumentNullException.ThrowIfNull(executor);

                // **`Math` 要写全名：本仓有 `Fuookami.Ospf.Math` 命名空间，裸写会被它抢走。**
        // **`Math` needs its full name: this repo has a `Fuookami.Ospf.Math` namespace that captures the bare name.**
        int effectiveBudget = System.Math.Max(1, concurrencyBudget);
        // **按 `Id` 用序数排序：** **区域文化相关的排序会让同一批场景在不同机器上有不同顺序，
        // 而那是"确定性"这个词最容易被破坏的地方。**
        // **Ordinal by `Id`: a culture-sensitive sort would order the same batch differently on different machines, which is
        // the easiest way for the word "deterministic" to quietly stop being true.**
        List<SolveExperimentCase<I>> ordered = [.. cases.OrderBy(static item => item.Id, StringComparer.Ordinal)];

        var results = new SolveExperimentResult<V>[ordered.Count];
        using var semaphore = new SemaphoreSlim(effectiveBudget, effectiveBudget);
        var tasks = new Task[ordered.Count];
        for (int index = 0; index < ordered.Count; ++index) {
            int slot = index;
            tasks[slot] = RunOneAsync(ordered[slot], slot);
        }
        await Task.WhenAll(tasks).ConfigureAwait(false);
        // **不再对结果做一次排序：** **结果按"已排序列表里的槽位"写入，所以它<b>必然</b>有序。
        // 初稿里这里还有一次 `OrderBy`，那是冗余的——而冗余的那一次会让"顺序保证"看起来依赖它，
        // 于是真正该被保护的机制（槽位对应）反而没人看。**
        //
        // **No second sort over the results: they are written by slot in the <b>already ordered</b> list, so they are
        // necessarily ordered.** **The first draft had an `OrderBy` here, which was redundant — and redundancy makes the
        // ordering look like it depends on that line, hiding the mechanism that actually provides it (slot correspondence).**

        return new BatchSolveExperimentReport<V>(
            SchemaVersion: CurrentSchemaVersion,
            ConcurrencyBudget: effectiveBudget,
            Results: results);

        async Task RunOneAsync(SolveExperimentCase<I> item, int slot) {
            await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
            try {
                Result<SolveReport<V>, ErrorCode, Error<ErrorCode>> outcome =
                    await executor(item, cancellationToken).ConfigureAwait(false);
                results[slot] = outcome switch {
                    Ok<SolveReport<V>, ErrorCode, Error<ErrorCode>> ok =>
                        new SolveExperimentResult<V>(item.Id, ok.Value, []),
                    Failed<SolveReport<V>, ErrorCode, Error<ErrorCode>> failed => FailedResult<V>(item.Id, failed.Error),
                    Fatal<SolveReport<V>, ErrorCode, Error<ErrorCode>> fatal => FatalResult<V>(item.Id, fatal.Errors),
                    _ => new SolveExperimentResult<V>(item.Id, null, []),
                };
            }
            finally {
                semaphore.Release();
            }
        }
    }

    private static SolveExperimentResult<V> FailedResult<V>(
        string caseId,
        Error<ErrorCode> error)
        where V : struct =>
        new(caseId, null, [new SolveIssue(error.Code.ToString(), SolveIssueCategory.Backend, error.Message)]);

    private static SolveExperimentResult<V> FatalResult<V>(
        string caseId,
        IReadOnlyList<Error<ErrorCode>> errors)
        where V : struct {
        var issues = new List<SolveIssue>(errors.Count);
        foreach (Error<ErrorCode> error in errors) {
            issues.Add(new SolveIssue(error.Code.ToString(), SolveIssueCategory.Backend, error.Message));
        }
        return new SolveExperimentResult<V>(caseId, null, issues);
    }
}
