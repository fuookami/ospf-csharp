#nullable enable

using System;
using System.Collections.Generic;
using System.Text.Json;
using Confluent.Kafka;

namespace Fuookami.Ospf.Framework.Plugin.Message.Kafka
{
    /// <summary>
    /// Kafka 生产者封装 / Kafka producer wrapper backed by Confluent.Kafka.
    /// </summary>
    public sealed class KafkaProducer : IDisposable
    {
        private readonly IProducer<string, string> _producer;

        internal KafkaProducer(IEnumerable<string> bootstrapServers, string? userName, string? password)
        {
            var config = BuildConfig(bootstrapServers, userName, password);
            _producer = new ProducerBuilder<string, string>(config).Build();
        }

        /// <summary>
        /// 发送字符串消息 / Send a string message to a topic.
        /// </summary>
        /// <param name="topic">主题名称 / Topic name</param>
        /// <param name="message">消息内容 / Message content</param>
        /// <param name="key">消息键（可选）/ Message key (optional)</param>
        public void Send(string topic, string message, string? key = null)
        {
            _producer.Produce(topic, new Message<string, string> { Key = key ?? "", Value = message });
        }

        /// <summary>
        /// 发送 JSON 序列化对象消息 / Send a JSON-serialized object message.
        /// </summary>
        /// <typeparam name="T">消息类型 / Message type</typeparam>
        /// <param name="topic">主题名称 / Topic name</param>
        /// <param name="value">消息对象 / Message object</param>
        /// <param name="key">消息键（可选）/ Message key (optional)</param>
        public void SendJson<T>(string topic, T value, string? key = null)
        {
            _producer.Produce(topic, new Message<string, string>
            {
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
        public void SendProtobuf(string topic, byte[] payload, string? key = null)
        {
            _producer.Produce(topic, new Message<string, string>
            {
                Key = key ?? "",
                Value = Convert.ToHexString(payload),
            });
        }

        /// <summary>释放资源 / Dispose.</summary>
        public void Dispose() => _producer.Dispose();

        private static ProducerConfig BuildConfig(
            IEnumerable<string> bootstrapServers, string? userName, string? password)
        {
            var cfg = new ProducerConfig
            {
                BootstrapServers = string.Join(",", bootstrapServers),
            };
            if (userName is not null && password is not null)
            {
                cfg.SecurityProtocol = SecurityProtocol.SaslSsl;
                cfg.SaslMechanism = SaslMechanism.ScramSha256;
                cfg.SaslUsername = userName;
                cfg.SaslPassword = password;
            }
            return cfg;
        }
    }
}
