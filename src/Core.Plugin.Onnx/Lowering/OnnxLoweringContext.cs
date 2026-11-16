#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Polynomial;

namespace Fuookami.Ospf.Core.Plugin.Onnx.Lowering;

/// <summary>
/// **降阶上下文：一张名字到表达式的表，加一层作用域** / The lowering context: names bound to expressions, plus scopes.
/// </summary>
/// <remarks>
/// `OnnxLoweringContext` 的最小形态。**它是子图展开的前提：** **`If`／`Loop`／`Scan` 的子图有自己的一张表，
/// 又必须能看见外层张量——** **"看得见外层、写不进外层"就是作用域要保证的全部。**
///
/// **为什么单独一层而不是一个大字典：** **一个大字典会让子图的中间结果泄漏到外层，** **而泄漏的表现是"外层某处引用到了一个它不该看见的名字"，**
/// **这在几百个节点的图里几乎无法定位。**
///
/// Syncs the minimal shape of `OnnxLoweringContext`. **It is the prerequisite for unrolling subgraphs: an `If`/`Loop`/`Scan` body has its own table while it must also
/// see the enclosing tensors — "reads outward, writes inward" is the whole of what scopes have to guarantee.**
///
/// **Why a real scope rather than one big dictionary: a single dictionary lets a subgraph's intermediates leak outward, and the symptom is "somewhere outside
/// references a name it should not see", which is close to impossible to locate in a graph of a few hundred nodes.**
/// </remarks>
public sealed class OnnxLoweringContext
{
    /// <summary>
    /// 一个绑定 / One binding.
    /// </summary>
    /// <param name="Expression">表达式 / The expression.</param>
    /// <param name="Shape">形状 / The shape.</param>
    public sealed record Binding(LinearPolynomial<Flt64> Expression, IReadOnlyList<int> Shape)
    {
        /// <summary>
        /// 逐元素列表（可选）/ The per-element list, when one exists.
        /// </summary>
        /// <remarks>
        /// **可空是有意的：** **绝大多数绑定是"一个名字一个标量表达式"，** **只有需要重排或切片时才必须有元素列表。** '
        /// **于是这一层能逐步接进来，而不是一次改遍每个调用点——** **也正因为可空，"有没有元素"本身成为一个可判断的事实，**
        /// **拒绝消息才能说"取不到逐轮切片"，而不是猜。**
        ///
        /// **Nullable on purpose: most bindings are "one name, one scalar expression", and only a reordering or a slice needs the element list.** **That lets this layer be wired in
        /// step by step instead of touching every call site — and because it is nullable, "are there elements" is a fact that can be checked, so a refusal can say "no slices
        /// available" instead of guessing.**
        /// </remarks>
        public OnnxElementList? Elements { get; init; }

        /// <summary>是否带逐元素列表 / Whether an element list is attached.</summary>
        public bool HasElements => Elements is not null;

        /// <summary>
        /// 二次域的元素列表（可选）/ The second-degree element list, when one exists.
        /// </summary>
        /// <remarks>
        /// **两个域互斥：** **一个绑定要么装一次、要么装二次，** **不会同时有两种——** **否则"这个张量现在是几次"就没有答案，** '
        /// **而下游只能猜。**
        ///
        /// **The two domains are exclusive: a binding holds either first-degree or second-degree forms, never both — otherwise "what degree is this tensor now" has no answer and every
        /// downstream has to guess.**
        /// </remarks>
        public OnnxQuadraticElementList? QuadraticElements { get; init; }

        /// <summary>是否带二次域列表 / Whether a second-degree list is attached.</summary>
        public bool HasQuadraticElements => QuadraticElements is not null;
    }

    private readonly List<Dictionary<string, Binding>> _scopes = new() { new Dictionary<string, Binding>(StringComparer.Ordinal) };

    /// <summary>当前嵌套深度（1 表示只有最外层）/ The current depth, 1 meaning the root scope only.</summary>
    public int Depth => _scopes.Count;

    /// <summary>
    /// 绑一个名字到表达式 / Bind a name to an expression.
    /// </summary>
    /// <param name="name">名字 / The name.</param>
    /// <param name="expression">表达式 / The expression.</param>
    /// <param name="shape">形状 / The shape.</param>
    /// <param name="failure">失败原因 / The failure reason.</param>
    /// <param name="elements">逐元素列表（可选）/ The per-element list, when one exists.</param>
    /// <param name="quadraticElements">二次域列表（可选，与前者互斥）/ The second-degree list when one exists, exclusive with the former.</param>
    /// <returns>是否绑定成功 / Whether the binding was made.</returns>
    /// <remarks>
    /// **同一作用域内重名直接拒绝：** **ONNX 的输出名在同一层是唯一的，** **重名意味着模型非法或我们读错了某个字段——**
    /// **而静默覆盖会把先写的那个节点的结果悄悄换掉。**
    ///
    /// **A duplicate within one scope is refused outright: ONNX output names are unique per level, so a duplicate means either an illegal model or a misread field, and
    /// silently overwriting swaps out the earlier node's result.**
    /// </remarks>
    public bool TryBind(
        string name,
        LinearPolynomial<Flt64> expression,
        IReadOnlyList<int> shape,
        out string? failure,
        OnnxElementList? elements = null,
        OnnxQuadraticElementList? quadraticElements = null) {
        if (string.IsNullOrEmpty(name)) {
            failure = "绑定缺少名字 / a binding has no name";
            return false;
        }
        Dictionary<string, Binding> current = _scopes[^1];
        if (current.ContainsKey(name)) {
            failure = "名字 '" + name + "' 在同一个作用域内被重复定义 / the name is defined twice in one scope";
            return false;
        }
        current[name] = new Binding(expression, shape ?? Array.Empty<int>()) { Elements = elements, QuadraticElements = quadraticElements };
        failure = null;
        return true;
    }

    /// <summary>
    /// 覆盖绑定一个名字（就地更新）/ Overwrite a binding, for in-place updates.
    /// </summary>
    /// <param name="name">名字 / The name.</param>
    /// <param name="expression">表达式 / The expression.</param>
    /// <param name="shape">形状 / The shape.</param>
    /// <param name="failure">失败原因 / The failure reason.</param>
    /// <param name="elements">逐元素列表（可选）/ The per-element list, when one exists.</param>
    /// <param name="quadraticElements">二次域列表（可选，与前者互斥）/ The second-degree list when one exists, exclusive with the former.</param>
    /// <returns>是否绑定成功 / Whether the binding was made.</returns>
    /// <remarks>
    /// **与 `TryBind` 的分工是语义上的，不是宽松程度上的：** **`TryBind` 用于"声明一个名字"，重名说明模型非法；** '
    /// **`TryRebind` 用于"这个名字现在指的是新值"——** **ONNX 允许节点输出与输入同名（就地更新），'
    /// **而那个名字在此之后的含义就是新值。** **重复的**节点输出**仍由图校验拦截，所以这里放宽不会让一个非法模型蒙混过关。**
    ///
    /// **The split with `TryBind` is semantic, not a matter of strictness: TryBind declares a name, so a duplicate means an illegal model; TryRebind says "this name now
    /// means the new value" — ONNX allows a node output to share an input's name (in-place update) and the name means the new value after it.** Duplicate **node outputs**
    /// are still caught by graph validation, so relaxing here cannot let an illegal model through.
    /// </remarks>
    public bool TryRebind(
        string name,
        LinearPolynomial<Flt64> expression,
        IReadOnlyList<int> shape,
        out string? failure,
        OnnxElementList? elements = null,
        OnnxQuadraticElementList? quadraticElements = null) {
        if (string.IsNullOrEmpty(name)) {
            failure = "绑定缺少名字 / a binding has no name";
            return false;
        }
        // 覆盖发生在当前作用域：外层同名不会被改，于是"体里就地更新"不会污染外层。
        // The overwrite happens in the current scope: an outer name of the same is untouched, so an in-place update inside a body cannot pollute the enclosing scope.
        _scopes[^1][name] = new Binding(expression, shape ?? Array.Empty<int>()) { Elements = elements, QuadraticElements = quadraticElements };
        failure = null;
        return true;
    }

    /// <summary>
    /// 查一个名字 / Look a name up.
    /// </summary>
    /// <param name="name">名字 / The name.</param>
    /// <param name="binding">绑定 / The binding.</param>
    /// <returns>是否找到 / Whether it was found.</returns>
    /// <remarks>
    /// **从内往外找：** **内层遮蔽外层是子图的正常语义，** **而反过来会让子图里的中间变量被外层同名张量顶替。**
    ///
    /// **Lookup runs inward out: an inner scope shadowing an outer one is normal subgraph semantics, and the reverse would let an outer tensor of the same name take
    /// over a subgraph's intermediate.**
    /// </remarks>
    public bool TryGet(string name, out Binding binding) {
        for (int index = _scopes.Count - 1; index >= 0; --index) {
            if (_scopes[index].TryGetValue(name, out Binding? found)) {
                binding = found;
                return true;
            }
        }
        binding = null!;
        return false;
    }

    /// <summary>
    /// 进入一层作用域 / Enter a nested scope.
    /// </summary>
    public void PushScope() => _scopes.Add(new Dictionary<string, Binding>(StringComparer.Ordinal));

    /// <summary>
    /// 退出一层作用域 / Leave the innermost scope.
    /// </summary>
    /// <param name="failure">失败原因 / The failure reason.</param>
    /// <returns>是否退出成功 / Whether the scope was popped.</returns>
    /// <remarks>
    /// **最外层不可弹出：** **弹掉根层会让整张表消失，** **而之后的每次查找都"找不到"——** **报错的地方会离真正出错的地方很远。**
    ///
    /// **The root scope cannot be popped: popping it removes the whole table, every later lookup fails, and the error surfaces far from where it happened.**
    /// </remarks>
    public bool TryPopScope(out string? failure) {
        if (_scopes.Count <= 1) {
            failure = "最外层作用域不可退出 / the root scope cannot be popped";
            return false;
        }
        _scopes.RemoveAt(_scopes.Count - 1);
        failure = null;
        return true;
    }

    /// <summary>
    /// 该名字是否定义在当前作用域内 / Whether the name is defined in the current scope.
    /// </summary>
    /// <param name="name">名字 / The name.</param>
    /// <returns>是否属于本层 / Whether it belongs to this level.</returns>
    public bool IsLocal(string name) => _scopes[^1].ContainsKey(name);
}