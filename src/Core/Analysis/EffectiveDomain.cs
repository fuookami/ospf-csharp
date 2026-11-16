#nullable enable

using Fuookami.Ospf.Core.Model.ConstraintProgramming;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Math.Algebra.Number;
using Int64 = Fuookami.Ospf.Math.Algebra.Number.Int64;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Core.Analysis;

/// <summary>
/// 在给定活跃证据下的有效定义域 / The effective domain under a given set of active evidence.
/// </summary>
/// <remarks>
/// **这是 `TargetFeasibilityAnalyzer.kt` 的 `effectiveDomain` / `copyDomain` 的移植。**
///
/// **这段逻辑决定"某个边界是否真的在起作用"，而它有一个不容易看出来的不变量：**
///
/// | 情况 | 结果 | 为什么 |
/// | --- | --- | --- |
/// | 没有活跃证据集合 | 原样复制 | 没有证据就没有可移除的东西 |
/// | **`Boolean` 定义域** | **原样复制，永不放开** | **二值性是类型不变量，不是可移除的数值边界** |
/// | 区间域，边界活跃 | 保留该边界 | 它确实在起作用 |
/// | 区间域，边界不活跃 | **放开到整数全域** | 这条边界是"被假设的"，不是模型本身的 |
/// | 稀疏域，稀疏活跃 | 保留 | 值集本身在起作用 |
/// | 稀疏域，稀疏不活跃 | **降级成区间并放开边界** | **值集不成立时它连"哪些值合法"都不再是约束** |
///
/// **`Boolean` 那一行最容易写错**：把它当成"上界 1、下界 0"的区间，就会在边界不活跃时
/// **放开成全域**——于是一个布尔变量被允许取 42。**那不是一个数值边界被移除，那是一个类型被抹掉。**
///
/// **The `Boolean` row is the easiest to get wrong**: treating it as an interval with bounds 0 and 1 would, when the bounds are inactive, **open it to the whole integer range** — letting a
/// boolean variable take 42. **That is not a numeric bound being removed, that is a type being erased.**
/// </remarks>
public static class EffectiveDomain
{
    /// <summary>
    /// 计算有效定义域 / Compute the effective domain.
    /// </summary>
    /// <param name="domain">原始定义域 / The original domain.</param>
    /// <param name="variableId">变量身份 / The variable identity.</param>
    /// <param name="activeSources">活跃证据集合；<c>null</c> 表示不放开任何边界 / The active evidence, or <c>null</c> to release no bound.</param>
    /// <returns>有效定义域 / The effective domain.</returns>
    public static IntegerDomain Compute(
        IntegerDomain domain,
        VariableId variableId,
        IReadOnlySet<DiagnosticSource>? activeSources) {
        ArgumentNullException.ThrowIfNull(domain);

        // **没有活跃证据集合 = 不放开任何边界。**
        // **No active evidence means nothing is released.**
        if (activeSources is null) {
            return Copy(domain);
        }

        // **二值性是类型不变量，不是可移除的数值边界。**
        // **Binary-ness is a type invariant, not a removable numeric bound.**
        if (Equals(domain, IntegerDomain.Boolean)) {
            return Copy(domain);
        }

        bool lowerActive = activeSources.Contains(new DiagnosticSource.VariableLowerBound(variableId));
        bool upperActive = activeSources.Contains(new DiagnosticSource.VariableUpperBound(variableId));
        bool sparseActive = activeSources.Contains(new DiagnosticSource.SparseDomain(variableId));

        return domain switch {
            IntegerDomain.Interval => new IntegerDomain.Interval(
                lowerActive ? domain.LowerBound : Int64Bound.Minimum,
                upperActive ? domain.UpperBound : Int64Bound.Maximum),

            // **值集不活跃时降级成区间**：它连"哪些值合法"都不再是约束，
            // **因此只剩边界还可谈，而边界也按各自的活跃性放开。**
            // **An inactive value set degrades to an interval**: it no longer constrains which values are legal, **so only the bounds remain to discuss, and they are released by their own
            // activity.**
            IntegerDomain.Values => sparseActive
                ? Copy(domain)
                : new IntegerDomain.Interval(
                    lowerActive ? domain.LowerBound : Int64Bound.Minimum,
                    upperActive ? domain.UpperBound : Int64Bound.Maximum),

            _ => Copy(domain),
        };
    }

    /// <summary>
    /// 原样复制一个定义域 / Copy a domain as-is.
    /// </summary>
    /// <param name="domain">定义域 / The domain.</param>
    /// <returns>复制 / The copy.</returns>
    /// <remarks>
    /// **复制而不是返回同一个实例**：`IntegerDomain.Values` 持有数组，
    /// 共享实例会让下游的"只读"变成"随时可能被别人改"。
    ///
    /// **A copy, not the same instance**: `IntegerDomain.Values` holds an array, and sharing the instance would turn a downstream "read-only" into "someone else may change it".
    /// </remarks>
    public static IntegerDomain Copy(IntegerDomain domain) {
        ArgumentNullException.ThrowIfNull(domain);

        return domain switch {
            IntegerDomain.Values values => new IntegerDomain.Values(values.Items.ToArray()),
            IntegerDomain.Interval => new IntegerDomain.Interval(domain.LowerBound, domain.UpperBound),
            _ => domain,
        };
    }

    /// <summary>整数全域的两个端点 / The two endpoints of the integer universe.</summary>
    private static class Int64Bound
    {
        /// <summary>最小整数 / The smallest integer.</summary>
        public static readonly Int64 Minimum = new(long.MinValue);

        /// <summary>最大整数 / The largest integer.</summary>
        public static readonly Int64 Maximum = new(long.MaxValue);
    }

    /// <summary>
    /// 每个变量的有效定义域 / The effective domain of every variable.
    /// </summary>
    /// <param name="snapshot">模型快照 / The model snapshot.</param>
    /// <param name="activeSources">活跃证据集合，可为 <c>null</c> / The active evidence, or <c>null</c>.</param>
    /// <returns>变量到有效定义域的映射 / A map from variable to effective domain.</returns>
    /// <remarks>
    /// **这是 `activeDomains` 的移植，而它此前在 C# 侧没有任何对应物**——
    /// **`Compute` 本身此后就被判据覆盖，却一个调用方都没有。**
    ///
    /// **This is the port of `activeDomains`, and it had no counterpart on the C# side** — **`Compute` had been covered by criteria since then while having no caller at all.**
    /// </remarks>
    public static IReadOnlyDictionary<VariableId, IntegerDomain> ActiveDomains(
        ConstraintProgrammingModelSnapshot snapshot,
        IReadOnlySet<DiagnosticSource>? activeSources) {
        ArgumentNullException.ThrowIfNull(snapshot);

        return snapshot.Variables.ToDictionary(
            variable => variable.Id,
            variable => Compute(variable.Domain, variable.Id, activeSources));
    }

    /// <summary>
    /// 把变量表达式节点换成有效定义域 / Rebind a variable expression node to its effective domain.
    /// </summary>
    /// <param name="expression">表达式 / The expression.</param>
    /// <param name="domains">有效定义域 / The effective domains.</param>
    /// <returns>重绑定后的表达式 / The rebound expression.</returns>
    /// <remarks>
    /// **值域必须写进 AST，不能只写在注册表里。** 求解器读的是表达式节点上的值域；
    /// **只改注册表会让"取消激活一个边界来源"在求解时完全不起作用**——
    /// **于是删除复验永远得到同一个不可行答案，每一个边界来源都显得"必要"。**
    ///
    /// **The domain has to land in the AST, not only in the registry.** The solver reads the domain on the expression node;
    /// **changing only the registry makes deactivating a bound source have no effect at solve time** —
    /// **so every deletion check returns the same infeasible answer and every bound source looks necessary.**
    /// </remarks>
    public static ConstraintProgrammingExpression RebindDomain(
        ConstraintProgrammingExpression expression,
        IReadOnlyDictionary<VariableId, IntegerDomain> domains) {
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentNullException.ThrowIfNull(domains);

        // **只有 `Variable` 节点自带值域**；`Constant` 没有变量，`Linear` 的项只带变量引用。
        // **Only `Variable` nodes carry a domain**; `Constant` has no variables and a `Linear` term carries only a variable reference.
        return expression is ConstraintProgrammingExpression.Variable variable
            && variable.Id is { } id
            && domains.TryGetValue(id, out IntegerDomain? effective)
            ? new ConstraintProgrammingExpression.Variable(variable.VariableItem, effective, id)
            : expression;
    }
}
