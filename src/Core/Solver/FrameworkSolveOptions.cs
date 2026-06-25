#nullable enable

using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Value;

namespace Fuookami.Ospf.Core.Solver;
/// <summary>
/// 框架级求解选项，用于列生成和 Benders 分解等高级求解器。
/// Framework-level solve options for advanced solvers like column generation and Benders decomposition.
/// </summary>
public sealed record FrameworkSolveOptions(
    SolveOptions? SolveOptions = null,
    bool LogModel = false,
    SolveValueConversionPolicy? ValueConversionPolicy = null) {
    /// <summary>有效的值转换策略 / Effective value conversion policy</summary>
    public SolveValueConversionPolicy EffectiveValueConversionPolicy
        => ValueConversionPolicy ?? SolveValueConversionPolicy.AllowRounding;
}
