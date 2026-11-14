#nullable enable

using FluentAssertions;
using System;
using System.IO;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Docs;

/// <summary>
/// "缺什么"这句话必须与实测一致 / The claim about what is missing must match the measurement.
/// </summary>
/// <remarks>
/// **B1051 没有新增源码。** 它改掉了一句被三个批次推翻的话。
///
/// 台账原来把三个 `analyze` 都写成"类本体需要真实后端"。**B1041 用假 `IFixedIntegerLpBackend`
/// 跑通了敏感性编排，B1049/B1050 把目标可达性的整个判定层用不到一个求解器地测通了。**
///
/// **于是那句话从"谨慎"变成了"低估"**，而低估的代价是真实的：
/// **它让下一批的人以为必须等许可证，而实际能做的活一直在那里。**
///
/// **B1051 added no source code.** It removed a sentence that three batches had already refuted. The ledger used to say all three `analyze` bodies **"need a real backend"**. **B1041 ran the sensitivity
/// orchestration against a stub `IFixedIntegerLpBackend`, and B1049/B1050 tested the entire reachability decision layer without a solver at all.** **So the sentence went from cautious to an
/// underestimate** — and underestimates cost something real: **they make the next session believe it must wait for a licence while the work sits there.**
///
/// **判据钉的是"两端"**：旧说法必须不在，新说法必须在。
/// **只钉 absence 会让一节被整段删掉也通过；只钉 presence 会让新旧两种说法并存。**
///
/// **The criteria pin both ends**: the old claim must be gone and the new one present. **Pinning only absence lets the whole section be deleted and still pass; pinning only presence lets both claims coexist.**
/// </remarks>
public class BackendClaimHonestyTests
{
    private const string LedgerPath = "docs/kotlin-sync-ledger.md";

    private static string Ledger() {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null
            && !File.Exists(Path.Combine(directory.FullName, "ospf-csharp.sln"))) {
            directory = directory.Parent;
        }
        if (directory is null) {
            throw new InvalidOperationException("repository root not found");
        }
        return File.ReadAllText(Path.Combine(directory.FullName, LedgerPath));
    }

    /// <summary>**旧说法必须消失** / The old claim must be gone.</summary>
    /// <remarks>
    /// **"类本体需要真实"这七个字本身就是那句低估。** 它只要还在任何一行里，
    /// **读的人就会以为这条路的下一步是等许可证。**
    ///
    /// **Those exact words are the underestimate.** As long as they appear on any line, **a reader concludes the next step on this road is waiting for a licence.**
    /// </remarks>
    [Fact]
    public void TheLedger_ShouldNotClaimTheBodyNeedsABackend()
    {
        string ledger = Ledger();

        ledger.Should().NotContain("类本体需要真实",
            "three batches refuted this sentence; keeping it makes the remaining work look blocked when it is not");
    }

    /// <summary>**新说法必须给出"能证明什么 / 证明不了什么"的分界** / The new claim must draw the line.</summary>
    /// <remarks>
    /// **只把旧话删掉会让台账变空**，而空话比错话更难被发现。
    /// **这一条要求新说法明确区分"编排"与"证据"。**
    ///
    /// **Deleting the old sentence alone leaves the ledger silent**, and silence is harder to spot than a wrong claim. **This requires the new wording to separate "orchestration" from "evidence".**
    /// </remarks>
    [Fact]
    public void TheLedger_ShouldSeparateOrchestrationFromEvidence()
    {
        string ledger = Ledger();

        // **不能只找"假后端能证明"这几个字**：它同时也是表头的一部分，
        // **所以第一版判据在删掉那句话之后仍然绿——它匹配到了表头。**
        // **Do not grep for "a stub can prove" alone**: that phrase is also part of the table header, **so the first version stayed green after the sentence was deleted — it matched the header.**
        ledger.Should().Contain("必须分开说",
            "the ledger has to say the distinction out loud, not only draw a table");
        ledger.Should().Contain("| 证据 | — |",
            "the evidence row has to be empty on the provable side");
        ledger.Should().Contain("对偶值本身、unsat core 本身",
            "and it has to name what a stub cannot produce");
        ledger.Should().Contain("缺的是**证据的来源**",
            "the remainder is named: evidence, not orchestration");
    }

    /// <summary>**三个 analyze 都必须被点名，且缺口各不相同** / All three are named, and each names a different remainder.</summary>
    /// <remarks>
    /// **三行写同一个"同上"是原来的毛病**：它让三件不同的缺失看起来像一件事。
    /// **这一条要求三个名字都在，且 `对偶` 与 `assumption` 都被提到。**
    ///
    /// **Three rows all saying "same as above" was the original problem**: it made three different gaps look like one. **This requires all three names and both remainders to appear.**
    /// </remarks>
    [Fact]
    public void TheLedger_ShouldNameEachAnalyzerAndItsOwnRemainder()
    {
        string ledger = Ledger();

        ledger.Should().Contain("`ConflictAnalyzer.analyze`");
        ledger.Should().Contain("`ConstraintPerturbationAnalyzer.analyze`");
        ledger.Should().Contain("`FixedIntegerLpSensitivityAnalyzer.analyze`");
        ledger.Should().Contain("对偶",
            "the sensitivity path's remainder is a dual value, and it must be named as such");
        ledger.Should().Contain("assumption",
            "the conflict path's remainder is a solver assumption, not the same thing");
    }
}
