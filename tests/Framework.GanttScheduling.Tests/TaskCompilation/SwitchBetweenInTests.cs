// "居中"指示量的判据 / Criteria for the in-between indicator.

using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model;
using Xunit;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.TaskCompilation;

/// <summary>
/// **任两任务相同即恒零；端点不算中间；同一时刻仍可居中** / Any two coinciding tasks force zero, endpoints are not middles, and touching starts may still sit between.
/// </summary>
/// <remarks>
/// 三条排除是 那条对角线的三维推广。漏掉哪条都算得出来：
/// 不排除 `t1 == t2` 会让一个任务**自己与自己之间**；不排除端点相同会让**没有中间**的三元组成立。
///
/// The three exclusions generalise the earlier diagonal to three dimensions. Dropping either still computes: without `t1 == t2` a task ends up **between itself**; without the
/// endpoint exclusions a triple with **no middle** holds.
/// </remarks>
public class SwitchBetweenInTests {
    /// **三点各不相同且有序时成立** / True when the three are distinct and ordered.
    [Fact]
    public void Holds_ShouldBeTrueWhenOrdered() =>
        SwitchBetweenIn.Holds(allDistinct: true, leftStart: 0.0, middleStart: 4.0, rightStart: 8.0)
            .Should().BeTrue();

    /// **中间在左端之前不成立** / False when the middle is before the left end.
    [Fact]
    public void Holds_ShouldFailWhenTheMiddleIsBeforeTheLeft() =>
        SwitchBetweenIn.Holds(allDistinct: true, leftStart: 5.0, middleStart: 1.0, rightStart: 8.0)
            .Should().BeFalse();

    /// **中间在右端之后不成立** / False when the middle is after the right end.
    [Fact]
    public void Holds_ShouldFailWhenTheMiddleIsAfterTheRight() =>
        SwitchBetweenIn.Holds(allDistinct: true, leftStart: 0.0, middleStart: 9.0, rightStart: 8.0)
            .Should().BeFalse();

    /// <summary>
    /// **任两任务相同即恒零，即使数值有序** / Any two coinciding tasks force zero, even when the numbers are ordered.
    /// </summary>
    /// <remarks>
    /// 判据喂的是**数值完全有序**的三元组（`0 ≤ 4 ≤ 8`），只让"两两不同"为假。
    /// 若实现漏掉排除，数值判定会给出 `true`——这正是最安静的一种错：**端点被当成了中间点**。
    ///
    /// The criterion feeds a **numerically ordered** triple (`0 ≤ 4 ≤ 8`) and only makes "all distinct" false. An implementation missing the exclusion returns `true` — the
    /// quietest error of all: **an endpoint counted as the middle**.
    /// </remarks>
    [Fact]
    public void Holds_ShouldFailWheneverTwoTasksCoincide() {
        SwitchBetweenIn.Holds(allDistinct: false, leftStart: 0.0, middleStart: 4.0, rightStart: 8.0)
            .Should().BeFalse("no middle exists when two of the three are the same task");
    }

    /// <summary>
    /// **同一时刻仍可居中** / Touching starts may still sit between.
    /// </summary>
    /// <remarks>
    /// `start[middle] == start[left]` 在 `≤` 下成立。写成严格不等会让**同时开始的排布全部失去居中关系**，
    /// 而这与 `frontOf` 的 `leq` 不一致。
    ///
    /// `start[middle] == start[left]` holds under `≤`. Strict inequality would **strip every such arrangement of its between-relations**, and that would contradict
    /// `frontOf`'s `leq`.
    /// </remarks>
    [Fact]
    public void Holds_ShouldBeTrueWhenTheMiddleTouchesTheLeft() =>
        SwitchBetweenIn.Holds(allDistinct: true, leftStart: 3.0, middleStart: 3.0, rightStart: 6.0)
            .Should().BeTrue();

    /// <summary>
    /// **中间贴住右端时也成立** / True when the middle touches the right end too.
    /// </summary>
    /// <remarks>
    /// 上一版只测了"中间贴住左端"，于是 `middleStart &lt; rightStart` 这个变异**存活**——
    /// 右侧那一半严格不等没人管。左侧与右侧必须各测一条，否则只钉住了一半。
    ///
    /// The previous version only covered "middle touches the left", so the `middleStart &lt; rightStart` mutant **survived** — nothing guarded the right-hand strict inequality.
    /// Each side needs its own criterion, otherwise only half the rule is pinned.
    /// </remarks>
    [Fact]
    public void Holds_ShouldBeTrueWhenTheMiddleTouchesTheRight() =>
        SwitchBetweenIn.Holds(allDistinct: true, leftStart: 0.0, middleStart: 6.0, rightStart: 6.0)
            .Should().BeTrue("the baseline uses ≤ on both sides");

    /// <summary>
    /// **表的三元数量是任务立方，且任两相同的组合恒为 `false`** / The table has task-cubed triples, and every combination with a repeat is false.
    /// </summary>
    /// <remarks>
    /// 判据同时钉住"数量"与"重复即零"：跳过重复组合的表数量会少，不跳过则会出现 `true`。
    /// 三个任务时，6 个两两不同的有序三元组里有 1 个有序（`A≤B≤C`）。
    ///
    /// The criterion pins both the count and "a repeat means zero": skipping repeated combinations leaves fewer entries, not skipping lets `true` appear. With three tasks,
    /// exactly 1 of the 6 distinct ordered triples is ordered (`A ≤ B ≤ C`).
    /// </remarks>
    [Fact]
    public void Table_ShouldBeCubicWithRepeatsForcedFalse() {
        var tasks = new List<string> { "A", "B", "C" };
        var starts = new Dictionary<string, double> { ["A"] = 0.0, ["B"] = 4.0, ["C"] = 8.0 };

        IReadOnlyDictionary<(string Middle, string Left, string Right), bool> table =
            SwitchBetweenIn.Table(tasks, task => starts[task]);

        table.Should().HaveCount(27, "3 × 3 × 3");
        table[("B", "A", "C")].Should().BeTrue("B sits between A and C");
        table[("C", "A", "B")].Should().BeFalse("C is not between A and B");
        table[("A", "A", "B")].Should().BeFalse("A is an endpoint, not a middle");
        table[("B", "A", "B")].Should().BeFalse("B is an endpoint on the right");
        table[("A", "B", "B")].Should().BeFalse("A is before a repeated endpoint, not between");
        table[("B", "A", "A")].Should().BeFalse("a repeated endpoint leaves no middle");
    }
}
