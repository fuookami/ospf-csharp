// 应用服务层 / Application service layer.
// 与固定 Kotlin （926 字节）、（6780 字节）对应。
// And.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Framework.NetworkScheduling.Application.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Design;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Design.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Flow;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.GeneralizedFlow.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Allocation;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.PathCompilation;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Structure;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Structure.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Infrastructure;
using Fuookami.Ospf.Framework.Solver;
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
/// 解后处理器 / Solution post-processor.
/// </summary>
/// <remarks>
/// 在 <c>VrptwApplicationService.Solve</c> 末尾调用，可对求解结果做后处理（添加汇总 KPI、单位换算、记录日志等），而不必改动主流程。
/// Called at the end of <c>VrptwApplicationService.Solve</c>, so the solve result can be post-processed (aggregate KPIs, unit
/// conversion, logging) without touching the main flow.
/// **多个 enricher 按声明顺序依次作用，后一个看到的是前一个的输出**。因此顺序是语义的一部分，不是无关细节——先换算单位再算 KPI 与
/// 反过来的结果不同。
/// **Multiple enrichers apply in declaration order, each seeing the previous one's output.** Order is therefore part of the
/// semantics rather than an incidental detail: converting units before computing KPIs gives a different result than the reverse.
/// </remarks>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <param name="result">待增强的求解结果 / The solve result to enrich.</param>
/// <returns>增强后的求解结果 / The enriched solve result.</returns>
public delegate VrptwSolveResult<V> SolutionEnricher<V>(VrptwSolveResult<V> result)
    where V : struct, IRealNumber<V>;

/// <summary>
/// 独立求解终态；无 incumbent 时保留报告而不尝试提取业务量 / Standalone outcome, retaining reports without an incumbent.
/// </summary>
/// <typeparam name="S">业务解类型 / Business-solution type.</typeparam>
/// <param name="Report">求解器边界报告 / The solver-boundary report.</param>
/// <param name="Solution">可选业务解 / The optional business solution.</param>
public sealed record NetworkSolveResult<S>(SolveReport<Flt64> Report, S? Solution);

/// <summary>
/// 共用装配入口，不在 application 内重复领域约束 / Shared orchestration without duplicating domain constraints.
/// </summary>
/// <remarks>
/// **求解器报告与业务解是两件事**：报告来自 solver 边界，业务解来自领域提取。没有 incumbent 时只返回报告、不提取——强行提取会让
/// 空解被当成"解出 0 个对象"，上层无法区分"确实为空"与"根本没有解"。
/// **The solver report and the business solution are two different things**: the report comes from the solver boundary, the
/// business solution from domain extraction. Without an incumbent only the report is returned and nothing is extracted — forcing
/// extraction would turn an empty solution into "solved zero objects", and the layer above could not tell "genuinely empty" from
/// "no solution at all".
/// **维度必须校验**：solver 声称有解却给出与 `TokensInSolver` 数量不符的向量时，逐位赋值会静默错位，把某个变量的值写到另一个变量上。
/// **The dimension must be checked**: when the solver claims an incumbent but hands back a vector whose length disagrees with
/// `TokensInSolver`, positional assignment silently misaligns and writes one variable's value into another.
/// </remarks>
public static class NetworkSolver {
    /// <summary>
    /// 共用装配入口 / The shared orchestration entry.
    /// </summary>
    /// <typeparam name="S">业务解类型 / Business-solution type.</typeparam>
    /// <param name="name">模型名称 / Model name.</param>
    /// <param name="solver">求解器 / The solver.</param>
    /// <param name="register">注册回调 / The registration callback.</param>
    /// <param name="extract">提取回调 / The extraction callback.</param>
    /// <returns>结构化终态 / The structured outcome.</returns>
    public static async Task<Result<NetworkSolveResult<S>, ErrorCode, Error<ErrorCode>>> SolveNetwork<S>(
    string name,
    IColumnGenerationSolver solver,
    Func<LinearMetaModel<Flt64>, Try> register,
    Func<LinearMetaModel<Flt64>, Result<S, ErrorCode, Error<ErrorCode>>> extract) {
    ArgumentNullException.ThrowIfNull(name);
    ArgumentNullException.ThrowIfNull(solver);
    ArgumentNullException.ThrowIfNull(register);
    ArgumentNullException.ThrowIfNull(extract);
    var model = new LinearMetaModel<Flt64>(name);
    try {
        Try registered = register(model);
        if (registered is Failed<Success, ErrorCode, Error<ErrorCode>> registrationFailure) {
            return Results.Failed<NetworkSolveResult<S>>(registrationFailure.Error);
        }
        Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> solved =
            await solver.SolveMILPAsync(name, model);
        if (solved is Failed<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> solveFailure) {
            return Results.Failed<NetworkSolveResult<S>>(solveFailure.Error);
        }
        FeasibleSolverOutput<Flt64> output =
            ((Ok<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>)solved).Value;
        var report = output.ToSolveReport();
        if (report.SolutionPresence == SolutionPresence.None) {
            return Results.Ok(new NetworkSolveResult<S>(report, default));
        }
        IReadOnlyList<Flt64> values = report.Solution!.Values.Values;
        if (values.Count != model.Tokens.TokensInSolver.Count) {
            return Results.Failed<NetworkSolveResult<S>>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "求解器解维度不匹配 / Solver solution dimension mismatch"));
        }
        model.SetSolution(values);
        Result<S, ErrorCode, Error<ErrorCode>> extracted = extract(model);
        if (extracted is Failed<S, ErrorCode, Error<ErrorCode>> extractionFailure) {
            return Results.Failed<NetworkSolveResult<S>>(extractionFailure.Error);
        }
        return Results.Ok(new NetworkSolveResult<S>(
            report,
            ((Ok<S, ErrorCode, Error<ErrorCode>>)extracted).Value));
    }
    finally {
        model.Dispose();
    }
    }
}

/// <summary>
/// 独立运输分配与中转求解入口 / Standalone transportation and transshipment entry.
/// </summary>
/// <remarks>
/// **每次求解创建独立注册态**；嵌入业务流程时应直接使用 <see cref="FlowContext{V}"/>，而不是在这里反复建服务对象——注册态与问题
/// 快照必须一一对应，复用会让两次求解互相污染。
/// **Each solve creates fresh registration state**; embedded business flows should use <see cref="FlowContext{V}"/> directly
/// rather than rebuilding service objects here — registration state must map one-to-one to a problem snapshot, and reuse would let
/// two solves contaminate each other.
/// </remarks>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed class NetworkFlowApplicationService<V> where V : struct, IRealNumber<V> {
    private readonly IColumnGenerationSolver _solver;
    private readonly INetworkSchedulingSolverValueAdapter<V> _adapter;

    /// <summary>
    /// 构造流应用服务 / Construct the flow application service.
    /// </summary>
    /// <param name="solver">求解器 / The solver.</param>
    /// <param name="adapter">数值转换边界 / The numeric conversion boundary.</param>
    public NetworkFlowApplicationService(IColumnGenerationSolver solver, INetworkSchedulingSolverValueAdapter<V> adapter) {
        ArgumentNullException.ThrowIfNull(solver);
        ArgumentNullException.ThrowIfNull(adapter);
        _solver = solver;
        _adapter = adapter;
    }

    /// <summary>
    /// 求解流问题 / Solve a flow problem.
    /// </summary>
    /// <param name="problem">流问题 / The flow problem.</param>
    /// <param name="extra">追加模型规则 / Additional model rules.</param>
    /// <returns>结构化终态 / The structured outcome.</returns>
    public Task<Result<NetworkSolveResult<FlowSolution<V>>, ErrorCode, Error<ErrorCode>>> Solve(
        FlowProblem<V> problem,
        Func<FlowContext<V>, LinearMetaModel<Flt64>, Try>? extra = null) {
        ArgumentNullException.ThrowIfNull(problem);
        var context = new FlowContext<V>(problem, _adapter);
        return NetworkSolver.SolveNetwork(
            context.Name,
            _solver,
            model => Chain(context.Register(model), extra, context, model),
            context.ExtractSolution);
    }

    /// <summary>组合领域注册与额外规则 / Chain domain registration with extra rules.</summary>
    /// <param name="domain">领域注册结果 / The domain registration result.</param>
    /// <param name="extra">额外规则 / The extra rules.</param>
    /// <param name="context">上下文 / The context.</param>
    /// <param name="model">模型 / The model.</param>
    /// <returns>组合结果 / The combined result.</returns>
    private static Try Chain<TContext>(
        Try domain,
        Func<TContext, LinearMetaModel<Flt64>, Try>? extra,
        TContext context,
        LinearMetaModel<Flt64> model) {
        if (domain is Failed<Success, ErrorCode, Error<ErrorCode>> failure) {
            return Results.Failed<Success>(failure.Error);
        }
        return extra is null ? Results.Ok(Results.SuccessInstance) : extra(context, model);
    }
}

/// <summary>
/// 独立设施、通道与扩容求解入口 / Standalone facility, service and expansion entry.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed class NetworkDesignApplicationService<V> where V : struct, IRealNumber<V> {
    private readonly IColumnGenerationSolver _solver;
    private readonly INetworkSchedulingSolverValueAdapter<V> _adapter;

    /// <summary>
    /// 构造建设应用服务 / Construct the design application service.
    /// </summary>
    /// <param name="solver">求解器 / The solver.</param>
    /// <param name="adapter">数值边界 / The numeric boundary.</param>
    public NetworkDesignApplicationService(
        IColumnGenerationSolver solver,
        INetworkSchedulingSolverValueAdapter<V> adapter) {
        ArgumentNullException.ThrowIfNull(solver);
        ArgumentNullException.ThrowIfNull(adapter);
        _solver = solver;
        _adapter = adapter;
    }

    /// <summary>
    /// 求解建设问题 / Solve a design problem.
    /// </summary>
    /// <param name="problem">建设问题 / The design problem.</param>
    /// <param name="extra">追加模型规则 / Additional model rules.</param>
    /// <returns>结构化终态 / The structured outcome.</returns>
    public Task<Result<NetworkSolveResult<NetworkDesignSolution<V>>, ErrorCode, Error<ErrorCode>>> Solve(
        NetworkDesignProblem<V> problem,
        Func<NetworkDesignContext<V>, LinearMetaModel<Flt64>, Try>? extra = null) {
        ArgumentNullException.ThrowIfNull(problem);
        var context = new NetworkDesignContext<V>(problem, _adapter);
        return NetworkSolver.SolveNetwork(
            context.Name,
            _solver,
            model => Chain(context.Register(model), extra, context, model),
            context.ExtractSolution);
    }

    /// <summary>组合领域注册与额外规则 / Chain domain registration with extra rules.</summary>
    private static Try Chain<TContext>(
        Try domain,
        Func<TContext, LinearMetaModel<Flt64>, Try>? extra,
        TContext context,
        LinearMetaModel<Flt64> model) {
        if (domain is Failed<Success, ErrorCode, Error<ErrorCode>> failure) {
            return Results.Failed<Success>(failure.Error);
        }
        return extra is null ? Results.Ok(Results.SuccessInstance) : extra(context, model);
    }
}

/// <summary>
/// 有根网络结构与设施归属的独立求解入口 / Standalone rooted-network structure and facility-ownership entry.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed class NetworkStructureApplicationService<V> where V : struct, IRealNumber<V> {
    private readonly IColumnGenerationSolver _solver;
    private readonly INetworkSchedulingSolverValueAdapter<V> _adapter;

    /// <summary>
    /// 构造结构应用服务 / Construct the structure application service.
    /// </summary>
    /// <param name="solver">求解器 / The solver.</param>
    /// <param name="adapter">数值边界 / The numeric boundary.</param>
    public NetworkStructureApplicationService(
        IColumnGenerationSolver solver,
        INetworkSchedulingSolverValueAdapter<V> adapter) {
        ArgumentNullException.ThrowIfNull(solver);
        ArgumentNullException.ThrowIfNull(adapter);
        _solver = solver;
        _adapter = adapter;
    }

    /// <summary>
    /// 求解网络结构问题 / Solve a network structure problem.
    /// </summary>
    /// <param name="problem">结构问题 / The structure problem.</param>
    /// <param name="extra">追加模型规则 / Additional model rules.</param>
    /// <returns>结构化终态 / The structured outcome.</returns>
    public Task<Result<NetworkSolveResult<NetworkStructureSolution<V>>, ErrorCode, Error<ErrorCode>>> Solve(
        NetworkStructureProblem<V> problem,
        Func<NetworkStructureContext<V>, LinearMetaModel<Flt64>, Try>? extra = null) {
        ArgumentNullException.ThrowIfNull(problem);
        var context = new NetworkStructureContext<V>(problem, _adapter);
        return NetworkSolver.SolveNetwork(
            context.Name,
            _solver,
            model => Chain(context.Register(model), extra, context, model),
            context.ExtractSolution);
    }

    /// <summary>组合领域注册与额外规则 / Chain domain registration with extra rules.</summary>
    private static Try Chain<TContext>(
        Try domain,
        Func<TContext, LinearMetaModel<Flt64>, Try>? extra,
        TContext context,
        LinearMetaModel<Flt64> model) {
        if (domain is Failed<Success, ErrorCode, Error<ErrorCode>> failure) {
            return Results.Failed<Success>(failure.Error);
        }
        return extra is null ? Results.Ok(Results.SuccessInstance) : extra(context, model);
    }
}

/// <summary>
/// 通用整条路径选择，不在应用层重新构造覆盖或资源约束 / Whole-path selection without rebuilding domain rows in application.
/// </summary>
/// <remarks>
/// **只求解有限候选集；solver 最优状态不证明全图或定价域最优**。结果必须连同 `CandidateIds` 一起理解。
/// **Only a finite candidate pool is solved; the solver's optimal status proves nothing about the full network or the pricing
/// domain.** The result must be read together with `CandidateIds`.
/// **不在应用层重建领域行**。覆盖、资源与配额行都由 <see cref="PathCompilationContext{V, C}"/> 管理；应用层只负责装配与提取。
/// **Domain rows are not rebuilt here.** Coverage, resource and quota rows belong to
/// <see cref="PathCompilationContext{V, C}"/>; this layer only assembles and extracts.
/// </remarks>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed class NetworkPathApplicationService<V> where V : struct, IRealNumber<V> {
    private readonly IColumnGenerationSolver _solver;
    private readonly INetworkSchedulingSolverValueAdapter<V> _adapter;

    /// <summary>
    /// 构造路径应用服务 / Construct the path application service.
    /// </summary>
    /// <param name="solver">求解器 / The solver.</param>
    /// <param name="adapter">数值边界 / The numeric boundary.</param>
    public NetworkPathApplicationService(
        IColumnGenerationSolver solver,
        INetworkSchedulingSolverValueAdapter<V> adapter) {
        ArgumentNullException.ThrowIfNull(solver);
        ArgumentNullException.ThrowIfNull(adapter);
        _solver = solver;
        _adapter = adapter;
    }

    /// <summary>
    /// 求解有限候选集 / Solve a finite candidate pool.
    /// </summary>
    /// <typeparam name="C">完整路径载荷类型 / Complete path payload type.</typeparam>
    /// <param name="problem">当前候选集合与业务行 / The current candidate pool and business rows.</param>
    /// <param name="extra">额外装配钩子；参与定价的行应声明在 problem 中 / Extra assembly hook; priced rows belong in the problem.</param>
    /// <returns>只对最终候选集合有效的结果 / An outcome valid only for the final candidate pool.</returns>
    public Task<Result<NetworkSolveResult<PathCompilationSolution<V, C>>, ErrorCode, Error<ErrorCode>>>
        Solve<C>(
            PathCompilationProblem<V, C> problem,
            Func<PathCompilationContext<V, C>, LinearMetaModel<Flt64>, Try>? extra = null) {
        ArgumentNullException.ThrowIfNull(problem);
        var context = new PathCompilationContext<V, C>(problem, _adapter);
        return NetworkSolver.SolveNetwork(
            context.Name,
            _solver,
            model => ChainPath(context.Register(model), extra, context, model),
            context.ExtractSolution);
    }

    /// <summary>组合领域注册与额外规则 / Chain domain registration with extra rules.</summary>
    private static Try ChainPath<TContext>(
        Try domain,
        Func<TContext, LinearMetaModel<Flt64>, Try>? extra,
        TContext context,
        LinearMetaModel<Flt64> model) {
        if (domain is Failed<Success, ErrorCode, Error<ErrorCode>> failure) {
            return Results.Failed<Success>(failure.Error);
        }
        return extra is null ? Results.Ok(Results.SuccessInstance) : extra(context, model);
    }
}

/// <summary>
/// 分配、运输及中转的独立求解编排 / Standalone orchestration for allocation, transportation and transshipment.
/// </summary>
/// <remarks>
/// **只求解给定业务输入，不在应用层重建领域行**。服务窗口、配额、枢纽启停与供需平衡都由
/// <see cref="AllocationContext{V}"/> 管理；应用层重建一次就会有第二套口径。
/// **Only the given business input is solved; domain rows are not rebuilt here.** Service windows, quotas, hub activation and
/// supply-demand balance all belong to <see cref="AllocationContext{V}"/>; rebuilding them here would create a second set of rules.
/// **构造失败必须在装配前返回**。分配上下文在校验阶段就会拒绝重复标识、未知引用与非法数量；把这些留到装配后，模型会带着残缺行进场。
/// **Construction failures must return before assembly.** The allocation context already rejects duplicate identities, unknown
/// references and illegal amounts during validation; deferring them to assembly lets a model enter with half its rows missing.
/// </remarks>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed class NetworkAllocationApplicationService<V> where V : struct, IRealNumber<V> {
    private readonly IColumnGenerationSolver _solver;
    private readonly INetworkSchedulingSolverValueAdapter<V> _adapter;

    /// <summary>
    /// 构造分配应用服务 / Construct the allocation application service.
    /// </summary>
    /// <param name="solver">求解器 / The solver.</param>
    /// <param name="adapter">数值边界 / The numeric boundary.</param>
    public NetworkAllocationApplicationService(
        IColumnGenerationSolver solver,
        INetworkSchedulingSolverValueAdapter<V> adapter) {
        ArgumentNullException.ThrowIfNull(solver);
        ArgumentNullException.ThrowIfNull(adapter);
        _solver = solver;
        _adapter = adapter;
    }

    /// <summary>
    /// 创建独立上下文并调用共享领域注册路径 / Create a fresh context and use the shared domain registration path.
    /// </summary>
    /// <param name="problem">分配问题 / The allocation problem.</param>
    /// <param name="extra">额外业务规则 / Extra business rules.</param>
    /// <returns>结构化求解终态 / The structured solve outcome.</returns>
    public Task<Result<NetworkSolveResult<AllocationSolution<V>>, ErrorCode, Error<ErrorCode>>> Solve(
        AllocationProblem<V> problem,
        Func<AllocationContext<V>, LinearMetaModel<Flt64>, Try>? extra = null) {
        ArgumentNullException.ThrowIfNull(problem);
        Result<AllocationContext<V>, ErrorCode, Error<ErrorCode>> created =
            AllocationContext<V>.Create(problem, _adapter);
        if (created is Failed<AllocationContext<V>, ErrorCode, Error<ErrorCode>> failure) {
            return Task.FromResult(Results.Failed<NetworkSolveResult<AllocationSolution<V>>>(failure.Error));
        }
        AllocationContext<V> context =
            ((Ok<AllocationContext<V>, ErrorCode, Error<ErrorCode>>)created).Value;
        return NetworkSolver.SolveNetwork(
            context.Name,
            _solver,
            model => ChainAllocation(context.Register(model), extra, context, model),
            context.ExtractSolution);
    }

    /// <summary>组合领域注册与额外规则 / Chain domain registration with extra rules.</summary>
    private static Try ChainAllocation(
        Try domain,
        Func<AllocationContext<V>, LinearMetaModel<Flt64>, Try>? extra,
        AllocationContext<V> context,
        LinearMetaModel<Flt64> model) {
        if (domain is Failed<Success, ErrorCode, Error<ErrorCode>> failure) {
            return Results.Failed<Success>(failure.Error);
        }
        return extra is null ? Results.Ok(Results.SuccessInstance) : extra(context, model);
    }
}

/// <summary>
/// 单输入产率转换、运输损耗与回收的独立求解入口 / Standalone single-input conversion, transport-loss and recovery entry.
/// </summary>
/// <remarks>
/// **不把多输入 BOM 或全局加工排程塞进广义流**。这个模型表达的是"单输入按产率转换"，多输入配方与时间维排程都需要另一套结构；
/// 硬塞进来会让产率系数失去意义。
/// **Multi-input BOM and global processing schedules are not forced into generalized flow.** This model expresses "single input
/// converted by a yield"; multi-input recipes and time-indexed scheduling need a different structure, and forcing them in would leave
/// the yield coefficients meaningless.
/// </remarks>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
public sealed class NetworkGeneralizedFlowApplicationService<V> where V : struct, IRealNumber<V> {
    private readonly IColumnGenerationSolver _solver;
    private readonly INetworkSchedulingSolverValueAdapter<V> _adapter;

    /// <summary>
    /// 构造广义流应用服务 / Construct the generalized-flow application service.
    /// </summary>
    /// <param name="solver">求解器 / The solver.</param>
    /// <param name="adapter">数值边界 / The numeric boundary.</param>
    public NetworkGeneralizedFlowApplicationService(
        IColumnGenerationSolver solver,
        INetworkSchedulingSolverValueAdapter<V> adapter) {
        ArgumentNullException.ThrowIfNull(solver);
        ArgumentNullException.ThrowIfNull(adapter);
        _solver = solver;
        _adapter = adapter;
    }

    /// <summary>
    /// 求解有限容量广义流问题 / Solve a finite-capacity generalized-flow problem.
    /// </summary>
    /// <param name="problem">有限容量广义流问题 / The finite-capacity generalized-flow problem.</param>
    /// <param name="extra">额外装配规则 / Extra assembly rules.</param>
    /// <returns>结构化终态 / The structured outcome.</returns>
    public Task<Result<NetworkSolveResult<GeneralizedFlowSolution<V>>, ErrorCode, Error<ErrorCode>>> Solve(
        GeneralizedFlowProblem<V> problem,
        Func<GeneralizedFlowContext<V>, LinearMetaModel<Flt64>, Try>? extra = null) {
        ArgumentNullException.ThrowIfNull(problem);
        var context = new GeneralizedFlowContext<V>(problem, _adapter);
        return NetworkSolver.SolveNetwork(
            context.Name,
            _solver,
            model => ChainGeneralized(context.Register(model), extra, context, model),
            context.ExtractSolution);
    }

    /// <summary>组合领域注册与额外规则 / Chain domain registration with extra rules.</summary>
    private static Try ChainGeneralized(
        Try domain,
        Func<GeneralizedFlowContext<V>, LinearMetaModel<Flt64>, Try>? extra,
        GeneralizedFlowContext<V> context,
        LinearMetaModel<Flt64> model) {
        if (domain is Failed<Success, ErrorCode, Error<ErrorCode>> failure) {
            return Results.Failed<Success>(failure.Error);
        }
        return extra is null ? Results.Ok(Results.SuccessInstance) : extra(context, model);
    }
}
