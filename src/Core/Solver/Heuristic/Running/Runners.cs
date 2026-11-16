// 运行器基类与轨迹运行器 / The runner base class and the trajectory runner.
// 同族的（11692 B）与其余 runner 单独成批。
// , (7017 B) and
// (4185 B). The same family's (11692 B) and the remaining runners come in
// their own batches.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fuookami.Ospf.Core.Model.Callback;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator.Rl;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Core.Solver.Heuristic.Running;

/// <summary>
/// 迭代观察者 / An iteration observer.
/// </summary>
/// <remarks>
/// **`Reset` 与 `Observe` 是两个独立契约**：`Reset` 在会话打开与独立运行开始时被调用，`Observe` 在每次迭代后被调用。
/// 把 `Reset` 写成"清空内部状态"而不是"重置计数器"，会让一次带检查点的恢复丢掉**本该保留**的累计量。
/// **`Reset` and `Observe` are two separate contracts**: `Reset` runs when a session opens and when a standalone run starts, and
/// `Observe` runs after each iteration. Implementing `Reset` as "clear internal state" rather than "reset counters" makes a
/// checkpointed restore drop accumulations that **should have been kept**.
/// </remarks>
public interface IIterationObserverContract {
    /// <summary>重置 / Reset.</summary>
    void Reset() { }

    /// <summary>
    /// 观察一次迭代 / Observe one iteration.
    /// </summary>
    /// <typeparam name="ObjValue">目标值类型 / The objective value type.</typeparam>
    /// <typeparam name="V">值类型 / The value type.</typeparam>
    /// <param name="iteration">当前迭代 / The current iteration.</param>
    /// <param name="members">本步成员 / The step's members.</param>
    /// <param name="improved">档案是否改善 / Whether the archive improved.</param>
    /// <param name="converter">值转换器 / The value converter.</param>
    void Observe<ObjValue, V>(
        Iteration iteration,
        IReadOnlyList<IIndividual<ObjValue, V>> members,
        bool improved,
        IIntoValue<V> converter)
        where V : struct, IRealNumber<V>, INumberField<V>;
}

/// <summary>
/// 把迭代观测者适配成运行器的观测契约 / Adapt an iteration observer to the runner's observer contract.
/// </summary>
/// <remarks>
/// **两个接口的 `Observe` 签名逐字相同，却没有任何一层负责把它们接起来**。这不是设计冗余，而是"契约接口"与"算子接口"分层的结果：
/// 前者给运行器用（可以有默认的 `Reset`），后者给算子用（更小的依赖面）。
///
/// **少了这个适配器，`PopulationRunner` 就收不到任何观测者**——于是 Q 学习这类"靠观测驱动"的算法
/// **照常跑完、照常给出结果，Q 表却永远是初始值**。适配器本身只是一行转发，但它决定了那套机制是否存在。
///
/// **The two interfaces have literally identical `Observe` signatures, yet nothing was wiring them together.** That is not redundancy but the
/// consequence of layering: a runner-facing contract (which can carry a default `Reset`) and an operator-facing interface (a smaller dependency
/// surface).
///
/// **Without this adapter `PopulationRunner` receives no observer at all** — so an observation-driven algorithm such as Q-learning
/// **runs to completion and returns in its usual shape while the Q table stays at its initial values**. The adapter is a one-line forward, but it
/// is what decides whether that mechanism exists.
/// </remarks>
public sealed class ObserverContractAdapter : IIterationObserverContract {
    private readonly IIterationObserver _observer;

    /// <summary>
    /// 构造适配器 / Construct the adapter.
    /// </summary>
    /// <param name="observer">被适配的观测者 / The observer being adapted.</param>
    public ObserverContractAdapter(IIterationObserver observer) {
        System.ArgumentNullException.ThrowIfNull(observer);
        _observer = observer;
    }

    /// <summary>被适配的观测者 / The observer being adapted.</summary>
    public IIterationObserver Observer => _observer;

    /// <inheritdoc/>
    public void Observe<ObjValue, V>(
        Iteration iteration,
        IReadOnlyList<IIndividual<ObjValue, V>> members,
        bool improved,
        IIntoValue<V> converter)
        where V : struct, IRealNumber<V>, INumberField<V> =>
        _observer.Observe(iteration, members, improved, converter);
}

/// <summary>
/// 运行器基类 / The runner base class.
/// </summary>
/// <remarks>
/// **`OpenSession` 与 `Run` 都必须先清空档案并重置观察者**。漏掉任一步会让一次新的运行从上一轮遗留的档案继续，
/// **而首次迭代看起来完全正常**——它只是在一个非空档案上做"改进"判断。
/// **Both `OpenSession` and `Run` must clear the archive and reset the observers.** Missing either step makes a new run continue from the
/// previous run's archive, **while the first iteration looks perfectly normal** — it merely judges "improvement" against a non-empty
/// archive.
/// **`Run` 的独立问题实例把支配判据接到模型的严格比较**，而不是"按目标值数值大小"。用数值大小会让多目标或自定义序的模型
/// 得到一个与自身语义矛盾的档案，**而档案规模仍然正确**。
/// **`Run`'s standalone problem wires dominance to the model's strict comparison** rather than "numeric order of the objective". Numeric
/// order gives a multi-objective or custom-ordered model an archive contradicting its own semantics, **while the archive size stays
/// correct**.
/// **`Run` 在 `NoCandidate` 时正常结束、在 `Failed` 时返回失败**。把两者都当成"正常结束"会让一次内部错误变成一份空结果，
/// **而调用方无法区分"搜索失败"与"没有候选"**。
/// **`Run` terminates normally on `NoCandidate` and returns a failure on `Failed`.** Treating both as "normal termination" turns an
/// internal error into an empty result, **and the caller cannot distinguish "the search failed" from "there were no candidates"**.
/// **`Run` 的循环条件包含 `first`**。写成 `while (!stopping(iteration))` 会让一次"立即停止"的判据在第一次迭代之前就退出，
/// **于是档案永远是空的**。
/// **`Run`'s loop condition includes `first`.** Writing `while (!stopping(iteration))` makes an "stop immediately" criterion exit before
/// the first iteration, **so the archive is always empty**.
/// </remarks>
/// <typeparam name="TState">运行状态类型 / The run-state type.</typeparam>
/// <typeparam name="ObjValue">目标值类型 / The objective value type.</typeparam>
/// <typeparam name="V">值类型 / The value type.</typeparam>
public abstract class AbstractRunner<TState, ObjValue, V> : IHeuristicRunner<ObjValue, V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    /// <summary>
    /// 构造运行器 / Construct the runner.
    /// </summary>
    /// <param name="archive">档案 / The archive.</param>
    /// <param name="stopping">停止判据 / The stopping criterion.</param>
    /// <param name="converter">值转换器 / The value converter.</param>
    /// <param name="observers">迭代观察者 / The iteration observers.</param>
    protected AbstractRunner(
        IArchive<ObjValue, V> archive,
        IStoppingCriterion stopping,
        IIntoValue<V> converter,
        IReadOnlyList<IIterationObserverContract>? observers = null) {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(stopping);
        ArgumentNullException.ThrowIfNull(converter);
        Archive = archive;
        Stopping = stopping;
        Converter = converter;
        Observers = observers ?? Array.Empty<IIterationObserverContract>();
    }

    /// <summary>档案 / The archive.</summary>
    public IArchive<ObjValue, V> Archive { get; }

    /// <summary>停止判据 / The stopping criterion.</summary>
    public IStoppingCriterion Stopping { get; }

    /// <summary>值转换器 / The value converter.</summary>
    public IIntoValue<V> Converter { get; }

    /// <summary>迭代观察者 / The iteration observers.</summary>
    public IReadOnlyList<IIterationObserverContract> Observers { get; }

    /// <summary>
    /// 初始化运行状态 / Initialize the run state.
    /// </summary>
    /// <param name="model">回调模型 / The call-back model.</param>
    /// <param name="iteration">当前迭代 / The current iteration.</param>
    /// <returns>运行状态 / The run state.</returns>
    protected abstract Task<TState> Initialize(
        IAbstractCallBackModelInterface<object?, ObjValue, V> model, Iteration iteration);

    /// <summary>
    /// 推进一步 / Advance one step.
    /// </summary>
    /// <param name="model">回调模型 / The call-back model.</param>
    /// <param name="iteration">当前迭代 / The current iteration.</param>
    /// <param name="state">当前状态 / The current state.</param>
    /// <returns>新状态 / The new state.</returns>
    protected abstract Task<TState> Advance(
        IAbstractCallBackModelInterface<object?, ObjValue, V> model, Iteration iteration, TState state);

    /// <summary>
    /// 取出状态成员 / Extract the state's members.
    /// </summary>
    /// <param name="state">状态 / The state.</param>
    /// <returns>成员列表 / The member list.</returns>
    protected abstract IReadOnlyList<IIndividual<ObjValue, V>> MembersOf(TState state);

    /// <summary>
    /// 初始化步进（内部入口）/ Initialize a step (internal entry).
    /// </summary>
    /// <param name="model">回调模型 / The call-back model.</param>
    /// <param name="iteration">当前迭代 / The current iteration.</param>
    /// <returns>运行状态 / The run state.</returns>
    internal Task<TState> InitializeStep(
        IAbstractCallBackModelInterface<object?, ObjValue, V> model, Iteration iteration) =>
        Initialize(model, iteration);

    /// <summary>
    /// 推进步进（内部入口）/ Advance a step (internal entry).
    /// </summary>
    /// <param name="model">回调模型 / The call-back model.</param>
    /// <param name="iteration">当前迭代 / The current iteration.</param>
    /// <param name="state">当前状态 / The current state.</param>
    /// <returns>新状态 / The new state.</returns>
    internal Task<TState> AdvanceStep(
        IAbstractCallBackModelInterface<object?, ObjValue, V> model, Iteration iteration, TState state) =>
        Advance(model, iteration, state);

    /// <summary>
    /// 取出状态成员（内部入口）/ Extract the state's members (internal entry).
    /// </summary>
    /// <param name="state">状态 / The state.</param>
    /// <returns>成员列表 / The member list.</returns>
    internal IReadOnlyList<IIndividual<ObjValue, V>> StateMembers(TState state) => MembersOf(state);

    /// <summary>
    /// 独立运行 / Run standalone.
    /// </summary>
    /// <remarks>
    /// **独立运行不提供检查点**：它构造的问题实例用不可用的编解码器，所以任何一次快照尝试都会失败并给出明确原因。
    /// 让独立运行"也能快照"会让调用方以为它可恢复，**而恢复时会发现根本没有状态编解码器**。
    /// **A standalone run exposes no checkpoints**: its problem instance uses unavailable codecs, so any snapshot attempt fails with a
    /// clear reason. Letting a standalone run "also snapshot" makes the caller believe it is recoverable, **only to find on restore that
    /// there is no state codec at all**.
    /// **循环条件必须包含"第一次"**。写成 `while (!Stopping.ShouldStop(Iteration))` 会让一个"立即停止"的判据在第一次迭代之前就退出，
    /// **于是档案永远是空的**。
    /// **The loop condition must include "the first iteration".** Writing `while (!Stopping.ShouldStop(Iteration))` makes an
    /// "stop immediately" criterion exit before the first iteration, **so the archive is always empty**.
    /// **`Failed` 终止返回失败、`NoCandidate` 正常结束**。把两者都当成"正常结束"会让一次内部错误变成一份空结果，
    /// **而调用方无法区分"搜索失败"与"没有候选"**。
    /// **A `Failed` termination returns a failure; `NoCandidate` ends normally.** Treating both as "normal termination" turns an internal
    /// error into an empty result, **and the caller cannot distinguish "the search failed" from "there were no candidates"**.
    /// </remarks>
    /// <param name="model">回调模型 / The call-back model.</param>
    /// <param name="iteration">起始迭代 / The starting iteration.</param>
    /// <param name="runningCallBack">每次迭代后的回调 / The per-iteration callback.</param>
    /// <returns>档案个体 / The archive individuals.</returns>
    public async Task<Result<IReadOnlyList<IIndividual<ObjValue, V>>, ErrorCode, Error<ErrorCode>>> Run(
        IAbstractCallBackModelInterface<object?, ObjValue, V> model,
        Iteration? iteration = null,
        Func<Iteration, IReadOnlyList<IIndividual<ObjValue, V>>,
            Result<Success, ErrorCode, Error<ErrorCode>>>? runningCallBack = null) {
        ArgumentNullException.ThrowIfNull(model);
        Iteration current = iteration ?? new Iteration();
        Archive.Clear();
        foreach (IIterationObserverContract observer in Observers) {
            observer.Reset();
        }

        var problem = new SearchProblem<ObjValue, V>(
            "standalone", "standalone", model,
            UnavailableCodec<ObjValue>.Instance,
            UnavailableCodec<V>.Instance,
            (lhs, rhs) => HeuristicOrdering.CompareStrictly(model, lhs, rhs) < 0);

        var session = new RunnerSearchSession<TState, ObjValue, V>(
            this, problem, codec: null, importer: null, constraints: null, iteration: current);
        var budget = new SearchBudget(long.MaxValue);
        bool first = true;
        while (first || !Stopping.ShouldStop(current)) {
            first = false;
            Result<SearchStepResult<ObjValue, V>, ErrorCode, Error<ErrorCode>> result =
                await session.Step(new SearchSlice(budget, long.MaxValue)).ConfigureAwait(false);
            if (result is Failed<SearchStepResult<ObjValue, V>, ErrorCode, Error<ErrorCode>> failed) {
                return Results.Failed<IReadOnlyList<IIndividual<ObjValue, V>>>(failed.Error);
            }
            SearchStepResult<ObjValue, V> report =
                ((Ok<SearchStepResult<ObjValue, V>, ErrorCode, Error<ErrorCode>>)result).Value;
            if (report.Termination == SearchTermination.Failed) {
                return Results.Failed<IReadOnlyList<IIndividual<ObjValue, V>>>(new Err<ErrorCode>(
                    ErrorCode.ApplicationError, string.Join("; ", report.Diagnostics)));
            }
            if (report.Termination == SearchTermination.NoCandidate) {
                break;
            }
            if (runningCallBack is not null) {
                Result<Success, ErrorCode, Error<ErrorCode>> callback;
                try {
                    callback = runningCallBack(current, Archive.Individuals);
                } catch (Exception exception) {
                    return Results.Failed<IReadOnlyList<IIndividual<ObjValue, V>>>(
                        new Err<ErrorCode>(ErrorCode.ApplicationError, exception.Message));
                }
                if (callback is Failed<Success, ErrorCode, Error<ErrorCode>> callbackFailed) {
                    return Results.Failed<IReadOnlyList<IIndividual<ObjValue, V>>>(callbackFailed.Error);
                }
            }
        }
        return Results.Ok(Archive.Individuals);
    }
}

/// <summary>
/// 不可用的状态编解码器 / An unavailable state codec.
/// </summary>
/// <remarks>
/// **独立运行从不提供检查点**，因此它的编解码器一律失败。返回一个"能编码成 `null`"的编解码器会让快照"成功"，
/// **而恢复时才发现没有任何状态被保存**。
/// **A standalone run never offers checkpoints**, so its codecs always fail. Returning one that "encodes to `null`" makes the snapshot
/// "succeed", **and only the restore reveals that no state was saved**.
/// </remarks>
/// <typeparam name="T">值类型 / The value type.</typeparam>
internal sealed class UnavailableCodec<T> : ISearchCodec<T> {
    /// <summary>共享单例 / The shared singleton.</summary>
    public static UnavailableCodec<T> Instance { get; } = new();

    /// <inheritdoc/>
    public Result<System.Text.Json.JsonElement, ErrorCode, Error<ErrorCode>> Encode(T value) =>
        SearchCodecSupport.SearchInvalid<System.Text.Json.JsonElement>(
            "standalone searches do not expose checkpoints");

    /// <inheritdoc/>
    public Result<T, ErrorCode, Error<ErrorCode>> Decode(System.Text.Json.JsonElement value) =>
        SearchCodecSupport.SearchInvalid<T>(
            "standalone searches do not expose checkpoints");
}

/// <summary>
/// 轨迹运行器 / The trajectory runner.
/// </summary>
/// <remarks>
/// **邻域失败时保留当前个体，而不是把状态置空**。置空会让档案在下一步变成空集合并触发 `NoCandidate`，
/// **而搜索会"成功结束"在一个明显还没收敛的位置**。
/// **A failed neighbourhood keeps the current individual rather than clearing the state.** Clearing makes the archive empty on the next
/// step and triggers `NoCandidate`, **so the search "terminates successfully" at a point that is clearly not converged**.
/// **初始化失败返回 `null` 状态**，由会话把它当作"尚未初始化"。返回一个空个体会让第一步就产生一个 `null` 解，
/// **而档案会把它记成一次改善**。
/// **A failed initialization returns a `null` state**, which the session treats as "not yet initialized". Returning an empty individual
/// makes the first step produce a `null` solution, **and the archive records it as an improvement**.
/// </remarks>
/// <typeparam name="ObjValue">目标值类型 / The objective value type.</typeparam>
/// <typeparam name="V">值类型 / The value type.</typeparam>
public sealed class TrajectoryRunner<ObjValue, V>
    : AbstractRunner<IIndividual<ObjValue, V>?, ObjValue, V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    private readonly IInitializer _initializer;
    private readonly INeighborhood _neighborhood;
    private readonly IAcceptance _acceptance;
    private readonly IFitnessEvaluator _fitnessEvaluator;
    private readonly Func<Flt64> _entropy;

    /// <summary>
    /// 构造轨迹运行器 / Construct the trajectory runner.
    /// </summary>
    /// <param name="neighborhood">邻域算子 / The neighbourhood operator.</param>
    /// <param name="entropy">熵源 / The entropy source.</param>
    /// <param name="archive">档案 / The archive.</param>
    /// <param name="stopping">停止判据 / The stopping criterion.</param>
    /// <param name="converter">值转换器 / The value converter.</param>
    /// <param name="initializer">初始化器 / The initializer.</param>
    /// <param name="acceptance">接受策略 / The acceptance policy.</param>
    /// <param name="fitnessEvaluator">适应度评估器 / The fitness evaluator.</param>
    /// <param name="observers">迭代观察者 / The iteration observers.</param>
    public TrajectoryRunner(
        INeighborhood neighborhood,
        Func<Flt64> entropy,
        IArchive<ObjValue, V> archive,
        IStoppingCriterion stopping,
        IIntoValue<V> converter,
        IInitializer? initializer = null,
        IAcceptance? acceptance = null,
        IFitnessEvaluator? fitnessEvaluator = null,
        IReadOnlyList<IIterationObserverContract>? observers = null)
        : base(archive, stopping, converter, observers) {
        ArgumentNullException.ThrowIfNull(neighborhood);
        ArgumentNullException.ThrowIfNull(entropy);
        _neighborhood = neighborhood;
        _entropy = entropy;
        _initializer = initializer ?? ModelInitializer.Instance;
        _acceptance = acceptance ?? BetterOnlyAcceptance.Instance;
        _fitnessEvaluator = fitnessEvaluator ?? DefaultOnMissFitnessEvaluator.Instance;
    }

    /// <summary>邻域算子 / The neighbourhood operator.</summary>
    public INeighborhood Neighborhood => _neighborhood;

    /// <inheritdoc/>
    protected override Task<IIndividual<ObjValue, V>?> Initialize(
        IAbstractCallBackModelInterface<object?, ObjValue, V> model, Iteration iteration) {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(iteration);
        IReadOnlyList<Solution<V>> generated =
            _initializer.Generate<V, object?, ObjValue>(iteration, model, 1UL, Converter);
        if (generated.Count == 0) {
            return Task.FromResult<IIndividual<ObjValue, V>?>(null);
        }
        return Task.FromResult(ToIndividual(model, generated[0]));
    }

    /// <inheritdoc/>
    protected override Task<IIndividual<ObjValue, V>?> Advance(
        IAbstractCallBackModelInterface<object?, ObjValue, V> model,
        Iteration iteration,
        IIndividual<ObjValue, V>? state) {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(iteration);
        if (state is null) {
            return Task.FromResult<IIndividual<ObjValue, V>?>(null);
        }
        Solution<V> neighbour = _neighborhood.Generate<object?, ObjValue, V>(
            iteration, state, model, _entropy, Converter);
        IIndividual<ObjValue, V>? candidate = ToIndividual(model, neighbour);
        if (candidate is null) {
            // **邻域失败保留当前个体**，而不是返回 null。
            // **A failed neighbourhood keeps the current individual** rather than returning null.
            return Task.FromResult<IIndividual<ObjValue, V>?>(state);
        }
        bool accepted = _acceptance.Invoke(model, iteration, state, candidate);
        return Task.FromResult<IIndividual<ObjValue, V>?>(accepted ? candidate : state);
    }

    /// <summary>
    /// 把一个解包装成个体 / Wrap a solution into an individual.
    /// </summary>
    /// <param name="model">回调模型 / The call-back model.</param>
    /// <param name="solution">解 / The solution.</param>
    /// <returns>个体，评估失败时返回 null / The individual, or null when evaluation fails.</returns>
    private IIndividual<ObjValue, V>? ToIndividual(
        IAbstractCallBackModelInterface<object?, ObjValue, V> model, Solution<V> solution) {
        ObjValue? objective = _fitnessEvaluator.Evaluate<object?, ObjValue, V>(model, solution);
        if (objective is null) {
            return null;
        }
        // **`SolutionWithFitness` 的适应度由目标值本身决定**：这里没有独立的适应度函数，用目标值作为适应度即可，
        // 而档案的排序只看 `Objective`。
        // **`SolutionWithFitness`'s fitness derives from the objective itself**: there is no separate fitness function here, so the
        // objective doubles as the fitness, and the archive's ordering only consults `Objective`.
        return new SolutionWithFitness<ObjValue, V>(
            solution.Values, objective, Fitness: 0.0);
    }

    /// <inheritdoc/>
    protected override IReadOnlyList<IIndividual<ObjValue, V>> MembersOf(
        IIndividual<ObjValue, V>? state) =>
        state is null
            ? Array.Empty<IIndividual<ObjValue, V>>()
            : new[] { state };
}
