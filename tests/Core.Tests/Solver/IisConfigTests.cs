#nullable enable

using System;
using Xunit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Core.Solver.Iis;

namespace Fuookami.Ospf.Core.Tests.Solver;

/// <summary>
/// 不可行诊断配置契约测试，对照固定 Kotlin 基线的 IISConfig。
/// Contract tests for the infeasibility diagnostic configuration, mirroring the pinned Kotlin IISConfig.
/// </summary>
public class IisConfigTests {
    [Fact]
    public void DefaultsMatchKotlinBaseline() {
        var config = new IisConfig();
        Assert.Equal(TimeSpan.FromSeconds(30), config.Time);
        Assert.Equal(1e-6, config.SlackTolerance.ToDouble(), 12);
        Assert.Null(config.NotImprovementTime);
        Assert.Null(config.InterruptibleTime);
        Assert.Null(config.ComputingStatusCallBack);
        Assert.Null(config.BackendConfiguration);
    }

    [Fact]
    public void DefaultThreadCountFollowsKotlinProcessorHeuristic() {
        int processors = Environment.ProcessorCount;
        ulong expected = processors <= 16 ? (ulong)processors :
            processors < 24 ? 16UL :
            processors < 32 ? 24UL : 32UL;
        Assert.Equal(expected, new IisConfig().ThreadCount);
        Assert.Equal((int)expected, new IisConfig().NativeThreadCount);
    }

    [Fact]
    public void ThreadCountWithoutNativeRepresentationIsRejected() {
        Assert.Null(new IisConfig { ThreadCount = 0UL }.NativeThreadCount);
        AssertIllegal(new IisConfig { ThreadCount = 0UL }.ValidateRuntime());
        AssertIllegal(new IisConfig { ThreadCount = (ulong)int.MaxValue + 1UL }.ValidateRuntime());
    }

    [Fact]
    public void ValidatedConfigCarriesEveryRuntimeField() {
        Assert.True(new IisConfig {
            Time = TimeSpan.Zero,
            ThreadCount = 3UL,
            NotImprovementTime = TimeSpan.FromSeconds(2),
            InterruptibleTime = TimeSpan.FromSeconds(4),
            SlackTolerance = new Flt64(1e-3),
        }.ValidateRuntime().IsOk);
    }

    [Fact]
    public void NegativeDurationsAreRejected() {
        AssertIllegal(new IisConfig { Time = TimeSpan.FromSeconds(-1) }.ValidateRuntime());
        AssertIllegal(new IisConfig { NotImprovementTime = TimeSpan.FromSeconds(-1) }.ValidateRuntime());
        AssertIllegal(new IisConfig { InterruptibleTime = TimeSpan.FromSeconds(-1) }.ValidateRuntime());
    }

    [Fact]
    public void NegativeOrNonFiniteSlackToleranceIsRejected() {
        AssertIllegal(new IisConfig { SlackTolerance = new Flt64(-1) }.ValidateRuntime());
        AssertIllegal(new IisConfig { SlackTolerance = new Flt64(double.NaN) }.ValidateRuntime());
        AssertIllegal(new IisConfig { SlackTolerance = new Flt64(double.PositiveInfinity) }.ValidateRuntime());
    }

    [Fact]
    public void RecordEqualityFollowsValues() {
        Assert.Equal(new IisConfig { Time = TimeSpan.FromSeconds(1) }, new IisConfig { Time = TimeSpan.FromSeconds(1) });
        Assert.NotEqual(new IisConfig { Time = TimeSpan.FromSeconds(1) }, new IisConfig { Time = TimeSpan.FromSeconds(2) });
    }

    [Fact]
    public void ComputingStatusExposesKotlinProgressRatios() {
        var status = new IisComputingStatus(
            RestBoundAmount: 2UL,
            TotalBoundAmount: 8UL,
            RestConstraintAmount: 1UL,
            TotalConstraintAmount: 7UL);

        Assert.Equal(0.25, status.BoundProgress.ToDouble(), 12);
        Assert.Equal(1d / 7d, status.ConstraintProgress.ToDouble(), 12);
        Assert.Equal(0.2, status.TotalProgress.ToDouble(), 12);
        // 分母为零沿用 IEEE 语义，与 Kotlin 的 Flt64 除法一致。 / Zero denominators keep IEEE semantics, matching Kotlin's Flt64 division.
        Assert.True(double.IsInfinity(new IisComputingStatus(0UL, 0UL, 1UL, 0UL).ConstraintProgress.ToDouble()));
        Assert.True(double.IsNaN(new IisComputingStatus(0UL, 0UL, 0UL, 0UL).TotalProgress.ToDouble()));
    }

    private static void AssertIllegal(Result<Success, ErrorCode, Error<ErrorCode>> result) =>
        Assert.Equal(
            ErrorCode.IllegalArgument,
            Assert.IsType<Failed<Success, ErrorCode, Error<ErrorCode>>>(result).Code);
}
