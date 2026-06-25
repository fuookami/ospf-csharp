#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Framework.Plugin.Message.Kafka;
using System;
using System.Reflection;
using Xunit;

namespace Fuookami.Ospf.Framework.Plugin.Tests.Message;
/// <summary>
/// Kafka 接口验证测试 / Kafka interface verification tests.
///
/// 验证 Kafka 生产者和消费者类型正确编译。
/// Verifies that Kafka producer and consumer types compile correctly.
/// </summary>
public class KafkaInterfaceTests {
    [Fact]
    public void KafkaProducer_Should_Implement_IDisposable() => typeof(IDisposable).IsAssignableFrom(typeof(KafkaProducer)).Should().BeTrue();

    [Fact]
    public void KafkaConsumer_Should_Implement_IDisposable() => typeof(IDisposable).IsAssignableFrom(typeof(KafkaConsumer)).Should().BeTrue();

    [Fact]
    public void KafkaProducer_Should_Have_Send_Method() {
        MethodInfo? method = typeof(KafkaProducer).GetMethod("Send");
        method.Should().NotBeNull();
    }

    [Fact]
    public void KafkaProducer_Should_Have_SendJson_Method() {
        MethodInfo? method = typeof(KafkaProducer).GetMethod("SendJson");
        method.Should().NotBeNull();
    }

    [Fact]
    public void KafkaProducer_Should_Have_SendProtobuf_Method() {
        MethodInfo? method = typeof(KafkaProducer).GetMethod("SendProtobuf");
        method.Should().NotBeNull();
    }

    [Fact]
    public void KafkaConsumer_Should_Have_Subscribe_Methods() {
        MethodInfo[] methods = typeof(KafkaConsumer).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        System.Array.Exists(methods, m => m.Name == "Subscribe").Should().BeTrue();
    }

    [Fact]
    public void KafkaConsumer_Should_Have_Consume_Method() {
        MethodInfo? method = typeof(KafkaConsumer).GetMethod("Consume");
        method.Should().NotBeNull();
    }

    [Fact]
    public void KafkaConsumer_Should_Have_ConsumeAsync_Method() {
        MethodInfo? method = typeof(KafkaConsumer).GetMethod("ConsumeAsync");
        method.Should().NotBeNull();
    }
}
