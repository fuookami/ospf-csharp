#nullable enable

using FluentAssertions;
using Fuookami.Ospf.Core.Analysis;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Math.Algebra.Number;
using System.Collections.Generic;
using Xunit;

namespace Fuookami.Ospf.Core.Tests.Analysis;

/// <summary>
/// **接上真实线性求解器的那一层：两条会静默出错的规则** /
/// The layer connecting a real linear solver: its two rules that fail silently.
/// </summary>
/// <remarks>
/// **这里是 `ILoweredFixedIntegerLpSolver` 唯一的缺口：一个生产实现都没有。**
/// **在它之前，`IFixedIntegerLpBackend`／`LoweredFixedIntegerLpBackend`／`ILoweredFixedIntegerLpSolver` 三样都在，
/// 两个插件后端也都设计成依赖它——而它们全都接不上，因为中间那一层不存在。**
///
/// **这一层里有两处「错了不会崩、只会给出错误答案」的逻辑，所以单独测：① 状态判定 ② 对偶映射。
/// 其余部分（降阶、建 triad、调求解器）是既有组件之间的连线。**
///
/// **而这两条判据各自抓到了我的一次实现错误** —— 见各自注释。
///
/// **Fills the only gap of `ILoweredFixedIntegerLpSolver`: it had no production implementation.** **Before it the
/// interface, the lowering adapter and the delegate were all present and both plugin backends were designed to depend on it —
/// and none could connect, because the middle layer did not exist.**
///
/// **Two pieces of logic here fail silently rather than crashing, so they are tested separately: the status rule and the dual
/// mapping.** **Each criterion caught one of my own implementation errors — see their remarks.**
/// </remarks>
public class LinearSolverFixedIntegerLpSolverTests
{
    /// <summary>
    /// **incumbent 不算可达，而已证不可行必须报为不可达** /
    /// An incumbent is not reachable, and proven infeasibility must report unreachable.
    /// </summary>
    /// <remarks>
    /// **第一版规则写成「只有 Optimal 才查映射」，于是 `None+Infeasible` 被判成 `Unknown`。**
    ///
    /// **那条用例是我为了覆盖「无解」随手加的，而它抓到的是一件真事：**
    /// **已证不可行是一个可证明的结论，压成 `Unknown` 等于把证明丢了。**
    ///
    /// **The first rule said "consult the mapping only when Optimal", which made `None+Infeasible` come out `Unknown`.** **That
    /// case was added casually to cover "no solution", and it caught something real: proven infeasibility is a <b>provable
    /// conclusion</b>, and flattening it to `Unknown` throws the proof away.**
    /// </remarks>
    [Theory]
    [InlineData(SolutionPresence.Optimal, ProblemStatus.Feasible, AnalysisStatus.Reachable)]
    [InlineData(SolutionPresence.Incumbent, ProblemStatus.Feasible, AnalysisStatus.Unknown)]
    [InlineData(SolutionPresence.None, ProblemStatus.Infeasible, AnalysisStatus.Unreachable)]
    [InlineData(SolutionPresence.None, ProblemStatus.Unknown, AnalysisStatus.Unknown)]
    public void TheStatusRule_ShouldRequireProofWhereProofIsClaimed(
        SolutionPresence presence,
        ProblemStatus problemStatus,
        AnalysisStatus expected) {
        LinearSolverFixedIntegerLpSolver
            .MapStatus(presence, problemStatus)
            .Should().Be(expected);
    }

    /// <summary>
    /// **对偶只给「唯一归属」的那些，其余一律不出现** /
    /// Duals appear only where the mapping is unique; everything else is omitted.
    /// </summary>
    /// <remarks>
    /// **基线原话：** **「仅当某个原始约束恰好对应唯一降阶行时才给出值」，
    /// 且「降阶辅助行及无法唯一归属原始来源的行不会进入返回的对偶映射」。**
    ///
    /// **第一版实现是「后写覆盖先写」，于是对应两行的约束会拿到第二行的对偶——
    /// 那个数字看起来完全合法，而它归属错误。这条判据把它抓了出来。**
    ///
    /// **三种输入断言三件事：唯一归属→保留；对应两行→丢弃；无原始来源→丢弃。**
    ///
    /// **The code says a value is given only when an original constraint corresponds to a unique lowered row, and that helper
    /// rows and rows without a unique original do not enter the map.** **The first implementation let later writes overwrite
    /// earlier ones, so a two-row constraint received the second row's dual — a plausible number with the wrong owner — and this
    /// criterion caught it.** **Three inputs assert three things: unique → kept; two rows → dropped; no original → dropped.**
    /// </remarks>
    [Fact]
    public void TheDualMapping_ShouldKeepOnlyUniqueOrigins() {
        var unique = new ConstraintId("c-unique");
        var doubled = new ConstraintId("c-doubled");
        var helperOnly = new ConstraintId("c-helper");

        var report = new SolveReport<Flt64> {
            ProblemStatus = ProblemStatus.Feasible,
            TerminationReason = TerminationReason.Completed,
            SolutionPresence = SolutionPresence.Optimal,
            Diagnostics = new SolveDiagnostics<Flt64>(
                ConstraintEvaluations: [
                    Evaluation(unique, 7.5),
                    Evaluation(doubled, 3.0),
                    Evaluation(doubled, 4.0),
                    Evaluation(helperOnly, 9.0),
                ]),
        };

        IReadOnlyDictionary<ConstraintId, Flt64?> duals =
            LinearSolverFixedIntegerLpSolver.ExtractDuals(report, [unique, doubled]);

        duals.Should().ContainSingle("only the unique origin survives");
        duals.Should().ContainKey(unique);
        duals[unique]!.Value.ToDouble().Should().BeApproximately(
            7.5, 1e-12,
            "the surviving value must be the row that owns it, not a neighbour's");
        duals.Should().NotContainKey(
            doubled, "a constraint spanning two rows has no unique mapping, so it must be omitted rather than overwritten");
        duals.Should().NotContainKey(helperOnly, "a row with no original constraint must never appear");
    }

    /// <summary>**对偶为 null 时保留为 null，不得补零** / A null dual stays null; zero must not be invented.</summary>
    /// <remarks>
    /// **「丢掉」与「补零」在报告里长得不一样：** **补零会造出一个看起来有依据的 0，
    /// 而下游会把它当成「该约束对目标无影响」。**
    ///
    /// **Dropping and zero-filling look different in a report: zero invents a number that looks grounded, and downstream would
    /// read that 0 as "this constraint does not affect the objective".**
    /// </remarks>
    [Fact]
    public void TheDualMapping_ShouldKeepNullAsNull() {
        var id = new ConstraintId("c-null");

        var report = new SolveReport<Flt64> {
            ProblemStatus = ProblemStatus.Feasible,
            TerminationReason = TerminationReason.Completed,
            SolutionPresence = SolutionPresence.Optimal,
            Diagnostics = new SolveDiagnostics<Flt64>(
                ConstraintEvaluations: [Evaluation(id, null)]),
        };

        IReadOnlyDictionary<ConstraintId, Flt64?> duals =
            LinearSolverFixedIntegerLpSolver.ExtractDuals(report, [id]);

        duals.Should().ContainKey(id, "the constraint has a unique row, so it is reported");
        duals[id].Should().BeNull("a backend that gave no dual is reported as absent, never as zero");
    }

    private static ConstraintEvaluation<Flt64> Evaluation(ConstraintId id, double? dual) =>
        new(
            id,
            new Flt64(1), new Flt64(1), SolveConstraintRelation.Equal,
            new Flt64(0), new Flt64(0), new Flt64(0),
            Satisfied: true,
            Dual: dual is null ? null : new Flt64(dual.Value));
}