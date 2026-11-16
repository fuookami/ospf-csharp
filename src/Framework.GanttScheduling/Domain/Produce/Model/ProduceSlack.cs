#nullable enable

using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Symbol.Flatten;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Math.Symbol.Inequality;
using ValueRange = Fuookami.Ospf.Math.Algebra.ValueRange.ValueRange<Fuookami.Ospf.Math.Algebra.Number.Flt64>;
using Fuookami.Ospf.Utils.Concept;
using Fuookami.Ospf.Framework.Model;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.Produce.Model;
/// <summary>
/// 生产松弛工具 / Produce slack utilities.
/// </summary>
/// <remarks>
/// `produceSlack` 与任务调度那边的 `thresholdSlack` **不是同一个函数**：
/// 前者的阈值是一个**常量**（`y = 常数多项式`），后者允许 `y` 是多项式。这里沿用常量版。
///
/// 三个开关的固定组合由调用方给，`constraint` 是这里的关键：**超量 / 不足表要自己加上界约束**，
/// 所以松弛本身必须是 `constraint: false`——否则会多出一对 `x ± slack ≤/≥ y` 的行，把
/// "超出上界多少"变成"与上界的绝对差"，超量和不足两个方向就都算错了。
///
/// `produceSlack` is **not** the same function as the task-scheduling `thresholdSlack`: here the threshold is a **constant**
/// (`y` is a constant polynomial) while the other allows a polynomial `y`. The constant form is used here. The three switches are supplied by the caller, and `constraint` is the crux:
/// **the over/less tables add their own upper-bound rows**, so the slack itself must be `constraint: false` — otherwise an extra pair of `x ± slack ≤/≥ y` rows appears and turns "how far past the
/// bound" into "the absolute distance from it", making both the over and the less direction wrong.
/// </remarks>
public static class ProduceSlackHelper {
    /// <summary>创建常量线性多项式值 / Create a constant linear polynomial value</summary>
    public static Flt64 ConstantPolynomial(Flt64 value) => value;

    /// <summary>计算生产松弛值 / Calculate produce slack value</summary>
    /// <param name="x">当前值 / Current value</param>
    /// <param name="threshold">阈值 / Threshold</param>
    /// <param name="withNegative">是否包含负松弛 / Whether to include negative slack</param>
    /// <param name="withPositive">是否包含正松弛 / Whether to include positive slack</param>
    /// <returns>松弛值 / Slack value</returns>
    public static Flt64 ProduceSlack(Flt64 x, Flt64 threshold, bool withNegative, bool withPositive) {
        Flt64 diff = x - threshold;
        if (withPositive && diff > Flt64.Zero) {
            return diff;
        }

        if (withNegative && diff < Flt64.Zero) {
            return -diff;
        }

        return Flt64.Zero;
    }

    /// <summary>
    /// 建一条生产松弛 / Build a produce slack.
    /// </summary>
    /// <param name="x">被松弛的符号 / The symbol being slacked.</param>
    /// <param name="threshold">常量阈值 / The constant threshold.</param>
    /// <param name="withNegative">是否包含负松弛 / Whether the negative side is included.</param>
    /// <param name="withPositive">是否包含正松弛 / Whether the positive side is included.</param>
    /// <param name="constraint">是否自带约束 / Whether the slack adds its own rows.</param>
    /// <param name="name">符号名 / The symbol name.</param>
    /// <returns>适配器 / The adapter.</returns>
    public static LinearFunctionSymbolAdapter<Flt64> Build(
        ILinearIntermediateSymbol<Flt64> x,
        Flt64 threshold,
        bool withNegative,
        bool withPositive,
        bool constraint,
        string name) {
        ArgumentNullException.ThrowIfNull(x);

        // **`x` 必须是"符号本身"，不能是它的多项式**：数量符号是空多项式（它就是一个变量），
        // 用 `x.Polynomial` 会让松弛的 `X` 为空，`SignedLeft` 于是只剩 `-pos`，上界行里数量项整个消失。
        // **`x` must be the symbol itself, not its polynomial**: a quantity symbol has an empty polynomial (it *is* a variable), so passing `x.Polynomial` leaves the slack's `X` empty,
        // `SignedLeft` then yields only `-pos`, and the quantity term vanishes from the bound row entirely.
        return new LinearFunctionSymbolAdapter<Flt64>(
            new SlackFunction<Flt64>(
                x: new LinearPolynomial<Flt64>(
                    new[] { new LinearMonomial<Flt64>(Flt64.One, x) },
                    Flt64.Zero),
                y: new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), threshold),
                converter: IIntoValue<Flt64>.Identity,
                withNegative: withNegative,
                withPositive: withPositive,
                threshold: true,
                constraint: constraint,
                name: name));
    }

    /// <summary>
    /// 建一张产量或消耗量符号表 / Build a quantity symbol table keyed by product or material.
    /// </summary>
    /// <param name="materials">材料与其求解器范围 / The materials and their solver ranges.</param>
    /// <param name="prefix">符号名前缀 / The symbol name prefix.</param>
    /// <param name="table">建出的符号表 / The built symbol table.</param>
    /// <returns>执行结果 / Execution result.</returns>
    /// <remarks>
    /// **键是材料对象本身，不是下标**：`LinearIntermediateSymbols1` 按 `Shape1` 位置索引，而位置只有在索引连续时才等于索引；
    /// 用对象做键就不必假设连续性。**符号名用 `Index` 而不是 `ToString`**：`${{product}}` 是 Kotlin data class 的
    /// `toString`，C# 的普通类只给出类型名，两个产品会撞成同一个符号名（`SymbolRepetitive` 拒绝）。
    ///
    /// **The key is the material object, not a position**：`produce_quantity_（产品）` 的上界来自需求、消耗量的来自储备；
    /// 不设范围，求解器只会把产量顶到无穷远。**范围非法就失败**，不要静默跳过——`.value!!`，跳过等于把上界丢了。
    ///
    /// **The key is the material object, not a position**: its `LinearIntermediateSymbols1` indexes by `Shape1` position, which equals the material index only when indices are
    /// contiguous; keying by object avoids assuming that. **The range must be set**: `produce_quantity_ (product)` takes its bound from the demand and the consumption one from the reserves; without
    /// a range the solver simply drives the quantity to infinity. **An invalid range fails rather than being skipped**, since the code uses `.value!!` and skipping silently drops the bound.
    /// </remarks>
    public static Try BuildQuantityTable<M>(
        IReadOnlyList<(M Material, Flt64? Lower, Flt64? Upper)> materials,
        string prefix,
        out IReadOnlyDictionary<M, ILinearIntermediateSymbol<Flt64>> table)
        where M : IIndexed {
        ArgumentNullException.ThrowIfNull(materials);

        var built = new Dictionary<M, ILinearIntermediateSymbol<Flt64>>();
        foreach ((M material, Flt64? lower, Flt64? upper) in materials) {
            var symbol = new LinearExpressionSymbol(
                new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero),
                name: prefix + "_" + material.Index);
            if (lower is { } lb && upper is { } ub) {
                Result<ValueRange, ErrorCode, Error<ErrorCode>> range = ValueRange.Of(lb, ub);
                if (range is not Ok<ValueRange, ErrorCode, Error<ErrorCode>> ok) {
                    table = built;
                    return new Failed<Success, ErrorCode, Error<ErrorCode>>(
                        ((Failed<ValueRange, ErrorCode, Error<ErrorCode>>)range).Error);
                }
                symbol.Range.SetRange(ok.Value);
            }
            built[material] = symbol;
        }

        table = built;
        return Results.Ok<Success>(Results.SuccessInstance);
    }

    /// <summary>
    /// 注册超量 / 不足量表到模型 / Register the over/less quantity tables.
    /// </summary>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <param name="quantity">产量或消耗量表 / The quantity table.</param>
    /// <param name="bounds">每项的上界与允许超量 / The upper bound and allowed excess per item.</param>
    /// <param name="overEnabled">是否启用超量 / Whether over quantity is enabled.</param>
    /// <param name="lessEnabled">是否启用不足 / Whether less quantity is enabled.</param>
    /// <param name="overPrefix">超量符号前缀 / The over-quantity prefix.</param>
    /// <param name="lessPrefix">不足符号前缀 / The less-quantity prefix.</param>
    /// <param name="onOver">超量表就绪 / The over table is ready.</param>
    /// <param name="onLess">不足表就绪 / The less table is ready.</param>
    /// <returns>执行结果 / Execution result.</returns>
    /// <remarks>
    /// 这里是超量、不足两段。三处必须沿用：
    ///
    /// 1. **松弛带 `constraint: false`**：上界行由本方法自己加（`pos ≤ overQuantity`、`neg ≥ -lessQuantity`）。
    ///    让松弛自带约束会多出一对行，把"超出多少"变成"与上界的绝对差"，两个方向都算错。
    /// 2. **没有需求 / 储备的项是空符号，不是零松弛**：空符号求值为零但不参与约束；给它建松弛等于凭空多出两个变量。
    /// 3. **先加表，再加行**：反过来的话行里引用的 `pos` / `neg` 还没进模型。
    ///
    /// The over/less halves. Three things are fixed: **the slack carries `constraint: false`**,
    /// because the bound rows are added here (`pos` at most `overQuantity`, `neg` at least `-lessQuantity`) and letting the slack carry its own would add an extra pair and turn "how far past the bound" into
    /// "the absolute distance from it"; **an item without a demand or reserve gets an empty symbol rather than a zero slack**, since an empty symbol evaluates to zero without entering any
    /// constraint while a slack invents two variables; and **the table goes in before the rows**, otherwise the `pos`/`neg` the rows reference are not in the model yet.
    /// </remarks>
    public static Try RegisterSlackTables<M>(
        IAbstractLinearMetaModel<Flt64> model,
        IReadOnlyDictionary<M, ILinearIntermediateSymbol<Flt64>> quantity,
        IReadOnlyDictionary<M, (Flt64 Bound, Flt64 SlackValue)?> bounds,
        bool overEnabled,
        bool lessEnabled,
        string overPrefix,
        string lessPrefix,
        Action<IReadOnlyDictionary<M, ILinearIntermediateSymbol<Flt64>>> onOver,
        Action<IReadOnlyDictionary<M, ILinearIntermediateSymbol<Flt64>>> onLess)
        where M : IIndexed {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(quantity);

        if (overEnabled) {
            var table = new Dictionary<M, ILinearIntermediateSymbol<Flt64>>();
            var rows = new List<LinearPolynomial<Flt64>>();
            foreach (KeyValuePair<M, (Flt64 Bound, Flt64 SlackValue)?> entry in bounds) {
                if (entry.Value is not { } bound) {
                    table[entry.Key] = EmptySymbol(overPrefix + "_" + entry.Key.Index);
                    continue;
                }
                LinearFunctionSymbolAdapter<Flt64> slack = Build(
                    quantity[entry.Key], bound.Bound,
                    withNegative: false, withPositive: true, constraint: false,
                    name: overPrefix + "_" + entry.Key.Index);
                table[entry.Key] = slack;
                rows.Add(Sum(slack.Polynomial, new LinearPolynomial<Flt64>(
                    Array.Empty<LinearMonomial<Flt64>>(), bound.SlackValue.Negate())));
            }
            Try added = model.Add(table.Values);
            if (!added.IsOk) {
                return added;
            }
            onOver(table);
            foreach (LinearPolynomial<Flt64> row in rows) {
                Try result = model.AddConstraint(row, null, name: overPrefix + "_ub");
                if (!result.IsOk) {
                    return result;
                }
            }
        }

        if (lessEnabled) {
            var table = new Dictionary<M, ILinearIntermediateSymbol<Flt64>>();
            var rows = new List<LinearPolynomial<Flt64>>();
            foreach (KeyValuePair<M, (Flt64 Bound, Flt64 SlackValue)?> entry in bounds) {
                if (entry.Value is not { } bound) {
                    table[entry.Key] = EmptySymbol(lessPrefix + "_" + entry.Key.Index);
                    continue;
                }
                LinearFunctionSymbolAdapter<Flt64> slack = Build(
                    quantity[entry.Key], bound.Bound,
                    withNegative: true, withPositive: false, constraint: false,
                    name: lessPrefix + "_" + entry.Key.Index);
                table[entry.Key] = slack;
                rows.Add(Sum(slack.Polynomial, new LinearPolynomial<Flt64>(
                    Array.Empty<LinearMonomial<Flt64>>(), bound.SlackValue)));
            }
            Try added = model.Add(table.Values);
            if (!added.IsOk) {
                return added;
            }
            onLess(table);
            foreach (LinearPolynomial<Flt64> row in rows) {
                Try result = model.AddConstraint(row, null, name: lessPrefix + "_lb");
                if (!result.IsOk) {
                    return result;
                }
            }
        }

        return Results.Ok<Success>(Results.SuccessInstance);
    }

    /// <summary>空中间符号 / An empty intermediate symbol.</summary>
    /// <remarks>
    /// **公开**：资源侧（`ExecutionResource` 等）也要拿它填"该槽没开超量 \/ 不足"的位置。
    /// 私下复制一份会让两条路径的空符号在名字与构造上分叉。
    ///
    /// **Public** because the resource side (`ExecutionResource` and friends) also needs it for slots whose over/less switch is off; a private copy would let the two paths drift apart in
    /// both name and construction.
    /// </remarks>
    public static ILinearIntermediateSymbol<Flt64> EmptySymbol(string name) =>
        new LinearExpressionSymbol(
            new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero),
            name: name);

    /// <summary>两个多项式相加 / Sum two polynomials.</summary>
    private static LinearPolynomial<Flt64> Sum(LinearPolynomial<Flt64> first, LinearPolynomial<Flt64> second) =>
        new(
            first.Monomials.Concat(second.Monomials).ToArray(),
            first.Constant.Plus(second.Constant));

    /// <summary>
    /// 建一条产量 / 消耗量目标 / Build a produce or consumption objective.
    /// </summary>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <param name="table">产量或消耗量表 / The quantity table.</param>
    /// <param name="items">要计费的项 / The items to charge for.</param>
    /// <param name="threshold">阈值提取器 / The threshold extractor.</param>
    /// <param name="coefficient">系数提取器 / The coefficient extractor.</param>
    /// <param name="slackPrefix">阈值松弛的符号名前缀 / The slack name prefix.</param>
    /// <param name="category">最小化还是最大化 / Minimum or maximum.</param>
    /// <param name="objectiveName">目标名 / The objective name.</param>
    /// <param name="skipWhenEmpty">列表为空时是否连目标都不加 / Whether to add no objective at all when the list is empty.</param>
    /// <param name="id">符号名里的键，缺省用 `ToString` / The key used in symbol names, defaulting to `ToString`.</param>
    /// <returns>执行结果 / Execution result.</returns>
    /// <remarks>
    /// 四个"数量"目标与四个"超量 / 不足"目标共用这一形状。四处必须沿用：
    ///
    /// 1. **阈值 0 时直接计费符号本身，非零时才建松弛**：给阈值 0 也建松弛，等于把"产量"换成"超出 0 的部分"，
    ///    在有下界时两者不同（下界 10 时前者是 10、后者是 0）。
    /// 2. **`constraint: true`**：这四个目标不像超量 / 不足表那样自己加行，松弛必须自带 `x - pos ≤ 阈值`，
    ///    否则 `pos` 是个没有上界的自由变量，最小化时恒取 0、最大化时冲到无穷。
    /// 3. **系数在松弛外面乘**，即 `coefficient(x) * slack`。
    /// 4. **空列表的处理两边不同**：四个"数量"目标照旧 `minimize` 一个空多项式，四个"超量 / 不足"目标
    ///    外层有 `if (materials.isNotEmpty)`，为空时连目标都不加。这个差别由 `skipWhenEmpty` 承载。
    ///
    /// shape shared by all eight: **a zero threshold charges the symbol itself and only a non-zero
    /// one builds a slack**, since building one at zero turns "the quantity" into "the part above zero" and the two differ whenever there is a lower bound (10 versus 0 at a bound of 10);
    /// **`constraint: true`**, because these four objectives do not add their own rows the way the over/less tables do, so the slack must carry "`x - pos` at most `threshold`" or `pos` is a free variable with
    /// no ceiling that minimisation pins at zero and maximisation drives to infinity; **the coefficient multiplies outside the slack**, i.e. `coefficient(x) * slack`; and **the empty
    /// list is handled differently on the two sides**, its four "quantity" objectives minimising an empty polynomial anyway while the four "over/less" ones sit inside
    /// `if (materials.isNotEmpty)` and add no objective at all — a difference carried by `skipWhenEmpty`.
    /// </remarks>
    public static Try BuildObjective<M>(
        IAbstractLinearMetaModel<Flt64> model,
        IReadOnlyDictionary<M, ILinearIntermediateSymbol<Flt64>> table,
        IReadOnlyList<M> items,
        Func<M, Flt64> threshold,
        Func<M, Flt64> coefficient,
        string slackPrefix,
        ObjectCategory category,
        string objectiveName,
        bool skipWhenEmpty,
        Func<M, string>? id = null)
        where M : notnull {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(items);

        if (skipWhenEmpty && items.Count == 0) {
            return Results.Ok<Success>(Results.SuccessInstance);
        }

        // **符号名要按调用方给的键，不能按 `ToString`**：资源时间槽的 `ToString` 只含资源与容量，
        // 同一容量下的两个槽会得到同名符号，第二次 `Add` 直接失败（"Repeated"）。产量侧不传就用 `ToString`，行为不变。
        // **The symbol name comes from the caller's key, not `ToString`**: a resource time slot's `ToString` holds only the resource and capacity, so two slots under one capacity get
        // the same name and the second `Add` fails outright ("Repeated"). Produce callers pass nothing and keep `ToString`, so their behaviour is unchanged.
        id ??= item => item.ToString() ?? string.Empty;

        var monomials = new List<LinearMonomial<Flt64>>();
        Flt64 constant = Flt64.Zero;
        foreach (M item in items) {
            if (!table.TryGetValue(item, out ILinearIntermediateSymbol<Flt64>? symbol)) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.ApplicationError,
                    "数量表缺项：" + item + " / the quantity table is missing an entry: " + item));
            }

            Flt64 weight = coefficient(item);
            Flt64 thisThreshold = threshold(item);
            // **阈值 0 时目标项是符号本身，不是它的多项式**：`produce_quantity_0` 是空多项式（它就是一个变量），
            // 展开多项式会得到零项——目标看起来"加了"却什么都没计费。
            // **At a zero threshold the term is the symbol itself, not its polynomial**: `produce_quantity_0` has an empty polynomial (it *is* a variable), so expanding it yields no terms and the
            // objective looks added while charging nothing.
            LinearPolynomial<Flt64> priced;
            if (thisThreshold.Eq(Flt64.Zero)) {
                priced = new LinearPolynomial<Flt64>(
                    new[] { new LinearMonomial<Flt64>(Flt64.One, symbol) },
                    Flt64.Zero);
            }
            else {
                LinearFunctionSymbolAdapter<Flt64> slack = Build(
                    symbol, thisThreshold,
                    withNegative: false, withPositive: true, constraint: true,
                    name: slackPrefix + "_" + id(item));
                Try added = model.Add(slack);
                if (!added.IsOk) {
                    return added;
                }
                priced = slack.Polynomial;
            }

            foreach (LinearMonomial<Flt64> monomial in priced.Monomials) {
                monomials.Add(new LinearMonomial<Flt64>(weight * monomial.Coefficient, monomial.Symbol));
            }
            constant = constant + weight * priced.Constant;
        }

        return model.AddObject(
            category,
            new Fuookami.Ospf.Core.Symbol.Flatten.LinearFlattenData<Flt64>(monomials.ToArray(), constant),
            objectiveName,
            displayName: null);
    }

    /// <summary>
    /// 建一条数量约束的上下界行 / Build the upper and lower bound rows of a quantity constraint.
    /// </summary>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <param name="quantity">数量符号 / The quantity symbol.</param>
    /// <param name="over">超量松弛，`null` 表示没有 / The over slack, null when absent.</param>
    /// <param name="less">不足松弛，`null` 表示没有 / The less slack, null when absent.</param>
    /// <param name="lower">下界 / The lower bound.</param>
    /// <param name="upper">上界 / The upper bound.</param>
    /// <param name="name">行名前缀 / The row name prefix.</param>
    /// <param name="id">行名里的项标识 / The item identifier in the row name.</param>
    /// <param name="args">影子价格键 / The shadow-price key.</param>
    /// <returns>执行结果 / Execution result.</returns>
    /// <remarks>
    /// 行名照做 `${name}_ub_${product}` 的顺序：**后缀在前、标识在后**，`produce_quantity_ub_0`。
    ///
    /// 三处必须沿用：
    ///
    /// 1. **有超量松弛时上界加在 `polyX` 上，不是 `quantity` 上**：`polyX = x - pos`，正是"扣掉超量之后的数量"。
    ///    加在 `quantity` 上会让超量变量失去意义——超出的部分照样违反上界。
    /// 2. **下界用 `x + neg`**（`withNegative` 的 `SignedLeft`），同理。
    /// 3. **两个分支的方向不能反**：`polyX` 与 `quantity` 求值都非负，反了照样能解，只是约束变成"上界至少"。
    ///
    /// Three things are fixed: **with an over slack the upper bound applies to
    /// `polyX`, not to `quantity`** — `polyX = x - pos` is exactly "the quantity after the excess is discounted", and applying it to `quantity` makes the over variable pointless since the excess
    /// would still violate the bound; **the lower bound uses `x + neg`** (the `SignedLeft` of `withNegative`), for the same reason; and **the two directions must not be swapped**, since both `polyX`
    /// and `quantity` evaluate non-negative and a swap still solves, merely asserting "at least the upper bound".
    /// </remarks>
    /// <remarks>
    /// **没有类型参数**：第一版带 `where M : IMaterial`，但方法体里一次也没用到 `M`——只是为了"看起来是产量侧的"。
    /// 资源侧（容量约束）要的正是同一对行，去掉约束即可复用。
    ///
    /// **No type parameter**: the first version carried `where M : IMaterial`, which the body never used — it only made the helper look produce-specific. The resource side (the capacity
    /// constraint) needs exactly the same pair of rows, and dropping the constraint lets it reuse this.
    /// </remarks>
    public static Try BuildQuantityRows(
        IAbstractLinearMetaModel<Flt64> model,
        ILinearIntermediateSymbol<Flt64> quantity,
        LinearFunctionSymbolAdapter<Flt64>? over,
        LinearFunctionSymbolAdapter<Flt64>? less,
        Flt64 lower,
        Flt64 upper,
        string name,
        string id,
        object? args) {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(quantity);

        // **数量符号本身是空多项式**：它就是一个变量，展开它得到零项。必须把它做成单项式。
        // **The quantity symbol has an empty polynomial**: it *is* a variable, so expanding it yields no terms; it must be turned into a monomial.
        var asPolynomial = new LinearPolynomial<Flt64>(
            new[] { new LinearMonomial<Flt64>(Flt64.One, quantity) },
            Flt64.Zero);
        LinearPolynomial<Flt64> upperLeft = over is null
            ? asPolynomial
            : ((SlackFunction<Flt64>)over.Function).SignedLeft();
        Try added = model.AddConstraint(
            new LinearInequality<Flt64>(
                upperLeft,
                new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), upper),
                Fuookami.Ospf.Math.Symbol.Inequality.Comparison.LE,
                name + "_ub_" + id),
            group: null,
            name: name + "_ub_" + id,
            args: args);
        if (!added.IsOk) {
            return added;
        }

        LinearPolynomial<Flt64> lowerLeft = less is null
            ? asPolynomial
            : ((SlackFunction<Flt64>)less.Function).SignedLeft();
        return model.AddConstraint(
            new LinearInequality<Flt64>(
                lowerLeft,
                new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), lower),
                Fuookami.Ospf.Math.Symbol.Inequality.Comparison.GE,
                name + "_lb_" + id),
            group: null,
            name: name + "_lb_" + id,
            args: args);
    }

    /// <summary>
    /// 把影子价格回填到数量表 / Write shadow prices back onto a quantity table.
    /// </summary>
    /// <param name="shadowPriceMap">影子价格接收器 / The shadow-price sink.</param>
    /// <param name="shadowPrices">对偶解 / The dual solution.</param>
    /// <param name="rows">模型里的全部约束行 / Every constraint row in the model.</param>
    /// <returns>执行结果 / Execution result.</returns>
    /// <remarks>
    /// 遍历超量 / 不足两张表，把每行的对偶值按它的 `args` 累加进映射。**取价的入口是约束行**：从**行的 `args`** 取键，
    /// 因为 `IIntermediateSymbol.Args` 是从 `Parent` 派生的只读属性（`IntermediateSymbol.cs:37`），
    /// 符号建好之后挂不上去；而行正是对偶值的来源，从行取键更直接。
    ///
    /// 四处必须沿用：
    ///
    /// 1. **只有挂键的行才有价格**：没挂键的行静默跳过，不是错误（`when` 没有 `else`）。
    /// 2. **按行累加**：同一个键可能被多条行命中，`putOrAdd`。
    /// 3. **对偶表里没有该行时什么都不做**，不能当成零价写进去——零价会被后续过滤掉，看起来像"算过"。
    /// 4. **表未建立时跳过整段**，不是失败：`refresh` 可能在注册之前被调用。
    ///
    /// The over/less tables are walked and each row's dual value is accumulated into the map by its `args`. **The key is read from the constraint row**, since
    /// `IIntermediateSymbol.Args` is a read-only property derived from `Parent` (`IntermediateSymbol.cs:37`) and cannot be attached after construction. The key is attached to the
    /// **constraint row's** `Args`, and the row is what the dual value is keyed by. Four things are carried over: **only a row carrying a key has
    /// a price** and a row without one is skipped silently rather than being an error (its `when` has no `else`); **prices accumulate per row**, since one key can be hit by several rows and
    /// the code uses `putOrAdd`; **a row absent from the dual table is left alone** rather than written as a zero price, which later filtering would hide as "already computed"; and **a table that
    /// was never built skips the whole block** instead of failing, since `refresh` may run before registration.
    /// </remarks>
    public static Try RefreshTable(
        IShadowPriceSink? shadowPriceMap,
        MetaDualSolution shadowPrices,
        IReadOnlyList<MathConstraint> rows) {
        ArgumentNullException.ThrowIfNull(shadowPrices);
        ArgumentNullException.ThrowIfNull(rows);
        if (shadowPriceMap is null) {
            return Results.Ok<Success>(Results.SuccessInstance);
        }

        foreach (MathConstraint row in rows) {
            // **按行自己的键取值**：行的 `Args` 已经是本模块的键类型（生产用的行挂生产键、消费用的行挂消费键），
            // 所以不需要再按材料过滤一遍——那一层在变异测试里**没有任何可观测差别**，删掉。
            // **The key comes from the row itself**: a row's `Args` already carries this module's key type (produce rows carry the produce key, consumption rows the consumption key), so a second
            // filter by item adds **no observable difference** under mutation testing and is gone.
            if (row.Args is not ShadowPriceKey key) {
                continue;
            }
            if (!shadowPrices.Constraints.TryGetValue(row, out Flt64 price)) {
                continue;
            }
            shadowPriceMap.PutOrAdd(new ShadowPrice(key, price));
        }

        return Results.Ok<Success>(Results.SuccessInstance);
    }

    /// <summary>
    /// 把带具体键类型的表重建成以 `object` 为键 / Re-key a typed table onto `object` keys.
    /// </summary>
    /// <param name="table">原表 / The source table.</param>
    /// <returns>重建后的表，输入为 `null` 时返回 `null` / The re-keyed table, null when the input is null.</returns>
    /// <remarks>
    /// **`as` 转不过去**：`IReadOnlyDictionary` 的键类型**不协变**（只有值类型协变），
    /// 所以把一张以具体类型为键的表 `as` 成以 `object` 为键的表恒为 `null`——不是"键类型不同"而是"整个转换无效"。
    ///
    /// **An `as` cast cannot do this**: a dictionary type is **not covariant in its key type** (only in its value type), so casting a concretely keyed table `as` one keyed by `object` is always null —
    /// not "a different key type" but "the whole conversion is invalid".
    /// </remarks>
    public static IReadOnlyDictionary<object, ILinearIntermediateSymbol<Flt64>>? ReKey<M>(
        object? table)
        where M : notnull {
        if (table is not IReadOnlyDictionary<M, ILinearIntermediateSymbol<Flt64>> typed) {
            return null;
        }
        var rebuilt = new Dictionary<object, ILinearIntermediateSymbol<Flt64>>(typed.Count);
        foreach (KeyValuePair<M, ILinearIntermediateSymbol<Flt64>> entry in typed) {
            rebuilt[entry.Key] = entry.Value;
        }
        return rebuilt;
    }
}
