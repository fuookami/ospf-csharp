// 列变量登记的判据 / Criteria for the column registry.

using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model;
using Xunit;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.TaskCompilation;

/// <summary>
/// **同名任务在不同轮次是不同的变量，必须按轮次分组** / The same task in different rounds is a different variable, so grouping by round cannot be skipped.
/// </summary>
/// <remarks>
/// 跨轮冲突写成 `xi[task1] + x[otherIteration][task2] ≤ 1`，要回头访问**任意一轮**的变量。
/// 一个扁平的 `Dictionary&lt;task, var&gt;` 会让同名任务在不同轮次互相覆盖，
/// 跨轮冲突于是约束到同一个变量（`xi + xi ≤ 1`），**本该互斥的两个任务安静地共处**。
///
/// Cross-round conflicts are written `xi[task1] + x[otherIteration][task2] ≤ 1`, reaching back into **any** earlier round. A flat
/// `Dictionary&lt;task, var&gt;` lets same-named tasks in different rounds overwrite each other, so a cross-round conflict constrains one variable to itself (`xi + xi ≤ 1`) and **two
/// tasks that should exclude each other coexist quietly**.
/// </remarks>
public class ColumnRegistryTests {
    /// **每轮各自登记** / Each round is registered separately.
    [Fact]
    public void AddRound_ShouldRegisterEachRound() {
        var registry = new ColumnRegistry<int, string>();

        registry.AddRound(0, new List<string> { "A", "B" });
        registry.AddRound(1, new List<string> { "C" });

        registry.RoundCount.Should().Be(2);
        registry.Rounds.Should().Equal(new[] { 0, 1 });
    }

    /// <summary>
    /// **同名任务在两轮里互不覆盖** / A same-named task in two rounds does not overwrite.
    /// </summary>
    /// <remarks>
    /// 这是本批的核心：`A` 在两轮里都出现，两轮各自都算"含有 A"。
    /// 若实现用扁平的单一映射，第二轮会把第一轮的 `A` 覆盖掉（或反过来），
    /// 于是跨轮冲突找不到"别的轮次里的 A"。
    ///
    /// This is the batch's core: `A` appears in both rounds and each round holds it. A flat single mapping would let the second round overwrite the first (or vice versa), so a
    /// cross-round conflict could not find "A in the other round".
    /// </remarks>
    [Fact]
    public void Contains_ShouldKeepRoundsApart() {
        var registry = new ColumnRegistry<int, string>();
        registry.AddRound(0, new List<string> { "A" });
        registry.AddRound(1, new List<string> { "A" });

        registry.Contains(0, "A").Should().BeTrue();
        registry.Contains(1, "A").Should().BeTrue();
        registry.RoundsContaining("A").Should().Equal(new[] { 0, 1 });
    }

    /// **查询只在同一轮内生效** / A lookup is scoped to its round.
    [Fact]
    public void Contains_ShouldNotLeakAcrossRounds() {
        var registry = new ColumnRegistry<int, string>();
        registry.AddRound(0, new List<string> { "A" });

        registry.Contains(1, "A").Should().BeFalse("round 1 was never given A");
        registry.Contains(99, "A").Should().BeFalse("an unknown round holds nothing");
    }

    /// **未知轮次取到空集合** / An unknown round yields an empty collection.
    [Fact]
    public void Round_ShouldBeEmptyForAnUnknownRound() {
        var registry = new ColumnRegistry<int, string>();

        registry.Round(7).Should().BeEmpty();
    }

    /// **同一轮内重复的列只算一次** / A duplicated column within one round counts once.
    [Fact]
    public void AddRound_ShouldDeduplicateWithinARound() {
        var registry = new ColumnRegistry<int, string>();

        IReadOnlyList<string> added = registry.AddRound(0, new List<string> { "A", "A", "B" });

        added.Should().HaveCount(2, "A appears twice but registers once");
        registry.Round(0).Should().HaveCount(2);
    }

    /// **同一轮加两次是追加，不是重置** / Adding to the same round twice appends rather than resets.
    [Fact]
    public void AddRound_ShouldAppendToAnExistingRound() {
        var registry = new ColumnRegistry<int, string>();
        registry.AddRound(0, new List<string> { "A" });

        registry.AddRound(0, new List<string> { "A", "B" }).Should().ContainSingle()
            .Which.Should().Be("B", "A was already registered");
        registry.Round(0).Should().HaveCount(2);
        registry.RoundCount.Should().Be(1, "the round is reused, not duplicated");
    }

    /// **空轮也登记** / An empty round is still registered.
    [Fact]
    public void AddRound_ShouldRegisterAnEmptyRound() {
        var registry = new ColumnRegistry<int, string>();

        registry.AddRound(0, new List<string>()).Should().BeEmpty();

        registry.RoundCount.Should().Be(1);
        registry.Round(0).Should().BeEmpty();
    }

    /// **跨轮遍历按登记顺序** / The cross-round walk follows registration order.
    [Fact]
    public void RoundsContaining_ShouldFollowRegistrationOrder() {
        var registry = new ColumnRegistry<int, string>();
        registry.AddRound(5, new List<string> { "X" });
        registry.AddRound(2, new List<string> { "X" });
        registry.AddRound(9, new List<string> { "Y" });

        registry.RoundsContaining("X").Should().Equal(new[] { 5, 2 }, "registration order, not sorted order");
        registry.RoundsContaining("Z").Should().BeEmpty();
    }
}
