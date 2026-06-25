#nullable enable

using Fuookami.Ospf.Framework.GanttScheduling.Application.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Fuookami.Ospf.Framework.GanttScheduling.Application.Model.Bunch;
/// <summary>
/// 任务束迭代器 / Bunch iterator
///
/// 跟踪基于任务束的分支定价算法的迭代状态，管理收敛检测、LP/IP 目标跟踪、上下界和最优率计算。
/// Tracks iteration state of bunch-based branch-and-price algorithm, managing convergence detection,
/// LP/IP objective tracking, bounds, and optimal-rate computation.
/// </summary>
/// <typeparam name="T">任务类型 / Task type</typeparam>
/// <typeparam name="E">执行器类型 / Executor type</typeparam>
/// <typeparam name="A">分配策略类型 / Assignment policy type</typeparam>
public class BunchIteration<T, E, A>
    where T : IAbstractTask<E, A>
    where E : Executor
    where A : IAssignmentPolicy<E> {
    private static readonly Flt64 InfValue = new(double.PositiveInfinity);
    private static readonly Flt64 MaxValue = new(double.MaxValue);
    private static readonly Flt64 MinValue = new(-double.MaxValue);

    private readonly Flt64 _initialSlowLpImprovementStep;
    private readonly Flt64 _relativeImprovementStep;
    private readonly ulong _improvementSlowCount;

    private ulong _iteration;
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

    private Flt64 _slowLpImprovementStep;
    private ulong _slowLpImprovementCount;
    private ulong _slowIpImprovementCount;

    private Flt64 _prevLpObj;
    private Flt64 _prevIpObj;

    private Flt64 _bestObj;
    private Flt64 _bestLpObj;
    private Flt64 _bestDualObj;
    private Flt64 _lowerBound;
    private Flt64? _upperBound;

    /// <summary>
    /// 任务束迭代器构造 / Bunch iteration constructor
    /// </summary>
    /// <param name="initialSlowLpImprovementStep">初始慢 LP 目标标量改进步长 / Initial slow LP improvement step</param>
    /// <param name="relativeImprovementStep">相对目标标量改进步长 / Relative improvement step</param>
    /// <param name="improvementSlowCount">改进缓慢计数 / Improvement slow count</param>
    public BunchIteration(
        Flt64? initialSlowLpImprovementStep = null,
        Flt64? relativeImprovementStep = null,
        ulong improvementSlowCount = 5UL) {
        _initialSlowLpImprovementStep = initialSlowLpImprovementStep ?? new Flt64(100.0);
        _relativeImprovementStep = relativeImprovementStep ?? new Flt64(0.01);
        _improvementSlowCount = improvementSlowCount;

        _slowLpImprovementStep = InfValue;
        _slowLpImprovementCount = 0UL;
        _slowIpImprovementCount = 0UL;
        _prevLpObj = MaxValue;
        _prevIpObj = MaxValue;
        _bestObj = MaxValue;
        _bestLpObj = MaxValue;
        _bestDualObj = MinValue;
        _lowerBound = Flt64.Zero;
    }

    /// <summary>当前迭代次数 / Current iteration count</summary>
    public ulong Iteration => _iteration;

    /// <summary>算法运行时长 / Algorithm runtime duration</summary>
    public TimeSpan RunTime => _stopwatch.Elapsed;

    /// <summary>是否改进缓慢 / Whether improvement is slow</summary>
    public bool IsImprovementSlow => _slowIpImprovementCount >= _improvementSlowCount;

    /// <summary>最优率，值域 [0, 1]，越接近 1 越优 / Optimal rate in [0, 1]; closer to 1 means more optimal</summary>
    public Flt64 OptimalRate {
        get {
            Flt64 actualOptimalRate = ((_lowerBound + Flt64.One) / (_bestObj + Flt64.One)).Sqrt();
            if (_upperBound.HasValue) {
                Flt64 ub = _upperBound.Value;
                Flt64 boundRate = (ub - _bestObj) / ub;
                return Min(Flt64.One, Max(actualOptimalRate, boundRate));
            }
            return Min(Flt64.One, actualOptimalRate);
        }
    }

    /// <summary>
    /// 刷新算法下界，约简成本为 branch-and-price 内部目标标量
    /// Refresh lower bound with branch-and-price internal reduced-cost scalar
    /// </summary>
    /// <param name="newBunches">新任务束列表 / List of new bunches</param>
    /// <param name="reducedCost">约简成本标量函数 / Reduced-cost scalar function</param>
    public void RefreshLowerBound(
        IReadOnlyList<AbstractTaskBunch<T, E, A>> newBunches,
        Func<AbstractTaskBunch<T, E, A>, Flt64> reducedCost) {
        var bestReducedCost = new Dictionary<E, Flt64>();
        foreach (AbstractTaskBunch<T, E, A> bunch in newBunches) {
            Flt64 thisReducedCost = reducedCost(bunch);
            if (bunch.Executor is not null) {
                if (bestReducedCost.TryGetValue(bunch.Executor, out Flt64 existing)) {
                    bestReducedCost[bunch.Executor] = thisReducedCost < existing ? thisReducedCost : existing;
                }
                else {
                    bestReducedCost[bunch.Executor] = thisReducedCost;
                }
            }
        }

        Flt64 sum = Flt64.Zero;
        foreach (Flt64 v in bestReducedCost.Values) {
            sum = sum + v;
        }
        Flt64 currentDualObj = _prevLpObj + sum;
        if (_bestDualObj < currentDualObj && currentDualObj < _bestObj) {
            _bestDualObj = currentDualObj;
            if (_lowerBound < currentDualObj) {
                _lowerBound = currentDualObj;
            }
        }
    }

    /// <summary>
    /// 刷新 LP 目标标量 / Refresh LP objective scalar
    /// </summary>
    /// <param name="obj">LP 目标标量 / LP objective scalar</param>
    /// <return>是否有改进 / Whether improved</return>
    public bool RefreshLpObj(Flt64 obj) {
        if (double.IsPositiveInfinity(_slowLpImprovementStep.ToDouble())) {
            Flt64 relativeStep = obj * _relativeImprovementStep;
            _slowLpImprovementStep = _initialSlowLpImprovementStep > relativeStep
                ? _initialSlowLpImprovementStep
                : relativeStep;
        }
        else {
            if ((_prevLpObj - obj) < _slowLpImprovementStep
                || ((_bestObj - obj) / _bestLpObj) < Flt64.One) {
                ++_slowLpImprovementCount;
            }
            else {
                _slowLpImprovementCount = 0UL;
            }
        }

        _prevLpObj = obj;

        bool flag = false;
        if (obj < _bestLpObj) {
            flag = true;
            _bestLpObj = obj;
            if (_bestLpObj < _lowerBound) {
                _lowerBound = _bestLpObj;
            }
        }
        return flag;
    }

    /// <summary>
    /// 刷新 IP 目标标量 / Refresh IP objective scalar
    /// </summary>
    /// <param name="obj">IP 目标标量 / IP objective scalar</param>
    /// <return>是否有改进 / Whether improved</return>
    public bool RefreshIpObj(Flt64 obj) {
        if ((_prevIpObj - obj).Abs() < new Flt64(0.01)
            || ((_bestObj - obj) / _bestObj) < new Flt64(0.01)) {
            ++_slowIpImprovementCount;
        }
        else {
            _slowIpImprovementCount = 0UL;
        }
        _prevIpObj = obj;
        if (!_upperBound.HasValue) {
            _upperBound = obj;
        }

        bool flag = false;
        if (obj < _bestObj) {
            flag = true;
            _bestObj = obj;
            if (_bestObj < _lowerBound) {
                _lowerBound = _bestObj;
            }
        }
        return flag;
    }

    /// <summary>减半步长 / Halve step</summary>
    public void HalveStep() => _slowLpImprovementStep = _slowLpImprovementStep / new Flt64(2.0);

    /// <summary>
    /// 生成泛型迭代快照 / Build a generic iteration snapshot
    /// </summary>
    /// <typeparam name="V">目标数值类型 / Target numeric type</typeparam>
    /// <param name="adapter">solver 数值适配器 / Solver value adapter</param>
    /// <param name="unit">目标值单位 / Objective unit</param>
    /// <return>泛型迭代快照 / Generic iteration snapshot</return>
    public IterationSnapshot<V> Snapshot<V>(
        ISchedulingSolverValueAdapter<V> adapter,
        PhysicalUnit? unit = null)
        where V : struct, IRealNumber<V> {
        unit ??= NoneUnit.Instance;
        return new IterationSnapshot<V>(
            Iteration: _iteration,
            RunTime: RunTime,
            BestObjective: MakeQuantity(_bestObj, adapter, unit),
            BestLpObjective: MakeQuantity(_bestLpObj, adapter, unit),
            BestDualObjective: MakeQuantity(_bestDualObj, adapter, unit),
            LowerBound: MakeQuantity(_lowerBound, adapter, unit),
            UpperBound: _upperBound.HasValue ? MakeQuantity(_upperBound.Value, adapter, unit) : null,
            SlowLpImprovementStep: double.IsPositiveInfinity(_slowLpImprovementStep.ToDouble())
                ? null
                : MakeQuantity(_slowLpImprovementStep, adapter, unit),
            OptimalRate: adapter.FromFlt64(OptimalRate),
            IsImprovementSlow: IsImprovementSlow
        );
    }

    /// <summary>递增迭代次数 / Increment iteration count</summary>
    public void Increment() => ++_iteration;

    /// <summary>递减迭代次数 / Decrement iteration count</summary>
    public void Decrement() => --_iteration;

    /// <summary>返回迭代次数的字符串表示 / Return string representation of iteration count</summary>
    public override string ToString() => $"{_iteration}";

    private static Quantity<V> MakeQuantity<V>(
        Flt64 value,
        ISchedulingSolverValueAdapter<V> adapter,
        PhysicalUnit unit)
        where V : struct, IRealNumber<V> => new Quantity<V>(adapter.FromFlt64(value), unit);

    private static Flt64 Min(Flt64 a, Flt64 b) => a < b ? a : b;
    private static Flt64 Max(Flt64 a, Flt64 b) => a > b ? a : b;
}
