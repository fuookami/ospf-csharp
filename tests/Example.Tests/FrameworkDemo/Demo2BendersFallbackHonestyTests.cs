#nullable enable

using System;
using System.IO;
using FluentAssertions;
using Xunit;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// demo2 的回退状态**不许声称一件没发生的事** / demo2's fallback status must not claim something that did not happen.
/// </summary>
/// <remarks>
/// `FullLoadApplication.SolveWithBenders` 在 Benders 失败且策略允许回退时，返回的状态是 `BendersFailed_MilpFallback`——
/// 而这条路径**根本没有 MILP 后端**：它没有跑过任何 MILP，只是"策略允许"。**状态名是一句假话。**
///
/// 框架的 `DiscreteLoadingApplicationService` 用 `milp_fallback_after_benders` 表示"回退**确实**跑了"，
/// 并且有判据钉住（`DiscreteLoadingBendersDispatchTests` 里桩会被真的调用）。两个路径用同一个字面值，
/// 就会让调用方无法区分"跑过 MILP"和"只是允许回退"。
///
/// In `FullLoadApplication.SolveWithBenders`, when Benders fails and the policy allows the fallback, the returned status was `BendersFailed_MilpFallback` — yet this path
/// **has no MILP backend at all**: it never ran a MILP, the policy merely allows one. **The status name is a lie.** The framework service uses
/// `milp_fallback_after_benders` for "the fallback **did** run" and has criteria pinning that (the stub is really called in `DiscreteLoadingBendersDispatchTests`);
/// sharing the same literal across both paths would leave a caller unable to tell "MILP ran" from "MILP was merely allowed".
/// </remarks>
/// <remarks>
/// ponytail: 这条判据扫**源码字面值**，不是行为——`SolveWithBenders` 是私有的、需要一个完整请求与模型，行为判据要一整台应用当夹具。
/// 升级路径：把 `SolveWithBenders` 的失败分支抽成一个可注入的决策函数，那时改成行为判据。
///
/// ponytail: this criterion scans **source literals**, not behaviour — `SolveWithBenders` is private and needs a whole request and model, so a behavioural criterion would
/// need the entire application as a fixture. Upgrade path: extract the failure branch into an injectable decision function and make it behavioural.
/// </remarks>
public sealed class Demo2BendersFallbackHonestyTests
{
    /// <summary>**不许声称回退跑过**，因为这条路径没有 MILP 后端 / It must not claim the fallback ran, because this path has no MILP backend.</summary>
    [Fact]
    public void TheFailureStatus_ShouldNotClaimTheFallbackRan() {
        string source = ApplicationSource();

        source.Should().NotContain("milp_fallback_after_benders",
            "that literal means the fallback really ran; this path has no MILP backend to run");
        source.Should().NotContain("NoSolution(\"BendersFailed_MilpFallback\"",
            "the old status claimed a fallback that never happened");
    }

    /// <summary>**必须说清回退没跑** / It must say the fallback did not run.</summary>
    [Fact]
    public void TheFailureStatus_ShouldSayTheFallbackDidNotRun() {
        string source = ApplicationSource();

        source.Should().Contain("benders_failed_without_milp_fallback",
            "the status must describe what this path actually did: fail, with no MILP to fall back to");
    }

    /// <summary>**`BendersFailed` 这条分支也要有人钉** / The `BendersFailed` branch needs a pin too.</summary>
    /// <remarks>
    /// **`BendersFailed` 是自造状态**（基线 `12fdb587` 里搜不到），而它**此前没有任何判据**：
    /// **判据只钉了 `benders_failed_without_milp_fallback` 那一条。**
    ///
    /// **两条分支的差别是有意义的**：一条是"策略允许回退但没有 MILP 可退"，
    /// **另一条是"回退也没成，彻底失败"**——**合并成一个状态，调用方就分不清"该补后端"还是"该看模型"。**
    ///
    /// **`BendersFailed` is an invented status** (absent from baseline `12fdb587`) **with no criterion at all** —
    /// **only `benders_failed_without_milp_fallback` was pinned.**
    ///
    /// **The distinction matters**: one says "the policy allows a fallback but there is no MILP to fall back to",
    /// **the other says "the fallback did not save it either"** — **merging them leaves the caller unable to tell "supply a backend" from "look at the model".**
    /// </remarks>
    [Fact]
    public void TheTerminalBendersFailure_ShouldBeDistinctFromTheMissingFallback() {
        string source = ApplicationSource();

        source.Should().Contain("NoSolution(\"BendersFailed\"",
            "the terminal failure keeps its own status, separate from the missing-fallback one");
        source.Should().Contain("NoSolution(\"benders_failed_without_milp_fallback\"",
            "and the missing-fallback status keeps its own");

        // **两条都在，且不是同一个字符串**——**否则"合并"会安静地通过。**
        // **Both present and not the same string** — **otherwise a merge would pass silently.**
        "BendersFailed".Should().NotBe("benders_failed_without_milp_fallback");
    }

    /// <summary>读 demo2 应用源码 / Read the demo2 application source.</summary>
    /// <returns>内容 / The content.</returns>
    private static string ApplicationSource() {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null) {
            string candidate = Path.Combine(
                directory.FullName, "src", "Example", "FrameworkDemo", "Demo2", "Application", "FullLoadApplication.cs");
            if (File.Exists(candidate)) {
                // **只看代码行**： 在注释里解释过这两个字面值，把注释算进来会让判据红在自己的说明上。
                // **Code lines only**: the earlier comments explain both literals, and counting them would red the criterion on its own explanation (the earlier lesson, again).
                var code = new System.Text.StringBuilder();
                foreach (string line in File.ReadAllLines(candidate)) {
                    if (line.TrimStart().StartsWith("//", StringComparison.Ordinal)) {
                        continue;
                    }
                    code.AppendLine(line);
                }
                return code.ToString();
            }
            directory = directory.Parent;
        }

        throw new FileNotFoundException("could not locate FullLoadApplication.cs");
    }

    /// <summary>
    /// **两种"没走回退"必须是两句话** / The two reasons not to fall back must be two sentences.
    /// </summary>
    /// <remarks>
    /// 基线要求回退**重建一个新模型与新上下文**；本仓的域上下文是**一次性**的。
    /// 所以"调用方没给 MILP 求解器"和"给了求解器也建不出新模型"是**两个不同的原因**，混成一句会让后来的人以为补个求解器就好了。
    ///
    /// The code requires the fallback to **rebuild a fresh model and context**; this implementation's domain contexts are **single-use** (measured). So "no MILP solver
    /// was supplied" and "a solver was supplied but no fresh model can be built" are **two different reasons**; merging them makes the next reader think supplying a
    /// solver is enough.
    /// </remarks>
    [Fact]
    public void TheTwoFallbackReasons_ShouldBeTwoDistinctSentences() {
        string code = ApplicationSource();

        code.Should().Contain("no MILP solver was supplied");
        code.Should().Contain("the fallback needs a fresh model and the domain contexts are single-use");
        code.Should().Contain("if (milpSolver is null)", "the two sentences must be selected by a real condition, not written both at once");
    }
}
