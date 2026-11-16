// 离散装载注册与取解入口 / Discrete-loading registration and solution-extraction entry.
// 与固定 Kotlin 的
// `register` 与 `extractSolution` 对应（`validate`/`verify` 已在 ）。

using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading.Model;
using Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading.Service.Limits;
using Fuookami.Ospf.Framework.Bpp.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<
    Fuookami.Ospf.Utils.Functional.Success,
    Fuookami.Ospf.Utils.Error.ErrorCode,
    Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading;

/// <summary>
/// 离散装载注册入口，每次完整求解使用新实例 / Discrete-loading registration entry; use a fresh instance per solve.
/// </summary>
/// <typeparam name="V">领域数值 / Domain number.</typeparam>
/// <remarks>
/// **每次求解都用新实例**：`registered` 是实例状态，复用实例会让第二次注册静默返回失败。
/// **Use a fresh instance per solve**: `registered` is instance state, and reusing an instance makes the second registration fail silently.
/// </remarks>
public sealed class DiscreteLoadingContext<V>
    where V : struct, IFloatingNumber<V> {
    private readonly DiscreteLoadingProblem<V> _problem;
    private readonly DiscreteLoadingValidator<V> _validator;
    private readonly ConflictIndex _conflictIndex;
    private bool _registered;

    /// <summary>
    /// 构造 / Constructor.
    /// </summary>
    /// <param name="problem">领域输入 / Domain input.</param>
    /// <param name="adapter">数值适配 / Numeric adapter.</param>
    public DiscreteLoadingContext(DiscreteLoadingProblem<V> problem, IPackingValueAdapter<V> adapter) {
        _problem = problem;
        Adapter = adapter;
        _validator = new DiscreteLoadingValidator<V>(problem, adapter);
        _conflictIndex = ConflictIndex.Build(problem.Conflicts);
    }

    /// <summary>领域输入 / The domain input.</summary>
    /// <remarks>
    /// **组合层（分区装载）需要它做分区归属、占位冲突、布局兼容与重量单位换算**，
    /// 而`DiscreteLoadingContext` 本来就公开 `problem` 与 `adapter`。这里补上同样的两个只读属性，
    /// **不在组合层塞一份重复的输入副本**。
    ///
    /// **The composition layer (partition loading) needs it for partition ownership, occupancy conflicts, layout compatibility and weight-unit conversion**, and
    /// `DiscreteLoadingContext` already exposes `problem` and `adapter`. The same two read-only properties are added here rather than keeping a duplicate
    /// copy of the input in the composition layer.
    /// </remarks>
    public DiscreteLoadingProblem<V> Problem => _problem;

    /// <summary>数值适配 / The numeric adapter.</summary>
    public IPackingValueAdapter<V> Adapter { get; }

    /// <summary>输入校验器；对外暴露以便组合层复核输入而不必先注册 / Input validator, exposed so adapters can re-check input without registering first.</summary>
    public DiscreteLoadingValidator<V> Validator => _validator;

    /// <summary>求解器注册构件 / Solver registration artifacts.</summary>
    public LoadingAggregation Aggregation { get; } = new();

    /// <summary>重用的冲突索引 / Reusable conflict index.</summary>
    public ConflictIndex ConflictIndex => _conflictIndex;

    /// <summary>是否已注册 / Whether registration already happened.</summary>
    public bool Registered => _registered;

    /// <summary>
    /// 注册共享变量及所有通用硬约束 / Register shared variables and generic hard constraints.
    /// </summary>
    /// <param name="model">元模型 / Meta model.</param>
    /// <returns>操作结果 / Operation result.</returns>
    public Try Register(LinearMetaModel<Flt64> model) {
        if (_registered) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument, "上下文已经注册 / Context already registered"));
        }
        Try validation = _validator.Validate();
        if (validation is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
            return validation;
        }

        Try built = Aggregation.Build(_problem, _validator.Capacities, _validator.ItemWeights, model);
        if (built is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
            return built;
        }

        Try capacity = new LoadingCapacityLimit<V>(_problem, Aggregation, _validator.Capacities).Invoke(model);
        if (capacity is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
            return capacity;
        }
        Try conflict = new LoadingConflictLimit<V>(_problem, Aggregation, _conflictIndex).Invoke(model);
        if (conflict is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
            return conflict;
        }

        _registered = true;
        return Results.OkInstance;
    }

    /// <summary>
    /// 追加预估/推荐重量并联动整体占位；上界来自真实舱位容量 / Link additional weight to whole-position occupancy, bounded by the real capacity.
    /// </summary>
    /// <param name="positionId">舱位 / Position.</param>
    /// <param name="additionalWeight">统一重量单位的附加重量表达式 / Additional normalised weight expression.</param>
    /// <param name="model">元模型 / Meta model.</param>
    /// <returns>操作结果 / Operation result.</returns>
    /// <remarks>
    /// **两条约束，第一条最要紧**：
    ///
    /// | 约束 | 形式 | 少了它 |
    /// | --- | --- | --- |
    /// | 附加重量非负 | `additional &gt;= 0` | 外部塞进一个**负**表达式，就能在容量行里**抵消**真实载荷 |
    /// | 容量联动 | `weights_p + additional &lt;= capacity_p · occupied_p` | 附加重量**不受容量约束**，可以无限追加 |
    ///
    /// **第一条与"货物重量非负"、"附加重量非负"是同一种病**：
    /// 负值在求和里抵消正项，让一个超载方案"看起来"合规，而**求解器不会报错**。
    /// 区别是这一层是**对外的接口**——调用方自己传来的表达式，因此这里的守卫尤其重要。
    ///
    /// **第二行的左端必须包含 `weights_p` 既有载荷**，不能只写 `additional`：
    /// 只写附加重量会让"已有装载 + 追加"可以超容量。
    ///
    /// **Two rows, the first matters most** (table above): a negative expression injected from outside **cancels** real load in the capacity row, and without the capacity
    /// link the additional weight is **unconstrained** and can grow forever. **The first check is the same disease as the earlier "cargo weight non-negative" and the earlier
    /// "additional weight non-negative"**: a negative value cancels positive terms and makes an overloaded plan look legal, with **no solver error** — and here it is an
    /// **outward-facing entry point** receiving a caller-supplied expression, so the guard matters more than in the internal paths. **The second row's left side must
    /// include the pre-existing `weights_p`**, or "already loaded plus appended" can exceed the capacity.
    /// </remarks>
    public Try LinkAdditionalWeight(
        string positionId,
        LinearPolynomial<Flt64> additionalWeight,
        LinearMetaModel<Flt64> model) {
        if (!Aggregation.Occupied.TryGetValue(positionId, out BinVar? active)) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                $"未知或未注册舱位 / Unknown or unregistered position: {positionId}"));
        }

        Try nonnegative = model.AddConstraint(
            new LinearInequality<Flt64>(
                additionalWeight,
                Constant(Flt64.Zero),
                Comparison.GE),
            new LoadingCapacityLimit<V>(_problem, Aggregation, _validator.Capacities),
            name: $"additional_weight_nonnegative_{positionId}");
        if (nonnegative is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
            return nonnegative;
        }

        Flt64 capacity = _validator.Capacities[positionId];
        return model.AddConstraint(
            new LinearInequality<Flt64>(
                Aggregation.Weights[positionId] + additionalWeight,
                new LinearPolynomial<Flt64>(
                    new List<LinearMonomial<Flt64>> { new(capacity, active) },
                    Flt64.Zero),
                Comparison.LE),
            new LoadingCapacityLimit<V>(_problem, Aggregation, _validator.Capacities),
            name: $"additional_weight_capacity_{positionId}");
    }

    /// <summary>
    /// 从模型 token 读出并复核解 / Read the solution back from the model tokens and verify it.
    /// </summary>
    /// <param name="model">元模型 / Meta model.</param>
    /// <param name="additionalWeights">附加重量的已提取值，单位同问题 / Extracted additional weights in the problem unit.</param>
    /// <returns>已验证解或失败 / Verified solution or failure.</returns>
    /// <remarks>
    /// **有一处有意差异**：`values: List&lt;Flt64&gt;` 与 `tokens.indexOf(variable)` 那种下标取法都不需要，
    /// 这里直接读 `model.Tokens.Find(variable)?.DoubleResult`——**同一份数据，少一个参数**，
    /// 且与 `Csp1dProduceContext` 已有的取解方式一致（同一套管线里求解器把结果写回 token）。
    ///
    /// **四条拒绝条件**：
    ///
    /// | 条件 | 若放过 | 后果 |
    /// | --- | --- | --- |
    /// | 变量 token 或值缺失 | 悄悄跳过 | **少算一半的分配**，读起来像"装得更少" |
    /// | **变量不满足二元性**（`min(|v|, |v-1|) > 1e-6`） | 四舍五入 | 一个 `0.5` 的分配被当成"装了"或"没装"，**报告一个不可行的计划** |
    /// | 同一件货物被两条分配选中 | 都收下 | `assignments` 是 `货物→舱位` 单值表，后者静默覆盖前者 |
    /// | 某个布局组不是恰好选中一种 | 少选或多选 | 组约束形同虚设或引用不存在的布局 |
    ///
    /// **第二条是这里最要紧的**：`0.5` 不是浮点噪声，是**求解器在说"这个解不是整数解"**。
    /// 四舍五入会把一个 LP 松弛解当成整数解报出去。
    ///
    /// **One deliberate deviation from the code**: it accepts a `values: List&lt;Flt64&gt;` and indexes by `tokens.indexOf(variable)`, whereas this reads
    /// `model.Tokens.Find(variable)?.DoubleResult` — **the same data with one fewer parameter**, matching how `Csp1dProduceContext` already extracts solutions
    /// (the solver writes results back into tokens in this pipeline). The four rejection rules are in the table; **the second is the important one** — `0.5` is not
    /// floating noise, it is **the solver saying "this is not an integer solution"**, and rounding it up reports an LP relaxation as an integer plan.
    /// </remarks>
    public Result<DiscreteLoadingSolution, ErrorCode, Error<ErrorCode>> ExtractSolution(
        LinearMetaModel<Flt64> model,
        IReadOnlyDictionary<string, Flt64>? additionalWeights = null) {
        if (!_registered) {
            return Results.Failed<DiscreteLoadingSolution>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument, "上下文未注册 / Context not registered"));
        }

        var binary = new Dictionary<string, double>();
        foreach (BinVar variable in Aggregation.Assignments.Values) {
            double? value = ReadBinary(model, variable);
            if (value is null) {
                return Results.Failed<DiscreteLoadingSolution>(new Err<ErrorCode>(
                    ErrorCode.DataNotFound, $"缺少装载变量值 / Missing loading value: {variable.Name}"));
            }
            binary[variable.Name] = value.Value;
        }
        foreach (BinVar variable in Aggregation.Occupied.Values) {
            double? value = ReadBinary(model, variable);
            if (value is null) {
                return Results.Failed<DiscreteLoadingSolution>(new Err<ErrorCode>(
                    ErrorCode.DataNotFound, $"缺少装载变量值 / Missing loading value: {variable.Name}"));
            }
            binary[variable.Name] = value.Value;
        }
        foreach (BinVar variable in Aggregation.Layouts.Values) {
            double? value = ReadBinary(model, variable);
            if (value is null) {
                return Results.Failed<DiscreteLoadingSolution>(new Err<ErrorCode>(
                    ErrorCode.DataNotFound, $"缺少装载变量值 / Missing loading value: {variable.Name}"));
            }
            binary[variable.Name] = value.Value;
        }

        var selected = new Dictionary<string, string>();
        foreach (KeyValuePair<AssignmentKey, BinVar> pair in Aggregation.Assignments) {
            if (binary[pair.Value.Name] > 0.5) {
                // **同一件货物被两条分配选中必须拒绝。** `assignments` 是单值表，静默覆盖会让
                // "装了两次"看起来像"装了一次"。
                // **Two assignments selecting the same cargo must be rejected.** `assignments` is a single-valued map, and silently overwriting makes "loaded twice"
                // look like "loaded once".
                if (!selected.TryAdd(pair.Key.ItemId, pair.Key.PositionId)) {
                    return Results.Failed<DiscreteLoadingSolution>(new Err<ErrorCode>(
                        ErrorCode.ApplicationError, "货物重复装载 / Duplicate cargo loading"));
                }
            }
        }

        var layoutChoices = new Dictionary<string, string>();
        var layoutCounts = new Dictionary<string, int>();
        foreach (KeyValuePair<(string GroupId, string LayoutId), BinVar> pair in Aggregation.Layouts) {
            if (binary[pair.Value.Name] <= 0.5) {
                continue;
            }
            layoutCounts[pair.Key.GroupId] = layoutCounts.TryGetValue(pair.Key.GroupId, out int seen) ? seen + 1 : 1;
            layoutChoices[pair.Key.GroupId] = pair.Key.LayoutId;
        }
        foreach (KeyValuePair<string, int> pair in layoutCounts) {
            if (pair.Value != 1) {
                return Results.Failed<DiscreteLoadingSolution>(new Err<ErrorCode>(
                    ErrorCode.ApplicationError, "布局必须恰选一种 / Exactly one layout must be selected"));
            }
        }

        var occupied = new HashSet<string>();
        foreach (KeyValuePair<string, BinVar> pair in Aggregation.Occupied) {
            if (binary[pair.Value.Name] > 0.5) {
                occupied.Add(pair.Key);
            }
        }

        var solution = new DiscreteLoadingSolution(
            Assignments: selected,
            OccupiedPositions: occupied,
            SelectedLayouts: layoutChoices);

        Try verified = _validator.Verify(solution, additionalWeights);
        if (verified is Failed<Success, ErrorCode, Error<ErrorCode>> failed) {
            return Results.Failed<DiscreteLoadingSolution>(failed.Error);
        }
        if (verified is Fatal<Success, ErrorCode, Error<ErrorCode>> fatal) {
            return new Fatal<DiscreteLoadingSolution, ErrorCode, Error<ErrorCode>>(fatal.Errors);
        }
        return Results.Ok(solution);
    }

    /// <summary>常数多项式 / A constant polynomial.</summary>
    /// <param name="value">常数 / The constant.</param>
    /// <returns>只含常数的多项式 / The polynomial holding only the constant.</returns>
    private static LinearPolynomial<Flt64> Constant(Flt64 value) =>
        new(new List<LinearMonomial<Flt64>>(), value);

    /// <summary>
    /// 读出一个二值变量并检查二元性 / Read one binary variable and enforce binary-ness.
    /// </summary>
    /// <param name="model">元模型 / Meta model.</param>
    /// <param name="variable">变量 / The variable.</param>
    /// <returns>值；缺失或非二元时为 `null` / The value, or `null` when missing or non-binary.</returns>
    /// <remarks>
    /// **容差取 `1e-6`**：求解器返回的 `1.0000001` 必须被当成 1，否则所有正常解都会被拒。
    /// 但 `0.5` 必须被拒——那是**求解器在说"这不是整数解"**。
    ///
    /// **The tolerance is `1e-6`, matching the code**: a solver's `1.0000001` must count as 1 or every normal solution would be rejected, while `0.5` must be
    /// refused — that is **the solver saying "this is not an integer solution"**.
    /// </remarks>
    private static double? ReadBinary(LinearMetaModel<Flt64> model, IVariableItem variable) {
        Token<Flt64>? token = model.Tokens.Find(variable);
        double? raw = token?.DoubleResult;
        if (raw is null || !double.IsFinite(raw.Value)) {
            return null;
        }
        double value = raw.Value;
        double distanceToZero = global::System.Math.Abs(value);
        double distanceToOne = global::System.Math.Abs(value - 1.0);
        if (global::System.Math.Min(distanceToZero, distanceToOne) > 1e-6) {
            return null;
        }
        return value;
    }
}
