// 通用路径主问题类型 / Generic path master-problem types.
// 与固定 Kotlin 基线 `path_compilation/PathCompilationContext.kt`（5133 字节）中的问题类型部分对应。
// Mirrors the problem types of the pinned baseline's `path_compilation/PathCompilationContext.kt`.

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Domain.PathCompilation;

/// <summary>
/// 通用主问题行；相同上下界表示精确覆盖 / A generic master row; equal bounds mean exact coverage.
/// </summary>
/// <remarks>
/// **`Lower` 与 `Upper` 相等是"恰好一次"，不是"至多一次"**。把它当区间处理会让覆盖行松弛成不等式，同一需求被多列重复满足却不被
/// 惩罚，主问题成本因此被摊薄，pricer 找不到真正缺失的列。
/// **`Lower` equal to `Upper` means "exactly once", not "at most once".** Treating it as an interval relaxes the coverage row into
/// an inequality, several columns satisfy the same demand without penalty, the master cost gets diluted, and the pricer never sees
/// which column is genuinely missing.
/// **上下界都必须与 `Unit` 同量纲**。混量纲时规范化要么静默换算错，要么直接抛异常——前者更危险。
/// **Both bounds must share `Unit`'s dimension.** With mixed dimensions normalisation either converts wrongly in silence or throws;
/// the former is the dangerous one.
/// </remarks>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <param name="Id">稳定行标识 / Stable row identity.</param>
/// <param name="Unit">系数与边界的计量单位 / The unit of coefficients and bounds.</param>
/// <param name="Lower">可选下界 / The optional lower bound.</param>
/// <param name="Upper">可选上界 / The optional upper bound.</param>
public sealed record PathCompilationRow<V>(
    string Id,
    PhysicalUnit Unit,
    Quantity<V>? Lower = null,
    Quantity<V>? Upper = null) where V : struct, IRealNumber<V>;

/// <summary>
/// 一条不可拆候选路径，使用一个二元变量选择 / An indivisible candidate path selected by one binary variable.
/// </summary>
/// <remarks>
/// **`Id` 相同必须代表相同载荷**。同 ID 不同载荷会让去重把两个真正不同的候选当成同一个，其中一个从此消失，而调用方以为它还在候选池里。
/// **Equal `Id` must denote an equal payload.** Same ID with a different payload makes deduplication collapse two genuinely
/// different candidates into one; the other silently disappears while the caller believes it is still in the pool.
/// **`Cost` 是完整费用，允许起止与非可加贡献**，而不是各段之和。让调用方自己拼装分段费用，会让"最后一公里"之类的固定成本在
/// 增量更新时被漏算。
/// **`Cost` is the full cost, including start, terminal and non-additive contributions**, not a sum of segments. Making the caller
/// assemble it from segments is how fixed costs such as the last mile get dropped during incremental updates.
/// </remarks>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <typeparam name="C">完整路径载荷类型 / Complete path payload type.</typeparam>
public sealed class PathColumn<V, C> where V : struct, IRealNumber<V> {
    /// <summary>
    /// 构造一条候选路径 / Construct a candidate path.
    /// </summary>
    /// <param name="Id">稳定列标识 / Stable column identity.</param>
    /// <param name="Payload">完整业务路径 / The complete business path.</param>
    /// <param name="Cost">完整费用 / The full cost.</param>
    /// <param name="Coefficients">按行标识汇总的完整用量 / Complete usage by row identity.</param>
    public PathColumn(
        string Id,
        C Payload,
        Quantity<V> Cost,
        IReadOnlyDictionary<string, Quantity<V>> Coefficients) {
        ArgumentNullException.ThrowIfNull(Id);
        ArgumentNullException.ThrowIfNull(Cost);
        ArgumentNullException.ThrowIfNull(Coefficients);
        this.Id = Id;
        this.Payload = Payload;
        this.Cost = Cost;
        this.Coefficients = Coefficients.ToDictionary(
            pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    }

    /// <summary>稳定列标识 / Stable column identity.</summary>
    public string Id { get; }

    /// <summary>完整业务路径 / The complete business path.</summary>
    public C Payload { get; }

    /// <summary>完整费用 / The full cost.</summary>
    public Quantity<V> Cost { get; }

    /// <summary>按行标识汇总的完整用量 / Complete usage by row identity.</summary>
    public IReadOnlyDictionary<string, Quantity<V>> Coefficients { get; }
}

/// <summary>
/// 不持有模型状态的有限候选主问题 / A finite-candidate master problem that holds no model state.
/// </summary>
/// <remarks>
/// **问题快照不可变**。注册态必须与一份快照一一对应；若快照可变，两次求解之间的问题会悄悄变化，而缓存的对偶与列变量仍然指向旧版本。
/// **The problem snapshot is immutable.** Registration state must map one-to-one to a snapshot; a mutable snapshot would let the
/// problem change silently between solves while cached duals and column variables still refer to the old version.
/// </remarks>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <typeparam name="C">完整路径载荷类型 / Complete path payload type.</typeparam>
public sealed class PathCompilationProblem<V, C> where V : struct, IRealNumber<V> {
    /// <summary>
    /// 构造有限候选主问题 / Construct the finite-candidate master problem.
    /// </summary>
    /// <param name="Rows">需求、配额、容量或服务覆盖行 / Demand, quota, capacity or service-coverage rows.</param>
    /// <param name="Columns">初始候选 / The initial candidates.</param>
    /// <param name="CostUnit">费用口径 / The cost unit.</param>
    public PathCompilationProblem(
        IReadOnlyList<PathCompilationRow<V>> Rows,
        IReadOnlyList<PathColumn<V, C>> Columns,
        PhysicalUnit CostUnit) {
        ArgumentNullException.ThrowIfNull(Rows);
        ArgumentNullException.ThrowIfNull(Columns);
        ArgumentNullException.ThrowIfNull(CostUnit);
        this.Rows = Rows.ToList();
        this.Columns = Columns.ToList();
        this.CostUnit = CostUnit;
    }

    /// <summary>需求、配额、容量或服务覆盖行 / Demand, quota, capacity or service-coverage rows.</summary>
    public IReadOnlyList<PathCompilationRow<V>> Rows { get; }

    /// <summary>初始候选 / The initial candidates.</summary>
    public IReadOnlyList<PathColumn<V, C>> Columns { get; }

    /// <summary>费用口径 / The cost unit.</summary>
    public PhysicalUnit CostUnit { get; }
}

/// <summary>
/// 提取的有限候选集结果，不包含全图最优证明 / The extracted finite-pool result, without a full-network optimality proof.
/// </summary>
/// <remarks>
/// **`CandidateIds` 必须随结果一起返回**。solver 的"最优"只对本轮候选池成立；不给候选范围，调用方会把有限池最优误当成全图最优。
/// **`CandidateIds` must travel with the result.** The solver's "optimal" holds only for this round's candidate pool; without the
/// candidate scope the caller mistakes finite-pool optimality for full-network optimality.
/// </remarks>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <typeparam name="C">完整路径载荷类型 / Complete path payload type.</typeparam>
/// <param name="Selected">选中的完整路径 / The selected complete paths.</param>
/// <param name="Cost">总费用 / The total cost.</param>
/// <param name="Usage">行用量 / Row usage.</param>
/// <param name="CandidateIds">本次最终候选范围 / The final candidate scope.</param>
public sealed record PathCompilationSolution<V, C>(
    IReadOnlyList<PathColumn<V, C>> Selected,
    Quantity<V> Cost,
    IReadOnlyDictionary<string, Quantity<V>> Usage,
    IReadOnlySet<string> CandidateIds) where V : struct, IRealNumber<V>;
