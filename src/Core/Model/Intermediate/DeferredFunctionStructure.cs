#nullable enable

namespace Fuookami.Ospf.Core.Model.Intermediate;

/// <summary>
/// 可延迟展开的结构节点 / A structure node whose expansion may be deferred.
/// </summary>
/// <remarks>
/// 对齐基线 `FunctionExpansion.kt` 的 `DeferredFunctionStructure`。
///
/// **这是一个<b>纯标记接口</b>，没有任何成员。**
///
/// **它存在的理由不是"将来会加方法"，而是<b>类型层面的集合划分</b>：**
/// **延迟展开的那条路径要处理"结构节点"这一类东西，而它不关心是
/// `IndicatorStructure`、`AbsStructure` 还是 `MaxStructure`。**
/// **没有这个标记，那条路径就只能用 `object` 或反射。**
///
/// **一个没有成员的接口在 C# 里是罕见的，所以必须写清它为什么存在——
/// 否则下一个人会以为它是没写完。**
///
/// Following the baseline's `DeferredFunctionStructure`.
///
/// **A <b>pure marker interface</b> with no members.**
///
/// **Its reason is not "methods will be added later" but a <b>type-level partition</b>:**
/// **the deferred-expansion path works with "structure nodes" without caring whether a node is an
/// `IndicatorStructure`, an `AbsStructure` or a `MaxStructure`.** **Without the marker that path would need `object` or
/// reflection.**
///
/// **A memberless interface is unusual in C#, so the reason has to be written down — otherwise the next reader will
/// think it is unfinished.**
/// </remarks>
public interface DeferredFunctionStructure;
