// 执行器目标项的代价 / Cost of an executor objective term.
// 与`task_compilation/Service/...` 的 与
// 对应：两者的判定结构完全相同，只差被罚的列。
// And, whose decision structure
// is identical and differs only in the penalised column.

using System;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Service.Limits;

/// <summary>
/// 执行器目标项的代价 / Cost of an executor objective term.
/// </summary>
/// <remarks>
/// 系数提取器可以缺席、可以给出 `null`、也可以给出数值。这里有**三种状态**，
/// 而它们看起来只差一个 `?`：
///
/// | 情形 | 含义 | 行为 |
/// | --- | --- | --- |
/// | 提取器本身为 `null` | 没有配置这项惩罚 | **什么都不加** |
/// | 提取器给出 `null` | 这个执行器的该项**不可定价** | 视为无穷，即禁止 |
/// | 提取器给出数值 | 正常 | 该数值 |
///
/// 把前两种混为一谈会出两种相反的错：
/// 把"没配置"当成"禁止"，一个没打算惩罚任何人的调用方会**让所有执行器都不可选**；
/// 把"不可定价"当成"权重一"，本该禁止的选项变得很便宜。
///
/// An absent extractor, one that yields `null` and one that yields a number: there are **three states** that differ by one `?`:
///
/// | Case | Meaning | Behaviour |
/// | --- | --- | --- |
/// | The extractor itself is `null` | this penalty was not configured | **add nothing at all** |
/// | The extractor yields `null` | this executor's term **cannot be priced** | treat as infinity, i.e. forbid |
/// | The extractor yields a number | normal | that number |
///
/// Conflating the first two produces two opposite errors: treating "not configured" as "forbid" makes a caller who never intended to penalise anyone **render every executor
/// unusable**; treating "unpriced" as "weight one" makes an option that should be forbidden very cheap.
/// </remarks>
public static class ExecutorObjectiveTerm {
    /// <summary>
    /// 判断是否应当加入这一项目标 / Whether this objective term should be added at all.
    /// </summary>
    /// <param name="hasExtractor">是否配置了提取器 / Whether an extractor is configured.</param>
    /// <param name="globalGate">全局开关 / The global switch.</param>
    /// <returns>是否加入 / Whether to add it.</returns>
    /// <remarks>
    /// **没配置提取器就不加目标**。加一个空目标不会算错，但会让"目标是否存在"这件事变得无法判断，
    /// 而求解器会把它当成一条恒为零的目标去处理——日志里于是出现一条看起来生效、实际什么都没约束的记录。
    ///
    /// **No extractor means no objective.** Adding an empty objective does not compute anything wrong, but it makes "is the objective present" undecidable, and the solver treats
    /// it as an identically-zero objective — leaving a log line that looks effective while constraining nothing.
    /// </remarks>
    public static bool ShouldAdd(bool hasExtractor, bool globalGate) => hasExtractor && globalGate;

    /// <summary>
    /// 计算一个执行器的权重 / Compute one executor's weight.
    /// </summary>
    /// <param name="value">提取到的权重 / The extracted weight.</param>
    /// <returns>权重或失败 / The weight, or a failure.</returns>
    public static Result<Flt64, ErrorCode, Error<ErrorCode>> Weight(Flt64? value) {
        if (value is not { } weight) {
            return Results.Failed<Flt64>(new Err<ErrorCode>(
                ErrorCode.ApplicationError,
                "执行器权重提取失败，该执行器的此项不可定价 / Executor weight extraction failed; this term cannot be priced for this executor"));
        }
        return Results.Ok(weight);
    }
}
