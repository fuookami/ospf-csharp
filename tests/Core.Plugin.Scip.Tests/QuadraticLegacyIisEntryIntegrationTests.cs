// 二次 IIS 完整入口的真实求解判据 / Real-solver criteria for the complete quadratic IIS entry point.

#if SCIP_INTEGRATION

using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Plugin.Scip;
using Fuookami.Ospf.Core.Solver;
using Fuookami.Ospf.Core.Solver.Iis;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Test.Core.Plugin.Scip;

/// <summary>
/// **二次 IIS 完整入口，用真实 SCIP 跑通** / The complete quadratic IIS entry point, driven by real SCIP.
/// </summary>
/// <remarks>
/// **B411 之前 `ComputeLegacyIISAsync` 恒返回 `Failed(LackOfPipelines)`**，因为弹性过滤与删除过滤都还没移植。
/// 两个函数现在都在，本判据钉住"入口真的能出结果"。
///
/// | 判据 | 钉住什么 |
/// | --- | --- |
/// | 参数非法 ⇒ `Failed` | 校验没有被子系统构建绕过 |
/// | 非 `QuadraticTetradModel` ⇒ `Failed(IllegalArgument)` | 拒绝无法弹性化的输入 |
/// | 可行模型 ⇒ `Ok` 且 `OriginRows` 为空 | 原始模型可行时**没有不可行子系统** |
/// | 不可行模型 ⇒ `Ok` 且 `OriginRows` 非空 | 真的找到了不可行子系统 |
/// | `OriginColumns` 与子系统列数一致 | 列回溯没有错位 |
///
/// **Before B411 `ComputeLegacyIISAsync` always returned `Failed(LackOfPipelines)`** because neither filter was ported; both are in place now, so these criteria pin
/// that the entry point really produces a result (see the table for what each one pins).
/// </remarks>
public class QuadraticLegacyIisEntryIntegrationTests {
    /// <summary>**可行的原模型没有不可行子系统** / A feasible original model has no infeasible subsystem.</summary>
    [Fact]
    public async Task ComputeLegacyIISAsync_ShouldReturnAnEmptySubsystemForAFeasibleModel() {
        using IQuadraticTetradModelView model = MakeModel(
            rows: [(0, -1, Flt64.One, ConstraintRelation.GreaterEqual, 1.0)],
            upperBound: 10.0);

        Result<QuadraticIisModel, ErrorCode, Error<ErrorCode>> result =
            await QuadraticLegacyIisCompute.ComputeLegacyIISAsync(model, new ScipQuadraticSolver(), new IisConfig());

        result.IsOk.Should().BeTrue(result.ToString());
        var ok = (Ok<QuadraticIisModel, ErrorCode, Error<ErrorCode>>)result;
        ok.Value.OriginRows.Should().BeEmpty(
            "x ≥ 1 is satisfiable with x ∈ [0,10], so there is no infeasible subsystem to report");
    }

    /// <summary>**不可行模型必须给出非空子系统** / An infeasible model must yield a non-empty subsystem.</summary>
    [Fact]
    public async Task ComputeLegacyIISAsync_ShouldFindTheSubsystemForAnInfeasibleModel() {
        // x ≥ 20 且 x ≤ 10：直接矛盾，松弛也救不回来 ⇒ 走删除过滤。
        // x ≥ 20 with x ≤ 10 is a flat contradiction that relaxation cannot fix, so deletion filtering runs.
        using IQuadraticTetradModelView model = MakeModel(
            rows: [
                (0, -1, Flt64.One, ConstraintRelation.GreaterEqual, 20.0),
                (0, -1, Flt64.One, ConstraintRelation.LessEqual, 10.0),
            ],
            upperBound: 10.0);

        Result<QuadraticIisModel, ErrorCode, Error<ErrorCode>> result =
            await QuadraticLegacyIisCompute.ComputeLegacyIISAsync(model, new ScipQuadraticSolver(), new IisConfig());

        result.IsOk.Should().BeTrue(result.ToString());
        var ok = (Ok<QuadraticIisModel, ErrorCode, Error<ErrorCode>>)result;
        ok.Value.OriginRows.Should().NotBeEmpty(
            "the model is infeasible, so at least one origin row must be reported");
        ok.Value.OriginColumns.Should().HaveCount(
            ok.Value.Subsystem.Variables.Count,
            "every subsystem column must trace back to exactly one origin column");
    }

    /// <summary>**参数非法时必须失败** / An invalid config must fail.</summary>
    [Fact]
    public async Task ComputeLegacyIISAsync_ShouldRejectAnInvalidConfig() {
        using IQuadraticTetradModelView model = MakeModel(
            rows: [(0, -1, Flt64.One, ConstraintRelation.GreaterEqual, 1.0)],
            upperBound: 10.0);

        Result<QuadraticIisModel, ErrorCode, Error<ErrorCode>> result =
            await QuadraticLegacyIisCompute.ComputeLegacyIISAsync(
                model,
                new ScipQuadraticSolver(),
                new IisConfig { SlackTolerance = new Flt64(double.NaN) });

        result.IsFailed.Should().BeTrue("a NaN tolerance must be rejected before any solving happens");
    }


    /// <summary>**删除过滤能区分"必要"与"不必要"的松弛** / Deletion filtering distinguishes necessary from unnecessary slacks.</summary>
    /// <remarks>
    /// **为什么不经过 `ComputeLegacyIISAsync`**：实测表明弹性过滤对**任何**模型都能救回
    /// （连 `x ∈ [20, 10]` 这种上下界矛盾也返回 `feasible=True`），因为弹性模型把边界也松弛了。
    /// 所以完整入口**永远走不到删除过滤**——这是真实的可达性问题，不是夹具没调好。
    ///
    /// 因此这里**直接调用** `PerformDeletionFilteringAsync` 覆盖它。
    /// 完整入口的删除分支**本批无法覆盖**，如实记录。
    ///
    /// **Why not through `ComputeLegacyIISAsync`**: measured, the elastic filter rescues **every** model (even contradictory bounds `x ∈ [20, 10]` return `feasible=True`)
    /// because the elastic model relaxes bounds too. So the full entry point **never reaches** deletion filtering.
    ///
    /// **B412 查证：这是基线行为，不是 C# 偏差。** 基线的 `QuadraticTetradElasticBuilder.kt:377` 同样给
    /// **每个有界变量**加 `ElasticLowerBound` / `ElasticUpperBound` 松弛行，入口结构也完全相同
    /// （弹性过滤命中即返回，否则才走删除过滤）。所以"删除分支几乎不可达"是**基线固有的性质**，
    /// C# 忠实还原了它——**本批不改代码**，只把这个事实钉下来。
    ///
    /// This therefore calls `PerformDeletionFilteringAsync` **directly**; the entry point's deletion branch **stays uncovered**, recorded honestly.
    ///
    /// **B412 verified this is baseline behaviour, not a C# deviation**: the baseline's `QuadraticTetradElasticBuilder.kt:377` also adds `ElasticLowerBound` /
    /// `ElasticUpperBound` slack rows for **every bounded variable**, and the entry structure is identical (elastic filter returns on success, deletion runs only otherwise).
    /// So "the deletion branch is nearly unreachable" is **inherent to the baseline**, faithfully reproduced here — **no code change in this batch**, just the fact pinned down.
    /// </remarks>
    [Fact]
    public async Task PerformDeletionFilteringAsync_ShouldKeepOnlyTheNecessarySlack() {
        using IQuadraticTetradModelView model = MakeModel(
            rows: [
                (0, -1, Flt64.One, ConstraintRelation.GreaterEqual, 20.0),
                (0, -1, Flt64.One, ConstraintRelation.LessEqual, 10.0),
            ],
            upperBound: 10.0);
        QuadraticTetradModel elastic = ((QuadraticTetradModel)model).Elastic();
        int slackCount = 0;
        for (int index = 0; index < elastic.Variables.Count; index++) {
            if (elastic.Variables[index].Slack is not null) {
                slackCount++;
            }
        }
        slackCount.Should().BeGreaterThan(0, "the elastic model must carry slack columns");

        Result<IReadOnlyList<int>, ErrorCode, Error<ErrorCode>> result =
            await QuadraticLegacyIisCompute.PerformDeletionFilteringAsync(
                elasticModel: elastic,
                solver: new ScipQuadraticSolver(),
                tolerance: new Flt64(1e-6),
                elapsed: global::System.TimeSpan.Zero,
                boundAmount: (ulong)slackCount,
                constraintAmount: (ulong)slackCount,
                config: new IisConfig());

        result.IsOk.Should().BeTrue(result.ToString());
        IReadOnlyList<int> kept = ((Ok<IReadOnlyList<int>, ErrorCode, Error<ErrorCode>>)result).Value;
        // **钉住确定的集合，不是"少于总数"。** 实测该模型有 4 条松弛列，
        // 保留 `row1_lb_slack`(2) 与 `x_ub_slack`(4)——这两条才是让 `x ≥ 20` 与 `x ≤ 10` 无法同时满足的原因；
        // `row0_ub_slack`(1) 与 `x_lb_slack`(3) 关掉之后仍不可行，因此**不必要**，被删除。
        //
        // 只断言"少于总数"会漏掉"删错了哪一条"——第一版变异因此存活。
        //
        // **Pin the exact set, not "fewer than total".** Measured, this model has 4 slack columns and keeps `row1_lb_slack`(2) and `x_ub_slack`(4) — those are what make
        // `x ≥ 20` and `x ≤ 10` incompatible; closing `row0_ub_slack`(1) or `x_lb_slack`(3) leaves it infeasible, so they are **unnecessary** and get dropped.
        // Asserting only "fewer than total" misses *which* one was dropped, which is why the first mutant survived.
        kept.Should().BeEquivalentTo(new[] { 2, 4 },
            "only row1_lb_slack and x_ub_slack are necessary for the contradiction");
    }

    /// <summary>构造单变量模型 / Build a single-variable model.</summary>
    /// <param name="rows">每行：(列索引, 列2, 系数, 关系, 右端项) / Each row: (column, partner, coefficient, relation, rhs).</param>
    /// <param name="upperBound">变量上界 / Variable upper bound.</param>
    /// <returns>模型视图 / The model view.</returns>
    private static IQuadraticTetradModelView MakeModel(
        (int Column, int Partner, Flt64 Coefficient, ConstraintRelation Relation, double Rhs)[] rows,
        double upperBound) {
        var variables = new List<ModelViewVariable> {
            new(0, Flt64.Zero, new Flt64(upperBound), Continuous.Instance, "x"),
        };
        var vectors = new List<SparseQuadraticVector>();
        var signs = new List<ConstraintRelation>();
        var rightSides = new List<Flt64>();
        var names = new List<string>();
        var sources = new List<ConstraintSource>();
        for (int index = 0; index < rows.Length; index++) {
            (int column, int partner, Flt64 coefficient, ConstraintRelation relation, double rhs) = rows[index];
            vectors.Add(new SparseQuadraticVector(new List<SparseQuadraticEntry> {
                new(column, partner, coefficient),
            }));
            signs.Add(relation);
            rightSides.Add(new Flt64(rhs));
            names.Add($"row{index}");
            sources.Add(ConstraintSource.Origin);
        }

        return new QuadraticTetradModel(
            variables: variables,
            constraints: new QuadraticConstraintBatch(
                lhs: new SparseQuadraticMatrix(vectors),
                signs: signs,
                rhs: rightSides,
                names: names,
                sources: sources),
            objective: new Objective<QuadraticObjectiveCell>(
                category: ObjectCategory.Minimum,
                cells: new List<QuadraticObjectiveCell> { new(0, -1, Flt64.One) },
                constant: Flt64.Zero),
            name: "iis-entry");
    }
}

#endif
