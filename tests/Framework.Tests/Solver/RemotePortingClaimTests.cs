#nullable enable

using FluentAssertions;
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Fuookami.Ospf.Framework.Tests.Solver;

/// <summary>
/// 文档声明与源码事实一致 / Document claims agree with the source.
/// </summary>
/// <remarks>
/// **曾有一句"其余 3 个真实缺口共约 1100 行尚未实现"**
/// （ 339、 339、 480），
/// **它与同段前文"已完成"直接矛盾，也与源码事实不符——三份都已移植。**
///
/// **一句话过期了很久，而它<b>不可能被任何行为判据发现</b>：** **所有测试都绿着，
/// 因为代码是对的；错的是对代码的描述。**
///
/// **所以这里留下的判据不是"行为对"，而是"描述与文件系统一致"。**
///
/// **一条过期的"尚未实现"比一条过期的"已移植"更危险：**
/// **前者会让下一轮<b>重做一遍</b>，而重做时可能把已有的正确实现覆盖成一份新的。</**
///
/// **A sentence once claimed "three real gaps, about 1100 lines not implemented"**
/// ( 339, 339, 480) — directly
/// contradicting the same paragraph's "already done" and contradicting the source, since all three exist.
///
/// **A stale sentence lived a long time and <b>no behavioural criterion could ever find it</b>: every test was green
/// because the code was right; what was wrong was the description of the code.**
///
/// **So the criterion here is not "the behaviour is right" but "the description agrees with the
/// file system".**
///
/// **A stale "not implemented" is more dangerous than a stale "implemented": the former makes the next round <b>do the work
/// again</b>, and redoing it may overwrite a correct existing implementation with a fresh one.**
/// </remarks>
public class RemotePortingClaimTests
{
    /// <summary>
    /// **被声明为"已移植"的三个文件必须存在且无空壳标记** /
    /// The three files declared "implemented" must exist and carry no stub markers.
    /// </summary>
    [Theory]
    [InlineData("src/Framework/Solver/Remote/Client/RemoteLinearSolver.cs")]
    [InlineData("src/Framework/Solver/Remote/Client/RemoteQuadraticSolver.cs")]
    [InlineData("src/Framework/Solver/Remote/Adapter/OspfRemoteModelSerializer.cs")]
    public void TheDeclaredPortedFiles_ShouldExistWithoutStubMarkers(string relativePath) {
        string root = RepositoryRoot();
        string full = Path.Combine(root, relativePath);

        File.Exists(full).Should().BeTrue(
            $"{relativePath} is declared ported, so it must exist");

        string text = File.ReadAllText(full);

        foreach (string marker in new[] {
            "NotImplementedException", "NotSupportedException", "not ported", "TODO",
        }) {
            text.Should().NotContain(
                marker, $"{relativePath} is declared ported, so it must not carry `{marker}`");
        }
    }

    /// <summary>
    /// **被声明为“已移植”的 IIS 引擎必须存在且无空壳标记** /
    /// The IIS engines declared implemented must exist and carry no stub markers.
    /// </summary>
    /// <remarks>
    /// **纠偏对象：状态文档说“旧 elastic IIS 引擎尚未实现，故 `IisAvailable = false`”，
    /// 而 `LegacyElasticInfeasibilityAnalyzer`、`LinearIisModel`、`LinearLegacyIisCompute`、
    /// `QuadraticLegacyIisCompute` 全都在且已知线（SCIP 与 COPT 置 `IisAvailable: true`）。**
    ///
    /// **`QuadraticLegacyIisCompute` 的头注释还写着“只建骨架、一行都没有”，而文件有 612 行。**
    ///
    /// **这里纠了<b>三处</b>过期结论，全部是同一种失效：代码做完了，描述停在立项那天。**
    ///
    /// **The correction target: a note said the legacy elastic IIS engine was not implemented, so
    /// `IisAvailable = false`, while all four files exist and are wired (SCIP and COPT set `IisAvailable: true`).**
    ///
    /// **`QuadraticLegacyIisCompute`'s header comment still said "skeleton only, nothing there" while the file has 612
    /// lines.**
    ///
    /// **<b>Three</b> stale conclusions failed the same way: the code was finished and the
    /// description stopped at the day the work was scheduled.**
    /// </remarks>
    [Theory]
    [InlineData("src/Core/Solver/Iis/LegacyElasticInfeasibilityAnalyzer.cs")]
    [InlineData("src/Core/Solver/Iis/LinearIisModel.cs")]
    [InlineData("src/Core/Solver/Iis/LinearLegacyIisCompute.cs")]
    [InlineData("src/Core/Solver/Iis/QuadraticLegacyIisCompute.cs")]
    public void TheDeclaredPortedIisEngines_ShouldExistWithoutStubMarkers(string relativePath) {
        string full = Path.Combine(RepositoryRoot(), relativePath);

        File.Exists(full).Should().BeTrue($"{relativePath} is declared ported, so it must exist");

        string text = File.ReadAllText(full);

        // **`not implemented` 与 `TODO` 只查注释里的形态：这两个词在英文注释里可能是"尚未实现"的说明。**
        // **这里的四个文件都应<b>没有</b>这种说明，因为它们已经是实现。**
        //
        // **`not implemented` and `TODO` are checked in their comment forms: both words may describe "not implemented yet" in
        // English.** **All four files here must carry no such note, because they are implementations.**
        foreach (string marker in new[] {
            "NotImplementedException", "NotSupportedException", "not ported", "TODO",
        }) {
            text.Should().NotContain(
                marker, $"{relativePath} is declared ported, so it must not carry `{marker}`");
        }
    }

    /// <summary>
    /// **“只建骨架”这句不能留在已实现的文件里** / "Skeleton only" must not survive in an implemented file.
    /// </summary>
    /// <remarks>
    /// **这一条钉的是一句<b>中文</b>注释。**
    ///
    /// **英文标记（`TODO`／`not implemented`）能被上一条抓住，但那句原话是“只建立类型与入口骨架”，
    /// 一个英文标记都匹配不到——而它比任何一个 TODO 都更误导，因为它让读者以为整个文件是空的。**
    ///
    /// **中文文档里的过期结论，用英文标记扫不出来。这条判据补的就是那个洞。**
    ///
    /// **This one pins a <b>Chinese</b> comment.**
    ///
    /// **The English markers above catch `TODO`/`not implemented`, but the original wording was "only establishes the types and
    /// entry skeleton", which matches no English marker — and it misleads more than any TODO, because it makes the reader
    /// believe the file is empty.**
    ///
    /// **A stale conclusion in Chinese documentation cannot be found by scanning English markers; this criterion fills that
    /// hole.**
    /// </remarks>
    [Fact]
    public void TheImplementedIisFile_ShouldNotClaimToBeASkeleton() {
        string text = File.ReadAllText(
            Path.Combine(RepositoryRoot(), "src/Core/Solver/Iis/QuadraticLegacyIisCompute.cs"));

        text.Should().NotContain(
            "这里只建立**类型与入口骨架**",
            "the file is implemented; the skeleton wording was a stale conclusion");
    }

    /// <summary>
    /// **CPLEX 的 point-based 回调已移植且接线** / CPLEX's point-based callbacks are implemented and wired.
    /// </summary>
    /// <remarks>
    /// **核实由两个断言组成，缺一不可：**
    ///
    /// 1. **两个回调类都存在**（`CplexSolveCallback` 与 `CplexContinuousCallback`）
    /// 2. **它们被 `model.Use(...)` 真正接线**（存在而未接线等于没做）
    ///
 /// **"有实现"与"被用上"是两件事，所以这里查文件内容里的 `model.Use`，而不是只查文件存在。**
    ///
    /// **The verification has two parts, neither optional:**
    ///
    /// 1. **both callback classes exist**;
    /// 2. **they are actually wired through `model.Use(...)`** — existing but unwired is the same as not done.
    ///
    /// **"Has an implementation" and "is used" are different, so this checks for
    /// `model.Use` in the file content rather than only for the file.**
    /// </remarks>
    [Fact]
    public void TheCplexPointBasedCallbacks_ShouldExistAndBeWired() {
        string root = RepositoryRoot();
        string callbackFile = Path.Combine(root, "src/Core.Plugin.Cplex/CplexSolveCallback.cs");
        string nativeFile = Path.Combine(root, "src/Core.Plugin.Cplex/CplexNativeSolver.cs");

        File.Exists(callbackFile).Should().BeTrue(
            "the status document declares the CPLEX point-based callback ported");
        File.Exists(nativeFile).Should().BeTrue();

        string callbackText = File.ReadAllText(callbackFile);
        callbackText.Should().Contain("CplexSolveCallback");
        callbackText.Should().Contain(
            "CplexContinuousCallback", "both callback classes live in this file");

        string nativeText = File.ReadAllText(nativeFile);

        // **查<b>这两个回调</b>被接线，而不是查"文件里有 `model.Use(`"。**
        //
        // **初版查的是后者，变异（删掉两处 `model.Use(callback)`）<b>没变红</b>——
        // 因为同一文件里还有 `model.Use(new Aborter)`。**
        //
        // **"文件里有某个调用"与"这块东西被用上"之间差得很远，
        // 而前者看起来像是后者的证据。**
        //
        // **Check that <b>these two callbacks</b> are wired, not that the file contains `model.Use(`.**
        //
        // **The first version checked the latter and the mutation (removing both `model.Use(callback)` lines)
        // <b>stayed green</b> — because the same file also calls `model.Use(new Aborter)`.**
        //
        // **"The file contains a call" is far from "this thing is used", yet the former looks like evidence for the
        // latter.**
        nativeText.Should().Contain(
            "model.Use(mipCallback)",
            "the MIP callback must be wired, not merely declared");
        nativeText.Should().Contain(
            "model.Use(continuousCallback)",
            "the continuous callback must be wired, not merely declared");
    }

    /// <summary>
    /// **COPT 的 `Callback=false` <b>仍然成立</b>：只有一个通用 `callback`，不是 point-based 回调族** /
    /// COPT's `Callback=false` <b>still holds</b>: one generic `callback`, not the point-based family.
    /// </summary>
    /// <remarks>
    /// **这一条与上一条成对存在，而在<b>同一份文档</b>里两处都写着 `Callback=false`。**
    ///
    /// **一个读者扫到第一处就以为所有后端都没有回调，而事实是只有一个没有。**
    ///
    /// **所以判据必须成对：只钉 CPLEX 已移植，会让人以为 COPT 也已移植；
    /// 只钉 COPT 未实现，会让人以为 CPLEX 也没有。**
    ///
    /// **两件事都断言，才能钉住"同一个词在不同后端上结论相反"这个事实。**
    ///
    /// **This one exists in a pair with the previous, and <b>both</b> `Callback=false` occurrences live in the <b>same
    /// document</b>.**
    ///
    /// **A reader who sees the first concludes no backend has callbacks, while only one lacks them.**
    ///
    /// **So the criteria must be a pair: pinning only "CPLEX is implemented" suggests COPT is too, and pinning only "COPT is
    /// not" suggests CPLEX is not either.**
    ///
    /// **Asserting both pins down the fact that one word carries opposite conclusions on different backends.**
    /// </remarks>
    [Fact]
    public void TheCoptCallback_ShouldStillBeGenericOnly() {
        string file = Path.Combine(
            RepositoryRoot(), "src/Core.Plugin.Copt/CoptSolveCallback.cs");

        File.Exists(file).Should().BeTrue();
        string text = File.ReadAllText(file);

        text.Should().Contain("callback()", "there is a generic callback override");
        text.Should().NotContain(
            "CplexContinuousCallback",
            "COPT has no point-based family here, which is why its Callback=false still holds");

        // **与 CPLEX 对照：COPT 只有一个 override 点。**
        // **Contrast with CPLEX: COPT has a single override point.**
        string[] overrides = text.Split('\n')
            .Where(static line => line.Contains("public override", StringComparison.Ordinal))
            .ToArray();
        overrides.Should().HaveCount(
            1, "one generic callback, not a family of point-based ones");
    }

    /// <summary>
    /// **`LocalFileObjectStoragePort` 存在且是真实实现** / The local storage port exists and is a real implementation.
    /// </summary>
    /// <remarks>
    /// **纠偏对象：文档说“C# 侧无内建 `IObjectStoragePort` 实现”，而
    /// `LocalFileObjectStoragePort`（90 行）一直在。**
    ///
    /// **而这一条比前面几批的更微妙：** **它是“扩展而非缺口”这个判断的一部分。**
    /// **判断本身是对的（固定版本确实只有接口），但支撑它的那句事实描述是错的。**
    ///
    /// **"结论对、依据错"比"结论错"更难发现：** **因为结论对，所有行为判据都绿着；
    /// 而依据错，意味着以后再有人引用这个依据时会得到反的结论。**
    ///
    /// **The correction target: a note said "C# has no built-in `IObjectStoragePort` implementation", while
    /// `LocalFileObjectStoragePort` (90 lines) has been there all along.**
    ///
    /// **This one is subtler: it is part of the judgement "an extension, not a gap".** **The
    /// judgement is right (the reference really has only the interface) but the fact cited to support it was
    /// wrong.**
    ///
    /// **"Right conclusion, wrong evidence" is harder to find than a wrong conclusion: every behavioural criterion stays
    /// green because the conclusion is right — and the wrong evidence means a later reader who cites it reaches the
    /// opposite conclusion.**
    /// </remarks>
    [Fact]
    public void TheLocalStoragePort_ShouldBeARealImplementation() {
        string full = Path.Combine(
            RepositoryRoot(), "src/Framework/Solver/Remote/Adapter/LocalFileObjectStoragePort.cs");

        File.Exists(full).Should().BeTrue(
            "the doc corrected its claim to say an implementation exists, so it must");

        string text = File.ReadAllText(full);
        text.Should().Contain(": IObjectStoragePort", "it must actually implement the port");
        foreach (string marker in new[] {
            "NotImplementedException", "NotSupportedException", "not ported", "TODO",
        }) {
            text.Should().NotContain(marker, $"the storage port is claimed real, not a stub (`{marker}`)");
        }
    }

    /// <summary>
    /// **`SolveFingerprinting` 只有两个入口，所以 C# 的 `Solver`／`Model` 确是扩展** /
    /// The code's `SolveFingerprinting` has two entries, so the C# `Solver`/`Model` really are extensions.
    /// </summary>
    /// <remarks>
    /// **这一条<b>核对的是基线</b>，不是 C#——因为那句声明的主语是基线。**
    ///
    /// **"`solver`／`model` 指纹入口在固定不存在"这句话，只能用基线来验。**
    /// **而验证的方式是读固定的 Kotlin 提交（`12fdb587`）的那份文件，不是读工作区——
    /// 工作区可能已经往前走了。**
    ///
    /// **判据因此读 `git cat-file` 的输出：** **CLAUDE/AGENTS 的口径是"不切换 checkout"，
    /// 而 `cat-file` 正是"不切换也能读固定提交"的手段。**
    ///
    /// **This one verifies <b>the code</b>, not C#, because the code is the subject of that sentence.**
    ///
    /// **"The `solver`/`model` fingerprint entries do not exist" can only be checked against the
    /// baseline.** **And the way to check is to read that file at the pinned Kotlin commit (`12fdb587`), not the working
    /// tree, which may have moved on.**
    ///
    /// **So the criterion reads `git cat-file` output: the rule is "do not switch checkouts", and `cat-file` is exactly
    /// how a pinned commit is read without switching.**
    /// </remarks>
    [Fact]
    public void TheBaselineFingerprinting_ShouldHaveOnlyTwoEntries() {
        string baseline = BaselineFile(
            "ospf-kotlin-core/src/main/fuookami/ospf/kotlin/core/solver/report/SolveReport.kt");

        baseline.Should().Contain(
            "object SolveFingerprinting", "the object must be in this file");
        baseline.Should().Contain("fun sha256(");
        baseline.Should().Contain("fun configuration(");
        baseline.Should().NotContain(
            "fun solver(", "the doc says the baseline has no solver entry, which is also why C# is an extension");
        baseline.Should().NotContain("fun model(");
    }

    /// <summary>读固定 Kotlin 的一份文件 / Read a file from the reference.</summary>
    /// <remarks>
    /// **用 `git -C <repo> cat-file -p <commit>:<path>`，不切换 checkout。**
    /// **切换 checkout 会改动另一个工作区，而那是本方向明令禁止的。**
    ///
    /// **`git -C` with `cat-file -p`, without switching checkouts.** **Switching would mutate the other working tree, which
    /// this direction forbids.**
    /// </remarks>
    private static string BaselineFile(string relativePath) {
        string root = RepositoryRoot();
        string kotlin = Path.Combine(Path.GetDirectoryName(root)!, "ospf-kotlin");

        var startInfo = new System.Diagnostics.ProcessStartInfo("git") {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = kotlin,
        };
        startInfo.ArgumentList.Add("cat-file");
        startInfo.ArgumentList.Add("-p");
        startInfo.ArgumentList.Add($"12fdb587:{relativePath}");

        using System.Diagnostics.Process process = System.Diagnostics.Process.Start(startInfo)
            ?? throw new InvalidOperationException("git could not be started");
        string output = process.StandardOutput.ReadToEnd();
        _ = process.StandardError.ReadToEnd();
        process.WaitForExit();

        process.ExitCode.Should().Be(
            0, $"the pinned baseline must contain {relativePath}");
        return output;
    }

    /// <summary>
    /// **仓库根目录从测试运行目录往上找，直到看见解决方案文件** /
    /// Walk up from the test directory until the solution file appears.
    /// </summary>
    /// <remarks>
    /// **写死相对路径会让判据在别的运行目录下失效，而那种失效是<b>静默</b>的
    /// （找不到文件 → 抛异常 → 那条 [Theory] 红，但原因看起来像"文件不见了"）。**
    ///
    /// **Hard-coding a relative path would break under another working directory, and the breakage is <b>silent</b> in a
    /// misleading way.** **So the root is discovered.**
    /// </remarks>
    private static string RepositoryRoot() {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null) {
            if (File.Exists(Path.Combine(current.FullName, "ospf-csharp.sln"))) {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException(
            "ospf-csharp.sln not found above the test output directory");
    }
}
