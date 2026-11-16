#nullable enable

// IDE0007 与 IDE0008 会在同一个泛型局部上互相要求，而本文件不止一处触发；按仓库既有做法文件级关闭并写理由。
// IDE0007 and IDE0008 demand opposite things on the same generic local and fire in more than one place here; disabled at file scope per repository practice.
#pragma warning disable IDE0007, IDE0008

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
/// **但它引入了前面所有批次都没有的东西：**图是被逐节点走过去的，中间结果留在上下文里**。**
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
        "Identity", "Cast", "Add", "Sub", "Neg", "Mul", "Concat", "Split", "Gather",
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
    /// <param name="allowMultiElementOutput">是否允许逐元素输出（调用方已给出每元素符号）/ whether a per-element output is allowed because the caller supplies per-element symbols.</param>
    /// <param name="constantIntArrays">常量名到整数数组的映射（`Reshape` 的目标形状用）/ The mapping from constant names to integer arrays, used by Reshape targets.</param>
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
        IReadOnlyDictionary<string, IReadOnlyList<double>>? constantArrays = null,
        IReadOnlyDictionary<string, IReadOnlyList<long>>? constantIntArrays = null,
        bool allowMultiElementOutput = false) {
        // 消费者表：判断一个重排有没有落到按位置读的算子上，只看直接消费者是不够的。
        // The consumer map: deciding whether a permutation reaches a positional reader cannot stop at the direct consumer.
        IReadOnlyDictionary<string, IReadOnlyList<OnnxNode>> consumers = Consumers(graph);
        foreach (OnnxNode node in graph.Nodes) {
            if (node.OpType == "If") {
                string? ifFailure = EvaluateIf(node, context, constants);
                if (ifFailure is not null) {
                    return new Result(null, ifFailure, node.Name);
                }
                continue;
            }
            if (OnnxShapeTransform.Operators.Contains(node.OpType)) {
                string? shapeFailure = EvaluateShape(node, context, constantIntArrays, consumers);
                if (shapeFailure is not null) {
                    return new Result(null, shapeFailure, node.Name);
                }
                continue;
            }
            // `Split` 有多个输出，而通用路径只绑 `node.Outputs[0]`：**按通用路径走会把第二段丢掉，**
            // 而图里后面若有人读第二段，就会读到"没有被任何节点产出"——或者更糟，读到同名的旧绑定。
            //
            // Split has several outputs while the generic path binds only node.Outputs[0]: **routing it generically drops the second piece, and anything reading it later would find
            // "not produced by any node" — or worse, a stale binding under the same name.**
            // `Gather` 的索引必须是静态常量：**索引是变量意味着"每次取的位置不同"，** **那就不是一层能展开的东西，**
            // **而按"取第一个"处理会让模型照常求解、结果不同。**
            //
            // A Gather index has to be a static constant: **a variable index means "a different position each time", which is not something this layer can unroll, and treating it as
            // "take the first" would let the model solve with a different answer.**
            if (node.OpType == "Gather") {
                string? gatherFailure = EvaluateGather(node, context, constantIntArrays);
                if (gatherFailure is not null) {
                    return new Result(null, gatherFailure, node.Name);
                }
                continue;
            }
            if (node.OpType == "Split") {
                string? splitFailure = EvaluateSplit(node, context);
                if (splitFailure is not null) {
                    return new Result(null, splitFailure, node.Name);
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

            OnnxLoweringContext.Binding? produced = Compute(node, operands, out string? computeFailure);
            if (produced is null) {
                return new Result(null, computeFailure, node.Name);
            }
            // 元素列表必须随绑定一起写下去：**丢了它，逐元素的结果就被压回一个标量表达式，**
            // 而下游会以为那个张量就是一个数——这正是列表语义要修的那件事。
            //
            // The element list has to be written together with the binding: **dropping it collapses an elementwise result back into one scalar expression**, and every consumer then
            // believes the tensor is one number — exactly what list semantics exist to fix.
            if (!context.TryRebind(node.Outputs[0], produced.Expression, produced.Shape, out string? bindFailure, produced.Elements)) {
                return new Result(null, bindFailure, node.Name);
            }
        }

        var outputs = new Dictionary<string, OnnxLoweringContext.Binding>(StringComparer.Ordinal);
        foreach (OnnxValueInfo output in graph.Outputs) {
            if (!context.TryGet(output.Name, out OnnxLoweringContext.Binding binding)) {
                return new Result(null, "图输出 '" + output.Name + "' 没有被任何节点产出 / the graph output is not produced by anything");
            }
            if (!allowMultiElementOutput && binding.HasElements && binding.Elements!.Count > 1) {
                return new Result(
                    null,
                    "图输出 '" + output.Name + "' 有 " + binding.Elements.Count
                    + " 个元素，而按元素取输出（对齐基线的 elementIndex）还没接 / the graph output holds several elements while element-indexed output selection is not wired yet",
                    null);
            }
            outputs[output.Name] = binding;
        }
        return new Result(outputs, null);
    }

    private static OnnxLoweringContext.Binding? Compute(
        OnnxNode node,
        IReadOnlyList<OnnxLoweringContext.Binding> operands,
        out string? failure) {
        // 节点本身要进来：**`Concat` 的轴写在属性上，** **而"只看算子名"的签名拿不到它——** '
        // **先前的算子都只靠名字，所以这一点一直没暴露。**
        //
        // The node itself has to come in: **Concat's axis lives in an attribute, and a signature that knows only the operator name cannot see it —** earlier operators needed nothing but
        // the name, which is why this never surfaced.
        string opType = node.OpType;
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
                if (operands[0].Elements is OnnxElementList negationSource) {
                    var negated = new List<LinearPolynomial<Flt64>>(negationSource.Count);
                    foreach (LinearPolynomial<Flt64> element in negationSource.Elements) {
                        negated.Add(-element);
                    }
                    OnnxElementList.Result list = OnnxElementList.Create(operands[0].Shape, negated);
                    if (list.Value is not OnnxElementList negatedList) {
                        failure = list.Failure;
                        return null;
                    }
                    return new OnnxLoweringContext.Binding(Provision(negatedList), operands[0].Shape) { Elements = negatedList };
                }
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
                // 有逐元素列表时按元素配对：一个 [3] 张量加另一个 [3]，是三个位置各自相加，
                // 而不是把"整个张量"当成一个数——这正是列表语义与标量语义的分界。
                //
                // With an element list the pairing is per position: [3] plus [3] adds three positions rather than treating "the tensor" as one number — exactly the line between list
                // semantics and scalar semantics.
                if (operands[0].HasElements || operands[1].HasElements) {
                    OnnxElementList.Result paired = Pair(opType, operands[0], operands[1]);
                    if (!paired.IsOk) {
                        failure = paired.Failure;
                        return null;
                    }
                    failure = null;
                    return new OnnxLoweringContext.Binding(Provision(paired.Value!), shape) { Elements = paired.Value };
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
            case "Concat": {
                // 拼接按轴进行，**必须有逐元素列表**：**没有它，"第几个元素接到哪里"没有任何依据，**
                // 而按"一个数"处理会把多段张量当成一个加数——数值上看不出异常。
                //
                // Concatenation happens along an axis and **requires element lists**: without them there is no ground for "which element goes where", and treating it as one number
                // would make several tensors into a single summand — invisible numerically.
                long axis = 0;
                foreach (OnnxAttribute attribute in node.Attributes) {
                    if (attribute.Name == "axis" && attribute.Integer is long value) {
                        axis = value;
                    }
                }
                var parts = new List<OnnxElementList>(operands.Count);
                foreach (OnnxLoweringContext.Binding operand in operands) {
                    if (operand.Elements is not OnnxElementList list) {
                        failure = "'Concat' 的输入缺少逐元素列表 / a Concat input has no element list"
                                  + "（对齐基线的 inputBindings：调用方要为每个输入给出每个元素的符号 / supply one symbol per element through inputBindings）";
                        return null;
                    }
                    parts.Add(list);
                }
                OnnxElementList.Result joined = OnnxElementList.Concat(parts, (int)axis);
                if (joined.Value is not OnnxElementList joinedList) {
                    failure = joined.Failure;
                    return null;
                }
                failure = null;
                return new OnnxLoweringContext.Binding(Provision(joinedList), joinedList.Shape) { Elements = joinedList };
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
    /// 求值一个只改形状的算子：表达式原样带过，形状按规则重算 / Evaluate a shape-only operator: the expression passes through and the shape is recomputed.
    /// </summary>
    /// <remarks>
    /// **"表达式原样带过"是有意的：** **`Reshape`／`Transpose` 不动数值，它们动的是"这个值排在第几个"。** '
    /// **当前这一层每个绑定只带一个标量表达式与一个形状，所以形状换了而表达式不换——** **而这恰好也说明：若哪天表达式变成逐元素向量，这一层必须重写。**
    ///
    /// **"Passing the expression through" is deliberate: Reshape and Transpose do not move values, they move which position a value sits in.** **Each binding here holds one
    /// scalar expression plus a shape, so the shape changes and the expression does not — which also means this layer has to be rewritten the day an expression becomes a
    /// per-element vector.**
    /// </remarks>
    private static string? EvaluateShape(
        OnnxNode node,
        OnnxLoweringContext context,
        IReadOnlyDictionary<string, IReadOnlyList<long>>? constantIntArrays,
        IReadOnlyDictionary<string, IReadOnlyList<OnnxNode>> consumers) {
        if (node.Inputs.Count == 0 || !context.TryGet(node.Inputs[0], out OnnxLoweringContext.Binding input)) {
            return "形状算子 '" + node.OpType + "' 的输入不在上下文里 / the shape operator's input is not in the context";
        }
        if (node.Outputs.Count != 1) {
            return "形状算子 '" + node.OpType + "' 输出不是单个 / the shape operator does not produce exactly one output";
        }

        IReadOnlyList<int>? target = null;
        if (node.OpType == "Reshape") {
            if (node.Inputs.Count < 2) {
                return "'Reshape' 缺少目标形状输入 / the reshape has no target input";
            }
            if (constantIntArrays is null || !constantIntArrays.TryGetValue(node.Inputs[1], out IReadOnlyList<long>? raw)) {
                return "'Reshape' 的目标形状必须是常量张量 / the Reshape target has to be a constant tensor";
            }
            var converted = new List<int>(raw.Count);
            foreach (long value in raw) {
                converted.Add((int)value);
            }
            target = converted;
        }

        IReadOnlyList<int>? perm = null;
        IReadOnlyList<int>? axesAttribute = null;
        foreach (OnnxAttribute attribute in node.Attributes) {
            if (attribute.Name == "perm" && attribute.Integers is { Count: > 0 } permValues) {
                perm = ToInts(permValues);
            }
            if (attribute.Name == "axes" && attribute.Integers is { Count: > 0 } axesValues) {
                axesAttribute = ToInts(axesValues);
            }
            if (attribute.Name == "axis" && attribute.Integer is long axisValue) {
                axesAttribute = new[] { (int)axisValue };
            }
        }

        // 顺序敏感的算子在多元素张量上无法落地：形状能算，而重排无处可写。
        // 但只有当这个重排**落到一个按位置读的算子**上时才可见——否则逐元素消费不看位置，重排是空操作。
        // 若绑定带逐元素列表，重排就真的发生（B1368），此时不再需要拒绝。
        //
        // An order-sensitive operator cannot land on a multi-element tensor here: the shape computes and the permutation has nowhere to go. But that is only visible when the
        // permutation reaches a positional reader — an elementwise consumer does not look at position, and then the permutation is a no-op.
        bool hasElements = input.HasElements && input.Elements!.Count == (int)ElementsOf(input.Shape);

        // 形状是多元素、却没有逐元素列表：**这一步没有东西可重排，而"没有东西可重排"不等于"重排没发生"。** '
        // 放过去会把一个 [2,3] 张量当成一个数，形状写着 6 而绑定里只有一个符号——**那是一个自相矛盾的绑定，**
        // 任何下游按位置读它都会拿到同一个数。**所以宁可在入口拒绝。**
        //
        // A multi-element shape with no element list: **this step has nothing to permute, and "nothing to permute" is not "the permutation did not happen".** Letting it through
        // treats a [2,3] tensor as one number — a shape of 6 with a single symbol in the binding, a self-contradictory binding whose every positional read returns the same number.
        // **So it is refused at the entrance instead.**
        if (!hasElements && ElementsOf(input.Shape) > 1) {
            return "'" + node.OpType + "' 的输入 '" + (node.Inputs.Count > 0 ? node.Inputs[0] : "?")
                   + "' 形状有 " + ElementsOf(input.Shape) + " 个元素，却没有逐元素列表 / the input has a multi-element shape but no element list"
                   + "（对齐基线的 inputBindings：调用方要为每个输入给出每个元素的符号 / sync the baseline inputBindings: the caller supplies one symbol per element）";
        }
        if (!hasElements
            && !OnnxShapeTransform.CanApply(node.OpType, input.Shape, out string? orderFailure)
            && ReachesPositionalConsumer(node, consumers)) {
            return orderFailure;
        }

        OnnxShapeTransform.Result transformed = OnnxShapeTransform.Compute(
            node.OpType,
            input.Shape,
            target,
            perm,
            axesAttribute);
        if (!transformed.IsOk) {
            return "'" + node.OpType + "' 的形状计算失败：" + transformed.Failure;
        }
        // 负的维度只可能来自 -1 推断；推断后仍是负说明目标形状本身有问题。
        // A negative dimension can only come from the -1 inference; still negative afterwards means the target itself is wrong.
        foreach (int dimension in transformed.Shape!) {
            if (dimension < 0) {
                return "'" + node.OpType + "' 得到负的维度 " + dimension + " / the operator produced a negative dimension";
            }
        }
        // 有元素列表时，顺序敏感算子真的重排它；顺序保持的算子原样带过。
        // With an element list, an order-sensitive operator really permutes it; an order-preserving one carries it through unchanged.
        OnnxElementList? reordered = input.Elements;
        if (hasElements && OnnxShapeTransform.OrderChanging.Contains(node.OpType) && outputPermutation(node) is IReadOnlyList<int> permutation) {
            OnnxElementList.Result permuted = input.Elements!.Transpose(permutation);
            if (!permuted.IsOk) {
                return permuted.Failure;
            }
            reordered = permuted.Value;
        }
        else if (hasElements && orderPreservingMismatch(node.OpType, input.Shape, transformed.Shape!)) {
            // `Reshape`／`Flatten`／`Squeeze`／`Unsqueeze` 只改形状，元素列表原样保留：
            // 改形状不改元素顺序，两者元素个数相同。
            // These only change the shape and keep the element list as is: changing the shape does not change the order, and the element counts agree.
            reordered = input.Elements;
        }
        return context.TryRebind(node.Outputs[0], input.Expression, transformed.Shape!, out string? bindFailure, reordered)
            ? null
            : bindFailure;
    }

    /// <summary>名字到消费它的节点的映射 / The mapping from a name to the nodes consuming it.</summary>
    private static IReadOnlyDictionary<string, IReadOnlyList<OnnxNode>> Consumers(OnnxGraph graph) {
        var map = new Dictionary<string, List<OnnxNode>>(StringComparer.Ordinal);
        foreach (OnnxNode node in graph.Nodes) {
            foreach (string input in node.Inputs) {
                if (!map.TryGetValue(input, out List<OnnxNode>? list)) {
                    list = new List<OnnxNode>();
                    map[input] = list;
                }
                list.Add(node);
            }
        }
        var result = new Dictionary<string, IReadOnlyList<OnnxNode>>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, List<OnnxNode>> entry in map) {
            result[entry.Key] = entry.Value;
        }
        return result;
    }

    /// <summary>
    /// 这个张量的下游是否有按位置读的算子 / Whether anything downstream reads this tensor positionally.
    /// </summary>
    /// <remarks>
    /// **必须沿链走到尽头：** **`Transpose → Transpose → Concat` 里第一个重排的影响会穿过第二个传下去，** **只看直接消费者会漏掉它。**
    /// **环用一个访问集合断掉：** 图的节点顺序在合法模型里无环，但非法模型不该让这里挂住。
    ///
    /// **The walk has to reach the end of the chain: in Transpose → Transpose → Concat the first permutation carries through the second, and looking only at the direct consumer
    /// misses it.** **A visited set breaks cycles: a legal graph has none, but an illegal one should not hang this.**
    /// </remarks>
    private static bool ReachesPositionalConsumer(OnnxNode node, IReadOnlyDictionary<string, IReadOnlyList<OnnxNode>> consumers) {
        var pending = new Queue<string>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        foreach (string output in node.Outputs) {
            pending.Enqueue(output);
        }
        while (pending.Count > 0) {
            string name = pending.Dequeue();
            if (!visited.Add(name)) {
                continue;
            }
            if (!consumers.TryGetValue(name, out IReadOnlyList<OnnxNode>? readers)) {
                continue;
            }
            foreach (OnnxNode reader in readers) {
                if (OnnxShapeTransform.PositionalConsumers.Contains(reader.OpType)) {
                    return true;
                }
                foreach (string output in reader.Outputs) {
                    pending.Enqueue(output);
                }
            }
        }
        return false;
    }

    /// <summary>状态数 = 体输入数 - 1（切片占一个）/ The state count is the body's input count minus the slice.</summary>
    private static int stateCountFor(OnnxGraph body) => body.Inputs.Count - 1;

    /// <summary>
    /// 逐元素配对两个操作数 / Pair two operands element by element.
    /// </summary>
    /// <remarks>
    /// **一方是标量（没有元素列表）时按它对齐到每个位置：** **一个标量加一个张量，在 ONNX 里就是逐元素加。** '
    /// **两方都有元素列表但长度不同则拒绝——** **那意味着广播规则没在这一层实现，而放过去会让某几个位置静默取错数。**
    ///
    /// **A scalar (no element list) aligns to every position: ONNX defines scalar-plus-tensor as elementwise.** **Two element lists of different lengths are refused — that means
    /// the broadcast rule is not implemented at this layer, and letting it through would silently take the wrong number at some positions.**
    /// </remarks>
    private static OnnxElementList.Result Pair(string opType, OnnxLoweringContext.Binding left, OnnxLoweringContext.Binding right) {
        IReadOnlyList<LinearPolynomial<Flt64>>? leftElements = left.Elements?.Elements;
        IReadOnlyList<LinearPolynomial<Flt64>>? rightElements = right.Elements?.Elements;
        if (leftElements is not null && rightElements is not null && leftElements.Count != rightElements.Count) {
            return new OnnxElementList.Result(
                null,
                "'" + opType + "' 的两个操作数分别有 " + leftElements.Count + " 与 " + rightElements.Count
                + " 个元素 / the two operands hold different element counts");
        }
        int count = leftElements?.Count ?? rightElements?.Count ?? 0;
        var shape = leftElements is not null ? left.Shape : right.Shape;
        var paired = new List<LinearPolynomial<Flt64>>(count);
        for (int index = 0; index < count; ++index) {
            LinearPolynomial<Flt64> a = leftElements is null ? left.Expression : leftElements[index];
            LinearPolynomial<Flt64> b = rightElements is null ? right.Expression : rightElements[index];
            paired.Add(opType == "Add"
                ? Normalise(Concat(a, b), a.Constant + b.Constant)
                : Normalise(Subtract(a, b), a.Constant - b.Constant));
        }
        return OnnxElementList.Create(shape, paired);
    }

    /// <summary>
    /// 多元素绑定的标量投影 / The scalar projection of a multi-element binding.
    /// </summary>
    /// <remarks>
    /// **多元素绑定没有唯一的标量表达式，** **所以这里的"表达式"不是结果，而是一个占位：** **取第一个元素，仅仅是让"要不要用 Elements"这件事在类型上说得通。**
    /// **任何真正读 `Expression` 的地方若碰上一个多元素绑定，就会读到只属于第一个位置的值——** **因此图输出若多元素，下方会明确拒绝，而不是让它漏出去。**
    ///
    /// **A multi-element binding has no single scalar expression, so the expression here is not the result but a placeholder: the first element, only so that "use Elements or not"
    /// makes sense to the type system.** **Anywhere that really reads Expression while holding a multi-element binding would get the first position only — which is why a
    /// multi-element graph output is refused below instead of leaking.**
    /// </remarks>
    private static LinearPolynomial<Flt64> Provision(OnnxElementList list) => list.Elements[0];

    /// <summary>
    /// 求值一个 `Gather` / Evaluate a Gather.
    /// </summary>
    /// <param name="node">节点 / The node.</param>
    /// <param name="context">上下文 / The context.</param>
    /// <param name="constantIntArrays">整数数组常量表（索引从这里取）/ the integer-array constant table the indices come from.</param>
    /// <returns>失败原因，或 `null` / the failure reason, or null.</returns>
    private static string? EvaluateGather(
        OnnxNode node,
        OnnxLoweringContext context,
        IReadOnlyDictionary<string, IReadOnlyList<long>>? constantIntArrays) {
        if (node.Inputs.Count < 2 || !context.TryGet(node.Inputs[0], out OnnxLoweringContext.Binding data)) {
            return "'Gather' 的数据输入 '" + (node.Inputs.Count > 0 ? node.Inputs[0] : "?") + "' 不在上下文里 / the data input is not in the context";
        }
        if (data.Elements is not OnnxElementList list) {
            return "'Gather' 的输入缺少逐元素列表 / the input has no element list"
                   + "（对齐基线的 inputBindings：调用方要为每个输入给出每个元素的符号 / supply one symbol per element through inputBindings）";
        }
        if (constantIntArrays is null || !constantIntArrays.TryGetValue(node.Inputs[1], out IReadOnlyList<long>? rawIndices)) {
            return "'Gather' 的索引 '" + node.Inputs[1] + "' 不是静态常量 / the indices are not a static constant"
                   + "（索引是变量意味着每次取的位置不同 / a variable index means a different position each time）";
        }
        long axis = 0;
        foreach (OnnxAttribute attribute in node.Attributes) {
            if (attribute.Name == "axis" && attribute.Integer is long value) {
                axis = value;
            }
        }
        var indices = new List<int>(rawIndices.Count);
        foreach (long raw in rawIndices) {
            indices.Add((int)raw);
        }
        OnnxElementList.Result gathered = list.Gather(indices, (int)axis);
        if (gathered.Value is not OnnxElementList gatheredList) {
            return gathered.Failure;
        }
        return context.TryRebind(node.Outputs[0], Provision(gatheredList), gatheredList.Shape, out string? bindFailure, gatheredList)
            ? null
            : bindFailure;
    }

    /// <summary>
    /// 求值一个 `Split`，并把每一段绑到各自声明的名字上 / Evaluate a Split and bind each piece to its own declared name.
    /// </summary>
    /// <param name="node">节点 / The node.</param>
    /// <param name="context">上下文 / The context.</param>
    /// <returns>失败原因，或 `null` / the failure reason, or null.</returns>
    /// <remarks>
    /// **多输出是这一批的目的：** **`Split` 的每一段都要绑，** **否则"形状对、元素对、而后面读不到"这种半成品会被当成成功。**
    ///
    /// **Each piece has to be bound: otherwise a half-done job — right shape, right elements, unreadable afterwards — passes as success.**
    /// </remarks>
    private static string? EvaluateSplit(OnnxNode node, OnnxLoweringContext context) {
        if (node.Inputs.Count == 0 || !context.TryGet(node.Inputs[0], out OnnxLoweringContext.Binding input)) {
            return "'Split' 的输入 '" + (node.Inputs.Count > 0 ? node.Inputs[0] : "?") + "' 不在上下文里 / the input is not in the context";
        }
        if (input.Elements is not OnnxElementList list) {
            return "'Split' 的输入缺少逐元素列表 / the input has no element list"
                   + "（对齐基线的 inputBindings：调用方要为每个输入给出每个元素的符号 / supply one symbol per element through inputBindings）";
        }
        long axis = 0;
        IReadOnlyList<int>? sizes = null;
        foreach (OnnxAttribute attribute in node.Attributes) {
            if (attribute.Name == "axis" && attribute.Integer is long value) {
                axis = value;
            }
            if (attribute.Name == "split" && attribute.Integers is { Count: > 0 } pieces) {
                sizes = ToInts(pieces);
            }
        }
        OnnxElementList.SplitResult split = list.Split((int)axis, sizes, node.Outputs.Count);
        if (split.Values is not IReadOnlyList<OnnxElementList> produced) {
            return split.Failure;
        }
        for (int index = 0; index < produced.Count; ++index) {
            OnnxElementList piece = produced[index];
            if (!context.TryRebind(node.Outputs[index], Provision(piece), piece.Shape, out string? bindFailure, piece)) {
                return bindFailure;
            }
        }
        return null;
    }

    private static long ElementsOf(IReadOnlyList<int> shape) {
        long total = 1;
        foreach (int dimension in shape) {
            total *= dimension;
        }
        return total;
    }

    /// <summary>`Transpose` 的置换（缺省为逆序）/ The permutation for Transpose, reversed when absent.</summary>
    private static IReadOnlyList<int>? outputPermutation(OnnxNode node) {
        foreach (OnnxAttribute attribute in node.Attributes) {
            if (attribute.Name == "perm" && attribute.Integers is { Count: > 0 } values) {
                return ToInts(values);
            }
        }
        return null;
    }

    /// <summary>顺序保持算子改完形状后，元素列表能否原样带过 / Whether an order-preserving operator can carry the list through.</summary>
    private static bool orderPreservingMismatch(string opType, IReadOnlyList<int> inputShape, IReadOnlyList<int> outputShape)
        => OnnxShapeTransform.Operators.Contains(opType)
           && !OnnxShapeTransform.OrderChanging.Contains(opType)
           && ElementsOf(inputShape) == ElementsOf(outputShape);

    private static IReadOnlyList<int> ToInts(IReadOnlyList<long> values) {
        var result = new List<int>(values.Count);
        foreach (long value in values) {
            result.Add((int)value);
        }
        return result;
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
        // 扫描输入带逐元素列表时，运行期切片就能真发生：长度取首维、每轮切一片（B1367 的入口）。
        // When the scan input carries an element list, runtime slicing can really happen: the length is the leading dimension and each round cuts one slice.
        if (node.Inputs.Count > 0 && context.TryGet(node.Inputs[^1], out OnnxLoweringContext.Binding scanBound) && scanBound.HasElements) {
            var elementStates = new List<OnnxLoweringContext.Binding>(stateCountFor(body));
            for (int index = 0; index < stateCountFor(body); ++index) {
                string name = node.Inputs[index];
                if (context.TryGet(name, out OnnxLoweringContext.Binding bound)) {
                    elementStates.Add(bound);
                }
                else if (TryConstant(name, context, constants, out double initial)) {
                    elementStates.Add(new OnnxLoweringContext.Binding(ConstantPolynomial(initial), Array.Empty<int>()));
                }
                else {
                    return "Scan 状态 '" + name + "' 既不在上下文里也不是常量 / a state is neither in the context nor a constant";
                }
            }
            var names = new List<string>();
            foreach (OnnxValueInfo input in body.Inputs) {
                names.Add(input.Name);
            }
            var shaped = new OnnxScanExpansion.ScanNode(
                body,
                scanBound.Elements!.Shape.Count == 0 ? 0 : scanBound.Elements.Shape[0],
                scanBound.Elements.Shape.Count == 0 ? 0 : scanBound.Elements.Shape[0],
                elementStates.Count,
                elementStates.Count,
                elementStates.Count,
                elementStates.Count);
            OnnxScanLowering.Result runtime = OnnxScanLowering.LowerWithElements(
                shaped,
                context,
                elementStates,
                scanBound.Elements,
                names,
                node.Outputs,
                constants);
            return runtime.IsOk ? null : runtime.Failure;
        }

        if (sliceValues is null || sliceValues.Count == 0) {
            // 说真话：不是"必须是常量"，而是"逐轮切片需要元素列表，而这一层每个绑定只有一个标量表达式"。
            // 基线用 `sliceLeading` 从张量表达式里切元素子集，那需要逐元素表示。
            //
            // Say the real reason: it is not "must be constant" but "a per-round slice needs an element list, and each binding here is one scalar expression". The baseline
            // slices a sub-list of elements out of a tensor expression with sliceLeading, which needs the per-element representation.
            return "'Scan' 的扫描输入这里取不到逐轮切片：导入期只有常量张量才拿得到每一片，"
                 + "而运行期切片需要逐元素表示（基线用 sliceLeading 从张量表达式中切元素子集），这一层每个绑定只有一个标量表达式 / "
                 + "per-round slices are unavailable here: only a constant tensor yields slices at import time, and a runtime slice needs the per-element representation "
                 + "(the baseline slices elements out of a tensor expression with sliceLeading) while each binding here is one scalar expression";
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