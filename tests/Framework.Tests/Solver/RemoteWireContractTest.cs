#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Framework.Solver.Remote.Adapter;
using Fuookami.Ospf.Framework.Solver.Remote.Client;
using Fuookami.Ospf.Framework.Solver.Remote.Domain;
using Domain = Fuookami.Ospf.Framework.Solver.Remote.Domain;
using Fuookami.Ospf.Framework.Solver.Remote.Port;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Framework.Tests.Solver;

/// <summary>
/// 远程 wire 结果契约测试 / Remote wire result-contract tests.
/// </summary>
/// <remarks>
/// 直接读取固定 Kotlin 基线 `12fdb587` 的 fixtures 副本，验证精确整数、未知枚举容错和毫秒时长。
/// Reads the pinned Kotlin baseline `12fdb587` fixture copies and verifies exact integers, unknown-enum tolerance and millisecond durations.
/// </remarks>
public class RemoteWireContractTest {
    [Fact]
    public void CanonicalCpV2Fixture_ShouldDecodeWithExactInt64ObjectiveAndStatistics() {
        SerializedSolution solution = Decode("remote-cp-result-v2.json");

        solution.SchemaVersion.Should().Be("2.0");
        solution.ObjectiveValueInt64.Should().Be(9007199254740993L);
        solution.VariableValuesById["x"].Should().Be(9007199254740993L);
        solution.IntervalValues["job"].Should().Be(new SerializedIntervalValue(1L, 2L, 3L, true));
        solution.TerminationReason.Should().Be(RemoteTerminationReason.TimeLimit);
        solution.FingerprintSchemas["model"].Should().Be("1.0");
        solution.Statistics["bestBound"].Should().Be("0.5");
        solution.ClaimsExactOptimum.Should().BeFalse();
    }

    [Fact]
    public void CanonicalLinearV2Fixture_ShouldDecodeReportFields() {
        SerializedSolution solution = Decode("remote-linear-result-v2.json");

        solution.SchemaVersion.Should().Be("2.0");
        solution.ObjectiveValue.Should().Be(12.5);
        solution.Gap.Should().Be(0.2);
        solution.VariableValues.Should().Equal(2.0, 3.0);
        solution.SolutionPresence.Should().Be(RemoteSolutionPresence.Incumbent);
        solution.ProofStatus.Should().Be(RemoteProofStatus.Claimed);
        solution.TerminationReason.Should().Be(RemoteTerminationReason.TimeLimit);
        solution.FingerprintSchemas["solver"].Should().Be("2.0");
        solution.RunId.Should().Be("run-linear-1");
        solution.AttemptId.Should().Be("attempt-linear-1");
        solution.ElapsedMs.Should().Be(23);
    }

    [Fact]
    public void LegacyV1Fixture_ShouldDecodeWithBaselineDefaults() {
        SerializedSolution solution = Decode("remote-cp-result-v1.json");

        solution.SchemaVersion.Should().Be("1.0");
        solution.Feasible.Should().BeTrue();
        solution.Optimal.Should().BeTrue();
        solution.ObjectiveValue.Should().Be(12.5);
        solution.ElapsedMs.Should().Be(7);
        solution.SolverStatus.Should().Be("optimal");
        solution.ProblemStatus.Should().BeNull();
        solution.ProofStatus.Should().BeNull();
        solution.ClaimsExactOptimum.Should().BeFalse();
    }

    [Fact]
    public void UnknownEnumValues_ShouldDecodeToUnknownWithoutThrowing() {
        const string json = """
        {
          "feasible": true,
          "optimal": false,
          "terminationReason": "VENDOR_SPECIFIC_STOP",
          "solutionPresence": "SOMETHING_NEW",
          "proofStatus": "PARTIAL",
          "problemStatus": "WARMING_UP"
        }
        """;

        SerializedSolution solution = SerializedSolution.Decode(json).Value;

        solution.TerminationReason.Should().Be(RemoteTerminationReason.Unknown);
        solution.SolutionPresence.Should().Be(RemoteSolutionPresence.Unknown);
        solution.ProofStatus.Should().Be(RemoteProofStatus.Unknown);
        solution.ProblemStatus.Should().Be(RemoteProblemStatus.Unknown);
        solution.ClaimsExactOptimum.Should().BeFalse();
    }

    [Fact]
    public void ExactInt64_ShouldSurviveRoundTripWithoutDoubleLoss() {
        var solution = new SerializedSolution {
            Feasible = true,
            Optimal = false,
            ObjectiveValueInt64 = 9007199254740993L,
            VariableValuesById = new Dictionary<string, long> { ["x"] = 9007199254740993L },
            IntervalValues = new Dictionary<string, SerializedIntervalValue> {
                ["job"] = new(Start: 1L, Size: 2L, End: 3L),
            },
            SolutionPresence = RemoteSolutionPresence.Incumbent,
            ProofStatus = RemoteProofStatus.Claimed,
            TerminationReason = RemoteTerminationReason.TimeLimit,
            ElapsedMs = 17,
            SchemaVersion = "2.0",
        };

        string encoded = solution.Encode();
        SerializedSolution decoded = SerializedSolution.Decode(encoded).Value;

        decoded.ObjectiveValueInt64.Should().Be(9007199254740993L);
        decoded.VariableValuesById["x"].Should().Be(9007199254740993L);
        decoded.IntervalValues["job"].Should().Be(new SerializedIntervalValue(1L, 2L, 3L, true));
        decoded.ElapsedMs.Should().Be(17);
        decoded.ObjectiveValue.Should().BeNull();
    }

    [Fact]
    public void ExactOptimum_ShouldRequireOptimalFlagFeasibilityAndNonNoneProof() {
        new SerializedSolution {
            Optimal = true,
            Feasible = true,
            SolutionPresence = RemoteSolutionPresence.Optimal,
            ProofStatus = RemoteProofStatus.Claimed,
        }.ClaimsExactOptimum.Should().BeTrue();

        new SerializedSolution {
            Optimal = true,
            Feasible = true,
            SolutionPresence = RemoteSolutionPresence.Optimal,
            ProofStatus = RemoteProofStatus.None,
        }.ClaimsExactOptimum.Should().BeFalse("NONE 证明不足以声明最优 / a NONE proof cannot claim optimality");

        new SerializedSolution {
            Optimal = true,
            Feasible = false,
            SolutionPresence = RemoteSolutionPresence.Optimal,
            ProofStatus = RemoteProofStatus.Verified,
        }.ClaimsExactOptimum.Should().BeFalse("不可行结果不得声明最优 / an infeasible result must not claim optimality");

        new SerializedSolution {
            Optimal = false,
            Feasible = true,
            SolutionPresence = RemoteSolutionPresence.Optimal,
            ProofStatus = RemoteProofStatus.Verified,
            Gap = 0.0,
        }.ClaimsExactOptimum.Should().BeFalse("optimal=false 时零间隙不算最优 / a zero gap with optimal=false is not optimal");

        new SerializedSolution {
            Optimal = true,
            Feasible = true,
            SolutionPresence = RemoteSolutionPresence.Optimal,
            ProofStatus = null,
        }.ClaimsExactOptimum.Should().BeFalse();
    }

    [Fact]
    public void InvalidJson_ShouldReturnStructuredFailureNotThrow() {
        SerializedSolution.Decode("{ \"feasible\": ").IsFailed.Should().BeTrue();
        SerializedSolution.Decode("null").IsFailed.Should().BeTrue();
    }

    [Fact]
    public async Task FetchFinalResult_ShouldReadArtifactThroughLatestResultRef() {
        using var transport = new StubTransport(
            statusCode: 200,
            body: """{"code":"OK","data":{"status":"COMPLETED","latestResultRef":{"path":"tenant/result/run-1"}}}""");
        var storage = new RecordingStorage {
            ObjectBytes = System.Text.Encoding.UTF8.GetBytes(
                File.ReadAllText(Path.Combine(FixtureRoot(), "remote-linear-result-v2.json"))),
        };
        var client = new RemoteSolverHttpClient(
            transport: transport.Send,
            baseUrl: "http://localhost",
            tenantId: new TenantId("tenant"),
            objectStoragePort: storage);
        var handle = new ExecutionHandle(
            HandleId: new HandleId("h"),
            TaskId: new TaskId("task"),
            SliceId: new SliceId("slice"),
            NodeId: new NodeId("node"),
            TenantId: new TenantId("tenant"));

        Result<SolveResult?, ErrorCode, Error<ErrorCode>> result =
            await client.FetchFinalResultAsync(handle);

        result.IsOk.Should().BeTrue();
        SolveResult solve = result.Value.Should().NotBeNull().And.BeOfType<SolveResult>().Which;
        solve.Feasible.Should().BeTrue();
        solve.Optimal.Should().BeFalse("INCUMBENT + CLAIMED 不是精确最优 / INCUMBENT + CLAIMED is not an exact optimum");
        solve.ObjectiveValue.Should().Be(new Flt64(12.5));
        storage.ReadPath!.Path.Value.Should().Be("tenant/result/run-1",
            "artifact 必须经 latestResultRef 从对象存储取回 / the artifact comes from the store via latestResultRef");
        solve.Elapsed.Should().Be(TimeSpan.FromMilliseconds(23));
    }

    [Fact]
    public async Task ExportCheckpoint_ShouldReadLatestCheckpointRefFromTaskView() {
        using var transport = new StubTransport(
            statusCode: 200,
            body: """{"code":"OK","message":"ok","data":{"status":"RUNNING","latestCheckpointRef":{"path":"checkpoints/task-1/slice-1","version":"v1"}}}""");
        var client = new RemoteSolverHttpClient(
            transport: transport.Send,
            baseUrl: "http://localhost",
            tenantId: new TenantId("tenant"));
        var handle = new ExecutionHandle(
            HandleId: new HandleId("h"),
            TaskId: new TaskId("task"),
            SliceId: new SliceId("slice"),
            NodeId: new NodeId("node"),
            TenantId: new TenantId("tenant"));

        Result<ObjectRef?, ErrorCode, Error<ErrorCode>> result =
            await client.ExportCheckpointAsync(handle);

        result.IsOk.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.Path.Value.Should().Be("checkpoints/task-1/slice-1");
        result.Value.Version!.Value.Value.Should().Be("v1");
    }

    [Fact]
    public async Task EmptyResultBody_ShouldStayNullRatherThanFabricateSuccess() {
        using var transport = new StubTransport(statusCode: 200, body: "");
        var client = new RemoteSolverHttpClient(
            transport: transport.Send,
            baseUrl: "http://localhost",
            tenantId: new TenantId("tenant"));
        var handle = new ExecutionHandle(
            HandleId: new HandleId("h"),
            TaskId: new TaskId("task"),
            SliceId: new SliceId("slice"),
            NodeId: new NodeId("node"),
            TenantId: new TenantId("tenant"));

        Result<SolveResult?, ErrorCode, Error<ErrorCode>> result =
            await client.FetchFinalResultAsync(handle);

        result.IsOk.Should().BeTrue();
        result.Value.Should().BeNull();
    }

    [Fact]
    public async Task ProbeCapabilities_ShouldCallV1EndpointAndDecodeEnvelope() {
        using var transport = new StubTransport(
            statusCode: 200,
            body: """
            {
              "code": "OK",
              "message": "success",
              "data": {
                "schemaVersion": "1.0",
                "protocolVersions": ["2.0"],
                "supportedModelTypes": ["CP", "LINEAR"],
                "supportsPortableCheckpoint": true,
                "supportsNativeCheckpoint": false
              }
            }
            """);
        var client = new RemoteSolverHttpClient(
            transport: transport.Send,
            baseUrl: "http://localhost/",
            tenantId: new TenantId("tenant"));

        Result<RemoteSolverCapabilities, ErrorCode, Error<ErrorCode>> result =
            await client.ProbeCapabilitiesAsync();

        result.IsOk.Should().BeTrue();
        RemoteSolverCapabilities capabilities = result.Value;
        capabilities.SchemaVersion.Should().Be("1.0");
        capabilities.ProtocolVersions.Should().BeEquivalentTo(new[] { "2.0" });
        capabilities.SupportedModelTypes.Should().BeEquivalentTo(new[] { "CP", "LINEAR" });
        capabilities.SupportsPortableCheckpoint.Should().BeTrue();
        capabilities.SupportsNativeCheckpoint.Should().BeFalse();
        transport.LastUrl.Should().Be("http://localhost/api/v1/capabilities");
        transport.LastMethod.Should().Be("GET");
    }

    [Fact]
    public async Task ProbeCapabilities_WhenEnvelopeCodeIsNotOk_ShouldFail() {
        using var transport = new StubTransport(
            statusCode: 200,
            body: """{"code":"INTERNAL_ERROR","message":"boom","data":null}""");
        var client = new RemoteSolverHttpClient(
            transport: transport.Send,
            baseUrl: "http://localhost",
            tenantId: new TenantId("tenant"));

        (await client.ProbeCapabilitiesAsync()).IsFailed.Should().BeTrue();
    }

    [Fact]
    public void SchedulingDurations_ShouldEncodeAsIntegerMilliseconds() {
        var decision = new SchedulingDecision(
            DispatchId: new DispatchId("dispatch-1"),
            TaskId: new TaskId("task-1"),
            SliceId: new SliceId("slice-1"),
            NodeId: new NodeId("node-1"),
            QuantumMs: 2000,
            QueueWaitMs: 125,
            PreemptionMode: PreemptionMode.ControlledReturn,
            ResumeMode: ResumeMode.WarmStart,
            Outcome: SliceOutcome.Checkpointed);

        string json = JsonSerializer.Serialize(decision, RemoteWireJson.Options);

        json.Should().Contain("\"quantumMs\":2000");
        json.Should().Contain("\"queueWaitMs\":125");
        json.Should().Contain("\"preemptionMode\":\"CONTROLLED_RETURN\"");
        json.Should().Contain("\"resumeMode\":\"WARM_START\"");
        json.Should().Contain("\"outcome\":\"CHECKPOINTED\"");

        SchedulingDecision decoded =
            JsonSerializer.Deserialize<SchedulingDecision>(json, RemoteWireJson.Options)!;
        decoded.QuantumMs.Should().Be(2000);
        decoded.QueueWaitMs.Should().Be(125);
        decoded.PreemptionMode.Should().Be(PreemptionMode.ControlledReturn);
        decoded.ResumeMode.Should().Be(ResumeMode.WarmStart);
        decoded.Outcome.Should().Be(SliceOutcome.Checkpointed);
    }

    [Fact]
    public void SchedulingEnums_ShouldMatchBaselineMembers() {
        RemoteWireEnums.ParsePreemptionMode("NON_PREEMPTIBLE").Should().Be(PreemptionMode.NonPreemptible);
        RemoteWireEnums.ParsePreemptionMode("NATIVE").Should().Be(PreemptionMode.Native);
        RemoteWireEnums.ParseResumeMode("NONE").Should().Be(ResumeMode.None);
        RemoteWireEnums.ParseResumeMode("BASIS").Should().Be(ResumeMode.Basis);
        RemoteWireEnums.ParseResumeMode("NATIVE_CHECKPOINT").Should().Be(ResumeMode.NativeCheckpoint);
        RemoteWireEnums.ParseSliceOutcome("RESUMABLE").Should().Be(SliceOutcome.Resumable);
        RemoteWireEnums.ParseSliceOutcome("CANCELLED").Should().Be(SliceOutcome.Cancelled);

        RemoteWireEnums.ParsePreemptionMode("IMMEDIATE").Should().Be(PreemptionMode.Unknown,
            "基线没有 IMMEDIATE 成员 / the baseline has no IMMEDIATE member");
        RemoteWireEnums.ParseResumeMode("STRICT_CHECKPOINT").Should().Be(ResumeMode.Unknown,
            "STRICT_CHECKPOINT 是 HTTP 恢复模式而非 ResumeMode / STRICT_CHECKPOINT is an HTTP resume mode, not a ResumeMode");
    }

    [Fact]
    public async Task StartAsync_WithCpPayload_ShouldRejectWhenCapabilityNotAdvertised() {
        using var transport = new StubTransport(
            statusCode: 200,
            body: """
            {
              "code": "OK",
              "message": "success",
              "data": {
                "schemaVersion": "1.0",
                "protocolVersions": ["2.0"],
                "supportedModelTypes": ["LINEAR"],
                "supportsPortableCheckpoint": true,
                "supportsNativeCheckpoint": false
              }
            }
            """);
        var client = new RemoteSolverHttpClient(
            transport: transport.Send,
            baseUrl: "http://localhost",
            tenantId: new TenantId("tenant"));
        var payload = new SolvePayload(
            ModelData: new ModelData(Format: "ospf-cp-snapshot-json"));

        Result<ExecutionHandle, ErrorCode, Error<ErrorCode>> result = await client.StartAsync(
            payload: payload,
            taskId: new TaskId("task"),
            sliceId: new SliceId("slice"),
            nodeId: new NodeId("node"),
            tenantId: new TenantId("tenant"));

        result.IsFailed.Should().BeTrue();
        result.Should()
            .BeOfType<Failed<ExecutionHandle, ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.IllegalArgument);
        transport.RequestCount.Should().Be(1, "能力不符时不得继续上传载荷 / no payload upload after a capability mismatch");
    }

    [Fact]
    public async Task StartAsync_WithoutObjectStoragePort_ShouldFailInsteadOfFabricatingPath() {
        using var transport = new SequencedTransport(
            new RemoteSolverHttpResponse(
                200,
                """{"code":"OK","message":"ok","data":{"taskId":"task","accepted":true,"status":"QUEUED","message":"ok"}}"""));
        var client = new RemoteSolverHttpClient(
            transport: transport.Send,
            baseUrl: "http://localhost",
            tenantId: new TenantId("tenant"));

        Result<ExecutionHandle, ErrorCode, Error<ErrorCode>> result = await client.StartAsync(
            payload: new SolvePayload(ModelData: new ModelData(Format: "x")),
            taskId: new TaskId("task"),
            sliceId: new SliceId("slice"),
            nodeId: new NodeId("node"),
            tenantId: new TenantId("tenant"));

        result.IsFailed.Should().BeTrue();
        result.Should()
            .BeOfType<Failed<ExecutionHandle, ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.IllegalArgument);
        transport.RequestCount.Should().Be(0, "没有存储端口时不得发出提交 / no submission without a storage port");
    }

    [Fact]
    public async Task StartAsync_ShouldUploadPayloadBeforeSubmittingItsReference() {
        using var transport = new SequencedTransport(
            new RemoteSolverHttpResponse(
                200,
                """{"code":"OK","message":"ok","data":{"taskId":"task","accepted":true,"status":"QUEUED","message":"ok"}}"""));
        var storage = new RecordingStorage();
        var client = new RemoteSolverHttpClient(
            transport: transport.Send,
            baseUrl: "http://localhost",
            tenantId: new TenantId("tenant"),
            objectStoragePort: storage);

        Result<ExecutionHandle, ErrorCode, Error<ErrorCode>> result = await client.StartAsync(
            payload: new SolvePayload(ModelData: new ModelData(Format: "x")),
            taskId: new TaskId("task"),
            sliceId: new SliceId("slice"),
            nodeId: new NodeId("node"),
            tenantId: new TenantId("tenant"));

        result.IsOk.Should().BeTrue();
        storage.UploadedBytes.Should().NotBeNull();
        storage.UploadedPath!.Path.Value.Should().Be("tenant/payload/task/slice");
        storage.UploadedBytes!.Length.Should().BeGreaterThan(0);
        transport.LastUrl.Should().Be("http://localhost/api/v1/tasks");
        transport.LastBody.Should().Contain("\"payloadRef\":\"tenant/payload/task/slice\"");
    }

    [Fact]
    public async Task StartAsync_WhenStorageFails_ShouldNotSubmit() {
        using var transport = new SequencedTransport(
            new RemoteSolverHttpResponse(
                200,
                """{"code":"OK","message":"ok","data":{"taskId":"task","accepted":true,"status":"QUEUED","message":"ok"}}"""));
        var storage = new RecordingStorage { Fail = true };
        var client = new RemoteSolverHttpClient(
            transport: transport.Send,
            baseUrl: "http://localhost",
            tenantId: new TenantId("tenant"),
            objectStoragePort: storage);

        Result<ExecutionHandle, ErrorCode, Error<ErrorCode>> result = await client.StartAsync(
            payload: new SolvePayload(ModelData: new ModelData(Format: "x")),
            taskId: new TaskId("task"),
            sliceId: new SliceId("slice"),
            nodeId: new NodeId("node"),
            tenantId: new TenantId("tenant"));

        result.IsFailed.Should().BeTrue("存储失败时不得提交 / a storage failure must not be submitted");
        transport.RequestCount.Should().Be(0);
    }

    [Fact]
    public async Task StartAsync_WithCpPayload_ShouldSubmitWhenCapabilityAdvertised() {
        using var transport = new SequencedTransport(
            new RemoteSolverHttpResponse(200, """
            {
              "code": "OK",
              "message": "success",
              "data": {
                "schemaVersion": "1.0",
                "protocolVersions": ["2.0"],
                "supportedModelTypes": ["CP", "LINEAR"],
                "supportsPortableCheckpoint": true,
                "supportsNativeCheckpoint": false
              }
            }
            """),
            new RemoteSolverHttpResponse(
                200,
                """{"code":"OK","message":"accepted","data":{"taskId":"task","accepted":true,"status":"QUEUED","message":"accepted"}}"""));
        var client = new RemoteSolverHttpClient(
            transport: transport.Send,
            baseUrl: "http://localhost",
            tenantId: new TenantId("tenant"),
            objectStoragePort: new RecordingStorage());
        var payload = new SolvePayload(
            ModelData: new ModelData(Format: "ospf-cp-snapshot-json"));

        Result<ExecutionHandle, ErrorCode, Error<ErrorCode>> result = await client.StartAsync(
            payload: payload,
            taskId: new TaskId("task"),
            sliceId: new SliceId("slice"),
            nodeId: new NodeId("node"),
            tenantId: new TenantId("tenant"));

        result.IsOk.Should().BeTrue();
        result.Value.TaskId.Value.Should().Be("task");
        transport.RequestCount.Should().Be(2, "能力探测后仍需提交 / a probe is followed by a submission");
    }

    [Fact]
    public void TransportPluginRegistry_ShouldResolveDefaultAndRejectUnknown() {
        RemoteSolverHttpTransportPlugins.Names().Should().Contain("jdk");
        RemoteSolverHttpTransportPlugins.Default().Name.Should().Be("jdk");

        Result<IRemoteSolverHttpTransportPlugin, ErrorCode, Error<ErrorCode>> resolved =
            RemoteSolverHttpTransportPlugins.Resolve("  JDK  ");
        resolved.IsOk.Should().BeTrue();
        resolved.Value.Name.Should().Be("jdk");

        Result<IRemoteSolverHttpTransportPlugin, ErrorCode, Error<ErrorCode>> missing =
            RemoteSolverHttpTransportPlugins.Resolve("not-registered");
        missing.Should()
            .BeOfType<Failed<IRemoteSolverHttpTransportPlugin, ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.SolverNotFound,
                "未注册插件不得静默回退到默认插件 / an unregistered plugin must not fall back silently");
    }

    [Fact]
    public void TransportPluginRegistry_ShouldRejectBlankNames() {
        var plugin = new NamedPlugin("   ");
        RemoteSolverHttpTransportPlugins.Register(plugin).IsFailed.Should().BeTrue();
    }

    [Fact]
    public void TransportConfig_ShouldRejectNegativeTimeouts() {
        Result<RemoteSolverHttpTransport, ErrorCode, Error<ErrorCode>> result =
            RemoteSolverHttpTransportPlugins.Create(
                name: "jdk",
                config: new RemoteSolverHttpTransportConfig(ConnectTimeoutMs: -1));

        result.Should()
            .BeOfType<Failed<RemoteSolverHttpTransport, ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.IllegalArgument);
    }

    [Fact]
    public async Task ResumeAsync_InStrictMode_ShouldRejectWithoutCallingTransport() {
        using var transport = new StubTransport(statusCode: 200, body: """{"code":"OK","data":{}}""");
        var client = new RemoteSolverHttpClient(
            transport: transport.Send,
            baseUrl: "http://localhost",
            tenantId: new TenantId("tenant"),
            resumeMode: RemoteSolverHttpResumeMode.StrictCheckpoint);

        Result<ExecutionHandle, ErrorCode, Error<ErrorCode>> result = await client.ResumeAsync(
            payload: new SolvePayload(ModelData: new ModelData(Format: "ospf-cp-snapshot-json")),
            snapshot: new ObjectRef(Path: new ObjectPath("t/checkpoint/task/cp1")),
            taskId: new TaskId("task"),
            sliceId: new SliceId("slice"),
            nodeId: new NodeId("node"),
            tenantId: new TenantId("tenant"));

        result.IsFailed.Should().BeTrue();
        result.Should()
            .BeOfType<Failed<ExecutionHandle, ErrorCode, Error<ErrorCode>>>()
            .Which.Code.Should().Be(ErrorCode.IllegalArgument);
        transport.RequestCount.Should().Be(0,
            "严格模式必须在上传前拒绝 / strict mode must reject before any request");
    }

    [Fact]
    public async Task StopAsync_WhenStatusNotTerminal_ShouldReportFalseInsteadOfClaimingSuccess() {
        using var transport = new SequencedTransport(
            new RemoteSolverHttpResponse(200, """{"code":"OK","data":{"status":"STOPPING","accepted":true}}"""),
            new RemoteSolverHttpResponse(200, """{"code":"OK","data":{"status":"RUNNING"}}"""));
        var client = new RemoteSolverHttpClient(
            transport: transport.Send,
            baseUrl: "http://localhost",
            tenantId: new TenantId("tenant"));

        Result<bool, ErrorCode, Error<ErrorCode>> result = await client.StopAsync(Handle());

        result.IsOk.Should().BeTrue();
        result.Value.Should().BeFalse("未确认终态不得谎报停止成功 / an unconfirmed stop must not report success");
    }

    [Fact]
    public async Task StopAsync_WhenTerminalStopped_ShouldReportTrue() {
        using var transport = new SequencedTransport(
            new RemoteSolverHttpResponse(200, """{"code":"OK","data":{"status":"STOPPING","accepted":true}}"""),
            new RemoteSolverHttpResponse(200, """{"code":"OK","data":{"status":"STOPPED"}}"""));
        var client = new RemoteSolverHttpClient(
            transport: transport.Send,
            baseUrl: "http://localhost",
            tenantId: new TenantId("tenant"));

        Result<bool, ErrorCode, Error<ErrorCode>> result = await client.StopAsync(Handle());

        result.IsOk.Should().BeTrue();
        result.Value.Should().BeTrue();
    }

    [Fact]
    public async Task StopAsync_WhenAlreadyCompleted_ShouldSucceedIdempotently() {
        using var transport = new SequencedTransport(
            new RemoteSolverHttpResponse(200, """{"code":"OK","data":{"status":"COMPLETED","accepted":true}}"""));
        var client = new RemoteSolverHttpClient(
            transport: transport.Send,
            baseUrl: "http://localhost",
            tenantId: new TenantId("tenant"));

        Result<bool, ErrorCode, Error<ErrorCode>> result = await client.StopAsync(Handle());

        result.IsOk.Should().BeTrue();
        result.Value.Should().BeTrue();
        transport.RequestCount.Should().Be(1, "已终态时不需要额外轮询 / no extra polling when already terminal");
    }

    [Fact]
    public void LinearQuadraticValidation_ShouldFollowBaselineVersionRules() {
        var legacy = new SerializedSolution {
            Feasible = true,
            Optimal = false,
            ObjectiveValue = 12.5,
            SchemaVersion = "1.0",
        };
        RemoteReportMapping.ValidateLinearQuadraticResult(legacy).IsOk.Should().BeTrue(
            "v1 保持旧生产端可读 / v1 stays readable for legacy producers");

        var future = new SerializedSolution {
            Feasible = true,
            Optimal = false,
            ObjectiveValue = 1.0,
            SchemaVersion = "3.0",
        };
        RemoteReportMapping.ValidateLinearQuadraticResult(future).IsFailed.Should().BeTrue();

        var strictMissingObjective = new SerializedSolution {
            Feasible = true,
            Optimal = false,
            SchemaVersion = "2.0",
            RunId = "run",
            AttemptId = "attempt",
        };
        RemoteReportMapping.ValidateLinearQuadraticResult(strictMissingObjective).IsFailed.Should().BeTrue(
            "严格可行结果必须带目标值 / a strict feasible result needs an objective");

        var bothObjectives = new SerializedSolution {
            Feasible = true,
            Optimal = false,
            ObjectiveValue = 1.0,
            ObjectiveValueInt64 = 2L,
            SchemaVersion = "1.0",
        };
        RemoteReportMapping.ValidateLinearQuadraticResult(bothObjectives).IsFailed.Should().BeTrue();

        var int64OnLinear = new SerializedSolution {
            Feasible = true,
            Optimal = false,
            ObjectiveValueInt64 = 9007199254740993L,
            SchemaVersion = "1.0",
        };
        RemoteReportMapping.ValidateLinearQuadraticResult(int64OnLinear).IsFailed.Should().BeTrue(
            "线性/二次结果不得使用 Int64 目标 / linear/quadratic results must not use the Int64 field");

        var infeasibleWithObjective = new SerializedSolution {
            Feasible = false,
            Optimal = false,
            ObjectiveValue = 1.0,
            SchemaVersion = "1.0",
        };
        RemoteReportMapping.ValidateLinearQuadraticResult(infeasibleWithObjective).IsFailed.Should().BeTrue();

        var mismatchedPresence = new SerializedSolution {
            Feasible = true,
            Optimal = false,
            SolutionPresence = RemoteSolutionPresence.Optimal,
            SchemaVersion = "1.0",
        };
        RemoteReportMapping.ValidateLinearQuadraticResult(mismatchedPresence).IsFailed.Should().BeTrue();
    }

    [Fact]
    public void LinearV2Fixture_ShouldPassStrictValidation() {
        SerializedSolution solution = Decode("remote-linear-result-v2.json");

        RemoteReportMapping.ValidateLinearQuadraticResult(solution).IsOk.Should().BeTrue();
        RemoteReportMapping.ToSolveResult(solution: solution, checkpointRef: null)
            .Optimal.Should().BeFalse();
    }

    [Fact]
    public void PortableCheckpointCodec_ShouldForwardToCoreAndSplitMigration() {
        const string json = """{"schemaVersion":"3.0","kind":"cp"}""";

        PortableCheckpointCodec.DecodeOrNull(json).Should().BeNull(
            "当前 schema 解码不接受非 envelope 内容 / current-schema decode rejects non-envelope content");
        PortableCheckpointCodec.DecodeCompatibleOrNull(json).Should().BeNull();

        PortableCheckpointCodec.Sha256("abc").Should().Be(
            "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
            "摘要必须与标准 SHA-256 一致 / the digest must match standard SHA-256");
    }

    [Fact]
    public void PortableCheckpointCodec_ShouldThrowOnUnencodableEnvelope() {
        var unencodable = new Fuookami.Ospf.Core.Solver.ConstraintProgramming.ConstraintProgrammingCheckpointEnvelope(
            SchemaVersion: "9.9",
            SourceFormat: "cp",
            MigratedFromLegacy: false,
            CheckpointId: "cp-1",
            IdentitySchemaVersion: "1.0",
            IdentityNamespace: "ns",
            ModelName: "model",
            ModelFingerprint: "model-fingerprint",
            ConfigurationFingerprint: null,
            SolverFingerprint: null,
            RunId: null,
            AttemptId: null,
            ParentCheckpointId: null,
            CreatedAtEpochMs: 0L,
            SnapshotJson: "{}",
            Incumbent: null,
            BestBound: null,
            Gap: null,
            Assumptions: null,
            Conflicts: null,
            Benders: null,
            CancellationChain: null,
            Provenance: null,
            Metadata: null,
            IntegritySha256: "");

        Action act = () => PortableCheckpointCodec.Encode(unencodable);

        act.Should().Throw<ArgumentException>(
            "无法编码时必须显式抛出而不是产出不可解码文档 / an unencodable envelope must throw rather than produce an undecodable document");
    }

    /// <summary>
    /// 该 fixture 由固定 Kotlin 12fdb587 侧真实编码产出（实测）。
    /// This fixture was produced by a real encode on the pinned Kotlin 12fdb587 side (measured this session).
    /// </summary>
    [Fact]
    public void KotlinEncodedSolution_ShouldDecodeAndPassValidation() {
        SerializedSolution solution = Decode("solution-kotlin-encoded-v2.json");

        solution.ObjectiveValue.Should().Be(12.5);
        solution.VariableValuesById["x"].Should().Be(7L);
        solution.ElapsedMs.Should().Be(17);
        solution.TerminationReason.Should().Be(RemoteTerminationReason.Completed);
        solution.SolutionPresence.Should().Be(RemoteSolutionPresence.Optimal);
        solution.Fingerprints.Should().ContainKey("model");
        RemoteReportMapping.ValidateLinearQuadraticResult(solution).IsOk.Should().BeTrue();
    }

    [Fact]
    public void CSharpEncodedSolution_ShouldNotEmitComputedProperties() {
        var solution = new SerializedSolution {
            Feasible = true,
            Optimal = false,
            ObjectiveValue = 1.5,
            SchemaVersion = "2.0",
            RunId = "run",
            AttemptId = "attempt",
        };

        string json = solution.Encode();

        // 固定 Kotlin 侧使用 ignoreUnknownKeys = false，计算属性一旦进入 wire 就会被拒绝。
        // The pinned Kotlin side uses ignoreUnknownKeys = false, so a computed property on the wire is rejected.
        json.Should().NotContain("claimsExactOptimum");
        json.Should().NotContain("\"present\":false");
    }

    [Fact]
    public void ModelData_ShouldNotEmitComputedProperties() {
        string json = JsonSerializer.Serialize(
            ModelData.Reference(new ObjectRef(Path: new ObjectPath("t/model/m"))),
            RemoteWireJson.Options);

        json.Should().NotContain("isReference");
        json.Should().NotContain("isInline");
        json.Should().NotContain("modelType");
    }

    [Fact]
    public void RoundTrip_ShouldPreserveInt64ObjectiveExactly() {
        const long exact = 9007199254740993L;
        var solution = new SerializedSolution {
            Feasible = true,
            Optimal = false,
            ObjectiveValueInt64 = exact,
            VariableValuesById = new System.Collections.Generic.Dictionary<string, long> { ["x"] = exact },
            SchemaVersion = "1.0",
        };

        SerializedSolution decoded = SerializedSolution.Decode(solution.Encode()).Value;

        decoded.ObjectiveValueInt64.Should().Be(exact, "Int64 目标必须逐位保真 / the Int64 objective must be bit-exact");
        decoded.ObjectiveValue.Should().BeNull("不得退化为浮点 / it must not degrade to a double");
        decoded.VariableValuesById["x"].Should().Be(exact);
    }

    [Fact]
    public async Task StartAsync_WhenSubmissionRejected_ShouldNotProduceHandle() {
        using var transport = new SequencedTransport(
            new RemoteSolverHttpResponse(
                200,
                """{"code":"OK","message":"x","data":{"taskId":"task","accepted":false,"status":"FAILED","message":"budget exhausted"}}"""));
        var client = new RemoteSolverHttpClient(
            transport: transport.Send,
            baseUrl: "http://localhost",
            tenantId: new TenantId("tenant"),
            objectStoragePort: new RecordingStorage());

        Result<ExecutionHandle, ErrorCode, Error<ErrorCode>> result = await client.StartAsync(
            payload: new SolvePayload(ModelData: new ModelData(Format: "x")),
            taskId: new TaskId("task"),
            sliceId: new SliceId("slice"),
            nodeId: new NodeId("node"),
            tenantId: new TenantId("tenant"));

        result.IsFailed.Should().BeTrue("未受理的提交不得产出句柄 / a rejected submission must not produce a handle");
        result.Should()
            .BeOfType<Failed<ExecutionHandle, ErrorCode, Error<ErrorCode>>>()
            .Which.Error.Message.Should().Contain("budget exhausted");
    }

    [Fact]
    public void TaskSubmitRequest_ShouldCarrySchedulingFieldsInBaselineShape() {
        var payload = new SolvePayload(
            ModelData: new ModelData(Format: "x"),
            Scheduling: new SchedulingRequest(
                Complexity: TaskComplexity.Complex,
                TimeSensitivity: TimeSensitivity.Realtime,
                Priority: 5,
                DeadlineEpochMs: 1700000000000L,
                BudgetScope: new BudgetScopeId("budget-a"),
                BudgetLimit: 42.5));

        var request = RemoteTaskSubmitRequest.FromPayload(
            payloadRef: new ObjectPath("t/payload/task/slice"),
            payload: payload,
            requestId: new RequestId("req-1"),
            tenantId: new TenantId("tenant"));

        string json = JsonSerializer.Serialize(request, RemoteWireJson.Options);

        json.Should().Contain("\"deadlineEpochMs\":1700000000000");
        json.Should().Contain("\"complexity\":\"COMPLEX\"");
        json.Should().Contain("\"timeSensitivity\":\"REALTIME\"");
        json.Should().Contain("\"priority\":5");
        json.Should().Contain("\"budgetScope\":\"budget-a\"");
        json.Should().Contain("\"budgetLimit\":42.5");
        json.Should().Contain("\"requestId\":\"req-1\"");
        json.Should().NotContain("\"deadline\":", "截止时间只能以毫秒整型出现 / the deadline must appear only as integer milliseconds");
    }

    [Fact]
    public async Task AwaitSliceEndAsync_ShouldPollTheCanonicalTaskEndpoint() {
        using var transport = new StubTransport(
            statusCode: 200,
            body: """{"code":"OK","data":{"status":"SUSPENDED"}}""");
        var client = new RemoteSolverHttpClient(
            transport: transport.Send,
            baseUrl: "http://localhost",
            tenantId: new TenantId("tenant"));

        await client.AwaitSliceEndAsync(Handle(), TimeSpan.FromMilliseconds(1500.7));

        transport.LastUrl.Should().Be("http://localhost/api/v1/tasks/task",
            "轮询走规范任务端点 / polling uses the canonical task endpoint");
        transport.LastUrl.Should().NotContain("quantum",
            "基线请求不带 quantum / the baseline request carries no quantum");
    }

    /// <summary>
    /// 该 fixture 已被固定 Kotlin 12fdb587 侧用 <c>ignoreUnknownKeys = false</c> 严格解码成功（实测）。
    /// This fixture was decoded successfully by the pinned Kotlin 12fdb587 side with
    /// <c>ignoreUnknownKeys = false</c> (measured this session).
    /// </summary>
    [Fact]
    public void SubmitRequestFixture_ShouldMatchBaselineBareStringIdentifiers() {
        string json = File.ReadAllText(
            Path.Combine(FixtureRoot(), "submit-request-csharp.json"));

        // 标识一律是裸字符串，不能是 {"value": "..."}；Kotlin value class 就是这样序列化的。
        // Identifiers are bare strings, never {"value": "..."}; that is how Kotlin value classes serialize.
        json.Should().Contain("\"payloadRef\":\"t/payload/task/slice\"");
        json.Should().Contain("\"requestId\":\"req-1\"");
        json.Should().Contain("\"tenantId\":\"tenant\"");
        json.Should().Contain("\"budgetScope\":\"budget-a\"");
        json.Should().NotContain("\"value\":", "标识不得包装成对象 / identifiers must not be wrapped in objects");
        json.Should().Contain("\"deadlineEpochMs\":1700000000000");
    }

    [Fact]
    public void Identifiers_ShouldRoundTripAsBareStrings() {
        var request = new RemoteTaskSubmitRequest(
            PayloadRef: new ObjectPath("p"),
            RequestId: new RequestId("r"),
            TenantId: new TenantId("t"));

        string json = JsonSerializer.Serialize(request, RemoteWireJson.Options);
        RemoteTaskSubmitRequest decoded =
            JsonSerializer.Deserialize<RemoteTaskSubmitRequest>(json, RemoteWireJson.Options)!;

        decoded.PayloadRef.Value.Should().Be("p");
        decoded.RequestId!.Value.Value.Should().Be("r");
        decoded.TenantId!.Value.Value.Should().Be("t");
    }

    /// <summary>
    /// 最新检查点模式不再被模式门限拒绝，而是进入检查点身份校验。
    /// Latest-checkpoint mode is no longer rejected by the mode gate; it proceeds to checkpoint identity validation.
    /// </summary>
    [Fact]
    public async Task ResumeAsync_InLatestMode_ShouldProceedPastModeGate() {
        using var transport = new StubTransport(statusCode: 200, body: """{"code":"OK","data":{}}""");
        var client = new RemoteSolverHttpClient(
            transport: transport.Send,
            baseUrl: "http://localhost",
            tenantId: new TenantId("tenant"));

        Result<ExecutionHandle, ErrorCode, Error<ErrorCode>> result = await client.ResumeAsync(
            payload: ResumePayload(),
            snapshot: new ObjectRef(Path: new ObjectPath("tenant/checkpoint/task/attempt-1-cp1")),
            taskId: new TaskId("task"),
            sliceId: new SliceId("slice"),
            nodeId: new NodeId("node"),
            tenantId: new TenantId("tenant"));

        result.IsFailed.Should().BeTrue("无存储端口时仍应失败 / it still fails without a storage port");
        result.Should()
            .BeOfType<Failed<ExecutionHandle, ErrorCode, Error<ErrorCode>>>()
            .Which.Error.Message.Should().Contain("objectStoragePort",
                "失败原因应是缺少存储端口，而不是模式门限 / the cause must be the missing storage port, not the mode gate");
        transport.RequestCount.Should().Be(0);
    }

    [Fact]
    public async Task ResumeAsync_ShouldRejectCheckpointOutsideTenantTaskScope() {
        using var transport = new SequencedTransport(
            new RemoteSolverHttpResponse(200, """{"code":"OK","data":{}}"""));
        var client = new RemoteSolverHttpClient(
            transport: transport.Send,
            baseUrl: "http://localhost",
            tenantId: new TenantId("tenant"),
            objectStoragePort: new RecordingStorage());

        Result<ExecutionHandle, ErrorCode, Error<ErrorCode>> result = await client.ResumeAsync(
            payload: ResumePayload(),
            snapshot: new ObjectRef(Path: new ObjectPath("other/checkpoint/task/cp-1")),
            taskId: new TaskId("task"),
            sliceId: new SliceId("slice"),
            nodeId: new NodeId("node"),
            tenantId: new TenantId("tenant"));

        result.IsFailed.Should().BeTrue("跨租户检查点必须拒绝 / a cross-tenant checkpoint must be rejected");
        transport.RequestCount.Should().Be(0);
    }

    [Fact]
    public async Task ResumeAsync_ShouldRejectPathTraversalSegment() {
        using var transport = new SequencedTransport(
            new RemoteSolverHttpResponse(200, """{"code":"OK","data":{}}"""));
        var client = new RemoteSolverHttpClient(
            transport: transport.Send,
            baseUrl: "http://localhost",
            tenantId: new TenantId("tenant"),
            objectStoragePort: new RecordingStorage());

        Result<ExecutionHandle, ErrorCode, Error<ErrorCode>> result = await client.ResumeAsync(
            payload: ResumePayload(),
            snapshot: new ObjectRef(Path: new ObjectPath("tenant/checkpoint/task/..")),
            taskId: new TaskId("task"),
            sliceId: new SliceId("slice"),
            nodeId: new NodeId("node"),
            tenantId: new TenantId("tenant"));

        result.IsFailed.Should().BeTrue("路径穿越段必须拒绝 / a traversal segment must be rejected");
    }

    [Fact]
    public async Task ResumeAsync_WithoutObjectStoragePort_ShouldFailBeforeRequesting() {
        using var transport = new SequencedTransport(
            new RemoteSolverHttpResponse(200, """{"code":"OK","data":{}}"""));
        var client = new RemoteSolverHttpClient(
            transport: transport.Send,
            baseUrl: "http://localhost",
            tenantId: new TenantId("tenant"));

        Result<ExecutionHandle, ErrorCode, Error<ErrorCode>> result = await client.ResumeAsync(
            payload: ResumePayload(),
            snapshot: new ObjectRef(Path: new ObjectPath("tenant/checkpoint/task/cp-1")),
            taskId: new TaskId("task"),
            sliceId: new SliceId("slice"),
            nodeId: new NodeId("node"),
            tenantId: new TenantId("tenant"));

        result.IsFailed.Should().BeTrue("校验检查点必须有存储端口 / verifying a checkpoint needs a storage port");
        transport.RequestCount.Should().Be(0);
    }

    [Fact]
    public async Task ResumeAsync_ShouldRejectFingerprintMismatch() {
        using var transport = new SequencedTransport(
            new RemoteSolverHttpResponse(200, """{"code":"OK","data":{}}"""));
        var storage = new RecordingStorage {
            ObjectBytes = System.Text.Encoding.UTF8.GetBytes(
                """{"schemaVersion":"3.0","sourceFormat":"v2"}"""),
        };
        var client = new RemoteSolverHttpClient(
            transport: transport.Send,
            baseUrl: "http://localhost",
            tenantId: new TenantId("tenant"),
            objectStoragePort: storage);

        Result<ExecutionHandle, ErrorCode, Error<ErrorCode>> result = await client.ResumeAsync(
            payload: ResumePayload(),
            snapshot: new ObjectRef(Path: new ObjectPath("tenant/checkpoint/task/cp-1")),
            taskId: new TaskId("task"),
            sliceId: new SliceId("slice"),
            nodeId: new NodeId("node"),
            tenantId: new TenantId("tenant"));

        result.IsFailed.Should().BeTrue("非 v2 已验证 envelope 必须拒绝 / a non-v2 verified envelope must be rejected");
        transport.RequestCount.Should().Be(0);
    }

    [Fact]
    public void ActionIdentityMismatches_ShouldTreatMissingFieldsAsMismatch() {
        var envelope = new Fuookami.Ospf.Core.Solver.ConstraintProgramming.ConstraintProgrammingCheckpointEnvelope(
            SchemaVersion: "3.0",
            SourceFormat: "v2",
            MigratedFromLegacy: false,
            CheckpointId: "attempt-1-cp1",
            IdentitySchemaVersion: "1.0",
            IdentityNamespace: "ns",
            ModelName: "model",
            ModelFingerprint: "model-sha",
            ConfigurationFingerprint: "config-sha",
            SolverFingerprint: "solver-sha",
            RunId: "task",
            AttemptId: "attempt-1",
            ParentCheckpointId: null,
            CreatedAtEpochMs: 1L,
            SnapshotJson: "{}",
            Incumbent: null,
            BestBound: null,
            Gap: null,
            Assumptions: null,
            Conflicts: null,
            Benders: null,
            CancellationChain: null,
            Provenance: null,
            Metadata: null,
            IntegritySha256: "digest");

        var empty = new RemoteTaskActionView(TaskId: "task", TenantId: "tenant", Accepted: true, Status: "RUNNING");
        empty.IdentityMismatches("task", "tenant", envelope).Should().BeEquivalentTo(
            new[] { "runId", "attemptId", "modelFingerprint", "configurationFingerprint", "solverFingerprint" },
            "缺失字段必须算作不一致 / absent fields must count as mismatches");

        var matching = new RemoteTaskActionView(
            TaskId: "task",
            TenantId: "tenant",
            Accepted: true,
            Status: "RUNNING",
            RunId: "task",
            AttemptId: "attempt-1",
            ModelFingerprint: "model-sha",
            ConfigurationFingerprint: "config-sha",
            SolverFingerprint: "solver-sha");
        matching.IdentityMismatches("task", "tenant", envelope).Should().BeEmpty();

        RemoteTaskActionView wrongTenant = matching with { TenantId = "other" };
        wrongTenant.IdentityMismatches("task", "tenant", envelope).Should().ContainSingle().Which.Should().Be("tenantId");
    }

    [Fact]
    public void RemoteIssues_ShouldParseIndexedFieldsInNumericOrder() {
        var fields = new Dictionary<string, string> {
            ["warning.10.code"] = "W10",
            ["warning.10.category"] = "Numerical",
            ["warning.10.message"] = "ten",
            ["warning.2.code"] = "W2",
            ["warning.2.category"] = "Backend",
            ["warning.2.message"] = "two",
            ["warning.2.details.hint"] = "detail",
        };

        Result<IReadOnlyList<Fuookami.Ospf.Core.Solver.Report.SolveIssue>, ErrorCode, Error<ErrorCode>> issues =
            RemoteReportMapping.ToRemoteIssues(fields, "warning");

        issues.IsOk.Should().BeTrue();
        issues.Value.Should().HaveCount(2);
        issues.Value[0].Code.Should().Be("W2", "索引按数值升序 / indexes sort numerically");
        issues.Value[0].Category.Should().Be(Fuookami.Ospf.Core.Solver.Report.SolveIssueCategory.Backend);
        issues.Value[0].Details["hint"].Should().Be("detail");
        issues.Value[1].Code.Should().Be("W10");
    }

    [Fact]
    public void RemoteIssues_ShouldRejectMalformedOrIncompleteFields() {
        var missingCategory = new Dictionary<string, string> {
            ["error.0.code"] = "E0",
            ["error.0.message"] = "boom",
        };
        RemoteReportMapping.ToRemoteIssues(missingCategory, "error").IsFailed.Should().BeTrue();

        var badCategory = new Dictionary<string, string> {
            ["error.0.code"] = "E0",
            ["error.0.category"] = "NoSuchCategory",
            ["error.0.message"] = "boom",
        };
        RemoteReportMapping.ToRemoteIssues(badCategory, "error").IsFailed.Should().BeTrue();

        var malformed = new Dictionary<string, string> {
            ["error.0.code"] = "E0",
            ["error.0.category"] = "Backend",
            ["error.0.message"] = "boom",
            ["error.0.unknown"] = "x",
        };
        RemoteReportMapping.ToRemoteIssues(malformed, "error").IsFailed.Should().BeTrue(
            "畸形字段不得静默忽略 / malformed fields must not be silently ignored");
    }

    [Fact]
    public void RemoteDiagnostics_ShouldRejectUnknownPrefix() {
        var unknown = new Dictionary<string, string> { ["mystery.0.code"] = "X" };
        RemoteReportMapping.ToRemoteDiagnostics(unknown).IsFailed.Should().BeTrue();

        var valid = new Dictionary<string, string> {
            ["warning.0.code"] = "W0",
            ["warning.0.category"] = "Numerical",
            ["warning.0.message"] = "warn",
            ["error.0.code"] = "E0",
            ["error.0.category"] = "Protocol",
            ["error.0.message"] = "fail",
        };
        Result<Fuookami.Ospf.Core.Solver.Report.SolveDiagnostics<Flt64>, ErrorCode, Error<ErrorCode>> diagnostics =
            RemoteReportMapping.ToRemoteDiagnostics(valid);
        diagnostics.IsOk.Should().BeTrue();
        diagnostics.Value.Warnings.Should().ContainSingle();
        diagnostics.Value.Errors.Should().ContainSingle();
    }

    /// <summary>
    /// 该期望值由固定 Kotlin 12fdb587 侧真实计算并逐字比对通过（实测），输入含 `=`、换行、
    /// 反斜杠与非 ASCII，覆盖转义与排序边界。
    /// This expected value was produced by the pinned Kotlin 12fdb587 side and compared byte for byte
    /// (measured this session); the input contains `=`, a newline, a backslash and non-ASCII, covering
    /// escaping and ordering boundaries.
    /// </summary>
    [Fact]
    public void ConfigurationFingerprint_ShouldMatchPinnedKotlinBaseline() {
        string expected = File.ReadAllText(
            Path.Combine(FixtureRoot(), "configuration-digest-kotlin.txt")).Trim();
        string tricky = "a=b" + (char)10 + "c" + (char)92 + "d";
        var parameters = new Dictionary<string, string> {
            ["beta"] = "2",
            ["alpha"] = tricky,
            ["gamma"] = "线",
        };

        AuditFingerprint fingerprint = SolveFingerprinting.Configuration(parameters);

        $"{fingerprint.SchemaVersion}|{fingerprint.Algorithm}|{fingerprint.Value}".Should().Be(expected,
            "配置指纹必须与固定 Kotlin 基线逐字一致 / the configuration fingerprint must match the pinned Kotlin baseline exactly");
    }

    [Fact]
    public void ConfigurationFingerprint_ShouldBeOrderIndependentAndEscapeDelimiters() {
        AuditFingerprint first = SolveFingerprinting.Configuration(
            new Dictionary<string, string> { ["alpha"] = "1", ["beta"] = "2" });
        AuditFingerprint reordered = SolveFingerprinting.Configuration(
            new Dictionary<string, string> { ["beta"] = "2", ["alpha"] = "1" });
        AuditFingerprint forged = SolveFingerprinting.Configuration(
            new Dictionary<string, string> { ["alpha"] = "1\nbeta=2" });

        first.Value.Should().Be(reordered.Value, "指纹必须与键顺序无关 / the fingerprint must be key-order independent");
        forged.Value.Should().NotBe(first.Value,
            "键值内的分隔符不得伪造出另一组配置 / a delimiter inside a value must not forge another configuration");
    }

    [Fact]
    public async Task AwaitSliceEndAsync_ShouldReadBaselineStatusAndCarryScheduling() {
        using var transport = new StubTransport(
            statusCode: 200,
            body: """
            {
              "code": "OK",
              "message": "done",
              "data": {
                "status": "COMPLETED",
                "outcome": "COMPLETED",
                "runId": "run-1",
                "attemptId": "attempt-1",
                "scheduling": {
                  "dispatchId": "dispatch-1",
                  "quantumMs": 2000,
                  "outcome": "COMPLETED"
                }
              }
            }
            """);
        var client = new RemoteSolverHttpClient(
            transport: transport.Send,
            baseUrl: "http://localhost",
            tenantId: new TenantId("tenant"));

        Result<SliceResult, ErrorCode, Error<ErrorCode>> result =
            await client.AwaitSliceEndAsync(Handle(), TimeSpan.FromMilliseconds(1000));

        result.IsOk.Should().BeTrue();
        result.Value.Status.Should().Be(Domain.TaskStatus.Completed);
        result.Value.Completed.Should().BeTrue();
        result.Value.Outcome.Should().Be(SliceOutcome.Completed);
        result.Value.RunId.Should().Be("run-1");
        result.Value.AttemptId.Should().Be("attempt-1");
        result.Value.Scheduling!.DispatchId!.Value.Value.Should().Be("dispatch-1");
        result.Value.Scheduling!.QuantumMs.Should().Be(2000);
    }

    [Fact]
    public async Task AwaitSliceEndAsync_ShouldRejectUnknownStatus() {
        using var transport = new StubTransport(
            statusCode: 200,
            body: """{"code":"OK","message":"x","data":{"status":"completed"}}""");
        var client = new RemoteSolverHttpClient(
            transport: transport.Send,
            baseUrl: "http://localhost",
            tenantId: new TenantId("tenant"));

        Result<SliceResult, ErrorCode, Error<ErrorCode>> result =
            await client.AwaitSliceEndAsync(Handle(), TimeSpan.FromMilliseconds(1000));

        result.IsFailed.Should().BeTrue(
            "小写状态不是基线枚举 / a lowercase status is not a baseline enum member");
    }

    [Fact]
    public async Task AwaitSliceEndAsync_ShouldPollUntilTerminal() {
        using var transport = new SequencedTransport(
            new RemoteSolverHttpResponse(200, """{"code":"OK","data":{"status":"RUNNING"}}"""),
            new RemoteSolverHttpResponse(200, """{"code":"OK","data":{"status":"QUEUED"}}"""),
            new RemoteSolverHttpResponse(
                200,
                """{"code":"OK","data":{"status":"COMPLETED","outcome":"COMPLETED","runId":"run-1"}}"""));
        var client = new RemoteSolverHttpClient(
            transport: transport.Send,
            baseUrl: "http://localhost",
            tenantId: new TenantId("tenant"));

        Result<SliceResult, ErrorCode, Error<ErrorCode>> result =
            await client.AwaitSliceEndAsync(Handle(), TimeSpan.FromMilliseconds(1000));

        result.IsOk.Should().BeTrue();
        result.Value.Status.Should().Be(Domain.TaskStatus.Completed);
        result.Value.Completed.Should().BeTrue();
        transport.RequestCount.Should().Be(3,
            "终态且无 resultRef 时三次轮询即止 / three polls suffice when no resultRef exists");
    }

    [Fact]
    public async Task AwaitSliceEndAsync_ShouldReturnNonTerminalOnSuspended() {
        using var transport = new SequencedTransport(
            new RemoteSolverHttpResponse(200, """{"code":"OK","data":{"status":"SUSPENDED"}}"""));
        var client = new RemoteSolverHttpClient(
            transport: transport.Send,
            baseUrl: "http://localhost",
            tenantId: new TenantId("tenant"));

        Result<SliceResult, ErrorCode, Error<ErrorCode>> result =
            await client.AwaitSliceEndAsync(Handle(), TimeSpan.FromMilliseconds(1000));

        result.IsOk.Should().BeTrue();
        result.Value.Completed.Should().BeFalse("暂停不是终态 / suspension is not terminal");
        result.Value.Status.Should().Be(Domain.TaskStatus.Suspended);
    }

    [Fact]
    public async Task AwaitSliceEndAsync_ShouldRejectNonPositiveQuantum() {
        using var transport = new StubTransport(statusCode: 200, body: """{"code":"OK","data":{}}""");
        var client = new RemoteSolverHttpClient(
            transport: transport.Send,
            baseUrl: "http://localhost",
            tenantId: new TenantId("tenant"));

        Result<SliceResult, ErrorCode, Error<ErrorCode>> result =
            await client.AwaitSliceEndAsync(Handle(), TimeSpan.Zero);

        result.IsFailed.Should().BeTrue("时间片必须为正 / quantum must be positive");
        transport.RequestCount.Should().Be(0);
    }

    [Fact]
    public void ActionView_ShouldNeverClaimFeasibilityUnderUnknownSemantics() {
        var unknownOutcome = new RemoteTaskActionView(
            Status: "COMPLETED",
            Accepted: true,
            SolutionPresence: RemoteSolutionPresence.Optimal,
            Outcome: SliceOutcome.Unknown);
        unknownOutcome.HasUnknownOutcomeSemantics.Should().BeTrue();
        unknownOutcome.InferredFeasible.Should().BeFalse(
            "未知结果语义下不得声称可行 / feasibility must not be claimed under unknown semantics");
        unknownOutcome.InferredOutcome.Should().Be(SliceOutcome.Unknown);

        var unknownProof = new RemoteTaskActionView(
            Status: "COMPLETED",
            Accepted: true,
            SolutionPresence: RemoteSolutionPresence.Optimal,
            ProofStatus: RemoteProofStatus.Unknown);
        unknownProof.InferredFeasible.Should().BeFalse();

        var unknownSchedulingOutcome = new RemoteTaskActionView(
            Status: "COMPLETED",
            Accepted: true,
            SolutionPresence: RemoteSolutionPresence.Optimal,
            Scheduling: new SchedulingDecision(Outcome: SliceOutcome.Unknown));
        unknownSchedulingOutcome.InferredFeasible.Should().BeFalse();
    }

    [Fact]
    public void ActionView_ShouldInferOutcomeInBaselineOrder() {
        new RemoteTaskActionView(Status: "STOPPED").InferredOutcome.Should().Be(SliceOutcome.Cancelled);
        new RemoteTaskActionView(Status: "FAILED").InferredOutcome.Should().Be(SliceOutcome.Failed);
        new RemoteTaskActionView(Status: "COMPLETED").InferredOutcome.Should().Be(SliceOutcome.Completed);
        new RemoteTaskActionView(
            Status: "SUSPENDED",
            SolutionPresence: RemoteSolutionPresence.Incumbent).InferredOutcome.Should().Be(SliceOutcome.Resumable);
        new RemoteTaskActionView(Status: "SUSPENDED").InferredOutcome.Should().Be(SliceOutcome.Preempted);
        new RemoteTaskActionView(
            Status: "COMPLETED",
            Outcome: SliceOutcome.Checkpointed).InferredOutcome.Should().Be(SliceOutcome.Checkpointed,
            "显式结果优先于状态推断 / an explicit outcome wins over status inference");
    }

    [Fact]
    public async Task AwaitSliceEndAsync_ShouldNotClaimFeasibleWhenSemanticsUnknown() {
        using var transport = new StubTransport(
            statusCode: 200,
            body: """{"code":"OK","data":{"status":"COMPLETED","solutionPresence":"UNKNOWN","outcome":"UNKNOWN"}}""");
        var client = new RemoteSolverHttpClient(
            transport: transport.Send,
            baseUrl: "http://localhost",
            tenantId: new TenantId("tenant"));

        Result<SliceResult, ErrorCode, Error<ErrorCode>> result =
            await client.AwaitSliceEndAsync(Handle(), TimeSpan.FromMilliseconds(1000));

        result.IsOk.Should().BeTrue();
        result.Value.Feasible.Should().BeFalse("未知语义下不得声称可行 / feasibility must not be claimed");
        result.Value.Outcome.Should().Be(SliceOutcome.Unknown);
    }

    [Fact]
    public async Task AwaitSliceEndAsync_ShouldPreferResultArtifactOverTaskView() {
        using var transport = new SequencedTransport(
            new RemoteSolverHttpResponse(200, """
            {"code":"OK","data":{"status":"COMPLETED","latestResultRef":{"path":"tenant/result/run-1"}}}
            """));
        var storage = new RecordingStorage {
            ObjectBytes = System.Text.Encoding.UTF8.GetBytes("""
            {"feasible":true,"optimal":true,"objectiveValue":42.5,"gap":0.0,
             "solutionPresence":"OPTIMAL","proofStatus":"VERIFIED","terminationReason":"COMPLETED",
             "schemaVersion":"2.0","elapsedMs":33,"statistics":{"bestBound":"42.5"}}
            """),
        };
        var client = new RemoteSolverHttpClient(
            transport: transport.Send,
            baseUrl: "http://localhost",
            tenantId: new TenantId("tenant"),
            objectStoragePort: storage);

        Result<SliceResult, ErrorCode, Error<ErrorCode>> result =
            await client.AwaitSliceEndAsync(Handle(), TimeSpan.FromMilliseconds(1000));

        result.IsOk.Should().BeTrue();
        result.Value.ObjectiveValue!.Value.ToDouble().Should().Be(42.5, "artifact 目标值优先 / the artifact objective wins");
        result.Value.Elapsed.Should().Be(TimeSpan.FromMilliseconds(33), "artifact 耗时优先 / the artifact elapsed wins");
        result.Value.ProofStatus.Should().Be(RemoteProofStatus.Verified);
        result.Value.SchemaVersion.Should().Be("2.0");
        result.Value.Statistics!["bestBound"].Should().Be("42.5");
    }

    [Fact]
    public void ToSliceResult_ShouldForceUnknownSemanticsThrough() {
        var view = new RemoteTaskActionView(
            Status: "COMPLETED",
            Accepted: true,
            SolutionPresence: RemoteSolutionPresence.Unknown,
            TerminationReason: RemoteTerminationReason.Unknown);

        var slice = view.ToSliceResult(Handle(), result: null, elapsed: TimeSpan.Zero);

        slice.Feasible.Should().BeFalse();
        slice.Outcome.Should().Be(SliceOutcome.Unknown);
        slice.ProblemStatus.Should().Be(RemoteProblemStatus.Unknown);
        slice.SolutionPresence.Should().Be(RemoteSolutionPresence.Unknown);
        slice.ProofStatus.Should().Be(RemoteProofStatus.Unknown);
        slice.TerminationReason.Should().Be(RemoteTerminationReason.Unknown);
    }

    [Fact]
    public void ToSliceResult_ShouldFollowBaselineTerminationFallbacks() {
        new RemoteTaskActionView(Status: "STOPPED").ToSliceResult(Handle(), null, TimeSpan.Zero)
            .TerminationReason.Should().Be(RemoteTerminationReason.Cancelled);
        new RemoteTaskActionView(Status: "FAILED").ToSliceResult(Handle(), null, TimeSpan.Zero)
            .TerminationReason.Should().Be(RemoteTerminationReason.BackendFailure);
        new RemoteTaskActionView(
            Status: "FAILED",
            TerminationReason: RemoteTerminationReason.Completed).ToSliceResult(Handle(), null, TimeSpan.Zero)
            .TerminationReason.Should().Be(RemoteTerminationReason.BackendFailure,
                "FAILED 不得以 COMPLETED 作为终止原因 / FAILED must not report COMPLETED");
        new RemoteTaskActionView(Status: "COMPLETED").ToSliceResult(Handle(), null, TimeSpan.Zero)
            .TerminationReason.Should().Be(RemoteTerminationReason.Completed);
        new RemoteTaskActionView(Status: "SUSPENDED").ToSliceResult(Handle(), null, TimeSpan.Zero)
            .TerminationReason.Should().Be(RemoteTerminationReason.TimeLimit);
    }

    [Fact]
    public void ToSliceResult_ShouldNeverInferProof() {
        var slice = new RemoteTaskActionView(
            Status: "COMPLETED",
            SolutionPresence: RemoteSolutionPresence.Optimal).ToSliceResult(Handle(), null, TimeSpan.Zero);

        slice.Feasible.Should().BeTrue();
        slice.ProofStatus.Should().Be(RemoteProofStatus.None,
            "证明绝不由可行性或零间隙反推 / proof is never back-inferred from feasibility");
    }

    [Fact]
    public void ToSliceResult_ShouldCompleteSchedulingWithoutOverwriting() {
        var view = new RemoteTaskActionView(
            Status: "COMPLETED",
            Accepted: true,
            SolutionPresence: RemoteSolutionPresence.Optimal,
            ModelFingerprint: "model-sha",
            Scheduling: new SchedulingDecision(
                TaskId: new TaskId("server-task"),
                QuantumMs: 2000,
                Priority: 3));

        var result = view.ToSliceResult(Handle(), result: null, elapsed: TimeSpan.Zero);

        result.Scheduling!.TaskId!.Value.Value.Should().Be("server-task",
            "服务端已给出的字段不得被覆盖 / a server-supplied field must not be overwritten");
        result.Scheduling!.SliceId!.Value.Value.Should().Be("slice");
        result.Scheduling!.NodeId!.Value.Value.Should().Be("node");
        result.Scheduling!.QuantumMs.Should().Be(2000);
        result.Scheduling!.Priority.Should().Be(3);
        result.Scheduling!.ModelFingerprint.Should().Be("model-sha");
        result.Scheduling!.Outcome.Should().Be(SliceOutcome.Completed);
    }

    [Fact]
    public void SchedulingComplete_ShouldForceUnknownOutcomeUnderUnknownSemantics() {
        var decision = new SchedulingDecision(Outcome: SliceOutcome.Completed);

        SchedulingDecision completed = decision.Complete(unknownSemantics: true);

        completed.Outcome.Should().Be(SliceOutcome.Unknown,
            "未知语义下调度结果不得声称已完成 / the scheduling outcome must not claim completion");
    }

    [Fact]
    public void IdentityFingerprints_ShouldApplyBaselinePrecedence() {
        var view = new RemoteTaskActionView(
            Status: "COMPLETED",
            Accepted: true,
            Fingerprints: new Dictionary<string, string> { ["model"] = "flat-map", ["extra"] = "kept" },
            ModelFingerprint: "flat-model",
            ConfigurationFingerprint: "config-sha",
            SolverFingerprint: "solver-sha",
            Scheduling: new SchedulingDecision(ModelFingerprint: "scheduled-model"));

        IReadOnlyDictionary<string, string> merged = view.IdentityFingerprints();

        merged["model"].Should().Be("scheduled-model",
            "调度决策的模型指纹优先级最高 / the scheduling model fingerprint has the highest precedence");
        merged["configuration"].Should().Be("config-sha");
        merged["solver"].Should().Be("solver-sha");
        merged["extra"].Should().Be("kept", "其余键必须保留 / other keys must survive");
    }

    [Fact]
    public void IdentityFingerprintSchemas_ShouldSkipBlankValues() {
        var view = new RemoteTaskActionView(
            Status: "COMPLETED",
            Accepted: true,
            ModelFingerprintSchema: "1.0",
            ConfigurationFingerprintSchema: "   ");

        IReadOnlyDictionary<string, string> schemas = view.IdentityFingerprintSchemas();

        schemas.Should().ContainKey("model");
        schemas.Should().NotContainKey("configuration",
            "空白 schema 不得写成空字符串 / a blank schema must not become an empty entry");
    }

    [Fact]
    public void ToSliceResult_ShouldCarryMergedFingerprints() {
        var view = new RemoteTaskActionView(
            Status: "COMPLETED",
            Accepted: true,
            ModelFingerprint: "view-model",
            SolverFingerprint: "view-solver",
            FingerprintSchemas: new Dictionary<string, string> { ["model"] = "1.0" });

        var slice = view.ToSliceResult(Handle(), result: null, elapsed: TimeSpan.Zero);

        slice.Fingerprints!["model"].Should().Be("view-model");
        slice.Fingerprints!["solver"].Should().Be("view-solver");
        slice.FingerprintSchemas!["model"].Should().Be("1.0");
    }

    /// <summary>
    /// <c>SolveFingerprinting.Solver</c> 在固定 Kotlin 基线里**没有对应实现**（基线只有 `sha256` 与
    /// `configuration`），因此这是 C# 侧扩展而非同步缺口。本测试锁定其审计值，避免重构时静默改变
    /// 已落库的求解器身份。
    /// <c>SolveFingerprinting.Solver</c> has **no counterpart** in the pinned Kotlin baseline (which only
    /// has `sha256` and `configuration`), so it is a C#-side extension rather than a sync gap. This test
    /// pins its audit value so a refactor cannot silently change persisted solver identities.
    /// </summary>
    [Fact]
    public void SolverFingerprint_ShouldStayStable() {
        var descriptor = new SolverDescriptor(
            SolverId: "test-solver",
            BackendName: "TestBackend",
            Capabilities: new SolverCapabilities(
                ModelTypes: new HashSet<SolverModelType> { SolverModelType.Lp }),
            BackendVersion: "1.2.3",
            PluginVersion: "0.1.0");

        AuditFingerprint fingerprint = SolveFingerprinting.Solver(descriptor);

        fingerprint.SchemaVersion.Should().Be("solver-2");
        fingerprint.Algorithm.Should().Be("SHA-256");
        fingerprint.Value.Should().Be(
            "1804dc16b54c9b07109f09b694b1fbfadd382fe06acb3f78cc6642cad70a622c",
            "求解器指纹变更会使审计身份失效 / changing it invalidates audit identities");
    }

    [Fact]
    public void SolverFingerprint_ShouldBeOrderIndependentForModelTypes() {
        AuditFingerprint first = SolveFingerprinting.Solver(new SolverDescriptor(
            SolverId: "s",
            BackendName: "B",
            Capabilities: new SolverCapabilities(
                ModelTypes: new HashSet<SolverModelType> { SolverModelType.Lp, SolverModelType.Mip })));
        AuditFingerprint reordered = SolveFingerprinting.Solver(new SolverDescriptor(
            SolverId: "s",
            BackendName: "B",
            Capabilities: new SolverCapabilities(
                ModelTypes: new HashSet<SolverModelType> { SolverModelType.Mip, SolverModelType.Lp })));

        first.Value.Should().Be(reordered.Value,
            "模型类型集合必须按序规范化 / model types must canonicalize by order");
    }

    [Fact]
    public void ToSliceResult_ShouldMergeProvenanceWithArtifactWinning() {
        var view = new RemoteTaskActionView(
            Status: "COMPLETED",
            Accepted: true,
            SolutionPresence: RemoteSolutionPresence.Optimal,
            Provenance: new Dictionary<string, string> { ["backend"] = "view", ["host"] = "n1" });

        var artifact = new SerializedSolution {
            Feasible = true,
            Optimal = true,
            ObjectiveValue = 1.0,
            SchemaVersion = "1.0",
        };

        var viewOnly = view.ToSliceResult(Handle(), result: null, elapsed: TimeSpan.Zero);
        viewOnly.Provenance!["backend"].Should().Be("view");
        viewOnly.Provenance!["host"].Should().Be("n1");

        var merged = view.ToSliceResult(Handle(), result: artifact, elapsed: TimeSpan.Zero);
        merged.Provenance!.Should().NotBeNull();
        merged.Provenance!["backend"].Should().Be("view",
            "artifact 未提供该键时保留视图值 / the view value survives when the artifact omits the key");
    }

    /// <summary>
    /// 固定 Kotlin 基线只有 5 个路径：<c>POST /api/v1/tasks</c>、<c>GET /api/v1/capabilities</c>、
    /// <c>GET /api/v1/tasks/{taskId}</c>、<c>POST.../stop</c>、<c>POST.../resume</c>。
    /// 本测试钉死这 5 个，防止再次自造端点。
    /// The pinned Kotlin baseline has exactly five paths: <c>POST /api/v1/tasks</c>,
    /// <c>GET /api/v1/capabilities</c>, <c>GET /api/v1/tasks/{taskId}</c>, <c>POST.../stop</c> and
    /// <c>POST.../resume</c>. This test pins those five so endpoints are not invented again.
    /// </summary>
    [Fact]
    public async Task AllRequests_ShouldUseOnlyBaselineEndpoints() {
        var seen = new List<string>();
        var storage = new RecordingStorage();
        using var transport = new SequencedTransport(
            new RemoteSolverHttpResponse(200, """{"code":"OK","data":{"status":"SUSPENDED"}}"""),
            new RemoteSolverHttpResponse(200, """{"code":"OK","data":{"status":"COMPLETED"}}"""),
            new RemoteSolverHttpResponse(200, """{"code":"OK","data":{"status":"STOPPED"}}"""),
            new RemoteSolverHttpResponse(200, """{"code":"OK","data":{"status":"STOPPED"}}"""),
            new RemoteSolverHttpResponse(200, """{"code":"OK","data":{"status":"COMPLETED","accepted":true}}"""),
            new RemoteSolverHttpResponse(200, """
            {"code":"OK","data":{"schemaVersion":"1.0","protocolVersions":["2.0"],"supportedModelTypes":["LINEAR"]}}
            """),
            new RemoteSolverHttpResponse(200, """{"code":"OK","data":{"taskId":"task","accepted":true,"status":"QUEUED"}}"""));
        var client = new RemoteSolverHttpClient(
            transport: request => {
                seen.Add($"{request.Method.Method} {new Uri(request.Url).AbsolutePath}");
                return transport.Send(request);
            },
            baseUrl: "http://localhost",
            tenantId: new TenantId("tenant"),
            objectStoragePort: storage);

        await client.AwaitSliceEndAsync(Handle(), TimeSpan.FromMilliseconds(1));
        await client.ExportCheckpointAsync(Handle());
        await client.StopAsync(Handle());
        await client.ProbeCapabilitiesAsync();
        await client.StartAsync(
            payload: new SolvePayload(ModelData: new ModelData(Format: "x")),
            taskId: new TaskId("task"),
            sliceId: new SliceId("slice"),
            nodeId: new NodeId("node"),
            tenantId: new TenantId("tenant"));

        seen.Should().OnlyContain(path => path.StartsWith("GET /api/v1/", StringComparison.Ordinal) ||
            path.StartsWith("POST /api/v1/", StringComparison.Ordinal),
            "所有请求必须落在 /api/v1 下 / every request must live under /api/v1");
        seen.Should().Contain("GET /api/v1/tasks/task");
        seen.Should().Contain("GET /api/v1/capabilities");
        seen.Should().Contain("POST /api/v1/tasks");
        seen.Should().NotContain(path => path.Contains("/slices/", StringComparison.Ordinal),
            "基线没有 slices 端点 / the baseline has no slices endpoint");
        seen.Should().NotContain(path => path.Contains("quantum", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FetchFinalResult_ShouldReturnNullWithoutObservation() {
        using var transport = new StubTransport(
            statusCode: 200,
            body: """{"code":"OK","data":{"status":"QUEUED","accepted":true}}""");
        var client = new RemoteSolverHttpClient(
            transport: transport.Send,
            baseUrl: "http://localhost",
            tenantId: new TenantId("tenant"));

        Result<SolveResult?, ErrorCode, Error<ErrorCode>> result =
            await client.FetchFinalResultAsync(Handle());

        result.IsOk.Should().BeTrue();
        result.Value.Should().BeNull(
            "无任何观测时不得产出空结果 / no observation must yield null, not an empty result");
    }

    [Fact]
    public async Task FetchFinalResult_ShouldFallBackToViewWithoutArtifact() {
        using var transport = new StubTransport(
            statusCode: 200,
            body: """{"code":"OK","data":{"status":"COMPLETED","objectiveValue":7.5,"solutionPresence":"INCUMBENT"}}""");
        var client = new RemoteSolverHttpClient(
            transport: transport.Send,
            baseUrl: "http://localhost",
            tenantId: new TenantId("tenant"));

        Result<SolveResult?, ErrorCode, Error<ErrorCode>> result =
            await client.FetchFinalResultAsync(Handle());

        result.IsOk.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.Feasible.Should().BeTrue();
        result.Value.ObjectiveValue.Should().Be(new Flt64(7.5),
            "无 artifact 时由任务视图兜底 / the task view supplies the value when no artifact exists");
    }

    [Fact]
    public async Task FetchFinalResult_ShouldReturnNullForMissingTask() {
        using var transport = new StubTransport(statusCode: 404, body: "not found");
        var client = new RemoteSolverHttpClient(
            transport: transport.Send,
            baseUrl: "http://localhost",
            tenantId: new TenantId("tenant"));

        Result<SolveResult?, ErrorCode, Error<ErrorCode>> result =
            await client.FetchFinalResultAsync(Handle());

        result.IsOk.Should().BeTrue();
        result.Value.Should().BeNull("任务不存在不是失败，而是无结果 / a missing task is not a failure");
    }

    /// <summary>
    /// 不可行证据的 wire 名是**枚举成员原名**（PascalCase），不是 SCREAMING_SNAKE——基线用
    /// <c>valueOf(name)</c> 直接比较、不做大小写转换。本测试钉住这一点，并覆盖
    /// <c>NativeIis</c>／<c>NativeIIS</c> 这一个拼写差异。
    /// Infeasibility-evidence wire names are the **raw enum member names** (PascalCase), not
    /// SCREAMING_SNAKE — the baseline compares with <c>valueOf(name)</c> and never changes case. This test
    /// pins that, including the single <c>NativeIis</c>/<c>NativeIIS</c> spelling divergence.
    /// </summary>
    [Fact]
    public void EvidenceWireNames_ShouldBeRawPascalCase() {
        RemoteWireEnumNames.ToWireName(EvidenceValidity.Verified).Should().Be("Verified");
        RemoteWireEnumNames.ToWireName(EvidenceExactness.Exact).Should().Be("Exact");
        RemoteWireEnumNames.ToWireName(EvidenceCompleteness.Complete).Should().Be("Complete");
        RemoteWireEnumNames.ToWireName(EvidenceMinimality.NotChecked).Should().Be("NotChecked");
        RemoteWireEnumNames.ToWireName(BoundSide.Lower).Should().Be("Lower");
        RemoteWireEnumNames.ToWireName(InfeasibilityEvidenceSource.NativeIis).Should().Be("NativeIIS",
            "基线写作 NativeIIS，C# 写作 NativeIis / the baseline spells it NativeIIS while C# uses NativeIis");
        RemoteWireEnumNames.TryParse("NativeIIS", out InfeasibilityEvidenceSource source).Should().BeTrue();
        source.Should().Be(InfeasibilityEvidenceSource.NativeIis);
        RemoteWireEnumNames.TryParse("VERIFIED", out EvidenceValidity _).Should().BeFalse(
            "SCREAMING_SNAKE 不是这些枚举的 wire 形式 / SCREAMING_SNAKE is not their wire form");
    }

    [Fact]
    public void ToRemoteDiagnostics_ShouldParseInfeasibilityEvidence() {
        var fields = new Dictionary<string, string> {
            ["infeasibility.source"] = "NativeIIS",
            ["infeasibility.exactness"] = "Exact",
            ["infeasibility.completeness"] = "Complete",
            ["infeasibility.validity"] = "Verified",
            ["infeasibility.minimality"] = "Irreducible",
            ["infeasibility.constraintIds"] = """["c1","c2"]""",
            ["infeasibility.variableBoundRefs"] = """["x:Lower","y:Upper"]""",
            ["infeasibility.variableDomainRefs"] = """["z"]""",
            ["infeasibility.members"] = """["constraint:c1","domain:z","bound:x:Lower"]""",
            ["infeasibility.assumptionIds"] = """["a1"]""",
        };

        Result<SolveDiagnostics<Flt64>, ErrorCode, Error<ErrorCode>> result =
            RemoteReportMapping.ToRemoteDiagnostics(fields);

        result.IsOk.Should().BeTrue();
        InfeasibilityEvidence evidence = result.Value.InfeasibilityEvidence!;
        evidence.Source.Should().Be(InfeasibilityEvidenceSource.NativeIis);
        evidence.Validity.Should().Be(EvidenceValidity.Verified);
        evidence.Minimality.Should().Be(EvidenceMinimality.Irreducible);
        evidence.ConstraintIds.Should().HaveCount(2);
        evidence.VariableBoundRefs.Should().HaveCount(2);
        evidence.VariableDomainRefs.Should().HaveCount(1);
        evidence.Members.Should().HaveCount(3);
        evidence.AssumptionIds.Should().HaveCount(1);
        evidence.VariableBoundRefs.Should().Contain(
            new VariableBoundRef(new VariableId("x"), BoundSide.Lower));
    }

    [Fact]
    public void ToRemoteDiagnostics_ShouldRejectMalformedEvidence() {
        static Dictionary<string, string> Base() => new() {
            ["infeasibility.source"] = "Farkas",
            ["infeasibility.exactness"] = "Exact",
            ["infeasibility.completeness"] = "Complete",
            ["infeasibility.validity"] = "Verified",
            ["infeasibility.minimality"] = "Irreducible",
        };

        Dictionary<string, string> badSource = Base();
        badSource["infeasibility.source"] = "not-a-source";
        RemoteReportMapping.ToRemoteDiagnostics(badSource).IsOk.Should().BeFalse();

        Dictionary<string, string> missing = Base();
        _ = missing.Remove("infeasibility.validity");
        RemoteReportMapping.ToRemoteDiagnostics(missing).IsOk.Should().BeFalse(
            "五个枚举字段全部必填 / all five enum fields are required");

        Dictionary<string, string> badList = Base();
        badList["infeasibility.constraintIds"] = "not-json";
        RemoteReportMapping.ToRemoteDiagnostics(badList).IsOk.Should().BeFalse();

        Dictionary<string, string> badBound = Base();
        badBound["infeasibility.variableBoundRefs"] = """["noSideHere"]""";
        RemoteReportMapping.ToRemoteDiagnostics(badBound).IsOk.Should().BeFalse(
            "缺少冒号即畸形 / a missing colon is malformed");

        Dictionary<string, string> badMember = Base();
        badMember["infeasibility.members"] = """["unknown:x"]""";
        RemoteReportMapping.ToRemoteDiagnostics(badMember).IsOk.Should().BeFalse(
            "未知成员类型必须拒绝 / an unknown member type must be rejected");
    }

    [Fact]
    public void ToRemoteDiagnostics_ShouldReturnNullEvidenceWhenAbsent() {
        Result<SolveDiagnostics<Flt64>, ErrorCode, Error<ErrorCode>> result =
            RemoteReportMapping.ToRemoteDiagnostics(new Dictionary<string, string>());

        result.IsOk.Should().BeTrue();
        result.Value.InfeasibilityEvidence.Should().BeNull(
            "没有 infeasibility.* 字段时不是错误 / no infeasibility fields is not an error");
    }

    [Fact]
    public void ToRemoteDiagnostics_ShouldParseEvidenceTailFields() {
        var fields = new Dictionary<string, string> {
            ["infeasibility.source"] = "Farkas",
            ["infeasibility.exactness"] = "Irreducible",
            ["infeasibility.completeness"] = "Partial",
            ["infeasibility.validity"] = "Heuristic",
            ["infeasibility.minimality"] = "Partial",
            ["infeasibility.variableBoundRefs"] = """["x:Lower","y:Upper"]""",
            ["infeasibility.elapsedMs"] = "1234",
            ["infeasibility.reference"] = "iis://run-1",
            ["infeasibility.verificationChecks"] = "7",
            ["infeasibility.terminationReason"] = "TimeLimit",
            ["infeasibility.unavailable.code"] = "U1",
            ["infeasibility.unavailable.category"] = "Numerical",
            ["infeasibility.unavailable.message"] = "no proof available",
        };

        Result<SolveDiagnostics<Flt64>, ErrorCode, Error<ErrorCode>> result =
            RemoteReportMapping.ToRemoteDiagnostics(fields);

        result.IsOk.Should().BeTrue();
        InfeasibilityEvidence evidence = result.Value.InfeasibilityEvidence!;
        evidence.Elapsed.Should().Be(TimeSpan.FromMilliseconds(1234));
        evidence.Reference.Should().Be("iis://run-1");
        evidence.VerificationChecks.Should().Be(7UL);
        evidence.TerminationReason.Should().Be(TerminationReason.TimeLimit);
        evidence.UnavailableReason.Should().NotBeNull();
        evidence.UnavailableReason!.Code.Should().Be("U1");
        evidence.VariableBoundIds.Should().HaveCount(2,
            "variableBoundIds 由引用去重得到 / variableBoundIds derives from the refs");
    }

    [Fact]
    public void ToRemoteDiagnostics_ShouldRejectInvalidTailFields() {
        static Dictionary<string, string> Base() => new() {
            ["infeasibility.source"] = "Farkas",
            ["infeasibility.exactness"] = "Exact",
            ["infeasibility.completeness"] = "Complete",
            ["infeasibility.validity"] = "Verified",
            ["infeasibility.minimality"] = "Irreducible",
        };

        Dictionary<string, string> badElapsed = Base();
        badElapsed["infeasibility.elapsedMs"] = "-5";
        RemoteReportMapping.ToRemoteDiagnostics(badElapsed).IsOk.Should().BeFalse(
            "负耗时无效 / a negative elapsed is invalid");

        Dictionary<string, string> badChecks = Base();
        badChecks["infeasibility.verificationChecks"] = "-1";
        RemoteReportMapping.ToRemoteDiagnostics(badChecks).IsOk.Should().BeFalse();

        Dictionary<string, string> badTermination = Base();
        badTermination["infeasibility.terminationReason"] = "NOT_A_REASON";
        RemoteReportMapping.ToRemoteDiagnostics(badTermination).IsOk.Should().BeFalse();

        Dictionary<string, string> missingCategory = Base();
        missingCategory["infeasibility.unavailable.code"] = "U1";
        missingCategory["infeasibility.unavailable.message"] = "msg";
        RemoteReportMapping.ToRemoteDiagnostics(missingCategory).IsOk.Should().BeFalse(
            "unavailable 的类别必填 / the unavailable category is required");

        Dictionary<string, string> blankMessage = Base();
        blankMessage["infeasibility.unavailable.code"] = "U1";
        blankMessage["infeasibility.unavailable.category"] = "Numerical";
        blankMessage["infeasibility.unavailable.message"] = "   ";
        RemoteReportMapping.ToRemoteDiagnostics(blankMessage).IsOk.Should().BeFalse(
            "空白消息算缺失 / a blank message counts as missing");
    }

    [Fact]
    public async Task AwaitSliceEndAsync_ShouldAttachParsedDiagnostics() {
        using var transport = new StubTransport(
            statusCode: 200,
            body: """
            {"code":"OK","data":{"status":"COMPLETED","accepted":true,
             "diagnostics":{"warning.0.code":"W0","warning.0.category":"Numerical","warning.0.message":"careful"}}}
            """);
        var client = new RemoteSolverHttpClient(
            transport: transport.Send,
            baseUrl: "http://localhost",
            tenantId: new TenantId("tenant"));

        Result<SliceResult, ErrorCode, Error<ErrorCode>> result =
            await client.AwaitSliceEndAsync(Handle(), TimeSpan.FromMilliseconds(1));

        result.IsOk.Should().BeTrue();
        result.Value.Diagnostics.Should().ContainKey("warning.0.code",
            "原始字段始终保留 / the raw field always survives");
        result.Value.SolveDiagnostics.Should().NotBeNull();
        result.Value.SolveDiagnostics!.Warnings.Should().HaveCount(1);
        result.Value.SolveDiagnostics!.Warnings![0].Code.Should().Be("W0");
    }

    [Fact]
    public async Task AwaitSliceEndAsync_ShouldNotFailWhenDiagnosticsAreMalformed() {
        using var transport = new StubTransport(
            statusCode: 200,
            body: """
            {"code":"OK","data":{"status":"COMPLETED","accepted":true,
             "diagnostics":{"warning.0.code":"W0"}}}
            """);
        var client = new RemoteSolverHttpClient(
            transport: transport.Send,
            baseUrl: "http://localhost",
            tenantId: new TenantId("tenant"));

        Result<SliceResult, ErrorCode, Error<ErrorCode>> result =
            await client.AwaitSliceEndAsync(Handle(), TimeSpan.FromMilliseconds(1));

        result.IsOk.Should().BeTrue("诊断畸形不应让整个结果失败 / malformed diagnostics must not fail the result");
        result.Value.SolveDiagnostics.Should().BeNull();
        result.Value.Message.Should().Contain("诊断解析失败",
            "失败原因写入消息 / the failure reason goes into the message");
    }

    /// <summary>
    /// 兜底变体必须保留原始文本，否则 <c>backend</c> 与 <c>external</c> 都会退化成 <c>Other</c> 的规范
    /// 代码而失真。
    /// The catch-all variant must keep the raw text, otherwise <c>backend</c> and <c>external</c> would
    /// both degrade to <c>Other</c>'s canonical code and lose fidelity.
    /// </summary>
    [Fact]
    public void CancellationChain_ShouldPreserveUnknownOrigins() {
        var backend = new RemoteTaskActionCancellation("backend", 1_700_000_000_000);
        var record = backend.ToCancellationRecord();

        record.Source.Should().Be(CancellationSource.Other);
        record.WireOrigin.Should().Be("backend",
            "未知来源保留原文 / an unknown origin keeps its raw text");
        record.RequestedAt.ToUnixTimeMilliseconds().Should().Be(1_700_000_000_000);

        var roundTripped = RemoteTaskActionCancellation.FromCancellationRecord(record);
        roundTripped.Origin.Should().Be("backend",
            "写回必须还原原文而不是 external / writing back restores the raw text, not external");
    }

    [Fact]
    public void CancellationChain_ShouldMapCanonicalOrigins() {
        var remoteStop = new RemoteTaskActionCancellation("remoteStop", 42, "operator");
        var record = remoteStop.ToCancellationRecord();

        record.Source.Should().Be(CancellationSource.Remote);
        record.Reason.Should().Be("operator");
        record.WireOrigin.Should().BeNull(
            "已知来源不保留原文 / a known origin keeps no raw text");
        RemoteTaskActionCancellation.FromCancellationRecord(record).Origin.Should().Be("remoteStop");
    }

    [Fact]
    public void CancellationChain_ShouldParseFromTaskView() {
        const string body = """
        {"code":"OK","data":{"status":"STOPPED","accepted":true,
         "cancellationChain":[{"origin":"user","requestedAtEpochMs":1700000000000,"reason":"stop"},
                              {"origin":"backend","requestedAtEpochMs":1700000001000}]}}
        """;

        RemoteTaskActionView? view = JsonSerializer.Deserialize<RemoteTaskActionView>(
            JsonDocument.Parse(body).RootElement.GetProperty("data").GetRawText(),
            RemoteWireJson.Options);

        view.Should().NotBeNull();
        view!.CancellationChain.Should().HaveCount(2);
        view.CancellationChain![0].ToCancellationRecord().Source.Should().Be(CancellationSource.Caller);
        view.CancellationChain![1].ToCancellationRecord().Source.Should().Be(CancellationSource.Other);
        view.CancellationChain![1].ToCancellationRecord().WireOrigin.Should().Be("backend");
    }

    /// <summary>
    /// 视图的 <c>provenance</c> 是 Map 形状，动作的 <c>provenance</c> 是强类型对象——同名不同形状，
    /// 因此两者不能共用一个字段。本测试确认视图按 Map 解析，强类型对象作为独立类型存在。
    /// The view's <c>provenance</c> is a map while the action's is a typed object — same name, different
    /// shape — so one field cannot serve both. This test confirms the view decodes the map and the typed
    /// object exists as a separate type.
    /// </summary>
    [Fact]
    public void Provenance_ShouldStayAMapOnTheView() {
        const string body = """
        {"status":"RUNNING","provenance":{"backend":"gurobi","host":"n1"}}
        """;

        RemoteTaskActionView? view = JsonSerializer.Deserialize<RemoteTaskActionView>(
            body,
            RemoteWireJson.Options);

        view.Should().NotBeNull();
        view!.Provenance!["backend"].Should().Be("gurobi");
        view.Provenance!["host"].Should().Be("n1");

        var typed = new RemoteTaskActionProvenance(
            SolverId: "gurobi",
            BackendName: "Gurobi",
            ThreadCount: 4,
            Deterministic: true);
        typed.ThreadCount.Should().Be(4);
    }

    /// <summary>
    /// 基线的 <c>storage.put(path, bytes, metadata)</c> 会把 <c>taskId</c>／<c>sliceId</c>／<c>tenantId</c>
    /// 写成对象元数据。本测试确认 C# 侧不再只传 content type。
    /// The baseline's <c>storage.put(path, bytes, metadata)</c> writes taskId/sliceId/tenantId as object
    /// metadata. This test confirms the C# side no longer passes only a content type.
    /// </summary>
    [Fact]
    public async Task StartAsync_ShouldUploadPayloadWithIdentityMetadata() {
        using var transport = new SequencedTransport(
            new RemoteSolverHttpResponse(200, """
            {"code":"OK","data":{"schemaVersion":"1.0","protocolVersions":["2.0"],"supportedModelTypes":["CP"],"supportsPortableCheckpoint":true}}
            """),
            new RemoteSolverHttpResponse(200, """{"code":"OK","data":{"taskId":"task","accepted":true,"status":"QUEUED"}}"""));
        var storage = new RecordingStorage();
        var client = new RemoteSolverHttpClient(
            transport: transport.Send,
            baseUrl: "http://localhost",
            tenantId: new TenantId("tenant"),
            objectStoragePort: storage);

        Result<ExecutionHandle, ErrorCode, Error<ErrorCode>> result = await client.StartAsync(
            payload: new SolvePayload(ModelData: new ModelData(Format: "ospf-cp-snapshot-json")),
            taskId: new TaskId("task"),
            sliceId: new SliceId("slice"),
            nodeId: new NodeId("node"),
            tenantId: new TenantId("tenant"));

        result.IsOk.Should().BeTrue();
        storage.UploadedMetadata.Should().NotBeNull(
            "基线写入对象元数据 / the baseline writes object metadata");
        storage.UploadedMetadata!["taskId"].Should().Be("task");
        storage.UploadedMetadata!["sliceId"].Should().Be("slice");
        storage.UploadedMetadata!["tenantId"].Should().Be("tenant");
    }

    /// <summary>
    /// 基线的 <c>requestId</c> 是**幂等请求 ID**：同一 (tenant, task, slice) 必须每次提交都得到同一个值，
    /// 否则重试会被服务端当成新任务。基线里没有"异步重放"或"恰好一次归并"这一层能力，幂等性就落在
    /// 这个 ID 上。
    /// The baseline's <c>requestId</c> is an **idempotent request ID**: the same (tenant, task, slice) must
    /// yield the same value on every submit, otherwise a retry reads as a new task. The baseline has no
    /// "async replay" or "exactly-once merge" layer — idempotence rests on this ID.
    /// </summary>
    [Fact]
    public async Task RequestId_ShouldBeStableForTheSameIdentity() {
        static SequencedTransport Fresh() => new(
            new RemoteSolverHttpResponse(200, """
            {"code":"OK","data":{"schemaVersion":"1.0","protocolVersions":["2.0"],"supportedModelTypes":["CP"],"supportsPortableCheckpoint":true}}
            """),
            new RemoteSolverHttpResponse(200, """{"code":"OK","data":{"taskId":"task","accepted":true,"status":"QUEUED"}}"""));

        static async Task<string> SubmitAsync(SequencedTransport transport, string tenant) {
            var client = new RemoteSolverHttpClient(
                transport: transport.Send,
                baseUrl: "http://localhost",
                tenantId: new TenantId(tenant),
                objectStoragePort: new RecordingStorage());
            _ = await client.StartAsync(
                payload: new SolvePayload(ModelData: new ModelData(Format: "ospf-cp-snapshot-json")),
                taskId: new TaskId("task"),
                sliceId: new SliceId("slice"),
                nodeId: new NodeId("node"),
                tenantId: new TenantId(tenant));
            using var document = JsonDocument.Parse(transport.LastBody!);
            return document.RootElement.GetProperty("requestId").GetString()!;
        }

        using SequencedTransport first = Fresh();
        using SequencedTransport retry = Fresh();
        using SequencedTransport otherTenant = Fresh();

        string firstId = await SubmitAsync(first, "tenant");
        string retryId = await SubmitAsync(retry, "tenant");
        string otherId = await SubmitAsync(otherTenant, "other");

        retryId.Should().Be(firstId, "重试必须复用同一个幂等 ID / a retry must reuse the same idempotent ID");
        firstId.Should().Be("tenant:task:slice");
        otherId.Should().NotBe(firstId, "不同租户必须是不同请求 / a different tenant is a different request");
    }

    /// <summary>
    /// 基线的取消是幂等的：只有首次请求会真正中断后端，后续请求直接成功返回。
    /// The baseline cancels idempotently: only the first request actually interrupts the backend and later
    /// requests simply succeed.
    /// </summary>
    [Fact]
    public void Cancellation_ShouldBeIdempotentAcrossRepeatedRequests() {
        var interrupts = new List<CancellationRecord>();
        var token = new SolveCancellationToken();
        _ = token.Register(record => {
            interrupts.Add(record);
            return Results.OkInstance;
        });

        token.Request(CancellationSource.Caller, "first").IsOk.Should().BeTrue();
        token.Request(CancellationSource.Timeout, "second").IsOk.Should().BeTrue();
        token.Request().IsOk.Should().BeTrue();

        interrupts.Should().HaveCount(1,
            "只有首次请求调用 backend 中断 / only the first request interrupts the backend");
        interrupts[0].Reason.Should().Be("first");
        token.Record!.Source.Should().Be(CancellationSource.Caller,
            "后续请求不得改写首次记录 / later requests must not rewrite the first record");
        token.IsCancellationRequested.Should().BeTrue();
    }

    /// <summary>
    /// 基线明确规定：**已取消时注册的监听器立即执行**。这条不能反过来——否则一个在取消请求之后才
    /// 注册的后端永远不会被中断，求解资源就漏了。
    /// The baseline states explicitly that a listener **registered after cancellation runs immediately**.
    /// This must not be inverted — otherwise a backend registered after the cancel request would never be
    /// interrupted and its resources would leak.
    /// </summary>
    [Fact]
    public void Cancellation_ShouldInvokeLateListenersImmediately() {
        var token = new SolveCancellationToken();
        _ = token.Request(CancellationSource.Timeout, "deadline");

        CancellationRecord? seen = null;
        int calls = 0;
        _ = token.Register(record => {
            calls++;
            seen = record;
            return Results.OkInstance;
        });

        calls.Should().Be(1,
            "已取消时注册必须立即执行 / registering after cancellation must run immediately");
        seen!.Reason.Should().Be("deadline",
            "必须收到首次取消事实 / the listener must receive the first cancellation record");
        token.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public void Cancellation_ShouldNotNotifyListenersTwice() {
        var token = new SolveCancellationToken();
        int calls = 0;
        _ = token.Register(_ => {
            calls++;
            return Results.OkInstance;
        });

        _ = token.Request(CancellationSource.Caller);
        _ = token.Request(CancellationSource.Timeout);

        calls.Should().Be(1,
            "重复请求不得重复执行监听 / repeated requests must not re-run listeners");
    }

    /// <summary>
    /// 基线的 <c>CheckpointMetadata</c> 用 <c>ref</c> 与 <c>createdAtEpochMs</c>；C# 此前写成
    /// <c>objectRef</c> 与 <c>createdAt</c>，两端读不到对方字段。
    /// The baseline's <c>CheckpointMetadata</c> uses <c>ref</c> and <c>createdAtEpochMs</c>; C# previously
    /// used <c>objectRef</c> and <c>createdAt</c>, so neither side could read the other's fields.
    /// </summary>
    [Fact]
    public void CheckpointMetadata_ShouldUseBaselineFieldNames() {
        var metadata = new CheckpointMetadata(
            TaskId: new TaskId("task"),
            SliceId: new SliceId("slice"),
            Ref: new ObjectRef(Path: new ObjectPath("tenant/checkpoint/task/cp-1")),
            CreatedAtEpochMs: 1_700_000_000_000);

        string json = JsonSerializer.Serialize(metadata, RemoteWireJson.Options);

        json.Should().Contain("\"ref\":", "基线字段名是 ref / the baseline field is ref");
        json.Should().NotContain("objectRef");
        json.Should().Contain("\"createdAtEpochMs\":1700000000000",
            "时间戳是整型毫秒 / the timestamp is integer milliseconds");
        json.Should().NotContain("createdAt\"");
    }

    [Fact]
    public void CheckpointMetadata_ShouldDecodeBaselineShape() {
        const string json = """
        {"taskId":"task","sliceId":"slice","ref":{"path":"tenant/checkpoint/task/cp-1"},
         "createdAtEpochMs":1700000000000}
        """;

        CheckpointMetadata? metadata = JsonSerializer.Deserialize<CheckpointMetadata>(
            json,
            RemoteWireJson.Options);

        metadata.Should().NotBeNull();
        metadata!.TaskId.Value.Should().Be("task");
        metadata.Ref.Path.Value.Should().Be("tenant/checkpoint/task/cp-1");
        metadata.CreatedAtEpochMs.Should().Be(1_700_000_000_000);
    }

    /// <summary>
    /// 基线的 <c>RemoteConstraintProgrammingClient.solve</c> 把 CP snapshot 编码为
    /// <c>ospf-cp-snapshot-json</c> 原始模型数据，并带上 <c>targetType=cp</c> 与规模估计。
    /// The baseline's <c>RemoteConstraintProgrammingClient.solve</c> encodes the CP snapshot as
    /// <c>ospf-cp-snapshot-json</c> raw model data with <c>targetType=cp</c> and size estimates.
    /// </summary>
    [Fact]
    public async Task ConstraintProgrammingClient_ShouldSubmitSnapshotAsRawModelData() {
        using var model = new Fuookami.Ospf.Core.Model.ConstraintProgramming.ConstraintProgrammingModel(
            name: "remote-cp",
            objectCategory: Fuookami.Ospf.Core.Model.Basic.ObjectCategory.Minimum);
        var variable = new Fuookami.Ospf.Core.Variable.IntVar("x");
        _ = model.RegisterVariable(variable).Value;
        model.AddConstraint(
            constraint: Fuookami.Ospf.Core.Model.ConstraintProgramming
                .ConstraintProgrammingConstraint.CreateEqual(
                    Fuookami.Ospf.Core.Model.ConstraintProgramming
                        .ConstraintProgrammingExpression.CreateVariable(variable).Value,
                    new Fuookami.Ospf.Math.Algebra.Number.Int64(3)).Value,
            name: "x is three").IsOk.Should().BeTrue();
        Fuookami.Ospf.Core.Model.ConstraintProgramming.ConstraintProgrammingModelSnapshot snapshot =
            model.Snapshot().Value;

        using var transport = new SequencedTransport(
            new RemoteSolverHttpResponse(200, """
            {"code":"OK","data":{"schemaVersion":"1.0","protocolVersions":["2.0"],"supportedModelTypes":["CP"],"supportsPortableCheckpoint":true}}
            """),
            new RemoteSolverHttpResponse(200, """{"code":"OK","data":{"taskId":"task","accepted":true,"status":"QUEUED"}}"""),
            new RemoteSolverHttpResponse(200, """{"code":"OK","data":{"status":"COMPLETED","accepted":true,"solutionPresence":"OPTIMAL"}}"""));
        var storage = new RecordingStorage();
        var http = new RemoteSolverHttpClient(
            transport: transport.Send,
            baseUrl: "http://localhost",
            tenantId: new TenantId("tenant"),
            objectStoragePort: storage);
        var client = new RemoteConstraintProgrammingClient(http);

        Result<SolveResult, ErrorCode, Error<ErrorCode>> result = await client.SolveAsync(
            snapshot: snapshot,
            taskId: new TaskId("task"),
            sliceId: new SliceId("slice"),
            nodeId: new NodeId("node"),
            tenantId: new TenantId("tenant"),
            quantum: TimeSpan.FromSeconds(4));

        result.IsOk.Should().BeTrue();

        using var uploaded = JsonDocument.Parse(System.Text.Encoding.UTF8.GetString(storage.UploadedBytes!));
        JsonElement modelData = uploaded.RootElement.GetProperty("modelData");
        modelData.GetProperty("format").GetString().Should().Be("ospf-cp-snapshot-json");
        modelData.TryGetProperty("rawBytes", out JsonElement rawBytes).Should().BeTrue();
        System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(rawBytes.GetString()!))
            .Should().Contain("remote-cp", "载荷必须包含 snapshot 内容 / the payload must carry the snapshot");

        JsonElement taskMeta = uploaded.RootElement.GetProperty("taskMeta");
        taskMeta.GetProperty("targetType").GetString().Should().Be("cp");
        taskMeta.GetProperty("estimatedVariableCount").GetInt32().Should().Be(1);
        taskMeta.TryGetProperty("config", out JsonElement _).Should().BeFalse(
            "未传求解配置时不应凭空生成 config / no config is invented when none is given");
    }

    [Fact]
    public async Task ConstraintProgrammingClient_ShouldRejectNonPositiveQuantum() {
        using var model = new Fuookami.Ospf.Core.Model.ConstraintProgramming.ConstraintProgrammingModel(
            name: "remote-cp",
            objectCategory: Fuookami.Ospf.Core.Model.Basic.ObjectCategory.Minimum);
        Fuookami.Ospf.Core.Model.ConstraintProgramming.ConstraintProgrammingModelSnapshot snapshot =
            model.Snapshot().Value;
        var client = new RemoteConstraintProgrammingClient(new RemoteSolverHttpClient(
            transport: _ => Task.FromResult(new RemoteSolverHttpResponse(200, "{}")),
            baseUrl: "http://localhost",
            tenantId: new TenantId("tenant"),
            objectStoragePort: new RecordingStorage()));

        Result<SolveResult, ErrorCode, Error<ErrorCode>> result = await client.SolveAsync(
            snapshot: snapshot,
            taskId: new TaskId("task"),
            sliceId: new SliceId("slice"),
            nodeId: new NodeId("node"),
            tenantId: new TenantId("tenant"),
            quantum: TimeSpan.Zero);

        result.Should().BeOfType<Failed<SolveResult, ErrorCode, Error<ErrorCode>>>()
            .Which.Error.Code.Should().Be(ErrorCode.IllegalArgument,
                "时间片必须为正 / the quantum must be positive");
    }

    /// <summary>
    /// 远程序列化前的身份闸门：身份不完整的模型必须被拒绝，而不是"尽力序列化"。
    /// The identity gate before remote serialization: an incomplete identity must be rejected rather than
    /// serialized on a best-effort basis.
    /// </summary>
    [Fact]
    public void RemoteIdentityGate_ShouldAcceptModelLocalWithoutIdentity() {
        OspfRemoteModelSerializer.IsValidRemoteIdentity(
            kind: ModelElementKind.Constraint,
            id: "row-1",
            scope: ModelElementScope.ModelLocal,
            origin: null,
            provenance: null,
            identityNamespace: null,
            identitySchemaVersion: null).Should().BeTrue();
    }

    [Fact]
    public void RemoteIdentityGate_ShouldRejectModelLocalWithIdentityOrArtifactId() {
        var origin = new ModelElementOrigin("plugin", "k1");

        OspfRemoteModelSerializer.IsValidRemoteIdentity(
            kind: ModelElementKind.Constraint,
            id: "row-1",
            scope: ModelElementScope.ModelLocal,
            origin: origin,
            provenance: null,
            identityNamespace: null,
            identitySchemaVersion: null).Should().BeFalse(
            "ModelLocal 不得携带 origin / ModelLocal must not carry an origin");

        OspfRemoteModelSerializer.IsValidRemoteIdentity(
            kind: ModelElementKind.Constraint,
            id: "row-1",
            scope: ModelElementScope.ModelLocal,
            origin: null,
            provenance: new[] { origin },
            identityNamespace: null,
            identitySchemaVersion: null).Should().BeFalse(
            "ModelLocal 不得携带 provenance / ModelLocal must not carry provenance");

        OspfRemoteModelSerializer.IsValidRemoteIdentity(
            kind: ModelElementKind.Constraint,
            id: "artifact:something",
            scope: ModelElementScope.ModelLocal,
            origin: null,
            provenance: null,
            identityNamespace: null,
            identitySchemaVersion: null).Should().BeFalse(
            "ModelLocal 不得借用 artifact 标识 / ModelLocal must not borrow an artifact id");
    }

    [Fact]
    public void RemoteIdentityGate_ShouldRequireCompleteStableIdentity() {
        var origin = new ModelElementOrigin("plugin", "k1");

        OspfRemoteModelSerializer.IsValidRemoteIdentity(
            kind: ModelElementKind.Constraint,
            id: "stable-1",
            scope: ModelElementScope.Stable,
            origin: origin,
            provenance: new[] { origin },
            identityNamespace: "ns",
            identitySchemaVersion: "1.0").Should().BeTrue();

        OspfRemoteModelSerializer.IsValidRemoteIdentity(
            kind: ModelElementKind.Constraint,
            id: "stable-1",
            scope: ModelElementScope.Stable,
            origin: origin,
            provenance: new[] { origin },
            identityNamespace: null,
            identitySchemaVersion: "1.0").Should().BeFalse(
            "Stable 必须有命名空间 / Stable requires a namespace");

        OspfRemoteModelSerializer.IsValidRemoteIdentity(
            kind: ModelElementKind.Constraint,
            id: "model-local-1",
            scope: ModelElementScope.Stable,
            origin: origin,
            provenance: new[] { origin },
            identityNamespace: "ns",
            identitySchemaVersion: "1.0").Should().BeFalse(
            "Stable 不得使用 model-local- 前缀 / Stable must not use the model-local- prefix");

        OspfRemoteModelSerializer.IsValidRemoteIdentity(
            kind: ModelElementKind.Constraint,
            id: "stable-1",
            scope: ModelElementScope.Stable,
            origin: null,
            provenance: null,
            identityNamespace: "ns",
            identitySchemaVersion: "1.0").Should().BeFalse(
            "Stable 必须存在来源 / Stable requires an origin or provenance");
    }

    [Fact]
    public void RemoteIdentityGate_ShouldRejectContradictoryProvenance() {
        var origin = new ModelElementOrigin("plugin", "declared");
        var other = new ModelElementOrigin("plugin", "other");

        OspfRemoteModelSerializer.IsValidRemoteIdentity(
            kind: ModelElementKind.Constraint,
            id: "stable-1",
            scope: ModelElementScope.Stable,
            origin: origin,
            provenance: new[] { other },
            identityNamespace: "ns",
            identitySchemaVersion: "1.0").Should().BeFalse(
            "自称来源必须出现在来源记录里 / the declared origin must appear in the provenance");
    }

    [Fact]
    public void RemoteIdentityGate_ShouldRejectBlankFields() {
        OspfRemoteModelSerializer.IsValidRemoteIdentity(
            kind: ModelElementKind.Constraint,
            id: "   ",
            scope: ModelElementScope.ModelLocal,
            origin: null,
            provenance: null,
            identityNamespace: null,
            identitySchemaVersion: null).Should().BeFalse(
            "空白标识无效 / a blank id is invalid");

        OspfRemoteModelSerializer.IsValidRemoteIdentity(
            kind: ModelElementKind.Constraint,
            id: "stable-1",
            scope: ModelElementScope.Stable,
            origin: new ModelElementOrigin(" ", "k"),
            provenance: new[] { new ModelElementOrigin(" ", "k") },
            identityNamespace: "ns",
            identitySchemaVersion: "1.0").Should().BeFalse(
            "origin 的 kind 空白无效 / a blank origin kind is invalid");
    }

    /// <summary>
    /// wire 模型的身份字段必须与基线同名同默认值：<c>identityScope</c> 默认 <c>MODEL_LOCAL</c>，
    /// null 字段不写出，<c>identityProvenance</c> 是对象列表。
    /// The wire models' identity fields must match the baseline name for name and default for default:
    /// <c>identityScope</c> defaults to <c>MODEL_LOCAL</c>, null fields are omitted and
    /// <c>identityProvenance</c> is a list of objects.
    /// </summary>
    [Fact]
    public void SerializedConstraint_ShouldCarryBaselineIdentityFields() {
        var constraint = new SerializedConstraint(
            Name: "cons0",
            Sign: SerializedConstraintSign.LessEqual,
            Rhs: 10.0,
            Cells: new List<SerializedConstraintCell> {
                new(VariableIndex: 0, Coefficient: 2.0),
            });

        using var document = JsonDocument.Parse(
            JsonSerializer.Serialize(constraint, RemoteWireJson.Options));
        JsonElement root = document.RootElement;

        root.GetProperty("identityScope").GetString().Should().Be("MODEL_LOCAL",
            "默认作用域是 MODEL_LOCAL / the default scope is MODEL_LOCAL");
        root.TryGetProperty("identityId", out JsonElement _).Should().BeFalse(
            "未设置的身份字段不写出 / unset identity fields are omitted");
        root.TryGetProperty("identityProvenance", out JsonElement _).Should().BeFalse();

        SerializedConstraint withIdentity = constraint with {
            IdentityId = "stable-1",
            IdentityScope = "STABLE",
            IdentityNamespace = "ns",
            IdentitySchemaVersion = "1.0",
            IdentityProvenance = new List<SerializedModelElementOrigin> {
                new(Kind: "plugin", Key: "k1"),
            },
        };
        using var second = JsonDocument.Parse(
            JsonSerializer.Serialize(withIdentity, RemoteWireJson.Options));
        JsonElement secondRoot = second.RootElement;

        secondRoot.GetProperty("identityId").GetString().Should().Be("stable-1");
        secondRoot.GetProperty("identityScope").GetString().Should().Be("STABLE");
        JsonElement provenance = secondRoot.GetProperty("identityProvenance");
        provenance.GetArrayLength().Should().Be(1);
        provenance[0].GetProperty("kind").GetString().Should().Be("plugin");
        provenance[0].GetProperty("key").GetString().Should().Be("k1");
    }

    [Fact]
    public void RemoteIdentityScope_ShouldEmitBaselineCodes() {
        OspfRemoteModelSerializer.ToRemoteIdentityScope(ModelElementScope.ModelLocal)
            .Should().Be("MODEL_LOCAL");
        OspfRemoteModelSerializer.ToRemoteIdentityScope(ModelElementScope.Stable)
            .Should().Be("STABLE");
    }

    [Fact]
    public void SerializedSign_ShouldMapEveryRelation() {
        OspfRemoteModelSerializer.ToSerializedSign(
            Fuookami.Ospf.Core.Model.Basic.ConstraintRelation.LessEqual)
            .Should().Be(SerializedConstraintSign.LessEqual);
        OspfRemoteModelSerializer.ToSerializedSign(
            Fuookami.Ospf.Core.Model.Basic.ConstraintRelation.Equal)
            .Should().Be(SerializedConstraintSign.Equal);
        OspfRemoteModelSerializer.ToSerializedSign(
            Fuookami.Ospf.Core.Model.Basic.ConstraintRelation.GreaterEqual)
            .Should().Be(SerializedConstraintSign.GreaterEqual);
    }

    [Fact]
    public void SerializedLinearModel_ShouldCarryModelIdentity() {
        var model = new SerializedLinearModel(
            Name: "m",
            Variables: new List<SerializedVariable>(),
            Constraints: new List<SerializedConstraint>(),
            Objective: new SerializedObjective(
                Category: SerializedObjectiveCategory.Minimize,
                Cells: new List<SerializedObjectiveCell>()),
            IdentityNamespace: "ns",
            IdentitySchemaVersion: "1.0");

        using var document = JsonDocument.Parse(
            JsonSerializer.Serialize(model, RemoteWireJson.Options));

        document.RootElement.GetProperty("identityNamespace").GetString().Should().Be("ns");
        document.RootElement.GetProperty("identitySchemaVersion").GetString().Should().Be("1.0");
        document.RootElement.GetProperty("objective")
            .GetProperty("identityScope").GetString().Should().Be("MODEL_LOCAL");
    }

    /// <summary>
    /// 端到端：真实线性模型经 <c>Serialize</c> 后应得到基线的变量／约束／目标结构，空约束名回退
    /// <c>cons&lt;index&gt;</c>，且未设置身份时作用域为 <c>MODEL_LOCAL</c>。
    /// End to end: a real linear model serializes into the baseline's variable/constraint/objective
    /// structure, an empty constraint name falls back to <c>cons&lt;index&gt;</c>, and an unset identity
    /// yields the <c>MODEL_LOCAL</c> scope.
    /// </summary>
    [Fact]
    public async Task SerializeLinearModel_ShouldProduceBaselineStructure() {
        using var parent = new Fuookami.Ospf.Core.Model.Mechanism.LinearMetaModel<
            Fuookami.Ospf.Math.Algebra.Number.Flt64>(
            objectCategory: Fuookami.Ospf.Core.Model.Basic.ObjectCategory.Minimum);
        var x = new Fuookami.Ospf.Core.Variable.RealVar("x");
        var y = new Fuookami.Ospf.Core.Variable.RealVar("y");
        _ = parent.Add(new Fuookami.Ospf.Core.Variable.IVariableItem[] { x, y }).IsOk.Should().BeTrue();
        _ = parent.AddObject(
            category: Fuookami.Ospf.Core.Model.Basic.ObjectCategory.Minimum,
            flattenData: new Fuookami.Ospf.Core.Symbol.Flatten.LinearFlattenData<
                Fuookami.Ospf.Math.Algebra.Number.Flt64>(
                new[] {
                    new Fuookami.Ospf.Math.Symbol.Monomial.LinearMonomial<
                        Fuookami.Ospf.Math.Algebra.Number.Flt64>(
                        new Fuookami.Ospf.Math.Algebra.Number.Flt64(2.0), x),
                },
                new Fuookami.Ospf.Math.Algebra.Number.Flt64(5.0)),
            name: "objective",
            displayName: null).IsOk.Should().BeTrue();
        _ = parent.AddConstraint(
            relation: new Fuookami.Ospf.Math.Symbol.Inequality.LinearInequality<
                Fuookami.Ospf.Math.Algebra.Number.Flt64>(
                Lhs: new Fuookami.Ospf.Math.Symbol.Polynomial.LinearPolynomial<
                    Fuookami.Ospf.Math.Algebra.Number.Flt64>(
                    new[] {
                        new Fuookami.Ospf.Math.Symbol.Monomial.LinearMonomial<
                            Fuookami.Ospf.Math.Algebra.Number.Flt64>(
                            new Fuookami.Ospf.Math.Algebra.Number.Flt64(2.0), x),
                        new Fuookami.Ospf.Math.Symbol.Monomial.LinearMonomial<
                            Fuookami.Ospf.Math.Algebra.Number.Flt64>(
                            new Fuookami.Ospf.Math.Algebra.Number.Flt64(3.0), y),
                    },
                    Fuookami.Ospf.Math.Algebra.Number.Flt64.Zero),
                Rhs: new Fuookami.Ospf.Math.Symbol.Polynomial.LinearPolynomial<
                    Fuookami.Ospf.Math.Algebra.Number.Flt64>(
                    System.Array.Empty<Fuookami.Ospf.Math.Symbol.Monomial.LinearMonomial<
                        Fuookami.Ospf.Math.Algebra.Number.Flt64>>(),
                    new Fuookami.Ospf.Math.Algebra.Number.Flt64(10.0)),
                Comparison: Fuookami.Ospf.Math.Symbol.Inequality.Comparison.LE),
            group: null,
            name: string.Empty).IsOk.Should().BeTrue();

        using Fuookami.Ospf.Core.Model.Mechanism.LinearMechanismModel<
            Fuookami.Ospf.Math.Algebra.Number.Flt64> mechanism =
            (await Fuookami.Ospf.Core.Model.Mechanism.LinearMechanismModel<
                Fuookami.Ospf.Math.Algebra.Number.Flt64>.InvokeAsync(parent)).Value;
        using Fuookami.Ospf.Core.Model.Intermediate.LinearTriadModel triad =
            Fuookami.Ospf.Core.Model.Intermediate
                .LinearMechanismModelDumpExtensions.ToLinearTriadModel(mechanism).Value;

        Result<SerializedLinearModel, ErrorCode, Error<ErrorCode>> serialized =
            OspfRemoteModelSerializer.Serialize(triad);

        serialized.IsOk.Should().BeTrue();
        SerializedLinearModel model = serialized.Value;
        model.Variables.Should().HaveCount(2);
        model.Constraints.Should().ContainSingle();
        model.Constraints[0].Name.Should().Be("cons0",
            "空名称回退 cons<index> / an empty name falls back to cons<index>");
        model.Constraints[0].Sign.Should().Be(SerializedConstraintSign.LessEqual);
        model.Constraints[0].Rhs.Should().Be(10.0);
        model.Constraints[0].Cells.Should().HaveCount(2);
        model.Constraints[0].IdentityScope.Should().Be("MODEL_LOCAL");
        model.Objective.Category.Should().Be(SerializedObjectiveCategory.Minimize);
        model.Objective.Cells.Should().ContainSingle()
            .Which.Coefficient.Should().Be(2.0);
        model.Objective.Constant.Should().Be(5.0);
    }

    /// <summary>
    /// 二次约束单元的 wire 形状必须带**两个**列索引；此前 C# 只有一个 <c>colIndex</c>，根本无法表达
    /// 二次项。
    /// A quadratic constraint cell must carry **two** column indices on the wire; C# previously had a
    /// single <c>colIndex</c> and could not express a quadratic term at all.
    /// </summary>
    [Fact]
    public void SerializedQuadraticConstraintCell_ShouldCarryTwoColumnIndices() {
        var cell = new SerializedQuadraticConstraintCell(
            RowIndex: 2,
            ColIndex1: 3,
            ColIndex2: 7,
            Coefficient: 1.5);

        using var document = JsonDocument.Parse(
            JsonSerializer.Serialize(cell, RemoteWireJson.Options));
        JsonElement root = document.RootElement;

        root.GetProperty("rowIndex").GetInt32().Should().Be(2);
        root.GetProperty("colIndex1").GetInt32().Should().Be(3,
            "基线字段名是 colIndex1 / the baseline field is colIndex1");
        root.GetProperty("colIndex2").GetInt32().Should().Be(7);
        root.TryGetProperty("colIndex", out JsonElement _).Should().BeFalse(
            "不得写成单个 colIndex / a bare colIndex must not be emitted");
    }

    [Fact]
    public void SerializedQuadraticObjectiveCell_ShouldOmitRowIndex() {
        var cell = new SerializedQuadraticObjectiveCell(
            ColIndex1: 1,
            ColIndex2: 4,
            Coefficient: 2.5);

        using var document = JsonDocument.Parse(
            JsonSerializer.Serialize(cell, RemoteWireJson.Options));

        document.RootElement.TryGetProperty("rowIndex", out JsonElement _).Should().BeFalse(
            "目标单元无行索引 / an objective cell has no row index");
        document.RootElement.GetProperty("colIndex1").GetInt32().Should().Be(1);
        document.RootElement.GetProperty("colIndex2").GetInt32().Should().Be(4);
    }

    /// <summary>
    /// 线性约束与二次约束必须是两个独立列表——合并后线性约束会丢掉自己本来的形状。
    /// Linear and quadratic constraints must be two separate lists — merging loses the linear
    /// constraints' own shape.
    /// </summary>
    [Fact]
    public void SerializedQuadraticModel_ShouldKeepTwoConstraintLists() {
        var model = new SerializedQuadraticModel(
            Name: "qp",
            Variables: new List<SerializedVariable>(),
            LinearConstraints: new List<SerializedConstraint> {
                new(Name: "lin0", Sign: SerializedConstraintSign.Equal, Rhs: 1.0,
                    Cells: new List<SerializedConstraintCell>()),
            },
            QuadraticConstraints: new List<SerializedQuadraticConstraint> {
                new(Name: "quad0", Sign: SerializedConstraintSign.LessEqual, Rhs: 2.0,
                    LinearCells: new List<SerializedConstraintCell>(),
                    QuadraticCells: new List<SerializedQuadraticConstraintCell> {
                        new(RowIndex: 0, ColIndex1: 0, ColIndex2: 1, Coefficient: 3.0),
                    }),
            },
            Objective: new SerializedQuadraticObjective(
                Category: SerializedObjectiveCategory.Minimize,
                LinearCells: new List<SerializedObjectiveCell>(),
                QuadraticCells: new List<SerializedQuadraticObjectiveCell>()),
            IdentityNamespace: "ns",
            IdentitySchemaVersion: "1.0");

        using var document = JsonDocument.Parse(
            JsonSerializer.Serialize(model, RemoteWireJson.Options));
        JsonElement root = document.RootElement;

        root.GetProperty("linearConstraints").GetArrayLength().Should().Be(1);
        root.GetProperty("quadraticConstraints").GetArrayLength().Should().Be(1);
        root.TryGetProperty("constraints", out JsonElement _).Should().BeFalse(
            "不得再写成合并的 constraints / a merged constraints list must not be emitted");
        root.GetProperty("quadraticConstraints")[0]
            .GetProperty("quadraticCells")[0]
            .GetProperty("colIndex2").GetInt32().Should().Be(1);
        root.GetProperty("identityNamespace").GetString().Should().Be("ns");
    }

    /// <summary>
    /// 端到端：真实二次模型经 <c>Serialize</c> 后，同一约束行出现在两个列表里，前者的单元只含线性项、
    /// 后者同时含线性与二次项；二次项带两个列索引。
    /// End to end: for a real quadratic model the same row lands in both lists — the first with only its
    /// linear terms, the second with linear and quadratic terms together — and quadratic terms carry two
    /// column indices.
    /// </summary>
    [Fact]
    public async Task SerializeQuadraticModel_ShouldSplitCellsBySecondColumn() {
        using var parent = new Fuookami.Ospf.Core.Model.Mechanism.QuadraticMetaModel<
            Fuookami.Ospf.Math.Algebra.Number.Flt64>(
            objectCategory: Fuookami.Ospf.Core.Model.Basic.ObjectCategory.Minimum);
        var x = new Fuookami.Ospf.Core.Variable.RealVar("x");
        var y = new Fuookami.Ospf.Core.Variable.RealVar("y");
        _ = parent.Add(new Fuookami.Ospf.Core.Variable.IVariableItem[] { x, y }).IsOk.Should().BeTrue();

        static Fuookami.Ospf.Math.Symbol.Monomial.QuadraticMonomial<
            Fuookami.Ospf.Math.Algebra.Number.Flt64> Quadratic(
                double coefficient,
                Fuookami.Ospf.Core.Variable.IVariableItem first,
                Fuookami.Ospf.Core.Variable.IVariableItem? second = null) =>
            new(
                Coefficient: new Fuookami.Ospf.Math.Algebra.Number.Flt64(coefficient),
                Symbol1: first,
                Symbol2: second);

        _ = parent.AddObject(
            category: Fuookami.Ospf.Core.Model.Basic.ObjectCategory.Minimum,
            flattenData: new Fuookami.Ospf.Core.Symbol.Flatten.QuadraticFlattenData<
                Fuookami.Ospf.Math.Algebra.Number.Flt64>(
                new[] { Quadratic(1.0, x, x), Quadratic(2.0, y) },
                new Fuookami.Ospf.Math.Algebra.Number.Flt64(0.0)),
            name: "objective",
            displayName: null).IsOk.Should().BeTrue();
        _ = parent.AddConstraint(
            relation: new Fuookami.Ospf.Math.Symbol.Inequality.QuadraticInequalityOf<
                Fuookami.Ospf.Math.Algebra.Number.Flt64>(
                Lhs: new Fuookami.Ospf.Math.Symbol.Polynomial.QuadraticPolynomial<
                    Fuookami.Ospf.Math.Algebra.Number.Flt64>(
                    new[] { Quadratic(1.0, x, y), Quadratic(3.0, x) },
                    Fuookami.Ospf.Math.Algebra.Number.Flt64.Zero),
                Rhs: new Fuookami.Ospf.Math.Symbol.Polynomial.QuadraticPolynomial<
                    Fuookami.Ospf.Math.Algebra.Number.Flt64>(
                    System.Array.Empty<Fuookami.Ospf.Math.Symbol.Monomial.QuadraticMonomial<
                        Fuookami.Ospf.Math.Algebra.Number.Flt64>>(),
                    new Fuookami.Ospf.Math.Algebra.Number.Flt64(4.0)),
                Comparison: Fuookami.Ospf.Math.Symbol.Inequality.Comparison.LE),
            group: null,
            name: "xy").IsOk.Should().BeTrue();

        using Fuookami.Ospf.Core.Model.Mechanism.QuadraticMechanismModel<
            Fuookami.Ospf.Math.Algebra.Number.Flt64> mechanism =
            (await Fuookami.Ospf.Core.Model.Mechanism.QuadraticMechanismModel<
                Fuookami.Ospf.Math.Algebra.Number.Flt64>.InvokeAsync(parent)).Value;
        using Fuookami.Ospf.Core.Model.Intermediate.QuadraticTetradModel tetrad =
            Fuookami.Ospf.Core.Model.Intermediate.QuadraticMechanismModelDumpExtensions
                .ToQuadraticTetradModel(mechanism).Value;

        Result<SerializedQuadraticModel, ErrorCode, Error<ErrorCode>> serialized =
            OspfRemoteModelSerializer.Serialize(tetrad);

        serialized.IsOk.Should().BeTrue();
        SerializedQuadraticModel model = serialized.Value;
        model.LinearConstraints.Should().ContainSingle();
        model.QuadraticConstraints.Should().ContainSingle();
        model.LinearConstraints[0].Cells.Should().ContainSingle(
            "线性列表只含线性项 / the linear list carries only linear terms");
        model.QuadraticConstraints[0].QuadraticCells.Should().ContainSingle();
        model.QuadraticConstraints[0].QuadraticCells[0]
            .ColIndex2.Should().BeGreaterThanOrEqualTo(0);
        model.Objective.QuadraticCells.Should().ContainSingle(
            "目标里的二次项进入二次列表 / quadratic objective terms go to the quadratic list");
        model.Objective.LinearCells.Should().ContainSingle();
    }

    private static Fuookami.Ospf.Core.Model.ConstraintProgramming.ConstraintProgrammingModelSnapshot
        CpSnapshot(string name = "cp") {
        using var model = new Fuookami.Ospf.Core.Model.ConstraintProgramming.ConstraintProgrammingModel(
            name: name,
            objectCategory: Fuookami.Ospf.Core.Model.Basic.ObjectCategory.Minimum);
        var variable = new Fuookami.Ospf.Core.Variable.IntVar("x");
        _ = model.RegisterVariable(variable).Value;
        model.AddConstraint(
            constraint: Fuookami.Ospf.Core.Model.ConstraintProgramming
                .ConstraintProgrammingConstraint.CreateEqual(
                    Fuookami.Ospf.Core.Model.ConstraintProgramming
                        .ConstraintProgrammingExpression.CreateVariable(variable).Value,
                    new Fuookami.Ospf.Math.Algebra.Number.Int64(3)).Value,
            name: "x is three").IsOk.Should().BeTrue();
        return model.Snapshot().Value;
    }

    /// <summary>
    /// 配置指纹必须对拼接无歧义：长度前缀缺失时 <c>a</c>+<c>bc</c> 与 <c>ab</c>+<c>c</c> 会撞车。
    /// 这里用真实的键值对验证不同配置得到不同指纹，相同配置得到相同指纹。
    /// The configuration fingerprint must be unambiguous: without length prefixes <c>a</c>+<c>bc</c> and
    /// <c>ab</c>+<c>c</c> would collide. Different configurations must hash differently and identical ones
    /// identically.
    /// </summary>
    [Fact]
    public void ConfigurationFingerprint_ShouldBeStableAndDistinguishConfigurations() {
        string first = RemoteReportMapping.ConfigurationFingerprint(
            new SolverConfig(SolutionLimit: 5));
        string second = RemoteReportMapping.ConfigurationFingerprint(
            new SolverConfig(SolutionLimit: 5));
        string different = RemoteReportMapping.ConfigurationFingerprint(
            new SolverConfig(SolutionLimit: 6));

        first.Should().Be(second, "相同配置指纹稳定 / identical configurations hash identically");
        first.Should().NotBe(different);
        first.Length.Should().Be(64, "SHA-256 小写十六进制 / lower-case hex SHA-256");
    }

    /// <summary>
    /// 未指定线程数与显式指定默认值应得到相同指纹——后端行为就是"不指定用 8"。
    /// An unspecified thread count hashes the same as an explicit default, which is what the backend does.
    /// </summary>
    [Fact]
    public void ConfigurationFingerprint_ShouldTreatMissingThreadsAsDefault() {
        RemoteReportMapping.ConfigurationFingerprint(new SolverConfig(SolutionLimit: 1))
            .Should().Be(RemoteReportMapping.ConfigurationFingerprint(
                new SolverConfig(SolutionLimit: 1, Threads: 8)));
    }

    /// <summary>
    /// 模型指纹是**实例级**绑定而非内容哈希：snapshot 编码内嵌进程级变量标识（<c>0:0</c>、<c>1:0</c>），
    /// 因此同一 snapshot 实例稳定，而"内容相同但重新构建"的 snapshot 会得到不同指纹。
    ///
    /// 这不是缺陷，而是必须被理解的性质：调用方只有对自己**实际上传的那一份** snapshot 计算指纹，绑定才
    /// 成立；指纹也不能当作内容地址或跨进程比较。
    ///
    /// The model fingerprint is an **instance-level** binding rather than a content hash: the snapshot
    /// encoding embeds process-global variable identifiers (<c>0:0</c>, <c>1:0</c>), so one snapshot
    /// instance is stable while an identically-shaped, freshly built one differs. That is not a defect but
    /// a property the caller must understand: the binding holds only when the fingerprint is computed from
    /// the very snapshot that was uploaded, and the value is not a content address nor comparable across
    /// processes.
    /// </summary>
    [Fact]
    public void ModelFingerprint_ShouldBeStablePerInstanceButNotAcrossInstances() {
        Fuookami.Ospf.Core.Model.ConstraintProgramming.ConstraintProgrammingModelSnapshot snapshot =
            CpSnapshot();
        RemoteReportMapping.ModelFingerprint(snapshot).Value
            .Should().Be(RemoteReportMapping.ModelFingerprint(snapshot).Value,
                "同一 snapshot 实例的指纹稳定 / one snapshot instance hashes stably");
        RemoteReportMapping.ModelFingerprint(snapshot).Value.Length.Should().Be(64);
    }

    /// <summary>
    /// 防串台核心：来自别的 task／slice 的结果必须被拒绝，即使它自身完全自相一致。
    /// The cross-talk guard: a result from another task/slice must be rejected even when it is internally
    /// consistent.
    /// </summary>
    [Fact]
    public void ValidateExecutionBinding_ShouldRejectForeignTaskOrSlice() {
        Fuookami.Ospf.Core.Model.ConstraintProgramming.ConstraintProgrammingModelSnapshot snapshot =
            CpSnapshot();
        string model = RemoteReportMapping.ModelFingerprint(snapshot).Value;
        var configuration = new SolverConfig(SolutionLimit: 5);
        string config = RemoteReportMapping.ConfigurationFingerprint(configuration);

        SliceResult bound = CpSlice() with {
            RunId = "task",
            AttemptId = "slice",
            Fingerprints = new Dictionary<string, string> {
                ["model"] = model,
                ["configuration"] = config,
            },
        };
        RemoteReportMapping.ValidateExecutionBinding(
            slice: bound,
            snapshot: snapshot,
            expectedTaskId: "task",
            expectedSliceId: "slice",
            expectedConfigurationFingerprint: config).IsOk.Should().BeTrue();

        RemoteReportMapping.ValidateExecutionBinding(
            slice: bound with { RunId = "other-task" },
            snapshot: snapshot,
            expectedTaskId: "task",
            expectedSliceId: "slice",
            expectedConfigurationFingerprint: config).IsOk.Should().BeFalse(
            "别的 task / a different task");

        RemoteReportMapping.ValidateExecutionBinding(
            slice: bound with { AttemptId = "other-slice" },
            snapshot: snapshot,
            expectedTaskId: "task",
            expectedSliceId: "slice",
            expectedConfigurationFingerprint: config).IsOk.Should().BeFalse(
            "别的 slice / a different slice");
    }

    /// <summary>
    /// 后端把**别的模型**的结果发回来时必须被拦住——这正是模型指纹存在的理由。
    /// A result produced for a **different model** must be blocked — that is why the model fingerprint
    /// exists.
    /// </summary>
    [Fact]
    public void ValidateExecutionBinding_ShouldRejectForeignModel() {
        Fuookami.Ospf.Core.Model.ConstraintProgramming.ConstraintProgrammingModelSnapshot snapshot =
            CpSnapshot();
        string foreignModel = RemoteReportMapping.ModelFingerprint(CpSnapshot(name: "foreign")).Value;
        string config = RemoteReportMapping.ConfigurationFingerprint(new SolverConfig());

        SliceResult foreign = CpSlice() with {
            RunId = "task",
            AttemptId = "slice",
            Fingerprints = new Dictionary<string, string> {
                ["model"] = foreignModel,
                ["configuration"] = config,
            },
        };
        RemoteReportMapping.ValidateExecutionBinding(
            slice: foreign,
            snapshot: snapshot,
            expectedTaskId: "task",
            expectedSliceId: "slice",
            expectedConfigurationFingerprint: config).IsOk.Should().BeFalse(
            "模型指纹不符 / the model fingerprint disagrees");
    }

    [Fact]
    public void ValidateExecutionBinding_ShouldRequireFingerprintsWhenObservable() {
        Fuookami.Ospf.Core.Model.ConstraintProgramming.ConstraintProgrammingModelSnapshot snapshot =
            CpSnapshot();
        string config = RemoteReportMapping.ConfigurationFingerprint(new SolverConfig());

        RemoteReportMapping.ValidateExecutionBinding(
            slice: CpSlice() with { RunId = "task", AttemptId = "slice" },
            snapshot: snapshot,
            expectedTaskId: "task",
            expectedSliceId: "slice",
            expectedConfigurationFingerprint: config).IsOk.Should().BeFalse(
            "严格结果缺少模型指纹 / a strict result without the model fingerprint");

        SliceResult wrongConfig = CpSlice() with {
            RunId = "task",
            AttemptId = "slice",
            Fingerprints = new Dictionary<string, string> {
                ["model"] = RemoteReportMapping.ModelFingerprint(snapshot).Value,
                ["configuration"] = RemoteReportMapping.ConfigurationFingerprint(
                    new SolverConfig(SolutionLimit: 99)),
            },
        };
        RemoteReportMapping.ValidateExecutionBinding(
            slice: wrongConfig,
            snapshot: snapshot,
            expectedTaskId: "task",
            expectedSliceId: "slice",
            expectedConfigurationFingerprint: config).IsOk.Should().BeFalse(
            "配置指纹不符 / the configuration fingerprint disagrees");
    }

    /// <summary>
    /// CP snapshot 的变量标识必须是 <c>identifier:index</c> 形状，且变量清单与约束内引用的标识必须
    /// **完全一致**——不一致时后端无法把表达式解析回变量。
    ///
    /// 该标识来自进程级递增计数器（两侧同构），因此它只能保证同一进程内唯一，不是内容地址。
    /// A CP snapshot variable identifier must be shaped <c>identifier:index</c>, and the identifier in the
    /// variable list must match the one referenced inside a constraint **exactly** — otherwise the backend
    /// cannot resolve the expression back to its variable. The identifier comes from a process-global
    /// incrementing counter (isomorphic on both sides), so it is unique only within one process and is not
    /// a content address.
    /// </summary>
    [Fact]
    public void CpSnapshot_ShouldUseConsistentIdentifierIndexIds() {
        Fuookami.Ospf.Core.Model.ConstraintProgramming.ConstraintProgrammingModelSnapshot snapshot =
            CpSnapshot();

        string encoded = Fuookami.Ospf.Core.Model.ConstraintProgramming
            .ConstraintProgrammingSnapshotCodec.Encode(snapshot).Value;
        using var document = System.Text.Json.JsonDocument.Parse(encoded);
        System.Text.Json.JsonElement root = document.RootElement;

        string variableId = root.GetProperty("variables")[0].GetProperty("id").GetString()!;
        variableId.Should().MatchRegex(@"^\d+:\d+$",
            "标识形状为 identifier:index / the shape is identifier:index");
        root.GetProperty("constraints")[0]
            .GetProperty("constraint")
            .GetProperty("expression")
            .GetProperty("variableId")
            .GetString()
            .Should().Be(variableId,
                "约束内的变量标识必须与变量清单一致 / the constraint must reference the listed id");
    }

    private static void Fixture(string name, Func<string, string> edit, string target) =>
        System.IO.File.WriteAllText(
            target,
            edit(System.IO.File.ReadAllText(
                System.IO.Path.Combine(FixtureRoot(), name))));

    /// <summary>
    /// 无结果引用时返回 null 而不是错误——旧后端可能不下发 resultRef。
    /// With no result reference this returns null rather than an error: a legacy backend may omit
    /// resultRef.
    /// </summary>
    [Fact]
    public async Task ReadSerializedCpResult_ShouldReturnNullWithoutReference() {
        var storage = new RecordingStorage();
        Result<SerializedSolution?, ErrorCode, Error<ErrorCode>> result =
            await RemoteCpResultArtifact.ReadSerializedCpResultAsync(
                slice: CpSlice() with { ResultRef = null },
                storage: storage);

        result.IsOk.Should().BeTrue();
        result.Value.Should().BeNull();
        storage.ReadPath.Should().BeNull("不应读取存储 / storage must not be read");
    }

    [Fact]
    public async Task ReadSerializedCpResult_ShouldRejectReferenceWithoutStorage() {
        Result<SerializedSolution?, ErrorCode, Error<ErrorCode>> result =
            await RemoteCpResultArtifact.ReadSerializedCpResultAsync(
                slice: CpSlice() with {
                    ResultRef = new ObjectRef(Path: new ObjectPath("tenant/result/1")),
                    ArtifactDigest = "abc",
                },
                storage: null);

        result.IsOk.Should().BeFalse(
            "有引用却无存储无法读取 / a reference without storage cannot be read");
    }

    /// <summary>
    /// 严格 v2 artifact 必须带全部审计字段，缺字段时给出**具体缺了哪些**，而不是笼统报错。
    /// A strict v2 artifact must carry every audit field, and the error names **which** are missing rather
    /// than reporting a generic failure.
    /// </summary>
    [Fact]
    public async Task ReadSerializedCpResult_ShouldNameMissingAuditFields() {
        string json = System.IO.File.ReadAllText(
            System.IO.Path.Combine(FixtureRoot(), "remote-cp-result-v2.json"));
        using var document = JsonDocument.Parse(json);
        var trimmed = new Dictionary<string, object?>();
        foreach (JsonProperty property in document.RootElement.EnumerateObject()) {
            if (property.Name != "statistics") {
                trimmed[property.Name] = property.Value.Clone();
            }
        }
        string edited = JsonSerializer.Serialize(trimmed, RemoteWireJson.Options);

        var storage = new RecordingStorage();
        storage.Objects[new ObjectPath("tenant/result/1")] = edited;
        Result<SerializedSolution?, ErrorCode, Error<ErrorCode>> result =
            await RemoteCpResultArtifact.ReadSerializedCpResultAsync(
                slice: CpSlice() with {
                    ResultRef = new ObjectRef(Path: new ObjectPath("tenant/result/1")),
                    ArtifactDigest = "artifact-sha",
                },
                storage: storage);

        result.IsOk.Should().BeFalse();
        (result as Failed<SerializedSolution?, ErrorCode, Error<ErrorCode>>)!
            .Error.Message.Should().Contain("statistics",
                "错误必须指明缺失字段 / the error must name the missing field");
    }

    /// <summary>
    /// 工件的摘要与原始结果声明的摘要不一致时，说明拿到的不是这次引用的那一份工件。
    /// A digest mismatch means the loaded artifact is not the one this result referenced.
    /// </summary>
    [Fact]
    public async Task ReadSerializedCpResult_ShouldRejectDigestMismatch() {
        string json = System.IO.File.ReadAllText(
            System.IO.Path.Combine(FixtureRoot(), "remote-cp-result-v2.json"));
        var storage = new RecordingStorage();
        storage.Objects[new ObjectPath("tenant/result/1")] = json;

        Result<SerializedSolution?, ErrorCode, Error<ErrorCode>> result =
            await RemoteCpResultArtifact.ReadSerializedCpResultAsync(
                slice: CpSlice() with {
                    ResultRef = new ObjectRef(Path: new ObjectPath("tenant/result/1")),
                    ArtifactDigest = "a-different-digest",
                },
                storage: storage);

        result.IsOk.Should().BeFalse(
            "摘要不一致必须拒绝 / a digest mismatch must be rejected");
        (result as Failed<SerializedSolution?, ErrorCode, Error<ErrorCode>>)!
            .Error.Message.Should().Contain("摘要",
                "必须是摘要这条规则拒绝，不能是别的检查恰好拦下 / "
                + "the digest rule must be the one that rejects, not some other check");
    }

    [Fact]
    public async Task ReadSerializedCpResult_ShouldRejectMalformedArtifact() {
        var storage = new RecordingStorage();
        storage.Objects[new ObjectPath("tenant/result/1")] = "{not json";
        Result<SerializedSolution?, ErrorCode, Error<ErrorCode>> malformed =
            await RemoteCpResultArtifact.ReadSerializedCpResultAsync(
                slice: CpSlice() with {
                    ResultRef = new ObjectRef(Path: new ObjectPath("tenant/result/1")),
                    ArtifactDigest = "abc",
                },
                storage: storage);
        malformed.IsOk.Should().BeFalse();

        var empty = new RecordingStorage();
        empty.Objects[new ObjectPath("tenant/result/2")] = string.Empty;
        Result<SerializedSolution?, ErrorCode, Error<ErrorCode>> blank =
            await RemoteCpResultArtifact.ReadSerializedCpResultAsync(
                slice: CpSlice() with {
                    ResultRef = new ObjectRef(Path: new ObjectPath("tenant/result/2")),
                    ArtifactDigest = "abc",
                },
                storage: empty);
        blank.IsOk.Should().BeFalse("空对象等同不存在 / an empty object equals nonexistent");
    }

    /// <summary>
    /// 不可行 + INFEASIBLE + COMPLETED 才是有结论的"不可行"；其余不可行都是"未知"，
    /// 不能把"没算完"当成"无解"。
    /// Only infeasible with INFEASIBLE and COMPLETED is a concluded infeasibility; every other infeasible
    /// result is **unknown**, because "did not finish" must not be reported as "no solution exists".
    /// </summary>
    [Fact]
    public void Classify_ShouldSeparateProvenInfeasibilityFromUnknown() {
        Fuookami.Ospf.Core.Model.ConstraintProgramming.ConstraintProgrammingModelSnapshot snapshot =
            CpSnapshot();

        CpMaterialization proven = RemoteCpResultMaterializer.Classify(
            snapshot: snapshot,
            slice: CpSlice(
                feasible: false,
                problemStatus: RemoteProblemStatus.Infeasible,
                presence: RemoteSolutionPresence.None,
                proof: RemoteProofStatus.Verified),
            serialized: null).Value;
        proven.Should().BeOfType<CpMaterialization.Infeasible>();

        CpMaterialization interrupted = RemoteCpResultMaterializer.Classify(
            snapshot: snapshot,
            slice: CpSlice(
                feasible: false,
                problemStatus: RemoteProblemStatus.Unknown,
                presence: RemoteSolutionPresence.None,
                proof: RemoteProofStatus.None) with {
                TerminationReason = RemoteTerminationReason.TimeLimit,
            },
            serialized: null).Value;
        interrupted.Should().BeOfType<CpMaterialization.Unknown>(
            "超时不是无解 / a time limit is not infeasibility");
    }

    /// <summary>
    /// 模型里有变量却没有稳定解时必须报错，不能伪造空解。
    /// A model with variables but no stable solution must fail rather than fabricate an empty solution.
    /// </summary>
    [Fact]
    public void Classify_ShouldRejectMissingArtifactWhenModelIsNotEmpty() {
        Fuookami.Ospf.Core.Model.ConstraintProgramming.ConstraintProgrammingModelSnapshot snapshot =
            CpSnapshot();

        Result<CpMaterialization, ErrorCode, Error<ErrorCode>> result =
            RemoteCpResultMaterializer.Classify(
                snapshot: snapshot,
                slice: CpSlice(),
                serialized: null);

        result.IsOk.Should().BeFalse(
            "有变量却无解不能物化 / variables without a solution cannot be materialized");
    }

    /// <summary>
    /// 变量集合必须与 snapshot 完全一致：多一个或少一个都说明返回的解不是这个问题的解。
    /// The variable set must match the snapshot exactly: one extra or missing means the returned solution
    /// answers a different problem.
    /// </summary>
    [Fact]
    public void Classify_ShouldRejectVariableSetMismatch() {
        Fuookami.Ospf.Core.Model.ConstraintProgramming.ConstraintProgrammingModelSnapshot snapshot =
            CpSnapshot();
        string variableId = snapshot.Variables[0].Id.Value;

        Result<CpMaterialization, ErrorCode, Error<ErrorCode>> missing =
            RemoteCpResultMaterializer.Classify(
                snapshot: snapshot,
                slice: CpSlice(),
                serialized: CpSolution() with {
                    VariableValuesById = new Dictionary<string, long>(),
                });
        missing.IsOk.Should().BeFalse("缺变量 / a missing variable");

        Result<CpMaterialization, ErrorCode, Error<ErrorCode>> unknown =
            RemoteCpResultMaterializer.Classify(
                snapshot: snapshot,
                slice: CpSlice(),
                serialized: CpSolution() with {
                    VariableValuesById = new Dictionary<string, long> {
                        [variableId] = 3,
                        ["extra:1"] = 5,
                    },
                });
        unknown.IsOk.Should().BeFalse("多变量 / an unexpected variable");
    }

    /// <summary>
    /// 可行解应被完整物化：值、精确目标与最佳界都要落到分类结果上。
    ///
    /// 说明：本用例**没有**覆盖"值越界被拒"——夹具里的 `IntVar` 取整型全域，没有值会越界。越界拒绝的
    /// 分支已在实现中存在，但要用受限值域的变量才能真正触发，留待后续补夹具。
    /// A feasible solution should be materialized completely: values, exact objective and best bound all
    /// land on the classification. Note this case does **not** cover out-of-domain rejection — the fixture's
    /// `IntVar` spans the whole integer domain, so no value is out of domain. That branch exists in the
    /// implementation but needs a restricted-domain variable to actually trigger; the fixture is deferred to
    /// the next step.
    /// </summary>
    [Fact]
    public void Classify_ShouldMaterializeFeasibleSolution() {
        Fuookami.Ospf.Core.Model.ConstraintProgramming.ConstraintProgrammingModelSnapshot snapshot =
            CpSnapshot();
        string variableId = snapshot.Variables[0].Id.Value;

        CpMaterialization.Feasible feasible = RemoteCpResultMaterializer.Classify(
            snapshot: snapshot,
            slice: CpSlice() with { ObjectiveValueInt64 = null },
            // 夹具 snapshot 没有目标，因此不得报告目标值——否则会被"凭空冒出目标"规则拒绝。
            // The fixture snapshot has no objective, so none may be reported; otherwise the
            // "objective appeared from nowhere" rule rejects it.
            serialized: CpSolution() with {
                ObjectiveValueInt64 = null,
                VariableValuesById = new Dictionary<string, long> { [variableId] = 3 },
            }).Value as CpMaterialization.Feasible
            ?? throw new InvalidOperationException("expected a feasible classification");

        feasible.Solution.Values[snapshot.Variables[0].Id].ToLong().Should().Be(3);
        feasible.ExactObjective.Should().BeNull(
            "夹具没有目标，目标值必须为空 / the fixture has no objective, so it must be null");
    }

    /// <summary>
    /// 后端返回的解必须真正满足 snapshot 里的约束——防串台指纹只保证量的是同一份输入，
    /// 不保证后端算对了。
    /// The returned solution must actually satisfy the snapshot's constraints: the cross-talk
    /// fingerprints only prove the same input was measured, not that the backend computed correctly.
    /// </summary>
    [Fact]
    public void Classify_ShouldRejectSolutionViolatingConstraint() {
        Fuookami.Ospf.Core.Model.ConstraintProgramming.ConstraintProgrammingModelSnapshot snapshot =
            CpSnapshot();
        string variableId = snapshot.Variables[0].Id.Value;

        // 夹具约束是 "x == 3"，这里给 x = 4。
        // The fixture constrains x == 3; this supplies x = 4.
        Result<CpMaterialization, ErrorCode, Error<ErrorCode>> result =
            RemoteCpResultMaterializer.Classify(
                snapshot: snapshot,
                slice: CpSlice(),
                serialized: CpSolution() with {
                    ObjectiveValueInt64 = null,
                    VariableValuesById = new Dictionary<string, long> { [variableId] = 4 },
                });

        result.IsOk.Should().BeFalse("违反约束必须拒绝 / a violated constraint must be rejected");
        (result as Failed<CpMaterialization, ErrorCode, Error<ErrorCode>>)!
            .Error.Message.Should().Contain("约束",
                "错误必须指明是约束违约 / the error must name the constraint violation");
    }

    /// <summary>
    /// 报告的目标值必须等于用 snapshot 独立算出的值，不能采信后端自称的数字。
    /// The reported objective must equal the value computed independently from the snapshot; the backend's
    /// own number is not trusted.
    /// </summary>
    [Fact]
    public void Classify_ShouldRejectObjectiveDisagreeingWithSnapshot() {
        Fuookami.Ospf.Core.Model.ConstraintProgramming.ConstraintProgrammingModelSnapshot snapshot =
            CpSnapshot();
        string variableId = snapshot.Variables[0].Id.Value;

        Result<CpMaterialization, ErrorCode, Error<ErrorCode>> result =
            RemoteCpResultMaterializer.Classify(
                snapshot: snapshot,
                slice: CpSlice(),
                serialized: CpSolution() with {
                    ObjectiveValueInt64 = 999,
                    VariableValuesById = new Dictionary<string, long> { [variableId] = 3 },
                });

        result.IsOk.Should().BeFalse(
            "目标值与模型求值不一致必须拒绝 / a disagreeing objective must be rejected");
        (result as Failed<CpMaterialization, ErrorCode, Error<ErrorCode>>)!
            .Error.Message.Should().Contain("目标值");
    }

    /// <summary>
    /// 正确的解必须通过全套模型校验——否则上面两条拒绝可能只是因为别的检查先拦下了。
    /// A correct solution must pass full model validation, otherwise the two rejections above might only be
    /// some earlier check firing.
    /// </summary>
    [Fact]
    public void Classify_ShouldAcceptSolutionSatisfyingModel() {
        Fuookami.Ospf.Core.Model.ConstraintProgramming.ConstraintProgrammingModelSnapshot snapshot =
            CpSnapshot();
        string variableId = snapshot.Variables[0].Id.Value;

        Result<CpMaterialization, ErrorCode, Error<ErrorCode>> accepted =
            RemoteCpResultMaterializer.Classify(
                snapshot: snapshot,
                // 目标值可能来自 slice 或工件任一侧，夹具没有目标时两侧都必须为空。
                // The objective may come from either the slice or the artifact, so both must be empty when
                // the fixture has no objective.
                slice: CpSlice() with { ObjectiveValueInt64 = null },
                serialized: CpSolution() with {
                    ObjectiveValueInt64 = null,
                    VariableValuesById = new Dictionary<string, long> { [variableId] = 3 },
                });

        accepted.IsOk.Should().BeTrue(
            (accepted as Failed<CpMaterialization, ErrorCode, Error<ErrorCode>>)?.Error.Message
            ?? "满足模型的解必须被接受 / a solution satisfying the model must be accepted");
        accepted.Value.Should().BeOfType<CpMaterialization.Feasible>();
    }

    private static readonly IReadOnlySet<Fuookami.Ospf.Core.Solver.Report.SolverModelType>
        _linearModelTypes = new HashSet<Fuookami.Ospf.Core.Solver.Report.SolverModelType> {
            Fuookami.Ospf.Core.Solver.Report.SolverModelType.Lp,
            Fuookami.Ospf.Core.Solver.Report.SolverModelType.Mip,
        };

    /// <summary>
    /// 两条最容易写错的映射：UNKNOWN 与缺失都必须落到 BackendFailure，不能伪装成 Completed；
    /// 解存在性的 UNKNOWN 落到 None，不能伪装成有解。
    /// The two easiest mappings to get wrong: UNKNOWN and a missing reason must both become BackendFailure
    /// rather than masquerading as Completed, and an UNKNOWN presence must become None rather than
    /// masquerading as a solution.
    /// </summary>
    [Fact]
    public void ToRemoteSolveReport_ShouldNotDisguiseUnknownAsCompletion() {
        RemoteSolverReportMapping.ToCoreTerminationReason(RemoteTerminationReason.Unknown)
            .Should().Be(Fuookami.Ospf.Core.Solver.Report.TerminationReason.BackendFailure);
        RemoteSolverReportMapping.ToCoreTerminationReason(null)
            .Should().Be(Fuookami.Ospf.Core.Solver.Report.TerminationReason.BackendFailure,
                "缺失终止原因同样归为 BackendFailure / a missing reason is also BackendFailure");
        RemoteSolverReportMapping.ToCoreSolutionPresence(RemoteSolutionPresence.Unknown)
            .Should().Be(Fuookami.Ospf.Core.Solver.Report.SolutionPresence.None);
        RemoteSolverReportMapping.ToCoreProblemStatus(null)
            .Should().Be(Fuookami.Ospf.Core.Solver.Report.ProblemStatus.Unknown);
    }

    /// <summary>
    /// 报告以切片结果为骨架：证明、统计与指纹都要落上去。
    /// The report is built on the slice result: proof, statistics and fingerprints must all land on it.
    /// </summary>
    [Fact]
    public void ToRemoteSolveReport_ShouldCarryProofStatisticsAndFingerprints() {
        SliceResult slice = CpSlice() with {
            RunId = "task",
            ProofStatus = RemoteProofStatus.Verified,
            Provenance = new Dictionary<string, string> {
                ["proof.kind"] = "exhaustive",
                ["solverId"] = "scip",
                ["backend"] = "scip-jni",
                ["parameter.gap"] = "0.01",
                ["environment.os"] = "linux",
            },
            Fingerprints = new Dictionary<string, string> {
                ["model"] = "model-hash",
                ["configuration"] = "config-hash",
            },
            FingerprintSchemas = new Dictionary<string, string> {
                ["model"] = "1.0",
                ["configuration"] = "1.0",
            },
            Statistics = new Dictionary<string, string> {
                ["iterations"] = "42",
                ["bestBound"] = "3.5",
            },
        };

        Fuookami.Ospf.Core.Solver.Report.SolveReport<Flt64> report =
            RemoteSolverReportMapping.ToRemoteSolveReport(
                slice: slice,
                output: null,
                modelTypes: _linearModelTypes).Value;

        report.Proof.Status.Should().Be(Fuookami.Ospf.Core.Solver.Report.ProofStatus.Verified);
        report.Proof.Kind.Should().Be("exhaustive");
        report.Statistics.Iterations.Should().Be(42);
        report.Statistics.BestBound!.Value.ToDouble().Should().Be(3.5);
        report.RunId!.Value.Value.Should().Be("task");
        report.Provenance!.Descriptor.SolverId.Should().Be("scip");
        report.Provenance.Descriptor.BackendName.Should().Be("scip-jni");
        report.Provenance.EffectiveParameters.Should().ContainKey("gap");
        report.Provenance.EnvironmentSummary.Should().ContainKey("os");
        report.Fingerprints.Model!.Value.Should().Be("model-hash");
        report.Fingerprints.Configuration!.Value.Should().Be("config-hash");
        report.Fingerprints.Solver.Should().BeNull(
            "没有给出 solver 指纹就不能编造 / no solver fingerprint may be invented");
    }

    /// <summary>
    /// 后端没给来源信息时不编造一个来源——报告里应为 null。
    /// No provenance is invented when the backend supplied none; the report carries null.
    /// </summary>
    [Fact]
    public void ToRemoteSolveReport_ShouldNotInventProvenance() {
        Fuookami.Ospf.Core.Solver.Report.SolveReport<Flt64> report =
            RemoteSolverReportMapping.ToRemoteSolveReport(
                slice: CpSlice() with { Provenance = null },
                output: null,
                modelTypes: _linearModelTypes).Value;

        report.Provenance.Should().BeNull();
        report.Fingerprints.Model.Should().BeNull();
    }

    /// <summary>
    /// <c>SolveSliceAsync</c> 必须返回**未压缩**的终态切片：精简的 <c>SolveResult</c> 没有
    /// <c>ProblemStatus</c>／<c>SolutionPresence</c>／<c>ProofStatus</c>，任何要物化稳定解或组装审计报告的
    /// 调用方都无法基于它工作。
    /// <c>SolveSliceAsync</c> must return the **unreduced** terminal slice: the small <c>SolveResult</c> lacks
    /// <c>ProblemStatus</c>/<c>SolutionPresence</c>/<c>ProofStatus</c>, so no caller could materialize a stable
    /// solution or assemble an audit report from it.
    /// </summary>
    [Fact]
    public async Task SolveSliceAsync_ShouldReturnRichTerminalSlice() {
        using var transport = new SequencedTransport(
            new RemoteSolverHttpResponse(200, """
            {"code":"OK","data":{"schemaVersion":"1.0","protocolVersions":["2.0"],"supportedModelTypes":["CP"],"supportsPortableCheckpoint":true}}
            """),
            new RemoteSolverHttpResponse(200, """{"code":"OK","data":{"taskId":"task","accepted":true,"status":"QUEUED"}}"""),
            new RemoteSolverHttpResponse(200, """
            {"code":"OK","data":{"status":"COMPLETED","accepted":true,"feasible":true,"problemStatus":"FEASIBLE","solutionPresence":"OPTIMAL","proofStatus":"VERIFIED","runId":"task","attemptId":"slice"}}
            """));
        var storage = new RecordingStorage();
        var http = new RemoteSolverHttpClient(
            transport: transport.Send,
            baseUrl: "http://localhost",
            tenantId: new TenantId("tenant"),
            objectStoragePort: storage);
        var client = new RemoteSolverClient(http);

        Result<SliceResult, ErrorCode, Error<ErrorCode>> result = await client.SolveSliceAsync(
            payload: new SolvePayload(
                ModelData: ModelData.Raw(
                    bytes: System.Text.Encoding.UTF8.GetBytes("{}"),
                    format: "ospf-cp-snapshot-json"),
                TaskMeta: new TaskMeta(TargetType: new TargetTypeName("cp"))),
            taskId: new TaskId("task"),
            sliceId: new SliceId("slice"),
            nodeId: new NodeId("node"),
            tenantId: new TenantId("tenant"),
            quantum: TimeSpan.FromSeconds(1));

        result.IsOk.Should().BeTrue();
        SliceResult slice = result.Value;
        slice.Completed.Should().BeTrue();
        slice.ProblemStatus.Should().Be(RemoteProblemStatus.Feasible,
            "富切片必须保留问题状态 / the rich slice must keep the problem status");
        slice.SolutionPresence.Should().Be(RemoteSolutionPresence.Optimal);
        slice.ProofStatus.Should().Be(RemoteProofStatus.Verified);
        slice.RunId.Should().Be("task");
    }

    /// <summary>
    /// 旧版阻塞入口**不得**悄悄转发给本地求解器——那会让"远程求解器"在某些入口上变成本地求解而调用方
    /// 无从察觉。必须返回明确的结构化错误。
    /// The legacy blocking entry must **not** be silently forwarded to the local solver — that would make a
    /// "remote solver" run locally on some entries without the caller noticing. It must return an explicit
    /// structured error.
    /// </summary>
    [Fact]
    public async Task RemoteLinearSolver_ShouldRejectLegacyEntryInsteadOfGoingLocal() {
        var stub = new StubLinearSolver();
        var solver = new RemoteLinearSolver(
            delegateSolver: stub,
            remoteClient: new RemoteSolverClient(NoopExecutionPort.Instance));

        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> result =
            await solver.InvokeAsync(
                (Fuookami.Ospf.Core.Model.Intermediate.ILinearTriadModelView)LinearView());

        result.IsOk.Should().BeFalse();
        (result as Failed<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>)!
            .Error.Message.Should().Contain("InvokeReportAsync",
                "错误必须指向统一报告入口 / the error must point at the unified report entry");
        stub.InvokeCalls.Should().Be(0,
            "不得把旧入口转发给本地求解 / the legacy entry must not be forwarded to the local solver");
    }

    /// <summary>
    /// 名称、配置、描述符与诊断分析器必须转发给本地委托——它们是求解器身份的一部分，不该因远程化而丢失。
    /// Name, config, descriptor and diagnostic analyzers must forward to the local delegate: they are part
    /// of the solver's identity and must not be lost by going remote.
    /// </summary>
    [Fact]
    public void RemoteLinearSolver_ShouldForwardIdentityMembers() {
        var stub = new StubLinearSolver();
        var solver = new RemoteLinearSolver(
            delegateSolver: stub,
            remoteClient: new RemoteSolverClient(NoopExecutionPort.Instance));

        solver.Name.Should().Be("stub-linear");
        solver.Config.Should().BeSameAs(stub.Config);
        solver.Descriptor.Should().BeSameAs(stub.Descriptor);
    }

    private static Fuookami.Ospf.Core.Model.Intermediate.LinearTriadModel LinearView() {
        using Fuookami.Ospf.Core.Model.Mechanism.LinearMetaModel<
            Fuookami.Ospf.Math.Algebra.Number.Flt64> parent = new(
            objectCategory: Fuookami.Ospf.Core.Model.Basic.ObjectCategory.Minimum);
        Fuookami.Ospf.Core.Variable.IVariableItem x = new Fuookami.Ospf.Core.Variable.RealVar("x");
        _ = parent.Add(new[] { x }).IsOk.Should().BeTrue();
        _ = parent.AddObject(
            category: Fuookami.Ospf.Core.Model.Basic.ObjectCategory.Minimum,
            flattenData: new Fuookami.Ospf.Core.Symbol.Flatten.LinearFlattenData<
                Fuookami.Ospf.Math.Algebra.Number.Flt64>(
                new[] {
                    new Fuookami.Ospf.Math.Symbol.Monomial.LinearMonomial<
                        Fuookami.Ospf.Math.Algebra.Number.Flt64>(
                        new Fuookami.Ospf.Math.Algebra.Number.Flt64(2.0), x),
                },
                Fuookami.Ospf.Math.Algebra.Number.Flt64.Zero),
            name: "objective",
            displayName: null).IsOk.Should().BeTrue();
        _ = parent.AddConstraint(
            relation: new Fuookami.Ospf.Math.Symbol.Inequality.LinearInequality<
                Fuookami.Ospf.Math.Algebra.Number.Flt64>(
                Lhs: new Fuookami.Ospf.Math.Symbol.Polynomial.LinearPolynomial<
                    Fuookami.Ospf.Math.Algebra.Number.Flt64>(
                    new[] {
                        new Fuookami.Ospf.Math.Symbol.Monomial.LinearMonomial<
                            Fuookami.Ospf.Math.Algebra.Number.Flt64>(
                            new Fuookami.Ospf.Math.Algebra.Number.Flt64(1.0), x),
                    },
                    Fuookami.Ospf.Math.Algebra.Number.Flt64.Zero),
                Rhs: new Fuookami.Ospf.Math.Symbol.Polynomial.LinearPolynomial<
                    Fuookami.Ospf.Math.Algebra.Number.Flt64>(
                    System.Array.Empty<Fuookami.Ospf.Math.Symbol.Monomial.LinearMonomial<
                        Fuookami.Ospf.Math.Algebra.Number.Flt64>>(),
                    new Fuookami.Ospf.Math.Algebra.Number.Flt64(5.0)),
                Comparison: Fuookami.Ospf.Math.Symbol.Inequality.Comparison.LE),
            group: null,
            name: "cap").IsOk.Should().BeTrue();

        using Fuookami.Ospf.Core.Model.Mechanism.LinearMechanismModel<
            Fuookami.Ospf.Math.Algebra.Number.Flt64> mechanism =
            Fuookami.Ospf.Core.Model.Mechanism.LinearMechanismModel<
                Fuookami.Ospf.Math.Algebra.Number.Flt64>.InvokeAsync(parent)
                .GetAwaiter().GetResult().Value;
        return Fuookami.Ospf.Core.Model.Intermediate.LinearMechanismModelDumpExtensions
            .ToLinearTriadModel(mechanism).Value;
    }

    /// <summary>本地求解器桩 / Local solver stub.</summary>
    private sealed class StubLinearSolver : ILinearSolver {
        public int InvokeCalls { get; private set; }

        public string Name => "stub-linear";

        public Fuookami.Ospf.Core.Solver.Config.SolverConfig Config { get; } =
            new Fuookami.Ospf.Core.Solver.Config.SolverConfig();

        public SolverDescriptor Descriptor { get; } = new SolverDescriptor(
            SolverId: "stub",
            BackendName: "stub",
            Capabilities: new SolverCapabilities(
                ModelTypes: new HashSet<SolverModelType> { SolverModelType.Lp }));

        public Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> InvokeAsync(
            Fuookami.Ospf.Core.Model.Intermediate.ILinearTriadModelView model,
            SolvingStatusCallBack? solvingStatusCallBack = null,
            CancellationToken cancellationToken = default) {
            InvokeCalls++;
            return Task.FromResult(Results.Failed<FeasibleSolverOutput<Flt64>>(
                new Err<ErrorCode>(ErrorCode.Other, "local solver must not be used")));
        }

        public Task<Result<SolverOutput, ErrorCode, Error<ErrorCode>>> InvokeAsync(
            Fuookami.Ospf.Core.Model.Intermediate.LinearTriadModel model,
            SolvingStatusCallBack? solvingStatusCallBack = null,
            Fuookami.Ospf.Core.Solver.Iis.IisConfig? iisConfig = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Results.Failed<SolverOutput>(
                new Err<ErrorCode>(ErrorCode.Other, "unused")));

        public Task<Result<(FeasibleSolverOutput<Flt64> Output, List<List<Flt64>> Solutions),
            ErrorCode, Error<ErrorCode>>> InvokeAsync(
            Fuookami.Ospf.Core.Model.Intermediate.ILinearTriadModelView model,
            ulong solutionAmount,
            SolvingStatusCallBack? solvingStatusCallBack = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Results.Failed<
                (FeasibleSolverOutput<Flt64> Output, List<List<Flt64>> Solutions)>(
                new Err<ErrorCode>(ErrorCode.Other, "unused")));

        public Task<Result<Fuookami.Ospf.Core.Model.Intermediate.LinearTriadModel,
            ErrorCode, Error<ErrorCode>>> DumpAsync(
            Fuookami.Ospf.Core.Model.Mechanism.LinearMechanismModel<Flt64> model,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Results.Failed<Fuookami.Ospf.Core.Model.Intermediate.LinearTriadModel>(
                new Err<ErrorCode>(ErrorCode.Other, "unused")));

        public Task<Result<Fuookami.Ospf.Core.Model.Mechanism.LinearMechanismModel<Flt64>,
            ErrorCode, Error<ErrorCode>>> DumpAsync(
            Fuookami.Ospf.Core.Model.Mechanism.LinearMetaModel<Flt64> model,
            RegistrationStatusCallBack? registrationStatusCallBack,
            MechanismModelDumpingStatusCallBack? dumpingStatusCallBack,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Results.Failed<
                Fuookami.Ospf.Core.Model.Mechanism.LinearMechanismModel<Flt64>>(
                new Err<ErrorCode>(ErrorCode.Other, "unused")));
    }

    /// <summary>记录最后一次收到的取消令牌的执行端口 / Execution port recording the last token it saw.</summary>
    private sealed class RecordingExecutionPort : ISolverExecutionPort {
        public CancellationToken LastToken { get; private set; }

        public Task<Result<ExecutionHandle, ErrorCode, Error<ErrorCode>>> StartAsync(
            SolvePayload payload, TaskId taskId, SliceId sliceId, NodeId nodeId,
            TenantId tenantId, CancellationToken cancellationToken = default) {
            LastToken = cancellationToken;
            return Task.FromResult(Results.Failed<ExecutionHandle>(
                new Err<ErrorCode>(ErrorCode.Other, "stop here")));
        }

        public Task<Result<ExecutionHandle, ErrorCode, Error<ErrorCode>>> ResumeAsync(
            SolvePayload payload, ObjectRef snapshot, TaskId taskId, SliceId sliceId,
            NodeId nodeId, TenantId tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Results.Failed<ExecutionHandle>(
                new Err<ErrorCode>(ErrorCode.Other, "not used")));

        public Task<Result<SliceResult, ErrorCode, Error<ErrorCode>>> AwaitSliceEndAsync(
            ExecutionHandle handle, TimeSpan quantum,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Results.Failed<SliceResult>(
                new Err<ErrorCode>(ErrorCode.Other, "not used")));

        public Task<Result<ObjectRef?, ErrorCode, Error<ErrorCode>>> ExportCheckpointAsync(
            ExecutionHandle handle, CancellationToken cancellationToken = default) =>
            Task.FromResult(Results.Ok<ObjectRef?>(null));

        public Task<Result<SolveResult?, ErrorCode, Error<ErrorCode>>> FetchFinalResultAsync(
            ExecutionHandle handle, CancellationToken cancellationToken = default) =>
            Task.FromResult(Results.Ok<SolveResult?>(null));

        public Task<Result<bool, ErrorCode, Error<ErrorCode>>> StopAsync(
            ExecutionHandle handle, CancellationToken cancellationToken = default) =>
            Task.FromResult(Results.Ok(true));
    }

    /// <summary>不执行任何操作的执行端口 / An execution port that does nothing.</summary>
    private sealed class NoopExecutionPort : ISolverExecutionPort {
        public static readonly NoopExecutionPort Instance = new();

        public Task<Result<ExecutionHandle, ErrorCode, Error<ErrorCode>>> StartAsync(
            SolvePayload payload, TaskId taskId, SliceId sliceId, NodeId nodeId,
            TenantId tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Results.Failed<ExecutionHandle>(
                new Err<ErrorCode>(ErrorCode.Other, "not used")));

        public Task<Result<ExecutionHandle, ErrorCode, Error<ErrorCode>>> ResumeAsync(
            SolvePayload payload, ObjectRef snapshot, TaskId taskId, SliceId sliceId,
            NodeId nodeId, TenantId tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Results.Failed<ExecutionHandle>(
                new Err<ErrorCode>(ErrorCode.Other, "not used")));

        public Task<Result<SliceResult, ErrorCode, Error<ErrorCode>>> AwaitSliceEndAsync(
            ExecutionHandle handle, TimeSpan quantum,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Results.Failed<SliceResult>(
                new Err<ErrorCode>(ErrorCode.Other, "not used")));

        public Task<Result<ObjectRef?, ErrorCode, Error<ErrorCode>>> ExportCheckpointAsync(
            ExecutionHandle handle, CancellationToken cancellationToken = default) =>
            Task.FromResult(Results.Ok<ObjectRef?>(null));

        public Task<Result<SolveResult?, ErrorCode, Error<ErrorCode>>> FetchFinalResultAsync(
            ExecutionHandle handle, CancellationToken cancellationToken = default) =>
            Task.FromResult(Results.Ok<SolveResult?>(null));

        public Task<Result<bool, ErrorCode, Error<ErrorCode>>> StopAsync(
            ExecutionHandle handle, CancellationToken cancellationToken = default) =>
            Task.FromResult(Results.Ok(true));
    }

    /// <summary>
    /// 二次适配器的旧版入口同样不得悄悄走本地求解。
    /// The quadratic adapter's legacy entries likewise must not silently go local.
    /// </summary>
    [Fact]
    public async Task RemoteQuadraticSolver_ShouldRejectLegacyEntryInsteadOfGoingLocal() {
        var stub = new StubQuadraticSolver();
        var solver = new RemoteQuadraticSolver(
            delegateSolver: stub,
            remoteClient: new RemoteSolverClient(NoopExecutionPort.Instance));

        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> result =
            await solver.InvokeAsync(
                (Fuookami.Ospf.Core.Model.Intermediate.IQuadraticTetradModelView)QuadraticView());

        result.IsOk.Should().BeFalse();
        (result as Failed<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>)!
            .Error.Message.Should().Contain("InvokeReportAsync");
        stub.InvokeCalls.Should().Be(0);
    }

    [Fact]
    public void RemoteQuadraticSolver_ShouldForwardIdentityMembers() {
        var stub = new StubQuadraticSolver();
        var solver = new RemoteQuadraticSolver(
            delegateSolver: stub,
            remoteClient: new RemoteSolverClient(NoopExecutionPort.Instance));

        solver.Name.Should().Be("stub-quadratic");
        solver.Config.Should().BeSameAs(stub.Config);
    }

    /// <summary>
    /// 运行时配置必须真的驱动提交：注入固定的 task／slice／tenant 后，请求体里应出现这些值——否则
    /// "可注入"只是空话。
    /// Runtime options must actually drive the submission: after injecting fixed task/slice/tenant values they
    /// must appear in the request body, otherwise "injectable" is just words.
    /// </summary>
    [Fact]
    public void RemoteSolverRuntimeOptions_ShouldDefaultToUsableFallbacks() {
        var options = new RemoteSolverRuntimeOptions();

        options.TaskIdProvider().Value.Should().NotBeNullOrWhiteSpace();
        options.TaskIdProvider().Value.Should().NotBe(options.TaskIdProvider().Value,
            "默认每次都是新任务 / each default call is a new task");
        options.NodeId.Should().NotBeNullOrWhiteSpace();
        options.TenantId.Should().Be(RemoteSolverRuntimeOptions.DefaultTenantId);
        options.Quantum.Should().Be(RemoteSolverRuntimeOptions.DefaultQuantum);
        options.MaxRounds.Should().Be(RemoteSolverRuntimeOptions.DefaultMaxRounds);
    }

    [Fact]
    public void RemoteSolverRuntimeOptions_ShouldHonourInjectedValues() {
        var options = new RemoteSolverRuntimeOptions {
            TaskIdProvider = static () => new TaskId("fixed-task"),
            SliceIdProvider = static () => new SliceId("fixed-slice"),
            NodeId = "node-a",
            TenantId = "tenant-b",
            Quantum = TimeSpan.FromSeconds(2),
            MaxRounds = 7,
        };

        options.TaskIdProvider().Value.Should().Be("fixed-task");
        options.SliceIdProvider().Value.Should().Be("fixed-slice");
        options.NodeId.Should().Be("node-a");
        options.TenantId.Should().Be("tenant-b");
        options.Quantum.Should().Be(TimeSpan.FromSeconds(2));
        options.MaxRounds.Should().Be(7UL);
    }

    private static Fuookami.Ospf.Core.Model.Intermediate.QuadraticTetradModel QuadraticView() {
        using Fuookami.Ospf.Core.Model.Mechanism.QuadraticMetaModel<
            Fuookami.Ospf.Math.Algebra.Number.Flt64> parent = new(
            objectCategory: Fuookami.Ospf.Core.Model.Basic.ObjectCategory.Minimum);
        Fuookami.Ospf.Core.Variable.IVariableItem x = new Fuookami.Ospf.Core.Variable.RealVar("qx");
        _ = parent.Add(new[] { x }).IsOk.Should().BeTrue();
        _ = parent.AddObject(
            category: Fuookami.Ospf.Core.Model.Basic.ObjectCategory.Minimum,
            flattenData: new Fuookami.Ospf.Core.Symbol.Flatten.QuadraticFlattenData<
                Fuookami.Ospf.Math.Algebra.Number.Flt64>(
                new[] {
                    new Fuookami.Ospf.Math.Symbol.Monomial.QuadraticMonomial<
                        Fuookami.Ospf.Math.Algebra.Number.Flt64>(
                        Coefficient: new Fuookami.Ospf.Math.Algebra.Number.Flt64(1.0),
                        Symbol1: x),
                },
                Fuookami.Ospf.Math.Algebra.Number.Flt64.Zero),
            name: "objective",
            displayName: null).IsOk.Should().BeTrue();

        using Fuookami.Ospf.Core.Model.Mechanism.QuadraticMechanismModel<
            Fuookami.Ospf.Math.Algebra.Number.Flt64> mechanism =
            Fuookami.Ospf.Core.Model.Mechanism.QuadraticMechanismModel<
                Fuookami.Ospf.Math.Algebra.Number.Flt64>.InvokeAsync(parent)
                .GetAwaiter().GetResult().Value;
        return Fuookami.Ospf.Core.Model.Intermediate.QuadraticMechanismModelDumpExtensions
            .ToQuadraticTetradModel(mechanism).Value;
    }

    /// <summary>本地二次求解器桩 / Local quadratic solver stub.</summary>
    private sealed class StubQuadraticSolver : IQuadraticSolver {
        public int InvokeCalls { get; private set; }

        public string Name => "stub-quadratic";

        public Fuookami.Ospf.Core.Solver.Config.SolverConfig Config { get; } =
            new Fuookami.Ospf.Core.Solver.Config.SolverConfig();

        public Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>> InvokeAsync(
            Fuookami.Ospf.Core.Model.Intermediate.IQuadraticTetradModelView model,
            SolvingStatusCallBack? solvingStatusCallBack = null,
            CancellationToken cancellationToken = default) {
            InvokeCalls++;
            return Task.FromResult(Results.Failed<FeasibleSolverOutput<Flt64>>(
                new Err<ErrorCode>(ErrorCode.Other, "local solver must not be used")));
        }

        public Task<Result<SolverOutput, ErrorCode, Error<ErrorCode>>> InvokeAsync(
            Fuookami.Ospf.Core.Model.Intermediate.IQuadraticTetradModelView model,
            SolvingStatusCallBack? solvingStatusCallBack,
            Fuookami.Ospf.Core.Solver.Iis.IisConfig? iisConfig,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Results.Failed<SolverOutput>(
                new Err<ErrorCode>(ErrorCode.Other, "unused")));

        public Task<Result<(FeasibleSolverOutput<Flt64> Output, List<List<Flt64>> Solutions),
            ErrorCode, Error<ErrorCode>>> InvokeAsync(
            Fuookami.Ospf.Core.Model.Intermediate.IQuadraticTetradModelView model,
            ulong solutionAmount,
            SolvingStatusCallBack? solvingStatusCallBack = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Results.Failed<
                (FeasibleSolverOutput<Flt64> Output, List<List<Flt64>> Solutions)>(
                new Err<ErrorCode>(ErrorCode.Other, "unused")));

        public Task<Result<Fuookami.Ospf.Core.Model.Intermediate.QuadraticTetradModel,
            ErrorCode, Error<ErrorCode>>> DumpAsync(
            Fuookami.Ospf.Core.Model.Mechanism.QuadraticMechanismModel<Flt64> model,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Results.Failed<Fuookami.Ospf.Core.Model.Intermediate.QuadraticTetradModel>(
                new Err<ErrorCode>(ErrorCode.Other, "unused")));

        public Task<Result<Fuookami.Ospf.Core.Model.Mechanism.QuadraticMechanismModel<Flt64>,
            ErrorCode, Error<ErrorCode>>> DumpAsync(
            Fuookami.Ospf.Core.Model.Mechanism.QuadraticMetaModel<Flt64> model,
            RegistrationStatusCallBack? registrationStatusCallBack,
            MechanismModelDumpingStatusCallBack? dumpingStatusCallBack,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Results.Failed<
                Fuookami.Ospf.Core.Model.Mechanism.QuadraticMechanismModel<Flt64>>(
                new Err<ErrorCode>(ErrorCode.Other, "unused")));
    }

    /// <summary>
    /// 三态必须映射到三个不同的 Core 输出记录——用同一个记录表达三态会让下游无法区分"无解"与"未知"。
    /// The three states must map to three distinct Core output records; collapsing them would leave downstream
    /// unable to tell "no solution" from "unknown".
    /// </summary>
    [Fact]
    public void ToOutput_ShouldMapThreeStatesToDistinctRecords() {
        Fuookami.Ospf.Core.Model.ConstraintProgramming.ConstraintProgrammingModelSnapshot snapshot =
            CpSnapshot();
        string variableId = snapshot.Variables[0].Id.Value;

        CpMaterialization proven = RemoteCpResultMaterializer.Classify(
            snapshot: snapshot,
            slice: CpSlice(
                feasible: false,
                problemStatus: RemoteProblemStatus.Infeasible,
                presence: RemoteSolutionPresence.None,
                proof: RemoteProofStatus.Verified),
            serialized: null).Value;
        RemoteCpResultMaterializer.ToOutput(proven, CpSlice()).Value
            .Should().BeOfType<ConstraintProgrammingInfeasibleOutput>();

        CpMaterialization interrupted = RemoteCpResultMaterializer.Classify(
            snapshot: snapshot,
            slice: CpSlice(
                feasible: false,
                problemStatus: RemoteProblemStatus.Unknown,
                presence: RemoteSolutionPresence.None,
                proof: RemoteProofStatus.None) with {
                TerminationReason = RemoteTerminationReason.TimeLimit,
            },
            serialized: null).Value;
        var unknown =
            (ConstraintProgrammingUnknownOutput)RemoteCpResultMaterializer
                .ToOutput(interrupted, CpSlice()).Value;
        unknown.TerminationReason.Should()
            .Be(Fuookami.Ospf.Core.Solver.Report.TerminationReason.TimeLimit);

        CpMaterialization feasible = RemoteCpResultMaterializer.Classify(
            snapshot: snapshot,
            slice: CpSlice() with { ObjectiveValueInt64 = null },
            serialized: CpSolution() with {
                ObjectiveValueInt64 = null,
                VariableValuesById = new Dictionary<string, long> { [variableId] = 3 },
            }).Value;
        RemoteCpResultMaterializer.ToOutput(feasible, CpSlice()).Value
            .Should().BeOfType<ConstraintProgrammingFeasibleOutput>();
    }

    /// <summary>
    /// 声称最优但**没有证明**时必须降级为 <c>Feasible</c>，不能照抄后端的乐观说法。
    /// An unproven optimality claim must be downgraded to <c>Feasible</c> rather than copied from the
    /// backend's optimism.
    /// </summary>
    [Fact]
    public void ToOutput_ShouldDowngradeUnprovenOptimality() {
        var feasible = new CpMaterialization.Feasible(
            Solution: new ConstraintProgrammingSolution(
                new Dictionary<VariableId, Fuookami.Ospf.Math.Algebra.Number.Int64>(),
                new Dictionary<
                    Fuookami.Ospf.Core.Model.ConstraintProgramming.IntervalId,
                    Fuookami.Ospf.Core.Model.ConstraintProgramming.IntervalValue>()),
            ExactObjective: 5,
            BestBound: null,
            ProvenOptimal: false);

        var output =
            (ConstraintProgrammingFeasibleOutput)RemoteCpResultMaterializer
                .ToOutput(feasible, CpSlice()).Value;

        output.Status.Should().Be(SolverStatus.Feasible,
            "未证明的最优必须降级 / an unproven optimum must be downgraded");
        output.ExactObjective!.Value.ToLong().Should().Be(5);
    }

    /// <summary>
    /// 值域越界必须被拒绝——这条分支此前只存在于实现中、没有夹具真正触发（变量取整型全域时没有值会越界）。
    /// 这里用受限值域 <c>[0, 2]</c> 的变量，给一个 5。
    ///
    /// An out-of-domain value must be rejected; this branch previously existed only in the implementation with
    /// no fixture able to trigger it (a variable spanning the whole integer domain has no out-of-domain
    /// value). This uses a variable restricted to <c>[0, 2]</c> and supplies 5.
    /// </summary>
    [Fact]
    public void Classify_ShouldRejectValueOutsideDomain() {
        using var model = new Fuookami.Ospf.Core.Model.ConstraintProgramming.ConstraintProgrammingModel(
            name: "domain-cp",
            objectCategory: Fuookami.Ospf.Core.Model.Basic.ObjectCategory.Minimum);
        var variable = new Fuookami.Ospf.Core.Variable.IntVar("bounded");
        Fuookami.Ospf.Core.Model.ConstraintProgramming.IntegerDomain.Interval domain =
            Fuookami.Ospf.Core.Model.ConstraintProgramming.IntegerDomain.CreateInterval(
                new Fuookami.Ospf.Math.Algebra.Number.Int64(0),
                new Fuookami.Ospf.Math.Algebra.Number.Int64(2)).Value;
        _ = model.RegisterVariable(variable, domain).Value;
        Fuookami.Ospf.Core.Model.ConstraintProgramming.ConstraintProgrammingModelSnapshot snapshot =
            model.Snapshot().Value;
        string variableId = snapshot.Variables[0].Id.Value;

        Result<CpMaterialization, ErrorCode, Error<ErrorCode>> inside =
            RemoteCpResultMaterializer.Classify(
                snapshot: snapshot,
                slice: CpSlice() with { ObjectiveValueInt64 = null },
                serialized: CpSolution() with {
                    ObjectiveValueInt64 = null,
                    VariableValuesById = new Dictionary<string, long> { [variableId] = 2 },
                });
        inside.IsOk.Should().BeTrue(
            "值域内必须通过，否则越界用例可能是因别的原因失败 / an in-domain value must pass, otherwise the "
            + "out-of-domain case could fail for some other reason");

        Result<CpMaterialization, ErrorCode, Error<ErrorCode>> outside =
            RemoteCpResultMaterializer.Classify(
                snapshot: snapshot,
                slice: CpSlice() with { ObjectiveValueInt64 = null },
                serialized: CpSolution() with {
                    ObjectiveValueInt64 = null,
                    VariableValuesById = new Dictionary<string, long> { [variableId] = 5 },
                });

        outside.IsOk.Should().BeFalse("越界必须拒绝 / an out-of-domain value must be rejected");
        (outside as Failed<CpMaterialization, ErrorCode, Error<ErrorCode>>)!
            .Error.Message.Should().Contain("值域",
                "错误必须指明是值域问题 / the error must name the domain");
    }

    /// <summary>
    /// 取消令牌必须真正传到执行端口，而不是被吞在中间层。
    /// The cancellation token must actually reach the execution port rather than being swallowed in the middle.
    /// </summary>
    [Fact]
    public async Task SolveSliceAsync_ShouldForwardCancellationToken() {
        using var source = new CancellationTokenSource();
        var port = new RecordingExecutionPort();

        _ = await new RemoteSolverClient(port).SolveSliceAsync(
            payload: new SolvePayload(
                ModelData: ModelData.Raw(
                    bytes: System.Text.Encoding.UTF8.GetBytes("{}"),
                    format: "ospf-cp-snapshot-json"),
                TaskMeta: new TaskMeta(TargetType: new TargetTypeName("cp"))),
            taskId: new TaskId("task"),
            sliceId: new SliceId("slice"),
            nodeId: new NodeId("node"),
            tenantId: new TenantId("tenant"),
            quantum: TimeSpan.FromSeconds(1),
            cancellationToken: source.Token);

        port.LastToken.Should().Be(source.Token,
            "取消令牌必须传到端口 / the token must reach the port");
    }

    /// <summary>
    /// 成功路径端到端：提交 → 读工件 → 模型重验通过 → 产出 <c>Feasible</c> 输出。
    /// Happy path end to end: submit, read the artifact, pass the model re-validation, emit a
    /// <c>Feasible</c> output.
    ///
    /// 夹具约束是 <c>x == 3</c>、模型无目标，因此解给 <c>x = 3</c> 且目标必须为**显式 null**——工件里删掉
    /// 目标键会因缺少审计字段被拒，给 0 又会因"无目标却返回目标值"被拒，只有显式 null 同时满足两边。
    /// The fixture constrains <c>x == 3</c> with no objective, so the solution supplies <c>x = 3</c> and the
    /// objective must be an **explicit null**: dropping the key fails the audit-field check, and supplying 0
    /// fails the "objective returned for an objective-less snapshot" rule — only an explicit null satisfies
    /// both.
    /// </summary>
    [Fact]
    public async Task SolveOutputAsync_ShouldProduceFeasibleOutputOnHappyPath() {
        Fuookami.Ospf.Core.Model.ConstraintProgramming.ConstraintProgrammingModelSnapshot snapshot =
            CpSnapshot();
        string variableId = snapshot.Variables[0].Id.Value;
        string config = RemoteReportMapping.ConfigurationFingerprint(new SolverConfig());
        string modelFp = RemoteReportMapping.ModelFingerprint(snapshot).Value;

        System.Text.Json.Nodes.JsonObject artifactNode =
            System.Text.Json.Nodes.JsonNode.Parse(System.IO.File.ReadAllText(
                System.IO.Path.Combine(FixtureRoot(), "remote-cp-result-v2.json")))!.AsObject();
        artifactNode["runId"] = "task";
        artifactNode["attemptId"] = "slice";
        artifactNode["artifactDigest"] = "digest-1";
        artifactNode["objectiveValue"] = null;
        artifactNode["objectiveValueInt64"] = null;
        artifactNode["feasible"] = true;
        artifactNode["optimal"] = false;
        artifactNode["solutionPresence"] = "INCUMBENT";
        artifactNode["problemStatus"] = "FEASIBLE";
        artifactNode["fingerprints"] = new System.Text.Json.Nodes.JsonObject {
            ["model"] = modelFp,
            ["configuration"] = config,
        };
        artifactNode["fingerprintSchemas"] = new System.Text.Json.Nodes.JsonObject {
            ["model"] = "sha256",
            ["configuration"] = "sha256",
        };
        artifactNode["variableValues"] = null;
        artifactNode["intervalValues"] = null;
        artifactNode["variableValuesById"] = new System.Text.Json.Nodes.JsonObject {
            [variableId] = 3,
        };

        var storage = new RecordingStorage();
        storage.Objects[new ObjectPath("result/1")] = artifactNode.ToJsonString();

        using var transport = new SequencedTransport(
            new RemoteSolverHttpResponse(200, """
            {"code":"OK","data":{"schemaVersion":"1.0","protocolVersions":["2.0"],"supportedModelTypes":["CP"],"supportsPortableCheckpoint":true}}
            """),
            new RemoteSolverHttpResponse(200, """{"code":"OK","data":{"taskId":"task","accepted":true,"status":"QUEUED"}}"""),
            new RemoteSolverHttpResponse(200, """
            {"code":"OK","data":{"status":"COMPLETED","accepted":true,"feasible":true,"problemStatus":"FEASIBLE","solutionPresence":"INCUMBENT","proofStatus":"NONE","terminationReason":"TIME_LIMIT","runId":"task","attemptId":"slice","schemaVersion":"2.0","latestResultRef":{"path":"result/1"},"artifactDigest":"digest-1","fingerprints":{"model":"MODEL_FP","configuration":"CONFIG_FP"},"fingerprintSchemas":{"model":"sha256","configuration":"sha256"}}}
            """.Replace("MODEL_FP", modelFp).Replace("CONFIG_FP", config)));
        var http = new RemoteSolverHttpClient(
            transport: transport.Send,
            baseUrl: "http://localhost",
            tenantId: new TenantId("tenant"),
            objectStoragePort: storage);
        var client = new RemoteConstraintProgrammingClient(http, storage);

        Result<ConstraintProgrammingSolverOutput, ErrorCode, Error<ErrorCode>> result =
            await client.SolveOutputAsync(
                snapshot: snapshot,
                taskId: new TaskId("task"),
                sliceId: new SliceId("slice"),
                nodeId: new NodeId("node"),
                tenantId: new TenantId("tenant"),
                quantum: TimeSpan.FromSeconds(1));

        result.IsOk.Should().BeTrue(
            "自一致的端到端路径必须产出输出 / a self-consistent end-to-end path must produce an output; got: "
            + (result as Failed<ConstraintProgrammingSolverOutput, ErrorCode, Error<ErrorCode>>)
                ?.Error.Message);
        ConstraintProgrammingFeasibleOutput output =
            result.Value.Should().BeOfType<ConstraintProgrammingFeasibleOutput>().Subject;
        output.Solution.Value(new VariableId(variableId))
            .Value.ToLong().Should().Be(3,
                "解必须是远端下发的那个值 / the solution must carry the value the remote supplied");
        output.Status.Should().Be(SolverStatus.Feasible);
        output.Report.Should().NotBeNull("报告必须一并产出 / the report must be produced too");
    }

    /// <summary>
    /// 提交失败必须原样上抛，不得降级成"成功但没有解"。
    /// A submission failure must propagate, never degrade into "succeeded without a solution".
    /// </summary>
    [Fact]
    public async Task SolveOutputAsync_ShouldPropagateSubmissionFailure() {
        Fuookami.Ospf.Core.Model.ConstraintProgramming.ConstraintProgrammingModelSnapshot snapshot =
            CpSnapshot();

        using var transport = new SequencedTransport(
            new RemoteSolverHttpResponse(200, """
            {"code":"OK","data":{"schemaVersion":"1.0","protocolVersions":["2.0"],"supportedModelTypes":["CP"],"supportsPortableCheckpoint":true}}
            """),
            new RemoteSolverHttpResponse(500, """{"code":"INTERNAL","message":"boom"}"""));
        var http = new RemoteSolverHttpClient(
            transport: transport.Send,
            baseUrl: "http://localhost",
            tenantId: new TenantId("tenant"));
        var client = new RemoteConstraintProgrammingClient(http);

        Result<ConstraintProgrammingSolverOutput, ErrorCode, Error<ErrorCode>> result =
            await client.SolveOutputAsync(
                snapshot: snapshot,
                taskId: new TaskId("task"),
                sliceId: new SliceId("slice"),
                nodeId: new NodeId("node"),
                tenantId: new TenantId("tenant"),
                quantum: TimeSpan.FromSeconds(1));

        result.IsOk.Should().BeFalse("提交失败必须上抛 / a submission failure must propagate");
        (result as Failed<ConstraintProgrammingSolverOutput, ErrorCode, Error<ErrorCode>>)!
            .Error.Message.Should().Contain("终态切片",
                "必须明确指出拿不到终态切片 / it must say no terminal slice was obtained");
    }

    /// <summary>
    /// 工件不可读时必须失败——不能因为"读不到工件"就降级成"没有解"。
    /// An unreadable artifact must fail: "cannot read the artifact" must not degrade into "no solution".
    /// </summary>
    [Fact]
    public async Task SolveOutputAsync_ShouldFailWhenArtifactIsUnreadable() {
        Fuookami.Ospf.Core.Model.ConstraintProgramming.ConstraintProgrammingModelSnapshot snapshot =
            CpSnapshot();
        string config = RemoteReportMapping.ConfigurationFingerprint(new SolverConfig());
        string modelFp = RemoteReportMapping.ModelFingerprint(snapshot).Value;

        using var transport = new SequencedTransport(
            new RemoteSolverHttpResponse(200, """
            {"code":"OK","data":{"schemaVersion":"1.0","protocolVersions":["2.0"],"supportedModelTypes":["CP"],"supportsPortableCheckpoint":true}}
            """),
            new RemoteSolverHttpResponse(200, """{"code":"OK","data":{"taskId":"task","accepted":true,"status":"QUEUED"}}"""),
            new RemoteSolverHttpResponse(200, """
            {"code":"OK","data":{"status":"COMPLETED","accepted":true,"feasible":true,"problemStatus":"FEASIBLE","solutionPresence":"INCUMBENT","proofStatus":"CLAIMED","objectiveValueInt64":0,"gap":0,"runId":"task","attemptId":"slice","schemaVersion":"1.0","latestResultRef":{"path":"result/missing"},"artifactDigest":"digest-1","gap":0.5,"fingerprints":{"model":"MODEL_FP","configuration":"CONFIG_FP"},"fingerprintSchemas":{"model":"sha256","configuration":"sha256"}}}
            """.Replace("MODEL_FP", modelFp).Replace("CONFIG_FP", config)));
        var storage = new RecordingStorage();
        var http = new RemoteSolverHttpClient(
            transport: transport.Send,
            baseUrl: "http://localhost",
            tenantId: new TenantId("tenant"),
            objectStoragePort: storage);
        var client = new RemoteConstraintProgrammingClient(http, storage);

        Result<ConstraintProgrammingSolverOutput, ErrorCode, Error<ErrorCode>> result =
            await client.SolveOutputAsync(
                snapshot: snapshot,
                taskId: new TaskId("task"),
                sliceId: new SliceId("slice"),
                nodeId: new NodeId("node"),
                tenantId: new TenantId("tenant"),
                quantum: TimeSpan.FromSeconds(1));

        result.IsOk.Should().BeFalse("工件不可读必须失败 / an unreadable artifact must fail");
        (result as Failed<ConstraintProgrammingSolverOutput, ErrorCode, Error<ErrorCode>>)!
            .Error.Message.Should().Contain("工件",
                "必须是被工件读取拦下 / it must be the artifact read that rejects");
    }

    private static SerializedSolution LinearSolution(
        int variableCount = 2,
        double objective = 7.5) =>
        new() {
            Feasible = true,
            Optimal = true,
            ObjectiveValue = objective,
            VariableValues = Enumerable.Range(0, variableCount)
                .Select(index => (double)index).ToList(),
            ProblemStatus = RemoteProblemStatus.Feasible,
            SolutionPresence = RemoteSolutionPresence.Optimal,
            ProofStatus = RemoteProofStatus.Verified,
            TerminationReason = RemoteTerminationReason.Completed,
            SchemaVersion = "1.0",
        };

    /// <summary>
    /// 解长度必须等于模型变量数——长度不符说明拿到的解不是这个问题，不能截断或补零。
    /// The solution length must equal the model's variable count: a mismatch means the solution answers
    /// another problem and must not be truncated or zero-padded.
    /// </summary>
    [Fact]
    public void ToIncumbentReport_ShouldRejectVariableCountMismatch() {
        Result<Fuookami.Ospf.Core.Solver.Report.SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>
            result = RemoteSolverReportMapping.ToIncumbentReport(
                slice: CpSlice(),
                serialized: LinearSolution(variableCount: 2),
                variableCount: 3);

        result.IsOk.Should().BeFalse(
            "解长度不符必须拒绝 / a length mismatch must be rejected");
    }

    /// <summary>
    /// 目标值缺失时报错而不是填 0——0 是合法目标值，拿它当"未知"会让下游误读。
    /// A missing objective is an error rather than 0, because zero is a legitimate objective.
    /// </summary>
    [Fact]
    public void ToIncumbentReport_ShouldNotSubstituteZeroForMissingObjective() {
        Result<Fuookami.Ospf.Core.Solver.Report.SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>
            result = RemoteSolverReportMapping.ToIncumbentReport(
                slice: CpSlice() with { ObjectiveValue = null },
                serialized: LinearSolution(variableCount: 2) with { ObjectiveValue = null },
                variableCount: 2);

        result.IsOk.Should().BeFalse(
            "缺目标值必须报错 / a missing objective must fail");
    }

    /// <summary>
    /// 不可行时的错误码必须是 ORModelInfeasible，不能混成"结果非法"。
    /// Infeasibility must surface as ORModelInfeasible rather than a generic invalid-result code.
    /// </summary>
    [Fact]
    public void ToIncumbentReport_ShouldReportModelInfeasible() {
        Result<Fuookami.Ospf.Core.Solver.Report.SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>
            result = RemoteSolverReportMapping.ToIncumbentReport(
                slice: CpSlice(feasible: false) with { Message = "no solution" },
                serialized: null,
                variableCount: 2);

        result.IsOk.Should().BeFalse();
        (result as Failed<Fuookami.Ospf.Core.Solver.Report.SolveReport<Flt64>, ErrorCode,
            Error<ErrorCode>>)!.Error.Code.Should().Be(ErrorCode.ORModelInfeasible);
    }

    /// <summary>
    /// 只有变量数为 0 才允许空解；否则"空解但成功"会掩盖结果不完整。
    /// An empty solution is allowed only when the model has zero variables; otherwise "empty but
    /// successful" would hide an incomplete result.
    /// </summary>
    [Fact]
    public void ToEmptySolutionOutput_ShouldOnlyAllowEmptyModel() {
        RemoteSolverReportMapping.ToEmptySolutionOutput(CpSlice(), variableCount: 2)
            .IsOk.Should().BeFalse("非空模型不允许空解 / a non-empty model may not have an empty solution");

        Result<Fuookami.Ospf.Core.Solver.Report.SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>
            empty = RemoteSolverReportMapping.ToEmptySolutionOutput(
                slice: CpSlice() with { ObjectiveValue = new Flt64(0.0) },
                variableCount: 0);
        empty.IsOk.Should().BeTrue(
            "空模型的 0 目标值是合法的 / a zero objective on an empty model is legitimate");
        empty.Value.Solution!.Values.Values.Should().BeEmpty();
    }

    /// <summary>
    /// 完整报告的 incumbent 路径：解、目标、证明与统计都要落到报告上。
    /// The incumbent path of the full report: solution, objective, proof and statistics all land on it.
    /// </summary>
    [Fact]
    public void ToSolveReport_ShouldCarryIncumbent() {
        Result<Fuookami.Ospf.Core.Solver.Report.SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>
            result = RemoteSolverReportMapping.ToSolveReport(
                slice: CpSlice(
                    presence: RemoteSolutionPresence.Optimal,
                    proof: RemoteProofStatus.Verified) with {
                    ObjectiveValue = new Flt64(7.5),
                    Statistics = new Dictionary<string, string> { ["bestBound"] = "7.0" },
                },
                serialized: LinearSolution(variableCount: 2, objective: 7.5),
                variableCount: 2,
                modelTypes: _linearModelTypes);

        result.IsOk.Should().BeTrue();
        Fuookami.Ospf.Core.Solver.Report.SolveReport<Flt64> report = result.Value;
        report.Solution.Should().NotBeNull();
        report.Solution!.Values.Values.Should().HaveCount(2);
        report.Solution.Objective!.Value.ToDouble().Should().Be(7.5);
        report.Proof.Status.Should().Be(Fuookami.Ospf.Core.Solver.Report.ProofStatus.Verified);
        report.Statistics.BestBound!.Value.ToDouble().Should().Be(7.0);
        report.SolutionPresence.Should().Be(Fuookami.Ospf.Core.Solver.Report.SolutionPresence.Optimal);
    }

    /// <summary>
    /// 解存在性为 NONE 时报告不带解——"说没有解"与"恰好没有解字段"不能混淆。
    /// With a NONE presence the report carries no solution, so "says there is none" is not confused with
    /// "happens to have no solution field".
    /// </summary>
    [Fact]
    public void ToSolveReport_ShouldOmitSolutionWhenPresenceIsNone() {
        Result<Fuookami.Ospf.Core.Solver.Report.SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>
            result = RemoteSolverReportMapping.ToSolveReport(
                slice: CpSlice(
                    feasible: false,
                    problemStatus: RemoteProblemStatus.Infeasible,
                    presence: RemoteSolutionPresence.None,
                    proof: RemoteProofStatus.Claimed),
                serialized: null,
                variableCount: 2,
                modelTypes: _linearModelTypes);

        result.IsOk.Should().BeTrue();
        result.Value.Solution.Should().BeNull();
        result.Value.SolutionPresence
            .Should().Be(Fuookami.Ospf.Core.Solver.Report.SolutionPresence.None);
    }

    private static SerializedSolution CpSolution(
        bool feasible = true,
        bool optimal = false,
        string schemaVersion = "2.0") =>
        new() {
            Feasible = feasible,
            Optimal = optimal,
            ObjectiveValueInt64 = 7,
            ProblemStatus = RemoteProblemStatus.Feasible,
            SolutionPresence = optimal
                ? RemoteSolutionPresence.Optimal
                : feasible
                    ? RemoteSolutionPresence.Incumbent
                    : RemoteSolutionPresence.None,
            ProofStatus = RemoteProofStatus.Claimed,
            TerminationReason = RemoteTerminationReason.Completed,
            SchemaVersion = schemaVersion,
            RunId = "task",
            AttemptId = "slice",
        };

    /// <summary>
    /// 两个真相源（原始结果与稳定解工件）一致时必须通过——否则任何 CP 结果都会被拒。
    /// Two sources of truth that agree must pass, otherwise every CP result would be rejected.
    /// </summary>
    [Fact]
    public void ValidateSerializedCpResult_ShouldAcceptAgreeingArtifact() {
        SliceResult slice = CpSlice() with { RunId = "task", AttemptId = "slice" };
        RemoteReportMapping.ValidateSerializedCpResult(CpSolution(), slice).IsOk.Should().BeTrue();
    }

    /// <summary>
    /// 工件与原始结果的可行性和最优性必须一致：否则无法判断最终交付的是哪一份。
    /// The artifact and raw result must agree on feasibility and optimality, otherwise it is impossible to
    /// tell which one is delivered.
    /// </summary>
    [Fact]
    public void ValidateSerializedCpResult_ShouldRejectFeasibilityOrOptimalityMismatch() {
        SliceResult slice = CpSlice() with { RunId = "task", AttemptId = "slice" };

        RemoteReportMapping.ValidateSerializedCpResult(
            CpSolution(feasible: false) with {
                ProblemStatus = RemoteProblemStatus.Infeasible,
                SolutionPresence = RemoteSolutionPresence.None,
                ObjectiveValueInt64 = 7,
            },
            slice).IsOk.Should().BeFalse("可行性不一致 / feasibility disagrees");

        SliceResult optimalSlice = CpSlice(
            presence: RemoteSolutionPresence.Optimal, proof: RemoteProofStatus.Verified)
            with { RunId = "task", AttemptId = "slice" };
        RemoteReportMapping.ValidateSerializedCpResult(
            CpSolution(optimal: false) with {
                ProofStatus = RemoteProofStatus.Verified,
            },
            optimalSlice).IsOk.Should().BeFalse("最优性不一致 / optimality disagrees");
    }

    [Fact]
    public void ValidateSerializedCpResult_ShouldRejectInfeasibleButOptimalArtifact() {
        SliceResult slice = CpSlice(
            feasible: false,
            problemStatus: RemoteProblemStatus.Infeasible,
            presence: RemoteSolutionPresence.None,
            proof: RemoteProofStatus.Claimed) with { RunId = "task", AttemptId = "slice" };

        RemoteReportMapping.ValidateSerializedCpResult(
            CpSolution(feasible: false, optimal: true) with {
                ProblemStatus = RemoteProblemStatus.Infeasible,
                SolutionPresence = RemoteSolutionPresence.Optimal,
            },
            slice).IsOk.Should().BeFalse(
            "工件自相矛盾：不可行却最优 / the artifact contradicts itself: infeasible yet optimal");
    }

    [Fact]
    public void ValidateSerializedCpResult_ShouldRejectObjectiveAndPresenceMismatch() {
        SliceResult slice = CpSlice() with { RunId = "task", AttemptId = "slice" };

        RemoteReportMapping.ValidateSerializedCpResult(
            CpSolution() with { ObjectiveValueInt64 = 99 },
            slice).IsOk.Should().BeFalse("目标值不一致 / the objective disagrees");

        RemoteReportMapping.ValidateSerializedCpResult(
            CpSolution() with { RunId = "other" },
            slice).IsOk.Should().BeFalse("run 不一致 / the run disagrees");
    }

    /// <summary>
    /// v1 工件允许缺省字段，但一旦给出就必须相等——历史后端不下发全部字段，新后端必须完整。
    /// A v1 artifact may omit fields but must match any it carries: legacy backends omit fields, current
    /// ones are complete.
    /// </summary>
    [Fact]
    public void ValidateSerializedCpResult_ShouldBeLenientOnlyForV1OmittedFields() {
        // schema 版本必须两侧一致——即使放宽字段比对，版本也不能不同。
        // The schema version must match on both sides; leniency about fields does not extend to the
        // version itself.
        SliceResult slice = CpSlice(schemaVersion: "1.0") with { RunId = "task", AttemptId = "slice" };

        RemoteReportMapping.ValidateSerializedCpResult(
            CpSolution(schemaVersion: "1.0") with {
                ProofStatus = null,
                TerminationReason = null,
                SolutionPresence = null,
                ProblemStatus = null,
                RunId = null,
                AttemptId = null,
            },
            slice).IsOk.Should().BeTrue("v1 允许缺省 / v1 may omit fields");

        RemoteReportMapping.ValidateSerializedCpResult(
            CpSolution(schemaVersion: "1.0") with {
                ProofStatus = RemoteProofStatus.Verified,
            },
            slice).IsOk.Should().BeFalse(
            "v1 一旦给出就必须相等 / once supplied, v1 must match");
    }

    [Fact]
    public void ValidateSerializedCpResult_ShouldRejectDuplicateObjectiveFields() {
        SliceResult slice = CpSlice() with { RunId = "task", AttemptId = "slice" };

        RemoteReportMapping.ValidateSerializedCpResult(
            CpSolution() with { ObjectiveValue = 7.0 },
            slice).IsOk.Should().BeFalse(
            "精确与浮点目标不得并存 / exact and floating objectives may not coexist");
    }

    private static SliceResult CpSlice(
        bool feasible = true,
        RemoteProblemStatus? problemStatus = RemoteProblemStatus.Feasible,
        RemoteSolutionPresence? presence = RemoteSolutionPresence.Incumbent,
        RemoteProofStatus? proof = RemoteProofStatus.Claimed,
        string schemaVersion = "2.0") =>
        new(
            Completed: true,
            Feasible: feasible,
            ObjectiveValue: null,
            Gap: null,
            Elapsed: TimeSpan.Zero,
            Status: Domain.TaskStatus.Completed,
            Outcome: SliceOutcome.Completed,
            ObjectiveValueInt64: 7,
            ProblemStatus: problemStatus,
            TerminationReason: RemoteTerminationReason.Completed,
            SolutionPresence: presence,
            ProofStatus: proof,
            SchemaVersion: schemaVersion);

    /// <summary>
    /// 一致的结果必须通过校验；否则任何 CP 结果都会被拒。
    /// A consistent result must pass, otherwise every CP result would be rejected.
    /// </summary>
    [Fact]
    public void ValidateRawCpResult_ShouldAcceptConsistentResult() {
        RemoteReportMapping.ValidateRawCpResult(CpSlice()).IsOk.Should().BeTrue();
        RemoteReportMapping.ValidateRawCpResult(CpSlice(schemaVersion: "1.0")).IsOk.Should().BeTrue();
    }

    [Fact]
    public void ValidateRawCpResult_ShouldRejectFutureSchemaMajor() {
        RemoteReportMapping.ValidateRawCpResult(CpSlice(schemaVersion: "3.0")).IsOk.Should().BeFalse(
            "未来主版本一律拒绝 / a future major version is rejected");
        RemoteReportMapping.ValidateRawCpResult(CpSlice(schemaVersion: "x")).IsOk.Should().BeFalse();
    }

    [Fact]
    public void ValidateRawCpResult_ShouldRejectFeasibilityContradictions() {
        RemoteReportMapping.ValidateRawCpResult(
            CpSlice(feasible: true, problemStatus: RemoteProblemStatus.Infeasible))
            .IsOk.Should().BeFalse("可行却标 INFEASIBLE / feasible but marked INFEASIBLE");
        RemoteReportMapping.ValidateRawCpResult(
            CpSlice(feasible: false, problemStatus: RemoteProblemStatus.Feasible,
                presence: RemoteSolutionPresence.None, proof: RemoteProofStatus.None))
            .IsOk.Should().BeFalse("不可行却标 FEASIBLE / infeasible but marked FEASIBLE");
        RemoteReportMapping.ValidateRawCpResult(
            CpSlice(feasible: false, problemStatus: RemoteProblemStatus.Infeasible,
                presence: RemoteSolutionPresence.Optimal, proof: RemoteProofStatus.Verified))
            .IsOk.Should().BeFalse("不可行却声称最优 / infeasible but claims optimality");
    }

    [Fact]
    public void ValidateRawCpResult_ShouldRequireProofForOptimality() {
        RemoteReportMapping.ValidateRawCpResult(
            CpSlice(presence: RemoteSolutionPresence.Optimal, proof: RemoteProofStatus.None))
            .IsOk.Should().BeFalse("最优必须有证明状态 / optimality requires a proof status");
        RemoteReportMapping.ValidateRawCpResult(
            CpSlice(presence: RemoteSolutionPresence.Optimal, proof: RemoteProofStatus.Verified))
            .IsOk.Should().BeTrue();
    }

    [Fact]
    public void ValidateRawCpResult_ShouldRequirePairedResultRefAndDigest() {
        SliceResult withRef = CpSlice() with {
            ResultRef = new ObjectRef(Path: new ObjectPath("tenant/result/1")),
        };
        RemoteReportMapping.ValidateRawCpResult(withRef).IsOk.Should().BeFalse(
            "只有引用没有摘要 / a reference without a digest");

        SliceResult withDigest = CpSlice() with { ArtifactDigest = "abc" };
        RemoteReportMapping.ValidateRawCpResult(withDigest).IsOk.Should().BeFalse();

        SliceResult paired = CpSlice() with {
            ResultRef = new ObjectRef(Path: new ObjectPath("tenant/result/1")),
            ArtifactDigest = "abc",
        };
        RemoteReportMapping.ValidateRawCpResult(paired).IsOk.Should().BeTrue();
    }

    [Fact]
    public void ValidateRawCpResult_ShouldRejectBadStatisticsAndDualObjectives() {
        SliceResult badBound = CpSlice() with {
            Statistics = new Dictionary<string, string> { ["bestBound"] = "not-a-number" },
        };
        RemoteReportMapping.ValidateRawCpResult(badBound).IsOk.Should().BeFalse();

        SliceResult dualObjective = CpSlice() with {
            ObjectiveValue = new Flt64(1.0),
        };
        RemoteReportMapping.ValidateRawCpResult(dualObjective).IsOk.Should().BeFalse(
            "精确与浮点目标不得并存 / exact and floating objectives may not coexist");
    }

    [Fact]
    public void ValidateRawCpResult_ShouldRejectMismatchedFingerprintSchemas() {
        SliceResult mismatched = CpSlice() with {
            Fingerprints = new Dictionary<string, string> { ["model"] = "a", ["solver"] = "b" },
            FingerprintSchemas = new Dictionary<string, string> { ["model"] = "1.0" },
        };
        RemoteReportMapping.ValidateRawCpResult(mismatched).IsOk.Should().BeFalse();

        SliceResult blankSchema = CpSlice() with {
            Fingerprints = new Dictionary<string, string> { ["model"] = "a" },
            FingerprintSchemas = new Dictionary<string, string> { ["model"] = "   " },
        };
        RemoteReportMapping.ValidateRawCpResult(blankSchema).IsOk.Should().BeFalse(
            "schema 不得空白 / a schema must not be blank");

        SliceResult matched = CpSlice() with {
            Fingerprints = new Dictionary<string, string> { ["model"] = "a" },
            FingerprintSchemas = new Dictionary<string, string> { ["model"] = "1.0" },
        };
        RemoteReportMapping.ValidateRawCpResult(matched).IsOk.Should().BeTrue();
    }

    /// <summary>
    /// 历史 1.0 结果缺证明字段时应记为 CLAIMED——"明确没有证明"与"字段不存在"不能混为一谈。
    /// A legacy 1.0 result without a proof field becomes CLAIMED — "explicitly unproven" and "field
    /// absent" must not be conflated.
    /// </summary>
    [Fact]
    public void NormalizeLegacyCpResult_ShouldMarkLegacyInfeasibilityAsClaimed() {
        SliceResult legacy = CpSlice(
            feasible: false,
            problemStatus: RemoteProblemStatus.Infeasible,
            presence: RemoteSolutionPresence.None,
            proof: RemoteProofStatus.None,
            schemaVersion: "1.0");

        SliceResult normalized = RemoteReportMapping.NormalizeLegacyCpResult(legacy);

        normalized.ProofStatus.Should().Be(RemoteProofStatus.Claimed);
        normalized.Feasible.Should().BeFalse("不得因合法化而改变可行性 / feasibility is unchanged");

        SliceResult modern = legacy with { SchemaVersion = "2.0" };
        RemoteReportMapping.NormalizeLegacyCpResult(modern).ProofStatus
            .Should().Be(RemoteProofStatus.None,
                "v2 明确给出 NONE 时不合法化 / a v2 explicit NONE is not normalized");
    }

    private static SolvePayload ResumePayload() => new(
        ModelData: new ModelData(Format: "x"),
        Scheduling: new SchedulingRequest(
            ModelFingerprint: "model-sha",
            Metadata: new Dictionary<string, string> {
                ["configurationFingerprint"] = "config-sha",
                ["solverFingerprint"] = "solver-sha",
            }));

    private static ExecutionHandle Handle() => new(
        HandleId: new HandleId("h"),
        TaskId: new TaskId("task"),
        SliceId: new SliceId("slice"),
        NodeId: new NodeId("node"),
        TenantId: new TenantId("tenant"));

    private sealed class NamedPlugin(string name) : IRemoteSolverHttpTransportPlugin {
        public string Name { get; } = name;

        public RemoteSolverHttpTransport Create(RemoteSolverHttpTransportConfig config) =>
            _ => Task.FromResult(new RemoteSolverHttpResponse(200, null));
    }

    private sealed class SequencedTransport : IDisposable {
        private readonly Queue<RemoteSolverHttpResponse> _responses;

        public SequencedTransport(params RemoteSolverHttpResponse[] responses) {
            _responses = new Queue<RemoteSolverHttpResponse>(responses);
        }

        public int RequestCount { get; private set; }
        public string? LastUrl { get; private set; }
        public string? LastBody { get; private set; }

        public Task<RemoteSolverHttpResponse> Send(RemoteSolverHttpRequest request) {
            ++RequestCount;
            LastUrl = request.Url;
            LastBody = request.Body;
            RemoteSolverHttpResponse response = _responses.Count > 0
                ? _responses.Dequeue()
                : new RemoteSolverHttpResponse(200, """{"code":"OK","data":{"status":"RUNNING"}}""");
            return Task.FromResult(response);
        }

        public void Dispose() { }
    }

    private sealed class RecordingStorage : IObjectStoragePort {
        public bool Fail { get; init; }
        public byte[]? ObjectBytes { get; init; }
        /// <summary>按路径给的工件内容 / Per-path artifact content.</summary>
        public Dictionary<ObjectPath, string> Objects { get; } = new();
        public ObjectRef? UploadedPath { get; private set; }
        public byte[]? UploadedBytes { get; private set; }
        public ObjectRef? ReadPath { get; private set; }
        public IReadOnlyDictionary<string, string>? UploadedMetadata { get; private set; }

        public Task<Result<ObjectRef, ErrorCode, Error<ErrorCode>>> PutAsync(
            ObjectPath path,
            byte[] data,
            string? contentType = null,
            CancellationToken cancellationToken = default) =>
            Store(path, data, metadata: null);

        public Task<Result<ObjectRef, ErrorCode, Error<ErrorCode>>> PutAsync(
            ObjectPath path,
            byte[] data,
            IReadOnlyDictionary<string, string>? metadata,
            CancellationToken cancellationToken = default) =>
            Store(path, data, metadata);

        private Task<Result<ObjectRef, ErrorCode, Error<ErrorCode>>> Store(
            ObjectPath path,
            byte[] data,
            IReadOnlyDictionary<string, string>? metadata) {
            if (Fail) {
                throw new IOException("storage down");
            }
            UploadedPath = new ObjectRef(Path: path);
            UploadedBytes = data;
            UploadedMetadata = metadata;
            return Task.FromResult(Results.Ok(new ObjectRef(Path: path)));
        }

        public Task<Result<StoredObject, ErrorCode, Error<ErrorCode>>> GetAsync(
            ObjectRef @ref,
            CancellationToken cancellationToken = default) {
            ReadPath = @ref;
            if (Objects.TryGetValue(@ref.Path, out string? text)) {
                return Task.FromResult(Results.Ok(new StoredObject(
                    @ref,
                    System.Text.Encoding.UTF8.GetBytes(text),
                    "application/json",
                    System.DateTime.UnixEpoch)));
            }
            if (ObjectBytes is null) {
                return Task.FromResult(Results.Failed<StoredObject>(new Err<ErrorCode>(
                    ErrorCode.ApplicationFailed,
                    "object missing")));
            }
            return Task.FromResult(Results.Ok(new StoredObject(
                @ref,
                ObjectBytes,
                "application/json",
                System.DateTime.UnixEpoch)));
        }

        public Task<Result<bool, ErrorCode, Error<ErrorCode>>> DeleteAsync(
            ObjectRef @ref,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Results.Ok(true));

        public Task<Result<bool, ErrorCode, Error<ErrorCode>>> ExistsAsync(
            ObjectRef @ref,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Results.Ok(UploadedPath is not null));
    }

    private sealed class StubTransport : IDisposable {
        private readonly int _statusCode;
        private readonly string? _body;

        public StubTransport(int statusCode, string? body) {
            _statusCode = statusCode;
            _body = body;
        }

        public string? LastUrl { get; private set; }
        public string? LastMethod { get; private set; }
        public int RequestCount { get; private set; }

        public Task<RemoteSolverHttpResponse> Send(RemoteSolverHttpRequest request) {
            LastUrl = request.Url;
            LastMethod = request.Method.Method;
            ++RequestCount;
            return Task.FromResult(new RemoteSolverHttpResponse(_statusCode, _body));
        }

        public void Dispose() { }
    }

    private static SerializedSolution Decode(string fixture) {
        string path = Path.Combine(FixtureRoot(), fixture);
        File.Exists(path).Should().BeTrue(
            $"fixture must exist at {path} / fixture 必须存在于 {path}");
        return SerializedSolution.Decode(File.ReadAllText(path)).Value;
    }

    private static string FixtureRoot() {
        string[] candidates = {
            Path.Combine(AppContext.BaseDirectory, "remote-fixtures"),
            Path.Combine(Directory.GetCurrentDirectory(), "tests", "Framework.Tests", "RemoteFixtures"),
        };
        return candidates.FirstOrDefault(Directory.Exists)
            ?? throw new DirectoryNotFoundException(
                "远程 fixture 目录缺失 / Remote fixture directory is missing: "
                + string.Join(", ", candidates));
    }
}
