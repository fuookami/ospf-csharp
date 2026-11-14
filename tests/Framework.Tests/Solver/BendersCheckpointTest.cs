#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
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
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Int64 = Fuookami.Ospf.Math.Algebra.Number.Int64;

namespace Fuookami.Ospf.Framework.Tests.Solver;

public class BendersCheckpointTest {
    [Fact]
    public void CaptureEncodeDecodeRestoreAndCoreEnvelopeShouldRoundTrip() {
        using ConstraintProgrammingModel subproblem = BuildSubproblem(
            name: "benders-checkpoint-round-trip",
            out _);
        ConstraintProgrammingModelSnapshot snapshot = subproblem.Snapshot().Value;
        BendersMasterCut cut = ConstantCut(
            source: "fixture-cut",
            kind: BendersCutKind.Conflict,
            validity: BendersCutValidity.Global,
            proofStatus: ProofStatus.Verified);
        LogicBasedBendersReport report = Report(
            master: MasterOutput(Flt64.One, objective: Flt64.Zero),
            cuts: new[] { cut },
            traces: new[] { Trace(iteration: 0) });
        var serializer = new ConstantCutSerializer();

        PortableConstraintProgrammingBendersState captured =
            BendersCheckpointCodec.Capture(
                report: report,
                serializer: serializer,
                subproblemSnapshot: snapshot,
                masterFingerprint: "master-fixture").Value;

        captured.Iteration.Should().Be(1);
        captured.Cuts.Should().ContainSingle();
        captured.SubproblemModelFingerprint.Should().NotBeNullOrWhiteSpace();
        string encoded = BendersCheckpointCodec.Encode(captured).Value;
        PortableConstraintProgrammingBendersState decoded =
            BendersCheckpointCodec.Decode(encoded).Value;
        decoded.Should().BeEquivalentTo(captured);

        BendersResumeState restored = BendersCheckpointCodec.Restore(
            state: decoded,
            serializer: serializer,
            proofMode: BendersProofMode.Exact,
            expectedSubproblemSnapshot: snapshot).Value;
        restored.Cuts.Should().ContainSingle()
            .Which.Source.Should().Be("fixture-cut");
        restored.Trace.Should().ContainSingle();

        string capturedEncoded = BendersCheckpointCodec.CaptureEncoded(
            report: report,
            serializer: serializer,
            subproblemSnapshot: snapshot,
            masterFingerprint: "master-fixture").Value;
        BendersCheckpointCodec.RestoreEncoded(
            encoded: capturedEncoded,
            serializer: serializer,
            proofMode: BendersProofMode.Exact,
            expectedSubproblemSnapshot: snapshot).IsOk.Should().BeTrue();

        ConstraintProgrammingCheckpointEnvelope envelope =
            BendersCheckpointCodec.CaptureEnvelope(
                report: report,
                serializer: serializer,
                snapshot: snapshot,
                descriptor: Descriptor(),
                checkpointId: "benders-envelope",
                createdAtEpochMs: 1,
                masterFingerprint: "master-fixture").Value;
        envelope.SchemaVersion.Should().Be(
            BendersCheckpointCodec.CoreEnvelopeSchema);
        envelope.Benders.Should().BeEquivalentTo(captured);
        ConstraintProgrammingCheckpointEnvelope envelopeRoundTrip =
            ConstraintProgrammingCheckpointCodec.Decode(
                ConstraintProgrammingCheckpointCodec.Encode(envelope).Value).Value;
        envelopeRoundTrip.Benders.Should().BeEquivalentTo(captured);

        BendersCheckpointCodec.Decode(
            encoded.Replace(
                "\"integritySha256\":\"",
                "\"integritySha256\":\"corrupted",
                StringComparison.Ordinal)).IsFailed.Should().BeTrue();
        BendersCheckpointCodec.Decode(
            encoded.Replace(
                "\"schemaVersion\":\"1.0\"",
                "\"schemaVersion\":\"9.0\"",
                StringComparison.Ordinal)).IsFailed.Should().BeTrue();
    }

    [Fact]
    public void PublishedSchemaOneWithoutMasterFingerprintShouldVerifyLegacyDigest() {
        var state = new PortableConstraintProgrammingBendersState(Iteration: 0);
        string current = BendersCheckpointCodec.Encode(state).Value;
        string legacy = LegacyWithoutMasterFingerprint(current);

        PortableConstraintProgrammingBendersState decoded =
            BendersCheckpointCodec.Decode(legacy).Value;

        decoded.MasterFingerprint.Should().BeNull();
        decoded.Iteration.Should().Be(0);
    }

    [Fact]
    public void CaptureShouldRejectMasterStateWithoutFingerprint() {
        LogicBasedBendersReport report = Report(
            master: MasterOutput(Flt64.One, objective: Flt64.Zero));

        BendersCheckpointCodec.Capture(
            report: report,
            serializer: new ConstantCutSerializer()).IsFailed.Should().BeTrue();
    }

    [Fact]
    public void EncodeShouldRejectDuplicatePrimitiveCutIds() {
        var state = new PortableConstraintProgrammingBendersState(
            Iteration: 1,
            Cuts: new[] {
                new PortableConstraintProgrammingCut(
                    Id: "duplicate",
                    SchemaVersion: "fixture-1",
                    Validity: nameof(BendersCutValidity.Global)),
                new PortableConstraintProgrammingCut(
                    Id: "duplicate",
                    SchemaVersion: "fixture-1",
                    Validity: nameof(BendersCutValidity.Global)),
            });

        BendersCheckpointCodec.Encode(state).IsFailed.Should().BeTrue();
    }

    [Fact]
    public void ExactRestoreShouldRejectUnverifiedCut() {
        var state = new PortableConstraintProgrammingBendersState(
            Iteration: 1,
            Cuts: new[] {
                new PortableConstraintProgrammingCut(
                    Id: "assignment-cut",
                    SchemaVersion: "fixture-1",
                    Validity: nameof(BendersCutValidity.Assignment),
                    Provenance: new Dictionary<string, string> {
                        ["kind"] = nameof(BendersCutKind.Optimality),
                    },
                    Payload: new Dictionary<string, string> {
                        ["source"] = "assignment-cut",
                    }),
            });

        BendersCheckpointCodec.Restore(
            state: state,
            serializer: new ConstantCutSerializer(),
            proofMode: BendersProofMode.Exact).IsFailed.Should().BeTrue();
        BendersCheckpointCodec.Restore(
            state: state,
            serializer: new ConstantCutSerializer(),
            proofMode: BendersProofMode.Heuristic).IsOk.Should().BeTrue();
    }

    [Fact]
    public void RestoreShouldRejectChangedSubproblemFingerprint() {
        using ConstraintProgrammingModel first = BuildSubproblem(
            name: "fingerprint-subproblem",
            out _,
            identitySchemaVersion: "1.1");
        using ConstraintProgrammingModel second = BuildSubproblem(
            name: "fingerprint-subproblem",
            out _,
            identitySchemaVersion: "1.2");
        var serializer = new ConstantCutSerializer();
        LogicBasedBendersReport report = Report();
        PortableConstraintProgrammingBendersState captured =
            BendersCheckpointCodec.Capture(
                report: report,
                serializer: serializer,
                subproblemSnapshot: first.Snapshot().Value).Value;

        BendersCheckpointCodec.Restore(
            state: captured,
            serializer: serializer,
            expectedSubproblemSnapshot: first.Snapshot().Value).IsOk.Should().BeTrue();
        BendersCheckpointCodec.Restore(
            state: captured,
            serializer: serializer,
            expectedSubproblemSnapshot: second.Snapshot().Value).IsFailed.Should().BeTrue();
    }

    [Fact]
    public void CaptureShouldKeepFixedBindingsAssumptionsAndConflictEvidence() {
        using ConstraintProgrammingModel subproblem = BuildSubproblem(
            name: "evidence-subproblem",
            out BinVar cpVariable);
        using var master = new LinearMetaModel<Flt64>(name: "evidence-master");
        var masterVariable = new BinVar("evidence-master-x");
        master.Add(masterVariable).IsOk.Should().BeTrue();
        var cpId = new VariableId("cp:stable");
        BendersSubproblemAssignment assignment = new(
            masterValues: new Dictionary<string, Flt64> {
                ["x"] = Flt64.One,
            },
            masterVariables: new Dictionary<string, IVariableItem> {
                ["x"] = masterVariable,
            },
            fixedValues: new Dictionary<VariableId, Int64> {
                [cpId] = Int64.One,
            },
            assumptions: new[] {
                new BooleanLiteral.VariableLiteral(
                    variable: cpVariable,
                    negated: false,
                    id: cpId),
            },
            assumptionToMaster: new Dictionary<string, string> {
                [cpId.Value] = "x",
            });
        var conflict = new ConstraintProgrammingConflict(
            constraintIds: new[] { new ConstraintId("cp:constraint") },
            assumptions: assignment.Assumptions,
            members: new[] {
                new InfeasibilityMember.ConstraintMember(
                    new ConstraintId("cp:constraint")),
            },
            validity: EvidenceValidity.Verified);
        LogicBasedBendersReport report = Report(
            assignment: assignment,
            subproblem: new InfeasibleSubproblemResult(
                Assignment: assignment,
                Conflict: conflict));
        var serializer = new ConstantCutSerializer();

        PortableConstraintProgrammingBendersState captured =
            BendersCheckpointCodec.Capture(
                report: report,
                serializer: serializer,
                subproblemSnapshot: subproblem.Snapshot().Value,
                masterFingerprint: "master-evidence").Value;

        captured.Assumptions.Should().Equal(cpId.Value);
        captured.FixedBindings.Should().ContainKey(cpId.Value)
            .WhoseValue.Should().Be(1);
        captured.Conflicts.Should().ContainSingle();
        captured.Conflicts![0].MemberIds!.Should().Equal("constraint:cp:constraint");
        captured.Conflicts[0].AssumptionIds!.Should().Equal(cpId.Value);
        BendersCheckpointCodec.Restore(
            state: captured,
            serializer: serializer,
            expectedSubproblemSnapshot: subproblem.Snapshot().Value).IsOk.Should().BeTrue();
    }

    [Fact]
    public async Task ResumedEngineShouldSeedMasterAndContinueFromNextIteration() {
        using var master = new LinearMetaModel<Flt64>(name: "resume-master");
        var masterVariable = new BinVar("resume-master-x");
        master.Add(masterVariable).IsOk.Should().BeTrue();
        using ConstraintProgrammingModel subproblem = BuildSubproblem(
            name: "resume-subproblem",
            out BinVar cpVariable);
        var binding = new BinaryBendersVariableBinding(new[] {
            new BinaryBendersVariable(
                Key: StableKey(masterVariable),
                MasterVariable: masterVariable,
                SubproblemVariable: cpVariable,
                SubproblemVariableId: new VariableId("cp:stable")),
        });
        BendersMasterCut resumeCut = ConstantCut(
            source: "resume-cut",
            kind: BendersCutKind.Optimality,
            validity: BendersCutValidity.Global,
            proofStatus: ProofStatus.Verified);
        var resume = new BendersResumeState(
            iteration: 1,
            cuts: new[] { resumeCut });
        int masterCalls = 0;
        BendersMasterProblemSolver masterSolver = model => {
            ++masterCalls;
            model.RelationConstraints.Should().NotBeEmpty();
            return Task.FromResult(Results.Ok(MasterOutput(
                Flt64.One,
                objective: Flt64.Zero)));
        };
        var engine = new LogicBasedBendersEngine(
            masterSolver: masterSolver,
            subproblemSolver: new FakeConstraintProgrammingSolver(),
            binding: binding,
            options: new LogicBasedBendersOptions {
                MaxIterations = 2,
                MasterVariables = new[] { masterVariable },
                ResumeState = resume,
            });

        LogicBasedBendersReport report = (await engine.SolveAsync(
            master: master,
            subproblem: subproblem)).Value;

        masterCalls.Should().Be(1);
        report.Cuts.Should().ContainSingle()
            .Which.Source.Should().Be("resume-cut");
        report.Iterations.Should().OnlyContain(trace => trace.Iteration >= 1);
        report.Proof.Status.Should().Be(ProofStatus.Verified);
    }

    [Fact]
    public async Task ResumeShouldRejectUnknownAssumption() {
        using var master = new LinearMetaModel<Flt64>(name: "resume-invalid-master");
        var masterVariable = new BinVar("resume-invalid-x");
        master.Add(masterVariable).IsOk.Should().BeTrue();
        using ConstraintProgrammingModel subproblem = BuildSubproblem(
            name: "resume-invalid-subproblem",
            out BinVar cpVariable);
        var engine = new LogicBasedBendersEngine(
            masterSolver: _ => Task.FromResult(Results.Ok(MasterOutput(
                Flt64.One,
                objective: Flt64.Zero))),
            subproblemSolver: new FakeConstraintProgrammingSolver(),
            binding: new BinaryBendersVariableBinding(new[] {
                new BinaryBendersVariable(
                    Key: StableKey(masterVariable),
                    MasterVariable: masterVariable,
                    SubproblemVariable: cpVariable,
                    SubproblemVariableId: new VariableId("cp:stable")),
            }),
            options: new LogicBasedBendersOptions {
                ResumeState = new BendersResumeState(
                    iteration: 1,
                    assumptions: new[] { "cp:missing" },
                    masterFingerprint: "master-invalid"),
                MasterVariables = new[] { masterVariable },
            });

        (await engine.SolveAsync(master: master, subproblem: subproblem))
            .IsFailed.Should().BeTrue();
    }

    [Fact]
    public async Task ResumeStateApplierShouldAcceptMatchingEvidence() {
        using var master = new LinearMetaModel<Flt64>(name: "resume-applier-master");
        var masterVariable = new BinVar("resume-applier-x");
        master.Add(masterVariable).IsOk.Should().BeTrue();
        using ConstraintProgrammingModel subproblem = BuildSubproblem(
            name: "resume-applier-subproblem",
            out BinVar cpVariable);
        var resume = new BendersResumeState(
            iteration: 1,
            masterFingerprint: "master-fixture",
            masterIncumbent: "0",
            masterBestBound: "0");
        bool applierCalled = false;
        var engine = new LogicBasedBendersEngine(
            masterSolver: _ => Task.FromResult(Results.Ok(MasterOutput(
                Flt64.One,
                objective: Flt64.Zero))),
            subproblemSolver: new FakeConstraintProgrammingSolver(),
            binding: new BinaryBendersVariableBinding(new[] {
                new BinaryBendersVariable(
                    Key: StableKey(masterVariable),
                    MasterVariable: masterVariable,
                    SubproblemVariable: cpVariable,
                    SubproblemVariableId: new VariableId("cp:stable")),
            }),
            options: new LogicBasedBendersOptions {
                MaxIterations = 2,
                MasterVariables = new[] { masterVariable },
                ResumeState = resume,
                ResumeStateApplier = (_, state) => {
                    applierCalled = true;
                    return Results.Ok(new BendersMasterResumeEvidence(
                        masterFingerprint: state.MasterFingerprint!,
                        incumbent: state.MasterIncumbent,
                        bestBound: state.MasterBestBound,
                        assumptions: state.Assumptions,
                        fixedBindings: state.FixedBindings,
                        conflicts: state.Conflicts,
                        convergenceVerified: state.ConvergenceVerified));
                },
            });

        Result<LogicBasedBendersReport, ErrorCode, Error<ErrorCode>> result =
            await engine.SolveAsync(master: master, subproblem: subproblem);

        result.IsOk.Should().BeTrue();
        applierCalled.Should().BeTrue();
    }

    [Fact]
    public async Task ResumeStateApplierShouldRejectMismatchedEvidenceBeforeMasterSolve() {
        using var master = new LinearMetaModel<Flt64>(name: "resume-applier-mismatch-master");
        var masterVariable = new BinVar("resume-applier-mismatch-x");
        master.Add(masterVariable).IsOk.Should().BeTrue();
        using ConstraintProgrammingModel subproblem = BuildSubproblem(
            name: "resume-applier-mismatch-subproblem",
            out BinVar cpVariable);
        var resume = new BendersResumeState(
            iteration: 1,
            masterFingerprint: "master-fixture",
            masterIncumbent: "0",
            masterBestBound: "0");
        int masterCalls = 0;
        var engine = new LogicBasedBendersEngine(
            masterSolver: _ => {
                ++masterCalls;
                return Task.FromResult(Results.Ok(MasterOutput(
                    Flt64.One,
                    objective: Flt64.Zero)));
            },
            subproblemSolver: new FakeConstraintProgrammingSolver(),
            binding: new BinaryBendersVariableBinding(new[] {
                new BinaryBendersVariable(
                    Key: StableKey(masterVariable),
                    MasterVariable: masterVariable,
                    SubproblemVariable: cpVariable,
                    SubproblemVariableId: new VariableId("cp:stable")),
            }),
            options: new LogicBasedBendersOptions {
                MaxIterations = 2,
                MasterVariables = new[] { masterVariable },
                ResumeState = resume,
                ResumeStateApplier = (_, state) => Results.Ok(
                    new BendersMasterResumeEvidence(
                        masterFingerprint: state.MasterFingerprint!,
                        incumbent: state.MasterIncumbent,
                        bestBound: "1",
                        assumptions: state.Assumptions,
                        fixedBindings: state.FixedBindings,
                        conflicts: state.Conflicts,
                        convergenceVerified: state.ConvergenceVerified)),
            });

        Result<LogicBasedBendersReport, ErrorCode, Error<ErrorCode>> result =
            await engine.SolveAsync(master: master, subproblem: subproblem);

        result.IsFailed.Should().BeTrue();
        masterCalls.Should().Be(0);
    }

    [Fact]
    public void DecodeTraceShouldRejectUnknownEnumsAndInvalidFields() {
        string encoded = BendersCheckpointCodec.EncodeTrace(Trace(iteration: 0)).Value;

        BendersCheckpointCodec.DecodeTrace(
            encoded.Replace(
                "\"subproblemStatus\":\"Feasible\"",
                "\"subproblemStatus\":\"Bogus\"",
                StringComparison.Ordinal)).IsFailed.Should().BeTrue();
        BendersCheckpointCodec.DecodeTrace(
            encoded.Replace(
                "\"proofMode\":\"Exact\"",
                "\"proofMode\":\"Bogus\"",
                StringComparison.Ordinal)).IsFailed.Should().BeTrue();
        BendersCheckpointCodec.DecodeTrace(
            encoded.Replace(
                "\"schemaVersion\":\"1.0\"",
                "\"schemaVersion\":\"9.0\"",
                StringComparison.Ordinal)).IsFailed.Should().BeTrue();
        BendersCheckpointCodec.DecodeTrace(
            encoded.Replace(
                "\"elapsedNanos\":1000000",
                "\"elapsedNanos\":-1",
                StringComparison.Ordinal)).IsFailed.Should().BeTrue();
        BendersCheckpointCodec.DecodeTrace(
            encoded.Replace("v1:", string.Empty, StringComparison.Ordinal))
            .IsFailed.Should().BeTrue();
    }

    [Fact]
    public void RestoreShouldRejectInvalidTraceOrderRangeAndProofMode() {
        var serializer = new ConstantCutSerializer();
        var wrongOrder = new PortableConstraintProgrammingBendersState(
            Iteration: 2,
            Trace: new[] {
                BendersCheckpointCodec.EncodeTrace(Trace(iteration: 1)).Value,
                BendersCheckpointCodec.EncodeTrace(Trace(iteration: 0)).Value,
            });
        BendersCheckpointCodec.Restore(
            state: wrongOrder,
            serializer: serializer).IsFailed.Should().BeTrue();

        var outOfRange = new PortableConstraintProgrammingBendersState(
            Iteration: 2,
            Trace: new[] {
                BendersCheckpointCodec.EncodeTrace(Trace(iteration: 2)).Value,
            });
        BendersCheckpointCodec.Restore(
            state: outOfRange,
            serializer: serializer).IsFailed.Should().BeTrue();

        var wrongProofMode = new PortableConstraintProgrammingBendersState(
            Iteration: 1,
            Trace: new[] {
                BendersCheckpointCodec.EncodeTrace(
                    Trace(iteration: 0, proofMode: BendersProofMode.Heuristic)).Value,
            });
        BendersCheckpointCodec.Restore(
            state: wrongProofMode,
            serializer: serializer,
            proofMode: BendersProofMode.Exact).IsFailed.Should().BeTrue();
    }

    [Fact]
    public void RestoreShouldRejectCutSchemaMismatchAndEmptyCutId() {
        var schemaMismatch = new PortableConstraintProgrammingBendersState(
            Iteration: 1,
            Cuts: new[] {
                new PortableConstraintProgrammingCut(
                    Id: "schema-mismatch",
                    SchemaVersion: "fixture-2",
                    Validity: nameof(BendersCutValidity.Global)),
            });
        BendersCheckpointCodec.Restore(
            state: schemaMismatch,
            serializer: new ConstantCutSerializer()).IsFailed.Should().BeTrue();

        var emptyId = new PortableConstraintProgrammingBendersState(
            Iteration: 1,
            Cuts: new[] {
                new PortableConstraintProgrammingCut(
                    Id: " ",
                    SchemaVersion: "fixture-1",
                    Validity: nameof(BendersCutValidity.Global)),
            });
        BendersCheckpointCodec.Encode(emptyId).IsFailed.Should().BeTrue();
    }

    [Fact]
    public void KotlinCanonicalCheckpointFixtureShouldRoundTripAndReencode() {
        PortableConstraintProgrammingBendersState state = KotlinFixtureState();

        string encoded = BendersCheckpointCodec.Encode(state).Value;

        encoded.Should().Be(KotlinCheckpointFixture);
        BendersCheckpointCodec.Decode(KotlinCheckpointFixture).Value
            .Should().BeEquivalentTo(state);
        BendersCheckpointCodec.Encode(
            BendersCheckpointCodec.Decode(KotlinCheckpointFixture).Value).Value
            .Should().Be(KotlinCheckpointFixture);
        BendersCheckpointCodec.EncodeTrace(KotlinFixtureTrace()).Value
            .Should().Be(KotlinTraceFixture);
    }

    private static ConstraintProgrammingModel BuildSubproblem(
        string name,
        out BinVar variable,
        string identitySchemaVersion = "1.0") {
        variable = new BinVar($"{name}-cp-x");
        var model = new ConstraintProgrammingModel(
            name: name,
            identitySchemaVersion: identitySchemaVersion);
        IntegerDomain domain = IntegerDomain.CreateInterval(
            lowerBound: Int64.Zero,
            upperBound: Int64.One).Value;
        model.RegisterVariable(
            id: new VariableId("cp:stable"),
            variable: variable,
            domain: domain).IsOk.Should().BeTrue();
        ConstraintProgrammingExpression expression =
            ConstraintProgrammingExpression.CreateVariable(variable).Value;
        model.AddConstraint(
            constraint: ConstraintProgrammingConstraint.CreateEqual(
                expression,
                Int64.One).Value,
            id: "cp:constraint").IsOk.Should().BeTrue();
        return model;
    }

    private static LogicBasedBendersReport Report(
        SolveReport<Flt64>? master = null,
        BendersSubproblemAssignment? assignment = null,
        LogicBasedBendersSubproblemResult? subproblem = null,
        IEnumerable<BendersMasterCut>? cuts = null,
        IEnumerable<BendersIterationTrace>? traces = null) =>
        new(
            problemStatus: ProblemStatus.Feasible,
            terminationReason: TerminationReason.Completed,
            proof: new SolveProof(ProofStatus.None, "test-benders"),
            masterOutput: master,
            assignment: assignment,
            subproblemResult: subproblem,
            cuts: cuts,
            iterations: traces);

    private static SolveReport<Flt64> MasterOutput(
        Flt64 value,
        Flt64 objective) =>
        new() {
            ProblemStatus = ProblemStatus.Feasible,
            TerminationReason = TerminationReason.Completed,
            SolutionPresence = SolutionPresence.Optimal,
            Solution = new SolveSolution<Flt64>(
                Values: new Solution<Flt64>(new[] { value }),
                Objective: objective),
            Proof = new SolveProof(ProofStatus.Verified, "test-master"),
            Statistics = new SolveStatistics(
                SolveTime: TimeSpan.Zero,
                BestBound: objective),
        };

    private static BendersIterationTrace Trace(
        int iteration,
        BendersProofMode proofMode = BendersProofMode.Exact) =>
        new(
            Iteration: iteration,
            MasterObjective: Flt64.Zero,
            MasterBestBound: Flt64.Zero,
            MasterStatus: SolverStatus.Optimal,
            SubproblemStatus: BendersSubproblemStatus.Feasible,
            SubproblemObjective: Flt64.Zero,
            CutCount: 0,
            ConflictCoreSize: 0,
            Elapsed: TimeSpan.FromMilliseconds(1),
            ProofMode: proofMode,
            ConvergenceGap: Flt64.Zero);

    private static BendersIterationTrace KotlinFixtureTrace() =>
        Trace(iteration: 0);

    private static PortableConstraintProgrammingBendersState KotlinFixtureState() =>
        new(
            Iteration: 2,
            MasterIncumbent: "-1.5",
            MasterBestBound: "-2",
            Cuts: new[] {
                new PortableConstraintProgrammingCut(
                    Id: "cut-a",
                    SchemaVersion: "fixture-1",
                    Validity: "Global",
                    Provenance: new Dictionary<string, string> {
                        ["kind"] = "Optimality",
                    },
                    Payload: new Dictionary<string, string> {
                        ["source"] = "cut-a",
                    }),
            },
            Trace: new[] { BendersCheckpointCodec.EncodeTrace(KotlinFixtureTrace()).Value },
            Assumptions: new[] { "cp:stable" },
            FixedBindings: new Dictionary<string, long> {
                ["cp:stable"] = 1,
            },
            Conflicts: new[] {
                new PortableConstraintProgrammingConflict(
                    Validity: "Verified",
                    Minimality: "Irreducible",
                    MemberIds: new[] { "constraint:cp:constraint" },
                    AssumptionIds: new[] { "cp:stable" },
                    Provenance: new Dictionary<string, string> {
                        ["message"] = "fixture",
                    }),
            },
            ConvergenceVerified: false,
            MasterFingerprint: "master-fixture",
            SubproblemModelFingerprint: "sub-fixture");

    private static BendersMasterCut ConstantCut(
        string source,
        BendersCutKind kind = BendersCutKind.Optimality,
        BendersCutValidity validity = BendersCutValidity.Global,
        ProofStatus proofStatus = ProofStatus.Verified) =>
        new(
            inequality: new LinearInequality<Flt64>(
                Lhs: new LinearPolynomial<Flt64>(
                    Array.Empty<LinearMonomial<Flt64>>(),
                    Flt64.Zero),
                Rhs: new LinearPolynomial<Flt64>(
                    Array.Empty<LinearMonomial<Flt64>>(),
                    Flt64.One),
                Comparison: Comparison.LE),
            kind: kind,
            validity: validity,
            proofStatus: proofStatus,
            source: source);

    private static string StableKey(IVariableItem variable) =>
        $"{variable.Identifier}:{variable.Index}";

    private static SolverDescriptor Descriptor() =>
        new(
            SolverId: "fixture.cp",
            BackendName: "fixture",
            Capabilities: new SolverCapabilities(
                new HashSet<SolverModelType> { SolverModelType.Cp },
                Checkpoint: true,
                Resume: true),
            BackendVersion: "1.0");

    private static string LegacyWithoutMasterFingerprint(string current) {
        string legacy = current.Replace(
            "\"masterFingerprint\":null,",
            string.Empty,
            StringComparison.Ordinal);
        const string digestPrefix = "\"integritySha256\":\"";
        int digestStart = legacy.IndexOf(digestPrefix, StringComparison.Ordinal);
        int digestEnd = legacy.IndexOf(
            '"',
            digestStart + digestPrefix.Length);
        string unsigned = string.Concat(
            legacy[..digestStart],
            "\"integritySha256\":\"\"",
            legacy[(digestEnd + 1)..]);
        string digest = SolveFingerprinting.Sha256(unsigned).Value;
        return unsigned.Replace(
            "\"integritySha256\":\"\"",
            $"\"integritySha256\":\"{digest}\"",
            StringComparison.Ordinal);
    }

    private const string KotlinTraceFixture =
        """v1:{"schemaVersion":"1.0","iteration":0,"masterObjective":"0","masterBestBound":"0","masterStatus":"Optimal","subproblemStatus":"Feasible","subproblemObjective":"0","cutCount":0,"conflictCoreSize":0,"elapsedNanos":1000000,"proofMode":"Exact","convergenceGap":"0"}""";

    private const string KotlinCheckpointFixture =
        """{"schemaVersion":"1.0","state":{"iteration":2,"masterIncumbent":"-1.5","masterBestBound":"-2","cuts":[{"id":"cut-a","schemaVersion":"fixture-1","validity":"Global","provenance":{"kind":"Optimality"},"payload":{"source":"cut-a"}}],"trace":["v1:{\"schemaVersion\":\"1.0\",\"iteration\":0,\"masterObjective\":\"0\",\"masterBestBound\":\"0\",\"masterStatus\":\"Optimal\",\"subproblemStatus\":\"Feasible\",\"subproblemObjective\":\"0\",\"cutCount\":0,\"conflictCoreSize\":0,\"elapsedNanos\":1000000,\"proofMode\":\"Exact\",\"convergenceGap\":\"0\"}"],"assumptions":["cp:stable"],"fixedBindings":{"cp:stable":1},"conflicts":[{"validity":"Verified","minimality":"Irreducible","memberIds":["constraint:cp:constraint"],"assumptionIds":["cp:stable"],"provenance":{"message":"fixture"}}],"convergenceVerified":false,"masterFingerprint":"master-fixture","subproblemModelFingerprint":"sub-fixture"},"integritySha256":"a73ccbf538651b356e3e01da979169e3a852ead6fa04f1b28986568fa9824472"}""";

    private sealed class ConstantCutSerializer : IBendersCutSerializationSpi {
        public string SchemaVersion => "fixture-1";

        public Result<
            PortableConstraintProgrammingCut,
            ErrorCode,
            Error<ErrorCode>> Encode(BendersMasterCut cut) =>
            Results.Ok(new PortableConstraintProgrammingCut(
                Id: cut.Source,
                SchemaVersion: SchemaVersion,
                Validity: cut.Validity.ToString(),
                Provenance: new Dictionary<string, string> {
                    ["kind"] = cut.Kind.ToString(),
                },
                Payload: new Dictionary<string, string> {
                    ["source"] = cut.Source,
                }));

        public Result<BendersMasterCut, ErrorCode, Error<ErrorCode>> Decode(
            PortableConstraintProgrammingCut cut) {
            try {
                string source = cut.Payload is not null &&
                    cut.Payload.TryGetValue("source", out string? value)
                    ? value
                    : cut.Id;
                string kind = cut.Provenance is not null &&
                    cut.Provenance.TryGetValue("kind", out string? kindValue)
                    ? kindValue
                    : nameof(BendersCutKind.Optimality);
                return Results.Ok(ConstantCut(
                    source: source,
                    kind: Enum.Parse<BendersCutKind>(kind),
                    validity: Enum.Parse<BendersCutValidity>(cut.Validity),
                    proofStatus: ProofStatus.Verified));
            }
            catch (Exception error) {
                return Results.Failed<BendersMasterCut>(new Err<ErrorCode>(
                    code: ErrorCode.IllegalArgument,
                    message: $"fixture cut decode failed: {error.Message}"));
            }
        }
    }
}
