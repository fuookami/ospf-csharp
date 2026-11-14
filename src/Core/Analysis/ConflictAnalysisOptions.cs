#nullable enable

using Fuookami.Ospf.Core.Model.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.Config;
using Fuookami.Ospf.Core.Solver.ConstraintProgramming;
using System;

namespace Fuookami.Ospf.Core.Analysis;

/// <summary>控制基于删除的 MUS 复验选项 / Options controlling deletion-based MUS verification.</summary>
/// <param name="SolveOptions">传递给每次 target 检查的 CP 求解选项 / CP solve options forwarded to every target check.</param>
/// <param name="MaxDeletionChecks">删除复验次数上限；零表示保留完整已证明冲突 / Maximum deletion checks; zero keeps the full proven conflict unshrunk.</param>
/// <param name="TimeBudget">target 与删除复验的墙钟预算 / Wall-clock budget for target plus deletion checks.</param>
public sealed record ConflictAnalysisOptions(
    ConstraintProgrammingSolveOptions? SolveOptions = null,
    int MaxDeletionChecks = int.MaxValue,
    TimeSpan? TimeBudget = null)
{
    /// <summary>CP 求解选项，未给出时取默认 / The CP solve options, defaults when omitted.</summary>
    public ConstraintProgrammingSolveOptions SolveOptionsValue => SolveOptions ?? new ConstraintProgrammingSolveOptions();

    /// <summary>
    /// 校验选项 / Validate the options.
    /// </summary>
    /// <returns>问题描述，合法时为 <c>null</c> / The problem, or <c>null</c> when well formed.</returns>
    /// <remarks>
    /// **零不在这些检查里，而且是故意留的**：`MaxDeletionChecks = 0` 是"保留完整已证明冲突、不缩减"的合法请求，
    /// 不是"没配"。**把它当非法拒掉，会让调用方拿不到一份本来能给的完整冲突。**
    ///
    /// **`TimeBudget = 0` 同样合法**：预算为零就是"不花时间"，它和"预算为负"是两件事，
    /// **而只有后者是配置错误。**
    ///
    /// **Zero is deliberately absent from these checks**: `MaxDeletionChecks = 0` is a legitimate request to "keep the full proven conflict unchanged", not a missing setting.
    /// **Rejecting it as invalid would deny the caller a complete conflict it could have had.** **`TimeBudget = 0` is legal for the same reason**: a zero budget means "spend no
    /// time", which is a different thing from a negative budget — **and only the latter is a configuration error.**
    /// </remarks>
    public string? Validate() {
        if (MaxDeletionChecks < 0) {
            return "删除复验次数不得为负 / Maximum deletion checks must not be negative";
        }
        if (TimeBudget is { } budget && budget < TimeSpan.Zero) {
            return "冲突分析时间预算不得为负 / Conflict analysis time budget must not be negative";
        }
        return null;
    }
}
