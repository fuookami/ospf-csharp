#nullable enable

using System;
using System.Threading.Tasks;

namespace Fuookami.Ospf.Utils.MetaProgramming;
/// <summary>
/// 延迟委托 / Lazy delegate (mirrors ospf-kotlin LazyDelegate&lt;THost, U&gt;).
/// </summary>
public sealed class LazyDelegate<THost, U>
    where U : class {
    private readonly Func<U> _lazyFunc;
    private U? _value;
    private bool _initialized;

    /// <summary>构造函数 / Constructor.</summary>
    public LazyDelegate(Func<U> lazyFunc) {
        _lazyFunc = lazyFunc;
    }

    /// <summary>值 / Value.</summary>
    public U Value {
        get {
            if (!_initialized) {
                _value = _lazyFunc();
                _initialized = true;
            }
            return _value!;
        }
        set {
            _value = value;
            _initialized = true;
        }
    }
}

/// <summary>
/// 自引用延迟委托 / Self-referencing lazy delegate (mirrors ospf-kotlin SelfLazyDelegate&lt;THost, U&gt;).
/// </summary>
public sealed class SelfLazyDelegate<THost, U>
    where U : class {
    private readonly Func<THost, U> _lazyFunc;
    private U? _value;
    private bool _initialized;

    /// <summary>构造函数 / Constructor.</summary>
    public SelfLazyDelegate(Func<THost, U> lazyFunc) {
        _lazyFunc = lazyFunc;
    }

    /// <summary>获取值 / Get value.</summary>
    public U GetValue(THost host) {
        if (!_initialized) {
            _value = _lazyFunc(host);
            _initialized = true;
        }
        return _value!;
    }
}

/// <summary>
/// 异步延迟 / Async lazy (mirrors ospf-kotlin SuspendLazy&lt;T&gt; → AsyncLazy&lt;T&gt;).
/// </summary>
public sealed class AsyncLazy<T> {
    private readonly Lazy<Task<T>> _lazy;

    /// <summary>构造函数 / Constructor.</summary>
    public AsyncLazy(Func<Task<T>> lazyFunc) {
        _lazy = new Lazy<Task<T>>(() => Task.Run(lazyFunc));
    }

    /// <summary>值 / Value (async).</summary>
    public Task<T> Value => _lazy.Value;
}
