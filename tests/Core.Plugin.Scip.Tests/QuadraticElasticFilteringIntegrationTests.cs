// 二次弹性过滤的真实求解判据 / Real-solver criteria for quadratic elastic filtering.

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
/// **二次弹性过滤的三层递进，用真实 SCIP 验证** / The three-tier quadratic elastic filter, verified against real SCIP.
/// </summary>
/// <remarks>
/// **B398—B403 我一直以"无求解器"为由拒绝移植这三个函数。B408 证明那个前提是错的**，
/// 本批据此把 `RelaxSpecificComponents` 与 `PerformElasticFiltering` 移植过来。
///
/// 判据用的模型（两层递进的关键）：
///
/// | 索引 | 名称 | 角色 |
/// | --- | --- | --- |
/// | `0` | `x` | 普通列，上界 `10` |
/// | `1` | `s` | **松弛列**，无边界变量、无来源行 ⇒ 只在**第 2 层**才被放宽 |
///
/// 约束 `x + s ≥ 20`：不给 `s` 放宽时**不可行**（`x ≤ 10`）；第 2 层把 `s` 的上界放到 `+∞`
/// 之后可行，`s = 10`。
///
/// | 实现 | 结果 |
/// | --- | --- |
/// | 三层递进正确 | 第 1 层不可行、**第 2 层可行**，`s = 10` |
/// | 直接跳到第 3 层 | 也可行，但**无法区分**——所以另有一条判据钉住"第 1 层优先" |
///
/// **B398—B403 refused to port these three functions on the grounds that "there is no solver". B408 proved that premise false**, so this batch ports
/// `RelaxSpecificComponents` and `PerformElasticFiltering`. The fixture is designed so that tier 1 **cannot** help but tier 2 can (tables above).
/// </remarks>
public class QuadraticElasticFilteringIntegrationTests {
    private const int X = 0;
    private const int Slack = 1;

    /// <summary>**第 1 层不可行时必须递进到第 2 层并给出松弛量** / When tier 1 cannot help, escalate to tier 2 and report the relaxed amount.</summary>
    [Fact]
    public async Task PerformElasticFilteringAsync_ShouldEscalateToTheSecondTier() {
        using IQuadraticTetradModelView model = MakeModel(SumGreaterOrEqual(20.0));

        Result<QuadraticElasticFilterResult, ErrorCode, Error<ErrorCode>> result =
            await QuadraticLegacyIisCompute.PerformElasticFilteringAsync(
                model, new ScipQuadraticSolver(), new Flt64(1e-6));

        result.IsOk.Should().BeTrue(result.ToString());
        var ok = (Ok<QuadraticElasticFilterResult, ErrorCode, Error<ErrorCode>>)result;
        ok.Value.Feasible.Should().BeTrue("tier 2 relaxes the slack column, so the model becomes feasible");
        ok.Value.Relaxed.Should().ContainKey(
            Slack,
            "the slack column carries the whole infeasibility, so it must be the one reported as relaxed");
        ok.Value.Relaxed[Slack].ToDouble().Should().BeApproximately(
            10.0, 1e-6,
            "x contributes 10 and the right-hand side is 20, so the slack must carry exactly 10");
    }

    /// <summary>**第 1 层能解决时不能往下走** / When tier 1 suffices, later tiers must not run.</summary>
    /// <remarks>
    /// 反向对照：上一条只证明"第 2 层能解决"。少了这条，一个**总是跳到第 3 层**的实现也能通过。
    ///
    /// 这里给松弛列一个**边界变量**，于是第 1 层就能放宽它：可行时 `Relaxed` 只应包含该列，
    /// 且**不应**包含那些只有第 3 层才会放宽的组件。
    ///
    /// Reverse control: the previous criterion only proves "tier 2 can work", so an implementation that **always jumps to tier 3** would also pass. Here the slack column is
    /// given a **bound variable**, so tier 1 alone can relax it; when feasible, `Relaxed` must contain that column and **not** the components only tier 3 would relax.
    /// </remarks>
    [Fact]
    public async Task PerformElasticFilteringAsync_ShouldStopAtTheFirstTierThatWorks() {
        using IQuadraticTetradModelView model = MakeModel(SumGreaterOrEqual(20.0), withBoundVariable: true);

        Result<QuadraticElasticFilterResult, ErrorCode, Error<ErrorCode>> result =
            await QuadraticLegacyIisCompute.PerformElasticFilteringAsync(
                model, new ScipQuadraticSolver(), new Flt64(1e-6));

        result.IsOk.Should().BeTrue(result.ToString());
        var ok = (Ok<QuadraticElasticFilterResult, ErrorCode, Error<ErrorCode>>)result;
        ok.Value.Feasible.Should().BeTrue("tier 1 relaxes the bound variable and that is enough");
        ok.Value.Relaxed.Should().OnlyContain(
            pair => pair.Key == Slack,
            "only the slack column is relaxed at tier 1; anything else means the filter skipped ahead");
    }

    /// <summary>**三层都不可行时返回"未找到"，而不是报错** / When all three tiers fail, report "not found" rather than an error.</summary>
    /// <remarks>
    /// 用一个**与松弛无关**的矛盾（`x ≥ 20` 且 `x ≤ 10`）造出"放宽什么都没用"的局面。
    /// 此时正确行为是返回 `Feasible = false`，让调用方转去删除过滤——
    /// **不是抛异常，也不是假装可行**。
    ///
    /// A contradiction **unrelated to the slack** (`x ≥ 20` while `x ≤ 10`) makes relaxation useless. The correct behaviour is `Feasible = false` so the caller can fall
    /// back to deletion filtering — **not an exception, and not a fake success**.
    /// </remarks>
    [Fact]
    public async Task PerformElasticFilteringAsync_ShouldReportNotFoundWhenRelaxationCannotHelp() {
        using IQuadraticTetradModelView model = MakeModel(SumGreaterOrEqual(20.0), forceXAboveItsBound: true);

        Result<QuadraticElasticFilterResult, ErrorCode, Error<ErrorCode>> result =
            await QuadraticLegacyIisCompute.PerformElasticFilteringAsync(
                model, new ScipQuadraticSolver(), new Flt64(1e-6));

        result.IsOk.Should().BeTrue(result.ToString());
        var ok = (Ok<QuadraticElasticFilterResult, ErrorCode, Error<ErrorCode>>)result;
        ok.Value.Feasible.Should().BeFalse(
            "relaxing the slack cannot fix a contradiction on x itself, so the filter must say 'not found'");
        ok.Value.Relaxed.Should().BeEmpty("nothing was found, so nothing is reported as relaxed");
    }

    /// <summary>构造弹性模型 / Build the elastic model.</summary>
    /// <param name="rhs">约束右端项 / Constraint right-hand side.</param>
    /// <param name="withBoundVariable">是否给松弛列一个边界变量 / Whether the slack column gets a bound variable.</param>
    /// <param name="forceXAboveItsBound">是否加上与松弛无关的矛盾 / Whether to add a slack-independent contradiction.</param>
    /// <returns>模型视图 / The model view.</returns>
    private static IQuadraticTetradModelView MakeModel(
        double rhs,
        bool withBoundVariable = false,
        bool forceXAboveItsBound = false) {
        var slackBound = new ModelViewVariable(X + 100, new Flt64(0.0), new Flt64(1000.0), Continuous.Instance, "s_lb");
        ModelViewVariable slack = withBoundVariable
            ? new ModelViewVariable(
                Slack, new Flt64(0.0), new Flt64(1000.0), Continuous.Instance, "s",
                slack: new VariableSlack(LowerBound: slackBound))
            : new ModelViewVariable(
                Slack, new Flt64(0.0), new Flt64(1000.0), Continuous.Instance, "s",
                slack: new VariableSlack());

        var variables = new List<ModelViewVariable> {
            new(X, new Flt64(0.0), new Flt64(10.0), Continuous.Instance, "x"),
            slack,
        };

        var rows = new List<SparseQuadraticVector> {
            new(new List<SparseQuadraticEntry> {
                new(X, -1, Flt64.One),
                new(Slack, -1, Flt64.One),
            }),
        };
        var signs = new List<ConstraintRelation> { ConstraintRelation.GreaterEqual };
        var rightSides = new List<Flt64> { new Flt64(rhs) };
        var names = new List<string> { "row0" };
        var sources = new List<ConstraintSource> { ConstraintSource.Origin };

        if (forceXAboveItsBound) {
            // `x ≥ 20` 与 `x ≤ 10` 直接矛盾，且**与松弛列无关**——放宽 `s` 帮不上忙。
            // `x ≥ 20` contradicts `x ≤ 10` and is **independent of the slack**, so relaxing `s` cannot help.
            rows.Add(new SparseQuadraticVector(new List<SparseQuadraticEntry> { new(X, -1, Flt64.One) }));
            signs.Add(ConstraintRelation.GreaterEqual);
            rightSides.Add(new Flt64(20.0));
            names.Add("x_lower");
            sources.Add(ConstraintSource.Origin);
        }

        var objective = new Objective<QuadraticObjectiveCell>(
            category: ObjectCategory.Minimum,
            // **松弛列必须有惩罚。** 目标为空时可行域有无数个最优解，
            // SCIP 会返回任意一个（实测拿到 `s = 100000`），松弛量就没有确定值可比。
            // 弹性模型本来就要最小化松弛，所以这里给 `s` 系数 1。
            //
            // **The slack column needs a penalty.** With an empty objective the feasible region has infinitely many optima and the solver returns an arbitrary one
            // (measured: `s = 100000`), so the relaxed amount has no determinate value. An elastic model minimises slack anyway, so `s` gets coefficient 1.
            cells: new List<QuadraticObjectiveCell> { new(Slack, -1, Flt64.One) },
            constant: Flt64.Zero);

        return new QuadraticTetradModel(
            variables: variables,
            constraints: new QuadraticConstraintBatch(
                lhs: new SparseQuadraticMatrix(rows),
                signs: signs,
                rhs: rightSides,
                names: names,
                sources: sources),
            objective: objective,
            name: "elastic");
    }

    /// <summary>构造 `x + s ≥ rhs` / Build `x + s ≥ rhs`.</summary>
    /// <param name="rhs">右端项 / Right-hand side.</param>
    /// <returns>右端值 / The right-hand side value.</returns>
    private static double SumGreaterOrEqual(double rhs) => rhs;
}

#endif
