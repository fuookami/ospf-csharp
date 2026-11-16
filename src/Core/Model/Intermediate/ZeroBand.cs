#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Core.Variable;

namespace Fuookami.Ospf.Core.Model.Intermediate;

/// <summary>
/// 零带：容差，以及"当前落在哪一侧"的那个变量 / A zero band: a tolerance and the variable saying which side we are on.
/// </summary>
/// <remarks>
/// 对齐。
///
/// **它是一个纯数据载体，不做校验**——。
/// **"负容差要拒绝"这条校验放在使用点**（`DeadZoneFunction` 已经在自己的构造函数里拒绝负的 `delta`），
/// **而不是放在载体里**：**载体拿不到值转换器，也就拿不到零，于是只能靠调用方传进来的比较语义。**
/// **把校验硬塞进来会多要一个参数，而那个参数的唯一用途是让载体能算零——不值得。**
///
/// **语义也不写在载体里**：**同一个容差经常被好几个函数共用，若把"多近算零"写在载体里，
/// 各函数就没法各自解释它。**
///
/// Following its.
///
/// **It is a pure data carrier and validates nothing**, matching the code.
/// **The "reject a negative tolerance" check lives at the use site** (`DeadZoneFunction` already rejects a negative
/// `delta` in its own constructor), **not in the carrier**: **the carrier has no value converter and therefore no zero,
/// so it could only rely on comparison semantics handed to it.** **Forcing the check in would need one more parameter
/// whose only job is letting the carrier compute zero — not worth it.**
///
/// **The meaning is not written into the carrier either**: **one tolerance is often shared by several functions, and
/// putting "how close counts as zero" in the carrier would stop each interpreting it differently.**
/// </remarks>
public sealed record ZeroBand<V>(V Tolerance, IVariableItem SideVariable)
    where V : struct, IRealNumber<V>;
