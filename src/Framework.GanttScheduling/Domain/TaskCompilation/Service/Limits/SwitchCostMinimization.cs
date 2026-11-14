#nullable enable

using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model;
using Fuookami.Ospf.Framework.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Service.Limits;
/// <summary>
/// 切换成本最小化 / Switch cost minimization
/// </summary>
/// <typeparam name="T">任务类型 / Task type</typeparam>
/// <typeparam name="E">执行器类型 / Executor type</typeparam>
/// <typeparam name="A">分配策略类型 / Assignment policy type</typeparam>
public class SwitchCostMinimization<T, E, A>
    : ICGPipeline<IGanttSchedulingShadowPriceArguments<E, A>, object, GanttSchedulingShadowPriceMap<E, A>>
    where T : IAbstractTask<E, A>
    where E : Executor
    where A : IAssignmentPolicy<E> {
    private readonly IReadOnlyList<E> _executors;
    private readonly IReadOnlyList<T> _tasks;
    private readonly ISwitch _switch;
    private readonly Func<(E Executor, T From, T To), Flt64?> _coefficient;
    private readonly string _name;

    /// <summary>
    /// 切换成本最小化构造 / Switch cost minimization constructor
    /// </summary>
    /// <param name="executors">执行器列表 / List of executors</param>
    /// <param name="tasks">任务列表 / List of tasks</param>
    /// <param name="switch">切换对象 / Switch object</param>
    /// <param name="coefficient">换型成本系数提取器；`null` 表示每个三元组 1 / Extractor for the switch cost; null means 1 per triple</param>
    /// <param name="name">管道名称 / Pipeline name</param>
    public SwitchCostMinimization(
        IReadOnlyList<E> executors,
        IReadOnlyList<T> tasks,
        ISwitch @switch,
        Func<(E Executor, T From, T To), Flt64?>? coefficient = null,
        string name = "switch_cost_minimization") {
        _executors = executors;
        _tasks = tasks;
        _switch = @switch;
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
    /// </remarks>
    public Try Invoke(object model) {
        ArgumentNullException.ThrowIfNull(model);

        if (model is not Fuookami.Ospf.Core.Model.Mechanism.IAbstractLinearMetaModel<Flt64> linear) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "换型成本最小化只能施加到线性元模型 / switch-cost minimisation can only be applied to a linear meta model"));
        }
        if (_switch is not ISwitchTables<T, E> { SwitchTable: IReadOnlyDictionary<(E Executor, T From, T To), object> table }) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.ApplicationError,
                "换型尚未注册，取不到换型指示表 / the switch has not been registered, so its indicator table is unavailable"));
        }

        var monomials = new List<Fuookami.Ospf.Math.Symbol.Monomial.LinearMonomial<Flt64>>();
        Flt64 constant = Flt64.Zero;
        foreach (E executor in _executors) {
            foreach (T from in _tasks) {
                foreach (T to in _tasks) {
                    if (!table.TryGetValue((executor, from, to), out object? symbol)) {
                        return Results.Failed<Success>(new Err<ErrorCode>(
                            ErrorCode.ApplicationError,
                            "换型指示表缺项：" + executor.Id + "/" + from.Id + "/" + to.Id
                            + " / the switch indicator table is missing an entry: " + executor.Id + "/" + from.Id + "/" + to.Id));
                    }
                    Flt64? weight = _coefficient((executor, from, to));
                    if (weight is not { } coefficient) {
                        return Results.Failed<Success>(new Err<ErrorCode>(
                            ErrorCode.ApplicationError,
                            "换型成本系数提取失败：" + executor.Id + "/" + from.Id + "/" + to.Id
                            + " / the switch-cost coefficient could not be extracted: " + executor.Id + "/" + from.Id + "/" + to.Id));
                    }
                    var polynomial = (Fuookami.Ospf.Core.Symbol.ILinearIntermediateSymbol<Flt64>)symbol;
                    foreach (Fuookami.Ospf.Math.Symbol.Monomial.LinearMonomial<Flt64> monomial in polynomial.Polynomial.Monomials) {
                        monomials.Add(new Fuookami.Ospf.Math.Symbol.Monomial.LinearMonomial<Flt64>(
                            coefficient * monomial.Coefficient, monomial.Symbol));
                    }
                    constant = constant + coefficient * polynomial.Polynomial.Constant;
                }
            }
        }
        if (monomials.Count == 0 && constant.Eq(Flt64.Zero)) {
            // 全是常量零（对角线与非相邻对）→ 目标恒零，加它只占一个目标位。
            // Everything is constant zero (the diagonal and non-adjacent pairs) → the objective is identically zero; adding it only occupies an objective slot.
            return Results.Ok<Success>(Results.SuccessInstance);
        }

        return linear.AddObject(
            Fuookami.Ospf.Core.Model.Basic.ObjectCategory.Minimum,
            new Fuookami.Ospf.Core.Symbol.Flatten.LinearFlattenData<Flt64>(monomials.ToArray(), constant),
            "switch cost",
            displayName: null);
    }
}
