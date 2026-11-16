#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Model.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.Iis;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Solver.Iis;

/// <summary>
/// CP 冲突分析器 / The CP conflict analyzer.
/// </summary>
/// <remarks>
/// B1261 按基线补这个分析器。**按 B1260 的经验先查依赖，结果又是"只差适配器"：
/// `IConstraintProgrammingSolver`、`ConstraintProgrammingInfeasibleOutput`、`SolveDiagnostics.InfeasibilityEvidence`、
/// `CollectConflict`／`ShrinkConflict` 全都在。**
///
/// adds
/// missing" again.**
/// </remarks>
public class ConstraintProgrammingConflictAnalyzerTests
{
    /// <summary>
    /// **它会强制打开 `CollectConflict`，即使调用方的选项里是关的** /
    /// It forces `CollectConflict` on even when the caller's options have it off.
    /// </summary>
    /// <remarks>
    /// **这是本批最要紧的一条，因为那个失败是<b>误导性</b>的：** **一个"冲突分析器"在不收集冲突时什么也分析不出来，
    /// 而失败会以"CP 输出缺少结构化 conflict 证据"的形式出现——看起来像求解器的问题，
    /// 其实是分析器没把开关打开。**
    ///
    /// **判据用一个<b>会记录收到的选项</b>的假求解器，直接核对开关。**
    /// **只断言"最终拿到了证据"不够：那只能证明这一次凑巧通了。**
    ///
    /// **This is the batch's key criterion because that failure is <b>misleading</b>: a conflict analyzer that does not
    /// collect conflicts analyzes nothing, and the failure reads "CP output has no structured conflict evidence" — like a
    /// solver problem when it is the analyzer failing to flip the switch.**
    ///
    /// **The criterion uses a fake solver that <b>records the options it receives</b>.** **Asserting "evidence came back"
    /// would not do: that only proves this run happened to work.**
    /// </remarks>
    [Fact]
    public async Task ItShouldForceConflictCollectionOn() {
        var solver = new RecordingSolver();

        var analyzer = new ConstraintProgrammingConflictAnalyzer(
            solver,
            new ConstraintProgrammingSolveOptions(CollectConflict: false));

        using ConstraintProgrammingModel model = new();
        Result<InfeasibilityEvidence, ErrorCode, Error<ErrorCode>> result =
            await analyzer.AnalyzeAsync(model);

        bool ok = result.IsOk;

        ok.Should().BeTrue(result.ToString());
        solver.ReceivedOptions.Should().NotBeNull();
        solver.ReceivedOptions!.CollectConflict.Should().BeTrue(
            "a conflict analyzer must collect conflicts, or its own failure message blames the solver");
    }

    /// <summary>
    /// **证据被打成"精确 / 机器验证"，而不是与弹性过滤同一档** /
    /// The evidence is graded exact / machine-checked, not at the elastic path's grade.
    /// </summary>
    /// <remarks>
    /// **两条路的证据强度不同：** **弹性过滤是启发式（`Exact = false`、`Heuristic`），
    /// 而这个分析器取的是求解器原生报出的冲突（`Exact = true`、`MachineChecked`）。**
    ///
    /// **把它们报成同一档会让"机器验证过的冲突"与"松弛过滤的猜测"在矩阵上无法区分。**
    ///
    /// **The two paths have different evidence strength: elastic filtering is heuristic, while this takes the solver's native
    /// conflict.** **Reporting them alike would make "machine-verified conflict" indistinguishable from "a relaxation guess".
    /// **</remarks>
    [Fact]
    public async Task ItShouldReportExactMachineCheckedEvidence() {
        var analyzer = new ConstraintProgrammingConflictAnalyzer(new RecordingSolver());
        analyzer.Capabilities.Exact.Should().BeTrue("a native conflict is exact, unlike a relaxation filter");
        analyzer.Capabilities.Source.Should().Be(InfeasibilityEvidenceSource.ConstraintConflict);
        analyzer.Source.Should().Be(InfeasibilityEvidenceSource.ConstraintConflict);

        using ConstraintProgrammingModel model = new();
        Result<InfeasibilityEvidence, ErrorCode, Error<ErrorCode>> result =
            await analyzer.AnalyzeAsync(model);

        bool ok = result.IsOk;

        ok.Should().BeTrue(result.ToString());
        InfeasibilityEvidence evidence =
            ((Ok<InfeasibilityEvidence, ErrorCode, Error<ErrorCode>>)result).Value;
        evidence.Validity.Should().Be(EvidenceValidity.Verified,
            "the conflict came from the solver, so it is Verified rather than Heuristic");
        evidence.Source.Should().Be(InfeasibilityEvidenceSource.ConstraintConflict);
    }

    /// <summary>
    /// **求解器报了可行时，分析器说清"这不是不可行输出"** /
    /// When the solver reports feasibility, the analyzer says so plainly.
    /// </summary>
    /// <remarks>
    /// **这条钉的是错误信息的内容，而不是"有没有失败"：**
    /// **"未返回不可行输出"与"缺少结构化证据"是两种不同的情况，
    /// 而它们的修法完全不同——前者要重新检查模型，后者要检查收集开关。**
    ///
    /// **This pins the message's <b>content</b> rather than that it failed: "did not return an infeasible output" and
    /// "missing structured evidence" are different situations with different fixes — the former means re-examine the model,
    /// the latter means check the collection switch.**
    /// </remarks>
    [Fact]
    public async Task AFeasibleOutput_ShouldBeReportedAsSuchNotAsMissingEvidence() {
        var analyzer = new ConstraintProgrammingConflictAnalyzer(new FeasibleSolver());

        using ConstraintProgrammingModel model = new();
        Result<InfeasibilityEvidence, ErrorCode, Error<ErrorCode>> result =
            await analyzer.AnalyzeAsync(model);

        result.IsOk.Should().BeFalse();
        var failed = (Failed<InfeasibilityEvidence, ErrorCode, Error<ErrorCode>>)result;
        failed.Error.Message.Should().Contain("未返回不可行输出");
        failed.Error.Message.Should().NotContain("缺少结构化 conflict 证据");
    }

    private sealed class RecordingSolver : IConstraintProgrammingSolver {
        public ConstraintProgrammingSolveOptions? ReceivedOptions { get; private set; }

        public SolverDescriptor Descriptor { get; } = new(
            SolverId: "recording",
            BackendName: "test",
            Capabilities: new SolverCapabilities(ModelTypes: new HashSet<SolverModelType>()));

        public Task<Result<ConstraintProgrammingSolverOutput, ErrorCode, Error<ErrorCode>>> SolveAsync(
            ConstraintProgrammingModel model,
            ConstraintProgrammingSolveOptions? options = null,
            CancellationToken cancellationToken = default) {
            ReceivedOptions = options;
            var report = new SolveReport<Fuookami.Ospf.Math.Algebra.Number.Int64> {
                ProblemStatus = ProblemStatus.Infeasible,
                TerminationReason = TerminationReason.Completed,
                SolutionPresence = SolutionPresence.None,
                Diagnostics = new SolveDiagnostics<Fuookami.Ospf.Math.Algebra.Number.Int64>(
                    InfeasibilityEvidence: new InfeasibilityEvidence {
                        Source = InfeasibilityEvidenceSource.ConstraintConflict,
                        Exactness = EvidenceExactness.Exact,
                        Completeness = EvidenceCompleteness.Complete,
                        Validity = EvidenceValidity.Verified,
                        Minimality = EvidenceMinimality.Irreducible,
                    }),
            };
            return Task.FromResult<Result<ConstraintProgrammingSolverOutput, ErrorCode, Error<ErrorCode>>>(
                new Ok<ConstraintProgrammingSolverOutput, ErrorCode, Error<ErrorCode>>(
                    new ConstraintProgrammingInfeasibleOutput(report: report)));
        }

        public Result<IConstraintProgrammingSession, ErrorCode, Error<ErrorCode>> CreateSession(
            ConstraintProgrammingModel model,
            ConstraintProgrammingSolveOptions? options = null) =>
            throw new NotSupportedException();
    }

    private sealed class FeasibleSolver : IConstraintProgrammingSolver {
        public SolverDescriptor Descriptor { get; } = new(
            SolverId: "feasible",
            BackendName: "test",
            Capabilities: new SolverCapabilities(ModelTypes: new HashSet<SolverModelType>()));

        public Task<Result<ConstraintProgrammingSolverOutput, ErrorCode, Error<ErrorCode>>> SolveAsync(
            ConstraintProgrammingModel model,
            ConstraintProgrammingSolveOptions? options = null,
            CancellationToken cancellationToken = default) {
            var report = new SolveReport<Fuookami.Ospf.Math.Algebra.Number.Int64> {
                ProblemStatus = ProblemStatus.Feasible,
                TerminationReason = TerminationReason.Completed,
                SolutionPresence = SolutionPresence.Incumbent,
            };
            return Task.FromResult<Result<ConstraintProgrammingSolverOutput, ErrorCode, Error<ErrorCode>>>(
                new Ok<ConstraintProgrammingSolverOutput, ErrorCode, Error<ErrorCode>>(
                    new ConstraintProgrammingFeasibleOutput(
                        solution: new ConstraintProgrammingSolution(
                            values: new Dictionary<VariableId, Fuookami.Ospf.Math.Algebra.Number.Int64>()),
                        status: SolverStatus.Feasible,
                        proofStatus: ProofStatus.None,
                        report: report)));
        }

        public Result<IConstraintProgrammingSession, ErrorCode, Error<ErrorCode>> CreateSession(
            ConstraintProgrammingModel model,
            ConstraintProgrammingSolveOptions? options = null) =>
            throw new NotSupportedException();
    }
}
