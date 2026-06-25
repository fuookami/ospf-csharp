#nullable enable

using System;
using System.Collections.Generic;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Fuookami.Ospf.Utils.Parallel;
/// <summary>
/// 通道守卫 / Channel guard (mirrors ospf-kotlin ChannelGuard&lt;T&gt;).
/// Wraps a Channel and provides IDisposable lifecycle.
/// </summary>
public sealed class ChannelGuard<T> : IDisposable {
    /// <summary>通道 / Channel.</summary>
    public Channel<T> Channel { get; }

    /// <summary>构造函数 / Constructor (unbounded).</summary>
    public ChannelGuard(Channel<T>? channel = null) {
        Channel = channel ?? System.Threading.Channels.Channel.CreateUnbounded<T>();
    }

    /// <summary>构造函数（有界通道）/ Constructor (bounded channel).</summary>
    public ChannelGuard(int capacity) {
        Channel = System.Threading.Channels.Channel.CreateBounded<T>(capacity);
    }

    /// <inheritdoc/>
    public void Dispose() => Channel.Writer.TryComplete();

    /// <summary>异步枚举 / Async enumerable.</summary>
    public IAsyncEnumerable<T> GetEnumerator() => Channel.Reader.ReadAllAsync();

    /// <summary>异步接收 / Async receive.</summary>
    public ValueTask<T> ReceiveAsync() => Channel.Reader.ReadAsync();

    /// <summary>尝试接收 / Try receive.</summary>
    public bool TryReceive(out T item) => Channel.Reader.TryRead(out item!);

    /// <summary>写入 / Write.</summary>
    public ValueTask WriteAsync(T item) => Channel.Writer.WriteAsync(item);

    /// <summary>尝试写入 / Try write.</summary>
    public bool TryWrite(T item) => Channel.Writer.TryWrite(item);
}
