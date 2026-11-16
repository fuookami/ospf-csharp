// 路线扩展主问题贡献 / Extra master-problem contributions for route compilation.
// 与固定 Kotlin （4307 字节）对应。

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.RouteCompilation.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Pricing;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<
    Fuookami.Ospf.Utils.Functional.Success,
    Fuookami.Ospf.Utils.Error.ErrorCode,
    Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Domain.RouteCompilation;

/// <summary>
/// 路线扩展主问题中的单列视图 / A single-column view for route master-problem extensions.
/// </summary>
/// <remarks>
/// **同一份系数保留两种表示：领域值与 solver 边界值**。领域值用于与业务规则比较（单位可读、量纲正确），solver 边界值用于直接装配
/// 到主问题。只留一种会强制每次装配都重新归一化，而归一化是有损的——反复归一化会让同一列在不同批次里得到略有不同的系数。
/// **The same coefficients are kept in two representations: domain values and solver-boundary values.** Domain values are for comparing
/// against business rules (readable units, right dimensions); solver-boundary values go straight into the master problem. Keeping only
/// one forces re-normalisation at every assembly, and normalisation is lossy — repeated normalisation gives the same column slightly
/// different coefficients in different batches.
/// </remarks>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed class RouteCompilationColumn<V> where V : struct, IRealNumber<V> {
    /// <summary>
    /// 构造单列视图 / Construct the single-column view.
    /// </summary>
    /// <param name="route">路线 / The route.</param>
    /// <param name="variable">对应的 RMP 列变量 / The corresponding RMP column variable.</param>
    /// <param name="coefficients">按扩展行键汇总的领域系数 / Domain coefficients keyed by extra-row identity.</param>
    /// <param name="solverCoefficients">同一系数的 solver 边界值 / Solver-boundary values of the same coefficients.</param>
    public RouteCompilationColumn(
        Route<V> route,
        LinearPolynomial<Flt64> variable,
        IReadOnlyDictionary<string, V> coefficients,
        IReadOnlyDictionary<string, Flt64> solverCoefficients) {
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(variable);
        ArgumentNullException.ThrowIfNull(coefficients);
        ArgumentNullException.ThrowIfNull(solverCoefficients);
        Route = route;
        Variable = variable;
        Coefficients = coefficients.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        SolverCoefficients = solverCoefficients.ToDictionary(
            pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    }

    /// <summary>路线 / The route.</summary>
    public Route<V> Route { get; }

    /// <summary>对应的 RMP 列变量 / The corresponding RMP column variable.</summary>
    public LinearPolynomial<Flt64> Variable { get; }

    /// <summary>按扩展行键汇总的领域系数 / Domain coefficients keyed by extra-row identity.</summary>
    public IReadOnlyDictionary<string, V> Coefficients { get; }

    /// <summary>同一系数的 solver 边界值 / Solver-boundary values of the same coefficients.</summary>
    public IReadOnlyDictionary<string, Flt64> SolverCoefficients { get; }
}

/// <summary>
/// VRPTW 额外主问题贡献的列生成生命周期扩展 / Lifecycle extension for extra VRPTW master-problem contributions.
/// </summary>
/// <remarks>
/// **所有生命周期钩子都有空默认实现**，扩展只需覆写它真正关心的一两个。这不是"方便"，而是让"这个扩展不参与某一阶段"成为显式的正常状态，
/// 而不是靠调用方传一堆 `null` 来绕开。
/// **Every lifecycle hook has an empty default**, so an extension overrides only the one or two it actually cares about. That is not
/// mere convenience: it makes "this extension does not take part in that phase" an explicit normal state instead of something the
/// caller dodges by passing a pile of `null`s.
/// **`Name` 同时是提取结果键**，因此必须在同一上下文内唯一；重名会让两个扩展的结果互相覆盖，而调用方只看得到其中一个。
/// **`Name` doubles as the extraction-result key**, so it must be unique within one context; a duplicate makes two extensions
/// overwrite each other's results and the caller only ever sees one of them.
/// </remarks>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public interface IRouteCompilationContribution<V> : IRouteArcCoefficientProvider<V>
    where V : struct, IRealNumber<V> {
    /// <summary>稳定扩展名称，同时用于提取结果键 / Stable extension name, also its extraction-result key.</summary>
    string Name { get; }

    /// <summary>
    /// 注册静态主问题结构 / Register the static master-problem structure.
    /// </summary>
    /// <param name="compilation">基础路线编译模型 / The base route compilation model.</param>
    /// <param name="model">当前线性主问题 / The current linear master problem.</param>
    /// <returns>操作结果 / The operation result.</returns>
    Try Register(RouteCompilation<V> compilation, LinearMetaModel<Flt64> model) =>
        Results.Ok(Results.SuccessInstance);

    /// <summary>
    /// 为新增路线列更新扩展表达式 / Update extra expressions for newly added route columns.
    /// </summary>
    /// <param name="columns">新增路线列及其共享系数 / The new route columns and their shared coefficients.</param>
    /// <param name="model">当前线性主问题 / The current linear master problem.</param>
    /// <returns>操作结果 / The operation result.</returns>
    Try AddColumns(IReadOnlyList<RouteCompilationColumn<V>> columns, LinearMetaModel<Flt64> model) =>
        Results.Ok(Results.SuccessInstance);

    /// <summary>
    /// 移除路线列对扩展表达式的贡献 / Remove route-column contributions from extra expressions.
    /// </summary>
    /// <param name="columns">被移除的路线列及其原系数 / The removed route columns and their original coefficients.</param>
    /// <param name="model">当前线性主问题 / The current linear master problem.</param>
    /// <returns>操作结果 / The operation result.</returns>
    Try RemoveColumns(IReadOnlyList<RouteCompilationColumn<V>> columns, LinearMetaModel<Flt64> model) =>
        Results.Ok(Results.SuccessInstance);

    /// <summary>
    /// 从当前主问题对偶解提取扩展定价行的对偶快照 / Extract the current dual snapshot for extra pricing rows.
    /// </summary>
    /// <param name="model">当前线性主问题 / The current linear master problem.</param>
    /// <param name="shadowPrices">solver 对偶解 / The solver dual solution.</param>
    /// <returns>行稳定键到 solver 有符号对偶的映射 / Stable row IDs mapped to signed solver duals.</returns>
    Result<IReadOnlyDictionary<string, Flt64>, ErrorCode, Error<ErrorCode>> RefreshShadowPrices(
        LinearMetaModel<Flt64> model,
        MetaDualSolution shadowPrices) =>
        Results.Ok<IReadOnlyDictionary<string, Flt64>>(new Dictionary<string, Flt64>());

    /// <summary>
    /// 提取扩展结果；调用方按 <see cref="Name"/> 收集异构结果 / Extract the extension result; callers collect heterogeneous
    /// values by <see cref="Name"/>.
    /// </summary>
    /// <param name="model">最终线性主问题 / The final linear master problem.</param>
    /// <param name="routeValues">路线列及其解值 / Route columns and their solution values.</param>
    /// <returns>扩展结果或提取失败 / The extension result, or an extraction failure.</returns>
    Result<object?, ErrorCode, Error<ErrorCode>> Extract(
        LinearMetaModel<Flt64> model,
        IReadOnlyList<(Route<V> Route, Flt64 Value)> routeValues) =>
        Results.Ok<object?>(null);
}
