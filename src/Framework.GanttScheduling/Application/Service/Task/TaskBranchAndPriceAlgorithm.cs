#nullable enable

using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Framework.GanttScheduling.Application.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Application.Model.Task;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Model;
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

namespace Fuookami.Ospf.Framework.GanttScheduling.Application.Service.Task;
/// <summary>
/// 任务分支定价算法 / Task branch and price algorithm
/// </summary>
/// <typeparam name="Map">影子价格映射类型 / Shadow price map type</typeparam>
/// <typeparam name="Args">影子价格参数类型 / Shadow price arguments type</typeparam>
/// <typeparam name="IT">迭代任务类型 / Iterative task type</typeparam>
/// <typeparam name="T">任务类型 / Task type</typeparam>
/// <typeparam name="E">执行器类型 / Executor type</typeparam>
/// <typeparam name="A">分配策略类型 / Assignment policy type</typeparam>
public class TaskBranchAndPriceAlgorithm<Map, Args, IT, T, E, A>
    where Map : GanttSchedulingShadowPriceMap<E, A>
    where Args : class, IGanttSchedulingShadowPriceArguments<E, A>
    where IT : IIterativeAbstractTask<E, A>
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
    /// <param name="TaskGenerator">任务生成器 / Task generator</param>
    public sealed record Policy(
        Func<ITaskBranchAndPriceContext<Args, IT, T, E, A>> ContextBuilder,
        IReadOnlyList<Func<ITaskBranchAndPriceContext<Args, IT, T, E, A>, IReadOnlyList<ITaskBranchAndPriceContext<Args, IT, T, E, A>>>> ExtractContextBuilder,
        Func<Map> ShadowPriceMap,
        Func<Map, IT, Flt64> ReducedCost,
        Func<ulong, IReadOnlyList<E>, Map, Task<Result<IReadOnlyList<IT>, ErrorCode, Error<ErrorCode>>>> TaskGenerator
    );

    /// <summary>
    /// 配置 / Configuration
    /// </summary>
    /// <param name="Solver">求解器名称 / Solver name</param>
    /// <param name="MaxBadReducedAmount">约简成本差的最大数量阈值 / Maximum bad reduced cost amount threshold</param>
    /// <param name="MaximumColumnAmount">最大列数 / Maximum column amount</param>
    /// <param name="MinimumColumnAmountPerExecutor">每个执行器最小列数 / Minimum column amount per executor</param>
    /// <param name="TimeLimit">时间限制 / Time limit</param>
    public sealed record Configuration(
        string? Solver = null,
        ulong MaxBadReducedAmount = 20UL,
        ulong MaximumColumnAmount = 50000UL,
        ulong MinimumColumnAmountPerExecutor = 0UL,
        TimeSpan? TimeLimit = null
    ) {
        /// <summary>解析后的时间限制 / Resolved time limit</summary>
        public TimeSpan ResolvedTimeLimit => TimeLimit ?? TimeSpan.FromSeconds(30000);
    }

    private readonly IReadOnlyList<E> _executors;
    private readonly IReadOnlyList<T> _tasks;
    private readonly IReadOnlyList<IT> _initialTasks;
    private readonly IColumnGenerationSolver _solver;
    private readonly Policy _policy;
    private readonly Configuration _configuration;

    private readonly ITaskBranchAndPriceContext<Args, IT, T, E, A> _context;
    private readonly IReadOnlyList<ITaskBranchAndPriceContext<Args, IT, T, E, A>> _extractContexts;

    private Map? _shadowPriceMap;

    private readonly HashSet<IT> _fixedTasks = new();
    private readonly HashSet<IT> _keptTasks = new();
    private readonly HashSet<E> _hiddenExecutors = new();

    private ulong _mainProblemSolvingTimes;
    private TimeSpan _mainProblemSolvingTime;
    private TimeSpan _mainProblemModelingTime;
    private ulong _subProblemSolvingTimes;
    private TimeSpan _subProblemSolvingTime;

    /// <summary>
    /// 任务分支定价算法构造 / Task branch and price algorithm constructor
    /// </summary>
    /// <param name="executors">执行器列表 / List of executors</param>
    /// <param name="tasks">任务列表 / List of tasks</param>
    /// <param name="initialTasks">初始任务列表 / List of initial tasks</param>
    /// <param name="solver">列生成求解器 / Column generation solver</param>
    /// <param name="policy">策略 / Policy</param>
    /// <param name="configuration">配置 / Configuration</param>
    public TaskBranchAndPriceAlgorithm(
        IReadOnlyList<E> executors,
        IReadOnlyList<T> tasks,
        IReadOnlyList<IT> initialTasks,
        IColumnGenerationSolver solver,
        Policy policy,
        Configuration? configuration = null) {
        _executors = executors;
        _tasks = tasks;
        _initialTasks = initialTasks;
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
    /// <return>任务解 / Task solution</return>
    public async Task<Result<TaskSolution<T, E, A>, ErrorCode, Error<ErrorCode>>> RunAsync(string id) {
        var maximumReducedCost1 = new Flt64(50.0);
        var maximumReducedCost2 = new Flt64(3000.0);

        var model = new LinearMetaModel<Flt64>(id);
        TaskSolution<T, E, A>? bestSolution = null;

        try {
            var iteration = new TaskIteration<IT, E, A>();

            // Register model
            Try registerResult = Register(model);
            if (registerResult.IsFailed) {
                return registerResult switch {
                    Failed<Success, ErrorCode, Error<ErrorCode>> f => Results.Failed<TaskSolution<T, E, A>>(f.Error),
                    Fatal<Success, ErrorCode, Error<ErrorCode>> ft => new Fatal<TaskSolution<T, E, A>, ErrorCode, Error<ErrorCode>>(ft.Errors),
                    _ => Results.Failed<TaskSolution<T, E, A>>(new Err<ErrorCode>(ErrorCode.ApplicationError, "Registration failed"))
                };
            }

            // Solve IP with initial columns
            Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> ipResult = await _solver.SolveMILPAsync($"{id}_{iteration}", model);
            if (ipResult.IsFailed) {
                return ipResult switch {
                    Failed<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> f => Results.Failed<TaskSolution<T, E, A>>(f.Error),
                    Fatal<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> ft => new Fatal<TaskSolution<T, E, A>, ErrorCode, Error<ErrorCode>>(ft.Errors),
                    _ => Results.Failed<TaskSolution<T, E, A>>(new Err<ErrorCode>(ErrorCode.ApplicationError, "IP solve failed"))
                };
            }
            FeasibleSolverOutput<Flt64> ipOutput = ((Ok<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>)ipResult).Value;

            // Analyze initial solution
            Result<TaskSolution<T, E, A>, ErrorCode, Error<ErrorCode>> analyzeResult = AnalyzeSolution(iteration.Iteration, model);
            if (analyzeResult.IsFailed) {
                return analyzeResult switch {
                    Failed<TaskSolution<T, E, A>, ErrorCode, Error<ErrorCode>> f => Results.Failed<TaskSolution<T, E, A>>(f.Error),
                    Fatal<TaskSolution<T, E, A>, ErrorCode, Error<ErrorCode>> ft => new Fatal<TaskSolution<T, E, A>, ErrorCode, Error<ErrorCode>>(ft.Errors),
                    _ => Results.Failed<TaskSolution<T, E, A>>(new Err<ErrorCode>(ErrorCode.ApplicationError, "Analysis failed"))
                };
            }
            bestSolution = ((Ok<TaskSolution<T, E, A>, ErrorCode, Error<ErrorCode>>)analyzeResult).Value;

            ++_mainProblemSolvingTimes;
            _mainProblemSolvingTime += ipOutput.Time;
            iteration.RefreshIpObj(ipOutput.Obj);

            if (ipOutput.Obj == Flt64.Zero) {
                return Results.Ok(bestSolution);
            }

            // Fix and keep tasks
            FixTasks(iteration.Iteration, model);
            KeepTasks(iteration.Iteration, model);

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
                    Result<IReadOnlyList<IT>, ErrorCode, Error<ErrorCode>> spResult = await SolveSP(id, iteration, _executors, _shadowPriceMap!);
                    if (spResult.IsFailed) {
                        return Results.Ok(bestSolution!);
                    }
                    IReadOnlyList<IT> newTasks = ((Ok<IReadOnlyList<IT>, ErrorCode, Error<ErrorCode>>)spResult).Value;

                    if (newTasks.Count == 0) {
                        if (iteration.OptimalRate == Flt64.One) {
                            return Results.Ok(bestSolution!);
                        }
                    }
                    ulong newTaskAmount = (ulong)newTasks.Count;

                    AddColumns(iteration.Iteration, newTasks, model);

                    // Re-solve RMP
                    rmpResult = await SolveRMP(id, iteration, model, true);
                    if (rmpResult.IsFailed) {
                        return Results.Ok(bestSolution!);
                    }
                    _shadowPriceMap = ((Ok<Map, ErrorCode, Error<ErrorCode>>)rmpResult).Value;

                    ulong badReducedAmount = (ulong)_fixedTasks.Count(t => _policy.ReducedCost(_shadowPriceMap!, t) > Flt64.Zero);
                    if (ColumnAmount > _configuration.MaximumColumnAmount) {
                        Result<Flt64, ErrorCode, Error<ErrorCode>> removeResult = RemoveColumns(maximumReducedCost1, _configuration.MaximumColumnAmount, _shadowPriceMap!, _fixedTasks, _keptTasks, model);
                        if (removeResult.IsOk) {
                            maximumReducedCost1 = ((Ok<Flt64, ErrorCode, Error<ErrorCode>>)removeResult).Value;
                        }
                    }
                    if (badReducedAmount >= _configuration.MaxBadReducedAmount
                        || newTaskAmount <= MinimumColumnAmount(_fixedTasks)) {
                        break;
                    }
                }
                maximumReducedCost1 = new Flt64(50.0);

                // Select free executors and globally fix
                ISet<E> freeExecutors = SelectFreeExecutors(_shadowPriceMap!, model);
                HashSet<IT> globallyFixedTasks = GloballyFix(freeExecutors);
                var freeExecutorList = freeExecutors.ToList();

                // Local column generation
                while (true) {
                    rmpResult = await SolveRMP(id, iteration, model, false);
                    if (rmpResult.IsFailed) {
                        return Results.Ok(bestSolution!);
                    }
                    _shadowPriceMap = ((Ok<Map, ErrorCode, Error<ErrorCode>>)rmpResult).Value;

                    iteration.Increment();
                    Result<IReadOnlyList<IT>, ErrorCode, Error<ErrorCode>> spResult = await SolveSP(id, iteration, freeExecutorList, _shadowPriceMap!);
                    if (spResult.IsFailed) {
                        return Results.Ok(bestSolution!);
                    }
                    IReadOnlyList<IT> newTasks = ((Ok<IReadOnlyList<IT>, ErrorCode, Error<ErrorCode>>)spResult).Value;

                    if (newTasks.Count == 0) {
                        iteration.Decrement();
                        break;
                    }
                    ulong newTaskAmount = (ulong)newTasks.Count;

                    AddColumns(iteration.Iteration, newTasks, model);
                    IReadOnlyList<IT> newFixedTasks = LocallyFix(iteration.Iteration, globallyFixedTasks, model);

                    if (newFixedTasks.Count > 0) {
                        foreach (IT task in newFixedTasks) {
                            if (task.Executor is not null) {
                                freeExecutorList.Remove(task.Executor);
                            }
                        }
                        foreach (IT task in newFixedTasks) {
                            globallyFixedTasks.Add(task);
                        }
                    }
                    else {
                        break;
                    }

                    if (ColumnAmount > _configuration.MaximumColumnAmount
                        && newTaskAmount > MinimumColumnAmount(globallyFixedTasks)) {
                        Result<Flt64, ErrorCode, Error<ErrorCode>> removeResult = RemoveColumns(maximumReducedCost2, _configuration.MaximumColumnAmount, _shadowPriceMap!, globallyFixedTasks, _keptTasks, model);
                        if (removeResult.IsOk) {
                            maximumReducedCost2 = ((Ok<Flt64, ErrorCode, Error<ErrorCode>>)removeResult).Value;
                        }
                    }
                }

                // Update fixed tasks
                _fixedTasks.Clear();
                foreach (IT t in globallyFixedTasks) {
                    _fixedTasks.Add(t);
                }

                // Solve IP
                Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>> thisIpResult = await _solver.SolveMILPAsync($"{id}_{iteration}_ip", model);
                if (thisIpResult.IsFailed) {
                    return Results.Ok(bestSolution!);
                }
                FeasibleSolverOutput<Flt64> thisIpOutput = ((Ok<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>)thisIpResult).Value;

                ++_mainProblemSolvingTimes;
                _mainProblemSolvingTime += thisIpOutput.Time;
                if (iteration.RefreshIpObj(thisIpOutput.Obj)) {
                    Result<TaskSolution<T, E, A>, ErrorCode, Error<ErrorCode>> analyzeResult2 = AnalyzeSolution(iteration.Iteration, model);
                    if (analyzeResult2.IsOk) {
                        bestSolution = ((Ok<TaskSolution<T, E, A>, ErrorCode, Error<ErrorCode>>)analyzeResult2).Value;
                        if (thisIpOutput.Obj == Flt64.Zero) {
                            return Results.Ok(bestSolution);
                        }
                    }
                }

                Flush(iteration.Iteration);
                iteration.HalveStep();

                ++mainIteration;
            }

            return Results.Ok(bestSolution!);
        }
        catch (Exception e) {
            return Results.Failed<TaskSolution<T, E, A>>(new Err<ErrorCode>(ErrorCode.ApplicationException, e.Message));
        }
    }

    private Try Register(LinearMetaModel<Flt64> model) {
        Try result = _context.Register(model);
        if (result.IsFailed) {
            return result;
        }

        foreach (ITaskBranchAndPriceContext<Args, IT, T, E, A> extractContext in _extractContexts) {
            result = extractContext.Register(model);
            if (result.IsFailed) {
                return result;
            }
        }

        Result<IReadOnlyList<IT>, ErrorCode, Error<ErrorCode>> addResult = _context.AddColumns(0UL, _initialTasks, model);
        if (addResult.IsFailed) {
            return addResult switch {
                Failed<IReadOnlyList<IT>, ErrorCode, Error<ErrorCode>> f => Results.Failed<Success>(f.Error),
                Fatal<IReadOnlyList<IT>, ErrorCode, Error<ErrorCode>> ft => new Fatal<Success, ErrorCode, Error<ErrorCode>>(ft.Errors),
                _ => Results.Failed<Success>(new Err<ErrorCode>(ErrorCode.ApplicationError, "AddColumns failed"))
            };
        }

        return Results.Ok<Success>(Results.SuccessInstance);
    }

    private async Task<Result<Map, ErrorCode, Error<ErrorCode>>> SolveRMP(
        string id,
        TaskIteration<IT, E, A> iteration,
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

        ++_mainProblemSolvingTimes;
        _mainProblemSolvingTime += lpOutput.Time;
        if (iteration.RefreshLpObj(lpOutput.Obj) && withKeeping) {
            KeepTasks(iteration.Iteration, model);
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

    private async Task<Result<IReadOnlyList<IT>, ErrorCode, Error<ErrorCode>>> SolveSP(
        string id,
        TaskIteration<IT, E, A> iteration,
        IReadOnlyList<E> executors,
        Map shadowPriceMap) {
        Result<IReadOnlyList<IT>, ErrorCode, Error<ErrorCode>> result = await _policy.TaskGenerator(iteration.Iteration, executors, shadowPriceMap);
        if (result.IsFailed) {
            return result switch {
                Failed<IReadOnlyList<IT>, ErrorCode, Error<ErrorCode>> f => Results.Failed<IReadOnlyList<IT>>(f.Error),
                Fatal<IReadOnlyList<IT>, ErrorCode, Error<ErrorCode>> ft => new Fatal<IReadOnlyList<IT>, ErrorCode, Error<ErrorCode>>(ft.Errors),
                _ => Results.Failed<IReadOnlyList<IT>>(new Err<ErrorCode>(ErrorCode.ApplicationError, "Task generation failed"))
            };
        }
        IReadOnlyList<IT> newTasks = ((Ok<IReadOnlyList<IT>, ErrorCode, Error<ErrorCode>>)result).Value;
        ++_subProblemSolvingTimes;

        iteration.RefreshLowerBound(newTasks, t => _policy.ReducedCost(shadowPriceMap, t));

        return Results.Ok(newTasks);
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

        foreach (ITaskBranchAndPriceContext<Args, IT, T, E, A> extractContext in _extractContexts) {
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

    private void AddColumns(ulong iteration, IReadOnlyList<IT> newTasks, LinearMetaModel<Flt64> model) {
        Result<IReadOnlyList<IT>, ErrorCode, Error<ErrorCode>> addResult = _context.AddColumns(iteration, newTasks, model);
        if (addResult.IsOk) {
            IReadOnlyList<IT> undupTasks = ((Ok<IReadOnlyList<IT>, ErrorCode, Error<ErrorCode>>)addResult).Value;
            if (undupTasks.Count > 0) {
                _context.Flush(iteration);
            }
        }
    }

    private Result<Flt64, ErrorCode, Error<ErrorCode>> RemoveColumns(
        Flt64 maximumReducedCost,
        ulong maximumColumnAmount,
        Map shadowPriceMap,
        ISet<IT> fixedTasks,
        ISet<IT> keptTasks,
        LinearMetaModel<Flt64> model) {
        return _context.RemoveColumns(
            maximumReducedCost,
            maximumColumnAmount,
            t => _policy.ReducedCost(shadowPriceMap, t),
            fixedTasks,
            keptTasks,
            model);
    }

    private void FixTasks(ulong iteration, LinearMetaModel<Flt64> model) {
        Result<ISet<IT>, ErrorCode, Error<ErrorCode>> result = _context.ExtractFixedTasks(iteration, model);
        if (result.IsOk) {
            foreach (IT t in ((Ok<ISet<IT>, ErrorCode, Error<ErrorCode>>)result).Value) {
                _fixedTasks.Add(t);
            }
        }
    }

    private void KeepTasks(ulong iteration, LinearMetaModel<Flt64> model) {
        Result<ISet<IT>, ErrorCode, Error<ErrorCode>> result = _context.ExtractKeptTasks(iteration, model);
        if (result.IsOk) {
            foreach (IT t in ((Ok<ISet<IT>, ErrorCode, Error<ErrorCode>>)result).Value) {
                _keptTasks.Add(t);
            }
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

    private ISet<E> SelectFreeExecutors(Map shadowPriceMap, LinearMetaModel<Flt64> model) {
        Result<ISet<E>, ErrorCode, Error<ErrorCode>> result = _context.SelectFreeExecutors(_fixedTasks, _hiddenExecutors, shadowPriceMap, model);
        if (result.IsOk) {
            return ((Ok<ISet<E>, ErrorCode, Error<ErrorCode>>)result).Value;
        }
        // Fallback: filter by fixed tasks and hidden executors
        var free = new HashSet<E>(_executors);
        foreach (IT task in _fixedTasks) {
            if (task.Executor is not null) {
                free.Remove(task.Executor);
            }
        }
        foreach (E e in _hiddenExecutors) {
            free.Remove(e);
        }
        return free;
    }

    private HashSet<IT> GloballyFix(ISet<E> freeExecutors) {
        var fixedTasks = new HashSet<IT>();
        foreach (IT task in _fixedTasks) {
            if (task.Executor is not null && !freeExecutors.Contains(task.Executor)) {
                fixedTasks.Add(task);
            }
        }
        _context.GloballyFix(fixedTasks);
        return fixedTasks;
    }

    private IReadOnlyList<IT> LocallyFix(ulong iteration, ISet<IT> fixedTasks, LinearMetaModel<Flt64> model) {
        var fixBar = new Flt64(0.9);
        Result<ISet<IT>, ErrorCode, Error<ErrorCode>> result = _context.LocallyFix(iteration, fixBar, fixedTasks, model);
        if (result.IsOk) {
            return ((Ok<ISet<IT>, ErrorCode, Error<ErrorCode>>)result).Value.ToList();
        }
        return Array.Empty<IT>();
    }

    private void Flush(ulong iteration) {
        _context.Flush(iteration);
        _keptTasks.Clear();
        _hiddenExecutors.Clear();
    }

    private Result<TaskSolution<T, E, A>, ErrorCode, Error<ErrorCode>> AnalyzeSolution(
        ulong iteration,
        LinearMetaModel<Flt64> model) => _context.AnalyzeSolution(iteration, _tasks, model);

    private ulong MinimumColumnAmount(ISet<IT> fixedTasks) {
        ulong fixedExecutorCount = (ulong)fixedTasks.Select(t => t.Executor).Where(e => e is not null).Distinct().Count();
        ulong notFixed = ExecutorAmount - fixedExecutorCount;
        return notFixed * _configuration.MinimumColumnAmountPerExecutor;
    }
}
