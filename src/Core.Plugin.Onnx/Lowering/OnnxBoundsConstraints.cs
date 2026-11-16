#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Plugin.Onnx.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;

namespace Fuookami.Ospf.Core.Plugin.Onnx.Lowering;

/// <summary>
/// **把输入界变成模型里的约束** / Turn input bounds into constraints the model can hold.
/// </summary>
/// <remarks>
/// 里"导入选项的界 → 模型里的界"这一步。**为什么是约束而不是变量属性：**
/// **C# 侧变量的 `Range` 由类型常量注册表给出（`Continuous.Instance` + `NumericConstantsRegistry`），** **逐变量的界没有设置入口；**
/// **而在模型里表达"这个变量在 `[a, b]` 内"的常规做法就是两条不等式。**
///
/// **这不是替代方案而是主路径：** **求解器最终看到的就是约束，** **变量属性只是另一条更早生效的同义路径。**
///
/// Syncs the "options bounds → model bounds" step. **Why constraints rather than a variable attribute: a C# variable's `Range` comes from the type's constants
/// registry, with no per-variable setter, and the ordinary way to say "this variable lies in [a, b]" inside a model is two inequalities.**
///
/// **This is the main path rather than a fallback: what the solver ultimately sees are constraints, and a variable attribute is only an earlier-acting synonym.**
/// </remarks>
public static class OnnxBoundsConstraints
{
    /// <summary>
    /// 生成结果 / The generated constraints.
    /// </summary>
    /// <param name="Constraints">界约束 / The bound constraints.</param>
    /// <param name="Failure">失败原因 / The failure reason.</param>
    public sealed record Generated(IReadOnlyList<LinearInequality<Flt64>> Constraints, string? Failure);

    /// <summary>
    /// 为一个变量生成上下界约束 / Generate the lower and upper bound for one variable.
    /// </summary>
    /// <param name="variable">变量 / The variable.</param>
    /// <param name="bounds">区间 / The range.</param>
    /// <param name="name">约束名前缀 / The constraint-name prefix.</param>
    /// <returns>生成结果 / The result.</returns>
    /// <remarks>
    /// **区间是闭的：** **`[a, b]` 生成 `x &gt;= a` 与 `x &lt;= b`，不是严格形式。** **开闭写错在紧解上会给出不同的最优值——**
    /// **而模型照常求解、照常"成功"。**
    ///
    /// **The interval is closed: `[a, b]` yields `x &gt;= a` and `x &lt;= b`, not the strict forms.** **Getting that wrong changes the optimum on tight solutions, while the
    /// model still solves and still reports success.**
    /// </remarks>
    public static Generated For(ISymbol variable, OnnxBounds bounds, string name = "bound") {
        if (!bounds.Validate(out string? failure)) {
            return new Generated(Array.Empty<LinearInequality<Flt64>>(), failure);
        }
        var lhs = new LinearPolynomial<Flt64>(
            new[] { new LinearMonomial<Flt64>(Flt64.One, variable) },
            Flt64.Zero);

        return new Generated(
            new[] {
                new LinearInequality<Flt64>(
                    lhs,
                    new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), new Flt64(bounds.Lower)),
                    Comparison.GE,
                    Name: name + "__lower"),
                new LinearInequality<Flt64>(
                    lhs,
                    new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), new Flt64(bounds.Upper)),
                    Comparison.LE,
                    Name: name + "__upper"),
            },
            null);
    }

    /// <summary>
    /// 为一组变量生成界约束 / Generate bounds for a set of variables.
    /// </summary>
    /// <param name="variables">变量名到符号的映射 / The mapping from names to symbols.</param>
    /// <param name="bounds">变量名到区间的映射 / The mapping from names to ranges.</param>
    /// <param name="missing">有变量但没界的名字 / The names with a variable but no range.</param>
    /// <returns>生成结果 / The result.</returns>
    /// <remarks>
    /// **有变量没界时不静默跳过：** **跳过等于让那个变量无界，** **而求解器会把它推到某个端点，解里出现一个没人预期的极值——**
    /// **却看不出是因为少了一条约束。**
    ///
    /// **A variable without a range is not silently skipped: skipping leaves it unbounded, the solver pushes it to some extreme, and the answer carries an unexpected
    /// value with nothing to show a constraint went missing.**
    /// </remarks>
    public static Generated ForAll(
        IReadOnlyDictionary<string, ISymbol> variables,
        IReadOnlyDictionary<string, OnnxBounds> bounds,
        out IReadOnlyList<string> missing) {
        var constraints = new List<LinearInequality<Flt64>>();
        var absent = new List<string>();

        foreach (KeyValuePair<string, ISymbol> entry in variables) {
            if (!bounds.TryGetValue(entry.Key, out OnnxBounds? range)) {
                absent.Add(entry.Key);
                continue;
            }
            Generated one = For(entry.Value, range, "bound_" + entry.Key);
            if (one.Failure is not null) {
                missing = absent;
                return new Generated(constraints, one.Failure);
            }
            constraints.AddRange(one.Constraints);
        }

        missing = absent;
        return new Generated(constraints, null);
    }
}