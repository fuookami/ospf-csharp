// 搜索快照载体测试 / Tests for the search snapshot carriers.

using System;
using System.Collections.Generic;
using System.Text.Json;
using FluentAssertions;
using Fuookami.Ospf.Core.Solver.Heuristic;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator;
using Fuookami.Ospf.Core.Solver.Heuristic.Persistence;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Xunit;

namespace Fuookami.Ospf.Core.Tests;

/// <summary>
/// 搜索快照载体 / The search snapshot carriers.
/// </summary>
public sealed class SearchSnapshotsTests {
    /// <summary>**策略档案保留两级池映射，不压平** / The policy archive keeps the two-level pool map.</summary>
    /// <remarks>
    /// 压平成一维会丢掉池这一层，两个不同池的同名动作互相覆盖，**而每个快照本身仍然合法**。
    /// Flattening loses the pool level, so same-named actions from two pools overwrite each other **while each snapshot remains valid**.
    /// </remarks>
    [Fact]
    public void PolicyArchive_ShouldKeepTwoLevelOperatorMap() {
        PolicySnapshot shared = Snapshot("a");
        PolicyArchive archive = new(
            "definition", "progress:1", shared,
            new Dictionary<string, IReadOnlyDictionary<string, PolicySnapshot>>(StringComparer.Ordinal) {
                ["pool-one"] = new Dictionary<string, PolicySnapshot>(StringComparer.Ordinal) { ["a"] = shared },
                ["pool-two"] = new Dictionary<string, PolicySnapshot>(StringComparer.Ordinal) { ["a"] = shared },
            });

        archive.Operators.Count.Should().Be(2, "两个池的同名动作必须分别保存 / same-named actions in two pools stay separate");
        archive.Operators["pool-one"]["a"].Should().Be(shared);
        archive.Operators["pool-two"]["a"].Should().Be(shared);
    }

    /// <summary>**策略档案的根策略与池策略分开保存** / The root policy is stored separately from pool policies.</summary>
    /// <remarks>
    /// 把根策略混进 `operators` 会让它在恢复时被当成某个池的策略，**而恢复过程一路成功**。
    /// Mixing the root policy into `operators` makes it look like some pool's policy on restore, **while the restore path succeeds
    /// throughout**.
    /// </remarks>
    [Fact]
    public void PolicyArchive_ShouldKeepRootSeparate() {
        PolicySnapshot root = Snapshot("root");
        PolicyArchive archive = new(
            "definition", "progress:1", root,
            new Dictionary<string, IReadOnlyDictionary<string, PolicySnapshot>>(StringComparer.Ordinal) {
                ["pool"] = new Dictionary<string, PolicySnapshot>(StringComparer.Ordinal) { ["a"] = Snapshot("a") },
            });

        archive.Root.Should().Be(root);
        archive.Operators.Should().NotContainKey("root");
    }

    /// <summary>**策略档案经 JSON 往返保持一致** / The policy archive survives a JSON round trip.</summary>
    [Fact]
    public void PolicyArchive_ShouldRoundTripThroughJson() {
        PolicyArchive archive = new(
            "definition", "progress:1", Snapshot("root"),
            new Dictionary<string, IReadOnlyDictionary<string, PolicySnapshot>>(StringComparer.Ordinal) {
                ["pool"] = new Dictionary<string, PolicySnapshot>(StringComparer.Ordinal) { ["a"] = Snapshot("a") },
            },
            new Dictionary<string, string>(StringComparer.Ordinal) { ["trainingInstances"] = "x,y" });

        string json = JsonSerializer.Serialize(archive);
        PolicyArchive restored = JsonSerializer.Deserialize<PolicyArchive>(json)!;

        restored.Definition.Should().Be("definition");
        restored.FeatureVersion.Should().Be("progress:1");
        restored.Metadata!["trainingInstances"].Should().Be("x,y");
        restored.Operators["pool"]["a"].Actions.Should().Equal("a");
    }

    /// <summary>**工作者检查点保留初始化与停用标记** / The worker checkpoint keeps the initialized and disabled flags.</summary>
    [Fact]
    public void WorkerCheckpoint_ShouldKeepFlags() {
        WorkerCheckpoint checkpoint = new(
            new SearchSessionSnapshot(false, 0L, 0L, JsonSerializer.SerializeToElement(0),
                JsonDocument.Parse("null").RootElement.Clone()),
            7L, true, false);

        checkpoint.Initialized.Should().BeTrue();
        checkpoint.Disabled.Should().BeFalse();
        checkpoint.Random.Should().Be(7L);
    }

    /// <summary>**运行检查点保留待消费转移，而不是全部历史** / The run checkpoint keeps pending transitions, not the whole history.</summary>
    /// <remarks>
    /// 把全部历史写进去会让恢复后重复观察同一批转移，**策略的计数翻倍而搜索轨迹看起来完全正常**。
    /// Writing the whole history makes restore observe the same transitions twice, **doubling the policy's counts while the search
    /// trajectory looks normal**.
    /// </remarks>
    [Fact]
    public void RunCheckpoint_ShouldCarryPendingTransitions() {
        RunCheckpoint checkpoint = BuildCheckpoint(
            transitions: new[] { Transition("a", 0L) });

        checkpoint.Transitions!.Count.Should().Be(1);
        checkpoint.Transitions![0].Action.Should().Be("a");
    }

    /// <summary>**运行检查点经 JSON 往返保持一致** / The run checkpoint survives a JSON round trip.</summary>
    [Fact]
    public void RunCheckpoint_ShouldRoundTripThroughJson() {
        RunCheckpoint checkpoint = BuildCheckpoint(transitions: new[] { Transition("a", 0L) });

        string json = JsonSerializer.Serialize(checkpoint);
        RunCheckpoint restored = JsonSerializer.Deserialize<RunCheckpoint>(json)!;

        restored.Configuration.Should().Be(checkpoint.Configuration);
        restored.Problem.Should().Be(checkpoint.Problem);
        restored.Evaluations.Should().Be(checkpoint.Evaluations);
        restored.Transitions!.Count.Should().Be(1);
        restored.Workers.Should().ContainKey("worker");
        restored.Policy.Root.Actions.Should().Equal("root");
    }

    /// <summary>**运行检查点保留调度检查点** / The run checkpoint keeps the schedule checkpoint.</summary>
    [Fact]
    public void RunCheckpoint_ShouldCarryScheduleCheckpoint() {
        ScheduleLog log = ScheduleLogs.Of("config", "problem", Array.Empty<ScheduleEvent>());
        RunCheckpoint checkpoint = BuildCheckpoint(
            transitions: Array.Empty<SelectionTransition>(),
            schedule: new ScheduleCheckpoint(0, log));

        checkpoint.Schedule!.Cursor.Should().Be(0);
        checkpoint.Schedule.Log.Events.Should().BeEmpty();
    }

    /// <summary>**`default(JsonElement)` 不可序列化** / `default(JsonElement)` is not serializable.</summary>
    /// <remarks>
    /// 这是本会话第三次踩到同一个坑（Batch 189 两处）。`JsonElementConverter.Write` 拒绝 `ValueKind == Undefined`，
    /// 而 `default(JsonElement)` 正是 `Undefined`。
    /// This is the third time this session hit the same trap (twice in Batch 189). `JsonElementConverter.Write` refuses
    /// `ValueKind == Undefined`, and `default(JsonElement)` is exactly `Undefined`.
    /// </remarks>
    [Fact]
    public void DefaultJsonElement_ShouldFailSerialization() {
        Action act = () => JsonSerializer.Serialize(new { value = default(JsonElement) });

        act.Should().Throw<InvalidOperationException>();
    }

    /// <summary>构造一个策略快照 / Build a policy snapshot.</summary>
    /// <param name="action">动作名 / The action name.</param>
    /// <returns>策略快照 / The policy snapshot.</returns>
    private static PolicySnapshot Snapshot(string action) => new(
        1, "fixed", new[] { action }, 0L, 0L, new[] { 0L }, new[] { 0.0 }, null,
        JsonDocument.Parse("null").RootElement.Clone());

    /// <summary>构造一个运行检查点 / Build a run checkpoint.</summary>
    /// <param name="transitions">待消费转移 / The pending transitions.</param>
    /// <param name="schedule">调度检查点 / The schedule checkpoint.</param>
    /// <returns>运行检查点 / The run checkpoint.</returns>
    private static RunCheckpoint BuildCheckpoint(
        IReadOnlyList<SelectionTransition> transitions,
        ScheduleCheckpoint? schedule = null) => new(
        Configuration: "config",
        Problem: "problem",
        Policy: new PolicyArchive("definition", "progress:1", Snapshot("root"),
            new Dictionary<string, IReadOnlyDictionary<string, PolicySnapshot>>(StringComparer.Ordinal) {
                ["pool"] = new Dictionary<string, PolicySnapshot>(StringComparer.Ordinal) { ["a"] = Snapshot("a") },
            }),
        Workers: new Dictionary<string, WorkerCheckpoint>(StringComparer.Ordinal) {
            ["worker"] = new WorkerCheckpoint(
                new SearchSessionSnapshot(false, 0L, 0L, JsonSerializer.SerializeToElement(0),
                    JsonDocument.Parse("null").RootElement.Clone()),
                7L, true, false),
        },
        Archive: JsonDocument.Parse("null").RootElement.Clone(),
        Working: JsonDocument.Parse("null").RootElement.Clone(),
        Evaluations: 10L,
        Dispatched: 10L,
        Completed: 9L,
        Steps: 9L,
        Stagnation: 0L,
        ActiveNanos: 1234L,
        Random: 7L,
        Diagnostics: Array.Empty<string>(),
        Transitions: transitions,
        Components: new Dictionary<string, JsonElement>(StringComparer.Ordinal),
        Schedule: schedule);

    /// <summary>构造一个转移 / Build a transition.</summary>
    /// <param name="action">动作 / The action.</param>
    /// <param name="decision">决策号 / The decision.</param>
    /// <returns>转移 / The transition.</returns>
    private static SelectionTransition Transition(string action, long decision) => new(
        "episode", decision, action, new SelectionContext(), new SelectionContext(),
        new[] { action }, new[] { action }, new Flt64(1.0), 1L, false, 0L);
}
