// 任务×执行器成本项 / Task-executor cost terms.
// 与固定 Kotlin 基线 `task_compilation/Service/.../TaskExecutorCostMinimization.kt` 的
// `tasks.flatMap { executors.map { ... } }` 求和对应。
// Mirrors the `tasks.flatMap { executors.map { ... } }` sum in the pinned baseline's `TaskExecutorCostMinimization.kt`.

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.TaskCompilation.Service.Limits;

/// <summary>
/// 任务×执行器成本项 / Task-executor cost terms.
/// </summary>
/// <remarks>
/// 求和域是**全部 `任务 × 执行器`**，包含那些执行器对该任务根本不可用的组合。
/// 这看着浪费，但不可用的组合在 已经被钉成 `x = 0`，所以**加进去的项恒为零**——
/// 「不漏」比「少算」重要：若为了省项而按可用集合过滤，一旦 的收紧规则改了，这里就会静默漏掉本该计费的组合。
///
/// 与 的执行器目标有一处**故意的不对称**：那两件的提取器是可空的，`null` 表示"没配置这项惩罚"，于是**不加目标**；
/// 这里提取器不可空、缺省返回 `1`，所以**永远加目标**。照抄基线的这个差别，不替它统一。
///
/// **缺省系数 `1` 是个占位，不是有意义的成本。** 全部系数为 `1` 时，目标和等于 `Σ x[t,e]`，
/// 也就是**被执行的任数量**：任务都不可取消时这是个常数（目标等于任务数，什么都没优化）；
/// 一旦有任务可取消，最小化它就变成**尽可能多取消**。调用方必须给出真实的成本计算器。
///
/// The summation domain is **all `task × executor`**, including combinations where that executor is not available for the task. That looks wasteful, but unavailable combinations
/// are already pinned to `x = 0` by，so **the added term is identically zero** — and "miss nothing" beats "compute less": filtering by the available set would silently drop
/// combinations that should be charged once the tightening rule changes.
///
/// There is one **deliberate asymmetry** with the earlier executor objectives: there the extractor is nullable and `null` means "this penalty was not configured", so **no objective is
/// Added
///
/// **The default coefficient of `1` is a placeholder, not a meaningful cost.** With every coefficient at `1` the objective equals `Σ x[t,e]`, i.e. **the number of executed tasks**:
/// a constant when nothing is cancellable (the objective equals the task count and optimises nothing), and **"cancel as much as possible"** as soon as cancellation is allowed.
/// Callers must supply a real cost calculator.
/// </remarks>
public static class TaskExecutorCostTerms {
    /// <summary>一个成本项 / One cost term.</summary>
    /// <typeparam name="T">任务类型 / Task type.</typeparam>
    /// <typeparam name="E">执行器类型 / Executor type.</typeparam>
    /// <param name="Task">任务 / The task.</param>
    /// <param name="Executor">执行器 / The executor.</param>
    /// <param name="Weight">成本系数 / The cost coefficient.</param>
    public sealed record Term<T, E>(T Task, E Executor, Flt64 Weight);

    /// <summary>
    /// 构造全部成本项 / Build every cost term.
    /// </summary>
    /// <typeparam name="T">任务类型 / Task type.</typeparam>
    /// <typeparam name="E">执行器类型 / Executor type.</typeparam>
    /// <param name="tasks">任务列表 / The tasks.</param>
    /// <param name="executors">执行器列表 / The executors.</param>
    /// <param name="costOf">成本提取器 / The cost extractor.</param>
    /// <returns>成本项或失败 / The terms, or a failure.</returns>
    public static Result<IReadOnlyList<Term<T, E>>, ErrorCode, Error<ErrorCode>> Build<T, E>(
        IReadOnlyList<T> tasks,
        IReadOnlyList<E> executors,
        Func<T, E, Flt64?> costOf) {
        ArgumentNullException.ThrowIfNull(tasks);
        ArgumentNullException.ThrowIfNull(executors);
        ArgumentNullException.ThrowIfNull(costOf);

        var terms = new List<Term<T, E>>();
        foreach (T task in tasks) {
            foreach (E executor in executors) {
                Result<Flt64, ErrorCode, Error<ErrorCode>> weight =
                    ExecutorObjectiveTerm.Weight(costOf(task, executor));
                switch (weight) {
                    case Ok<Flt64, ErrorCode, Error<ErrorCode>> ok:
                        terms.Add(new Term<T, E>(task, executor, ok.Value));
                        break;
                    case Failed<Flt64, ErrorCode, Error<ErrorCode>> failed:
                        // 报第一个缺项就停下：与 的约束族一致，部分成本表比没有成本表更危险。
                        // Stop at the first gap, consistent with the constraint family: a partial cost table is more dangerous than none.
                        return Results.Failed<IReadOnlyList<Term<T, E>>>(failed.Error);
                    default:
                        return Results.Failed<IReadOnlyList<Term<T, E>>>(new Err<ErrorCode>(
                            ErrorCode.ApplicationError,
                            "成本提取失败 / Cost extraction failed"));
                }
            }
        }
        return Results.Ok<IReadOnlyList<Term<T, E>>>(terms);
    }
}
