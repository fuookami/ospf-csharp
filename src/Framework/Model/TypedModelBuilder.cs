#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Polynomial;

namespace Fuookami.Ospf.Framework.Model;

/// <summary>
/// 带类型的建模积木 / Typed modelling building blocks.
/// </summary>
/// <remarks>
/// 与基线 `TypedModelBuilder.kt` 对齐。**这一族存在的理由是"把建模动作变成值"**：
/// `Expr` / `Weight` 产出值，`Constraint` / `Objective` 产出值，最后由子类的 `Build` 一次性注册进模型。
/// 好处是同一套建模代码可以对不同后端产出不同的东西（LP 注册、校验、打印、导出）。
///
/// Aligned with the baseline's `TypedModelBuilder.kt`. **This family exists to turn modelling actions into values**: `Expr` and `Weight` produce values, `Constraint` and `Objective` produce values,
/// and a subclass's `Build` registers them into the model in one go, letting the same modelling code produce different things for different backends (LP registration, validation, printing,
/// export).
/// </remarks>
/// <param name="Coefficient">系数 / The coefficient.</param>
public sealed record TypedExpression(LinearPolynomial<Flt64> Coefficient);

/// <summary>
/// 带类型的约束 / A typed constraint.
/// </summary>
/// <param name="Coefficient">左边减右边的系数 / The coefficient as left minus right.</param>
/// <param name="Sign">比较符 / The comparison sign.</param>
/// <param name="Rhs">右端常数 / The right-hand constant.</param>
public sealed record TypedConstraint(LinearPolynomial<Flt64> Coefficient, Comparison Sign, Flt64 Rhs);

/// <summary>
/// 带类型的目标 / A typed objective.
/// </summary>
/// <param name="Coefficient">系数 / The coefficient.</param>
/// <param name="Type">目标类型 / The objective type.</param>
public sealed record TypedObjective(LinearPolynomial<Flt64> Coefficient, ObjectCategory Type);

/// <summary>
/// 带类型的权重 / A typed weight.
/// </summary>
/// <param name="Coefficient">系数 / The coefficient.</param>
public sealed record TypedTerm(LinearPolynomial<Flt64> Coefficient);

/// <summary>
/// 带类型的模型构造器 / A typed model builder.
/// </summary>
/// <typeparam name="T">子类的构建上下文 / The subclass's build context.</typeparam>
/// <typeparam name="R">构建结果 / The build result.</typeparam>
/// <remarks>
/// 三处照抄：
///
/// 1. **约束归一成"左边减右边 + 右端常数"**：两个表达式相减时右端固定为零，表达式对常数时右端就是常数——
///    这样下游只处理一种形状，不用再分情况。
/// 2. **模型可以为空块**：`Model` 不带委托时产出一个空模型，不是 null。
/// 3. **`Build` 是抽象方法**：本类只产出值，注册归子类——**父类不预设任何后端**。
///
/// Three things copied: **constraints normalise to "left minus right plus a right-hand constant"** — subtracting two expressions fixes the constant at zero, while an expression against a constant
/// takes that constant, so downstream handles one shape instead of cases; **the model may be built from a null block**, yielding an empty model rather than null; and **`Build` is abstract**,
/// this class only producing values while registration belongs to the subclass, **which presumes no backend**.
/// </remarks>
public abstract class TypedModelBuilder<T, R> {
    /// <summary>
    /// 造一个模型 / Build a model.
    /// </summary>
    /// <param name="block">可选初始化，null 表示空模型 / Optional initialisation, null meaning an empty model.</param>
    /// <returns>元模型 / The meta-model.</returns>
    public LinearMetaModel<Flt64> Model(Action<LinearMetaModel<Flt64>>? block = null) {
        var model = new LinearMetaModel<Flt64>();
        block?.Invoke(model);
        return model;
    }

    /// <summary>
    /// 造一个约束：表达式对 表达式 / A constraint of expression against expression.
    /// </summary>
    /// <param name="left">左边 / The left side.</param>
    /// <param name="sign">比较符 / The comparison sign.</param>
    /// <param name="right">右边 / The right side.</param>
    /// <returns>约束 / The constraint.</returns>
    /// <remarks>**相减后右端为零**：移项已经把常数吃进系数里。</remarks>
    /// <remarks>**The constant is zero after subtraction**: the transposition has already folded constants into the coefficient.</remarks>
    public TypedConstraint Constraint(TypedExpression left, Comparison sign, TypedExpression right) =>
        new(left.Coefficient - right.Coefficient, sign, new Flt64(0.0));

    /// <summary>
    /// 造一个约束：表达式对 常数 / A constraint of expression against a constant.
    /// </summary>
    /// <param name="left">左边 / The left side.</param>
    /// <param name="sign">比较符 / The comparison sign.</param>
    /// <param name="right">右端常数 / The right-hand constant.</param>
    /// <returns>约束 / The constraint.</returns>
    public TypedConstraint Constraint(TypedExpression left, Comparison sign, Flt64 right) =>
        new(left.Coefficient, sign, right);

    /// <summary>造一个目标 / Build an objective</summary>
    /// <param name="coefficient">系数 / The coefficient.</param>
    /// <param name="type">目标类型 / The objective type.</param>
    /// <returns>目标 / The objective.</returns>
    public TypedObjective Objective(LinearPolynomial<Flt64> coefficient, ObjectCategory type) =>
        new(coefficient, type);

    /// <summary>造一个表达式 / Build an expression</summary>
    /// <param name="coefficient">系数 / The coefficient.</param>
    /// <returns>表达式 / The expression.</returns>
    public TypedExpression Expr(LinearPolynomial<Flt64> coefficient) => new(coefficient);

    /// <summary>造一个权重 / Build a weight</summary>
    /// <param name="coefficient">系数 / The coefficient.</param>
    /// <returns>权重 / The weight.</returns>
    public TypedTerm Weight(LinearPolynomial<Flt64> coefficient) => new(coefficient);

    /// <summary>批量造权重 / Build weights in bulk</summary>
    /// <param name="coefficients">系数 / The coefficients.</param>
    /// <returns>权重列表 / The weight list.</returns>
    public IReadOnlyList<TypedTerm> Weights(params LinearPolynomial<Flt64>[] coefficients) {
        ArgumentNullException.ThrowIfNull(coefficients);
        var terms = new List<TypedTerm>(coefficients.Length);
        foreach (LinearPolynomial<Flt64> coefficient in coefficients) {
            terms.Add(new TypedTerm(coefficient));
        }
        return terms;
    }

    /// <summary>在一个已有模型上构建 / Build on an existing model</summary>
    /// <param name="model">模型 / The model.</param>
    /// <param name="block">构建上下文 / The build context.</param>
    /// <returns>构建结果 / The build result.</returns>
    public R WithModel(LinearMetaModel<Flt64> model, Action<T> block) {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(block);
        return Build(model, block);
    }

    /// <summary>
    /// 子类实现：把值注册进模型 / Implemented by subclasses: register the values into the model.
    /// </summary>
    /// <param name="model">模型 / The model.</param>
    /// <param name="block">构建上下文 / The build context.</param>
    /// <returns>构建结果 / The build result.</returns>
    public abstract R Build(LinearMetaModel<Flt64> model, Action<T> block);
}
