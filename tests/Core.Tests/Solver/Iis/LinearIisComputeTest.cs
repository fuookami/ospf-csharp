#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver;
using Fuookami.Ospf.Core.Solver.Iis;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Variable;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Core.Tests.Solver.Iis;

/// <summary>线性 IIS 物化与 legacy 过滤测试 / Linear IIS materialization and legacy filtering tests.</summary>
public class LinearIisComputeTest {
    private static readonly Flt64 Neg = NumericConstantsRegistry.For<Flt64>().NegativeInfinity!.Value;
    private static readonly Flt64 Pos = NumericConstantsRegistry.For<Flt64>().PositiveInfinity!.Value;

    /// <summary>
    /// 只放开变量界松弛的第一轮过滤：命中的松弛全部来自界，因此子系统没有约束行，只留下被保留边界的列。
    /// The first filtering round relaxes bound slacks only, so a hit yields no subsystem rows and only the columns whose
    /// bounds stay.
    /// </summary>
    [Fact]
    public async Task ComputeLegacyIIS_ShouldReturnBoundOnlySubsystemWhenFirstRoundHits() {
        using LinearTriadModel primal = BoundFixture();
        var solver = new ScriptedSolver(RelaxationReport);

        LinearIisModel iis = Unwrap(await solver.ComputeLegacyIISAsync(
            model: primal,
            config: new IisConfig()));
        using (iis) {
            Assert.Equal("primal_iis", iis.Name);
            Assert.True(iis.RelaxedFeasible);
            Assert.Null(iis.GuardConstraints);
            Assert.Same(primal, iis.Origin);
            Assert.Empty(iis.OriginRows);
            Assert.Equal([1], iis.OriginColumns);
            Assert.Empty(iis.Rows.Signs);
            // 界松弛同时命中上下界，因此 x1 的两个边界都保留下来。
            // Both bound slacks hit, so both bounds of x1 stay.
            Assert.Single(iis.Variables);
            Assert.Equal("x1", iis.Variables[0].Name);
            Assert.Equal(5, iis.Variables[0].LowerBound.ToDouble());
            Assert.Equal(6, iis.Variables[0].UpperBound.ToDouble());
            Assert.Equal(0, iis.Rows.Size);
            Assert.Single(solver.Seen);
        }
    }

    /// <summary>
    /// 第二轮过滤只放开不等式行与变量界松弛，等式行的松弛仍固定为 0，因此等式行不会进入子系统。
    /// The second round relaxes inequality rows and bounds only, so equality rows stay out of the subsystem.
    /// </summary>
    [Fact]
    public async Task ComputeLegacyIIS_ShouldSkipEqualityRowsWhenSecondRoundHits() {
        using LinearTriadModel primal = RowKindFixture();
        var solver = new ScriptedSolver(model => {
            int[] free = FreeSlackIndices(model);
            bool inequality = free.Any(index => IsInequalityRowSlack(model.Variables[index]));
            return Report(model, inequality ? free : [], feasible: inequality);
        });

        LinearIisModel iis = Unwrap(await solver.ComputeLegacyIISAsync(
            model: primal,
            config: new IisConfig()));
        using (iis) {
            Assert.Equal([0, 1], iis.OriginRows);
            Assert.Equal(["c0", "c1"], iis.Rows.Names);
            Assert.Equal([0], iis.OriginColumns);
            Assert.Equal(ConstraintRelation.LessEqual, iis.Rows.Signs[0]);
            Assert.Equal(3, iis.Rows.Rhs[0].ToDouble());
            Assert.Equal(ConstraintRelation.GreaterEqual, iis.Rows.Signs[1]);
            Assert.Equal(5, iis.Rows.Rhs[1].ToDouble());
            // x0 是自由列，子系统里没有保留它的任何一侧边界。
            // x0 is a free column, so neither of its bounds is kept in the subsystem.
            Assert.Single(iis.Variables);
            Assert.True(iis.Variables[0].LowerBound == Neg);
            Assert.True(iis.Variables[0].UpperBound == Pos);
            Assert.Equal(2, solver.SeenCount());
        }
    }

    /// <summary>
    /// 删除过滤按脚本把不可行子系统缩到单行，并留下空守卫批次，证据降级为部分完整的删除过滤。
    /// Deletion filtering shrinks the subsystem to a single row by script and leaves an empty guard batch, so the evidence
    /// degrades to a partial deletion-filter proof.
    /// </summary>
    [Fact]
    public async Task ComputeLegacyIIS_ShouldReduceSubsystemByDeletionFiltering() {
        using LinearTriadModel primal = RowKindFixture(equalRow: false);
        // 脚本语义：恰好一个松弛可自由时弹性模型才可行，因此全放开时不可行，删除过滤会留下最后一个自由松弛。
        // Script: the elastic model is feasible only when exactly one slack is free, so it stays infeasible when all are
        // open and deletion filtering keeps the last free slack.
        var solver = new ScriptedSolver(model => {
            int[] free = FreeSlackIndices(model);
            return Report(model, free.Length == 1 ? free : [], feasible: free.Length == 1);
        });
        var statuses = new List<IisComputingStatus>();

        LinearIisModel iis = Unwrap(await solver.ComputeLegacyIISAsync(
            model: primal,
            config: new IisConfig {
                ComputingStatusCallBack = (isFeasible, time, status) => {
                    statuses.Add(status);
                    return Results.OkInstance;
                },
            }));
        using (iis) {
            Assert.Equal([1], iis.OriginRows);
            Assert.Equal(["c1"], iis.Rows.Names);
            Assert.NotNull(iis.GuardConstraints);
            Assert.Empty(iis.GuardConstraints!.Signs);
            Assert.False(iis.RelaxedFeasible);
            // 三轮过滤 + 两个候选松弛的删除测试。 / Three filtering rounds plus two deletion candidates.
            Assert.Equal(5, solver.SeenCount());
            Assert.Equal(3, statuses.Count);
            Assert.Equal(0UL, statuses[0].TotalBoundAmount);
            Assert.Equal(2UL, statuses[0].TotalConstraintAmount);
            // 删除过滤先归零 c0 的松弛后模型已可行，因此 c0 被剔除，只剩 c1 的松弛仍在子系统里。
            // Zeroing c0's slack already restores feasibility, so c0 is dropped and only c1's slack stays in the subsystem.
            Assert.Equal(1UL, statuses[^1].RestConstraintAmount);
        }
    }

    /// <summary>所有松弛放开后仍不可行时，删除过滤保留全部行松弛，子系统覆盖这些松弛的来源行。 / Deletion filtering keeps every row slack when full relaxation stays infeasible, so the subsystem covers all their source rows.</summary>
    [Fact]
    public async Task ComputeLegacyIIS_ShouldKeepEveryRowSlackWhenAlwaysInfeasible() {
        using LinearTriadModel primal = RowKindFixture(equalRow: false);
        var solver = new ScriptedSolver(model => Report(model, [], feasible: false));

        LinearIisModel iis = Unwrap(await solver.ComputeLegacyIISAsync(
            model: primal,
            config: new IisConfig()));
        using (iis) {
            Assert.Equal([0, 1], iis.OriginRows);
            Assert.False(iis.RelaxedFeasible);
            Assert.Equal(2, iis.Rows.Size);
            // 两条行松弛都来自同一列 x0 的行，子系统因此只保留一列。
            // Both row slacks come from rows over x0, so the subsystem keeps a single column.
            Assert.Equal([0], iis.OriginColumns);
            Assert.Single(iis.Variables);
        }
    }

    /// <summary>Legacy IIS 需要可构建弹性模型的线性三元模型，配置非法时也直接拒绝。 / Legacy IIS needs a linear triad model that can build an elastic model, and an invalid configuration is rejected as well.</summary>
    [Fact]
    public async Task ComputeLegacyIIS_ShouldRejectNonTriadModelAndInvalidConfig() {
        var solver = new ScriptedSolver(model => Report(model, [], feasible: false));
        var view = new ViewOnlyModel();

        Assert.Equal(
            ErrorCode.IllegalArgument,
            Assert.IsType<Failed<LinearIisModel, ErrorCode, Error<ErrorCode>>>(
                await solver.ComputeLegacyIISAsync(
                    model: view,
                    config: new IisConfig())).Code);
        using LinearTriadModel primal = RowKindFixture(equalRow: false);
        Assert.Equal(
            ErrorCode.IllegalArgument,
            Assert.IsType<Failed<LinearIisModel, ErrorCode, Error<ErrorCode>>>(
                await solver.ComputeLegacyIISAsync(
                    model: primal,
                    config: new IisConfig { Time = TimeSpan.FromSeconds(-1) })).Code);
    }

    /// <summary>
    /// 原生 IIS 证据物化只保留证据命中的行与那一侧边界，并把列重排成连续索引。
    /// Native IIS materialization keeps only the evidenced rows and the evidenced bound side, and compacts columns.
    /// </summary>
    [Fact]
    public void MaterializeNativeIIS_ShouldKeepEvidencedRowsAndBoundSideOnly() {
        using LinearTriadModel primal = TwoRowFixture();
        var evidence = new InfeasibilityEvidence {
            Source = InfeasibilityEvidenceSource.NativeIis,
            ConstraintIds = new HashSet<ConstraintId>([primal.DiagnosticConstraintId(1)]),
            VariableBoundRefs = new HashSet<VariableBoundRef>([
                new VariableBoundRef(new VariableId("model-local-variable:0"), BoundSide.Lower),
            ]),
        };

        LinearIisModel? iis = LinearIisCompute.MaterializeNativeIis(primal, evidence);
        Assert.NotNull(iis);
        using (iis!) {
            Assert.Equal([1], iis.OriginRows);
            Assert.Equal([0], iis.OriginColumns);
            Assert.Equal("primal_iis", iis.Name);
            Assert.True(iis.RelaxedFeasible);
            Assert.Single(iis.Variables);
            // 证据只指出下界，因此上界放开到无穷。 / The evidence names the lower bound only, so the upper bound opens up.
            Assert.Equal(2, iis.Variables[0].LowerBound.ToDouble());
            Assert.True(iis.Variables[0].UpperBound == Pos);
            Assert.Equal(["c1"], iis.Rows.Names);
            Assert.Equal(ConstraintRelation.GreaterEqual, iis.Rows.Signs[0]);
            Assert.Equal(new[] { (0, 1d) }, iis.Rows.Lhs[0].Select(cell => (cell.ColIndex, cell.Coefficient.ToDouble())));
        }
    }

    /// <summary>位置型约束标识同样能命中，未被证据引用的列会被丢弃；非原生 IIS 或空证据不物化。 / Positional constraint identifiers match as well and unevidenced columns drop out; non-native or empty evidence does not materialize.</summary>
    [Fact]
    public void MaterializeNativeIIS_ShouldSupportPositionalIdsAndRejectOtherSources() {
        using LinearTriadModel primal = TwoRowFixture();
        var positional = new InfeasibilityEvidence {
            Source = InfeasibilityEvidenceSource.NativeIis,
            ConstraintIds = new HashSet<ConstraintId>([new ConstraintId("constraint-0")]),
        };

        LinearIisModel? iis = LinearIisCompute.MaterializeNativeIis(primal, positional);
        Assert.NotNull(iis);
        using (iis!) {
            Assert.Equal([0], iis.OriginRows);
            // 第一个行同时含 x0 与 x1，两列都被保留且边界都放开。 / The row touches both columns, so both stay with opened bounds.
            Assert.Equal([0, 1], iis.OriginColumns);
            Assert.True(iis.Variables.All(variable => variable.LowerBound == Neg));
            Assert.True(iis.Variables.All(variable => variable.UpperBound == Pos));
            Assert.Equal(new[] { (0, 1d), (1, 1d) }, iis.Rows.Lhs[0].Select(cell => (cell.ColIndex, cell.Coefficient.ToDouble())));
        }

        Assert.Null(LinearIisCompute.MaterializeNativeIis(
            primal,
            new InfeasibilityEvidence { Source = InfeasibilityEvidenceSource.Farkas }));
        Assert.Null(LinearIisCompute.MaterializeNativeIis(
            primal,
            new InfeasibilityEvidence { Source = InfeasibilityEvidenceSource.NativeIis }));
    }

    /// <summary>编排器给出原生 IIS 证据时，统一入口直接物化子系统而不必运行 legacy 过滤。 / The unified entry materializes the subsystem directly when the orchestrator returns native IIS evidence, without running the legacy filter.</summary>
    [Fact]
    public async Task ComputeIIS_ShouldMaterializeNativeEvidenceFromBackendAnalyzer() {
        using LinearTriadModel primal = TwoRowFixture();
        var native = new StubAnalyzer(
            source: InfeasibilityEvidenceSource.NativeIis,
            evidence: new InfeasibilityEvidence {
                Source = InfeasibilityEvidenceSource.NativeIis,
                ConstraintIds = new HashSet<ConstraintId>([primal.DiagnosticConstraintId(1)]),
            });
        var solver = new ScriptedSolver(
            RelaxationReport) {
            Analyzers = [native],
            Capabilities = new SolverCapabilities(
                ModelTypes: new HashSet<SolverModelType> { SolverModelType.Lp },
                NativeIis: true),
        };

        LinearIisResult outcome = Unwrap(await solver.ComputeIISAsync(
            model: primal,
            config: new IisConfig()));
        using (outcome.Model) {
            Assert.Equal([1], outcome.Model.OriginRows);
            Assert.Equal(InfeasibilityEvidenceSource.NativeIis, outcome.Evidence.Source);
            Assert.Empty(solver.Seen);
        }
    }

    /// <summary>
    /// 后端只有 Farkas 证据时统一入口回退到 legacy 过滤；能力声明不含 Farkas 时分析器在编排前就被过滤掉。
    /// With only Farkas evidence the unified entry falls back to legacy filtering, and an analyzer whose capability is not
    /// declared gets filtered out before orchestration.
    /// </summary>
    [Fact]
    public async Task ComputeIIS_ShouldFallBackToLegacyWhenEvidenceHasNoArtifact() {
        using LinearTriadModel primal = RowKindFixture(equalRow: false);
        var farkas = new StubAnalyzer(
            source: InfeasibilityEvidenceSource.Farkas,
            evidence: new InfeasibilityEvidence {
                Source = InfeasibilityEvidenceSource.Farkas,
                ConstraintIds = new HashSet<ConstraintId>([primal.DiagnosticConstraintId(0)]),
            });
        var solver = new ScriptedSolver(
            RelaxationReport) {
            Analyzers = [farkas],
            Capabilities = new SolverCapabilities(
                ModelTypes: new HashSet<SolverModelType> { SolverModelType.Lp, SolverModelType.Mip },
                Farkas: true),
        };

        LinearIisResult outcome = Unwrap(await solver.ComputeIISAsync(
            model: primal,
            config: new IisConfig()));
        using (outcome.Model) {
            // Farkas 证据无法物化，因此子系统来自 legacy 过滤，证据等级也降为启发式。
            // Farkas evidence cannot materialize, so the subsystem comes from legacy filtering and the grade drops to heuristic.
            Assert.Equal(InfeasibilityEvidenceSource.ElasticFilter, outcome.Evidence.Source);
            Assert.Equal(EvidenceExactness.Heuristic, outcome.Evidence.Exactness);
            Assert.Equal(2, solver.SeenCount());
            Assert.Equal([0, 1], outcome.Model.OriginRows);
            Assert.Equal("legacy-iis-heuristic", outcome.Evidence.UnavailableReason?.Code);
            Assert.Equal(SolveIssueCategory.Unsupported, outcome.Evidence.UnavailableReason?.Category);
        }

        var uncapable = new ScriptedSolver(
            RelaxationReport) {
            Analyzers = [farkas],
            Capabilities = new SolverCapabilities(
                ModelTypes: new HashSet<SolverModelType> { SolverModelType.Lp }),
        };
        IReadOnlyList<IInfeasibilityAnalyzer<ILinearTriadModelView>> available =
            new InfeasibilityDiagnosticOrchestrator<ILinearTriadModelView>(
                analyzers: [.. uncapable.DiagnosticAnalyzers(new IisConfig()), new LegacyElasticInfeasibilityAnalyzer(uncapable)],
                solverCapabilities: uncapable.Capabilities).AvailableAnalyzers();
        Assert.All(available, analyzer => Assert.NotEqual(InfeasibilityEvidenceSource.Farkas, analyzer.Source));
    }

    /// <summary>分析器全部失败时统一入口仍由 legacy 过滤交付子系统。 / The unified entry still delivers the subsystem through legacy filtering when every backend analyzer fails.</summary>
    [Fact]
    public async Task ComputeIIS_ShouldContinueChainWhenBackendAnalyzerFails() {
        using LinearTriadModel primal = RowKindFixture(equalRow: false);
        var solver = new ScriptedSolver(
            RelaxationReport) {
            Analyzers = [new FailingAnalyzer()],
            Capabilities = new SolverCapabilities(
                ModelTypes: new HashSet<SolverModelType> { SolverModelType.Lp },
                NativeIis: true),
        };

        LinearIisResult outcome = Unwrap(await solver.ComputeIISAsync(
            model: primal,
            config: new IisConfig()));
        using (outcome.Model) {
            Assert.Equal(InfeasibilityEvidenceSource.ElasticFilter, outcome.Evidence.Source);
            Assert.Equal([0, 1], outcome.Model.OriginRows);
        }
    }

    /// <summary>证据成员同时列出子系统行与被保留的变量界。 / The evidence members list both subsystem rows and the kept variable bounds.</summary>
    [Fact]
    public async Task ComputeLegacyIIS_ShouldGradeEvidenceAndListMembers() {
        using LinearTriadModel primal = BoundFixture();
        var solver = new ScriptedSolver(RelaxationReport);

        var analyzer = new LegacyElasticInfeasibilityAnalyzer(solver);
        MaterializedInfeasibilityEvidence<LinearIisModel> materialized = Unwrap(
            await analyzer.AnalyzeMaterializedAsync(primal));
        using (materialized.Artifact!) {
            Assert.Same(primal, materialized.Artifact!.Origin);
            Assert.Equal(InfeasibilityEvidenceSource.ElasticFilter, materialized.Evidence.Source);
            Assert.Equal(analyzer.Source, materialized.Evidence.Source);
            Assert.False(analyzer.Capabilities.Exact);
            Assert.Equal(EvidenceCompleteness.Complete, materialized.Evidence.Completeness);
            Assert.Equal(EvidenceValidity.Heuristic, materialized.Evidence.Validity);
            Assert.Equal(EvidenceMinimality.NotChecked, materialized.Evidence.Minimality);
            Assert.Empty(materialized.Evidence.ConstraintIds);
            Assert.Equal(2, materialized.Evidence.VariableBoundRefs.Count);
            Assert.Equal(2, materialized.Evidence.Members.Count);
            Assert.NotNull(materialized.Evidence.Elapsed);
        }
    }

    private static SolveReport<Flt64> RelaxationReport(ILinearTriadModelView model) {
        int[] free = FreeSlackIndices(model);
        return Report(model, free, feasible: free.Length > 0);
    }

    private static int[] FreeSlackIndices(ILinearTriadModelView model) =>
        [.. Enumerable.Range(0, model.Variables.Count)
            .Where(index => model.Variables[index].Slack is not null
                && model.Variables[index].UpperBound == Pos)];

    private static bool IsInequalityRowSlack(ModelViewVariable variable) =>
        variable.Slack?.Constraint is { } constraint
        && (constraint.Relation == ConstraintRelation.LessEqual
            || constraint.Relation == ConstraintRelation.GreaterEqual);

    private static SolveReport<Flt64> Report(
        ILinearTriadModelView model,
        IReadOnlyCollection<int> nonzero,
        bool feasible) {
        if (!feasible) {
            return new SolveReport<Flt64> {
                ProblemStatus = ProblemStatus.Infeasible,
                TerminationReason = TerminationReason.Completed,
                SolutionPresence = SolutionPresence.None,
            };
        }
        var values = new List<Flt64>(model.Variables.Count);
        for (int index = 0; index < model.Variables.Count; index++) {
            values.Add(nonzero.Contains(index) ? Flt64.One : Flt64.Zero);
        }
        return new SolveReport<Flt64> {
            ProblemStatus = ProblemStatus.Feasible,
            TerminationReason = TerminationReason.Completed,
            SolutionPresence = SolutionPresence.Optimal,
            Solution = new SolveSolution<Flt64>(
                Values: new Solution<Flt64>(values),
                Objective: Flt64.One),
        };
    }

    /// <summary>2 列 1 行：自由列 x0、有界列 x1 与一条小于等于行，弹性模型因此有 1 条行松弛与 2 条界松弛。 / Two columns and one row: free x0, bounded x1 and a single less-equal row, so the elastic model has one row slack and two bound slacks.</summary>
    private static LinearTriadModel BoundFixture() => new(
        variables: [
            Column(0, "x0", Neg, Pos),
            Column(1, "x1", new Flt64(5), new Flt64(6)),
        ],
        constraints: new LinearConstraintBatch(
            lhs: new SparseMatrix([
                new SparseVector([new SparseVectorEntry(1, Flt64.One)]),
            ]),
            signs: [ConstraintRelation.LessEqual],
            rhs: [new Flt64(7)],
            names: ["c0"],
            sources: [ConstraintSource.Origin]),
        objective: new Objective<LinearObjectiveCell>(
            category: ObjectCategory.Minimum,
            cells: [new LinearObjectiveCell(0, Flt64.One)]),
        name: "primal");

    /// <summary>自由列 x0 与两条矛盾的行，可选的第 3 条等式行用于检验等式松弛只在第三轮被放开。 / Free x0 with two contradicting rows, plus an optional equality row to check that equality slacks only open in the third round.</summary>
    private static LinearTriadModel RowKindFixture(bool equalRow = true) {
        List<ModelViewVariable> variables = [Column(0, "x0", Neg, Pos)];
        var rows = new List<SparseVector> {
            new([new SparseVectorEntry(0, Flt64.One)]),
            new([new SparseVectorEntry(0, Flt64.One)]),
        };
        var signs = new List<ConstraintRelation> {
            ConstraintRelation.LessEqual,
            ConstraintRelation.GreaterEqual,
        };
        var rhs = new List<Flt64> { new(3), new(5) };
        var names = new List<string> { "c0", "c1" };
        if (equalRow) {
            rows.Add(new SparseVector([new SparseVectorEntry(0, Flt64.One)]));
            signs.Add(ConstraintRelation.Equal);
            rhs.Add(Flt64.Zero);
            names.Add("c2");
        }
        return new LinearTriadModel(
            variables: variables,
            constraints: new LinearConstraintBatch(
                lhs: new SparseMatrix(rows),
                signs: signs,
                rhs: rhs,
                names: names,
                sources: [.. names.Select(_ => ConstraintSource.Origin)]),
            objective: new Objective<LinearObjectiveCell>(
                category: ObjectCategory.Minimum,
                cells: [new LinearObjectiveCell(0, Flt64.One)]),
            name: "primal");
    }

    /// <summary>2 列 2 行，行 0 同时含两列、行 1 只含 x0，便于核对列重排。 / Two columns and two rows where row 0 touches both columns and row 1 only x0, which makes the column compaction easy to check.</summary>
    private static LinearTriadModel TwoRowFixture() => new(
        variables: [
            Column(0, "x0", new Flt64(2), new Flt64(4)),
            Column(1, "x1", Neg, Pos),
        ],
        constraints: new LinearConstraintBatch(
            lhs: new SparseMatrix([
                new SparseVector([
                    new SparseVectorEntry(0, Flt64.One),
                    new SparseVectorEntry(1, Flt64.One),
                ]),
                new SparseVector([new SparseVectorEntry(0, Flt64.One)]),
            ]),
            signs: [ConstraintRelation.LessEqual, ConstraintRelation.GreaterEqual],
            rhs: [new Flt64(5), new Flt64(1)],
            names: ["c0", "c1"],
            sources: [ConstraintSource.Origin, ConstraintSource.Origin]),
        objective: new Objective<LinearObjectiveCell>(
            category: ObjectCategory.Minimum,
            cells: [new LinearObjectiveCell(0, Flt64.One)]),
        name: "primal");

    private static ModelViewVariable Column(int index, string name, Flt64 lower, Flt64 upper) => new(
        index: index,
        lowerBound: lower,
        upperBound: upper,
        type: Continuous.Instance,
        name: name);

    private static T Unwrap<T>(Result<T, ErrorCode, Error<ErrorCode>> result) =>
        Assert.IsType<Ok<T, ErrorCode, Error<ErrorCode>>>(result).Value;

    /// <summary>只实现报告入口的脚本求解器 / Scripted solver that implements only the report entry.</summary>
    private sealed class ScriptedSolver(
        Func<ILinearTriadModelView, SolveReport<Flt64>> script) : IAbstractLinearSolver {
        private readonly Func<ILinearTriadModelView, SolveReport<Flt64>> _script = script;

        internal List<ILinearTriadModelView> Seen { get; } = [];
        internal IReadOnlyList<IInfeasibilityAnalyzer<ILinearTriadModelView>> Analyzers { get; init; } =
            Array.Empty<IInfeasibilityAnalyzer<ILinearTriadModelView>>();
        internal SolverCapabilities? Capabilities { get; init; }

        public string Name => "scripted";

        public SolverDescriptor? Descriptor => Capabilities is null
            ? null
            : new SolverDescriptor(
                SolverId: "scripted",
                BackendName: "Scripted",
                Capabilities: Capabilities);

        public int SeenCount() => Seen.Count;

        public Task<Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>> InvokeReportAsync(
            ILinearTriadModelView model,
            SolvingStatusCallBack? solvingStatusCallBack = null,
            CancellationToken cancellationToken = default) {
            Seen.Add(model);
            return Task.FromResult(Results.Ok(_script(model)));
        }

        public IReadOnlyList<IInfeasibilityAnalyzer<ILinearTriadModelView>> DiagnosticAnalyzers(IisConfig config) =>
            Analyzers;

        public Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> InvokeAsync(
            ILinearTriadModelView model,
            SolvingStatusCallBack? solvingStatusCallBack = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("本测试未使用 / unused by this test");

        public Task<Result<SolverOutput, ErrorCode, Error<ErrorCode>>> InvokeAsync(
            LinearTriadModel model,
            SolvingStatusCallBack? solvingStatusCallBack = null,
            IisConfig? iisConfig = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("本测试未使用 / unused by this test");

        public Task<Result<(FeasibleSolverOutput<Flt64> Output, List<List<Flt64>> Solutions), ErrorCode, Error<ErrorCode>>> InvokeAsync(
            ILinearTriadModelView model,
            ulong solutionAmount,
            SolvingStatusCallBack? solvingStatusCallBack = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("本测试未使用 / unused by this test");

        public Task<Result<LinearTriadModel, ErrorCode, Error<ErrorCode>>> DumpAsync(
            LinearMechanismModel<Flt64> model,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("本测试未使用 / unused by this test");

        public Task<Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>> DumpAsync(
            LinearMetaModel<Flt64> model,
            RegistrationStatusCallBack? registrationStatusCallBack,
            MechanismModelDumpingStatusCallBack? dumpingStatusCallBack,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("本测试未使用 / unused by this test");
    }

    /// <summary>返回固定证据的后端分析器 / Backend analyzer returning fixed evidence.</summary>
    private sealed class StubAnalyzer(
        InfeasibilityEvidenceSource source,
        InfeasibilityEvidence evidence) : ICapabilityAwareInfeasibilityAnalyzer<ILinearTriadModelView> {
        public InfeasibilityEvidenceSource Source { get; } = source;

        public InfeasibilityAnalyzerCapabilities Capabilities { get; } = new(Source: source);

        public Task<Result<InfeasibilityEvidence, ErrorCode, Error<ErrorCode>>> AnalyzeAsync(
            ILinearTriadModelView model,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Results.Ok(evidence));
    }

    /// <summary>总是失败的后端分析器 / Backend analyzer that always fails.</summary>
    private sealed class FailingAnalyzer : IInfeasibilityAnalyzer<ILinearTriadModelView> {
        public InfeasibilityEvidenceSource Source => InfeasibilityEvidenceSource.ConstraintConflict;

        public Task<Result<InfeasibilityEvidence, ErrorCode, Error<ErrorCode>>> AnalyzeAsync(
            ILinearTriadModelView model,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Results.Failed<InfeasibilityEvidence>(new Err<ErrorCode>(
                ErrorCode.OREngineSolvingException,
                "桩分析器失败 / stub analyzer failure")));
    }

    /// <summary>不是线性三元模型的视图，用于验证入口拒绝。 / A view that is not a linear triad model, used to check the entry rejects it.</summary>
    private sealed class ViewOnlyModel : ILinearTriadModelView {
        public IReadOnlyList<ModelViewVariable> Variables => Array.Empty<ModelViewVariable>();
        public ModelConstraint<LinearConstraintCell> Constraints => throw new NotSupportedException("本测试未使用 / unused by this test");
        public Objective<LinearObjectiveCell> Objective => throw new NotSupportedException("本测试未使用 / unused by this test");
        public string Name => "view-only";
        public Try ExportLP(StreamWriter? writer) => Results.OkInstance;
        public void Dispose() {
        }
    }
}
