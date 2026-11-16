#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;
using Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using StowageSolution = Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model.Solution;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Service;

/// <summary>
/// 方案分析器 / The solution analyzer.
/// </summary>
/// <remarks>
/// 从求解器的**原始解值向量**里提取配载域方案。三张表：
///
/// | 表 | 来源 | 判定 |
/// | --- | --- | --- |
/// | 货物分配 | `assignments` 的每个二元变量 | 值**大于 0.5** 即视为已装 |
/// | 谓词装载重量 | `y_j` | 值**大于 0** 才收录 |
/// | 推荐装载重量 | `z_j` | 值**大于 0** 才收录 |
///
/// 四处必须照做：
///
/// 1. **阈值是 `0.5`，不是 `0`**。二元变量在数值上可能回来 `1e-9` 或 `0.999`，用 `> 0` 会把浮点噪声当成"装了"。
/// 2. **`y` 与 `z` 的单位是变量自带的**（`aircraftModel.weightUnit`），不是 `mlw` 的单位。
/// 3. **`z` 是整数数量，却按重量输出**——`z` 是件数变量，分析器却把它转成重量单位。沿用：这是声明 `z` 时带 `weightUnit`
///    造成的**语义缺陷**，此处保留并记录，不擅自"修好"。
/// 4. **token 缺失是失败**，不是 0。折成 0 会造出"什么都没装"的方案，而那个方案**在下游可能看起来可行**。
///
/// Three tables extracted from the solver's **raw value vector**, tabulated above. Four things must be copied verbatim: **the threshold is `0.5`, not
/// `0`** (a binary variable may come back as `1e-9` or `0.999`, and `> 0` would read float noise as "loaded"); **`y` and `z` carry the variable's own unit**
/// (`aircraftModel.weightUnit`), not `mlw`'s; **`z` is an integer count yet is emitted as a weight** — it is declared as a piece-count variable and then converted to a
/// weight unit, a **semantic defect** kept as-is and recorded rather than silently "fixed"; and **a missing token is a failure, not a zero**, since folding to zero fabricates a
/// "loads nothing" solution that may look feasible downstream.
/// </remarks>
public sealed class SolutionAnalyzer
{
    private readonly StowageAggregation _aggregation;

    /// <summary>
    /// 构造方案分析器 / Construct the solution analyzer.
    /// </summary>
    /// <param name="aggregation">配载聚合 / The stowage aggregation.</param>
    public SolutionAnalyzer(StowageAggregation aggregation)
    {
        ArgumentNullException.ThrowIfNull(aggregation);
        _aggregation = aggregation;
    }

    /// <summary>
    /// 从求解器解值中提取配载方案 / Extract the stowage solution from the solver's values.
    /// </summary>
    /// <param name="solution">求解器解值 / The solver values.</param>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>配载方案或失败 / The stowage solution or a failure.</returns>
    public Result<StowageSolution, ErrorCode, Error<ErrorCode>> Invoke(
        IReadOnlyList<Flt64> solution,
        LinearMetaModel<Flt64> model)
    {
        ArgumentNullException.ThrowIfNull(solution);
        ArgumentNullException.ThrowIfNull(model);

        var assignments = new List<ItemAssignment>();
        if (_aggregation.Stowage.LoadingContext is { } context)
        {
            foreach (KeyValuePair<Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading.Model.AssignmentKey, BinVar> pair
                in context.Aggregation.Assignments)
            {
                Item? item = FindItem(pair.Key.ItemId);
                StowagePosition? position = FindPosition(pair.Key.PositionId);
                if (item is null || position is null)
                {
                    return Results.Failed<StowageSolution>(new Err<ErrorCode>(
                        ErrorCode.DataNotFound,
                        "解中出现未知的货物或舱位 / the solution names an unknown item or position: "
                        + pair.Key.ItemId + "->" + pair.Key.PositionId));
                }
                Result<Flt64, ErrorCode, Error<ErrorCode>> value = SolverValue(
                    solution, model, pair.Value, pair.Key.ItemId + "->" + pair.Key.PositionId);
                if (!value.IsOk)
                {
                    return Rethrow<StowageSolution, Flt64>(value);
                }
                // 阈值 `0.5`：二元变量回 `0.999` 也算装，回 `1e-9` 也算没装。
                // The `0.5` threshold: `0.999` counts as loaded and `1e-9` does not.
                if (((Ok<Flt64, ErrorCode, Error<ErrorCode>>)value).Value.ToDouble() > 0.5)
                {
                    assignments.Add(new ItemAssignment(item, position, Stowed: true));
                }
            }
        }

        Result<IReadOnlyDictionary<StowagePosition, Quantity<Flt64>>, ErrorCode, Error<ErrorCode>> predicate =
            CollectWeights(solution, model, recommended: false);
        if (!predicate.IsOk)
        {
            return Rethrow<StowageSolution, IReadOnlyDictionary<StowagePosition, Quantity<Flt64>>>(predicate);
        }
        Result<IReadOnlyDictionary<StowagePosition, Quantity<Flt64>>, ErrorCode, Error<ErrorCode>> recommended =
            CollectWeights(solution, model, recommended: true);
        if (!recommended.IsOk)
        {
            return Rethrow<StowageSolution, IReadOnlyDictionary<StowagePosition, Quantity<Flt64>>>(recommended);
        }

        return Results.Ok<StowageSolution>(new StowageSolution(
            assignments,
            ((Ok<IReadOnlyDictionary<StowagePosition, Quantity<Flt64>>, ErrorCode, Error<ErrorCode>>)predicate).Value,
            ((Ok<IReadOnlyDictionary<StowagePosition, Quantity<Flt64>>, ErrorCode, Error<ErrorCode>>)recommended).Value));
    }

    /// <summary>
    /// 收集一族装载重量 / Collect one family of load weights.
    /// </summary>
    /// <param name="solution">求解器解值 / The solver values.</param>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <param name="recommended">`true` 取 `z`，`false` 取 `y` / `true` for `z`, `false` for `y`.</param>
    /// <returns>重量表或失败 / The weight table or a failure.</returns>
    /// <remarks>
    /// `y` 与 `z` 都是**按舱位下标**的一维变量，且**只在对应标志为真的舱位上注册**——所以循环必须先查标志再取值，
    /// 否则会在未注册的舱位上找 token，拿到"缺失"而误判为失败。
    ///
    /// Both `y` and `z` are **indexed by position** and registered **only where the matching flag is true**, so the loop must test the flag before reading, or it will look for a
    /// token on an unregistered position and report a spurious failure.
    /// </remarks>
    private Result<IReadOnlyDictionary<StowagePosition, Quantity<Flt64>>, ErrorCode, Error<ErrorCode>> CollectWeights(
        IReadOnlyList<Flt64> solution,
        LinearMetaModel<Flt64> model,
        bool recommended)
    {
        var weights = new Dictionary<StowagePosition, Quantity<Flt64>>();
        URealVariable1? real = _aggregation.Load.Y;
        UIntVariable1? integer = _aggregation.Load.Z;
        IReadOnlyList<StowagePosition> positions = _aggregation.Positions;

        for (int j = 0; j < positions.Count; ++j)
        {
            StowagePosition position = positions[j];
            bool needed = recommended
                ? position.Status.RecommendedWeightNeeded
                : position.Status.PredicateWeightNeeded;
            if (!needed)
            {
                continue;
            }

            IVariableItem? variable;
            if (recommended)
            {
                if (integer is null)
                {
                    return Results.Failed<IReadOnlyDictionary<StowagePosition, Quantity<Flt64>>>(new Err<ErrorCode>(
                        ErrorCode.ApplicationFailed,
                        "推荐装载重量变量未注册 / the recommended load-weight variable is not registered"));
                }
                variable = integer[j];
            }
            else
            {
                if (real is null)
                {
                    return Results.Failed<IReadOnlyDictionary<StowagePosition, Quantity<Flt64>>>(new Err<ErrorCode>(
                        ErrorCode.ApplicationFailed,
                        "谓词装载重量变量未注册 / the predicate load-weight variable is not registered"));
                }
                variable = real[j];
            }

            Result<Flt64, ErrorCode, Error<ErrorCode>> value = SolverValue(
                solution,
                model,
                variable,
                "舱位 " + position.Name + (recommended ? " 的推荐装载重量变量" : " 的谓词装载重量变量"));
            if (!value.IsOk)
            {
                return Rethrow<IReadOnlyDictionary<StowagePosition, Quantity<Flt64>>, Flt64>(value);
            }
            Flt64 raw = ((Ok<Flt64, ErrorCode, Error<ErrorCode>>)value).Value;
            if (raw.ToDouble() <= 0.0)
            {
                continue;
            }

            // 单位来自**变量**（`aircraftModel.weightUnit`），不是 `mlw` 的单位。
            // The unit comes from the **variable** (`aircraftModel.weightUnit`), not from `mlw`.
            weights[position] = new Quantity<Flt64>(raw, Aircraft.Model.AircraftModel.WeightUnit);
        }
        return Results.Ok<IReadOnlyDictionary<StowagePosition, Quantity<Flt64>>>(weights);
    }

    private Result<Flt64, ErrorCode, Error<ErrorCode>> SolverValue(
        IReadOnlyList<Flt64> solution,
        LinearMetaModel<Flt64> model,
        IVariableItem variable,
        string name)
    {
        int? index = model.Tokens.IndexOf(variable);
        if (index is null)
        {
            return Results.Failed<Flt64>(new Err<ErrorCode>(
                ErrorCode.ApplicationFailed,
                "求解器结果缺少 " + name + " 的 token / the solver result is missing the token of " + name));
        }
        if (index.Value < 0 || index.Value >= solution.Count)
        {
            return Results.Failed<Flt64>(new Err<ErrorCode>(
                ErrorCode.ApplicationFailed,
                "求解器结果缺少 " + name + " 的值（索引 " + index.Value + "） / the solver result is missing the value of "
                + name + " at index " + index.Value));
        }
        return Results.Ok<Flt64>(solution[index.Value]);
    }

    private Item? FindItem(string id)
    {
        foreach (Item item in _aggregation.Items)
        {
            if (string.Equals(item.Id, id, StringComparison.Ordinal))
            {
                return item;
            }
        }
        return null;
    }

    private StowagePosition? FindPosition(string id)
    {
        foreach (StowagePosition position in _aggregation.Positions)
        {
            if (string.Equals(position.Name, id, StringComparison.Ordinal))
            {
                return position;
            }
        }
        return null;
    }

    private static Result<T, ErrorCode, Error<ErrorCode>> Rethrow<T, TSource>(
        Result<TSource, ErrorCode, Error<ErrorCode>> result)
        where T : notnull
        where TSource : notnull =>
        new Failed<T, ErrorCode, Error<ErrorCode>>(
            ((Failed<TSource, ErrorCode, Error<ErrorCode>>)result).Error);
}
