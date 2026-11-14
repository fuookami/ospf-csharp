// 分配唯一性、容量及整体占用联动；重叠舱位及布局选择 / Assignment, capacity, occupancy links, overlap and layout selection.
// 与固定 Kotlin 基线 `bpp/domain/discrete_loading/service/limits/LoadingLimits.kt` 对应。
// Mirrors the pinned Kotlin baseline's `bpp/domain/discrete_loading/service/limits/LoadingLimits.kt`.

using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading.Model;
using Fuookami.Ospf.Framework.Model;
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

namespace Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading.Service.Limits;

/// <summary>
/// 分配唯一性、容量及整体占用联动 / Assignment, capacity and whole-position occupancy links.
/// </summary>
/// <typeparam name="V">领域数值 / Domain number.</typeparam>
/// <remarks>
/// **四条规则**：
///
/// | 规则 | 形式 | 为什么必须有 |
/// | --- | --- | --- |
/// | 必装/固定货物恰好装一次 | `loaded_i == 1` | 用 `≤ 1` 会让求解器**直接不装**必装货物 |
/// | 其余货物至多装一次 | `loaded_i ≤ 1` | 缺了下界，但重复装同一件会虚增占用统计 |
/// | 件数受占用联动 | `counts_p ≤ min(maxCount, 可用分配数) · occupied_p` | 不乘 `occupied_p` 就能在**未占用**的舱位堆东西 |
/// | 重量受占用联动 | `weights_p ≤ capacity_p · occupied_p` | 同上 |
///
/// **第一条与第二条的差别是"装载义务"，不是"装载上限"**：把必装货物也写成 `≤ 1`，
/// 求解器会非常乐意一件都不装——目标函数里没有惩罚项的话它甚至认为那是更好的解。
///
/// **Four rules** (table above). **The first two differ in "obligation", not "limit"**: writing a mandatory cargo as `&lt;= 1` too would make the solver happily load none
/// of it — with no penalty in the objective it may even consider that the better solution. **The count and weight bounds are multiplied by `occupied_p`**; without
/// that factor the solver could stack cargo on a position that is never marked occupied.
/// </remarks>
public sealed class LoadingCapacityLimit<V> : IMetaConstraintGroup, IPipeline<LinearMetaModel<Flt64>>
    where V : struct, IFloatingNumber<V> {
    private readonly DiscreteLoadingProblem<V> _problem;
    private readonly LoadingAggregation _aggregation;
    private readonly IReadOnlyDictionary<string, Flt64> _capacities;

    /// <summary>
    /// 构造 / Constructor.
    /// </summary>
    /// <param name="problem">领域输入 / Domain input.</param>
    /// <param name="aggregation">注册构件 / Registration artifacts.</param>
    /// <param name="capacities">已统一单位的重量上限 / Normalised weight capacities.</param>
    /// <param name="name">约束组名 / Constraint group name.</param>
    public LoadingCapacityLimit(
        DiscreteLoadingProblem<V> problem,
        LoadingAggregation aggregation,
        IReadOnlyDictionary<string, Flt64> capacities,
        string name = "loading_capacity") {
        _problem = problem;
        _aggregation = aggregation;
        _capacities = capacities;
        Name = name;
    }

    /// <inheritdoc/>
    public string Name { get; }

    /// <inheritdoc/>
    public bool Lazy => false;

    /// <inheritdoc/>
    public Try Invoke(LinearMetaModel<Flt64> model) {
        foreach (LoadingItem<V> item in _problem.Items) {
            LinearPolynomial<Flt64> loaded = _aggregation.Loaded[item.Id];
            // **必装或固定 ⇒ 恰好一次；其余 ⇒ 至多一次。** 这个分支缺了，必装要求会静默失效。
            // **Required or fixed means exactly once; everything else at most once.** Dropping this branch silently voids the mandatory requirement.
            bool mustLoad = item.Required || item.FixedPositionId is not null;
            Comparison comparison = mustLoad ? Comparison.EQ : Comparison.LE;
            Flt64 bound = Flt64.One;
            Try added = model.AddConstraint(
                new LinearInequality<Flt64>(loaded, Number(bound), comparison),
                this,
                name: $"{Name}_item_{item.Id}");
            if (added is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
                return added;
            }
        }

        foreach (LoadingPosition<V> position in _problem.Positions) {
            BinVar active = _aggregation.Occupied[position.Id];

            // **件数上界取 `min(最大件数, 该舱位的可用分配数)`。** 只取最大件数会给出一个
            // 根本达不到的上界——不错误，但**约束太松**，等于没有；取分配数则等于"装得下的最多"。
            // **The count bound is `min(maxCount, available assignments)`.** Max count alone gives an unreachable bound: not wrong, just **too loose to do anything**;
            // the assignment count is what "as many as can physically go here" actually is.
            ulong available = 0;
            foreach (AssignmentKey key in _aggregation.Assignments.Keys) {
                if (key.PositionId == position.Id) {
                    available++;
                }
            }
            ulong bound = position.MaxCount < available ? position.MaxCount : available;

            Try counted = model.AddConstraint(
                new LinearInequality<Flt64>(
                    _aggregation.Counts[position.Id],
                    Times(new Flt64((double)bound), active),
                    Comparison.LE),
                this,
                name: $"{Name}_count_{position.Id}");
            if (counted is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
                return counted;
            }

            Try weighted = model.AddConstraint(
                new LinearInequality<Flt64>(
                    _aggregation.Weights[position.Id],
                    Times(_capacities[position.Id], active),
                    Comparison.LE),
                this,
                name: $"{Name}_weight_{position.Id}");
            if (weighted is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
                return weighted;
            }
        }

        return Results.OkInstance;
    }

    /// <summary>常数多项式 / A constant polynomial.</summary>
    /// <param name="value">常数 / The constant.</param>
    /// <returns>只含常数的多项式 / The polynomial holding only the constant.</returns>
    private static LinearPolynomial<Flt64> Number(Flt64 value) =>
        new(new List<LinearMonomial<Flt64>>(), value);

    /// <summary>系数乘以一个二值变量 / Multiply a coefficient by one binary variable.</summary>
    /// <param name="coefficient">系数 / The coefficient.</param>
    /// <param name="variable">变量 / The variable.</param>
    /// <returns>单项式多项式 / The single-monomial polynomial.</returns>
    private static LinearPolynomial<Flt64> Times(Flt64 coefficient, IVariableItem variable) =>
        new(new List<LinearMonomial<Flt64>> { new(coefficient, variable) }, Flt64.Zero);
}

/// <summary>
/// 重叠舱位及布局选择约束 / Overlap and layout-selection constraints.
/// </summary>
/// <typeparam name="V">领域数值 / Domain number.</typeparam>
/// <remarks>
/// **团约束是 `Σ occupied(clique) ≤ 1`**，依赖 <see cref="ConflictIndex"/> 的**团**语义：
/// 团内逐对都冲突，所以"至多占一个"恰好等价于"每对冲突不能同时占"。
/// **若团里混进了不冲突的舱位，这条约束就会过强**——见 `ConflictIndex` 的判据。
///
/// **布局组是两条**：组内 `Σ layout == 1`（恰选一种），然后**每个受约束舱位**
/// `occupied_p ≤ Σ 覆盖它的布局`——即"要用这个舱位，就得选一个允许它的布局"。
///
/// **The clique constraint is `Σ occupied(clique) &lt;= 1`** and relies on `ConflictIndex`'s **clique** semantics: every pair inside conflicts, so "occupy at most one" is
/// exactly "no conflicting pair is occupied together". **A non-conflicting position smuggled into a clique makes this constraint too strong** — see `ConflictIndex`'s
/// criteria. **Each layout group gets two kinds of rows**: `Σ layout == 1` inside the group, then one `occupied_p &lt;= Σ covering layouts` per governed position, i.e.
/// "to use this position you must pick a layout that allows it".
/// </remarks>
public sealed class LoadingConflictLimit<V> : IMetaConstraintGroup, IPipeline<LinearMetaModel<Flt64>>
    where V : struct, IFloatingNumber<V> {
    private readonly DiscreteLoadingProblem<V> _problem;
    private readonly LoadingAggregation _aggregation;
    private readonly ConflictIndex _index;

    /// <summary>
    /// 构造 / Constructor.
    /// </summary>
    /// <param name="problem">输入 / Input.</param>
    /// <param name="aggregation">注册构件 / Registration artifacts.</param>
    /// <param name="index">已验证团覆盖 / Verified clique cover.</param>
    /// <param name="name">约束组名 / Constraint group name.</param>
    public LoadingConflictLimit(
        DiscreteLoadingProblem<V> problem,
        LoadingAggregation aggregation,
        ConflictIndex index,
        string name = "loading_conflict") {
        _problem = problem;
        _aggregation = aggregation;
        _index = index;
        Name = name;
    }

    /// <inheritdoc/>
    public string Name { get; }

    /// <inheritdoc/>
    public bool Lazy => false;

    /// <inheritdoc/>
    public Try Invoke(LinearMetaModel<Flt64> model) {
        for (int cliqueIndex = 0; cliqueIndex < _index.Cliques.Count; cliqueIndex++) {
            IReadOnlySet<string> clique = _index.Cliques[cliqueIndex];
            var monomials = new List<LinearMonomial<Flt64>>(clique.Count);
            foreach (string positionId in clique) {
                monomials.Add(new LinearMonomial<Flt64>(Flt64.One, _aggregation.Occupied[positionId]));
            }
            Try added = model.AddConstraint(
                new LinearInequality<Flt64>(
                    new LinearPolynomial<Flt64>(monomials, Flt64.Zero),
                    Number(Flt64.One),
                    Comparison.LE),
                this,
                name: $"{Name}_clique_{cliqueIndex}");
            if (added is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
                return added;
            }
        }

        foreach (LayoutSelectionGroup group in _problem.LayoutGroups) {
            var chosen = new List<LinearMonomial<Flt64>>(group.Layouts.Count);
            foreach (LoadingLayout layout in group.Layouts) {
                chosen.Add(new LinearMonomial<Flt64>(Flt64.One, _aggregation.Layouts[(group.Id, layout.Id)]));
            }
            Try selected = model.AddConstraint(
                new LinearInequality<Flt64>(
                    new LinearPolynomial<Flt64>(chosen, Flt64.Zero),
                    Number(Flt64.One),
                    Comparison.EQ),
                this,
                name: $"{Name}_layout_{group.Id}");
            if (selected is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
                return selected;
            }

            foreach (string positionId in group.PositionIds) {
                var choices = new List<LinearMonomial<Flt64>>();
                foreach (LoadingLayout layout in group.Layouts) {
                    if (layout.PositionIds.Contains(positionId)) {
                        choices.Add(new LinearMonomial<Flt64>(Flt64.One, _aggregation.Layouts[(group.Id, layout.Id)]));
                    }
                }
                Try governed = model.AddConstraint(
                    new LinearInequality<Flt64>(
                        new LinearPolynomial<Flt64>(
                            new List<LinearMonomial<Flt64>> {
                                new(Flt64.One, _aggregation.Occupied[positionId]),
                            },
                            Flt64.Zero),
                        new LinearPolynomial<Flt64>(choices, Flt64.Zero),
                        Comparison.LE),
                    this,
                    name: $"{Name}_layout_{group.Id}_{positionId}");
                if (governed is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
                    return governed;
                }
            }
        }

        return Results.OkInstance;
    }

    /// <summary>常数多项式 / A constant polynomial.</summary>
    /// <param name="value">常数 / The constant.</param>
    /// <returns>只含常数的多项式 / The polynomial holding only the constant.</returns>
    private static LinearPolynomial<Flt64> Number(Flt64 value) =>
        new(new List<LinearMonomial<Flt64>>(), value);
}
