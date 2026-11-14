#nullable enable

using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Docs;

/// <summary>
/// 迁移指南里的方法教训 / The method lesson in the migration guide.
/// </summary>
/// <remarks>
/// **一个没有判据守护的教训会重新被人踩一遍。** B942 放弃逐文件比对时没留下数字，后来又有人想试；B961 量出 98.9% 才把它挡住。
/// 这一条把教训钉在**指南**上，而不只是矩阵上——**写代码的人先看指南，先看矩阵的是写文档的人。**
///
/// **A lesson with no criterion watching it gets trodden on again.** B942 abandoned per-file comparison without leaving a number and people tried again; B961's 98.9% is
/// what stops it. This pins the lesson in **the guide**, not only the matrix — **coders read the guide first; readers of the matrix are the ones writing documents.**
/// </remarks>
public class MigrationGuideMethodTests
{
    /// <summary>**指南有这一节，并写明了比例** / The guide has this section and states the rate.</summary>
    [Fact]
    public void TheGuide_ShouldCarryTheNameBasedGapLesson() {
        string guide = Guide();

        guide.Contains("## 十一、按文件名判缺口", StringComparison.Ordinal)
            .Should().BeTrue("the lesson needs its own section, or it stays a sentence buried in the matrix");
        // **钉那一句本身，不是"文档里出现过 98.9%"**：英文段落里也有同一个数字，
        // 只查"出现过"的判据，把中文结论改成 50% 也照样绿（B962 的第一次变异就撞上了）。
        // **Pin the sentence, not "98.9% appears somewhere"**: the English paragraph carries the same number, so a mere presence check stayed green
        // when the Chinese conclusion was changed to 50% — which is exactly what B962's first mutation hit.
        guide.Contains("**误报率 98.9%。**", StringComparison.Ordinal)
            .Should().BeTrue("the conclusion sentence is the part that stops the next attempt");
        guide.Contains("91 个", StringComparison.Ordinal)
            .Should().BeTrue("and the raw count, so the rate can be recomputed from the document alone");
    }

    /// <summary>**它必须给出替代做法** / It has to say what to do instead.</summary>
    /// <remarks>
    /// 只写"别这么做"会把下一个人留在原地。判据要求文档里有**可执行的替代**：按概念名 grep、数"确认缺失"。
    ///
    /// "Do not do this" alone leaves the reader stuck. The criterion demands an **executable alternative** in the document.
    /// </remarks>
    [Fact]
    public void TheGuide_ShouldOfferAReplacementMethod() {
        string guide = Guide();

        guide.Contains("grep 整个 `src/`", StringComparison.Ordinal)
            .Should().BeTrue("searching the whole source tree by concept is the replacement, and it has to be spelled out");
        guide.Contains("确认缺失", StringComparison.Ordinal)
            .Should().BeTrue("counting confirmed gaps instead of name differences is the other half");
    }

    /// <summary>**指南与矩阵说的是同一个比例** / The guide and the matrix state the same rate.</summary>
    /// <remarks>
    /// 两处数字不一致，就等于又造了一次 B960 那种"两份账"。
    ///
    /// Two documents disagreeing is another instance of the two-account problem B960 found.
    /// </remarks>
    [Fact]
    public void TheGuideAndTheMatrix_ShouldAgreeOnTheRate() {
        string guide = Guide();
        string root = Root();
        string matrix = File.ReadAllText(Path.Combine(root, "docs", "kotlin-capability-matrix.md"));

        guide.Contains("98.9%", StringComparison.Ordinal).Should().BeTrue();
        matrix.Contains("98.9%", StringComparison.Ordinal).Should().BeTrue(
            "the matrix already stated it in B961; a guide with a different number would be a new disagreement");
    }

    private static string Guide() {
        string root = Root();
        return File.ReadAllText(Path.Combine(root, "docs", "kotlin-migration-guide.md"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static string Root() {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ospf-csharp.sln"))) {
            directory = directory.Parent;
        }
        if (directory is null) {
            throw new InvalidOperationException("repository root not found");
        }
        return directory.FullName;
    }
}
