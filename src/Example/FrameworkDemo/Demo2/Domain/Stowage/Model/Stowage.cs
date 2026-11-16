// 装载矩阵：把业务舱位/货物桥接到共享的离散装载内核 / The stowage matrix bridging business positions and cargo onto the shared discrete-loading kernel.

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading;
using Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading.Model;
using Fuookami.Ospf.Math.Algebra.ValueRange;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;

/// <summary>
/// 货物状态的派生语义 / Derived semantics of an item status.
/// </summary>
/// <remarks>
/// 基线的 `ItemStatus` 是**带默认值的枚举成员**（`Loaded { override val loaded = true }` …），C# 的枚举没有成员属性，
/// 于是这里的映射就是那份默认值的移植。**照抄而不要"合理推断"**：`Preassigned` 和 `Optional` 都是"需要装载"，
/// 但 `Preassigned` 不是"需要调整"；`AdjustmentNeeded` 同时是"已装载"和"可用"。
///
/// The baseline's `ItemStatus` is an enum **with per-member overrides** (`Loaded { override val loaded = true }` …); a C# enum has no member properties, so this mapping is that
/// set of defaults. **Copy it, do not "reason it out"**: `Preassigned` and `Optional` both need stowage but `Preassigned` is not "needs adjustment"; `AdjustmentNeeded` is both
/// loaded and available.
/// </remarks>
public static class ItemStatuses
{
    /// <summary>是否需要装载决策 / Whether a stowage decision is needed.</summary>
    /// <param name="status">货物状态 / The item status.</param>
    /// <returns>是否需要 / Whether needed.</returns>
    public static bool StowageNeeded(ItemStatus status) =>
        status is ItemStatus.Preassigned or ItemStatus.Optional;

    /// <summary>是否需要调整 / Whether adjustment is needed.</summary>
    /// <param name="status">货物状态 / The item status.</param>
    /// <returns>是否需要 / Whether needed.</returns>
    public static bool AdjustmentNeeded(ItemStatus status) => status is ItemStatus.AdjustmentNeeded;

    /// <summary>是否已装载 / Whether already loaded.</summary>
    /// <param name="status">货物状态 / The item status.</param>
    /// <returns>是否已装载 / Whether loaded.</returns>
    public static bool Loaded(ItemStatus status) =>
        status is ItemStatus.Loaded or ItemStatus.AdjustmentNeeded;

    /// <summary>是否可参与决策 / Whether it may take part in decisions.</summary>
    /// <param name="status">货物状态 / The item status.</param>
    /// <returns>是否可用 / Whether available.</returns>
    public static bool Available(ItemStatus status) => status is not (ItemStatus.Loaded or ItemStatus.Reserved);
}

/// <summary>
/// 装载模型的业务桥接：决策由共享内核拥有，符号在这里翻译 / Business bridge for stowage: the kernel owns the decisions, the symbols are translated here.
/// </summary>
/// <remarks>
/// 逐条对齐基线 `demo2/domain/stowage/model/Stowage.kt`：**合法性判定**（`StowageNeeded` / `AdjustmentNeeded`）与
/// **符号桥接**（`stowage_{i}_{j}` 每个合法组合一项、`loaded_{i}` 每件货一行）。
///
/// 与基线的一处已知差异：基线用 `items[i].id` 做键，C# 的 `Item` 目前只有 `Name`，所以键是**名称**。
/// 名称重复会让两件货共用一列——这是 `Item` 还缺 `Id` 的后果，已记入缺口。
///
/// Mirrors the baseline `Stowage.kt` closely: **legality** (`StowageNeeded` / `AdjustmentNeeded`) and **symbol bridging** (`stowage_{i}_{j}` per legal pair, `loaded_{i}` per item).
/// One known divergence: the baseline keys by `items[i].id` while C#'s `Item` only has `Name`, so the key is the **name**. Duplicate names would make two items share a column —
/// a consequence of `Item` still lacking an `Id`, recorded as a gap.
/// </remarks>
public sealed class Stowage
{
    private readonly IReadOnlyList<Item> _items;
    private readonly IReadOnlyList<StowagePosition> _positions;

    private readonly Dictionary<(int Item, int Position), LinearExpressionSymbol> _stowage = new();
    private readonly Dictionary<int, LinearExpressionSymbol> _loaded = new();

    /// <summary>
    /// 构造装载桥接 / Construct the stowage bridge.
    /// </summary>
    /// <param name="items">货物 / The cargo items.</param>
    /// <param name="positions">舱位 / The stowage positions.</param>
    public Stowage(IReadOnlyList<Item> items, IReadOnlyList<StowagePosition> positions) {
        _items = items;
        _positions = positions;
    }

    /// <summary>共享装载内核 / The shared loading kernel.</summary>
    public DiscreteLoadingContext<Flt64>? LoadingContext { get; set; }

    /// <summary>
    /// 装载合法性 / Whether this pair may be decided for stowage.
    /// </summary>
    /// <param name="item">货物 / The item.</param>
    /// <param name="position">舱位 / The position.</param>
    /// <returns>是否合法 / Whether legal.</returns>
    public static bool StowageNeeded(Item item, StowagePosition position) {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(position);
        return ItemStatuses.StowageNeeded(item.Status)
            && position.Status.StowageNeeded
            && position.Enabled(item).Ok;
    }

    /// <summary>
    /// 调整合法性 / Whether this pair may be decided for adjustment.
    /// </summary>
    /// <param name="item">货物 / The item.</param>
    /// <param name="position">舱位 / The position.</param>
    /// <returns>是否合法 / Whether legal.</returns>
    public static bool AdjustmentNeeded(Item item, StowagePosition position) {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(position);
        if (!ItemStatuses.AdjustmentNeeded(item.Status)) {
            return false;
        }
        if (position.Status.StowageNeeded && position.Enabled(item).Ok) {
            return true;
        }
        return position.Status.AdjustmentNeeded
            && (position.Enabled(item).Ok || position.LoadedItems.Contains(item));
    }

    /// <summary>
    /// 取某一组合的决策变量 / The decision variable of one pair.
    /// </summary>
    /// <param name="itemIndex">货物下标 / Item index.</param>
    /// <param name="positionIndex">舱位下标 / Position index.</param>
    /// <returns>决策变量；不合法组合没有变量 / The variable; illegal pairs have none.</returns>
    /// <remarks>
    /// **不合法组合返回 `null` 而不是零变量**：基线的稀疏决策只给合法组合建变量，返回零会让"没有这条决策"
    /// 和"这条决策被固定为不选"看起来一样，而后者是一个可以写错的约束。
    ///
    /// **Illegal pairs return `null` rather than a zero variable**: the baseline's sparse decisions only create variables for legal pairs, and returning zero would make "no such
    /// decision" look identical to "the decision is fixed to not-chosen" — the latter being a constraint one can get wrong.
    /// </remarks>
    public BinVar? AssignmentVariable(int itemIndex, int positionIndex) {
        if (LoadingContext is null) {
            return null;
        }
        var key = new AssignmentKey(_items[itemIndex].Id, _positions[positionIndex].Name);
        return LoadingContext.Aggregation.Assignments.TryGetValue(key, out BinVar? variable) ? variable : null;
    }

    /// <summary>
    /// 取某一组合的装载量符号 / The stowage-amount symbol of one pair.
    /// </summary>
    /// <param name="itemIndex">货物下标 / Item index.</param>
    /// <param name="positionIndex">舱位下标 / Position index.</param>
    /// <returns>装载量符号 / The stowage symbol.</returns>
    public LinearExpressionSymbol? StowageSymbol(int itemIndex, int positionIndex) =>
        _stowage.TryGetValue((itemIndex, positionIndex), out LinearExpressionSymbol? symbol) ? symbol : null;

    /// <summary>
    /// 取某件货的已装载符号 / The loaded symbol of one item.
    /// </summary>
    /// <param name="itemIndex">货物下标 / Item index.</param>
    /// <returns>已装载符号 / The loaded symbol.</returns>
    public LinearExpressionSymbol? LoadedSymbol(int itemIndex) =>
        _loaded.TryGetValue(itemIndex, out LinearExpressionSymbol? symbol) ? symbol : null;

    /// <summary>
    /// 注册共享内核并把业务符号接上 / Register the shared kernel and bridge the business symbols.
    /// </summary>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>操作结果 / Operation result.</returns>
    /// <remarks>
    /// 顺序是硬要求：**先**注册内核（决策变量由它建立），**再**取变量翻译成 `stowage_{i}_{j}`，最后加 `loaded_{i}`。
    /// 反过来会翻译到一张还没有变量的聚合表上，得到一片空多项式——每个 `stowage` 恒为零，模型照样可解。
    ///
    /// The order is a hard requirement: register the kernel **first** (it creates the decisions), then translate variables into `stowage_{i}_{j}`, then add `loaded_{i}`. Doing it
    /// the other way translates against an aggregation that has no variables yet and yields empty polynomials — every `stowage` is identically zero and the model still solves.
    /// </remarks>
    public Try Register(LinearMetaModel<Flt64> model) {
        ArgumentNullException.ThrowIfNull(model);
        if (LoadingContext is null) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "装载上下文未初始化 / the loading context is not initialized"));
        }

        Try registered = LoadingContext.Register(model);
        if (!registered.IsOk) {
            return registered;
        }

        _stowage.Clear();
        for (int i = 0; i < _items.Count; ++i) {
            for (int j = 0; j < _positions.Count; ++j) {
                BinVar? variable = AssignmentVariable(i, j);
                var polynomial = new MutableLinearPolynomial<Flt64>();
                if (variable is not null) {
                    polynomial.AddMonomial(new LinearMonomial<Flt64>(Flt64.One, variable));
                }
                var symbol = new LinearExpressionSymbol(
                    polynomial,
                    name: "stowage_" + i + "_" + j);
                // **给表达式符号声明上下界**：它是"货 i 是否在舱位 j"的 0/1 指示量，界就是 `[0, 1]`。
                // 不声明的话，`BigMValues.SymbolBounds` 拿不到范围，谓词重量的绝对值公式就**推不出 Big-M**——
                // 而它推不出来时是**报错**而不是猜一个数（：预配载建模型直接失败）。
                //
                // **Declare bounds on the expression symbol**: it is the 0/1 indicator "is item i in position j", so the bounds are `[0, 1]`. Without them
                // `BigMValues.SymbolBounds` has no range to read and the predicate weight's absolute-value formulation **cannot infer a Big-M** — and when it cannot,
                // it **fails rather than guessing a number** .
                SetExpressionRange(symbol, new Flt64(0), new Flt64(1));
                Try added = model.Add(symbol);
                if (!added.IsOk) {
                    return added;
                }
                _stowage[(i, j)] = symbol;
            }
        }

        _loaded.Clear();
        for (int i = 0; i < _items.Count; ++i) {
            LinearPolynomial<Flt64> polynomial = LoadingContext.Aggregation.Loaded.TryGetValue(
                _items[i].Name, out LinearPolynomial<Flt64>? loaded)
                ? loaded
                : new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), Flt64.Zero);
            var symbol = new LinearExpressionSymbol(
                new MutableLinearPolynomial<Flt64>(polynomial.Monomials, polynomial.Constant),
                name: "loaded_" + _items[i].Name);
            // `loaded_i = Σ_j stowage_{i}_{j}`，所以界是"0 到合法舱位数"——同样是为了让下游能推 Big-M。
            // `loaded_i = Σ_j stowage_{i}_{j}`, so the bounds are "zero to the number of legal positions" — again so downstream can infer a Big-M.
            SetExpressionRange(symbol, new Flt64(0), new Flt64(_positions.Count));
            Try added = model.Add(symbol);
            if (!added.IsOk) {
                return added;
            }
            _loaded[i] = symbol;
        }

        return Results.Ok<Success>(Results.SuccessInstance);
    }

    /// <summary>
    /// 给表达式符号声明上下界 / Declare bounds on an expression symbol.
    /// </summary>
    /// <param name="symbol">表达式符号 / The expression symbol.</param>
    /// <param name="lower">下界 / The lower bound.</param>
    /// <param name="upper">上界 / The upper bound.</param>
    /// <remarks>
    /// **界推不出来时 Big-M 推不出来，而 Big-M 推不出来是报错**：`BigMValues.SymbolBounds` 只认
    /// `IVariableItem` 的范围与 `IIntermediateSymbol` 的 `Range.Range`，表达式符号属于后者，不声明就永远是空的。
    /// 所以这里失败也**不吞掉**——界没设上就等于没写，静默跳过只会把错误推到几步之后。
    ///
    /// **No bounds means no Big-M, and no Big-M is an error**: `BigMValues.SymbolBounds` only reads ranges from `IVariableItem` and the `Range.Range` of an
    /// `IIntermediateSymbol`; expression symbols are the latter, so an undeclared range stays empty forever. A failure here is **not swallowed** — a bound that did not
    /// land is the same as no bound, and skipping silently pushes the error several steps downstream.
    /// </remarks>
    private static void SetExpressionRange(LinearExpressionSymbol symbol, Flt64 lower, Flt64 upper) {
        Result<ValueRange<Flt64>, ErrorCode, Error<ErrorCode>> range = ValueRange<Flt64>.Of(lower, upper);
        if (range.IsOk) {
            symbol.Range.SetRange(range.Value);
        }
    }
}
