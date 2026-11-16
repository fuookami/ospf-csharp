// 经验证的团覆盖 / Verified clique cover.
// 与固定 Kotlin 对应。
// The pinned Kotlin: .

using System.Collections.Generic;
using PositionConflict = Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading.Model.PositionConflict;

namespace Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading.Model;

/// <summary>
/// 经验证的团覆盖，未覆盖边仍保留；绝不把连通分量当作团 / Verified clique cover, never a connected-component closure.
/// </summary>
/// <remarks>
/// **两个不可让步的性质**：
///
/// | 性质 | 若被破坏 | 后果 |
/// | --- | --- | --- |
/// | **每个团内部逐对都冲突** | 把**连通分量**当成团（`a—b—c` 但 `a` 与 `c` 不冲突） | 模型禁止 `a`、`c` 同时占用，**静默丢掉可行解** |
/// | **每条原始边都被某个团覆盖** | 只建部分团 | 某对冲突舱位失去约束，**物理冲突被放过** |
///
/// 前者是"约束太强"（丢可行解），后者是"约束太弱"（放过冲突）。**两个方向都要测**。
///
/// **Two non-negotiable properties** (table above): **every pair inside a clique must conflict** — treating a **connected component** as a clique (`a—b—c` with `a` and `c`
/// not conflicting) would forbid `a` and `c` coexisting and **silently discard feasible solutions**; and **every original edge must land in some clique** — a partial
/// cover would leave a conflicting pair unconstrained and **let a physical conflict through**. One over-constrains, the other under-constrains; **both directions need
/// criteria**.
/// </remarks>
public sealed class ConflictIndex {
    /// <summary>每个集合中的所有舱位逐对冲突 / Pairwise-conflicting position sets.</summary>
    public IReadOnlyList<IReadOnlySet<string>> Cliques { get; }

    private ConflictIndex(IReadOnlyList<IReadOnlySet<string>> cliques) {
        Cliques = cliques;
    }

    /// <summary>
    /// 构建确定性的贪心团覆盖 / Build a deterministic greedy clique cover.
    /// </summary>
    /// <param name="edges">已验证互斥边 / Verified edges.</param>
    /// <returns>完整边覆盖 / Complete edge cover.</returns>
    /// <remarks>
    /// **贪心，不是精确**：从剩余边里取一条作种子，再取两个端点邻域的**交集**作候选，
    /// 逐个验证"与团内每个成员都相邻"才加入。**精确的最大团覆盖是 NP 难的**，
    /// 这里只求"团数尽量少、但一定合法且全覆盖"。
    ///
    /// **`ponytail:` 贪心团覆盖（`O(边数 × 候选数 × 团大小)`），团数不是最小的；**
    /// 若约束规模成为瓶颈，换成 Bron–Kerbosch 枚举极大团再做集合覆盖。
    ///
    /// **Greedy, not exact**: seed from a remaining edge, take the **intersection** of the two endpoints' neighbourhoods as candidates, and add a candidate only when it is
    /// adjacent to every clique member. **Exact maximum clique cover is NP-hard**, so we only ask for "as few cliques as reasonable, but always legal and always a
    /// complete edge cover". `ponytail:` greedy cover, not a minimum one — switch to Bron–Kerbosch plus a set-cover step if constraint count ever becomes the bottleneck.
    /// </remarks>
    public static ConflictIndex Build(IReadOnlySet<PositionConflict> edges) {
        var adjacency = new Dictionary<string, SortedSet<string>>();
        var edgeOrder = new List<string[]>();
        var seenPairs = new HashSet<string>();

        foreach (PositionConflict edge in edges) {
            if (!adjacency.TryGetValue(edge.First, out SortedSet<string>? firstNeighbours)) {
                firstNeighbours = new SortedSet<string>(System.StringComparer.Ordinal);
                adjacency[edge.First] = firstNeighbours;
            }
            firstNeighbours.Add(edge.Second);
            if (!adjacency.TryGetValue(edge.Second, out SortedSet<string>? secondNeighbours)) {
                secondNeighbours = new SortedSet<string>(System.StringComparer.Ordinal);
                adjacency[edge.Second] = secondNeighbours;
            }
            // **必须双向登记。** 变异"只登记一个方向"在这里的判据下**存活**——
            // 因为下面那句守卫已经保证了正确性，单向登记只是让候选集变小（有的团因此拆得更碎，
            // 但**每个团仍合法、每条边仍被覆盖**）。**这是真实的等价变异，不是判据不足。**
            // 仍双向登记，因为那才是无向图的正确表示。
            //
            // **Both directions must be registered.** The "register only one direction" mutant **survived** the criteria — the guard below already guarantees
            // correctness, and one-way registration merely shrinks the candidate set (some cliques end up split more finely, but **every clique stays legal and every edge
            // stays covered**). **That is a genuine equivalent mutant, not a weak criterion.** Both directions are still registered because that is the correct
            // representation of an undirected graph.
            secondNeighbours.Add(edge.First);

            // **无向化**：`(a,b)` 与 `(b,a)` 是同一条边，只保留一条。
            // **Undirected**: `(a,b)` and `(b,a)` are the same edge and only one is kept.
            string key = string.CompareOrdinal(edge.First, edge.Second) <= 0
                ? $"{edge.First}\u0000{edge.Second}"
                : $"{edge.Second}\u0000{edge.First}";
            // **去重是必要的，但这里的变异"不去重"存活**：因为下面存的本来就是**排序后的**
            // 二元组，`(A,B)` 与 `(B,A)` 去重前后**都是同一对** `["A","B"]`，
            // 第二轮把它当已覆盖的边直接删掉。**真实的等价变异。**
            //
            // **Deduplication is needed, but the "no dedup" mutant survived**: the entry stored below is already the **sorted** pair, so `(A,B)` and `(B,A)` collapse to the
            // same `["A","B"]` and the second round simply drops it as already covered. **A genuine equivalent mutant.**
            if (seenPairs.Add(key)) {
                edgeOrder.Add(new[] {
                    string.CompareOrdinal(edge.First, edge.Second) <= 0 ? edge.First : edge.Second,
                    string.CompareOrdinal(edge.First, edge.Second) <= 0 ? edge.Second : edge.First,
                });
            }
        }

        var remaining = new List<string[]>(edgeOrder);
        var cliques = new List<IReadOnlySet<string>>();
        while (remaining.Count > 0) {
            string[] seed = remaining[0];
            var clique = new HashSet<string>(System.StringComparer.Ordinal) { seed[0], seed[1] };
            var candidates = new SortedSet<string>(System.StringComparer.Ordinal);
            // **交集只是优化，不是正确性要求。** 变异"只用一个端点的邻域"在这里判据下**存活**：
            // 无论候选怎么进来，下面那句守卫都会把"不与团内全部成员冲突"的候选拒掉。
            // 求交集只是让候选集更小。**真实的等价变异。**
            //
            // **The intersection is an optimisation, not a correctness requirement.** The "use only one endpoint's neighbourhood" mutant **survived**: however a candidate
            // arrives, the guard below rejects it unless it conflicts with every clique member. Intersecting merely shrinks the candidate set. **A genuine equivalent mutant.**
            candidates.UnionWith(adjacency[seed[0]]);
            candidates.IntersectWith(adjacency[seed[1]]);
            foreach (string candidate in candidates) {
                // **这一句是"团"与"连通分量"的唯一区别**：候选必须与团内**每一个**成员都冲突。
                // **This one line is the only difference between a clique and a connected component**: the candidate must conflict with **every** clique member.
                bool adjacentToAll = true;
                foreach (string member in clique) {
                    if (!adjacency[member].Contains(candidate)) {
                        adjacentToAll = false;
                        break;
                    }
                }
                if (adjacentToAll) {
                    clique.Add(candidate);
                }
            }
            cliques.Add(clique);
            // **已覆盖的边移除**，否则循环不终止。
            // **Remove covered edges**, otherwise the loop never terminates.
            remaining.RemoveAll(pair => clique.Contains(pair[0]) && clique.Contains(pair[1]));
        }

        return new ConflictIndex(cliques);
    }
}
