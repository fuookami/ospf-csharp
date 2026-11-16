// VRPTW 应用服务 / The VRPTW application service.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Fuookami.Ospf.Framework.NetworkScheduling.Application.Model;
using Fuookami.Ospf.Framework.Solver;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Service;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<
    Fuookami.Ospf.Utils.Functional.Success,
    Fuookami.Ospf.Utils.Error.ErrorCode,
    Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Application.Service;

/// <summary>
/// VRPTW 应用服务 / The VRPTW application service.
/// </summary>
/// <remarks>
/// **校验先于构造**。实例在进入分支定价前先整体复核一次；把校验留到算法内部，第一个非法客户只会表现为某条路径上的
/// `NaN` 或者一个"看起来可行"的方案。
/// **Validation precedes construction.** The instance is fully re-checked before branch-and-price starts; deferring it into the
/// algorithm would surface the first illegal customer as a `NaN` on some route or as a "feasible-looking" plan.
/// **正常算法终态返回 `Ok`，只有输入 / 单位换算 / solver 调用 / 对偶证书失败才返回 `Failed`**。把 `Infeasible` 之类当作错误会让
/// 调用方把"问题无解"和"服务坏了"混为一谈。
/// **Normal algorithm terminal states return `Ok`; only input, unit conversion, solver-call or dual-certificate failures return
/// `Failed`.** Treating `Infeasible` and friends as errors would conflate "the problem has no solution" with "the service broke".
/// **enricher 按顺序串联，且只能看到已组装的结果**。它们拿不到内部算法对象，因此无法绕过校验改写下界或轨迹。
/// **Enrichers are applied in order and only see the assembled result.** They never receive internal algorithm objects, so they
/// cannot bypass validation to rewrite bounds or the trace.
/// </remarks>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed class VrptwApplicationService<V> where V : struct, IFloatingNumber<V> {
    private readonly VrptwInstance<V> _instance;
    private readonly IColumnGenerationSolver _solver;
    private readonly BranchAndPriceAlgorithm<V>.Configuration _configuration;
    private readonly BranchAndPriceAlgorithm<V>.Policy _policy;
    private readonly IReadOnlyList<SolutionEnricher<V>> _solutionEnrichers;

    /// <summary>
    /// 构造 VRPTW 应用服务 / Construct the VRPTW application service.
    /// </summary>
    /// <param name="instance">实例 / The instance.</param>
    /// <param name="solver">列生成求解器 / The column-generation solver.</param>
    /// <param name="configuration">算法配置 / The algorithm configuration.</param>
    /// <param name="policy">策略注入 / The policy injection.</param>
    /// <param name="solutionEnrichers">结果增强器 / The solution enrichers.</param>
    public VrptwApplicationService(
        VrptwInstance<V> instance,
        IColumnGenerationSolver solver,
        BranchAndPriceAlgorithm<V>.Configuration configuration,
        BranchAndPriceAlgorithm<V>.Policy policy,
        IReadOnlyList<SolutionEnricher<V>>? solutionEnrichers = null) {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(solver);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(policy);
        _instance = instance;
        _solver = solver;
        _configuration = configuration;
        _policy = policy;
        _solutionEnrichers = solutionEnrichers ?? Array.Empty<SolutionEnricher<V>>();
    }

    /// <summary>
    /// 求解 VRPTW 实例 / Solve the VRPTW instance.
    /// </summary>
    /// <returns>求解结果 / The solve result.</returns>
    public async Task<Result<VrptwSolveResult<V>, ErrorCode, Error<ErrorCode>>> Solve() {
        // 1. 校验输入 / Validate the input.
        Try validation = VrptwValidator.Validate(_instance);
        if (validation is Failed<Success, ErrorCode, Error<ErrorCode>> validationFailure) {
            return Results.Failed<VrptwSolveResult<V>>(validationFailure.Error);
        }

        // 2. 构造算法 / Construct the algorithm.
        var algorithm = new BranchAndPriceAlgorithm<V>(
            _instance, _solver, _configuration, _policy);

        // 3. 调用求解 / Invoke the solve.
        Result<BranchAndPriceResult<V>, ErrorCode, Error<ErrorCode>> solved = await algorithm.Solve();
        if (solved is Failed<BranchAndPriceResult<V>, ErrorCode, Error<ErrorCode>> solveFailure) {
            return Results.Failed<VrptwSolveResult<V>>(solveFailure.Error);
        }
        BranchAndPriceResult<V> result =
            ((Ok<BranchAndPriceResult<V>, ErrorCode, Error<ErrorCode>>)solved).Value;

        // 4. 映射结果 / Map the result.
        var solveResult = new VrptwSolveResult<V>(
            result.Status,
            result.Solution,
            result.LowerBound,
            result.UpperBound,
            result.Trace.RelativeGap,
            result.Trace);

        // 5. 应用 enricher / Apply the enrichers.
        foreach (SolutionEnricher<V> enricher in _solutionEnrichers) {
            solveResult = enricher(solveResult);
        }
        return Results.Ok(solveResult);
    }
}
