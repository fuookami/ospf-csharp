// 运行检查点 / Run checkpoints.
// 对应固定 Kotlin （47241 B）里 `HyperHeuristicRun.checkpoint` / `restore(saved)` / `exportPolicy` / `restorePolicy(saved)`。
// Mirrors `HyperHeuristicRun.checkpoint` / `restore(saved)` / `exportPolicy` / `restorePolicy(saved)`'s
// (47241 B).

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Fuookami.Ospf.Core.Solver.Heuristic.Configuration;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator;
using Fuookami.Ospf.Core.Solver.Heuristic.Persistence;
using Fuookami.Ospf.Core.Solver.Heuristic.Scheduling;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Core.Solver.Heuristic;

/// <summary>
/// 运行的检查点 / Run checkpoints.
/// </summary>
/// <remarks>
/// **检查点里的 `transitions` 是"已发生但尚未喂给策略的转移"，不是全部历史**。把全部历史写进去，恢复后会把同一批转移再喂一遍，
/// **策略的计数翻倍而搜索轨迹看起来完全正常**。
///
/// 而本实现里转移在 `Advance` 内**当场**交给 `Merge` 并落进 `Transitions`——**并没有"待喂"队列**。
/// 因此检查点保存的是**已观测的转移记录**（供导出/训练用），恢复时**绝不重新 `Observe`**；策略状态由 `PolicyArchive` 恢复。
/// 这两者的区别正是条注释想防的事。
///
/// **The `transitions` in a checkpoint are "transitions that happened but have not yet been fed to the policy", not the whole history.** Writing
/// the whole history makes restore feed the same batch again, **doubling the policy's counts while the search trajectory looks normal**.
///
/// In this implementation a transition goes to `Merge` and into `Transitions` **immediately** inside `Advance` — there is **no "pending" queue**. So the
/// checkpoint stores the **already-observed transition log** (for export and training), and restore **never calls `Observe` again**; the policy state
/// comes back from `PolicyArchive`. That distinction is exactly what its comment is guarding.
/// </remarks>
/// <typeparam name="O">目标类型 / The objective type.</typeparam>
/// <typeparam name="V">值类型 / The value type.</typeparam>
public static class HyperHeuristicSnapshots<O, V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    /// <summary>
    /// 捕获检查点 / Capture a checkpoint.
    /// </summary>
    /// <param name="run">运行 / The run.</param>
    /// <param name="config">配置 / The configuration.</param>
    /// <param name="problem">问题 / The problem.</param>
    /// <param name="configuration">配置摘要 / The configuration digest.</param>
    /// <param name="schedule">调度控制 / The schedule control.</param>
    /// <returns>检查点或失败 / The checkpoint or a failure.</returns>
    public static Result<RunCheckpoint, ErrorCode, Error<ErrorCode>> Capture(
        HyperHeuristicRun<O, V> run,
        HyperHeuristicConfig config,
        SearchProblem<O, V> problem,
        string configuration,
        ScheduleControl? schedule = null) {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(problem);

        var workers = new Dictionary<string, WorkerCheckpoint>(StringComparer.Ordinal);
        foreach (HyperHeuristicWorker<O, V> worker in run.Workers) {
            Result<SearchSessionSnapshot, ErrorCode, Error<ErrorCode>> snapshot = worker.Session.Snapshot();
            if (snapshot is Failed<SearchSessionSnapshot, ErrorCode, Error<ErrorCode>> snapshotFailure) {
                return Results.Failed<RunCheckpoint>(snapshotFailure.Error);
            }
            workers[worker.Id] = new WorkerCheckpoint(
                ((Ok<SearchSessionSnapshot, ErrorCode, Error<ErrorCode>>)snapshot).Value,
                worker.Random.State,
                run.IsInitialized(worker.Id),
                run.IsDisabled(worker.Id),
                null);
        }

        Result<JsonElement, ErrorCode, Error<ErrorCode>> archive = Encode(run.Memory.Archive, problem);
        if (archive is Failed<JsonElement, ErrorCode, Error<ErrorCode>> archiveFailure) {
            return Results.Failed<RunCheckpoint>(archiveFailure.Error);
        }
        Result<JsonElement, ErrorCode, Error<ErrorCode>> working = Encode(run.Memory.Working, problem);
        if (working is Failed<JsonElement, ErrorCode, Error<ErrorCode>> workingFailure) {
            return Results.Failed<RunCheckpoint>(workingFailure.Error);
        }
        ScheduleCheckpoint? scheduleState = null;
        if (schedule is not null) {
            Result<ScheduleCheckpoint?, ErrorCode, Error<ErrorCode>> saved = schedule.Snapshot();
            if (saved is Failed<ScheduleCheckpoint?, ErrorCode, Error<ErrorCode>> scheduleFailure) {
                return Results.Failed<RunCheckpoint>(scheduleFailure.Error);
            }
            scheduleState = ((Ok<ScheduleCheckpoint?, ErrorCode, Error<ErrorCode>>)saved).Value;
        }

        HyperHeuristicLedger ledger = run.Ledger;
        return Results.Ok(new RunCheckpoint(
            configuration,
            problem.Id + ":" + problem.Fingerprint,
            new PolicyArchive(
                configuration, run.FeatureVersion, run.PolicySnapshot(),
                new Dictionary<string, IReadOnlyDictionary<string, PolicySnapshot>>(StringComparer.Ordinal),
                null, null, 1),
            workers,
            ((Ok<JsonElement, ErrorCode, Error<ErrorCode>>)archive).Value,
            ((Ok<JsonElement, ErrorCode, Error<ErrorCode>>)working).Value,
            ledger.Accounted,
            ledger.Dispatched,
            ledger.Completed,
            ledger.Steps,
            ledger.Stagnation,
            0L,
            run.RandomState,
            new List<string>(ledger.Diagnostics),
            null,
            ledger.ObjectiveCalls,
            run.Transitions,
            ledger.Successes,
            ledger.LastCost,
            JsonSerializer.SerializeToElement(config),
            null,
            scheduleState,
            1));
    }

    /// <summary>
    /// 从检查点恢复 / Restore from a checkpoint.
    /// </summary>
    /// <param name="run">运行 / The run.</param>
    /// <param name="saved">检查点 / The checkpoint.</param>
    /// <param name="configuration">配置摘要 / The configuration digest.</param>
    /// <param name="problem">问题 / The problem.</param>
    /// <returns>成功或失败 / Success or failure.</returns>
    public static Result<Success, ErrorCode, Error<ErrorCode>> Restore(
        HyperHeuristicRun<O, V> run,
        RunCheckpoint saved,
        string configuration,
        SearchProblem<O, V> problem) {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(saved);
        ArgumentNullException.ThrowIfNull(problem);
        if (saved.Version != 1) {
            return Fail("unsupported checkpoint version");
        }
        // **配置与问题的摘要必须一致**：把一份别的配置的检查点恢复进本次运行，会让计数与记忆都对不上，
        // 而**搜索会若无其事地继续**——从外部看不出它是从别人的状态里接着跑的。
        // **The configuration and problem digests must match**: restoring another configuration's checkpoint into this run leaves the counters and memory
        // inconsistent, while **the search carries on regardless** — nothing outside shows it resumed from someone else's state.
        if (!string.Equals(saved.Configuration, configuration, StringComparison.Ordinal)
            || !string.Equals(saved.Problem, problem.Id + ":" + problem.Fingerprint, StringComparison.Ordinal)) {
            return Fail("incompatible checkpoint identity");
        }
        var expected = new HashSet<string>(StringComparer.Ordinal);
        foreach (HyperHeuristicWorker<O, V> worker in run.Workers) {
            expected.Add(worker.Id);
        }
        if (expected.Count != saved.Workers.Count) {
            return Fail("checkpoint worker set does not match the run");
        }
        foreach (string id in expected) {
            if (!saved.Workers.ContainsKey(id)) {
                return Fail("checkpoint worker set does not match the run");
            }
        }

        Result<IReadOnlyList<IIndividual<O, V>>, ErrorCode, Error<ErrorCode>> archive =
            Decode(saved.Archive, problem);
        if (archive is Failed<IReadOnlyList<IIndividual<O, V>>, ErrorCode, Error<ErrorCode>> archiveFailure) {
            return Results.Failed<Success>(archiveFailure.Error);
        }
        Result<IReadOnlyList<IIndividual<O, V>>, ErrorCode, Error<ErrorCode>> working =
            Decode(saved.Working, problem);
        if (working is Failed<IReadOnlyList<IIndividual<O, V>>, ErrorCode, Error<ErrorCode>> workingFailure) {
            return Results.Failed<Success>(workingFailure.Error);
        }
        run.Memory.Restore(
            ((Ok<IReadOnlyList<IIndividual<O, V>>, ErrorCode, Error<ErrorCode>>)archive).Value,
            ((Ok<IReadOnlyList<IIndividual<O, V>>, ErrorCode, Error<ErrorCode>>)working).Value);

        foreach (HyperHeuristicWorker<O, V> worker in run.Workers) {
            WorkerCheckpoint checkpoint = saved.Workers[worker.Id];
            Result<Success, ErrorCode, Error<ErrorCode>> restored = worker.Session.Restore(checkpoint.Session);
            if (restored is Failed<Success, ErrorCode, Error<ErrorCode>> restoreFailure) {
                return Results.Failed<Success>(restoreFailure.Error);
            }
            worker.Random.Restore(checkpoint.Random);
            run.SetInitialized(worker.Id, checkpoint.Initialized);
            run.SetDisabled(worker.Id, checkpoint.Disabled);
        }
        run.Random.Restore(saved.Random);
        run.Ledger.Restore(saved.Evaluations, saved.Dispatched, saved.Completed, saved.Steps,
            saved.Stagnation, saved.Successes, saved.LastCost, saved.ObjectiveCalls,
            saved.Diagnostics, saved.Transitions);
        // **恢复只重建"转移记录"，不重新 `Observe`**：策略状态来自 `PolicyArchive`，再喂一遍会让计数翻倍。
        // **Restore rebuilds the transition log only and never calls `Observe` again**: the policy state comes from `PolicyArchive`, and feeding it
        // twice would double the counts.
        run.ClearTransitions();
        if (saved.Transitions is not null) {
            foreach (SelectionTransition transition in saved.Transitions) {
                run.AddTransition(transition);
            }
        }
        return Results.Ok(new Success());
    }

    /// <summary>
    /// 编码一组个体 / Encode a set of individuals.
    /// </summary>
    /// <remarks>
    /// **解向量、目标值、适应度三者都要存**。只存解向量会让恢复后目标值需要重新求值——而"重新求值"意味着
    /// **恢复过程会再花一次目标调用**，在目标调用有预算或有副作用时，这本身就是行为差异。
    ///
    /// **The solution vector, the objective value and the fitness all have to be stored.** Keeping only the solution vector forces a re-evaluation on restore — and
    /// "re-evaluation" means **the restore spends objective calls again**, which is itself a behavioural difference when objective calls are budgeted
    /// or have side effects.
    /// </remarks>
    /// <param name="individuals">个体 / The individuals.</param>
    /// <param name="problem">问题 / The problem.</param>
    /// <returns>载荷或失败 / The payload or a failure.</returns>
    private static Result<JsonElement, ErrorCode, Error<ErrorCode>> Encode(
        IReadOnlyList<IIndividual<O, V>> individuals, SearchProblem<O, V> problem) {
        var array = new JsonArray();
        foreach (IIndividual<O, V> individual in individuals) {
            var values = new JsonArray();
            foreach (V value in individual.Solution) {
                Result<JsonElement, ErrorCode, Error<ErrorCode>> encoded = problem.ValueCodec.Encode(value);
                if (encoded is Failed<JsonElement, ErrorCode, Error<ErrorCode>> valueFailure) {
                    return Results.Failed<JsonElement>(valueFailure.Error);
                }
                values.Add(JsonNode.Parse(((Ok<JsonElement, ErrorCode, Error<ErrorCode>>)encoded).Value.GetRawText()));
            }
            Result<JsonElement, ErrorCode, Error<ErrorCode>> objective =
                problem.ObjectiveCodec.Encode(individual.Objective);
            if (objective is Failed<JsonElement, ErrorCode, Error<ErrorCode>> objectiveFailure) {
                return Results.Failed<JsonElement>(objectiveFailure.Error);
            }
            array.Add(new JsonObject {
                ["solution"] = values,
                ["objective"] = JsonNode.Parse(
                    ((Ok<JsonElement, ErrorCode, Error<ErrorCode>>)objective).Value.GetRawText()),
                ["fitness"] = individual is SolutionWithFitness<O, V> withFitness
                    ? withFitness.Fitness
                    : double.NaN,
            });
        }
        return Results.Ok(JsonSerializer.SerializeToElement(array));
    }

    /// <summary>
    /// 解码一组个体 / Decode a set of individuals.
    /// </summary>
    /// <param name="payload">载荷 / The payload.</param>
    /// <param name="problem">问题 / The problem.</param>
    /// <returns>个体或失败 / The individuals or a failure.</returns>
    private static Result<IReadOnlyList<IIndividual<O, V>>, ErrorCode, Error<ErrorCode>> Decode(
        JsonElement payload, SearchProblem<O, V> problem) {
        if (payload.ValueKind != JsonValueKind.Array) {
            return FailIndividuals("invalid individual payload");
        }
        var individuals = new List<IIndividual<O, V>>();
        foreach (JsonElement element in payload.EnumerateArray()) {
            if (element.ValueKind != JsonValueKind.Object
                || !element.TryGetProperty("solution", out JsonElement solutionElement)
                || !element.TryGetProperty("objective", out JsonElement objectiveElement)
                || !element.TryGetProperty("fitness", out JsonElement fitnessElement)) {
                return FailIndividuals("invalid individual payload");
            }
            var solution = new List<V>();
            foreach (JsonElement value in solutionElement.EnumerateArray()) {
                Result<V, ErrorCode, Error<ErrorCode>> decoded = problem.ValueCodec.Decode(value);
                if (decoded is Failed<V, ErrorCode, Error<ErrorCode>> valueFailure) {
                    return Results.Failed<IReadOnlyList<IIndividual<O, V>>>(valueFailure.Error);
                }
                solution.Add(((Ok<V, ErrorCode, Error<ErrorCode>>)decoded).Value);
            }
            Result<O, ErrorCode, Error<ErrorCode>> objective = problem.ObjectiveCodec.Decode(objectiveElement);
            if (objective is Failed<O, ErrorCode, Error<ErrorCode>> objectiveFailure) {
                return Results.Failed<IReadOnlyList<IIndividual<O, V>>>(objectiveFailure.Error);
            }
            individuals.Add(new SolutionWithFitness<O, V>(
                solution,
                ((Ok<O, ErrorCode, Error<ErrorCode>>)objective).Value,
                fitnessElement.GetDouble()));
        }
        return Results.Ok<IReadOnlyList<IIndividual<O, V>>>(individuals);
    }

    /// <summary>失败 / A failure.</summary>
    /// <param name="message">消息 / The message.</param>
    /// <returns>失败结果 / The failure.</returns>
    private static Result<Success, ErrorCode, Error<ErrorCode>> Fail(string message) =>
        Results.Failed<Success>(new Err<ErrorCode>(ErrorCode.IllegalArgument, message));

    /// <summary>个体载荷失败 / An individual-payload failure.</summary>
    /// <param name="message">消息 / The message.</param>
    /// <returns>失败结果 / The failure.</returns>
    private static Result<IReadOnlyList<IIndividual<O, V>>, ErrorCode, Error<ErrorCode>> FailIndividuals(
        string message) =>
        Results.Failed<IReadOnlyList<IIndividual<O, V>>>(
            new Err<ErrorCode>(ErrorCode.IllegalArgument, message));
}
