#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Core.Solver;
using Fuookami.Ospf.Core.Solver.Config;
using Fuookami.Ospf.Core.Solver.Report;

namespace Fuookami.Ospf.Core.Tests.Solver;

/// <summary>
/// 统一求解器运行配置、状态数值契约与可审计来源的测试。
/// Tests for the unified solver runtime configuration, status numeric contracts and auditable provenance.
/// </summary>
/// <remarks>
/// 对照固定 Kotlin 基线的单一 SolverConfig：所有求解器共用运行参数，供应商专有值走 backend 配置并脱敏。
/// Mirrors the pinned Kotlin single SolverConfig: every solver shares the runtime parameters while
/// vendor-specific values travel in the redacted backend configuration.
/// </remarks>
public class SolverConfigContractTest {
    private static readonly Flt64 _precision = NumericConstantsRegistry.For<Flt64>().DecimalPrecision!.Value;

    private static readonly SolverDescriptor _descriptor = new(
        SolverId: "fake",
        BackendName: "Fake",
        Capabilities: new SolverCapabilities(ModelTypes: new HashSet<SolverModelType> { SolverModelType.Lp }),
        BackendVersion: "9.9.9");

    [Fact]
    public void DefaultsMatchKotlinBaseline() {
        var config = new SolverConfig();
        Assert.Equal(TimeSpan.FromSeconds(30), config.TimeLimit);
        Assert.Equal(0d, config.Gap.ToDouble());
        Assert.Equal(_precision.ToDouble(), config.ImproveThreshold.ToDouble());
        Assert.Null(config.NotImprovementTime);
        Assert.Null(config.InterruptibleTime);
        Assert.Null(config.InterruptibleGap);
        Assert.Null(config.BackendConfiguration);
        Assert.All(new bool?[] {
            config.DumpMechanismModelConcurrent,
            config.DumpMechanismModelBlocking,
            config.DumpIntermediateModelConcurrent,
            config.DumpIntermediateModelBounds,
            config.DumpIntermediateModelForceBounds,
        }, Assert.Null);
    }

    [Fact]
    public void DefaultThreadCountFollowsKotlinProcessorHeuristic() {
        int processors = Environment.ProcessorCount;
        ulong expected = processors <= 16 ? (ulong)processors :
            processors < 24 ? 16UL :
            processors < 32 ? 24UL : 32UL;
        Assert.Equal(expected, new SolverConfig().ThreadCount);
        Assert.Equal((int)expected, new SolverConfig().NativeThreadCount);
    }

    [Fact]
    public void ThreadCountBelowOneHasNoNativeRepresentation() {
        var config = new SolverConfig { ThreadCount = 0UL };
        Assert.Null(config.NativeThreadCount);
        AssertIllegal(config.ValidateRuntime());
    }

    [Fact]
    public void ThreadCountAboveNativeIntRangeIsRejected() {
        var config = new SolverConfig { ThreadCount = (ulong)int.MaxValue + 1UL };
        Assert.Null(config.NativeThreadCount);
        AssertIllegal(config.ValidateRuntime());
    }

    [Fact]
    public void ValidatedConfigCarriesEveryRuntimeField() {
        Assert.True(new SolverConfig {
            TimeLimit = TimeSpan.Zero,
            ThreadCount = 4UL,
            Gap = new Flt64(0.01),
            NotImprovementTime = TimeSpan.FromSeconds(5),
            InterruptibleTime = TimeSpan.FromSeconds(10),
            InterruptibleGap = new Flt64(0.5),
        }.ValidateRuntime().IsOk);
    }

    [Fact]
    public void NegativeDurationsAreRejected() {
        AssertIllegal(new SolverConfig { TimeLimit = TimeSpan.FromSeconds(-1) }.ValidateRuntime());
        AssertIllegal(new SolverConfig { NotImprovementTime = TimeSpan.FromSeconds(-1) }.ValidateRuntime());
        AssertIllegal(new SolverConfig { InterruptibleTime = TimeSpan.FromSeconds(-1) }.ValidateRuntime());
    }

    [Fact]
    public void NegativeOrNonFiniteTolerancesAreRejected() {
        AssertIllegal(new SolverConfig { Gap = new Flt64(-1) }.ValidateRuntime());
        AssertIllegal(new SolverConfig { Gap = new Flt64(double.NaN) }.ValidateRuntime());
        AssertIllegal(new SolverConfig { ImproveThreshold = new Flt64(-1) }.ValidateRuntime());
        AssertIllegal(new SolverConfig { InterruptibleGap = new Flt64(-1) }.ValidateRuntime());
        AssertIllegal(new SolverConfig { InterruptibleGap = new Flt64(double.PositiveInfinity) }.ValidateRuntime());
    }

    [Fact]
    public void GurobiBackendParametersAreSortedAndRedacted() {
        var backend = new GurobiSolverConfig(
            Server: "host",
            Password: "secret",
            ConnectionTime: TimeSpan.FromSeconds(5));
        Assert.Equal("gurobi", backend.Type);
        Assert.Equal(new[] { "connectionTime", "password", "server" }, Names(backend.Parameters()));
        Assert.Equal("<redacted>", Value(backend.Parameters(), "password"));
        Assert.True(backend.Parameters().Single(parameter => parameter.Name == "password").Sensitive);
        Assert.DoesNotContain("secret", string.Join(",", backend.Parameters().Select(parameter => parameter.RedactedValue())));
    }

    [Fact]
    public void EmptyGurobiBackendPublishesNothing() {
        Assert.Empty(new GurobiSolverConfig().Parameters());
        Assert.Equal("gurobi", new SolverConfig { BackendConfiguration = new GurobiSolverConfig() }.ConfigurationSnapshot().Type);
    }

    [Fact]
    public void CoptBackendParametersCarryPortAndRedaction() {
        var backend = new CoptSolverConfig(Server: "host", Port: 8701L, Password: "secret");
        Assert.Equal("copt", backend.Type);
        Assert.Equal(new[] { "password", "port", "server" }, Names(backend.Parameters()));
        Assert.Equal("8701", Value(backend.Parameters(), "port"));
        Assert.Equal("<redacted>", Value(backend.Parameters(), "password"));
    }

    [Fact]
    public void ScipBackendParametersMergeNativeParametersInOrder() {
        var backend = new ScipSolverConfig(
            Presolve: false,
            RandomSeed: 7L,
            Deterministic: true,
            NativeParameters: new Dictionary<string, BackendParameterValue> {
                ["limits/time"] = new BackendParameterValue.Text("10"),
                ["lp/algorithm"] = new BackendParameterValue.Text("1"),
            });
        Assert.Equal("scip", backend.Type);
        Assert.Equal(
            new[] { "deterministic", "limits/time", "lp/algorithm", "presolve", "randomSeed" },
            Names(backend.Parameters()));
        Assert.Equal("False", Value(backend.Parameters(), "presolve"));
        Assert.Equal("7", Value(backend.Parameters(), "randomSeed"));
        Assert.Empty(new ScipSolverConfig().NativeParameters);
    }

    [Fact]
    public void ConfigurationSnapshotIsSortedWithRedactedBackendPrefix() {
        var config = new SolverConfig {
            TimeLimit = TimeSpan.FromSeconds(60),
            ThreadCount = 4UL,
            Gap = new Flt64(0.01),
            ImproveThreshold = new Flt64(0.25),
            NotImprovementTime = TimeSpan.FromSeconds(5),
            InterruptibleTime = TimeSpan.FromSeconds(10),
            InterruptibleGap = new Flt64(0.5),
            BackendConfiguration = new GurobiSolverConfig(Server: "host", Password: "secret"),
        };
        BackendConfigurationSnapshot snapshot = config.ConfigurationSnapshot();
        Assert.Equal(new[] {
            "backend.password", "backend.server", "gap", "improveThreshold", "interruptibleGap",
            "interruptibleTime", "notImprovementTime", "threadNum", "time",
        }, Names(snapshot.Parameters));
        Assert.Equal("60", Value(snapshot.Parameters, "time"));
        Assert.Equal("4", Value(snapshot.Parameters, "threadNum"));
        Assert.Equal("0.01", Value(snapshot.Parameters, "gap"));
        Assert.Equal("0.25", Value(snapshot.Parameters, "improveThreshold"));
        Assert.Equal("<redacted>", Value(snapshot.Parameters, "backend.password"));
    }

    [Fact]
    public void ConfigurationSnapshotOmitsUnsetRuntimeFields() {
        BackendConfigurationSnapshot snapshot = new SolverConfig().ConfigurationSnapshot();
        Assert.Equal("solver", snapshot.Type);
        Assert.Equal(new[] { "gap", "improveThreshold", "threadNum", "time" }, Names(snapshot.Parameters));
    }

    [Fact]
    public void ProvenanceFallsBackToDescriptorVersionAndKeepsIgnoredParameters() {
        var config = new SolverConfig {
            ThreadCount = 8UL,
            BackendConfiguration = new ScipSolverConfig(RandomSeed: 11L, Deterministic: false),
        };
        SolverProvenance provenance = config.Provenance(
            _descriptor,
            ignoredParameters: new Dictionary<string, string> { ["parallel/maxnthreads"] = "not ported" });
        Assert.Equal("9.9.9", provenance.NativeVersion);
        Assert.Equal(8, provenance.ThreadCount);
        Assert.Equal(11L, provenance.RandomSeed);
        Assert.Equal(false, provenance.Deterministic);
        Assert.Equal("not ported", provenance.IgnoredParameters["parallel/maxnthreads"]);
        Assert.Equal("8", provenance.EffectiveParameters["threadNum"]);
        Assert.Equal("scip", provenance.Configuration!.Type);
        Assert.Equal("11", provenance.EffectiveParameters["backend.randomSeed"]);
    }

    [Fact]
    public void ProvenancePrefersAnExplicitNativeVersion() {
        SolverProvenance provenance = new SolverConfig { ThreadCount = 1UL }
            .Provenance(_descriptor, nativeVersion: "1.2.3");
        Assert.Equal("1.2.3", provenance.NativeVersion);
        Assert.Null(provenance.RandomSeed);
        Assert.Null(provenance.Deterministic);
        Assert.Empty(provenance.IgnoredParameters);
    }

    [Fact]
    public void CallbackGapUsesThePrecisionShiftedFormula() {
        double expected = (10d - 5d + _precision.ToDouble()) / (10d + _precision.ToDouble());
        Assert.Equal(expected, SolvingStatusSupport.Gap(new Flt64(10), new Flt64(5)).ToDouble(), 12);
    }

    [Fact]
    public void CallbackGapAtZeroStaysFinite() =>
        Assert.Equal(1d, SolvingStatusSupport.Gap(Flt64.Zero, Flt64.Zero).ToDouble(), 12);

    [Fact]
    public void MonitorStaysQuietWithoutANoImprovementTime() {
        var monitor = new NoImprovementMonitor(new SolverConfig());
        Assert.False(monitor.ShouldAbort(TimeSpan.Zero, new Flt64(10), new Flt64(0)));
        Assert.False(monitor.ShouldAbort(TimeSpan.FromHours(1), new Flt64(10), new Flt64(0)));
    }

    [Fact]
    public void MonitorAbortsOnlyAfterTheStaleWindow() {
        var monitor = new NoImprovementMonitor(new SolverConfig { NotImprovementTime = TimeSpan.FromSeconds(10) });
        Assert.False(monitor.ShouldAbort(TimeSpan.Zero, new Flt64(10), new Flt64(0)));
        Assert.False(monitor.ShouldAbort(TimeSpan.FromSeconds(9), new Flt64(10), new Flt64(0)));
        Assert.True(monitor.ShouldAbort(TimeSpan.FromSeconds(10), new Flt64(10), new Flt64(0)));
    }

    [Fact]
    public void MonitorRestartsTheWindowOnImprovementOrBoundMovement() {
        var config = new SolverConfig {
            NotImprovementTime = TimeSpan.FromSeconds(10),
            ImproveThreshold = new Flt64(0.5),
        };
        var monitor = new NoImprovementMonitor(config);
        Assert.False(monitor.ShouldAbort(TimeSpan.Zero, new Flt64(10), new Flt64(0)));
        Assert.False(monitor.ShouldAbort(TimeSpan.FromSeconds(4), new Flt64(10), new Flt64(0)));
        // 目标移动 1 ≥ 阈值，窗口从 5 秒重新计时 / a movement of 1 restarts the window at five seconds
        Assert.False(monitor.ShouldAbort(TimeSpan.FromSeconds(5), new Flt64(9), new Flt64(0)));
        Assert.False(monitor.ShouldAbort(TimeSpan.FromSeconds(9), new Flt64(9), new Flt64(0)));
        Assert.True(monitor.ShouldAbort(TimeSpan.FromSeconds(15), new Flt64(9), new Flt64(0)));
    }

    [Fact]
    public void InterruptibleTimeSuppressesAnEarlyAbort() {
        var config = new SolverConfig {
            NotImprovementTime = TimeSpan.FromSeconds(10),
            InterruptibleTime = TimeSpan.FromSeconds(100),
        };
        var monitor = new NoImprovementMonitor(config);
        Assert.False(monitor.ShouldAbort(TimeSpan.Zero, new Flt64(10), new Flt64(0)));
        Assert.False(monitor.ShouldAbort(TimeSpan.FromSeconds(50), new Flt64(10), new Flt64(0)));
        Assert.True(monitor.ShouldAbort(TimeSpan.FromSeconds(100), new Flt64(10), new Flt64(0)));
    }

    [Fact]
    public void WideInterruptibleGapSuppressesTheAbort() {
        var config = new SolverConfig {
            NotImprovementTime = TimeSpan.FromSeconds(10),
            InterruptibleGap = new Flt64(1),
        };
        var monitor = new NoImprovementMonitor(config);
        Assert.False(monitor.ShouldAbort(TimeSpan.Zero, new Flt64(10), new Flt64(0)));
        // 间隙仍宽于 1，继续求解 / the gap is still wider than one, so the solve continues
        Assert.False(monitor.ShouldAbort(TimeSpan.FromSeconds(30), new Flt64(10), new Flt64(0)));
        Assert.False(monitor.ShouldAbort(TimeSpan.FromSeconds(31), new Flt64(10), new Flt64(9.5)));
        Assert.True(monitor.ShouldAbort(TimeSpan.FromSeconds(41), new Flt64(10), new Flt64(9.5)));
    }

    private static void AssertIllegal(Result<Success, ErrorCode, Error<ErrorCode>> result) =>
        Assert.Equal(
            ErrorCode.IllegalArgument,
            Assert.IsType<Failed<Success, ErrorCode, Error<ErrorCode>>>(result).Code);

    private static string[] Names(IReadOnlyList<BackendParameter> parameters) =>
        parameters.Select(parameter => parameter.Name).ToArray();

    private static string Value(IReadOnlyList<BackendParameter> parameters, string name) =>
        parameters.Single(parameter => parameter.Name == name).RedactedValue();
}
