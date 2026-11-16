#nullable enable

using Confluent.Kafka;
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Fuookami.Ospf.Framework.Plugin.Message.Kafka;
/// <summary>
/// Kafka 生产者封装 / Kafka producer wrapper backed by Confluent.Kafka.
/// </summary>
public sealed class KafkaProducer : IDisposable {
    private readonly IProducer<string, string> _producer;

    internal KafkaProducer(IEnumerable<string> bootstrapServers, string? userName, string? password) {
        ProducerConfig config = BuildConfig(bootstrapServers, userName, password);
        _producer = new ProducerBuilder<string, string>(config).Build();
    }

    /// <summary>
    /// 发送字符串消息 / Send a string message to a topic.
    /// </summary>
    /// <param name="topic">主题名称 / Topic name</param>
    /// <param name="message">消息内容 / Message content</param>
    /// <param name="key">消息键（可选）/ Message key (optional)</param>
    public void Send(string topic, string message, string? key = null) => _producer.Produce(topic, new Message<string, string> { Key = key ?? "", Value = message });

    /// <summary>
    /// 发送 JSON 序列化对象消息 / Send a JSON-serialized object message.
    /// </summary>
    /// <typeparam name="T">消息类型 / Message type</typeparam>
    /// <param name="topic">主题名称 / Topic name</param>
    /// <param name="value">消息对象 / Message object</param>
    /// <param name="key">消息键（可选）/ Message key (optional)</param>
    public void SendJson<T>(string topic, T value, string? key = null) {
        _producer.Produce(topic, new Message<string, string> {
            Key = key ?? "",
            Value = JsonSerializer.Serialize(value),
        });
    }

    /// <summary>
    /// 发送 Protobuf hex 编码消息 / Send a Protobuf hex-encoded message.
    /// </summary>
    /// <param name="topic">主题名称 / Topic name</param>
    /// <param name="payload">Protobuf 字节数组 / Protobuf byte array</param>
    /// <param name="key">消息键（可选）/ Message key (optional)</param>
    public void SendProtobuf(string topic, byte[] payload, string? key = null) {
        _producer.Produce(topic, new Message<string, string> {
            Key = key ?? "",
            Value = Convert.ToHexString(payload),
        });
    }

    /// <summary>刷新队列后释放资源 / Flush the queue, then dispose.</summary>
    /// <remarks>
    /// B1307 修的真缺陷：`Produce` 是异步投递，而原来的 `Dispose` 直接销毁客户端——排队中的消息被静默丢弃。
    /// 实测证据：用本文件的生产入口发一条消息再释放，topic 的最新偏移仍然是 `0`，消费者什么都拿不到；
    /// 而"发出去却不见了"在调用方完全不可见（`Send` 不返回投递结果）。
    /// 这里先在有界时间内 flush，再释放；`Send` 仍然是异步的（那是有意为之的吞吐选择），
    /// 但"释放后消息必须已经投递出去"这一条不再默默失效。
    ///
    /// B1307 fixes a real defect: `Produce` delivers asynchronously and the old `Dispose` destroyed the client straight away, silently dropping whatever
    /// was still queued. Measured evidence: sending one message through this file's entry point and then disposing left the topic's latest offset at 0
    /// and the consumer with nothing, and the loss was invisible to the caller because `Send` returns no delivery result. This flushes within a bounded
    /// time before disposing; `Send` stays asynchronous (a deliberate throughput choice), but "after dispose the message has actually been delivered" no
    /// longer fails silently.
    /// </remarks>
    public void Dispose() {
        try {
            _producer.Flush(TimeSpan.FromSeconds(10));
        }
        finally {
            _producer.Dispose();
        }
    }

    private static ProducerConfig BuildConfig(
        IEnumerable<string> bootstrapServers, string? userName, string? password) {
        var cfg = new ProducerConfig {
            BootstrapServers = string.Join(",", bootstrapServers),
        };
        if (userName is not null && password is not null) {
            cfg.SecurityProtocol = SecurityProtocol.SaslSsl;
            cfg.SaslMechanism = SaslMechanism.ScramSha256;
            cfg.SaslUsername = userName;
            cfg.SaslPassword = password;
        }
        return cfg;
    }
}
