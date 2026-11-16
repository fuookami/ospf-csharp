#nullable enable

using System;
using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Fuookami.Ospf.Framework.Bpp3d.Tests.Docs;

/// <summary>
/// **ONNX 移植的未移植项必须逐条写清理由与升级路径** / The unmigrated ONNX items need a reason and an upgrade path each.
/// </summary>
/// <remarks>
/// **未移植项若不逐条写清"为什么没做、怎么补上、影响谁"，下一个人只能从代码里猜——** **而猜错的方向通常是"以为已经支持了"。**
/// **这条判据钉的是清单本身的存在与形状，不是清单内容的对错：** **内容的对错由各批的判据负责。**
///
/// **Without a per-item "why not, how to add it, who is affected", the next reader can only guess from the code — and the usual wrong guess is "it is already
/// supported".** **This criterion pins that the list exists and has the right shape, not that its content is right: the content is each batch's own criteria's job.**
/// </remarks>
public class OnnxPortBoundaryTests
{
    /// <summary>**清单存在，且每行都带理由、升级路径与影响** / The list exists and each row carries reason, path and impact.</summary>
    [Fact]
    public void TheUnmigratedList_ShouldCarryReasonAndUpgradePath() {
        string matrix = File.ReadAllText(Path.Combine(RepositoryRoot(), "docs", "kotlin-capability-matrix.md"));

        matrix.Should().Contain("## ONNX 移植：已完成与未移植（逐项，含理由与升级路径）",
            "the section is the only place that answers \"what did we deliberately not do\"");
        matrix.Should().Contain("| # | 未移植项 | 为什么没做 | 升级路径 | 影响 |",
            "the table header states the four things every row owes");

        // 只扫 ONNX 那一节：矩阵里别处也有 "| 数字 |" 开头的表格，全局扫会抓到它们。
        // Scan only the ONNX section: other tables also start rows with "| number |", and a global scan catches them.
        int boundary = matrix.IndexOf("## ONNX 移植：已完成与未移植", StringComparison.Ordinal);
        boundary.Should().BeGreaterThanOrEqualTo(0);
        string[] rows = matrix.Substring(boundary).Split('\n');
        int counted = 0;
        foreach (string row in rows) {
            if (!Regex.IsMatch(row, @"^\| \d+ \|")) {
                continue;
            }
            ++counted;
            string[] cells = row.Split('|');
            cells.Length.Should().BeGreaterThanOrEqualTo(6, "a row needs the four content cells: " + row);
            cells[2].Trim().Length.Should().BeGreaterThan(4, "the item has to be named: " + row);
            cells[3].Trim().Length.Should().BeGreaterThan(8, "the reason cannot be left empty: " + row);
            cells[4].Trim().Length.Should().BeGreaterThan(8, "the upgrade path cannot be left empty: " + row);
            cells[5].Trim().Length.Should().BeGreaterThan(4, "the impact has to be stated: " + row);
        }
        counted.Should().BeGreaterThanOrEqualTo(5,
            "the KNOWN unmigrated items are: activation on symbolic input, shape-operator model rewriting, "
            + "multi-state Scan, the name-level parity that is deliberately not a goal, and the ir_version branch that is not taken "
            + "(in-place loop bodies left the list in B1355, which is why this count can only go down when a batch lands one)");
    }

    /// <summary>**每一项的动机写在文档里的那一条，与它对应** / Each reason names what it would take, not merely "not done".</summary>
    /// <remarks>
    /// **"未做"不是理由，"需要什么"才是：** **一条写成"暂未实现"的记录，三个月后仍然只是"暂未实现"，** **而一条写成"需要模型层的函数符号注册"的记录**
    /// 指出了下一个人该动哪里。
    ///
    /// **"Not done" is not a reason; "what it needs" is: a note saying "not implemented yet" is still that three months later, while one saying "needs function-symbol
    /// registration at the model layer" points at where to work.**
    /// </remarks>
    [Fact]
    public void EachReason_ShouldSayWhatItWouldTake() {
        string matrix = File.ReadAllText(Path.Combine(RepositoryRoot(), "docs", "kotlin-capability-matrix.md"));
        int section = matrix.IndexOf("## ONNX 移植：已完成与未移植", StringComparison.Ordinal);
        section.Should().BeGreaterThanOrEqualTo(0);
        string slice = matrix.Substring(section, System.Math.Min(6000, matrix.Length - section));

        slice.Should().Contain("`MechanismModel`", "the activation gap is a model-layer gap, and naming the layer is the whole point");
        slice.Should().Contain("TryRebind", "the in-place limitation has a concrete upgrade with a name");
        slice.Should().Contain("函数符号", "the missing piece is stated in the same words the refusal message uses");
    }

    private static string RepositoryRoot() {
        string? directory = AppContext.BaseDirectory;
        while (directory is not null && !File.Exists(Path.Combine(directory, "ospf-csharp.sln"))) {
            directory = Path.GetDirectoryName(directory);
        }
        return directory ?? throw new InvalidOperationException("solution root not found");
    }
}
