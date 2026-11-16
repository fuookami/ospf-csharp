using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Xunit;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// 只写字段必须**在声明处**说清楚，不许靠下一个人去猜 / Write-only fields must be explained where they are declared.
/// </summary>
/// <remarks>
/// 本仓已经因为"没人读的输入"踩过两次： 把 `weightUnit` 记成缺口（后来发现不读），
/// 发现请求里的两个推荐优先级**真的**被丢掉。两次都说明一件事：**"这个字段有没有读者"必须能查、能写下来。**
///
/// 这里把两处只写字段的结论钉在源码文本上：**结论消失或措辞被删掉时会变红**，逼着下一个人重新做一次同样的调查，
/// 而不是把"看起来没用到"当成"可以接一条规则上去"。
///
/// The port has been bitten twice by "an input with no reader": recorded `weightUnit` as a gap (the code does not read it either), and found that the two
/// recommendation priorities really were dropped. Both say the same thing: **whether a field has a reader has to be checkable and written down**. This pins the two
/// write-only conclusions to the source text: **removing them turns it red**, forcing the next person to redo the investigation instead of reading "looks unused" as
/// "safe to attach a rule to".
/// </remarks>
public sealed class Demo2WriteOnlyFieldTests
{
    /// <summary>
    /// `RequiresSeparation` 的只写结论必须写在声明处 / The write-only conclusion for `RequiresSeparation` must sit at its declaration.
    /// </summary>
    [Fact]
    public void RequiresSeparation_ShouldSayThatItHasNoReader() {
        string source = ReadSource("DTO.cs");

        source.Should().Contain("`RequiresSeparation` 在本仓没有读者",
            "the conclusion must name the field and say who does NOT read it");
        source.Should().Contain("不要为了让它\"看起来被用上\"而临时接一条约束",
            "the warning against inventing a consumer is the point of the comment");
    }

    /// <summary>
    /// 请求里的 `weightUnit` 必须**有人读**，不能是只写字段 / The request's `weightUnit` must have a reader, never be write-only.
    /// </summary>
    /// <remarks>
    /// 把这条判据的结论翻了过来，因为它的前提是错的。** 原话写着「基线从不读 `request.weightUnit`」，
    /// 于是钉"字段不要存在"。 实际读下去发现三处：
    /// 舱位承重按 `input.weightUnit.unit` 取单位；机型默认 `weightUnit = Kilogram`；
    /// 聚合时又用 `aggregation.aircraftModel.weightUnit` 覆盖。
    ///
    /// **这个字段确实被读，而且默认就是 `Kilogram`。** 旧判据钉的是一个错的事实——**一条钉住错误事实的判据比没有判据更糟**
    /// 。
    ///
    /// 现在它钉**真正该钉的东西**：字段在、**且真的被用上**。只加字段不接消费方仍然是"只写"，这条会红。
    ///
    /// **flipped this criterion's conclusion, because its premise was wrong.** It claimed the code never reads `request.weightUnit` and therefore pinned
    /// "the field must not exist". Reading the code shows it does read it, and that the default is `Kilogram` — **a criterion pinning a wrong fact is worse than
    /// none** (the same lesson recorded). It now pins **what deserves pinning**: the field exists **and is actually used**. Adding it without a consumer is
    /// still write-only, and this reddens.
    /// </remarks>
    [Fact]
    public void TheWeightUnit_ShouldExistAndHaveAReader() {
        // **字段在**（，默认 `Kilogram`）。
        // **The field exists** (the code has it, defaulting to `Kilogram`).
        typeof(RequestDTO).GetProperties().Select(property => property.Name)
            .Should().Contain("WeightUnit", "the baseline reads request.weightUnit for the position capacity");
        typeof(RequestDTO).Assembly.GetTypes().Select(type => type.Name)
            .Should().Contain("WeightUnitInput");

        // **而且要被真的读**：`LoadingContextFactory` 里必须有一处从请求取它。
        // **And it must really be read**: `LoadingContextFactory` has to take it from the request somewhere.
        string source = ReadSource("LoadingContextFactory.cs");
        source.Should().Contain("input.WeightUnit",
            "a field nobody reads is write-only, which is what the old criterion was actually guarding against");
        // **一个变量贯穿三处**：只改一处会让重量数字与声明单位差 1000 倍，而可行性往往看不出来。
        // **One variable through all three sites**: changing only one makes numbers and declared unit differ by 1000x while feasibility usually looks fine.
        source.Should().Contain("WeightUnit: weightUnit",
            "the problem must declare the same unit its numbers use");
    }
    private static string ReadSource(string fileName) {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ospf-csharp.sln"))) {
            directory = directory.Parent;
        }
        if (directory is null) {
            throw new InvalidOperationException("repository root not found");
        }
        // **在 `Demo2` 整棵子树里按文件名找**：写死 `Infrastructure` 只能看 DTO，
        // 而"字段有没有消费者"往往要看 `Domain` 下的服务。
        // **Search the whole `Demo2` subtree by file name**: hard-coding `Infrastructure` limits this to DTOs, while "does the field have a consumer"
        // usually needs a service under `Domain`.
        string[] found = Directory.GetFiles(
            Path.Combine(directory.FullName, "src", "Example", "FrameworkDemo", "Demo2"),
            fileName,
            SearchOption.AllDirectories);
        found.Should().NotBeEmpty(fileName + " must exist under Demo2");
        return File.ReadAllText(found[0]);
    }
}
