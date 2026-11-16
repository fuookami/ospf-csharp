#nullable enable

using System;
using Fuookami.Ospf.Core.Plugin.Gurobi;
using Fuookami.Ospf.Core.Plugin.Scip;
using Fuookami.Ospf.Core.Solver.Config;
using Fuookami.Ospf.Framework.Solver;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo5;

/// <summary>
/// demo5 列生成求解器构建器 / demo5 column-generation solver builder.
/// </summary>
/// <remarks>
///
/// **未知求解器名必须失败**。`when (solver.lowercase) { "scip" -> …; else -> Gurobi }`——
/// 写错一个名字（`"gruobi"`）会**静默拿到 Gurobi**，而错误要到后端不可用时才以一个无关的失败形式出现。
/// 这里改成显式枚举，未知名返回失败并指名它。
///
/// **An unknown solver name must fail.** The code is `when (solver.lowercase) { "scip" -> …; else -> Gurobi }` — a typo (`"gruobi"`) **silently
/// gets Gurobi**, and the mistake only surfaces later as an unrelated backend-unavailable failure. This version enumerates explicitly and reports the
/// unknown name.
///
/// **名字大小写不敏感**：`Gurobi`、`GUROBI`、`gurobi` 都指向同一后端。
/// **Names are case-insensitive** (matching the code): `Gurobi`, `GUROBI`, `gurobi` all select the same backend.
/// </remarks>
public static class Demo5SolverFactory {
    /// <summary>Gurobi 后端名 / The Gurobi backend name.</summary>
    public const string GurobiName = "gurobi";

    /// <summary>SCIP 后端名 / The SCIP backend name.</summary>
    public const string ScipName = "scip";

    /// <summary>
    /// 创建列生成求解器 / Create a column-generation solver.
    /// </summary>
    /// <param name="solver">求解器名称 / Solver name.</param>
    /// <param name="config">求解器配置 / Solver configuration.</param>
    /// <returns>列生成求解器或未知名称失败 / The solver, or an unknown-name failure.</returns>
    public static Result<IColumnGenerationSolver, ErrorCode, Error<ErrorCode>> Create(
        string solver,
        SolverConfig? config = null) {
        ArgumentNullException.ThrowIfNull(solver);
        // **后端实例自己会携带"SDK 不可用"状态**：这里不去探测本机是否装了 SDK，否则工厂的返回值就依赖构建机器，
        // 而同一份代码在两台机器上给出不同的可求解性——**这种差异不会出现在任何一处类型里**。
        // **The backend instance carries its own "SDK unavailable" state**: this factory does not probe the machine for an SDK, because then its result
        // would depend on the build machine and the same code would report different solvability on two machines — **a difference that appears in no
        // type anywhere**.
        return solver.Trim().ToLowerInvariant() switch {
            GurobiName => Results.Ok<IColumnGenerationSolver>(
                new GurobiColumnGenerationSolver(config)),
            ScipName => Results.Ok<IColumnGenerationSolver>(
                new ScipColumnGenerationSolver(config)),
            _ => Results.Failed<IColumnGenerationSolver>(
                new Err<ErrorCode>(
                    code: ErrorCode.IllegalArgument,
                    message: $"未知求解器：{solver} / Unknown solver: {solver}")),
        };
    }
}
