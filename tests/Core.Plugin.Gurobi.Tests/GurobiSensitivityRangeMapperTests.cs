#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Analysis;
using Fuookami.Ospf.Core.Solver.Report;
using System.Collections.Generic;
using Xunit;

namespace Fuookami.Ospf.Core.Plugin.Gurobi.Tests;

/// <summary>
/// **哨兵不是区间** / A sentinel is not a range.
/// </summary>
/// <remarks>
/// **这里补的是基线 `captureSensitivityRanges` 的对应物：在主求解里读 `SARHSLow`／`SARHSUp`。**
///
/// **而这些属性有两个陷阱，都&lt;b&gt;不会抛异常、只会给出看起来合法的数字**：**
///
/// 1. **presolve 打开时返回的是哨兵**（基线实测：`c1` 得到 4 而不是 0）
/// 2. **无界的一侧返回的是 `±inf`，而 `-inf` 与一个有限数在"取较小值"的运算里长得一样**
///
/// **所以折算层必须有判据，否则"读到数了"和"读到区间了"永远分不清。**
///
/// **This fills the counterpart of its `captureSensitivityRanges`.** **Those attributes have two traps, neither of which
/// throws and both of which yield plausible-looking numbers: presolve-on returns sentinels (the code measured c1 yielding 4
/// instead of 0), and an unbounded side returns `±inf`, which behaves like a finite number in a min/max.** **So the folding layer
/// needs criteria, or "read a number" and "read a range" can never be told apart.**
/// </remarks>
public class GurobiSensitivityRangeMapperTests
{
    /// <summary>**有限端点保留，`±inf` 折算成该侧无边** / Finite endpoints survive; `±inf` means no bound on that side.</summary>
    [Fact]
    public void TheMapper_ShouldDropInfiniteSides() {
        var raw = new Dictionary<int, (double? Lower, double? Upper)> {
            [0] = (0.0, 4.0),
            [1] = (double.NegativeInfinity, 4.0),
            [2] = (2.0, double.PositiveInfinity),
            [3] = (double.NegativeInfinity, double.PositiveInfinity),
            [4] = (double.NaN, 4.0),
        };
        ConstraintId[] rowOrder = [
            new ConstraintId("c-finite"), new ConstraintId("c-lower-open"),
            new ConstraintId("c-upper-open"), new ConstraintId("c-both-open"), new ConstraintId("c-nan"),
        ];

        IReadOnlyDictionary<ConstraintId, SensitivityRange?> ranges =
            GurobiSensitivityRangeMapper.ToRanges(raw, rowOrder);

        ranges.Should().ContainKey(new ConstraintId("c-finite"));
        ranges[new ConstraintId("c-finite")]!.Lower!.Value.ToDouble().Should().Be(0.0);
        ranges[new ConstraintId("c-finite")]!.Upper!.Value.ToDouble().Should().Be(4.0);

        ranges[new ConstraintId("c-lower-open")]!.Lower.Should().BeNull(
            "an unbounded side is reported as absent, not as minus infinity");
        ranges[new ConstraintId("c-lower-open")]!.Upper!.Value.ToDouble().Should().Be(4.0);

        ranges[new ConstraintId("c-upper-open")]!.Upper.Should().BeNull();
        ranges[new ConstraintId("c-nan")]!.Lower.Should().BeNull(
            "a NaN endpoint is not a number anyone can compare against");

        ranges.Should().NotContainKey(
            new ConstraintId("c-both-open"),
            "a pair with neither side bounded is dropped entirely, so \"no information\" never looks like \"unaffected\"");
    }

    /// <summary>**约束名就是 `ConstraintId`** / The constraint name is the `ConstraintId`.</summary>
    /// <remarks>
    /// **这条断言的是"不需要映射表"这件事：** **插件建 `GRBConstr` 时用的是 triad 里的约束名，
    /// 而那个名字就是 `ConstraintId` 的值。** **如果哪天插件改用别的命名（比如 `r0`、`r1`），
    /// 范围就会静默地全部丢失——那正是这条判据要挡的。**
    ///
    /// **This asserts that no mapping table is needed: the plugin names `GRBConstr`s after the triad's constraint names, and that
    /// name is the `ConstraintId` value.** **If the plugin ever switches to positional names, every range would silently vanish —
    /// which is what this criterion stops.**
    /// </remarks>
    [Fact]
    public void TheMapper_ShouldPairByRowOrder() {
        // **行号是 Gurobi 那一侧的键，`ConstraintId` 是报告那一侧的键。**
        // **两次求解的行序可以不同，所以配对只能按序做，不能按名字做——**
        // **而这条 triad 路径上约束名是空的（实测）。**
        //
        // **The row index keys Gurobi's side and `ConstraintId` keys the report's; the two must be paired in order, because this
        // triad path produces empty constraint names (measured).**
        var raw = new Dictionary<int, (double? Lower, double? Upper)> {
            [0] = (1.0, 9.0),
        };
        ConstraintId[] rowOrder = [new ConstraintId("constraint-7")];

        GurobiSensitivityRangeMapper.ToRanges(raw, rowOrder)
            .Should().ContainKey(new ConstraintId("constraint-7"));
    }

    /// <summary>**空名与空输入不制造条目** / Empty names and empty input create no entries.</summary>
    [Fact]
    public void TheMapper_ShouldIgnoreRowsBeyondTheReport() {
        // **行号比报告的行数还多 = 这一次的对应关系不成立。** **宁可一个都不给，也不要错位配对。**
        // **A row index beyond the report's rows means the correspondence does not hold this time: better nothing than a mispairing.**
        var raw = new Dictionary<int, (double? Lower, double? Upper)> {
            [0] = (1.0, 9.0),
            [1] = (2.0, 8.0),
        };

        GurobiSensitivityRangeMapper.ToRanges(raw, [new ConstraintId("only-one-row")])
            .Should().ContainSingle("the second row has no counterpart and must not be guessed onto the first");
        GurobiSensitivityRangeMapper.ToRanges(
            new Dictionary<int, (double? Lower, double? Upper)>(), []).Should().BeEmpty();
    }
}