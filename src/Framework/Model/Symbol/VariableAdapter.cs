#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Concept;

namespace Fuookami.Ospf.Framework.Model.Symbol;

/// <summary>
/// 变量适配器：把一个变量"外面看到的那只"与"里面真正的那只"绑在一起 / A variable adapter binding the variable seen outside to the one actually inside.
/// </summary>
/// <typeparam name="T">数值类型 / The numeric type.</typeparam>
/// <typeparam name="TType">变量类型 / The variable type.</typeparam>
/// <remarks>
/// **这是"别名"的载体**：外层变量与内层变量是同一决策的两个门面，
/// 求解器只认一个，建模者按另一面写约束。
///
/// **It carries an alias**: the outer and inner variables are two faces of the same decision, the solver honouring one while the modeller writes
/// constraints against the other.
/// </remarks>
public sealed record VariableAdapter<T, TType>
    where T : struct, IRealNumber<T>, INumberField<T>
    where TType : VariableType<T> {
    /// <summary>
    /// 构造 / Construct.
    /// </summary>
    /// <param name="inner">内层变量 / The inner variable.</param>
    /// <param name="outer">外层变量 / The outer variable.</param>
    public VariableAdapter(AbstractVariableItem<T, TType> inner, AbstractVariableItem<T, TType> outer) {
        Inner = inner ?? throw new ArgumentNullException(nameof(inner));
        Outer = outer ?? throw new ArgumentNullException(nameof(outer));
    }

    /// <summary>内层变量 / The inner variable</summary>
    public AbstractVariableItem<T, TType> Inner { get; }

    /// <summary>外层变量 / The outer variable</summary>
    public AbstractVariableItem<T, TType> Outer { get; }

    /// <summary>把内层变量包成"内层视角" / Wrap the inner variable as the inner view</summary>
    /// <returns>内层变量视图 / The inner variable view.</returns>
    public InternalVariable InnerVariable() => new(Inner, this);

    /// <summary>把外层变量包成"外层视角" / Wrap the outer variable as the outer view</summary>
    /// <returns>外层变量视图 / The outer variable view.</returns>
    public AdapterVariable OuterVariable() => new(Outer, this);

    /// <summary>
    /// 内层视角：变量及其所属适配器 / The inner view: a variable with its adapter.
    /// </summary>
    /// <param name="Variable">变量 / The variable.</param>
    /// <param name="Adapter">适配器 / The adapter.</param>
    public sealed record InternalVariable(AbstractVariableItem<T, TType> Variable, VariableAdapter<T, TType> Adapter);

    /// <summary>
    /// 外层视角：变量及其所属适配器 / The outer view: a variable with its adapter.
    /// </summary>
    /// <param name="Variable">变量 / The variable.</param>
    /// <param name="Adapter">适配器 / The adapter.</param>
    public sealed record AdapterVariable(AbstractVariableItem<T, TType> Variable, VariableAdapter<T, TType> Adapter);
}

/// <summary>
/// 找出某个变量所属的适配器 / Find the adapter a variable belongs to.
/// </summary>
/// <remarks>
/// **按引用相等判定，不按值相等**：`===` 是引用相等。这一条是全部意义所在——
/// `AbstractVariableItem` 的值相等可能对两只不同变量成立（同类型同上下界），
/// 用值相等会把"别人的别名"认成自己的。
///
/// **Identity, not value equality**: its `===` compares references. That is the whole point: two distinct variable items can be value-equal (same type, same bounds), and value equality
/// would mistake someone else's alias for one's own.
/// </remarks>
public static class VariableAdapterLookup {
    /// <summary>
    /// 在若干适配器里找"这个变量参与的那一个" / Find the adapter this variable takes part in, among several.
    /// </summary>
    /// <typeparam name="T">数值类型 / The numeric type.</typeparam>
    /// <typeparam name="TType">变量类型 / The variable type.</typeparam>
    /// <param name="variable">要查的变量 / The variable to look up.</param>
    /// <param name="adapters">候选适配器 / The candidate adapters.</param>
    /// <returns>所属适配器，没有则 null / The owning adapter, or null.</returns>
    public static VariableAdapter<T, TType>? Adapter<T, TType>(
        this AbstractVariableItem<T, TType> variable,
        params VariableAdapter<T, TType>[] adapters)
        where T : struct, IRealNumber<T>, INumberField<T>
        where TType : VariableType<T> {
        ArgumentNullException.ThrowIfNull(variable);
        ArgumentNullException.ThrowIfNull(adapters);
        foreach (VariableAdapter<T, TType> adapter in adapters) {
            if (ReferenceEquals(adapter.Inner, variable) || ReferenceEquals(adapter.Outer, variable)) {
                return adapter;
            }
        }
        return null;
    }

    /// <summary>重载：接受列表 / Overload taking a list</summary>
    /// <typeparam name="T">数值类型 / The numeric type.</typeparam>
    /// <typeparam name="TType">变量类型 / The variable type.</typeparam>
    /// <param name="variable">要查的变量 / The variable to look up.</param>
    /// <param name="adapters">候选适配器 / The candidate adapters.</param>
    /// <returns>所属适配器，没有则 null / The owning adapter, or null.</returns>
    public static VariableAdapter<T, TType>? Adapter<T, TType>(
        this AbstractVariableItem<T, TType> variable,
        IReadOnlyList<VariableAdapter<T, TType>> adapters)
        where T : struct, IRealNumber<T>, INumberField<T>
        where TType : VariableType<T> {
        ArgumentNullException.ThrowIfNull(variable);
        ArgumentNullException.ThrowIfNull(adapters);
        foreach (VariableAdapter<T, TType> adapter in adapters) {
            if (ReferenceEquals(adapter.Inner, variable) || ReferenceEquals(adapter.Outer, variable)) {
                return adapter;
            }
        }
        return null;
    }
}
