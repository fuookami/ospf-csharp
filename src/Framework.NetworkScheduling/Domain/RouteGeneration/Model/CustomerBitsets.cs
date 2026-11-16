// ESPPRC 客户位集 / ESPPRC customer bitsets.
// 与固定 Kotlin 、（77 行）对应。
// And.
//
// 两个位集都**不可变**：每次添加客户返回新副本。标签会大量共享与比较，若位集可变，一个标签的扩展就会悄悄改掉另一个标签的
// 状态，支配判定随之失去意义——而且这种错误不会报错，只会让定价返回错误的 reduced cost。
// Both bitsets are **immutable**: adding a customer returns a new copy. Labels share and compare heavily, so a mutable bitset
// would let one label's extension silently mutate another's state and void the dominance test — with no error, only wrong
// reduced costs out of pricing.

using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Domain.RouteGeneration.Model;

/// <summary>
/// 已访问客户位集 / Bitset of visited customers.
/// </summary>
public sealed class VisitedCustomers {
    private readonly long[] _bits;

    /// <summary>
    /// 由位与客户总数构造 / Construct from bits and a customer count.
    /// </summary>
    /// <param name="bits">位集 / The bitset.</param>
    /// <param name="size">客户总数 / Total customer count.</param>
    public VisitedCustomers(IReadOnlyList<long> bits, int size) {
        ArgumentNullException.ThrowIfNull(bits);
        _bits = new long[bits.Count];
        for (int index = 0; index < bits.Count; index++) {
            _bits[index] = bits[index];
        }
        Size = size;
    }

    /// <summary>客户总数 / Total customer count.</summary>
    public int Size { get; }

    /// <summary>
    /// 是否访问了指定客户 / Whether the specified customer has been visited.
    /// </summary>
    /// <param name="customerIndex">客户索引 / Customer index.</param>
    /// <returns>是否已访问 / Whether it was visited.</returns>
    public bool Contains(int customerIndex) {
        int word = customerIndex >>> 6;
        int bit = customerIndex & 63;
        return word < _bits.Length && (_bits[word] & (1L << bit)) != 0L;
    }

    /// <summary>
    /// 添加客户，返回新的副本 / Add a customer, returning a new copy.
    /// </summary>
    /// <remarks>
    /// **与一处刻意差异**：扩容循环是一段无效代码（只赋值 `newBits[0]` 后立即 `break`），因此对索引 ≥ `size`
    /// 的输入会越界抛异常。本实现按那段代码的**本意**实现真正的扩容，越界输入被安全接纳而不是抛异常。合法索引（`&lt; size`）
    /// 下两者行为完全一致。
    /// **One deliberate divergence**: its grow loop is dead code (it assigns `newBits[0]` then breaks), so an index
    /// `&gt;= size` throws there. This implementation implements what that loop intended — real growth — so out-of-range inputs are
    /// accepted rather than throwing. For legal indices (`&lt; size`) behaviour is identical.
    /// </remarks>
    /// <param name="customerIndex">客户索引 / Customer index.</param>
    /// <returns>新的位集 / The new bitset.</returns>
    public VisitedCustomers Add(int customerIndex) {
        if (Contains(customerIndex)) {
            return this;
        }
        int word = customerIndex >>> 6;
        int bit = customerIndex & 63;
        long[] newBits = new long[global::System.Math.Max(_bits.Length, word + 1)];
        Array.Copy(_bits, newBits, _bits.Length);
        newBits[word] |= 1L << bit;
        return new VisitedCustomers(newBits, Size);
    }

    /// <summary>访问的客户数量 / Number of visited customers.</summary>
    /// <returns>数量 / The count.</returns>
    public int Count() {
        int count = 0;
        for (int index = 0; index < Size; index++) {
            if (Contains(index)) {
                count++;
            }
        }
        return count;
    }

    /// <summary>是否为空 / Whether empty.</summary>
    /// <returns>是否为空 / Whether it is empty.</returns>
    public bool IsEmpty() => Count() == 0;

    /// <summary>是否访问了所有客户 / Whether all customers have been visited.</summary>
    /// <returns>是否已满 / Whether it is full.</returns>
    public bool IsFull() => Count() == Size;

    /// <summary>供子集判定读取的位快照 / A bit snapshot for subset checks.</summary>
    /// <param name="wordIndex">字索引 / Word index.</param>
    /// <returns>该字，越界为 0 / The word, or zero when out of range.</returns>
    internal long WordAt(int wordIndex) =>
        wordIndex >= 0 && wordIndex < _bits.Length ? _bits[wordIndex] : 0L;

    /// <summary>位集字数 / The number of words.</summary>
    internal int WordCount => _bits.Length;

    /// <summary>创建空位集 / Create an empty bitset.</summary>
    /// <param name="size">客户总数 / Total customer count.</param>
    /// <returns>空位集 / An empty bitset.</returns>
    public static VisitedCustomers Empty(int size) {
        int wordCount = (size + 63) >>> 6;
        return new VisitedCustomers(new long[wordCount], size);
    }
}

/// <summary>
/// 不可达客户位集 / Bitset of unreachable customers.
/// </summary>
/// <remarks>
/// `ForbiddenCustomers = visited ∪ 已证明不可达客户`（Feillet 2004 标记）。把两者放在同一个位集里，支配判定只需一次子集比较
/// 就能同时覆盖 elementarity 与"可扩展性"两个条件。
/// `ForbiddenCustomers = visited ∪ customers proven unreachable` (Feillet 2004 marking). Keeping both in one bitset lets a
/// single subset comparison cover both elementarity and extendability during dominance.
/// </remarks>
public sealed class ForbiddenCustomers {
    private readonly long[] _bits;

    /// <summary>
    /// 由位与客户总数构造 / Construct from bits and a customer count.
    /// </summary>
    /// <param name="bits">位集 / The bitset.</param>
    /// <param name="size">客户总数 / Total customer count.</param>
    public ForbiddenCustomers(IReadOnlyList<long> bits, int size) {
        ArgumentNullException.ThrowIfNull(bits);
        _bits = new long[bits.Count];
        for (int index = 0; index < bits.Count; index++) {
            _bits[index] = bits[index];
        }
        Size = size;
    }

    /// <summary>客户总数 / Total customer count.</summary>
    public int Size { get; }

    /// <summary>
    /// 是否包含指定客户 / Whether the specified customer is forbidden.
    /// </summary>
    /// <param name="customerIndex">客户索引 / Customer index.</param>
    /// <returns>是否包含 / Whether it is included.</returns>
    public bool Contains(int customerIndex) {
        int word = customerIndex >>> 6;
        int bit = customerIndex & 63;
        return word < _bits.Length && (_bits[word] & (1L << bit)) != 0L;
    }

    /// <summary>
    /// 添加客户，返回新的副本 / Add a customer, returning a new copy.
    /// </summary>
    /// <param name="customerIndex">客户索引 / Customer index.</param>
    /// <returns>新的位集 / The new bitset.</returns>
    public ForbiddenCustomers Add(int customerIndex) {
        if (Contains(customerIndex)) {
            return this;
        }
        int word = customerIndex >>> 6;
        int bit = customerIndex & 63;
        long[] newBits = new long[global::System.Math.Max(_bits.Length, word + 1)];
        Array.Copy(_bits, newBits, _bits.Length);
        newBits[word] |= 1L << bit;
        return new ForbiddenCustomers(newBits, Size);
    }

    /// <summary>
    /// 子集判定：this 是否为 other 的子集 / Subset check: whether this is a subset of other.
    /// </summary>
    /// <param name="other">另一集合 / The other set.</param>
    /// <returns>是否为子集 / Whether it is a subset.</returns>
    public bool IsSubsetOf(ForbiddenCustomers other) {
        ArgumentNullException.ThrowIfNull(other);
        for (int index = 0; index < _bits.Length; index++) {
            if ((_bits[index] & ~other.WordAt(index)) != 0L) {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// 合并：返回包含两者所有客户的新位集 / Union: return a new bitset containing all customers from both.
    /// </summary>
    /// <param name="other">另一集合 / The other set.</param>
    /// <returns>并集 / The union.</returns>
    public ForbiddenCustomers Union(ForbiddenCustomers other) {
        ArgumentNullException.ThrowIfNull(other);
        int length = global::System.Math.Max(_bits.Length, other._bits.Length);
        long[] newBits = new long[length];
        Array.Copy(_bits, newBits, _bits.Length);
        for (int index = 0; index < other._bits.Length; index++) {
            newBits[index] |= other._bits[index];
        }
        return new ForbiddenCustomers(newBits, Size);
    }

    /// <summary>供子集判定读取的位快照 / A bit snapshot for subset checks.</summary>
    /// <param name="wordIndex">字索引 / Word index.</param>
    /// <returns>该字，越界为 0 / The word, or zero when out of range.</returns>
    private long WordAt(int wordIndex) =>
        wordIndex >= 0 && wordIndex < _bits.Length ? _bits[wordIndex] : 0L;

    /// <summary>从已访问集合创建 / Create from a visited set.</summary>
    /// <param name="visited">已访问集合 / The visited set.</param>
    /// <param name="size">客户总数 / Total customer count.</param>
    /// <returns>不可达集合 / The forbidden set.</returns>
    public static ForbiddenCustomers FromVisited(VisitedCustomers visited, int size) {
        ArgumentNullException.ThrowIfNull(visited);
        int wordCount = (size + 63) >>> 6;
        long[] bits = new long[wordCount];
        for (int index = 0; index < size; index++) {
            if (visited.Contains(index)) {
                int word = index >>> 6;
                int bit = index & 63;
                if (word < bits.Length) {
                    bits[word] |= 1L << bit;
                }
            }
        }
        return new ForbiddenCustomers(bits, size);
    }

    /// <summary>创建空位集 / Create an empty bitset.</summary>
    /// <param name="size">客户总数 / Total customer count.</param>
    /// <returns>空位集 / An empty bitset.</returns>
    public static ForbiddenCustomers Empty(int size) {
        int wordCount = (size + 63) >>> 6;
        return new ForbiddenCustomers(new long[wordCount], size);
    }
}
