// 活动排程上下文的判据 / Criteria for the activity scheduling context.

using System;
using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Activity.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Activity.Service;
using Fuookami.Ospf.Framework.GanttScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.Activity;

/// <summary>
/// **命名空间非法必须失败；候选清单必须是快照** / An illegal namespace must fail, and the candidate list must be a snapshot.
/// </summary>
/// <remarks>
/// 命名空间会成为求解器里每条约束名的前缀。放任任意字符串进去，两条不同上下文的约束名可能撞在一起，
/// 而求解器只按名字索引——**约束被静默覆盖**。
///
/// 若上下文直接持有调用方的列表，调用方之后一次 `Add` 就会**静默改变候选集**，而上下文没有任何迹象表明自己变了。
///
/// The namespace becomes the prefix of every constraint name in the solver. Letting arbitrary strings through lets two different contexts collide on names, and the solver
/// indexes by name alone — **constraints are silently overwritten**. Holding the caller's list directly means one later `Add` by the caller **silently changes the candidate
/// set**, with the context showing no sign of having changed.
/// </remarks>
public class ActivitySchedulingContextTests {
    private static readonly DateTimeOffset Origin = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// **合法命名空间通过** / A legal namespace passes.
    [Theory]
    [InlineData("activity_scheduling")]
    [InlineData("a")]
    [InlineData("Model9")]
    [InlineData("a_b_c")]
    public void ValidateNamespace_ShouldAcceptLegalNames(string name) {
        ActivitySchedulingContext<Flt64>.ValidateNamespace(name)
            .Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>
    /// **首字符为数字被拒** / A leading digit is rejected.
    /// </summary>
    /// <remarks>
    /// 这个判据价值最高：`[A-Za-z0-9_]*` 这样的宽松正则会放过 `9_model`，而正则要求**首字符是字母**。
    /// 两者只在首字符上不同，所以判据必须是"首字符数字"这一种输入。
    ///
    /// This is the most valuable criterion: a lenient pattern like `[A-Za-z0-9_]*` would accept `9_model`, while its pattern requires **a leading letter**. The two
    /// differ only at the first character, so the fixture must use a digit there.
    /// </remarks>
    [Theory]
    [InlineData("9_model")]
    [InlineData("_model")]
    [InlineData("-model")]
    public void ValidateNamespace_ShouldRejectANonLetterFirstCharacter(string name) {
        ActivitySchedulingContext<Flt64>.ValidateNamespace(name)
            .Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
    }

    /// **空或含非法字符被拒** / Empty or containing illegal characters is rejected.
    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("model name")]
    [InlineData("model-name")]
    [InlineData("model.name")]
    public void ValidateNamespace_ShouldRejectEmptyAndIllegalCharacters(string? name) {
        ActivitySchedulingContext<Flt64>.ValidateNamespace(name)
            .Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>
    /// **候选清单是快照，调用方之后改动不影响上下文** / The candidate list is a snapshot; later caller changes do not reach the context.
    /// </summary>
    /// <remarks>
    /// 判据在构造之后往原列表追加一项，再断言上下文里的数量没变。
    /// 只断言"构造时数量正确"是测不到这一点的——持有引用与拷贝在构造那一刻完全相同。
    ///
    /// The criterion appends to the original list after construction and then asserts the context's count is unchanged. Asserting merely "the count was right at construction"
    /// cannot catch this — holding the reference and copying are indistinguishable at that instant.
    /// </remarks>
    [Fact]
    public void Constructor_ShouldSnapshotTheChoices() {
        var source = new List<ActivityPlacement<Flt64>> { Placement("A") };
        var context = new ActivitySchedulingContext<Flt64>(source);

        source.Add(Placement("B"));

        context.Choices.Should().ContainSingle("the context took a copy, not the caller's list");
        context.Choices[0].Key.Task.Should().Be("A");
    }

    /// **空候选是合法输入** / An empty candidate list is legal input.
    [Fact]
    public void Constructor_ShouldAcceptAnEmptyChoiceList() {
        var context = new ActivitySchedulingContext<Flt64>(Array.Empty<ActivityPlacement<Flt64>>());

        context.Choices.Should().BeEmpty();
    }

    /// <summary>构造一次放置 / Build a placement.</summary>
    /// <param name="task">任务身份 / The task identity.</param>
    /// <returns>放置 / The placement.</returns>
    private static ActivityPlacement<Flt64> Placement(string task) =>
        new(new ActivityChoiceKey(task, "mode", Origin),
            new TimeRange(Origin.AddHours(1), Origin.AddHours(2)),
            Array.Empty<ActivityReservation<Flt64>>());
}
