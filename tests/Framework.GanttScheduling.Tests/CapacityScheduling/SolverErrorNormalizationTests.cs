// 求解器错误规范化的判据 / Criteria for solver error normalization.

using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.CapacityScheduling.Service;
using Fuookami.Ospf.Utils.Error;
using Xunit;

namespace Fuookami.Ospf.Framework.GanttScheduling.Tests.CapacityScheduling;

/// <summary>
/// **非 `ErrorCode` 的编码降级为 `Unknown`，但原始信息必须保留** / A non-`ErrorCode` code degrades to `Unknown`, but the original detail must survive.
/// </summary>
/// <remarks>
/// 下游按 `ErrorCode` 分支（可重试、不可行、超时）。一个未知枚举值会落进 `default`，于是"求解器超时"被当成"未知错误"，
/// 重试策略整体失效。降级给出可分支的值，保留原文才让人查得到真正发生了什么——**只做前者就是信息黑洞**。
///
/// Downstream branches on `ErrorCode` (retryable, infeasible, timeout). An unknown enum value falls into `default`, so "the solver timed out" becomes "unknown error" and the
/// retry policy stops working. Degrading gives something to branch on; preserving the original text is what lets a human find out what happened — **only the former makes an
/// information black hole**.
/// </remarks>
public class SolverErrorNormalizationTests {
    /// <summary>测试用后端编码 / A backend code used in tests.</summary>
    private enum BackendCode {
        /// <summary>求解器超时 / The solver timed out.</summary>
        SolverTimeout = 7,
    }

    /// **`ErrorCode` 错误原样通过，编码不变** / An `ErrorCode` error passes through unchanged.
    [Fact]
    public void Normalize_ShouldPassThroughAnErrorCodeError() {
        var original = new Err<ErrorCode>(ErrorCode.ORModelInfeasible, "model is infeasible");

        Error<ErrorCode> normalized = SolverErrorNormalization.Normalize(original);

        normalized.Code.Should().Be(ErrorCode.ORModelInfeasible);
        normalized.Message.Should().Be("model is infeasible");
        normalized.Should().BeOfType<Err<ErrorCode>>("there is no associated value to preserve");
    }

    /// <summary>
    /// **关联值在 `ErrorCode` 分支同样要保留** / The associated value survives on the `ErrorCode` branch too.
    /// </summary>
    /// <remarks>
    /// 一条携带模型片段的求解器错误若在这一层被压成无值 `Err`，那段片段就丢了，
    /// 而它往往正是定位问题的唯一线索。
    ///
    /// A solver error carrying a model fragment that gets flattened into a value-less `Err` here loses that fragment, and it is often the only clue for locating the issue.
    /// </remarks>
    [Fact]
    public void Normalize_ShouldKeepTheValueOnTheErrorCodeBranch() {
        var original = new ExErr<ErrorCode, string>(ErrorCode.OREngineSolvingException, "boom", "row 42");

        Error<ErrorCode> normalized = SolverErrorNormalization.Normalize(original);

        normalized.WithValue.Should().BeTrue("the fragment must not be dropped at the boundary");
        normalized.Should().BeOfType<ExErr<ErrorCode, object?>>();
        normalized.Value.Should().Be("row 42", "the fragment itself is what carries the clue");
    }

    /// <summary>
    /// **非 `ErrorCode` 的编码降级为 `Unknown`** / A non-`ErrorCode` code degrades to `Unknown`.
    /// </summary>
    /// <remarks>
    /// 判据同时断言三件事：编码是 `Unknown`、**原文里还能看到原始编码**、消息里点明了是哪种降级。
    /// 只断言第一件的话，一个把所有信息都扔掉的实现照样通过。
    ///
    /// The criterion asserts three things: the code is `Unknown`, **the original code still appears in the text**, and the message names the degradation. Asserting only the
    /// first would let an implementation that throws all detail away pass.
    /// </remarks>
    [Fact]
    public void Normalize_ShouldDegradeAForeignCodeWhilePreservingIt() {
        var original = new Err<BackendCode>(BackendCode.SolverTimeout, "timed out after 30s");

        Error<ErrorCode> normalized = SolverErrorNormalization.Normalize(original);

        normalized.Code.Should().Be(ErrorCode.Unknown, "downstream needs a value it can branch on");
        normalized.WithValue.Should().BeTrue("the original code must be preserved somewhere");
        normalized.Value!.ToString().Should().Contain("SolverTimeout",
            "the original code must remain findable, otherwise this is an information black hole");
        normalized.Message.Should().Contain("non-ErrorCode", "the message must name what happened");
    }

    /// **批量规范化保持顺序与条数** / Batch normalization keeps the order and the count.
    [Fact]
    public void NormalizeAll_ShouldPreserveOrderAndCount() {
        var errors = new List<Error<object>> {
            new Err<object>(ErrorCode.DataNotFound, "first"),
            new Err<object>(BackendCode.SolverTimeout, "second"),
            new Err<object>(ErrorCode.ApplicationFailed, "third"),
        };

        IReadOnlyList<Error<ErrorCode>> normalized = SolverErrorNormalization.NormalizeAll(errors);

        normalized.Should().HaveCount(3);
        normalized[0].Message.Should().Be("first");
        normalized[1].Code.Should().Be(ErrorCode.Unknown);
        normalized[2].Message.Should().Be("third");
    }

    /// **空列表返回空列表，不是 null** / An empty list returns an empty list, not null.
    [Fact]
    public void NormalizeAll_ShouldReturnAnEmptyListForAnEmptyInput() {
        IReadOnlyList<Error<ErrorCode>> normalized =
            SolverErrorNormalization.NormalizeAll(new List<Error<object>>());

        normalized.Should().NotBeNull("a null here becomes a NullReferenceException at the Fatal constructor");
        normalized.Should().BeEmpty();
    }
}
