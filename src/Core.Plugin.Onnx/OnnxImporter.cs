#nullable enable

// IDE0007 与 IDE0008 会在同一个泛型局部上互相要求，而本文件两处都触发；按仓库既有做法文件级关闭并写理由。
// IDE0007 and IDE0008 demand opposite things on the same generic local and both fire here; disabled at file scope per repository practice.
#pragma warning disable IDE0007, IDE0008

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Plugin.Onnx.Graph;
using Fuookami.Ospf.Core.Plugin.Onnx.Lowering;
using Fuookami.Ospf.Core.Plugin.Onnx.Model;
using Fuookami.Ospf.Core.Plugin.Onnx.Proto;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;

namespace Fuookami.Ospf.Core.Plugin.Onnx;

/// <summary>
/// **导入一个 ONNX 模型的单一入口** / The single entry point for importing an ONNX model.
/// </summary>
/// <remarks>
/// 同步 Kotlin `dee897d2` 的 `OnnxFunctionalModel` 里"从字节到可用模型"那条主干。**它把前 17 批的东西按固定次序串起来：**
/// **解析 → 图校验 → 前置检查 → 界解析 → 仿射求值 → 模型组装。**
///
/// **次序本身就是契约：** **同一份坏模型可能同时触发三处错误，报告哪一处决定了调用方能否一次改对。** '
/// **所以判据不只验"会不会失败"，还验"先报哪一处"。**
///
/// Syncs the "bytes to usable model" trunk of `OnnxFunctionalModel`. **It strings the previous seventeen batches together in a fixed order: parse → graph
/// validation → preflight → bound resolution → affine evaluation → model assembly.**
///
/// **The order is itself the contract: one bad model can trip three checks at once, and which one is reported decides whether the caller can fix it in one pass.**
/// **The criteria therefore check not only that it fails, but which failure comes first.**
/// </remarks>
public static class OnnxImporter
{
    /// <summary>
    /// 导入结果 / The import result.
    /// </summary>
    /// <param name="Polynomial">求值出的仿射多项式 / The evaluated affine polynomial.</param>
    /// <param name="Variables">输入变量 / The input variables.</param>
    /// <param name="Stage">失败发生在哪一步 / The stage the failure happened in.</param>
    /// <param name="Failure">失败原因 / The failure reason.</param>
    /// <param name="OutputElements">多元素输出的逐元素表达式 / The per-element expressions of a multi-element output.</param>
    /// <param name="OutputShape">多元素输出的形状 / The shape of a multi-element output.</param>
    /// <param name="OutputQuadraticElements">二次模式下的逐元素输出；与 `OutputElements` 互斥 / the per-element output in quadratic mode, exclusive with OutputElements.</param>
    public sealed record Imported(
        LinearPolynomial<Flt64>? Polynomial,
        IReadOnlyDictionary<string, RealVar>? Variables,
        string? Stage,
        string? Failure,
        IReadOnlyList<LinearPolynomial<Flt64>>? OutputElements = null,
        IReadOnlyList<int>? OutputShape = null,
        IReadOnlyList<QuadraticPolynomial<Flt64>>? OutputQuadraticElements = null)
    {
        /// <summary>是否成功 / Whether it succeeded.</summary>
        public bool IsOk => Failure is null;

        /// <summary>
        /// 按元素取输出 / Take the output by element index.
        /// </summary>
        /// <param name="index">元素下标 / The element index.</param>
        /// <returns>该元素的表达式 / That element's expression.</returns>
        /// <remarks>
        /// 对齐基线的 `linearOutput(outputName, elementIndex)`：**多元素输出没有唯一的标量表达式，** '
        /// **所以"第几个元素"必须由调用方给出，而不是由这里挑一个。** **单元素输入上它等价于 `Polynomial`。**
        ///
        /// Syncs the baseline's `linearOutput(outputName, elementIndex)`: **a multi-element output has no single scalar expression, so "which element" has to come from the caller
        /// rather than being picked here.** **On a single-element output it is the same as Polynomial.**
        /// </remarks>
        public LinearPolynomial<Flt64> Output(int index) {
            if (OutputElements is not null) {
                return OutputElements[index];
            }
            if (Polynomial is null) {
                throw new InvalidOperationException("没有可取的输出 / no output is available: " + (Failure ?? "unknown"));
            }
            return Polynomial;
        }
    }

    /// <summary>
    /// 导入 / Import.
    /// </summary>
    /// <summary>
    /// 导入，并由调用方给出每个元素的符号 / Import with caller-supplied symbols, one per element.
    /// </summary>
    /// <param name="bytes">模型字节 / The model bytes.</param>
    /// <param name="options">导入选项 / The options.</param>
    /// <param name="inputBindings">输入名到符号列表的绑定，长度等于元素数 / bindings from input name to a symbol list of length equal to the element count.</param>
    /// <returns>导入结果 / The result.</returns>
    /// <remarks>
    /// 对齐基线 `load(bytes, inputBindings: Map&lt;String, List&lt;Symbol&gt;&gt;, options)`：**张量拆成每元素符号是调用方的事，**
    /// **导入器不去猜命名。** **这一入口开启之后，多元素输出按 `OutputElements`／`Output(index)` 取，**
    /// **而旧入口在没有符号列表的情况下仍然明确拒绝多元素输出——** **它没有表达这件事的地方。**
    ///
    /// Syncs the baseline's `load(bytes, inputBindings: Map&lt;String, List&lt;Symbol&gt;&gt;, options)`: **splitting a tensor into per-element symbols is the caller's job, and the importer
    /// does not guess names.** **With this entry a multi-element output is read through OutputElements / Output(index), while the old entry keeps refusing a multi-element output —
    /// it has nowhere to express it.**
    /// </remarks>
    public static Imported Import(
        byte[] bytes,
        OnnxImportOptions options,
        IReadOnlyDictionary<string, IReadOnlyList<RealVar>> inputBindings)
        => ImportCore(bytes, options, inputBindings);

    /// <param name="bytes">模型字节 / The model bytes.</param>
    /// <param name="options">导入选项 / The import options.</param>
    /// <returns>导入结果 / The result.</returns>
    public static Imported Import(byte[] bytes, OnnxImportOptions options)
        => ImportCore(bytes, options, null);

    private static Imported ImportCore(
        byte[] bytes,
        OnnxImportOptions options,
        IReadOnlyDictionary<string, IReadOnlyList<RealVar>>? inputBindings) {
        if (!options.Validate(out string? optionFailure)) {
            return new Imported(null, null, "options", optionFailure);
        }

        OnnxModel model;
        try {
            model = OnnxModelParser.Parse(bytes);
        }
        catch (FormatException error) {
            return new Imported(null, null, "parse", "模型字节无法解析 / the bytes are not a parseable model: " + error.Message);
        }

        if (model.Graph is not OnnxGraph graph) {
            return new Imported(null, null, "parse", "模型没有计算图 / the model carries no graph");
        }

        OnnxGraphValidator.Result valid = OnnxGraphValidator.Validate(graph);
        if (!valid.IsValid) {
            return new Imported(null, null, "graph", valid.Failure);
        }

        OnnxOperatorRegistry.PreflightResult preflight = OnnxOperatorRegistry.Preflight(graph, options);
        if (!preflight.IsValid) {
            return new Imported(null, null, "operators", preflight.Failure);
        }

        OnnxInputBounds.Resolution bounds = OnnxInputBounds.Resolve(graph, options);
        if (!bounds.IsComplete) {
            string detail = bounds.Failure
                            ?? ("缺少输入区间：" + string.Join(", ", bounds.Missing) + " / missing input bounds");
            return new Imported(null, null, "bounds", detail);
        }

        // 变量来自图的输入，不是"需要界的那些输入"：一条纯仿射链没有任何算子要求界，
        // 而它照样有 x 和 y——按界建变量会让它取不到任何输入。
        //
        // Variables come from the graph's inputs, not from "the inputs that need bounds": a purely affine chain has no operator demanding a bound and still has x and y,
        // and building variables from the bounds would leave it with none.
        var initializerNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (OnnxTensor initializer in graph.Initializers) {
            if (initializer.Name is not null) {
                initializerNames.Add(initializer.Name);
            }
        }
        var variables = new Dictionary<string, RealVar>(StringComparer.Ordinal);
        var bindings = new Dictionary<string, IReadOnlyList<RealVar>>(StringComparer.Ordinal);
        foreach (OnnxValueInfo input in graph.Inputs) {
            if (initializerNames.Contains(input.Name)) {
                continue;
            }
            if (inputBindings is not null && inputBindings.TryGetValue(input.Name, out IReadOnlyList<RealVar>? supplied)) {
                bindings[input.Name] = supplied;
                variables[input.Name] = supplied[0];
                continue;
            }
            variables[input.Name] = new RealVar(input.Name);
        }

        // 调用方给了符号列表时，长度必须等于声明的元素数：**少一个会让某个位置无符号可用，**
        // 多一个意味着调用方对形状的理解与模型不同，两者都会在后续静默错位。
        //
        // When the caller supplies symbol lists their lengths must equal the declared element counts: one short leaves a position without a symbol, one extra means the caller and
        // the model disagree about the shape, and either way something misaligns later.
        foreach (KeyValuePair<string, IReadOnlyList<RealVar>> entry in bindings) {
            IReadOnlyList<int> declared = DeclaredShape(graph, entry.Key);
            long expected = 1;
            foreach (int dimension in declared) {
                expected *= System.Math.Max(dimension, 1);
            }
            if (declared.Count == 0) {
                continue;
            }
            if (entry.Value.Count != expected) {
                return new Imported(
                    null, variables, "bindings",
                    "输入 '" + entry.Key + "' 声明有 " + expected + " 个元素，而给了 " + entry.Value.Count
                    + " 个符号 / the input declares " + expected + " elements but got " + entry.Value.Count + " symbols");
            }
        }

        // 带控制流的图走逐节点求值：`If` 的分支体要递归走一遍，而仿射链只看节点、不看子图。
        // 两条路都收敛到同一个 `LinearPolynomial`，所以返回类型不变。
        //
        // A graph with control flow goes through node-by-node evaluation: an If body has to be walked recursively, while the affine chain looks at nodes and not at
        // subgraphs. Both paths end in the same LinearPolynomial, so the return type is unchanged.
        // 调用方给了元素符号就必须走逐节点求值：**仿射链只看节点、不看形状，会把一个张量当成一个数，**
        // **那样"每元素一个符号"这件事当场失效，而导入照样成功。**
        //
        // Caller-supplied element symbols force the node-by-node evaluator: **the affine chain looks at nodes and not shapes, so it treats a tensor as one number, and the
        // per-element symbols would silently do nothing while the import still succeeded.**
        if (NeedsEvaluator(graph) || bindings.Count > 0) {
            return ImportThroughEvaluator(graph, variables, bounds.Resolved, bindings, options.Mode != OnnxImportMode.Linear);
        }

        // 默认入口没有符号列表可用：**多元素输入在仿射链里只能被当成一个数，** **而"当成一个数"在导入时看不出来，只在最优值上错。**
        // **规则只放在这里，是因为它描述的正是仿射链的局限：** **形状算子与控制流那条路径有自己的拒绝（缺元素列表、多元素输出），** '
        // **在这里再拦一次只会把那些更有信息量的消息换成一句笼统的。**
        //
        // The default entry has no symbol lists: **a multi-element input can only be treated as one number in the affine chain, and that mistake is invisible at import and wrong only in
        // the optimum.** **The rule lives here because it describes the affine chain's limitation: the shape-operator and control-flow path has its own refusals (missing element lists,
        // multi-element outputs), and stopping it here would replace those more informative messages with a generic one.**
        foreach (OnnxValueInfo input in graph.Inputs) {
            if (initializerNames.Contains(input.Name)) {
                continue;
            }
            long elements = 1;
            foreach (OnnxDimension dimension in input.Shape) {
                elements *= System.Math.Max(dimension.Value ?? 1, 1);
            }
            if (input.ElementType != 0 && elements > 1) {
                return new Imported(
                    null, variables, "bindings",
                    "输入 '" + input.Name + "' 有 " + elements + " 个元素，而仿射链没有逐元素表示 / the input holds "
                    + elements + " elements and the affine chain has no per-element representation"
                    + "（用 `Import(bytes, options, inputBindings)` 给出每元素符号，对齐基线的 inputBindings / supply one symbol per element）");
            }
        }

        // 仿射链没有逐元素表示，所以多元素输出在这里无法表达：**此前它会被静默压成一个标量多项式，**
        // **而调用方拿到的是一个"看起来没问题"的结果——那正是列表语义要消灭的错法。**
        //
        // The affine chain has no per-element representation, so a multi-element output cannot be expressed here: **it used to be silently collapsed into one scalar polynomial and the
        // caller received something that looked fine — exactly the failure mode list semantics exist to remove.**
        if (graph.Outputs.Count > 0) {
            // 形状靠沿节点推：**只查声明的形状会漏掉"没写形状的输出"，** **而那种输出正是被静默压成一个数的那个。**
            // The shape is inferred along the nodes: **looking only at declared shapes misses an output that declares nothing — exactly the one silently collapsed into one number.**
            long? outputElements = OnnxShapePropagation.Infer(graph).Elements(graph.Outputs[0].Name);
            if (outputElements is long known && known > 1) {
                return new Imported(
                    null, variables, "evaluate",
                    "图输出 '" + graph.Outputs[0].Name + "' 有 " + known
                    + " 个元素，而仿射链没有逐元素表示 / the affine chain has no per-element representation for a multi-element output"
                    + "（对齐基线：用 inputBindings 给出每元素符号 / sync the baseline: supply one symbol per element through inputBindings）");
            }
        }

        OnnxAffineChain.Result chain = OnnxAffineChain.Evaluate(graph, variables);
        if (chain.Failure is not null) {
            return new Imported(null, null, "evaluate", chain.Failure);
        }

        return new Imported(chain.Polynomial, variables, null, null);
    }

    /// <summary>
    /// 找出被 `Scan` 当作扫描输入的多元素输入，并按元素展开 / Find the multi-element inputs a Scan reads as its scan input and expand them per element.
    /// </summary>
    /// <param name="graph">图 / The graph.</param>
    /// <param name="variables">输入名到变量的映射 / The mapping from input names to variables.</param>
    /// <param name="bounds">输入区间 / The input ranges.</param>
    /// <returns>逐个输入的逐元素表示 / The per-element representation of each such input.</returns>
    /// <remarks>
    /// **展开的只有"被 `Scan` 扫描的输入"，因为只有那里需要元素列表。** **其余多元素输入保持"一个名字一个变量"——**
    /// **那在数学上是错的（一个张量被当成一个数），但它是既有契约；** **这一批不偷偷改它，只把需要的那一处打开。**
    ///
    /// **范围写在这里，是因为"偷偷把所有张量展开"会让 298 条依赖"一名一标量"的判据全部变义。**
    ///
    /// **Only inputs scanned by a Scan are expanded, because only there is an element list needed.** Every other multi-element input keeps "one name, one variable" — which is
    /// mathematically wrong (a tensor treated as one number) but is the existing contract; **this batch does not change it quietly, it opens only the place that needs it.**
    ///
    /// **The scope is stated here because "quietly expand every tensor" would change the meaning of all 298 criteria that rely on one-name-one-scalar.**
    /// </remarks>
    private static IReadOnlyDictionary<string, OnnxElementList> ExpandedScanInputs(
        OnnxGraph graph,
        Dictionary<string, RealVar> variables,
        IReadOnlyDictionary<string, OnnxBounds> bounds) {
        var result = new Dictionary<string, OnnxElementList>(StringComparer.Ordinal);
        foreach (OnnxNode node in graph.Nodes) {
            if (node.OpType != "Scan" || node.Inputs.Count == 0) {
                continue;
            }
            string scanName = node.Inputs[^1];
            if (result.ContainsKey(scanName) || !variables.ContainsKey(scanName)) {
                continue;
            }
            IReadOnlyList<int> shape = DeclaredShape(graph, scanName);
            if (shape.Count == 0 || shape[0] <= 1) {
                continue;
            }
            var elements = new List<LinearPolynomial<Flt64>>(shape[0]);
            for (int index = 0; index < shape[0]; ++index) {
                // 逐元素变量沿用该名字的区间：模型里它被同一个约束区间包着，这一点写在文档里而不是猜。
                // The per-element variables reuse that name's range: the model constrains it with the same interval, and that is documented rather than guessed.
                var element = new RealVar(scanName + "_" + index);
                elements.Add(new LinearPolynomial<Flt64>(
                    new[] { new LinearMonomial<Flt64>(Flt64.One, element) },
                    Flt64.Zero));
                if (bounds.TryGetValue(scanName, out OnnxBounds? range)) {
                    element.Range.SetRange(Fuookami.Ospf.Math.Algebra.ValueRange.ValueRange<Flt64>.Of(
                        new Flt64(range.Lower),
                        new Flt64(range.Upper),
                        new Fuookami.Ospf.Math.Algebra.ValueRange.Interval.Closed(),
                        new Fuookami.Ospf.Math.Algebra.ValueRange.Interval.Closed()).Value);
                }
            }
            result[scanName] = OnnxElementList.Create(shape, elements).Value!;
        }
        return result;
    }

    /// <summary>
    /// 图里声明的形状（输入与输出都算）/ The shape the graph declares, for an input or an output.
    /// </summary>
    /// <remarks>
    /// **输出也要查：** **"这个输出有几个元素"是判断"'一个数'这个说法还能不能用"的唯一依据，** '
    /// **只看输入会让多元素输出一路按标量走完而没人发现。**
    ///
    /// **Outputs are consulted too: "how many elements does this output hold" is the only ground for deciding whether "one number" is still a valid description, and looking at inputs
    /// alone lets a multi-element output walk all the way through as a scalar unnoticed.**
    /// </remarks>
    private static IReadOnlyList<int> DeclaredShape(OnnxGraph graph, string name) {
        foreach (OnnxValueInfo input in AllDeclarations(graph)) {
            if (input.Name != name) {
                continue;
            }
            var shape = new List<int>(input.Shape.Count);
            foreach (OnnxDimension dimension in input.Shape) {
                if (dimension.Value is not long value) {
                    // 动态维在导入期没有确定值；留空比猜一个值安全。
                    // A dynamic dimension has no value at import time; leaving it empty is safer than guessing one.
                    return Array.Empty<int>();
                }
                shape.Add((int)value);
            }
            return shape;
        }
        return Array.Empty<int>();
    }

    /// <summary>输入与输出的声明合在一起 / The input and output declarations together.</summary>
    private static IEnumerable<OnnxValueInfo> AllDeclarations(OnnxGraph graph) {
        foreach (OnnxValueInfo input in graph.Inputs) {
            yield return input;
        }
        foreach (OnnxValueInfo output in graph.Outputs) {
            yield return output;
        }
    }

    /// <summary>
    /// 这张图是否必须走逐节点求值 / Whether this graph has to go through node-by-node evaluation.
    /// </summary>
    /// <remarks>
    /// **判断依据是"仿射链不认识这些算子"，不是"这些算子叫控制流"：** **`Reshape` 不是控制流，**
    /// **但它同样需要逐节点走一遍才能换形状。** **按名字列举会漏掉新接进来的算子，按"谁认识"判断则不会。**
    ///
    /// **The test is "the affine chain does not know these operators", not "these are called control flow": Reshape is not control flow, yet it also needs a node-by-node
    /// walk to change the shape.** **Enumerating names misses each newly wired operator; asking "who knows it" does not.**
    /// </remarks>
    private static bool NeedsEvaluator(OnnxGraph graph) {
        foreach (OnnxNode node in graph.Nodes) {
            if (OnnxShapeTransform.Operators.Contains(node.OpType)) {
                return true;
            }
            if (node.OpType is "If" or "Loop" or "Scan") {
                return true;
            }
        }
        return false;
    }

    private static Imported ImportThroughEvaluator(
        OnnxGraph graph,
        Dictionary<string, RealVar> variables,
        IReadOnlyDictionary<string, OnnxBounds> bounds,
        IReadOnlyDictionary<string, IReadOnlyList<RealVar>>? bindings = null,
        bool quadraticMode = false) {
        var context = new OnnxLoweringContext();
        // 被 `Scan` 当作扫描输入的多元素输入，展开成逐位置变量：**这是唯一需要元素列表的地方，**
        // 因而也只对它们展开——其余输入仍按"一个名字一个变量"，既有模型的行为一点不变。
        //
        // A multi-element input that some Scan reads as its scan input is expanded into one variable per position: **that is the only place an element list is needed**, so only those
        // are expanded — every other input keeps "one name, one variable", and existing models behave exactly as before.
        IReadOnlyDictionary<string, OnnxElementList> scanInputs = ExpandedScanInputs(graph, variables, bounds);
        foreach (KeyValuePair<string, OnnxElementList> entry in scanInputs) {
            OnnxElementList elements = entry.Value;
            var perElement = new List<LinearPolynomial<Flt64>>(elements.Count);
            foreach (LinearPolynomial<Flt64> element in elements.Elements) {
                perElement.Add(element);
            }
            var list = OnnxElementList.Create(elements.Shape, perElement).Value!;
            context.TryBind(entry.Key, perElement[0], elements.Shape, out _, list);
        }

        // 调用方给的符号列表直接变成元素列表：**这就是基线 inputBindings 的落地形态，**
        // **之后逐元素运算、形状算子、`Scan` 全程按元素走，不再回到"一个数"。**
        //
        // The caller's symbol lists become element lists directly: **this is the baseline inputBindings taking shape here, and from this point elementwise operators, shape operators
        // and Scan all stay per element instead of collapsing back to one number.**
        if (bindings is not null) {
            foreach (KeyValuePair<string, IReadOnlyList<RealVar>> entry in bindings) {
                var list = new List<LinearPolynomial<Flt64>>(entry.Value.Count);
                foreach (RealVar symbol in entry.Value) {
                    list.Add(new LinearPolynomial<Flt64>(
                        new[] { new LinearMonomial<Flt64>(Flt64.One, symbol) },
                        Flt64.Zero));
                }
                OnnxElementList.Result built = OnnxElementList.Create(DeclaredShape(graph, entry.Key), list);
                if (built.Value is not OnnxElementList elements) {
                    return new Imported(null, variables, "bindings", built.Failure);
                }
                context.TryBind(entry.Key, list[0], DeclaredShape(graph, entry.Key), out _, elements);
            }
        }

        foreach (KeyValuePair<string, RealVar> entry in variables) {
            if (scanInputs.ContainsKey(entry.Key) || (bindings is not null && bindings.ContainsKey(entry.Key))) {
                continue;
            }
            var polynomial = new LinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(Flt64.One, entry.Value) },
                Flt64.Zero);
            // 形状要用图里声明的：`Reshape`／`Squeeze` 的守恒检查没有形状就无从谈起，
            // 而"空形状"会让任何多元素的重塑都判成元素个数不符。
            //
            // The shape comes from what the graph declares: Reshape and Squeeze cannot check conservation without one, and an empty shape makes every multi-element reshape look
            // like a mismatch.
            context.TryBind(entry.Key, polynomial, DeclaredShape(graph, entry.Key), out _);
        }

        // 常量初值必须交给求值器：`If` 的条件常常是一个 initializer，
        // 它既不是变量也不是节点输出，只作为常量存在。
        //
        // Initialisers have to reach the evaluator: an If condition is often an initializer, which is neither a variable nor a node output and exists only as a constant.
        var constants = new Dictionary<string, double>(StringComparer.Ordinal);
        var arrays = new Dictionary<string, IReadOnlyList<double>>(StringComparer.Ordinal);
        var intArrays = new Dictionary<string, IReadOnlyList<long>>(StringComparer.Ordinal);
        foreach (OnnxTensor initializer in graph.Initializers) {
            if (initializer.Name is null) {
                continue;
            }
            if (initializer.FloatData is { Count: > 0 } floats) {
                constants[initializer.Name] = floats[0];
                // 整个数组也要留下：`Scan` 的长度与每轮切片都来自它，只留第一个元素会让扫描长度恒为 1。
                // The whole array has to survive as well: Scan takes both its length and its slices from it, and keeping only the first element makes every scan length one.
                var values = new List<double>(floats.Count);
                foreach (float value in floats) {
                    values.Add(value);
                }
                arrays[initializer.Name] = values;
            }
            else if (initializer.Int64Data is { Count: > 0 } integers) {
                constants[initializer.Name] = integers[0];
                // 整数数组同样整份留下：`Reshape` 的目标形状、`Transpose` 的置换轴都可能是它们。
                // Integer arrays survive whole as well: a Reshape target or a Transpose permutation can be one.
                intArrays[initializer.Name] = new List<long>(integers);
            }
        }

        // 常量张量也要有逐元素列表：**`MatMul`／`Gemm` 的一侧是权重，而权重是 `initializer`。** '
        // **只把它们当"一个数"存着，矩阵乘就没有第二维可用——** **算法写好了也接不上。**
        //
        // Constant tensors need element lists too: **one side of MatMul/Gemm is the weights, and the weights are initializers.** **Keeping them as "one number" leaves matrix multiplication
        // without a second dimension — the algorithm exists and still cannot connect.**
        foreach (OnnxTensor initializer in graph.Initializers) {
            if (initializer.Name is null || initializer.Dims.Count == 0) {
                continue;
            }
            var shape = new List<int>(initializer.Dims.Count);
            long total = 1;
            foreach (long dimension in initializer.Dims) {
                shape.Add((int)dimension);
                total *= System.Math.Max(dimension, 1);
            }
            var elements = new List<LinearPolynomial<Flt64>>((int)total);
            for (long index = 0; index < total; ++index) {
                double value = index < initializer.FloatData.Count ? initializer.FloatData[(int)index] : 0.0;
                elements.Add(new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), new Flt64(value)));
            }
            OnnxElementList.Result built = OnnxElementList.Create(shape, elements);
            if (built.Value is OnnxElementList constantsList) {
                context.TryBind(initializer.Name, elements[0], shape, out _, constantsList);
            }
        }

        OnnxGraphEvaluator.Result evaluated = OnnxGraphEvaluator.Evaluate(
            graph, context, constants, arrays, intArrays,
            allowMultiElementOutput: bindings is not null,
            quadraticMode: quadraticMode);
        if (!evaluated.IsOk) {
            return new Imported(
                null,
                variables,
                "evaluate",
                evaluated.NodeName is null ? evaluated.Failure : evaluated.Failure + "（节点 " + evaluated.NodeName + "）");
        }
        if (graph.Outputs.Count == 0) {
            return new Imported(null, variables, "evaluate", "图没有输出 / the graph declares no output");
        }
        OnnxLoweringContext.Binding produced = evaluated.Outputs![graph.Outputs[0].Name];
        if (produced.QuadraticElements is OnnxQuadraticElementList quadratic)
        {
            var quadraticValues = new List<QuadraticPolynomial<Flt64>>(quadratic.Count);
            foreach (QuadraticPolynomial<Flt64> element in quadratic.Elements) {
                quadraticValues.Add(element);
            }
            return new Imported(null, variables, null, null, null, quadratic.Shape, quadraticValues);
        }
        if (produced.Elements is OnnxElementList producedElements && producedElements.Count > 1) {
            var values = new List<LinearPolynomial<Flt64>>(producedElements.Count);
            foreach (LinearPolynomial<Flt64> element in producedElements.Elements) {
                values.Add(element);
            }
            return new Imported(null, variables, null, null, values, produced.Shape);
        }
        return new Imported(produced.Expression, variables, null, null);
    }
}