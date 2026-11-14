#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Analysis;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Analysis;

/// <summary>
/// 目标批次校验 / Validating a batch of targets.
/// </summary>
/// <remarks>
/// **空列表与重复是两种不同的错误。** 空列表是"你没给我东西"，重复是"你给了我两次同一个东西"——
/// **混成一句话，调用方无法判断该补数据还是该去重。**
///
/// **Empty and duplicate are two different errors.** Empty means "you gave me nothing"; a duplicate means "you gave me the same thing twice" — **merged into one message, the caller cannot
/// tell whether to add data or de-duplicate.**
/// </remarks>
public class CriticalityTargetBatchTests {
    private static ObjectiveTarget Target(string name) =>
        ObjectiveTarget.CreateAtLeast(new ObjectiveId(name), new Flt64(9.0));

    /// <summary>**正常批次通过** / A normal batch passes.</summary>
    [Fact]
    public void TheNormalBatch_ShouldPass() {
        CriticalityProfileBuilder
            .ValidateTargetBatch([Target("objective:a"), Target("objective:b")])
            .Should().BeOfType<Ok<Success, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>**空批次被拒，且说的是"至少一个"** / An empty batch is refused, saying "at least one".</summary>
    [Fact]
    public void TheEmptyBatch_ShouldBeRefused() {
        Result<Success, ErrorCode, Error<ErrorCode>> result =
            CriticalityProfileBuilder.ValidateTargetBatch([]);

        result.Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>(
            "a profile needs at least one target");
        ((Failed<Success, ErrorCode, Error<ErrorCode>>)result).Error.Message
            .Should().Contain("至少需要一个", "the message tells the caller what to add");
    }

    /// <summary>**重复批次被拒，且点名重复的那一个** / A duplicate batch is refused, naming the duplicate.</summary>
    /// <remarks>
    /// **反向对照**：只查"是不是失败"，一个把所有失败都报成"至少需要一个"的实现照样通过——
    /// **而那会让调用方去补数据，而问题其实是去重。**
    ///
    /// **The reverse control**: checking only "did it fail" lets an implementation that reports every failure as "at least one is required" pass — **and that sends the caller off to add
    /// data when the real problem is a duplicate.**
    /// </remarks>
    [Fact]
    public void TheDuplicateBatch_ShouldNameTheDuplicate() {
        Result<Success, ErrorCode, Error<ErrorCode>> result =
            CriticalityProfileBuilder.ValidateTargetBatch([Target("objective:a"), Target("objective:a")]);

        result.Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
        var failed = (Failed<Success, ErrorCode, Error<ErrorCode>>)result;
        failed.Error.Message.Should().Contain("objective:a", "the caller needs to know which one repeats");
        failed.Error.Message.Should().NotContain("至少需要一个",
            "a duplicate is not an empty batch, and saying so sends the caller to the wrong fix");
    }

    /// <summary>**三个相同只报一次** / Three identical targets report the duplicate once.</summary>
    [Fact]
    public void TheTripleDuplicate_ShouldStillNameItOnce() {
        Result<Success, ErrorCode, Error<ErrorCode>> result = CriticalityProfileBuilder.ValidateTargetBatch(
            [Target("objective:a"), Target("objective:a"), Target("objective:a")]);

        result.Should().BeOfType<Failed<Success, ErrorCode, Error<ErrorCode>>>();
        ((Failed<Success, ErrorCode, Error<ErrorCode>>)result).Error.Message
            .Should().Contain("objective:a");
    }
}
