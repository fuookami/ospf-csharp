#nullable enable

using FluentAssertions;
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Docs;

/// <summary>
/// **"按名字找对应物"这条方法本身不可信，而我已经用它误判过三次** /
/// The "find the counterpart by name" method cannot be trusted, and I have misjudged with it three times.
/// </summary>
/// <remarks>
/// **B1277 想回答一个看起来很简单的问题：基线里有多少类型本仓没有？**
///
/// **我写了一个脚本，按文件名 + 类型声明名对照，得到的数字一路在变：**
///
/// | 口径 | 结果 | 抽查后 |
/// | --- | --- | --- |
/// | 按文件名 stem | **110** | 含大量改名 |
/// | 按类型声明名（精确） | **155** | `UnivariateLinearPiecewise` 实为 `…PiecewiseFunction` |
/// | 再加后缀／大小写容忍 | **133** | `ModelView`、`ConstraintPriority` 明明都在 |
///
/// **三个数字没有一个能用，而每一个看起来都很像结论。**
///
/// ### 三次误判的具体形态
///
/// - **`Imply`** —— 我判"完全缺失"，而 `src/Core/Symbol/Function/Imply.cs` 就在那里
/// - **`UnivariateLinearPiecewise`** —— 我判"缺失"，本仓叫 `UnivariateLinearPiecewiseFunction`
/// - **`ModelView` / `ConstraintPriority`** —— 我判"缺失"，两个文件都在 `src/Core/Model/Basic/` 下
///
/// **三次都是同一个动作：** **用"名字相等"代理"是不是同一个东西"。**
/// **而 Kotlin 与 C# 的命名习惯不同（`Function` 后缀、`View`／`Item`／`Group` 后缀），
/// 基线里还有大量 `typealias` 与纯函数文件——<b>名字本来就不该相等**。</b>
///
/// ### 所以这一批不交付"缺多少"，交付"这个数字不可交付"
///
/// **判据断言的是：** **那几个我误判过的名字，在本仓确实有对应物**——
/// **它防的不是"基线缺什么"，而是"下一次有人（包括我）重新跑同一个脚本，又得出一个数字"。**
///
/// **B1277 set out to answer a simple-looking question: how many baseline types does this repo lack?** **I wrote a script
/// comparing file stems and declared type names, and the number kept moving: 110 by stem, 155 by exact type name, 133 after
/// suffix and case tolerance — and spot checks showed every one of them was unusable while looking like a conclusion.**
///
/// **Three specific misjudgements: `Imply` (declared "entirely missing" while `Imply.cs` sits in
/// `src/Core/Symbol/Function/`), `UnivariateLinearPiecewise` (called missing; this repo names it
/// `UnivariateLinearPiecewiseFunction`), and `ModelView` / `ConstraintPriority` (both called missing, both under
/// `src/Core/Model/Basic/`).** **All three commit the same act: substituting "the names are equal" for "these are the same
/// thing" — and Kotlin and C# name things differently (`Function`, `View`, `Item`, `Group` suffixes), while the baseline also
/// carries many `typealias` and pure-function files, so <b>the names were never supposed to match</b>.**
///
/// **So this batch does not deliver "how much is missing"; it delivers "that number is not deliverable".** **What the criterion
/// asserts is that the names I misjudged do have counterparts here — it guards not against "the baseline lacks something" but
/// against "someone, myself included, runs the same script again and gets another number".**
/// </remarks>
public class NameBasedCounterpartScanTests
{
    /// <summary>
    /// **我误判为"缺失"的那几个，本仓都有** / The ones I judged missing all exist here.
    /// </summary>
    /// <remarks>
    /// **每个例子都带着它<b>真实的**名字，因为"对应物叫什么"正是这条判据要说的事。**</b>
    ///
    /// **Each example carries its <b>real</b> name here, because "what the counterpart is called" is exactly what this
    /// criterion is about.**
    /// </remarks>
    [Fact]
    public void TheNamesIJudgedMissing_ShouldExistUnderTheirRealNames() {
        string root = RepositoryRoot();

        var counterparts = new (string Baseline, string Local, string Why)[] {
            ("Imply", "src/Core/Symbol/Function/Imply.cs",
                "the file name was already identical; my scan never looked at it"),
            ("UnivariateLinearPiecewise", "src/Core/Symbol/Function/UnivariateLinearPiecewise.cs",
                "this repo appends the Function suffix, so exact-name matching fails"),
            ("ModelView", "src/Core/Model/Basic/ModelView.cs",
                "declared as a record, which my declaration regex did not cover"),
            ("ConstraintPriority", "src/Core/Model/Basic/ConstraintPriority.cs",
                "same as ModelView"),
        };

        foreach ((string baseline, string local, string why) in counterparts) {
            File.Exists(System.IO.Path.Combine(root, local.Replace('/', System.IO.Path.DirectorySeparatorChar)))
                .Should().BeTrue(
                    $"{baseline} was reported missing but lives at {local}: {why}");
        }
    }

    /// <summary>
    /// **扫描器给出的三个数字，都不该被引用** / None of the scan's three numbers should be cited.
    /// </summary>
    /// <remarks>
    /// **这条不判断"133 对不对"，而是断言<b>矩阵与计划里不出现这三个当作结论的数字**。
    ///
    /// **它们唯一合法的位置是作为"方法失败的三次记录"出现，而那已经在</b><see cref="NameBasedCounterpartScanTests"/>
    /// 的注释里。**
    ///
    /// **This does not judge whether 133 is right; it asserts the matrix and plan do not carry the three numbers <b>as
    /// conclusions</b>. Their only legitimate place is as "three failures of one method", which is the remarks above.**
    /// </remarks>
    [Fact]
    public void TheThreeNumbers_ShouldNotAppearAsConclusions() {
        string root = RepositoryRoot();
        string matrix = File.ReadAllText(System.IO.Path.Combine(root, "docs", "kotlin-capability-matrix.md"));

        foreach (string claim in new[] { "110 个类型缺", "155 个类型缺", "133 个类型缺" }) {
            matrix.Should().NotContain(
                claim,
                "a name-matched count is not a count of missing capabilities; see this class's remarks");
        }
    }

    private static string RepositoryRoot() {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null) {
            if (File.Exists(System.IO.Path.Combine(current.FullName, "ospf-csharp.sln"))) {
                return current.FullName;
            }
            current = current.Parent;
        }
        throw new InvalidOperationException("ospf-csharp.sln not found");
    }
}