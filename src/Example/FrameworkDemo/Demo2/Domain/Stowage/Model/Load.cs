// 装载模型此前：装载重量/数量变量、装载量与满载符号 / The first slice of the load model: weight/count variables plus stowage-amount and full symbols.

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;
using Fuookami.Ospf.Quantities.Quantity;

using Fuookami.Ospf.MultiArray;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Algebra.ValueRange;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;

/// <summary>
/// 装载模型 / The load model.
/// </summary>
/// <remarks>
/// 本文件共 585 行。这里落**第一片**：`y`（推荐装载重量）、`z`（推荐装载数量）、
/// `load_amount_{舱位}`（该舱位装了几件）、`full_{舱位}`（该舱位是否装满）。未落部分逐条记在 <see cref="Register"/> 的注释里。
///
/// This file is 585 lines. It lands the **first slice**: `y` (recommended load weight), `z` (recommended load count), `load_amount_{position}` and
/// `full_{position}`. What is not landed is listed on <see cref="Register"/>.
/// </remarks>
public sealed class Load
{
    private readonly IReadOnlyList<Item> _items;

    // **记住注册进了哪张模型**：`Register` 会重建 `Y`/`Z` 等符号，重复调用不只是"多几行"——**它会把上一次那些符号
    // 换成新对象，而外部还握着旧引用**。配载与装载效能两个域共用同一个 `Load`，所以这条路径必然会被走两次。
    // **Remembers which model it registered into**: `Register` rebuilds `Y`/`Z` and the rest, so a second call is not merely "a few extra rows" — **it swaps those symbols for
    // new objects while callers still hold the old references**. The stowage and loading-effectiveness domains share one `Load`, so this path is always walked twice.
    private LinearMetaModel<Flt64>? _registeredIn;
    private readonly IReadOnlyList<StowagePosition> _positions;
    private readonly Stowage _stowage;
    private readonly AircraftModel? _aircraftModel;
    private readonly Formula? _formula;

    private readonly Dictionary<int, LinearExpressionSymbol> _loadAmount = new();
    private readonly Dictionary<int, ILinearIntermediateSymbol<Flt64>> _full = new();
    private readonly Dictionary<int, ILinearIntermediateSymbol<Flt64>> _predicateLoadWeightSlack = new();
    private readonly Dictionary<int, SlackFunction<Flt64>> _predicateLoadWeightSlackFunction = new();
    private readonly Dictionary<int, LinearExpressionSymbol> _estimateLoadWeight = new();
    private readonly Dictionary<int, LinearExpressionSymbol> _actualLoadWeight = new();
    private readonly Dictionary<int, LinearExpressionSymbol> _estimateLoaded = new();
    private readonly Dictionary<int, ILinearIntermediateSymbol<Flt64>> _actualLoaded = new();
    private readonly Dictionary<int, LinearExpressionSymbol> _estimateLongitudinalTorque = new();
    private readonly Dictionary<int, LinearExpressionSymbol> _actualLongitudinalTorque = new();
    private readonly Dictionary<int, LinearExpressionSymbol> _lateralTorque = new();
    private readonly Dictionary<int, LinearExpressionSymbol> _clim = new();
    private readonly Dictionary<int, LinearExpressionSymbol> _index = new();

    /// <summary>
    /// 构造装载模型 / Construct the load model.
    /// </summary>
    /// <param name="items">货物 / The cargo items.</param>
    /// <param name="positions">舱位 / The stowage positions.</param>
    /// <param name="stowage">装载矩阵 / The stowage matrix.</param>
    /// <param name="withMultiLoadingSchema">是否启用多装载方案 / Whether the multi-loading schema is enabled.</param>
    /// <param name="aircraftModel">飞机型号（提供单位与重力）/ The aircraft model, which provides the units and gravity.</param>
    /// <param name="formula">配重公式（提供指数换算）/ The balance formula, which provides the index conversion.</param>
    public Load(
        IReadOnlyList<Item> items,
        IReadOnlyList<StowagePosition> positions,
        Stowage stowage,
        bool withMultiLoadingSchema = false,
        AircraftModel? aircraftModel = null,
        Formula? formula = null) {
        _items = items;
        _positions = positions;
        _stowage = stowage;
        WithMultiLoadingSchema = withMultiLoadingSchema;
        _aircraftModel = aircraftModel;
        _formula = formula;
    }

    /// <summary>是否启用多装载方案 / Whether the multi-loading schema is enabled</summary>
    public bool WithMultiLoadingSchema { get; }

    /// <summary>推荐装载重量变量 / The recommended load-weight variables</summary>
    public URealVariable1? Y { get; private set; }

    /// <summary>推荐装载数量变量 / The recommended load-count variables</summary>
    public UIntVariable1? Z { get; private set; }

    /// <summary>
    /// 取某舱位的装载量符号 / The stowage-amount symbol of one position.
    /// </summary>
    /// <param name="positionIndex">舱位下标 / Position index.</param>
    /// <returns>装载量符号 / The stowage-amount symbol.</returns>
    public LinearExpressionSymbol? LoadAmountSymbol(int positionIndex) =>
        _loadAmount.TryGetValue(positionIndex, out LinearExpressionSymbol? symbol) ? symbol : null;

    /// <summary>
    /// 取某舱位的满载符号 / The full symbol of one position.
    /// </summary>
    /// <param name="positionIndex">舱位下标 / Position index.</param>
    /// <returns>满载符号 / The full symbol.</returns>
    public ILinearIntermediateSymbol<Flt64>? FullSymbol(int positionIndex) =>
        _full.TryGetValue(positionIndex, out ILinearIntermediateSymbol<Flt64>? symbol) ? symbol : null;

    /// <summary>
    /// 匹配谓词的货物在某舱位上的装载量 / The stowage amount of the items matching a predicate at one position.
    /// </summary>
    /// <param name="positionIndex">舱位下标 / Position index.</param>
    /// <param name="predicate">货物过滤谓词 / The item filter predicate.</param>
    /// <returns>装载量符号 / The amount symbol.</returns>
    /// <remarks>
    /// 与 <see cref="LoadAmountSymbol"/> 成对：那个是**全部**货物的和，这个是**满足谓词的那部分**。两者用同一批 `stowage` 符号，
    /// 所以"主舱货有多少件"与"总共有多少件"在同一份解里必然自洽。
    ///
    /// Paired with <see cref="LoadAmountSymbol"/>: that one sums **every** item, this one only those matching the predicate. Both use the same `stowage` symbols, so "how many
    /// main-deck pieces" and "how many pieces in total" are consistent within one solution by construction.
    /// </remarks>
    public LinearExpressionSymbol LoadAmountOf(int positionIndex, Func<Item, bool> predicate) {
        ArgumentNullException.ThrowIfNull(predicate);
        var polynomial = new MutableLinearPolynomial<Flt64>();
        for (int i = 0; i < _items.Count; ++i) {
            if (!predicate(_items[i])) {
                continue;
            }
            if (_stowage.StowageSymbol(i, positionIndex) is { } stowageSymbol) {
                polynomial.AddMonomial(new LinearMonomial<Flt64>(Flt64.One, stowageSymbol));
            }
        }
        return new LinearExpressionSymbol(
            polynomial,
            name: "load_amount_of_" + _positions[positionIndex].Name);
    }

    /// <summary>
    /// 按谓词捕获货物下标 / Capture the item indices matching a predicate.
    /// </summary>
    /// <param name="predicate">分类谓词 / The classification predicate.</param>
    /// <returns>原序下标 / Indices in original order.</returns>
    /// <remarks>
    /// 单独暴露是因为 <see cref="LoadAmountsOf"/> 要的是**下标**而不是谓词：一次分类可以被多个舱位复用，逐舱位重跑谓词会
    /// 把同一个分类算 N 遍，而分类一变就必须 N 处同时改。
    ///
    /// Exposed separately because <see cref="LoadAmountsOf"/> takes **indices**, not a predicate: one classification can serve several positions, while re-running the predicate per
    /// position costs N evaluations and N places to change when the classification changes.
    /// </remarks>
    public IReadOnlyList<int> ItemIndicesOf(Func<Item, bool> predicate) {
        ArgumentNullException.ThrowIfNull(predicate);
        var indices = new List<int>();
        for (int i = 0; i < _items.Count; ++i) {
            if (predicate(_items[i])) {
                indices.Add(i);
            }
        }
        return indices;
    }

    /// <summary>
    /// 为一组舱位批量构造装载量 / Build the amounts for a group of positions at once.
    /// </summary>
    /// <param name="positionIndices">这里需要的舱位下标 / The positions needed here.</param>
    /// <param name="itemIndices">货物下标，保留调用方顺序及重复项 / Item indices, keeping the caller's order and duplicates.</param>
    /// <returns>按舱位下标的装载量符号 / The amount symbols by position index.</returns>
    /// <remarks>
    /// 两条的规矩：**同一舱位只建一个符号**，**顺序保留**（调用方的顺序与重复项沿用，因为{code}List&lt;Int&gt;{/code} 而不是 {code}Set{/code}）。
    ///
    /// `if (!amounts.containsKey(position))` 守卫**没有搬过来**：C# 的字典索引赋值本来就按 key 覆盖，去掉守卫后重复下标得到的仍是同一个
    /// key 下的同一个值，差别只是少构造一次——那是个不可观测的差异，没有判据能钉住它，也就不该留一行只能靠"照做"来自证合理的代码。
    ///
    /// Two rules carried over: **one symbol per position** and **order preserved** (the caller's order and duplicates kept, because the code takes a {code}List&lt;Int&gt;{/code}, not a {code}Set{/code}).
    /// The code's `if (!amounts.containsKey(position))` guard was **not** carried over: a C# dictionary assignment already replaces by key, so a repeated index lands on the same
    /// key with the same value either way — the only difference is one fewer construction, which is unobservable, so leaving a line justified solely by "the code has it" would
    /// be worse than dropping it.
    /// </remarks>
    public IReadOnlyDictionary<int, LinearExpressionSymbol> LoadAmountsOf(
        IEnumerable<int> positionIndices,
        IReadOnlyList<int> itemIndices) {
        ArgumentNullException.ThrowIfNull(positionIndices);
        ArgumentNullException.ThrowIfNull(itemIndices);
        var amounts = new Dictionary<int, LinearExpressionSymbol>();
        foreach (int j in positionIndices) {
            var polynomial = new MutableLinearPolynomial<Flt64>();
            foreach (int i in itemIndices) {
                if (i < 0 || i >= _items.Count) {
                    // 越界下标是**调用方错误**，要么当场抛，要么静默返回半份结果。后者更坏：调用方拿到一个少了几个舱位的
                    // 字典，看起来"这些舱位没货"，而真相是索引错了。
                    // An out-of-range index is a **caller error**: throw, or silently return half a result. The latter is worse — the caller gets a dictionary missing some
                    // positions and reads it as "those positions are empty" when the real story is a wrong index.
                    throw new ArgumentOutOfRangeException(
                        nameof(itemIndices),
                        "货物下标越界 / item index out of range: " + i);
                }
                if (_stowage.StowageSymbol(i, j) is { } stowageSymbol) {
                    polynomial.AddMonomial(new LinearMonomial<Flt64>(Flt64.One, stowageSymbol));
                }
            }
            amounts[j] = new LinearExpressionSymbol(
                polynomial,
                name: "load_amount_of_" + _positions[j].Name);
        }
        return amounts;
    }

    /// <summary>谓词装载重量松弛符号 / The predicate load-weight slack symbol</summary>
    /// <param name="positionIndex">舱位下标 / Position index.</param>
    /// <returns>谓词装载重量松弛符号 / The predicate load-weight slack symbol.</returns>
    public ILinearIntermediateSymbol<Flt64>? PredicateLoadWeightSlackSymbol(int positionIndex) =>
        _predicateLoadWeightSlack.TryGetValue(positionIndex, out ILinearIntermediateSymbol<Flt64>? symbol) ? symbol : null;

    /// <summary>谓词装载重量松弛函数（判据要看它的右端）/ The predicate load weight slack function, whose right-hand side the criteria inspect</summary>
    /// <param name="positionIndex">舱位下标 / Position index.</param>
    /// <returns>松弛函数；常数零的那个舱位为 `null` / The slack function, or `null` for a constant-zero position.</returns>
    public SlackFunction<Flt64>? PredicateLoadWeightSlackFunction(int positionIndex) =>
        _predicateLoadWeightSlackFunction.TryGetValue(positionIndex, out SlackFunction<Flt64>? function)
            ? function
            : null;

    /// <summary>推荐装载重量符号 / The estimated load-weight symbol</summary>
    /// <param name="positionIndex">舱位下标 / Position index.</param>
    /// <returns>推荐装载重量符号 / The estimated load-weight symbol.</returns>
    public LinearExpressionSymbol? EstimateLoadWeightSymbol(int positionIndex) =>
        _estimateLoadWeight.TryGetValue(positionIndex, out LinearExpressionSymbol? symbol) ? symbol : null;

    /// <summary>实际装载重量符号 / The actual load-weight symbol</summary>
    /// <param name="positionIndex">舱位下标 / Position index.</param>
    /// <returns>实际装载重量符号 / The actual load-weight symbol.</returns>
    public LinearExpressionSymbol? ActualLoadWeightSymbol(int positionIndex) =>
        _actualLoadWeight.TryGetValue(positionIndex, out LinearExpressionSymbol? symbol) ? symbol : null;

    /// <summary>推荐已装载符号 / The estimated loaded symbol</summary>
    /// <param name="positionIndex">舱位下标 / Position index.</param>
    /// <returns>推荐已装载符号 / The estimated loaded symbol.</returns>
    public LinearExpressionSymbol? EstimateLoadedSymbol(int positionIndex) =>
        _estimateLoaded.TryGetValue(positionIndex, out LinearExpressionSymbol? symbol) ? symbol : null;

    /// <summary>实际已装载符号 / The actual loaded symbol</summary>
    /// <param name="positionIndex">舱位下标 / Position index.</param>
    /// <returns>实际已装载符号 / The actual loaded symbol.</returns>
    public ILinearIntermediateSymbol<Flt64>? ActualLoadedSymbol(int positionIndex) =>
        _actualLoaded.TryGetValue(positionIndex, out ILinearIntermediateSymbol<Flt64>? symbol) ? symbol : null;

    /// <summary>推荐纵向力矩符号 / The estimated longitudinal torque symbol</summary>
    /// <param name="positionIndex">舱位下标 / Position index.</param>
    /// <returns>纵向力矩符号 / The longitudinal torque symbol.</returns>
    public LinearExpressionSymbol? EstimateLongitudinalTorqueSymbol(int positionIndex) =>
        _estimateLongitudinalTorque.TryGetValue(positionIndex, out LinearExpressionSymbol? symbol) ? symbol : null;

    /// <summary>实际纵向力矩符号 / The actual longitudinal torque symbol</summary>
    /// <param name="positionIndex">舱位下标 / Position index.</param>
    /// <returns>实际纵向力矩符号 / The actual longitudinal torque symbol.</returns>
    public LinearExpressionSymbol? ActualLongitudinalTorqueSymbol(int positionIndex) =>
        _actualLongitudinalTorque.TryGetValue(positionIndex, out LinearExpressionSymbol? symbol) ? symbol : null;

    /// <summary>横向力矩符号 / The lateral torque symbol</summary>
    /// <param name="positionIndex">舱位下标 / Position index.</param>
    /// <returns>横向力矩符号 / The lateral torque symbol.</returns>
    public LinearExpressionSymbol? LateralTorqueSymbol(int positionIndex) =>
        _lateralTorque.TryGetValue(positionIndex, out LinearExpressionSymbol? symbol) ? symbol : null;

    /// <summary>CLIM 符号（仅宽体）/ The CLIM symbol (wide body only)</summary>
    /// <param name="positionIndex">舱位下标 / Position index.</param>
    /// <returns>CLIM 符号 / The CLIM symbol.</returns>
    public LinearExpressionSymbol? ClimSymbol(int positionIndex) =>
        _clim.TryGetValue(positionIndex, out LinearExpressionSymbol? symbol) ? symbol : null;

    /// <summary>指数符号 / The index symbol</summary>
    /// <param name="positionIndex">舱位下标 / Position index.</param>
    /// <returns>指数符号 / The index symbol.</returns>
    public LinearExpressionSymbol? IndexSymbol(int positionIndex) =>
        _index.TryGetValue(positionIndex, out LinearExpressionSymbol? symbol) ? symbol : null;

    /// <summary>
 /// 注册此前装载符号 / Register the first slice of load symbols.
    /// </summary>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>操作结果 / Operation result.</returns>
    /// <remarks>
    /// 顺序固定：**先**变量（`y`、`z`），**再** `load_amount`（要用 `stowage`），**最后** `full`（要用 `load_amount`）。`Load` 是 `Stowage` 的下游，自己不建矩阵。
    ///
    /// 一处刻意差异：对"不需要该状态"的舱位不建零范围变量、也不加进模型，**只加需要的**。
    /// 零范围的变量加进去对模型没有任何作用，只会让求解规模虚增——差异记在这里而不是埋着。
    ///
    /// 尚未落地 / Not landed yet：`predicate_load_weight_slack`、`estimateLoadWeight`/`actualLoadWeight`/`estimateLoaded`/`actualLoaded`、
    /// `loadEstimateLongitudinalTorque`/`loadActualLongitudinalTorque`/`loadLateralTorque`/`loadCLIM`/`loadIndex`。
    ///
    /// Order follows the code: **variables first**, **then** `load_amount` (needs `stowage`), **last** `full` (needs `load_amount`). `Load` is downstream of `Stowage` and
    /// does not build the matrix. One deliberate divergence: the code creates zero-range variables for positions that do not need the status **and still adds them**; C# only adds
    /// the needed ones — a zero-range variable contributes nothing and merely inflates the model.
    /// </remarks>
    public Try Register(LinearMetaModel<Flt64> model) {
        ArgumentNullException.ThrowIfNull(model);
        // **同一个模型上重复注册是空操作**：符号对象已经建好并进去了，再建一遍会撞名字、还会让旧引用失效。
        // 模型允许重复 `add`，所以这里也必须允许——**否则"配载 + 装载效能"这种正常组合根本跑不起来**。
        // **Re-registering into the same model is a no-op**: the symbols are already built and in, and rebuilding them collides on names and invalidates old references.
        // The code tolerates a repeated `add`, so this must too — **otherwise a normal "stowage + loading effectiveness" combination cannot run at all**.
        if (_registeredIn is not null && ReferenceEquals(_registeredIn, model)) {
            return Results.Ok<Success>(Results.SuccessInstance);
        }
        _registeredIn = null;
        // `y`/`z` **无条件构建**：它们不在任何 `if` 里，`if (withMultiLoadingSchema)` 只包 `estimateLoadWeight`。
        // 曾把它们包进守卫（判断错了），结果是"谓词重量松弛"在单方案下拿不到 `y`——而段松弛同样不看方案。
        // **无条件**还有一层原因：`y`/`z` 的范围按舱位状态钉过零，单方案下它们本来就是常数零，不会虚增求解规模。
        // `y`/`z` are **built unconditionally**: in the code they sit inside no `if` at all, and `if (withMultiLoadingSchema)` wraps only `estimateLoadWeight`. wrongly
        // put them under the guard, which left the predicate weight slack without a `y` under the single schema — and that slack does not depend on the schema either.
        // `y`/`z` also have their ranges pinned to zero per position status, so under the single schema they are constants that inflate nothing.
        {
            Y = new URealVariable1("y", _positions.Count);
            for (int j = 0; j < _positions.Count; ++j) {
                StowagePosition position = _positions[j];
                Y[j].Name = "y_" + position.Name;
                if (!position.Status.PredicateWeightNeeded) {
                    SetFlt64Range(Y[j], Flt64.Zero, Flt64.Zero);
                    continue;
                }
                SetFlt64Range(Y[j], Flt64.Zero, new Flt64(position.Mlw.Mlw));
                Try added = model.Add(Y[j]);
                if (!added.IsOk) {
                    return added;
                }
            }

            Z = new UIntVariable1("z", Shape1.Invoke(_positions.Count));
            for (int j = 0; j < _positions.Count; ++j) {
                StowagePosition position = _positions[j];
                Z[j].Name = "z_" + position.Name;
                if (!position.Status.RecommendedWeightNeeded) {
                    SetUInt64Range(Z[j], 0UL, 0UL);
                    continue;
                }
                SetUInt64Range(Z[j], 0UL, (ulong)System.Math.Floor(position.Mlw.Mlw));
                Try added = model.Add(Z[j]);
                if (!added.IsOk) {
                    return added;
                }
            }
        }

        _loadAmount.Clear();
        for (int j = 0; j < _positions.Count; ++j) {
            var polynomial = new MutableLinearPolynomial<Flt64>();
            for (int i = 0; i < _items.Count; ++i) {
                if (_stowage.StowageSymbol(i, j) is { } stowageSymbol) {
                    polynomial.AddMonomial(new LinearMonomial<Flt64>(Flt64.One, stowageSymbol));
                }
            }
            var symbol = new LinearExpressionSymbol(
                polynomial,
                name: "load_amount_" + _positions[j].Name);
            // 装载量加了上界 `range.leq(mla)`：件数上限是**舱位规格**，不是靠 `stowage` 的二元变量碰巧凑出来的。
            // 少了这一行，一个 3 槽位舱位里装 5 件在模型层面完全合法——二元变量各自为 1，加起来就是 5。
            // The code bounds the amount by `range.leq(mla)`: the piece limit is a **position specification**, not something the binary `stowage` variables happen to imply.
            // Without it a 3-slot position holding 5 pieces is perfectly legal in the model — five binaries set to one and the sum is five.
            Result<ValueRange<Flt64>, ErrorCode, Error<ErrorCode>> amountRange =
                ValueRange<Flt64>.Of(Flt64.Zero, new Flt64(_positions[j].Mla));
            if (amountRange.IsOk) {
                symbol.Range.SetRange(amountRange.Value);
            }
            Try added = model.Add(symbol);
            if (!added.IsOk) {
                return added;
            }
            _loadAmount[j] = symbol;
        }

        _full.Clear();
        for (int j = 0; j < _positions.Count; ++j) {
            StowagePosition position = _positions[j];
            LinearExpressionSymbol amount = _loadAmount[j];
            if (!position.Status.Available) {
                // 不可用的舱位永远满（取值为常数 1）：它不参与装载，别让别的行去用它的容量。
                // An unavailable position is always full (a constant of one), so no other row may use its capacity.
                var full = new LinearExpressionSymbol(
                    new MutableLinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), Flt64.One),
                    name: "full_" + position.Name);
                Try addedFull = model.Add(full);
                if (!addedFull.IsOk) {
                    return addedFull;
                }
                _full[j] = full;
                continue;
            }
            if (position.Mla == 1UL) {
                // 一个位的舱位：装满即装了（`LinearExpressionSymbol(loadAmount[j], Flt64, ...)`）。
                // A single-slot position: full means loaded (a `LinearExpressionSymbol` over `loadAmount[j]`).
                var full = new LinearExpressionSymbol(
                    new MutableLinearPolynomial<Flt64>(amount.Polynomial.Monomials, amount.Polynomial.Constant),
                    name: "full_" + position.Name);
                Try addedFull = model.Add(full);
                if (!addedFull.IsOk) {
                    return addedFull;
                }
                _full[j] = full;
                continue;
            }

            var sameAs = new SameAsFunction(
                inequalities: new[] {
                    new LinearInequality<Flt64>(
                        amount.Polynomial,
                        new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), new Flt64(position.Mla)),
                        Comparison.EQ,
                        "full_" + position.Name),
                },
                epsilon: Flt64.One,
                constraint: true,
                bigM: new Flt64(position.Mla + 1.0),
                name: "full_" + position.Name);
            var adapted = new LinearFunctionSymbolAdapter<Flt64>(sameAs);
            Try addedAdapter = model.Add(adapted);
            if (!addedAdapter.IsOk) {
                return addedAdapter;
            }
            _full[j] = adapted;
        }

        // 谓词装载重量松弛：`y` 与"该舱位需要的谓词下界"之间的双侧差。**每舱位一份，与多装载方案无关**——
        // 这一段同样不在 `if (withMultiLoadingSchema)` 里。
        // The predicate load-weight slack: the two-sided gap between `y` and the lower bound this position needs. **One per position, independent of the multi-loading schema** —
        // its block is not inside `if (withMultiLoadingSchema)` either.
        _predicateLoadWeightSlack.Clear();
        _predicateLoadWeightSlackFunction.Clear();
        for (int j = 0; j < _positions.Count; ++j) {
            StowagePosition position = _positions[j];
            if (!position.Status.PredicateWeightNeeded) {
                // 不需要谓词重量的舱位：常数零。**仍然建符号**，因为下游（目标、报告）按下标取用，缺一个就得处处判空。
                // A position that does not need the predicate weight gets the constant zero. The symbol is **still created** because downstream lookups are by index; a hole
                // would force a null check everywhere.
                var zero = new LinearExpressionSymbol(
                    new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero),
                    name: "predicate_load_weight_slack_" + position.Name);
                Try addedZero = model.Add(zero);
                if (!addedZero.IsOk) {
                    return addedZero;
                }
                _predicateLoadWeightSlack[j] = zero;
                continue;
            }

            if (position.Plw is null || Y is null) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.IllegalArgument,
                    "舱位 " + position.Name + " 缺少谓词装载重量规格 / Position " + position.Name +
                    " is missing predicate load weight specification"));
            }

            // 右端两支：需要装载决策时，下界要乘"该舱位装了几件"——空舱位没有下界要求；
            // 不需要决策时右端就是那个常数下界。
            // Two right-hand branches: when a stowage decision is needed the bound multiplies "how many pieces this position holds" (an empty position has no
            // lower bound), otherwise the bound is that constant.
            var right = new MutableLinearPolynomial<Flt64>();
            double min = position.Plw.Min;
            if (position.Status.StowageNeeded || position.Status.AdjustmentNeeded) {
                for (int i = 0; i < _items.Count; ++i) {
                    if (_stowage.StowageSymbol(i, j) is { } stowageSymbol) {
                        right.AddMonomial(new LinearMonomial<Flt64>(new Flt64(min), stowageSymbol));
                    }
                }
            } else {
                right.AddConstant(new Flt64(min));
            }

            var slackFunction = new SlackFunction<Flt64>(
                x: new LinearPolynomial<Flt64>(
                    new[] { new LinearMonomial<Flt64>(Flt64.One, Y[j]) },
                    Flt64.Zero),
                y: new LinearPolynomial<Flt64>(right.Monomials, right.Constant),
                converter: IIntoValue<Flt64>.Identity,
                withNegative: true,
                withPositive: true,
                name: "predicate_load_weight_slack_" + position.Name);
            var adaptedSlack = new LinearFunctionSymbolAdapter<Flt64>(slackFunction);
            Try addedSlack = model.Add(adaptedSlack);
            if (!addedSlack.IsOk) {
                return addedSlack;
            }
            _predicateLoadWeightSlack[j] = adaptedSlack;
            _predicateLoadWeightSlackFunction[j] = slackFunction;
        }

        // 推荐装载重量：只有多装载方案才有（个 if 也在这里）。项是实际重量 + 谓词松弛 y + 推荐数量 z。
        // The estimated load weight exists only under the multi-schema (its `if` is here). Its terms are the actual weights plus the predicate slack `y` plus the
        // recommended count `z`.
        _estimateLoadWeight.Clear();
        if (WithMultiLoadingSchema && Y is not null && Z is not null) {
            for (int j = 0; j < _positions.Count; ++j) {
                StowagePosition position = _positions[j];
                var polynomial = new MutableLinearPolynomial<Flt64>();
                AddItemWeights(polynomial, j);
                if (position.Status.PredicateWeightNeeded) {
                    polynomial.AddMonomial(new LinearMonomial<Flt64>(Flt64.One, Y[j]));
                }
                if (position.Status.RecommendedWeightNeeded) {
                    polynomial.AddMonomial(new LinearMonomial<Flt64>(Flt64.One, Z[j]));
                }
                var symbol = new LinearExpressionSymbol(polynomial, name: "load_weight_" + position.Name);
                Try added = model.Add(symbol);
                if (!added.IsOk) {
                    return added;
                }
                _estimateLoadWeight[j] = symbol;
            }
        }

        // 实际装载重量：只有货物重量，没有松弛也没有推荐数量，且**不看方案**。
        // The actual load weight: item weights only — no slack, no recommended count, and independent of the schema.
        _actualLoadWeight.Clear();
        for (int j = 0; j < _positions.Count; ++j) {
            var polynomial = new MutableLinearPolynomial<Flt64>();
            AddItemWeights(polynomial, j);
            var symbol = new LinearExpressionSymbol(polynomial, name: "actual_load_weight_" + _positions[j].Name);
            Try added = model.Add(symbol);
            if (!added.IsOk) {
                return added;
            }
            _actualLoadWeight[j] = symbol;
        }

        // 推荐已装载：直接取内核聚合表的占用变量。自己按件数重算会拿到另一批变量，主问题与报告脱钩。
        // The estimated loaded symbol: the kernel aggregation's occupancy variable. Recomputing from a count yields different variables and decouples the report from the master problem.
        _estimateLoaded.Clear();
        for (int j = 0; j < _positions.Count; ++j) {
            StowagePosition position = _positions[j];
            BinVar? occupied = _stowage.LoadingContext is { } context
                && context.Aggregation.Occupied.TryGetValue(position.Name, out BinVar? variable)
                    ? variable
                    : null;
            var polynomial = new MutableLinearPolynomial<Flt64>();
            if (occupied is not null) {
                polynomial.AddMonomial(new LinearMonomial<Flt64>(Flt64.One, occupied));
            }
            var symbol = new LinearExpressionSymbol(polynomial, name: "estimate_loaded_" + position.Name);
            Try added = model.Add(symbol);
            if (!added.IsOk) {
                return added;
            }
            _estimateLoaded[j] = symbol;
        }

        // 实际已装载：不可用舱位恒 1、单槽位就是装载量、多槽位需要二值化。
        // The actual loaded symbol: an unavailable position is constant one, a single slot is the amount itself, and multiple slots need binarization.
        _actualLoaded.Clear();
        for (int j = 0; j < _positions.Count; ++j) {
            StowagePosition position = _positions[j];
            LinearExpressionSymbol amount = _loadAmount[j];
            if (!position.Status.Available) {
                var full = new LinearExpressionSymbol(
                    new MutableLinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), Flt64.One),
                    name: "actual_loaded_" + position.Name);
                Try added = model.Add(full);
                if (!added.IsOk) {
                    return added;
                }
                _actualLoaded[j] = full;
            } else if (position.Mla == 1UL) {
                var single = new LinearExpressionSymbol(
                    new MutableLinearPolynomial<Flt64>(amount.Polynomial.Monomials, amount.Polynomial.Constant),
                    name: "actual_loaded_" + position.Name);
                Try added = model.Add(single);
                if (!added.IsOk) {
                    return added;
                }
                _actualLoaded[j] = single;
            } else {
                var binarization = new BinaryzationFunction(
                    amount.Polynomial,
                    bigM: new Flt64(position.Mla),
                    name: "actual_loaded_" + position.Name);
                var adapted = new LinearFunctionSymbolAdapter<Flt64>(binarization);
                Try added = model.Add(adapted);
                if (!added.IsOk) {
                    return added;
                }
                _actualLoaded[j] = adapted;
            }
        }

        // 纵向力矩两族：力臂 × 重力/千克（"每千克力"），再乘各自的重重量。**推荐与实际**各一，不能共用。
        // The two longitudinal torque families: the arm times force-per-kilogram, then times the respective weight. **Estimate and actual** are separate, never shared.
        _estimateLongitudinalTorque.Clear();
        _actualLongitudinalTorque.Clear();
        _lateralTorque.Clear();
        if (_aircraftModel is not null) {
            for (int j = 0; j < _positions.Count; ++j) {
                StowagePosition position = _positions[j];
                double longitudinal = TorquePerWeight(position.AircraftPosition.Coordinate.LongitudinalArm);
                double lateral = TorquePerWeight(position.AircraftPosition.Coordinate.LateralArm);

                if (_estimateLoadWeight.TryGetValue(j, out LinearExpressionSymbol? estimateWeight)) {
                    var estimate = new LinearExpressionSymbol(
                        Scale(estimateWeight.Polynomial, new Flt64(longitudinal)),
                        name: "load_longitudinal_torque_" + position.Name);
                    Try addedEstimate = model.Add(estimate);
                    if (!addedEstimate.IsOk) {
                        return addedEstimate;
                    }
                    _estimateLongitudinalTorque[j] = estimate;

                    var lateralTorque = new LinearExpressionSymbol(
                        Scale(estimateWeight.Polynomial, new Flt64(lateral)),
                        name: "load_lateral_torque_" + position.Name);
                    Try addedLateral = model.Add(lateralTorque);
                    if (!addedLateral.IsOk) {
                        return addedLateral;
                    }
                    _lateralTorque[j] = lateralTorque;
                }

                if (_actualLoadWeight.TryGetValue(j, out LinearExpressionSymbol? actualWeight)) {
                    var actual = new LinearExpressionSymbol(
                        Scale(actualWeight.Polynomial, new Flt64(longitudinal)),
                        name: "actual_load_longitudinal_torque_" + position.Name);
                    Try addedActual = model.Add(actual);
                    if (!addedActual.IsOk) {
                        return addedActual;
                    }
                    _actualLongitudinalTorque[j] = actual;
                }
            }
        }

        // CLIM 只在宽体上存在（由 `if (aircraftModel.wideBody)` 守卫）。窄体建出来会多一族无人约束的力臂符号。
        // CLIM exists only on wide bodies (guarded by `if (aircraftModel.wideBody)`). Building it on a narrow body adds an unconstrained arm family.
        _clim.Clear();
        if (_aircraftModel is not null && _aircraftModel.WideBody) {
            for (int j = 0; j < _positions.Count; ++j) {
                if (!_estimateLongitudinalTorque.TryGetValue(j, out LinearExpressionSymbol? torque)) {
                    continue;
                }
                var clim = new LinearExpressionSymbol(
                    new MutableLinearPolynomial<Flt64>(torque.Polynomial.Monomials, torque.Polynomial.Constant),
                    name: "load_clim_" + _positions[j].Name);
                Try added = model.Add(clim);
                if (!added.IsOk) {
                    return added;
                }
                _clim[j] = clim;
            }
        }

        // 指数：权重是"该货在本次舱位的指数系数"（由 Formula 的力臂-指数换算得到），再叠 y/z 两族。
        // The index: the weight is the cargo's index coefficient at this position (from the formula's arm-to-index conversion), plus the y/z families.
        _index.Clear();
        if (_aircraftModel is not null && _formula is not null) {
            for (int j = 0; j < _positions.Count; ++j) {
                StowagePosition position = _positions[j];
                double arm = position.AircraftPosition.Coordinate.LongitudinalArm;
                var polynomial = new MutableLinearPolynomial<Flt64>();
                for (int i = 0; i < _items.Count; ++i) {
                    if (_stowage.StowageSymbol(i, j) is { } stowageSymbol) {
                        double coefficient = _formula.IndexFromWeight(_items[i].Weight, arm);
                        polynomial.AddMonomial(new LinearMonomial<Flt64>(new Flt64(coefficient), stowageSymbol));
                    }
                }
                if (position.Status.PredicateWeightNeeded || position.Status.RecommendedWeightNeeded) {
                    double indexPerWeight = _formula.IndexFromWeight(1.0, arm);
                    if (position.Status.PredicateWeightNeeded && Y is not null) {
                        polynomial.AddMonomial(new LinearMonomial<Flt64>(new Flt64(indexPerWeight), Y[j]));
                    }
                    if (position.Status.RecommendedWeightNeeded && Z is not null) {
                        polynomial.AddMonomial(new LinearMonomial<Flt64>(new Flt64(indexPerWeight), Z[j]));
                    }
                }
                var symbol = new LinearExpressionSymbol(polynomial, name: "load_index_" + position.Name);
                Try added = model.Add(symbol);
                if (!added.IsOk) {
                    return added;
                }
                _index[j] = symbol;
            }
        }

        // 把 y/z 的重量**接进内核的容量约束**：不接就等于凭空多装了几百公斤，容量行看不见它们。
        // **无条件**（不在任何 `if` 里）： 曾把它包进 `if (WithMultiLoadingSchema)`，于是单方案下 `load_amount` 的容量
        // 联动整段消失——而单方案恰恰是最常用的那种。
        // Link the y/z weight **into the kernel's capacity row**: without it the slack is hundreds of kilograms the capacity row never sees.
        // **Unconditionally** (the code has no `if` here): wrapped this in `if (WithMultiLoadingSchema)`, so under the single schema the whole capacity link vanished — and the
        // single schema is the common case.
        if (Y is not null && Z is not null) {
            for (int j = 0; j < _positions.Count; ++j) {
                StowagePosition position = _positions[j];
                var additional = new MutableLinearPolynomial<Flt64>();
                if (position.Status.PredicateWeightNeeded) {
                    additional.AddMonomial(new LinearMonomial<Flt64>(Flt64.One, Y[j]));
                }
                if (position.Status.RecommendedWeightNeeded) {
                    additional.AddMonomial(new LinearMonomial<Flt64>(Flt64.One, Z[j]));
                }
                if (_stowage.LoadingContext is null) {
                    continue;
                }
                Try linked = _stowage.LoadingContext.LinkAdditionalWeight(
                    position.Name,
                    new LinearPolynomial<Flt64>(additional.Monomials, additional.Constant),
                    model);
                if (!linked.IsOk) {
                    return linked;
                }
            }
        }

        _registeredIn = model;
        return Results.Ok<Success>(Results.SuccessInstance);
    }

    private static void SetFlt64Range(
        CombinationVariableItem<Flt64, UContinuous> variable,
        Flt64 lower,
        Flt64 upper) {
        Result<ValueRange<Flt64>, ErrorCode, Error<ErrorCode>> range = ValueRange<Flt64>.Of(lower, upper);
        if (range.IsOk) {
            variable.Range.SetRange(range.Value);
        }
    }

    private static void SetUInt64Range(
        CombinationVariableItem<Fuookami.Ospf.Math.Algebra.Number.UInt64, UInteger> variable,
        ulong lower,
        ulong upper) {
        Result<ValueRange<Fuookami.Ospf.Math.Algebra.Number.UInt64>, ErrorCode, Error<ErrorCode>> range =
            ValueRange<Fuookami.Ospf.Math.Algebra.Number.UInt64>.Of(
                new Fuookami.Ospf.Math.Algebra.Number.UInt64(lower),
                new Fuookami.Ospf.Math.Algebra.Number.UInt64(upper));
        if (range.IsOk) {
            variable.Range.SetRange(range.Value);
        }
    }

    private void AddItemWeights(MutableLinearPolynomial<Flt64> polynomial, int positionIndex) {
        for (int i = 0; i < _items.Count; ++i) {
            if (_stowage.StowageSymbol(i, positionIndex) is { } stowageSymbol) {
                polynomial.AddMonomial(
                    new LinearMonomial<Flt64>(new Flt64(_items[i].Weight), stowageSymbol));
            }
        }
    }

    /// <summary>
    /// 每千克重量的力矩 / Torque per kilogram of weight.
    /// </summary>
    /// <param name="arm">力臂（英寸）/ The arm in inches.</param>
    /// <returns>千克力·英寸每千克 / Kilogram-force inches per kilogram.</returns>
    /// <remarks>
    /// `gravity(Quantity(one, weightUnit)) * longitudinalArm`——**先**把 1 千克变成力，**再**乘力臂量（英寸），得到力矩量，
    /// 最后换算到 `torqueUnit`。顺序反了（先换算再乘）会拿"力/质量"去对"力×长度"做量纲检查，直接失败。
    /// `item.weight` 是带单位的量；C# 的 `Item.Weight` 是无单位 `double`，这里按 `weightUnit`（千克）解读，与 `y`/`z` 一致。
    ///
    /// The code is `gravity(Quantity(one, weightUnit)) * longitudinalArm` — turn one kilogram into a force **first**, then multiply by the arm quantity (inches) to get a
    /// torque, and finally convert to `torqueUnit`. Doing it the other way round asks the dimensionality check to accept force/mass against force×length, which fails. The
    /// `item.weight` is a unit-carrying quantity; C#'s `Item.Weight` is a bare `double`, interpreted here as `weightUnit` (kilograms), consistent with `y`/`z`.
    /// </remarks>
    private static double TorquePerWeight(double arm) {
        Quantity<Flt64> force = AircraftModel.Gravity(new Quantity<Flt64>(Flt64.One, AircraftModel.WeightUnit));
        Quantity<Flt64> moment = force.Multiply(new Quantity<Flt64>(new Flt64(arm), AircraftModel.LengthUnit));
        Result<Quantity<Flt64>, ErrorCode, Error<ErrorCode>> converted = moment.To(AircraftModel.TorqueUnit);
        return converted.IsOk ? converted.Value.Value.ToDouble() : double.NaN;
    }

    /// <summary>
    /// 整体缩放一个线性多项式 / Scale a linear polynomial wholesale.
    /// </summary>
    /// <param name="polynomial">多项式 / The polynomial.</param>
    /// <param name="factor">系数 / The factor.</param>
    /// <returns>缩放后的多项式 / The scaled polynomial.</returns>
    private static MutableLinearPolynomial<Flt64> Scale(LinearPolynomial<Flt64> polynomial, Flt64 factor) {
        var scaled = new MutableLinearPolynomial<Flt64>(constant: polynomial.Constant.Times(factor));
        foreach (LinearMonomial<Flt64> monomial in polynomial.Monomials) {
            scaled.AddMonomial(new LinearMonomial<Flt64>(monomial.Coefficient.Times(factor), monomial.Symbol));
        }
        return scaled;
    }
}
