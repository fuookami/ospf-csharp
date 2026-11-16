#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.Bpp2d.Domain.Item.Model;
using Fuookami.Ospf.Framework.Bpp2d.Domain.LayerAssignment.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.Bpp2d.Domain.LayerAssignment.Service.Limits;

/// <summary>
/// 板数最小化目标 / The sheet-amount minimisation objective.
/// </summary>
/// <remarks>
/// 与基线 `Objectives.kt` 的 `SheetAmountMinimization` 对齐：把每层的份数变量 `y_i` 按系数放进目标。
///
/// **系数是一个函数而不是常数**（基线的 `coefficient: (Layer) -> FltX`）。这样同一份代码既能做"每层权重相同"，
/// 也能做"按板材类型成本加权"——`SheetTypeCostMinimization` 就是后者。把它写成常数会逼出第二个类。
///
/// 三处细节：
///
/// 1. **`y` 未注册就失败**，与 `AreaMinimization` 同一口径（"返回 ok 但目标为空"是最难查的一类缺陷）。
/// 2. **零系数项被跳过**（基线用 `safeLinearPolynomial` 处理全零的情形）：零系数项留着不影响最优解，
///    但会让目标多项式里塞满无效项，求解器日志无法阅读。
/// 3. **全部系数为零时目标为空**，这仍是成功——基线如此，且"所有层都不要"是合法输入。
///
/// Aligned with the baseline's `SheetAmountMinimization`: it puts each layer's copy variable `y_i` into the objective with a coefficient. **The coefficient is a function rather than a constant**
/// (the baseline's `coefficient: (Layer) -> FltX`), which lets one implementation cover both uniform weighting and per-sheet-type costing — `SheetTypeCostMinimization` is the latter — while a
/// constant would force a second class. Three details: **an unregistered `y` fails**, matching `AreaMinimization`, since "returns ok with an empty objective" is the hardest defect class to
/// find; **zero-coefficient terms are skipped** as the baseline does through `safeLinearPolynomial`, since keeping them changes no optimum but fills the polynomial with dead terms and makes
/// solver logs unreadable; and **an all-zero coefficient set yields an empty objective** while still succeeding, as in the baseline, because "use no layer" is a legitimate input.
/// </remarks>
public sealed class SheetAmountMinimization
{
    private readonly IReadOnlyList<Layer<FltX>> _layers;
    private readonly PreciseAssignment _assignment;
    private readonly Func<Layer<FltX>, FltX?> _coefficient;

    /// <summary>
    /// 构造板数最小化 / Construct the sheet-amount minimisation.
    /// </summary>
    /// <param name="layers">候选层 / The candidate layers.</param>
    /// <param name="assignment">精确层分配 / The precise layer assignment.</param>
    /// <param name="coefficient">每层的系数，`null` 表示该层不参与目标 / The per-layer coefficient, null meaning the layer takes no part.</param>
    /// <param name="name">目标名 / The objective name.</param>
    public SheetAmountMinimization(
        IReadOnlyList<Layer<FltX>> layers,
        PreciseAssignment assignment,
        Func<Layer<FltX>, FltX?> coefficient,
        string name = "sheet_amount_minimization")
    {
        ArgumentNullException.ThrowIfNull(layers);
        ArgumentNullException.ThrowIfNull(assignment);
        ArgumentNullException.ThrowIfNull(coefficient);
        _layers = layers;
        _assignment = assignment;
        _coefficient = coefficient;
        Name = name;
    }

    /// <summary>目标名 / The objective name</summary>
    public string Name { get; }

    /// <summary>
    /// 系数函数 / The coefficient function.
    /// </summary>
    /// <remarks>
    /// **对外暴露是为了让"缺数据（null）"与"零系数（0）"可观测**：在目标层两者都被跳过，看不出差别，
    /// 但调用方（例如成本目标）需要知道某层到底是"没定价"还是"定价为零"。不暴露就只能靠注释约定。
    ///
    /// **Exposed so that "missing data (null)" and "zero coefficient (0)" are observable**: both are skipped at the objective level, but a caller such as the cost objective needs to know
    /// whether a layer is unpriced or priced at zero, and without this the distinction rests on a comment.
    /// </remarks>
    public Func<Layer<FltX>, FltX?> Coefficient => _coefficient;

    /// <summary>
    /// 把目标加到模型 / Add the objective to the model.
    /// </summary>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>执行结果 / Execution result.</returns>
    public Try Invoke(LinearMetaModel<Flt64> model)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (_assignment.Y is null)
        {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.ApplicationFailed,
                "份数变量尚未注册，无法建立板数目标 / the copy-count variable is not registered, so the sheet-amount objective cannot be built"));
        }

        var monomials = new List<LinearMonomial<Flt64>>();
        for (int index = 0; index < _layers.Count; ++index)
        {
            // **`null` 与零是两回事**：`null` 表示"这一层不参与"（缺数据），零表示"这一层系数为零"（有数据但无权重）。
            // 合并两者会让"某类型没有成本"与"某类型成本为零"无法区分，而前者不该被当成免费。
            // **`null` and zero differ**: null means the layer takes no part (missing data) and zero means its coefficient is zero (data present, no weight). Merging them makes "no cost for
            // this type" indistinguishable from "this type costs zero", and the former must not be read as free.
            if (_coefficient(_layers[index]) is not { } coefficient)
            {
                continue;
            }
            if (coefficient.ToFlt64().ToDouble() == 0.0)
            {
                continue;
            }
            if (_assignment.YOf(index) is not { } variable)
            {
                continue;
            }
            monomials.Add(new LinearMonomial<Flt64>(coefficient.ToFlt64(), variable));
        }

        return model.AddObject(
            ObjectCategory.Minimum,
            new LinearPolynomial<Flt64>(monomials, Flt64.Zero),
            Name,
            displayName: null);
    }
}

/// <summary>
/// 板材面积最小化目标 / The sheet-area minimisation objective.
/// </summary>
/// <remarks>
/// 与基线 `MixedSheetObjectives.kt` 的 `SheetAreaMinimization` 对齐：每层的系数是 **`weight × 该层板材面积`**，
/// 目标是把"用的板材总面积"压到最小。
///
/// **跳过两类层**（基线用 `mapIndexedNotNull` 表达）：
///
/// 1. **没有板材类型的层**（`layer.sheet == null`）：没有面积可算，硬塞一个系数等于给该层凭空定价。
/// 2. **系数为零的层**：`weight` 为 0 或板材面积为 0。
///
/// 这两条与 `SheetAmountMinimization` 的"跳过零系数"不同——那里跳的是数值，这里跳的是**缺失的板材**。
/// 混为一谈会在"某层没有板材类型"时静默漏项。
///
/// Aligned with the baseline's `SheetAreaMinimization`: each layer's coefficient is **`weight × that layer's sheet area`**, minimising the total sheet area used. **Two kinds of layer are skipped**
/// (the baseline's `mapIndexedNotNull`): **a layer without a sheet type** (`layer.sheet == null`), which has no area to price and where inserting a coefficient would invent one; and **a layer
/// whose coefficient is zero**, because `weight` is 0 or the sheet area is 0. These differ from `SheetAmountMinimization`'s zero-coefficient skip — that one skips a value, this one skips a
/// **missing sheet** — and conflating them silently drops terms when a layer has no sheet type.
/// </remarks>
public sealed class SheetAreaMinimization
{
    private readonly IReadOnlyList<Layer<FltX>> _layers;
    private readonly PreciseAssignment _assignment;
    private readonly FltX _weight;

    /// <summary>
    /// 构造板材面积最小化 / Construct the sheet-area minimisation.
    /// </summary>
    /// <param name="layers">候选层 / The candidate layers.</param>
    /// <param name="assignment">精确层分配 / The precise layer assignment.</param>
    /// <param name="weight">面积权重 / The area weight.</param>
    /// <param name="name">目标名 / The objective name.</param>
    public SheetAreaMinimization(
        IReadOnlyList<Layer<FltX>> layers,
        PreciseAssignment assignment,
        FltX? weight = null,
        string name = "sheet_area_minimization")
    {
        ArgumentNullException.ThrowIfNull(layers);
        ArgumentNullException.ThrowIfNull(assignment);
        _layers = layers;
        _assignment = assignment;
        _weight = weight ?? new FltX(1.0);
        Name = name;
    }

    /// <summary>目标名 / The objective name</summary>
    public string Name { get; }

    /// <summary>面积权重 / The area weight</summary>
    public FltX Weight => _weight;

    /// <summary>
    /// 把目标加到模型 / Add the objective to the model.
    /// </summary>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>执行结果 / Execution result.</returns>
    public Try Invoke(LinearMetaModel<Flt64> model)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (_assignment.Y is null)
        {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.ApplicationFailed,
                "份数变量尚未注册，无法建立板材面积目标 / the copy-count variable is not registered, so the sheet-area objective cannot be built"));
        }

        var monomials = new List<LinearMonomial<Flt64>>();
        for (int index = 0; index < _layers.Count; ++index)
        {
            // **没有板材类型就跳过**：没有面积可算，凭空定价会误导求解器。
            // **A layer without a sheet type is skipped**: there is no area to price, and inventing one misleads the solver.
            if (_layers[index].Sheet is not { } sheetType)
            {
                continue;
            }
            FltX coefficient = _weight.Times(sheetType.Area.Value);
            if (coefficient.ToFlt64().ToDouble() == 0.0)
            {
                continue;
            }
            if (_assignment.YOf(index) is not { } variable)
            {
                continue;
            }
            monomials.Add(new LinearMonomial<Flt64>(coefficient.ToFlt64(), variable));
        }

        return model.AddObject(
            ObjectCategory.Minimum,
            new LinearPolynomial<Flt64>(monomials, Flt64.Zero),
            Name,
            displayName: null);
    }
}

/// <summary>
/// 板材类型成本最小化目标 / The sheet-type cost minimisation objective.
/// </summary>
/// <remarks>
/// 与基线 `MixedSheetObjectives.kt` 的 `SheetTypeCostMinimization` 对齐：按**板材类型**查成本表，把 `weight × cost` 作为该层的系数。
///
/// **它是 `SheetAmountMinimization` 的一个系数实现，不是一个新算法**——判据证明的是同一件事：系数由"层 → 数"的函数给出。
/// 本类只负责那个函数，目标本身复用 <see cref="SheetAmountMinimization"/>。这是 把系数做成函数的直接收益。
///
/// 三处跳过，各有不同含义：
///
/// 1. **没有板材类型的层**：没有 `TypeCode` 可查，跳过。
/// 2. **成本表里没有该类型**：跳过而**不是按零计**。按零计等于"这个类型免费"，会把求解器推向未定价的类型。
/// 3. **乘积为零**（`weight` 为 0 或成本为 0）：跳过。
///
/// 另外**成本表为空或 `weight` 为零时整条目标不建**（基线 `return ok`），且**成功返回**——"没有成本要考虑"是合法输入。
///
/// Aligned with the baseline's `SheetTypeCostMinimization`: it looks up a cost by **sheet type** and uses `weight × cost` as that layer's coefficient. **It is a coefficient implementation for
/// `SheetAmountMinimization`, not a new algorithm** — the criterion proves the same thing, that the coefficient comes from a "layer to number" function — and this class only supplies that
/// function while the objective reuses `SheetAmountMinimization`, the direct payoff of making the coefficient a function. Three skips with distinct meanings: **a layer without a sheet
/// type** has no `TypeCode` to look up; **a type missing from the cost table is skipped rather than priced at zero**, since zero would mean "this type is free" and push the solver toward
/// unpriced types; and **a zero product** (zero `weight` or zero cost) is skipped. Additionally, **an empty cost table or a zero `weight` builds no objective at all** (the baseline's
/// `return ok`) and **still succeeds**, because "there is no cost to consider" is a legitimate input.
/// </remarks>
public static class SheetTypeCostMinimization
{
    /// <summary>默认目标名 / The default objective name</summary>
    public const string DefaultName = "sheet_type_cost_minimization";

    /// <summary>
    /// 建立成本最小化目标 / Build the cost-minimisation objective.
    /// </summary>
    /// <param name="layers">候选层 / The candidate layers.</param>
    /// <param name="assignment">精确层分配 / The precise layer assignment.</param>
    /// <param name="typeCost">各类型成本 / The per-type costs.</param>
    /// <param name="weight">权重 / The weight.</param>
    /// <param name="name">目标名 / The objective name.</param>
    /// <returns>目标，无可考虑的成本时为 null / The objective, null when there is no cost to consider.</returns>
    /// <remarks>
    /// **无可考虑的成本时返回 `null`**，而不是返回一个空目标对象：调用方由此知道"这一步什么都不用做"，
    /// 而不是拿到一个加进去也无效的目标。
    ///
    /// **Returns `null` when there is no cost to consider** rather than an empty objective object, so the caller learns that this step has nothing to do instead of receiving an objective that
    /// does nothing when added.
    /// </remarks>
    public static SheetAmountMinimization? Build(
        IReadOnlyList<Layer<FltX>> layers,
        PreciseAssignment assignment,
        IReadOnlyDictionary<SheetTypeId, FltX> typeCost,
        FltX? weight = null,
        string name = DefaultName)
    {
        ArgumentNullException.ThrowIfNull(layers);
        ArgumentNullException.ThrowIfNull(assignment);
        ArgumentNullException.ThrowIfNull(typeCost);
        FltX actualWeight = weight ?? new FltX(1.0);
        if (typeCost.Count == 0 || actualWeight.ToFlt64().ToDouble() == 0.0)
        {
            return null;
        }

        return new SheetAmountMinimization(
            layers,
            assignment,
            layer =>
            {
                // **没有类型码或表里没有该类型就返回 `null`**（跳过），不是返回零（那会被读成免费）。
                // **A missing type code or table entry returns null** (skip), not zero, which would read as free.
                if (layer.Sheet is not { } sheetType || !typeCost.TryGetValue(sheetType.TypeCode, out FltX cost))
                {
                    // **返回 `null` 而不是零**：缺数据与零成本是两回事。
                    // **Returns null rather than zero**: missing data and zero cost are different things.
                    return null;
                }
                return actualWeight.Times(cost);
            },
            name);
    }
}
