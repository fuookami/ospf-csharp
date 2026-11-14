#nullable enable

using System;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Core.Solver.Heuristic;
/// <summary>
/// 启发式策略抽象接口。
/// Heuristic policy abstraction interface.
/// </summary>
public interface IAbstractHeuristicPolicy {
    /// <summary>策略名称 / Policy name</summary>
    string Name { get; }
    /// <summary>最大迭代次数 / Maximum iterations</summary>
    int MaxIterations { get; }
    /// <summary>种群大小 / Population size</summary>
    int PopulationSize { get; }
    /// <summary>无改进最大迭代次数 / Maximum iterations without improvement</summary>
    int MaxIterationsWithoutImprovement { get; }
}

/// <summary>
/// 启发式策略基类 / Heuristic policy base class.
/// </summary>
public abstract class HeuristicPolicy : IAbstractHeuristicPolicy {
    /// <inheritdoc/>
    public abstract string Name { get; }
    /// <inheritdoc/>
    public virtual int MaxIterations { get; init; } = 1000;
    /// <inheritdoc/>
    public virtual int PopulationSize { get; init; } = 100;
    /// <inheritdoc/>
    public virtual int MaxIterationsWithoutImprovement { get; init; } = 100;

    /// <summary>
    /// 判断搜索是否结束 / Check if the search should terminate.
    /// </summary>
    /// <param name="iteration">当前迭代状态 / current iteration state</param>
    /// <returns>是否结束 / whether finished</returns>
    public virtual bool Finished(Iteration iteration) =>
        iteration.Current >= MaxIterations
        || iteration.NotBetterIteration >= MaxIterationsWithoutImprovement;
}

/// <summary>
/// 启发式迭代计数器，跟踪迭代次数、无改进迭代与有效运行时间。
/// Heuristic iteration counter, tracking the iteration count, no-improvement iterations and active runtime.
/// </summary>
/// <remarks>
/// **计时可以被暂停，迭代计数不会**。暂停只冻结"有效运行时间"，`Current` 与 `NotBetterIteration` 照常推进——把两者一起冻结会让
/// "按迭代次数设定预算"的收敛判据在暂停期间静默失效。
/// **Timing can pause; the counters cannot.** Pausing freezes only the active runtime, while `Current` and `NotBetterIteration`
/// keep advancing. Freezing both would silently break convergence criteria expressed as an iteration budget during the pause.
/// **暂停时结算已累计的有效时间**，恢复时重新打点；否则中间的空转时间会在恢复后被算成有效时间，而调用方暂停正是为了排除它。
/// **A pause settles the active time accumulated so far** and resuming re-marks. Otherwise the idle interval would count as active
/// after resuming — and excluding that interval is exactly why the caller paused.
/// **`Time` 是实时读数**，不是只在 `Next` 时更新；否则在长时间不推进迭代的空档里，超时判据读到的是过期值。
/// **`Time` is a live reading**, not updated only inside `Next`; otherwise a timeout check reading it during a long gap between
/// iterations would see a stale value.
/// **与基线的有意偏离**：基线用 `UInt64` 计数并支持构造时恢复计数与耗时；这里保留 `int` 计数，因为全部调用点都与 `int` 预算比较，
/// 换成 `UInt64` 会牵动整条调用链且没有行为收益。恢复语义只保留耗时，计数通过 `Reset` 归零。
/// **Deliberate divergence from the baseline**: the baseline counts with `UInt64` and restores both counters and elapsed time through
/// its constructor. This port keeps `int` counters because every call site compares against `int` budgets; switching to `UInt64`
/// would ripple through the whole chain with no behavioural gain. Only elapsed time is restorable, and `Reset` zeroes the counters.
/// </remarks>
public class Iteration {
    private readonly System.Diagnostics.Stopwatch _stopwatch = new();
    private TimeSpan _accumulated;
    private bool _running;

    /// <summary>
    /// 初始化迭代计数器 / Initialize the iteration counter.
    /// </summary>
    /// <param name="elapsed">恢复的有效运行时间 / The restored active runtime.</param>
    public Iteration(TimeSpan elapsed = default) {
        Current = 0;
        NotBetterIteration = 0;
        _accumulated = elapsed;
        _running = true;
        _stopwatch.Start();
    }

    /// <summary>当前迭代 / Current iteration</summary>
    public int Current { get; private set; }

    /// <summary>无改进迭代次数 / Iterations without improvement</summary>
    public int NotBetterIteration { get; private set; }

    /// <summary>已用有效时间 / Elapsed active time</summary>
    public TimeSpan Time => _running ? _accumulated + _stopwatch.Elapsed : _accumulated;

    /// <summary>
    /// 推进到下一次迭代 / Advance to the next iteration.
    /// </summary>
    /// <param name="better">是否有所改进 / Whether improved</param>
    public void Next(bool better) {
        Current++;
        if (better) {
            NotBetterIteration = 0;
        }
        else {
            NotBetterIteration++;
        }
    }

    /// <summary>
    /// 暂停有效计时，不影响迭代计数 / Pause the active timing without changing the counters.
    /// </summary>
    public void Pause() {
        if (!_running) {
            return;
        }
        _accumulated = Time;
        _stopwatch.Reset();
        _running = false;
    }

    /// <summary>
    /// 恢复有效计时 / Resume the active timing.
    /// </summary>
    public void Resume() {
        if (_running) {
            return;
        }
        _stopwatch.Restart();
        _running = true;
    }

    /// <summary>
    /// 从检查点恢复计数与计时 / Restore the counters and the timing from a checkpoint.
    /// </summary>
    /// <remarks>
    /// **必须整体恢复三个量，不能只恢复迭代数**。只恢复 `Current` 会让停滞计数从 0 重新开始，于是恢复后的运行比原始运行更"乐观"，
    /// **而两条轨迹的差异出现在下一次停滞判据触发时，距离恢复点已经很远**。
    /// **All three quantities must be restored together, not just the iteration count.** Restoring only `Current` restarts the
    /// stagnation count from 0, making the restored run more "optimistic" than the original, **and the divergence surfaces at the next
    /// stagnation-criterion trigger, far from the restore point**.
    /// **负计数被拒绝**。检查点里的负数只可能来自损坏或伪造，静默接受会让一次"恢复成功"的调用拿到一个永远不退缩的停滞计数。
    /// **Negative counters are rejected.** A negative number in a checkpoint can only come from corruption or forgery; accepting it
    /// silently gives a call that reports "restored successfully" a stagnation count that never retracts.
    /// </remarks>
    /// <param name="current">迭代数 / The iteration count.</param>
    /// <param name="notBetter">无改进迭代数 / The iterations without improvement.</param>
    /// <param name="elapsed">已用有效时间 / The elapsed active time.</param>
    /// <returns>恢复结果 / The restore result.</returns>
    public Result<Success, ErrorCode, Error<ErrorCode>> Restore(
        int current, int notBetter, TimeSpan elapsed) {
        if (current < 0 || notBetter < 0 || elapsed < TimeSpan.Zero) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "无效的迭代状态 / Invalid iteration state"));
        }
        Current = current;
        NotBetterIteration = notBetter;
        _accumulated = elapsed;
        _stopwatch.Reset();
        _running = false;
        return Results.Ok(Results.SuccessInstance);
    }

    /// <summary>重置计数与计时 / Reset the counters and the timing.</summary>
    public void Reset() {
        Current = 0;
        NotBetterIteration = 0;
        _accumulated = TimeSpan.Zero;
        _stopwatch.Restart();
        _running = true;
    }
}
