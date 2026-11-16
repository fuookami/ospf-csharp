#nullable enable

namespace Fuookami.Ospf.Core.Plugin.Optverse;

/// <summary>
/// OPTVerse 线性求解器占位符，不声明求解能力。
/// OPTVerse linear solver placeholder with no declared solving capability.
/// </summary>
/// <remarks>
/// 该模块尚未实现，因此这里不实现 ILinearSolver，也不伪造环境错误入口。
/// This module is not implemented yet, so this type does not implement ILinearSolver or expose a fake environment-error entry.
/// </remarks>
public sealed class OptverseLinearSolver {
}
