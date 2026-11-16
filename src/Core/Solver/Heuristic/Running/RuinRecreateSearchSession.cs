// 破坏—重建搜索会话 / The ruin-and-recreate search session.
// 与固定 Kotlin （9190 B）对应。

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator.Neighborhood;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Core.Solver.Heuristic.Running;

/// <summary>
/// 破坏—重建搜索会话 / The ruin-and-recreate search session.
/// </summary>
/// <remarks>
/// **只接受排列编码**。破坏—重建的全部语义都建立在"解是元素的一个排列"上；放到连续编码上，
/// "移走若干元素再插回去"会变成"改几个坐标"，**而算法名字与调用方式都还成立**。
/// **Accepts permutation encoding only.** The whole meaning of ruin-and-recreate rests on "the solution is a permutation of the elements";
/// under a continuous encoding, "remove some elements and insert them back" degenerates into "edit a few coordinates", **while the
/// algorithm's name and call signature still hold**.
/// **当前解只在"不差于现有解"时才被替换**。用"严格优于"会让等价解永远进不来，**于是破坏—重建的"横向平移"能力被悄悄关掉**，
/// 而收敛曲线只是"更平"。
/// **The current solution is replaced only when the candidate is "not worse"**. Requiring strict improvement would keep equivalent solutions
/// out, **silently disabling ruin-and-recreate's ability to move sideways**, and the convergence curve merely looks "flatter".
/// **评估被预算拒绝时立即停止本片段，并把终止原因写成 `TimeLimit` 或 `EvaluationLimit`**。继续评估会让"这一次没算成"
/// 变成"这一步什么都没发生"，**而上层只看到步数没增加**。
/// **When the budget refuses the evaluation the slice stops at once, with the reason `TimeLimit` or `EvaluationLimit`**. Continuing to
/// evaluate turns "this evaluation did not happen" into "this step did nothing", **while the caller only sees that the step count did not
/// move**.
/// **模型刷新失败时保留已经产出的候选，并把这一步标成失败**，而不是把候选一起丢掉。丢掉会让上层看不到"已经算出来的东西"，
/// **而失败本身又不足以解释产出为零**。
/// **When the model flush fails the candidates already produced are kept and the step is marked failed**, rather than discarding them.
/// Discarding hides "what was already computed", **while the failure alone does not explain why the output is empty**.
/// **快照必须先声明"域估计是否无状态"，否则拒绝**。不声明就让恢复后的估计值来源不明，
/// **而恢复出来的运行会以一份没人知道来历的估计继续推进**。
/// **The snapshot refuses unless the domain estimate is declared stateless or given a recovery object.** Without the declaration a restored
/// run would continue from an estimate of unknown provenance.
/// **恢复时会重新校验"初始化标志与解是否一致"、"解是否满足编码/可行/有效"**。不校验会让一份被改坏的检查点悄悄生效，
/// **而它产出的每一步都是合法的**。
/// **Restore re-checks "the initialisation flag matches the solution" and "the solution satisfies encoding/feasibility/validity"**. Skipping it
/// lets a corrupted checkpoint take effect silently, **and every step it produces is itself legal**.
/// **编码当前解时同时存"解向量"与"目标值"**。的是 `SearchCodecs.individual(problem)`，本实现没有那个工厂，
/// 因此改用问题自带的值编码器与目标编码器逐项编码——**语义等价，但 JSON 形状不同，不宣称与基线逐字节兼容**。
/// **Encoding the current solution stores both "the solution vector" and "the objective"**. The code uses
/// `SearchCodecs.individual(problem)`, which this implementation does not have, so it encodes each entry with the problem's own value and objective
/// codecs instead — **semantically equivalent, but the JSON shape differs and is not claimed to be byte-compatible with the code**.
/// </remarks>
/// <typeparam name="ObjValue">目标值类型 / The objective-value type.</typeparam>
/// <typeparam name="V">值类型 / The value type.</typeparam>
public sealed class RuinRecreateSearchSession<ObjValue, V> : ISearchSession<ObjValue, V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    private readonly SearchProblem<ObjValue, V> _problem;
    private readonly RuinRecreateOperator<V> _operator;
    private readonly int _amount;
    private readonly SearchRandom _random;
    private readonly bool _statelessEstimates;
    private readonly ISearchProblemState? _estimateState;
    private IIndividual<ObjValue, V>? _current;
    private long _iterations;

    /// <summary>
    /// 构造破坏—重建搜索会话 / Construct the ruin-and-recreate search session.
    /// </summary>
    /// <param name="problem">搜索问题 / The search problem.</param>
    /// <param name="operator">破坏—重建算子 / The ruin-and-recreate operator.</param>
    /// <param name="amount">每次移除数量 / The removal amount.</param>
    /// <param name="random">随机源 / The random source.</param>
    /// <param name="statelessEstimates">域估计是否无状态 / Whether the domain estimate is stateless.</param>
    /// <param name="estimateState">域估计的可恢复状态 / The recoverable estimate state.</param>
    public RuinRecreateSearchSession(
        SearchProblem<ObjValue, V> problem,
        RuinRecreateOperator<V> @operator,
        int amount,
        SearchRandom random,
        bool statelessEstimates = false,
        ISearchProblemState? estimateState = null) {
        ArgumentNullException.ThrowIfNull(problem);
        ArgumentNullException.ThrowIfNull(@operator);
        ArgumentNullException.ThrowIfNull(random);
        _problem = problem;
        _operator = @operator;
        _amount = amount;
        _random = random;
        _statelessEstimates = statelessEstimates;
        _estimateState = estimateState;
    }

    /// <inheritdoc/>
    public bool SupportsImport => true;

    /// <summary>已执行的迭代数 / The number of iterations executed.</summary>
    public long Iterations => _iterations;

    /// <summary>当前解 / The current solution.</summary>
    public IIndividual<ObjValue, V>? Current => _current;

    /// <inheritdoc/>
    public Task<Result<SearchStepResult<ObjValue, V>, ErrorCode, Error<ErrorCode>>> Step(
        SearchSlice budget) {
        ArgumentNullException.ThrowIfNull(budget);
        if (_problem.Encoding != SearchEncoding.Permutation || _amount < 0 || budget.Steps < 0) {
            return Task.FromResult(SearchCodecSupport.SearchInvalid<SearchStepResult<ObjValue, V>>(
                "ruin/recreate session requires permutation encoding and nonnegative limits"));
        }

        long start = budget.Used;
        var candidates = new List<IIndividual<ObjValue, V>>();
        long steps = 0;
        SearchTermination reason = SearchTermination.SliceComplete;

        while (steps < budget.Steps && !budget.Expired) {
            IIndividual<ObjValue, V>? previous = _current;

            IReadOnlyList<V>? neighbor;
            if (previous is null) {
                IReadOnlyList<IReadOnlyList<V>> provided = _problem.Model.InitialSolutions(1UL);
                neighbor = provided.Count == 0 ? null : provided[0];
            } else {
                Result<IReadOnlyList<V>, ErrorCode, Error<ErrorCode>> ruined =
                    _operator.Invoke(previous.Solution, _amount, _random);
                if (ruined is Failed<IReadOnlyList<V>, ErrorCode, Error<ErrorCode>> ruinFailure) {
                    return Task.FromResult(Fail(candidates, budget.Used - start, steps,
                        $"破坏修复失败 / Ruin/recreate failed: {ruinFailure.Error.Message}"));
                }
                neighbor = ((Ok<IReadOnlyList<V>, ErrorCode, Error<ErrorCode>>)ruined).Value;
            }
            if (neighbor is null) {
                return Task.FromResult(Ok(candidates, budget.Used - start, steps,
                    SearchTermination.NoCandidate));
            }

            EvaluationModel<ObjValue, V> evaluated =
                new(_problem.Model, _problem, budget, null);
            ObjValue? objective = evaluated.Objective(neighbor);
            if (evaluated.Exhausted) {
                reason = budget.Expired
                    ? SearchTermination.TimeLimit
                    : SearchTermination.EvaluationLimit;
                break;
            }
            if (objective is not null) {
                var candidate = new SolutionWithFitness<ObjValue, V>(
                    neighbor, objective, 0.0);
                candidates.Add(candidate);
                bool accepted = previous is null
                    || !_problem.Dominates(previous.Objective, candidate.Objective);
                if (accepted) {
                    _current = candidate;
                }
            }

            _iterations++;
            steps++;
            _problem.Model.Flush();
            if (_current is null) {
                reason = SearchTermination.NoCandidate;
                break;
            }
        }

        return Task.FromResult(Ok(candidates, budget.Used - start, steps,
            budget.Expired ? SearchTermination.TimeLimit : reason));
    }

    /// <inheritdoc/>
    public Task<Result<SearchStepResult<ObjValue, V>, ErrorCode, Error<ErrorCode>>> ImportSolutions(
        IReadOnlyList<Solution<V>> solutions, SearchSlice budget) {
        ArgumentNullException.ThrowIfNull(solutions);
        ArgumentNullException.ThrowIfNull(budget);
        if (_problem.Encoding != SearchEncoding.Permutation || _amount < 0) {
            return Task.FromResult(SearchCodecSupport.SearchInvalid<SearchStepResult<ObjValue, V>>(
                "invalid ruin/recreate session"));
        }

        long start = budget.Used;
        var candidates = new List<IIndividual<ObjValue, V>>();
        SearchTermination reason = SearchTermination.SliceComplete;
        foreach (Solution<V> solution in solutions) {
            EvaluationModel<ObjValue, V> evaluated =
                new(_problem.Model, _problem, budget, null);
            ObjValue? objective = evaluated.Objective(solution.Values);
            if (evaluated.Exhausted) {
                reason = SearchTermination.EvaluationLimit;
                break;
            }
            if (objective is null) {
                continue;
            }
            var candidate = new SolutionWithFitness<ObjValue, V>(
                solution.Values, objective, 0.0);
            candidates.Add(candidate);
            IIndividual<ObjValue, V>? previous = _current;
            bool accepted = previous is null
                || !_problem.Dominates(previous.Objective, candidate.Objective);
            if (accepted) {
                _current = candidate;
            }
        }

        return Task.FromResult(Ok(candidates, budget.Used - start, 0,
            budget.Expired ? SearchTermination.TimeLimit : reason));
    }

    /// <inheritdoc/>
    public Result<SearchSessionSnapshot, ErrorCode, Error<ErrorCode>> Snapshot() {
        if (!_statelessEstimates && _estimateState is null) {
            return SearchCodecSupport.SearchInvalid<SearchSessionSnapshot>(
                "domain estimates must declare statelessness or provide recovery");
        }
        JsonElement current = JsonNull();
        if (_current is not null) {
            Result<JsonElement, ErrorCode, Error<ErrorCode>> encoded = Encode(_current);
            if (encoded is Failed<JsonElement, ErrorCode, Error<ErrorCode>> failure) {
                return Results.Failed<SearchSessionSnapshot>(failure.Error);
            }
            current = ((Ok<JsonElement, ErrorCode, Error<ErrorCode>>)encoded).Value;
        }
        JsonElement callback;
        if (_estimateState is null) {
            callback = JsonNull();
        } else {
            Result<JsonElement, ErrorCode, Error<ErrorCode>> saved = _estimateState.Save();
            if (saved is Failed<JsonElement, ErrorCode, Error<ErrorCode>> failure) {
                return Results.Failed<SearchSessionSnapshot>(failure.Error);
            }
            callback = ((Ok<JsonElement, ErrorCode, Error<ErrorCode>>)saved).Value;
        }

        using var buffer = new System.IO.MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer)) {
            writer.WriteStartObject();
            writer.WritePropertyName("current");
            current.WriteTo(writer);
            writer.WritePropertyName("callback");
            callback.WriteTo(writer);
            writer.WriteNumber("random", _random.State);
            writer.WriteEndObject();
        }
        JsonElement state = JsonDocument.Parse(buffer.ToArray()).RootElement.Clone();

        return Results.Ok(new SearchSessionSnapshot(
            _current is not null, _iterations, 0, state, EmptyArray(), 1, 0));
    }

    /// <inheritdoc/>
    public Result<Success, ErrorCode, Error<ErrorCode>> Restore(SearchSessionSnapshot snapshot) {
        if (snapshot.Version != 1 || snapshot.Iteration < 0
            || (!_statelessEstimates && _estimateState is null)) {
            return SearchCodecSupport.SearchInvalid<Success>(
                "incompatible ruin/recreate snapshot");
        }
        if (snapshot.State.ValueKind != JsonValueKind.Object
            || !snapshot.State.TryGetProperty("current", out JsonElement storedCurrent)
            || !snapshot.State.TryGetProperty("callback", out JsonElement storedCallback)
            || !snapshot.State.TryGetProperty("random", out JsonElement storedRandom)
            || !storedRandom.TryGetInt64(out long randomState)) {
            return SearchCodecSupport.SearchInvalid<Success>("malformed ruin/recreate snapshot");
        }

        IIndividual<ObjValue, V>? individual = null;
        if (storedCurrent.ValueKind != JsonValueKind.Null) {
            Result<IIndividual<ObjValue, V>, ErrorCode, Error<ErrorCode>> decoded =
                Decode(storedCurrent);
            if (decoded is Failed<IIndividual<ObjValue, V>, ErrorCode, Error<ErrorCode>> failure) {
                return Results.Failed<Success>(failure.Error);
            }
            individual = ((Ok<IIndividual<ObjValue, V>, ErrorCode, Error<ErrorCode>>)decoded).Value;
        }
        if (snapshot.Initialized != (individual is not null)) {
            return SearchCodecSupport.SearchInvalid<Success>(
                "inconsistent ruin/recreate initialization");
        }
        if (individual is not null) {
            if (!_problem.ValidSolution(individual.Solution)
                || !_problem.Admissible(individual.Solution)
                || !_problem.Valid(individual.Objective)) {
                return SearchCodecSupport.SearchInvalid<Success>(
                    "invalid restored ruin/recreate candidate");
            }
        }
        if (_estimateState is not null) {
            Result<Success, ErrorCode, Error<ErrorCode>> loaded = _estimateState.Load(storedCallback);
            if (loaded is Failed<Success, ErrorCode, Error<ErrorCode>> loadFailure) {
                return Results.Failed<Success>(loadFailure.Error);
            }
        } else if (storedCallback.ValueKind != JsonValueKind.Null) {
            return SearchCodecSupport.SearchInvalid<Success>("unexpected estimate state");
        }

        _current = individual;
        _iterations = snapshot.Iteration;
        _random.Restore(randomState);
        return Results.Ok(Results.SuccessInstance);
    }

    /// <inheritdoc/>
    public Result<Success, ErrorCode, Error<ErrorCode>> Close() => Results.Ok(Results.SuccessInstance);

    /// <summary>
    /// 编码一个个体 / Encode one individual.
    /// </summary>
    /// <param name="individual">个体 / The individual.</param>
    /// <returns>JSON，失败时失败 / The JSON, failing on failure.</returns>
    private Result<JsonElement, ErrorCode, Error<ErrorCode>> Encode(
        IIndividual<ObjValue, V> individual) {
        var values = new JsonElement[individual.Solution.Count];
        for (int index = 0; index < individual.Solution.Count; index++) {
            Result<JsonElement, ErrorCode, Error<ErrorCode>> encoded =
                _problem.ValueCodec.Encode(individual.Solution[index]);
            if (encoded is Failed<JsonElement, ErrorCode, Error<ErrorCode>> failure) {
                return Results.Failed<JsonElement>(failure.Error);
            }
            values[index] = ((Ok<JsonElement, ErrorCode, Error<ErrorCode>>)encoded).Value;
        }
        Result<JsonElement, ErrorCode, Error<ErrorCode>> objective =
            _problem.ObjectiveCodec.Encode(individual.Objective);
        if (objective is Failed<JsonElement, ErrorCode, Error<ErrorCode>> objectiveFailure) {
            return Results.Failed<JsonElement>(objectiveFailure.Error);
        }
        JsonElement objectiveValue =
            ((Ok<JsonElement, ErrorCode, Error<ErrorCode>>)objective).Value;

        using var buffer = new System.IO.MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer)) {
            writer.WriteStartObject();
            writer.WriteStartArray("values");
            foreach (JsonElement value in values) {
                value.WriteTo(writer);
            }
            writer.WriteEndArray();
            writer.WritePropertyName("objective");
            objectiveValue.WriteTo(writer);
            writer.WriteEndObject();
        }
        return Results.Ok(JsonDocument.Parse(buffer.ToArray()).RootElement.Clone());
    }

    /// <summary>
    /// 解码一个个体 / Decode one individual.
    /// </summary>
    /// <param name="state">JSON / The JSON.</param>
    /// <returns>个体，失败时失败 / The individual, failing on failure.</returns>
    private Result<IIndividual<ObjValue, V>, ErrorCode, Error<ErrorCode>> Decode(
        JsonElement state) {
        if (state.ValueKind != JsonValueKind.Object
            || !state.TryGetProperty("values", out JsonElement values)
            || values.ValueKind != JsonValueKind.Array
            || !state.TryGetProperty("objective", out JsonElement objectiveElement)) {
            return SearchCodecSupport.SearchInvalid<IIndividual<ObjValue, V>>(
                "malformed ruin/recreate individual");
        }
        var solution = new List<V>(values.GetArrayLength());
        foreach (JsonElement value in values.EnumerateArray()) {
            Result<V, ErrorCode, Error<ErrorCode>> decoded = _problem.ValueCodec.Decode(value);
            if (decoded is Failed<V, ErrorCode, Error<ErrorCode>> failure) {
                return Results.Failed<IIndividual<ObjValue, V>>(failure.Error);
            }
            solution.Add(((Ok<V, ErrorCode, Error<ErrorCode>>)decoded).Value);
        }
        Result<ObjValue, ErrorCode, Error<ErrorCode>> objective =
            _problem.ObjectiveCodec.Decode(objectiveElement);
        if (objective is Failed<ObjValue, ErrorCode, Error<ErrorCode>> objectiveFailure) {
            return Results.Failed<IIndividual<ObjValue, V>>(objectiveFailure.Error);
        }
        return Results.Ok<IIndividual<ObjValue, V>>(new SolutionWithFitness<ObjValue, V>(
            solution, ((Ok<ObjValue, ErrorCode, Error<ErrorCode>>)objective).Value, 0.0));
    }

    /// <summary>空的 JSON 数组 / An empty JSON array.</summary>
    /// <returns>空的 JSON 数组 / The empty JSON array.</returns>
    private static JsonElement EmptyArray() => JsonDocument.Parse("[]").RootElement.Clone();

    /// <summary>`null` 的 JSON 表示 / The JSON representation of `null`.</summary>
    /// <returns>`null` 的 JSON 表示 / The JSON representation of `null`.</returns>
    private static JsonElement JsonNull() => JsonDocument.Parse("null").RootElement.Clone();

    /// <summary>构造成功结果 / Build a success result.</summary>
    /// <param name="candidates">候选 / The candidates.</param>
    /// <param name="evaluations">评估数 / The evaluation count.</param>
    /// <param name="steps">步数 / The step count.</param>
    /// <param name="termination">终止原因 / The termination reason.</param>
    /// <returns>成功结果 / The success result.</returns>
    private static Result<SearchStepResult<ObjValue, V>, ErrorCode, Error<ErrorCode>> Ok(
        IReadOnlyList<IIndividual<ObjValue, V>> candidates,
        long evaluations, long steps, SearchTermination termination) =>
        Results.Ok(new SearchStepResult<ObjValue, V>(candidates, evaluations, steps, termination));

    /// <summary>构造失败结果 / Build a failure result.</summary>
    /// <param name="candidates">候选 / The candidates.</param>
    /// <param name="evaluations">评估数 / The evaluation count.</param>
    /// <param name="steps">步数 / The step count.</param>
    /// <param name="diagnostic">诊断消息 / The diagnostic message.</param>
    /// <returns>成功包装的失败步进 / A successful wrapper around a failed step.</returns>
    private static Result<SearchStepResult<ObjValue, V>, ErrorCode, Error<ErrorCode>> Fail(
        IReadOnlyList<IIndividual<ObjValue, V>> candidates,
        long evaluations, long steps, string diagnostic) =>
        Results.Ok(new SearchStepResult<ObjValue, V>(
            candidates, evaluations, steps, SearchTermination.Failed, new[] { diagnostic }));
}
