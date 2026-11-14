#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.ConstraintProgramming;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Framework.Solver;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Operation;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Int64 = Fuookami.Ospf.Math.Algebra.Number.Int64;

namespace Fuookami.Ospf.Framework.Tests.Solver;

public class LogicBasedBendersTest {
    [Fact]
    public async Task BinaryMaster_ShouldConvergeAfterConflictCut() {
        using var master = new LinearMetaModel<Flt64>(name: "binary-master");
        var masterVariable = new BinVar("master-x");
        master.Add(masterVariable).IsOk.Should().BeTrue();
        using ConstraintProgrammingModel subproblem = ConstrainedSubproblem(
            out BinVar cpVariable,
            "cp-x");
        BinaryBendersVariableBinding binding = BinaryBinding(masterVariable, cpVariable);
        int masterCalls = 0;
        BendersMasterProblemSolver masterSolver = model => {
            ++masterCalls;
            Flt64 value = model.RelationConstraints.Count == 0
                ? Flt64.Zero
                : Flt64.One;
            return MasterResult(Output(value));
        };
        var engine = new LogicBasedBendersEngine(
            masterSolver: masterSolver,
            subproblemSolver: new FakeConstraintProgrammingSolver(),
            binding: binding);

        Result<LogicBasedBendersReport, ErrorCode, Error<ErrorCode>> result =
            await engine.SolveAsync(master, subproblem);

        LogicBasedBendersReport report = ReportOf(result);
        masterCalls.Should().Be(2);
        report.ProblemStatus.Should().Be(ProblemStatus.Feasible);
        report.TerminationReason.Should().Be(TerminationReason.Completed);
        report.Proof.Status.Should().Be(ProofStatus.Verified);
        report.Iterations.Should().HaveCount(2);
        report.Cuts.Should().ContainSingle();
        report.Cuts[0].Kind.Should().Be(BendersCutKind.Conflict);
        report.MasterOutput!.Solution!.Values[0].Should().Be(Flt64.One);
        master.RelationConstraints.Should().ContainSingle();
    }

    [Fact]
    public async Task SupplementaryMasterVariables_ShouldUseTokenOrderNotVariableIndex() {
        var secondVariable = new BinVar("created-first");
        var firstVariable = new BinVar("created-second");
        using var master = new LinearMetaModel<Flt64>(name: "token-order");
        master.Add(firstVariable).IsOk.Should().BeTrue();
        master.Add(secondVariable).IsOk.Should().BeTrue();
        using ConstraintProgrammingModel subproblem = ConstrainedSubproblem(
            out BinVar cpVariable,
            "token-order-cp");
        var binding = new BinaryBendersVariableBinding(new[] {
            new BinaryBendersVariable(
                Key: StableKey(secondVariable),
                MasterVariable: secondVariable,
                SubproblemVariable: cpVariable),
        });
        int masterCalls = 0;
        BendersMasterProblemSolver masterSolver = model => {
            ++masterCalls;
            IReadOnlyList<Flt64> values = model.Tokens.TokensInSolver
                .Select(token => ReferenceEquals(token.Variable, secondVariable)
                    ? Flt64.One
                    : Flt64.Zero)
                .ToList();
            return MasterResult(Output(values));
        };
        var engine = new LogicBasedBendersEngine(
            masterSolver: masterSolver,
            subproblemSolver: new FakeConstraintProgrammingSolver(),
            binding: binding,
            options: new LogicBasedBendersOptions {
                MasterVariables = new[] { secondVariable },
            });

        LogicBasedBendersReport report = ReportOf(
            await engine.SolveAsync(master, subproblem));

        masterCalls.Should().Be(1);
        report.Proof.Status.Should().Be(ProofStatus.Verified);
        report.Iterations.Should().ContainSingle();
    }

    [Fact]
    public async Task ExactMode_ShouldRejectNonoptimalMaster() {
        using var master = new LinearMetaModel<Flt64>(name: "limited-master");
        var masterVariable = new BinVar("limited-x");
        master.Add(masterVariable).IsOk.Should().BeTrue();
        using ConstraintProgrammingModel subproblem = ConstrainedSubproblem(
            out BinVar cpVariable,
            "limited-cp-x");
        var engine = new LogicBasedBendersEngine(
            masterSolver: _ => MasterResult(Output(
                Flt64.Zero,
                terminationReason: TerminationReason.TimeLimit,
                solutionPresence: SolutionPresence.Incumbent,
                proofStatus: ProofStatus.None)),
            subproblemSolver: new FakeConstraintProgrammingSolver(),
            binding: BinaryBinding(masterVariable, cpVariable));

        LogicBasedBendersReport report = ReportOf(
            await engine.SolveAsync(master, subproblem));

        report.ProblemStatus.Should().Be(ProblemStatus.Unknown);
        report.TerminationReason.Should().Be(TerminationReason.BackendFailure);
        report.Proof.Status.Should().Be(ProofStatus.None);
        report.Diagnostics.Should().ContainSingle(issue =>
            issue.Code == "benders-master-not-proven-optimal");
    }

    [Fact]
    public async Task ExactMode_ShouldRejectUnverifiedSubproblem() {
        using var master = new LinearMetaModel<Flt64>(name: "unverified-master");
        var masterVariable = new BinVar("unverified-x");
        master.Add(masterVariable).IsOk.Should().BeTrue();
        using ConstraintProgrammingModel subproblem = ConstrainedSubproblem(
            out BinVar cpVariable,
            "unverified-cp-x");
        var engine = new LogicBasedBendersEngine(
            masterSolver: _ => MasterResult(Output(Flt64.One)),
            subproblemSolver: new TransformingConstraintProgrammingSolver(
                OutputTransform.MarkUnverified),
            binding: BinaryBinding(masterVariable, cpVariable));

        LogicBasedBendersReport report = ReportOf(
            await engine.SolveAsync(master, subproblem));

        report.ProblemStatus.Should().Be(ProblemStatus.Unknown);
        report.Proof.Status.Should().Be(ProofStatus.None);
        report.Diagnostics.Should().ContainSingle(issue =>
            issue.Code == "benders-subproblem-not-proven");
    }

    [Theory]
    [InlineData(BendersCutValidity.Assignment)]
    [InlineData(BendersCutValidity.Heuristic)]
    public async Task ExactMode_ShouldRejectNonGlobalCuts(BendersCutValidity validity) {
        using var master = new LinearMetaModel<Flt64>(name: "invalid-cut-master");
        var masterVariable = new BinVar("invalid-cut-x");
        master.Add(masterVariable).IsOk.Should().BeTrue();
        using ConstraintProgrammingModel subproblem = ConstrainedSubproblem(
            out BinVar cpVariable,
            "invalid-cut-cp-x");
        var oracle = new ScriptedCutOracle(
            emitOptimality: iteration => iteration == 0,
            kind: BendersCutKind.Heuristic,
            validity: validity);
        var engine = new LogicBasedBendersEngine(
            masterSolver: _ => MasterResult(Output(Flt64.One)),
            subproblemSolver: new FakeConstraintProgrammingSolver(),
            binding: BinaryBinding(masterVariable, cpVariable),
            cutOracle: oracle);

        Result<LogicBasedBendersReport, ErrorCode, Error<ErrorCode>> result =
            await engine.SolveAsync(master, subproblem);

        result.Should()
            .BeOfType<Failed<LogicBasedBendersReport, ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.IllegalArgument);
    }

    [Fact]
    public async Task OptimizationSubproblem_ShouldNotClaimWithoutOptimalityCut() {
        using var master = new LinearMetaModel<Flt64>(name: "no-optimality-master");
        var masterVariable = new BinVar("no-optimality-x");
        master.Add(masterVariable).IsOk.Should().BeTrue();
        using ConstraintProgrammingModel subproblem = ConstrainedSubproblem(
            out BinVar cpVariable,
            "no-optimality-cp-x",
            optimize: true);
        var engine = new LogicBasedBendersEngine(
            masterSolver: _ => MasterResult(Output(
                Flt64.One,
                objective: Flt64.One,
                bestBound: Flt64.One)),
            subproblemSolver: new FakeConstraintProgrammingSolver(),
            binding: BinaryBinding(masterVariable, cpVariable));

        LogicBasedBendersReport report = ReportOf(
            await engine.SolveAsync(master, subproblem));

        report.ProblemStatus.Should().Be(ProblemStatus.Feasible);
        report.TerminationReason.Should().Be(TerminationReason.IterationLimit);
        report.Proof.Status.Should().Be(ProofStatus.None);
        report.Diagnostics.Should().ContainSingle(issue =>
            issue.Code == "benders-no-optimality-proof");
    }

    [Fact]
    public async Task OptimizationSubproblem_ShouldRejectMissingObjective() {
        using var master = new LinearMetaModel<Flt64>(name: "missing-objective-master");
        var masterVariable = new BinVar("missing-objective-x");
        master.Add(masterVariable).IsOk.Should().BeTrue();
        using ConstraintProgrammingModel subproblem = ConstrainedSubproblem(
            out BinVar cpVariable,
            "missing-objective-cp-x",
            optimize: true);
        var engine = new LogicBasedBendersEngine(
            masterSolver: _ => MasterResult(Output(
                Flt64.One,
                objective: Flt64.One,
                bestBound: Flt64.One)),
            subproblemSolver: new TransformingConstraintProgrammingSolver(
                OutputTransform.StripObjective),
            binding: BinaryBinding(masterVariable, cpVariable));

        LogicBasedBendersReport report = ReportOf(
            await engine.SolveAsync(master, subproblem));

        report.ProblemStatus.Should().Be(ProblemStatus.Unknown);
        report.Diagnostics.Should().ContainSingle(issue =>
            issue.Code == "benders-subproblem-objective-missing");
    }

    [Fact]
    public async Task OptimizationSubproblem_ShouldRequireCompleteObjectiveContract() {
        using var master = new LinearMetaModel<Flt64>(name: "objective-contract-master");
        var masterVariable = new BinVar("objective-contract-x");
        master.Add(masterVariable).IsOk.Should().BeTrue();
        using ConstraintProgrammingModel subproblem = ConstrainedSubproblem(
            out BinVar cpVariable,
            "objective-contract-cp-x",
            optimize: true);
        var engine = new LogicBasedBendersEngine(
            masterSolver: _ => MasterResult(Output(
                Flt64.One,
                objective: Flt64.Zero,
                bestBound: Flt64.Zero)),
            subproblemSolver: new FakeConstraintProgrammingSolver(),
            binding: BinaryBinding(masterVariable, cpVariable),
            cutOracle: new ScriptedCutOracle(
                emitOptimality: iteration => iteration == 0,
                kind: BendersCutKind.Optimality,
                validity: BendersCutValidity.Global));

        LogicBasedBendersReport report = ReportOf(
            await engine.SolveAsync(master, subproblem));

        report.ProblemStatus.Should().Be(ProblemStatus.Feasible);
        report.Proof.Status.Should().Be(ProofStatus.None);
        report.Diagnostics.Should().ContainSingle(issue =>
            issue.Code == "benders-objective-contract-missing");
    }

    [Fact]
    public async Task OptimizationSubproblem_ShouldReportObjectiveMismatch() {
        using var master = new LinearMetaModel<Flt64>(name: "objective-mismatch-master");
        var masterVariable = new BinVar("objective-mismatch-x");
        master.Add(masterVariable).IsOk.Should().BeTrue();
        using ConstraintProgrammingModel subproblem = ConstrainedSubproblem(
            out BinVar cpVariable,
            "objective-mismatch-cp-x",
            optimize: true);
        var engine = new LogicBasedBendersEngine(
            masterSolver: _ => MasterResult(Output(
                Flt64.One,
                objective: Flt64.Zero,
                bestBound: Flt64.Zero)),
            subproblemSolver: new FakeConstraintProgrammingSolver(),
            binding: BinaryBinding(masterVariable, cpVariable),
            cutOracle: new ScriptedCutOracle(
                emitOptimality: iteration => iteration == 0,
                kind: BendersCutKind.Optimality,
                validity: BendersCutValidity.Global),
            options: new LogicBasedBendersOptions {
                CompleteObjectiveEvaluator = (_, _, _) => Results.Ok(new Flt64(2)),
            });

        LogicBasedBendersReport report = ReportOf(
            await engine.SolveAsync(master, subproblem));

        report.ProblemStatus.Should().Be(ProblemStatus.Feasible);
        report.Proof.Status.Should().Be(ProofStatus.None);
        report.Diagnostics.Should().ContainSingle(issue =>
            issue.Code == "benders-objective-inconsistent");
    }

    [Fact]
    public async Task OptimizationSubproblem_ShouldRequireConvergenceProof() {
        using var master = new LinearMetaModel<Flt64>(name: "convergence-master");
        var masterVariable = new BinVar("convergence-x");
        master.Add(masterVariable).IsOk.Should().BeTrue();
        using ConstraintProgrammingModel subproblem = ConstrainedSubproblem(
            out BinVar cpVariable,
            "convergence-cp-x",
            optimize: true);
        var engine = new LogicBasedBendersEngine(
            masterSolver: _ => MasterResult(Output(
                Flt64.One,
                objective: Flt64.One,
                bestBound: Flt64.Zero)),
            subproblemSolver: new FakeConstraintProgrammingSolver(),
            binding: BinaryBinding(masterVariable, cpVariable),
            cutOracle: new ScriptedCutOracle(
                emitOptimality: iteration => iteration == 0,
                kind: BendersCutKind.Optimality,
                validity: BendersCutValidity.Global),
            options: new LogicBasedBendersOptions {
                CompleteObjectiveEvaluator = (_, _, _) => Results.Ok(Flt64.One),
            });

        LogicBasedBendersReport report = ReportOf(
            await engine.SolveAsync(master, subproblem));

        report.ProblemStatus.Should().Be(ProblemStatus.Feasible);
        report.Proof.Status.Should().Be(ProofStatus.None);
        report.Iterations[^1].ConvergenceGap.Should().Be(Flt64.One);
        report.Diagnostics.Should().ContainSingle(issue =>
            issue.Code == "benders-no-convergence-proof");
    }

    [Fact]
    public async Task OptimizationSubproblem_ShouldVerifyWithCompleteEvidence() {
        using var master = new LinearMetaModel<Flt64>(name: "verified-master");
        var masterVariable = new BinVar("verified-x");
        master.Add(masterVariable).IsOk.Should().BeTrue();
        using ConstraintProgrammingModel subproblem = ConstrainedSubproblem(
            out BinVar cpVariable,
            "verified-cp-x",
            optimize: true);
        var engine = new LogicBasedBendersEngine(
            masterSolver: _ => MasterResult(Output(
                Flt64.One,
                objective: new Flt64(2),
                bestBound: new Flt64(2))),
            subproblemSolver: new FakeConstraintProgrammingSolver(),
            binding: BinaryBinding(masterVariable, cpVariable),
            cutOracle: new ScriptedCutOracle(
                emitOptimality: iteration => iteration == 0,
                kind: BendersCutKind.Optimality,
                validity: BendersCutValidity.Global),
            options: new LogicBasedBendersOptions {
                CompleteObjectiveEvaluator = (_, _, _) => Results.Ok(new Flt64(2)),
            });

        LogicBasedBendersReport report = ReportOf(
            await engine.SolveAsync(master, subproblem));

        report.ProblemStatus.Should().Be(ProblemStatus.Feasible);
        report.TerminationReason.Should().Be(TerminationReason.Completed);
        report.Proof.Status.Should().Be(ProofStatus.Verified);
        report.Cuts.Should().ContainSingle();
    }

    [Fact]
    public async Task UnknownSubproblem_ShouldNotBeReportedAsFeasible() {
        using var master = new LinearMetaModel<Flt64>(name: "unknown-master");
        var masterVariable = new BinVar("unknown-x");
        master.Add(masterVariable).IsOk.Should().BeTrue();
        using ConstraintProgrammingModel subproblem = ConstrainedSubproblem(
            out BinVar cpVariable,
            "unknown-cp-x");
        subproblem.RegisterVariable(
            new IntVar("unknown-free"),
            IntegerDomain.CreateInterval(
                lowerBound: Int64.Zero,
                upperBound: new Int64(2)).Value).IsOk.Should().BeTrue();
        var engine = new LogicBasedBendersEngine(
            masterSolver: _ => MasterResult(Output(Flt64.Zero)),
            subproblemSolver: new FakeConstraintProgrammingSolver(enumerationLimit: 1),
            binding: BinaryBinding(masterVariable, cpVariable));

        LogicBasedBendersReport report = ReportOf(
            await engine.SolveAsync(master, subproblem));

        report.ProblemStatus.Should().Be(ProblemStatus.Unknown);
        report.Proof.Status.Should().Be(ProofStatus.None);
        report.Diagnostics.Select(issue => issue.Code).Should().Equal(
            "benders-subproblem-unknown");
    }

    [Fact]
    public async Task CancelledSolve_ShouldStopBeforeMasterInvocation() {
        using var master = new LinearMetaModel<Flt64>(name: "cancelled-master");
        var masterVariable = new BinVar("cancelled-x");
        master.Add(masterVariable).IsOk.Should().BeTrue();
        using ConstraintProgrammingModel subproblem = ConstrainedSubproblem(
            out BinVar cpVariable,
            "cancelled-cp-x");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        int masterCalls = 0;
        var engine = new LogicBasedBendersEngine(
            masterSolver: _ => {
                ++masterCalls;
                return MasterResult(Output(Flt64.One));
            },
            subproblemSolver: new FakeConstraintProgrammingSolver(),
            binding: BinaryBinding(masterVariable, cpVariable),
            options: new LogicBasedBendersOptions {
                CancellationToken = cancellation.Token,
            });

        LogicBasedBendersReport report = ReportOf(
            await engine.SolveAsync(master, subproblem));

        masterCalls.Should().Be(0);
        report.ProblemStatus.Should().Be(ProblemStatus.Unknown);
        report.TerminationReason.Should().Be(TerminationReason.Cancelled);
        report.Diagnostics.Should().ContainSingle(issue =>
            issue.Code == "benders-cancelled");
    }

    [Fact]
    public async Task ClosedModels_ShouldFailBeforeSolving() {
        var engine = new LogicBasedBendersEngine(
            masterSolver: _ => MasterResult(Output(Flt64.One)),
            subproblemSolver: new FakeConstraintProgrammingSolver(),
            binding: new BinaryBendersVariableBinding(Array.Empty<BinaryBendersVariable>()));
        using var closedMaster = new LinearMetaModel<Flt64>(name: "closed-master");
        using var openSubproblem = new ConstraintProgrammingModel(name: "open-subproblem");
        closedMaster.Close();

        Result<LogicBasedBendersReport, ErrorCode, Error<ErrorCode>> masterResult =
            await engine.SolveAsync(closedMaster, openSubproblem);

        masterResult.Should()
            .BeOfType<Failed<LogicBasedBendersReport, ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.ApplicationStopped);

        using var openMaster = new LinearMetaModel<Flt64>(name: "open-master");
        using var closedSubproblem = new ConstraintProgrammingModel(name: "closed-subproblem");
        closedSubproblem.Close();
        Result<LogicBasedBendersReport, ErrorCode, Error<ErrorCode>> subproblemResult =
            await engine.SolveAsync(openMaster, closedSubproblem);

        subproblemResult.Should()
            .BeOfType<Failed<LogicBasedBendersReport, ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.ApplicationStopped);
    }

    [Fact]
    public async Task DuplicateCuts_ShouldBeInstalledOnlyOnce() {
        using var master = new LinearMetaModel<Flt64>(name: "duplicate-master");
        var masterVariable = new BinVar("duplicate-x");
        master.Add(masterVariable).IsOk.Should().BeTrue();
        using ConstraintProgrammingModel subproblem = ConstrainedSubproblem(
            out BinVar cpVariable,
            "duplicate-cp-x");
        int masterCalls = 0;
        var engine = new LogicBasedBendersEngine(
            masterSolver: _ => {
                ++masterCalls;
                return MasterResult(Output(Flt64.Zero));
            },
            subproblemSolver: new FakeConstraintProgrammingSolver(),
            binding: BinaryBinding(masterVariable, cpVariable),
            options: new LogicBasedBendersOptions {
                MaxIterations = 3,
                StallIterationLimit = 1,
            });

        LogicBasedBendersReport report = ReportOf(
            await engine.SolveAsync(master, subproblem));

        masterCalls.Should().Be(2);
        report.ProblemStatus.Should().Be(ProblemStatus.Unknown);
        report.Cuts.Should().ContainSingle();
        report.Iterations.Should().ContainSingle();
        report.Diagnostics.Should().ContainSingle(issue =>
            issue.Code == "benders-no-feasibility-cut");
    }

    [Fact]
    public void AddCuts_ShouldRollbackConstraintsWhenInstallationFails() {
        using var master = new LinearMetaModel<Flt64>(name: "atomic-cut-master");
        var masterVariable = new BinVar("atomic-cut-x");
        master.Add(masterVariable).IsOk.Should().BeTrue();
        BendersMasterCut[] cuts = {
            ConstantCut("atomic-cut-1"),
            ConstantCut("atomic-cut-2"),
        };
        var allCuts = new List<BendersMasterCut>();
        var knownCuts = new HashSet<string>(StringComparer.Ordinal);
        var engine = new LogicBasedBendersEngine(
            masterSolver: _ => MasterResult(Output(Flt64.One)),
            subproblemSolver: new FakeConstraintProgrammingSolver(),
            binding: BinaryBinding(masterVariable, new BinVar("atomic-cut-cp")));
        int installations = 0;

        Result<int, ErrorCode, Error<ErrorCode>> result = engine.AddCuts(
            master: master,
            cuts: cuts,
            allCuts: allCuts,
            knownCuts: knownCuts,
            addConstraint: (inequality, name) => {
                ++installations;
                return installations == 1
                    ? master.AddConstraint(
                        relation: inequality,
                        group: null,
                        name: name,
                        displayName: name)
                    : Results.Failed<Success>(new Err<ErrorCode>(
                        ErrorCode.Other,
                        "forced cut installation failure / 强制割安装失败"));
            });

        result.Should()
            .BeOfType<Failed<int, ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.Other);
        installations.Should().Be(2);
        master.RelationConstraints.Should().BeEmpty();
        master.Tokens.TokensInSolver.Should().ContainSingle()
            .Which.Variable.Should().BeSameAs(masterVariable);
        allCuts.Should().BeEmpty();
        knownCuts.Should().BeEmpty();
    }

    [Fact]
    public async Task VerifiedConflictCore_ShouldProjectOnlyCoreVariables() {
        var masterX = new BinVar("core-x");
        var masterY = new BinVar("core-y");
        var cpX = new BinVar("core-cp-x");
        var cpY = new BinVar("core-cp-y");
        using var subproblem = new ConstraintProgrammingModel(name: "core-subproblem");
        subproblem.RegisterVariable(cpX).IsOk.Should().BeTrue();
        subproblem.RegisterVariable(cpY).IsOk.Should().BeTrue();
        var binding = new BinaryBendersVariableBinding(new[] {
            new BinaryBendersVariable("x", masterX, cpX),
            new BinaryBendersVariable("y", masterY, cpY),
        });
        BendersSubproblemAssignment assignment = binding.Bind(
            new MapConstraintProgrammingValueSource(new Dictionary<string, Flt64> {
                ["x"] = Flt64.Zero,
                ["y"] = Flt64.One,
            }),
            subproblem).Value;
        var conflict = new ConstraintProgrammingConflict(
            assumptions: new[] { assignment.Assumptions[0] },
            validity: EvidenceValidity.Verified);
        using var master = new LinearMetaModel<Flt64>(name: "core-master");
        master.Add(masterX).IsOk.Should().BeTrue();
        master.Add(masterY).IsOk.Should().BeTrue();
        var context = new BendersCutContext(
            master,
            subproblem,
            assignment,
            Iteration: 0,
            ProofMode: BendersProofMode.Exact);

        BendersMasterCut cut = new BinaryNoGoodCutOracle()
            .FeasibilityCuts(
                new InfeasibleSubproblemResult(assignment, conflict),
                context)
            .Value
            .Single();

        cut.Kind.Should().Be(BendersCutKind.Conflict);
        cut.Inequality.Lhs.Monomials.Should().ContainSingle()
            .Which.Symbol.Should().BeSameAs(masterX);
    }

    [Fact]
    public void UnverifiedConflictCore_ShouldFallBackToFullNoGood() {
        var masterX = new BinVar("unverified-core-x");
        var masterY = new BinVar("unverified-core-y");
        var cpX = new BinVar("unverified-core-cp-x");
        var cpY = new BinVar("unverified-core-cp-y");
        using var subproblem = new ConstraintProgrammingModel(name: "unverified-core-subproblem");
        subproblem.RegisterVariable(cpX).IsOk.Should().BeTrue();
        subproblem.RegisterVariable(cpY).IsOk.Should().BeTrue();
        var binding = new BinaryBendersVariableBinding(new[] {
            new BinaryBendersVariable("x", masterX, cpX),
            new BinaryBendersVariable("y", masterY, cpY),
        });
        BendersSubproblemAssignment assignment = binding.Bind(
            new MapConstraintProgrammingValueSource(new Dictionary<string, Flt64> {
                ["x"] = Flt64.Zero,
                ["y"] = Flt64.One,
            }),
            subproblem).Value;
        var conflict = new ConstraintProgrammingConflict(
            assumptions: new[] { assignment.Assumptions[0] },
            validity: EvidenceValidity.Unknown);
        using var master = new LinearMetaModel<Flt64>(name: "unverified-core-master");
        master.Add(masterX).IsOk.Should().BeTrue();
        master.Add(masterY).IsOk.Should().BeTrue();
        var context = new BendersCutContext(
            master,
            subproblem,
            assignment,
            Iteration: 0,
            ProofMode: BendersProofMode.Exact);

        BendersMasterCut cut = new BinaryNoGoodCutOracle()
            .FeasibilityCuts(
                new InfeasibleSubproblemResult(assignment, conflict),
                context)
            .Value
            .Single();

        cut.Kind.Should().Be(BendersCutKind.NoGood);
        cut.Inequality.Lhs.Monomials.Should().HaveCount(2);
    }

    [Fact]
    public async Task IntegerMaster_ShouldInstallMultiConstraintNoGood() {
        using var master = new LinearMetaModel<Flt64>(name: "integer-master");
        var masterVariable = new IntVar("integer-x");
        master.Add(masterVariable).IsOk.Should().BeTrue();
        IntegerDomain domain = IntegerDomain.CreateInterval(
            lowerBound: Int64.Zero,
            upperBound: Int64.One).Value;
        var subproblemVariable = new IntVar("integer-cp-x");
        using var subproblem = new ConstraintProgrammingModel(name: "integer-subproblem");
        subproblem.RegisterVariable(subproblemVariable, domain).IsOk.Should().BeTrue();
        ConstraintProgrammingExpression expression =
            ConstraintProgrammingExpression.CreateVariable(subproblemVariable).Value;
        subproblem.AddConstraint(
            ConstraintProgrammingConstraint.CreateEqual(expression, Int64.One).Value)
            .IsOk.Should().BeTrue();
        var binding = new IntegerBendersVariableBinding(new[] {
            new IntegerBendersVariable(
                Key: StableKey(masterVariable),
                MasterVariable: masterVariable,
                SubproblemVariable: subproblemVariable,
                Domain: domain),
        });
        var oracle = new IntegerNoGoodCutOracle(new[] {
            new IntegerBendersVariable(
                Key: StableKey(masterVariable),
                MasterVariable: masterVariable,
                SubproblemVariable: subproblemVariable,
                Domain: domain),
        });
        var engine = new LogicBasedBendersEngine(
            masterSolver: model => {
                Flt64 desired = model.RelationConstraints.Count == 0
                    ? Flt64.Zero
                    : Flt64.One;
                IReadOnlyList<Flt64> values = model.Tokens.TokensInSolver
                    .Select(token => ReferenceEquals(token.Variable, masterVariable)
                        ? desired
                        : Flt64.Zero)
                    .ToList();
                return MasterResult(Output(values));
            },
            subproblemSolver: new FakeConstraintProgrammingSolver(),
            binding: binding,
            cutOracle: oracle);

        LogicBasedBendersReport report = ReportOf(
            await engine.SolveAsync(master, subproblem));

        report.ProblemStatus.Should().Be(ProblemStatus.Feasible);
        report.Proof.Status.Should().Be(ProofStatus.Verified);
        report.Cuts.Should().ContainSingle();
        report.Cuts[0].AuxiliaryVariables.Should().NotBeEmpty();
        report.Cuts[0].AdditionalInequalities.Should().NotBeEmpty();
        report.MasterOutput!.Solution!.Values[0].Should().Be(Flt64.One);
    }

    [Fact]
    public void IntegerNoGood_ShouldExcludeOnlyRequestedInteriorAndBoundaryValues() {
        foreach (long excluded in new long[] { 0, 2, 3 }) {
            var variable = new IntVar($"integer-boundary-{excluded}");
            IntegerNoGoodEncoding encoding = IntegerNoGoodCutEncoder.Encode(new[] {
                new IntegerNoGoodVariable(
                    Key: "x",
                    Variable: variable,
                    Value: new Int64(excluded),
                    Domain: IntegerDomain.CreateInterval(
                        lowerBound: Int64.Zero,
                        upperBound: new Int64(3)).Value),
            }).Value;

            HashSet<long> feasible = FeasibleSingleVariableValues(
                variable,
                encoding,
                lower: 0,
                upper: 3);

            feasible.Should().BeEquivalentTo(
                Enumerable.Range(0, 4).Select(value => (long)value)
                    .Where(value => value != excluded));
        }
    }

    [Fact]
    public void IntegerNoGood_ShouldExcludeOnlyRequestedMultiVariableAssignment() {
        var x = new IntVar("integer-multi-x");
        var y = new IntVar("integer-multi-y");
        IntegerDomain domain = IntegerDomain.CreateInterval(
            lowerBound: Int64.Zero,
            upperBound: new Int64(2)).Value;
        IntegerNoGoodEncoding encoding = IntegerNoGoodCutEncoder.Encode(new[] {
            new IntegerNoGoodVariable("x", x, Int64.One, domain),
            new IntegerNoGoodVariable("y", y, new Int64(2), domain),
        }).Value;
        var feasible = new HashSet<(long X, long Y)>();
        for (long xValue = 0; xValue <= 2; xValue++) {
            for (long yValue = 0; yValue <= 2; yValue++) {
                var values = new Dictionary<ISymbol, Flt64> {
                    [x] = new Flt64(xValue),
                    [y] = new Flt64(yValue),
                };
                if (HasFeasibleAuxiliaryAssignment(encoding, values)) {
                    feasible.Add((xValue, yValue));
                }
            }
        }

        feasible.Should().BeEquivalentTo(
            from xValue in Enumerable.Range(0, 3)
            from yValue in Enumerable.Range(0, 3)
            where (xValue, yValue) != (1, 2)
            select ((long)xValue, (long)yValue));
    }

    [Fact]
    public void IntegerNoGood_ShouldRejectBigMOutsideExactFlt64Range() {
        var variable = new IntVar("integer-large-domain");
        Result<IntegerNoGoodEncoding, ErrorCode, Error<ErrorCode>> result =
            IntegerNoGoodCutEncoder.Encode(new[] {
                new IntegerNoGoodVariable(
                    Key: "x",
                    Variable: variable,
                    Value: Int64.Zero,
                    Domain: IntegerDomain.CreateInterval(
                        lowerBound: Int64.Zero,
                        upperBound: new Int64(long.MaxValue)).Value),
            });

        result.Should()
            .BeOfType<Failed<IntegerNoGoodEncoding, ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.Other);
    }

    [Fact]
    public void IntegerNoGoodInstall_ShouldRejectDuplicateAuxiliaryKeysBeforeMutation() {
        var auxiliary = new BinVar("duplicate-auxiliary");
        var encoding = new IntegerNoGoodEncoding(
            name: "duplicate-encoding",
            auxiliaryVariables: new[] { auxiliary, auxiliary },
            constraints: Array.Empty<LinearInequality<Flt64>>());
        using var master = new LinearMetaModel<Flt64>(name: "duplicate-install-master");

        Result<Success, ErrorCode, Error<ErrorCode>> result = encoding.Install(master);

        result.Should()
            .BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.IllegalArgument);
        master.Tokens.TokensInSolver.Should().BeEmpty();
    }

    private static ConstraintProgrammingModel ConstrainedSubproblem(
        out BinVar variable,
        string name,
        bool optimize = false) {
        variable = new BinVar(name);
        var model = new ConstraintProgrammingModel(
            name: $"subproblem-{variable.Name}",
            objectCategory: ObjectCategory.Minimum);
        model.RegisterVariable(variable).IsOk.Should().BeTrue();
        ConstraintProgrammingExpression expression =
            ConstraintProgrammingExpression.CreateVariable(variable).Value;
        model.AddConstraint(
            ConstraintProgrammingConstraint.CreateEqual(expression, Int64.One).Value)
            .IsOk.Should().BeTrue();
        if (optimize) {
            model.Minimize(expression).IsOk.Should().BeTrue();
        }
        return model;
    }

    private static BinaryBendersVariableBinding BinaryBinding(
        IVariableItem masterVariable,
        BinVar subproblemVariable) {
        return new BinaryBendersVariableBinding(new[] {
            new BinaryBendersVariable(
                Key: StableKey(masterVariable),
                MasterVariable: masterVariable,
                SubproblemVariable: subproblemVariable),
        });
    }

    private static string StableKey(IVariableItem variable) =>
        $"{variable.Identifier}:{variable.Index}";

    private static Task<Result<
        SolveReport<Flt64>,
        ErrorCode,
        Error<ErrorCode>>> MasterResult(SolveReport<Flt64> report) =>
        Task.FromResult(Results.Ok(report));

    private static BendersMasterCut ConstantCut(string source) =>
        new(
            inequality: new LinearPolynomial<Flt64>(
                Array.Empty<LinearMonomial<Flt64>>(),
                Flt64.Zero).Le(Flt64.One),
            kind: BendersCutKind.NoGood,
            validity: BendersCutValidity.Global,
            proofStatus: ProofStatus.Verified,
            source: source);

    private static SolveReport<Flt64> Output(
        Flt64 value,
        Flt64 objective = default,
        Flt64? bestBound = null,
        TerminationReason terminationReason = TerminationReason.Completed,
        SolutionPresence solutionPresence = SolutionPresence.Optimal,
        ProofStatus proofStatus = ProofStatus.Verified) =>
        Output(
            values: new[] { value },
            objective: objective,
            bestBound: bestBound,
            terminationReason: terminationReason,
            solutionPresence: solutionPresence,
            proofStatus: proofStatus);

    private static SolveReport<Flt64> Output(
        IReadOnlyList<Flt64> values,
        Flt64 objective = default,
        Flt64? bestBound = null,
        TerminationReason terminationReason = TerminationReason.Completed,
        SolutionPresence solutionPresence = SolutionPresence.Optimal,
        ProofStatus proofStatus = ProofStatus.Verified) {
        return new SolveReport<Flt64> {
            ProblemStatus = ProblemStatus.Feasible,
            TerminationReason = terminationReason,
            SolutionPresence = solutionPresence,
            Solution = new SolveSolution<Flt64>(
                Values: new Solution<Flt64>(values),
                Objective: objective),
            Proof = new SolveProof(proofStatus, "test-master"),
            Statistics = new SolveStatistics(
                SolveTime: TimeSpan.Zero,
                BestBound: bestBound ?? objective),
        };
    }

    private static LogicBasedBendersReport ReportOf(
        Result<LogicBasedBendersReport, ErrorCode, Error<ErrorCode>> result) {
        result.IsOk.Should().BeTrue(result.ToString());
        return result.Value;
    }

    private static HashSet<long> FeasibleSingleVariableValues(
        IntVar variable,
        IntegerNoGoodEncoding encoding,
        long lower,
        long upper) {
        var feasible = new HashSet<long>();
        for (long value = lower; value <= upper; value++) {
            var values = new Dictionary<ISymbol, Flt64> {
                [variable] = new Flt64(value),
            };
            if (HasFeasibleAuxiliaryAssignment(encoding, values)) {
                feasible.Add(value);
            }
        }
        return feasible;
    }

    private static bool HasFeasibleAuxiliaryAssignment(
        IntegerNoGoodEncoding encoding,
        IReadOnlyDictionary<ISymbol, Flt64> baseValues) {
        int combinations = 1 << encoding.AuxiliaryVariables.Count;
        for (int mask = 0; mask < combinations; mask++) {
            var values = new Dictionary<ISymbol, Flt64>(baseValues);
            for (int index = 0; index < encoding.AuxiliaryVariables.Count; index++) {
                values[encoding.AuxiliaryVariables[index]] =
                    (mask & (1 << index)) == 0 ? Flt64.Zero : Flt64.One;
            }
            if (encoding.Constraints.All(constraint =>
                constraint.IsSatisfied(values) == true)) {
                return true;
            }
        }
        return false;
    }

    private sealed class ScriptedCutOracle : IBendersCutOracle {
        private readonly Func<int, bool> _emitOptimality;
        private readonly BendersCutKind _kind;
        private readonly BendersCutValidity _validity;

        public ScriptedCutOracle(
            Func<int, bool> emitOptimality,
            BendersCutKind kind,
            BendersCutValidity validity) {
            _emitOptimality = emitOptimality;
            _kind = kind;
            _validity = validity;
        }

        public Result<List<BendersMasterCut>, ErrorCode, Error<ErrorCode>> FeasibilityCuts(
            InfeasibleSubproblemResult result,
            BendersCutContext context) =>
            Results.Ok(new List<BendersMasterCut>());

        public Result<List<BendersMasterCut>, ErrorCode, Error<ErrorCode>> OptimalityCuts(
            FeasibleSubproblemResult result,
            BendersCutContext context) {
            if (!_emitOptimality(context.Iteration)) {
                return Results.Ok(new List<BendersMasterCut>());
            }
            return Results.Ok(new List<BendersMasterCut> {
                new(
                    inequality: ConstantCut(),
                    kind: _kind,
                    validity: _validity,
                    proofStatus: ProofStatus.Verified,
                    source: "scripted-optimality"),
            });
        }

        private static LinearInequality<Flt64> ConstantCut() =>
            new LinearPolynomial<Flt64>(
                Array.Empty<LinearMonomial<Flt64>>(),
                Flt64.Zero).Le(Flt64.One);
    }

    private enum OutputTransform {
        StripObjective,
        MarkUnverified,
    }

    private sealed class TransformingConstraintProgrammingSolver
        : IConstraintProgrammingSolver {
        private readonly FakeConstraintProgrammingSolver _delegate = new();
        private readonly OutputTransform _transform;

        public TransformingConstraintProgrammingSolver(OutputTransform transform) {
            _transform = transform;
        }

        public SolverDescriptor Descriptor => _delegate.Descriptor;

        public async Task<Result<
            ConstraintProgrammingSolverOutput,
            ErrorCode,
            Error<ErrorCode>>> SolveAsync(
            ConstraintProgrammingModel model,
            ConstraintProgrammingSolveOptions? options = null,
            CancellationToken cancellationToken = default) {
            Result<ConstraintProgrammingSolverOutput, ErrorCode, Error<ErrorCode>> result =
                await _delegate.SolveAsync(model, options, cancellationToken)
                    .ConfigureAwait(false);
            return result.Map(Transform);
        }

        public Result<
            IConstraintProgrammingSession,
            ErrorCode,
            Error<ErrorCode>> CreateSession(
            ConstraintProgrammingModel model,
            ConstraintProgrammingSolveOptions? options = null) =>
            _delegate.CreateSession(model, options)
                .Map<IConstraintProgrammingSession>(session =>
                    new TransformingSession(session, Transform));

        private ConstraintProgrammingSolverOutput Transform(
            ConstraintProgrammingSolverOutput output) {
            if (output is not ConstraintProgrammingFeasibleOutput feasible) {
                return output;
            }
            return _transform switch {
                OutputTransform.StripObjective => new ConstraintProgrammingFeasibleOutput(
                    solution: feasible.Solution,
                    objective: null,
                    bestBound: feasible.BestBound,
                    status: feasible.Status,
                    proofStatus: feasible.ProofStatus,
                    report: feasible.Report,
                    exactObjective: null),
                OutputTransform.MarkUnverified => new ConstraintProgrammingFeasibleOutput(
                    solution: feasible.Solution,
                    objective: feasible.Objective,
                    bestBound: feasible.BestBound,
                    status: SolverStatus.Feasible,
                    proofStatus: ProofStatus.None,
                    report: feasible.Report,
                    exactObjective: feasible.ExactObjective),
                _ => output,
            };
        }
    }

    private sealed class TransformingSession : IConstraintProgrammingSession {
        private readonly IConstraintProgrammingSession _delegate;
        private readonly Func<
            ConstraintProgrammingSolverOutput,
            ConstraintProgrammingSolverOutput> _transform;

        public TransformingSession(
            IConstraintProgrammingSession @delegate,
            Func<ConstraintProgrammingSolverOutput, ConstraintProgrammingSolverOutput> transform) {
            _delegate = @delegate;
            _transform = transform;
        }

        public ConstraintProgrammingModel Model => _delegate.Model;

        public ConstraintProgrammingSolveOptions Options => _delegate.Options;

        public bool IsClosed => _delegate.IsClosed;

        public async Task<Result<
            ConstraintProgrammingSolverOutput,
            ErrorCode,
            Error<ErrorCode>>> SolveAsync(
            IReadOnlyList<BooleanLiteral>? assumptions = null,
            IReadOnlyDictionary<VariableId, Int64>? fixedValues = null,
            ConstraintProgrammingSolution? hints = null,
            CancellationToken cancellationToken = default) {
            Result<ConstraintProgrammingSolverOutput, ErrorCode, Error<ErrorCode>> result =
                await _delegate.SolveAsync(
                    assumptions,
                    fixedValues,
                    hints,
                    cancellationToken).ConfigureAwait(false);
            return result.Map(_transform);
        }

        public void Dispose() => _delegate.Dispose();
    }
}
