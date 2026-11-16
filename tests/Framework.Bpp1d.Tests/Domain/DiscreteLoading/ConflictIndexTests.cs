// 团覆盖判据 / Criteria for the verified clique cover.

using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading.Model;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp1d.Tests.Domain.DiscreteLoading;

/// <summary>
/// **经验证的团覆盖** / The verified clique cover.
/// </summary>
/// <remarks>
/// 对应基线 `ConflictIndex.build`。**两个不可让步的性质**，方向相反，都要测：
///
/// | 性质 | 若被破坏 | 后果 |
/// | --- | --- | --- |
/// | **每个团内部逐对都冲突** | 把**连通分量**当成团（`a—b—c` 但 `a` 与 `c` 不冲突） | 模型禁止 `a`、`c` 同时占用，**静默丢掉可行解** |
/// | **每条原始边都被某个团覆盖** | 只建部分团 | 某对冲突舱位失去约束，**物理冲突被放过** |
///
/// 前者是"约束太强"，后者是"约束太弱"。**只测一边，另一边就能错着上线。**
///
/// The two properties pull in opposite directions and both are pinned: one **over-constrains** (a connected component
/// used as a clique silently discards feasible solutions), the other **under-constrains** (a partial cover lets a physical conflict through). **Testing only one side
/// leaves the other free to ship broken.**
/// </remarks>
public class ConflictIndexTests {
    /// <summary>**三角形应收成一个三元素团，而不是三条二元素团** / A triangle must become one three-element clique, not three two-element ones.</summary>
    /// <remarks>
    /// 三种都冲突（`A—B`、`B—C`、`A—C`）。若实现"每条边自成一个团"，
    /// **覆盖仍然完整、每个团仍然合法**——只是约束数从 1 涨到 3。这条判据正是区分两者的唯一办法。
    ///
    /// All three pairs conflict. "One clique per edge" would still be a **complete and legal cover** — it just emits 3 constraints instead of 1. This criterion is the only
    /// way to tell the two apart.
    /// </remarks>
    [Fact]
    public void Build_ShouldMergeATriangleIntoASingleClique() {
        var index = ConflictIndex.Build(new HashSet<PositionConflict> {
            new("A", "B"), new("B", "C"), new("A", "C"),
        });

        index.Cliques.Should().HaveCount(1, "a triangle is one maximal clique");
        index.Cliques[0].Should().BeEquivalentTo(new[] { "A", "B", "C" });
    }

    /// <summary>**路径不得被当成团** / A path must never be treated as a clique.</summary>
    /// <remarks>
    /// `A—B`、`B—C`，但 **`A` 与 `C` 不冲突**。正确的覆盖是两个团 `{A,B}`、`{B,C}`。
    ///
    /// 若把连通分量 `{A,B,C}` 当成团，模型会加上 `occupied(A) + occupied(B) + occupied(C) <= 1`，
    /// **禁止 `A` 与 `C` 同时占用**——而它们本来可以同时占用。**这是静默丢掉可行解**，
    /// 求解器不会报错，只是给出一个更差的"最优解"。
    ///
    /// `A—B` and `B—C`, but **`A` and `C` do not conflict**. The correct cover is two cliques `{A,B}` and `{B,C}`. Treating the connected component `{A,B,C}` as a clique
    /// would add `occupied(A) + occupied(B) + occupied(C) &lt;= 1`, **forbidding `A` and `C` from being occupied together** when they legitimately can be. That is a
    /// **silent loss of feasible solutions**: the solver never complains, it just reports a worse "optimum".
    /// </remarks>
    [Fact]
    public void Build_ShouldNeverTreatAConnectedComponentAsAClique() {
        var index = ConflictIndex.Build(new HashSet<PositionConflict> {
            new("A", "B"), new("B", "C"),
        });

        index.Cliques.Should().HaveCount(2, "A—B—C is connected but A and C do not conflict");
        foreach (IReadOnlySet<string> clique in index.Cliques) {
            clique.Should().HaveCount(2, "no genuine triangle exists here");
        }
        index.Cliques.Should().NotContainEquivalentOf(new[] { "A", "B", "C" },
            "including A and C in one clique would forbid a legal combination");
    }

    /// <summary>**候选必须与团内每一个成员都冲突** / A candidate must conflict with every clique member.</summary>
    /// <remarks>
    /// **这条判据是补的。** 原来的路径夹具（`A—B—C`）**根本没走到那句守卫**：
    /// 种子 `{A,B}` 的候选是 `adj(A) ∩ adj(B) = {B} ∩ {A,C} = ∅`，**候选项为空**，
    /// 守卫对不对都不影响结果——变异存活。
    ///
    /// 现在用**需要长到第四个成员**的夹具：
    ///
    /// | 边 | 说明 |
    /// | --- | --- |
    /// | `A—B`、`B—C`、`A—C` | 三角形，种子之外先加入 `C` |
    /// | `A—D`、`B—D` | `D` 与 `A`、`B` 冲突，但**与 `C` 不冲突** |
    ///
    /// 正确的覆盖是 **2 个团**：`{A,B,C}` 与 `{A,B,D}`。
    /// 若守卫失效，`D` 会在 `C` 已入团后仍被加入，得到**一个四元素团** `{A,B,C,D}`——
    /// 而 `C` 与 `D` 从未声明冲突，那个团会**禁止 `C`、`D` 同时占用**，静默丢掉可行解。
    ///
    /// **This criterion was added later.** The original path fixture (`A—B—C`) **never reached that guard**: the seed `{A,B}` yields candidates
    /// `adj(A) ∩ adj(B) = {B} ∩ {A,C} = ∅`, so **the candidate list is empty** and the guard makes no difference either way — the mutant survived. The fixture now
    /// **needs a fourth member** (table above). The correct cover is **two cliques**; with the guard gone, `D` joins after `C` is already in, producing **one four-element
    /// clique** where `C` and `D` never conflicted — silently discarding feasible solutions.
    /// </remarks>
    [Fact]
    public void Build_ShouldRejectACandidateThatMissesOneCliqueMember() {
        var index = ConflictIndex.Build(new HashSet<PositionConflict> {
            new("A", "B"), new("B", "C"), new("A", "C"),
            new("A", "D"), new("B", "D"),
        });

        index.Cliques.Should().HaveCount(2, "C and D never conflict, so they cannot share a clique");
        index.Cliques.Should().NotContain(clique => clique.Contains("C") && clique.Contains("D"),
            "putting C and D in one clique would forbid a legal simultaneous occupancy");
    }

    /// <summary>**每条原始边都必须被某个团覆盖** / Every original edge must be covered by some clique.</summary>
    /// <remarks>
    /// 反向对照：若实现只建了一部分团，上面的三角形与路径用例**都可能仍然通过**。
    /// 这条用"两个互不相连的边 + 一个三角形"的组合，逐条验证覆盖完整性。
    ///
    /// Reverse control: an implementation that builds only part of the cover could still pass the triangle and path cases. This one uses two disconnected edges plus a
    /// triangle and checks coverage edge by edge.
    /// </remarks>
    [Fact]
    public void Build_ShouldCoverEveryOriginalEdge() {
        var edges = new HashSet<PositionConflict> {
            new("A", "B"), new("B", "C"), new("A", "C"),
            new("D", "E"),
            new("F", "G"),
        };
        var index = ConflictIndex.Build(edges);

        foreach (PositionConflict edge in edges) {
            bool covered = false;
            foreach (IReadOnlySet<string> clique in index.Cliques) {
                if (clique.Contains(edge.First) && clique.Contains(edge.Second)) {
                    covered = true;
                    break;
                }
            }
            covered.Should().BeTrue($"the edge {edge.First}—{edge.Second} must appear in some clique");
        }
    }

    /// <summary>**每个团内部逐对都必须真的冲突** / Every pair inside every clique must genuinely conflict.</summary>
    /// <remarks>
    /// 与上一条相反方向：那一条查"边都在",这一条查"团里没有多余的舱位"。
    /// 两者合起来才等价于"团"的定义。
    ///
    /// The opposite direction: the previous check asserts every edge appears; this one asserts no clique contains an extra position. Together they are exactly the
    /// definition of a clique.
    /// </remarks>
    [Fact]
    public void Build_ShouldOnlyEmitGenuineCliques() {
        var edges = new HashSet<PositionConflict> {
            new("A", "B"), new("B", "C"), new("A", "C"),
            new("D", "E"),
            new("F", "G"),
        };
        var index = ConflictIndex.Build(edges);

        foreach (IReadOnlySet<string> clique in index.Cliques) {
            var members = new List<string>(clique);
            for (int first = 0; first < members.Count; first++) {
                for (int second = first + 1; second < members.Count; second++) {
                    bool declared = edges.Contains(new PositionConflict(members[first], members[second]))
                        || edges.Contains(new PositionConflict(members[second], members[first]));
                    declared.Should().BeTrue(
                        $"{members[first]} and {members[second]} sit in one clique but were never declared conflicting");
                }
            }
        }
    }

    /// <summary>**空输入给出空覆盖** / An empty input yields an empty cover.</summary>
    [Fact]
    public void Build_ShouldReturnNoCliquesForNoEdges() {
        var index = ConflictIndex.Build(new HashSet<PositionConflict>());

        index.Cliques.Should().BeEmpty();
    }

    /// <summary>**方向无关：`(a,b)` 与 `(b,a)` 是同一条边** / Direction-agnostic: `(a,b)` and `(b,a)` are the same edge.</summary>
    /// <remarks>
    /// 若实现不去重，同一条反向边会留到第二轮，产生**重复的团**——约束重复不影响正确性，但白白多一行，
    /// 且"团数"这个指标就失去了意义。
    ///
    /// Without deduplication the reversed duplicate survives into a second round and produces a **duplicate clique** — harmless to correctness, but it doubles the
    /// constraint count and makes "clique count" meaningless as a metric.
    /// </remarks>
    [Fact]
    public void Build_ShouldTreatReversedEdgesAsOneEdge() {
        var index = ConflictIndex.Build(new HashSet<PositionConflict> {
            new("A", "B"), new("B", "A"),
        });

        index.Cliques.Should().HaveCount(1, "A—B and B—A are the same conflict");
        index.Cliques[0].Should().HaveCount(2);
    }

    /// <summary>**孤立的边各自成团** / Disconnected edges each form their own clique.</summary>
    [Fact]
    public void Build_ShouldKeepDisconnectedEdgesApart() {
        var index = ConflictIndex.Build(new HashSet<PositionConflict> {
            new("A", "B"), new("C", "D"),
        });

        index.Cliques.Should().HaveCount(2);
        IndexOfPair(index, "A", "B").Should().Be(0, "the first edge seeds the first clique");
        IndexOfPair(index, "C", "D").Should().Be(1);
    }

    /// <summary>找到包含指定两个成员的团的下标 / Find the index of the clique containing both members.</summary>
    /// <param name="index">团覆盖 / The cover.</param>
    /// <param name="first">成员一 / First member.</param>
    /// <param name="second">成员二 / Second member.</param>
    /// <returns>下标 / The index.</returns>
    private static int IndexOfPair(ConflictIndex index, string first, string second) {
        for (int position = 0; position < index.Cliques.Count; position++) {
            if (index.Cliques[position].Contains(first) && index.Cliques[position].Contains(second)) {
                return position;
            }
        }
        return -1;
    }
}
