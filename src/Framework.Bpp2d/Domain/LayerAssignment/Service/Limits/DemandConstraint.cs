#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Framework.Bpp2d.Domain;
using Fuookami.Ospf.Framework.Bpp2d.Domain.LayerAssignment.Model;
using Fuookami.Ospf.Framework.Bpp2d.Domain.Item.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.Bpp2d.Domain.LayerAssignment.Service.Limits;

/// <summary>
/// 需求约束 / The demand constraint.
/// </summary>
/// <remarks>
/// 三处必须沿用：
///
/// 1. **下界是必须的**：`load[i] &gt;= demand[i]`。只注册上界会让"最小化"退化成"什么都不装"——目标值最小、
///    需求全未满足，而模型看起来完全正常。
/// 2. **上界是可选的**：只有 `UpperBound` 非空时才注册 `load[i] &lt;= upperBound`，两个方向的名不同
///    （`{name}_{tag}_lb_{i}` / `{name}_{tag}_ub_{i}`）。
/// 3. **`tag` 来自需求模式**（`item_amount` / `item_area`），不是索引——用索引做 tag 会让两个模式的行同名冲突。
///
/// **一处形状差异**：装载符号本该从 `load.load[i] as Symbol` 取，本仓的 `Load` 类型（求解器符号层）
/// 尚未落地，所以这条约束直接收 `IReadOnlyList&lt;ILinearIntermediateSymbol&lt;Flt64&gt;&gt;`——**装载向量本身就是符号表**，
/// 收它而不是伪造一个 `Load`。等 `Load` 落地后本类只需换构造参数。
///
/// Three things must be copied: **the lower bound is mandatory** — `load[i] &gt;= demand[i]`, since registering only the upper bound would let the minimisation
/// degenerate to shipping nothing while the model still looks perfectly normal; **the upper bound is optional**, registered only when `UpperBound` is set, with different names per
/// direction (`{name}_{tag}_lb_{i}` / `{name}_{tag}_ub_{i}`); and **the tag comes from the demand mode** (`item_amount` / `item_area`) rather than the index, which would collide.
///
/// **One structural difference**: load symbols would come from `load.load[i] as Symbol`, but this repository's `Load` (the solver-symbol layer) is not yet implemented, so this
/// constraint takes an `IReadOnlyList&lt;ILinearIntermediateSymbol&lt;Flt64&gt;&gt;` directly — **the load vector is itself a symbol table**, so taking it beats fabricating a `Load`. Once `Load`
/// lands, only this constructor needs to change.
/// </remarks>
public sealed class DemandConstraint
{
    private readonly ILoad _load;
    private readonly IReadOnlyList<Bpp2dAssignmentDemandEntry> _demandEntries;
    private readonly string _name;

    /// <summary>
    /// 构造需求约束 / Construct the demand constraint.
    /// </summary>
    /// <param name="load">装载 / The load.</param>
    /// <param name="demandEntries">需求项，null 表示沿用装载里那一份 / The demand entries, null meaning the load's own list.</param>
    /// <param name="name">管线名 / The pipeline name.</param>
    public DemandConstraint(
        ILoad load,
        IReadOnlyList<Bpp2dAssignmentDemandEntry>? demandEntries = null,
        string name = "demand")
    {
        ArgumentNullException.ThrowIfNull(load);
        _load = load;
        _demandEntries = demandEntries ?? load.DemandEntries;
        _name = name;
    }

    /// <summary>管线名 / The pipeline name</summary>
    public string Name => _name;

    /// <summary>
    /// 需求模式标签 / The demand-mode tag.
    /// </summary>
    /// <param name="mode">需求模式 / The demand mode.</param>
    /// <returns>标签 / The tag.</returns>
    public static string ModeTag(Bpp2dAssignmentDemandMode mode) => mode switch
    {
        Bpp2dAssignmentDemandMode.ItemAmount => "item_amount",
        Bpp2dAssignmentDemandMode.ItemArea => "item_area",
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "unknown demand mode"),
    };

    /// <summary>
    /// 施加约束 / Apply the constraints.
    /// </summary>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>执行结果 / Execution result.</returns>
    public Try Invoke(LinearMetaModel<Flt64> model)
    {
        ArgumentNullException.ThrowIfNull(model);

        for (int index = 0; index < _demandEntries.Count; ++index)
        {
            if (index >= _load.Load.Count)
            {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.IllegalArgument,
                    "装载符号不足 / missing a load symbol at index " + index));
            }
            Bpp2dAssignmentDemandEntry entry = _demandEntries[index];
            string tag = ModeTag(entry.Mode);
            var demandValue = entry.Demand.ToSolver().ToFlt64();

            // **下界必须注册**：只注册上界会让最小化退化成不装任何货。
            // **The lower bound is mandatory**: registering only the upper bound would let the minimisation degenerate to shipping nothing.
            string lowerName = _name + "_" + tag + "_lb_" + index;
            Try lower = model.AddConstraint(
                new LinearInequality<Flt64>(
                    SymbolOf(_load.Load[index]), ConstantOf(demandValue), Comparison.GE, lowerName),
                group: null,
                name: lowerName);
            if (!lower.IsOk)
            {
                return lower;
            }

            // **上界是可选的**。
            // **The upper bound is optional.**
            if (entry.UpperBound is not { } upperBound)
            {
                continue;
            }
            string upperName = _name + "_" + tag + "_ub_" + index;
            Result<Success, ErrorCode, Error<ErrorCode>> upper = model.AddConstraint(
                new LinearInequality<Flt64>(
                    SymbolOf(_load.Load[index]), ConstantOf(upperBound.ToSolver().ToFlt64()), Comparison.LE, upperName),
                group: null,
                name: upperName);
            if (!upper.IsOk)
            {
                return upper;
            }
        }
        return Results.Ok<Success>(Results.SuccessInstance);
    }

    /// <summary>
    /// 计算一条候选列的需求影子价格 / Compute a candidate column's demand shadow price.
    /// </summary>
    /// <param name="layer">候选列 / The candidate column.</param>
    /// <param name="shadowPrices">需求影子价格 / The demand shadow prices.</param>
    /// <returns>影子价格 / The shadow price.</returns>
    /// <remarks>
    /// **影子价格为零的需求被跳过**：零乘任何系数都是零，跳过省掉乘法。**没有登记的需求也跳过**，不是当成零参与计算——
    /// 那样会把"还没定价"和"定价为零"混为一谈。
    ///
    /// **Demands priced at zero are skipped**, since zero times any coefficient is zero and skipping saves the multiplications. **Unregistered demands are skipped too** rather than
    /// treated as zero, which would conflate "not priced yet" with "priced at zero".
    /// </remarks>
    public Flt64 Extract(Layer<FltX> layer, Bpp2dShadowPriceMap shadowPrices)
    {
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentNullException.ThrowIfNull(shadowPrices);
        Flt64 price = Flt64.Zero;
        foreach (Bpp2dAssignmentDemandEntry entry in _demandEntries)
        {
            if (shadowPrices.At(entry.Mode, entry.ItemId) is not { } shadow || shadow.ToDouble() == 0.0)
            {
                continue;
            }
            price += shadow * LayerAssignmentMath.LayerDemandCoefficient(layer, entry).ToFlt64();
        }
        return price;
    }

    /// <summary>
    /// 单位系数多项式 / A unit-coefficient polynomial over one symbol.
    /// </summary>
    /// <param name="symbol">符号 / The symbol.</param>
    /// <returns>多项式 / The polynomial.</returns>
    private static LinearPolynomial<Flt64> SymbolOf(ILinearIntermediateSymbol<Flt64> symbol) =>
        new(new[] { new LinearMonomial<Flt64>(Flt64.One, symbol) }, Flt64.Zero);

    /// <summary>
    /// 常数多项式 / A constant polynomial.
    /// </summary>
    /// <param name="value">常数 / The constant.</param>
    /// <returns>多项式 / The polynomial.</returns>
    private static LinearPolynomial<Flt64> ConstantOf(Flt64 value) =>
        new(Array.Empty<LinearMonomial<Flt64>>(), value);
}
