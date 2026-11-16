#nullable enable

using System;
using System.IO;
using System.Linq;
using FluentAssertions;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Docs;

/// <summary>
/// **名字对不上不是证据，只是线索** / A name mismatch is a lead, not evidence.
/// </summary>
/// <remarks>
/// 把基线 `math` 的 320 个文件名与本仓做差，得到 **91 个"缺失"**；逐个查下来**只有 1 个是真的**，
/// 其余都在别的文件里（`Einsum`、`CombinatoricsAsync`、`EscapeTime`、`Gamma`、`Beta`）。
///
/// **误报率 98.9%。** 当年放弃逐文件比对时只是"觉得不可靠"，**这里把它量出来了**——
/// 于是"按文件名清单判缺口"这个做法，从此在仓库里有数字挡着。
///
/// differenced its 320 math file names against this repo and got **91 "missing"**; checking each showed **only one was real**, the rest living inside
/// differently-named files. **A 98.9% false-positive rate** — abandoned per-file comparison because it "felt unreliable", and ** measured it**, so the method now
/// has a number standing in front of it.
/// </remarks>
public class NameBasedGapMethodTests
{
    /// <summary>**那五个误报的确实在，只是不在同名文件里** / The five false positives exist, just not under their own names.</summary>
    /// <remarks>
    /// 这条判据的直接作用：**任何人下次想用"文件名不在"当缺口证据，先得解释这五个为什么在。**
    ///
    /// What this does: **anyone reaching for "the file name is absent" as gap evidence has to explain these five first.**
    /// </remarks>
    [Fact]
    public void TheNameBasedFalsePositives_ShouldStillLiveSomewhere() {
        string root = Root();
        (string Type, string File)[] present = {
            ("Einsum", @"src\\Math\\MultiArray\\Einsum\\EinsumParser.cs"),
            ("CombinatoricsAsync", @"src\\Math\\Combinatorics\\CombinatoricsAsyncScope.cs"),
            ("EscapeTime", @"src\\Math\\Fractal\\FractalAnalysis.cs"),
            ("Gamma", @"src\\Core\\Solver\\Heuristic\\Operator\\Mutation\\ContinuousMutations.cs"),
            ("Beta", @"src\\Core.Plugin.Heuristic\\GWO\\GwoPolicy.cs"),
        };

        foreach ((string type, string file) in present) {
            string path = Path.Combine(root, file);
            File.Exists(path).Should().BeTrue(file + " is where the baseline's same-named file actually ended up");
            File.ReadAllText(path).Contains(type, StringComparison.Ordinal)
                .Should().BeTrue(type + " has no same-named file here, yet the concept is implemented — which is exactly the false positive");
        }
    }

    /// <summary>**矩阵把误报率写出来了** / The matrix states the false-positive rate.</summary>
    [Fact]
    public void TheMatrix_ShouldStateTheFalsePositiveRate() {
        string root = Root();
        string matrix = File.ReadAllText(Path.Combine(root, "docs", "kotlin-capability-matrix.md"));

        matrix.Contains("98.9%", StringComparison.Ordinal)
            .Should().BeTrue("a measured rate is what turns 觉得不可靠 into 一条判据");
        matrix.Contains("名字对不上", StringComparison.Ordinal)
            .Should().BeTrue("the sentence that names the mistake has to be there");
        matrix.Contains("91 个", StringComparison.Ordinal)
            .Should().BeTrue("and the raw count, so the rate can be recomputed");
    }

    /// <summary>**补上的那个入口真的在，且真的是后序** / The one real gap is filled, in post-order.</summary>
    /// <remarks>
    /// 判据落在**源码里有这个文件**，语义落在 `ExpressionTransformTests` 的六条——**文档不该替测试下语义结论**。
    ///
    /// The criterion is that the file exists; the semantics belong to `ExpressionTransformTests`' six facts — **a document should not carry a semantic verdict.**
    /// </remarks>
    [Fact]
    public void TheExpressionTransform_ShouldExist() {
        string root = Root();
        string path = Path.Combine(root, "src", "Math", "Symbol", "Expression", "ExpressionTransform.cs");
        File.Exists(path).Should().BeTrue("B961 added it; the matrix row cites it");

        string source = File.ReadAllText(path);
        foreach (string entry in new[] { "Transform<", "TransformScalars<", "TransformBooleans" }) {
            source.Contains(entry, StringComparison.Ordinal)
                .Should().BeTrue(entry + " is one of the three entry points the baseline has");
        }
        // **后序**：`return transformer(rebuilt)` 必须在重建之后。
        // **Post-order**: the callback call has to come after the rebuild.
        int rebuild = source.IndexOf("ScalarExpression<T> rebuilt = expression switch", StringComparison.Ordinal);
        int callback = source.IndexOf("return transformer(rebuilt)", StringComparison.Ordinal);
        rebuild.Should().BeGreaterThan(-1);
        callback.Should().BeGreaterThan(rebuild, "a pre-order version applies the callback before the children are rebuilt");
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
