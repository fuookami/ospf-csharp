#nullable enable
#if SCIP_INTEGRATION

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Solver;
using Fuookami.Ospf.Core.Solver.Iis;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Variable;

namespace Fuookami.Ospf.Core.Plugin.Scip.Tests;

/// <summary>
/// SCIP 结构化不可行诊断验收：连续 LP 的 Farkas 证书与能力声明。
/// Acceptance tests for the structured SCIP infeasibility diagnosis: continuous-LP Farkas certificates and capability
/// declarations.
/// </summary>
public class ScipFarkasDiagnosticTest {
    /// <summary>线性后端只声明 Farkas，不声明原生 IIS。 / The linear backend declares Farkas but not native IIS.</summary>
    [Fact]
    public void DiagnosticAnalyzersExposeOnlyContinuousFarkas() {
        IReadOnlyList<IInfeasibilityAnalyzer<ILinearTriadModelView>> analyzers =
            new ScipLinearSolver().DiagnosticAnalyzers(new IisConfig());

        IInfeasibilityAnalyzer<ILinearTriadModelView> analyzer = Assert.Single(analyzers);
        Assert.Equal(InfeasibilityEvidenceSource.Farkas, analyzer.Source);
        InfeasibilityAnalyzerCapabilities capabilities =
            Assert.IsAssignableFrom<ICapabilityAwareInfeasibilityAnalyzer<ILinearTriadModelView>>(analyzer).Capabilities;
        Assert.True(capabilities.Exact);
        Assert.Equal(new HashSet<SolverModelType> { SolverModelType.Lp }, capabilities.ModelTypes);
        Assert.False(ScipLinearNativeSolver.LinearCapabilities.NativeIis);
        Assert.True(ScipLinearNativeSolver.LinearCapabilities.Farkas);
        Assert.True(ScipLinearNativeSolver.LinearCapabilities.Dual);
    }

    /// <summary>两行矛盾只需 LP 证伪：证书是两行本身，不含变量界。 / A two-row contradiction falsified by the LP yields the two rows and no bounds.</summary>
    [Fact]
    public async Task FarkasCertificateCoversBothContradictoryRows() {
        using LinearTriadModel model = Triad(
            variables: [
                Column(0, "x", double.NegativeInfinity, double.PositiveInfinity),
                Column(1, "y", double.NegativeInfinity, double.PositiveInfinity),
                Column(2, "unused", 0, 1),
            ],
            rows: [
                new([new SparseVectorEntry(0, Flt64.One), new SparseVectorEntry(1, Flt64.One)]),
                new([new SparseVectorEntry(0, Flt64.One), new SparseVectorEntry(1, Flt64.One)]),
            ],
            signs: [ConstraintRelation.GreaterEqual, ConstraintRelation.LessEqual],
            rhs: [2, 0],
            names: ["lower-row", "upper-row"],
            identities: [
                Identity("fixture:constraint:lower"),
                Identity("fixture:constraint:upper"),
            ]);
        Result<InfeasibilityEvidence, ErrorCode, Error<ErrorCode>> result =
            await FarkasAnalyzer().AnalyzeAsync(model);

        Assert.True(result.IsOk, result.ToString());
        InfeasibilityEvidence evidence = result.Value!;
        Assert.Equal(InfeasibilityEvidenceSource.Farkas, evidence.Source);
        Assert.Equal(EvidenceExactness.Exact, evidence.Exactness);
        Assert.Equal(EvidenceCompleteness.Complete, evidence.Completeness);
        Assert.Equal(EvidenceValidity.Verified, evidence.Validity);
        Assert.Equal(EvidenceMinimality.NotChecked, evidence.Minimality);
        Assert.Equal("scip.getDualFarkasLinear", evidence.Reference);
        Assert.NotNull(evidence.Elapsed);
        Assert.Equal(
            new[] { "fixture:constraint:lower", "fixture:constraint:upper" },
            evidence.ConstraintIds.Select(id => id.Value).OrderBy(value => value, StringComparer.Ordinal).ToArray());
        Assert.Empty(evidence.VariableBoundRefs);
        Assert.Equal(2, evidence.Members.Count);
        Assert.All(evidence.Members, member => Assert.IsType<InfeasibilityMember.ConstraintMember>(member));
    }

    /// <summary>需要 LP 才能证伪的打包矛盾：一行需求加一行两两上限，再加三个变量界。 / The packing contradiction needs the LP: one demand row, one pairwise cap and three variable bounds.</summary>
    [Fact]
    public async Task FarkasCertificateAddsTheBoundsItAggregated() {
        using LinearTriadModel model = Triad(
            variables: [
                Column(0, "x1", 0, 1),
                Column(1, "x2", 0, 1),
                Column(2, "x3", 0, 1),
            ],
            rows: [
                new([new SparseVectorEntry(0, Flt64.One), new SparseVectorEntry(1, Flt64.One)]),
                new([new SparseVectorEntry(0, Flt64.One), new SparseVectorEntry(2, Flt64.One)]),
                new([new SparseVectorEntry(1, Flt64.One), new SparseVectorEntry(2, Flt64.One)]),
                new([
                    new SparseVectorEntry(0, Flt64.One),
                    new SparseVectorEntry(1, Flt64.One),
                    new SparseVectorEntry(2, Flt64.One),
                ]),
            ],
            signs: [
                ConstraintRelation.LessEqual,
                ConstraintRelation.LessEqual,
                ConstraintRelation.LessEqual,
                ConstraintRelation.GreaterEqual,
            ],
            rhs: [1, 1, 1, 1.6],
            names: ["p12", "p13", "p23", "demand"],
            objective: [
                new LinearObjectiveCell(0, Flt64.One),
                new LinearObjectiveCell(1, Flt64.One),
                new LinearObjectiveCell(2, Flt64.One),
            ]);
        Result<InfeasibilityEvidence, ErrorCode, Error<ErrorCode>> result =
            await FarkasAnalyzer().AnalyzeAsync(model);

        Assert.True(result.IsOk, result.ToString());
        InfeasibilityEvidence evidence = result.Value!;
        Assert.Contains(new ConstraintId("model-local-constraint:demand:3"), evidence.ConstraintIds);
        Assert.Equal(1, evidence.ConstraintIds.Count(id => !id.Value.EndsWith("demand:3", StringComparison.Ordinal)));
        Assert.Equal(
            new[] { "model-local-constraint:demand:3", "model-local-constraint:p12:0" },
            evidence.ConstraintIds.Select(id => id.Value).OrderBy(value => value, StringComparer.Ordinal).ToArray());
        Assert.Equal(
            new[] {
                new VariableBoundRef(new VariableId("model-local-variable:0"), BoundSide.Upper),
                new VariableBoundRef(new VariableId("model-local-variable:1"), BoundSide.Upper),
                new VariableBoundRef(new VariableId("model-local-variable:2"), BoundSide.Lower),
            },
            evidence.VariableBoundRefs.OrderBy(reference => reference.VariableId.Value, StringComparer.Ordinal).ToArray());
        Assert.Equal(
            new[] { "model-local-variable:0", "model-local-variable:1", "model-local-variable:2" },
            evidence.VariableBoundIds.Select(id => id.Value).OrderBy(value => value, StringComparer.Ordinal).ToArray());
        Assert.Equal(5, evidence.Members.Count);
    }

    /// <summary>转换阶段即判定不可行时没有 LP 行，证书必须如实失败而不是编造成员。 / Infeasibility proven during transformation has no LP row, so the certificate must fail instead of inventing members.</summary>
    [Fact]
    public async Task TransformationStageContradictionHasNoFarkasComponent() {
        using LinearTriadModel model = Triad(
            variables: [Column(0, "y", double.NegativeInfinity, double.PositiveInfinity)],
            rows: [
                new([new SparseVectorEntry(0, Flt64.One)]),
                new([new SparseVectorEntry(0, Flt64.One)]),
            ],
            signs: [ConstraintRelation.GreaterEqual, ConstraintRelation.LessEqual],
            rhs: [2.5, 2],
            names: ["floor", "cap"]);
        Result<InfeasibilityEvidence, ErrorCode, Error<ErrorCode>> result =
            await FarkasAnalyzer().AnalyzeAsync(model);

        Failed<InfeasibilityEvidence, ErrorCode, Error<ErrorCode>> failed =
            Assert.IsType<Failed<InfeasibilityEvidence, ErrorCode, Error<ErrorCode>>>(result);
        Assert.Equal(ErrorCode.OREngineSolvingException, failed.Code);
        Assert.Contains("未返回非零 Farkas 成分", failed.Message, StringComparison.Ordinal);
    }

    /// <summary>整数列不在 LP 证书的有效范围内。 / Integer columns are outside the scope of an LP certificate.</summary>
    [Fact]
    public async Task IntegerColumnsAreRejectedBeforeTheDiagnosticLp() {
        using LinearTriadModel model = Triad(
            variables: [
                new ModelViewVariable(
                    index: 0,
                    lowerBound: Flt64.Zero,
                    upperBound: Flt64.One,
                    type: Binary.Instance,
                    name: "z"),
            ],
            rows: [
                new([new SparseVectorEntry(0, Flt64.One)]),
                new([new SparseVectorEntry(0, Flt64.One)]),
            ],
            signs: [ConstraintRelation.GreaterEqual, ConstraintRelation.LessEqual],
            rhs: [1, 0],
            names: ["floor", "cap"]);
        Result<InfeasibilityEvidence, ErrorCode, Error<ErrorCode>> result =
            await FarkasAnalyzer().AnalyzeAsync(model);

        Assert.Equal(ErrorCode.IllegalArgument,
            Assert.IsType<Failed<InfeasibilityEvidence, ErrorCode, Error<ErrorCode>>>(result).Code);
    }

    /// <summary>可行模型没有证书可提取。 / A feasible model has no certificate to extract.</summary>
    [Fact]
    public async Task FeasibleModelIsNotReportedAsACertificate() {
        using LinearTriadModel model = Triad(
            variables: [Column(0, "x", 0, 1)],
            rows: [new([new SparseVectorEntry(0, Flt64.One)])],
            signs: [ConstraintRelation.LessEqual],
            rhs: [0.5],
            names: ["cap"]);
        Result<InfeasibilityEvidence, ErrorCode, Error<ErrorCode>> result =
            await FarkasAnalyzer().AnalyzeAsync(model);

        Assert.Equal(ErrorCode.ORModelInfeasible,
            Assert.IsType<Failed<InfeasibilityEvidence, ErrorCode, Error<ErrorCode>>>(result).Code);
    }

    /// <summary>
    /// 旧 IIS 入口给出不可行输出、证据与归约后的子系统，而不再是“暂不支持”，也不是原模型快照。
    /// The legacy IIS entry now returns the infeasible output, evidence and a reduced subsystem instead of "not yet
    /// supported" and instead of an original-model snapshot.
    /// </summary>
    [Fact]
    public async Task LegacyIisEntryCarriesEvidenceInsteadOfRejection() {
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
            names: ["lower-row", "upper-row"],
            identities: [
                Identity("fixture:constraint:lower"),
                Identity("fixture:constraint:upper"),
            ]);
        var solver = new ScipLinearSolver();

        Result<SolverOutput, ErrorCode, Error<ErrorCode>> result = await solver.InvokeAsync(
            model: model,
            iisConfig: new IisConfig { Time = TimeSpan.FromSeconds(5) });

        Assert.True(result.IsOk, result.ToString());
        LinearInfeasibleSolverOutput output = Assert.IsType<LinearInfeasibleSolverOutput>(result.Value);
        Assert.Equal(SolverStatus.Infeasible, output.Status);
        Assert.True(output.IisAvailable);
        using LinearIisModel iis = Assert.IsType<LinearIisModel>(output.Iis);
        // SCIP 没有原生 IIS，子系统由 legacy 弹性过滤给出，因此只保留被违反的行。
        // SCIP has no native IIS, so the subsystem comes from legacy elastic filtering and keeps only violated rows.
        Assert.NotEmpty(iis.OriginRows);
        Assert.All(iis.OriginRows, row => Assert.InRange(row, 0, 1));
        Assert.True(iis.RelaxedFeasible);
        InfeasibilityEvidence? evidence = output.Diagnostics?.InfeasibilityEvidence;
        Assert.NotNull(evidence);
        Assert.Equal(InfeasibilityEvidenceSource.ElasticFilter, evidence!.Source);
        Assert.Equal(EvidenceExactness.Heuristic, evidence.Exactness);
        Assert.NotEmpty(evidence.ConstraintIds);
        Assert.True(evidence.ConstraintIds.Count <= 2);

        var report = output.ToSolveReport();
        Assert.Equal(ProblemStatus.Infeasible, report.ProblemStatus);
        Assert.Equal(InfeasibilityEvidenceSource.ElasticFilter, report.Diagnostics.InfeasibilityEvidence?.Source);
        Assert.Equal(EvidenceCompleteness.Complete, report.Diagnostics.InfeasibilityEvidence?.Completeness);
    }

    private static IInfeasibilityAnalyzer<ILinearTriadModelView> FarkasAnalyzer(
        IisConfig? config = null) =>
        new ScipLinearSolver()
            .DiagnosticAnalyzers(config ?? new IisConfig { Time = TimeSpan.FromSeconds(5) })
            .Single(analyzer => analyzer.Source == InfeasibilityEvidenceSource.Farkas);

    private static ModelViewVariable Column(int index, string name, double lower, double upper) => new(
        index: index,
        lowerBound: new Flt64(lower),
        upperBound: new Flt64(upper),
        type: Continuous.Instance,
        name: name);

    // Stable 身份必须带来源与 namespace/schema，否则模型校验会在求解边界拒绝。 / A stable identity needs provenance plus namespace/schema, otherwise model validation rejects it at the solve boundary.
    private static ModelElementIdentity Identity(string id) => new(
        Kind: ModelElementKind.Constraint,
        Id: new ModelElementId(id),
        Scope: ModelElementScope.Stable,
        Origin: new ModelElementOrigin(Kind: "fixture", Key: id));

    private static LinearTriadModel Triad(
        ModelViewVariable[] variables,
        SparseVector[] rows,
        ConstraintRelation[] signs,
        double[] rhs,
        string[] names,
        ModelElementIdentity[]? identities = null,
        LinearObjectiveCell[]? objective = null) => new(
            variables: variables,
            constraints: new LinearConstraintBatch(
                lhs: new SparseMatrix(rows),
                signs: signs,
                rhs: [.. rhs.Select(value => new Flt64(value))],
                names: names,
                sources: [.. names.Select(_ => ConstraintSource.Origin)],
                identities: identities ?? [],
                identityNamespace: "scip-farkas-fixture",
                identitySchemaVersion: "1"),
            objective: new Objective<LinearObjectiveCell>(
                category: ObjectCategory.Minimum,
                cells: objective ?? [],
                constant: Flt64.Zero),
            name: "farkas-fixture");
}
#endif
