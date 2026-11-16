#nullable enable

using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.Core.Solver.Function;

/// <summary>
/// 插件可探测的原生数学接口 / Native mathematical interfaces detectable by plugins.
/// </summary>
/// <remarks>
/// **与基线 `FunctionExpansion.kt:409` 的 `enum class FunctionNativeCapability` 的关系需要说准确：**
/// **基线那个枚举有 15 个成员——9 个是<b>能力</b>，6 个是<b>位置修饰</b>**
/// **（`ObjectiveOnly`／`ConstraintOnly`／`ObjectiveAndConstraint`／`Nested`／`External`／`Unused`）。**
/// **本枚举只有那 9 个能力；6 个修饰在 `FunctionUsageLocation` 里，那是合理的拆分。**
/// **所以下面这句话原来是"逐一对应，成员名与顺序一致"，而那会让下一个读者去核一个<b>永远不会相等的集合</b>。**
///
/// **The relation to the baseline's `enum class FunctionNativeCapability` must be stated exactly : the baseline enum
/// has 15 members — 9 <b>capabilities</b> and 6 <b>positional qualifiers</b> — while this one holds only the 9, with the
/// qualifiers living in `FunctionUsageLocation`.** **That split is reasonable; what was wrong was claiming the member sets
/// match one-to-one, which invites a reader to verify a set that can never be equal.**
///
/// **另外必须记住：这份表在本仓<b>还没有任何一端连着实际</b>** ——
/// **`src/` 里没有任何一处调用原生构造方法，`Supports(...)` 的生产调用方也为零。**
/// **它现在记录的只是"基线认为这些能力可以被原生表达"，而不是"本仓用得上"。**
///
/// **And remember: this table <b>touches reality at neither end</b> here — no native constructor is called anywhere in
/// `src/`, and `Supports(...)` has no production caller.** **It records what the baseline considers natively expressible, not
/// what this repo uses.**
/// </remarks>
public enum FunctionNativeCapability
{
    /// <summary>目标里的分段线性函数 / Piecewise-linear function in the objective.</summary>
    PiecewiseLinearObjective,

    /// <summary>约束里的分段线性函数 / Piecewise-linear function in a constraint.</summary>
    PiecewiseLinearConstraint,

    /// <summary>SOS1 约束 / An SOS1 constraint.</summary>
    Sos1,

    /// <summary>SOS2 约束 / An SOS2 constraint.</summary>
    Sos2,

    /// <summary>指示约束 / An indicator constraint.</summary>
    Indicator,

    /// <summary>一般绝对值 / A general absolute value.</summary>
    GeneralAbs,

    /// <summary>一般最大/最小 / A general max or min.</summary>
    GeneralMinMax,

    /// <summary>半连续变量 / A semi-continuous variable.</summary>
    SemiContinuous,

    /// <summary>二元逻辑 / Binary logic.</summary>
    BinaryLogic,
}

/// <summary>
/// 某个求解器**声称**支持的接口集合 / The set of interfaces a solver **claims** to support.
/// </summary>
/// <remarks>
/// 与基线 `FunctionExpansion.kt:483` 的 `data class FunctionSolverCapabilities` 对应。
///
/// **`Version` 可以为 `null`**，而且它必须可以为 `null`：SDK 不在时"不知道版本"和"版本是空字符串"
/// 是两件事，前者是**未探测**，后者是**探测到了空值**。把两者混起来，报告上就会写着"支持 0 项，版本 ''"，
/// 读者无法区分"这台机器没有 SDK"和"这个 SDK 什么都不支持"。
///
/// Corresponds to the baseline's `data class FunctionSolverCapabilities`. **`Version` may be `null`**, and it must be: with no SDK present, "the version is unknown"
/// and "the version is the empty string" are different facts — the first is **not probed**, the second is **probed and found empty**. Merging them would make a report
/// read "supports 0 capabilities, version ''", leaving the reader unable to tell "no SDK here" from "this SDK supports nothing".
/// </remarks>
/// <param name="Solver">求解器名 / Solver name.</param>
/// <param name="Version">SDK 版本；`null` 表示未探测 / SDK version; `null` means not probed.</param>
/// <param name="Supported">声称支持的接口 / The claimed capabilities.</param>
public sealed record FunctionSolverCapabilities(
    string Solver,
    string? Version,
    IReadOnlySet<FunctionNativeCapability> Supported)
{
    /// <summary>
    /// 是否支持指定接口 / Whether a capability is supported.
    /// </summary>
    /// <param name="capability">待检查的接口 / The capability to check.</param>
    /// <returns>是否支持 / Whether it is supported.</returns>
    /// <remarks>
    /// **纯集合查询，不做任何推断。** 没有"默认支持"这种东西：没列进 `Supported` 就是不支持。
    /// 这条约束是整个契约的意义所在——能力声明**宁可少说，不可多猜**。
    ///
    /// **A pure set lookup with no inference.** There is no such thing as "supported by default": anything not listed in `Supported` is unsupported. That constraint is the
    /// whole point of the contract — a capability declaration **may understate, never guess**.
    /// </remarks>
    public bool Supports(FunctionNativeCapability capability) => Supported.Contains(capability);
}
