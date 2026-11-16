#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Solver;
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
/// Gurobi 原生 IIS 诊断：真实后端上的 `computeIIS`。
/// The Gurobi native IIS diagnosis: `computeIIS` on the real backend.
/// </summary>
/// <remarks>
/// 补了 里两个 IIS 类中的线性那个**。
/// 二次那个（`GurobiQuadraticNativeIISAnalyzer`）仍缺——它要处理二次约束，不是同一条路径。
///
/// **The linear one of the two IIS classes. is implemented** The quadratic one is still missing: it handles quadratic constraints, which is not
/// the same path.
/// </remarks>
public class GurobiNativeIisDiagnosticTest {
    /// <summary>**声明与实现一致：报告说支持原生 IIS，就真有一条跑得通的路** / The declaration matches the implementation.
    /// </summary>
    /// <remarks>
    /// `NativeIis` 从 `false` 改成 `true`，**不是因为改它能让分析器跑起来**——`LinearIisCompute` 传给编排器的
    /// 是 `solver.Descriptor?.Capabilities`，而 Gurobi 没覆写 `Descriptor`（默认 `null`），**这个标志当前不参与过滤**。
    /// 改它是因为**声明和实现对不上了**：这个能力，C# 现在也有了。
    ///
    /// 这条判据不查那个布尔值，**它查的是"这个能力真的能用"**：在一份真不可行的模型上走完整条路，
    /// 看回来的证据是不是 `NativeIis`。**标志对了但分析器是空壳，这句话就假了。**
    ///
    /// `NativeIis` moved from `false` to `true`, and **not because that is what lets the analyzer run** — `LinearIisCompute` hands the orchestrator
    /// `solver.Descriptor?.Capabilities` and Gurobi does not override `Descriptor` (default `null`), so **the flag does not currently take part in filtering.** It changed because
    /// **declaration and implementation stopped agreeing**: the code has this capability and C# now does too. This criterion does not inspect that boolean — **it checks that
    /// the capability actually works**, by running the whole path on a genuinely infeasible model and seeing whether the evidence comes back as `NativeIis`.
    /// **A correct flag over a hollow analyzer would make that sentence false.**
    /// </remarks>
    [Fact]
    public async Task TheDeclaredCapability_ShouldBeReachableEndToEnd() {
        using LinearTriadModel model = Triad(
            variables: [
                Column(0, "x", 0, 10),
                Column(1, "y", 0, 10),
            ],
            signs: [ConstraintRelation.GreaterEqual, ConstraintRelation.LessEqual],
            rhs: [5, 4]);

        var solver = new GurobiLinearSolver();
        solver.DiagnosticAnalyzers(new IisConfig())
            .Select(analyzer => analyzer.Source)
            .Should().Contain(InfeasibilityEvidenceSource.NativeIis,
                "the native IIS analyzer has to be in the list the Core orchestrator consumes");

        Result<LinearIisResult, ErrorCode, Error<ErrorCode>> computed =
            await solver.ComputeIISAsync(model, new IisConfig { Time = TimeSpan.FromSeconds(10) });
        computed.IsOk.Should().BeTrue(computed.ToString());
        computed.Value!.Evidence.Source.Should().Be(InfeasibilityEvidenceSource.NativeIis,
            "the whole path has to end on the native IIS, not on a legacy fallback");
        computed.Value.Evidence.Reference.Should().Be("gurobi.computeIIS");
    }
    /// <summary>**原生 IIS 覆盖 LP 与 MIP** / Native IIS covers both LP and MIP.</summary>
    /// <remarks>
    /// 这正是它与 Farkas 的分工：Farkas 只对连续 LP 成立，**而原生 IIS 对整数模型同样成立。**
    ///
    /// This is exactly how it differs from Farkas: Farkas only holds for a continuous LP, **while a native IIS holds for integer models too.**
    /// </remarks>
    [Fact]
    public void TheAnalyzer_ShouldDeclareBothLinearAndInteger() {
        IInfeasibilityAnalyzer<ILinearTriadModelView> analyzer = new GurobiLinearSolver()
            .DiagnosticAnalyzers(new IisConfig())
            .Single(candidate => candidate.Source == InfeasibilityEvidenceSource.NativeIis);

        InfeasibilityAnalyzerCapabilities capabilities = ((ICapabilityAwareInfeasibilityAnalyzer<ILinearTriadModelView>)analyzer).Capabilities;
        capabilities.ModelTypes.Should().BeEquivalentTo(new[] { SolverModelType.Lp, SolverModelType.Mip });
        capabilities.Exact.Should().BeTrue();
    }

    /// <summary>**整数模型能得到原生 IIS，而 Farkas 拿不到** / An integer model yields a native IIS and no Farkas.</summary>
    /// <remarks>
    /// 这条判据把两个分析器的分工钉在一起：同一个模型，**一个必须成功、另一个必须拒绝**。
    /// 只测其中一侧，会让"两个分析器其实是同一个"这种错误通过——因为它们都叫"不可行分析"。
    ///
    /// This criterion pins the two analyzers' division of labour on one model: **one must succeed and the other must refuse.** Testing only one side would let
    /// "both analyzers are really the same" pass, since both are called infeasibility analysis.
    /// </remarks>
    [Fact]
    public async Task TheIntegerModel_ShouldGetAnIisAndNoFarkas() {
        using LinearTriadModel model = Triad(
            variables: [
                IntegerColumn(0, "x", 0, 10),
                IntegerColumn(1, "y", 0, 10),
            ],
            signs: [ConstraintRelation.GreaterEqual, ConstraintRelation.LessEqual],
            rhs: [5, 4]);

        IReadOnlyList<IInfeasibilityAnalyzer<ILinearTriadModelView>> analyzers =
            new GurobiLinearSolver().DiagnosticAnalyzers(new IisConfig { Time = TimeSpan.FromSeconds(10) });

        Result<InfeasibilityEvidence, ErrorCode, Error<ErrorCode>> iis = await analyzers
            .Single(candidate => candidate.Source == InfeasibilityEvidenceSource.NativeIis)
            .AnalyzeAsync(model);
        iis.IsOk.Should().BeTrue(iis.ToString());
        iis.Value!.Source.Should().Be(InfeasibilityEvidenceSource.NativeIis);
        iis.Value.Members.Should().NotBeEmpty("an empty IIS would read as 'infeasible on its own'");
        iis.Value.Reference.Should().Be("gurobi.computeIIS");

        Result<InfeasibilityEvidence, ErrorCode, Error<ErrorCode>> farkas = await analyzers
            .Single(candidate => candidate.Source == InfeasibilityEvidenceSource.Farkas)
            .AnalyzeAsync(model);
        farkas.IsOk.Should().BeFalse(
            "an LP certificate cannot prove an integer model infeasible");
        Assert.IsType<Failed<InfeasibilityEvidence, ErrorCode, Error<ErrorCode>>>(farkas).Code
            .Should().Be(ErrorCode.IllegalArgument);
    }

    /// <summary>**可行性来自模型本身时，IIS 是空的** / When infeasibility comes from the model itself, the IIS is empty.</summary>
    /// <remarks>
    /// 这里钉的是**"成员为空必须报失败"**：报一条"已证明不可行但没有任何成员"的证据，下游会读成
    /// "模型自己不可行，删什么也没用"——**而那正是没归约出来的样子。**
    ///
    /// This pins **"an empty member set has to fail"**: evidence saying "proven infeasible with no members" would be read downstream as "the model is infeasible on its own,
    /// removing anything is pointless" — **which is exactly what a failed reduction looks like.**
    /// </remarks>
    [Fact]
    public async Task TheFeasibleModel_ShouldBeRefused() {
        using LinearTriadModel model = Triad(
            variables: [
                Column(0, "x", 0, 10),
                Column(1, "y", 0, 10),
            ],
            signs: [ConstraintRelation.GreaterEqual, ConstraintRelation.LessEqual],
            rhs: [1, 9]);

        Result<InfeasibilityEvidence, ErrorCode, Error<ErrorCode>> result = await new GurobiLinearSolver()
            .DiagnosticAnalyzers(new IisConfig { Time = TimeSpan.FromSeconds(10) })
            .Single(candidate => candidate.Source == InfeasibilityEvidenceSource.NativeIis)
            .AnalyzeAsync(model);

        result.IsOk.Should().BeFalse("a feasible model has no infeasible subsystem");
        Assert.IsType<Failed<InfeasibilityEvidence, ErrorCode, Error<ErrorCode>>>(result).Code
            .Should().Be(ErrorCode.ORModelInfeasible);
    }

    /// <summary>**精确度与最小性同源** / Exactness and minimality come from the same signal.</summary>
    /// <remarks>
    /// `IISMinimal` 决定两者。一个标了 `Irreducible` 却给了 `Heuristic` 精确度的证据，
    /// 会让下游**既以为它最小、又以为它只是启发式。**
    ///
    /// `IISMinimal` drives both. Evidence marked `Irreducible` while claiming `Heuristic` exactness would leave downstream believing it is **both minimal and merely a
    /// heuristic.**
    /// </remarks>
    [Fact]
    public async Task TheExactness_ShouldAgreeWithTheMinimality() {
        using LinearTriadModel model = Triad(
            variables: [
                Column(0, "x", 0, 10),
                Column(1, "y", 0, 10),
            ],
            signs: [ConstraintRelation.GreaterEqual, ConstraintRelation.LessEqual],
            rhs: [5, 4]);

        Result<InfeasibilityEvidence, ErrorCode, Error<ErrorCode>> result = await new GurobiLinearSolver()
            .DiagnosticAnalyzers(new IisConfig { Time = TimeSpan.FromSeconds(10) })
            .Single(candidate => candidate.Source == InfeasibilityEvidenceSource.NativeIis)
            .AnalyzeAsync(model);

        result.IsOk.Should().BeTrue(result.ToString());
        InfeasibilityEvidence evidence = result.Value!;
        (evidence.Exactness == EvidenceExactness.Irreducible)
            .Should().Be(evidence.Minimality == EvidenceMinimality.Irreducible,
                "both fields come from IISMinimal; disagreeing means one of them was guessed");
    }

    private static ModelViewVariable Column(int index, string name, double lower, double upper) => new(
        index: index,
        lowerBound: new Flt64(lower),
        upperBound: new Flt64(upper),
        type: Continuous.Instance,
        name: name);

    private static ModelViewVariable IntegerColumn(int index, string name, double lower, double upper) => new(
        index: index,
        lowerBound: new Flt64(lower),
        upperBound: new Flt64(upper),
        type: Integer.Instance,
        name: name);

    private static LinearTriadModel Triad(
        ModelViewVariable[] variables,
        ConstraintRelation[] signs,
        double[] rhs) {
        var rows = new SparseVector[variables.Length];
        for (int row = 0; row < variables.Length; row++) {
            rows[row] = new SparseVector(
                variables.Select((_, column) => new SparseVectorEntry(column, Flt64.One)).ToArray());
        }
        string[] names = [.. Enumerable.Range(0, variables.Length).Select(index => $"row-{index}")];
        return new LinearTriadModel(
            variables: variables,
            constraints: new LinearConstraintBatch(
                lhs: new SparseMatrix(rows),
                signs: signs,
                rhs: [.. rhs.Select(value => new Flt64(value))],
                names: names,
                sources: [.. names.Select(_ => ConstraintSource.Origin)],
                identities: [],
                identityNamespace: "gurobi-iis-fixture",
                identitySchemaVersion: "1"),
            objective: new Objective<LinearObjectiveCell>(
                category: ObjectCategory.Minimum,
                cells: [],
                constant: Flt64.Zero),
            name: "gurobi-iis-fixture");
    }
}
