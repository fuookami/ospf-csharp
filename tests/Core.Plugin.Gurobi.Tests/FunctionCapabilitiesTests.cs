#nullable enable

using System;
using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Core.Plugin.Gurobi;
using Fuookami.Ospf.Core.Solver.Function;
using Xunit;

namespace Fuookami.Ospf.Core.Plugin.Gurobi.Tests;

/// <summary>
/// 能力探测**没有 SDK 就不声明任何能力** / With no SDK, detect claims nothing.
/// </summary>
/// <remarks>
/// 基线的探测器有两个可注入点（`modelClass` 与 SDK 常量），测试就靠这两个点验证"探测不到就什么都不说"。
/// 把同样的可注入点搬到.NET 侧：`Detect(modelType)` 可以喂一个假类，于是判据**不依赖本机装没装 Gurobi**——
/// 这一点很要紧，否则判据在 CI 上绿、在开发机上红，红绿都由环境决定。
///
/// The baseline's detector has two injectable points (`modelClass` and an SDK constant), and its tests use them to verify "cannot detect → say nothing". carries the
/// same injectable point over: `Detect(modelType)` takes a fake class, so the criteria **do not depend on whether this machine has Gurobi** — which matters, because
/// otherwise the criteria go green in CI and red on a developer machine, with the colour decided by the environment.
/// </remarks>
public sealed class GurobiFunctionCapabilitiesTests
{
    /// <summary>**没有 SDK 就一项都不声明** / Nothing is claimed with no SDK.</summary>
    /// <remarks>
    /// 这条判据喂 `null`——它**明确表示没有 SDK**，而不是"随便给我一个搜索"。
    /// 的第一版把"搜索程序集"和"判断某个类型"合成一个 `Detect`，于是这条路径**没有任何判据能单独走进去**：
    /// 变异把"没有 SDK"改成"声称支持 BinaryLogic"时，判据仍然绿。
    ///
    /// This criterion feeds `null`, which **explicitly means no SDK** rather than "please go searching". the earlier first version merged "search assemblies" into the same
    /// `Detect`, so **no criterion could enter this path alone**: a mutation turning "no SDK" into "claims BinaryLogic" left the criteria green.
    /// </remarks>
    [Fact]
    public void NoSdk_ShouldClaimNothing() {
        FunctionSolverCapabilities detected = GurobiFunctionCapabilities.Detect(null);

        detected.Supported.Should().BeEmpty("a solver whose SDK is absent must not claim a single native path");
        detected.Version.Should().BeNull("'not probed' and 'probed and empty' are different facts, and only null says the first one");
        detected.Solver.Should().Be(GurobiFunctionCapabilities.SolverName);
    }

    /// <summary>
    /// **`GeneralMinMax` 要两个方法都在才声明** / `GeneralMinMax` needs both methods before it is claimed.
    /// </summary>
    /// <remarks>
    /// 基线也是 `addGenConstrMax` 与 `addGenConstrMin` **两个都要**。只有一个时声明"支持"，会让上层去调不存在的那一半——
    /// 而调用那一刻的错误会出现在**另一端**，看起来像上层写错了。
    ///
    /// The baseline requires **both** `addGenConstrMax` and `addGenConstrMin`. Claiming support with only one lets the layer above call the missing half, and the error
    /// surfaces **at the other end**, looking like the caller's mistake.
    /// </remarks>
    [Fact]
    public void OnlyOneHalfOfTheMinMaxPair_ShouldNotClaimMinMax() {
        FunctionSolverCapabilities detected = GurobiFunctionCapabilities.Detect(typeof(OnlyMaxModel));

        detected.Supports(FunctionNativeCapability.GeneralMinMax).Should().BeFalse(
            "half a pair is not a capability; the missing half would fail at call time on the far side");
    }

    /// <summary>**一个方法可以支撑两种能力** / One method can back two capabilities.</summary>
    /// <remarks>
    /// `AddSOS` 的 `type` 参数决定 SOS1 还是 SOS2（基线的 `addSOS` 同理），所以它一出现就同时声明两项。
    ///
    /// `AddSOS`'s type argument picks SOS1 or SOS2 (the baseline's `addSOS` is the same), so its presence claims both.
    /// </remarks>
    [Fact]
    public void AnSosMethod_ShouldClaimBothTypes() {
        FunctionSolverCapabilities detected = GurobiFunctionCapabilities.Detect(typeof(FullModel));

        detected.Supports(FunctionNativeCapability.Sos1).Should().BeTrue();
        detected.Supports(FunctionNativeCapability.Sos2).Should().BeTrue();
    }

    /// <summary>**`Supports` 是纯集合查询，没有"默认支持"** / `Supports` is a plain lookup with no default.</summary>
    [Fact]
    public void Supports_ShouldNotInferAnything() {
        var claimed = new FunctionSolverCapabilities(
            "fake",
            "1.0",
            new HashSet<FunctionNativeCapability> { FunctionNativeCapability.Indicator });

        claimed.Supports(FunctionNativeCapability.Indicator).Should().BeTrue();
        claimed.Supports(FunctionNativeCapability.Sos1).Should().BeFalse(
            "unlisted means unsupported; any inference here would put a native path in front of a solver that cannot take it");
    }

    /// <summary>什么都不暴露的桩 / A stub that exposes no function interface.</summary>
    private sealed class NoSuchSdk { }

    /// <summary>只有 Max 一半的桩 / A stub with only half of the max/min pair.</summary>
    private sealed class OnlyMaxModel {
        /// <summary>一对里的上半 / The upper half of the pair.</summary>
        /// <returns>占位 / A placeholder.</returns>
        public object AddGenConstrMax() => new object();
    }

    /// <summary>暴露全部接口的桩 / A stub exposing every interface.</summary>
    private sealed class FullModel {
        /// <summary>占位 / A placeholder.</summary>
        /// <returns>占位 / A placeholder.</returns>
        public object SetPWLObj() => new object();

        /// <summary>占位 / A placeholder.</summary>
        /// <returns>占位 / A placeholder.</returns>
        public object AddGenConstrPWL() => new object();

        /// <summary>占位 / A placeholder.</summary>
        /// <returns>占位 / A placeholder.</returns>
        public object AddSOS() => new object();

        /// <summary>占位 / A placeholder.</summary>
        /// <returns>占位 / A placeholder.</returns>
        public object AddGenConstrIndicator() => new object();

        /// <summary>占位 / A placeholder.</summary>
        /// <returns>占位 / A placeholder.</returns>
        public object AddGenConstrAbs() => new object();

        /// <summary>占位 / A placeholder.</summary>
        /// <returns>占位 / A placeholder.</returns>
        public object AddGenConstrMax() => new object();

        /// <summary>占位 / A placeholder.</summary>
        /// <returns>占位 / A placeholder.</returns>
        public object AddGenConstrMin() => new object();

        /// <summary>占位 / A placeholder.</summary>
        /// <returns>占位 / A placeholder.</returns>
        public object AddGenConstrAnd() => new object();

        /// <summary>占位 / A placeholder.</summary>
        /// <returns>占位 / A placeholder.</returns>
        public object AddGenConstrOr() => new object();
    }
}
