#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Framework.Bpp2d.Domain.Item.Model;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.Bpp2d.Domain.Item.Model;

/// <summary>
/// 二维货物的量纲形状 / The quantity-based 2D item shape.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <remarks>
/// 与的 `QuantityItemShape` 对齐：**两种形状**，
/// 矩形与圆。这个类型存在的理由是"外部数据只给量纲，不给解算器形状"——转换是**单独一步**，不塞进构造函数。
///
/// 两个转换方法分工：<see cref="ToShape"/> 保留 `V`，<see cref="ToFltXShape"/> 换成解算器用的 `FltX`。
/// 合成一个泛型方法会让调用方还要指定目标类型，而实际只有一个去处。
///
/// Aligned with its `QuantityItemShape`: **two shapes**, rectangle and circle. The type exists because external data supplies quantities rather than solver shapes, and
/// conversion is **a separate step** rather than something stuffed into a constructor. The two conversion methods divide the work: <see cref="ToShape"/> keeps `V`,
/// <see cref="ToFltXShape"/> switches to the solver's `FltX`. Merging them into one generic method would force callers to name a target type that only ever has one value.
/// </remarks>
public abstract record QuantityItemShape<V> where V : struct, IFloatingNumber<V>
{
    /// <summary>
    /// 矩形 / Rectangle.
    /// </summary>
    /// <param name="Width">宽 / Width.</param>
    /// <param name="Height">高 / Height.</param>
    public sealed record Rectangle<W>(Quantity<W> Width, Quantity<W> Height)
        : QuantityItemShape<W> where W : struct, IFloatingNumber<W>;

    /// <summary>
    /// 圆 / Circle.
    /// </summary>
    /// <param name="Radius">半径 / Radius.</param>
    public sealed record Circle<W>(Quantity<W> Radius)
        : QuantityItemShape<W> where W : struct, IFloatingNumber<W>;

    /// <summary>
    /// 转成同数值类型的解算器形状 / Convert to a solver shape of the same numeric type.
    /// </summary>
    /// <returns>解算器形状 / The solver shape.</returns>
    public PackingShape2<V> ToShape() => this switch
    {
        Rectangle<V> rectangle => new RectanglePackingShape2<V>(rectangle.Width, rectangle.Height),
        Circle<V> circle => new CirclePackingShape2<V>(circle.Radius),
        _ => throw new ArgumentOutOfRangeException(nameof(QuantityItemShape<V>), this, "unknown item shape"),
    };

    /// <summary>
    /// 转成 `FltX` 的解算器形状 / Convert to a solver shape in `FltX`.
    /// </summary>
    /// <returns>`FltX` 解算器形状 / The `FltX` solver shape.</returns>
    public PackingShape2<FltX> ToFltXShape() => this switch
    {
        Rectangle<V> rectangle => new RectanglePackingShape2<FltX>(
            ToFltX(rectangle.Width), ToFltX(rectangle.Height)),
        Circle<V> circle => new CirclePackingShape2<FltX>(ToFltX(circle.Radius)),
        _ => throw new ArgumentOutOfRangeException(nameof(QuantityItemShape<V>), this, "unknown item shape"),
    };

    /// <summary>
    /// 量纲转 `FltX` / Convert a quantity to `FltX`.
    /// </summary>
    /// <param name="quantity">量 / The quantity.</param>
    /// <returns>`FltX` 量 / The `FltX` quantity.</returns>
    /// <remarks>
    /// 走 `ToFlt64.ToDouble` 再构造，而不是要求 `V` 实现某个转换接口——这是本仓既有的通用路径
    /// （`PackingRendererAdapter` 等处同样用法）。**单位原样带过去**。
    ///
    /// Goes through `ToFlt64.ToDouble` and reconstructs rather than requiring `V` to implement a conversion interface — the existing generic path here (used the same way in
    /// `PackingRendererAdapter`). **The unit carries over unchanged.**
    /// </remarks>
    internal static Quantity<FltX> ToFltX(Quantity<V> quantity) =>
        new(new FltX(quantity.Value.ToFlt64().ToDouble()), quantity.Unit);
}

/// <summary>
/// 二维货物的量纲模型 / The quantity-based 2D item model.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <remarks>
/// 与基线 `QuantityItem` 对齐。**`ToModel` 会丢掉不在物料目录里的物料**（基线 `mapNotNull { materialCatalog[no]?.let { … } }`），
/// 而不是报错——外部数据里多出来的物料编号是常态，报错会让整批请求失败。
///
/// Aligned with its `QuantityItem`. **`ToModel` drops materials absent from the catalogue** (its `mapNotNull { materialCatalog[no]?.let { … } }`) rather than
/// failing, because extra material numbers in external data are normal and erroring would fail the whole batch.
/// </remarks>
/// <param name="Id">货物编号 / The item id.</param>
/// <param name="Name">名称 / The name.</param>
/// <param name="Shape">量纲形状 / The quantity shape.</param>
/// <param name="AllowRotate">是否允许旋转 / Whether rotation is allowed.</param>
/// <param name="Weight">重量 / The weight.</param>
/// <param name="MaterialAmounts">物料用量 / The material amounts.</param>
/// <param name="BatchNo">批次号 / The batch number.</param>
/// <param name="Amount">件数 / The amount.</param>
public sealed record QuantityItem<V>(
    ItemId Id,
    string Name,
    QuantityItemShape<V> Shape,
    bool AllowRotate,
    Quantity<V> Weight,
    IReadOnlyDictionary<MaterialNo, ulong>? MaterialAmounts = null,
    BatchNo? BatchNo = null,
    ulong Amount = 1UL)
    where V : struct, IFloatingNumber<V>
{
    /// <summary>
    /// 转成解算器模型 / Convert to the solver model.
    /// </summary>
    /// <param name="materialCatalog">物料目录 / The material catalogue.</param>
    /// <returns>解算器模型 / The solver item.</returns>
    public ActualItem<FltX> ToModel(IReadOnlyDictionary<MaterialNo, MaterialKey>? materialCatalog = null)
    {
        var amounts = new Dictionary<MaterialKey, ulong>();
        if (MaterialAmounts is { } source && materialCatalog is { } catalog)
        {
            // **目录里没有的物料直接丢掉**，不报错。
            // **Materials missing from the catalogue are dropped**, not reported.
            foreach (KeyValuePair<MaterialNo, ulong> entry in source)
            {
                if (catalog.TryGetValue(entry.Key, out MaterialKey? key))
                {
                    amounts[key] = entry.Value;
                }
            }
        }

        return new ActualItem<FltX>(
            Id: Id,
            Name: Name,
            Shape: Shape.ToFltXShape(),
            AllowRotate: AllowRotate,
            Weight: QuantityItemShape<V>.ToFltX(Weight),
            MaterialAmounts: amounts,
            BatchNo: BatchNo);
    }
}

/// <summary>
/// 层内单件放置（量纲）/ One placement inside a layer, in quantities.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <remarks>
/// 与基线 `QuantityLayerPlacement` 对齐。**旋转要落到形状上**：基线 `if (rotated) fltXShape.rotated else fltXShape`，
/// 所以 `Rotated` 不只是记录用的标记，它会换掉形状本身。只把标记传下去而形状不动，解算器看到的还是未旋转的那一件。
///
/// Aligned with its `QuantityLayerPlacement`. **Rotation lands on the shape**: the code does `if (rotated) fltXShape.rotated else fltXShape`, so `Rotated` rewrites the
/// shape rather than merely recording a flag. Passing the flag through without rotating would leave the solver looking at the unrotated piece.
/// </remarks>
/// <param name="Shape">量纲形状 / The quantity shape.</param>
/// <param name="X">横坐标 / The x coordinate.</param>
/// <param name="Y">纵坐标 / The y coordinate.</param>
/// <param name="Rotated">是否已旋转 / Whether it is rotated.</param>
/// <param name="ItemId">货物编号 / The item id.</param>
public sealed record QuantityLayerPlacement<V>(
    QuantityItemShape<V> Shape,
    Quantity<V> X,
    Quantity<V> Y,
    bool Rotated = false,
    ItemId? ItemId = null)
    where V : struct, IFloatingNumber<V>
{
    /// <summary>
    /// 转成解算器放置 / Convert to the solver placement.
    /// </summary>
    /// <returns>解算器放置 / The solver placement.</returns>
    public Placement2<FltX> ToModel()
    {
        PackingShape2<FltX> shape = Shape.ToFltXShape();
        // **旋转换掉形状本身**，不只是记一个标记。
        // **Rotation rewrites the shape itself**, not just a flag.
        PackingShape2<FltX> final = Rotated ? shape.Rotated() : shape;
        return new Placement2<FltX>(
            Shape: final,
            Position: new QuantityPoint2<FltX>(
                QuantityItemShape<V>.ToFltX(X), QuantityItemShape<V>.ToFltX(Y)),
            Rotated: Rotated,
            ItemId: ItemId?.Value);
    }
}
