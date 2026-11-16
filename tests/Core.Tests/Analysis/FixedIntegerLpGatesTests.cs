#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Analysis;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Math.Algebra.Number;
using System;
using System.Collections.Generic;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Analysis;

/// <summary>
/// 固定整数 LP 的门控 / The fixed-integer LP gates.
/// </summary>
/// <remarks>
/// **本文件曾被一次脚本中断截断过（脚本中断把文件写成 0 行），随后按源码 API 完整重写。**
/// 重写时补了一条原来没有的判据：**`AssembleSensitivities` 此前没有任何调用方，
/// 也没有任何判据**——`LocalConstraintSensitivity` 这个类型存在但从来没有被构造过。
///
/// **This file was truncated to zero lines by my own interrupted script, then rewritten against the source API.** The rewrite added a criterion that did not exist before:
/// **`AssembleSensitivities` had no caller and no criterion** — the `LocalConstraintSensitivity` type existed but was never constructed.
/// </remarks>
public class FixedIntegerLpGatesTests {
    private static readonly ConstraintId C1 = new("constraint:c1");
    private static readonly ConstraintId C2 = new("constraint:c2");

    /// <summary>**证明门控的三态** / The three states of the proof gate.</summary>
    /// <remarks>
    /// **中间那一态最容易写错**：带时间上限的求解器几乎总能返回点什么，
    /// **而"返回了东西"和"证明了最优"隔着整个搜索空间。**
    ///
    /// **The middle state is the easiest to get wrong**: a time-limited solver almost always returns something, **and "returned something" is a whole search space away from "proved optimal".**
    /// </remarks>
    [Fact]
    public void TheProofGate_ShouldSeparateIncumbentFromProof() {
        FixedIntegerLpGates.ProofStatus(provenOptimal: true, hasSolution: true)
            .Should().Be(AnalysisStatus.Reachable);

        FixedIntegerLpGates.ProofStatus(provenOptimal: false, hasSolution: true)
            .Should().Be(AnalysisStatus.Unknown, "an unproven incumbent is not a proof");

        FixedIntegerLpGates.ProofStatus(provenOptimal: false, hasSolution: false)
            .Should().Be(AnalysisStatus.Unreachable);
    }

    /// <summary>**证明与 incumbent 都没有时不得报可达** / Neither a proof nor an incumbent is not reachable.</summary>
    /// <remarks>
    /// **反向对照**：只测上面三行会让"恒返回 `Reachable`"通过其中一行之外的部分，
    /// 所以这里再钉一次"两个都没有"。
    ///
    /// **The reverse control**: the three rows above would let a constant `Reachable` pass all but one, so the "neither" case is pinned again here.
    /// </remarks>
    [Fact]
    public void TheProofGate_ShouldNotClaimReachableWithoutEither() {
        FixedIntegerLpGates.ProofStatus(provenOptimal: false, hasSolution: false)
            .Should().NotBe(AnalysisStatus.Reachable, "nothing was proved and nothing was found");
    }

    /// <summary>**对偶失败是 `Unsupported`，不是 `Unknown`** / A dual failure is Unsupported, not Unknown.</summary>
    /// <remarks>
    /// 这不是"这次没算出来"，而是**"这条路径根本给不出证据"**。**两者的区别决定了调用方是重试还是放弃。**
    ///
    /// This is not "it did not come out this time" but **"this path cannot produce evidence at all"**. **The difference decides whether the caller retries or gives up.**
    /// </remarks>
    [Fact]
    public void TheDualFailure_ShouldBeUnsupported() {
        var objective = new Flt64(3.0);
        FixedIntegerLpSolveResult result = FixedIntegerLpGates.DualFailed(objective, ["no duals"]);

        result.Status.Should().Be(AnalysisStatus.Unsupported);
        result.ObjectiveValue.Should().NotBeNull("the objective is still known even when the duals are not");
        result.Message.Should().Contain("no duals", "the caller needs the backend's own words");

        FixedIntegerLpGates.DualFailed(objective, []).Message.Should().Contain("unknown",
            "an empty failure list still has to say something rather than end with a colon");
    }

    /// <summary>**有约束却没对偶要拒绝；没约束时不拒绝** / Refuse when constraints lack duals, but not when there are none.</summary>
    /// <remarks>
    /// **空对偶会被读成"敏感度为零"**，而真相是**"后端没告诉我们"**。
    /// **模型里没有约束时这条不适用——那时"零敏感度"是正确答案。**
    ///
    /// **Empty duals read as "zero sensitivity"** while the truth is **"the backend told us nothing"**. **It does not apply when the model has no constraints — there "zero sensitivity" is correct.**
    /// </remarks>
    [Fact]
    public void TheEmptyDuals_ShouldBeRefusedOnlyWhenConstraintsExist() {
        FixedIntegerLpGates.NoMappableDuals(constraintCount: 3, mappableDualCount: 0, objective: null)
            .Should().NotBeNull("three constraints with no duals is a missing measurement, not a zero one");

        FixedIntegerLpGates.NoMappableDuals(constraintCount: 0, mappableDualCount: 0, objective: null)
            .Should().BeNull("with no constraints, zero sensitivity is the correct answer");

        FixedIntegerLpGates.NoMappableDuals(constraintCount: 3, mappableDualCount: 2, objective: null)
            .Should().BeNull("some duals came back, so this gate does not apply");
    }

    /// <summary>**多行降阶不得冒充单个约束的范围** / A multi-row lowering must not impersonate one constraint's range.</summary>
    /// <remarks>
    /// **直接转发到 `ProvenanceDualMapping.MapRanges`**，规则与前面一致完全一致。
    ///
    /// **Forwards to `ProvenanceDualMapping.MapRanges`**, with the rule ported.
    /// </remarks>
    [Fact]
    public void TheSingleRowRemap_ShouldDropMultiRowLowerings() {
        var unique = new ConstraintId("constraint:unique");
        var duplicated = new ConstraintId("constraint:duplicated");
        var range = new SensitivityRange(new Flt64(-1.0), new Flt64(1.0));

        IReadOnlyDictionary<ConstraintId, SensitivityRange> mapped = FixedIntegerLpGates.RemapSingleRowRanges(
            rowProvenance: [unique, duplicated, duplicated],
            rowOrigins: ["row:a", "row:b", "row:c"],
            rangesByOrigin: new Dictionary<string, SensitivityRange> {
                ["row:a"] = range,
                ["row:b"] = range,
                ["row:c"] = range,
            },
            originalIds: new HashSet<ConstraintId> { unique, duplicated });

        mapped.Should().ContainKey(unique, "one row lowered to one constraint is a real range");
        mapped.Should().NotContainKey(duplicated,
            "two rows lowering to one constraint make the range a statement about neither");
    }

    /// <summary>**没有对偶就是 `null`，不是 `false`** / No dual is null, not false.</summary>
    /// <remarks>
    /// 无对偶意味着**后端没给出这个数**；`false` 意味着**给出了，且它接近零**。
    /// **把前者写成后者，报告会说"这条约束不影响结果"——一个没测过的结论。**
    ///
    /// No dual means **the backend did not give the number**; `false` means **it did, and it is near zero**. **Writing the first as the second makes the report say "this constraint does not matter" — a
    /// conclusion nobody measured.**
    /// </remarks>
    [Fact]
    public void TheMissingDual_ShouldLeaveLocalEffectiveNull() {
        var solved = new FixedIntegerLpSolveResult(AnalysisStatus.Reachable);

        IReadOnlyList<LocalConstraintSensitivity> sensitivities = FixedIntegerLpGates.AssembleSensitivities(
            [C1], solved, [null], dualTolerance: 1e-9);

        sensitivities.Should().HaveCount(1);
        sensitivities[0].ConstraintId.Should().Be(C1);
        sensitivities[0].DualValue.Should().BeNull("the backend did not supply one");
        sensitivities[0].LocalEffective.Should().BeNull(
            "no dual means no verdict, and false would be the verdict \"it does not matter\"");
        sensitivities[0].Scope.Should().Be(LocalSensitivityScope.FixedIntegerIncumbent);
    }

    /// <summary>**有对偶时按阈值判定，两侧都要** / With a dual the threshold decides, both sides.</summary>
    /// <remarks>
    /// **反向对照**：只测"大对偶 → `true`"会让"恒为 `true`"通过。
    ///
    /// **The reverse control**: testing only "large dual → true" lets "always true" pass.
    /// </remarks>
    [Fact]
    public void ThePresentDual_ShouldBeJudgedByTheThreshold() {
        var solved = new FixedIntegerLpSolveResult(
            AnalysisStatus.Reachable,
            Duals: new Dictionary<ConstraintId, Flt64?> {
                [C1] = new Flt64(2.5),
                [C2] = new Flt64(1e-12),
            });

        IReadOnlyList<LocalConstraintSensitivity> sensitivities = FixedIntegerLpGates.AssembleSensitivities(
            [C1, C2], solved, [null, null], dualTolerance: 1e-9);

        sensitivities[0].LocalEffective.Should().BeTrue("2.5 is well above the tolerance");
        sensitivities[1].LocalEffective.Should().BeFalse(
            "a dual at 1e-12 is a measured near-zero, which is a verdict, not an absence");
    }

    /// <summary>**长度不一致必须抛，不能错位** / A length mismatch throws instead of misaligning.</summary>
    /// <remarks>
    /// **错位的活跃度会让每一条约束都挂上邻居的证据**——这种错误不抛异常，只会让报告安静地错。
    ///
    /// **Misaligned activities attach the neighbour's evidence to every constraint** — an error that throws nothing and simply makes the report quietly wrong.
    /// </remarks>
    [Fact]
    public void TheMismatchedLengths_ShouldThrow() {
        var solved = new FixedIntegerLpSolveResult(AnalysisStatus.Reachable);

        Action mismatched = () => FixedIntegerLpGates.AssembleSensitivities(
            [C1, C2], solved, [null], dualTolerance: 1e-9);

        mismatched.Should().Throw<ArgumentException>("a misaligned pair is worse than a refusal");
    }

    /// <summary>**求不出固定值时报 `Unknown`，不是报后端不支持** / No fixed values is Unknown, not Unsupported.</summary>
    /// <remarks>
    /// **顺序不能换。** 先判能力门会把"这个模型求不出固定值"报成"后端不支持"，
    /// **于是调用方去换一个根本不是问题的后端。**
    ///
    /// **The order cannot change.** Capability first reports "this model has no fixed values" as "the backend is unsupported", **sending the caller to swap a backend that was never the problem.**
    /// </remarks>
    [Fact]
    public void TheMissingFixedValues_ShouldBeUnknownNotUnsupported() {
        FixedIntegerLpGates.Preflight(fixedValuesAvailable: false, backendAvailable: false)
            .Should().Be(AnalysisStatus.Unknown,
                "a model with no fixed values is not a statement about the backend");

        FixedIntegerLpGates.Preflight(fixedValuesAvailable: false, backendAvailable: true)
            .Should().Be(AnalysisStatus.Unknown, "the missing fixed values are the reason either way");
    }

    /// <summary>**有固定值、没后端才是 `Unsupported`** / Fixed values plus no backend is Unsupported.</summary>
    /// <remarks>**反向对照**：只测上一条会让"恒返回 `Unknown`"通过。</remarks>
    [Fact]
    public void TheMissingBackend_ShouldBeUnsupported() {
        FixedIntegerLpGates.Preflight(fixedValuesAvailable: true, backendAvailable: false)
            .Should().Be(AnalysisStatus.Unsupported, "this route genuinely needs a dual backend");
    }

    /// <summary>**两道门都通过时放行** / Both gates passing lets the pipeline continue.</summary>
    /// <remarks>
    /// **`null` 不是"没问题"，是"继续"。** 用 `Reachable` 代替会让下游把它当成一个真实结论。
    ///
    /// **`null` means "continue", not "fine".** Using `Reachable` instead would let downstream treat it as a real conclusion.
    /// </remarks>
    [Fact]
    public void TheClearedPreflight_ShouldReturnNull() {
        FixedIntegerLpGates.Preflight(fixedValuesAvailable: true, backendAvailable: true)
            .Should().BeNull("passing the gates is not a status, it is permission to keep going");
    }
}
