// 稀疏求解器注册构件 / Sparse solver registration artifacts.
// 与 `DiscreteLoadingContext` 对应，
// 并把 `DiscreteLoadingContext.register` 里的"建变量 + 累计表达式"三圈提到这里复用。
// And lifts the "create variables, accumulate expressions" loops of
// `DiscreteLoadingContext.register` into a reusable builder.

using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading.Model;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<
    Fuookami.Ospf.Utils.Functional.Success,
    Fuookami.Ospf.Utils.Error.ErrorCode,
    Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading.Model;

/// <summary>
/// 稀疏求解器注册构件；领域到 `Flt64` 的转换只发生在注册边界 / Sparse solver registration artifacts.
/// </summary>
/// <remarks>
/// **`assignments` 只包含合法组合**，永不补全笛卡尔积——与 `AllowedAssignments` 的语义一致。
/// **`assignments` holds legal pairs only** and never completes a Cartesian product, matching `AllowedAssignments`.
///
/// **聚合由调用方持有**：它不挂在 `DiscreteLoadingContext` 实例上，而由调用者 `new` 一份再传进
/// <see cref="Build{V}"/>。**不放静态属性**——静态状态会让两个并发建模互相覆盖，而且比多写一行难查得多。
/// </remarks>
public sealed class LoadingAggregation {
    /// <summary>仅包含合法组合的变量 / Variables for legal pairs only.</summary>
    public Dictionary<AssignmentKey, BinVar> Assignments { get; } = new();

    /// <summary>整体占位变量 / Whole-position occupancy variables.</summary>
    public Dictionary<string, BinVar> Occupied { get; } = new();

    /// <summary>布局选择变量 / Layout-choice variables.</summary>
    public Dictionary<(string GroupId, string LayoutId), BinVar> Layouts { get; } = new();

    /// <summary>舱位件数 / Position counts.</summary>
    public Dictionary<string, LinearPolynomial<Flt64>> Counts { get; } = new();

    /// <summary>统一单位下的实际重量 / Actual weights in the normalised unit.</summary>
    public Dictionary<string, LinearPolynomial<Flt64>> Weights { get; } = new();

    /// <summary>每件货物的装载表达式 / Per-item loading expressions.</summary>
    public Dictionary<string, LinearPolynomial<Flt64>> Loaded { get; } = new();

    /// <summary>
    /// 建变量并累计表达式，不添加任何约束 / Create the variables and accumulate the expressions, adding no constraints.
    /// </summary>
    /// <typeparam name="V">领域数值 / Domain number.</typeparam>
    /// <param name="problem">领域输入 / Domain input.</param>
    /// <param name="capacities">已统一单位的重量上限 / Normalised weight capacities.</param>
    /// <param name="itemWeights">已统一单位的单件重量 / Normalised item weights.</param>
    /// <param name="model">元模型 / Meta model.</param>
    /// <returns>操作结果 / Operation result.</returns>
    /// <remarks>
    /// **这一圈只建变量、不加约束**——约束全在两个限制类里。把两件事分开的好处是：
    /// 变量与表达式的正确性可以**单独**验证，不必先跑一遍求解器。
    ///
    /// **一条跳过规则必须看清**：`(!enabled 或单件超重)` **且** `不是该货物的固定舱位` 时跳过这条分配。
    /// 两个条件用 `且` 连接——**固定货物即使舱位禁用、即使单件超重，也仍然生成变量**，
    /// 因为固定装载是需求方的硬要求，不能因为"放不下"就悄悄不建变量。
    ///
    /// **This loop only creates variables and adds no constraints** — those live entirely in the two limit classes, so variable and expression correctness can be
    /// verified **independently** without running a solver first. **One skip rule needs care**: an assignment is skipped when `(!enabled or item too heavy)` **and**
    /// `this is not that item's fixed position`. The conditions are joined with `and` — **fixed cargo still gets a variable even if the position is disabled or the
    /// item alone exceeds the capacity**, because a fixed load is the customer's hard requirement and must not silently lose its variable just because "it does not fit".
    /// </remarks>
    public Try Build<V>(
        DiscreteLoadingProblem<V> problem,
        IReadOnlyDictionary<string, Flt64> capacities,
        IReadOnlyDictionary<string, Flt64> itemWeights,
        LinearMetaModel<Flt64> model)
        where V : struct, IFloatingNumber<V> {
        var positions = new Dictionary<string, LoadingPosition<V>>();
        foreach (LoadingPosition<V> position in problem.Positions) {
            positions[position.Id] = position;
        }
        var items = new Dictionary<string, LoadingItem<V>>();
        var fixedPositionIds = new HashSet<string>();
        foreach (LoadingItem<V> item in problem.Items) {
            items[item.Id] = item;
            if (item.FixedPositionId is { } fixedPositionId) {
                fixedPositionIds.Add(fixedPositionId);
            }
        }

        foreach (LoadingPosition<V> position in problem.Positions) {
            var variable = new BinVar($"occupied_{position.Id}");
            if (position.FixedOccupied || fixedPositionIds.Contains(position.Id)) {
                variable.Range.Eq(true);
            }
            else if (!position.Enabled) {
                variable.Range.Eq(false);
            }
            this.Occupied[position.Id] = variable;
        }
        foreach (LoadingItem<V> item in problem.Items) {
            this.Loaded[item.Id] = Empty();
        }
        foreach (LoadingPosition<V> position in problem.Positions) {
            this.Counts[position.Id] = Empty();
            this.Weights[position.Id] = Empty();
        }

        var ordered = new List<AssignmentKey>(problem.AllowedAssignments);
        ordered.Sort((left, right) => {
            int byItem = string.CompareOrdinal(left.ItemId, right.ItemId);
            return byItem != 0 ? byItem : string.CompareOrdinal(left.PositionId, right.PositionId);
        });

        foreach (AssignmentKey key in ordered) {
            LoadingItem<V> item = items[key.ItemId];
            LoadingPosition<V> position = positions[key.PositionId];
            bool unusable = !position.Enabled || itemWeights[item.Id].CompareTo(capacities[position.Id]) > 0;
            if (unusable && item.FixedPositionId != position.Id) {
                continue;
            }
            if (item.FixedPositionId is { } fixedPositionId && fixedPositionId != position.Id) {
                continue;
            }
            var variable = new BinVar($"assignment_{key.ItemId}_{key.PositionId}");
            if (item.FixedPositionId == position.Id) {
                variable.Range.Eq(true);
            }
            this.Assignments[key] = variable;
            this.Loaded[item.Id] = this.Loaded[item.Id] + Monomial(variable);
            this.Counts[position.Id] = this.Counts[position.Id] + Monomial(variable);
            this.Weights[position.Id] = this.Weights[position.Id]
                + new LinearPolynomial<Flt64>(
                    new List<LinearMonomial<Flt64>> { new(itemWeights[item.Id], variable) },
                    Flt64.Zero);
        }

        foreach (LayoutSelectionGroup group in problem.LayoutGroups) {
            foreach (LoadingLayout layout in group.Layouts) {
                this.Layouts[(group.Id, layout.Id)] = new BinVar($"layout_{group.Id}_{layout.Id}");
            }
        }

        // **变量在这里一次性进模型**，两个限制类只负责加约束。
        // **All variables enter the model here**; the two limit classes only add constraints.
        foreach (BinVar variable in this.Occupied.Values) {
            Try added = model.Add(variable);
            if (added is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
                return added;
            }
        }
        foreach (BinVar variable in this.Assignments.Values) {
            Try added = model.Add(variable);
            if (added is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
                return added;
            }
        }
        foreach (BinVar variable in this.Layouts.Values) {
            Try added = model.Add(variable);
            if (added is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
                return added;
            }
        }

        return Results.OkInstance;
    }

    /// <summary>空多项式 / The empty polynomial.</summary>
    /// <returns>零多项式 / The zero polynomial.</returns>
    private static LinearPolynomial<Flt64> Empty() =>
        new(new List<LinearMonomial<Flt64>>(), Flt64.Zero);

    /// <summary>把单个二值变量包成多项式 / Wrap one binary variable as a polynomial.</summary>
    /// <param name="variable">变量 / The variable.</param>
    /// <returns>系数为 1 的多项式 / The polynomial with coefficient 1.</returns>
    private static LinearPolynomial<Flt64> Monomial(IVariableItem variable) =>
        new(new List<LinearMonomial<Flt64>> { new(Flt64.One, variable) }, Flt64.Zero);
}
