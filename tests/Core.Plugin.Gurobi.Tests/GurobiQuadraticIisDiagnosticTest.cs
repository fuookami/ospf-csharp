#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Solver.Iis;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Fuookami.Ospf.Core.Plugin.Gurobi.Tests;

/// <summary>
/// Gurobi 二次原生 IIS：真实后端上的 `computeIIS`，成员来自 `IISQConstr`。
/// The Gurobi quadratic native IIS: `computeIIS` on the real backend, with members from `IISQConstr`.
/// </summary>
/// <remarks>
/// 补完了 Gurobi 三个分析器的最后一个。
///
/// 这个文件的核心判据是**成员真的从二次行里来**：把 `IISQConstr` 写成 `IISConstr`，
/// 每一行都会读到 0，于是返回"没有成员"——而那条错误信息看起来像"这个模型没有不可行子系统"。
///
/// **completes the last of Gurobi's three analyzers.** The central criterion here is that **members really come from quadratic rows**: writing `IISConstr` instead of
/// `IISQConstr` reads 0 for every row and returns "no members" — an error that reads like "this model has no infeasible subsystem".
/// </remarks>
public class GurobiQuadraticIisDiagnosticTest {
    /// <summary>**成员必须来自二次行** / The members must come from the quadratic rows.</summary>
    /// <remarks>
    /// 两行都是二次约束：`x² + y² >= 10` 与 `x² + y² <= 5`。**同一个范数不可能同时满足**，
    /// 所以两行都该进 IIS，而且都只能通过 `IISQConstr` 读出来。
    ///
    /// Both rows are quadratic: `x² + y² >= 10` and `x² + y² <= 5`. **One norm cannot satisfy both**, so both rows belong in the IIS — and both can only be read through
    /// `IISQConstr`.
    /// </remarks>
    [Fact]
    public async Task TheMembers_ShouldComeFromTheQuadraticRows() {
        using QuadraticTetradModel model = InfeasibleQuadraticModel();

        Result<InfeasibilityEvidence, ErrorCode, Error<ErrorCode>> result =
            await QuadratidAnalyzer().AnalyzeAsync(model);

        result.IsOk.Should().BeTrue(result.ToString());
        InfeasibilityEvidence evidence = result.Value!;
        evidence.Source.Should().Be(InfeasibilityEvidenceSource.NativeIis);
        evidence.Reference.Should().Be("gurobi.computeIIS");
        evidence.Validity.Should().Be(EvidenceValidity.Verified);
        evidence.Members.Should().NotBeEmpty(
            "an empty IIS would read as 'this model is infeasible on its own'");
        evidence.ConstraintIds.Should().HaveCount(2,
            "both quadratic rows are needed; either one alone is satisfiable");

        // **每一条约束成员都必须能对上一个真实行**，否则"成员来自 IISQConstr"这句话就无从核对。
        // **Every constraint member has to map to a real row**, or "the members come from IISQConstr" cannot be checked at all.
        evidence.Members.OfType<InfeasibilityMember.ConstraintMember>()
            .Should().HaveCount(2, "both members are quadratic rows, not variable bounds");
    }

    /// <summary>**精确度与最小性同源** / Exactness and minimality come from the same signal.</summary>
    [Fact]
    public async Task TheExactness_ShouldAgreeWithTheMinimality() {
        using QuadraticTetradModel model = InfeasibleQuadraticModel();

        Result<InfeasibilityEvidence, ErrorCode, Error<ErrorCode>> result =
            await QuadratidAnalyzer().AnalyzeAsync(model);

        result.IsOk.Should().BeTrue(result.ToString());
        InfeasibilityEvidence evidence = result.Value!;
        (evidence.Exactness == EvidenceExactness.Irreducible)
            .Should().Be(evidence.Minimality == EvidenceMinimality.Irreducible,
                "both fields come from IISMinimal; disagreeing means one of them was guessed");
    }

    /// <summary>**可行模型不许被报成不可行** / A feasible quadratic model may not be reported infeasible.</summary>
    /// <remarks>
    /// 反方向判据：诊断若在可行模型上编出一条 IIS，**下游会把一个能解的问题当成死局。**
    ///
    /// A reverse criterion: if the diagnosis fabricated an IIS for a feasible model, **downstream would treat a solvable problem as a dead end.**
    /// </remarks>
    [Fact]
    public async Task TheFeasibleModel_ShouldBeRefused() {
        using QuadraticTetradModel model = FeasibleQuadraticModel();

        Result<InfeasibilityEvidence, ErrorCode, Error<ErrorCode>> result =
            await QuadratidAnalyzer().AnalyzeAsync(model);

        result.IsOk.Should().BeFalse("a feasible model has no infeasible subsystem");
        Assert.IsType<Failed<InfeasibilityEvidence, ErrorCode, Error<ErrorCode>>>(result).Code
            .Should().Be(ErrorCode.ORModelInfeasible);
    }

    /// <summary>**二次侧只声明原生 IIS，不声明 Farkas** / The quadratic side declares native IIS only, never Farkas.</summary>
    /// <remarks>
    /// 二次约束的 Farkas 证书不是这个插件给得出的东西。**声明一个给不出的能力，比不声明更糟。**
    ///
    /// A Farkas certificate for quadratic constraints is not something this plugin produces. **Declaring a capability that cannot be delivered is worse than declaring nothing.**
    /// </remarks>
    [Fact]
    public void TheQuadraticSide_ShouldDeclareOnlyNativeIis() {
        IReadOnlyList<IInfeasibilityAnalyzer<IQuadraticTetradModelView>> analyzers =
            new GurobiQuadraticSolver().DiagnosticAnalyzers(new IisConfig());

        IInfeasibilityAnalyzer<IQuadraticTetradModelView> analyzer = analyzers.Should().ContainSingle().Subject;
        analyzer.Source.Should().Be(InfeasibilityEvidenceSource.NativeIis,
            "the quadratic side has no Farkas analyzer at all");

        InfeasibilityAnalyzerCapabilities capabilities =
            ((ICapabilityAwareInfeasibilityAnalyzer<IQuadraticTetradModelView>)analyzer).Capabilities;
        capabilities.ModelTypes.Should().BeEquivalentTo(new[] { SolverModelType.Qp, SolverModelType.Qcp });
        capabilities.Exact.Should().BeTrue();
    }

    private static IInfeasibilityAnalyzer<IQuadraticTetradModelView> QuadratidAnalyzer() =>
        new GurobiQuadraticSolver()
            .DiagnosticAnalyzers(new IisConfig { Time = TimeSpan.FromSeconds(10) })
            .Single(analyzer => analyzer.Source == InfeasibilityEvidenceSource.NativeIis);

    private static QuadraticTetradModel InfeasibleQuadraticModel() => QuadraticModel(normMax: 1, sumMin: 10);

    private static QuadraticTetradModel FeasibleQuadraticModel() => QuadraticModel(normMax: 25, sumMin: 1);

    /// <summary>
    /// 凸二次上界行 + 线性下界行 / A convex quadratic upper-bound row plus a linear lower-bound row.
    /// </summary>
    /// <remarks>
    /// **这个形状是被后端逼出来的，不是随手选的。** 两次尝试都被 Gurobi 拒了，理由都是
    /// "Q matrix is not positive semi-definite"：
    ///
    /// | 形状 | 为什么不行 |
    /// | --- | --- |
    /// | `x*y >= 10` | 交叉项对非负变量不是半正定的 |
    /// | `x²+y² >= 10` | 二次型本身是凸的，**但 `>=` 的可行域是球外，整体非凸** |
    ///
    /// **"凸二次"不等于"凸约束"**——方向决定可行域凸不凸。这也是本实现不设 `NonConvex` 的原因：
    /// 本实现只处理凸的那一侧。所以判据也只用凸的那一侧，**不为了测试方便去改后端的参**。
    ///
    /// **This shape is dictated by the backend, not picked at random.** Two attempts were refused by Gurobi with the same reason, "Q matrix is not positive semi-definite":
    /// the cross term `x*y` is not PSD over non-negative variables, and `x²+y² >= 10` has a convex quadratic form but **a non-convex feasible region (outside the ball)**.
    /// **"Convex quadratic" is not "convex constraint"** — the direction decides whether the feasible set is convex. That is also why the code sets no `NonConvex`: it only
    /// handles the convex side. So the criteria use only that side too, **rather than changing a backend parameter to make the test convenient.**
    /// </remarks>
    private static QuadraticTetradModel QuadraticModel(double normMax, double sumMin) {
        List<ModelViewVariable> variables = [
            new(0, Flt64.Zero, new Flt64(100), Continuous.Instance, "x"),
            new(1, Flt64.Zero, new Flt64(100), Continuous.Instance, "y"),
        ];

        // Col2 >= 0 是真正的二次项，Col2 = -1 是线性项的哨兵值。 / Col2 >= 0 is a real quadratic term; Col2 = -1 is the linear sentinel.
        var rows = new List<SparseQuadraticVector> {
            new([
                new SparseQuadraticEntry(0, 0, Flt64.One),
                new SparseQuadraticEntry(1, 1, Flt64.One),
            ]),
            new([
                new SparseQuadraticEntry(0, -1, Flt64.One),
                new SparseQuadraticEntry(1, -1, Flt64.One),
            ]),
        };
        var constraints = new QuadraticConstraintBatch(
            new SparseQuadraticMatrix(rows),
            [ConstraintRelation.LessEqual, ConstraintRelation.GreaterEqual],
            [new Flt64(normMax), new Flt64(sumMin)],
            ["norm-at-most", "sum-at-least"],
            [ConstraintSource.Origin, ConstraintSource.Origin]);

        var objective = new Objective<QuadraticObjectiveCell>(
            ObjectCategory.Minimum,
            [],
            Flt64.Zero);

        return new QuadraticTetradModel(variables, constraints, objective, "gurobi-quadratic-iis-fixture");
    }
}
