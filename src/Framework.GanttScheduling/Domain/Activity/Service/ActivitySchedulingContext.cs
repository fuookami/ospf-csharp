// 活动排程上下文 / Activity scheduling context.
// 与固定 Kotlin 对应。
// The pinned Kotlin: .

using System.Collections.Generic;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Activity.Model;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.GanttScheduling.Domain.Activity.Service;

/// <summary>
/// 活动排程上下文 / The activity scheduling context.
/// </summary>
/// <remarks>
/// 上下文是"一次注册"的载体：它持有候选清单，并把 `register` / `validate` / `extractSolution` 三个动作转发给聚合层。
/// 两处边界值得单独钉住：
///
/// 1. **命名空间必须合法**。基线要求 `[A-Za-z][A-Za-z0-9_]*`，因为这个名字会成为求解器里每条约束名的前缀。
///    放任任意字符串进去，两条不同上下文的约束名可能撞在一起，而求解器只按名字索引——**约束被静默覆盖**。
/// 2. **候选清单必须是快照**。基线写的是 `choices.toList`，即拷贝一份。若直接持有调用方的列表，
///    调用方之后一次 `Add` 就会**静默改变这个上下文的候选集**，而上下文没有任何迹象表明自己变了。
///
/// The context carries one registration: it holds the candidate list and forwards `register` / `validate` / `extractSolution` to the aggregation layer. Two boundaries are
/// worth pinning:
///
/// 1. **The namespace must be legal.** The code requires `[A-Za-z][A-Za-z0-9_]*` because that name becomes the prefix of every constraint name in the solver. Letting
///    arbitrary strings through lets two different contexts collide on constraint names, and the solver indexes by name alone — **constraints are silently overwritten**.
/// 2. **The candidate list must be a snapshot.** The code writes `choices.toList`, a copy. Holding the caller's list directly means one later `Add` by the caller
///    **silently changes this context's candidate set**, with the context showing no sign of having changed.
/// </remarks>
public sealed class ActivitySchedulingContext<V>
    where V : struct, IFloatingNumber<V> {
    /// <summary>
    /// 构造上下文 / Construct the context.
    /// </summary>
    /// <param name="choices">候选放置 / The candidate placements.</param>
    public ActivitySchedulingContext(IReadOnlyList<ActivityPlacement<V>> choices) {
        System.ArgumentNullException.ThrowIfNull(choices);

        // 拷贝一份：调用方之后改自己的列表不该改变这个上下文。
        // Copy: a later change to the caller's list must not change this context.
        var snapshot = new List<ActivityPlacement<V>>(choices.Count);
        foreach (ActivityPlacement<V> placement in choices) {
            snapshot.Add(placement);
        }
        Choices = snapshot;
    }

    private ActivitySchedulingContext(
        ActivitySchedulingProblem<V> problem,
        ActivitySchedulingAggregation aggregation,
        IReadOnlyList<ActivityPlacement<V>> choices) {
        Problem = problem;
        Aggregation = aggregation;
        Choices = choices;
    }

    /// <summary>候选放置的快照 / The snapshot of candidate placements.</summary>
    public IReadOnlyList<ActivityPlacement<V>> Choices { get; }

    /// <summary>原问题 / The source problem.</summary>
    public ActivitySchedulingProblem<V>? Problem { get; }

    /// <summary>聚合层 / The aggregation layer.</summary>
    public ActivitySchedulingAggregation? Aggregation { get; }

    /// <summary>
    /// 由问题建上下文 / Build a context from a problem.
    /// </summary>
    /// <param name="problem">问题 / The problem.</param>
    /// <param name="name">命名空间 / The namespace.</param>
    /// <returns>上下文或失败 / The context, or a failure.</returns>
    /// <remarks>
    /// 与基线 `ActivitySchedulingContext.create` 对齐，**顺序不可换**：
    /// 先校验命名空间（否则后面每一步都会带着一个非法前缀），再编译问题（否则聚合层拿不到索引），
    /// 最后才建上下文。**候选清单只收带 placement 的选择**——取消项不是"可排的候选"，
    /// 把它放进 `Choices` 会让调用方以为它排得进去。
    ///
    /// Aligned with its `ActivitySchedulingContext.create`, **the order is not interchangeable**: validate the namespace first (otherwise every later step carries an illegal prefix), then
    /// compile the problem (otherwise the aggregation has no indices), and only then build the context. **The candidate list takes only choices that carry a placement**, since a cancellation is not a
    /// schedulable candidate and putting it in `Choices` would tell the caller it can be placed.
    /// </remarks>
    public static Result<ActivitySchedulingContext<V>, ErrorCode, Error<ErrorCode>> Create(
        ActivitySchedulingProblem<V> problem,
        string name = "activity_scheduling") {
        System.ArgumentNullException.ThrowIfNull(problem);

        Result<Success, ErrorCode, Error<ErrorCode>> namespaceCheck = ValidateNamespace(name);
        if (namespaceCheck is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
            return namespaceCheck is Failed<Success, ErrorCode, Error<ErrorCode>> failed
                ? Results.Failed<ActivitySchedulingContext<V>>(failed.Error)
                : new Fatal<ActivitySchedulingContext<V>, ErrorCode, Error<ErrorCode>>(
                    ((Fatal<Success, ErrorCode, Error<ErrorCode>>)namespaceCheck).Errors);
        }

        // 基线经由 `SchedulingSolverValueAdapter<V>` 做数值转换；C# 侧该类型是静态类且下游固定 `Flt64`，
        // 因此这里只接受 `Flt64` 问题——与聚合层同一个口径。
        // The code converts through `SchedulingSolverValueAdapter<V>`; in C# that type is static and the downstream is fixed to `Flt64`,
        // so only an `Flt64` problem is accepted here — the same scope as the aggregation layer.
        if (problem is not ActivitySchedulingProblem<Flt64> concrete) {
            return Results.Failed<ActivitySchedulingContext<V>>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "only an Flt64 activity problem is supported / 只支持 Flt64 活动问题"));
        }

        Result<CompiledActivityProblem<Flt64>, ErrorCode, Error<ErrorCode>> compiled =
            ActivityProblemCompiler.Prepare(concrete);
        if (compiled is not Ok<CompiledActivityProblem<Flt64>, ErrorCode, Error<ErrorCode>> ok) {
            return compiled is Failed<CompiledActivityProblem<Flt64>, ErrorCode, Error<ErrorCode>> failed
                ? Results.Failed<ActivitySchedulingContext<V>>(failed.Error)
                : new Fatal<ActivitySchedulingContext<V>, ErrorCode, Error<ErrorCode>>(
                    ((Fatal<CompiledActivityProblem<Flt64>, ErrorCode, Error<ErrorCode>>)compiled).Errors);
        }

        var choices = new List<ActivityPlacement<V>>();
        foreach (CompiledActivityChoice<Flt64> choice in ok.Value.Choices) {
            if (choice.Placement is { } placement) {
                choices.Add((ActivityPlacement<V>)(object)placement);
            }
        }
        return Results.Ok(new ActivitySchedulingContext<V>(
            problem, new ActivitySchedulingAggregation(ok.Value, name), choices));
    }

    /// <summary>
    /// 注册到模型 / Register into a model.
    /// </summary>
    /// <param name="model">模型 / The model.</param>
    /// <param name="minimizeCost">是否最小化成本 / Whether to minimise the cost.</param>
    /// <returns>成功或失败 / Success or failure.</returns>
    public Result<Success, ErrorCode, Error<ErrorCode>> Register(
        Fuookami.Ospf.Core.Model.Mechanism.LinearMetaModel<Flt64> model,
        bool minimizeCost = true) {
        if (Aggregation is null) {
            return Results.Failed<Success>(new Err<ErrorCode>(ErrorCode.IllegalArgument,
                "this context was not built from a problem and cannot register"));
        }
        return Aggregation.Register(model, minimizeCost);
    }

    /// <summary>
    /// 复核一组候选 / Validate a set of choices.
    /// </summary>
    /// <param name="choices">候选键 / The candidate keys.</param>
    /// <returns>校验结果 / The validation result.</returns>
    public ActivityScheduleValidation Validate(IReadOnlyList<ActivityChoiceKey> choices) {
        if (Aggregation is null) {
            return new ActivityScheduleValidation(new[] { "context:no-aggregation" });
        }
        return Aggregation.Validate(choices);
    }

    /// <summary>
    /// 从模型取解 / Extract the schedule from a model.
    /// </summary>
    /// <param name="model">模型 / The model.</param>
    /// <returns>排程或错误 / The schedule or an error.</returns>
    public Result<ActivitySchedule<Flt64>, ErrorCode, Error<ErrorCode>> ExtractSolution(
        Fuookami.Ospf.Core.Model.Mechanism.LinearMetaModel<Flt64> model) {
        if (Aggregation is null) {
            return Results.Failed<ActivitySchedule<Flt64>>(new Err<ErrorCode>(ErrorCode.IllegalArgument,
                "this context was not built from a problem and cannot extract a solution"));
        }
        return Aggregation.ExtractSolution(model);
    }

    /// <summary>
    /// 校验模型命名空间 / Validate the model namespace.
    /// </summary>
    /// <param name="name">命名空间 / The namespace.</param>
    /// <returns>成功或非法命名空间的失败 / Success, or a failure for an illegal namespace.</returns>
    /// <remarks>
    /// 规则：**首字符必须是字母**，其余只能是字母、数字、下划线。首字符不允许数字，因为这个名字会直接拼进约束名，
    /// 而 `0_selection` 这样的名字在某些后端会被解析成别的东西。
    ///
    /// The rule matches the code: **the first character must be a letter**, the rest letters, digits or underscores. A leading digit is refused because the name goes
    /// straight into constraint names, and a name like `0_selection` gets parsed as something else by some backends.
    /// </remarks>
    public static Result<Success, ErrorCode, Error<ErrorCode>> ValidateNamespace(string? name) {
        if (string.IsNullOrEmpty(name)) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "模型命名空间为空 / Model namespace is empty"));
        }

        if (!char.IsAsciiLetter(name![0])) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "模型命名空间非法：首字符必须是字母 / Invalid model namespace: the first character must be a letter (" + name + ")"));
        }

        for (int index = 1; index < name!.Length; index++) {
            char character = name[index];
            if (!char.IsAsciiLetterOrDigit(character) && character != '_') {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.IllegalArgument,
                    "模型命名空间非法：只允许字母、数字与下划线 / Invalid model namespace: only letters, digits and underscores are allowed (" + name + ")"));
            }
        }
        return Results.Ok<Success>(Results.SuccessInstance);
    }
}
