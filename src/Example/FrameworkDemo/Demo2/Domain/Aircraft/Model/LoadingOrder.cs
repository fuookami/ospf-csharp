#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;

/// <summary>
/// 装载顺序。Loading order with precedence and succession constraints.
/// </summary>
public sealed class LoadingOrder
{
    /// <summary>甲板位置 / Deck location</summary>
    public DeckLocation Location { get; }
    /// <summary>顺序号 / Order number</summary>
    public byte Order { get; }
    /// <summary>直接前驱集 / Direct predecessor set</summary>
    public IReadOnlySet<Position> DirectPrec { get; }
    /// <summary>直接后继集 / Direct successor set</summary>
    public IReadOnlySet<Position> DirectSucc { get; }

    public LoadingOrder(
        DeckLocation location,
        byte order,
        IReadOnlySet<Position> directPrec,
        IReadOnlySet<Position> directSucc)
    {
        Location = location;
        Order = order;
        DirectPrec = directPrec;
        DirectSucc = directSucc;
    }

    /// <summary>
    /// 前驱深度 / The predecessor depth.
    /// </summary>
    /// <remarks>
    /// **照做，包括它的怪癖**：基线写的是 `directPrec.minOfOrNull { it.precDepth } ?: UInt8.zero`——
    /// 取直接前驱的 `precDepth` 的**最小值**，而叶子节点的 `precDepth` 也是 0（空集回退到 0）。于是**整条链上每个节点的
    /// `precDepth` 都是 0**，它并不表示"离起点多远"。
    ///
    /// 这不是笔误：`RecommendedWeightEqualization` 的 `PriorityAppointment` 用 `precDepth` 做"预约深度集合"，
    /// 而按语义那个集合恒为 `{0}`。改成真正的深度会让预约判定与基线不同——那是一个**行为改动**，不是修复，
    /// 所以留在这里沿用，并记下这个 ceiling。
    ///
    /// **Copied from the code, quirk included**: the code writes `directPrec.minOfOrNull { it.precDepth } ?: UInt8.zero` — the **minimum** `precDepth` among the direct
    /// predecessors, while a leaf's own `precDepth` is also 0 (an empty set falls back to 0). So **every node in the chain has `precDepth` 0**; it does not mean "distance from the
    /// start". This is not a typo: `PriorityAppointment` in recommended-weight equalisation uses `precDepth` to build its "appointed depth sets", and under its semantics
    /// that set is always `{0}`. Making it a real depth would change the appointment decision — a **behaviour change**, not a fix — so it is copied as-is with this ceiling recorded.
    /// </remarks>
    public byte PrecDepth =>
        DirectPrec.Count == 0 ? (byte)0 : DirectPrec.Min(position => position.LoadingOrder?.PrecDepth ?? 0);

    /// <summary>
    /// 后继深度 / The successor depth.
    /// </summary>
    /// <remarks>
    /// 与 <see cref="PrecDepth"/> 同构，同样恒为 0。
    ///
    /// Isomorphic to <see cref="PrecDepth"/> and likewise always 0.
    /// </remarks>
    public byte SuccDepth =>
        DirectSucc.Count == 0 ? (byte)0 : DirectSucc.Min(position => position.LoadingOrder?.SuccDepth ?? 0);

    /// <summary>
    /// 全部前驱（传递闭包）/ All predecessors (transitive closure).
    /// </summary>
    /// <remarks>
    /// `directPrec + directPrec.flatMap { it.prec }`——**递归且无环保护**。本仓的 `DirectPrec` 由装载顺序串成一条链，
    /// 天然无环；但若有人手工构造出环，这里会无限递归。加一个 `visited` 集合并保持结果语义不变，成本极低。
    ///
    /// `directPrec + directPrec.flatMap { it.prec }` — **recursive with no cycle guard**. This implementation's `DirectPrec` forms a chain and is acyclic by construction,
    /// but a hand-built cycle would recurse forever. A `visited` set keeps the semantics identical at negligible cost.
    /// </remarks>
    public IReadOnlySet<Position> Prec => Closure(DirectPrec, position => position.LoadingOrder?.DirectPrec);

    /// <summary>
    /// 全部后继（传递闭包）/ All successors (transitive closure).
    /// </summary>
    /// <remarks>
    /// 与 <see cref="Prec"/> 同构。
    ///
    /// Isomorphic to <see cref="Prec"/>.
    /// </remarks>
    public IReadOnlySet<Position> Succ => Closure(DirectSucc, position => position.LoadingOrder?.DirectSucc);

    /// <summary>
    /// 求传递闭包 / Compute a transitive closure.
    /// </summary>
    /// <param name="seed">起始集 / The seed set.</param>
    /// <param name="next">取下一步 / Fetch the next step.</param>
    /// <returns>闭包 / The closure.</returns>
    private static IReadOnlySet<Position> Closure(
        IReadOnlySet<Position> seed,
        Func<Position, IReadOnlySet<Position>?> next)
    {
        var result = new HashSet<Position>(seed);
        var pending = new Stack<Position>(seed);
        while (pending.Count > 0)
        {
            Position current = pending.Pop();
            if (next(current) is not { } step)
            {
                continue;
            }
            foreach (Position candidate in step)
            {
                if (result.Add(candidate))
                {
                    pending.Push(candidate);
                }
            }
        }
        return result;
    }
}
