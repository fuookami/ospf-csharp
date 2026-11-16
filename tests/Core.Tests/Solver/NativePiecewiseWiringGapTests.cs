#nullable enable

using FluentAssertions;
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Solver;

/// <summary>
/// **"申报了"与"用上了"之间的那段距离** / The distance between "declared" and "actually used".
/// </summary>
/// <remarks>
/// **一次复核发现的是<b>文档在说谎</b>，不是缺功能。**
///
/// **能力矩阵上 CPLEX 与 COPT 两行都写着"原生 SOS2/PWL 选择与写入未实现"。**
///
/// **而实际上：**
///
/// - **`CplexNativePiecewise.cs` 存在**（172 行，SOS2 lambda 形式，含点数校验、键映射、重复结果键拒绝）
/// - **`CoptNativePiecewise.cs` 存在**
/// - **两者的 `*FunctionCapabilities` 都<b>已经申报</b> `PiecewiseLinearConstraint` 原生支持**
///
/// **缺的只有一件事：没有生产调用方。**
///
/// ### 为什么这个差别严重
///
/// **"未实现"是一个<b>工作项</b>；“已实现但没接上，而能力声明已经说支持”是一个<b>bug</b>。**
///
/// **能力声明是被<b>别人读取</b>的：** **调用方看到 `PiecewiseLinearConstraint` 被申报为支持，
/// 会据此认为"这个后端走原生形式"，而实际走的是通用线性化。**
/// **那不会让解变错——但会让任何基于能力声明做出的选择落在错误的前提上。**
///
/// **所以这两行的正确写法不是"未实现"，而是"已实现、未接线，而申报已越过实际"。**
///
/// **A follow-up check found that <b>the docs were lying</b> — not a missing feature.**
///
/// **The matrix says of both CPLEX and COPT "native SOS2/PWL selection and writing are not implemented".** **In fact both
/// `CplexNativePiecewise.cs` (172 lines, SOS2 lambda form, with point-count validation, key mapping and duplicate-result
/// rejection) and `CoptNativePiecewise.cs` exist, and both `*FunctionCapabilities` <b>already declare</b>
/// `PiecewiseLinearConstraint` native support.** **Only one thing is missing: a production caller.**
///
/// **Why the difference matters: "not implemented" is a <b>work item</b>; "implemented but unwired while the capability
/// declaration already says supported" is a <b>bug</b>.** **Capability declarations are <b>read by others</b>: a caller seeing
/// `PiecewiseLinearConstraint` marked supported will conclude this backend uses the native form, while it actually uses the
/// generic linearization.** **That does not make a solution wrong — it makes any decision built on the declaration rest on a
/// false premise.** **So the correct wording is not "not implemented" but "implemented, unwired, and the declaration has
/// outrun reality".**
/// </remarks>
public class NativePiecewiseWiringGapTests
{
    /// <summary>
    /// **两个后端都有原生实现，而<b>都没有生产调用方</b>** /
    /// Both backends have a native implementation and <b>neither has a production caller</b>.
    /// </summary>
    /// <remarks>
    /// **这条判据同时钉住四件事：** **实现在、调用方不在、申报在。</b>**
    /// **缺任何一条，矩阵上那句话就会写错方向：**
    ///
    /// - **只说"实现不在"** → 读成"要写一个功能"（实际只是接线）
    /// - **只说"调用方不在"** → 读成"根本没人做"
    /// - **不说"申报在"** → 前面两条都成立，而那个 bug 被藏起来了
    ///
    /// **The criterion pins four things at once: the implementation is there, the caller is not, and the declaration is.**
    /// **Drop any one and the matrix sentence points the wrong way** — say only "no implementation" and it reads as "write a
    /// feature"; say only "no caller" and it reads as "nobody has touched this"; omit the declaration and the bug hides behind
    /// two true statements.
    /// </remarks>
    [Fact]
    public void BothBackends_ShouldBeImplementedUnwiredAndDeclared() {
        string root = RepositoryRoot();

        foreach ((string plugin, string piecewise) in new[] {
            ("Cplex", "CplexNativePiecewise.cs"),
            ("Copt", "CoptNativePiecewise.cs"),
        }) {
            string implementation = System.IO.Path.Combine(
                root, "src", $"Core.Plugin.{plugin}", piecewise);
            File.Exists(implementation).Should().BeTrue(
                $"{plugin} has a native piecewise implementation; the matrix must not say otherwise");

            string declaration = File.ReadAllText(System.IO.Path.Combine(
                root, "src", $"Core.Plugin.{plugin}", $"{plugin}FunctionCapabilities.cs"));
            declaration.Should().Contain(
                "PiecewiseLinearConstraint",
                $"{plugin} claims native piecewise support — which is what makes 'unwired' a defect rather than a to-do");

            string[] callers = Directory
                .EnumerateFiles(System.IO.Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
                .Where(path => !path.EndsWith(piecewise, StringComparison.Ordinal))
                .Where(path => File.ReadAllText(path).Contains(
                    System.IO.Path.GetFileNameWithoutExtension(piecewise),
                    StringComparison.Ordinal))
                .ToArray();

            callers.Should().BeEmpty(
                $"{plugin}'s native piecewise writer still has no production caller; once it does, "
                + "the matrix row must change from 'declared beyond reality' to 'wired'");
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
