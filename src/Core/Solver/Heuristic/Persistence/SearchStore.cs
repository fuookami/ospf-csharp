// 搜索存储 / The search store.
// 与固定 Kotlin （6559 B）对应。

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Fuookami.Ospf.Core.Solver.Heuristic.Operator;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Core.Solver.Heuristic.Persistence;

/// <summary>
/// 搜索存储 / A search store.
/// </summary>
public interface ISearchStore {
    /// <summary>保存策略 / Save a policy.</summary>
    /// <param name="key">键 / The key.</param>
    /// <param name="policy">策略档案 / The policy archive.</param>
    /// <returns>成功或失败 / Success or failure.</returns>
    Result<Success, ErrorCode, Error<ErrorCode>> SavePolicy(string key, PolicyArchive policy);

    /// <summary>加载策略 / Load a policy.</summary>
    /// <param name="key">键 / The key.</param>
    /// <returns>策略档案 / The policy archive.</returns>
    Result<PolicyArchive, ErrorCode, Error<ErrorCode>> LoadPolicy(string key);

    /// <summary>保存检查点 / Save a checkpoint.</summary>
    /// <param name="key">键 / The key.</param>
    /// <param name="checkpoint">检查点 / The checkpoint.</param>
    /// <returns>成功或失败 / Success or failure.</returns>
    Result<Success, ErrorCode, Error<ErrorCode>> SaveCheckpoint(string key, RunCheckpoint checkpoint);

    /// <summary>加载检查点 / Load a checkpoint.</summary>
    /// <param name="key">键 / The key.</param>
    /// <returns>检查点 / The checkpoint.</returns>
    Result<RunCheckpoint, ErrorCode, Error<ErrorCode>> LoadCheckpoint(string key);
}

/// <summary>
/// 配置迁移 / A configuration migration.
/// </summary>
public interface IConfigurationMigration {
    /// <summary>
    /// 迁移一份文档 / Migrate a document.
    /// </summary>
    /// <param name="document">文档 / The document.</param>
    /// <returns>迁移后的文档 / The migrated document.</returns>
    Result<JsonObject, ErrorCode, Error<ErrorCode>> Migrate(JsonObject document);
}

/// <summary>
/// 存储文档信封 / A stored-document envelope.
/// </summary>
/// <param name="Kind">文档类别 / The document kind.</param>
/// <param name="Checksum">校验和 / The checksum.</param>
/// <param name="Payload">载荷 / The payload.</param>
/// <param name="Version">版本 / The version.</param>
public sealed record StoredDocument(
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("checksum")] string Checksum,
    [property: JsonPropertyName("payload")] JsonElement Payload,
    [property: JsonPropertyName("version")] int Version = 1);

/// <summary>
/// 文件存储 / A file-backed search store.
/// </summary>
/// <remarks>
/// **键必须被校验成"根目录内的单段文件名"**：拒绝空白、路径分隔符、`:`、`.` 与 `..`，并在规范化后确认结果仍在根目录之下。
/// 只做其中一步都不够——`a/../../etc/passwd` 规范化后逃出根目录，而字符串检查看不出这一点。
/// **Keys must be validated as "a single path segment inside the root"**: rejecting blanks, separators, `:`, `.` and `..`, and then
/// confirming the normalized result still lies below the root. Neither step alone suffices — `a/../../etc/passwd` escapes the root after
/// normalization while a string-only check sees nothing wrong.
/// **写入先落到同目录的临时文件，再原子替换**。直接写目标文件会让一次崩溃留下半截文件，而校验和恰好覆盖"文件写了一半"这种
/// 情形——**读回来会报损坏，但原始数据已经没了**。
/// **Writes go to a temporary file in the same directory first, then atomically replace.** Writing the target directly leaves a truncated
/// file after a crash, and the checksum covers exactly the "half-written file" case — **reading back reports corruption while the
/// original data is already gone**.
/// **读取拒绝符号链接**。符号链接能让一次读取越过根目录，而键校验在写入侧通过、读取侧却绕开了它。
/// **Reads refuse symbolic links.** A symlink lets one read escape the root while the key check passes on the write side and is bypassed
/// on the read side.
/// **读取拒绝超过 64 MB 的文件，并且拒绝"类别不符"与"校验和不符"**。类别不符意味着这份文件本来是另一种文档，
/// 校验和不符意味着内容被改动过或写入被截断；两者都必须失败而不是尽力解析。
/// **Reads refuse files over 64 MB and reject both "wrong kind" and "checksum mismatch"**. A wrong kind means the file was originally a
/// different document, and a checksum mismatch means the content was modified or the write truncated; both must fail rather than
/// best-effort parse.
/// **版本 1 的文档不经过任何迁移直接返回**。对版本 1 也跑迁移会让一份本已正确的文档被迁移器改写，**而迁移器自己不做版本检查**。
/// **Version-1 documents bypass every migration.** Running migrations on version 1 lets a correct document be rewritten by a migrator,
/// **and the migrator itself does not check versions**.
/// **迁移器必须产出版本 1**。不检查会让一条迁移链停在中间版本，而文档仍然"能解析"、只是字段语义不对。
/// **A migrator must produce version 1.** Without the check a migration chain can stop at an intermediate version while the document
/// still "parses" — only its field semantics are wrong.
/// </remarks>
public sealed class FileSearchStore : ISearchStore {
    private const long MaxBytes = 64L * 1024 * 1024;

    private readonly string _root;
    private readonly IReadOnlyDictionary<int, IConfigurationMigration> _policyMigrations;
    private readonly IReadOnlyDictionary<int, IConfigurationMigration> _checkpointMigrations;

    /// <summary>
    /// 构造存储 / Construct the store.
    /// </summary>
    /// <param name="directory">根目录 / The root directory.</param>
    /// <param name="policyMigrations">策略迁移器 / The policy migrations.</param>
    /// <param name="checkpointMigrations">检查点迁移器 / The checkpoint migrations.</param>
    public FileSearchStore(
        string directory,
        IReadOnlyDictionary<int, IConfigurationMigration>? policyMigrations = null,
        IReadOnlyDictionary<int, IConfigurationMigration>? checkpointMigrations = null) {
        ArgumentNullException.ThrowIfNull(directory);
        _root = Path.GetFullPath(directory);
        _policyMigrations = policyMigrations
            ?? new Dictionary<int, IConfigurationMigration>();
        _checkpointMigrations = checkpointMigrations
            ?? new Dictionary<int, IConfigurationMigration>();
    }

    /// <summary>根目录 / The root directory.</summary>
    public string Root => _root;

    /// <summary>
    /// 保存调度日志 / Save a schedule log.
    /// </summary>
    /// <param name="key">键 / The key.</param>
    /// <param name="log">调度日志 / The schedule log.</param>
    /// <returns>成功或失败 / Success or failure.</returns>
    public Result<Success, ErrorCode, Error<ErrorCode>> SaveSchedule(string key, ScheduleLog log) {
        ArgumentNullException.ThrowIfNull(log);
        Result<Success, ErrorCode, Error<ErrorCode>> valid = log.Validate();
        if (valid is Failed<Success, ErrorCode, Error<ErrorCode>> invalid) {
            return Results.Failed<Success>(invalid.Error);
        }
        return Write(key, "schedule", JsonSerializer.SerializeToElement(log));
    }

    /// <summary>
    /// 加载调度日志 / Load a schedule log.
    /// </summary>
    /// <param name="key">键 / The key.</param>
    /// <returns>调度日志 / The schedule log.</returns>
    public Result<ScheduleLog, ErrorCode, Error<ErrorCode>> LoadSchedule(string key) {
        Result<JsonElement, ErrorCode, Error<ErrorCode>> read = Read(key, "schedule");
        if (read is Failed<JsonElement, ErrorCode, Error<ErrorCode>> readFailed) {
            return Results.Failed<ScheduleLog>(readFailed.Error);
        }
        Result<ScheduleLog, ErrorCode, Error<ErrorCode>> decoded = SearchBoundary.Execute(
            () => JsonSerializer.Deserialize<ScheduleLog>(
                ((Ok<JsonElement, ErrorCode, Error<ErrorCode>>)read).Value.GetRawText())!);
        if (decoded is Failed<ScheduleLog, ErrorCode, Error<ErrorCode>> decodeFailed) {
            return Results.Failed<ScheduleLog>(decodeFailed.Error);
        }
        ScheduleLog log = ((Ok<ScheduleLog, ErrorCode, Error<ErrorCode>>)decoded).Value;
        Result<Success, ErrorCode, Error<ErrorCode>> valid = log.Validate();
        return valid is Failed<Success, ErrorCode, Error<ErrorCode>> invalid
            ? Results.Failed<ScheduleLog>(invalid.Error)
            : Results.Ok(log);
    }

    /// <inheritdoc/>
    public Result<Success, ErrorCode, Error<ErrorCode>> SavePolicy(string key, PolicyArchive policy) {
        ArgumentNullException.ThrowIfNull(policy);
        return Write(key, "policy", JsonSerializer.SerializeToElement(policy));
    }

    /// <inheritdoc/>
    public Result<PolicyArchive, ErrorCode, Error<ErrorCode>> LoadPolicy(string key) {
        Result<JsonElement, ErrorCode, Error<ErrorCode>> read = Read(key, "policy");
        if (read is Failed<JsonElement, ErrorCode, Error<ErrorCode>> readFailed) {
            return Results.Failed<PolicyArchive>(readFailed.Error);
        }
        Result<JsonObject, ErrorCode, Error<ErrorCode>> migrated = Migrate(
            ((Ok<JsonElement, ErrorCode, Error<ErrorCode>>)read).Value, _policyMigrations);
        if (migrated is Failed<JsonObject, ErrorCode, Error<ErrorCode>> migrateFailed) {
            return Results.Failed<PolicyArchive>(migrateFailed.Error);
        }
        JsonObject document = ((Ok<JsonObject, ErrorCode, Error<ErrorCode>>)migrated).Value;
        return SearchBoundary.Execute(
            () => JsonSerializer.Deserialize<PolicyArchive>(document.ToJsonString())!);
    }

    /// <inheritdoc/>
    public Result<Success, ErrorCode, Error<ErrorCode>> SaveCheckpoint(string key, RunCheckpoint checkpoint) {
        ArgumentNullException.ThrowIfNull(checkpoint);
        return Write(key, "checkpoint", JsonSerializer.SerializeToElement(checkpoint));
    }

    /// <inheritdoc/>
    public Result<RunCheckpoint, ErrorCode, Error<ErrorCode>> LoadCheckpoint(string key) {
        Result<JsonElement, ErrorCode, Error<ErrorCode>> read = Read(key, "checkpoint");
        if (read is Failed<JsonElement, ErrorCode, Error<ErrorCode>> readFailed) {
            return Results.Failed<RunCheckpoint>(readFailed.Error);
        }
        Result<JsonObject, ErrorCode, Error<ErrorCode>> migrated = Migrate(
            ((Ok<JsonElement, ErrorCode, Error<ErrorCode>>)read).Value, _checkpointMigrations);
        if (migrated is Failed<JsonObject, ErrorCode, Error<ErrorCode>> migrateFailed) {
            return Results.Failed<RunCheckpoint>(migrateFailed.Error);
        }
        JsonObject document = ((Ok<JsonObject, ErrorCode, Error<ErrorCode>>)migrated).Value;
        return SearchBoundary.Execute(
            () => JsonSerializer.Deserialize<RunCheckpoint>(document.ToJsonString())!);
    }

    /// <summary>
    /// 迁移一份文档 / Migrate a document.
    /// </summary>
    /// <param name="value">载荷 / The payload.</param>
    /// <param name="migrations">迁移器 / The migrations.</param>
    /// <returns>迁移后的文档 / The migrated document.</returns>
    private static Result<JsonObject, ErrorCode, Error<ErrorCode>> Migrate(
        JsonElement value, IReadOnlyDictionary<int, IConfigurationMigration> migrations) {
        if (value.ValueKind != JsonValueKind.Object) {
            return SearchCodecSupport.SearchInvalid<JsonObject>("stored payload must be an object");
        }
        JsonObject document = JsonNode.Parse(value.GetRawText())!.AsObject();
        return MigrateDocument(document, migrations);
    }

    /// <summary>
    /// 按版本迁移文档 / Migrate a document by version.
    /// </summary>
    /// <param name="document">文档 / The document.</param>
    /// <param name="migrations">迁移器 / The migrations.</param>
    /// <returns>迁移后的文档 / The migrated document.</returns>
    internal static Result<JsonObject, ErrorCode, Error<ErrorCode>> MigrateDocument(
        JsonObject document, IReadOnlyDictionary<int, IConfigurationMigration> migrations) {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(migrations);
        int version = ReadVersion(document);
        // **版本 1 直接返回，不跑迁移**：让迁移器改写一份本已正确的文档，会把"无需迁移"变成"被迁移过"。
        // **Version 1 returns as-is without any migration**: letting a migrator rewrite an already-correct document turns "no migration
        // needed" into "migrated".
        if (version == 1) {
            return Results.Ok(document);
        }
        if (!migrations.TryGetValue(version, out IConfigurationMigration? migration)) {
            return SearchCodecSupport.SearchInvalid<JsonObject>(
                $"unsupported document version: {version}");
        }
        // **迁移器自身已经返回 `Result`，不能再包一层 `SearchBoundary`**：双层包装会得到 `Result<Result<...>>`，
        // 而编译器只会报"无法隐式转换"，不会指出多包了一层。
        // **The migrator already returns a `Result`, so it must not be wrapped in `SearchBoundary` again**: double wrapping yields
        // `Result<Result<...>>`, and the compiler only reports "cannot convert implicitly" without pointing at the extra layer.
        Result<JsonObject, ErrorCode, Error<ErrorCode>> migrated = migration.Migrate(document);
        if (migrated is Failed<JsonObject, ErrorCode, Error<ErrorCode>> failed) {
            return Results.Failed<JsonObject>(failed.Error);
        }
        JsonObject result = ((Ok<JsonObject, ErrorCode, Error<ErrorCode>>)migrated).Value;
        return ReadVersion(result) != 1
            ? SearchCodecSupport.SearchInvalid<JsonObject>("migration must produce version 1")
            : Results.Ok(result);
    }

    /// <summary>
    /// 读取文档版本 / Read a document version.
    /// </summary>
    /// <param name="document">文档 / The document.</param>
    /// <returns>版本 / The version.</returns>
    private static int ReadVersion(JsonObject document) {
        JsonNode? node = document["version"];
        if (node is null) {
            return 1;
        }
        if (node is JsonValue value && value.TryGetValue(out int number)) {
            return number;
        }
        if (node is JsonValue other && other.TryGetValue(out string? text) && int.TryParse(text, out int parsed)) {
            return parsed;
        }
        return 1;
    }

    /// <summary>
    /// 把键解析为目标路径 / Resolve a key into a target path.
    /// </summary>
    /// <param name="key">键 / The key.</param>
    /// <returns>目标路径 / The target path.</returns>
    private Result<string, ErrorCode, Error<ErrorCode>> Target(string key) {
        ArgumentNullException.ThrowIfNull(key);
        if (string.IsNullOrWhiteSpace(key)) {
            return SearchCodecSupport.SearchInvalid<string>("invalid storage key");
        }
        foreach (char character in key) {
            if (character is '/' or '\\' or ':') {
                return SearchCodecSupport.SearchInvalid<string>("invalid storage key");
            }
        }
        if (key is "." or "..") {
            return SearchCodecSupport.SearchInvalid<string>("invalid storage key");
        }
        string target = Path.GetFullPath(Path.Combine(_root, key));
        string normalizedRoot = Path.GetFullPath(_root);
        // **规范化之后再确认仍在根目录之下**：`a/../../etc/passwd` 的字符串检查看不出问题，规范化后才暴露出逃逸。
        // **Re-confirm containment after normalization**: a string check sees nothing wrong with `a/../../etc/passwd`; only after
        // normalization does the escape become visible.
        bool inside = target.Length > normalizedRoot.Length
            && target.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
        if (!inside) {
            return SearchCodecSupport.SearchInvalid<string>("storage path escapes root");
        }
        return Results.Ok(target);
    }

    /// <summary>
    /// 写入一份文档 / Write one document.
    /// </summary>
    /// <param name="key">键 / The key.</param>
    /// <param name="kind">文档类别 / The document kind.</param>
    /// <param name="value">载荷 / The payload.</param>
    /// <returns>成功或失败 / Success or failure.</returns>
    private Result<Success, ErrorCode, Error<ErrorCode>> Write(
        string key, string kind, JsonElement value) {
        Result<string, ErrorCode, Error<ErrorCode>> target = Target(key);
        if (target is Failed<string, ErrorCode, Error<ErrorCode>> targetFailed) {
            return Results.Failed<Success>(targetFailed.Error);
        }
        string destination = ((Ok<string, ErrorCode, Error<ErrorCode>>)target).Value;
        return SearchBoundary.Execute(() => {
            Directory.CreateDirectory(_root);
            string temp = Path.Combine(_root,
                $".search-{Guid.NewGuid():N}.tmp");
            try {
                var document = new StoredDocument(kind, Fingerprint(value), value);
                File.WriteAllText(temp, JsonSerializer.Serialize(document));
                File.Move(temp, destination, overwrite: true);
            } finally {
                if (File.Exists(temp)) {
                    File.Delete(temp);
                }
            }
            return new Success();
        });
    }

    /// <summary>
    /// 读取一份文档 / Read one document.
    /// </summary>
    /// <param name="key">键 / The key.</param>
    /// <param name="kind">文档类别 / The document kind.</param>
    /// <returns>载荷 / The payload.</returns>
    private Result<JsonElement, ErrorCode, Error<ErrorCode>> Read(string key, string kind) {
        Result<string, ErrorCode, Error<ErrorCode>> target = Target(key);
        if (target is Failed<string, ErrorCode, Error<ErrorCode>> targetFailed) {
            return Results.Failed<JsonElement>(targetFailed.Error);
        }
        string source = ((Ok<string, ErrorCode, Error<ErrorCode>>)target).Value;
        if (IsSymbolicLink(source)) {
            return SearchCodecSupport.SearchInvalid<JsonElement>(
                "symbolic-link storage entries are not supported");
        }
        Result<StoredDocument?, ErrorCode, Error<ErrorCode>> decoded = SearchBoundary.Execute(
            () => {
                if (!File.Exists(source)) {
                    return (StoredDocument?)null;
                }
                if (new FileInfo(source).Length > MaxBytes) {
                    return null;
                }
                return JsonSerializer.Deserialize<StoredDocument>(File.ReadAllText(source));
            });
        if (decoded is Failed<StoredDocument?, ErrorCode, Error<ErrorCode>> decodeFailed) {
            return Results.Failed<JsonElement>(decodeFailed.Error);
        }
        StoredDocument? document = ((Ok<StoredDocument?, ErrorCode, Error<ErrorCode>>)decoded).Value;
        if (document is null || document.Version != 1
            || !string.Equals(document.Kind, kind, StringComparison.Ordinal)
            || !string.Equals(document.Checksum, Fingerprint(document.Payload), StringComparison.Ordinal)) {
            return SearchCodecSupport.SearchInvalid<JsonElement>(
                "incompatible or corrupt stored document");
        }
        return Results.Ok(document.Payload);
    }

    /// <summary>
    /// 路径是否为符号链接 / Whether a path is a symbolic link.
    /// </summary>
    /// <param name="path">路径 / The path.</param>
    /// <returns>是否符号链接 / Whether it is a symbolic link.</returns>
    private static bool IsSymbolicLink(string path) {
        try {
            FileInfo info = new(path);
            return info.Exists && info.LinkTarget is not null;
        } catch (IOException) {
            return false;
        }
    }

    /// <summary>
    /// 载荷指纹 / The payload fingerprint.
    /// </summary>
    /// <param name="value">载荷 / The payload.</param>
    /// <returns>指纹 / The fingerprint.</returns>
    private static string Fingerprint(JsonElement value) =>
        SolveFingerprinting.Sha256(value.GetRawText()).Value;
}
