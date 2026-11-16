// 复合搜索会话 / The composite search session.

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator.MultiObjective;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Core.Solver.Heuristic.Scheduling;

/// <summary>
/// 复合搜索会话：把多个子会话按顺序或轮转调度 / A composite session that drives several child sessions sequentially or round-robin.
/// </summary>
/// <remarks>
/// **顺序模式的游标只在"相位用满"时前进，且最后一格不再前进**。少了前半句，每个子算法只能跑一步就被换掉；
/// 少了后半句，最后一个子算法永远得不到执行，**而两者的表现都是"复合搜索不如单算法"**，看不出是哪条写错。
///
/// **轮转模式每步都换子算法**，并用"总步数对 `相位 × 子算法数` 取模为零"来把记忆里的解投给下一个子算法；
/// 这个周期不是随手取的：**它保证每个子算法各跑满一个相位之后才收到一次共享种子**。
///
/// **In sequential mode the cursor advances only when a phase is used up, and never past the last slot.** Without the first half every child gets
/// replaced after a single step; without the second the last child never runs — **and both look like "the composite is worse than a single algorithm"**,
/// with no clue which half is wrong.
///
/// **Round-robin swaps children every step** and feeds a remembered solution to the next child when "the total step count is divisible by
/// `phase × child count`". That period is not arbitrary: **it guarantees each child finishes a full phase before receiving a shared seed**.
/// </remarks>
/// <typeparam name="O">目标类型 / The objective type.</typeparam>
/// <typeparam name="V">值类型 / The value type.</typeparam>
public sealed class CompositeSearchSession<O, V> : ISearchSession<O, V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    private readonly Dictionary<string, ISearchSession<O, V>> _children;
    private readonly Dictionary<string, SearchRandom> _streams;
    private readonly bool _sequential;
    private readonly int _phaseSteps;
    private readonly SearchProblem<O, V> _problem;
    private readonly List<string> _ids;
    private readonly bool[] _initialized;
    private readonly SearchMemory<O, V> _memory;
    private int _cursor;
    private long _completed;

    /// <summary>
    /// 构造复合会话 / Construct the composite session.
    /// </summary>
    /// <remarks>
    /// **要求问题提供目标坐标**：共享记忆要用它做接受判定，而坐标投影属于问题、不属于会话。缺坐标时在构造期就抛出，
    /// 而不是退化成"永远只接受更好解"——后者会让复合搜索看起来正常，**而记忆里其实只剩当前最优**。
    ///
    /// **Requires the problem to supply objective coordinates**: the shared memory needs them for its acceptance test, and the projection belongs to
    /// the problem rather than to the session. Missing coordinates throw at construction instead of degrading to "accept only improvements" — the latter
    /// looks normal while **the memory in fact holds nothing but the current best**.
    /// </remarks>
    /// <param name="children">子会话 / The child sessions.</param>
    /// <param name="streams">子随机流 / The child random streams.</param>
    /// <param name="sequential">是否顺序调度 / Whether to schedule sequentially.</param>
    /// <param name="phaseSteps">相位步数 / The phase length in steps.</param>
    /// <param name="problem">问题边界 / The problem boundary.</param>
    public CompositeSearchSession(
        IReadOnlyDictionary<string, ISearchSession<O, V>> children,
        IReadOnlyDictionary<string, SearchRandom> streams,
        bool sequential,
        int phaseSteps,
        SearchProblem<O, V> problem) {
        ArgumentNullException.ThrowIfNull(children);
        ArgumentNullException.ThrowIfNull(streams);
        ArgumentNullException.ThrowIfNull(problem);
        if (children.Count == 0) {
            throw new ArgumentException(
                "复合会话至少需要一个子会话 / a composite session needs at least one child",
                nameof(children));
        }
        if (phaseSteps <= 0) {
            throw new ArgumentOutOfRangeException(
                nameof(phaseSteps), "相位步数必须为正 / the phase length must be positive");
        }
        Func<O, IReadOnlyList<Flt64>> coordinates = problem.ObjectiveCoordinates
            ?? throw new ArgumentException(
                "复合会话需要目标坐标 / a composite session needs objective coordinates",
                nameof(problem));
        _children = new Dictionary<string, ISearchSession<O, V>>(children, StringComparer.Ordinal);
        _streams = new Dictionary<string, SearchRandom>(streams, StringComparer.Ordinal);
        _sequential = sequential;
        _phaseSteps = phaseSteps;
        _problem = problem;
        _ids = new List<string>(children.Keys);
        _initialized = new bool[_ids.Count];
        _memory = new SearchMemory<O, V>(
            100,
            20,
            new NotWorseAcceptancePolicy<O, V>(new AnyArityMinimizationDominance(), coordinates),
            problem.MultiObjective,
            new AnyArityMinimizationDominance(),
            coordinates,
            individual => problem.Admissible(individual.Solution));
    }

    /// <summary>当前位置 / The current position.</summary>
    public int Cursor => _cursor;

    /// <summary>已完成的子步数 / The completed child steps.</summary>
    public long Completed => _completed;

    /// <summary>共享记忆的档案 / The shared memory's archive.</summary>
    public IReadOnlyList<IIndividual<O, V>> Archive => _memory.Archive;

    /// <summary>共享记忆的工作集 / The shared memory's working set.</summary>
    public IReadOnlyList<IIndividual<O, V>> Working => _memory.Working;

    /// <inheritdoc/>
    public bool SupportsImport {
        get {
            foreach (ISearchSession<O, V> child in _children.Values) {
                if (!child.SupportsImport) {
                    return false;
                }
            }
            return true;
        }
    }

    /// <inheritdoc/>
    public async Task<Result<SearchStepResult<O, V>, ErrorCode, Error<ErrorCode>>> Step(
        SearchSlice budget) {
        ArgumentNullException.ThrowIfNull(budget);
        var candidates = new List<IIndividual<O, V>>();
        long steps = 0L;
        long begin = budget.Used;
        SearchTermination reason = SearchTermination.SliceComplete;
        var diagnostics = new List<string>();
        long requested = budget.Steps < 0 ? 0 : budget.Steps;

        for (long index = 0; index < requested; index++) {
            ISearchSession<O, V> child = _children[_ids[_cursor]];
            long period = (long)_phaseSteps * _ids.Count;
            if (!_sequential && _initialized[_cursor] && _completed > 0 && period > 0
                && _completed % period == 0L) {
                Result<SearchStepResult<O, V>, ErrorCode, Error<ErrorCode>> imported =
                    await ImportInto(child, budget, candidates, begin).ConfigureAwait(false);
                if (imported is Failed<SearchStepResult<O, V>, ErrorCode, Error<ErrorCode>> failure) {
                    return Results.Failed<SearchStepResult<O, V>>(failure.Error);
                }
            }

            long remaining = budget.Evaluations - (budget.Used - begin);
            if (remaining <= 0L || budget.Expired) {
                return Results.Ok(new SearchStepResult<O, V>(
                    candidates, budget.Used - begin, steps, SearchTermination.EvaluationLimit));
            }

            Result<SearchStepResult<O, V>, ErrorCode, Error<ErrorCode>> stepped =
                await child.Step(budget.Child(remaining, 1)).ConfigureAwait(false);
            if (stepped is Failed<SearchStepResult<O, V>, ErrorCode, Error<ErrorCode>> stepFailure) {
                return Results.Failed<SearchStepResult<O, V>>(stepFailure.Error);
            }
            SearchStepResult<O, V> report =
                ((Ok<SearchStepResult<O, V>, ErrorCode, Error<ErrorCode>>)stepped).Value;
            candidates.AddRange(report.Candidates);
            steps += report.Steps;
            reason = report.Termination;
            diagnostics.AddRange(report.Diagnostics);

            if (!_initialized[_cursor] && report.Steps > 0L) {
                _initialized[_cursor] = true;
                Result<SearchStepResult<O, V>, ErrorCode, Error<ErrorCode>> seeded =
                    await ImportInto(child, budget, candidates, begin).ConfigureAwait(false);
                if (seeded is Failed<SearchStepResult<O, V>, ErrorCode, Error<ErrorCode>> seedFailure) {
                    return Results.Failed<SearchStepResult<O, V>>(seedFailure.Error);
                }
            }

            _memory.Offer(report.Candidates);

            if (report.Steps > 0L) {
                _completed++;
                if (_sequential) {
                    if (_completed % _phaseSteps == 0L && _cursor < _ids.Count - 1) {
                        _cursor++;
                    }
                } else {
                    _cursor = (_cursor + 1) % _ids.Count;
                }
            }

            if (reason != SearchTermination.SliceComplete) {
                return Results.Ok(new SearchStepResult<O, V>(
                    candidates, budget.Used - begin, steps, reason, diagnostics));
            }
        }

        return Results.Ok(new SearchStepResult<O, V>(
            candidates, budget.Used - begin, steps, reason, diagnostics));
    }

    /// <inheritdoc/>
    public Task<Result<SearchStepResult<O, V>, ErrorCode, Error<ErrorCode>>> ImportSolutions(
        IReadOnlyList<Solution<V>> solutions, SearchSlice budget) {
        ArgumentNullException.ThrowIfNull(solutions);
        ArgumentNullException.ThrowIfNull(budget);
        if (!_initialized[_cursor]) {
            return Task.FromResult(Results.Ok(new SearchStepResult<O, V>(
                Array.Empty<IIndividual<O, V>>(), 0L, 0L, SearchTermination.SliceComplete)));
        }
        return _children[_ids[_cursor]].ImportSolutions(solutions, budget);
    }

    /// <inheritdoc/>
    public Result<SearchSessionSnapshot, ErrorCode, Error<ErrorCode>> Snapshot() {
        var children = new JsonObject();
        foreach ((string id, ISearchSession<O, V> child) in _children) {
            Result<SearchSessionSnapshot, ErrorCode, Error<ErrorCode>> snapshot = child.Snapshot();
            if (snapshot is Failed<SearchSessionSnapshot, ErrorCode, Error<ErrorCode>> failure) {
                return Results.Failed<SearchSessionSnapshot>(failure.Error);
            }
            children[id] = JsonSerializer.SerializeToNode(
                ((Ok<SearchSessionSnapshot, ErrorCode, Error<ErrorCode>>)snapshot).Value);
        }
        ISearchCodec<List<IIndividual<O, V>>> codec =
            SearchCodecs.List(SearchCodecs.Individual(_problem));
        Result<JsonElement, ErrorCode, Error<ErrorCode>> archive =
            codec.Encode(new List<IIndividual<O, V>>(_memory.Archive));
        if (archive is Failed<JsonElement, ErrorCode, Error<ErrorCode>> archiveFailure) {
            return Results.Failed<SearchSessionSnapshot>(archiveFailure.Error);
        }
        Result<JsonElement, ErrorCode, Error<ErrorCode>> working =
            codec.Encode(new List<IIndividual<O, V>>(_memory.Working));
        if (working is Failed<JsonElement, ErrorCode, Error<ErrorCode>> workingFailure) {
            return Results.Failed<SearchSessionSnapshot>(workingFailure.Error);
        }
        JsonElement archiveElement =
            ((Ok<JsonElement, ErrorCode, Error<ErrorCode>>)archive).Value;
        JsonElement workingElement =
            ((Ok<JsonElement, ErrorCode, Error<ErrorCode>>)working).Value;
        var streams = new JsonObject();
        foreach ((string id, SearchRandom stream) in _streams) {
            streams[id] = stream.State;
        }
        var initialized = new JsonArray();
        foreach (bool flag in _initialized) {
            initialized.Add(flag);
        }
        var state = new JsonObject {
            ["children"] = children,
            ["random"] = streams,
            ["cursor"] = _cursor,
            ["steps"] = _completed,
            ["initialized"] = initialized,
            ["archive"] = JsonNode.Parse(archiveElement.GetRawText()),
            ["working"] = JsonNode.Parse(workingElement.GetRawText()),
        };
        bool anyInitialized = false;
        foreach (bool flag in _initialized) {
            anyInitialized |= flag;
        }
        return Results.Ok(new SearchSessionSnapshot(
            anyInitialized, _completed, 0L,
            JsonDocument.Parse(state.ToJsonString()).RootElement.Clone(),
            archiveElement));
    }

    /// <inheritdoc/>
    public Result<Success, ErrorCode, Error<ErrorCode>> Restore(SearchSessionSnapshot snapshot) {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Version != 1) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument, "incompatible composite state"));
        }
        JsonDocument document;
        try {
            document = JsonDocument.Parse(snapshot.State.GetRawText());
        } catch (JsonException exception) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument, exception.Message));
        }
        using (document) {
            JsonElement state = document.RootElement;
            if (!TryReadChildren(state, out JsonElement children)
                || !TryReadStreams(state, out JsonElement streams)
                || !TryReadCursor(state, out int cursor)
                || !TryReadSteps(state, out long steps)
                || !TryReadInitialized(state, out bool[] initialized)
                || children.ValueKind != JsonValueKind.Object
                || streams.ValueKind != JsonValueKind.Object
                || cursor < 0 || cursor >= _ids.Count
                || steps < 0L
                || initialized.Length != _ids.Count) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.IllegalArgument, "incompatible composite state"));
            }
            int childCount = 0;
            foreach (JsonProperty unusedChild in children.EnumerateObject()) {
                childCount++;
            }
            int streamCount = 0;
            foreach (JsonProperty unusedStream in streams.EnumerateObject()) {
                streamCount++;
            }
            if (childCount != _children.Count || streamCount != _streams.Count) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.IllegalArgument, "incompatible composite state"));
            }
            foreach ((string id, ISearchSession<O, V> child) in _children) {
                if (!children.TryGetProperty(id, out JsonElement childState)) {
                    return Results.Failed<Success>(new Err<ErrorCode>(
                        ErrorCode.IllegalArgument, "incompatible composite state"));
                }
                SearchSessionSnapshot? restored;
                try {
                    restored = childState.Deserialize<SearchSessionSnapshot>();
                } catch (JsonException exception) {
                    return Results.Failed<Success>(new Err<ErrorCode>(
                        ErrorCode.IllegalArgument, exception.Message));
                }
                if (restored is null) {
                    return Results.Failed<Success>(new Err<ErrorCode>(
                        ErrorCode.IllegalArgument, "incompatible composite state"));
                }
                Result<Success, ErrorCode, Error<ErrorCode>> childResult = child.Restore(restored);
                if (childResult is Failed<Success, ErrorCode, Error<ErrorCode>> childFailure) {
                    return Results.Failed<Success>(childFailure.Error);
                }
            }
            ISearchCodec<List<IIndividual<O, V>>> codec =
                SearchCodecs.List(SearchCodecs.Individual(_problem));
            if (!state.TryGetProperty("archive", out JsonElement archiveElement)
                || !state.TryGetProperty("working", out JsonElement workingElement)) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.IllegalArgument, "incompatible composite state"));
            }
            Result<List<IIndividual<O, V>>, ErrorCode, Error<ErrorCode>> archive =
                codec.Decode(archiveElement);
            if (archive is Failed<List<IIndividual<O, V>>, ErrorCode, Error<ErrorCode>> archiveFailure) {
                return Results.Failed<Success>(archiveFailure.Error);
            }
            Result<List<IIndividual<O, V>>, ErrorCode, Error<ErrorCode>> working =
                codec.Decode(workingElement);
            if (working is Failed<List<IIndividual<O, V>>, ErrorCode, Error<ErrorCode>> workingFailure) {
                return Results.Failed<Success>(workingFailure.Error);
            }
            _memory.Restore(
                ((Ok<List<IIndividual<O, V>>, ErrorCode, Error<ErrorCode>>)archive).Value,
                ((Ok<List<IIndividual<O, V>>, ErrorCode, Error<ErrorCode>>)working).Value);
            foreach ((string id, SearchRandom stream) in _streams) {
                if (!streams.TryGetProperty(id, out JsonElement streamState)
                    || !streamState.TryGetInt64(out long value)) {
                    return Results.Failed<Success>(new Err<ErrorCode>(
                        ErrorCode.IllegalArgument, "incompatible composite state"));
                }
                stream.Restore(value);
            }
            _cursor = cursor;
            _completed = steps;
            for (int index = 0; index < _initialized.Length; index++) {
                _initialized[index] = initialized[index];
            }
            return Results.Ok(new Success());
        }
    }

    /// <inheritdoc/>
    public Result<Success, ErrorCode, Error<ErrorCode>> Close() => Results.Ok(new Success());

    /// <summary>
    /// 把共享记忆里的解投给一个子会话 / Feed one remembered solution into a child session.
    /// </summary>
    /// <param name="child">子会话 / The child session.</param>
    /// <param name="budget">片段预算 / The slice budget.</param>
    /// <param name="candidates">候选收集器 / The candidate collector.</param>
    /// <param name="begin">片段起始用量 / The slice's starting usage.</param>
    /// <returns>导入报告 / The import report.</returns>
    private async Task<Result<SearchStepResult<O, V>, ErrorCode, Error<ErrorCode>>> ImportInto(
        ISearchSession<O, V> child,
        SearchSlice budget,
        List<IIndividual<O, V>> candidates,
        long begin) {
        IReadOnlyList<IIndividual<O, V>> working = _memory.Working;
        if (working.Count == 0) {
            return Results.Ok(new SearchStepResult<O, V>(
                Array.Empty<IIndividual<O, V>>(), 0L, 0L, SearchTermination.SliceComplete));
        }
        long allowance = budget.Evaluations - (budget.Used - begin);
        if (allowance < 0L) {
            allowance = 0L;
        }
        Result<SearchStepResult<O, V>, ErrorCode, Error<ErrorCode>> imported =
            await child.ImportSolutions(
                new[] { new Solution<V>(working[0].Solution) }, budget.Child(allowance, 0L))
                .ConfigureAwait(false);
        if (imported is Ok<SearchStepResult<O, V>, ErrorCode, Error<ErrorCode>> ok) {
            candidates.AddRange(ok.Value.Candidates);
        }
        return imported;
    }

    /// <summary>读子会话快照 / Read the child snapshots.</summary>
    /// <param name="state">状态 / The state.</param>
    /// <param name="children">子快照 / The child snapshots.</param>
    /// <returns>是否成功 / Whether it succeeded.</returns>
    private static bool TryReadChildren(JsonElement state, out JsonElement children) =>
        state.TryGetProperty("children", out children);

    /// <summary>读随机流状态 / Read the random-stream states.</summary>
    /// <param name="state">状态 / The state.</param>
    /// <param name="streams">流状态 / The stream states.</param>
    /// <returns>是否成功 / Whether it succeeded.</returns>
    private static bool TryReadStreams(JsonElement state, out JsonElement streams) =>
        state.TryGetProperty("random", out streams);

    /// <summary>读游标 / Read the cursor.</summary>
    /// <param name="state">状态 / The state.</param>
    /// <param name="cursor">游标 / The cursor.</param>
    /// <returns>是否成功 / Whether it succeeded.</returns>
    private static bool TryReadCursor(JsonElement state, out int cursor) {
        cursor = 0;
        return state.TryGetProperty("cursor", out JsonElement element)
            && element.ValueKind == JsonValueKind.Number
            && element.TryGetInt32(out cursor);
    }

    /// <summary>读已完成步数 / Read the completed steps.</summary>
    /// <param name="state">状态 / The state.</param>
    /// <param name="steps">步数 / The steps.</param>
    /// <returns>是否成功 / Whether it succeeded.</returns>
    private static bool TryReadSteps(JsonElement state, out long steps) {
        steps = 0L;
        return state.TryGetProperty("steps", out JsonElement element)
            && element.ValueKind == JsonValueKind.Number
            && element.TryGetInt64(out steps);
    }

    /// <summary>读初始化标记 / Read the initialised flags.</summary>
    /// <param name="state">状态 / The state.</param>
    /// <param name="initialized">标记 / The flags.</param>
    /// <returns>是否成功 / Whether it succeeded.</returns>
    private static bool TryReadInitialized(JsonElement state, out bool[] initialized) {
        initialized = Array.Empty<bool>();
        if (!state.TryGetProperty("initialized", out JsonElement element)
            || element.ValueKind != JsonValueKind.Array) {
            return false;
        }
        var flags = new List<bool>(element.GetArrayLength());
        foreach (JsonElement item in element.EnumerateArray()) {
            if (item.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) {
                return false;
            }
            flags.Add(item.GetBoolean());
        }
        initialized = flags.ToArray();
        return true;
    }
}
