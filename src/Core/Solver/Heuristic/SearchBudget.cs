// 启发式搜索预算与随机流 / Heuristic search budget and random stream.
// 与固定 Kotlin （4888 字节）对应。

using System;
using System.Collections.Generic;
using System.Threading;
using Fuookami.Ospf.Math.Algebra.Number;

namespace Fuookami.Ospf.Core.Solver.Heuristic;

/// <summary>
/// 全局评估令牌，所有子会话共享 / Global evaluation tokens shared by every child session.
/// </summary>
/// <remarks>
/// **领取必须是原子的**。多个并行子会话同时评估时，先读后写的判定会让两个会话在"还剩一个令牌"时同时通过，总评估次数越过上限。
/// 这类越界不会报错，只会让"预算"这个语义失效。
/// **Acquisition must be atomic.** With several child sessions evaluating in parallel, a read-then-write check lets two sessions
/// pass on the last remaining token, pushing the total past the limit. That overrun raises nothing; it merely voids the meaning of
/// "budget".
/// **`ObjectiveCalls` 与 `Used` 是两个不同的量**。`Used` 是**被批准**的评估次数，`ObjectiveCalls` 是**真正调用模型目标**的次数；
/// 候选被判定为不可行时前者增加而后者不增加。把两者混为一谈会让"目标调用成本"的度量凭空变高。
/// **`ObjectiveCalls` and `Used` measure different things.** `Used` counts *admitted* evaluations, `ObjectiveCalls` counts actual
/// model-objective invocations; an infeasible candidate increments the former only. Conflating them inflates any cost metric built
/// on objective calls.
/// </remarks>
public sealed class SearchBudget {
    private long _used;
    private long _objectiveCalls;

    /// <summary>
    /// 构造全局评估令牌 / Construct the global evaluation budget.
    /// </summary>
    /// <param name="limit">评估上限 / The evaluation limit.</param>
    /// <param name="consumed">已消费额度 / The tokens already consumed.</param>
    /// <param name="objectiveCalls">已发生的目标调用次数 / The objective invocations already made.</param>
    public SearchBudget(long limit, long consumed = 0, long objectiveCalls = 0) {
        Limit = limit;
        _used = consumed;
        _objectiveCalls = objectiveCalls;
    }

    /// <summary>评估上限 / The evaluation limit.</summary>
    public long Limit { get; }

    /// <summary>已使用额度 / The consumed tokens.</summary>
    public long Used => Interlocked.Read(ref _used);

    /// <summary>实际模型目标调用次数 / The actual model-objective invocations.</summary>
    public long ObjectiveCalls => Interlocked.Read(ref _objectiveCalls);

    /// <summary>
    /// 记录一次模型目标调用 / Record one model-objective invocation.
    /// </summary>
    public void ObjectiveCalled() => Interlocked.Increment(ref _objectiveCalls);

    /// <summary>
    /// 原子领取一个评估令牌 / Atomically claim one evaluation token.
    /// </summary>
    /// <returns>是否成功 / Whether the claim succeeded.</returns>
    public bool Acquire() {
        while (true) {
            long current = Interlocked.Read(ref _used);
            if (current >= Limit) {
                return false;
            }
            // 只有 CAS 成功才真正消费；失败说明别的会话抢先一步，重读后再判定。
            // Only a successful CAS consumes; a failure means another session got there first, so re-read and re-check.
            if (Interlocked.CompareExchange(ref _used, current + 1, current) == current) {
                return true;
            }
        }
    }
}

/// <summary>
/// 单会话片段，不能并发复用 / A single-session slice, not reusable concurrently.
/// </summary>
/// <remarks>
/// **片段额度、全局额度与父级额度三者同时生效**。只查自己那一份会让嵌套片段把父级额度吃掉，而父级以为还有余量。
/// **The slice allowance, the global budget and the parent allowance all apply at once.** Checking only the slice would let a
/// nested slice consume the parent's allowance while the parent still believes it has room.
/// **`Remaining` 只是视图，不能代替原子领取**。它给调用方一个"还值不值得继续"的判断依据，但真正发放令牌必须走
/// <see cref="Acquire"/>；用 `Remaining` 做准入会重新引入"先读后写"的竞态。
/// **`Remaining` is a view only and never replaces atomic admission.** It tells the caller whether continuing is worthwhile, but
/// actually granting a token must go through <see cref="Acquire"/>; admitting on `Remaining` reintroduces the read-then-write race.
/// **`Expired` 会向父级传播**。父级超时后子级继续跑，会让协作式时限形同虚设。
/// **`Expired` propagates to children.** A child continuing after the parent timed out would make the cooperative deadline
/// meaningless.
/// </remarks>
public sealed class SearchSlice {
    private readonly System.Diagnostics.Stopwatch _stopwatch = System.Diagnostics.Stopwatch.StartNew();
    private readonly object _gate = new();
    private long _used;

    /// <summary>
    /// 构造单会话片段 / Construct a single-session slice.
    /// </summary>
    /// <param name="global">全局令牌 / The global budget.</param>
    /// <param name="evaluations">本片段额度 / The slice allowance.</param>
    /// <param name="steps">最大完整步数 / The maximum complete steps.</param>
    /// <param name="duration">协作式时间限制 / The cooperative time limit.</param>
    /// <param name="parent">父级片段 / The parent slice.</param>
    public SearchSlice(
        SearchBudget global,
        long evaluations,
        long steps = 1,
        TimeSpan? duration = null,
        SearchSlice? parent = null) {
        ArgumentNullException.ThrowIfNull(global);
        Global = global;
        Evaluations = evaluations;
        Steps = steps;
        Duration = duration ?? TimeSpan.MaxValue;
        Parent = parent;
    }

    /// <summary>全局令牌 / The global budget.</summary>
    public SearchBudget Global { get; }

    /// <summary>本片段额度 / The slice allowance.</summary>
    public long Evaluations { get; }

    /// <summary>最大完整步数 / The maximum complete steps.</summary>
    public long Steps { get; }

    /// <summary>协作式时间限制 / The cooperative time limit.</summary>
    public TimeSpan Duration { get; }

    /// <summary>父级片段 / The parent slice.</summary>
    public SearchSlice? Parent { get; }

    /// <summary>本片段已用请求 / The requests consumed by this slice.</summary>
    public long Used => Interlocked.Read(ref _used);

    /// <summary>协作式剩余额度视图 / The cooperative remaining-allowance view.</summary>
    public long Remaining {
        get {
            long local = global::System.Math.Max(Evaluations - Used, 0);
            long global = global::System.Math.Max(Global.Limit - Global.Used, 0);
            long parent = Parent?.Remaining ?? long.MaxValue;
            return global::System.Math.Min(global::System.Math.Min(local, global), parent);
        }
    }

    /// <summary>时间额度是否用尽 / Whether the deadline expired.</summary>
    public bool Expired => _stopwatch.Elapsed >= Duration || Parent?.Expired == true;

    /// <summary>
    /// 创建嵌套片段，评估同时记入父级 / Create a nested slice that also charges its parent.
    /// </summary>
    /// <param name="evaluations">子额度 / The child allowance.</param>
    /// <param name="steps">子步数 / The child steps.</param>
    /// <returns>子片段 / The child slice.</returns>
    public SearchSlice Child(long evaluations, long steps) =>
        new(Global, evaluations, steps, Duration, this);

    /// <summary>
    /// 申请请求，只有成功时才消费额度 / Admit a request, consuming allowance only on success.
    /// </summary>
    /// <returns>是否允许评估 / Whether the evaluation is admitted.</returns>
    public bool Acquire() {
        // 锁保护本片段的计数与父级链路：父级 `Acquire` 是可重入路径，必须在同一临界区内完成判定与扣减。
        // The lock guards this slice's counter and the parent chain: the parent `Acquire` is a re-entrant path, so the decision and
        // the decrement must happen in one critical section.
        lock (_gate) {
            if (Expired || Used >= Evaluations) {
                return false;
            }
            bool parentGranted = Parent is null ? Global.Acquire() : Parent.Acquire();
            if (!parentGranted) {
                return false;
            }
            Interlocked.Increment(ref _used);
            return true;
        }
    }
}

/// <summary>
/// SplitMix64 随机流，状态可无损保存 / A SplitMix64 stream whose state can be saved losslessly.
/// </summary>
/// <remarks>
/// **不用 `System.Random`**。它不承诺跨运行时版本的序列稳定，也不暴露状态；而重放要求"同一状态必然给出同一序列"，否则检查点恢复后的
/// 轨迹与原始运行不一致，且差异极难归因。
/// **`System.Random` is not used.** It guarantees no sequence stability across runtime versions and exposes no state; replay
/// requires "the same state yields the same sequence", or a restored checkpoint diverges from the original run in a way that is
/// extremely hard to attribute.
/// **`Split` 不消费父流**。派生组件若推进父流，那么"新增一个组件"就会改变已有组件的全部随机数；不消费父流让组件之间互不干扰。
/// **`Split` never consumes the parent stream.** If deriving advanced the parent, adding one component would change every existing
/// component's numbers; leaving the parent untouched keeps components independent.
/// </remarks>
public sealed class SearchRandom {
    private const ulong Gamma = 0x9E3779B97F4A7C15UL;
    private const ulong MixOne = 0xBF58476D1CE4E5B9UL;
    private const ulong MixTwo = 0x94D049BB133111EBUL;
    private const ulong FnvPrime = 1099511628211UL;
    private const double TwoToNegative53 = 1.0 / 9007199254740992.0;

    /// <summary>
    /// 构造随机流 / Construct the random stream.
    /// </summary>
    /// <param name="state">初始状态 / The initial state.</param>
    public SearchRandom(long state) => State = state;

    /// <summary>当前状态 / The current state.</summary>
    public long State { get; private set; }

    /// <summary>
    /// 取下一个 [0, 1) 双精度值 / Draw the next value in [0, 1).
    /// </summary>
    /// <returns>随机值 / The random value.</returns>
    public Flt64 Invoke() => new(NextDouble());

    /// <summary>
    /// 取下一个 [0, 1) 双精度值 / Draw the next value in [0, 1).
    /// </summary>
    /// <returns>随机值 / The random value.</returns>
    public double NextDouble() {
        unchecked {
            State += (long)Gamma;
            ulong bits = (ulong)State;
            bits = (bits ^ (bits >> 30)) * MixOne;
            bits = (bits ^ (bits >> 27)) * MixTwo;
            bits ^= bits >> 31;
            // 取高 53 位映射到 [0,1)：`2^53` 是可精确表示的最大区间，低 11 位必须丢弃而不是参与除法。
            // Take the high 53 bits: `2^53` is the largest exactly representable range, and the low 11 bits must be discarded rather
            // than folded into the division.
            return (bits >> 11) * TwoToNegative53;
        }
    }

    /// <summary>
    /// 恢复流状态 / Restore the stream state.
    /// </summary>
    /// <param name="state">要恢复的状态 / The state to restore.</param>
    public void Restore(long state) => State = state;

    /// <summary>
    /// 派生稳定子流，不消费父流 / Derive a stable child stream without consuming the parent.
    /// </summary>
    /// <param name="id">稳定组件标识 / The stable component identifier.</param>
    /// <returns>子随机流 / The child random stream.</returns>
    public SearchRandom Split(string id) {
        ArgumentNullException.ThrowIfNull(id);
        unchecked {
            long seed = State;
            foreach (char character in id) {
                seed = (seed ^ character) * (long)FnvPrime;
            }
            return new SearchRandom(seed);
        }
    }
}
