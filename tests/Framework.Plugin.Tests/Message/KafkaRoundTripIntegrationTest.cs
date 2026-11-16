#nullable enable
#if KAFKA_INTEGRATION

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using FluentAssertions;
using Confluent.Kafka;
using Fuookami.Ospf.Framework.Plugin.Message.Kafka;
using Xunit;

namespace Fuookami.Ospf.Framework.Plugin.Tests.Message;

/// <summary>
/// Kafka 真实往返（连本机单节点 broker，不是影子检查）/ A real Kafka round-trip against a live single-node broker.
/// </summary>
/// <remarks>
/// 此前 Kafka 被记成"环境未提供"，而它只是没被起起来。补的是三条真的去连它的判据：
/// 生产出去的消息能被消费回来（键与值都对）、JSON 载荷往返、不可达 broker 在超时内返回空而不是挂住。
///
/// Kafka used to be recorded as "not provided by the environment" when it had simply never been started. These three criteria really connect: a
/// produced message comes back with its key and value intact, a JSON payload round-trips, and an unreachable broker yields nothing within the timeout
/// instead of hanging.
///
/// 地址从环境变量 `OSPF_TEST_KAFKA_BOOTSTRAP` 读，缺省 `127.0.0.1:9092`。
/// The address comes from `OSPF_TEST_KAFKA_BOOTSTRAP`, defaulting to `127.0.0.1:9092`.
/// </remarks>
public class KafkaRoundTripIntegrationTest
{
    private static string[] Bootstrap =>
        (Environment.GetEnvironmentVariable("OSPF_TEST_KAFKA_BOOTSTRAP") ?? "127.0.0.1:9092").Split(',');

    /// <summary>生产出去的消息能带着键消费回来 / A produced message comes back with its key.</summary>
    /// <remarks>
    /// **这里断言的是"消息真的在 broker 里"，而不是"`Send` 没抛异常"：** **`KafkaProducer.Send` 是 fire-and-forget，
    /// 投递失败不会回流**——**所以只有消费回来才算证据。**
    ///
    /// **What is asserted is that the message really reached the broker, not that `Send` did not throw: `KafkaProducer.Send` is fire-and-forget and a
    /// delivery failure never comes back, so only the consume side counts as evidence.**
    /// </remarks>
    [Fact]
    public void TheProducer_ShouldDeliverAMessageTheConsumerCanRead() {
        string topic = "ospf-roundtrip-" + Guid.NewGuid().ToString("N")[..8];
        string payload = "hello-ospf-" + Guid.NewGuid().ToString("N")[..8];
        EnsureTopic(topic);

        using (var producer = new KafkaProducer(Bootstrap, null, null)) {
            producer.Send(topic, payload, key: "k1");
        }

        using var consumer = new KafkaConsumer(Bootstrap, null, null, "ospf-roundtrip-group-" + Guid.NewGuid().ToString("N")[..8]);
        consumer.Subscribe(new[] { topic });

        ConsumeResult<string, string>? received = WaitForMessage(consumer, TimeSpan.FromSeconds(30));
        received.Should().NotBeNull("the message must come back through the broker");
        received!.Message.Value.Should().Be(payload, "the value round-trips exactly");
        received.Message.Key.Should().Be("k1", "the key round-trips too");
        received.Topic.Should().Be(topic, "it arrives on the topic it was sent to");
    }

    /// <summary>JSON 载荷往返 / A JSON payload round-trips.</summary>
    [Fact]
    public void TheJsonPayload_ShouldRoundTrip() {
        string topic = "ospf-json-" + Guid.NewGuid().ToString("N")[..8];
        var sent = new RoundTripPayload("SKU-42", 7, 12.5);
        EnsureTopic(topic);

        using (var producer = new KafkaProducer(Bootstrap, null, null)) {
            producer.SendJson(topic, sent);
        }

        using var consumer = new KafkaConsumer(Bootstrap, null, null, "ospf-json-group-" + Guid.NewGuid().ToString("N")[..8]);
        consumer.Subscribe(new[] { topic });

        ConsumeResult<string, string>? received = WaitForMessage(consumer, TimeSpan.FromSeconds(30));
        received.Should().NotBeNull("the JSON message must come back through the broker");

        RoundTripPayload? parsed = JsonSerializer.Deserialize<RoundTripPayload>(received!.Message.Value);
        parsed.Should().Be(sent, "the payload deserialises back to the same record");
    }

    /// <summary>不可达 broker：超时内返回空，不挂住、不抛 / An unreachable broker yields nothing within the timeout, without hanging or throwing.</summary>
    /// <remarks>
    /// **这条判据钉的是"有界"：** **一个连不上的 broker 必须让 `Consume` 在给定超时后返回 null。**
    /// **如果它挂住，消费侧的任何循环都会静默卡死；如果它抛异常，调用方就得靠捕获来兜底。** **两者都不是这里的行为，也不该是。**
    ///
    /// **This pins boundedness: an unreachable broker must make `Consume` return null after the given timeout.** **Hanging would silently freeze any
    /// consuming loop, and throwing would push the burden onto every caller.** **Neither is the behaviour here, and neither should be.**
    /// </remarks>
    [Fact]
    public void AnUnreachableBroker_ShouldReturnNothingWithinTheTimeout() {
        using var consumer = new KafkaConsumer(
            new[] { "127.0.0.1:1" }, null, null, "ospf-unreachable-group-" + Guid.NewGuid().ToString("N")[..8]);
        consumer.Subscribe(new[] { "ospf-nowhere" });

        var started = DateTime.UtcNow;
        ConsumeResult<string, string>? received = null;
        for (int attempt = 0; attempt < 3 && received is null; ++attempt) {
            received = consumer.Consume(TimeSpan.FromSeconds(2));
        }
        TimeSpan elapsed = DateTime.UtcNow - started;

        received.Should().BeNull("port 1 never answers");
        elapsed.Should().BeLessThan(
            TimeSpan.FromSeconds(30),
            "consuming from a dead broker has to stay bounded; an unbounded wait is a hang in production");
    }

    /// <summary>
    /// 显式建 topic / Create the topic explicitly.
    /// </summary>
    /// <remarks>
    /// **不依赖 broker 的自动建 topic：** **第一版靠它，消费者立刻拿到 `Unknown topic or partition` 而抛异常。**
    /// **自动建 topic 是"生产时顺带建"，消费端订阅在先就不成立；测试要的是确定，不是时序巧合。**
    ///
    /// **Not relying on the broker's auto-creation: the first version did, and the consumer immediately threw `Unknown topic or partition`.**
    /// **Auto-creation happens as a side effect of producing, which does not hold when the consumer subscribes first; a test wants determinism, not a
    /// lucky ordering.**
    /// </remarks>
    private static void EnsureTopic(string topic) {
        using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = string.Join(",", Bootstrap) }).Build();
        admin.CreateTopicsAsync(new[] { new Confluent.Kafka.Admin.TopicSpecification { Name = topic, NumPartitions = 1, ReplicationFactor = 1 } })
            .GetAwaiter()
            .GetResult();
    }

    private static ConsumeResult<string, string>? WaitForMessage(KafkaConsumer consumer, TimeSpan budget) {
        DateTime deadline = DateTime.UtcNow + budget;
        while (DateTime.UtcNow < deadline) {
            try {
                ConsumeResult<string, string>? result = consumer.Consume(TimeSpan.FromSeconds(1));
                if (result is not null) {
                    return result;
                }
            }
            catch (ConsumeException) {
                // **启动窗口里的"未知 topic"是暂时的：** **它既不是"没有消息"，也不该让判据失败，忍到预算用完为止。**
                // **An unknown topic during start-up is temporary: it is neither "no message" nor a reason to fail, so wait the budget out.**
            }
        }
        return null;
    }

    /// <summary>往返用的载荷 / The payload used for the JSON round-trip.</summary>
    /// <param name="Sku">货号 / Stock keeping unit.</param>
    /// <param name="Count">数量 / Count.</param>
    /// <param name="Weight">重量 / Weight.</param>
    public sealed record RoundTripPayload(string Sku, int Count, double Weight);
}
#endif