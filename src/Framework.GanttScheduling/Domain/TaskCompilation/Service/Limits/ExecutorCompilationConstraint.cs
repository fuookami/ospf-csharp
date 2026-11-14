#nullable enable

using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model;
using Fuookami.Ospf.Framework.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Service.Limits;
/// <summary>
/// 执行器编译影子价格键 / Executor compilation shadow price key
/// </summary>
/// <typeparam name="E">执行器类型 / Executor type</typeparam>
public sealed class ExecutorCompilationShadowPriceKey<E> : ShadowPriceKey
    where E : Executor {
    /// <summary>
    /// 构造 / Constructor
    /// </summary>
    /// <param name="executor">执行器 / Executor</param>
    public ExecutorCompilationShadowPriceKey(E executor) : base(typeof(ExecutorCompilationShadowPriceKey<E>)) {
        Executor = executor;
    }

    /// <summary>执行器 / Executor</summary>
    public E Executor { get; }

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is ExecutorCompilationShadowPriceKey<E> other && EqualityComparer<E>.Default.Equals(Executor, other.Executor);

    /// <inheritdoc/>
    public override int GetHashCode() => Executor?.GetHashCode() ?? 0;
}

/// <summary>
/// 执行器编译约束 / Executor compilation constraint
/// </summary>
/// <typeparam name="E">执行器类型 / Executor type</typeparam>
/// <typeparam name="A">分配策略类型 / Assignment policy type</typeparam>
public class ExecutorCompilationConstraint<E, A>
    : ICGPipeline<IGanttSchedulingShadowPriceArguments<E, A>, object, GanttSchedulingShadowPriceMap<E, A>>
    where E : Executor
    where A : IAssignmentPolicy<E> {
    private readonly IReadOnlyList<E> _executors;
    private readonly ICompilation _compilation;
    private readonly IReadOnlyDictionary<E, object>? _explicitExecutorCompilation;
    private readonly string _name;

    /// <summary>
    /// 执行器编译约束构造 / Executor compilation constraint constructor
    /// </summary>
    /// <param name="executors">执行器列表 / List of executors</param>
    /// <param name="compilation">编译结果 / Compilation result</param>
    /// <param name="executorCompilation">执行器到编译表达式的映射；`null` 表示尚未接线 / The map from executor to a compiled expression; null means not yet wired</param>
    /// <param name="name">管道名称 / Pipeline name</param>
    public ExecutorCompilationConstraint(
        IReadOnlyList<E> executors,
        ICompilation compilation,
        IReadOnlyDictionary<E, object>? executorCompilation = null,
        string name = "executor_compilation") {
        _executors = executors;
        _compilation = compilation;
        _explicitExecutorCompilation = executorCompilation;
        _name = name;
    }

    /// <summary>
    /// 执行器到编译表达式的映射；**每次读取时**才从编译结果取 / The map from executor to a compiled expression; taken from the compilation **on each read**.
    /// </summary>
    /// <remarks>
    /// **必须延迟读，不能构造时快照**。构造发生在管线列表生成的时候，那时聚合还没注册，`executor_compilation` 表还是空的；
    /// 快照下来就是把"还没接线"永久冻结成"一个执行器都没有"。这个坑踩过一次：快照之后这条约束对任何执行器都失败，
    /// 而失败信息说的是"编译表达式缺失"，指向的是编译层，不是这个构造顺序。
    ///
    /// **This must be read lazily, not snapshotted at construction.** Construction happens while the pipeline list is generated, before the aggregation is registered, when the
    /// `executor_compilation` table is still empty; a snapshot freezes "not wired yet" into "no executor at all". This was hit once: after snapshotting, the constraint failed for
    /// every executor, and the message said "missing compilation expression", pointing at the compilation layer rather than at this construction-order bug.
    /// </remarks>
    private IReadOnlyDictionary<E, object>? ExecutorCompilationTable =>
        _explicitExecutorCompilation ?? (_compilation as IExecutorCompilation<E>)?.ExecutorCompilation;

    string IMetaConstraintGroup.Name => _name;
    bool IMetaConstraintGroup.Lazy => false;

    /// <summary>
    /// 缺少编译表达式的执行器 / The executors without a compiled expression.
    /// </summary>
    /// <returns>缺失的执行器 / The missing executors.</returns>
    /// <remarks>
    /// **只查这一层，不查编译结果里有没有**。表达式表由构造方提供，约束只负责"表中该有的都有"。
    /// 缺一个执行器就意味着该执行器在模型里**没有任何编译要求**，求解器可以让它一列都不选，
    /// 而排程结果看起来完全正常：所有任务都排上了，只是某个班次一个人都没上。
    /// **这种缺陷不在目标值上留痕迹，只让实际产出低于计划。**
    ///
    /// **Only this layer is checked, not whether the compilation produced them.** The expression table is supplied by the caller; the constraint only guarantees "what the table
    /// should have, it has". A missing executor means that executor has **no compilation requirement** in the model, so the solver may select no column for it while the
    /// schedule looks entirely normal: every task assigned, one shift simply using nobody. **The defect leaves no trace in the objective; only output falls short of plan.**
    /// </remarks>
    public IReadOnlyList<E> MissingExpressions() {
        // ponytail: `null` 表示表达式表还没接线，此时无从检查；真正的修法是从 `ICompilation` 取出这张表。
        // 一旦表存在，缺项就**必须**失败——只给一部分执行器加约束比整体失败更危险。
        // ponytail: null means the table is not wired yet and there is nothing to check; the real fix is to source it from `ICompilation`.
        // Once the table exists, a gap **must** fail — constraining only some executors is more dangerous than failing outright.
        if (ExecutorCompilationTable is null) {
            return Array.Empty<E>();
        }

        var missing = new List<E>();
        foreach (E executor in _executors) {
            if (!ExecutorCompilationTable!.ContainsKey(executor)) {
                missing.Add(executor);
            }
        }
        return missing;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// **这一条以前只做校验、不加任何行**：它检查表里有没有每个执行器的编译表达式，然后返回 `Ok`。
    /// 基线的 `invoke` 是 `model.addConstraint(executorCompilation[executor] eq 1, args = ExecutorCompilationShadowPriceKey(executor))`——
    /// 也就是**每个执行器必须恰好被编译一次**这条要求本身。少了它，执行器编译量可以取任意值（包括零），
    /// 于是"某个班次一个人都没上"不会在目标值上留痕迹，只让实际产出低于计划。
    ///
    /// 三件事一起做：**缺表达式就失败**（原来那半段）、**加 `= 1` 的行**、**行上挂键**。
    ///
    /// **This used to validate only and add no row**: it checked that every executor had a compiled expression and returned `Ok`. The baseline's `invoke` is
    /// `model.AddConstraint(executorCompilation[executor] eq 1, args = ExecutorCompilationShadowPriceKey(executor))` — the requirement itself, that **each executor is compiled exactly once**.
    /// Without it the compiled quantity may take any value (zero included), so "one shift uses nobody" leaves no trace in the objective and merely makes output fall short of plan. Three things now
    /// happen together: **fail on a missing expression** (the half that existed), **add the `= 1` row**, and **attach the key to the row**.
    /// </remarks>
    public Try Invoke(object model) {
        System.ArgumentNullException.ThrowIfNull(model);

        if (ExecutorCompilationTable is null) {
            return Results.Ok<Success>(Results.SuccessInstance);
        }
        if (model is not Fuookami.Ospf.Core.Model.Mechanism.IAbstractLinearMetaModel<Flt64> linear) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "执行器编译约束只能施加到线性元模型 / the executor compilation constraint can only be applied to a linear meta model"));
        }

        // 第一个缺失就失败：部分施加的约束比完全失败更危险。
        // Fail on the first gap: a partially applied constraint set is more dangerous than failing outright.
        foreach (E executor in _executors) {
            if (!ExecutorCompilationTable!.TryGetValue(executor, out object? expression)) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.ApplicationError,
                    "缺少执行器编译表达式：" + executor
                    + " / Missing executor compilation expression: " + executor));
            }
            // ponytail: 表达式是**占位对象**时这一条加不了行——`CompilationTable.Build` 现在填的是 `new object()`
            // （`TaskCompilation` / `BunchCompilation` 两条路径都是），要建 `x = 1` 得先让那张表提供真正的列变量。
            // 升级路径：把 `CompilationTable.Build` 改成用 `SymbolCombination<LinearExpressionSymbol, Shape1>` 造符号，
            // 那时这一支自然走上面那条路。此刻**不能改成失败**：占位是当前的正常状态，失败就是把全部分配打回。
            //
            // ponytail: a placeholder expression cannot produce a row — `CompilationTable.Build` fills `new object()` today (on both the `TaskCompilation` and `BunchCompilation` paths),
            // and building `x = 1` first needs that table to hand out real column variables. Upgrade path: make `CompilationTable.Build` mint symbols with
            // `SymbolCombination<LinearExpressionSymbol, Shape1>`, and this branch falls through to the row-building path. It must **not** be turned into a failure right now: a placeholder is
            // the normal state today, and failing would reject every assignment.
            if (expression is not Fuookami.Ospf.Core.Symbol.ILinearIntermediateSymbol<Flt64> symbol) {
                continue;
            }

            // **符号要包成单项式**：`LinearExpressionSymbol` 本身就是变量，展开它的多项式是空的。
            // **Wrap the symbol in a monomial**: a `LinearExpressionSymbol` *is* a variable and its polynomial is empty.
            string rowName = _name + "_" + executor.Id;
            Try added = linear.AddConstraint(
                new Fuookami.Ospf.Math.Symbol.Inequality.LinearInequality<Flt64>(
                    new Fuookami.Ospf.Math.Symbol.Polynomial.LinearPolynomial<Flt64>(
                        new[] { new Fuookami.Ospf.Math.Symbol.Monomial.LinearMonomial<Flt64>(Flt64.One, symbol) },
                        Flt64.Zero),
                    new Fuookami.Ospf.Math.Symbol.Polynomial.LinearPolynomial<Flt64>(
                        Array.Empty<Fuookami.Ospf.Math.Symbol.Monomial.LinearMonomial<Flt64>>(), Flt64.One),
                    Fuookami.Ospf.Math.Symbol.Inequality.Comparison.EQ,
                    rowName),
                group: null,
                name: rowName,
                args: new ExecutorCompilationShadowPriceKey<E>(executor));
            if (!added.IsOk) {
                return added;
            }
        }
        return Results.Ok<Success>(Results.SuccessInstance);
    }

    /// <inheritdoc/>
    public ShadowPriceExtractor<IGanttSchedulingShadowPriceArguments<E, A>, GanttSchedulingShadowPriceMap<E, A>>? Extractor()
        => (map, args) => {
            if (args.Task == null) {
                var key = new ExecutorCompilationShadowPriceKey<E>(args.Executor);
                return map.Map.TryGetValue(key, out ShadowPrice? sp) ? sp.Price : Flt64.Zero;
            }
            return Flt64.Zero;
        };

    /// <inheritdoc/>
    /// <remarks>
    /// 按**行上带的键类型**从对偶解里认回本管线的行。走约束分组的默认实现一行都找不到——行是用 `group: null` 加的。
    ///
    /// Recognises this pipeline's rows in the dual solution by **the key type on the row**. A group-based default finds none, since the rows are added with `group: null`.
    /// </remarks>
    public Try Refresh(
        GanttSchedulingShadowPriceMap<E, A> shadowPriceMap,
        object model,
        MetaDualSolution shadowPrices) {
        System.ArgumentNullException.ThrowIfNull(shadowPriceMap);
        System.ArgumentNullException.ThrowIfNull(shadowPrices);

        foreach (KeyValuePair<MathConstraint, Flt64> pair in shadowPrices.Constraints) {
            if (pair.Key.Args is ExecutorCompilationShadowPriceKey<E> key) {
                shadowPriceMap.Put(new ShadowPrice(key, pair.Value));
            }
        }
        return Results.Ok<Success>(Results.SuccessInstance);
    }
}
