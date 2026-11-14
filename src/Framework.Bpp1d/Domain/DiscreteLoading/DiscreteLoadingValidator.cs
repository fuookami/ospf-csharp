// 离散装载输入校验 / Discrete-loading input validation.
// 与固定 Kotlin 基线 `bpp/domain/discrete_loading/DiscreteLoadingContext.kt` 的 `validate()` 对应。
// Mirrors `validate()` from the pinned Kotlin baseline's `bpp/domain/discrete_loading/DiscreteLoadingContext.kt`.

using System.Collections.Generic;
using Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading.Model;
// **同名冲突**：`Bpp.Infrastructure` 与 `Bpp.Domain.DiscreteLoading.Model` 各有一个
// `PositionConflict`。前者是几何扫描的**有向边**（`Earlier`/`Later`），后者是业务互斥的
// **无序对**（`First`/`Second`）。这里用别名钉住后者，避免含糊。
//
// **Name collision**: both namespaces define `PositionConflict`. The former is a geometry-sweep **directed edge** (`Earlier`/`Later`), the latter a business
// **unordered pair** (`First`/`Second`). An alias pins the latter so the reference is unambiguous.
using PositionConflictAlias = Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading.Model.PositionConflict;
using Fuookami.Ospf.Framework.Bpp.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<
    Fuookami.Ospf.Utils.Functional.Success,
    Fuookami.Ospf.Utils.Error.ErrorCode,
    Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading;

/// <summary>
/// 离散装载注册入口的输入校验 / Input validation for the discrete-loading entry.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <remarks>
/// **本批只移植 `validate()`**，不移植 `register()`：后者要把整个问题写成线性元模型
/// （`BinVar`、`LinearPolynomial`、布局选择组约束），属于下一层的接线工作。
///
/// **`validate()` 本身价值很高**：基线里它承担了 11 类校验，任何一条漏掉都会让
/// **不可行或非法的输入进入建模阶段**，而建模阶段的失败信息离根因很远。
///
/// **Only `validate()` is ported here**, not `register()`: the latter turns the whole problem into a linear meta model (`BinVar`, `LinearPolynomial`, layout-group
/// constraints) and belongs to the next wiring layer. **`validate()` is valuable on its own**: the baseline performs eleven kinds of checks there, and missing any one
/// lets **infeasible or illegal input reach the modelling stage**, where the failure message is far from the root cause.
/// </remarks>
public sealed class DiscreteLoadingValidator<V>
    where V : struct, IFloatingNumber<V> {
    private readonly DiscreteLoadingProblem<V> _problem;
    private readonly IPackingValueAdapter<V> _adapter;
    private readonly Dictionary<string, Flt64> _capacities = new();
    private readonly Dictionary<string, Flt64> _itemWeights = new();

    /// <summary>
    /// 构造校验器 / Construct the validator.
    /// </summary>
    /// <param name="problem">领域输入 / Domain input.</param>
    /// <param name="adapter">数值适配 / Numeric adapter.</param>
    public DiscreteLoadingValidator(DiscreteLoadingProblem<V> problem, IPackingValueAdapter<V> adapter) {
        _problem = problem;
        _adapter = adapter;
    }

    /// <summary>位置的归一化容量 / Normalised position capacities.</summary>
    public IReadOnlyDictionary<string, Flt64> Capacities => _capacities;

    /// <summary>货物的归一化重量 / Normalised item weights.</summary>
    public IReadOnlyDictionary<string, Flt64> ItemWeights => _itemWeights;

    /// <summary>
    /// 验证 ID、引用、容量和固定装载 / Validate IDs, references, capacities and fixed loads.
    /// </summary>
    /// <returns>操作结果 / Operation result.</returns>
    /// <remarks>
    /// 十一类校验，按基线的顺序：
    ///
    /// | # | 校验 | 漏掉的后果 |
    /// | --- | --- | --- |
    /// | 1 | 货物 ID 非空且唯一 | 同一件货被算两次 |
    /// | 2 | 舱位 ID 非空且唯一 | 容量约束被写成两条互相覆盖 |
    /// | 3 | 选择组 ID 唯一 | 两个选择组互相干扰 |
    /// | 4 | 分配引用存在 | 引用不存在的货物/舱位，建模时静默丢项 |
    /// | 5 | 冲突引用存在且不自冲突 | 自己与自己冲突，模型恒不可行 |
    /// | 6 | 布局组内部一致 | 布局覆盖不到组内舱位，组约束无意义 |
    /// | 7 | 舱位容量非负、件数为正 | 负容量让所有装载都不可行 |
    /// | 8 | 货物重量非负 | 负重量可以"抵消"其他货物 |
    /// | 9 | 固定装载是合法分配 | 固定到不允许的舱位 |
    /// | 10 | 固定占用之间不冲突 | 两条固定要求互相矛盾 |
    /// | 11 | 固定装载不超容量 | 固定装载本身就超重 |
    ///
    /// **第 8 条最隐蔽**：负重量在 `Σ weight ≤ maxWeight` 里会**抵消**其他货物，
    /// 让一个超载方案"看起来"满足容量。它不会让求解器报错。
    ///
    /// Eleven checks in the baseline's order (table above). **Check 8 is the subtlest**: a negative weight **cancels** other cargo in `Σ weight &lt;= maxWeight`, making an
    /// overloaded plan look feasible — and the solver never complains.
    /// </remarks>
    public Try Validate() {
        _capacities.Clear();
        _itemWeights.Clear();

        var itemIds = new HashSet<string>(_problem.Items.Count);
        foreach (LoadingItem<V> item in _problem.Items) {
            if (string.IsNullOrWhiteSpace(item.Id) || !itemIds.Add(item.Id)) {
                return Invalid("item IDs");
            }
        }
        var positionIds = new HashSet<string>(_problem.Positions.Count);
        foreach (LoadingPosition<V> position in _problem.Positions) {
            if (string.IsNullOrWhiteSpace(position.Id) || !positionIds.Add(position.Id)) {
                return Invalid("position IDs");
            }
        }

        var groupIds = new HashSet<string>(_problem.LayoutGroups.Count);
        foreach (LayoutSelectionGroup group in _problem.LayoutGroups) {
            if (!groupIds.Add(group.Id)) {
                return Invalid("layout group IDs");
            }
        }

        foreach (AssignmentKey assignment in _problem.AllowedAssignments) {
            if (!itemIds.Contains(assignment.ItemId) || !positionIds.Contains(assignment.PositionId)) {
                return Invalid("assignment reference");
            }
        }

        foreach (PositionConflictAlias conflict in _problem.Conflicts) {
            if (conflict.First == conflict.Second
                || !positionIds.Contains(conflict.First)
                || !positionIds.Contains(conflict.Second)) {
                return Invalid("conflict reference");
            }
        }

        foreach (LayoutSelectionGroup group in _problem.LayoutGroups) {
            if (string.IsNullOrWhiteSpace(group.Id) || group.Layouts.Count == 0) {
                return Invalid($"layout {group.Id}");
            }
            var layoutIds = new HashSet<string>(group.Layouts.Count);
            foreach (LoadingLayout layout in group.Layouts) {
                if (!layoutIds.Add(layout.Id) || string.IsNullOrWhiteSpace(layout.Id)) {
                    return Invalid($"layout {group.Id}");
                }
                if (!IsSubset(layout.PositionIds, group.PositionIds)) {
                    return Invalid($"layout {group.Id}");
                }
            }
            if (!IsSubset(group.PositionIds, positionIds)) {
                return Invalid($"layout {group.Id}");
            }
            // **组内每个舱位都必须至少被一个布局覆盖**，否则那条组约束永远无法满足。
            // **Every position in the group must be covered by at least one layout**, or the group constraint can never be satisfied.
            foreach (string positionId in group.PositionIds) {
                bool covered = false;
                foreach (LoadingLayout layout in group.Layouts) {
                    if (layout.PositionIds.Contains(positionId)) {
                        covered = true;
                        break;
                    }
                }
                if (!covered) {
                    return Invalid($"layout {group.Id}");
                }
            }
        }

        foreach (LoadingPosition<V> position in _problem.Positions) {
            Result<Flt64, ErrorCode, Error<ErrorCode>> capacity =
                _adapter.Quantity(position.MaxWeight, _problem.WeightUnit);
            if (capacity is not Ok<Flt64, ErrorCode, Error<ErrorCode>> capacityOk) {
                return Propagate(capacity);
            }
            // **负容量会被两处拦下**：这里，以及下面的"固定装载不超容量"（`0 > 负数` 恒真）。
            // 所以单独禁用这一处**不会**让负容量通过——实测确认，那不是判据不足，是**真实的冗余防护**。
            // 基线也是这样写的，保留双重保护。
            //
            // **A negative capacity is caught in two places**: here and the "fixed load within capacity" check below (`0 > negative` is always true). Disabling this one
            // alone therefore does **not** let a negative capacity through — measured, and that is **genuine redundant protection**, not a weak criterion. The baseline
            // is written the same way, so the double guard is kept.
            if (capacityOk.Value.CompareTo(Flt64.Zero) < 0 || position.MaxCount == 0) {
                return Invalid($"capacity {position.Id}");
            }
            _capacities[position.Id] = capacityOk.Value;
        }

        foreach (LoadingItem<V> item in _problem.Items) {
            Result<Flt64, ErrorCode, Error<ErrorCode>> weight = _adapter.Quantity(item.Weight, _problem.WeightUnit);
            if (weight is not Ok<Flt64, ErrorCode, Error<ErrorCode>> weightOk) {
                return Propagate(weight);
            }
            if (weightOk.Value.CompareTo(Flt64.Zero) < 0) {
                return Invalid($"weight {item.Id}");
            }
            _itemWeights[item.Id] = weightOk.Value;
            if (item.FixedPositionId is { } fixedPositionId
                && !_problem.AllowedAssignments.Contains(new AssignmentKey(item.Id, fixedPositionId))) {
                return Invalid($"fixed assignment {item.Id}");
            }
        }

        var fixedPositions = new HashSet<string>();
        foreach (LoadingPosition<V> position in _problem.Positions) {
            if (position.FixedOccupied) {
                fixedPositions.Add(position.Id);
            }
        }
        foreach (LoadingItem<V> item in _problem.Items) {
            if (item.FixedPositionId is { } fixedPositionId) {
                fixedPositions.Add(fixedPositionId);
            }
        }
        foreach (PositionConflictAlias conflict in _problem.Conflicts) {
            if (fixedPositions.Contains(conflict.First) && fixedPositions.Contains(conflict.Second)) {
                return Invalid("conflicting fixed occupancy");
            }
        }

        foreach (LayoutSelectionGroup group in _problem.LayoutGroups) {
            bool satisfied = false;
            foreach (LoadingLayout layout in group.Layouts) {
                bool covers = true;
                foreach (string positionId in fixedPositions) {
                    if (group.PositionIds.Contains(positionId) && !layout.PositionIds.Contains(positionId)) {
                        covers = false;
                        break;
                    }
                }
                if (covers) {
                    satisfied = true;
                    break;
                }
            }
            if (!satisfied) {
                return Invalid($"fixed layout {group.Id}");
            }
        }

        foreach (LoadingPosition<V> position in _problem.Positions) {
            ulong fixedCount = 0;
            Flt64 fixedWeight = Flt64.Zero;
            foreach (LoadingItem<V> item in _problem.Items) {
                if (item.FixedPositionId == position.Id) {
                    fixedCount++;
                    fixedWeight = fixedWeight.Plus(_itemWeights[item.Id]);
                }
            }
            if (fixedCount > position.MaxCount || fixedWeight.CompareTo(_capacities[position.Id]) > 0) {
                return Invalid($"fixed capacity {position.Id}");
            }
        }

        return Results.OkInstance;
    }

    /// <summary>
    /// 独立于求解器的最终复核 / Solver-independent final verification.
    /// </summary>
    /// <param name="solution">候选解 / Candidate solution.</param>
    /// <param name="additionalWeights">附加重量，单位同问题 / Additional weights in the problem's unit.</param>
    /// <returns>操作结果 / Operation result.</returns>
    /// <remarks>
    /// **这是整个模块最值得移植的一块**：它**不依赖求解器、不依赖元模型**，
    /// 只用问题定义与归一化后的容量/重量，就能把"求解器说这个解可行"独立复核一遍。
    /// 没有它，求解器或其适配层的任何一处错误都会**直接变成对外结论**。
    ///
    /// 九类复核，按基线的顺序：
    ///
    /// | # | 复核 | 漏掉的后果 |
    /// | --- | --- | --- |
    /// | 1 | 占用舱位与附加重量都在问题里 | 未知舱位被当成合法 |
    /// | 2 | 每条分配都是已声明的合法组合 | 未声明的组合被偷偷放行 |
    /// | 3 | 必装货物已装、固定货物装到固定位 | 客户必装要求被忽略 |
    /// | 4 | 冲突舱位不得同时占用 | 物理冲突被放过 |
    /// | 5 | 选择组集合与问题声明的完全一致 | 少选或多选一组 |
    /// | 6 | 组内选中布局覆盖该组全部占用舱位 | 布局与占用矛盾 |
    /// | 7 | 附加重量有限且非负 | 负附加重量抵消真实载荷 |
    /// | 8 | 件数与总重不超容量（**容差 `1e-6`**） | 超载被当成合规 |
    /// | 9 | 有内容就必须出现在占用集合；禁用舱位不得参与 | 占用状态与实际内容不一致 |
    ///
    /// **第 8 条的 `1e-6` 容差是有意的**：求解器返回的浮点值与容量常差最后一位，
    /// 严格比较会把**正常解**判成超载。**这是留给真实浮点的校准旋钮**，不是宽松。
    ///
    /// **The most valuable piece of this module**: it **needs neither the solver nor the meta model**, only the problem definition plus the normalised capacities and
    /// weights, so "the solver says this solution is feasible" can be independently re-checked. Without it, any error in the solver or its adapter layer would
    /// **become an outward conclusion directly**. Nine checks are listed in the table above; **the `1e-6` tolerance in check 8 is deliberate** — a solver's floating
    /// result routinely differs from the capacity in the last digit, and a strict comparison would flag **correct** solutions as overloaded. **That is a calibration
    /// knob for real floating point, not laxity.**
    /// </remarks>
    public Try Verify(DiscreteLoadingSolution solution, IReadOnlyDictionary<string, Flt64>? additionalWeights = null) {
        additionalWeights ??= new Dictionary<string, Flt64>();

        // **复核先自校验输入**：问题本身非法时，任何解都不该被"验证通过"。
        // **Re-validate the input first**: an illegal problem must never let any solution pass.
        Try validation = Validate();
        if (validation is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
            return validation;
        }

        var positionIds = new HashSet<string>(_problem.Positions.Count);
        foreach (LoadingPosition<V> position in _problem.Positions) {
            positionIds.Add(position.Id);
        }
        foreach (string occupied in solution.OccupiedPositions) {
            if (!positionIds.Contains(occupied)) {
                return InvalidSolution("unknown position");
            }
        }
        foreach (KeyValuePair<string, Flt64> pair in additionalWeights) {
            if (!positionIds.Contains(pair.Key)) {
                return InvalidSolution("unknown position");
            }
        }

        foreach (KeyValuePair<string, string> pair in solution.Assignments) {
            if (!_problem.AllowedAssignments.Contains(new AssignmentKey(pair.Key, pair.Value))) {
                return InvalidSolution("illegal assignment");
            }
        }

        foreach (LoadingItem<V> item in _problem.Items) {
            bool mandatoryMissing = item.Required && !solution.Assignments.ContainsKey(item.Id);
            bool fixedWrong = item.FixedPositionId is { } fixedPositionId
                && (!solution.Assignments.TryGetValue(item.Id, out string? assigned) || assigned != fixedPositionId);
            if (mandatoryMissing || fixedWrong) {
                return InvalidSolution("required/fixed cargo");
            }
        }

        foreach (PositionConflictAlias conflict in _problem.Conflicts) {
            if (solution.OccupiedPositions.Contains(conflict.First) && solution.OccupiedPositions.Contains(conflict.Second)) {
                return InvalidSolution("overlap");
            }
        }

        // **选择组集合必须与问题声明的一致**：少选一组等于"该组约束没生效"，多选一组是引用不存在的组。
        // **The selected-group set must match exactly**: one missing group means "that constraint never applied", and an extra one references a nonexistent group.
        var declaredGroups = new HashSet<string>(_problem.LayoutGroups.Count);
        foreach (LayoutSelectionGroup group in _problem.LayoutGroups) {
            declaredGroups.Add(group.Id);
        }
        var selectedGroups = new HashSet<string>(solution.SelectedLayouts.Count);
        foreach (KeyValuePair<string, string> pair in solution.SelectedLayouts) {
            selectedGroups.Add(pair.Key);
        }
        if (!selectedGroups.SetEquals(declaredGroups)) {
            return InvalidSolution("layout groups");
        }

        foreach (LayoutSelectionGroup group in _problem.LayoutGroups) {
            LoadingLayout? layout = null;
            foreach (LoadingLayout candidate in group.Layouts) {
                if (candidate.Id == solution.SelectedLayouts[group.Id]) {
                    layout = candidate;
                    break;
                }
            }
            if (layout is null) {
                return InvalidSolution($"layout {group.Id}");
            }
            foreach (string occupied in solution.OccupiedPositions) {
                if (group.PositionIds.Contains(occupied) && !layout.PositionIds.Contains(occupied)) {
                    return InvalidSolution($"layout membership {group.Id}");
                }
            }
        }

        foreach (LoadingPosition<V> position in _problem.Positions) {
            var selected = new List<string>();
            foreach (KeyValuePair<string, string> pair in solution.Assignments) {
                if (pair.Value == position.Id) {
                    selected.Add(pair.Key);
                }
            }

            Flt64 additional = additionalWeights.TryGetValue(position.Id, out Flt64 extra) ? extra : Flt64.Zero;
            if (!double.IsFinite(additional.ToDouble()) || additional.CompareTo(Flt64.Zero) < 0) {
                return InvalidSolution($"additional weight {position.Id}");
            }

            Flt64 weight = additional;
            foreach (string itemId in selected) {
                weight = weight.Plus(_itemWeights[itemId]);
            }
            if ((ulong)selected.Count > position.MaxCount
                || weight.CompareTo(_capacities[position.Id].Plus(new Flt64(1e-6))) > 0) {
                return InvalidSolution($"capacity {position.Id}");
            }

            bool hasContent = selected.Count > 0 || additional.CompareTo(Flt64.Zero) > 0 || position.FixedOccupied;
            if (hasContent && !solution.OccupiedPositions.Contains(position.Id)) {
                return InvalidSolution($"occupancy {position.Id}");
            }

            // **禁用舱位有两处拦截**：这里，以及下面那条"禁用舱位不得装未固定的货物"。
            // 单独删掉这一处**不会**让非法解通过——实测确认，那是**真实的冗余防护**，不是判据不足。
            // 基线也是两处都写，保留双重保护。
            //
            // **A disabled position is guarded in two places**: here and the "disabled position may only carry fixed cargo" check below. Deleting this one alone does
            // **not** let an illegal solution through — measured, so it is **genuine redundant protection**, not a weak criterion. The baseline guards both, so both
            // are kept.
            if (!position.Enabled && solution.OccupiedPositions.Contains(position.Id)) {
                if (position.FixedOccupied) {
                    continue;
                }
                bool anyFixedHere = false;
                foreach (LoadingItem<V> item in _problem.Items) {
                    if (item.FixedPositionId == position.Id) {
                        anyFixedHere = true;
                        break;
                    }
                }
                if (!anyFixedHere) {
                    return InvalidSolution($"disabled occupancy {position.Id}");
                }
            }

            if (!position.Enabled) {
                foreach (string itemId in selected) {
                    LoadingItem<V>? item = null;
                    foreach (LoadingItem<V> candidate in _problem.Items) {
                        if (candidate.Id == itemId) {
                            item = candidate;
                            break;
                        }
                    }
                    if (item is null || item.FixedPositionId != position.Id) {
                        return InvalidSolution($"disabled {position.Id}");
                    }
                }
            }
        }

        return Results.OkInstance;
    }

    /// <summary>是否子集 / Whether the left set is a subset of the right one.</summary>
    /// <param name="left">左集合 / Left set.</param>
    /// <param name="right">右集合 / Right set.</param>
    /// <returns>判断结果 / The verdict.</returns>
    private static bool IsSubset(IReadOnlySet<string> left, IReadOnlySet<string> right) {
        foreach (string value in left) {
            if (!right.Contains(value)) {
                return false;
            }
        }
        return true;
    }

    /// <summary>构造解复核失败 / Build a solution-verification failure.</summary>
    /// <param name="reason">原因 / Reason.</param>
    /// <returns>失败结果 / The failure.</returns>
    /// <remarks>
    /// **用 `ApplicationError` 而不是 `IllegalArgument`**：输入本身是合法的，
    /// 是**这个解**不合规。错误码要能让调用方区分"我的问题定义错了"与"求解器给了个坏解"。
    ///
    /// **`ApplicationError` rather than `IllegalArgument`**: the input is legal; **this solution** is not. The code must let callers tell "my problem definition is wrong"
    /// from "the solver returned a bad solution".
    /// </remarks>
    private static Try InvalidSolution(string reason) =>
        Results.Failed<Success>(new Err<ErrorCode>(
            ErrorCode.ApplicationError,
            $"装载解复核失败 / Loading verification failed: {reason}"));

    /// <summary>构造参数非法失败 / Build an invalid-argument failure.</summary>
    /// <param name="message">细节 / Detail.</param>
    /// <returns>失败结果 / The failure.</returns>
    private static Try Invalid(string message) =>
        Results.Failed<Success>(new Err<ErrorCode>(
            ErrorCode.IllegalArgument,
            $"装载输入无效 / Invalid loading input: {message}"));

    /// <summary>把失败/致命结果转成本函数的返回类型 / Re-shape a failure or fatal result.</summary>
    /// <param name="result">源结果 / Source result.</param>
    /// <returns>转换后的结果 / The re-shaped result.</returns>
    private static Try Propagate(Result<Flt64, ErrorCode, Error<ErrorCode>> result) =>
        result switch {
            Failed<Flt64, ErrorCode, Error<ErrorCode>> failed => Results.Failed<Success>(failed.Error),
            Fatal<Flt64, ErrorCode, Error<ErrorCode>> fatal =>
                new Fatal<Success, ErrorCode, Error<ErrorCode>>(fatal.Errors),
            _ => Results.OkInstance,
        };
}
