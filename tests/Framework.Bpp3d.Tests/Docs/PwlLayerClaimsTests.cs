// PWL 的三层：符号在、展开层缺、插件只探测 / PWL in three layers: symbols present, expansion missing, plugins only detect.

using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Docs;

/// <summary>
/// **"未实现"这三个字要能落到具体一层** / "Not implemented" has to name a layer.
/// </summary>
/// <remarks>
/// 矩阵里三行插件都写着"PWL 未实现"，第九节第 3 条写着"`PackingRendererAdapter` 里 0 处 `PWL`"。
/// **那些都是症状，不是缺口的大小。** 量出来的是三层，状态各不相同：
///
/// | 层 | C# |
/// | --- | --- |
/// | 分段线性符号 | **在**，而且 Demo2 真在用 |
/// | 函数展开层（576 行） | **完全没有** |
/// | 插件原生写入 | **没有**；Gurobi 只有探测 |
///
/// **把三层混成"未实现"，会让人以为补上最容易的那层就够了——而那层已经在了。**
///
/// Three plugin rows said "PWL not implemented" and item 3 said "no `PWL` in `PackingRendererAdapter`". **Those are symptoms, not the size of the gap.** measured three
/// layers with different states: the piecewise symbols are **present** and Demo2 really uses them; the **function expansion layer (576 lines in the code) is entirely
/// absent**; the plugins have **no native writes**, Gurobi only detection. **Conflating them into "not implemented" suggests that filling in the easiest layer would do — and
/// that layer is already here.**
/// </remarks>
public class PwlLayerClaimsTests
{
    /// <summary>**符号层在，而且有人用** / The symbol layer exists and has a consumer.</summary>
    [Fact]
    public void ThePiecewiseSymbols_ShouldExistAndBeUsed() {
        string root = Root();
        string symbols = Path.Combine(root, "src", "Core", "Symbol", "Function");
        Directory.Exists(symbols).Should().BeTrue();
        File.Exists(Path.Combine(symbols, "UnivariateLinearPiecewise.cs")).Should().BeTrue(
            "the piecewise symbol is the one layer of this that is not missing");

        // **有人用才算在**：只有符号没有消费者，和没有符号是一回事。
        // **Used, or it does not count**: a symbol with no consumer is the same as no symbol.
        string demo2 = Path.Combine(root, "src", "Example", "FrameworkDemo", "Demo2");
        string[] users = Directory.GetFiles(demo2, "*.cs", SearchOption.AllDirectories)
            .Where(file => File.ReadAllText(file).Contains("UnivariateLinearPiecewise", StringComparison.Ordinal))
            .ToArray();
        users.Should().NotBeEmpty("Demo2's Envelope and StowagePosition really build piecewise expressions");
    }

    /// <summary>**展开层完全缺失** / The expansion layer is entirely absent.</summary>
    /// <remarks>
    /// 有 **576 行**：`AbsStructure`/`MaxStructure`/`SemiStructure`/`DeferredFunctionConstraintRegion` 与策略枚举。
    /// C# 的 `Core/Solver/Function/` 只有 `FunctionSolverCapabilities.cs` 一个文件——**这是缺口的真实大小。**
    ///
    /// The code's is **576 lines**; the C# `Core/Solver/Function/` holds only a capability declaration — **that is the real size of the gap.**
    /// </remarks>
    [Fact]
    public void TheFunctionExpansionLayer_ShouldStillBeAbsent() {
        string root = Root();
        string folder = Path.Combine(root, "src", "Core", "Solver", "Function");
        string[] files = Directory.GetFiles(folder, "*.cs").Select(Path.GetFileName).ToArray()!;

        files.Should().Contain("FunctionSolverCapabilities.cs", "the capability declaration is what exists");
        // **写成"只有这一个"而不是"没有展开层"**：这条判据要能看见它被补上的那一天。
        // **State it as "only this one", not "no expansion layer"**: the criterion should be able to see the day it gets implemented.
        files.Should().HaveCount(1,
            "the expansion layer is 576 baseline lines and none of it is here; a second file means somebody started porting it, and the matrix row has to change with them");
    }

    /// <summary>**插件侧只有探测，没有原生写入** / The plugins only detect, never write natively.</summary>
    [Fact]
    public void ThePlugins_ShouldStillOnlyDetect() {
        string root = Root();

        // **Gurobi 探测在**：反射方法名，不引用 SDK。
        // **Gurobi detection is present**: it reflects method names without referencing the SDK.
        string gurobi = File.ReadAllText(Path.Combine(root, "src", "Core.Plugin.Gurobi", "GurobiFunctionCapabilities.cs"));
        gurobi.Should().Contain("SetPWLObj").And.Contain("AddGenConstrPWL",
            "detection is the layer that is already ported, and the matrix now says so");

        // **Copt 现在<b>有</b>原生分段文件了。**
        //
        // **这条判据此前断言"Copt / Cplex 都没有 Piecewise 文件"。**
        // **判断哪一侧是依据：** **有 209 行，
 // 而 C# 的读侧早已存在——所以缺的是写侧；写侧补上之后"没有文件"就不再成立。**
        //
        // **改法：Copt 必须<b>有</b>写入器（否则缺口回来了），Cplex 仍然<b>没有</b>（那一侧还没做）。**
        // **两侧都保留断言，只是方向不同——因为能力矩阵现在对它们说的是不同的话。**
        //
        // **Copt now <b>has</b> a native piecewise file.**
        //
        // **How the code side was decided: the code is 209 lines and C#'s read side
 // already existed — so the write side was the gap.**
        //
        // **后来 Cplex 的写侧也补上了，于是这一条现在断言<b>两者都有</b>。**
        // **判据的方向跟着缺口走：缺口在哪一侧，断言就指向哪一侧；缺口消失，断言改成"必须有"。**
        string[] coptFiles = Directory.GetFiles(Path.Combine(root, "src", "Core.Plugin.Copt"), "*.cs")
            .Select(Path.GetFileName).ToArray()!;
        coptFiles.Should().Contain(name => name!.Contains("Piecewise", StringComparison.OrdinalIgnoreCase),
            "B1232 ported the COPT native piecewise writer; the matrix row has to keep saying so");

        string[] cplexFiles = Directory.GetFiles(Path.Combine(root, "src", "Core.Plugin.Cplex"), "*.cs")
            .Select(Path.GetFileName).ToArray()!;
        cplexFiles.Should().Contain(name => name!.Contains("Piecewise", StringComparison.OrdinalIgnoreCase),
            "B1233 ported the CPLEX native piecewise writer, and the matrix row has to keep saying so");
    }

    /// <summary>**矩阵必须把三层分开写** / The matrix has to separate the three layers.</summary>
    [Fact]
    public void TheMatrix_ShouldSeparateTheThreeLayers() {
        string root = Root();
        string matrix = File.ReadAllText(Path.Combine(root, "docs", "kotlin-capability-matrix.md"));

        matrix.Contains("**能力探测已移植**", StringComparison.Ordinal)
            .Should().BeTrue("the detection layer is present and the matrix used to say it was not");
        matrix.Contains("函数**展开层**", StringComparison.Ordinal)
            .Should().BeTrue("the missing layer has to be named as a layer");
        matrix.Contains("576 行", StringComparison.Ordinal)
            .Should().BeTrue("the gap needs its size, or nobody can judge it");
        // **"探测未实现"不能再出现**：那是被推翻的那句话。
        // **"Detection not implemented" must not reappear**: that is the retracted sentence.
        matrix.Contains("原生 PWL/SOS2 能力探测未移植", StringComparison.Ordinal)
            .Should().BeFalse("that claim was false; leaving it in lets the next batch repeat it");
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
