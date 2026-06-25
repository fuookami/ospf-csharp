#nullable enable

using Confluent.Kafka;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Fuookami.Ospf.Framework.Plugin.Message.Kafka;
/// <summary>
/// Kafka 消费者封装 / Kafka consumer wrapper backed by Confluent.Kafka.
/// </summary>
public sealed class KafkaConsumer : IDisposable {
    private readonly IConsumer<string, string> _consumer;

    internal KafkaConsumer(
        IEnumerable<string> bootstrapServers,
        string? userName,
        string? password,
        string? groupId = null) {
        ConsumerConfig config = BuildConfig(bootstrapServers, userName, password, groupId);
        _consumer = new ConsumerBuilder<string, string>(config).Build();
    }

    /// <summary>
    /// 订阅主题列表 / Subscribe to a list of topics.
    /// </summary>
    public void Subscribe(IEnumerable<string> topics) => _consumer.Subscribe(topics);

    /// <summary>
    /// 按正则模式订阅主题 / Subscribe to topics matching a regex pattern.
    /// </summary>
    public void Subscribe(string pattern) => _consumer.Subscribe(pattern);

    /// <summary>
    /// 拉取消息（阻塞）/ Poll for a message; returns null on timeout.
    /// </summary>
    /// <param name="timeout">超时时间 / Timeout duration</param>
    /// <returns>消费结果或 null / Consume result or null</returns>
    public ConsumeResult<string, string>? Consume(TimeSpan timeout) => _consumer.Consume(timeout);

    /// <summary>
    /// 循环消费直到取消 / Consume in a loop until the cancellation token is cancelled.
    /// </summary>
    /// <param name="process">消息处理函数 / Message processor</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    public async Task ConsumeAsync(
        Action<string, ConsumeResult<string, string>> process,
        CancellationToken cancellationToken) {
        await Task.Run(() => {
            while (!cancellationToken.IsCancellationRequested) {
                try {
                    ConsumeResult<string, string>? result = _consumer.Consume(cancellationToken);
                    if (result is null || result.IsPartitionEOF) { continue; }
                    process(result.Message.Value, result);
                }
                catch (OperationCanceledException) {
                    break;
                }
            }
        }, cancellationToken);
    }

    /// <summary>释放资源 / Dispose.</summary>
    public void Dispose() => _consumer.Dispose();

    private static ConsumerConfig BuildConfig(
        IEnumerable<string> bootstrapServers,
        string? userName,
        string? password,
        string? groupId) {
        var cfg = new ConsumerConfig {
            BootstrapServers = string.Join(",", bootstrapServers),
            GroupId = groupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
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
