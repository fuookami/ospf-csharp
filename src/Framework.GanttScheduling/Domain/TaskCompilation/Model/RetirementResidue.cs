// 回收后的残留引用 / Residual references after a variable is retired.
// `remove(item)` 只有一行 `tokens.remove(item)`：
// `remove` **只摘变量**，不动约束与目标项。
// `remove` **drops the variable only**, leaving constraints and objective terms alone.

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model;

/// <summary>
/// 回收后仍被引用的情况 / What still refers to a retired variable.
/// </summary>
/// <remarks>
/// `remove(item)` 只有一行 `tokens.remove(item)`。于是回收一个列变量之后：
///
/// | 残留 | 后果 |
/// | --- | --- |
/// | 以它为系数的**约束**仍在 | 约束引用了一个已不在符号表里的变量 |
/// | 以它为系数的**目标项**仍在 | 目标对着一个已不存在的变量定价 |
///
/// 这两处残留**不会报错**：变量已经被固定为 0，所以约束仍可满足、目标项恒为 0。
/// 也就是说**行为是对的，而模型内容是脏的**——脏到任何"按符号表遍历模型"的下游
/// （导出、审计、二次求解）都会看到悬空引用。
///
/// 照做就**不能**顺手清理：`removeColumns` 不调 `RemoveObjectiveTerms`，
/// 加了会改变解的行为（乃至求解器看到的模型）。所以这里提供的是**观测**，
/// 不是修复——让这份偏离被看见、被断言，而不是变成一条静默的 ceiling。
///
/// `remove(item)` is one line: `tokens.remove(item)`. So after a column variable is retired:
///
/// | Residue | Consequence |
/// | --- | --- |
/// | **constraints** using it as a coefficient remain | a constraint refers to a variable no longer in the symbol table |
/// | **objective terms** using it remain | the objective prices a variable that no longer exists |
///
/// Neither raises anything: the variable is already pinned to 0, so the constraints stay satisfiable and the objective terms are identically 0. In other words **the behaviour is
/// right while the model content is dirty** — dirty enough that any downstream walking the model by symbol table (export, audit, re-solve) sees a dangling reference.
///
/// Cleaning this up on the side is **not** an option: `removeColumns` does not call `RemoveObjectiveTerms`, and adding it would move the
/// solution behaviour (and the model the solver sees). So what is offered here is **observation, not repair** — so the divergence is seen and
/// asserted instead of turning into a silent ceiling.
/// </remarks>
public static class RetirementResidue {
    /// <summary>
    /// 列出仍引用该变量的约束名 / List the constraints that still refer to the variable.
    /// </summary>
    /// <param name="model">元模型 / The meta model.</param>
    /// <param name="variable">已回收的变量 / The retired variable.</param>
    /// <returns>约束名列表 / The constraint names.</returns>
    public static IReadOnlyList<string> ConstraintsReferencing(
        IAbstractLinearMetaModel<Flt64> model,
        IVariableItem variable) {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(variable);

        var names = new List<string>();
        foreach (LinearInequalityConstraint<Flt64> constraint in model.Constraints.OfType<LinearInequalityConstraint<Flt64>>()) {
            if (Mentions(constraint, variable)) {
                names.Add(constraint.Name);
            }
        }
        return names;
    }

    /// <summary>
    /// 目标项里是否仍引用该变量 / Whether the objective still refers to the variable.
    /// </summary>
    /// <param name="model">元模型 / The meta model.</param>
    /// <param name="variable">已回收的变量 / The retired variable.</param>
    /// <returns>是否仍被引用 / Whether it is still referenced.</returns>
    public static bool ObjectiveReferences(LinearMetaModel<Flt64> model, IVariableItem variable) {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(variable);

        return model.MetaSubObjects.Any(subObject =>
            subObject.Polynomial.Monomials.Any(monomial =>
                monomial.Symbol is IVariableItem symbol && symbol.Equals(variable)));
    }

    /// <summary>约束的任一侧是否提到该变量 / Whether either side of the constraint mentions the variable.</summary>
    /// <param name="constraint">约束 / The constraint.</param>
    /// <param name="variable">变量 / The variable.</param>
    /// <returns>是否提到 / Whether it mentions it.</returns>
    private static bool Mentions(LinearInequalityConstraint<Flt64> constraint, IVariableItem variable) =>
        MentionsAny(constraint.Inequality.Lhs, variable)
        || MentionsAny(constraint.Inequality.Rhs, variable);

    /// <summary>多项式里是否提到该变量 / Whether the polynomial mentions the variable.</summary>
    /// <param name="polynomial">多项式 / The polynomial.</param>
    /// <param name="variable">变量 / The variable.</param>
    /// <returns>是否提到 / Whether it mentions it.</returns>
    private static bool MentionsAny(LinearPolynomial<Flt64> polynomial, IVariableItem variable) =>
        polynomial.Monomials.Any(monomial =>
            monomial.Symbol is IVariableItem symbol && symbol.Equals(variable));
}
