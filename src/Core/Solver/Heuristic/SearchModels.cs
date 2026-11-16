// 搜索目标视图 / The search objective view.
// 与固定 Kotlin （1973 字节）对应。

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Callback;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Core.Solver.Heuristic;

/// <summary>
/// 以替换后的目标语义包装原模型，变量与约束仍来自原模型 / Wrap a source model with replaced objective semantics while the
/// variables and constraints still come from that source.
/// </summary>
/// <remarks>
/// **只替换目标，不替换变量与约束**。用途是"同一组变量与约束、换一种目标口径"（例如把加权和换成字典序）。若连变量表一起换，包装后的
/// 模型与原模型就是两个不同的问题，调用方却仍以为在解同一个。
/// **Only the objective is replaced; variables and constraints are not.** The purpose is "the same variables and constraints under a
/// different objective convention" (say, weighted sum to lexicographic). Replacing the token table too would make the wrapped model a
/// different problem while callers still believe it is the same one.
/// **`ObjectiveFunctions` 必须清空**。原模型的提取器写的是旧目标；留着它们会让外部按名字取到的目标函数与当前 `Objective` 不一致，
/// 而这种不一致不会报错，只会在对比两条结果时表现为"数字对不上"。
/// **`ObjectiveFunctions` must be emptied.** The source extractors describe the old objective; keeping them would let external code
/// fetch an objective function by name that disagrees with the current `Objective`. Nothing raises an error — the disagreement only
/// surfaces as "the numbers do not match" when comparing two results.
/// **`Operation` 返回左操作数**。新目标类型上不存在加法语义，包装器无法做有意义的聚合；返回左值让"取第一个"成为显式行为，而不是
/// 悄悄用原模型的加法算出一个混合值。
/// **`Operation` returns the left operand.** The new objective type has no addition semantics, so the wrapper cannot aggregate
/// meaningfully; returning the left operand makes "take the first" explicit instead of silently mixing values through the source
/// model's addition.
/// </remarks>
/// <typeparam name="TObj">原模型的目标符号类型 / The source model's objective-symbol type.</typeparam>
/// <typeparam name="TSourceValue">原模型的目标值类型 / The source model's objective-value type.</typeparam>
/// <typeparam name="O">视图的目标值类型 / The view's objective-value type.</typeparam>
/// <typeparam name="V">解值类型 / The solution-value type.</typeparam>
public sealed class ObjectiveModelView<TObj, TSourceValue, O, V> : IAbstractCallBackModelInterface<object?, O, V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    private readonly IAbstractCallBackModelInterface<TObj, TSourceValue, V> _source;
    private readonly Func<IReadOnlyList<V>, O?> _objective;
    private readonly Func<O?, O?, Order?> _compare;

    /// <summary>
    /// 构造目标视图 / Construct the objective view.
    /// </summary>
    /// <param name="source">原始模型 / The source model.</param>
    /// <param name="defaultObjective">默认目标 / The default objective.</param>
    /// <param name="objective">目标提取 / The objective extraction.</param>
    /// <param name="compare">目标比较 / The objective comparison.</param>
    public ObjectiveModelView(
        IAbstractCallBackModelInterface<TObj, TSourceValue, V> source,
        O defaultObjective,
        Func<IReadOnlyList<V>, O?> objective,
        Func<O?, O?, Order?> compare) {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(objective);
        ArgumentNullException.ThrowIfNull(compare);
        _source = source;
        DefaultObjective = defaultObjective;
        _objective = objective;
        _compare = compare;
    }

    /// <inheritdoc/>
    public O DefaultObjective { get; }

    /// <inheritdoc/>
    public ObjectCategory ObjectCategory => _source.ObjectCategory;

    /// <inheritdoc/>
    public Fuookami.Ospf.Core.Token.IAbstractMutableTokenTable<V> Tokens => _source.Tokens;

    /// <inheritdoc/>
    public IReadOnlyList<(Func<IReadOnlyList<V>, bool?> Extractor, string Name)> Constraints => _source.Constraints;

    /// <inheritdoc/>
    public IReadOnlyList<(Func<IReadOnlyList<V>, object?> Extractor, string Name)> ObjectiveFunctions =>
        Array.Empty<(Func<IReadOnlyList<V>, object?> Extractor, string Name)>();

    /// <inheritdoc/>
    public IReadOnlyList<IReadOnlyList<V>> InitialSolutions(ulong initialSolutionAmount = 1) =>
        _source.InitialSolutions(initialSolutionAmount);

    /// <inheritdoc/>
    public O Operation(O lhs, O rhs) => lhs;

    /// <inheritdoc/>
    public O ObjectiveValue() => DefaultObjective;

    /// <inheritdoc/>
    public O ObjectiveValue(object? obj) => DefaultObjective;

    /// <inheritdoc/>
    public O? Objective(IReadOnlyList<V> solution) => _objective(solution);

    /// <inheritdoc/>
    public Order? CompareObjective(O? lhs, O? rhs) => _compare(lhs, rhs);

    /// <inheritdoc/>
    public bool? ConstraintSatisfied(IReadOnlyList<V> solution) => _source.ConstraintSatisfied(solution);

    /// <inheritdoc/>
    public void Flush() => _source.Flush();

    /// <inheritdoc/>
    public void Dispose() => _source.Dispose();
}

/// <summary>
/// 搜索目标视图工厂 / Factories for search objective views.
/// </summary>
public static class SearchModels {
    /// <summary>
    /// 以替换后的目标语义包装原模型 / Wrap a source model with replaced objective semantics.
    /// </summary>
    /// <typeparam name="TObj">原模型的目标符号类型 / The source model's objective-symbol type.</typeparam>
    /// <typeparam name="TSourceValue">原模型的目标值类型 / The source model's objective-value type.</typeparam>
    /// <typeparam name="O">视图的目标值类型 / The view's objective-value type.</typeparam>
    /// <typeparam name="V">解值类型 / The solution-value type.</typeparam>
    /// <param name="source">原始模型 / The source model.</param>
    /// <param name="defaultObjective">默认目标 / The default objective.</param>
    /// <param name="objective">目标提取 / The objective extraction.</param>
    /// <param name="compare">目标比较 / The objective comparison.</param>
    /// <returns>目标视图 / The objective view.</returns>
    public static IAbstractCallBackModelInterface<object?, O, V> ObjectiveModel<TObj, TSourceValue, O, V>(
        IAbstractCallBackModelInterface<TObj, TSourceValue, V> source,
        O defaultObjective,
        Func<IReadOnlyList<V>, O?> objective,
        Func<O?, O?, Order?> compare) where V : struct, IRealNumber<V>, INumberField<V> =>
        new ObjectiveModelView<TObj, TSourceValue, O, V>(source, defaultObjective, objective, compare);
}
