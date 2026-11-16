// 搜索存储测试 / Tests for the search store.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Fuookami.Ospf.Core.Solver.Heuristic;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator;
using Fuookami.Ospf.Core.Solver.Heuristic.Persistence;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Core.Tests;

/// <summary>
/// 文件搜索存储 / The file-backed search store.
/// </summary>
public sealed class FileSearchStoreTests : IDisposable {
    private readonly string _directory;

    /// <summary>构造一个临时根目录 / Construct a temporary root directory.</summary>
    public FileSearchStoreTests() {
        _directory = Path.Combine(Path.GetTempPath(), $"ospf-store-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_directory);
    }

    /// <inheritdoc/>
    public void Dispose() {
        if (Directory.Exists(_directory)) {
            Directory.Delete(_directory, recursive: true);
        }
    }

    /// <summary>**策略可以往返保存与加载** / A policy round-trips through save and load.</summary>
    [Fact]
    public void Policy_ShouldRoundTrip() {
        var store = new FileSearchStore(_directory);
        PolicyArchive archive = BuildArchive("progress:1");

        store.SavePolicy("policy-one", archive).Should()
            .BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();
        Result<PolicyArchive, ErrorCode, Error<ErrorCode>> loaded = store.LoadPolicy("policy-one");

        loaded.Should().BeOfType<Ok<PolicyArchive, ErrorCode, Error<ErrorCode>>>();
        PolicyArchive restored = ((Ok<PolicyArchive, ErrorCode, Error<ErrorCode>>)loaded).Value;
        restored.FeatureVersion.Should().Be("progress:1");
        restored.Root.Actions.Should().Equal("root");
        restored.Operators["pool"]["a"].Actions.Should().Equal("a");
    }

    /// <summary>**检查点可以往返保存与加载** / A checkpoint round-trips through save and load.</summary>
    [Fact]
    public void Checkpoint_ShouldRoundTrip() {
        var store = new FileSearchStore(_directory);
        RunCheckpoint checkpoint = BuildCheckpoint();

        store.SaveCheckpoint("checkpoint-one", checkpoint).Should()
            .BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();
        Result<RunCheckpoint, ErrorCode, Error<ErrorCode>> loaded = store.LoadCheckpoint("checkpoint-one");

        loaded.Should().BeOfType<Ok<RunCheckpoint, ErrorCode, Error<ErrorCode>>>();
        ((Ok<RunCheckpoint, ErrorCode, Error<ErrorCode>>)loaded).Value.Evaluations.Should().Be(10L);
    }

    /// <summary>**路径逃逸被拒绝** / A path escape is rejected.</summary>
    /// <remarks>
    /// 只做字符串检查不够：`a/../../etc/passwd` 在字符串层面看不出问题，规范化之后才逃出根目录。
    /// A string-only check is insufficient: `a/../../etc/passwd` looks fine at the string level and only escapes the root after
    /// normalization.
    /// </remarks>
    [Fact]
    public void Key_ShouldRejectPathEscape() {
        var store = new FileSearchStore(_directory);

        store.SavePolicy("..", BuildArchive("progress:1"))
            .Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
        store.SavePolicy("a/b", BuildArchive("progress:1"))
            .Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
        store.SavePolicy("", BuildArchive("progress:1"))
            .Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
        store.SavePolicy("a:b", BuildArchive("progress:1"))
            .Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**包含性检查是等价变异** / The containment check is an equivalent mutant.</summary>
    /// <remarks>
 /// 最初以为需要一个"只有规范化后检查才能拦下"的键来证伪这条防线，实测**做不到**：只要键里不含 `/`、`\`、`:` 且不是
    /// `..`，`Path.Combine(root, key)` 加 `GetFullPath` 就**不可能**逃出根目录——单段文件名没有逃逸手段。
    /// 把包含性检查改成 `true` 后，上面 `Key_ShouldRejectPathEscape` 的四项断言**仍然全部通过**。
    ///
    /// 因此这是**第六次等价变异**。保留它的理由与前面几次不同：**它不是"基线里的防御性代码"，而是"纵深防御的第二道"**——
    /// 字符串检查是白名单式的（拒绝分隔符），包含性检查是黑盒式的（验证结果）。白名单写错一个字符，第二道仍能兜住；
    /// 但它当前不承担可观测职责，**不能声称它在防住什么**。
    /// I originally assumed a key that only the post-normalization check could stop would disprove this line, but that proved
    /// **impossible**: as long as the key contains no `/`, `\` or `:` and is not `..`, `Path.Combine(root, key)` plus `GetFullPath`
    /// **cannot** escape the root — a single path segment has no escape mechanism. Replacing the containment check with `true` still
    /// passes all four assertions in `Key_ShouldRejectPathEscape` above.
    ///
    /// So this is the **sixth equivalent mutant**. It is kept for a different reason than the earlier ones: **it is not "defensive code
    /// from the baseline" but "the second layer of defence in depth"** — the string check is whitelist-style (rejecting separators) and
    /// the containment check is black-box-style (verifying the result). If the whitelist misses a character, the second layer still
    /// catches it; but it currently carries no observable duty, and **we must not claim it stops anything**.
    /// </remarks>
    [Fact]
    public void Key_ShouldAcceptPlainSingleSegment() {
        var store = new FileSearchStore(_directory);

        store.SavePolicy("plain-key", BuildArchive("progress:1"))
            .Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**篡改载荷后校验和不符，加载失败** / A tampered payload fails the checksum on load.</summary>
    /// <remarks>
    /// 校验和不符意味着内容被改动过或写入被截断，必须失败而不是尽力解析。
    /// A checksum mismatch means the content was modified or the write truncated; it must fail rather than best-effort parse.
    /// </remarks>
    [Fact]
    public void Load_ShouldRejectTamperedPayload() {
        var store = new FileSearchStore(_directory);
        store.SavePolicy("policy-one", BuildArchive("progress:1")).Should()
            .BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();

        string path = Path.Combine(_directory, "policy-one");
        StoredDocument document = JsonSerializer.Deserialize<StoredDocument>(
            File.ReadAllText(path))!;
        StoredDocument tampered = document with {
            Payload = JsonSerializer.SerializeToElement(new { definition = "tampered" }),
        };
        File.WriteAllText(path, JsonSerializer.Serialize(tampered));

        store.LoadPolicy("policy-one")
            .Should().BeOfType<Failed<PolicyArchive, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**类别不符被拒绝** / A wrong document kind is rejected.</summary>
    /// <remarks>
    /// 类别不符意味着这份文件本来是另一种文档，必须失败而不是尽力解析。
    /// A wrong kind means the file was originally a different document; it must fail rather than best-effort parse.
    /// </remarks>
    [Fact]
    public void Load_ShouldRejectWrongKind() {
        var store = new FileSearchStore(_directory);
        store.SaveCheckpoint("shared-key", BuildCheckpoint()).Should()
            .BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();

        store.LoadPolicy("shared-key")
            .Should().BeOfType<Failed<PolicyArchive, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**版本 1 的文档不经过迁移** / Version-1 documents bypass migration.</summary>
    /// <remarks>
    /// 让迁移器改写一份本已正确的文档，会把"无需迁移"变成"被迁移过"。
    /// Letting a migrator rewrite an already-correct document turns "no migration needed" into "migrated".
    /// </remarks>
    [Fact]
    public void Load_ShouldSkipMigrationForVersionOne() {
        var migrations = new Dictionary<int, IConfigurationMigration> {
            [1] = new RecordingMigration(),
        };
        var store = new FileSearchStore(_directory, migrations);
        store.SavePolicy("policy-one", BuildArchive("progress:1")).Should()
            .BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();

        store.LoadPolicy("policy-one").Should()
            .BeOfType<Ok<PolicyArchive, ErrorCode, Error<ErrorCode>>>();
        ((RecordingMigration)migrations[1]).Calls.Should().Be(0,
            "版本 1 不得触发迁移 / version 1 must not trigger a migration");
    }

    /// <summary>**迁移器必须产出版本 1** / A migrator must produce version 1.</summary>
    [Fact]
    public void MigrateDocument_ShouldRequireVersionOne() {
        JsonObject document = JsonNode.Parse("{\"version\":2,\"definition\":\"x\"}")!.AsObject();
        var migrations = new Dictionary<int, IConfigurationMigration> {
            [2] = new StuckMigration(),
        };

        FileSearchStore.MigrateDocument(document, migrations)
            .Should().BeOfType<Failed<JsonObject, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**缺失的版本被拒绝** / An unsupported version is rejected.</summary>
    [Fact]
    public void MigrateDocument_ShouldRejectUnknownVersion() {
        JsonObject document = JsonNode.Parse("{\"version\":7,\"definition\":\"x\"}")!.AsObject();

        FileSearchStore.MigrateDocument(document, new Dictionary<int, IConfigurationMigration>())
            .Should().BeOfType<Failed<JsonObject, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**成功的迁移返回版本 1 的文档** / A successful migration returns a version-1 document.</summary>
    [Fact]
    public void MigrateDocument_ShouldReturnVersionOne() {
        JsonObject document = JsonNode.Parse("{\"version\":2,\"definition\":\"x\"}")!.AsObject();
        var migrations = new Dictionary<int, IConfigurationMigration> {
            [2] = new UpgradingMigration(),
        };

        Result<JsonObject, ErrorCode, Error<ErrorCode>> migrated =
            FileSearchStore.MigrateDocument(document, migrations);

        migrated.Should().BeOfType<Ok<JsonObject, ErrorCode, Error<ErrorCode>>>();
        ((Ok<JsonObject, ErrorCode, Error<ErrorCode>>)migrated).Value["version"]!.GetValue<int>()
            .Should().Be(1);
    }

    /// <summary>**非法调度日志被拒绝写入** / An invalid schedule log is rejected before writing.</summary>
    /// <remarks>
 /// **往返已由 `ScheduleLogRoundTripTests` 覆盖**（earlier 找到真正成因：`ScheduleLog` 有两个构造函数，
    /// `System.Text.Json` 于是选不出反序列化目标）。本测试只保留"先校验后写入"这一步。
    ///
 /// **The round trip is covered by `ScheduleLogRoundTripTests`** (earlier found the actual cause: `ScheduleLog` had two constructors, leaving
    /// `System.Text.Json` unable to pick a deserialisation target). This test keeps only the "validate before write" step.
    ///`ScheduleLogs.Fingerprint` 用一份手工组装的字典序列化
    /// （`version`/`configuration`/`problem`/`events`），而 `ScheduleLog` 自身的 `JsonPropertyName` 序列化会带上
    /// `checksum` 字段——两者产生的 JSON 不同，于是**读回后重算的摘要与原摘要不符**，`Validate` 必然失败。
 /// 这是 `ScheduleLog` 自身的问题（属于 earlier 的范围），不是存储层的问题；在这里测往返会得到一个
    /// "看起来是存储 bug"的假象。已记入计划，留待 `ScheduleLog` 往返修复后补测。
 /// ** only tests the "validate before write" step, not the round trip.** `ScheduleLogs.Fingerprint` serializes a
    /// hand-assembled dictionary (`version`/`configuration`/`problem`/`events`), while `ScheduleLog`'s own `JsonPropertyName`
    /// serialization also carries `checksum` — the two JSON texts differ, so **the digest recomputed after reading back no longer
 /// matches**, and `Validate` necessarily fails. That is a `ScheduleLog` problem (earlier's scope), not a store problem; testing
    /// the round trip here would produce a false "looks like a store bug" signal. It is recorded in the plan and will be covered once
    /// `ScheduleLog` round-tripping is fixed.
    /// </remarks>
    [Fact]
    public void Schedule_ShouldRejectInvalidLogBeforeWriting() {
        var store = new FileSearchStore(_directory);
        // 空事件序列过不了 `isSealed`（缺 `end`），必须被拒绝。
        // An empty event list fails `isSealed` (no `end`) and must be rejected.
        ScheduleLog log = ScheduleLogs.Of("config", "problem", Array.Empty<ScheduleEvent>());

        store.SaveSchedule("schedule-one", log).Should()
            .BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
        File.Exists(Path.Combine(_directory, "schedule-one")).Should().BeFalse(
            "校验失败不得留下文件 / a validation failure must not leave a file behind");
    }

    /// <summary>**不存在的键加载失败** / Loading a missing key fails.</summary>
    [Fact]
    public void Load_ShouldFailForMissingKey() {
        var store = new FileSearchStore(_directory);

        store.LoadPolicy("absent")
            .Should().BeOfType<Failed<PolicyArchive, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>构造一个策略档案 / Build a policy archive.</summary>
    /// <param name="featureVersion">特征版本 / The feature version.</param>
    /// <returns>策略档案 / The policy archive.</returns>
    private static PolicyArchive BuildArchive(string featureVersion) => new(
        "definition", featureVersion, Snapshot("root"),
        new Dictionary<string, IReadOnlyDictionary<string, PolicySnapshot>>(StringComparer.Ordinal) {
            ["pool"] = new Dictionary<string, PolicySnapshot>(StringComparer.Ordinal) { ["a"] = Snapshot("a") },
        });

    /// <summary>构造一个策略快照 / Build a policy snapshot.</summary>
    /// <param name="action">动作名 / The action name.</param>
    /// <returns>策略快照 / The policy snapshot.</returns>
    private static PolicySnapshot Snapshot(string action) => new(
        1, "fixed", new[] { action }, 0L, 0L, new[] { 0L }, new[] { 0.0 }, null,
        JsonDocument.Parse("null").RootElement.Clone());

    /// <summary>构造一个运行检查点 / Build a run checkpoint.</summary>
    /// <returns>运行检查点 / The run checkpoint.</returns>
    private static RunCheckpoint BuildCheckpoint() => new(
        Configuration: "config",
        Problem: "problem",
        Policy: BuildArchive("progress:1"),
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
        Transitions: Array.Empty<SelectionTransition>(),
        Components: new Dictionary<string, JsonElement>(StringComparer.Ordinal));

    /// <summary>记录调用次数的迁移器 / A migrator that records calls.</summary>
    private sealed class RecordingMigration : IConfigurationMigration {
        /// <summary>调用次数 / The call count.</summary>
        public int Calls { get; private set; }

        /// <inheritdoc/>
        public Result<JsonObject, ErrorCode, Error<ErrorCode>> Migrate(JsonObject document) {
            Calls++;
            return Results.Ok(document);
        }
    }

    /// <summary>一个不产出版本 1 的迁移器 / A migrator that does not produce version 1.</summary>
    private sealed class StuckMigration : IConfigurationMigration {
        /// <inheritdoc/>
        public Result<JsonObject, ErrorCode, Error<ErrorCode>> Migrate(JsonObject document) =>
            Results.Ok(JsonNode.Parse("{\"version\":3,\"definition\":\"x\"}")!.AsObject());
    }

    /// <summary>一个正确升到版本 1 的迁移器 / A migrator that correctly upgrades to version 1.</summary>
    private sealed class UpgradingMigration : IConfigurationMigration {
        /// <inheritdoc/>
        public Result<JsonObject, ErrorCode, Error<ErrorCode>> Migrate(JsonObject document) =>
            Results.Ok(JsonNode.Parse("{\"version\":1,\"definition\":\"x\"}")!.AsObject());
    }
}
