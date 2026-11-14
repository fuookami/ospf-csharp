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
/// Gurobi Farkas 诊断：真实后端上的连续 LP 证书。
/// The Gurobi Farkas diagnosis: continuous-LP certificates on the real backend.
/// </summary>
/// <remarks>
/// **B979 补了 `GurobiInfeasibilityAnalyzers.kt` 里 Farkas 那一条**。这个文件里的每一条判据都真跑 Gurobi，
/// **没有一条用假后端**——Farkas 证书存在与否只有真后端答得出来。
///
/// **B979 ports the Farkas member of `GurobiInfeasibilityAnalyzers.kt`.** Every criterion here really runs Gurobi; **none uses a fake backend**, because whether a Farkas
/// certificate exists is something only a real backend can answer.
/// </remarks>
public class GurobiFarkasDiagnosticTest {
    /// <summary>**只声明 Farkas，且只声明 LP** / Only Farkas is declared, and only for LP.</summary>
    /// <remarks>
    /// 原生 IIS 还没搬，**`NativeIis` 就必须继续是 `false`**。提前写成 `true` 会让上层以为有一条它其实没有的提取路径。
    ///
    /// Native IIS is not ported yet, so **`NativeIis` must stay `false`**. Flipping it early would tell callers about an extraction path that does not exist.
    /// </remarks>
    [Fact]
    public void TheAnalyzer_ShouldDeclareOnlyContinuousFarkas() {
        IReadOnlyList<IInfeasibilityAnalyzer<ILinearTriadModelView>> analyzers =
            new GurobiLinearSolver().DiagnosticAnalyzers(new IisConfig());

        // **B980 之后列表里有两个分析器**：原生 IIS 在前。这里只挑 Farkas 那个来验它的声明。
        // **After B980 there are two analyzers**, native IIS first. This picks the Farkas one and checks its declaration.
        IInfeasibilityAnalyzer<ILinearTriadModelView> analyzer =
            analyzers.Single(candidate => candidate.Source == InfeasibilityEvidenceSource.Farkas);
        analyzer.Source.Should().Be(InfeasibilityEvidenceSource.Farkas);

        InfeasibilityAnalyzerCapabilities capabilities =
            ((ICapabilityAwareInfeasibilityAnalyzer<ILinearTriadModelView>)analyzer).Capabilities;
        capabilities.Exact.Should().BeTrue();
        capabilities.ModelTypes.Should().BeEquivalentTo(new[] { SolverModelType.Lp });
        capabilities.ModelTypes.Should().NotContain(SolverModelType.Mip,
            "a Farkas certificate cannot prove a mixed-integer model infeasible");
    }

    /// <summary>**两行矛盾给出的证书是那两行本身** / A two-row contradiction yields those two rows.</summary>
    [Fact]
    public async Task TheCertificate_ShouldCoverBothContradictoryRows() {
        using LinearTriadModel model = Triad(
            variables: [
                Column(0, "x", double.NegativeInfinity, double.PositiveInfinity),
                Column(1, "y", double.NegativeInfinity, double.PositiveInfinity),
            ],
            rows: [
                new([new SparseVectorEntry(0, Flt64.One), new SparseVectorEntry(1, Flt64.One)]),
                new([new SparseVectorEntry(0, Flt64.One), new SparseVectorEntry(1, Flt64.One)]),
            ],
            signs: [ConstraintRelation.GreaterEqual, ConstraintRelation.LessEqual],
            rhs: [2, 0],
            names: ["lower-row", "upper-row"]);
        Result<InfeasibilityEvidence, ErrorCode, Error<ErrorCode>> result =
            await Analyzer().AnalyzeAsync(model);

        result.IsOk.Should().BeTrue(result.ToString());
        InfeasibilityEvidence evidence = result.Value!;
        evidence.Source.Should().Be(InfeasibilityEvidenceSource.Farkas);
        evidence.Exactness.Should().Be(EvidenceExactness.Exact);
        evidence.Validity.Should().Be(EvidenceValidity.Verified,
            "the certificate comes from the backend, not from a heuristic search");
        evidence.Minimality.Should().Be(EvidenceMinimality.NotChecked,
            "a Farkas certificate is not an irreducibility claim");
        evidence.Reference.Should().Be("gurobi.farkas");
        evidence.ConstraintIds.Should().HaveCount(2,
            "both contradictory rows are needed; one alone is feasible");
        evidence.VariableBoundRefs.Should().BeEmpty(
            "unbounded variables carry no bound in this proof");
        evidence.Members.Should().HaveCount(2);
        evidence.Members.Should().OnlyContain(member => member is InfeasibilityMember.ConstraintMember);
    }

    /// <summary>**整数模型在花后端开销之前就被拒** / An integer model is refused before backend cost.</summary>
    /// <remarks>
    /// "跑了一遍才发现"的拒绝会让调用方以为后端尝试过，**而 LP 证书从原理上就不可能给出整数结论。**
    ///
    /// A refusal that only comes after a full solve leaves the caller thinking the backend tried, **when an LP certificate is impossible for an integer model anyway.**
    /// </remarks>
    [Fact]
    public async Task TheIntegerModel_ShouldBeRefused() {
        using LinearTriadModel model = Triad(
            variables: [
                IntegerColumn(0, "x", 0, 10),
                IntegerColumn(1, "y", 0, 10),
            ],
            rows: [
                new([new SparseVectorEntry(0, Flt64.One), new SparseVectorEntry(1, Flt64.One)]),
                new([new SparseVectorEntry(0, Flt64.One), new SparseVectorEntry(1, Flt64.One)]),
            ],
            signs: [ConstraintRelation.GreaterEqual, ConstraintRelation.LessEqual],
            rhs: [5, 4],
            names: ["lower-row", "upper-row"]);
        Result<InfeasibilityEvidence, ErrorCode, Error<ErrorCode>> result =
            await Analyzer().AnalyzeAsync(model);

        result.IsOk.Should().BeFalse("a continuous certificate cannot prove an integer model infeasible");
        Assert.IsType<Failed<InfeasibilityEvidence, ErrorCode, Error<ErrorCode>>>(result).Code
            .Should().Be(ErrorCode.IllegalArgument);
    }

    /// <summary>**可行模型不许被报成不可行** / A feasible model may not be reported infeasible.</summary>
    /// <remarks>
    /// 这是反方向的判据：诊断若在可行模型上编出一条证书，**下游会把一个能解的问题当成死局。**
    ///
    /// This criterion points the other way: if the diagnosis fabricated a certificate for a feasible model, **downstream would treat a solvable problem as a dead end.**
    /// </remarks>
    [Fact]
    public async Task TheFeasibleModel_ShouldBeRefused() {
        using LinearTriadModel model = Triad(
            variables: [
                Column(0, "x", 0, 10),
                Column(1, "y", 0, 10),
            ],
            rows: [
                new([new SparseVectorEntry(0, Flt64.One), new SparseVectorEntry(1, Flt64.One)]),
                new([new SparseVectorEntry(0, Flt64.One), new SparseVectorEntry(1, Flt64.One)]),
            ],
            signs: [ConstraintRelation.GreaterEqual, ConstraintRelation.LessEqual],
            rhs: [1, 9],
            names: ["lower-row", "upper-row"]);
        Result<InfeasibilityEvidence, ErrorCode, Error<ErrorCode>> result =
            await Analyzer().AnalyzeAsync(model);

        result.IsOk.Should().BeFalse("a feasible model has no Farkas certificate");
        Assert.IsType<Failed<InfeasibilityEvidence, ErrorCode, Error<ErrorCode>>>(result).Code
            .Should().Be(ErrorCode.ORModelInfeasible);
    }

    private static IInfeasibilityAnalyzer<ILinearTriadModelView> Analyzer() =>
        new GurobiLinearSolver()
            .DiagnosticAnalyzers(new IisConfig { Time = TimeSpan.FromSeconds(10) })
            .Single(analyzer => analyzer.Source == InfeasibilityEvidenceSource.Farkas);

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
        SparseVector[] rows,
        ConstraintRelation[] signs,
        double[] rhs,
        string[] names) => new(
            variables: variables,
            constraints: new LinearConstraintBatch(
                lhs: new SparseMatrix(rows),
                signs: signs,
                rhs: [.. rhs.Select(value => new Flt64(value))],
                names: names,
                sources: [.. names.Select(_ => ConstraintSource.Origin)],
                identities: [],
                identityNamespace: "gurobi-farkas-fixture",
                identitySchemaVersion: "1"),
            objective: new Objective<LinearObjectiveCell>(
                category: ObjectCategory.Minimum,
                cells: [],
                constant: Flt64.Zero),
            name: "gurobi-farkas-fixture");
}
