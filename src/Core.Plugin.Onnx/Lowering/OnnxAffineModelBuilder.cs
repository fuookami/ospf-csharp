#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Plugin.Onnx.Model;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;

namespace Fuookami.Ospf.Core.Plugin.Onnx.Lowering;

/// <summary>
/// **把一条仿射多项式装成一个可求解的线性模型** / Assemble an affine polynomial into a solvable linear model.
/// </summary>
/// <remarks>
/// 同步 Kotlin `dee897d2` 的 `OnnxFunctionalModel` 里"仿射目标 → 线性模型"那一小段。**这一层的产出是"能力可用"的第一份硬证据：**
/// **一个 ONNX 图经这里之后，真的能被求解器解出来，并与手算对照。**
///
/// **覆盖范围明确有限：** **只有仿射链。** **激活与形状算子需要函数符号与模型机制，** **不在这一层里，也不假装在。**
///
/// Syncs the "affine objective → linear model" fragment of `OnnxFunctionalModel`. **The output is the first hard evidence that the capability works: a graph
/// passing through here is actually solved and compared against hand arithmetic.**
///
/// **The coverage is deliberately narrow: affine chains only. Activations and shape operators need function symbols and model machinery, are not in, and are
/// not pretended to be.**
/// </remarks>
public static class OnnxAffineModelBuilder
{
    /// <summary>
    /// 组装结果 / The assembly result.
    /// </summary>
    /// <param name="Model">线性模型 / The linear model.</param>
    /// <param name="Failure">失败原因 / The failure reason.</param>
    public sealed record Built(LinearTriadModel? Model, string? Failure);

    /// <summary>
    /// 组装 / Assemble.
    /// </summary>
    /// <param name="polynomial">目标多项式 / The objective polynomial.</param>
    /// <param name="bounds">变量名到区间的映射 / The mapping from variable names to ranges.</param>
    /// <param name="category">优化方向 / The optimisation direction.</param>
    /// <param name="name">模型名 / The model name.</param>
    /// <returns>组装结果 / The result.</returns>
    /// <remarks>
    /// **多项式里出现的每个符号都必须有区间，缺一个就失败：** **给缺失的符号补一个 ±∞ 的界会让模型"可解"——**
    /// **而那个变量会跑到无界方向，解里出现一个没人预期的极值。**
    ///
    /// **Every symbol appearing in the polynomial needs a range, and one missing fails outright: filling a missing symbol with ±infinity makes the model "solvable" while
    /// that variable runs off to infinity, putting an unexpected extreme in the answer.**
    /// </remarks>
    public static Built Build(
        LinearPolynomial<Flt64> polynomial,
        IReadOnlyDictionary<string, OnnxBounds> bounds,
        ObjectCategory category = ObjectCategory.Minimum,
        string name = "onnx-affine") {
        var order = new List<string>();
        var indexOf = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (LinearMonomial<Flt64> monomial in polynomial.Monomials) {
            string key = monomial.Symbol.Name;
            if (indexOf.ContainsKey(key)) {
                continue;
            }
            if (!bounds.TryGetValue(key, out OnnxBounds? range)) {
                return new Built(null, "变量 '" + key + "' 缺少区间 / the variable has no range");
            }
            if (!range.Validate(out string? boundFailure)) {
                return new Built(null, "变量 '" + key + "' 的" + boundFailure);
            }
            indexOf[key] = order.Count;
            order.Add(key);
        }

        var variables = new List<ModelViewVariable>(order.Count);
        for (int index = 0; index < order.Count; ++index) {
            OnnxBounds range = bounds[order[index]];
            variables.Add(new ModelViewVariable(
                index: index,
                lowerBound: new Flt64(range.Lower),
                upperBound: new Flt64(range.Upper),
                type: Continuous.Instance,
                name: order[index]));
        }

        var cells = new List<LinearObjectiveCell>(polynomial.Monomials.Count);
        foreach (LinearMonomial<Flt64> monomial in polynomial.Monomials) {
            cells.Add(new LinearObjectiveCell(indexOf[monomial.Symbol.Name], monomial.Coefficient));
        }
        if (cells.Count == 0) {
            return new Built(null, "目标多项式没有任何变量项 / the objective has no variable term to optimise");
        }

        // 常数项不参与优化，但必须被调用方看到：最优值是"变量部分的最优"加常数。
        // The constant takes no part in the optimisation but the caller has to see it: the reported value is the optimal variable part plus the constant.
        var model = new LinearTriadModel(
            variables: variables,
            constraints: new LinearConstraintBatch(
                lhs: new SparseMatrix(Array.Empty<SparseVector>()),
                signs: Array.Empty<ConstraintRelation>(),
                rhs: Array.Empty<Flt64>(),
                names: Array.Empty<string>(),
                sources: Array.Empty<ConstraintSource>()),
            objective: new Objective<LinearObjectiveCell>(category, cells),
            name: name);
        return new Built(model, null);
    }
}