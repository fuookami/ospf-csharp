#nullable enable

using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using System.Linq;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.Produce.Model;
/// <summary>生产接口 / Produce interface</summary>
public interface IProduce {
    /// <summary>产量变量；注册前为 `null` / Quantity variables (indexed by product); null before registration</summary>
    object? Quantity { get; }

    /// <summary>超量变量 / Over quantity variables</summary>
    object? OverQuantity { get; }

    /// <summary>不足量变量 / Less quantity variables</summary>
    object? LessQuantity { get; }

    /// <summary>是否启用超量 / Whether over quantity is enabled</summary>
    bool OverEnabled { get; }

    /// <summary>是否启用不足 / Whether less quantity is enabled</summary>
    bool LessEnabled { get; }

    /// <summary>注册到模型 / Register to model</summary>
    Try Register(object model);

    /// <summary>刷新影子价格 / Refresh shadow prices</summary>
    Try Refresh(object shadowPriceMap, object model, object shadowPrices);
}

/// <summary>
/// 抽象生产 / Abstract produce
/// </summary>
/// <remarks>
/// 提供生产量管理的通用框架，包括超量和不足量的松弛变量注册及影子价格提取。
/// Provides a common framework for produce quantity management, including slack variable registration and shadow price extraction.
/// </remarks>
/// <typeparam name="P">产品类型 / Product type</typeparam>
/// <param name="Products">产品与需求列表 / List of products and demands</param>
public abstract class AbstractProduce<P>(
    IReadOnlyList<(P Product, MaterialDemand? Demand)> Products
) : IProduce where P : IMaterial {
    /// <summary>产品与需求列表（按索引排序）/ Products and demands sorted by index</summary>
    protected IReadOnlyList<(P Product, MaterialDemand? Demand)> SortedProducts
        = Products.OrderBy(p => p.Product.Index).ToList();

    /// <inheritdoc/>
    /// <remarks>注册前为 `null`，不是 `new object`——一个空对象会被当成"表已就绪"。
    /// Null before registration rather than `new object`, which would read as "the table is ready".</remarks>
    public abstract object? Quantity { get; }

    /// <inheritdoc/>
    public object? OverQuantity { get; protected set; }

    /// <inheritdoc/>
    public object? LessQuantity { get; protected set; }

    /// <inheritdoc/>
    public abstract bool OverEnabled { get; }

    /// <inheritdoc/>
    public abstract bool LessEnabled { get; }


    /// <inheritdoc/>
    public virtual Try Register(object model) => Results.Ok<Success>(Results.SuccessInstance);

    /// <inheritdoc/>
    /// <remarks>
    /// 对应基线 `Produce.refresh` / `Consumption.refresh`：遍历超量 / 不足两张表，把每行的对偶值按它的 `args` 累加进映射。
    /// 四处必须沿用：
    ///
    /// 1. **只有挂键的行才有价格**：没挂键的行静默跳过，不是错误（`when` 没有 `else`）。
    /// 2. **按键累加**：同一个键可能被多条行命中，`putOrAdd`。
    /// 3. **对偶表里没有该行时什么都不做**，不能当成零价写进去——零价会被后续过滤掉，看起来像"算过"。
    /// 4. **表未建立时跳过整段**，不是失败：`refresh` 可能在注册之前被调用。
    ///
    /// **取价入口与基线不同**：基线从**符号的 `args`** 取键，而 C# 的 `IIntermediateSymbol.Args` 是从 `Parent` 派生的只读属性
    /// （`IntermediateSymbol.cs:37`），`LinearFunctionSymbolAdapter` 也不接受 parent，符号建好之后挂不上去。 已经把键挂在**约束行**上，
    /// 而行正是对偶值的来源，所以这里按行取键——等价且更直接。这也是本方法**必须收模型**的原因：行只能从模型里枚举。
    ///
    /// And `Consumption.refresh`: walk the over and less tables and accumulate each row's dual value into the map by its `args`. Four things are carried over:
    /// **only a row carrying a key has a price** and a row without one is skipped silently rather than being an error (its `when` has no `else`); **prices accumulate per key**, since one
    /// key can be hit by several rows and the code uses `putOrAdd`; **a row absent from the dual table is left alone** rather than written as a zero price, which later filtering would hide as
    /// "already computed"; and **a table that was never built skips the whole block** instead of failing, since `refresh` may run before registration. **The key's entry point differs from the
    /// baseline**: the code reads it from **the symbol's `args`**, while C#'s `IIntermediateSymbol.Args` is a read-only property derived from `Parent` (`IntermediateSymbol.cs:37`) and
    /// `LinearFunctionSymbolAdapter` takes no parent, so it cannot be attached after construction. already attaches the key to the **constraint row**, which is what the dual value is keyed by, so
    /// this reads it from the row — equivalent and more direct. That is also why this method **must take the model**: rows can only be enumerated from it.
    /// </remarks>
    public virtual Try Refresh(object shadowPriceMap, object model, object shadowPrices) {
        ArgumentNullException.ThrowIfNull(shadowPriceMap);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(shadowPrices);
        if (shadowPrices is not Fuookami.Ospf.Core.Model.Mechanism.MetaDualSolution dual) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "影子价格回填需要元对偶解 / refreshing shadow prices needs a meta dual solution"));
        }
        if (shadowPriceMap is not Fuookami.Ospf.Framework.Model.IShadowPriceSink sink) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "影子价格回填需要影子价格映射 / refreshing shadow prices needs a shadow-price map"));
        }
        if (model is not Fuookami.Ospf.Core.Model.Mechanism.IMetaModel<Flt64> meta) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "影子价格回填需要元模型 / refreshing shadow prices needs a meta model"));
        }

        // **表未建立时整段跳过**：没有表就没有需要回填的量，`refresh` 在注册之前被调用是正常路径。
        // **Skip the whole block when no table exists**: with no table there is nothing to fill, and `refresh` running before registration is a normal path.
        if (!Tables().Any(table => table is not null)) {
            return Results.Ok<Success>(Results.SuccessInstance);
        }
        return ProduceSlackHelper.RefreshTable(sink, dual, meta.Constraints);
    }

    /// <summary>
    /// 需要回填的表与其行名前缀 / The tables to refresh and their row-name prefixes.
    /// </summary>
    /// <returns>表与前缀，未建立的表为 `null` / The tables and prefixes, null when a table was never built.</returns>
    /// <remarks>
    /// **默认空表而不是抽象成员**：四个子类里只有两个真的建表，另外两个（`TaskScheduling*`）注册直接失败。
    ///
    /// **An empty default rather than an abstract member**: only two of the four subclasses actually build tables and the other two (`TaskScheduling*`) fail registration outright.
    /// </remarks>
    protected virtual IReadOnlyList<IReadOnlyDictionary<object, Fuookami.Ospf.Core.Symbol.ILinearIntermediateSymbol<Flt64>>?> Tables() =>
        Array.Empty<IReadOnlyDictionary<object, Fuookami.Ospf.Core.Symbol.ILinearIntermediateSymbol<Flt64>>?>();

}

/// <summary>
/// 任务调度生产 / Task scheduling produce
/// </summary>
/// <remarks>
/// 暂未实现，请使用 BunchSchedulingProduce。
/// Not yet implemented, please use BunchSchedulingProduce.
/// </remarks>
/// <typeparam name="P">产品类型 / Product type</typeparam>
public sealed class TaskSchedulingProduce<P>(
    IReadOnlyList<(P Product, MaterialDemand? Demand)> Products,
    bool OverEnabledFlag = false,
    bool LessEnabledFlag = false
) : AbstractProduce<P>(Products) where P : IMaterial {
    private readonly object _quantity = new();

    /// <inheritdoc/>
    public override object? Quantity => _quantity;

    /// <inheritdoc/>
    public override bool OverEnabled => OverEnabledFlag;

    /// <inheritdoc/>
    public override bool LessEnabled => LessEnabledFlag;

    /// <inheritdoc/>
    public override Try Register(object model) {
        return new Failed<Success, ErrorCode, Error<ErrorCode>>(
            new Err<ErrorCode>(ErrorCode.ApplicationFailed,
                "TaskSchedulingProduce.Register is not implemented. Use BunchSchedulingProduce instead."));
    }
}

/// <summary>
/// 任务束调度生产 / Bunch scheduling produce
/// </summary>
/// <remarks>
/// 用于列生成场景，支持通过 addColumns 追加产量贡献。
/// Used for column generation scenarios, supports adding produce contribution through addColumns.
/// </remarks>
/// <typeparam name="P">产品类型 / Product type</typeparam>
public sealed class BunchSchedulingProduce<P> : AbstractProduce<P>
    where P : IMaterial {
    private readonly object _quantity = new();

    /// <summary>
    /// 任务束调度生产构造 / Bunch scheduling produce constructor
    /// </summary>
    /// <param name="products">产品与需求列表 / List of products and demands</param>
    public BunchSchedulingProduce(IReadOnlyList<(P Product, MaterialDemand? Demand)> products)
        : base(products) { }

    /// <inheritdoc/>
    public override bool OverEnabled => true;

    /// <inheritdoc/>
    public override bool LessEnabled => true;

    private IReadOnlyDictionary<P, ILinearIntermediateSymbol<Flt64>>? _quantityTable;

    /// <inheritdoc/>
    /// <remarks>
    /// 与基线 `BunchSchedulingProduce.register` 一致：先建 `produce_quantity_（P）` 符号表并设范围，再补超量 / 不足两表。
    /// **符号表建在排序后的列表上**，与基线 `sortedBy { it.first.index }` 一致——不排序的话索引与位置不对应，范围会设到别的Product上。
    ///
    /// Matches its `BunchSchedulingProduce.register`: build the `produce_quantity_ (item)` table with its ranges first, then the over/less tables. **The table is built over the sorted
    /// list**, matching its `sortedBy { it.first.index }`; without the sort the index and the position disagree and a range lands on the wrong item.
    /// </remarks>
    public override Try Register(object model) {
        ArgumentNullException.ThrowIfNull(model);
        if (model is not IAbstractLinearMetaModel<Flt64> linear) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "产品注册只能施加到线性元模型 / produce registration can only be applied to a linear meta model"));
        }

        if (_quantityTable is null) {
            Try built = ProduceSlackHelper.BuildQuantityTable(
                SortedProducts
                    .Select(entry => (
                        entry.Product,
                        entry.Demand is { } demand
                            ? (Flt64?)demand.SolverRangeLowerBound()
                            : null,
                        entry.Demand is { } demand2
                            ? (Flt64?)demand2.SolverRangeUpperBound()
                            : null))
                    .ToList(),
                "produce_quantity",
                out IReadOnlyDictionary<P, ILinearIntermediateSymbol<Flt64>> table);
            if (!built.IsOk) {
                return built;
            }
            _quantityTable = table;
        }

        Try added = linear.Add(_quantityTable.Values);
        if (!added.IsOk) {
            return added;
        }

        return ProduceSlackHelper.RegisterSlackTables(
            linear, _quantityTable,
            SortedProducts.ToDictionary(
                entry => entry.Product,
                entry => entry.Demand is { } demand3
                    ? ((Flt64, Flt64)?)(demand3.SolverUpperBound(), demand3.SolverOverQuantity())
                    : null),
            OverEnabled, LessEnabled,
            "produce_over_quantity", "produce_less_quantity",
            table => OverQuantity = table,
            table => LessQuantity = table);
    }

    /// <inheritdoc/>
    public override object? Quantity => _quantityTable;

    /// <inheritdoc/>
    /// <remarks>只有超量 / 不足两张表带影子价格键；数量表本身没有——它的行不带 `args`。
    ///
    /// Only the over and less tables carry shadow-price keys; the quantity table itself does not, since its rows carry no `args`.
    /// </remarks>
    protected override IReadOnlyList<IReadOnlyDictionary<object, Fuookami.Ospf.Core.Symbol.ILinearIntermediateSymbol<Flt64>>?> Tables() =>
        new IReadOnlyDictionary<object, Fuookami.Ospf.Core.Symbol.ILinearIntermediateSymbol<Flt64>>?[] {
            ProduceSlackHelper.ReKey<P>(OverQuantity),
            ProduceSlackHelper.ReKey<P>(LessQuantity),
        };

}
