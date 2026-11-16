#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Framework.Bpp2d.Domain.Item.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.Bpp2d.Domain.LayerAssignment.Model;

/// <summary>
/// 装载容量契约 / The load-capacity contract.
/// </summary>
/// <remarks>
/// 四个成员里两个是 `LinearIntermediateSymbols1&lt;FltX&gt;`——
/// 这是个 typealias，C# 无法表达（`src/Core/Symbol/SymbolCombination.cs:95`），因此用 `IReadOnlyList&lt;...&gt;`。
/// **这是本仓第四次同样的处置**，不是随手改的形状。
///
/// Two of the four members are `LinearIntermediateSymbols1&lt;FltX&gt;`, a typealias C# cannot express, so `IReadOnlyList&lt;...&gt;` is used —
/// **the fourth time this repository has handled the same problem** , not an ad-hoc shape.
/// </remarks>
public interface ICapacity
{
    /// <summary>每列的装载面积符号 / The load-area symbol per column</summary>
    IReadOnlyList<ILinearIntermediateSymbol<Flt64>> LoadArea { get; }

    /// <summary>每列的装载率符号 / The loading-rate symbol per column</summary>
    IReadOnlyList<ILinearIntermediateSymbol<Flt64>> LoadingRate { get; }

    /// <summary>
    /// 第 i 列的板材面积 / The sheet area of column i.
    /// </summary>
    /// <param name="index">列序号 / The column index.</param>
    /// <returns>板材面积，缺失或越界时为零 / The sheet area, zero when missing or out of range.</returns>
    FltX SheetAreaAt(int index);

    /// <summary>列数 / The column count</summary>
    int ColumnCount();
}

/// <summary>
/// 精确装载容量 / The precise load capacity.
/// </summary>
/// <remarks>
/// 每列两个符号，都由该列的份数变量 `y_i` 线性表出。
///
/// | 符号 | 系数 | 用途 |
/// | --- | --- | --- |
/// | `load_area_i` | `该列实际占用面积`（`Layer.Area`，件面积之和） | 板材容量约束的左端 |
/// | `loading_rate_i` | `该列实际占用面积 / 板材面积` | 装载率上限约束 |
///
/// **`Layer.Area` 与 `Layer.Shape.Area` 是两个数**：前者是件面积之和（实际占用），后者是板材面积。
/// 用错会让装载率恒为 1（第一次就踩了这个，判据的 `200 vs 10000` 直接指出来）。
///
/// **三处必须沿用**：
///
/// 1. **板材面积为零或缺失时装载率系数取零**，不做除法——除零会得到 `NaN`/`Infinity`，而求解器不会拒绝它，
///    只会给出无意义的最优解。写法是 `if (denominator &lt;= 0) 0 else area / denominator`。
/// 2. **`Register` 幂等**：已建好的符号组复用，重复注册**不再 `model.Add`**——符号注册对重名是拒绝的
///    （与变量注册不同，实测过变量注册容忍重名）。
/// 3. **两个符号组都注册**，只注册一个会让容量约束在另一半上找不到符号。
///
/// Two symbols per column, both linear in that column's copy variable `y_i`. **Three things must be copied**: **a zero or missing sheet area makes
/// the loading-rate coefficient zero** rather than dividing, since a division by zero yields NaN or Infinity that the solver will not reject and will merely turn into a meaningless optimum;
/// **`Register` is idempotent**, reusing the same symbol objects; and **both symbol groups are registered**, since registering only one leaves the capacity constraint unable to find symbols on
/// the other half.
/// </remarks>
public sealed class PreciseLoadCapacity : ICapacity
{
    private readonly IReadOnlyList<Layer<FltX>> _layers;
    private readonly PreciseAssignment _assignment;
    private IReadOnlyList<ILinearIntermediateSymbol<Flt64>>? _loadArea;
    private IReadOnlyList<ILinearIntermediateSymbol<Flt64>>? _loadingRate;
    private bool _registered;

    /// <summary>
    /// 构造精确装载容量 / Construct the precise load capacity.
    /// </summary>
    /// <param name="layers">候选层 / The candidate layers.</param>
    /// <param name="assignment">精确层分配 / The precise layer assignment.</param>
    public PreciseLoadCapacity(
        IReadOnlyList<Layer<FltX>> layers,
        PreciseAssignment assignment)
    {
        ArgumentNullException.ThrowIfNull(layers);
        ArgumentNullException.ThrowIfNull(assignment);
        _layers = layers;
        _assignment = assignment;
    }

    /// <inheritdoc/>
    /// <remarks>注册前为空表 / Empty before registration.</remarks>
    public IReadOnlyList<ILinearIntermediateSymbol<Flt64>> LoadArea =>
        _loadArea ?? Array.Empty<ILinearIntermediateSymbol<Flt64>>();

    /// <inheritdoc/>
    /// <remarks>注册前为空表 / Empty before registration.</remarks>
    public IReadOnlyList<ILinearIntermediateSymbol<Flt64>> LoadingRate =>
        _loadingRate ?? Array.Empty<ILinearIntermediateSymbol<Flt64>>();

    /// <inheritdoc/>
    public int ColumnCount() => _layers.Count;

    /// <inheritdoc/>
    public FltX SheetAreaAt(int index) =>
        index >= 0 && index < _layers.Count && _layers[index].Sheet is { } sheetType
            ? sheetType.Area.Value
            : new FltX(0.0);

    /// <summary>
    /// 注册容量符号 / Register the capacity symbols.
    /// </summary>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>执行结果 / Execution result.</returns>
    public Try Register(LinearMetaModel<Flt64> model)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (_assignment.Y is null)
        {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.ApplicationFailed,
                "份数变量尚未注册，无法建立容量符号 / the copy-count variable is not registered, so the capacity symbols cannot be built"));
        }

        if (_loadArea is null)
        {
            var loadArea = new List<ILinearIntermediateSymbol<Flt64>>(_layers.Count);
            for (int index = 0; index < _layers.Count; ++index)
            {
                loadArea.Add(new LinearExpressionSymbol(
                    new MutableLinearPolynomial<Flt64>(
                        monomials: new[] {
                            new LinearMonomial<Flt64>(_layers[index].Area.Value.ToFlt64(), _assignment.Y[index]),
                        },
                        constant: Flt64.Zero),
                    name: "load_area_" + index));
            }
            _loadArea = loadArea;
        }
        if (_loadingRate is null)
        {
            var loadingRate = new List<ILinearIntermediateSymbol<Flt64>>(_layers.Count);
            for (int index = 0; index < _layers.Count; ++index)
            {
                // **除数为零时系数取零**，不做除法。
                // **A zero divisor makes the coefficient zero** rather than dividing.
                FltX sheetArea = SheetAreaAt(index);
                double denominator = sheetArea.ToFlt64().ToDouble();
                FltX coefficient = denominator <= 0.0
                    ? new FltX(0.0)
                    : _layers[index].Area.Value.ToFltX().Div(sheetArea);
                loadingRate.Add(new LinearExpressionSymbol(
                    new MutableLinearPolynomial<Flt64>(
                        monomials: new[] {
                            new LinearMonomial<Flt64>(coefficient.ToFlt64(), _assignment.Y[index]),
                        },
                        constant: Flt64.Zero),
                    name: "loading_rate_" + index));
            }
            _loadingRate = loadingRate;
        }

        // **符号只注册一次**：符号注册对重名是**拒绝**的（与变量注册不同，见 的实测），
        // 所以第二次调用复用已注册的符号、不再重复 `model.Add`。
        // **Symbols are registered once**: symbol registration **rejects** a duplicate name (unlike variable registration, measured), so a second call reuses the registered symbols
        // instead of adding them again.
        if (!_registered)
        {
            foreach (ILinearIntermediateSymbol<Flt64> symbol in LoadArea)
            {
                Try added = model.Add(symbol);
                if (!added.IsOk)
                {
                    return added;
                }
            }
            foreach (ILinearIntermediateSymbol<Flt64> symbol in LoadingRate)
            {
                Try added = model.Add(symbol);
                if (!added.IsOk)
                {
                    return added;
                }
            }
            _registered = true;
        }
        return Results.Ok<Success>(Results.SuccessInstance);
    }
}
