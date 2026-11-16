#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Framework.Bpp2d.Domain;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.Bpp2d.Domain.LayerAssignment.Model;

/// <summary>
/// 装载契约 / The load contract.
/// </summary>
/// <remarks>
/// 六个成员里 `Load` / `OverLoad` / `LessLoad`
/// 在`LinearExpressionSymbols1` / `LinearIntermediateSymbols1`——Kotlin 的 typealias，C# 无法表达
/// （`src/Core/Symbol/SymbolCombination.cs:95`），因此这里用 `IReadOnlyList&lt;...&gt;`。这是本仓第三次同样的处置，
/// 不是随手改的形状。
///
/// Aligned with its `interface Load`. Three of the six members are Kotlin typealiases (`LinearExpressionSymbols1` /
/// `LinearIntermediateSymbols1`) that C# cannot express (see `src/Core/Symbol/SymbolCombination.cs:95`), so `IReadOnlyList&lt;...&gt;` is used instead — the third time this repository has handled
/// the same problem, not an ad-hoc shape.
/// </remarks>
public interface ILoad
{
    /// <summary>需求项 / The demand entries</summary>
    IReadOnlyList<Bpp2dAssignmentDemandEntry> DemandEntries { get; }

    /// <summary>装载量 / The load amounts</summary>
    IReadOnlyList<LinearExpressionSymbol> Load { get; }

    /// <summary>超额装载 / Over-load</summary>
    IReadOnlyList<ILinearIntermediateSymbol<Flt64>> OverLoad { get; }

    /// <summary>欠额装载 / Under-load</summary>
    IReadOnlyList<ILinearIntermediateSymbol<Flt64>> LessLoad { get; }

    /// <summary>是否启用超额 / Whether over-load is enabled</summary>
    bool OverEnabled { get; }

    /// <summary>是否启用欠额 / Whether under-load is enabled</summary>
    bool LessEnabled { get; }
}

/// <summary>
/// 非精确装载 / The imprecise load.
/// </summary>
/// <remarks>
/// 它是 `DemandConstraint` 等约束真正要的东西：**每个需求一项装载变量**，
/// 且每项是"候选列 × 该列对该需求的系数"的线性和。
///
/// 三处必须沿用：
///
/// 1. **装载变量只建一次**（`registerLoad` 幂等）：重复建会把 `<c>load_0</c>` 变成两个不同符号，约束找不到。
/// 2. **`appendColumnTerms` 在没注册时直接返回**，不抛——列生成是先注册、每轮追加，顺序反过来不该炸。
/// 3. **系数来自 <c>LayerAssignmentMath.LayerDemandCoefficient</c>**，不是"该列件数"：按面积需求时件数完全不对。
///
/// `OverLoad` / `LessLoad` 在`AbstractLoad` 里按开关懒建。本仓先留空表 + 开关（`OverEnabled` / `LessEnabled`
/// 默认 `false` / `true`），等松弛约束落地时再填——**空表配上开关不等于能力**，所以这里明确记下它还没实现。
///
/// Aligned with its `ImpreciseLoad`. It is what `DemandConstraint` and friends actually need: **one load variable per demand**, each a linear sum of "candidate column times
/// that column's coefficient for the demand". Three things must be copied: **the load variables are built once** (`registerLoad` is idempotent), since rebuilding turns `load_0` into two
/// different symbols and constraints cannot find them; **`appendColumnTerms` returns early when unregistered** rather than throwing, because column generation registers first and appends
/// each round and the reverse order must not explode; and **the coefficient comes from `LayerAssignmentMath.LayerDemandCoefficient`** rather than "units in the column", which is simply
/// wrong for area demands. This implementation builds `OverLoad` / `LessLoad` lazily in `AbstractLoad`; here they are empty lists plus the switches (`OverEnabled` / `LessEnabled` default
/// `false` / `true`.
/// </remarks>
public sealed class ImpreciseLoad : ILoad
{
    private readonly ImpreciseAssignment _assignment;
    private readonly bool _overEnabled;
    private readonly bool _lessEnabled;
    private readonly List<LinearExpressionSymbol> _load = new();
    private readonly List<ILinearIntermediateSymbol<Flt64>> _overLoad = new();
    private readonly List<ILinearIntermediateSymbol<Flt64>> _lessLoad = new();

    /// <summary>
    /// 构造非精确装载 / Construct the imprecise load.
    /// </summary>
    /// <param name="demandEntries">需求项 / The demand entries.</param>
    /// <param name="assignment">非精确层分配 / The imprecise layer assignment.</param>
    /// <param name="overEnabled">是否启用超额 / Whether over-load is enabled.</param>
    /// <param name="lessEnabled">是否启用欠额 / Whether under-load is enabled.</param>
    public ImpreciseLoad(
        IReadOnlyList<Bpp2dAssignmentDemandEntry> demandEntries,
        ImpreciseAssignment assignment,
        bool overEnabled = false,
        bool lessEnabled = true)
    {
        ArgumentNullException.ThrowIfNull(demandEntries);
        ArgumentNullException.ThrowIfNull(assignment);
        DemandEntries = demandEntries;
        _assignment = assignment;
        _overEnabled = overEnabled;
        _lessEnabled = lessEnabled;
    }

    /// <inheritdoc/>
    public IReadOnlyList<Bpp2dAssignmentDemandEntry> DemandEntries { get; }

    /// <inheritdoc/>
    public IReadOnlyList<LinearExpressionSymbol> Load => _load;

    /// <inheritdoc/>
    public IReadOnlyList<ILinearIntermediateSymbol<Flt64>> OverLoad => _overLoad;

    /// <inheritdoc/>
    public IReadOnlyList<ILinearIntermediateSymbol<Flt64>> LessLoad => _lessLoad;

    /// <inheritdoc/>
    public bool OverEnabled => _overEnabled;

    /// <inheritdoc/>
    public bool LessEnabled => _lessEnabled;

    /// <summary>
    /// 注册装载变量 / Register the load variables.
    /// </summary>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>执行结果 / Execution result.</returns>
    public Try RegisterLoad(LinearMetaModel<Flt64> model)
    {
        ArgumentNullException.ThrowIfNull(model);

        // **只建一次**：重复建会把同名符号变成两个不同对象。
        // **Built once**: rebuilding turns the same name into two different objects.
        if (_load.Count == 0)
        {
            for (int index = 0; index < DemandEntries.Count; ++index)
            {
                _load.Add(new LinearExpressionSymbol(
                    new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero),
                    name: "load_" + index));
            }
        }

        foreach (LinearExpressionSymbol symbol in _load)
        {
            Try added = model.Add(symbol);
            if (!added.IsOk)
            {
                return added;
            }
        }
        AppendColumnTerms();
        return Results.Ok<Success>(Results.SuccessInstance);
    }

    /// <summary>
    /// 注册超额 / 欠额符号层 / Register the over-load and under-load symbol layers.
    /// </summary>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>执行结果 / Execution result.</returns>
    /// <remarks>
    /// 四点沿用：
    ///
    /// 1. **按开关懒建，且只建一次**：重复建会把 `over_load_0` 变成两个不同符号。
    /// 2. **超额 / 欠额的初值是"当前`load`的拷贝"**，不是新变量——它们表达的是"这个需求现在被超/欠了多少"，
    ///    所以要跟着装载量走。注册那一刻拷贝，**之后 `appendColumnTerms` 只追加到 `load`**，
    ///    超/欠不会跟着涨；这是既有语义，这里沿用并记档，不擅自"修好"。
    /// 3. **开关关着就完全不建也不加**：建了不加等于留一个悬空符号，查不到也删不掉。
    /// 4. **任一 `Add` 失败立即返回失败**，不继续加剩下的——半个符号层比没有更糟。
    ///
    /// 注意 `model.Add` 走的是 `Add(IIntermediateSymbol)` 重载（`LinearExpressionSymbol` 实现了
    /// `ILinearIntermediateSymbol&lt;Flt64&gt;`），不是变量重载。
    ///
    /// Four things copied: **built lazily by switch and only once**, since rebuilding turns `over_load_0` into two different symbols; **the over and under
    /// symbols start as a copy of the current `load`**, not as fresh variables, because they express by how much the demand is currently over- or under-supplied and therefore follow the load
 /// amounts — the implementation copies at registration time and **later `appendColumnTerms` calls only append to `load`**, so over and under do not grow with it, which is its existing semantics
    /// and is copied and recorded here rather than quietly "fixed"; **a disabled switch builds and adds nothing**, since a built-but-unregistered symbol is unreachable and undeletable; and **any
    /// failing `Add` returns at once** instead of continuing, a half-built symbol layer being worse than none.
    /// </remarks>
    public Try Register(LinearMetaModel<Flt64> model)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (_overEnabled && _overLoad.Count == 0)
        {
            for (int index = 0; index < _load.Count; ++index)
            {
                _overLoad.Add(new LinearExpressionSymbol(
                    new MutableLinearPolynomial<Flt64>(_load[index].Polynomial.Monomials, _load[index].Polynomial.Constant),
                    name: "over_load_" + index));
            }
        }
        if (_lessEnabled && _lessLoad.Count == 0)
        {
            for (int index = 0; index < _load.Count; ++index)
            {
                _lessLoad.Add(new LinearExpressionSymbol(
                    new MutableLinearPolynomial<Flt64>(_load[index].Polynomial.Monomials, _load[index].Polynomial.Constant),
                    name: "less_load_" + index));
            }
        }

        if (_overEnabled)
        {
            foreach (ILinearIntermediateSymbol<Flt64> symbol in _overLoad)
            {
                Try added = model.Add(symbol);
                if (!added.IsOk)
                {
                    return added;
                }
            }
        }
        if (_lessEnabled)
        {
            foreach (ILinearIntermediateSymbol<Flt64> symbol in _lessLoad)
            {
                Try added = model.Add(symbol);
                if (!added.IsOk)
                {
                    return added;
                }
            }
        }
        return Results.Ok<Success>(Results.SuccessInstance);
    }

    /// <summary>
    /// 追加列的贡献 / Append the columns' contributions.
    /// </summary>
    /// <remarks>
    /// **没注册时直接返回**：列生成的顺序是"先注册、每轮追加"，反过来不该炸。
    ///
    /// **Returns early when unregistered**: column generation registers first and appends each round, and the reverse order must not throw.
    /// </remarks>
    public void AppendColumnTerms()
    {
        if (_load.Count == 0)
        {
            return;
        }

        IReadOnlyList<Layer<FltX>> layers = _assignment.Layers;
        for (int index = 0; index < DemandEntries.Count && index < _load.Count; ++index)
        {
            var monomials = new List<LinearMonomial<Flt64>>();
            for (int layerIndex = 0; layerIndex < layers.Count; ++layerIndex)
            {
                Layer<FltX> layer = layers[layerIndex];
                // **系数来自 `LayerDemandCoefficient`**，不是件数：按面积需求时件数完全不对。
                // **The coefficient comes from `LayerDemandCoefficient`**, not the unit count, which is simply wrong for area demands.
                var coefficient = LayerAssignmentMath
                    .LayerDemandCoefficient(layer, DemandEntries[index])
                    .ToFlt64();
                if (coefficient.ToDouble() == 0.0)
                {
                    continue;
                }
                monomials.Add(new LinearMonomial<Flt64>(coefficient, _assignment.VariableOf(layerIndex)));
            }
            if (monomials.Count == 0)
            {
                continue;
            }
            _load[index].Flush();
            _load[index].AsMutable().Add(new LinearPolynomial<Flt64>(monomials, Flt64.Zero));
        }
    }
}
