// 持久化层不再有撒谎的名字/ The persistence layer has no lying name left.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Xunit;

namespace Fuookami.Ospf.Framework.Plugin.Tests.Persistence;

/// <summary>
/// **持久化层不再有撒谎的名字** / The persistence layer has no lying name left.
/// </summary>
/// <remarks>
/// 发现 `EfCoreRepository` 名字里有 EF Core、行为是 `List&lt;T&gt;`。 先把名字改对（`InMemoryRepository`，
/// 挪到 `Persistence/InMemory/`），再确认其余四个后端**本来就是真的**：
///
/// | 后端 | 依赖的真实类型 |
/// | --- | --- |
/// | `MySqlRepository` | `DbContext` |
/// | `SqliteRepository` | `DbContext` |
/// | `MongoRepository` | `IMongoDatabase` |
/// | `RedisClient` | `IConnectionMultiplexer` / `IDatabase` |
/// | `KafkaProducer` / `KafkaConsumer` | `IProducer` / `IConsumer` |
///
/// **名字撒谎比没有名字更糟**：读代码的人会以为已经有 EF Core 实现，于是不再去找。
///
/// found `EfCoreRepository` with EF Core in the name and a `List&lt;T&gt;` in the body. fixes the name first (`InMemoryRepository`, moved to `Persistence/InMemory/`), then confirms the other
/// four backends **were real all along** (see the table above). **A lying name is worse than no name**: a reader concludes an EF Core implementation already exists and stops looking.
/// </remarks>
public class PersistenceNamingTests {
    /// <summary>
    /// **没有类型再叫 `EfCoreRepository`，内存仓储在 `InMemory` 命名空间** / No type is named `EfCoreRepository`; the in-memory one lives in the `InMemory` namespace.
    /// </summary>
    /// <remarks>
    /// 三点成对：`Persistence/EfCore/` 下**不再有** `EfCoreRepository.cs` / `Persistence/InMemory/InMemoryRepository.cs` 存在且声明的命名空间是
    /// `...Persistence.InMemory` / 全仓 `.cs` 里再没有 `class EfCoreRepository`——改名要改干净，留一个别名等于没改。
    ///
    /// Three paired points: `Persistence/EfCore/` no longer holds `EfCoreRepository.cs`; `Persistence/InMemory/InMemoryRepository.cs` exists and declares the `...Persistence.InMemory`
    /// namespace; and no `.cs` in the repository declares `class EfCoreRepository` — a rename with a leftover alias is not a rename.
    /// </remarks>
    [Fact]
    public void InMemoryRepository_ShouldBeNamedAfterWhatItIs() {
        string root = RepoRoot();
        string persistence = Path.Combine(root, "src", "Framework.Plugin", "Persistence");

        File.Exists(Path.Combine(persistence, "EfCore", "EfCoreRepository.cs"))
            .Should().BeFalse("a `List<T>` must not be filed under the EF Core provider directory");
        string inMemory = Path.Combine(persistence, "InMemory", "InMemoryRepository.cs");
        File.Exists(inMemory).Should().BeTrue();
        File.ReadAllText(inMemory).Should().Contain("namespace Fuookami.Ospf.Framework.Plugin.Persistence.InMemory;");

        IEnumerable<string> offenders = Directory
            .EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => File.ReadAllText(path).Contains("class EfCoreRepository", StringComparison.Ordinal));
        offenders.Should().BeEmpty("the old name must be gone entirely, not kept as an alias");
    }

    /// <summary>
    /// **其余四个后端本来就是真的** / The other four backends were real all along.
    /// </summary>
    /// <remarks>
    /// 每个后端都必须依赖一个**真实客户端类型**，而不是 `List&lt;T&gt;`：MySQL / SQLite 用 `DbContext`、Mongo 用 `IMongoDatabase`、
    /// Redis 用 `IConnectionMultiplexer`、Kafka 用 `IProducer` / `IConsumer`。任一处退化成内存集合，这条判据就红。
    ///
    /// Every backend must depend on a **real client type** rather than a `List&lt;T&gt;`: MySQL and SQLite on `DbContext`, Mongo on `IMongoDatabase`, Redis on
    /// `IConnectionMultiplexer`, Kafka on `IProducer` / `IConsumer`. If any one degrades to an in-memory collection, this criterion turns red.
    /// </remarks>
    [Fact]
    public void OtherBackends_ShouldHoldRealClients() {
        string root = RepoRoot();
        string persistence = Path.Combine(root, "src", "Framework.Plugin", "Persistence");
        string message = Path.Combine(root, "src", "Framework.Plugin", "Message", "Kafka");

        var expected = new Dictionary<string, string>(StringComparer.Ordinal) {
            [Path.Combine(persistence, "MySQL", "MySqlRepository.cs")] = "DbContext",
            [Path.Combine(persistence, "SQLite", "SqliteRepository.cs")] = "DbContext",
            // PostgreSQL 也必须是真后端**（`DbContext` + Npgsql 提供程序），不是内存集合。
            // PostgreSQL must be a real backend too** (`DbContext` plus the Npgsql provider), not an in-memory collection.
            [Path.Combine(persistence, "PostgreSQL", "PostgreSqlRepository.cs")] = "DbContext",
            [Path.Combine(persistence, "MongoDB", "MongoRepository.cs")] = "IMongoDatabase",
            [Path.Combine(persistence, "Redis", "RedisClient.cs")] = "IConnectionMultiplexer",
            [Path.Combine(message, "KafkaProducer.cs")] = "IProducer",
            [Path.Combine(message, "KafkaConsumer.cs")] = "IConsumer",
        };

        foreach (KeyValuePair<string, string> entry in expected) {
            File.Exists(entry.Key).Should().BeTrue(entry.Key + " must exist");
            File.ReadAllText(entry.Key).Should().Contain(entry.Value,
                Path.GetFileName(entry.Key) + " must talk to a real client, not a list");
        }
    }

    /// <summary>仓库根目录 / The repository root.</summary>
    /// <returns>绝对路径 / The absolute path.</returns>
    private static string RepoRoot() {
        string? directory = AppContext.BaseDirectory;
        while (directory is not null) {
            if (Directory.Exists(Path.Combine(directory, "src")) &&
                File.Exists(Path.Combine(directory, "ospf-csharp.sln"))) {
                return directory;
            }
            directory = Path.GetDirectoryName(directory);
        }
        throw new InvalidOperationException("repository root not found");
    }
}
