#nullable enable

namespace Fuookami.Ospf.Core.Plugin.Lingo;

/// <summary>
/// LINGO 线性求解器占位符，与固定 Kotlin 基线一致，不声明求解能力。
/// LINGO linear solver placeholder matching the pinned Kotlin baseline, with no declared solving capability.
/// </summary>
/// <remarks>
/// Kotlin 固定基线尚未实现该模块，因此这里不实现 ILinearSolver，也不伪造环境错误入口。
/// The pinned Kotlin baseline has not implemented this module, so this type does not implement ILinearSolver or expose a fake environment-error entry.
/// </remarks>
public sealed class LingoLinearSolver {
}
