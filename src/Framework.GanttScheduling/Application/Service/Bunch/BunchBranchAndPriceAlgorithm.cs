#nullable enable

using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Framework.GanttScheduling.Application.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Application.Model.Bunch;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.BunchCompilation;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.BunchCompilation.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.Solver;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
namespace Fuookami.Ospf.Framework.GanttScheduling.Application.Service.Bunch;
/// <summary>
/// 任务束分支定价算法 / Bunch branch and price algorithm
/// </summary>
/// <typeparam name="Map">影子价格映射类型 / Shadow price map type</typeparam>
/// <typeparam name="Args">影子价格参数类型 / Shadow price arguments type</typeparam>
/// <typeparam name="B">任务束类型 / Bunch type</typeparam>
/// <typeparam name="T">任务类型 / Task type</typeparam>
/// <typeparam name="E">执行器类型 / Executor type</typeparam>
/// <typeparam name="A">分配策略类型 / Assignment policy type</typeparam>
public class BunchBranchAndPriceAlgorithm<Map, Args, B, T, E, A>
    where Map : GanttSchedulingShadowPriceMap<E, A>
    where Args : class, IGanttSchedulingShadowPriceArguments<E, A>
    where B : AbstractTaskBunch<T, E, A>
    where T : IAbstractTask<E, A>
    where E : Executor
    where A : IAssignmentPolicy<E> {
    /// <summary>
    /// 策略 / Policy
    /// </summary>
    /// <param name="ContextBuilder">上下文构建器 / Context builder</param>
    /// <param name="ExtractContextBuilder">提取上下文构建器列表 / Extract context builder list</param>
    /// <param name="ShadowPriceMap">影子价格映射构建器 / Shadow price map builder</param>
    /// <param name="ReducedCost">约简成本标量函数 / Reduced-cost scalar function</param>
    /// <param name="BunchGenerator">任务束生成器 / Bunch generator</param>
    public sealed record Policy(
        Func<IBunchCompilationContext<Args, B, T, E, A>> ContextBuilder,
        IReadOnlyList<Func<IBunchCompilationContext<Args, B, T, E, A>, IReadOnlyList<IExtractBunchCompilationContext<Args, B, T, E, A>>>> ExtractContextBuilder,
        Func<Map> ShadowPriceMap,
        Func<Map, AbstractTaskBunch<T, E, A>, Flt64> ReducedCost,
        Func<ulong, IReadOnlyList<E>, Map, Task<Result<IReadOnlyList<B>, ErrorCode, Error<ErrorCode>>>> BunchGenerator
    );

    /// <summary>
    /// 配置 / Configuration
    /// </summary>
    /// <param name="BadReducedAmount">约简成本差的数量阈值 / Bad reduced cost amount threshold</param>
    /// <param name="MaximumColumnAmount">最大列数 / Maximum column amount</param>
    /// <param name="MinimumColumnAmountPerExecutor">每个执行器最小列数 / Minimum column amount per executor</param>
    /// <param name="TimeLimit">时间限制 / Time limit</param>
    public sealed record Configuration(
        ulong BadReducedAmount = 20UL,
        ulong MaximumColumnAmount = 50000UL,
        ulong MinimumColumnAmountPerExecutor = 0UL,
        TimeSpan? TimeLimit = null
    ) {
        /// <summary>解析后的时间限制 / Resolved time limit</summary>
        public TimeSpan ResolvedTimeLimit => TimeLimit ?? TimeSpan.FromSeconds(30000);
    }

    private readonly IReadOnlyList<E> _executors;
    private readonly IReadOnlyList<T> _tasks;
    private readonly IReadOnlyList<B> _initialBunches;
    private readonly IColumnGenerationSolver _solver;
    private readonly Policy _policy;
    private readonly Configuration _configuration;

    private readonly IBunchCompilationContext<Args, B, T, E, A> _context;
    private readonly IReadOnlyList<IExtractBunchCompilationContext<Args, B, T, E, A>> _extractContexts;

    private Map? _shadowPriceMap;

    private readonly HashSet<B> _fixedBunches = new();
    private readonly HashSet<B> _keptBunches = new();
    private IReadOnlyDictionary<B, Flt64> _lastKeptBunches = new Dictionary<B, Flt64>();
    private readonly HashSet<E> _hiddenExecutors = new();

    private ulong _mainProblemSolvingTimes;
    private TimeSpan _mainProblemSolvingTime;
    private TimeSpan _mainProblemModelingTime;
    private ulong _subProblemSolvingTimes;
    private TimeSpan _subProblemSolvingTime;

    /// <summary>
    /// 任务束分支定价算法构造 / Bunch branch and price algorithm constructor
    /// </summary>
    /// <param name="executors">执行器列表 / List of executors</param>
    /// <param name="tasks">任务列表 / List of tasks</param>
    /// <param name="initialBunches">初始任务束列表 / List of initial bunches</param>
    /// <param name="solver">列生成求解器 / Column generation solver</param>
    /// <param name="policy">策略 / Policy</param>
    /// <param name="configuration">配置 / Configuration</param>
    public BunchBranchAndPriceAlgorithm(
        IReadOnlyList<E> executors,
        IReadOnlyList<T> tasks,
        IReadOnlyList<B> initialBunches,
        IColumnGenerationSolver solver,
        Policy policy,
        Configuration? configuration = null) {
        _executors = executors;
        _tasks = tasks;
        _initialBunches = initialBunches;
        _solver = solver;
        _policy = policy;
        _configuration = configuration ?? new Configuration();

        _context = _policy.ContextBuilder();
        _extractContexts = _policy.ExtractContextBuilder
            .SelectMany(builder => builder(_context))
            .ToList();
    }

    private ulong ColumnAmount => _context.ColumnAmount;
    private ulong ExecutorAmount => (ulong)_executors.Count;

    /// <summary>
    /// 执行分支定价算法 / Execute branch and price algorithm
    /// </summary>
    /// <param name="id">标识符 / Identifier</param>
    /// <param name="heartBeatCallBack">心跳回调 / Heartbeat callback</param>
    /// <return>任务束解 / Bunch solution</return>
    public async Task<Result<BunchSolution<B, T, E, A>, ErrorCode, Error<ErrorCode>>> RunAsync(
        string id,
        Action<string, Flt64>? heartBeatCallBack = null) {
        var maximumReducedCost1 = new Flt64(50.0);
        var maximumReducedCost2 = new Flt64(3000.0);

        var model = new LinearMetaModel<Flt64>(id);
        BunchSolution<B, T, E, A>? bestSolution = null;

        try {
            var iteration = new BunchIteration<T, E, A>();

            // Register model
            Try registerResult = Register(model);
            if (registerResult.IsFailed) {
                return registerResult switch {
                    Failed<Success, ErrorCode, Error<ErrorCode>> f => Results.Failed<BunchSolution<B, T, E, A>>(f.Error),
                    Fatal<Success, ErrorCode, Error<ErrorCode>> ft => new Fatal<BunchSolution<B, T, E, A>, ErrorCode, Error<ErrorCode>>(ft.Errors),
                    _ => Results.Failed<BunchSolution<B, T, E, A>>(new Err<ErrorCode>(ErrorCode.ApplicationError, "Registration failed"))
                };
            }

            // Solve IP with initial columns
            Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> ipResult = await _solver.SolveMILPAsync($"{id}_{iteration}", model);
            if (ipResult.IsFailed) {
                return ipResult switch {
                    Failed<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> f => Results.Failed<BunchSolution<B, T, E, A>>(f.Error),
                    Fatal<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> ft => new Fatal<BunchSolution<B, T, E, A>, ErrorCode, Error<ErrorCode>>(ft.Errors),
                    _ => Results.Failed<BunchSolution<B, T, E, A>>(new Err<ErrorCode>(ErrorCode.ApplicationError, "IP solve failed"))
                };
            }
            FeasibleSolverOutput<Flt64> ipOutput = ((Ok<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>)ipResult).Value;

            // Analyze initial solution
            Result<BunchSolution<B, T, E, A>, ErrorCode, Error<ErrorCode>> analyzeResult = AnalyzeSolution(iteration.Iteration, model);
            if (analyzeResult.IsFailed) {
                return analyzeResult switch {
                    Failed<BunchSolution<B, T, E, A>, ErrorCode, Error<ErrorCode>> f => Results.Failed<BunchSolution<B, T, E, A>>(f.Error),
                    Fatal<BunchSolution<B, T, E, A>, ErrorCode, Error<ErrorCode>> ft => new Fatal<BunchSolution<B, T, E, A>, ErrorCode, Error<ErrorCode>>(ft.Errors),
                    _ => Results.Failed<BunchSolution<B, T, E, A>>(new Err<ErrorCode>(ErrorCode.ApplicationError, "Analysis failed"))
                };
            }
            bestSolution = ((Ok<BunchSolution<B, T, E, A>, ErrorCode, Error<ErrorCode>>)analyzeResult).Value;

            RefreshIp(ipOutput);
            iteration.RefreshIpObj(ipOutput.Obj);

            if (ipOutput.Obj == Flt64.Zero) {
                return Results.Ok(bestSolution);
            }

            // Fix and keep bunches
            FixBunch(iteration.Iteration, model);
            KeepBunch(iteration.Iteration, model);

            ulong mainIteration = 1UL;
            while (!iteration.IsImprovementSlow
                && iteration.RunTime < _configuration.ResolvedTimeLimit) {
                // Solve RMP (LP relaxation)
                Result<Map, ErrorCode, Error<ErrorCode>> rmpResult = await SolveRMP(id, iteration, model, true);
                if (rmpResult.IsFailed) {
                    return Results.Ok(bestSolution!);
                }
                _shadowPriceMap = ((Ok<Map, ErrorCode, Error<ErrorCode>>)rmpResult).Value;

                // Hide saturated executors
                HideExecutors(model);

                // Global column generation (1 iteration)
                for (int count = 0; count < 1; ++count) {
                    iteration.Increment();
                    Result<IReadOnlyList<B>, ErrorCode, Error<ErrorCode>> spResult = await SolveSP(id, iteration, _executors, _shadowPriceMap!);
                    if (spResult.IsFailed) {
                        return Results.Ok(bestSolution!);
                    }
                    IReadOnlyList<B> newBunches = ((Ok<IReadOnlyList<B>, ErrorCode, Error<ErrorCode>>)spResult).Value;

                    if (newBunches.Count == 0) {
                        if (iteration.OptimalRate == Flt64.One) {
                            return Results.Ok(bestSolution!);
                        }
                    }
                    ulong newBunchAmount = (ulong)newBunches.Count;

                    AddColumns(iteration.Iteration, newBunches, model);

                    // Re-solve RMP
                    rmpResult = await SolveRMP(id, iteration, model, true);
                    if (rmpResult.IsFailed) {
                        return Results.Ok(bestSolution!);
                    }
                    _shadowPriceMap = ((Ok<Map, ErrorCode, Error<ErrorCode>>)rmpResult).Value;

                    ulong reducedAmount = (ulong)_fixedBunches.Count(b => _policy.ReducedCost(_shadowPriceMap!, b) > Flt64.Zero);
                    if (ColumnAmount > _configuration.MaximumColumnAmount) {
                        Result<Flt64, ErrorCode, Error<ErrorCode>> removeResult = RemoveColumns(maximumReducedCost1, _configuration.MaximumColumnAmount, _shadowPriceMap!, _fixedBunches, _keptBunches, model);
                        if (removeResult.IsOk) {
                            maximumReducedCost1 = ((Ok<Flt64, ErrorCode, Error<ErrorCode>>)removeResult).Value;
                        }
                    }
                    if (reducedAmount >= _configuration.BadReducedAmount
                        || newBunchAmount <= MinimumColumnAmount(_fixedBunches)) {
                        break;
                    }
                }
                maximumReducedCost1 = new Flt64(50.0);

                // Select free executors and globally fix
                ISet<E> freeExecutors = SelectFreeExecutors(model);
                HashSet<B> globallyFixedBunches = GloballyFix(freeExecutors);
                var freeExecutorList = freeExecutors.ToList();

                // Local column generation
                while (true) {
                    rmpResult = await SolveRMP(id, iteration, model, false);
                    if (rmpResult.IsFailed) {
                        return Results.Ok(bestSolution!);
                    }
                    _shadowPriceMap = ((Ok<Map, ErrorCode, Error<ErrorCode>>)rmpResult).Value;

                    iteration.Increment();
                    Result<IReadOnlyList<B>, ErrorCode, Error<ErrorCode>> spResult = await SolveSP(id, iteration, freeExecutorList, _shadowPriceMap!);
                    if (spResult.IsFailed) {
                        return Results.Ok(bestSolution!);
                    }
                    IReadOnlyList<B> newBunches = ((Ok<IReadOnlyList<B>, ErrorCode, Error<ErrorCode>>)spResult).Value;

                    if (newBunches.Count == 0) {
                        iteration.Decrement();
                        break;
                    }
                    ulong newBunchAmount = (ulong)newBunches.Count;

                    AddColumns(iteration.Iteration, newBunches, model);
                    IReadOnlyList<B> newFixedBunches = LocallyFix(iteration.Iteration, globallyFixedBunches, model);

                    if (newFixedBunches.Count > 0) {
                        foreach (B bunch in newFixedBunches) {
                            if (bunch.Executor is not null) {
                                freeExecutorList.Remove(bunch.Executor);
                            }
                        }
                        foreach (B bunch in newFixedBunches) {
                            globallyFixedBunches.Add(bunch);
                        }
                    }
                    else {
                        break;
                    }

                    if (ColumnAmount > _configuration.MaximumColumnAmount
                        && newBunchAmount > MinimumColumnAmount(globallyFixedBunches)) {
                        Result<Flt64, ErrorCode, Error<ErrorCode>> removeResult = RemoveColumns(maximumReducedCost2, _configuration.MaximumColumnAmount, _shadowPriceMap!, globallyFixedBunches, _keptBunches, model);
                        if (removeResult.IsOk) {
                            maximumReducedCost2 = ((Ok<Flt64, ErrorCode, Error<ErrorCode>>)removeResult).Value;
                        }
                    }
                }

                // Update fixed bunches
                _fixedBunches.Clear();
                foreach (B b in globallyFixedBunches) {
                    _fixedBunches.Add(b);
                }

                // Solve IP
                Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> thisIpResult = await _solver.SolveMILPAsync($"{id}_{iteration}_ip", model);
                if (thisIpResult.IsFailed) {
                    return Results.Ok(bestSolution!);
                }
                FeasibleSolverOutput<Flt64> thisIpOutput = ((Ok<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>)thisIpResult).Value;

                RefreshIp(thisIpOutput);
                if (iteration.RefreshIpObj(thisIpOutput.Obj)) {
                    Result<BunchSolution<B, T, E, A>, ErrorCode, Error<ErrorCode>> analyzeResult2 = AnalyzeSolution(iteration.Iteration, model);
                    if (analyzeResult2.IsOk) {
                        bestSolution = ((Ok<BunchSolution<B, T, E, A>, ErrorCode, Error<ErrorCode>>)analyzeResult2).Value;
                        if (thisIpOutput.Obj == Flt64.Zero) {
                            return Results.Ok(bestSolution);
                        }
                    }
                }

                heartBeatCallBack?.Invoke(id, iteration.OptimalRate);

                Flush(iteration.Iteration);
                iteration.HalveStep();

                ++mainIteration;
            }

            return Results.Ok(bestSolution!);
        }
        catch (Exception e) {
            return Results.Failed<BunchSolution<B, T, E, A>>(new Err<ErrorCode>(ErrorCode.ApplicationException, e.Message));
        }
    }

    private Try Register(LinearMetaModel<Flt64> model) {
        Try result = _context.Register(model);
        if (result.IsFailed) {
            return result;
        }

        foreach (IExtractBunchCompilationContext<Args, B, T, E, A> extractContext in _extractContexts) {
            result = extractContext.Register(model);
            if (result.IsFailed) {
                return result;
            }
        }

        Result<IReadOnlyList<B>, ErrorCode, Error<ErrorCode>> addResult = _context.AddColumns(0UL, _initialBunches, model);
        if (addResult.IsFailed) {
            return addResult switch {
                Failed<IReadOnlyList<B>, ErrorCode, Error<ErrorCode>> f => Results.Failed<Success>(f.Error),
                Fatal<IReadOnlyList<B>, ErrorCode, Error<ErrorCode>> ft => new Fatal<Success, ErrorCode, Error<ErrorCode>>(ft.Errors),
                _ => Results.Failed<Success>(new Err<ErrorCode>(ErrorCode.ApplicationError, "AddColumns failed"))
            };
        }
        IReadOnlyList<B> unduplicatedBunches = ((Ok<IReadOnlyList<B>, ErrorCode, Error<ErrorCode>>)addResult).Value;

        foreach (IExtractBunchCompilationContext<Args, B, T, E, A> extractContext in _extractContexts) {
            result = extractContext.AddColumns(0UL, unduplicatedBunches, model);
            if (result.IsFailed) {
                return result;
            }
        }

        return Results.Ok<Success>(Results.SuccessInstance);
    }

    private async Task<Result<Map, ErrorCode, Error<ErrorCode>>> SolveRMP(
        string id,
        BunchIteration<T, E, A> iteration,
        LinearMetaModel<Flt64> model,
        bool withKeeping) {
        Result<IColumnGenerationSolver.LpResult, ErrorCode, Error<ErrorCode>> lpResult = await _solver.SolveLPAsync($"{id}_{iteration}_lp", model);
        if (lpResult.IsFailed) {
            return lpResult switch {
                Failed<IColumnGenerationSolver.LpResult, ErrorCode, Error<ErrorCode>> f => Results.Failed<Map>(f.Error),
                Fatal<IColumnGenerationSolver.LpResult, ErrorCode, Error<ErrorCode>> ft => new Fatal<Map, ErrorCode, Error<ErrorCode>>(ft.Errors),
                _ => Results.Failed<Map>(new Err<ErrorCode>(ErrorCode.ApplicationError, "LP solve failed"))
            };
        }
        IColumnGenerationSolver.LpResult lpOutput = ((Ok<IColumnGenerationSolver.LpResult, ErrorCode, Error<ErrorCode>>)lpResult).Value;

        RefreshLp(lpOutput);
        if (iteration.RefreshLpObj(lpOutput.Obj) && withKeeping) {
            KeepBunch(iteration.Iteration, model);
        }

        var shadowPrices = new MetaDualSolution(lpOutput.DualSolution, new Dictionary<IIntermediateSymbol, IReadOnlyList<(IConstraint<Flt64, LinearCategory> Constraint, Flt64 Price)>>());
        Result<Map, ErrorCode, Error<ErrorCode>> extractResult = ExtractShadowPrice(model, shadowPrices);
        if (extractResult.IsFailed) {
            return extractResult switch {
                Failed<Map, ErrorCode, Error<ErrorCode>> f => Results.Failed<Map>(f.Error),
                Fatal<Map, ErrorCode, Error<ErrorCode>> ft => new Fatal<Map, ErrorCode, Error<ErrorCode>>(ft.Errors),
                _ => Results.Failed<Map>(new Err<ErrorCode>(ErrorCode.ApplicationError, "Shadow price extraction failed"))
            };
        }
        return extractResult;
    }

    private async Task<Result<IReadOnlyList<B>, ErrorCode, Error<ErrorCode>>> SolveSP(
        string id,
        BunchIteration<T, E, A> iteration,
        IReadOnlyList<E> executors,
        Map shadowPriceMap) {
        Result<IReadOnlyList<B>, ErrorCode, Error<ErrorCode>> result = await _policy.BunchGenerator(iteration.Iteration, executors, shadowPriceMap);
        if (result.IsFailed) {
            return result switch {
                Failed<IReadOnlyList<B>, ErrorCode, Error<ErrorCode>> f => Results.Failed<IReadOnlyList<B>>(f.Error),
                Fatal<IReadOnlyList<B>, ErrorCode, Error<ErrorCode>> ft => new Fatal<IReadOnlyList<B>, ErrorCode, Error<ErrorCode>>(ft.Errors),
                _ => Results.Failed<IReadOnlyList<B>>(new Err<ErrorCode>(ErrorCode.ApplicationError, "Bunch generation failed"))
            };
        }
        IReadOnlyList<B> newBunches = ((Ok<IReadOnlyList<B>, ErrorCode, Error<ErrorCode>>)result).Value;
        ++_subProblemSolvingTimes;

        iteration.RefreshLowerBound(
            newBunches.Cast<AbstractTaskBunch<T, E, A>>().ToList(),
            b => _policy.ReducedCost(shadowPriceMap, b));

        return Results.Ok(newBunches);
    }

    private Result<Map, ErrorCode, Error<ErrorCode>> ExtractShadowPrice(
        LinearMetaModel<Flt64> model,
        MetaDualSolution shadowPrices) {
        Map map = _policy.ShadowPriceMap();

        Try result = _context.ExtractShadowPrice(map, model, shadowPrices);
        if (result.IsFailed) {
            return result switch {
                Failed<Success, ErrorCode, Error<ErrorCode>> f => Results.Failed<Map>(f.Error),
                Fatal<Success, ErrorCode, Error<ErrorCode>> ft => new Fatal<Map, ErrorCode, Error<ErrorCode>>(ft.Errors),
                _ => Results.Failed<Map>(new Err<ErrorCode>(ErrorCode.ApplicationError, "ExtractShadowPrice failed"))
            };
        }

        foreach (IExtractBunchCompilationContext<Args, B, T, E, A> extractContext in _extractContexts) {
            result = extractContext.ExtractShadowPrice(map, model, shadowPrices);
            if (result.IsFailed) {
                return result switch {
                    Failed<Success, ErrorCode, Error<ErrorCode>> f => Results.Failed<Map>(f.Error),
                    Fatal<Success, ErrorCode, Error<ErrorCode>> ft => new Fatal<Map, ErrorCode, Error<ErrorCode>>(ft.Errors),
                    _ => Results.Failed<Map>(new Err<ErrorCode>(ErrorCode.ApplicationError, "ExtractShadowPrice failed"))
                };
            }
        }

        return Results.Ok(map);
    }

    private void AddColumns(ulong iteration, IReadOnlyList<B> newBunches, LinearMetaModel<Flt64> model) {
        Result<IReadOnlyList<B>, ErrorCode, Error<ErrorCode>> addResult = _context.AddColumns(iteration, newBunches, model);
        if (addResult.IsOk) {
            IReadOnlyList<B> undupBunches = ((Ok<IReadOnlyList<B>, ErrorCode, Error<ErrorCode>>)addResult).Value;
            if (undupBunches.Count > 0) {
                _context.Aggregation.Flush(iteration);
            }

            foreach (IExtractBunchCompilationContext<Args, B, T, E, A> extractContext in _extractContexts) {
                extractContext.AddColumns(iteration, undupBunches, model);
            }
        }
    }

    private Result<Flt64, ErrorCode, Error<ErrorCode>> RemoveColumns(
        Flt64 maximumReducedCost,
        ulong maximumColumnAmount,
        Map shadowPriceMap,
        ISet<B> fixedBunches,
        ISet<B> keptBunches,
        LinearMetaModel<Flt64> model) {
        return _context.RemoveColumns(
            maximumReducedCost,
            maximumColumnAmount,
            b => _policy.ReducedCost(shadowPriceMap, b),
            fixedBunches,
            keptBunches,
            model);
    }

    private void FixBunch(ulong iteration, LinearMetaModel<Flt64> model) {
        Result<ISet<B>, ErrorCode, Error<ErrorCode>> result = _context.ExtractFixedBunches(iteration, model);
        if (result.IsOk) {
            foreach (B b in ((Ok<ISet<B>, ErrorCode, Error<ErrorCode>>)result).Value) {
                _fixedBunches.Add(b);
            }
        }
    }

    private void KeepBunch(ulong iteration, LinearMetaModel<Flt64> model) {
        Result<ISet<B>, ErrorCode, Error<ErrorCode>> result = _context.ExtractKeptBunches(iteration, model);
        if (result.IsOk) {
            foreach (B b in ((Ok<ISet<B>, ErrorCode, Error<ErrorCode>>)result).Value) {
                _keptBunches.Add(b);
            }
        }

        Result<IReadOnlyDictionary<B, Flt64>, ErrorCode, Error<ErrorCode>> ratioResult = _context.ExtractKeptBunchesWithRatio(iteration, model);
        if (ratioResult.IsOk) {
            _lastKeptBunches = ((Ok<IReadOnlyDictionary<B, Flt64>, ErrorCode, Error<ErrorCode>>)ratioResult).Value;
        }
    }

    private void HideExecutors(LinearMetaModel<Flt64> model) {
        Result<ISet<E>, ErrorCode, Error<ErrorCode>> result = _context.ExtractHiddenExecutors(_executors, model);
        if (result.IsOk) {
            foreach (E e in ((Ok<ISet<E>, ErrorCode, Error<ErrorCode>>)result).Value) {
                _hiddenExecutors.Add(e);
            }
        }
    }

    private ISet<E> SelectFreeExecutors(LinearMetaModel<Flt64> model) {
        var free = new HashSet<E>(_executors);
        foreach (B bunch in _fixedBunches) {
            if (bunch.Executor is not null) {
                free.Remove(bunch.Executor);
            }
        }
        foreach (E e in _hiddenExecutors) {
            free.Remove(e);
        }
        return free;
    }

    private HashSet<B> GloballyFix(ISet<E> freeExecutors) {
        var fixedBunches = new HashSet<B>();
        foreach (B bunch in _fixedBunches) {
            if (bunch.Executor is not null && !freeExecutors.Contains(bunch.Executor)) {
                fixedBunches.Add(bunch);
            }
        }
        _context.GloballyFix(fixedBunches);
        return fixedBunches;
    }

    private IReadOnlyList<B> LocallyFix(ulong iteration, ISet<B> fixedBunches, LinearMetaModel<Flt64> model) {
        var fixBar = new Flt64(0.9);
        Result<ISet<B>, ErrorCode, Error<ErrorCode>> result = _context.LocallyFix(iteration, fixBar, fixedBunches, model);
        if (result.IsOk) {
            return ((Ok<ISet<B>, ErrorCode, Error<ErrorCode>>)result).Value.ToList();
        }
        return Array.Empty<B>();
    }

    private void Flush(ulong iteration) {
        _context.Aggregation.Flush(iteration);
        _keptBunches.Clear();
        _hiddenExecutors.Clear();
    }

    private Result<BunchSolution<B, T, E, A>, ErrorCode, Error<ErrorCode>> AnalyzeSolution(
        ulong iteration,
        LinearMetaModel<Flt64> model) => _context.AnalyzeBunchSolution(iteration, _tasks, model);

    private void RefreshLp(IColumnGenerationSolver.LpResult lpResult) {
        ++_mainProblemSolvingTimes;
        _mainProblemSolvingTime += lpResult.Time;
    }

    private void RefreshIp(FeasibleSolverOutput<Flt64> ipResult) {
        ++_mainProblemSolvingTimes;
        _mainProblemSolvingTime += ipResult.Time;
    }

    private ulong MinimumColumnAmount(ISet<B> fixedBunches) {
        ulong notFixed = ExecutorAmount - (ulong)fixedBunches.Select(b => b.Executor).Where(e => e is not null).Distinct().Count();
        return notFixed * _configuration.MinimumColumnAmountPerExecutor;
    }
}
