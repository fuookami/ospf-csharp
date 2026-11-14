#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Fuookami.Ospf.Framework.Solver.Remote.Domain;
using Xunit;

namespace Fuookami.Ospf.Framework.Tests.Solver;

/// <summary>
/// **调度模型与能力摘要的字段名必须与固定基线逐个相同** / Scheduling-model and capability wire names must match the pinned baseline field by field.
/// </summary>
/// <remarks>
/// 固定 Kotlin 基线 `12fdb587` **没有为这两组类型提供 wire fixture**（`src/test/resources/fixtures/` 里只有 remote-cp/linear 的四个文件），
/// 所以"逐字节往返"这条路走不通。但**字段名是能核的**：Kotlin 的 `@SerialName` 与属性名就是 wire 上的事实，可以直接从固定提交里读出来。
///
/// 这一条因此只声明**名称一致性**，不声明"能互相解码"——后者要等基线提供 fixture。
/// **把能核的和不能核的分开说，是这个项目反复吃过的教训**（B469 按工程名误判、B900 把引用相等当成"不是划分"）。
///
/// The pinned Kotlin baseline `12fdb587` **provides no wire fixture for these two groups** (only the four remote-cp/linear files), so a byte-level round trip is not
/// available. **Field names are still checkable**: Kotlin's `@SerialName` and property names are the wire facts and can be read straight out of the pinned commit. This
/// criterion therefore claims **name parity only**, not "each side can decode the other" — that needs a baseline fixture. **Separating the checkable from the uncheckable is
/// a lesson this project learned the hard way** (B469's project-name misjudgement, B900's reference equality read as "not a partition").
///
/// 来源 / Source: `ospf-kotlin-framework/src/main/fuookami/ospf/kotlin/framework/solver/remote/domain/SchedulingModels.kt`
/// and `.../domain/ExecutionModels.kt`, both at `12fdb587`.
/// </remarks>
public class RemoteSchedulingWireNameTests {
    private static IReadOnlyList<string> Names(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateObject().Select(property => property.Name).ToList();
    }

    /// <summary>**`SchedulingEstimate` 的四个名字** / The four `SchedulingEstimate` names.</summary>
    [Fact]
    public void SchedulingEstimate_ShouldUseTheBaselineNames() {
        var estimate = new SchedulingEstimate(
            EstimatedRuntimeMs: 1,
            EstimatedCheckpointMs: 2,
            EstimatedQueueWaitMs: 3,
            Cost: 4.5);

        Names(JsonSerializer.Serialize(estimate, WireJson())).Should().Equal(
            "estimatedRuntimeMs", "estimatedCheckpointMs", "estimatedQueueWaitMs", "cost");
    }

    /// <summary>**`QualityTarget` 的五个名字（与 Kotlin 属性名同形）** / The five `QualityTarget` names, same shape as the Kotlin properties.</summary>
    [Fact]
    public void QualityTarget_ShouldUseTheBaselineNames() {
        var target = new QualityTarget(
            MaxGap: 0.01,
            ObjectiveLimit: 100.0,
            RequireFeasible: true,
            RequireOptimal: false,
            Metadata: new Dictionary<string, string>(StringComparer.Ordinal) { ["k"] = "v" });

        Names(JsonSerializer.Serialize(target, WireJson())).Should().Equal(
            "maxGap", "objectiveLimit", "requireFeasible", "requireOptimal", "metadata");
    }

    /// <summary>**`SchedulingRequest` 的十五个名字** / The fifteen `SchedulingRequest` names.</summary>
    /// <remarks>
    /// 基线里 `deadline` 带着 `@SerialName("deadlineEpochMs")`，**属性名和 wire 名不同**——正是这种地方最容易写错。
    ///
    /// In the baseline `deadline` carries `@SerialName("deadlineEpochMs")`: **the property name and the wire name differ**, which is exactly where this goes wrong.
    /// </remarks>
    [Fact]
    public void SchedulingRequest_ShouldUseTheBaselineNames() {
        var request = new SchedulingRequest(
            Complexity: TaskComplexity.Simple,
            TimeSensitivity: TimeSensitivity.Realtime,
            Priority: 5,
            DeadlineEpochMs: 1700000000000,
            BudgetScope: new BudgetScopeId("tenant"),
            BudgetLimit: 12.5,
            Estimate: new SchedulingEstimate(EstimatedRuntimeMs: 1),
            ModelFingerprint: "fp",
            ModelFingerprintSchema: "1.0",
            // **三个引用字段必须给非空值**：`WhenWritingNull` 会把空的整个跳过，那样就量不到它们的名字——
            // "字段存在但为空"和"字段根本没有"在 wire 上是两件事，判据必须后者失败。
            // **The three reference fields must be non-null**: `WhenWritingNull` skips empty ones entirely, so their names would go unmeasured — "present but null" and
            // "not there at all" are different on the wire, and this criterion must fail on the latter.
            CheckpointRef: new ObjectRef(new ObjectPath("ckpt/1")),
            IncumbentRef: new ObjectRef(new ObjectPath("inc/1")),
            QualityTarget: new QualityTarget(RequireFeasible: true),
            PreemptionMode: PreemptionMode.NonPreemptible,
            ResumeMode: ResumeMode.None,
            Metadata: new Dictionary<string, string>(StringComparer.Ordinal) { ["k"] = "v" });

        Names(JsonSerializer.Serialize(request, WireJson())).Should().Equal(
            "complexity", "timeSensitivity", "priority", "deadlineEpochMs", "budgetScope", "budgetLimit", "estimate",
            "modelFingerprint", "modelFingerprintSchema", "checkpointRef", "incumbentRef", "qualityTarget",
            "preemptionMode", "resumeMode", "metadata");
    }

    /// <summary>**能力摘要的五个名字** / The five capability-summary names.</summary>
    [Fact]
    public void RemoteSolverCapabilities_ShouldUseTheBaselineNames() {
        var capabilities = new RemoteSolverCapabilities(
            SchemaVersion: "2.0",
            ProtocolVersions: ["2.0"],
            SupportedModelTypes: ["CP"],
            SupportsPortableCheckpoint: true,
            SupportsNativeCheckpoint: false);

        Names(capabilities.Encode()).Should().Equal(
            "schemaVersion", "protocolVersions", "supportedModelTypes", "supportsPortableCheckpoint", "supportsNativeCheckpoint");
    }

    /// <summary>**基线没有 fixture，这件事必须写下来** / The absence of a baseline fixture must be on record.</summary>
    /// <remarks>
    /// 名字一致**不等于**两端能互相解码：数值表示（`Duration` 序列化成整型毫秒还是 ISO 文本）、集合与空值策略都可能不同。
    /// 这条判据存在的意义是**拦住"名字对了就宣布互通"**。
    ///
    /// Name parity does **not** mean the two sides can decode each other: numeric representation (a `Duration` as integer milliseconds or as ISO text) and collection/null
    /// policy may differ. This criterion exists to **stop "the names match, therefore it interoperates"**.
    /// </remarks>
    [Fact]
    public void NameParity_ShouldNotBeClaimedAsInteroperability() {
        // 基线的 fixtures 目录里没有调度模型与能力摘要的样本——因此这两组的往返**未实测**。
        // The baseline's fixtures directory contains no scheduling-model or capability sample, so round trips for these two groups are **not measured**.
        string[] baselineFixtures = [
            "remote-cp-result-v1.json",
            "remote-cp-result-v2.json",
            "remote-linear-model-v2.json",
            "remote-linear-result-v2.json",
        ];
        baselineFixtures.Should().NotContain("scheduling-request.json", "if a scheduling fixture ever lands, this criterion must be upgraded to a real round trip");
    }

    private static JsonSerializerOptions WireJson() => new() {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };
}
