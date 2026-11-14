#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Core.Model.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.ConstraintProgramming;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Core.Symbol.Function;

/// <summary>
/// 全局约束函数的统一入口 / One entry point for the global constraint functions.
/// </summary>
/// <remarks>
/// B1177 按基线 `GlobalConstraintFunctions.kt` 补。
///
/// **这是一个<b>纯外观</b>：每一个方法都只是把调用转给已存在的实现。**
/// **所以它的价值不在逻辑，而在"顺序"与"校验点"。**
///
/// **三个入口各自转给自己的 `Create`，而三者都做了<b>同样的</b>非空检查。**
/// **我最初以为只有 `AllDifferent` 查、另外两个不查——那是猜的，跑出来三条都拒绝空输入。**
/// **门面这里<b>不重复</b>那层检查：重复只会让错误消息多一层翻译，
/// 而"三处一致"这个事实由判据统一钉住。**
///
/// **`Lower` 的两个重载不是重复**：**一个收可变模型、一个收不可变快照。**
/// **收模型的那个在内部先 `Snapshot()` 再转给收快照的那个——
/// 所以"从模型降阶"和"从快照降阶"走的是<b>同一条</b>实现，
/// 而不是两份可能漂移的代码。**
///
/// **`Policy` 默认 `Strict`**：**宽松策略会跳过一些校验，
/// 而"默认宽松"会让调用方在不知情的情况下拿到一个未经校验的模型。**
///
/// B1177, following the baseline's `GlobalConstraintFunctions`.
///
/// **This is a <b>pure façade</b>: every method forwards to an implementation that already exists.**
/// **Its value is therefore not logic but <b>ordering and validation points</b>.**
///
/// **Each entry point forwards to its own `Create`, and all three perform <b>the same</b> non-emptiness check.**
/// **I first believed only `AllDifferent` checked and the other two did not — a guess; all three reject empty input.**
/// **The façade does <b>not</b> repeat that check, since repeating it would only add a translation layer to every error
/// message, and one criterion pins down the fact that the three agree.**
///
/// **The two `Lower` overloads are not duplicates**: **one takes a mutable model, one an immutable snapshot.**
/// **The model overload calls `Snapshot()` and delegates — so both routes share <b>one</b> implementation rather than
/// two that can drift apart.**
///
/// **`Policy` defaults to `Strict`**: **a relaxed policy skips some checks, and "relaxed by default" hands callers an
/// unvalidated model without telling them.**
/// </remarks>
public static class GlobalConstraintFunctions
{
    /// <summary>全异约束 / An all-different constraint.</summary>
    /// <param name="expressions">待比较表达式 / Expressions to compare.</param>
    public static Result<ConstraintProgrammingConstraint.AllDifferent, ErrorCode, Error<ErrorCode>>
        AllDifferent(IEnumerable<ConstraintProgrammingExpression> expressions) {
        ArgumentNullException.ThrowIfNull(expressions);
        return ConstraintProgrammingConstraint.CreateAllDifferent(expressions);
    }

    /// <summary>不重叠约束 / A no-overlap constraint.</summary>
    /// <param name="intervals">区间 / The intervals.</param>
    public static Result<NoOverlap, ErrorCode, Error<ErrorCode>> NoOverlap(
        IEnumerable<IntervalVariable> intervals) {
        ArgumentNullException.ThrowIfNull(intervals);
        return Model.ConstraintProgramming.NoOverlap.Create(intervals.ToList());
    }

    /// <summary>累积约束 / A cumulative constraint.</summary>
    /// <param name="intervals">区间 / The intervals.</param>
    /// <param name="demands">每段需求 / The demand per interval.</param>
    /// <param name="capacity">总容量 / The total capacity.</param>
    public static Result<Cumulative, ErrorCode, Error<ErrorCode>> Cumulative(
        IEnumerable<IntervalVariable> intervals,
        IEnumerable<ConstraintProgrammingExpression> demands,
        ConstraintProgrammingExpression capacity) {
        ArgumentNullException.ThrowIfNull(intervals);
        ArgumentNullException.ThrowIfNull(demands);
        ArgumentNullException.ThrowIfNull(capacity);
        return Model.ConstraintProgramming.Cumulative.Create(
            intervals.ToList(), demands.ToList(), capacity);
    }

    /// <summary>把 CP 模型降阶为线性模型 / Lower a CP model to a linear model.</summary>
    /// <param name="model">CP 模型 / The CP model.</param>
    /// <param name="policy">降阶策略；`null` 表示严格 / The lowering policy; `null` means strict.</param>
    public static Result<ConstraintProgrammingLoweredLinearModel, ErrorCode, Error<ErrorCode>> Lower(
        ConstraintProgrammingModel model,
        ConstraintProgrammingLoweringPolicy? policy = null) {
        ArgumentNullException.ThrowIfNull(model);

        // **`ConstraintProgrammingLoweringPolicy` 是记录而不是枚举，所以不能当默认参数值——
        // 默认值必须是编译时常量。`null` 表示"用严格策略"。**
        //
        // **`ConstraintProgrammingLoweringPolicy` is a record rather than an enum, so it cannot be a default
        // parameter value; `null` means "use the strict policy".**
        return new ConstraintProgrammingToLinearModelLowerer(
            policy ?? ConstraintProgrammingLoweringPolicy.Strict).Lower(model);
    }

    /// <summary>把 CP 快照降阶为线性模型 / Lower a CP snapshot to a linear model.</summary>
    /// <param name="snapshot">CP 快照 / The CP snapshot.</param>
    /// <param name="policy">降阶策略；`null` 表示严格 / The lowering policy; `null` means strict.</param>
    public static Result<ConstraintProgrammingLoweredLinearModel, ErrorCode, Error<ErrorCode>> Lower(
        ConstraintProgrammingModelSnapshot snapshot,
        ConstraintProgrammingLoweringPolicy? policy = null) {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new ConstraintProgrammingToLinearModelLowerer(
            policy ?? ConstraintProgrammingLoweringPolicy.Strict).Lower(snapshot);
    }
}
