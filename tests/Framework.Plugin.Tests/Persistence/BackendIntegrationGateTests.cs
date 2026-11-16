#nullable enable

using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using Xunit;

namespace Fuookami.Ospf.Framework.Plugin.Tests.Persistence;

/// <summary>
/// **三个后端真实往返的护栏：开关还在、Kafka 的 flush 没有被改回去** /
/// Guards for the three backend round-trips: the gates still exist and Kafka still flushes on dispose.
/// </summary>
/// <remarks>
/// **这两条判据守的都不是"逻辑对不对"，而是"证据还能不能重新拿到"：**
/// **① 四个集成开关必须留在测试工程里**——它们一消失，那九条真实往返就再也跑不起来，而失败方式是"什么都没发生"；
/// **② `KafkaProducer.Dispose` 必须在释放前 flush**—— 实测过：不 flush 时消息静默丢失、topic 偏移停在 0。
/// **把 `Dispose` 改回一行销毁的实现，这条判据会红。**
///
/// **Neither criterion guards correctness of the logic; both guard whether the evidence can be obtained again:** **the four integration switches must
/// stay in the test project, because losing them makes the nine real round-trips unrunnable and the failure mode is "nothing happened", and
/// `KafkaProducer.Dispose` must flush before disposing, because measured that without the flush messages are silently lost and the topic offset
/// stays at 0.** **Reverting `Dispose` to a one-line destroy turns this red.**
/// </remarks>
public class BackendIntegrationGateTests
{
    /// <summary>三个集成开关都在 / The three integration switches are present.</summary>
    [Fact]
    public void TheIntegrationSwitches_ShouldStillExist() {
        string project = File.ReadAllText(Path.Combine(Root(), "tests", "Framework.Plugin.Tests", "Framework.Plugin.Tests.csproj"));

        foreach ((string property, string constant) in new[] {
                     ("RunMySqlIntegration", "MYSQL_INTEGRATION"),
                     ("RunPostgreSqlIntegration", "POSTGRES_INTEGRATION"),
                     ("RunKafkaIntegration", "KAFKA_INTEGRATION"),
                     ("RunMongoIntegration", "MONGO_INTEGRATION"),
                 }) {
            project.Should().Contain(property, property + " is the switch the run instruction uses");
            project.Should().Contain(constant, constant + " is what the test file compiles against");
        }
    }

    /// <summary>Kafka 生产者释放前刷新 / The Kafka producer flushes before disposing.</summary>
    [Fact]
    public void TheKafkaProducer_ShouldFlushBeforeDisposing() {
        string source = File.ReadAllText(Path.Combine(
            Root(), "src", "Framework.Plugin", "Message", "Kafka", "KafkaProducer.cs"));

        source.Should().Contain("Flush(", "B1307 measured that without a flush the queued message never reaches the broker");
        source.Should().NotContain(
            "public void Dispose() => _producer.Dispose();",
            "the one-line destroy is exactly what dropped queued messages; keeping it would make the round-trip test lose its subject");
    }

    private static string Root() {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ospf-csharp.sln"))) {
            directory = directory.Parent;
        }
        directory.Should().NotBeNull("the repository root holds the solution file");
        return directory!.FullName;
    }
}