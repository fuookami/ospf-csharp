// 附加重量联动与一维几何适配判据 / Criteria for additional-weight linking and the 1-D geometry adapter.

using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading;
using Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading.Model;
using Fuookami.Ospf.Framework.Bpp.Infrastructure;
using Fuookami.Ospf.Framework.Bpp1d.Domain.DiscreteLoading;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;
using PositionConflict = Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading.Model.PositionConflict;
using Try = Fuookami.Ospf.Utils.Functional.Result<
    Fuookami.Ospf.Utils.Functional.Success,
    Fuookami.Ospf.Utils.Error.ErrorCode,
    Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.Bpp1d.Tests.Domain.DiscreteLoading;

/// <summary>
/// **附加重量联动与一维几何适配** / Additional-weight linking and the 1-D geometry adapter.
/// </summary>
/// <remarks>
/// 对应基线的 `linkAdditionalWeight()` 与 `Bpp1dDiscreteLoading.context()`：
///
/// | 点 | 错法 | 后果 |
/// | --- | --- | --- |
/// | 附加重量禁止为负 | 不写那条 `>= 0` | 外部塞进**负**表达式，在容量行里**抵消**真实载荷 |
/// | 容量行要含既有载荷 | 左端只写附加重量 | "已有装载 + 追加"可以超容量 |
/// | 舱位与几何一一对应 | 单向检查 | 没有区间的舱位**冲突全漏** |
/// | 冲突是合并 | 直接替换 | 调用方**业务上已声明的互斥**被悄悄丢掉 |
///
/// Mirrors the baseline's `linkAdditionalWeight()` and `Bpp1dDiscreteLoading.context()`; the table lists the four failure modes.
/// </remarks>
public class DiscreteLoadingLinkTests {
    /// <summary>**附加重量必须被约束为非负** / The additional weight must be constrained non-negative.</summary>
    /// <remarks>
    /// **本批最要紧的一条。** 这一层是**对外接口**——表达式由调用方传入。
    /// 若不写 `additional >= 0`，一个负表达式就能在容量行里**抵消**既有载荷，
    /// 让"再有 N 件"看起来还能装。**求解器不会报错，它只会返回那个超载的"最优解"。**
    ///
    /// **The most important criterion in this batch.** This layer is an **outward-facing entry point** receiving a caller-supplied expression. Without
    /// `additional &gt;= 0`, a negative expression **cancels** the existing load in the capacity row, making "N more pieces" look like they still fit. **The solver never
    /// complains; it just returns that overloaded "optimum".**
    /// </remarks>
    [Fact]
    public void LinkAdditionalWeight_ShouldConstrainTheAdditionalWeightNonNegative() {
        DiscreteLoadingProblem<Flt64> problem = MakeProblem(
            positions: [("P1", 5UL, 5000.0)]);
        var context = new DiscreteLoadingContext<Flt64>(problem, Flt64PackingValueAdapter.Instance);
        LinearMetaModel<Flt64> model = new();
        context.Register(model).IsOk.Should().BeTrue();

        Try result = context.LinkAdditionalWeight("P1", Additional(model, "guess"), model);

        result.IsOk.Should().BeTrue(result.ToString());
        RowOf(model, "additional_weight_nonnegative_P1").Comparison.Should().Be(
            Comparison.GE, "a negative additional weight would cancel the real cargo in the sum");
    }

    /// <summary>**容量行左端必须含既有载荷** / The capacity row's left side must include the existing weights.</summary>
    /// <remarks>
    /// 先让 `P1` 已有一条分配（既有载荷 1 项），再加上 1 项附加重量 ⇒ 左端是 **2** 项。
    /// 若只写附加重量，左端只有 1 项——"已有装载 + 追加"就能超容量。
    ///
    /// With one pre-existing assignment on `P1` plus one additional term the left side holds **2** monomials. Writing only the additional weight leaves **1**, letting
    /// "already loaded plus appended" exceed the capacity.
    /// </remarks>
    [Fact]
    public void LinkAdditionalWeight_ShouldIncludeTheExistingLoadOnTheLeftSide() {
        DiscreteLoadingProblem<Flt64> problem = MakeProblem(
            items: [("I1", 1000.0, false, null)],
            positions: [("P1", 5UL, 5000.0)]);
        var context = new DiscreteLoadingContext<Flt64>(problem, Flt64PackingValueAdapter.Instance);
        LinearMetaModel<Flt64> model = new();
        context.Register(model).IsOk.Should().BeTrue();

        context.LinkAdditionalWeight("P1", Additional(model, "guess"), model).IsOk.Should().BeTrue();

        LinearInequality<Flt64> row = RowOf(model, "additional_weight_capacity_P1");
        row.Lhs.Monomials.Should().HaveCount(
            2, "one assignment term plus one additional term: the existing load must not be dropped");
        row.Rhs.Monomials.Should().HaveCount(1, "the capacity multiplies the occupancy variable");
        row.Rhs.Monomials[0].Coefficient.ToDouble().Should().BeApproximately(
            5000.0, 1e-9, "the problem's weight unit is grams");
    }

    /// <summary>**未知舱位必须被拒，且是 `IllegalArgument`** / An unknown position must be rejected as `IllegalArgument`.</summary>
    [Fact]
    public void LinkAdditionalWeight_ShouldRejectAnUnknownPosition() {
        DiscreteLoadingProblem<Flt64> problem = MakeProblem(
            positions: [("P1", 5UL, 5000.0)]);
        var context = new DiscreteLoadingContext<Flt64>(problem, Flt64PackingValueAdapter.Instance);
        LinearMetaModel<Flt64> model = new();
        context.Register(model).IsOk.Should().BeTrue();

        Try result = context.LinkAdditionalWeight("NOPE", Additional(model, "guess"), model);

        result.IsFailed.Should().BeTrue("NOPE is not a registered position");
        ((Failed<Success, ErrorCode, Error<ErrorCode>>)result).Error.Code.Should().Be(ErrorCode.IllegalArgument);
    }

    /// </// <summary>**舱位与几何必须双向对应** / The position/geometry correspondence must hold both ways.</summary>
    /// <remarks>
    /// 两个方向各测一次：
    ///
    /// | 夹具 | 若只查一个方向 |
    /// | --- | --- |
    /// | 几何多一个不重叠的 `P9` | 多余的区间被静默忽略 |
    /// | 几何缺一个舱位（空列表） | **该舱位没有区间 ⇒ 它的真实冲突全部漏掉** |
    ///
    /// The mismatch is tested in both directions (table above); a one-way check silently ignores the other side, and a missing interval means **every real conflict of that
    /// position is lost**.
    /// </remarks>
    [Fact]
    public void Context_ShouldRejectGeometryThatDoesNotMatchThePositions() {
        DiscreteLoadingProblem<Flt64> problem = MakeProblem(
            positions: [("P1", 5UL, 5000.0)], includeAllParties: true);

        // **多余的 `P9` 必须与 `P1` 不重叠。** 第一版让两者都在 `[0,2]`，于是几何扫描产出一条
        // `P1—P9` 冲突，`validate()` 的"冲突引用必须存在"**先把它拒了**——判据绿了，
        // 但**没有测到"舱位与几何一一对应"那条检查**，单向检查的变异因此存活。
        //
        // **The extra `P9` must not overlap `P1`.** The first version put both on `[0,2]`, so the sweep produced a `P1—P9` edge and `validate()`'s "conflict references must
        // exist" **rejected it first** — green without **exercising the correspondence check**, letting the one-way mutant survive.
        var extraGeometry = new List<LoadingInterval<Flt64>> {
            Metres("P1", 0.0, 1.0),
            Metres("P9", 2.0, 3.0),
        };
        Bpp1dDiscreteLoading.Context(problem, extraGeometry, Flt64PackingValueAdapter.Instance)
            .IsFailed.Should().BeTrue("the geometry carries a position the problem does not have");

        var missingGeometry = new List<LoadingInterval<Flt64>>();
        Bpp1dDiscreteLoading.Context(problem, missingGeometry, Flt64PackingValueAdapter.Instance)
            .IsFailed.Should().BeTrue("the problem carries a position the geometry does not have");
    }

    /// <summary>**几何冲突必须被合并进去** / Geometry conflicts must be merged in.</summary>
    [Fact]
    public void Context_ShouldAddTheGeometryConflicts() {
        DiscreteLoadingProblem<Flt64> problem = MakeProblem(
            positions: [("P1", 5UL, 5000.0), ("P2", 5UL, 5000.0)]);
        var geometry = new List<LoadingInterval<Flt64>> {
            Metres("P1", 0.0, 2.0),
            Metres("P2", 1.0, 3.0),
        };

        Result<DiscreteLoadingContext<Flt64>, ErrorCode, Error<ErrorCode>> result =
            Bpp1dDiscreteLoading.Context(problem, geometry, Flt64PackingValueAdapter.Instance);

        result.IsOk.Should().BeTrue(result.ToString());
        DiscreteLoadingContext<Flt64> context =
            ((Ok<DiscreteLoadingContext<Flt64>, ErrorCode, Error<ErrorCode>>)result).Value;
        context.ConflictIndex.Cliques.Should().HaveCount(
            1, "P1 and P2 overlap in time, so one clique of two must appear");
    }

    /// <summary>**既有业务互斥必须保留，不能被几何冲突替换** / Pre-existing business conflicts must survive and not be replaced.</summary>
    /// <remarks>
    /// 业务上已声明 `P1—P2` 互斥（**几何上不重叠**，比如两条航线不能同时用）。
    /// 几何又给出 `P2—P3`。**正确结果是两条都在**；若直接替换，业务那条**悄悄消失**，
    /// 求解器会给出一个违反业务规则的解，而**不会报错**。
    ///
    /// The business rule declares `P1—P2` exclusive (**their intervals do not overlap at all**, e.g. two lanes that may not be used together) while geometry adds `P2—P3`.
    /// **Both must be present**; replacing instead makes the business edge **vanish silently**, and the solver returns a business-rule-violating solution **without an error**.
    /// </remarks>
    [Fact]
    public void Context_ShouldMergeBusinessAndGeometryConflicts() {
        DiscreteLoadingProblem<Flt64> problem = MakeProblem(
            positions: [("P1", 5UL, 5000.0), ("P2", 5UL, 5000.0), ("P3", 5UL, 5000.0)],
            conflicts: [("P1", "P2")]);
        var geometry = new List<LoadingInterval<Flt64>> {
            Metres("P1", 0.0, 1.0),
            Metres("P2", 2.0, 3.0),
            Metres("P3", 1.0, 3.0),
        };

        Result<DiscreteLoadingContext<Flt64>, ErrorCode, Error<ErrorCode>> result =
            Bpp1dDiscreteLoading.Context(problem, geometry, Flt64PackingValueAdapter.Instance);

        result.IsOk.Should().BeTrue(result.ToString());
        ConflictIndex index =
            ((Ok<DiscreteLoadingContext<Flt64>, ErrorCode, Error<ErrorCode>>)result).Value.ConflictIndex;

        bool businessKept = false;
        bool geometryAdded = false;
        foreach (IReadOnlySet<string> clique in index.Cliques) {
            if (clique.Contains("P1") && clique.Contains("P2")) {
                businessKept = true;
            }
            if (clique.Contains("P2") && clique.Contains("P3")) {
                geometryAdded = true;
            }
        }
        businessKept.Should().BeTrue("P1—P2 was declared by the caller and does not come from geometry");
        geometryAdded.Should().BeTrue("P2—P3 comes from the geometry sweep");
    }

    /// <summary>**非法问题在适配层就被拒** / An illegal problem is rejected at the adapter.</summary>
    [Fact]
    public void Context_ShouldRejectAnIllegalProblem() {
        DiscreteLoadingProblem<Flt64> problem = MakeProblem(
            items: [("I1", -1000.0, false, null)],
            positions: [("P1", 5UL, 5000.0)]);
        var geometry = new List<LoadingInterval<Flt64>> { Metres("P1", 0.0, 2.0) };

        Result<DiscreteLoadingContext<Flt64>, ErrorCode, Error<ErrorCode>> result =
            Bpp1dDiscreteLoading.Context(problem, geometry, Flt64PackingValueAdapter.Instance);

        result.IsFailed.Should().BeTrue("a negative cargo weight must never yield a usable context");
    }

    /// <summary>构造一个附加重量表达式 / Build an additional-weight expression.</summary>
    /// <param name="model">元模型 / The meta model.</param>
    /// <param name="name">变量名 / Variable name.</param>
    /// <returns>单变量多项式 / The single-variable polynomial.</returns>
    private static LinearPolynomial<Flt64> Additional(LinearMetaModel<Flt64> model, string name) {
        var variable = new URealVar(name);
        model.Add(variable).IsOk.Should().BeTrue();
        return new LinearPolynomial<Flt64>(new List<LinearMonomial<Flt64>> { new(Flt64.One, variable) }, Flt64.Zero);
    }

    /// <summary>按名称取出行 / Find a row by name.</summary>
    /// <param name="model">元模型 / The meta model.</param>
    /// <param name="name">约束名 / Constraint name.</param>
    /// <returns>线性不等式 / The linear inequality.</returns>
    private static LinearInequality<Flt64> RowOf(LinearMetaModel<Flt64> model, string name) {
        foreach (LinearInequalityConstraint<Flt64> row in model.RelationConstraints) {
            if (row.Name == name) {
                return row.Inequality;
            }
        }
        throw new global::System.InvalidOperationException($"no constraint named {name}");
    }

    /// <summary>构造区间 / Build an interval.</summary>
    /// <param name="id">舱位标识 / Position identity.</param>
    /// <param name="start">起点（米）/ Start in metres.</param>
    /// <param name="end">终点（米）/ End in metres.</param>
    /// <returns>区间 / The interval.</returns>
    private static LoadingInterval<Flt64> Metres(string id, double start, double end) =>
        new(id, "D1", new Quantity<Flt64>(new Flt64(start), SIBaseUnits.Meter),
            new Quantity<Flt64>(new Flt64(end), SIBaseUnits.Meter));

    /// <summary>构造问题 / Build a problem.</summary>
    /// <param name="positions">舱位：(标识, 最大件数, 克容量) / Positions: (identity, max count, gram capacity).</param>
    /// <param name="items">货物：(标识, 克重, 必装, 固定舱位) / Items: (identity, grams, required, fixed position).</param>
    /// <param name="conflicts">互斥对 / Conflicting pairs.</param>
    /// <param name="includeAllParties">是否补全全部合法分配 / Whether to complete every legal assignment.</param>
    /// <returns>问题 / The problem.</returns>
    private static DiscreteLoadingProblem<Flt64> MakeProblem(
        (string Id, ulong MaxCount, double Capacity)[] positions,
        (string Id, double Weight, bool Required, string? Fixed)[]? items = null,
        (string First, string Second)[]? conflicts = null,
        bool includeAllParties = true) {
        var itemList = new List<LoadingItem<Flt64>>();
        foreach ((string id, double weight, bool required, string? fixedPosition) in items ?? []) {
            itemList.Add(new LoadingItem<Flt64>(id, Grams(weight), Required: required, FixedPositionId: fixedPosition));
        }
        var positionList = new List<LoadingPosition<Flt64>>(positions.Length);
        foreach ((string id, ulong maxCount, double capacity) in positions) {
            positionList.Add(new LoadingPosition<Flt64>(id, maxCount, Grams(capacity)));
        }

        var assignments = new HashSet<AssignmentKey>();
        if (includeAllParties) {
            foreach (LoadingItem<Flt64> item in itemList) {
                foreach (LoadingPosition<Flt64> position in positionList) {
                    assignments.Add(new AssignmentKey(item.Id, position.Id));
                }
            }
        }

        var conflictSet = new HashSet<PositionConflict>();
        if (conflicts is not null) {
            foreach ((string first, string second) in conflicts) {
                conflictSet.Add(new PositionConflict(first, second));
            }
        }

        return new DiscreteLoadingProblem<Flt64>(
            Items: itemList,
            Positions: positionList,
            AllowedAssignments: assignments,
            Conflicts: conflictSet,
            LayoutGroups: new List<LayoutSelectionGroup>(),
            WeightUnit: MassUnits.Gram);
    }

    /// <summary>构造克量 / Build a gram quantity.</summary>
    /// <param name="value">数值 / Value.</param>
    /// <returns>带单位的量 / The unit-carrying quantity.</returns>
    private static Quantity<Flt64> Grams(double value) =>
        new(new Flt64(value), MassUnits.Gram);
}
