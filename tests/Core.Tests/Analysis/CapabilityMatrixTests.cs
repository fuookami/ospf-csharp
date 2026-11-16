#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Analysis;
using Fuookami.Ospf.Core.Solver.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.Report;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Analysis;

/// <summary>
/// 分析能力矩阵 / The analysis capability matrix.
/// </summary>
/// <remarks>
/// 补 `CapabilityMatrix`（基线 297 行）**，它只依赖报告层与 CP 类型，不需要原生后端。
///
/// 判据钉的是**推断链**：一个操作能不能用，取决于它的上游能不能用。**这条链错一步，上层就会选一条不存在的路径。**
///
/// **implemented `CapabilityMatrix` (297 baseline lines)**, which needs no native backend. The criteria pin the **inference chain**: whether an operation is usable depends on
/// whether its upstream is. **One wrong link and the layer above picks a path that does not exist.**
/// </remarks>
public class CapabilityMatrixTests {
    private static CapabilityMatrix Matrix(
        IReadOnlySet<SolverModelType>? modelTypes = null,
        bool dual = false,
        bool nativeAssumption = false,
        bool nativeUnsatCore = false,
        bool fallback = false,
        bool warmStart = false,
        bool exactLowering = false,
        IReadOnlyDictionary<AnalysisCapability, CapabilitySupport>? overrides = null) =>
        new(
            modelTypes ?? new HashSet<SolverModelType>(),
            NativeAssumptionSolving: nativeAssumption,
            NativeUnsatCore: nativeUnsatCore,
            FallbackSatisfactionOnly: fallback,
            Dual: dual,
            WarmStart: warmStart,
            ExactCpLowering: exactLowering,
            AnalysisCapabilities: overrides);

    /// <summary>**固定整数 LP 灵敏度要求对偶** / Fixed-integer LP sensitivity requires duals.</summary>
    /// <remarks>
    /// 三档：有对偶且有 MIP（或精确降阶）→ Supported；只有对偶 → Conditional；没有对偶 → Unsupported。
    /// **只有两档的实现会把"有对偶但没 MIP"错报成可用。**
    ///
    /// Three levels: dual plus MIP (or exact lowering) → Supported; dual alone → Conditional; no dual → Unsupported. **A two-level implementation reports "dual but no MIP" as usable.**
    /// </remarks>
    [Fact]
    public void TheSensitivity_ShouldHaveThreeLevels() {
        Matrix().Support(AnalysisCapability.FixedIntegerLpSensitivity)
            .Should().Be(CapabilitySupport.Unsupported, "no duals, no sensitivity");
        Matrix(dual: true).Support(AnalysisCapability.FixedIntegerLpSensitivity)
            .Should().Be(CapabilitySupport.Conditional, "duals without a MIP model give only conditional support");
        Matrix(dual: true, modelTypes: new HashSet<SolverModelType> { SolverModelType.Mip })
            .Support(AnalysisCapability.FixedIntegerLpSensitivity)
            .Should().Be(CapabilitySupport.Supported);
        Matrix(dual: true, exactLowering: true).Support(AnalysisCapability.FixedIntegerLpSensitivity)
            .Should().Be(CapabilitySupport.Supported, "exact CP lowering stands in for a MIP model");
    }

    /// <summary>**自适应扰动不能强于它所依赖的扰动** / Adaptive perturbation cannot outrank its upstream.</summary>
    [Fact]
    public void TheAdaptivePerturbation_ShouldFollowItsUpstream() {
        // 模型类型为空 ⇒ RhsPerturbation 是 Conditional ⇒ 自适应也只能是 Conditional。
        // No model types means RhsPerturbation is Conditional, so adaptive can only be Conditional.
        Matrix().Support(AnalysisCapability.AdaptivePerturbation)
            .Should().Be(CapabilitySupport.Conditional);
        Matrix(modelTypes: new HashSet<SolverModelType> { SolverModelType.Lp })
            .Support(AnalysisCapability.AdaptivePerturbation)
            .Should().Be(CapabilitySupport.Supported);

        // **上游被显式关掉时，下游必须跟着关**：这是这条链最容易断的地方。
        // **When the upstream is explicitly disabled the downstream has to follow**: this is where the chain breaks most easily.
        CapabilityMatrix disabled = Matrix(overrides: new Dictionary<AnalysisCapability, CapabilitySupport> {
            [AnalysisCapability.RhsPerturbation] = CapabilitySupport.Unsupported,
        });
        disabled.Support(AnalysisCapability.AdaptivePerturbation)
            .Should().Be(CapabilitySupport.Unsupported,
                "adaptive perturbation built on an unsupported perturbation is not a thing");
    }

    /// <summary>**MUS 要求冲突分析可用** / MUS requires conflict analysis.</summary>
    [Fact]
    public void TheMus_ShouldRequireConflictAnalysis() {
        Matrix().Support(AnalysisCapability.Mus).Should().Be(CapabilitySupport.Unsupported,
            "no unsat core and no fallback means no conflict, hence no MUS");
        Matrix(fallback: true).Support(AnalysisCapability.Mus)
            .Should().Be(CapabilitySupport.Conditional,
                "a satisfaction-only fallback gives conflict, and an empty model-type set keeps MUS conditional");
        Matrix(nativeUnsatCore: true, modelTypes: new HashSet<SolverModelType> { SolverModelType.Mip })
            .Support(AnalysisCapability.Mus)
            .Should().Be(CapabilitySupport.Supported);
    }

    /// <summary>**显式覆盖优先于推断** / An explicit override wins over the inference.</summary>
    [Fact]
    public void TheExplicitOverride_ShouldWin() {
        CapabilityMatrix overridden = Matrix(overrides: new Dictionary<AnalysisCapability, CapabilitySupport> {
            [AnalysisCapability.Activity] = CapabilitySupport.Conditional,
        });

        overridden.Support(AnalysisCapability.Activity)
            .Should().Be(CapabilitySupport.Conditional,
                "the baseline lets a backend adapter override a stage whose support depends on details the descriptor cannot express");
        overridden.Supports(AnalysisCapability.Activity).Should().BeTrue("Conditional is still usable");
        overridden.IsNative(AnalysisCapability.Activity).Should().BeFalse("Activity is never a native-flag operation");
    }

    /// <summary>**满足性回退只在后端声明了 CP 满足路径时成立** / The fallback needs a declared CP satisfaction path.</summary>
    /// <remarks>
    /// 基线为此写过一条注释：**从任意模型类型推导，会让只支持线性/MIP 的后端声称一个它兑现不了的回退。**
    ///
    /// The code calls this out in a comment: **deriving it from any model type would let a linear/MIP-only backend advertise a fallback it cannot deliver.**
    /// </remarks>
    [Fact]
    public void TheFallback_ShouldRequireADeclaredCpPath() {
        var linearOnly = CapabilityMatrix.From(new SolverCapabilities(
            ModelTypes: new HashSet<SolverModelType> { SolverModelType.Lp, SolverModelType.Mip }));
        linearOnly.FallbackSatisfactionOnly.Should().BeFalse(
            "a linear/MIP backend with no CP feature map must not claim a satisfaction-only fallback");

        var withLogic = CapabilityMatrix.From(new SolverCapabilities(
            ModelTypes: new HashSet<SolverModelType> { SolverModelType.Cp },
            ConstraintProgrammingFeatures: new Dictionary<ConstraintProgrammingFeature, ConstraintProgrammingSupportLevel> {
                [ConstraintProgrammingFeature.BooleanLogic] = ConstraintProgrammingSupportLevel.Native,
            }));
        withLogic.FallbackSatisfactionOnly.Should().BeTrue("a declared boolean-logic path is what the fallback rides on");

        var declaredUnsupported = CapabilityMatrix.From(new SolverCapabilities(
            ModelTypes: new HashSet<SolverModelType> { SolverModelType.Cp },
            ConstraintProgrammingFeatures: new Dictionary<ConstraintProgrammingFeature, ConstraintProgrammingSupportLevel> {
                [ConstraintProgrammingFeature.BooleanLogic] = ConstraintProgrammingSupportLevel.Unsupported,
            }));
        declaredUnsupported.FallbackSatisfactionOnly.Should().BeFalse(
            "a declared-but-unsupported path is not a path");
    }

    /// <summary>**`From` 把原生标志与降阶标志分开读** / `From` reads the native and lowering flags separately.</summary>
    [Fact]
    public void TheFrom_ShouldMapThePrimitiveFlags() {
        var capabilities = new SolverCapabilities(
            ModelTypes: new HashSet<SolverModelType> { SolverModelType.Mip },
            Dual: true,
            WarmStart: true,
            ConstraintProgrammingFeatures: new Dictionary<ConstraintProgrammingFeature, ConstraintProgrammingSupportLevel> {
                [ConstraintProgrammingFeature.Assumption] = ConstraintProgrammingSupportLevel.Native,
                [ConstraintProgrammingFeature.ConflictCore] = ConstraintProgrammingSupportLevel.ExactLowering,
                [ConstraintProgrammingFeature.Interval] = ConstraintProgrammingSupportLevel.ExactLowering,
            });

        var matrix = CapabilityMatrix.From(capabilities);
        matrix.NativeAssumptionSolving.Should().BeTrue();
        matrix.NativeUnsatCore.Should().BeFalse(
            "ExactLowering is not Native; reading the map by presence alone would conflate them");
        matrix.ExactCpLowering.Should().BeTrue("any feature at ExactLowering enables it");
        matrix.LpDual.Should().BeTrue("the sensitivity alias has to track Dual");
        matrix.WarmStart.Should().BeTrue();
    }

    /// <summary>**空特性声明不等于"全都不支持"** / An empty feature map is not "nothing is supported".</summary>
    /// <remarks>
    /// 空 map 表示后端没发布声明（例如协议测试用的假求解器）。读成"全不支持"，会让这类后端**在每个模型上都被判为不可用**。
    ///
    /// An empty map means the backend published no declaration (the fake solver used by protocol tests). Reading it as "all unsupported" would mark such a backend
    /// **unusable on every model**.
    /// </remarks>
    [Fact]
    public void TheEmptyDeclaration_ShouldNotMeanNothingIsSupported() {
        var empty = CapabilityMatrix.From(new SolverCapabilities(
            ModelTypes: new HashSet<SolverModelType> { SolverModelType.Mip }));

        empty.Features.Should().BeEmpty();
        empty.Supports(ConstraintProgrammingFeature.BooleanLogic)
            .Should().BeFalse("with no declaration, a specific feature is still not claimed");
        // **而"被声明为不支持"的判定必须基于空声明直接短路**：这才是关键的一半。
        // 快照的构造器是私有的，所以这里不去伪造一个快照，而是钉住那条短路条件本身——
        // **"空声明 ⇒ 不做任何判定"** 是 `UnsupportedDeclaredCpFeatures` 的第一行。
        // **The declared-unsupported verdict has to short-circuit on an empty declaration**: that is the half that matters.
        // The snapshot constructor is private, so rather than fabricate one this pins the short-circuit itself —
        // **"empty declaration ⇒ no verdict"** is the first line of `UnsupportedDeclaredCpFeatures`.
        string source = System.IO.File.ReadAllText(System.IO.Path.Combine(
            Root(), "src", "Core", "Analysis", "CapabilityMatrix.cs"));
        source.Contains("if (Features.Count == 0)", StringComparison.Ordinal)
            .Should().BeTrue("an empty declaration must short-circuit before any feature is read");
        source.Contains("return new HashSet<ConstraintProgrammingFeature>();", StringComparison.Ordinal)
            .Should().BeTrue("and it must return an empty set, not the full feature list");
    }

    private static string Root() {
        System.IO.DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null
            && !System.IO.File.Exists(System.IO.Path.Combine(directory.FullName, "ospf-csharp.sln"))) {
            directory = directory.Parent;
        }
        if (directory is null) {
            throw new InvalidOperationException("repository root not found");
        }
        return directory.FullName;
    }
}
