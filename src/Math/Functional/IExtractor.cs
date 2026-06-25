#nullable enable

using Fuookami.Ospf.Utils.Functional;
using System;

namespace Fuookami.Ospf.Math.Functional;
/// <summary>
/// 提取器接口 / Extractor interface.
/// Kotlin fun interface Extractor&lt;TIn, TOut&gt; { operator fun invoke(x: TIn): TOut }.
/// 与 phase-3 的 Extractor&lt;TOut,TIn&gt;=Func&lt;TIn,TOut&gt; 委托别名互转。
/// </summary>
public interface IExtractor<in TIn, out TOut> {
    /// <summary>对输入执行一步提取 / Extract one output from the input.</summary>
    TOut Invoke(TIn input);
}

/// <summary>
/// 生成器接口 / Generator interface.
/// Kotlin fun interface Generator&lt;T&gt; { operator fun invoke(): T }.
/// 返回当前状态并推进一次 / Returns the current state, then advances by one step.
/// </summary>
public interface IGenerator<out T> {
    /// <summary>返回当前状态并推进 / Return current state and advance.</summary>
    T Invoke();
}
