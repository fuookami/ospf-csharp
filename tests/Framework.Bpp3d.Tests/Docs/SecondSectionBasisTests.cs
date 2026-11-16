#nullable enable

using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Docs;

/// <summary>
/// **空白备注等于"没人写过它凭什么 ✅"** / An empty remark means nobody wrote down why this is ✅.
/// </summary>
/// <remarks>
/// 核了矩阵第二节四行：`元模型与线性模型`、`求解器接口与注册`、`组合求解器`、`启发式算子`，**四行都是 ✅ 配空白备注**。
/// 空白不等于"没问题"：下一个人要判断"这个模块能不能用"，只能自己去把源码读一遍。
///
/// **唯一一处真张力**：`元模型与线性模型` 是 ✅，而 §九第 3、6 条说函数展开层整层缺失。两者不矛盾，**但只有写清楚才不矛盾**。
///
/// checked section two's four rows — **all ✅ with an empty remark**, and empty does not mean "fine": the next reader has to re-read the source to judge usability.
/// **The one real tension**: the metamodel row is ✅ while §九 items 3 and 6 say the function expansion layer is entirely missing; they do not contradict **only if it is
/// spelled out**.
/// </remarks>
public class SecondSectionBasisTests
{
    /// <summary>**四行都写明了依据，且依据指名到文件或判据** / Each of the four names its basis.</summary>
    [Fact]
    public void TheFourRows_ShouldNameAFileOrACriterion() {
        string root = Root();

        (string Row, string Basis)[] rows = {
            ("| 元模型与线性模型 |", "MetaModel.cs"),
            ("| 求解器接口与注册 |", "ILinearSolver"),
            ("| 组合求解器 |", "CombinatorialSolver.cs"),
            ("| 启发式算子 |", "13 个源文件"),
        };

        foreach ((string prefix, string basis) in rows) {
            string row = Row(root, prefix);
            row.Contains(basis, StringComparison.Ordinal)
                .Should().BeTrue(prefix + " has to name what backs its ✅ (" + basis + ")");
        }
    }

    /// <summary>**点名的类型真的在** / The named types really exist.</summary>
    /// <remarks>
    /// 行里写了 `MetaModel.cs`，文件就得在；写了 `ILinearSolver`，类型就得在。**否则"有依据"只是一句好听话。**
    ///
    /// The row names `MetaModel.cs`, so the file has to exist; it names `ILinearSolver`, so the type does too. **Otherwise "it has a basis" is just a kind word.**
    /// </remarks>
    [Fact]
    public void TheNamedTypes_ShouldExist() {
        string root = Root();

        File.Exists(Path.Combine(root, "src", "Core", "Model", "Mechanism", "MetaModel.cs"))
            .Should().BeTrue("the metamodel row names this file");
        File.Exists(Path.Combine(root, "src", "Core", "Solver", "CombinatorialSolver.cs"))
            .Should().BeTrue("the composite-solver row names this file");
        File.ReadAllText(Path.Combine(root, "src", "Core", "Solver", "ILinearSolver.cs"))
            .Contains("interface ILinearSolver", StringComparison.Ordinal)
            .Should().BeTrue("the solver-interface row names this type");

        // **必须排除 `bin/` 与 `obj/`：** **`SearchOption.AllDirectories` 会把 SDK 生成的文件也算进去**
        // **（`*.AssemblyInfo.cs`、`*.GlobalUsings.g.cs`），于是这个数字取决于"当时 obj 里生成了几个文件"。**
        //
        // **这一点被实测到：** **同一个工作区，清掉 `obj/` 之前这条数是 16，清掉之后重建又变成 14。**
        // **一条每次构建都可能变数的判据，测的是机器，不是代码。**
        //
        // **`bin/` and `obj/` must be excluded: `SearchOption.AllDirectories` also counts SDK-generated files, so the number
        // depends on how many were generated at that moment.** **Measured: in one workspace the count was 16 before
        // `obj/` was cleared and 14 after a rebuild — a criterion whose number can change per build measures the machine, not
 // the implementation.**
        int heuristicTests = Directory
            .GetFiles(
                Path.Combine(root, "tests", "Core.Plugin.Heuristic.Tests"),
                "*.cs",
                SearchOption.AllDirectories)
            .Count(static path =>
                !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.AltDirectorySeparatorChar}obj{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.AltDirectorySeparatorChar}bin{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal));
        heuristicTests.Should().Be(13,
            "the heuristic row states the measured source-file count; a different count means the row is stale");
    }

    /// <summary>**✅ 与函数展开缺口必须同时出现** / The ✅ and the missing expansion layer must appear together.</summary>
    /// <remarks>
    /// 这条判据钉的是**张力被写出来**，不是张力消失：`元模型与线性模型` 那一行要同时提到自己的 ✅ 范围与 §九的函数缺口。
    /// 缺任何一半，读者都可能把"线性模型完整"读成"函数也能落成行"。
    ///
    /// This pins that **the tension is written down**, not that it is gone: the metamodel row has to mention its own ✅ scope and §九's function gap together. With either half
    /// missing, a reader can take "the linear model is complete" for "functions can become rows too".
    /// </remarks>
    [Fact]
    public void TheMetamodelRow_ShouldCarryTheTension() {
        string root = Root();
        string row = Row(root, "| 元模型与线性模型 |");

        row.Contains("函数", StringComparison.Ordinal)
            .Should().BeTrue("the row has to mention functions, or the ✅ reads as covering them");
        row.Contains("§九 第 3、6 条", StringComparison.Ordinal)
            .Should().BeTrue("and point at the deviation section, so the two documents do not silently disagree");
        row.Contains("不是", StringComparison.Ordinal)
            .Should().BeTrue("it has to say what the ✅ does not cover, which is the whole point of the row");
    }

    /// <summary>**第五节那处张力说明也在** / The tension note is there too.</summary>
    [Fact]
    public void TheNote_ShouldExplainWhyEmptyIsNotFine() {
        string root = Root();
        string matrix = File.ReadAllText(Path.Combine(root, "docs", "kotlin-capability-matrix.md"));

        matrix.Contains("空白不等于", StringComparison.Ordinal)
            .Should().BeTrue("the reason the rows are being filled has to be stated once, not assumed");
        matrix.Contains("没人写过它凭什么 ✅", StringComparison.Ordinal)
            .Should().BeTrue("that phrase is the diagnosis; without it the fill-in looks like cosmetic editing");
    }

    private static string Row(string root, string prefix) {
        string matrix = File.ReadAllText(Path.Combine(root, "docs", "kotlin-capability-matrix.md"));
        return matrix.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')
            .FirstOrDefault(line => line.StartsWith(prefix, StringComparison.Ordinal))
            ?? throw new InvalidOperationException(prefix + " row not found");
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
