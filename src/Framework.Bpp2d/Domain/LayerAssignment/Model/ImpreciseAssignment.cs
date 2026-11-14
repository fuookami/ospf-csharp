#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Framework.Bpp2d.Domain;
using Fuookami.Ospf.Framework.Bpp2d.Domain.Item.Model;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.MultiArray;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

// 基线的 `typealias Bpp2dColumnVariable = AbstractVariableItem<UInt64, UInteger>` 无法在 C# 表达
// （`src/Core/Symbol/SymbolCombination.cs:95` 同样记录），所以用 `using` 别名；别名不能带 XML 文档。
// The baseline's typealias cannot be expressed in C#, so a using alias is used; aliases cannot carry XML docs.
using Bpp2dColumnVariable = Fuookami.Ospf.Core.Variable.CombinationVariableItem<Fuookami.Ospf.Math.Algebra.Number.UInt64, Fuookami.Ospf.Core.Variable.UInteger>;

namespace Fuookami.Ospf.Framework.Bpp2d.Domain.LayerAssignment.Model;

/// <summary>
/// 非精确层分配 / The imprecise layer assignment.
/// </summary>
/// <remarks>
/// 与基线 `bpp2d-domain-layer-assignment-context/.../model/Assignment.kt` 的 `ImpreciseAssignment` 对齐。它是
/// `Load`、`Capacity`、`Objectives` 与十余条 `Limits` 的共同前置：没有它，后几块连变量都拿不到。
///
/// 四处必须照抄：
///
/// 1. **列变量按迭代分批**（`x_0`、`x_1`…），`VariableOf` 先找已有分组、找不到才用**最后一组的第一项**。
///    直接取"新建一组"会让老列失去变量，目标函数与约束对不上列。
/// 2. **列变量上界是保守估计**：`ceil(该列要覆盖的最大需求量 / 该列件数)`。漏掉它会让同一个候选列被无限次使用。
/// 3. **`AddColumns` 先去重**：`aggregation.AddColumns` 返回的是去重后的新列；去重后为空就直接返回空表，**不注册变量**——
///    为一个空集合注册 `x_1` 会白占一个变量组。
/// 4. **`Area` 是"列面积 × 该列变量"的线性和**，每轮 `AddColumns` 都往里追加项，不是重算。
///
/// Aligned with the baseline's `ImpreciseAssignment`. It is the common prerequisite for `Load`, `Capacity`, `Objectives` and a dozen `Limits`: without it those blocks cannot even get a
/// variable. Four things must be copied: **column variables are grouped per iteration** (`x_0`, `x_1`, …) and `VariableOf` looks through the existing groups first, falling back to **the
/// first item of the last group**, since always creating a new group would leave older columns without a variable and desynchronise the objective from the constraints; **the column
/// upper bound is a conservative estimate**, `ceil(largest demand to cover / units in the column)`, and omitting it would let one candidate column be reused indefinitely;
/// **`AddColumns` deduplicates first**, returning early on an empty result and **registering no variable**, because registering `x_1` for an empty set wastes a variable group; and
/// **`Area` is the linear sum of column area times column variable**, appended to on every `AddColumns` rather than recomputed.
/// </remarks>
public sealed class ImpreciseAssignment
{
    private readonly IReadOnlyDictionary<ItemId, ulong> _items;
    private readonly LayerAggregation<FltX> _aggregation;
    private readonly List<UIntVariable1> _variableGroups = new();
    private readonly MutableLinearPolynomial<Flt64> _area = new(constant: Flt64.Zero);
    private ILinearIntermediateSymbol<Flt64>? _areaSymbol;

    /// <summary>
    /// 构造非精确层分配 / Construct the imprecise layer assignment.
    /// </summary>
    /// <param name="items">物品需求量 / The item demands.</param>
    /// <param name="aggregation">层聚合 / The layer aggregation.</param>
    public ImpreciseAssignment(
        IReadOnlyDictionary<ItemId, ulong> items,
        LayerAggregation<FltX>? aggregation = null)
    {
        ArgumentNullException.ThrowIfNull(items);
        _items = items;
        _aggregation = aggregation ?? new LayerAggregation<FltX>();
    }

    /// <summary>层 / The layers</summary>
    public IReadOnlyList<Layer<FltX>> Layers => _aggregation.Layers;

    /// <summary>上一轮迭代的层 / The layers from the last iteration</summary>
    public IReadOnlyList<Layer<FltX>> LastIterationLayers => _aggregation.LastIterationLayers;

    /// <summary>已建立的变量组数 / The number of variable groups built</summary>
    public int VariableGroupCount => _variableGroups.Count;

    /// <summary>面积符号 / The area symbol</summary>
    public ILinearIntermediateSymbol<Flt64>? AreaSymbol => _areaSymbol;

    /// <summary>
    /// 注册面积符号 / Register the area symbol.
    /// </summary>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>执行结果 / Execution result.</returns>
    public Try Register(LinearMetaModel<Flt64> model)
    {
        ArgumentNullException.ThrowIfNull(model);
        _areaSymbol ??= new LinearExpressionSymbol(_area, name: "area");
        return model.Add(_areaSymbol);
    }

    /// <summary>
    /// 取一条层的列变量 / Fetch a layer's column variable.
    /// </summary>
    /// <param name="columnIndex">列在这一组里的下标 / The column's index inside its group.</param>
    /// <returns>列变量 / The column variable.</returns>
    /// <exception cref="InvalidOperationException">还没有任何变量组 / No variable group exists yet.</exception>
    /// <remarks>
    /// **与基线的一处差异**：基线用 `layer.index`（`ManualIndexed`），而本仓的 `Layer` **没有手动下标**，
    /// 只有 `Iteration`（优化轮次，不是列序号）。用 `Iteration` 代替会把"第几轮"当成"第几列"，
    /// 列一多就索引错位。所以这里让调用方传列下标——**传错的锅在调用方，不在类型里藏一个假下标**。
    ///
    /// **One difference from the baseline**: the baseline uses `layer.index` (from `ManualIndexed`), while this repository's `Layer` **has no manual index**, only `Iteration` (the optimisation
    /// round, not the column number). Substituting `Iteration` would treat "which round" as "which column" and misalign as soon as a round carries more than one column, so the caller passes
    /// the index — **a wrong index is then the caller's problem rather than a fake index hidden in the type**.
    /// </remarks>
    public Bpp2dColumnVariable VariableOf(int columnIndex)
    {
        foreach (UIntVariable1 group in _variableGroups)
        {
            if (columnIndex >= 0 && columnIndex < group.Items.Length)
            {
                // `Items` 是 `CombinationVariableItem<UInt64, UInteger>`，与别名的目标类型一致。
                return group.Items[columnIndex];
            }
        }
        if (_variableGroups.Count == 0)
        {
            throw new InvalidOperationException("尚未建立变量组 / no variable group has been built");
        }
        // **最后一组的第一项**：新列还没分组时复用最后一组的头一个变量。
        // **The first item of the last group**: an ungrouped new column reuses the head variable.
        return _variableGroups[^1].Items[0];
    }

    /// <summary>
    /// 追加新列 / Append new columns.
    /// </summary>
    /// <param name="iteration">迭代序号 / The iteration number.</param>
    /// <param name="newLayers">新层 / The new layers.</param>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>去重后的新层 / The deduplicated new layers.</returns>
    public Result<IReadOnlyList<Layer<FltX>>, ErrorCode, Error<ErrorCode>> AddColumns(
        ulong iteration,
        IReadOnlyList<Layer<FltX>> newLayers,
        LinearMetaModel<Flt64> model)
    {
        ArgumentNullException.ThrowIfNull(newLayers);
        ArgumentNullException.ThrowIfNull(model);

        IReadOnlyList<Layer<FltX>> unduplicated = _aggregation.AddColumns(newLayers);
        if (unduplicated.Count == 0)
        {
            // **去重后为空就不注册变量**：为一个空集合建组会白占一组变量。
            // **No variable is registered on an empty result**: building a group for an empty set wastes one.
            return Results.Ok<IReadOnlyList<Layer<FltX>>>(
                Array.Empty<Layer<FltX>>());
        }

        UIntVariable1 variables = new("x_" + iteration, Shape1.Invoke(unduplicated.Count));
        for (int index = 0; index < unduplicated.Count; ++index)
        {
            Layer<FltX> layer = unduplicated[index];
            // **保守上界**：`ceil(最大需求 / 该列件数)`。
            // **A conservative bound**: `ceil(largest demand / units in the column)`.
            if (_items.Count == 0)
            {
                continue;
            }
            ulong demand = _items.Values.Max();
            ulong covered = layer.Amount == 0UL ? 1UL : layer.Amount;
            ulong bound = (demand + covered - 1UL) / covered;
            variables[index].Range.Leq(new Fuookami.Ospf.Math.Algebra.Number.UInt64(bound));
        }
        _variableGroups.Add(variables);

        Try added = model.Add(variables);
        if (!added.IsOk)
        {
            return added is Failed<Success, ErrorCode, Error<ErrorCode>> failed
                ? Results.Failed<IReadOnlyList<Layer<FltX>>>(failed.Error)
                : throw new InvalidOperationException("unreachable");
        }

        // **追加面积项**，不是重算：每轮新列都要贡献一份。
        // **Append area terms** rather than recomputing: every new column contributes one.
        for (int index = 0; index < unduplicated.Count; ++index)
        {
            Bpp2dColumnVariable variable = VariableOf(index);
            _area.Add(new LinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(unduplicated[index].Area.Value.ToFlt64(), variable) }, Flt64.Zero));
        }
        return Results.Ok<IReadOnlyList<Layer<FltX>>>(unduplicated);
    }
}
