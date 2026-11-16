#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Plugin.Onnx.Proto;

namespace Fuookami.Ospf.Core.Plugin.Onnx.Lowering;

/// <summary>
/// **沿节点推形状**：没有声明形状的张量也能知道它有几个元素 / Inferring shapes along the nodes, so an undeclared tensor still tells how many elements it holds.
/// </summary>
/// <remarks>
/// 同步 Kotlin `dee897d2` 的 `materializeShapes`：**降阶之前先把形状补齐，** **因为"这个张量有几个元素"决定了后面每一步能不能按元素走。**
///
/// **为什么需要它：** **仿射链只看节点、不看形状。** **输出没有声明形状时，它把一个三元素张量当成一个数，而导入照样成功——**
/// **调用方拿到的是"看起来没问题"的结果。** **这一层把元素个数推出来，那条路就堵上了。**
///
/// **推不出来就说推不出来：** **`Unknown` 不是一个可以当 1 用的值**，**它只是"这里没有依据"。** **能不能继续由调用方决定，** '
/// **而不是由这里猜一个 1 蒙过去。**
///
/// Syncs `materializeShapes`: **the code fills shapes in before lowering, because "how many elements does this tensor hold" decides whether every later step can stay per element.**
///
/// **Why it is needed: the affine chain looks at nodes and not shapes, so an undeclared output shape lets a three-element tensor pass as one number while the import succeeds — the
/// caller receives something that looks fine.** **This layer infers the element count and closes that path.**
///
/// **When inference has no ground it says so: Unknown is not a value usable as 1, it means "there is no basis here", and the caller decides whether to continue instead of this layer
/// guessing a 1 and getting away with it.**
/// </remarks>
public static class OnnxShapePropagation
{
    /// <summary>
    /// 推断结果 / The inference result.
    /// </summary>
    /// <param name="Shapes">名字到形状的映射；形状为 `null` 表示推不出来 / the mapping from names to shapes, null meaning it cannot be inferred.</param>
    public sealed record Result(IReadOnlyDictionary<string, IReadOnlyList<int>?> Shapes)
    {
        /// <summary>取一个名字的形状 / The shape of one name.</summary>
        /// <param name="name">名字 / The name.</param>
        /// <returns>形状，或 `null` 表示推不出来 / the shape, or null when it cannot be inferred.</returns>
        public IReadOnlyList<int>? this[string name] => Shapes.TryGetValue(name, out IReadOnlyList<int>? shape) ? shape : null;

        /// <summary>取一个名字的元素个数 / The element count of one name.</summary>
        /// <param name="name">名字 / The name.</param>
        /// <returns>元素个数，或 `null` 表示推不出来 / the element count, or null when it cannot be inferred.</returns>
        public long? Elements(string name) {
            IReadOnlyList<int>? shape = this[name];
            return shape is null ? null : Count(shape);
        }
    }

    /// <summary>
    /// 推断整张图的形状 / Infer the shapes of a whole graph.
    /// </summary>
    /// <param name="graph">图 / The graph.</param>
    /// <returns>推断结果 / The result.</returns>
    public static Result Infer(OnnxGraph graph) {
        var shapes = new Dictionary<string, IReadOnlyList<int>?>(StringComparer.Ordinal);

        foreach (OnnxValueInfo input in graph.Inputs) {
            shapes[input.Name] = Declared(input);
        }
        foreach (OnnxTensor initializer in graph.Initializers) {
            if (initializer.Name is not null) {
                // 常量的形状来自 `dims`：**它是模型自己写的数，不需要推。**
                // A constant's shape comes from dims: the model wrote that number down itself, so nothing is inferred.
                shapes[initializer.Name] = initializer.Dims.Count == 0
                    ? (IReadOnlyList<int>?)Array.Empty<int>()
                    : ToInts(initializer.Dims);
            }
        }

        foreach (OnnxNode node in graph.Nodes) {
            IReadOnlyList<int>? produced = InferNode(node, shapes);
            foreach (string output in node.Outputs) {
                shapes[output] = produced;
            }
        }
        return new Result(shapes);
    }

    private static IReadOnlyList<int>? InferNode(OnnxNode node, IReadOnlyDictionary<string, IReadOnlyList<int>?> shapes) {
        switch (node.OpType) {
            case "Add":
            case "Sub":
            case "Mul": {
                if (node.Inputs.Count != 2) {
                    return null;
                }
                return Broadcast(ShapeOf(shapes, node.Inputs[0]), ShapeOf(shapes, node.Inputs[1]));
            }
            case "Sum": {
                IReadOnlyList<int>? running = Array.Empty<int>();
                foreach (string input in node.Inputs) {
                    running = Broadcast(running, ShapeOf(shapes, input));
                    if (running is null) {
                        return null;
                    }
                }
                return running;
            }
            case "Neg":
            case "Identity":
            case "Cast": {
                if (node.Inputs.Count != 1) {
                    return null;
                }
                return ShapeOf(shapes, node.Inputs[0]);
            }
            default:
                // 不认识的算子：**推不出来就是推不出来，不当成标量。**
                // An operator this layer does not know: cannot be inferred is cannot be inferred, and it is not treated as a scalar.
                return null;
        }
    }

    private static IReadOnlyList<int>? ShapeOf(IReadOnlyDictionary<string, IReadOnlyList<int>?> shapes, string name)
        => shapes.TryGetValue(name, out IReadOnlyList<int>? shape) ? shape : null;

    private static IReadOnlyList<int>? Broadcast(IReadOnlyList<int>? left, IReadOnlyList<int>? right) {
        if (left is null || right is null) {
            return null;
        }
        IReadOnlyList<int>? merged = OnnxShape.BroadcastShape(left, right, out _);
        return merged;
    }

    private static IReadOnlyList<int>? Declared(OnnxValueInfo info) {
        // **"有没有声明形状"看 `element_type`：** **模型写了类型，形状就是写下的那个（空形状表示标量）；**
        // **没写类型则是"没有声明"，** **那时把它当标量就是在猜。**
        //
        // **Whether a shape is declared is read off element_type: if the model wrote a type, the shape is what it wrote (an empty shape being a scalar); if it wrote none, nothing was
        // declared, and treating that as a scalar would be a guess.**
        if (info.ElementType == 0) {
            return null;
        }
        if (info.Shape.Count == 0) {
            return Array.Empty<int>();
        }
        var shape = new List<int>(info.Shape.Count);
        foreach (OnnxDimension dimension in info.Shape) {
            if (dimension.Value is not long value) {
                return null;
            }
            shape.Add((int)value);
        }
        return shape;
    }

    private static IReadOnlyList<int> ToInts(IReadOnlyList<long> values) {
        var result = new List<int>(values.Count);
        foreach (long value in values) {
            result.Add((int)value);
        }
        return result;
    }

    private static long Count(IReadOnlyList<int> shape) {
        long total = 1;
        foreach (int dimension in shape) {
            total *= System.Math.Max(dimension, 1);
        }
        return total;
    }
}