#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Plugin.Onnx.Proto;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;

namespace Fuookami.Ospf.Core.Plugin.Onnx.Lowering;

/// <summary>
/// **在一个作用域里逐节点求值一张图** / Evaluate a graph node by node inside a scope.
/// </summary>
/// <remarks>
/// 同步 Kotlin `dee897d2` 的子图体递归降阶的第一刀。**这一批只覆盖"逐元素仿射"那几个算子（`Identity`／`Cast`／`Add`／`Sub`／`Neg`／`Mul` 常数），**
/// **但它引入了前面所有批次都没有的东西：<b>图是被逐节点走过去的，中间结果留在上下文里</b>。**
///
/// **这就是子图展开能成立的形式：** **`If` 选中一个分支体之后，把外层可见的张量作为种子灌进上下文，** **再对分支体调用这里——** **于是"子图"不再是一个特殊概念。**
///
/// Syncs the first cut of the recursive body lowering. **It covers only the elementwise affine operators, but it brings something no earlier batch had: the graph is
/// walked node by node and the intermediates stay in the context.**
///
/// **That is the shape subgraph unrolling needs: after `If` picks a branch body, the visible outer tensors seed the context and this runs over the body — so "a subgraph"
/// stops being a special concept.**
/// </remarks>
public static class OnnxGraphEvaluator
{
    /// <summary>本批支持的算子 / The operators this batch supports.</summary>
    public static readonly IReadOnlySet<string> Supported = new HashSet<string>(StringComparer.Ordinal) {
        "Identity", "Cast", "Add", "Sub", "Neg", "Mul",
        // 激活算子只在输入是常量时折叠：符号输入需要函数符号（分段线性 / 正部），那要挂进模型，
        // 不是这一层能做的。把这条边界写成显式的拒绝，比让它悄悄返回输入安全得多。
        //
        // Activations fold only when the input is constant: a symbolic input needs a function symbol (piecewise / positive part) that has to be attached to a model,
        // which this layer cannot do. Stating that boundary as an explicit refusal is far safer than quietly returning the input.
        "Relu", "Clip", "LeakyRelu", "HardSigmoid", "Sigmoid", "Tanh", "Softplus", "Elu",
        // 控制流：`If` 已接进求值路径（递归走分支体），`Loop`／`Scan` 需要种子回写与切片，由调用方按需驱动。
        // Control flow: If is wired into evaluation (it recursively walks the chosen body); Loop and Scan need seed write-back and slices, which the caller drives.
        "If", "Loop", "Scan",
    };

    /// <summary>
    /// 求值结果 / The evaluation result.
    /// </summary>
    /// <param name="Outputs">图输出名到绑定的映射 / The mapping from graph output names to bindings.</param>
    /// <param name="Failure">失败原因 / The failure reason.</param>
    /// <param name="NodeName">出错的节点 / The offending node.</param>
    public sealed record Result(
        IReadOnlyDictionary<string, OnnxLoweringContext.Binding>? Outputs,
        string? Failure,
        string? NodeName = null)
    {
        /// <summary>是否成功 / Whether it succeeded.</summary>
        public bool IsOk => Failure is null;
    }

    /// <summary>
    /// 在一个上下文里求值一张图 / Evaluate one graph inside a context.
    /// </summary>
    /// <param name="graph">图 / The graph.</param>
    /// <param name="context">上下文（提供外层可见的张量）/ The context supplying the enclosing tensors.</param>
    /// <param name="constants">常量名到标量值的映射 / The mapping from constant names to scalar values.</param>
    /// <param name="constantArrays">常量名到数组值的映射（`Scan` 用）/ The mapping from constant names to arrays, used by Scan.</param>
    /// <returns>求值结果 / The result.</returns>
    /// <remarks>
    /// **中间结果留在上下文里，且由调用方负责作用域：** **这里既不看也不动 `PushScope`／`PopScope`，** **于是"分支体的中间结果不外泄"这件事只由一个地方保证，**
    /// **而不是两处各保证一半。**
    ///
    /// **Intermediates stay in the context and the caller owns the scope: this neither pushes nor pops, so "a body's intermediates do not escape" is guaranteed in one
    /// place rather than half-guaranteed in two.**
    /// </remarks>
    public static Result Evaluate(
        OnnxGraph graph,
        OnnxLoweringContext context,
        IReadOnlyDictionary<string, double>? constants = null,
        IReadOnlyDictionary<string, IReadOnlyList<double>>? constantArrays = null) {
        foreach (OnnxNode node in graph.Nodes) {
            if (node.OpType == "If") {
                string? ifFailure = EvaluateIf(node, context, constants);
                if (ifFailure is not null) {
                    return new Result(null, ifFailure, node.Name);
                }
                continue;
            }
            if (node.OpType == "Scan") {
                IReadOnlyList<double>? slices = null;
                if (node.Inputs.Count > 0 && constantArrays is not null) {
                    constantArrays.TryGetValue(node.Inputs[^1], out slices);
                }
                string? scanFailure = EvaluateScan(node, context, constants, slices);
                if (scanFailure is not null) {
                    return new Result(null, scanFailure, node.Name);
                }
                continue;
            }
            if (node.OpType == "Loop") {
                string? loopFailure = EvaluateLoop(node, context, constants);
                if (loopFailure is not null) {
                    return new Result(null, loopFailure, node.Name);
                }
                continue;
            }
            if (!Supported.Contains(node.OpType)) {
                return new Result(null, "算子 '" + node.OpType + "' 尚未接入逐节点求值 / the operator is not yet part of node-by-node evaluation", node.Name);
            }

            var operands = new List<OnnxLoweringContext.Binding>(node.Inputs.Count);
            foreach (string input in node.Inputs) {
                if (input.Length == 0) {
                    continue;
                }
                if (context.TryGet(input, out OnnxLoweringContext.Binding binding)) {
                    operands.Add(binding);
                    continue;
                }
                if (constants is not null && constants.TryGetValue(input, out double value)) {
                    operands.Add(new OnnxLoweringContext.Binding(
                        new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), new Flt64(value)),
                        Array.Empty<int>()));
                    continue;
                }
                return new Result(null, "输入 '" + input + "' 在上下文里找不到 / the input is not in the context", node.Name);
            }

            if (node.Outputs.Count != 1) {
                return new Result(null, "节点输出不是单个 / the node does not produce exactly one output", node.Name);
            }

            OnnxLoweringContext.Binding? produced = Compute(node.OpType, operands, out string? computeFailure);
            if (produced is null) {
                return new Result(null, computeFailure, node.Name);
            }
            if (!context.TryRebind(node.Outputs[0], produced.Expression, produced.Shape, out string? bindFailure)) {
                return new Result(null, bindFailure, node.Name);
            }
        }

        var outputs = new Dictionary<string, OnnxLoweringContext.Binding>(StringComparer.Ordinal);
        foreach (OnnxValueInfo output in graph.Outputs) {
            if (!context.TryGet(output.Name, out OnnxLoweringContext.Binding binding)) {
                return new Result(null, "图输出 '" + output.Name + "' 没有被任何节点产出 / the graph output is not produced by anything");
            }
            outputs[output.Name] = binding;
        }
        return new Result(outputs, null);
    }

    private static OnnxLoweringContext.Binding? Compute(
        string opType,
        IReadOnlyList<OnnxLoweringContext.Binding> operands,
        out string? failure) {
        switch (opType) {
            case "Identity":
            case "Cast": {
                // Cast 在这里只做透传：本批的表达式都是 Flt64，转成别的类型不在范围内。
                // Cast passes through here: this batch's expressions are all Flt64, and converting to another type is out of scope.
                if (operands.Count != 1) {
                    failure = "'" + opType + "' 需要恰好一个输入 / the operator needs exactly one input";
                    return null;
                }
                failure = null;
                return operands[0];
            }
            case "Neg": {
                if (operands.Count != 1) {
                    failure = "'Neg' 需要恰好一个输入 / the operator needs exactly one input";
                    return null;
                }
                failure = null;
                return new OnnxLoweringContext.Binding(-operands[0].Expression, operands[0].Shape);
            }
            case "Add":
            case "Sub": {
                if (operands.Count != 2) {
                    failure = "'" + opType + "' 需要恰好两个输入 / the operator needs exactly two inputs";
                    return null;
                }
                int[]? shape = OnnxShape.BroadcastShape(operands[0].Shape, operands[1].Shape, out string? shapeFailure);
                if (shape is null) {
                    failure = shapeFailure;
                    return null;
                }
                failure = null;
                // 必须合并同名单项式：`x + x` 若留成两个单项式，下游会把同一个变量算两遍——
                // 求解器看到的是"两个不同的列"，而它给出的最优值对应的是一个变量被拆成两半的模型。
                //
                // Like terms have to be merged: leaving `x + x` as two monomials makes the downstream count the same variable twice — the solver sees "two different
                // columns" and its optimum belongs to a model where one variable was split in half.
                return new OnnxLoweringContext.Binding(
                    Normalise(
                        opType == "Add"
                            ? Concat(operands[0].Expression, operands[1].Expression)
                            : Subtract(operands[0].Expression, operands[1].Expression),
                        opType == "Add"
                            ? operands[0].Expression.Constant + operands[1].Expression.Constant
                            : operands[0].Expression.Constant - operands[1].Expression.Constant),
                    shape);
            }
            case "Mul": {
                if (operands.Count != 2) {
                    failure = "'Mul' 需要恰好两个输入 / the operator needs exactly two inputs";
                    return null;
                }
                bool leftConstant = operands[0].Expression.Monomials.Count == 0;
                bool rightConstant = operands[1].Expression.Monomials.Count == 0;
                if (!leftConstant && !rightConstant) {
                    failure = "'Mul' 的两个输入都不是常量，那是二次 / neither Mul input is constant, which would be quadratic";
                    return null;
                }
                OnnxLoweringContext.Binding scaled = leftConstant
                    ? Scale(operands[1], operands[0].Expression.Constant)
                    : Scale(operands[0], operands[1].Expression.Constant);
                failure = null;
                return scaled;
            }
            case "Relu":
            case "Clip":
            case "LeakyRelu":
            case "HardSigmoid":
            case "Sigmoid":
            case "Tanh":
            case "Softplus":
            case "Elu":
                return FoldActivation(opType, operands, out failure);
            default:
                failure = "算子 '" + opType + "' 没有实现 / the operator is not implemented";
                return null;
        }
    }

    /// <summary>
    /// 常量输入的激活直接折成一个数 / An activation over a constant folds into a number.
    /// </summary>
    /// <remarks>
    /// **符号输入在这里被明确拒绝，而不是"原样返回"：** **原样返回会让激活从模型里消失，** **而模型照常求解、照常给出"最优解"——**
    /// **只是它优化的是一个没有激活的网络。** 要真正支持符号输入，得把函数符号挂进模型，那是模型层的事。
    ///
    /// **A symbolic input is refused here rather than passed through: passing it through erases the activation, and the model still solves and still returns an "optimum" —
    /// it just optimises a network without that activation.** Supporting symbolic inputs properly means attaching function symbols to a model, which is the model layer's job.
    /// </remarks>
    private static OnnxLoweringContext.Binding? FoldActivation(
        string opType,
        IReadOnlyList<OnnxLoweringContext.Binding> operands,
        out string? failure) {
        if (operands.Count == 0) {
            failure = "'" + opType + "' 没有输入 / the operator has no input";
            return null;
        }
        foreach (OnnxLoweringContext.Binding operand in operands) {
            if (operand.Expression.Monomials.Count != 0) {
                failure = "算子 '" + opType + "' 的输入含变量，符号激活需要函数符号（模型层）/ the input carries a variable, and a symbolic activation needs a function symbol (model layer)";
                return null;
            }
        }

        double x = operands[0].Expression.Constant.ToDouble();
        double value = opType switch {
            "Relu" => OnnxActivationSemantics.Relu(x),
            "Sigmoid" => OnnxActivationSemantics.Sigmoid(x),
            "Softplus" => OnnxActivationSemantics.Softplus(x),
            "Tanh" => System.Math.Tanh(x),
            "LeakyRelu" => OnnxActivationSemantics.LeakyRelu(x, Attribute(operands, 1, 0.01)),
            "HardSigmoid" => OnnxActivationSemantics.HardSigmoid(x, Attribute(operands, 1, 0.2), Attribute(operands, 2, 0.5)),
            "Elu" => OnnxActivationSemantics.Elu(x, Attribute(operands, 1, 1.0)),
            "Clip" => OnnxActivationSemantics.Clip(x, Attribute(operands, 1, double.NaN), Attribute(operands, 2, double.NaN)),
            _ => double.NaN,
        };
        if (double.IsNaN(value) && opType != "Clip") {
            failure = "算子 '" + opType + "' 没有折叠实现 / the operator has no folding implementation";
            return null;
        }
        failure = null;
        return new OnnxLoweringContext.Binding(
            new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), new Flt64(value)),
            operands[0].Shape);
    }

    /// <summary>
    /// 求值一个 `If`：条件取自常量，分支体递归走一遍 / Evaluate an If: the condition comes from a constant and the chosen body is walked recursively.
    /// </summary>
    /// <remarks>
    /// **条件必须是常量，否则这里返回失败而不是"挑一个分支"：** **挑错了不会报错，模型照常求解——** **只是它算的是另一条路径上的网络。**
    ///
    /// **The condition must be constant, otherwise this fails rather than picking a branch: a wrong pick throws nothing and the model still solves — it just evaluates the
    /// network on the other path.**
    /// </remarks>
    private static string? EvaluateIf(
        OnnxNode node,
        OnnxLoweringContext context,
        IReadOnlyDictionary<string, double>? constants) {
        OnnxGraph? then = Subgraph(node, "then_branch");
        OnnxGraph? @else = Subgraph(node, "else_branch");

        double condition = 0.0;
        bool isConstant = false;
        if (node.Inputs.Count > 0) {
            string conditionName = node.Inputs[0];
            if (context.TryGet(conditionName, out OnnxLoweringContext.Binding bound) && bound.Expression.Monomials.Count == 0) {
                condition = bound.Expression.Constant.ToDouble();
                isConstant = true;
            }
            else if (constants is not null && constants.TryGetValue(conditionName, out double value)) {
                condition = value;
                isConstant = true;
            }
        }

        OnnxIfLowering.Result lowered = OnnxIfLowering.Lower(
            new OnnxIfExpansion.IfNode(condition, isConstant, then, @else, node.Outputs),
            context,
            constants);
        return lowered.IsOk ? null : lowered.Failure;
    }

    /// <summary>
    /// 求值一个 `Loop`：次数取自常量，每轮把上一轮的结果写回种子 / Evaluate a Loop: the trip count comes from a constant and each round writes back into the seed.
    /// </summary>
    /// <remarks>
    /// **`Loop` 的输入顺序是 `[M, cond?, v...]` 而输出是 `[v..., scan?]`：** **`cond` 可缺省，'
    /// **所以"有没有 cond"只能靠循环体的输入个数反推——** **推错一位会把第一个累加器当成终止条件，而模型照常展开、照常求解。**
    ///
    /// **A Loop's inputs are `[M, cond?, v...]` and its outputs `[v..., scan?]`: cond is optional, so whether it is present can only be inferred from the body's input
    /// count — an off-by-one takes the first accumulator for the termination condition, and the model still unrolls and still solves.**
    /// </remarks>
    private static string? EvaluateLoop(
        OnnxNode node,
        OnnxLoweringContext context,
        IReadOnlyDictionary<string, double>? constants) {
        if (node.Inputs.Count < 1) {
            return "'Loop' 没有迭代次数输入 / the loop has no trip-count input";
        }
        if (!TryConstant(node.Inputs[0], context, constants, out double tripValue)) {
            return "'Loop' 的迭代次数必须是常量 / the trip count has to be a constant";
        }
        OnnxGraph? body = Subgraph(node, "body");
        if (body is null) {
            return "'Loop' 缺少循环体 / the loop has no body";
        }

        // 循环体的输入：迭代变量在前，其后是携带值。带 cond 时节点输入多一个。
        // The body's inputs are the iteration variable first, then the carried values; a cond adds one node input.
        bool hasCondition = node.Inputs.Count - 1 == body.Inputs.Count;
        int firstCarried = hasCondition ? 2 : 1;
        var carried = new List<OnnxLoweringContext.Binding>();
        var bodyNames = new List<string>();
        for (int index = 0; index < body.Inputs.Count; ++index) {
            bodyNames.Add(body.Inputs[index].Name);
        }
        for (int index = firstCarried; index < node.Inputs.Count; ++index) {
            // 携带值的初值多半是 initializer：它是一条常量，不是上下文里的绑定。
            // A carried value's initial value is usually an initializer: a constant, not a binding in the context.
            if (context.TryGet(node.Inputs[index], out OnnxLoweringContext.Binding bound)) {
                carried.Add(bound);
                continue;
            }
            if (TryConstant(node.Inputs[index], context, constants, out double initial)) {
                carried.Add(new OnnxLoweringContext.Binding(
                    new System.Collections.Generic.List<object>() is null ? null! : ConstantPolynomial(initial),
                    Array.Empty<int>()));
                continue;
            }
            return "循环携带值 '" + node.Inputs[index] + "' 既不在上下文里也不是常量 / a carried value is neither in the context nor a constant";
        }

        var loopNode = new OnnxLoopExpansion.LoopNode(
            (long)tripValue,
            true,
            body,
            carried.Count,
            carried.Count,
            carried.Count,
            carried.Count);
        OnnxLoopLowering.Result lowered = OnnxLoopLowering.Lower(
            loopNode,
            context,
            carried,
            bodyNames,
            node.Outputs,
            constants);
        return lowered.IsOk ? null : lowered.Failure;
    }

    private static LinearPolynomial<Flt64> ConstantPolynomial(double value) =>
        new(Array.Empty<LinearMonomial<Flt64>>(), new Flt64(value));

    /// <summary>
    /// 求值一个 `Scan`：长度来自常量张量的首维，每轮取一片 / Evaluate a Scan: the length is a constant tensor's leading dimension, one slice per round.
    /// </summary>
    /// <remarks>
    /// **切片来源必须是常量张量：** **`Scan` 的语义就是"沿一个输入的某一维逐轮取片"，** **而那一维的数据若不来自常量，'
    /// **导入期根本不知道要取几次——** **猜一个次数会让模型照常展开，只是它算的是另一次扫描。**
    ///
    /// **The slices have to come from a constant tensor: Scan means "slice one input's dimension round by round", and if that dimension's data is not constant the import
    /// time cannot know how many slices exist — guessing a count still unrolls, it just evaluates a different scan.**
    /// </remarks>
    private static string? EvaluateScan(
        OnnxNode node,
        OnnxLoweringContext context,
        IReadOnlyDictionary<string, double>? constants,
        IReadOnlyList<double>? sliceValues = null) {
        OnnxGraph? body = Subgraph(node, "body");
        if (body is null) {
            return "'Scan' 缺少循环体 / the scan has no body";
        }
        if (sliceValues is null || sliceValues.Count == 0) {
            return "'Scan' 的扫描输入必须是常量张量 / the scan input has to be a constant tensor, or the length is unknown at import time";
        }

        // 节点输入：状态在前，被逐轮切片的输入在后。缺省时以"状态数 = 体输入数 - 1"推断。
        // Node inputs: states first, the sliced input last; inferred from the body's input count when absent.
        int stateCount = body.Inputs.Count - 1;
        if (stateCount < 0) {
            return "'Scan' 的循环体没有输入 / the scan body takes no input";
        }
        var states = new List<OnnxLoweringContext.Binding>(stateCount);
        for (int index = 0; index < stateCount; ++index) {
            string name = node.Inputs[index];
            if (context.TryGet(name, out OnnxLoweringContext.Binding bound)) {
                states.Add(bound);
            }
            else if (TryConstant(name, context, constants, out double initial)) {
                states.Add(new OnnxLoweringContext.Binding(ConstantPolynomial(initial), Array.Empty<int>()));
            }
            else {
                return "Scan 状态 '" + name + "' 既不在上下文里也不是常量 / a state is neither in the context nor a constant";
            }
        }

        var slices = new List<OnnxLoweringContext.Binding>(sliceValues.Count);
        foreach (double value in sliceValues) {
            slices.Add(new OnnxLoweringContext.Binding(ConstantPolynomial(value), Array.Empty<int>()));
        }

        var bodyNames = new List<string>();
        foreach (OnnxValueInfo input in body.Inputs) {
            bodyNames.Add(input.Name);
        }

        var scanNode = new OnnxScanExpansion.ScanNode(
            body,
            slices.Count,
            slices.Count,
            states.Count,
            states.Count,
            states.Count,
            states.Count);
        OnnxScanLowering.Result lowered = OnnxScanLowering.Lower(
            scanNode,
            context,
            states,
            slices,
            bodyNames,
            node.Outputs,
            constants);
        return lowered.IsOk ? null : lowered.Failure;
    }

    /// <summary>把一个输入名解析成常量 / Resolve one input name to a constant.</summary>
    private static bool TryConstant(
        string name,
        OnnxLoweringContext context,
        IReadOnlyDictionary<string, double>? constants,
        out double value) {
        if (context.TryGet(name, out OnnxLoweringContext.Binding bound) && bound.Expression.Monomials.Count == 0) {
            value = bound.Expression.Constant.ToDouble();
            return true;
        }
        if (constants is not null && constants.TryGetValue(name, out value)) {
            return true;
        }
        value = 0.0;
        return false;
    }

    /// <summary>从节点的属性里取一张子图 / Take a subgraph out of the node's attributes.</summary>
    private static OnnxGraph? Subgraph(OnnxNode node, string name) {
        foreach (OnnxAttribute attribute in node.Attributes) {
            if (attribute.Name == name) {
                return attribute.Graph;
            }
        }
        return null;
    }

    /// <summary>取第 `index` 个操作数的常量值，缺省时用 `fallback` / The index-th operand's constant, falling back when absent.</summary>
    private static double Attribute(IReadOnlyList<OnnxLoweringContext.Binding> operands, int index, double fallback) =>
        index < operands.Count && operands[index].Expression.Monomials.Count == 0
            ? operands[index].Expression.Constant.ToDouble()
            : fallback;

    private static IReadOnlyList<LinearMonomial<Flt64>> Concat(LinearPolynomial<Flt64> left, LinearPolynomial<Flt64> right) {
        var merged = new List<LinearMonomial<Flt64>>(left.Monomials.Count + right.Monomials.Count);
        merged.AddRange(left.Monomials);
        merged.AddRange(right.Monomials);
        return merged;
    }

    private static IReadOnlyList<LinearMonomial<Flt64>> Subtract(LinearPolynomial<Flt64> left, LinearPolynomial<Flt64> right) {
        var merged = new List<LinearMonomial<Flt64>>(left.Monomials.Count + right.Monomials.Count);
        merged.AddRange(left.Monomials);
        foreach (LinearMonomial<Flt64> monomial in right.Monomials) {
            merged.Add(new LinearMonomial<Flt64>(-monomial.Coefficient, monomial.Symbol));
        }
        return merged;
    }

    /// <summary>按符号合并同名单项式、丢掉零系数 / Merge like terms by symbol and drop zero coefficients.</summary>
    private static LinearPolynomial<Flt64> Normalise(IReadOnlyList<LinearMonomial<Flt64>> monomials, Flt64 constant) {
        var bySymbol = new Dictionary<string, (Flt64 Coefficient, ISymbol Symbol)>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (LinearMonomial<Flt64> monomial in monomials) {
            string key = monomial.Symbol.Name;
            if (bySymbol.TryGetValue(key, out (Flt64 Coefficient, ISymbol Symbol) existing)) {
                bySymbol[key] = (existing.Coefficient + monomial.Coefficient, existing.Symbol);
            }
            else {
                bySymbol[key] = (monomial.Coefficient, monomial.Symbol);
                order.Add(key);
            }
        }

        var merged = new List<LinearMonomial<Flt64>>(order.Count);
        foreach (string key in order) {
            (Flt64 coefficient, ISymbol symbol) = bySymbol[key];
            if (coefficient.ToDouble() == 0.0) {
                continue;
            }
            merged.Add(new LinearMonomial<Flt64>(coefficient, symbol));
        }
        return new LinearPolynomial<Flt64>(merged, constant);
    }

    private static OnnxLoweringContext.Binding Scale(OnnxLoweringContext.Binding input, Flt64 factor) {
        var scaled = new List<LinearMonomial<Flt64>>(input.Expression.Monomials.Count);
        foreach (LinearMonomial<Flt64> monomial in input.Expression.Monomials) {
            scaled.Add(new LinearMonomial<Flt64>(monomial.Coefficient * factor, monomial.Symbol));
        }
        return new OnnxLoweringContext.Binding(
            new LinearPolynomial<Flt64>(scaled, input.Expression.Constant * factor),
            input.Shape);
    }
}