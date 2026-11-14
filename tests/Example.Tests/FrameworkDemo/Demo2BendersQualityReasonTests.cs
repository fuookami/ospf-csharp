using System;
using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Xunit;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// B830：质量原因的**优先级**与"弱间隙"这道闸 / The precedence of the quality reasons, and the weak-gap gate.
/// </summary>
/// <remarks>
/// `ResolveQualityReason` 里五个原因**是一个链条**，不是五个独立判断：
///
/// | 顺序 | 原因 | 自己的前置条件 |
/// | --- | --- | --- |
/// | 1 | `gap_guard_exceeded` | 间隙超过 `gapGuard` |
/// | 2 | `time_guard_exceeded` | 弱间隙 **且** 超时 |
/// | 3 | `progress_guard_triggered` | 弱间隙 **且** 迭代压力 |
/// | 4 | `trajectory_weak` | 弱间隙 **且** 快照够多 **且** 步长小 |
/// | 5 | `cut_efficiency_low` | 弱间隙 **且** 迭代够多 **且** 割密度低 |
///
/// 单原因测试证明"每条都会触发"，**证明不了顺序**：把四条都换成同一个返回值，或者把两个分支对调，单原因测试全绿。
/// 所以这里的判据**每条都构造两个原因同时成立**，断言**靠前的那个**胜出。
///
/// The five reasons in `ResolveQualityReason` are **a chain**, not five independent checks (table above). Single-cause tests prove each fires; they **cannot
/// prove the order** — returning the same value from four branches, or swapping two branches, keeps single-cause tests green. Every criterion here **makes two
/// reasons true at once** and asserts the earlier one wins.
/// </remarks>
public sealed class Demo2BendersQualityReasonTests
{
    // tolerance 1e-4 让 gapGuard = 1e-4 * 100 = 0.01，弱间隙门槛 = max(1e-4 * 20, 1e-5) = 2e-3。
    // With tolerance 1e-4, gapGuard = 0.01 and the weak-gap threshold is max(1e-4 * 20, 1e-5) = 2e-3.
    private static readonly EffectiveBendersAdaptiveConfig Adaptive =
        new(MinBinaryVariables: 4, MaxIterations: 100, Tolerance: 1e-4, MaxStallIterations: 10, ObjectiveStallIterations: 5);

    private static readonly BendersQualityGuardConfig Guard = new();

    // 时间守卫 = 100 * 40 + 10 * 30 = 4300 ms。
    // The time guard is 100 * 40 + 10 * 30 = 4300 ms.
    private const long OverTimeGuard = 5000;

    /// <summary>
    /// **间隙过大压过一切** / An over-large gap outranks everything.
    /// </summary>
    /// <remarks>
    /// 间隙 `0.02` 超过 `gapGuard`（0.01），同时时间也超守卫、迭代也到压力线、快照也停滞。**必须报间隙**——
    /// 其余四个都是"间隙已经弱"时才看的细节，间隙本身超了还去看细节，等于**先量了尺子再看尺子准不准**。
    ///
    /// The gap at `0.02` exceeds `gapGuard` (0.01) while the time is over the guard, the iteration pressure is on and the snapshots are stagnant. **The gap must be
    /// reported**: the other four are details you only look at *after* the gap is already weak.
    /// </remarks>
    [Fact]
    public void AnOverLargeGap_ShouldOutrankEveryOtherReason() {
        string? reason = BendersStrategy.ResolveQualityReason(
            Adaptive, Guard,
            bendersIterations: 95,
            bendersGap: 0.02,
            bendersTimeMs: OverTimeGuard,
            executedIterations: 95,
            totalCuts: 1,
            iterationSnapshots: new List<double> { 1.0, 1.0, 1.0, 1.0, 1.0, 1.0 });

        reason.Should().Be("gap_guard_exceeded");
    }

    /// <summary>
    /// **超时压过迭代压力** / Overtime outranks iteration pressure.
    /// </summary>
    /// <remarks>
    /// 间隙只到"弱"（未超守卫），时间超守卫，迭代也到压力线。基线先报时间：**时间是不会自己变好的那个**，
    /// 迭代还能再来一轮，时间不能。
    ///
    /// The gap is only "weak" (inside the guard), the time is over the guard and the iteration pressure is on. The baseline reports time first: **time is the one
    /// that does not improve by itself**.
    /// </remarks>
    [Fact]
    public void Overtime_ShouldOutrankIterationPressure() {
        string? reason = BendersStrategy.ResolveQualityReason(
            Adaptive, Guard,
            bendersIterations: 95,
            bendersGap: 0.005,
            bendersTimeMs: OverTimeGuard,
            executedIterations: 95);

        reason.Should().Be("time_guard_exceeded");
    }

    /// <summary>
    /// **迭代压力压过轨迹停滞** / Iteration pressure outranks a stagnant trajectory.
    /// </summary>
    /// <remarks>
    /// 迭代已到 `iterationPressurePercent`（95 ≥ 90），快照也确实停滞。基线报迭代压力：
    /// **"已经跑满预算"比"每步进步很小"更能解释结果**，前者是原因，后者是症状。
    ///
    /// Iterations have reached the pressure line (95 ≥ 90) and the snapshots really are stagnant. The baseline reports iteration pressure: **"the budget is
    /// exhausted" explains the outcome better than "each step improved little"** — the former is the cause, the latter the symptom.
    /// </remarks>
    [Fact]
    public void IterationPressure_ShouldOutrankAStagnantTrajectory() {
        string? reason = BendersStrategy.ResolveQualityReason(
            Adaptive, Guard,
            bendersIterations: 95,
            bendersGap: 0.005,
            bendersTimeMs: 0,
            executedIterations: 95,
            totalCuts: 90,
            iterationSnapshots: new List<double> { 1.0, 1.0, 1.0, 1.0, 1.0, 1.0 });

        reason.Should().Be("progress_guard_triggered");
    }

    /// <summary>
    /// **轨迹停滞压过割效率低** / A stagnant trajectory outranks low cut efficiency.
    /// </summary>
    /// <remarks>
    /// 迭代没到压力线（20 / 100），快照停滞，且割密度也低（1/20 = 0.05 < 0.25）。基线报轨迹：
    /// **轨迹说的是"主问题目标已经不动了"，割密度说的是"割产出少"** —— 而目标不动更接近结论。
    ///
    /// Iterations did not reach the pressure line (20 / 100), the snapshots are stagnant and the cut density is low too (1/20 = 0.05 < 0.25). The baseline reports
    /// the trajectory: **"the master objective has stopped moving" is closer to the conclusion than "cuts are scarce."**
    /// </remarks>
    [Fact]
    public void AStagnantTrajectory_ShouldOutrankLowCutEfficiency() {
        string? reason = BendersStrategy.ResolveQualityReason(
            Adaptive, Guard,
            bendersIterations: 20,
            bendersGap: 0.005,
            bendersTimeMs: 0,
            executedIterations: 20,
            totalCuts: 1,
            iterationSnapshots: new List<double> { 1.0, 1.0, 1.0, 1.0, 1.0, 1.0 });

        reason.Should().Be("trajectory_weak");
    }

    /// <summary>
    /// **割效率是最后一环** / Cut efficiency is the last link.
    /// </summary>
    /// <remarks>
    /// 没有快照、迭代没到压力线、时间没超，只剩"割密度低"——这时才报它。
    /// **这条存在是为了证明前面四条不是"全都报"**：如果实现里前四条被写成了无条件返回，这条会拿到 `trajectory_weak` 之类。
    ///
    /// No snapshots, iterations below the pressure line, time inside the guard — only the low cut density is left, and now it is reported. **This criterion
    /// exists to prove the first four are not unconditional**: if they were, this would come back as something other than the cut reason.
    /// </remarks>
    [Fact]
    public void LowCutEfficiency_ShouldBeReportedWhenNothingElseFires() {
        string? reason = BendersStrategy.ResolveQualityReason(
            Adaptive, Guard,
            bendersIterations: 20,
            bendersGap: 0.005,
            bendersTimeMs: 0,
            executedIterations: 20,
            totalCuts: 1);

        reason.Should().Be("cut_efficiency_low");
    }

    /// <summary>
    /// **间隙不弱 → 其余四个一起哑火** / A tight gap silences all four of the others.
    /// </summary>
    /// <remarks>
    /// 这是**整条链最关键的一道闸**：间隙 `0.001` 低于弱门槛 `2e-3`，其余信号全部"很差"（时间超、迭代到压力线、割密度极低、快照完全停滞）。
    /// 基线返回 `null`——**质量可接受**。
    ///
    /// 这道闸是"守卫会自己关掉自己"的唯一入口。漏了它，任何一个长跑都会因为"时间超了"被判为质量不足，
    /// **而症状是"Benders 一启用就被判质量差"**——读者会去调阈值，而不是去查这道闸。
    ///
    /// This is **the most important gate in the whole chain**: the gap at `0.001` is below the weak threshold of `2e-3` while every other signal is bad
    /// (overtime, iteration pressure, almost no cuts, completely stagnant snapshots). The baseline returns `null` — quality is acceptable. This gate is the only
    /// way the guard switches itself off; miss it and any long run is judged poor **because it took time**, with the symptom being "Benders is always judged bad
    /// the moment it starts" — the reader will go tune thresholds instead of looking at the gate.
    /// </remarks>
    [Fact]
    public void ATightGap_ShouldSilenceTheOtherFourReasons() {
        string? reason = BendersStrategy.ResolveQualityReason(
            Adaptive, Guard,
            bendersIterations: 95,
            bendersGap: 0.001,
            bendersTimeMs: OverTimeGuard,
            executedIterations: 95,
            totalCuts: 0,
            iterationSnapshots: new List<double> { 1.0, 1.0, 1.0, 1.0, 1.0, 1.0 });

        reason.Should().BeNull("a tight gap means acceptable quality, whatever the other signals say");
    }

    /// <summary>
    /// **一项风险都不冒时评分正好是 0，五项全冒时正好是 100** / All risks at zero scores exactly 0; all five saturated scores exactly 100.
    /// </summary>
    /// <remarks>
    /// 这两条替代了原来的 `> = 0` 与 `< = 100`（B830）。那两句在 `Clamp(0, 100)` 之后**恒真**，等于只证明了"函数返回了个 double"。
    /// 换成"正好 0 / 正好 100"就有内容了：**要让 100 落地，五个风险必须各自饱和到 1，而且五个权重必须和为 1**——
    /// 漏掉任何一项、或者某个权重没归一化，都到不了 100。
    ///
    /// These two replace the former `>= 0` and `<= 100` (B830). After `Clamp(0, 100)` both held **unconditionally**, proving only that the function returns a
    /// double. "Exactly 0 / exactly 100" has content: **hitting 100 requires all five risks to saturate at 1 and all five weights to sum to 1** — drop a term or leave
    /// a weight unnormalised and 100 becomes unreachable.
    /// </remarks>
    [Fact]
    public void AZeroRiskRun_ShouldScoreExactlyZero() {
        double score = BendersStrategy.ResolveQualityScore(
            Adaptive, Guard,
            bendersIterations: 0, bendersGap: 0.0, bendersTimeMs: 0);

        score.Should().Be(0.0);
    }

    [Fact]
    public void ASaturatedRiskRun_ShouldScoreExactlyOneHundred() {
        double score = BendersStrategy.ResolveQualityScore(
            Adaptive, Guard,
            bendersIterations: Adaptive.MaxIterations,
            bendersGap: 0.02,
            bendersTimeMs: 6000,
            executedIterations: Adaptive.MaxIterations,
            totalCuts: 0,
            iterationSnapshots: new List<double> { 1.0, 1.0, 1.0, 1.0, 1.0, 1.0 });

        score.Should().Be(100.0, "every one of the five risks is saturated, and the weights sum to one");
    }

    /// <summary>
    /// **间隙守卫本身有上下界** / The gap guard itself is bounded.
    /// </summary>
    /// <remarks>
    /// `gapGuard = (tolerance * 100).coerceIn(1e-4, 0.2)`。两端都要钉：
    /// 容差很大（`1e-2` → 1.0）时夹到 `0.2`，否则**稍大的间隙会被判超守卫**；
    /// 容差很小时夹到 `1e-4`，否则**守卫变得形同虚设**（`1e-7 * 100 = 1e-5`，任何实质间隙都超）。
    ///
    /// Both ends are pinned: a large tolerance (`1e-2` → 1.0) clamps to `0.2`, otherwise slightly large gaps report exceeding the guard; a tiny tolerance clamps
    /// to `1e-4`, otherwise the guard becomes vacuous (`1e-7 * 100 = 1e-5`, which any real gap exceeds).
    /// </remarks>
    [Theory]
    [InlineData(1e-2, 0.2)]
    [InlineData(1e-3, 0.1)]
    [InlineData(1e-7, 1e-4)]
    public void TheGapGuard_ShouldStayWithinItsBounds(double tolerance, double expected) {
        double gapGuard = BendersStrategy.ResolveGapGuard(new EffectiveBendersAdaptiveConfig(
            MinBinaryVariables: 4, MaxIterations: 10, Tolerance: tolerance));

        gapGuard.Should().BeApproximately(expected, 1e-12);
    }
}
