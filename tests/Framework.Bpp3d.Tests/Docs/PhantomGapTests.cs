#nullable enable

using FluentAssertions;
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Docs;

/// <summary>
/// 不存在的缺口也要记下来 / A gap that does not exist has to be recorded too.
/// </summary>
/// <remarks>
/// **"缺一个函数"有两种可能：真的没写，和"别处已经提供了"。** 后者如果只靠人记着，
/// 下一任维护者会重新写一遍遍历逻辑——**而那份遍历会漏掉后来新增的变体。**
///
/// **"A missing function" has two explanations: it was never written, or something else already provides it.** If the second only lives in someone's memory, the next maintainer writes the
/// traversal again — **and that traversal will miss whatever variants were added later.**
/// </remarks>
public class PhantomGapTests {
    private static string ConstraintsPath() {
        string? directory = AppContext.BaseDirectory;
        while (directory is not null) {
            string candidate = Path.Combine(directory, "src", "Core", "Model", "ConstraintProgramming", "ConstraintProgrammingConstraint.cs");
            if (File.Exists(candidate)) {
                return candidate;
            }
            directory = Path.GetDirectoryName(directory);
        }
        throw new InvalidOperationException("ConstraintProgrammingConstraint.cs not found");
    }

    /// <summary>**每个约束变体都必须自己给出变量集合** / Every constraint variant has to supply its own variable set.</summary>
    /// <remarks>
    /// 基类把它声明成 `abstract`，**所以漏写会直接编译失败**——这条判据挡的不是"漏写"，
    /// **挡的是有人把它改成 `virtual` 加一个空默认实现。**
    ///
    /// The base class declares it `abstract`, **so an omission fails compilation** — this criterion does not guard against an omission, **it guards against someone turning it into a
    /// `virtual` with an empty default.**
    /// </remarks>
    [Fact]
    public void TheVariablesMember_ShouldStayAbstract() {
        string source = File.ReadAllText(ConstraintsPath());

        Regex.IsMatch(source, @"public\s+abstract\s+IReadOnlySet<VariableId>\s+Variables\s*\{")
            .Should().BeTrue(
                "the variable set must stay abstract, or a new variant can silently report no variables");

        source.Should().NotContain("virtual IReadOnlySet<VariableId> Variables",
            "a virtual with a default is how the guarantee gets quietly dropped");
    }

    /// <summary>**每个派生变体都覆盖了它** / Every derived variant overrides it.</summary>
    /// <remarks>
    /// 与上一条合起来才完整：`abstract` 保证"编译期不漏"，这条保证"**真的没有空实现**"。
    ///
    /// Together with the previous one this is complete: `abstract` guarantees "not missed at compile time", and this guarantees **"no empty implementation"**.
    /// </remarks>
    [Fact]
    public void EveryVariant_ShouldOverrideIt() {
        string source = File.ReadAllText(ConstraintsPath());

        int variants = Regex.Matches(source, @"public\s+sealed\s+record\s+\w+\s*:\s*ConstraintProgrammingConstraint").Count;
        int overrides = Regex.Matches(source, @"public\s+override\s+IReadOnlySet<VariableId>\s+Variables").Count;

        variants.Should().BeGreaterThan(0, "the file holds the constraint variants");
        overrides.Should().Be(variants,
            $"each of the {variants} variants needs its own variable set, or the traversal gap comes back");
    }

    /// <summary>**台账必须记下这个"不是缺口"的结论** / The ledger records that this was never a gap.</summary>
    [Fact]
    public void TheLedger_ShouldRecordThePhantomGap() {
        string? directory = AppContext.BaseDirectory;
        string? ledger = null;
        while (directory is not null) {
            string candidate = Path.Combine(directory, "docs", "kotlin-sync-ledger.md");
            if (File.Exists(candidate)) {
                ledger = candidate;
                break;
            }
            directory = Path.GetDirectoryName(directory);
        }
        ledger.Should().NotBeNull();

        File.ReadAllText(ledger!).Should().Contain("collectConstraintVariables",
            "the reader has to know this one was checked and found already provided, not forgotten");
    }
}
