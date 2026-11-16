#nullable enable

using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Framework.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Service.Limits;
/// <summary>
/// 切换时间最小化 / Switch time minimization
/// </summary>
/// <typeparam name="T">任务类型 / Task type</typeparam>
/// <typeparam name="E">执行器类型 / Executor type</typeparam>
/// <typeparam name="A">分配策略类型 / Assignment policy type</typeparam>
public class SwitchTimeMinimization<T, E, A>
    : ICGPipeline<IGanttSchedulingShadowPriceArguments<E, A>, object, GanttSchedulingShadowPriceMap<E, A>>
    where T : IAbstractTask<E, A>
    where E : Executor
    where A : IAssignmentPolicy<E> {
    private readonly SolverTimeWindowBoundary _timeBoundary;
    private readonly IReadOnlyList<T> _tasks;
    private readonly ISwitch _switch;
    private readonly Func<(T From, T To), TimeSpan?> _threshold;
    private readonly Func<(T From, T To), Flt64?> _coefficient;
    private readonly string _name;

    /// <summary>
    /// 切换时间最小化构造 / Switch time minimization constructor
    /// </summary>
    /// <param name="timeWindow">时间窗口 / Time window</param>
    /// <param name="tasks">任务列表 / List of tasks</param>
    /// <param name="switch">切换对象 / Switch object</param>
    /// <param name="threshold">阈值提取器；`null` 表示每对 0 / Threshold extractor; null means zero per pair</param>
    /// <param name="coefficient">换型时间系数提取器；`null` 表示每对 1 / Extractor for the switch time; null means 1 per pair</param>
    /// <param name="name">管道名称 / Pipeline name</param>
    public SwitchTimeMinimization(
        TimeWindow<Flt64> timeWindow,
        IReadOnlyList<T> tasks,
        ISwitch @switch,
        Func<(T From, T To), TimeSpan?>? threshold = null,
        Func<(T From, T To), Flt64?>? coefficient = null,
        string name = "switch_time_minimization") {
        _timeBoundary = new SolverTimeWindowBoundary(timeWindow);
        _tasks = tasks;
        _switch = @switch;
        _threshold = threshold ?? (_ => TimeSpan.Zero);
        _coefficient = coefficient ?? (_ => Flt64.One);
        _name = name;
    }

    /// <summary>
    /// 通过 solver 时间窗口边界创建 / Create from solver time-window boundary
    /// </summary>
    /// <param name="timeBoundary">solver 时间窗口边界 / Solver time-window boundary</param>
    /// <param name="tasks">任务列表 / List of tasks</param>
    /// <param name="switch">切换对象 / Switch object</param>
    /// <param name="threshold">阈值提取器；`null` 表示每对 0 / Threshold extractor; null means zero per pair</param>
    /// <param name="coefficient">换型时间系数提取器；`null` 表示每对 1 / Extractor for the switch time; null means 1 per pair</param>
    /// <param name="name">管道名称 / Pipeline name</param>
    public SwitchTimeMinimization(
        SolverTimeWindowBoundary timeBoundary,
        IReadOnlyList<T> tasks,
        ISwitch @switch,
        Func<(T From, T To), TimeSpan?>? threshold = null,
        Func<(T From, T To), Flt64?>? coefficient = null,
        string name = "switch_time_minimization") {
        _timeBoundary = timeBoundary;
        _tasks = tasks;
        _switch = @switch;
        _threshold = threshold ?? (_ => TimeSpan.Zero);
        _coefficient = coefficient ?? (_ => Flt64.One);
        _name = name;
    }

    string IMetaConstraintGroup.Name => _name;
    bool IMetaConstraintGroup.Lazy => false;

    /// <inheritdoc/>
    /// <remarks>
    /// **未接线就失败，不要静默成功。** 一个返回 `Ok` 的空壳等于宣称"该约束已施加且未违反"——
    /// 而实际什么都没做。这类缺陷不报错、不崩、只是结果偏掉，是最难查的一种。
    ///
    /// **Fails while unwired; never silently succeeds.** An empty stub returning `Ok` claims "this constraint was applied and is satisfied" while doing nothing at all. The
    /// defect raises nothing, crashes nothing and merely skews the result — the hardest kind to find.
    ///
    /// **阈值分支**：阈值非零时先建 `switch_time_threshold_（from）_（to）` 再最小化其正部。与延迟 / 提前目标同形，
    /// 复用 `ThresholdSlack`；**系数在松弛外面乘**，与基线 `coefficient * slack.positiveSlackPolynomial` 一致。
    /// **The threshold branch**: with a non-zero threshold the code builds `switch_time_threshold_ (from)_(to)` first and minimises its positive part, the same shape as the delay and advance
    /// objectives and reusing `ThresholdSlack`; **the coefficient multiplies outside the slack**, matching its `coefficient * slack.positiveSlackPolynomial`.
    /// </remarks>
    public Try Invoke(object model) {
        ArgumentNullException.ThrowIfNull(model);

        if (model is not Fuookami.Ospf.Core.Model.Mechanism.IAbstractLinearMetaModel<Flt64> linear) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "换型时间最小化只能施加到线性元模型 / switch-time minimisation can only be applied to a linear meta model"));
        }
        if (_switch is not ISwitchTables<T, E> { SwitchTimeTable: IReadOnlyDictionary<(T From, T To), object> table }) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.ApplicationError,
                "换型尚未注册，取不到换型时间表 / the switch has not been registered, so its switch-time table is unavailable"));
        }

        var monomials = new List<Fuookami.Ospf.Math.Symbol.Monomial.LinearMonomial<Flt64>>();
        Flt64 constant = Flt64.Zero;
        foreach (T from in _tasks) {
            foreach (T to in _tasks) {
                if (!table.TryGetValue((from, to), out object? symbol)) {
                    return Results.Failed<Success>(new Err<ErrorCode>(
                        ErrorCode.ApplicationError,
                        "换型时间表缺项：" + from.Id + "/" + to.Id
                        + " / the switch-time table is missing an entry: " + from.Id + "/" + to.Id));
                }
                Flt64? weight = _coefficient((from, to));
                if (weight is not { } coefficient) {
                    return Results.Failed<Success>(new Err<ErrorCode>(
                        ErrorCode.ApplicationError,
                        "换型时间系数提取失败：" + from.Id + "/" + to.Id
                        + " / the switch-time coefficient could not be extracted: " + from.Id + "/" + to.Id));
                }
                var polynomial = (Fuookami.Ospf.Core.Symbol.ILinearIntermediateSymbol<Flt64>)symbol;
                Flt64 thisThreshold = _timeBoundary.ValueOf(_threshold((from, to)) ?? TimeSpan.Zero);
                Fuookami.Ospf.Math.Symbol.Polynomial.LinearPolynomial<Flt64> priced;
                if (thisThreshold.ToDouble() == 0.0) {
                    priced = polynomial.Polynomial;
                } else {
                    // 阈值非零：先建一条阈值松弛并加到模型，再对**它的正部**计费。
                    // A non-zero threshold builds a threshold slack, adds it to the model, and prices **its positive part**.
                    Try registered = ThresholdSlack.Register(
                        linear, polynomial.Polynomial, thisThreshold,
                        "switch_time_threshold_" + from.Id + "_" + to.Id,
                        out Fuookami.Ospf.Core.Symbol.Function.LinearFunctionSymbolAdapter<Flt64> slack);
                    if (!registered.IsOk) {
                        return registered;
                    }
                    priced = ThresholdSlack.PositivePart(slack);
                }
                Fuookami.Ospf.Math.Symbol.Polynomial.LinearPolynomial<Flt64> weighted =
                    ThresholdSlack.Scale(priced, coefficient);
                monomials.AddRange(weighted.Monomials);
                constant = constant + weighted.Constant;
            }
        }
        if (monomials.Count == 0 && constant.Eq(Flt64.Zero)) {
            // 没有可计费的换型 → 目标恒零，加它只占一个目标位。
            // No chargeable switch → the objective is identically zero; adding it only occupies an objective slot.
            return Results.Ok<Success>(Results.SuccessInstance);
        }

        return linear.AddObject(
            Fuookami.Ospf.Core.Model.Basic.ObjectCategory.Minimum,
            new Fuookami.Ospf.Core.Symbol.Flatten.LinearFlattenData<Flt64>(monomials.ToArray(), constant),
            "switch time",
            displayName: null);
    }
}
