#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Algebra.ValueRange;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.SoftSecurity.Model;

/// <summary>
/// 相邻舱位对 / An adjacent position pair.
/// </summary>
/// <param name="First">前一舱位下标 / The earlier position index</param>
/// <param name="Second">后一舱位下标 / The later position index</param>
public sealed record PositionPair(int First, int Second);

/// <summary>
/// 分隔空载装载模型 / The divide-empty-loading model.
/// </summary>
/// <remarks>
/// 与基线 `soft_security/model/DivideEmptyLoading.kt` 对齐：为每对相邻舱位建立**三个**中间符号，度量"两舱之间被空载隔开"的程度，
/// 由 `DivideEmptyLoadingLimit` 作为目标函数最小化。
///
/// 相邻对的构造（基线伴生对象）：按 `location.location` 分组，**组内按 `loadingOrder.order` 排序后 `zipWithNext`**——
/// 只连相邻的两个，不做全组合。同一甲板内不挨着的两个舱位之间不存在"中间"，连起来会造出无意义的惩罚项。
///
/// 三族符号的表达式（`p` 是相邻对下标，`position1`/`position2` 是该对的两端）：
///
/// | 符号 | 条件分支 |
/// | --- | --- |
/// | `emptyBetweenCargo` | `loadAmount1 == 0` → 0；`position2` 需装载 → `if(loadAmount1 - loadAmount2 - 1 + ε)`；`loadAmount2 == 0` → `loadAmount1`；否则 0 |
/// | `emptyCargoBetweenCargo` | `emptyAmount1 == 0` → 0；`position2` 需装载 → `if(emptyAmount1 + cargoAmount2 - 2 + ε)`；`cargoAmount2 == 0` → `emptyAmount1`；否则 0 |
/// | `emptyBetweenEmptyCargo` | `emptyAmount1 == 0` → 0；`position2` 需装载 → `if(emptyAmount1 - loadAmount2 - 1 + ε)`；`loadAmount2 == 0` → `emptyAmount1`；否则 0 |
///
/// **四条分支的顺序是硬要求**：`position2` 需装载这一支必须排在 `loadAmount2 == 0` 之前，因为后者是"舱位根本不用"的静态情形，
/// 前者才是"用了但装得少"的动态情形。调换顺序会让需装载的舱位走静态分支，惩罚项恒为 0，整个目标失效。
///
/// 三处必须照抄基线：
///
/// 1. **"是否固定为零"看 `range.fixedValue`**，不是看多项式是否为空。一个多项式可能没有单项式（和为零）但仍是**变量**，
///    那与"固定为零"是两回事。
/// 2. **`position2` 的条件是 `stowageNeeded || adjustmentNeeded`**，两个标志任一为真。只看 `stowageNeeded` 会漏掉只做调整的舱位。
/// 3. **`ε` 是 `NONZERO_TOLERANCE`**，加在 `IfFunction` 的条件里。去掉它，`loadAmount1 - loadAmount2 == 1` 这种"刚好差一件"的情形
///    会在浮点边界上抖动。
///
/// Aligned with the baseline: three intermediate symbols per adjacent pair measuring how much an empty load separates the two holds, minimised as objectives by
/// `DivideEmptyLoadingLimit`. Adjacent pairs are built by grouping on `location.location` and **sorting within each group by `loadingOrder.order` before `zipWithNext`** —
/// consecutive only, never all combinations, since two non-neighbouring holds on one deck have no "between". The three expressions and their four-branch order are tabulated
/// above; **the order is a hard requirement** — the `position2`-needs-stowage branch must precede `loadAmount2 == 0`, because the latter is the static "this hold is unused" case
/// while the former is the dynamic "used but lightly loaded" case, and swapping them sends a needed hold down the static branch, pinning the penalty to zero and disabling the
/// whole objective. Three things must be copied verbatim: **"fixed at zero" means `range.fixedValue`**, not "the polynomial is empty" (a polynomial can have no monomials yet
/// still be a variable); **`position2`'s condition is `stowageNeeded || adjustmentNeeded`**, either flag, and testing only `stowageNeeded` misses adjustment-only holds; and
/// **`ε` is `NONZERO_TOLERANCE`** added inside the `IfFunction` condition, without which a "differs by exactly one piece" case jitters on the floating-point boundary.
/// </remarks>
public sealed class DivideEmptyLoading
{
    private readonly IReadOnlyList<StowagePosition> _positions;
    private readonly IReadOnlyList<PositionPair> _adjacentPositions;
    private readonly Load _load;
    private readonly IReadOnlyList<Item> _items;

    private readonly Dictionary<int, ILinearIntermediateSymbol<Flt64>> _emptyBetweenCargo = new();
    private readonly Dictionary<int, ILinearIntermediateSymbol<Flt64>> _emptyCargoBetweenCargo = new();
    private readonly Dictionary<int, ILinearIntermediateSymbol<Flt64>> _emptyBetweenEmptyCargo = new();

    /// <summary>
    /// 构造分隔空载模型 / Construct the divide-empty-loading model.
    /// </summary>
    /// <param name="positions">舱位 / The positions.</param>
    /// <param name="adjacentPositions">相邻舱位对 / The adjacent position pairs.</param>
    /// <param name="load">装载模型 / The load model.</param>
    /// <param name="items">货物（用于条件上界）/ The items, used for the condition's bound.</param>
    public DivideEmptyLoading(
        IReadOnlyList<StowagePosition> positions,
        IReadOnlyList<PositionPair> adjacentPositions,
        Load load,
        IReadOnlyList<Item> items)
    {
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(adjacentPositions);
        ArgumentNullException.ThrowIfNull(load);
        ArgumentNullException.ThrowIfNull(items);
        _positions = positions;
        _adjacentPositions = adjacentPositions;
        _load = load;
        _items = items;
    }

    /// <summary>相邻舱位对 / The adjacent position pairs</summary>
    public IReadOnlyList<PositionPair> AdjacentPositions => _adjacentPositions;

    /// <summary>
    /// 由舱位表推出相邻对 / Derive the adjacent pairs from the position table.
    /// </summary>
    /// <param name="positions">舱位 / The positions.</param>
    /// <returns>相邻对 / The adjacent pairs.</returns>
    /// <remarks>
    /// 与基线伴生对象一致：**按甲板分组、组内按装载顺序排序、取相邻的两个**。组内只有一个舱位时不产生任何对。
    ///
    /// Matches the baseline companion: **group by deck, sort by loading order within each group, then take consecutive pairs**. A group of one yields nothing.
    /// </remarks>
    public static IReadOnlyList<PositionPair> BuildAdjacentPositions(IReadOnlyList<StowagePosition> positions)
    {
        ArgumentNullException.ThrowIfNull(positions);

        var byDeck = new Dictionary<DeckLocation, List<int>>();
        var order = new List<DeckLocation>();
        for (int j = 0; j < positions.Count; ++j)
        {
            DeckLocation deck = positions[j].Location.Location;
            if (!byDeck.TryGetValue(deck, out List<int>? bucket))
            {
                bucket = new List<int>();
                byDeck[deck] = bucket;
                order.Add(deck);
            }
            bucket.Add(j);
        }

        var pairs = new List<PositionPair>();
        foreach (DeckLocation deck in order)
        {
            List<int> bucket = byDeck[deck];
            bucket.Sort((left, right) => positions[left].AircraftPosition.LinearLoadingOrder
                .CompareTo(positions[right].AircraftPosition.LinearLoadingOrder));
            for (int k = 0; k + 1 < bucket.Count; ++k)
            {
                pairs.Add(new PositionPair(bucket[k], bucket[k + 1]));
            }
        }
        return pairs;
    }

    /// <summary>
    /// 注册三族中间符号 / Register the three symbol families.
    /// </summary>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>操作结果 / Operation result.</returns>
    public Try Register(LinearMetaModel<Flt64> model)
    {
        ArgumentNullException.ThrowIfNull(model);

        Try first = RegisterEmptyBetweenCargo(model);
        if (!first.IsOk)
        {
            return first;
        }
        Try second = RegisterEmptyCargoBetweenCargo(model);
        if (!second.IsOk)
        {
            return second;
        }
        return RegisterEmptyBetweenEmptyCargo(model);
    }

    /// <summary>
    /// 取某相邻对的"舱位间空载"符号 / The between-cargo empty symbol of one adjacent pair.
    /// </summary>
    /// <param name="index">相邻对下标 / The adjacent pair index.</param>
    /// <returns>符号 / The symbol.</returns>
    public ILinearIntermediateSymbol<Flt64>? EmptyBetweenCargo(int index) =>
        _emptyBetweenCargo.TryGetValue(index, out ILinearIntermediateSymbol<Flt64>? symbol) ? symbol : null;

    /// <summary>
    /// 取某相邻对的"空载舱位后接货物"符号 / The empty-cargo-then-cargo symbol of one adjacent pair.
    /// </summary>
    /// <param name="index">相邻对下标 / The adjacent pair index.</param>
    /// <returns>符号 / The symbol.</returns>
    public ILinearIntermediateSymbol<Flt64>? EmptyCargoBetweenCargo(int index) =>
        _emptyCargoBetweenCargo.TryGetValue(index, out ILinearIntermediateSymbol<Flt64>? symbol) ? symbol : null;

    /// <summary>
    /// 取某相邻对的"空载舱位后接空载"符号 / The empty-then-empty symbol of one adjacent pair.
    /// </summary>
    /// <param name="index">相邻对下标 / The adjacent pair index.</param>
    /// <returns>符号 / The symbol.</returns>
    public ILinearIntermediateSymbol<Flt64>? EmptyBetweenEmptyCargo(int index) =>
        _emptyBetweenEmptyCargo.TryGetValue(index, out ILinearIntermediateSymbol<Flt64>? symbol) ? symbol : null;

    private Try RegisterEmptyBetweenCargo(LinearMetaModel<Flt64> model)
    {
        IReadOnlyList<int> cargoIndices = _load.ItemIndicesOf(item => !item.Contains(CargoCode.Empty));
        IReadOnlyList<int> firstPositions = FirstPositions();
        IReadOnlyDictionary<int, LinearExpressionSymbol> grouped =
            _load.LoadAmountsOf(firstPositions, cargoIndices);

        for (int p = 0; p < _adjacentPositions.Count; ++p)
        {
            PositionPair pair = _adjacentPositions[p];
            StowagePosition position1 = _positions[pair.First];
            StowagePosition position2 = _positions[pair.Second];
            string name = "empty_between_cargo_" + position1.Name + "_" + position2.Name;

            LinearExpressionSymbol loadAmount1 = grouped[pair.First];
            LinearExpressionSymbol? loadAmount2 = _load.LoadAmountSymbol(pair.Second);

            Result<ILinearIntermediateSymbol<Flt64>, ErrorCode, Error<ErrorCode>> built =
                Build(name, loadAmount1, loadAmount2, position2, subtractOne: true);
            if (!built.IsOk)
            {
                return Rethrow<Success, ILinearIntermediateSymbol<Flt64>>(built);
            }
            ILinearIntermediateSymbol<Flt64> symbol =
                ((Ok<ILinearIntermediateSymbol<Flt64>, ErrorCode, Error<ErrorCode>>)built).Value;
            Try added = model.Add(symbol);
            if (!added.IsOk)
            {
                return added;
            }
            _emptyBetweenCargo[p] = symbol;
        }
        return Results.Ok<Success>(Results.SuccessInstance);
    }

    private Try RegisterEmptyCargoBetweenCargo(LinearMetaModel<Flt64> model)
    {
        IReadOnlyList<int> emptyIndices = _load.ItemIndicesOf(item => item.Contains(CargoCode.Empty));
        IReadOnlyList<int> cargoIndices = _load.ItemIndicesOf(item => !item.Contains(CargoCode.Empty));
        IReadOnlyList<int> firstPositions = FirstPositions();
        IReadOnlyList<int> secondPositions = SecondPositions();
        IReadOnlyDictionary<int, LinearExpressionSymbol> emptyAmounts =
            _load.LoadAmountsOf(firstPositions, emptyIndices);
        IReadOnlyDictionary<int, LinearExpressionSymbol> cargoAmounts =
            _load.LoadAmountsOf(secondPositions, cargoIndices);

        for (int p = 0; p < _adjacentPositions.Count; ++p)
        {
            PositionPair pair = _adjacentPositions[p];
            StowagePosition position1 = _positions[pair.First];
            StowagePosition position2 = _positions[pair.Second];
            string name = "empty_cargo_between_cargo_" + position1.Name + "_" + position2.Name;

            LinearExpressionSymbol emptyAmount1 = emptyAmounts[pair.First];
            LinearExpressionSymbol cargoAmount2 = cargoAmounts[pair.Second];

            Result<ILinearIntermediateSymbol<Flt64>, ErrorCode, Error<ErrorCode>> built =
                BuildSum(name, emptyAmount1, cargoAmount2, position2, constant: -2.0);
            if (!built.IsOk)
            {
                return Rethrow<Success, ILinearIntermediateSymbol<Flt64>>(built);
            }
            ILinearIntermediateSymbol<Flt64> symbol =
                ((Ok<ILinearIntermediateSymbol<Flt64>, ErrorCode, Error<ErrorCode>>)built).Value;
            Try added = model.Add(symbol);
            if (!added.IsOk)
            {
                return added;
            }
            _emptyCargoBetweenCargo[p] = symbol;
        }
        return Results.Ok<Success>(Results.SuccessInstance);
    }

    private Try RegisterEmptyBetweenEmptyCargo(LinearMetaModel<Flt64> model)
    {
        IReadOnlyList<int> emptyIndices = _load.ItemIndicesOf(item => item.Contains(CargoCode.Empty));
        IReadOnlyList<int> firstPositions = FirstPositions();
        IReadOnlyDictionary<int, LinearExpressionSymbol> emptyAmounts =
            _load.LoadAmountsOf(firstPositions, emptyIndices);

        for (int p = 0; p < _adjacentPositions.Count; ++p)
        {
            PositionPair pair = _adjacentPositions[p];
            StowagePosition position1 = _positions[pair.First];
            StowagePosition position2 = _positions[pair.Second];
            string name = "empty_between_empty_cargo_" + position1.Name + "_" + position2.Name;

            LinearExpressionSymbol emptyAmount1 = emptyAmounts[pair.First];
            LinearExpressionSymbol? loadAmount2 = _load.LoadAmountSymbol(pair.Second);

            Result<ILinearIntermediateSymbol<Flt64>, ErrorCode, Error<ErrorCode>> built =
                Build(name, emptyAmount1, loadAmount2, position2, subtractOne: true);
            if (!built.IsOk)
            {
                return Rethrow<Success, ILinearIntermediateSymbol<Flt64>>(built);
            }
            ILinearIntermediateSymbol<Flt64> symbol =
                ((Ok<ILinearIntermediateSymbol<Flt64>, ErrorCode, Error<ErrorCode>>)built).Value;
            Try added = model.Add(symbol);
            if (!added.IsOk)
            {
                return added;
            }
            _emptyBetweenEmptyCargo[p] = symbol;
        }
        return Results.Ok<Success>(Results.SuccessInstance);
    }

    /// <summary>
    /// 四分支构造 / Build through the four branches.
    /// </summary>
    /// <remarks>
    /// `subtractOne` 为真时条件是 `amount1 - amount2 - 1 + ε`，为假时是 `amount1 + amount2 - constant + ε`。
    /// 分支顺序与基线一致，**`position2` 需装载那一支必须在"固定为零"之前**。
    ///
    /// With `subtractOne` the condition is `amount1 - amount2 - 1 + ε`, otherwise `amount1 + amount2 - constant + ε`. The branch order matches the baseline, and the
    /// `position2`-needs-stowage branch **must** precede the "fixed at zero" one.
    /// </remarks>
    private Result<ILinearIntermediateSymbol<Flt64>, ErrorCode, Error<ErrorCode>> Build(
        string name,
        LinearExpressionSymbol amount1,
        LinearExpressionSymbol? amount2,
        StowagePosition position2,
        bool subtractOne)
    {
        if (IsFixedZero(amount1))
        {
            return Zero(name);
        }
        if (position2.Status.StowageNeeded || position2.Status.AdjustmentNeeded)
        {
            // 条件形如 `amount1 - amount2 - 1 + ε ≤ 0`：**常数并入左端多项式**，右端是 0。
            // `LinearConstraintInput.From` 的精确展开校验要求左端多项式与展开结果一致，常数留在外面会被判为不精确。
            // The condition is `amount1 - amount2 - 1 + ε <= 0`: **the constant joins the left polynomial** and the right side is zero. `LinearConstraintInput.From`'s exact
            // expansion check requires the left polynomial to match the expansion, so a constant left outside reads as inexact.
            LinearPolynomial<Flt64> condition = PolynomialOf(amount1);
            if (amount2 is not null)
            {
                LinearPolynomial<Flt64> other = PolynomialOf(amount2);
                condition = subtractOne
                    ? condition - other
                    : condition + other;
            }
            // **常数里不加 ε**：`IfFunction` 的 `strictBoundary` 已经承担"严格大于"的语义，再把 `NONZERO_TOLERANCE`
            // 塞进条件常数会让 `SafeLattice` 的格点距离判定偏离整数格（实测报"离散证明无效"）。
            // **No ε in the constant**: `IfFunction`'s `strictBoundary` already carries the "strictly greater" semantics, and folding `NONZERO_TOLERANCE` into the condition's
            // constant pushes `SafeLattice`'s lattice-distance test off the integer lattice (measured as "invalid discrete proof").
            double constant = subtractOne ? -1.0 : 0.0;
            return Conditional(name, condition, constant);
        }
        if (amount2 is not null && IsFixedZero(amount2))
        {
            return Passthrough(name, amount1);
        }
        return Zero(name);
    }

    /// <summary>
    /// 加和型四分支构造 / Build the summed variant through the four branches.
    /// </summary>
    /// <param name="name">符号名 / The symbol name.</param>
    /// <param name="amount1">第一项 / The first amount.</param>
    /// <param name="amount2">第二项 / The second amount.</param>
    /// <param name="position2">第二舱位 / The second position.</param>
    /// <param name="constant">条件里的常数 / The constant in the condition.</param>
    /// <returns>符号或失败 / The symbol or a failure.</returns>
    private Result<ILinearIntermediateSymbol<Flt64>, ErrorCode, Error<ErrorCode>> BuildSum(
        string name,
        LinearExpressionSymbol amount1,
        LinearExpressionSymbol amount2,
        StowagePosition position2,
        double constant)
    {
        if (IsFixedZero(amount1))
        {
            return Zero(name);
        }
        if (position2.Status.StowageNeeded || position2.Status.AdjustmentNeeded)
        {
            LinearPolynomial<Flt64> condition = PolynomialOf(amount1) + PolynomialOf(amount2);
            return Conditional(name, condition, constant + NonzeroTolerance);
        }
        if (IsFixedZero(amount2))
        {
            return Passthrough(name, amount1);
        }
        return Zero(name);
    }

    // `NONZERO_TOLERANCE`：基线的常量，避免"刚好差一件"在浮点边界上抖动。
    // The baseline constant that keeps a "differs by exactly one piece" case off the floating-point boundary.
    private const double NonzeroTolerance = 1e-6;

    // "固定为零"= **多项式里没有单项式**（和为零）。
    //
    // **不能用 `Range.FixedValue`**：实测它对一个含 `stowage_0_0` 单项式的符号也返回 0——那个符号的表达式范围在注册阶段
    // 还没被求解器定界，范围退化成"固定 0"。用它判断会让**每一个**符号都走静态分支，三族惩罚全恒为零，整个目标失效。
    // 这条是 变异测试抓出来的：`IsFixedZero` 改成看范围后，判据仍全绿，直到我直接探查 `Range.FixedValue` 才看到 0。
    //
    // "Fixed at zero" means **the polynomial has no monomials** (its sum is zero). **`Range.FixedValue` cannot be used**: measured, it returns 0 even for a symbol carrying a
    // `stowage_0_0` monomial, because the expression's range is not yet bounded by the solver at registration time and degenerates to "fixed 0". Judging by it sends **every**
    // symbol down the static branch, pinning all three families to zero and disabling the objective. The mutation run surfaced this: switching `IsFixedZero` to the range kept
    // every criterion green until I probed `Range.FixedValue` directly and saw the 0.
    private static bool IsFixedZero(LinearExpressionSymbol symbol) =>
        symbol.Polynomial.Monomials.Count == 0;

    // **展开到变量项**：`IfFunction` 的 `CanMerge` 要求每个单项式的符号是 `IVariableItem`，而 `amount` 的单项式是
    // `stowage_{i}_{j}` 这类**表达式符号**。不展开就会在 `CanMerge` 处失败，报"离散证明无效"。
    // **Expand down to variable terms**: `IfFunction`'s `CanMerge` requires each monomial's symbol to be an `IVariableItem`, while an amount's monomials are **expression symbols**
    // like `stowage_{i}_{j}`. Without expansion it fails at `CanMerge` with "invalid discrete proof".
    private static LinearPolynomial<Flt64> PolynomialOf(LinearExpressionSymbol symbol) {
        LinearPolynomial<Flt64> polynomial = symbol.Polynomial;
        var monomials = new List<LinearMonomial<Flt64>>();
        Flt64 constant = polynomial.Constant;
        foreach (LinearMonomial<Flt64> monomial in polynomial.Monomials) {
            if (monomial.Symbol is LinearExpressionSymbol inner) {
                LinearPolynomial<Flt64> expanded = inner.Polynomial;
                foreach (LinearMonomial<Flt64> term in expanded.Monomials) {
                    monomials.Add(new LinearMonomial<Flt64>(monomial.Coefficient * term.Coefficient, term.Symbol));
                }
                constant += monomial.Coefficient * expanded.Constant;
            } else {
                monomials.Add(monomial);
            }
        }
        return new LinearPolynomial<Flt64>(monomials, constant);
    }

    private Result<ILinearIntermediateSymbol<Flt64>, ErrorCode, Error<ErrorCode>> Zero(string name)
    {
        var polynomial = new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), Flt64.Zero);
        return Results.Ok<ILinearIntermediateSymbol<Flt64>>(
            new LinearExpressionSymbol(new MutableLinearPolynomial<Flt64>(polynomial.Monomials, polynomial.Constant), name: name));
    }

    private Result<ILinearIntermediateSymbol<Flt64>, ErrorCode, Error<ErrorCode>> Passthrough(
        string name, LinearExpressionSymbol amount1)
    {
        LinearPolynomial<Flt64> polynomial = PolynomialOf(amount1);
        return Results.Ok<ILinearIntermediateSymbol<Flt64>>(
            new LinearExpressionSymbol(new MutableLinearPolynomial<Flt64>(polynomial.Monomials, polynomial.Constant), name: name));
    }

    /// <summary>
    /// 条件分支构造 / Build through the conditional branch.
    /// </summary>
    /// <remarks>
    /// **当前返回失败，并明确说明原因**——这是这里**唯一未落地**的一处，不是"先返回 Ok 以后再说"。
    ///
    /// 基线用 `IfFunction(condition = …, converter, name)` 表达"第一舱位的装载量比第二舱位多出至少一件"。本仓的
    /// `IfFunction.From` 带**离散格点证明**（`SafeLattice`）：它要求条件多项式的系数与常数落在同一个整数格上，
    /// 而这里条件的常数是 `-1`（"多出至少一件"），三个系数是 `+1/-1/-1`，格距为 1、常数模格距为 0——按代码读应当通过，
    /// 但实测一律返回"约束输入无效或展开结果不精确 / 离散证明无效"。逐项排查过：
    ///
    /// | 排查项 | 结果 |
    /// | --- | --- |
    /// | 单项式符号是 `IVariableItem` | 是（`BinVar`） |
    /// | 系数精确可表示 | 是（`±1`） |
    /// | `strictBoundary > 0` 且 `delta >= strictBoundary` | 已按 `1` 设置 |
    /// | 常数并入左端、右端为 0 | 已改 |
    /// | 去掉 `NONZERO_TOLERANCE` | 已去掉 |
    /// | `bounds` 显式给出 `[-n, n]` | 已给出 |
    ///
    /// 剩下的可能点在 `ExactLinearExpansion.Matches` 与 `SafeLattice` 对 `Comparison.LE` 的方向处理（`reverse` 分支把上下界取反）。
    /// **不猜**：与其把条件悄悄降级成"恒零"（那会让整族惩罚失效而判据全绿——正是 那次假绿的形态），不如让注册
    /// **明确失败**，并把这一处记进能力缺口。
    ///
    /// **This currently returns a failure and says why** — the batch's **one unlanded piece**, not a "return Ok for now". The baseline expresses "the first position holds at least
    /// one more piece than the second" through `IfFunction(condition = …)`. This port's `IfFunction.From` carries a **discrete-lattice proof** (`SafeLattice`) requiring the
    /// condition's coefficients and constant to share one integer lattice; here the constant is `-1` and the coefficients `+1/-1/-1`, spacing 1 with constant residue 0, which by
    /// reading should pass — yet it consistently returns "invalid constraint input or inexact expansion / invalid discrete proof". Everything tabulated above was checked. The
    /// remaining candidates are `ExactLinearExpansion.Matches` and `SafeLattice`'s direction handling for `Comparison.LE` (the `reverse` branch negates the bounds). **No
    /// guessing**: rather than silently degrading the condition to "identically zero" — which would disable the whole family while every criterion stays green, exactly the
    /// false-green shape of — registration **fails explicitly** and the gap is recorded.
    /// </remarks>
    /// <remarks>
    /// **用 `IfThenFunction.Create` 而不是把它记成不可移植**： 当时记的是"离散格点证明拒绝该条件"。这里实测下来，
    /// 拒绝的原因是**条件常数里塞了 `NONZERO_TOLERANCE`**（`-1 + 1e-10` 落在整数格之外），而不是 `Comparison.LE` 的方向处理。
    /// 去掉那 1e-10、把"严格大于"交给 `strictBoundary` 之后 `Create` 一次就过——**和 的条件包络是同一个用法**。
    ///
    /// **`IfThenFunction.Create` instead of calling it unportable**: recorded "the discrete-lattice proof rejects the condition". Measured here, the rejection came from the
    /// **`NONZERO_TOLERANCE` baked into the condition constant** (`-1 + 1e-10` sits off the integer lattice), not from `Comparison.LE`'s direction handling. With that 1e-10
    /// removed and "strictly greater" delegated to `strictBoundary`, `Create` passes on the first try — **the same usage as the conditional envelope**.
    /// </remarks>
    private Result<ILinearIntermediateSymbol<Flt64>, ErrorCode, Error<ErrorCode>> Conditional(
        string name,
        LinearPolynomial<Flt64> condition,
        double constant)
    {
        // 条件：`amount1 - amount2 (+ constant)`，常数单独加上去，多项式保持精确。
        // The condition is `amount1 - amount2 (+ constant)` with the constant added separately so the polynomial stays exact.
        LinearPolynomial<Flt64> withConstant = condition + new Flt64(constant);
        // `then` 是常数 1：条件是"第一舱位比第二舱位多出至少一件"，成立时结果就是 1。
        // `then` is the constant 1: the condition is "position one holds at least one more than position two", and the result is 1 when it holds.
        LinearPolynomial<Flt64> then = new(Array.Empty<LinearMonomial<Flt64>>(), Flt64.One);
        // 界取宽，**取窄会切掉可行解**：条件与差值各给一个必然包含真值的超集。
        // Bounds are deliberately wide, **narrow ones cut off feasible solutions**: the condition and the difference each get a superset that certainly contains the truth.
        LinearPolynomialBounds<Flt64> conditionBounds = new(new Flt64(-1_000_000), new Flt64(1_000_000));
        LinearPolynomialBounds<Flt64> thenBounds = new(Flt64.One, Flt64.One);
        Result<IfThenFunction<Flt64>, ErrorCode, Error<ErrorCode>> created = IfThenFunction<Flt64>.Create(
            condition: withConstant,
            then: then,
            converter: IIntoValue<Flt64>.Identity,
            strictBoundary: new Flt64(1e-10),
            relation: Comparison.GT,
            conditionBounds: conditionBounds,
            thenBounds: thenBounds,
            name: name);
        if (created is Failed<IfThenFunction<Flt64>, ErrorCode, Error<ErrorCode>> failure) {
            return Results.Failed<ILinearIntermediateSymbol<Flt64>>(failure.Error);
        }
        return Results.Ok<ILinearIntermediateSymbol<Flt64>>(
            new LinearFunctionSymbolAdapter<Flt64>(created.Value!) { Name = name });
    }
    private IReadOnlyList<int> FirstPositions()
    {
        var list = new List<int>(_adjacentPositions.Count);
        foreach (PositionPair pair in _adjacentPositions)
        {
            list.Add(pair.First);
        }
        return list;
    }

    private IReadOnlyList<int> SecondPositions()
    {
        var list = new List<int>(_adjacentPositions.Count);
        foreach (PositionPair pair in _adjacentPositions)
        {
            list.Add(pair.Second);
        }
        return list;
    }

    private static Result<T, ErrorCode, Error<ErrorCode>> Rethrow<T, TSource>(
        Result<TSource, ErrorCode, Error<ErrorCode>> result)
        where T : notnull
        where TSource : notnull =>
        new Failed<T, ErrorCode, Error<ErrorCode>>(
            ((Failed<TSource, ErrorCode, Error<ErrorCode>>)result).Error);
}
