#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Plugin.Onnx.Graph;
using Fuookami.Ospf.Core.Plugin.Onnx.Proto;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;

namespace Fuookami.Ospf.Core.Plugin.Onnx.Lowering;

/// <summary>
/// **把一条纯仿射的算子链求值成多项式** / Evaluate a purely affine operator chain into a polynomial.
/// </summary>
/// <remarks>
/// 同步 Kotlin `dee897d2` 降阶层里"逐元素线性算子"那一部分（`Add`／`Sub`／`Neg`／`Identity`／`Cast`／`Mul` 常数）。
/// **它是唯一能端到端验证的部分：** **整条链都是仿射时，降阶结果就是一条多项式，可以直接与手算对照，也可以真的丢给求解器。**
///
/// **其余算子（激活、形状、归约）都必须先产生函数符号或改变形状，** **那一步需要模型机制，** **而把"能不能算"与"能不能挂进模型"混在一批里，
/// 会让前者永远得不到单独的证据。**
///
/// Syncs the elementwise-linear part of the lowering layer. **It is the only part that can be verified end to end: when the whole chain is affine the result is one
/// polynomial, directly comparable with hand arithmetic and directly solvable.**
///
/// **Every other operator either needs a function symbol or changes shape, which requires model machinery, and mixing "can it compute" with "can it be attached to a
/// model" in one batch would leave the former without evidence of its own.**
/// </remarks>
public static class OnnxAffineChain
{
    /// <summary>
    /// 求值结果 / The evaluation result.
    /// </summary>
    /// <param name="Polynomial">结果多项式 / The resulting polynomial.</param>
    /// <param name="Failure">失败原因 / The failure reason.</param>
    public sealed record Result(LinearPolynomial<Flt64>? Polynomial, string? Failure);

    /// <summary>支持的逐元素线性算子 / The supported elementwise linear operators.</summary>
    public static readonly IReadOnlySet<string> Operators = new HashSet<string>(StringComparer.Ordinal) {
        "Add", "Sub", "Neg", "Identity", "Cast", "Mul",
    };

    /// <summary>
    /// 求值一条仿射链 / Evaluate one affine chain.
    /// </summary>
    /// <param name="graph">图 / The graph.</param>
    /// <param name="variables">输入名到变量的映射 / The mapping from input names to variables.</param>
    /// <returns>求值结果 / The result.</returns>
    /// <remarks>
    /// **常量从初始化器里取，不认识的输入直接失败：** **把未知输入当成零会让整条链静默少一项，** **而少掉的那一项在解里看起来完全正常。**
    ///
    /// **Constants come from the initialisers and an unknown input fails outright: treating an unknown input as zero silently drops a term, and the answer without
    /// that term looks perfectly normal.**
    /// </remarks>
    public static Result Evaluate(OnnxGraph graph, IReadOnlyDictionary<string, RealVar> variables) {
        var constants = new Dictionary<string, Flt64>(StringComparer.Ordinal);
        foreach (OnnxTensor initializer in graph.Initializers) {
            if (initializer.Name is null) {
                continue;
            }
            if (initializer.Dims.Count != 0) {
                return new Result(null, "初始化器 '" + initializer.Name + "' 不是标量 / the initialiser is not a scalar");
            }
            if (initializer.DataType == 1 && initializer.FloatData.Count == 1) {
                constants[initializer.Name] = new Flt64(initializer.FloatData[0]);
            }
            else if (initializer.DataType == 11 && initializer.DoubleData.Count == 1) {
                constants[initializer.Name] = new Flt64(initializer.DoubleData[0]);
            }
            else if (initializer.Int64Data.Count == 1) {
                constants[initializer.Name] = new Flt64(initializer.Int64Data[0]);
            }
            else {
                return new Result(null, "初始化器 '" + initializer.Name + "' 的取值不在支持的标量类型内 / unsupported scalar initialiser");
            }
        }

        var values = new Dictionary<string, LinearPolynomial<Flt64>>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, RealVar> entry in variables) {
            values[entry.Key] = new LinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(Flt64.One, entry.Value) },
                Flt64.Zero);
        }

        foreach (OnnxNode node in graph.Nodes) {
            if (!Operators.Contains(node.OpType)) {
                return new Result(null, "算子 '" + node.OpType + "' 不在纯仿射链里 / the operator is not part of an affine chain");
            }
            var operands = new List<LinearPolynomial<Flt64>>(node.Inputs.Count);
            foreach (string input in node.Inputs) {
                if (input.Length == 0) {
                    continue;
                }
                if (values.TryGetValue(input, out LinearPolynomial<Flt64>? value)) {
                    operands.Add(value);
                    continue;
                }
                if (constants.TryGetValue(input, out Flt64 constant)) {
                    operands.Add(new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), constant));
                    continue;
                }
                return new Result(null, "节点输入 '" + input + "' 既不是已知变量也不是常量 / the input is neither a known variable nor a constant");
            }

            LinearPolynomial<Flt64>? result = node.OpType switch {
                "Add" => Add(operands),
                "Sub" => Subtract(operands),
                "Neg" => Negate(operands),
                "Identity" or "Cast" => operands.Count == 1 ? operands[0] : null,
                "Mul" => Multiply(operands),
                _ => null,
            };
            if (result is null) {
                return new Result(null, "算子 '" + node.OpType + "' 的输入个数不合法 / the operator has an illegal number of inputs");
            }
            if (node.Outputs.Count != 1) {
                return new Result(null, "节点 '" + node.Name + "' 的输出不是单个 / the node does not produce exactly one output");
            }
            values[node.Outputs[0]] = result;
        }

        if (graph.Outputs.Count != 1) {
            return new Result(null, "仿射链需要恰好一个图输出 / an affine chain needs exactly one graph output");
        }
        if (!values.TryGetValue(graph.Outputs[0].Name, out LinearPolynomial<Flt64>? output)) {
            return new Result(null, "图输出 '" + graph.Outputs[0].Name + "' 未被求值 / the graph output was never computed");
        }
        return new Result(Normalise(output), null);
    }

    /// <summary>
    /// 合并同类项 / Merge like terms.
    /// </summary>
    /// <remarks>
    /// **`x + x` 在这里会得到两个同符号的单项式，而下游（约束展平、对偶解释）默认"每个符号只出现一次"。** '
    /// **不合并会让系数被算两次或漏算一次，而两种错都只在数值上看得见。**
    ///
    /// **`x + x` yields two monomials over the same symbol here, while everything downstream (constraint flattening, dual interpretation) assumes each symbol appears
    /// once. Without merging the coefficient is counted twice or missed once, and both errors only show at the numeric level.**
    /// </remarks>
    private static LinearPolynomial<Flt64> Normalise(LinearPolynomial<Flt64> input) {
        var merged = new List<LinearMonomial<Flt64>>(input.Monomials.Count);
        var position = new Dictionary<ISymbol, int>();
        foreach (LinearMonomial<Flt64> monomial in input.Monomials) {
            if (position.TryGetValue(monomial.Symbol, out int index)) {
                merged[index] = new LinearMonomial<Flt64>(merged[index].Coefficient + monomial.Coefficient, merged[index].Symbol);
            }
            else {
                position[monomial.Symbol] = merged.Count;
                merged.Add(monomial);
            }
        }
        merged.RemoveAll(monomial => monomial.Coefficient.Eq(Flt64.Zero));
        return new LinearPolynomial<Flt64>(merged, input.Constant);
    }

    private static LinearPolynomial<Flt64>? Add(IReadOnlyList<LinearPolynomial<Flt64>> operands) {
        if (operands.Count == 0) {
            return null;
        }
        LinearPolynomial<Flt64> sum = operands[0];
        for (int index = 1; index < operands.Count; ++index) {
            sum = sum + operands[index];
        }
        return sum;
    }

    private static LinearPolynomial<Flt64>? Subtract(IReadOnlyList<LinearPolynomial<Flt64>> operands) =>
        operands.Count == 2 ? operands[0] - operands[1] : null;

    private static LinearPolynomial<Flt64>? Negate(IReadOnlyList<LinearPolynomial<Flt64>> operands) =>
        operands.Count == 1 ? -operands[0] : null;

    /// <summary>
    /// `Mul` 只支持一个操作数是常量 / `Mul` accepts only one constant operand.
    /// </summary>
    /// <remarks>
    /// **两个非常量相乘就是二次，而这条链只表达仿射：** **在这里"尽量算一下"会让二次项被静默丢掉，** **而模型对着一条少了一项的表达式优化出"最优解"。**
    ///
    /// **Two non-constant factors would be a quadratic, and this chain only expresses affine forms: trying anyway would silently drop the quadratic terms, and the model
    /// would optimise to an "optimum" of an expression missing them.**
    /// </remarks>
    private static LinearPolynomial<Flt64>? Multiply(IReadOnlyList<LinearPolynomial<Flt64>> operands) {
        if (operands.Count != 2) {
            return null;
        }
        bool leftConstant = operands[0].Monomials.Count == 0;
        bool rightConstant = operands[1].Monomials.Count == 0;
        if (leftConstant && rightConstant) {
            return new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), operands[0].Constant * operands[1].Constant);
        }
        if (leftConstant) {
            return Scale(operands[1], operands[0].Constant);
        }
        if (rightConstant) {
            return Scale(operands[0], operands[1].Constant);
        }
        return null;
    }

    private static LinearPolynomial<Flt64> Scale(LinearPolynomial<Flt64> input, Flt64 factor) {
        var scaled = new List<LinearMonomial<Flt64>>(input.Monomials.Count);
        foreach (LinearMonomial<Flt64> monomial in input.Monomials) {
            scaled.Add(new LinearMonomial<Flt64>(monomial.Coefficient * factor, monomial.Symbol));
        }
        return new LinearPolynomial<Flt64>(scaled, input.Constant * factor);
    }
}