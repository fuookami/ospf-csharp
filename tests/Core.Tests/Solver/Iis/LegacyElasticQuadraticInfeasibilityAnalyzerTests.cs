#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Solver.Iis;
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Solver.Iis;

/// <summary>
/// 二次旧式 IIS 分析器已接入框架 / The quadratic legacy IIS analyzer is wired into the framework.
/// </summary>
/// <remarks>
/// B1260 补的是一个"机器都在、只差适配器"的缺口：`QuadraticLegacyIisCompute`、`QuadraticIisModel`、
/// `QuadraticElasticFilterResult`、二次的 `DiagnosticConstraintId` 全都在，线性版分析器也在，
/// **唯独缺这个类——没有它，那整套二次 IIS 机器无法经分析器框架被调用。**
///
/// **所以判据的第一件事不是"算得对"，而是"接进去了没有"：**
/// **一个能力齐备但没有入口的东西，与没有这个东西，在能力矩阵上是同一格。**
///
/// B1260 fills an "everything is here except an adapter" gap, so the criterion's first job is not correctness but
/// reachability: **a capability with no entry point occupies the same cell of the matrix as no capability at all.**
/// </remarks>
public class LegacyElasticQuadraticInfeasibilityAnalyzerTests
{
    /// <summary>
    /// **它确实实现了框架要的两个接口** / It really implements both interfaces the framework requires.
    /// </summary>
    /// <remarks>
    /// **这条用反射而不是读源码：** **接口实现是编译器保证的，而"这个类型能被当成
    /// `ICapabilityAwareInfeasibilityAnalyzer` 使用"才是这条判据要说的事。**
    ///
    /// **This uses reflection rather than reading the source: interface implementation is guaranteed by the compiler, and
    /// what the criterion must say is "this type can be used as an `ICapabilityAwareInfeasibilityAnalyzer`".**
    /// </remarks>
    [Fact]
    public void ItShouldImplementTheFrameworkInterfaces() {
        Type type = typeof(LegacyElasticQuadraticInfeasibilityAnalyzer);
        Type[] interfaces = type.GetInterfaces();

        interfaces.Should().Contain(
            typeof(ICapabilityAwareInfeasibilityAnalyzer<IQuadraticTetradModelView>),
            "without this the orchestrator cannot choose it at all — the capability would be unreachable");
        interfaces.Should().Contain(
            typeof(IMaterializingInfeasibilityAnalyzer<IQuadraticTetradModelView, QuadraticIisModel>),
            "without this the caller cannot obtain the materialised quadratic IIS model");
    }

    /// <summary>
    /// **它一律报 `Partial`，因为二次模型不携带松弛可行性** /
    /// It always reports `Partial` because the quadratic model carries no relaxation feasibility.
    /// </summary>
    /// <remarks>
    /// **线性版用 `iis.RelaxedFeasible` 在 `Complete` 与 `Partial` 之间选；`QuadraticIisModel` 只有三个字段，
    /// 没有这个标志。**
    ///
    /// **所以二次版固定报弱的那一档。** **这条判据把那个<b>决定</b>钉住：**
    /// **如果有人把 `Partial` 改成 `Complete`，他会红——而那时他必须先让 `QuadraticIisModel` 带上那个标志，
    /// 而不是只改一个字面量。**
    ///
    /// **The linear version picks between `Complete` and `Partial` via `iis.RelaxedFeasible`; `QuadraticIisModel` has three
    /// fields and no such flag, so this one always reports the weaker grade.** **The criterion pins that <b>decision</b>:
    /// changing `Partial` to `Complete` goes red, and whoever does it must first give `QuadraticIisModel` the flag rather than
    /// edit a literal.**
    /// </remarks>
    [Fact]
    public void ItShouldAlwaysReportTheWeakerGrade() {
        string root = RepositoryRoot();

        string analyzer = File.ReadAllText(
            System.IO.Path.Combine(
                root, "src/Core/Solver/Iis/LegacyElasticQuadraticInfeasibilityAnalyzer.cs"));
        analyzer.Should().Contain("Completeness = EvidenceCompleteness.Partial",
            "the quadratic path has no relaxation-feasibility flag to justify a stronger claim");
        analyzer.Should().NotContain("EvidenceCompleteness.Complete",
            "claiming completeness here would turn 'not checked' into 'checked'");

        // **而理由必须仍然成立：二次 IIS 模型确实还是三个字段。**
        // **And the reason must still hold: the quadratic IIS model still has exactly three fields.**
        string model = File.ReadAllText(
            System.IO.Path.Combine(root, "src/Core/Solver/Iis/QuadraticLegacyIisCompute.cs"));
        model.Should().Contain("public sealed record QuadraticIisModel(",
            "if the model gains a relaxation-feasibility flag, this criterion must be revisited");
        model.Should().Contain("IReadOnlyList<int> OriginColumns",
            "the three-field shape is what leaves no room for the flag");
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
