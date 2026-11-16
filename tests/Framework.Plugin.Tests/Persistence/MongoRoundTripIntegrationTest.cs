#nullable enable
#if MONGO_INTEGRATION

using System;
using System.Threading.Tasks;
using FluentAssertions;
using Fuookami.Ospf.Framework.Plugin.Persistence.MongoDB;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using MongoDB.Bson;
using MongoDB.Driver;
using Xunit;

namespace Fuookami.Ospf.Framework.Plugin.Tests.Persistence;

/// <summary>
/// MongoDB 真实往返（连本机实例，不是影子检查）/ A real MongoDB round-trip against a live instance.
/// </summary>
/// <remarks>
/// 这是"环境所限"那一组的最后一个：MySQL、PostgreSQL、Redis、Kafka 都已验过，MongoDB 一直缺实例。
/// 此后了一个本机容器，于是它也能用同一组判据验：入口能连、增查改删往返、不可达报原因。
///
/// This is the last of the "limited by the environment" list: MySQL, PostgreSQL, Redis and Kafka were verified, while MongoDB had no
/// instance. A local container starts, so it gets the same set of criteria: the entry point connects, five round-trip steps, and an unreachable
/// endpoint explains itself.
///
/// 连接串从环境变量 `OSPF_TEST_MONGO_CONN` 读；未设时取本机缺省。
/// The connection string comes from `OSPF_TEST_MONGO_CONN`, defaulting to the local instance.
/// </remarks>
public class MongoRoundTripIntegrationTest
{
    private const string DefaultConnection = "mongodb://127.0.0.1:27017";
    private const string DatabaseName = "ospf_test";

    private static string ConnectionString =>
        Environment.GetEnvironmentVariable("OSPF_TEST_MONGO_CONN") ?? DefaultConnection;

    /// <summary>入口能连上：真实跑一次 `ping` / The entry point connects: it really runs `ping`.</summary>
    /// <remarks>
    /// **不用"构造成功"当证据：** **`MongoClient` 的构造是惰性的，连不上也照样返回对象。** **所以要真发一条命令。**
    ///
    /// **Construction is not evidence: `MongoClient` builds lazily and returns an object whether or not the server is there.** **So a command is
    /// really sent.**
    /// </remarks>
    [Fact]
    public async Task TheEntryPoint_ShouldConnect() {
        var client = Client(ConnectionString);
        BsonDocument reply = await client.GetDatabase(DatabaseName).RunCommandAsync<BsonDocument>(
            new BsonDocument("ping", 1));

        reply.GetValue("ok", 0).ToDouble().Should().Be(
            1.0,
            "the server answered the ping with ok: 1, which is the only proof that it is really there");
    }

    /// <summary>插入、按主键查、更新、删除，四步都断言真实结果 / Insert, find by key, update, delete — each asserted.</summary>
    [Fact]
    public async Task TheRepository_ShouldRoundTripADocument() {
        var client = Client(ConnectionString);
        IMongoDatabase database = client.GetDatabase(DatabaseName);
        await database.DropCollectionAsync("entries");

        var repository = new EntryRepository(database);
        var document = new RoundTripDocument { Name = "first" };

        (await repository.InsertAsync(document)).IsOk.Should().BeTrue("the insert goes through the repository");
        document.Id.Should().NotBe(ObjectId.Empty, "the driver assigned an identifier");

        (await repository.FindAllAsync()).Value.Should().HaveCount(1, "one document was inserted into an emptied collection");

        var found = await repository.FindByIdAsync(document.Id);
        found.IsOk.Should().BeTrue("the document just inserted must be findable");
        found.Value.Should().NotBeNull();
        found.Value!.Name.Should().Be("first", "the field round-trips its text as stored");

        document.Name = "second";
        // **`UpdateAsync` 的默认实现是 replace-one，返回 ModifiedCount。** **已存在的文档改一个字段，ModifiedCount 就是 1。**
        // **The default `UpdateAsync` is a replace-one returning ModifiedCount, and changing one field of an existing document gives 1.**
        (await repository.UpdateAsync(document)).Value.Should().Be(
            1L,
            "exactly one document matched the key, so the replace reports one modified document");

        (await repository.FindByIdAsync(document.Id)).Value!.Name.Should().Be(
            "second",
            "the replace really landed, not just returned ok");

        (await repository.DeleteAsync(document.Id)).IsOk.Should().BeTrue(
            "the repository deletes by key, so the key is what it gets");
        (await repository.FindAllAsync()).Value.Should().BeEmpty("the only document was deleted");
    }

    /// <summary>不可达端点报出原因，而不是抛异常或挂住 / An unreachable endpoint reports why instead of throwing or hanging.</summary>
    /// <remarks>
    /// **端口 1 上没有服务；`ServerSelectionTimeout` 设成 3 秒，让失败有界。** **断言的是"它给了原因"——**
    /// **一条连不上却没有原因的错误，在排障时与没有这条测试等价。**
    ///
    /// **Nothing listens on port 1, and `ServerSelectionTimeout` is three seconds so the failure stays bounded.** **What is asserted is that it gives a
    /// reason: a "cannot connect" with no reason is worth as much as no test at all when you are debugging.**
    /// </remarks>
    [Fact]
    public async Task AnUnreachableEndpoint_ShouldReportWhy() {
        var settings = MongoClientSettings.FromConnectionString("mongodb://127.0.0.1:1");
        settings.ServerSelectionTimeout = TimeSpan.FromSeconds(3);
        var repository = new EntryRepository(new MongoClient(settings).GetDatabase(DatabaseName));

        var result = await repository.FindAllAsync();
        result.IsOk.Should().BeFalse("port 1 has no server");
        string reason = ((Failed<System.Collections.Generic.IReadOnlyList<RoundTripDocument>, ErrorCode, Error<ErrorCode>>)result).Message;
        reason.Should().Contain("MongoDB", "the message has to name the backend, or a multi-backend failure is unattributable");
        reason.Length.Should().BeGreaterThan(20, "the reason has to carry the driver's own words, not just a code");
    }

    private static IMongoClient Client(string connectionString) => new MongoClient(connectionString);

    /// <summary>往返用的文档 / The document used for the round-trip.</summary>
    public sealed class RoundTripDocument {
        public ObjectId Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    private sealed class EntryRepository(IMongoDatabase database)
        : MongoRepository<RoundTripDocument>(database, "entries") {
    }
}
#endif