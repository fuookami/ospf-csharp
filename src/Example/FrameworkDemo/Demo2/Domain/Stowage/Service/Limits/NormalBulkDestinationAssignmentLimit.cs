// 普通散货同目的地约束 / The "same-destination normal bulk" rows.

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using StowageModel = Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model.Stowage;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Service.Limits;

/// <summary>
/// 普通散货同目的地约束 / The "same-destination normal bulk" rows.
/// </summary>
/// <remarks>
/// 对齐：普通散货舱里，**两件普通散货最多装一件**——
/// 一行 <c>stowage_{i1,j} + stowage_{i2,j} ≤ 1</c> 覆盖每一对。散货舱是"一堆散装混在一起"，两件不同目的地的散货混装会分不开，
/// 所以同一舱位只允许一件普通散货。
///
/// 四个过滤条件缺一不可：
///
/// | 条件 | 少了它的后果 |
/// | --- | --- |
/// | 两件货都是散货 | 给非散货加行，白撑大约束表 |
/// | 两件货都不是 AOG/MAT | 航材本来有专属规则（见 `AOGMATBulkConflictLimit`），这里再加一行是重复 |
/// | 两件货对该舱位都 `stowageNeeded` | 给本来就不能放的组合加行 |
/// | 舱位不是 `AOGMATAppointed` 且**未装载** | 航材指定舱位与已装货舱位另有规则 |
///
/// 配对用 `i2 > i1` 的**组合**而不是排列：写成全排列会让每一对出现两次，约束表翻倍而模型不变。
///
/// In a normal bulk hold, **at most one of two normal bulk items** may be loaded, one row <c>stowage_{i1,j} + stowage_{i2,j} ≤ 1</c> per pair. A bulk
/// hold is a loose pile, so two items for different destinations would be inseparable; hence one normal bulk item per position. Four filters, tabulated above, are all required.
/// Pairs are **combinations** with `i2 > i1`, not permutations: the latter emits every pair twice, doubling the table while the model is unchanged.
/// </remarks>
public sealed class NormalBulkDestinationAssignmentLimit
{
    private readonly IReadOnlyList<Item> _items;
    private readonly IReadOnlyList<StowagePosition> _positions;
    private readonly StowageModel _stowage;

    /// <summary>
    /// 构造同目的地约束 / Construct the same-destination rows.
    /// </summary>
    /// <param name="items">货物 / The items.</param>
    /// <param name="positions">舱位 / The positions.</param>
    /// <param name="stowage">装载模型 / The stowage model.</param>
    /// <param name="name">管线名称 / The pipeline name.</param>
    public NormalBulkDestinationAssignmentLimit(
        IReadOnlyList<Item> items,
        IReadOnlyList<StowagePosition> positions,
        StowageModel stowage,
        string name = "normal_bulk_destination_assignment_limit") {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(stowage);
        _items = items;
        _positions = positions;
        _stowage = stowage;
        Name = name;
    }

    /// <summary>管线名称 / The pipeline name</summary>
    public string Name { get; }

    /// <summary>
    /// 施加约束 / Apply the rows.
    /// </summary>
    /// <param name="linear">线性元模型 / The linear meta model.</param>
    /// <returns>操作结果 / Operation result.</returns>
    public Try Invoke(LinearMetaModel<Flt64> linear) {
        ArgumentNullException.ThrowIfNull(linear);

        for (int i1 = 0; i1 < _items.Count; ++i1) {
            Item item1 = _items[i1];
            if (!IsNormalBulk(item1)) {
                continue;
            }
            for (int i2 = i1 + 1; i2 < _items.Count; ++i2) {
                Item item2 = _items[i2];
                if (!IsNormalBulk(item2)) {
                    continue;
                }
                for (int j = 0; j < _positions.Count; ++j) {
                    StowagePosition position = _positions[j];
                    if (!StowageModel.StowageNeeded(item1, position)
                        || !StowageModel.StowageNeeded(item2, position)
                        || position.Type.Contains(PositionTypeCode.AOGMATAppointed)
                        || position.LoadedItems.Count > 0) {
                        continue;
                    }
                    if (_stowage.StowageSymbol(i1, j) is not { } first) {
                        return Missing(item1, position);
                    }
                    if (_stowage.StowageSymbol(i2, j) is not { } second) {
                        return Missing(item2, position);
                    }
                    string rowName = Name + "_" + item1.Name + "_" + item2.Name + "_" + position.Name;
                    var row = new LinearInequality<Flt64>(
                        new LinearPolynomial<Flt64>(
                            new[] {
                                new LinearMonomial<Flt64>(Flt64.One, first),
                                new LinearMonomial<Flt64>(Flt64.One, second),
                            },
                            Flt64.Zero),
                        new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), Flt64.One),
                        Comparison.LE,
                        rowName);
                    Try added = linear.AddConstraint(row, group: null, name: rowName);
                    if (!added.IsOk) {
                        return added;
                    }
                }
            }
        }
        return Results.Ok<Success>(Results.SuccessInstance);
    }

    // 普通散货 = 散货位置 **且** 不含 AOG/MAT。两个条件合成一个判定，避免在两个地方各写一遍。
    // Normal bulk = a bulk location **and** no AOG/MAT, folded into one predicate so it is not written twice.
    private static bool IsNormalBulk(Item item) =>
        item.Location.IsBulk && !item.Contains(CargoCode.AOG) && !item.Contains(CargoCode.MAT);

    private Try Missing(Item item, StowagePosition position) =>
        Results.Failed<Success>(new Err<ErrorCode>(
            ErrorCode.ApplicationFailed,
            "货物 " + item.Name + " 与舱位 " + position.Name +
            " 的装载符号未注册 / the stowage symbol is not registered"));
}
