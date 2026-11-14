#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;
using Int64 = Fuookami.Ospf.Math.Algebra.Number.Int64;

namespace Fuookami.Ospf.Core.Tests.Solver.ConstraintProgramming;

public class ConstraintProgrammingCheckpointCodecTest {
    [Fact]
    public void CaptureShouldKeepAbsentIncumbentAsNull() {
        using ConstraintProgrammingModel model = BuildModel(
            "checkpoint-no-incumbent",
            out _,
            out _,
            out _);

        ConstraintProgrammingCheckpointEnvelope envelope =
            ConstraintProgrammingCheckpointCodec.Capture(
                snapshot: model.Snapshot().Value,
                descriptor: Descriptor(),
                checkpointId: "checkpoint:none",
                createdAtEpochMs: 1).Value;

        envelope.Incumbent.Should().BeNull();
    }

    [Fact]
    public void CaptureRestoreAndForkShouldRevalidateAndStayIdempotent() {
        using ConstraintProgrammingModel model = BuildModel(
            "checkpoint-round-trip",
            out VariableId variableId,
            out _,
            out IntervalId intervalId);
        ConstraintProgrammingModelSnapshot snapshot = model.Snapshot().Value;
        var cancellation = new CancellationRecord(
            CancellationSource.Remote,
            DateTimeOffset.FromUnixTimeMilliseconds(1_000),
            "peer stop");
        PortableSolverProvenance provenance = Provenance();

        ConstraintProgrammingCheckpointEnvelope captured =
            ConstraintProgrammingCheckpointCodec.Capture(
                snapshot: snapshot,
                descriptor: Descriptor(),
                checkpointId: "checkpoint:parent",
                createdAtEpochMs: 1_000,
                incumbent: new ConstraintProgrammingSolution(
                    values: new Dictionary<VariableId, Int64> {
                        [variableId] = Int64.One,
                    }),
                configurationFingerprint: "configuration:1",
                runId: "run:1",
                attemptId: "attempt:1",
                bestBound: "1",
                gap: "0",
                cancellationChain: new[] { cancellation },
                provenance: provenance).Value;

        captured.Incumbent.Should().NotBeNull();
        captured.Incumbent!.ValuesById![variableId.Value].Should().Be(1);
        captured.Incumbent.IntervalsById![intervalId.Value]
            .Should().Be(new PortableConstraintProgrammingIntervalValue(
                Start: 1,
                Size: 1,
                End: 2));
        captured.Incumbent.Objective.Should().Be("1");

        string encoded = ConstraintProgrammingCheckpointCodec.Encode(captured).Value;
        ConstraintProgrammingCheckpointEnvelope firstDecoded =
            ConstraintProgrammingCheckpointCodec.Decode(encoded).Value;
        ConstraintProgrammingCheckpointEnvelope secondDecoded =
            ConstraintProgrammingCheckpointCodec.Decode(encoded).Value;
        ConstraintProgrammingCheckpointRestore firstRestore =
            ConstraintProgrammingCheckpointCodec.Restore(
                firstDecoded,
                snapshot,
                expectedConfigurationFingerprint: "configuration:1",
                expectedSolverFingerprint: firstDecoded.SolverFingerprint,
                expectedRunId: "run:1",
                expectedAttemptId: "attempt:1",
                expectedCancellationChain: firstDecoded.CancellationChain,
                expectedProvenance: provenance).Value;
        ConstraintProgrammingCheckpointRestore secondRestore =
            ConstraintProgrammingCheckpointCodec.Restore(
                secondDecoded,
                snapshot,
                expectedConfigurationFingerprint: "configuration:1",
                expectedSolverFingerprint: secondDecoded.SolverFingerprint,
                expectedRunId: "run:1",
                expectedAttemptId: "attempt:1",
                expectedCancellationChain: secondDecoded.CancellationChain,
                expectedProvenance: provenance).Value;

        firstRestore.Envelope.Should().BeEquivalentTo(secondRestore.Envelope);
        firstRestore.Incumbent.Should().BeEquivalentTo(secondRestore.Incumbent);
        firstRestore.BestBound.Should().Be("1");
        firstRestore.Gap.Should().Be("0");
        ConstraintProgrammingCheckpointCodec.Encode(firstRestore.Envelope).Value
            .Should().Be(encoded);

        ConstraintProgrammingCheckpointEnvelope child =
            ConstraintProgrammingCheckpointCodec.ForkForResume(
                captured,
                checkpointId: "checkpoint:child",
                createdAtEpochMs: 2_000,
                attemptId: "attempt:2").Value;

        child.RunId.Should().Be(captured.RunId);
        child.ParentCheckpointId.Should().Be(captured.CheckpointId);
        child.AttemptId.Should().Be("attempt:2");
        child.CancellationChain.Should().Equal(captured.CancellationChain);
        ConstraintProgrammingCheckpointCodec.ValidateResumedChild(captured, child)
            .Should().BeNull();
        ConstraintProgrammingCheckpointCodec.ValidateResumedChild(
            captured,
            child with { CancellationChain = Array.Empty<PortableCancellationRecord>() })
            .Should().NotBeNull();
        ConstraintProgrammingCheckpointCodec.ForkForResume(
            captured,
            checkpointId: captured.CheckpointId,
            createdAtEpochMs: 3_000).IsFailed.Should().BeTrue();
    }

    [Fact]
    public void RestoreShouldRejectInvalidIncumbentEvidenceBoundsAndBendersState() {
        using ConstraintProgrammingModel model = BuildModel(
            "checkpoint-invalid",
            out VariableId variableId,
            out ConstraintId constraintId,
            out IntervalId intervalId);
        ConstraintProgrammingModelSnapshot snapshot = model.Snapshot().Value;
        ConstraintProgrammingCheckpointEnvelope captured =
            ConstraintProgrammingCheckpointCodec.Capture(
                snapshot: snapshot,
                descriptor: Descriptor(),
                checkpointId: "checkpoint:invalid",
                createdAtEpochMs: 1,
                incumbent: new ConstraintProgrammingSolution(
                    values: new Dictionary<VariableId, Int64> {
                        [variableId] = Int64.One,
                    }),
                configurationFingerprint: "configuration:invalid",
                bestBound: "1",
                gap: "0").Value;

        RestoreFails(captured with {
            Assumptions = new[] { "missing-variable" },
        });
        RestoreFails(captured with {
            Conflicts = new[] {
                new PortableConstraintProgrammingConflict(
                    Validity: "Verified",
                    Minimality: "Irreducible",
                    MemberIds: new[] { "constraint:missing" }),
            },
        });
        RestoreFails(captured with {
            Conflicts = new[] {
                new PortableConstraintProgrammingConflict(
                    Validity: "Verified",
                    Minimality: "Irreducible"),
            },
        });
        RestoreFails(captured with { Gap = "0.5" });
        RestoreFails(captured with {
            Incumbent = captured.Incumbent! with {
                ValuesById = new Dictionary<string, long>(),
            },
        });
        RestoreFails(captured with {
            Incumbent = captured.Incumbent! with {
                Objective = "2",
            },
        });
        RestoreFails(captured with {
            Incumbent = captured.Incumbent! with {
                ValuesById = new Dictionary<string, long> {
                    [variableId.Value] = 4,
                },
            },
        });
        RestoreFails(captured with {
            Incumbent = captured.Incumbent! with {
                IntervalsById = new Dictionary<
                    string,
                    PortableConstraintProgrammingIntervalValue> {
                    [intervalId.Value] = new(
                        Start: 1,
                        Size: 1,
                        End: 9),
                },
            },
        });
        RestoreFails(captured with {
            Benders = new PortableConstraintProgrammingBendersState(
                Iteration: 0,
                MasterIncumbent: "1"),
        });
        ConstraintProgrammingCheckpointCodec.Capture(
            snapshot: snapshot,
            descriptor: Descriptor(),
            checkpointId: "checkpoint:violates-constraint",
            createdAtEpochMs: 1,
            incumbent: new ConstraintProgrammingSolution(
                values: new Dictionary<VariableId, Int64> {
                    [variableId] = Int64.Zero,
                }),
            configurationFingerprint: "configuration:invalid").IsFailed
            .Should().BeTrue();

        ConstraintProgrammingCheckpointEnvelope validEvidence =
            ConstraintProgrammingCheckpointCodec.Capture(
                snapshot: snapshot,
                descriptor: Descriptor(),
                checkpointId: "checkpoint:evidence",
                createdAtEpochMs: 1,
                configurationFingerprint: "configuration:evidence",
                assumptions: new[] { variableId.Value },
                conflicts: new[] {
                    new PortableConstraintProgrammingConflict(
                        Validity: "Verified",
                        Minimality: "Irreducible",
                        MemberIds: new[] {
                            $"constraint:{constraintId.Value}",
                            $"bound:{variableId.Value}:Lower",
                        }),
                }).Value;
        RestoreOk(
            validEvidence,
            snapshot,
            expectedConfigurationFingerprint: "configuration:evidence",
            expectedSolverFingerprint: validEvidence.SolverFingerprint);

        void RestoreFails(ConstraintProgrammingCheckpointEnvelope envelope) {
            ConstraintProgrammingCheckpointCodec.Restore(
                envelope,
                snapshot,
                expectedConfigurationFingerprint: "configuration:invalid",
                expectedSolverFingerprint: envelope.SolverFingerprint)
                .IsFailed.Should().BeTrue();
        }
    }

    [Fact]
    public void CompatibleDecodeShouldMigrateLegacyFormatsButRejectMalformedV2() {
        using ConstraintProgrammingModel model = BuildModel(
            "checkpoint-migration",
            out _,
            out _,
            out _);
        ConstraintProgrammingModelSnapshot snapshot = model.Snapshot().Value;
        string snapshotJson = ConstraintProgrammingSnapshotCodec.Encode(snapshot).Value;

        string legacyV2 = EncodeLegacyV2(snapshot, snapshotJson);
        ConstraintProgrammingCheckpointEnvelope migratedV2 =
            ConstraintProgrammingCheckpointCodec.DecodeCompatible(legacyV2).Value;

        migratedV2.SchemaVersion.Should().Be("3.0");
        migratedV2.SourceFormat.Should().Be("v2");
        migratedV2.Benders.Should().NotBeNull();
        migratedV2.Benders!.MasterFingerprint.Should().BeNull();
        migratedV2.Benders.SubproblemModelFingerprint.Should().Be("subproblem:1");

        string legacyV1 = JsonSerializer.Serialize(new {
            schema = 1,
            modelName = snapshot.Name,
            solverId = "legacy-solver",
            snapshotJson,
        });
        ConstraintProgrammingCheckpointEnvelope decodedV1 =
            ConstraintProgrammingCheckpointCodec.DecodeCompatible(legacyV1).Value;
        decodedV1.SourceFormat.Should().Be("legacy-v1");
        decodedV1.CheckpointId.Should().Be("legacy-v1");
        decodedV1.MigratedFromLegacy.Should().BeFalse();

        string migratedText =
            ConstraintProgrammingCheckpointCodec.Encode(decodedV1).Value;
        ConstraintProgrammingCheckpointEnvelope remigrated =
            ConstraintProgrammingCheckpointCodec.DecodeCompatible(migratedText).Value;
        remigrated.SourceFormat.Should().Be("v2");
        remigrated.CheckpointId.Should().Be("legacy-v1-migrated");
        remigrated.MigratedFromLegacy.Should().BeTrue();
        ConstraintProgrammingCheckpointCodec.Restore(
            remigrated,
            snapshot,
            expectedConfigurationFingerprint: null,
            expectedSolverFingerprint: null).IsOk.Should().BeTrue();

        ConstraintProgrammingCheckpointEnvelope current =
            ConstraintProgrammingCheckpointCodec.Capture(
                snapshot: snapshot,
                descriptor: Descriptor(),
                checkpointId: "checkpoint:current",
                createdAtEpochMs: 1).Value;
        string currentText =
            ConstraintProgrammingCheckpointCodec.Encode(current).Value;
        string malformed = currentText[..^1] + ",\"schema\":1}";
        ConstraintProgrammingCheckpointCodec.DecodeCompatible(malformed)
            .IsFailed.Should().BeTrue();
    }

    private static string EncodeLegacyV2(
        ConstraintProgrammingModelSnapshot snapshot,
        string snapshotJson) {
        var benders = new LegacyV2BendersState(
            Iteration: 2,
            MasterIncumbent: null,
            MasterBestBound: null,
            Cuts: Array.Empty<PortableConstraintProgrammingCut>(),
            Trace: Array.Empty<string>(),
            Assumptions: Array.Empty<string>(),
            FixedBindings: new Dictionary<string, long>(),
            Conflicts: Array.Empty<PortableConstraintProgrammingConflict>(),
            ConvergenceVerified: false,
            SubproblemModelFingerprint: "subproblem:1");
        var envelope = new LegacyV2Envelope(
            SchemaVersion: "2.0",
            SourceFormat: "v2",
            MigratedFromLegacy: false,
            CheckpointId: "checkpoint:legacy-v2",
            IdentitySchemaVersion: snapshot.IdentitySchemaVersion,
            IdentityNamespace: snapshot.IdentityNamespace,
            ModelName: snapshot.Name,
            ModelFingerprint: SolveFingerprinting.Sha256(snapshotJson).Value,
            ConfigurationFingerprint: "configuration:legacy",
            SolverFingerprint: "solver:legacy",
            RunId: "run:legacy",
            AttemptId: "attempt:legacy",
            ParentCheckpointId: null,
            CreatedAtEpochMs: 1,
            SnapshotJson: snapshotJson,
            Incumbent: null,
            BestBound: null,
            Gap: null,
            Assumptions: Array.Empty<string>(),
            Conflicts: Array.Empty<PortableConstraintProgrammingConflict>(),
            Benders: benders,
            IntegritySha256: string.Empty);
        string unsigned = JsonSerializer.Serialize(
            envelope,
            ConstraintProgrammingSnapshotCodec.JsonOptions);
        return JsonSerializer.Serialize(
            envelope with {
                IntegritySha256 = SolveFingerprinting.Sha256(unsigned).Value,
            },
            ConstraintProgrammingSnapshotCodec.JsonOptions);
    }

    private static ConstraintProgrammingCheckpointRestore RestoreOk(
        ConstraintProgrammingCheckpointEnvelope envelope,
        ConstraintProgrammingModelSnapshot snapshot,
        string? expectedConfigurationFingerprint,
        string? expectedSolverFingerprint) {
        Result<
            ConstraintProgrammingCheckpointRestore,
            ErrorCode,
            Error<ErrorCode>> result =
            ConstraintProgrammingCheckpointCodec.Restore(
                envelope,
                snapshot,
                expectedConfigurationFingerprint,
                expectedSolverFingerprint);
        result.Should().BeOfType<
            Ok<
                ConstraintProgrammingCheckpointRestore,
                ErrorCode,
                Error<ErrorCode>>>(because: result is Failed<
                    ConstraintProgrammingCheckpointRestore,
                    ErrorCode,
                    Error<ErrorCode>> failed
                    ? failed.Error.Message
                    : string.Empty);
        return result.Value;
    }

    private static ConstraintProgrammingModel BuildModel(
        string name,
        out VariableId variableId,
        out ConstraintId constraintId,
        out IntervalId intervalId) {
        var model = new ConstraintProgrammingModel(
            name: name,
            objectCategory: ObjectCategory.Minimum);
        var variable = new IntVar($"{name}-x");
        variableId = new VariableId("variable:x");
        constraintId = new ConstraintId("lower");
        intervalId = new IntervalId("interval:work");
        IntegerDomain domain = IntegerDomain.CreateInterval(
            Int64.Zero,
            new Int64(3)).Value;
        var expression = new ConstraintProgrammingExpression.Variable(
            variable,
            domain,
            variableId);
        model.RegisterVariable(
            id: variableId,
            variable: variable,
            domain: domain,
            scope: ModelElementScope.Stable,
            origin: "fixture/x").IsOk.Should().BeTrue();
        model.AddConstraint(
            constraint: ConstraintProgrammingConstraint.CreateGreaterOrEqual(
                expression,
                Int64.One).Value,
            id: constraintId).IsOk.Should().BeTrue();
        IntervalVariable interval = IntervalVariable.CreateFixed(
            id: intervalId.Value,
            start: expression,
            size: Int64.One,
            end: ConstraintProgrammingExpression.Add(
                expression,
                ConstraintProgrammingExpression.CreateConstant(1)).Value).Value;
        model.RegisterInterval(interval).IsOk.Should().BeTrue();
        model.Minimize(
            expression: expression,
            id: new ObjectiveId("objective:main")).IsOk.Should().BeTrue();
        return model;
    }

    private static SolverDescriptor Descriptor() =>
        new(
            SolverId: "fixture.cp",
            BackendName: "fixture",
            Capabilities: new SolverCapabilities(
                new HashSet<SolverModelType> { SolverModelType.Cp },
                Checkpoint: true,
                Resume: true),
            BackendVersion: "1.0",
            PluginVersion: "1.0");

    private static PortableSolverProvenance Provenance() =>
        new(
            SolverId: "fixture.cp",
            BackendName: "fixture",
            BackendVersion: "1.0",
            PluginVersion: "1.0",
            RequestedConfiguration: new Dictionary<string, string> {
                ["threads"] = "2",
                ["seed"] = "1",
            },
            EffectiveConfiguration: new Dictionary<string, string> {
                ["threads"] = "2",
            },
            ThreadCount: 2,
            RandomSeed: 1,
            Deterministic: true,
            EnvironmentSummary: new Dictionary<string, string> {
                ["os"] = "windows",
            });
}
