#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Framework.Bpp3d.Infra;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.Bpp3d.Domain.LayerAssignment.Model;

/// <summary>
/// 精确层分配 / The precise layer assignment.
/// </summary>
/// <remarks>
/// `PreciseAssignment` 对齐，四个符号：
///
/// | 符号 | 维度 | 含义 |
/// | --- | --- | --- |
/// | `x` | 箱 × 层 | 整数装载量 |
/// | `u` | 箱 × 层 | `x` 是否为正 |
/// | `v` | 箱 | 该箱是否被用到 |
/// | `tail` | 箱 | 该箱是否是尾箱 |
///
/// 六处必须沿用：
///
/// 1. **`x[i, j]` 的上界分两种**：层与箱型不匹配、或箱装不下该层时**上界为 0**（该格被钉死），
///    否则为 `floor(箱高 / 层高)`。写成"不匹配时不设上界"会让不相容的组合在解里出现。
/// 2. **只把相容的格子注册进模型**，不相容的格子连符号都不加——不是加了再约束成 0。
/// 3. **`u` 是 `x` 的二值化**，逐格一层 `BinaryzationFunction`。
/// 4. **`v` 是整行 `x` 求和后的二值化**，所以 `v[i]` 为真等价于"第 i 个箱至少装了一层"。
/// 5. **`tail` 是普通二值变量**，没有函数包装。
/// 6. `floor(箱高 / 层高)` 用**向下取整**，不是四舍五入——多算一层就会让装不下的层被用上。
///
/// Aligned with the implementation, four symbols: `x` (integer load over bin × layer), `u` (whether `x` is positive, same shape), `v` (whether the bin is used), `tail` (whether the bin is a tail
/// bin). Six things must be copied: **`x[i, j]`'s upper bound has two cases** — zero when the layer's bin type mismatches or the bin cannot hold the layer, pinning that cell, and
/// `floor(bin height / layer height)` otherwise, since leaving the mismatched case unbounded would let incompatible combinations into solutions; **only compatible cells are registered on
/// the model**, not registered-then-constrained-to-zero; **`u` binarises `x` cell by cell**; **`v` binarises the row sum**, so `v[i]` holds exactly when bin i carries at least one layer;
/// **`tail` is a plain binary variable** with no function wrapper; and `floor(bin height / layer height)` **rounds down**, because rounding up would admit a layer that does not fit.
/// </remarks>
public sealed class PreciseAssignment
{
    private readonly IReadOnlyList<Bin<BinLayer, FltX>> _bins;
    private readonly IReadOnlyList<BinLayer> _layers;
    private readonly Dictionary<(int, int), ILinearIntermediateSymbol<Flt64>> _u = new();
    private readonly Dictionary<int, ILinearIntermediateSymbol<Flt64>> _v = new();

    /// <summary>
    /// 构造精确层分配 / Construct the precise layer assignment.
    /// </summary>
    /// <param name="bins">箱 / The bins.</param>
    /// <param name="layers">层 / The layers.</param>
    public PreciseAssignment(
        IReadOnlyList<Bin<BinLayer, FltX>> bins,
        IReadOnlyList<BinLayer> layers)
    {
        ArgumentNullException.ThrowIfNull(bins);
        ArgumentNullException.ThrowIfNull(layers);
        _bins = bins;
        _layers = layers;
    }

    /// <summary>整数装载量 / The integer load matrix</summary>
    public UIntVariable2? X { get; private set; }

    /// <summary>尾箱标记 / The tail-bin flags</summary>
    public BinVariable1? Tail { get; private set; }

    /// <summary>
    /// 取"该箱是否用到" / Fetch "is this bin used".
    /// </summary>
    /// <param name="binIndex">箱下标 / The bin index.</param>
    /// <returns>符号，未注册时为 null / The symbol, or null when not registered.</returns>
    public ILinearIntermediateSymbol<Flt64>? V(int binIndex) =>
        _v.TryGetValue(binIndex, out ILinearIntermediateSymbol<Flt64>? symbol) ? symbol : null;

    /// <summary>
    /// 判断箱能否容纳某层 / Whether a bin can hold a layer.
    /// </summary>
    /// <param name="bin">箱 / The bin.</param>
    /// <param name="layer">层 / The layer.</param>
    /// <returns>能否容纳 / Whether it fits.</returns>
    /// <remarks>
    /// `bin.enabled(layer)` 落在 `Container3Geometry.enabled` 上：宽高深三项都不小于该层。
    /// 注意它是**几何可容纳**，与 <see cref="Matches"/>（层与箱型同类型）是两件事，两个条件在注册时是**且**的关系。
    ///
 /// Its `bin.enabled(layer)` resolves to `Container3Geometry.enabled`: width, height and depth all at least the layer's. Note this is **geometric fit**, distinct from
    /// <see cref="Matches"/> (the layer's bin type equals the bin's); registration requires **both**.
    /// </remarks>
    public static bool Enabled(Bin<BinLayer, FltX> bin, BinLayer layer)
    {
        ArgumentNullException.ThrowIfNull(bin);
        ArgumentNullException.ThrowIfNull(layer);
        return bin.Type.Width.IsGreaterThanOrEqual(layer.Shape.Width)
            && bin.Type.Height.IsGreaterThanOrEqual(layer.Shape.Height)
            && bin.Type.Depth.IsGreaterThanOrEqual(layer.Shape.Depth);
    }

    /// <summary>
    /// 判断层与箱型是否同类型 / Whether the layer's bin type equals the bin's.
    /// </summary>
    /// <param name="bin">箱 / The bin.</param>
    /// <param name="layer">层 / The layer.</param>
    /// <returns>是否同类型 / Whether the types match.</returns>
    public static bool Matches(Bin<BinLayer, FltX> bin, BinLayer layer)
    {
        ArgumentNullException.ThrowIfNull(bin);
        ArgumentNullException.ThrowIfNull(layer);
        return layer.Bin == bin.Type;
    }

    /// <summary>
    /// 注册四个符号 / Register the four symbols.
    /// </summary>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>执行结果 / Execution result.</returns>
    public Try Register(LinearMetaModel<Flt64> model)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (X is null)
        {
            X = new UIntVariable2("x", _bins.Count, _layers.Count);
            for (int i = 0; i < _bins.Count; ++i)
            {
                for (int j = 0; j < _layers.Count; ++j)
                {
                    // **不相容的格子直接钉成 0。**
                    // **Incompatible cells are pinned to zero.**
                    if (!Matches(_bins[i], _layers[j]) || !Enabled(_bins[i], _layers[j]))
                    {
                        X[i, j].Range.Eq(new Fuookami.Ospf.Math.Algebra.Number.UInt64(0UL));
                    }
                    else
                    {
                        // **向下取整**：多算一层就会让装不下的层被用上。
                        // **Rounds down**: rounding up would admit a layer that does not fit.
                        double capacity = System.Math.Floor(
                            _bins[i].Type.Depth.ToDouble() / _layers[j].Shape.Depth.ToDouble());
                        X[i, j].Range.Leq(new Fuookami.Ospf.Math.Algebra.Number.UInt64((ulong)System.Math.Max(capacity, 0.0)));
                    }
                }
            }
        }

        // **只注册相容的格子**，不是注册后约束成 0。
        // **Only compatible cells are registered**, not registered-then-constrained.
        for (int i = 0; i < _bins.Count; ++i)
        {
            for (int j = 0; j < _layers.Count; ++j)
            {
                if (!Matches(_bins[i], _layers[j]) || !Enabled(_bins[i], _layers[j]))
                {
                    continue;
                }
                Try added = model.Add(X[i, j]);
                if (!added.IsOk)
                {
                    return added;
                }
            }
        }

        if (_u.Count == 0)
        {
            for (int i = 0; i < _bins.Count; ++i)
            {
                for (int j = 0; j < _layers.Count; ++j)
                {
                    string name = "u_" + i + "_" + j;
                    var polynomial = new LinearPolynomial<Flt64>(
                        new[] { new LinearMonomial<Flt64>(Flt64.One, X[i, j]) }, Flt64.Zero);
                    ILinearIntermediateSymbol<Flt64> symbol =
                        new LinearFunctionSymbolAdapter<Flt64>(new BinaryzationFunction(polynomial, name: name));
                    Try added = model.Add(symbol);
                    if (!added.IsOk)
                    {
                        return added;
                    }
                    _u[(i, j)] = symbol;
                }
            }
        }

        if (_v.Count == 0)
        {
            for (int i = 0; i < _bins.Count; ++i)
            {
                string name = "v_" + i;
                // **整行求和后的二值化**：`v[i]` 为真等价于"第 i 个箱至少装了一层"。
                // **Binarised row sum**: `v[i]` holds exactly when bin i carries at least one layer.
                var monomials = new List<LinearMonomial<Flt64>>(_layers.Count);
                for (int j = 0; j < _layers.Count; ++j)
                {
                    monomials.Add(new LinearMonomial<Flt64>(Flt64.One, X[i, j]));
                }
                var polynomial = new LinearPolynomial<Flt64>(monomials, Flt64.Zero);
                ILinearIntermediateSymbol<Flt64> symbol =
                    new LinearFunctionSymbolAdapter<Flt64>(new BinaryzationFunction(polynomial, name: name));
                Try added = model.Add(symbol);
                if (!added.IsOk)
                {
                    return added;
                }
                _v[i] = symbol;
            }
        }

        if (Tail is null)
        {
            // **普通二值变量**，没有函数包装。
            // **A plain binary variable**, with no function wrapper.
            Tail = new BinVariable1("tail", _bins.Count);
            // 组合变量要逐项加：`Add` 没有接受组合类型的重载。
            // A combination is added item by item: `Add` has no overload taking the combination itself.
            foreach (CombinationVariableItem<Fuookami.Ospf.Math.Algebra.Number.UInt8, Binary> item in Tail.Items)
            {
                Try added = model.Add(item);
                if (!added.IsOk)
                {
                    return added;
                }
            }
        }

        return Results.Ok<Success>(Results.SuccessInstance);
    }
}
