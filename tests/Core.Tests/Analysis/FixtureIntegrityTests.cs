#nullable enable

using FluentAssertions;
using System;
using System.IO;
using System.Security.Cryptography;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Analysis;

/// <summary>
/// 实现进来的契约文件必须与钉住的那份逐字节相同 / A copied contract has to be byte-identical to the pinned copy.
/// </summary>
/// <remarks>
/// **这两份文件是"跨语言"这个词的全部依据。** 它们一旦被改动——哪怕只是补个空格、
/// 修个错别字——**就不再是同一份契约，而"两端断言同一份文件"这句话也不成立了。**
///
/// **而且改动不会报错。** 解析器会照常读、判据会照常绿，
/// **只有把文件本身钉住，才看得出它变过。**
///
/// **These two files are the entire basis of the word "cross-language".** Once either is edited — even one space, even one typo fix —
/// **it is no longer the same contract, and "both sides assert the same file" stops being true.**
/// **And the edit raises nothing.** The parser still reads it, the criteria still pass, **and only pinning the file itself shows that it changed.**
/// </remarks>
public class FixtureIntegrityTests
{
    /// <summary>这两份文件的 SHA-256 / The SHA-256 of both files.</summary>
    /// <remarks>
    /// **这两个值只该随文件一起变。** 换文件时重新算一遍并连同提交信息一起改，
    /// **而不是在某个批次的尾随修改里顺手改掉。**
    ///
    /// **These two values may only change with the file.** Recompute them when the file changes, **not as a trailing fix inside some batch.**
    /// </remarks>
    private static readonly (string Name, string Sha256)[] Pinned =
    [
        ("analysis-contract.tsv", "d1ec3ce2c55aaa54f9b1494207d14f02874f494a8074b53439577964257e12d3"),
        ("analysis-cases.tsv", "ba3d7d4266dee49cb8a2618c0900e86e310e117023b109fe552e50a902007a60"),
    ];

    private static string FixturePath(string name)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ospf-csharp.sln")))
        {
            directory = directory.Parent;
        }

        return Path.Combine(
            directory!.FullName, "tests", "Core.Tests", "Analysis", "Fixtures", name);
    }

    /// <summary>**逐字节一致** / Byte-identical.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void TheFixture_ShouldBeByteIdenticalToTheBaseline(int index)
    {
        (string name, string expected) = Pinned[index];
        byte[] content = File.ReadAllBytes(FixturePath(name));

        Convert.ToHexStringLower(SHA256.HashData(content)).Should().Be(expected,
            name + " has to stay byte-identical to the baseline; edit it and it stops being the same contract");
    }

    /// <summary>**两份都在，且都不是空的** / Both exist and neither is empty.</summary>
    /// <remarks>
    /// **一个被截断成 0 字节的 fixture 会让所有读它的判据"通过"**：
    /// 没有数据行，就没有一条断言失败。**这一条挡的就是那种通过。**
    ///
    /// **A fixture truncated to zero bytes makes every criterion that reads it "pass"**: no data rows, no failed assertion. **This is the guard against exactly that pass.**
    /// </remarks>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void TheFixture_ShouldNotBeEmpty(int index)
    {
        (string name, string _) = Pinned[index];
        File.ReadAllBytes(FixturePath(name)).Length.Should().BeGreaterThan(1000,
            name + " has to carry real content, or every row-driven criterion above is vacuously green");
    }
}
