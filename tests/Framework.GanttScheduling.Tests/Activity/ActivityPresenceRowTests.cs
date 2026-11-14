// 存在性行构造的判据 / Criteria for presence row construction.

using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Activity.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Xunit;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.Activity;

/// <summary>
/// **固定事实只进常数、不进系数；两侧固定事实互相抵消** / Fixed facts contribute to the constant only, and they cancel when both sides have them.
/// </summary>
/// <remarks>
/// 一个已执行完的任务没有决策变量。把它写进系数表会引用**一列并不存在**，而把它写进常数才对——
/// 但写错时模型依然可解，只是行约束了一个不存在的变量。这类错误不会在求解状态里留下痕迹。
///
/// An already-executed task has no decision column. Putting it in the coefficient table references **a column that does not exist**; putting it in the constant is correct — yet
/// when done the wrong way the model stays solvable and the row merely constrains a variable that is not there. The error leaves no trace in the solver status.
/// </remarks>
public class ActivityPresenceRowTests {
    /// **两侧都是决策变量时，系数为 +1 与 −1，上界为零** / With two decision columns the coefficients are +1 and −1 and the upper bound is zero.
    [Fact]
    public void Build_ShouldEmitAlternatingCoefficients() {
        CompiledActivityRow row = ActivityPresenceRow.Build("presence_0", new int?[] { 0 }, new int?[] { 1 });

        row.Coefficients[0].ToDouble().Should().BeApproximately(1.0, 1e-12);
        row.Coefficients[1].ToDouble().Should().BeApproximately(-1.0, 1e-12);
        row.Upper!.Value.ToDouble().Should().BeApproximately(0.0, 1e-12, "neither side is a fixed fact");
    }

    /// **前驱侧的固定事实变成上界 1** / A fixed fact on the `from` side becomes an upper bound of 1.
    [Fact]
    public void Build_ShouldMoveAFixedFromFactIntoTheUpperBound() {
        CompiledActivityRow row = ActivityPresenceRow.Build("presence_0", new int?[] { null }, new int?[] { 1 });

        row.Coefficients.Should().ContainKey(1);
        row.Coefficients.Should().NotContainKey(-1, "a null index has no column");
        row.Coefficients[1].ToDouble().Should().BeApproximately(-1.0, 1e-12);
        row.Upper!.Value.ToDouble().Should().BeApproximately(-1.0, 1e-12, "-(1 fixed fact - 0 fixed facts)");
    }

    /// <summary>
    /// **两侧都有固定事实时上界为零，系数被忽略** / With fixed facts on both sides the bound is zero and the coefficient side is ignored.
    /// </summary>
    /// <remarks>
    /// 这条是"已完成的前驱与已完成的后续之间无需再约束"的表达。判据同时断言**系数表为空**：
    /// 上界为零这件事，一个把 `null` 当成下标 0 的实现也能凑出来，但它会多出一列凭空引用。
    ///
    /// This expresses "no constraint is needed between an executed predecessor and an executed successor". The criterion also asserts the **coefficient table is empty**:
    /// an implementation treating `null` as index 0 could still produce the zero bound while referencing a column that should not be there.
    /// </remarks>
    [Fact]
    public void Build_ShouldCancelWhenBothSidesAreFixed() {
        CompiledActivityRow row = ActivityPresenceRow.Build("presence_0", new int?[] { null }, new int?[] { null });

        row.Coefficients.Should().BeEmpty();
        row.Upper!.Value.ToDouble().Should().BeApproximately(0.0, 1e-12);
    }

    /// **同一列出现多次时系数累加** / A column appearing twice accumulates.
    [Fact]
    public void Build_ShouldAccumulateRepeatedColumns() {
        CompiledActivityRow row = ActivityPresenceRow.Build("presence_0", new int?[] { 0, 0 }, new int?[] { 1 });

        row.Coefficients[0].ToDouble().Should().BeApproximately(2.0, 1e-12);
        row.Coefficients[1].ToDouble().Should().BeApproximately(-1.0, 1e-12);
    }

    /// **同一列同时出现在两侧时互相抵消** / A column on both sides cancels.
    [Fact]
    public void Build_ShouldCancelAColumnPresentOnBothSides() {
        CompiledActivityRow row = ActivityPresenceRow.Build("presence_0", new int?[] { 0 }, new int?[] { 0 });

        row.Coefficients[0].ToDouble().Should().BeApproximately(0.0, 1e-12);
        row.Upper!.Value.ToDouble().Should().BeApproximately(0.0, 1e-12);
    }

    /// **空两侧给出一条上界为零的空行** / Two empty sides yield an empty row with a zero bound.
    [Fact]
    public void Build_ShouldHandleEmptySides() {
        CompiledActivityRow row = ActivityPresenceRow.Build("presence_0", new int?[0], new int?[0]);

        row.Coefficients.Should().BeEmpty();
        row.Upper!.Value.ToDouble().Should().BeApproximately(0.0, 1e-12);
        row.Lower.Should().BeNull("a presence row is one-sided");
    }
}
