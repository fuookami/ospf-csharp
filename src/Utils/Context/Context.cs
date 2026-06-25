#nullable enable

using System;
using System.Threading;

namespace Fuookami.Ospf.Utils.Context
{
    /// <summary>
    /// 上下文变量 / Context variable (mirrors ospf-kotlin ContextVar&lt;T&gt;).
    /// Uses AsyncLocal for async-flow context.
    /// </summary>
    public sealed class ContextVar<T>
    {
        private readonly T _defaultValue;
        private readonly AsyncLocal<Holder?> _storage = new();

        /// <summary>构造函数 / Constructor.</summary>
        public ContextVar(T defaultValue)
        {
            _defaultValue = defaultValue;
        }

        /// <summary>设置值 / Set value.</summary>
        public ContextScope<T> Set(T value)
        {
            var previous = _storage.Value;
            _storage.Value = new Holder(value);
            return new ContextScope<T>(this, previous);
        }

        /// <summary>设置值（延迟构建）/ Set value (lazy builder).</summary>
        public ContextScope<T> Set(Func<T> builder) => Set(builder());

        /// <summary>获取值 / Get value.</summary>
        public T Get()
        {
            var holder = _storage.Value;
            return holder is not null ? holder.Value : _defaultValue;
        }

        /// <summary>恢复之前的值 / Restore previous value.</summary>
        internal void Restore(Holder? previous)
        {
            _storage.Value = previous;
        }

        internal sealed class Holder
        {
            public T Value { get; }
            public Holder(T value) => Value = value;
        }
    }

    /// <summary>
    /// 上下文作用域 / Context scope (mirrors ospf-kotlin Context&lt;T&gt; : AutoCloseable).
    /// Implements IDisposable for `using` pattern.
    /// </summary>
    public sealed class ContextScope<T> : IDisposable
    {
        private readonly ContextVar<T> _contextVar;
        private readonly ContextVar<T>.Holder? _previous;
        private bool _disposed;

        internal ContextScope(ContextVar<T> contextVar, ContextVar<T>.Holder? previous)
        {
            _contextVar = contextVar;
            _previous = previous;
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (!_disposed)
            {
                _contextVar.Restore(_previous);
                _disposed = true;
            }
        }
    }
}
