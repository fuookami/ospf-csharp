#nullable enable

using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Symbol.Flatten;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Framework.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System.Linq;
using System;
using System.Collections.Generic;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Service.Limits;
/// <summary>
/// 最大完工时间最小化 / Makespan minimization
/// </summary>
/// <typeparam name="E">执行器类型 / Executor type</typeparam>
/// <typeparam name="A">分配策略类型 / Assignment policy type</typeparam>
public class MakespanMinimization<E, A>
    : ICGPipeline<IGanttSchedulingShadowPriceArguments<E, A>, object, GanttSchedulingShadowPriceMap<E, A>>
    where E : Executor
    where A : IAssignmentPolicy<E> {
    private readonly SolverTimeWindowBoundary _timeBoundary;
    private readonly object _makespan;
    private readonly DateTimeOffset? _threshold;
    private readonly Flt64 _coefficient;
    private readonly string _name;

    /// <summary>
    /// 最大完工时间最小化构造 / Makespan minimization constructor
    /// </summary>
    /// <param name="timeWindow">时间窗口 / Time window</param>
    /// <param name="makespan">最大完工时间对象 / Makespan object</param>
    /// <param name="threshold">阈值；`null` 取窗口起点 / The threshold; null means the window start.</param>
    /// <param name="coefficient">成本系数 / Cost coefficient</param>
    /// <param name="name">管道名称 / Pipeline name</param>
    public MakespanMinimization(
        TimeWindow<Flt64> timeWindow,
        object makespan,
        DateTimeOffset? threshold = null,
        Flt64? coefficient = null,
        string name = "makespan_minimization") {
        _timeBoundary = new SolverTimeWindowBoundary(timeWindow);
        _makespan = makespan;
        _threshold = threshold;
        _coefficient = coefficient ?? Flt64.One;
        _name = name;
    }

    /// <summary>
    /// 通过 solver 时间窗口边界创建 / Create from solver time-window boundary
    /// </summary>
    /// <param name="timeBoundary">solver 时间窗口边界 / Solver time-window boundary</param>
    /// <param name="makespan">最大完工时间对象 / Makespan object</param>
    /// <param name="threshold">阈值；`null` 取窗口起点 / The threshold; null means the window start.</param>
    /// <param name="coefficient">成本系数 / Cost coefficient</param>
    /// <param name="name">管道名称 / Pipeline name</param>
    public MakespanMinimization(
        SolverTimeWindowBoundary timeBoundary,
        object makespan,
        DateTimeOffset? threshold = null,
        Flt64? coefficient = null,
        string name = "makespan_minimization") {
        _timeBoundary = timeBoundary;
        _makespan = makespan;
        _threshold = threshold;
        _coefficient = coefficient ?? Flt64.One;
        _name = name;
    }

    string IMetaConstraintGroup.Name => _name;
    bool IMetaConstraintGroup.Lazy => false;

    /// <inheritdoc/>
    /// <remarks>
    /// 两条分支都在：
    ///
    /// 1. **阈值等于窗口起点**（数值为 `0`）→ 直接最小化 `coefficient × 完工时间`，名为 `makespan`。
    /// 2. **阈值在窗口内** → 先建一条阈值松弛（`withNegative = false, withPositive = true, threshold = true`，
    ///    名 `makespan_threshold`）并加到模型，再最小化 `coefficient × 松弛的正部`，名字仍是 `makespan`。
    ///
    /// 三处必须沿用：
    ///
    /// - **`withNegative = false`**：带下侧会让松弛在完工时间**小于**阈值时也变大，惩罚方向整个反过来。
    /// - **阈值判定用 `timeBoundary.ValueOf(threshold)` 与 `0` 比较**，不是"阈值等于窗口起点"这种对象比较——
    ///   比较的是转成 solver 数值之后的结果。
    /// - **两条分支的目标名都是 `makespan`**：名字不同会让调用方按名字找目标时漏掉一条分支。
    ///
    /// 松弛**必须先加到模型**再做目标：建一个未被约束的松弛去最小化，等于又造一个安静的空操作（恒为零）。
    /// 函数约束的注册在机制模型成形时发生（`RegisterFunctionConstraints`），所以这里只负责把符号放进去。
    ///
    /// Both branches are present, with **a threshold at the window start** (value `0`) minimising `coefficient × makespan` directly under the name
    /// `makespan`; **a threshold inside the window** first builds a threshold slack (`withNegative = false, withPositive = true, threshold = true`, named `makespan_threshold`), adds it to the model,
    /// and then minimises `coefficient ×` the slack's positive part, still under the name `makespan`. Three things must be copied: **`withNegative = false`**, since a lower side would make the
    /// slack grow when the makespan is *below* the threshold and invert the penalty; **the threshold comparison uses `timeBoundary.ValueOf(threshold)` against `0`** rather than an object comparison
    /// with the window start, because the comparison is on the converted solver value; and **both branches name the objective `makespan`**, since different names would make a caller looking it up by
    /// name miss one branch. The slack **must be added to the model before** it is minimised: minimising an unconstrained slack is another quiet no-op fixed at zero. The function's own constraints are
    /// registered when the mechanism model is built (`RegisterFunctionConstraints`), so this only has to put the symbol in place.
    /// </remarks>
    public Try Invoke(object model) {
        ArgumentNullException.ThrowIfNull(model);

        if (model is not IAbstractLinearMetaModel<Flt64> linear) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "最大完工时间最小化只能施加到线性元模型 / makespan minimisation can only be applied to a linear meta model"));
        }
        if (_makespan is not IMakespan { MakespanSymbol: ILinearIntermediateSymbol<Flt64> symbol }) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.ApplicationError,
                "最大完工时间尚未注册到模型，不能最小化它 / the makespan is not registered in the model, so it cannot be minimised"));
        }

        Flt64 thresholdValue = _timeBoundary.ValueOf(_threshold ?? _timeBoundary.Source.Start);
        if (thresholdValue.ToDouble() == 0.0) {
            // `coefficient` 乘在完工时间上：目标不是"完工时间"本身，而是 `coefficient × 完工时间`。
            // The coefficient multiplies the makespan: the objective is `coefficient × makespan`, not the makespan alone.
            return Minimize(linear, symbol.Polynomial);
        }

        var slack = new SlackFunction<Flt64>(
            x: symbol.Polynomial,
            y: new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), thresholdValue),
            converter: IIntoValue<Flt64>.Identity,
            withNegative: false,
            withPositive: true,
            threshold: true,
            constraint: true,
            name: "makespan_threshold");
        var adapter = new LinearFunctionSymbolAdapter<Flt64>(slack);

        // **先加进模型再最小化**：建一个未被约束的松弛去最小化，等于又造一个恒为零的空操作。
        // **Add it to the model before minimising**: minimising an unconstrained slack is another no-op fixed at zero.
        Try added = linear.Add(adapter);
        if (!added.IsOk) {
            return added;
        }
        // `adapter.Polynomial` 就是松弛的正部：`withNegative = false` 让 `ResultPolynomial` 只剩 `pos` 那一项。
        // `adapter.Polynomial` is exactly the slack's positive part: `withNegative = false` leaves only the `pos` term in `ResultPolynomial`.
        return Minimize(linear, adapter.Polynomial);

        Try Minimize(IAbstractLinearMetaModel<Flt64> target, LinearPolynomial<Flt64> polynomial) {
            LinearFlattenData<Flt64> data = new(
                polynomial.Monomials
                    .Select(monomial => new LinearMonomial<Flt64>(_coefficient * monomial.Coefficient, monomial.Symbol))
                    .ToArray(),
                _coefficient * polynomial.Constant);
            return target.AddObject(ObjectCategory.Minimum, data, "makespan", displayName: null);
        }
    }
}

/// <summary>
/// 提供完工时间符号的对象 / An object that exposes its makespan symbol.
/// </summary>
public interface IMakespan {
    /// <summary>完工时间符号；`null` 表示尚未注册 / The makespan symbol; null means not registered yet.</summary>
    object? MakespanSymbol { get; }
}
