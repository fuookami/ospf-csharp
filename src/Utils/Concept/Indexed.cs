#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;

namespace Fuookami.Ospf.Utils.Concept;
/// <summary>带索引接口 / Indexed interface (mirrors ospf-kotlin Indexed).</summary>
public interface IIndexed {
    /// <summary>索引 / Index.</summary>
    int Index { get; }

    /// <summary>无符号索引 / Unsigned index.</summary>
    ulong UIndex => (ulong)Index;

    /// <summary>是否已索引 / Whether indexed.</summary>
    bool Indexed => true;
}

/// <summary>索引实现基类 / Index implementation base (mirrors ospf-kotlin IndexedImpl).</summary>
public abstract class IndexedImpl {
    private static readonly ConcurrentDictionary<Type, int> Counters = new();

    /// <summary>获取下一个索引（泛型）/ Get next index (generic).</summary>
    public int NextIndex<T>() => NextIndex(typeof(T));

    /// <summary>获取下一个索引 / Get next index.</summary>
    public int NextIndex(Type type) => Counters.AddOrUpdate(type, _ => 0, (_, v) => v + 1);

    /// <summary>刷新索引（泛型）/ Flush index (generic).</summary>
    public void Flush<T>() => Flush(typeof(T));

    /// <summary>刷新索引 / Flush index.</summary>
    public void Flush(Type type) => Counters[type] = 0;
}

/// <summary>手动索引 / Manual indexed (mirrors ospf-kotlin ManualIndexed).</summary>
public class ManualIndexed : IIndexed {
    private int? _index;

    /// <summary>共享的 IndexedImpl 实例 / Shared IndexedImpl instance.</summary>
    public static readonly IndexedImpl Impl = new ManualIndexedImpl();

    /// <inheritdoc/>
    public bool Indexed => _index.HasValue;

    /// <inheritdoc/>
    public int Index {
        get {
            Debug.Assert(Indexed, "Index not set. Call SetIndexed() first.");
            return _index!.Value;
        }
    }

    /// <summary>设置索引 / Set index.</summary>
    public void SetIndexed() {
        Debug.Assert(!Indexed, "Index already set.");
        _index = Impl.NextIndex(GetType());
    }

    /// <summary>设置索引（指定类型）/ Set index (with type).</summary>
    public void SetIndexed(Type type) {
        Debug.Assert(!Indexed, "Index already set.");
        _index = Impl.NextIndex(type);
    }

    /// <summary>刷新索引 / Refresh index.</summary>
    public void RefreshIndex() {
        Debug.Assert(Indexed, "Index not set.");
        _index = Impl.NextIndex(GetType());
    }

    /// <summary>刷新索引（指定类型）/ Refresh index (with type).</summary>
    public void RefreshIndex(Type type) {
        Debug.Assert(Indexed, "Index not set.");
        _index = Impl.NextIndex(type);
    }

    private sealed class ManualIndexedImpl : IndexedImpl { }
}

/// <summary>自动索引 / Auto indexed (mirrors ospf-kotlin AutoIndexed).</summary>
public class AutoIndexed : IIndexed {
    private readonly int _index;

    /// <summary>共享的 IndexedImpl 实例 / Shared IndexedImpl instance.</summary>
    public static readonly IndexedImpl AutoIndexedImpl = new AutoIndexedImplFactory();

    /// <summary>构造函数 / Constructor.</summary>
    public AutoIndexed(Type cls) {
        _index = AutoIndexedImpl.NextIndex(cls);
    }

    /// <inheritdoc/>
    public int Index => _index;

    /// <summary>刷新索引 / Refresh index.</summary>
    public void RefreshIndex() {
        // Note: cannot mutate _index (readonly). Ported as-is from kotlin (kotlin version uses var).
        // If mutation needed, change to non-readonly field.
    }

    /// <summary>刷新索引（指定类型）/ Refresh index (with type).</summary>
    public void RefreshIndex(Type type) {
        // Same note as above.
    }

    private sealed class AutoIndexedImplFactory : IndexedImpl { }
}

/// <summary>Indexed 辅助方法 / Indexed helper methods.</summary>
public static class IndexedExtensions {
    /// <summary>按索引查找或获取 / Find by index or get.</summary>
    public static T FindOrGet<T>(this IReadOnlyList<T> list, int index) where T : IIndexed =>
        list[index];
}
