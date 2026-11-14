#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol.Flatten;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Activity.Service;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.Activity.Model;

/// <summary>
/// 排程结果 / A scheduling result.
/// </summary>
/// <param name="Placements">选中的排程 / The chosen placements.</param>
/// <param name="Executions">已执行排程 / The executed placements.</param>
/// <param name="Cancelled">被取消的活动 / The cancelled activities.</param>
/// <param name="Cost">总成本 / The total cost.</param>
/// <param name="CandidateScope">参与求解的候选键 / The candidate keys that took part in the solve.</param>
/// <param name="MaterialInventory">物料库存 / The material inventory.</param>
public sealed record ActivitySchedule<W>(
    IReadOnlyList<ActivityPlacement<W>> Placements,
    IReadOnlyList<ActivityExecution<W>> Executions,
    IReadOnlySet<string> Cancelled,
    Quantity<W> Cost,
    IReadOnlySet<ActivityChoiceKey> CandidateScope,
    IReadOnlyDictionary<string, IReadOnlyList<ActivityInventoryLevel<W>>> MaterialInventory)
    where W : struct, IFloatingNumber<W>;

/// <summary>
/// 活动排程聚合：注册、校验、取解 / The activity scheduling aggregation: register, validate, extract.
/// </summary>
/// <remarks>
/// 与基线 `ActivitySchedulingAggregation` 对齐。三处照抄：
///
/// 1. **只能注册一次**：第二次注册给出具体错误，而不是静默返回成功——静默成功会让调用方以为新模型被建好了。
/// 2. **`minimizeCost` 为真时要求模型是最小化**，为假时**根本不注册成本目标**——两件事，不是一个开关的两种写法。
/// 3. **`ExtractSolution` 要求模型就是注册时那个实例**（引用相等），别人的模型解不出来。
///
/// 与基线的差异：基线是 `ActivitySchedulingAggregation` 的泛型版本，这里**固定为 `Flt64`**。
/// 原因不是省事，而是**下游全是 `Flt64`**：`ActivityCompilation` 的变量是 `BinVar`、行系数是 `Flt64`、
/// 模型是 `LinearMetaModel` 的 `Flt64` 实例。参数化 `V` 只会让 `CompiledActivityProblem` 的泛型版本 与
/// `ActivityCompilation` 之间多出一个永远走不到的分支。**真需要别的数值类型时，连编译层一起参数化。**
///
/// Aligned with the baseline's `ActivitySchedulingAggregation`. Three things copied: **registration happens once**, a second attempt reporting a concrete error rather than silently succeeding, since a
/// silent success would leave the caller believing a new model had been built; **`minimizeCost` requires a minimisation model when true and registers no cost objective at all when false**, two
/// separate things rather than two spellings of one switch; and **`ExtractSolution` requires the very model instance that was registered** (reference equality).
///
/// The difference from the baseline is that this is **fixed to `Flt64`** while the baseline is generic over `V`. That is not a shortcut but a consequence of **the whole downstream being `Flt64`**:
/// `ActivityCompilation` 的变量是 `BinVar`, the row coefficients are `Flt64` and the model is `LinearMetaModel` 的 `Flt64` 实例. Parameterising `V` would only add a branch between
/// `CompiledActivityProblem` 的泛型版本 and `ActivityCompilation` that can never be taken. **When another numeric type is genuinely needed, parameterise the compilation layer along with it.**
/// </remarks>
public sealed class ActivitySchedulingAggregation
{
    private readonly CompiledActivityProblem<Flt64> _data;
    private readonly string _name;
    private bool _attempted;
    private bool _usable;
    private LinearMetaModel<Flt64>? _owner;

    /// <summary>
    /// 构造 / Construct.
    /// </summary>
    /// <param name="data">编译后的问题 / The compiled problem.</param>
    /// <param name="name">命名空间 / The namespace.</param>
    public ActivitySchedulingAggregation(CompiledActivityProblem<Flt64> data, string name) {
        ArgumentNullException.ThrowIfNull(data);
        _data = data;
        _name = name;
        var costs = new List<Flt64>(data.Choices.Count);
        foreach (CompiledActivityChoice<Flt64> choice in data.Choices) {
            costs.Add(choice.Cost);
        }
        Compilation = new ActivityCompilation(data.Choices.Count, costs, name);
    }

    /// <summary>编译结果 / The compilation</summary>
    public ActivityCompilation Compilation { get; }

    /// <summary>是否已成功注册 / Whether registration succeeded</summary>
    public bool IsUsable => _usable;

    /// <summary>
    /// 注册到模型 / Register into a model.
    /// </summary>
    /// <param name="model">模型 / The model.</param>
    /// <param name="minimizeCost">是否最小化成本 / Whether to minimise the cost.</param>
    /// <returns>成功或失败 / Success or failure.</returns>
    public Try Register(LinearMetaModel<Flt64> model, bool minimizeCost = true) {
        ArgumentNullException.ThrowIfNull(model);

        if (_attempted) {
            return Fail("a context registers only once; create a fresh context for another model");
        }
        if (minimizeCost && model.ObjectCategory != ObjectCategory.Minimum) {
            return Fail("the cost objective requires a minimisation model");
        }
        _attempted = true;
        _owner = model;

        foreach (BinVar variable in Compilation.Variables) {
            Try added = model.Add(variable);
            if (!added.IsOk) {
                return added;
            }
        }

        foreach (CompiledActivityRow row in ActivitySchedulingRowBuilder.Rows(_data)) {
            Try registered = ActivityRowPipeline.Register(row, model, Compilation, _name);
            if (!registered.IsOk) {
                return registered;
            }
        }

        if (minimizeCost) {
            var monomials = new List<LinearMonomial<Flt64>>(Compilation.Cost.Monomials.Count);
            foreach (LinearMonomial<Flt64> monomial in Compilation.Cost.Monomials) {
                monomials.Add(monomial);
            }
            Try added = model.AddObject(
                ObjectCategory.Minimum,
                new LinearFlattenData<Flt64>(monomials, Compilation.Cost.Constant),
                $"{_name}_cost",
                displayName: null);
            if (!added.IsOk) {
                return added;
            }
        }

        _usable = true;
        return Results.Ok<Success>(Results.SuccessInstance);
    }

    /// <summary>
    /// 复核一组候选 / Validate a set of choices.
    /// </summary>
    /// <param name="choices">候选键 / The candidate keys.</param>
    /// <returns>校验结果 / The validation result.</returns>
    public ActivityScheduleValidation Validate(IReadOnlyList<ActivityChoiceKey> choices) {
        ArgumentNullException.ThrowIfNull(choices);

        var activities = new List<string>(_data.Problem.Activities.Count);
        foreach (Activity<Flt64> activity in _data.Problem.Activities) {
            activities.Add(activity.Id);
        }
        var cancellable = new HashSet<string>(StringComparer.Ordinal);
        foreach (string task in _data.Cancellations.Keys) {
            cancellable.Add(task);
        }
        var resources = new HashSet<string>(StringComparer.Ordinal);
        foreach (ActivityResource<Flt64> resource in _data.Problem.Resources) {
            resources.Add(resource.Id);
        }

        var placements = new List<ActivityPlacement<Flt64>>(choices.Count);
        foreach (ActivityChoiceKey key in choices) {
            if (_data.Indices.TryGetValue(key, out int index) && _data.Choices[index].Placement is { } placement) {
                placements.Add(placement);
            }
        }

        return ActivityScheduleValidator.Validate(
            new ActivityScheduleInput(activities, cancellable, _data.Problem.DependenciesValue, resources),
            placements);
    }

    /// <summary>
    /// 从模型取解 / Extract the schedule from a model.
    /// </summary>
    /// <param name="model">模型 / The model.</param>
    /// <returns>排程或错误 / The schedule or an error.</returns>
    public Result<ActivitySchedule<Flt64>, ErrorCode, Error<ErrorCode>> ExtractSolution(LinearMetaModel<Flt64> model) {
        ArgumentNullException.ThrowIfNull(model);

        if (!_usable || !ReferenceEquals(_owner, model)) {
            return Results.Failed<ActivitySchedule<Flt64>>(new Err<ErrorCode>(ErrorCode.IllegalArgument,
                "the model was not registered successfully or does not belong to this context"));
        }

        var selected = new List<int>();
        for (int index = 0; index < Compilation.Variables.Count; index++) {
            if (!TryTokenValue(model, Compilation.Variables[index], out double value)) {
                return Results.Failed<ActivitySchedule<Flt64>>(new Err<ErrorCode>(ErrorCode.IllegalArgument,
                    "missing activity-choice solution value"));
            }
            if (double.IsNaN(value) || double.IsInfinity(value)
                || System.Math.Abs(value - System.Math.Round(value)) > 1e-6
                || value < -1e-6 || value > 1.0 + 1e-6) {
                return Results.Failed<ActivitySchedule<Flt64>>(new Err<ErrorCode>(ErrorCode.IllegalArgument,
                    "the activity choice is not a finite binary solution"));
            }
            if (value > 0.5) {
                selected.Add(index);
            }
        }

        foreach (Activity<Flt64> activity in _data.Problem.Activities) {
            int count = 0;
            foreach (int index in selected) {
                if (string.Equals(_data.Choices[index].Task, activity.Id, StringComparison.Ordinal)) {
                    count++;
                }
            }
            if (count != 1) {
                return Results.Failed<ActivitySchedule<Flt64>>(new Err<ErrorCode>(ErrorCode.IllegalArgument,
                    "each activity must select exactly one execution or an explicit cancellation"));
            }
        }

        var chosen = new List<ActivityChoiceKey>(selected.Count);
        foreach (int index in selected) {
            if (_data.Choices[index].Placement is { } placement) {
                chosen.Add(placement.Key);
            }
        }
        ActivityScheduleValidation validation = Validate(chosen);
        if (!validation.IsValid) {
            return Results.Failed<ActivitySchedule<Flt64>>(new Err<ErrorCode>(ErrorCode.IllegalArgument,
                "independent schedule validation failed: " + string.Join(",", validation.Violations)));
        }

        var cost = new Flt64(0.0);
        var placements = new List<ActivityPlacement<Flt64>>(selected.Count);
        var cancelled = new HashSet<string>(StringComparer.Ordinal);
        foreach (int index in selected) {
            cost = cost + _data.Choices[index].Cost;
            if (_data.Choices[index].Placement is { } placement) {
                placements.Add(placement);
            } else {
                cancelled.Add(_data.Choices[index].Task);
            }
        }

        Result<Quantity<Flt64>, ErrorCode, Error<ErrorCode>> quantity =
            ActivityValueBoundary.QuantityOf(cost, _data.Problem.CostUnit);
        if (quantity is Failed<Quantity<Flt64>, ErrorCode, Error<ErrorCode>> failed) {
            return Results.Failed<ActivitySchedule<Flt64>>(failed.Error);
        }
        if (quantity is not Ok<Quantity<Flt64>, ErrorCode, Error<ErrorCode>> ok) {
            return new Fatal<ActivitySchedule<Flt64>, ErrorCode, Error<ErrorCode>>(
                ((Fatal<Quantity<Flt64>, ErrorCode, Error<ErrorCode>>)quantity).Errors);
        }

        Dictionary<string, IReadOnlyList<ActivityInventoryLevel<Flt64>>> levels = ActivityMaterialAudit.Levels(
            _data.Problem.MaterialsValue, _data.Problem.MaterialEventsValue,
            _data.Problem.Horizon.Start, _data.Problem.Horizon.End);
        var scope = new HashSet<ActivityChoiceKey>(_data.Indices.Keys);
        return Results.Ok(new ActivitySchedule<Flt64>(
            Placements: placements,
            Executions: _data.Problem.ExecutionsValue,
            Cancelled: cancelled,
            Cost: ok.Value,
            CandidateScope: scope,
            MaterialInventory: levels));
    }

    private static Try Fail(string message) =>
        Results.Failed<Success>(new Err<ErrorCode>(ErrorCode.IllegalArgument, message));

    private static bool TryTokenValue(LinearMetaModel<Flt64> model, BinVar variable, out double value) {
        value = double.NaN;
        Fuookami.Ospf.Core.Token.Token<Flt64>? token = model.Tokens.Find(variable);
        if (token?.DoubleResult is not { } result) {
            return false;
        }
        value = result;
        return true;
    }
}
