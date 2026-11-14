#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Core.Solver.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.Iis;
using Fuookami.Ospf.Core.Solver.Report;

namespace Fuookami.Ospf.Core.Tests.Solver.Iis;

/// <summary>
/// 后端无关的不可行分析 SPI、编排器与链的契约测试。
/// Contract tests for the backend-neutral infeasibility analyzer SPI, orchestrator and chain.
/// </summary>
public class InfeasibilityAnalyzerTest {
    [Fact]
    public void AvailableAnalyzersAreOrderedByStrategyAndFilteredByCapability() {
        StubCapable native = Stub(InfeasibilityEvidenceSource.NativeIis);
        StubCapable farkas = StubModelTypes(InfeasibilityEvidenceSource.Farkas, SolverModelType.Lp);
        StubCapable conflict = Stub(InfeasibilityEvidenceSource.ConstraintConflict);
        StubCapable elastic = Stub(InfeasibilityEvidenceSource.ElasticFilter);
        var plain = new StubPlain(
            InfeasibilityEvidenceSource.DeletionFilter,
            () => Results.Ok(Evidence(InfeasibilityEvidenceSource.DeletionFilter)));
        var orchestrator = new InfeasibilityDiagnosticOrchestrator<string>(
            [plain, elastic, conflict, farkas, native],
            SolverCapabilities(nativeIis: false, farkas: true, modelTypes: [SolverModelType.Lp]));

        IReadOnlyList<IInfeasibilityAnalyzer<string>> available = orchestrator.AvailableAnalyzers();

        // 原生 IIS 与冲突核被能力过滤删除，剩余分析器按策略优先级重排。 / Native IIS and the conflict core are filtered out and the rest are re-ordered by strategy.
        Assert.Equal(
            [
                InfeasibilityEvidenceSource.Farkas,
                InfeasibilityEvidenceSource.ElasticFilter,
                InfeasibilityEvidenceSource.DeletionFilter,
            ],
            available.Select(analyzer => analyzer.Source).ToArray());
        Assert.Equal(0, conflict.Calls + native.Calls);
    }

    [Fact]
    public void ConflictCoreAnalyzerSurvivesWhenTheBackendSupportsIt() {
        StubCapable conflict = Stub(InfeasibilityEvidenceSource.ConstraintConflict);
        var orchestrator = new InfeasibilityDiagnosticOrchestrator<string>(
            [conflict],
            SolverCapabilities(conflictCore: true));

        Assert.Equal(
            [InfeasibilityEvidenceSource.ConstraintConflict],
            orchestrator.AvailableAnalyzers().Select(analyzer => analyzer.Source).ToArray());
    }

    [Fact]
    public void CapabilityModelTypesMustIntersectWithTheSolver() {
        StubCapable mismatched = StubModelTypes(InfeasibilityEvidenceSource.Farkas, SolverModelType.Mip);
        var orchestrator = new InfeasibilityDiagnosticOrchestrator<string>(
            [mismatched],
            SolverCapabilities(farkas: true, modelTypes: [SolverModelType.Lp]));

        Assert.Empty(orchestrator.AvailableAnalyzers());
    }

    [Fact]
    public async Task WithoutUsableAnalyzersTheConclusionStaysUnavailable() {
        StubCapable native = Stub(InfeasibilityEvidenceSource.NativeIis);
        var orchestrator = new InfeasibilityDiagnosticOrchestrator<string>(
            [native],
            SolverCapabilities(nativeIis: false));

        Result<InfeasibilityEvidence, ErrorCode, Error<ErrorCode>> result = await orchestrator.AnalyzeAsync("model");

        InfeasibilityEvidence evidence =
            Assert.IsType<Ok<InfeasibilityEvidence, ErrorCode, Error<ErrorCode>>>(result).Value;
        Assert.Equal(InfeasibilityEvidenceSource.None, evidence.Source);
        Assert.Equal(EvidenceCompleteness.Unavailable, evidence.Completeness);
        Assert.Equal(EvidenceValidity.Unknown, evidence.Validity);
        Assert.Equal(EvidenceMinimality.NotChecked, evidence.Minimality);
        Assert.Equal("infeasibility-analysis-unavailable", evidence.UnavailableReason?.Code);
        Assert.Equal(SolveIssueCategory.Unsupported, evidence.UnavailableReason?.Category);
        Assert.Equal(0, native.Calls);
    }

    [Fact]
    public async Task ChainContinuesAfterFailureAndStopsOnFatal() {
        var first = new StubPlain(InfeasibilityEvidenceSource.NativeIis, () => Failed<InfeasibilityEvidence>());
        var second = new StubPlain(InfeasibilityEvidenceSource.Farkas, () => Results.Ok(Evidence(InfeasibilityEvidenceSource.Farkas)));
        var third = new StubPlain(InfeasibilityEvidenceSource.ElasticFilter, () => Results.Ok(Evidence(InfeasibilityEvidenceSource.ElasticFilter)));

        Result<InfeasibilityEvidence, ErrorCode, Error<ErrorCode>> continued =
            await new InfeasibilityAnalyzerChain<string>([first, second, third]).AnalyzeAsync("model");

        Assert.Equal(InfeasibilityEvidenceSource.Farkas,
            Assert.IsType<Ok<InfeasibilityEvidence, ErrorCode, Error<ErrorCode>>>(continued).Value.Source);
        Assert.Equal(1, first.Calls);
        Assert.Equal(1, second.Calls);
        Assert.Equal(0, third.Calls);

        var fatal = new StubPlain(InfeasibilityEvidenceSource.NativeIis, () => Fatal<InfeasibilityEvidence>());
        var after = new StubPlain(InfeasibilityEvidenceSource.Farkas, () => Results.Ok(Evidence(InfeasibilityEvidenceSource.Farkas)));
        Result<InfeasibilityEvidence, ErrorCode, Error<ErrorCode>> stopped =
            await new InfeasibilityAnalyzerChain<string>([fatal, after]).AnalyzeAsync("model");

        Assert.IsType<Fatal<InfeasibilityEvidence, ErrorCode, Error<ErrorCode>>>(stopped);
        Assert.Equal(0, after.Calls);
    }

    [Fact]
    public async Task ChainKeepsTheLastFailureWhenEveryAnalyzerFails() {
        var first = new StubPlain(InfeasibilityEvidenceSource.NativeIis, () => Failed<InfeasibilityEvidence>(ErrorCode.Other));
        var second = new StubPlain(InfeasibilityEvidenceSource.Farkas, () => Failed<InfeasibilityEvidence>(ErrorCode.IllegalArgument));

        Result<InfeasibilityEvidence, ErrorCode, Error<ErrorCode>> result =
            await new InfeasibilityAnalyzerChain<string>([first, second]).AnalyzeAsync("model");

        Assert.Equal(ErrorCode.IllegalArgument,
            Assert.IsType<Failed<InfeasibilityEvidence, ErrorCode, Error<ErrorCode>>>(result).Code);
    }

    [Fact]
    public async Task ChainReportsAnErrorWhenItIsEmpty() {
        Result<InfeasibilityEvidence, ErrorCode, Error<ErrorCode>> result = await
            new InfeasibilityAnalyzerChain<string>(Array.Empty<IInfeasibilityAnalyzer<string>>()).AnalyzeAsync("model");

        Assert.Equal(ErrorCode.Other,
            Assert.IsType<Failed<InfeasibilityEvidence, ErrorCode, Error<ErrorCode>>>(result).Code);
    }

    [Fact]
    public async Task OrchestratorUsesThePreferredStrategyFirst() {
        StubCapable farkas = Stub(InfeasibilityEvidenceSource.Farkas);
        StubCapable native = Stub(InfeasibilityEvidenceSource.NativeIis);
        var orchestrator = new InfeasibilityDiagnosticOrchestrator<string>([farkas, native]);

        Result<InfeasibilityEvidence, ErrorCode, Error<ErrorCode>> result = await orchestrator.AnalyzeAsync("model");

        Assert.Equal(InfeasibilityEvidenceSource.NativeIis,
            Assert.IsType<Ok<InfeasibilityEvidence, ErrorCode, Error<ErrorCode>>>(result).Value.Source);
        Assert.Equal(1, native.Calls);
        Assert.Equal(0, farkas.Calls);
    }

    [Fact]
    public async Task MaterializedRunKeepsArtifactsOnlyFromMaterializers() {
        StubCapable plain = Stub(InfeasibilityEvidenceSource.NativeIis);

        Result<MaterializedInfeasibilityEvidence<string>, ErrorCode, Error<ErrorCode>> withArtifact =
            await new InfeasibilityDiagnosticOrchestrator<string>([new StubMaterializer()])
                .AnalyzeMaterializedAsync<string>("model");
        Result<MaterializedInfeasibilityEvidence<string>, ErrorCode, Error<ErrorCode>> withoutArtifact =
            await new InfeasibilityDiagnosticOrchestrator<string>([plain])
                .AnalyzeMaterializedAsync<string>("model");

        Assert.Equal("artifact",
            Assert.IsType<Ok<MaterializedInfeasibilityEvidence<string>, ErrorCode, Error<ErrorCode>>>(withArtifact).Value.Artifact);
        Assert.Null(Assert.IsType<Ok<MaterializedInfeasibilityEvidence<string>, ErrorCode, Error<ErrorCode>>>(withoutArtifact).Value.Artifact);
    }

    [Fact]
    public async Task MaterializedRunReturnsUnavailableWhenNothingIsUsable() {
        StubCapable native = Stub(InfeasibilityEvidenceSource.NativeIis);
        var orchestrator = new InfeasibilityDiagnosticOrchestrator<string>(
            [native],
            SolverCapabilities(nativeIis: false));

        Result<MaterializedInfeasibilityEvidence<string>, ErrorCode, Error<ErrorCode>> result =
            await orchestrator.AnalyzeMaterializedAsync<string>("model");

        MaterializedInfeasibilityEvidence<string> materialized =
            Assert.IsType<Ok<MaterializedInfeasibilityEvidence<string>, ErrorCode, Error<ErrorCode>>>(result).Value;
        Assert.Null(materialized.Artifact);
        Assert.Equal(InfeasibilityEvidenceSource.None, materialized.Evidence.Source);
    }

    [Fact]
    public async Task MaterializedRunKeepsGoingAfterAFailureAndStopsOnFatal() {
        StubCapable failed = Stub(InfeasibilityEvidenceSource.NativeIis, () => Failed<InfeasibilityEvidence>());
        StubCapable fatal = Stub(InfeasibilityEvidenceSource.Farkas, () => Fatal<InfeasibilityEvidence>());
        var winner = new StubMaterializer();

        Result<MaterializedInfeasibilityEvidence<string>, ErrorCode, Error<ErrorCode>> continued =
            await new InfeasibilityDiagnosticOrchestrator<string>([failed, winner])
                .AnalyzeMaterializedAsync<string>("model");
        Assert.Equal("artifact",
            Assert.IsType<Ok<MaterializedInfeasibilityEvidence<string>, ErrorCode, Error<ErrorCode>>>(continued).Value.Artifact);

        // Fatal 按策略优先序排在后位，因此首个 Fatal 之后的分析器不再被调用。 / The fatal stub is ordered first by strategy, so the analyzer behind it is never called.
        StubCapable ignored = Stub(InfeasibilityEvidenceSource.ElasticFilter);
        Result<MaterializedInfeasibilityEvidence<string>, ErrorCode, Error<ErrorCode>> stopped =
            await new InfeasibilityDiagnosticOrchestrator<string>([fatal, ignored])
                .AnalyzeMaterializedAsync<string>("model");
        Assert.IsType<Fatal<MaterializedInfeasibilityEvidence<string>, ErrorCode, Error<ErrorCode>>>(stopped);
        Assert.Equal(0, ignored.Calls);
    }

    [Fact]
    public async Task AnalyzeIntoAttachesEvidenceWithoutChangingTheConclusion() {
        var orchestrator = new InfeasibilityDiagnosticOrchestrator<string>(
            [Stub(InfeasibilityEvidenceSource.Farkas, reference: "farkas")]);

        SolveReport<Flt64> diagnosed = await orchestrator.AnalyzeIntoAsync(InfeasibleReport(), "model");

        Assert.Equal(ProblemStatus.Infeasible, diagnosed.ProblemStatus);
        Assert.Equal(TerminationReason.Completed, diagnosed.TerminationReason);
        Assert.Equal("farkas", diagnosed.Diagnostics.InfeasibilityEvidence?.Reference);
        Assert.Empty(diagnosed.Diagnostics.Errors);
    }

    [Fact]
    public async Task AnalyzeIntoRecordsErrorsAndKeepsTheConclusionWhenDiagnosisFails() {
        var orchestrator = new InfeasibilityDiagnosticOrchestrator<string>(
            [Stub(InfeasibilityEvidenceSource.Farkas, () => Failed<InfeasibilityEvidence>(
                ErrorCode.OREngineSolvingException,
                "certificate unavailable"))]);

        SolveReport<Flt64> diagnosed = await orchestrator.AnalyzeIntoAsync(InfeasibleReport(), "model");

        Assert.Equal(ProblemStatus.Infeasible, diagnosed.ProblemStatus);
        Assert.Null(diagnosed.Diagnostics.InfeasibilityEvidence);
        SolveIssue issue = Assert.Single(diagnosed.Diagnostics.Errors);
        Assert.Equal("OREngineSolvingException", issue.Code);
        Assert.Equal(SolveIssueCategory.Backend, issue.Category);
        Assert.Equal("certificate unavailable", issue.Message);
    }

    [Fact]
    public async Task AdaptersExposeTheirSourceAndDelegateTheAnalysis() {
        var native = new NativeIisAnalyzerAdapter<string>(
            (_, _) => Task.FromResult(Results.Ok(Evidence(InfeasibilityEvidenceSource.NativeIis))),
            new HashSet<SolverModelType> { SolverModelType.Mip });
        var farkas = new FarkasAnalyzerAdapter<string>(
            (_, _) => Task.FromResult(Failed<InfeasibilityEvidence>(ErrorCode.Other)));
        var delegating = new DelegatingInfeasibilityAnalyzer<string>(
            AnalyzerCapabilities(InfeasibilityEvidenceSource.ConstraintConflict),
            (_, _) => Task.FromResult(Results.Ok(Evidence(InfeasibilityEvidenceSource.ConstraintConflict))));

        Assert.True(native.Capabilities.Exact);
        Assert.Equal(new HashSet<SolverModelType> { SolverModelType.Mip }, native.Capabilities.ModelTypes);
        Assert.Equal(InfeasibilityEvidenceSource.NativeIis, native.Source);
        Assert.Equal(InfeasibilityEvidenceSource.Farkas, farkas.Source);
        Assert.Equal(InfeasibilityEvidenceSource.ConstraintConflict, delegating.Source);
        Assert.Equal(InfeasibilityEvidenceSource.NativeIis,
            Assert.IsType<Ok<InfeasibilityEvidence, ErrorCode, Error<ErrorCode>>>(await native.AnalyzeAsync("model")).Value.Source);
        Assert.Equal(ErrorCode.Other,
            Assert.IsType<Failed<InfeasibilityEvidence, ErrorCode, Error<ErrorCode>>>(await farkas.AnalyzeAsync("model")).Code);
        Assert.True(await delegating.AnalyzeAsync("model") is Ok<InfeasibilityEvidence, ErrorCode, Error<ErrorCode>>);
    }

    [Fact]
    public async Task UnavailableAnalyzerReportsTheConfiguredReason() {
        InfeasibilityEvidence custom = Assert.IsType<Ok<InfeasibilityEvidence, ErrorCode, Error<ErrorCode>>>(
            await new UnavailableInfeasibilityAnalyzer<string>(new SolveIssue(
                Code: "custom",
                Category: SolveIssueCategory.Numerical,
                Message: "custom reason")).AnalyzeAsync("model")).Value;
        InfeasibilityEvidence defaultEvidence = Assert.IsType<Ok<InfeasibilityEvidence, ErrorCode, Error<ErrorCode>>>(
            await new UnavailableInfeasibilityAnalyzer<string>().AnalyzeAsync("model")).Value;

        Assert.Equal(InfeasibilityEvidenceSource.None, custom.Source);
        Assert.Equal("custom", custom.UnavailableReason?.Code);
        Assert.Equal(
            UnavailableInfeasibilityAnalyzer<string>.DefaultIssue.Code,
            defaultEvidence.UnavailableReason?.Code);
    }

    private static StubCapable Stub(
        InfeasibilityEvidenceSource source,
        string reference = "fixture") =>
        new(AnalyzerCapabilities(source), () => Results.Ok(Evidence(source, reference)));

    private static StubCapable Stub(
        InfeasibilityEvidenceSource source,
        Func<Result<InfeasibilityEvidence, ErrorCode, Error<ErrorCode>>> produce) =>
        new(AnalyzerCapabilities(source), produce);

    private static StubCapable StubModelTypes(
        InfeasibilityEvidenceSource source,
        params SolverModelType[] modelTypes) =>
        new(AnalyzerCapabilities(source, modelTypes), () => Results.Ok(Evidence(source)));

    private static InfeasibilityAnalyzerCapabilities AnalyzerCapabilities(
        InfeasibilityEvidenceSource source,
        params SolverModelType[] modelTypes) =>
        new(
            Source: source,
            ModelTypes: new HashSet<SolverModelType>(modelTypes),
            Exact: source is InfeasibilityEvidenceSource.NativeIis or InfeasibilityEvidenceSource.Farkas);

    private static SolverCapabilities SolverCapabilities(
        bool nativeIis = false,
        bool farkas = false,
        bool conflictCore = false,
        params SolverModelType[] modelTypes) =>
        new(
            ModelTypes: new HashSet<SolverModelType>(modelTypes),
            NativeIis: nativeIis,
            Farkas: farkas,
            ConstraintProgrammingFeatures: new Dictionary<ConstraintProgrammingFeature, ConstraintProgrammingSupportLevel> {
                [ConstraintProgrammingFeature.ConflictCore] = conflictCore
                    ? ConstraintProgrammingSupportLevel.Native
                    : ConstraintProgrammingSupportLevel.Unsupported,
            });

    private static InfeasibilityEvidence Evidence(InfeasibilityEvidenceSource source, string reference = "fixture") =>
        new() { Source = source, Reference = reference };

    private static SolveReport<Flt64> InfeasibleReport() => new() {
        ProblemStatus = ProblemStatus.Infeasible,
        TerminationReason = TerminationReason.Completed,
        SolutionPresence = SolutionPresence.None,
    };

    private static Result<T, ErrorCode, Error<ErrorCode>> Failed<T>(
        ErrorCode code = ErrorCode.OREngineSolvingException,
        string message = "failed") =>
        Results.Failed<T>(new Err<ErrorCode>(code, message));

    private static Result<T, ErrorCode, Error<ErrorCode>> Fatal<T>(ErrorCode code = ErrorCode.OREngineSolvingException) =>
        new Fatal<T, ErrorCode, Error<ErrorCode>>(code, "fatal");

    private sealed class StubPlain(
        InfeasibilityEvidenceSource source,
        Func<Result<InfeasibilityEvidence, ErrorCode, Error<ErrorCode>>> produce) : IInfeasibilityAnalyzer<string> {
        internal int Calls;

        public InfeasibilityEvidenceSource Source => source;

        public Task<Result<InfeasibilityEvidence, ErrorCode, Error<ErrorCode>>> AnalyzeAsync(
            string model,
            CancellationToken cancellationToken = default) {
            Calls++;
            return Task.FromResult(produce());
        }
    }

    private sealed class StubMaterializer : IMaterializingInfeasibilityAnalyzer<string, string> {
        public InfeasibilityEvidenceSource Source => InfeasibilityEvidenceSource.NativeIis;

        public Task<Result<InfeasibilityEvidence, ErrorCode, Error<ErrorCode>>> AnalyzeAsync(
            string model,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Results.Ok(Evidence(InfeasibilityEvidenceSource.NativeIis)));

        public Task<Result<MaterializedInfeasibilityEvidence<string>, ErrorCode, Error<ErrorCode>>>
            AnalyzeMaterializedAsync(string model, CancellationToken cancellationToken = default) =>
            Task.FromResult(Results.Ok(new MaterializedInfeasibilityEvidence<string>(
                Evidence(InfeasibilityEvidenceSource.NativeIis),
                "artifact")));
    }

    private sealed class StubCapable(
        InfeasibilityAnalyzerCapabilities capabilities,
        Func<Result<InfeasibilityEvidence, ErrorCode, Error<ErrorCode>>> produce)
        : ICapabilityAwareInfeasibilityAnalyzer<string> {
        internal int Calls;

        public InfeasibilityAnalyzerCapabilities Capabilities => capabilities;
        public InfeasibilityEvidenceSource Source => capabilities.Source;

        public Task<Result<InfeasibilityEvidence, ErrorCode, Error<ErrorCode>>> AnalyzeAsync(
            string model,
            CancellationToken cancellationToken = default) {
            Calls++;
            return Task.FromResult(produce());
        }
    }
}
