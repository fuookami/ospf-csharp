// 方案定价：目标系数、影子价格与检验数 / Pattern pricing: objective coefficients, shadow prices and reduced costs.

using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Framework.Bpp.Infrastructure;
using Fuookami.Ospf.Framework.Bpp1d.Domain.Item.Model;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<
    Fuookami.Ospf.Utils.Functional.Success,
    Fuookami.Ospf.Utils.Error.ErrorCode,
    Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.Bpp1d.Domain.PatternAssignment;

/// <summary>
/// 模型注册阶段 / Model registration stage.
/// </summary>
public enum PackingModelStage {
    /// <summary>整数模型 / Integer model.</summary>
    Milp,

    /// <summary>线性松弛 / Linear relaxation.</summary>
    Lp,

    /// <summary>最终整数模型 / Final integer model.</summary>
    FinalMilp,
}

/// <summary>
/// 下游模型扩展的定价钩子 / The pricing hook of a downstream modeling extension.
/// </summary>
/// <typeparam name="V">领域数值 / Domain number.</typeparam>
/// <remarks>
/// **只有定价钩子。** 扩展还带 `register`/`refresh` 两个建模钩子，它们随建模半边一起落地。
/// **Pricing hook only.** The implementation extension also carries `register`/`refresh` modelling hooks, which land with the modelling half .
/// </remarks>
public interface IPatternModelingExtension<V>
    where V : struct, IFloatingNumber<V> {
    /// <summary>
    /// 附加定价项 / Additional reduced-cost contribution.
    /// </summary>
    /// <param name="pattern">候选 / Candidate.</param>
    /// <param name="context">注册上下文 / Registration context.</param>
    /// <param name="dual">对偶解 / Dual solution.</param>
    /// <returns>附加检验数 / Additional reduced cost.</returns>
    Flt64 ReducedCost(PackingPattern<V> pattern, PatternAssignmentContext<V> context, MetaDualSolution dual);
}

/// <summary>
/// 方案变量及共享中间符号聚合 / Pattern variables and shared intermediate symbols.
/// </summary>
/// <typeparam name="V">领域数值 / Domain number.</typeparam>
/// <remarks>
/// **本文件是泛型的**：列池要存放具体类型的方案，用 `object` 会让每次读取都要转换。
/// **The aggregation is generic**: the pool holds concrete patterns, and `object` would force a cast on every read.
/// </remarks>
public sealed class PatternAggregation<V>
    where V : struct, IFloatingNumber<V> {
    /// <summary>列池 / The column pool.</summary>
    public Dictionary<string, PackingPattern<V>> Pool { get; } = new();

    /// <summary>方案键到变量 / Pattern key to variable.</summary>
    public Dictionary<string, UIntVar> Variables { get; } = new();

    /// <summary>货物到需求表达式 / Item to demand expression.</summary>
    public Dictionary<string, LinearExpressionSymbol> Demands { get; } = new();

    /// <summary>箱型到库存表达式 / Bin type to inventory expression.</summary>
    public Dictionary<string, LinearExpressionSymbol> Inventories { get; } = new();

    /// <summary>目标表达式 / The objective expression.</summary>
    public LinearExpressionSymbol? Objective { get; set; }
}

/// <summary>
/// 列池里存放的方案类型占位 / Placeholder type for patterns held in the pool.
/// </summary>
/// <summary>
/// 方案定价上下文 / Pattern pricing context.
/// </summary>
/// <typeparam name="V">领域数值 / Domain number.</typeparam>
/// <remarks>
/// **这里只有定价半边。** 建模半边（`register`/`addColumns`/`removeColumns`/`refresh`/`extractSolution`）随 落地；
/// 这个类在 会补上模型字段与那些方法。
///
/// **Only the pricing half is here.** The modelling half (`register`/`addColumns`/`removeColumns`/`refresh`/`extractSolution`) lands with，which adds the
/// model fields and those methods to this class.
/// </remarks>
public sealed class PatternAssignmentContext<V>
    where V : struct, IFloatingNumber<V> {
    private readonly PackingProblem<V> _problem;
    private readonly IPackingValueAdapter<V> _adapter;
    private readonly IReadOnlyList<IPatternModelingExtension<V>> _extensions;
    private MetaDualSolution _shadowPrice = MetaDualSolution.Empty;

    /// <summary>
    /// 构造 / Constructor.
    /// </summary>
    /// <param name="problem">输入 / Input.</param>
    /// <param name="adapter">数值边界 / Numeric boundary.</param>
    /// <param name="extensions">下游扩展 / Downstream extensions.</param>
    public PatternAssignmentContext(
        PackingProblem<V> problem,
        IPackingValueAdapter<V> adapter,
        IReadOnlyList<IPatternModelingExtension<V>>? extensions = null) {
        _problem = problem;
        _adapter = adapter;
        _extensions = extensions ?? new List<IPatternModelingExtension<V>>();
    }

    /// <summary>输入 / Input.</summary>
    public PackingProblem<V> Problem => _problem;

    /// <summary>数值边界 / Numeric boundary.</summary>
    public IPackingValueAdapter<V> Adapter => _adapter;

    /// <summary>下游扩展 / Downstream extensions.</summary>
    public IReadOnlyList<IPatternModelingExtension<V>> Extensions => _extensions;

    /// <summary>当前影子价格 / The current shadow prices.</summary>
    public MetaDualSolution ShadowPrice => _shadowPrice;

    private readonly PatternAggregation<V> _aggregation = new();
    private bool _registered;
    private int _columnSequence;

    /// <summary>已创建过多少列变量（含重复键被拒之前的计数）/ How many column variables have been created, including attempts rejected as duplicates.</summary>
    /// <remarks>
    /// **这是"重复键没有重复建变量"唯一可靠的观察点。** `Variables` 与 `Pool` 都是**按键覆盖**的字典，
    /// 重复建出的第二个变量会被覆盖掉、条数不变——判据看不出问题。
    ///
    /// **This is the only reliable observation point for "a duplicate key creates no second variable".** Both `Variables` and `Pool` are **keyed dictionaries whose
    /// entries get overwritten**, so a duplicated second variable disappears and the counts stay the same.
    /// </remarks>
    public int ColumnSequence => _columnSequence;

    /// <summary>方案变量及共享中间符号聚合 / Pattern variables and shared intermediate symbols.</summary>
    public PatternAggregation<V> Aggregation => _aggregation;

    /// <summary>
    /// 注册初始模型 / Register the initial model.
    /// </summary>
    /// <param name="initialPatterns">初始列 / Initial columns.</param>
    /// <param name="model">元模型 / Meta model.</param>
    /// <returns>操作结果 / Operation result.</returns>
    /// <remarks>
    /// **需求行是 `== amount`，不是 `&lt;=`。** 写成 `&lt;=` 会让"少装一件"也算可行——
    /// 求解器非常乐意把它当成更省的解。**库存行才是 `&lt;=`，而且只在声明了库存时才写。**
    ///
    /// **The demand row is `== amount`, not `&lt;=`.** `&lt;=` would make "pack one fewer" feasible, and the solver would happily call that the cheaper solution. **The
    /// inventory row is `&lt;=` and is only written when an inventory is declared.**
    /// </remarks>
    public Result<Success, ErrorCode, Error<ErrorCode>> Register(
        IReadOnlyList<PackingPattern<V>> initialPatterns,
        LinearMetaModel<Flt64> model) {
        if (_registered) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument, "方案上下文已经注册 / Pattern context already registered"));
        }
        Try validation = _problem.Validate();
        if (validation is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
            return validation;
        }

        foreach (Item<V> item in _problem.Items) {
            LinearExpressionSymbol symbol = Symbol($"demand_{item.Id}");
            _aggregation.Demands[item.Id] = symbol;
            Try added = model.Add(symbol);
            if (added is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
                return added;
            }
            Try row = model.AddConstraint(
                new LinearInequality<Flt64>(
                    new LinearPolynomial<Flt64>(
                        new List<LinearMonomial<Flt64>> { new(Flt64.One, symbol) },
                        Flt64.Zero),
                    new LinearPolynomial<Flt64>(
                        new List<LinearMonomial<Flt64>>(),
                        new Flt64((double)item.Amount)),
                    Fuookami.Ospf.Math.Symbol.Inequality.Comparison.EQ),
                null,
                name: $"demand_{item.Id}",
                args: $"demand:{item.Id}");
            if (row is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
                return row;
            }
        }

        foreach (BinType<V> bin in _problem.BinTypes) {
            LinearExpressionSymbol symbol = Symbol($"inventory_{bin.Id}");
            _aggregation.Inventories[bin.Id] = symbol;
            Try added = model.Add(symbol);
            if (added is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
                return added;
            }
            if (bin.Inventory is not { } inventory) {
                continue;
            }
            Try row = model.AddConstraint(
                new LinearInequality<Flt64>(
                    new LinearPolynomial<Flt64>(
                        new List<LinearMonomial<Flt64>> { new(Flt64.One, symbol) },
                        Flt64.Zero),
                    new LinearPolynomial<Flt64>(
                        new List<LinearMonomial<Flt64>>(),
                        new Flt64((double)inventory)),
                    Fuookami.Ospf.Math.Symbol.Inequality.Comparison.LE),
                null,
                name: $"inventory_{bin.Id}",
                args: $"inventory:{bin.Id}");
            if (row is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
                return row;
            }
        }

        LinearExpressionSymbol objective = Symbol("packing_objective");
        _aggregation.Objective = objective;
        Try objectiveAdded = model.Add(objective);
        if (objectiveAdded is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
            return objectiveAdded;
        }
        Try objectiveRegistered = model.AddObject(
            ObjectCategory.Minimum,
            new LinearPolynomial<Flt64>(
                new List<LinearMonomial<Flt64>> { new(Flt64.One, objective) },
                Flt64.Zero),
            name: "packing",
            displayName: null);
        if (objectiveRegistered is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
            return objectiveRegistered;
        }

        _registered = true;
        Try columns = AddColumns(initialPatterns, model);
        if (columns is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
            return columns;
        }
        return Refresh(model);
    }

    /// <summary>
    /// 追加列并刷新所有中间符号 / Add columns and refresh all intermediate symbols.
    /// </summary>
    /// <param name="columns">新列 / New columns.</param>
    /// <param name="model">元模型 / Meta model.</param>
    /// <returns>操作结果 / Operation result.</returns>
    /// <remarks>
    /// 六条拒绝条件（下表），**第二条与第五条最容易漏**：
    ///
    /// | # | 条件 | 漏掉的后果 |
    /// | --- | --- | --- |
    /// | 1 | 键已在池中 ⇒ **跳过**，不报错 | 同一方案被建两次变量，目标里重复计费 |
    /// | 2 | `ValidatePattern`（尺寸/件重不超箱） | 一个物理上装不下的方案进了模型 |
    /// | 3 | 件数为空 | 一个什么都不装的"方案"，只是白拿一份资金 |
    /// | 4 | 引用未知货物 / 件数超需求 / 未知箱型 | 引用不存在的身份，约束静默不生效 |
    /// | 5 | **上界 = 各货物 `需求 / 件数` 的最小值** | 不设上界 ⇒ 同一方案可以被"用"超过需求允许的次数 |
    ///
    /// **第五条是整数除法**：需求 5、每箱 2 件 ⇒ 上界 `5 / 2 = 2`。用 `5 * 1.0 / 2 = 2.5` 会让求解器
    /// 允许 2.5 份——那是**非整数解**，取解时会被二元性/整数性检查拒掉。
    ///
    /// Six rejection rules (table above); **the second and fifth are the easiest to miss**. **The fifth is integer division**: demand 5 with 2 pieces per bin gives an
    /// upper bound of `5 / 2 = 2`; using `5 * 1.0 / 2 = 2.5` would let the solver take 2.5 copies — a **non-integer solution** rejected at extraction time.
    /// </remarks>
    public Result<Success, ErrorCode, Error<ErrorCode>> AddColumns(
        IReadOnlyList<PackingPattern<V>> columns,
        LinearMetaModel<Flt64> model) {
        if (!_registered) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument, "方案上下文未注册 / Pattern context not registered"));
        }

        foreach (PackingPattern<V> pattern in columns) {
            if (_aggregation.Pool.ContainsKey(pattern.Key)) {
                continue;
            }
            Try validation = _problem.ValidatePattern(pattern);
            if (validation is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
                return validation;
            }
            if (pattern.Amounts.Count == 0 || HasInvalidReference(pattern)) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.IllegalArgument, "方案引用或件数无效 / Invalid pattern references or counts"));
            }

            var variable = new UIntVar($"pattern_{_columnSequence}");
            _columnSequence++;

            // **上界是各货物 `需求 / 件数` 的整数最小值。** 不设上界，同一方案就能被"用"超过需求允许的次数。
            // **The bound is the integer minimum over items of `demand / amount`**; without it the same pattern could be "used" more often than the demand allows.
            ulong upper = ulong.MaxValue;
            foreach (KeyValuePair<string, ulong> pair in pattern.Amounts) {
                ulong demand = 0UL;
                foreach (Item<V> item in _problem.Items) {
                    if (item.Id == pair.Key) {
                        demand = item.Amount;
                        break;
                    }
                }
                ulong ratio = demand / pair.Value;
                if (ratio < upper) {
                    upper = ratio;
                }
            }
            // **上界必须写进变量的区间，不能只写一条约束行。**
            // `UIntVar` 的缺省上界是 `ulong.MaxValue`，换成 `double` 是 `1.8e19`，
            // **超过 binary64 能精确表示的整数上界 `2^53`**——求解器在注册阶段直接拒
            // （"整数变量边界须在二进制64精确整数范围内且有限"）。
            // **曾把上界写成一条约束行、并在文档里记成"有意偏离"，真实的 SCIP 一跑就露馅。**
            //
            // **The bound must go into the variable's range, not into a constraint row alone.** `UIntVar`'s default upper bound is `ulong.MaxValue`, which as a `double`
            // is `1.8e19` — **beyond binary64's exact-integer limit `2^53`** — so the solver refuses at registration. **wrote the bound as a row and recorded that as a
            // deliberate deviation; one real SCIP run exposed it.**
            variable.Range.Leq(new InvariantUInt64Wrapper(new UInt64(upper)));

            Try added = model.Add(variable);
            if (added is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
                return added;
            }
            // **目标只挂一个来源：聚合符号。逐列再挂一份会把它算两次。**
            // 若 `register` 挂符号、`addColumns` 再逐列挂一项，而挂符号时**符号已被展开成当时的（空）列池**，
            // 那么逐列那份额外挂在目标里，符号本身贡献为零。
            //
            // **C# 侧不会在注册时展开符号**（实测：一列 `I1=4`、成本 1，SCIP 报目标 **2**，
            // 也就是符号那一份与逐列那一份都在）。**所以这里只保留符号这一份。**
            //
 // **The objective has exactly one source: the aggregate symbol.** Adding a per-column term as well counts it twice. The implementation registers the symbol and
            // then adds a per-column term, but there the symbol is **flattened into the then-empty pool at registration**, so only the per-column term actually
            // contributes. **C# does not flatten at registration** (measured: one column `I1=4` with cost 1 makes SCIP report an objective of **2**, i.e. both copies
            // are present), **so only the symbol is kept here.**

            _aggregation.Pool[pattern.Key] = pattern;
            _aggregation.Variables[pattern.Key] = variable;
        }

        return Refresh(model);
    }

    /// <summary>
    /// 按当前列池全量重建需求、库存与目标表达式 / Rebuild demand, inventory and objective expressions from the current pool.
    /// </summary>
    /// <param name="model">元模型 / Meta model.</param>
    /// <returns>操作结果 / Operation result.</returns>
    /// <remarks>
    /// **与一处有意偏离：每次全量重建，不做"只追加新列"的增量。** 增量靠
    /// `asMutable.mutationVersion` 这个**可变版本号**来判断哪些符号没变过；
    /// C# 侧没有这个计数器，**也没有任何外部可观察的等价信号**。
    ///
    /// 增量的收益是少算几次，风险是**一旦判断错了就静默保留旧系数**。全量重建
    /// **结果完全相同**、代码少一个只存在于优化里的状态。**这是拿常数因子换正确性，值得。**
    /// `ponytail:` 每次刷新 O(列 × 项)；若列池规模成为瓶颈，再引入真正的脏标记。
    ///
 /// **One deliberate deviation: a full rebuild every time, no "append only the new columns" increment.** Its increment relies on the **mutable
    /// `asMutable.mutationVersion` counter** to decide which symbols changed, and C# has neither that counter **nor any externally observable equivalent**. The
    /// increment saves a few recomputations and risks **silently keeping stale coefficients** when the judgement is wrong. A full rebuild produces **exactly the same
    /// result** with one less piece of state that only exists for optimisation. **That trades a constant factor for correctness, which is worth it.**
    /// `ponytail:` each refresh is O(columns × terms); introduce a real dirty flag if pool size ever becomes the bottleneck.
    /// </remarks>
    public Result<Success, ErrorCode, Error<ErrorCode>> Refresh(LinearMetaModel<Flt64> model) {
        _ = model;

        foreach (KeyValuePair<string, LinearExpressionSymbol> pair in _aggregation.Demands) {
            List<LinearMonomial<Flt64>> monomials = new();
            foreach (PackingPattern<V> pattern in _aggregation.Pool.Values) {
                ulong amount = pattern.Amounts.TryGetValue(pair.Key, out ulong found) ? found : 0UL;
                if (amount == 0UL) {
                    continue;
                }
                monomials.Add(new LinearMonomial<Flt64>(
                    new Flt64((double)amount), _aggregation.Variables[pattern.Key]));
            }
            Replace(pair.Value, monomials);
        }

        foreach (KeyValuePair<string, LinearExpressionSymbol> pair in _aggregation.Inventories) {
            List<LinearMonomial<Flt64>> monomials = new();
            foreach (PackingPattern<V> pattern in _aggregation.Pool.Values) {
                if (pattern.BinTypeId == pair.Key) {
                    monomials.Add(new LinearMonomial<Flt64>(Flt64.One, _aggregation.Variables[pattern.Key]));
                }
            }
            Replace(pair.Value, monomials);
        }

        List<LinearMonomial<Flt64>> objectiveMonomials = new();
        foreach (PackingPattern<V> pattern in _aggregation.Pool.Values) {
            Result<Flt64, ErrorCode, Error<ErrorCode>> coefficient = Cost(pattern);
            if (coefficient is not Ok<Flt64, ErrorCode, Error<ErrorCode>> coefficientOk) {
                return ((Failed<Flt64, ErrorCode, Error<ErrorCode>>)coefficient).Error is { } error
                    ? Results.Failed<Success>(error)
                    : new Fatal<Success, ErrorCode, Error<ErrorCode>>(
                        ((Fatal<Flt64, ErrorCode, Error<ErrorCode>>)coefficient).Errors);
            }
            objectiveMonomials.Add(new LinearMonomial<Flt64>(
                coefficientOk.Value, _aggregation.Variables[pattern.Key]));
        }
        if (_aggregation.Objective is { } objective) {
            Replace(objective, objectiveMonomials);
        }

        return Results.OkInstance;
    }

    /// <summary>
    /// 目标系数，按显式口径归一 / Objective coefficient in an explicit normalised metric.
    /// </summary>
    /// <param name="pattern">候选 / Candidate.</param>
    /// <returns>系数或失败 / The coefficient or a failure.</returns>
    /// <remarks>
    /// 三种口径：`BinCount` 恒为 **1**（数箱数）；`ConsumedLength` 是**箱长换算到米**；
    /// `Cost` 是箱单价。
    ///
    /// **换算失败的处理**：`bin.length.convertTo(Meter)` 若断言一定成功、失败就抛，
    /// 量纲不兼容就变成异常。这里返回 `Result`：那本是一个**可查的错误**，不该变成空指针异常。
    ///
    /// Three metrics: `BinCount` is always **1** (counting bins), `ConsumedLength` is the **bin length in metres**, `Cost` is the unit bin cost. **How a failed conversion is
    /// handled**: `bin.length.convertTo(Meter)` asserting success would turn an incompatible dimension into an exception. This returns a `Result`, because an
    /// incompatible length dimension is a **checkable error** and must not become a null-reference exception.
    /// </remarks>
    public Result<Flt64, ErrorCode, Error<ErrorCode>> Cost(PackingPattern<V> pattern) {
        BinType<V>? bin = null;
        foreach (BinType<V> candidate in _problem.BinTypes) {
            if (candidate.Id == pattern.BinTypeId) {
                bin = candidate;
                break;
            }
        }
        if (bin is null) {
            return Results.Failed<Flt64>(new Err<ErrorCode>(
                ErrorCode.DataNotFound, $"未知箱型 / Unknown bin type: {pattern.BinTypeId}"));
        }

        switch (_problem.Objective) {
            case PackingObjective.BinCount:
                return Results.Ok(Flt64.One);
            case PackingObjective.Cost:
                return Results.Ok(_adapter.ToSolver(bin.Cost));
            case PackingObjective.ConsumedLength:
                Result<Quantity<V>, ErrorCode, Error<ErrorCode>> length = bin.Length.To(SIBaseUnits.Meter);
                if (length is not Ok<Quantity<V>, ErrorCode, Error<ErrorCode>> lengthOk) {
                    return Results.Failed<Flt64>(new Err<ErrorCode>(
                        ErrorCode.IllegalArgument,
                        $"箱长量纲不兼容 / Incompatible bin length units: {bin.Id}"));
                }
                return Results.Ok(_adapter.ToSolver(lengthOk.Value.Value));
            default:
                return Results.Failed<Flt64>(new Err<ErrorCode>(
                    ErrorCode.IllegalArgument, "未知目标口径 / Unknown objective"));
        }
    }

    /// <summary>
    /// 刷新约束身份对应的对偶价格 / Refresh dual prices by constraint identity.
    /// </summary>
    /// <param name="dual">当前 LP 对偶解 / Current LP dual solution.</param>
    /// <returns>操作结果 / Operation result.</returns>
    /// <remarks>
    /// **非有限的对偶价格必须被拒。** 一个 `NaN` 影子价格会让之后**每一个**检验数都变成 `NaN`，
    /// 而 `NaN` 与原目标值比较恒为假 ⇒ **所有候选看起来都"不值得加入"**，列生成**静默停止**，
    /// 报告出来的却是"已经收敛"。这是最坏的一种失败：它看起来像正常结束。
    ///
    /// **Non-finite dual prices must be rejected.** A `NaN` shadow price turns **every** subsequent reduced cost into `NaN`, and every comparison with `NaN` is false ⇒
    /// **every candidate looks "not worth adding"**, column generation **stops silently**, and the report says "converged". That is the worst kind of failure: it
    /// looks like a normal finish.
    /// </remarks>
    public Result<Success, ErrorCode, Error<ErrorCode>> RefreshShadowPrice(MetaDualSolution dual) {
        foreach (KeyValuePair<MathConstraint, Flt64> pair in dual.Constraints) {
            if (!double.IsFinite(pair.Value.ToDouble())) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.IllegalArgument, "影子价格必须有限 / Shadow prices must be finite"));
            }
        }
        foreach (KeyValuePair<IIntermediateSymbol, IReadOnlyList<(IConstraint<Flt64, LinearCategory> Constraint, Flt64 Price)>> pair
                 in dual.Symbols) {
            foreach ((_, Flt64 price) in pair.Value) {
                if (!double.IsFinite(price.ToDouble())) {
                    return Results.Failed<Success>(new Err<ErrorCode>(
                        ErrorCode.IllegalArgument, "影子价格必须有限 / Shadow prices must be finite"));
                }
            }
        }
        _shadowPrice = dual;
        return Results.OkInstance;
    }

    /// <summary>
    /// 使用最新影子价格定价 / Price using the latest shadow prices.
    /// </summary>
    /// <param name="pattern">候选 / Candidate.</param>
    /// <returns>检验数 / Reduced cost.</returns>
    public Flt64 ReducedCost(PackingPattern<V> pattern) => ReducedCost(pattern, _shadowPrice);

    /// <summary>
    /// 按约束身份刷新影子价格并计算完整检验数 / Compute the reduced cost using constraint-identity dual prices.
    /// </summary>
    /// <param name="pattern">候选 / Candidate.</param>
    /// <param name="dual">元对偶解 / Meta dual solution.</param>
    /// <returns>检验数 / Reduced cost.</returns>
    /// <remarks>
    /// 系数为 `cost`，然后减去需求行与库存行的贡献，再加上扩展项。
    ///
    /// **`inventory:` 的键必须带箱型**：写成 `"inventory:"` 前缀匹配会让**所有箱型**的库存价格都减掉，
    /// 检验数集体偏低 ⇒ **该加的列不加**。
    ///
    /// The coefficient starts at `cost`, then demand-row and inventory-row contributions are subtracted, then extension terms are added. **The `inventory:` key must
    /// carry the bin type**: matching on the `"inventory:"` prefix alone would subtract **every** bin type's price, dragging all reduced costs down ⇒ **columns that
    /// should be added are not**.
    /// </remarks>
    public Flt64 ReducedCost(PackingPattern<V> pattern, MetaDualSolution dual) {
        Flt64 cost = Cost(pattern) is Ok<Flt64, ErrorCode, Error<ErrorCode>> ok ? ok.Value : Flt64.Zero;
        foreach (KeyValuePair<MathConstraint, Flt64> pair in dual.Constraints) {
            if (pair.Key.Args is not string key) {
                continue;
            }
            if (key.StartsWith("demand:", global::System.StringComparison.Ordinal)) {
                string itemId = key.Substring("demand:".Length);
                ulong amount = pattern.Amounts.TryGetValue(itemId, out ulong found) ? found : 0UL;
                cost = cost.Minus(new Flt64((double)amount).Times(pair.Value));
            }
            if (key == $"inventory:{pattern.BinTypeId}") {
                cost = cost.Minus(pair.Value);
            }
        }
        foreach (IPatternModelingExtension<V> extension in _extensions) {
            cost = cost.Plus(extension.ReducedCost(pattern, this, dual));
        }
        return cost;
    }

    /// <summary>
    /// 在只读定价批次内共享稀疏价格索引，仍按原对偶行顺序累加 / Share a sparse price index within a read-only pricing batch, still accumulating in the original dual-row order.
    /// </summary>
    /// <param name="patterns">待报价候选，顺序不变 / Candidates in unchanged order.</param>
    /// <returns>对应候选的完整检验数，包含扩展项 / Complete reduced costs including extension contributions.</returns>
    /// <remarks>
    /// **这一条的全部意义在"按原对偶行顺序累加"。** 浮点加法**不满足结合律**：
    /// `(0.1 + 0.2) + 0.3 = 0.6000000000000001`，而 `(0.3 + 0.2) + 0.1 = 0.6`。
    /// 若按 `pattern.amounts` 的字典序累加，同一个候选就会得到**与单件重载不同**的值——
    /// 于是一个候选在两处得到两个检验数，**加入与否取决于走的哪条路**。
    ///
    /// 所以：先按对偶行的枚举下标收集贡献，**按该下标排序**，再累加。
    /// 判据就是把批量结果与 `ReducedCost(pattern)`（单件重载）逐位比较。
    ///
    /// **The whole point of this overload is "accumulating in the original dual-row order."** Floating-point addition is **not associative**:
    /// `(0.1 + 0.2) + 0.3 = 0.6000000000000001` while `(0.3 + 0.2) + 0.1 = 0.6`. Accumulating in the `pattern.amounts` dictionary order would give one candidate **a
    /// different value from the single-item overload**, so the same candidate gets two reduced costs and **whether it is added depends on which path is taken**. Hence:
    /// collect contributions with the dual row's enumeration index, **sort by that index**, then accumulate. The criterion compares the batched result bit-for-bit
    /// against `ReducedCost(pattern)`.
    /// </remarks>
    public IReadOnlyList<Flt64> ReducedCosts(IReadOnlyList<PackingPattern<V>> patterns) {
        MetaDualSolution dual = _shadowPrice;

        // **这条兜底分支通过公开接口走不到。** `RefreshShadowPrice` 已经拒绝了任何非有限价格，
        // 而 `_shadowPrice` 只有它一个写入点，所以 `allFinite` 恒真。
        // **这里没为它写判据**——写一条"绕过守卫再调用"的判据只能测到构造函数，测不到真实路径，
        // 那是"判据通过但没证明要证明的事"。**如实记录为不可达分支**（与物料溢出分支同类）。
        //
        // **This fallback is unreachable through the public API.** `RefreshShadowPrice` already rejects any non-finite price and `_shadowPrice` has no other writer, so
        // `allFinite` is always true. **No criterion covers it** — a criterion that bypasses the guard only exercises the constructor, not the real path,
        // and that is "passing without proving anything". **Recorded as an unreachable branch**, the same class as the earlier material-overflow branch.
        bool allFinite = true;
        foreach (KeyValuePair<MathConstraint, Flt64> pair in dual.Constraints) {
            if (!double.IsFinite(pair.Value.ToDouble())) {
                allFinite = false;
                break;
            }
        }
        if (!allFinite) {
            var fallback = new List<Flt64>(patterns.Count);
            foreach (PackingPattern<V> pattern in patterns) {
                fallback.Add(ReducedCost(pattern, dual));
            }
            return fallback;
        }

        Dictionary<string, List<(int Index, Flt64 Price)>> demandPrices = new();
        Dictionary<string, List<(int Index, Flt64 Price)>> inventoryPrices = new();
        int rowIndex = 0;
        foreach (KeyValuePair<MathConstraint, Flt64> pair in dual.Constraints) {
            int index = rowIndex;
            rowIndex++;
            if (pair.Key.Args is not string key) {
                continue;
            }
            if (key.StartsWith("demand:", global::System.StringComparison.Ordinal)) {
                Accumulate(demandPrices, key.Substring("demand:".Length), index, pair.Value);
            }
            if (key.StartsWith("inventory:", global::System.StringComparison.Ordinal)) {
                Accumulate(inventoryPrices, key.Substring("inventory:".Length), index, pair.Value);
            }
        }

        var results = new List<Flt64>(patterns.Count);
        foreach (PackingPattern<V> pattern in patterns) {
            var contributions = new List<(int Index, Flt64 Value)>();
            foreach (KeyValuePair<string, ulong> item in pattern.Amounts) {
                if (!demandPrices.TryGetValue(item.Key, out List<(int Index, Flt64 Price)>? prices)) {
                    continue;
                }
                foreach ((int index, Flt64 price) in prices) {
                    contributions.Add((index, new Flt64((double)item.Value).Times(price)));
                }
            }
            if (inventoryPrices.TryGetValue(pattern.BinTypeId, out List<(int Index, Flt64 Price)>? inventory)) {
                foreach ((int index, Flt64 price) in inventory) {
                    contributions.Add((index, price));
                }
            }
            contributions.Sort((left, right) => left.Index.CompareTo(right.Index));

            Flt64 cost = Cost(pattern) is Ok<Flt64, ErrorCode, Error<ErrorCode>> ok ? ok.Value : Flt64.Zero;
            foreach ((_, Flt64 value) in contributions) {
                cost = cost.Minus(value);
            }
            foreach (IPatternModelingExtension<V> extension in _extensions) {
                cost = cost.Plus(extension.ReducedCost(pattern, this, dual));
            }
            results.Add(cost);
        }
        return results;
    }

    /// <summary>是否有未知引用或超需求的件数 / Whether any reference is unknown or any count exceeds the demand.</summary>
    /// <param name="pattern">候选 / Candidate.</param>
    /// <returns>判断结果 / The verdict.</returns>
    private bool HasInvalidReference(PackingPattern<V> pattern) {
        foreach (KeyValuePair<string, ulong> pair in pattern.Amounts) {
            if (pair.Value == 0UL) {
                return true;
            }
            bool matched = false;
            foreach (Item<V> item in _problem.Items) {
                if (item.Id == pair.Key) {
                    matched = pair.Value <= item.Amount;
                    break;
                }
            }
            if (!matched) {
                return true;
            }
        }
        foreach (BinType<V> bin in _problem.BinTypes) {
            if (bin.Id == pattern.BinTypeId) {
                return false;
            }
        }
        return true;
    }

    /// <summary>把符号内容整体换掉 / Replace a symbol's contents wholesale.</summary>
    /// <param name="symbol">符号 / The symbol.</param>
    /// <param name="monomials">新单项式 / The new monomials.</param>
    private static void Replace(LinearExpressionSymbol symbol, IReadOnlyList<LinearMonomial<Flt64>> monomials) {
        symbol.AsMutable().Clear();
        symbol.AsMutable().Add(new LinearPolynomial<Flt64>(
            new List<LinearMonomial<Flt64>>(monomials), Flt64.Zero));
    }

    /// <summary>新建一个零表达式符号 / Create a fresh zero-expression symbol.</summary>
    /// <param name="name">名字 / Name.</param>
    /// <returns>符号 / The symbol.</returns>
    private static LinearExpressionSymbol Symbol(string name) =>
        new(new MutableLinearPolynomial<Flt64>(
            new List<LinearMonomial<Flt64>>(), Flt64.Zero),
            name: name);

    /// <summary>
    /// 删除列，同时移除模型中的变量与目标项 / Remove columns together with their variables and objective terms.
    /// </summary>
    /// <param name="keys">列键 / Column keys.</param>
    /// <param name="model">元模型 / Meta model.</param>
    /// <returns>操作结果 / Operation result.</returns>
    /// <remarks>
    /// **顺序不能换**：
    ///
    /// | 步 | 做什么 | 换掉之后 |
    /// | --- | --- | --- |
    /// | 1 | 从池与变量表里摘掉 | — |
    /// | 2 | 按**已缩减的池**重建需求/库存/目标表达式 | 先删目标项再刷新，刷新又会把已删列写回去 |
    /// | 3 | `RemoveObjectiveTerms` 清掉逐列目标项 | — |
    /// | 4 | 从模型里移除变量 | 先移变量再清目标项，目标项里会留下一个已不存在的变量 |
    ///
    /// **未知键静默跳过，不报错**——`mapNotNull { variables.remove(it) }`。
    /// 定价循环每轮都可能重发候选，把"本来就不在池里"当成错误会让每轮都失败。
    ///
    /// **The order cannot change** (table above): removing the objective terms before refreshing would let the refresh write the deleted columns back, and removing the
    /// variables before clearing their terms would leave an objective term pointing at a variable that no longer exists. **Unknown keys are skipped silently, not
 /// reported** — this implementation uses `mapNotNull { variables.remove(it) }`, and a pricing loop may resend candidates every round, so treating "not in the pool" as an
    /// error would fail every round.
    /// </remarks>
    public Result<Success, ErrorCode, Error<ErrorCode>> RemoveColumns(
        IReadOnlySet<string> keys,
        LinearMetaModel<Flt64> model) {
        if (!_registered) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument, "方案上下文未注册 / Pattern context not registered"));
        }

        var variables = new List<UIntVar>();
        foreach (string key in keys) {
            if (_aggregation.Variables.TryGetValue(key, out UIntVar? variable)) {
                variables.Add(variable);
                _aggregation.Variables.Remove(key);
            }
            _aggregation.Pool.Remove(key);
        }

        Result<Success, ErrorCode, Error<ErrorCode>> refreshed = Refresh(model);
        if (refreshed is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
            return refreshed;
        }

        if (variables.Count == 0) {
            return Results.OkInstance;
        }

        // **不需要 `RemoveObjectiveTerms`。** 把目标改成**单一来源**（聚合符号），
        // 而 `Refresh` 已经按剩余列池重建了符号内容——逐列目标项根本不存在，没有东西可清。
        // 那条 API 在这里变成多余的一步。**删掉，不留"以防万一"的调用。**
        //
        // **`RemoveObjectiveTerms` is not needed.** The objective has a **single source** (the aggregate symbol) and `Refresh` already rebuilds its contents from the
        // remaining pool, so per-column objective terms no longer exist and there is nothing to clear.
        foreach (UIntVar variable in variables) {
            model.Remove(variable);
        }
        return Results.OkInstance;
    }

    /// <summary>
    /// 提取整数列份数 / Extract integer pattern usages.
    /// </summary>
    /// <param name="model">元模型 / Meta model.</param>
    /// <returns>列份数或失败 / Usages or failure.</returns>
    /// <remarks>
    /// **与一处偏离：返回有序对列表，而不是以方案为键的字典。**
    /// `Map&lt;PackingPattern, UInt64&gt;` 依赖 data class 按**内容**比较（含 `amounts` 这个 Map）；
    /// C# 的 `record` 对字段里的字典做**引用比较**，以方案为键的字典会**错键**——
    /// 两个数量完全相同的方案会被当成两个不同的键。**有序对列表绕开这个陷阱，也更清楚。**
    ///
    /// **四条拒绝条件**：
    ///
    /// | 条件 | 若放过 | 后果 |
    /// | --- | --- | --- |
    /// | 缺 token 或缺值 | 悄悄跳过 | **少算一份用量**，读起来像"用得更少" |
    /// | 非整数（`\|v - round(v)\| > 1e-6`） | 四舍五入 | 一个 LP 松弛值被当成整数用量 |
    /// | 负数 | 收下 | 负用量让"总数"凭空减少 |
    /// | 超过 `long` 上界 | 截断 | 用量溢出成荒谬的小值 |
    ///
    /// **容差 `1e-6` 两侧都钉住**：`1.9999999` 必须算 2（否则正常解全被拒），`2.0001` 必须被拒。
    ///
 /// **One deviation: an ordered list of pairs instead of a dictionary keyed by pattern.** Its `Map&lt;PackingPattern, UInt64&gt;` relies on
    /// Kotlin data-class **content** equality over the `amounts` map, while a C# `record` compares a nested dictionary **by reference**, so a pattern-keyed dictionary
    /// would **mis-key**: two patterns with identical amounts would count as two distinct keys. The ordered list sidesteps that trap and reads more clearly. The four
    /// rejection rules are in the table; **the `1e-6` tolerance is pinned from both sides** — `1.9999999` must count as 2 or every normal solution is rejected, while
    /// `2.0001` must be refused.
    /// </remarks>
    public Result<IReadOnlyList<(PackingPattern<V> Pattern, ulong Usage)>, ErrorCode, Error<ErrorCode>>
        ExtractSolution(LinearMetaModel<Flt64> model) {
        var usages = new List<(PackingPattern<V> Pattern, ulong Usage)>();
        foreach (PackingPattern<V> pattern in _aggregation.Pool.Values) {
            if (!_aggregation.Variables.TryGetValue(pattern.Key, out UIntVar? variable)) {
                return Results.Failed<IReadOnlyList<(PackingPattern<V> Pattern, ulong Usage)>>(
                    new Err<ErrorCode>(ErrorCode.DataNotFound, "缺少方案 token / Missing pattern token"));
            }
            if (model.Tokens.Find(variable)?.DoubleResult is not { } raw || !double.IsFinite(raw)) {
                return Results.Failed<IReadOnlyList<(PackingPattern<V> Pattern, ulong Usage)>>(
                    new Err<ErrorCode>(ErrorCode.DataNotFound, "缺少方案值 / Missing pattern value"));
            }
            double rounded = global::System.Math.Round(raw, global::System.MidpointRounding.AwayFromZero);
            if (!double.IsFinite(rounded)
                || rounded < 0.0
                || global::System.Math.Abs(raw - rounded) > 1e-6
                || rounded > long.MaxValue) {
                return Results.Failed<IReadOnlyList<(PackingPattern<V> Pattern, ulong Usage)>>(
                    new Err<ErrorCode>(ErrorCode.ApplicationError, "非法方案份数 / Invalid pattern usage"));
            }
            if (rounded > 0.0) {
                usages.Add((pattern, (ulong)rounded));
            }
        }
        return Results.Ok<IReadOnlyList<(PackingPattern<V> Pattern, ulong Usage)>>(usages);
    }

    /// <summary>累积一条稀疏价格 / Accumulate one sparse price entry.</summary>
    /// <param name="table">价格表 / The price table.</param>
    /// <param name="key">键 / Key.</param>
    /// <param name="index">对偶行下标 / Dual-row index.</param>
    /// <param name="price">价格 / Price.</param>
    private static void Accumulate(
        Dictionary<string, List<(int Index, Flt64 Price)>> table,
        string key,
        int index,
        Flt64 price) {
        if (!table.TryGetValue(key, out List<(int Index, Flt64 Price)>? list)) {
            list = new List<(int Index, Flt64 Price)>();
            table[key] = list;
        }
        list.Add((index, price));
    }
}
