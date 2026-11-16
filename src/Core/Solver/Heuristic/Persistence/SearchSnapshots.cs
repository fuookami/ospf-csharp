// 搜索快照载体 / Search snapshot carriers.
// 与固定 Kotlin （4615 B）对应。

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator;

namespace Fuookami.Ospf.Core.Solver.Heuristic.Persistence;

/// <summary>
/// 策略档案 / A policy archive.
/// </summary>
/// <remarks>
/// **`definition` 与 `featureVersion` 是"这份策略属于哪个算法定义、用哪一版特征训练"的契约**。省略它们会让一份用
/// `progress:1` 训练出来的策略被加载进 `progress:2` 的算法，**特征下标错位而选择仍在正常返回动作**。
/// **`definition` and `featureVersion` are the contract for "which algorithm definition this policy belongs to and which feature
/// version trained it".** Omitting them lets a policy trained on `progress:1` load into a `progress:2` algorithm, **misaligning the
/// feature indices while selection still returns actions normally**.
/// **`operators` 是"池标识 → 动作名 → 快照"的两级映射**。压平成一维会丢掉池这一层，两个不同池的同名动作互相覆盖，
/// **而每个快照本身仍然合法**。
/// **`operators` is a two-level map "pool id → action name → snapshot".** Flattening it loses the pool level, so same-named actions from
/// two pools overwrite each other **while each snapshot remains individually valid**.
/// **`root` 是"没有池上下文的那份策略"**（由算法自身持有）。把它与 `operators` 混在一起会让根策略在恢复时被当成
/// 某个池的策略，**而恢复过程一路成功**。
/// **`root` is "the policy without a pool context"** (held by the algorithm itself in the code). Mixing it into `operators` makes the
/// root policy look like some pool's policy on restore, **while the restore path succeeds throughout**.
/// </remarks>
/// <param name="Definition">算法定义标识 / The algorithm definition identity.</param>
/// <param name="FeatureVersion">特征版本 / The feature version.</param>
/// <param name="Root">根策略快照 / The root policy snapshot.</param>
/// <param name="Operators">各池策略快照 / The per-pool policy snapshots.</param>
/// <param name="Metadata">元数据 / The metadata.</param>
/// <param name="Definitions">附加定义载荷 / The extra definition payload.</param>
/// <param name="Version">版本 / The version.</param>
public sealed record PolicyArchive(
    [property: JsonPropertyName("definition")] string Definition,
    [property: JsonPropertyName("featureVersion")] string FeatureVersion,
    [property: JsonPropertyName("root")] PolicySnapshot Root,
    [property: JsonPropertyName("operators")] IReadOnlyDictionary<string, IReadOnlyDictionary<string, PolicySnapshot>> Operators,
    [property: JsonPropertyName("metadata")] IReadOnlyDictionary<string, string>? Metadata = null,
    [property: JsonPropertyName("definitions")] JsonElement? Definitions = null,
    [property: JsonPropertyName("version")] int Version = 1);

/// <summary>
/// 工作者检查点 / A worker checkpoint.
/// </summary>
/// <param name="Session">会话快照 / The session snapshot.</param>
/// <param name="Random">随机流状态 / The random-stream state.</param>
/// <param name="Initialized">是否已初始化 / Whether it is initialized.</param>
/// <param name="Disabled">是否已停用 / Whether it is disabled.</param>
/// <param name="ProblemState">问题状态 / The problem state.</param>
public sealed record WorkerCheckpoint(
    [property: JsonPropertyName("session")] SearchSessionSnapshot Session,
    [property: JsonPropertyName("random")] long Random,
    [property: JsonPropertyName("initialized")] bool Initialized,
    [property: JsonPropertyName("disabled")] bool Disabled,
    [property: JsonPropertyName("problemState")] JsonElement? ProblemState = null);

/// <summary>
/// 运行检查点 / A run checkpoint.
/// </summary>
/// <remarks>
/// **`transitions` 是"已经发生但尚未喂给策略的转移"**，不是全部历史。把全部历史写进去会让恢复后重复观察同一批转移，
/// **策略的计数翻倍而搜索轨迹看起来完全正常**。
/// **`transitions` are "transitions that happened but have not yet been fed to the policy"**, not the whole history. Writing the whole
/// history makes restore observe the same transitions twice, **doubling the policy's counts while the search trajectory looks normal**.
/// **`configurationData` 与 `configuration` 是两个字段**：前者是可迁移的配置载荷，后者是它的摘要。只留摘要会让配置
/// 无法被迁移，只留载荷会让"这是哪份配置"不可校验。
/// **`configurationData` and `configuration` are two fields**: the former is the migratable configuration payload and the latter its
/// digest. Keeping only the digest makes the configuration unmigratable, and keeping only the payload makes "which configuration is
/// this" unverifiable.
/// </remarks>
/// <param name="Configuration">配置摘要 / The configuration digest.</param>
/// <param name="Problem">问题标识 / The problem identity.</param>
/// <param name="Policy">策略档案 / The policy archive.</param>
/// <param name="Workers">工作者检查点 / The worker checkpoints.</param>
/// <param name="Archive">档案载荷 / The archive payload.</param>
/// <param name="Working">工作集载荷 / The working-set payload.</param>
/// <param name="Evaluations">已用评估 / The consumed evaluations.</param>
/// <param name="Dispatched">已派发 / The dispatched count.</param>
/// <param name="Completed">已完成 / The completed count.</param>
/// <param name="Steps">步数 / The step count.</param>
/// <param name="Stagnation">停滞计数 / The stagnation count.</param>
/// <param name="ActiveNanos">活跃耗时 / The active nanoseconds.</param>
/// <param name="Random">随机流状态 / The random-stream state.</param>
/// <param name="Diagnostics">诊断 / The diagnostics.</param>
/// <param name="ProblemState">问题状态 / The problem state.</param>
/// <param name="ObjectiveCalls">目标调用数 / The objective-call count.</param>
/// <param name="Transitions">待消费转移 / The pending transitions.</param>
/// <param name="Successes">成功数 / The success count.</param>
/// <param name="LastCost">最近代价 / The latest cost.</param>
/// <param name="ConfigurationData">配置载荷 / The configuration payload.</param>
/// <param name="Components">组件状态 / The component states.</param>
/// <param name="Schedule">调度检查点 / The schedule checkpoint.</param>
/// <param name="Version">版本 / The version.</param>
public sealed record RunCheckpoint(
    [property: JsonPropertyName("configuration")] string Configuration,
    [property: JsonPropertyName("problem")] string Problem,
    [property: JsonPropertyName("policy")] PolicyArchive Policy,
    [property: JsonPropertyName("workers")] IReadOnlyDictionary<string, WorkerCheckpoint> Workers,
    [property: JsonPropertyName("archive")] JsonElement Archive,
    [property: JsonPropertyName("working")] JsonElement Working,
    [property: JsonPropertyName("evaluations")] long Evaluations,
    [property: JsonPropertyName("dispatched")] long Dispatched,
    [property: JsonPropertyName("completed")] long Completed,
    [property: JsonPropertyName("steps")] long Steps,
    [property: JsonPropertyName("stagnation")] long Stagnation,
    [property: JsonPropertyName("activeNanos")] long ActiveNanos,
    [property: JsonPropertyName("random")] long Random,
    [property: JsonPropertyName("diagnostics")] IReadOnlyList<string>? Diagnostics = null,
    [property: JsonPropertyName("problemState")] JsonElement? ProblemState = null,
    [property: JsonPropertyName("objectiveCalls")] long ObjectiveCalls = 0,
    [property: JsonPropertyName("transitions")] IReadOnlyList<SelectionTransition>? Transitions = null,
    [property: JsonPropertyName("successes")] long Successes = 0,
    [property: JsonPropertyName("lastCost")] long LastCost = 0,
    [property: JsonPropertyName("configurationData")] JsonElement? ConfigurationData = null,
    [property: JsonPropertyName("components")] IReadOnlyDictionary<string, JsonElement>? Components = null,
    [property: JsonPropertyName("schedule")] ScheduleCheckpoint? Schedule = null,
    [property: JsonPropertyName("version")] int Version = 1);
