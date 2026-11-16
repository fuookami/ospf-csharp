// 二次 IIS 的类型与计算入口 / Types and computation entry for quadratic IIS.
// **更正：文件头原写"入口骨架"，与实际实现不符，已改。**

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Solver.Iis;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<
    Fuookami.Ospf.Utils.Functional.Success,
    Fuookami.Ospf.Utils.Error.ErrorCode,
    Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Core.Solver.Iis;

/// <summary>
/// **二次 IIS 结果：子系统模型 + 证据来源** / Quadratic IIS result: the subsystem model plus the evidence source.
/// </summary>
/// <param name="Subsystem">IIS 子系统 / The IIS subsystem.</param>
/// <param name="OriginRows">每个子系统行对应的原模型行索引 / Origin row index for each subsystem row.</param>
/// <param name="OriginColumns">每个子系统列对应的原模型列索引 / Origin column index for each subsystem column.</param>
/// <remarks>
/// **曾把这里记为"最大单块缺失"**：`computeLegacyIIS`
/// （，846 行 / 12 个函数）当时在 C# 侧一行都没有。
///
/// **更正：本文件现有 612 行、`ComputeLegacyIISAsync` 已实现、
/// 空壳标记计数为 0。
/// "只建骨架"是一句过期结论，已删。**
///
/// 与线性侧对照：
///
/// | 线性 | 二次 |
/// | --- | --- |
/// | `LinearIisModel` | 本类型 |
/// | `LinearLegacyIisCompute.ComputeLegacyIISAsync` | `QuadraticLegacyIisCompute.ComputeLegacyIISAsync` |
/// | 490 行 | 612 行 |
///
/// **行数<b>不</b>等于"还缺多少"：** C# 用了不同的类型与错误表达，
/// **行数不是可比单位。能比的是入口与函数覆盖，而不是行数。**
///
/// **once recorded this as the "largest single missing block"** — its `computeLegacyIIS`
/// (846 lines, 12 functions) had nothing on the C# side then.
///
/// **A correction: this file now has 612 lines, `ComputeLegacyIISAsync` is implemented, and the
/// stub-marker count is zero.** **"Only a skeleton" was a stale
/// conclusion and has been removed.**
///
/// **Line counts do <b>not</b> mean "how much remains": C# uses different types and error expression, so line
/// counts are not a comparable unit. Entry points and function coverage are.**
/// establishes the **types and entry skeleton** rather than attempting the whole port at once (see the comparison table above).
/// </remarks>
public sealed record QuadraticIisModel(
    IQuadraticTetradModelView Subsystem,
    IReadOnlyList<int> OriginRows,
    IReadOnlyList<int> OriginColumns
);

/// <summary>
/// **一次松弛求解的结果** / The result of one relaxation solve.
/// </summary>
/// <param name="Feasible">是否找到可行点 / Whether a feasible point was found.</param>
/// <param name="Relaxed">变量索引到松弛量的映射 / Variable index to relaxed amount.</param>
public sealed record QuadraticElasticFilterResult(
    bool Feasible,
    IReadOnlyDictionary<int, Flt64> Relaxed);

/// <summary>
/// **二次 IIS 的证据来源** / Quadratic IIS evidence source.
/// </summary>
/// <remarks>
/// 与`InfeasibilityEvidenceSource` 同名同义：弹性过滤先跑，失败才跑删除过滤。
///
/// The elastic filter runs first, and the deletion filter only if it fails.
/// </remarks>
public enum QuadraticIisEvidenceSource {
    /// <summary>弹性过滤找到子系统 / The elastic filter found the subsystem.</summary>
    ElasticFilter,

    /// <summary>删除过滤找到子系统 / The deletion filter found the subsystem.</summary>
    DeletionFilter,
}

/// <summary>
/// **二次 IIS 计算入口（骨架，未实现）** / Quadratic IIS entry point (skeleton, unimplemented).
/// </summary>
/// <remarks>
/// **本类刻意不实现算法。** 完整流程是：
///
/// 1. `model.elastic` 构建弹性模型；
/// 2. 统计弹性边界约束数与弹性约束数；
/// 3. `performElasticFiltering` —— 先试弹性过滤；
/// 4. 弹性过滤成功 ⇒ 上报状态、`dump` 出子系统、来源标 `ElasticFilter`；
/// 5. 否则 `performDeletionFiltering` —— 删除过滤；
/// 6. 删除结果为空 ⇒ 返回原模型快照；否则 `dump` 出子系统、来源标 `DeletionFilter`。
///
/// **每一步都需要真实二次求解器**（`performElasticFiltering` 反复求解可行性），
/// 而当前环境没有 Gurobi SDK（已实测确认）。
/// 所以这里**只建立入口**，让"二次 IIS 未实现"从一个**文档记录**变成一个
/// **可编译、可断言、可被后续批次填充**的结构。
///
/// **This class deliberately does not implement the algorithm.** Its `computeLegacyIIS` runs the six steps above, and **every one needs a real quadratic
/// solver** (`performElasticFiltering` repeatedly solves feasibility) — which this environment lacks (measured). So **only establishes the entry
/// point**, turning "quadratic IIS is unimplemented" from a **doc note** into a **compilable, assertable structure that later batches can fill in**.
/// </remarks>
public static class QuadraticLegacyIisCompute {
    /// <summary>
    /// **按条件松弛特定组件后求解一次** / Relax the components matching a condition, then solve once.
    /// </summary>
    /// <param name="elasticModel">弹性模型 / Elastic model.</param>
    /// <param name="solver">二次求解器 / Quadratic solver.</param>
    /// <param name="tolerance">松弛容差 / Slack tolerance.</param>
    /// <param name="relaxCondition">判断某变量是否应被松弛 / Predicate deciding whether a variable is relaxed.</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token.</param>
    /// <returns>是否找到可行解，以及松弛量的映射 / Whether a feasible point was found, plus the relaxed amounts.</returns>
    /// <remarks>
    /// 对应`relaxSpecificComponents`。**关键差异（有意）**：
    ///
    /// `ModelViewVariable.UpperBound` **是只读的**，无法原地改写上界。
    /// 所以这里**按调整后的上界重建一个模型**再求解——语义等价（带松弛的变量上界置 `+∞` 或 `0`），
    /// 但**不修改调用方的模型**，因此不会把脏状态留在原对象上。
    ///
    /// 重建比"打开只读性"更安全，代价是一次模型构造。
    ///
    /// `UpperBound` is **read-only**, so this **rebuilds a model with the adjusted upper bounds** and solves that — semantically equivalent (relaxed variables get `+∞`
    /// or `0`) but **the caller's model is left untouched**, so no dirty state leaks. Rebuilding is safer than loosening readonly-ness, at the cost of one model construction.
    /// </remarks>
    public static async Task<Result<QuadraticElasticFilterResult, ErrorCode, Error<ErrorCode>>> RelaxSpecificComponentsAsync(
        IQuadraticTetradModelView elasticModel,
        IAbstractQuadraticSolver solver,
        Flt64 tolerance,
        global::System.Func<ModelViewVariable, bool> relaxCondition,
        CancellationToken cancellationToken = default) {
        INumericConstants<Flt64> constants = NumericConstantsRegistry.For<Flt64>();
        Flt64 positiveInfinity = constants.PositiveInfinity!.Value;

        var variables = new List<ModelViewVariable>(elasticModel.Variables.Count);
        foreach (ModelViewVariable variable in elasticModel.Variables) {
            if (variable.Slack is null) {
                variables.Add(variable);
                continue;
            }
            variables.Add(new ModelViewVariable(
                index: variable.Index,
                lowerBound: variable.LowerBound,
                upperBound: relaxCondition(variable) ? positiveInfinity : Flt64.Zero,
                type: variable.Type,
                name: variable.Name,
                initialResult: variable.InitialResult,
                slack: variable.Slack,
                origin: variable.Origin,
                identity: variable.Identity,
                identityNamespace: variable.IdentityNamespace,
                identitySchemaVersion: variable.IdentitySchemaVersion));
        }

        QuadraticTetradModel relaxedModel = RebuildWithUpperBounds(elasticModel, variables);

        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> solved =
            await solver.InvokeReportAsync(relaxedModel, solvingStatusCallBack: null, cancellationToken: cancellationToken);

        if (solved is Failed<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> failed) {
            return failed.Code is ErrorCode.ORModelInfeasible or ErrorCode.ORModelInfeasibleOrUnbounded
                ? Results.Ok(new QuadraticElasticFilterResult(Feasible: false, Relaxed: new Dictionary<int, Flt64>()))
                : Results.Failed<QuadraticElasticFilterResult>(failed.Error);
        }
        if (solved is Fatal<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> fatal) {
            return new Fatal<QuadraticElasticFilterResult, ErrorCode, Error<ErrorCode>>(fatal.Errors);
        }

        SolveReport<Flt64> report = ((Ok<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>>)solved).Value;
        switch (report.ProblemStatus) {
            case ProblemStatus.Infeasible:
            case ProblemStatus.InfeasibleOrUnbounded:
                return Results.Ok(new QuadraticElasticFilterResult(Feasible: false, Relaxed: new Dictionary<int, Flt64>()));
            case ProblemStatus.Feasible:
                break;
            default:
                return Results.Failed<QuadraticElasticFilterResult>(new Err<ErrorCode>(
                    ErrorCode.OREngineSolvingException,
                    $"elastic quadratic solve returned an inconclusive status: {report.ProblemStatus}"));
        }

        if (report.Solution is not { } solution) {
            return Results.Failed<QuadraticElasticFilterResult>(new Err<ErrorCode>(
                ErrorCode.ORSolutionInvalid,
                "elastic quadratic solve reported feasible without a solution"));
        }

        var relaxed = new Dictionary<int, Flt64>();
        foreach (ModelViewVariable variable in variables) {
            if (variable.Slack is null || variable.Index >= solution.Length) {
                continue;
            }
            Flt64 value = solution[variable.Index];
            if (value >= tolerance) {
                relaxed[variable.Index] = value;
            }
        }
        return Results.Ok(new QuadraticElasticFilterResult(Feasible: true, Relaxed: relaxed));
    }

    /// <summary>
    /// **按给定变量列表重建模型** / Rebuild the model from a given variable list.
    /// </summary>
    /// <param name="source">源模型 / Source model.</param>
    /// <param name="variables">替换用的变量列表 / Replacement variable list.</param>
    /// <returns>重建的模型 / The rebuilt model.</returns>
    /// <remarks>
    /// **调用方不得 `using` 返回值。** 重建的模型**共享源模型的约束批次与目标**，
    /// 释放它会关闭那个批次，后续调用拿到 `ApplicationStopped / 约束批次已关闭`。
    /// 实测：三层递进的第 2 层就是这样被打断的。
    ///
    /// **Callers must not `using` the result.** The rebuilt model **shares the source's constraint batch and objective**; disposing it closes that batch and later
    /// calls fail with `ApplicationStopped / constraint batch is closed`. Measured: this is exactly what broke tier 2 of the escalation .
    /// </remarks>
    private static QuadraticTetradModel RebuildWithUpperBounds(
        IQuadraticTetradModelView source,
        IReadOnlyList<ModelViewVariable> variables) =>
        new(
            variables: variables,
            constraints: (QuadraticConstraintBatch)source.Constraints,
            objective: source.Objective,
            name: source.Name);

    /// <summary>
    /// **弹性过滤：三层递进地放宽，直到找到可行点** / Elastic filtering: escalate the relaxation in three tiers until a feasible point appears.
    /// </summary>
    /// <param name="elasticModel">弹性模型 / Elastic model.</param>
    /// <param name="solver">二次求解器 / Quadratic solver.</param>
    /// <param name="tolerance">松弛容差 / Slack tolerance.</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token.</param>
    /// <returns>是否找到了可行点，以及被松弛的组件 / Whether a feasible point was found, plus the relaxed components.</returns>
    /// <remarks>
    /// 对应`performElasticFiltering`，**三层递进**：
    ///
    /// | 层 | 松弛条件 | 含义 |
    /// | --- | --- | --- |
    /// | 1 | 只放宽**变量边界** | 最保守，尽量少动 |
    /// | 2 | 再放宽**不等式约束** | 不足以恢复可行时扩大范围 |
    /// | 3 | 放宽**所有带松弛的组件** | 兜底 |
    ///
    /// **顺序不能颠倒**：越靠前的前提越弱（改动越小），IIS 越"必要"。
    /// 一旦某层可行就**立即返回**，不再往下走——继续放宽只会给出更大的松弛集合，
    /// 那会**削弱 IIS 的证据力**（松弛得越多，越容易被误读成"这条约束不必要"）。
    ///
    /// 三层全不可行时返回 `(false, 空)`，由调用方转去删除过滤。
    ///
    /// With the **three tiers** above. **The order must not be inverted**: earlier tiers are weaker assumptions
    /// (smaller changes), making the IIS more necessary. As soon as a tier is feasible it **returns immediately** — relaxing further would only produce a larger relaxed
    /// set, which **weakens the evidence** (the more is relaxed, the easier it is to misread a constraint as unnecessary). If all three fail it returns `(false, empty)` and
    /// the caller falls back to deletion filtering.
    /// </remarks>
    public static async Task<Result<QuadraticElasticFilterResult, ErrorCode, Error<ErrorCode>>> PerformElasticFilteringAsync(
        IQuadraticTetradModelView elasticModel,
        IAbstractQuadraticSolver solver,
        Flt64 tolerance,
        CancellationToken cancellationToken = default) {
        var tiers = new global::System.Func<ModelViewVariable, bool>[] {
            variable => variable.Slack?.LowerBound is not null || variable.Slack?.UpperBound is not null,
            variable => variable.Slack?.Constraint is { } constraint
                    && (constraint.Relation == ConstraintRelation.LessEqual
                        || constraint.Relation == ConstraintRelation.GreaterEqual)
                || variable.Slack?.LowerBound is not null
                || variable.Slack?.UpperBound is not null,
            variable => variable.Slack is not null,
        };

        foreach (global::System.Func<ModelViewVariable, bool> relaxCondition in tiers) {
            Result<QuadraticElasticFilterResult, ErrorCode, Error<ErrorCode>> tier = await RelaxSpecificComponentsAsync(
                elasticModel, solver, tolerance, relaxCondition, cancellationToken);

            if (tier is Failed<QuadraticElasticFilterResult, ErrorCode, Error<ErrorCode>> failed) {
                return failed;
            }
            if (tier is Fatal<QuadraticElasticFilterResult, ErrorCode, Error<ErrorCode>> fatal) {
                return fatal;
            }
            var ok = (Ok<QuadraticElasticFilterResult, ErrorCode, Error<ErrorCode>>)tier;
            if (ok.Value.Feasible) {
                return tier;
            }
        }

        return Results.Ok(new QuadraticElasticFilterResult(Feasible: false, Relaxed: new Dictionary<int, Flt64>()));
    }

    /// <summary>
    /// **删除过滤：逐个关掉松弛列，看可行性能否被恢复** / Deletion filtering: close slack columns one by one and see whether feasibility returns.
    /// </summary>
    /// <param name="elasticModel">弹性模型 / Elastic model.</param>
    /// <param name="solver">二次求解器 / Quadratic solver.</param>
    /// <param name="tolerance">松弛容差 / Slack tolerance.</param>
    /// <param name="elapsed">已耗时 / Elapsed time so far.</param>
    /// <param name="boundAmount">边界松弛总数 / Total bound slacks.</param>
    /// <param name="constraintAmount">约束松弛总数 / Total constraint slacks.</param>
    /// <param name="config">IIS 配置 / IIS config.</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token.</param>
    /// <returns>仍必须保持松弛的列索引 / Indices of the columns that must stay relaxed.</returns>
    /// <remarks>
    /// 对应`performDeletionFiltering`。**逐个**把候选松弛列的**上界压到 `0`**（即"关掉这个松弛"），
    /// 再求解一次：
    ///
    /// | 结果 | 含义 | 处理 |
    /// | --- | --- | --- |
    /// | 可行 | 这个松弛**不必要** | 从活跃集合里删掉，**保持关闭** |
    /// | 不可行 | 这个松弛**必要** | 恢复为 `+∞`，留在活跃集合 |
    ///
    /// 走完之后，活跃集合就是**最小松弛集**——每条都是"IIS 的证据"。
    ///
    /// **每次探测重建模型**（`UpperBound` 只读），因此用 `closed` 集合显式记住
    /// "哪些列当前处于关闭状态"，语义与原地改写等价。
    ///
    /// **one by one** push a candidate slack's **upper bound to `0`** (i.e. "turn that relaxation off") and re-solve
    /// (see the table above for the two outcomes). When the loop ends, the active set is the **minimal relaxed set** — each member is evidence for the IIS.
    ///
 /// **One difference**: the implementation mutates `candidate._upperBound` and re-solves in place; here **each probe rebuilds the model**
    /// (`UpperBound` is read-only), so a `closed` set explicitly records which columns are currently off, equivalent to its in-place mutation.
    /// </remarks>
    public static async Task<Result<IReadOnlyList<int>, ErrorCode, Error<ErrorCode>>> PerformDeletionFilteringAsync(
        IQuadraticTetradModelView elasticModel,
        IAbstractQuadraticSolver solver,
        Flt64 tolerance,
        global::System.TimeSpan elapsed,
        ulong boundAmount,
        ulong constraintAmount,
        IisConfig config,
        CancellationToken cancellationToken = default) {
        _ = tolerance;
        var candidates = new List<int>();
        for (int index = 0; index < elasticModel.Variables.Count; index++) {
            if (elasticModel.Variables[index].Slack is not null) {
                candidates.Add(index);
            }
        }
        if (candidates.Count == 0) {
            return Results.Ok<IReadOnlyList<int>>(global::System.Array.Empty<int>());
        }

        var active = new SortedSet<int>(candidates);
        var closed = new SortedSet<int>();

        foreach (int candidate in candidates) {
            if (!active.Contains(candidate)) {
                continue;
            }
            closed.Add(candidate);

            var probeVariables = new List<ModelViewVariable>(elasticModel.Variables.Count);
            foreach (ModelViewVariable variable in elasticModel.Variables) {
                probeVariables.Add(closed.Contains(variable.Index)
                    ? WithUpperBound(variable, Flt64.Zero)
                    : variable);
            }

            Result<bool, ErrorCode, Error<ErrorCode>> feasible = await SolveFeasibilityAsync(
                RebuildWithUpperBounds(elasticModel, probeVariables), solver, cancellationToken);
            if (feasible is not Ok<bool, ErrorCode, Error<ErrorCode>> probe) {
                return Propagate<IReadOnlyList<int>, bool>(feasible);
            }
            if (probe.Value) {
                active.Remove(candidate);
            }
            else {
                closed.Remove(candidate);
            }

            if (config.ComputingStatusCallBack is { } callback) {
                Try notified = callback(
                    true,
                    elapsed,
                    new IisComputingStatus(
                        RestBoundAmount: (ulong)global::System.Linq.Enumerable.Count(active, index => IsBoundSlack(elasticModel.Variables[index])),
                        TotalBoundAmount: boundAmount,
                        RestConstraintAmount: (ulong)global::System.Linq.Enumerable.Count(active, index => IsRowSlack(elasticModel.Variables[index])),
                        TotalConstraintAmount: constraintAmount));
                if (!notified.IsOk) {
                    return Propagate<IReadOnlyList<int>>(notified);
                }
            }
        }

        return Results.Ok<IReadOnlyList<int>>([.. active]);
    }

    /// <summary>复制变量并替换上界 / Copy a variable with a replaced upper bound.</summary>
    /// <param name="variable">源变量 / Source variable.</param>
    /// <param name="upperBound">新上界 / New upper bound.</param>
    /// <returns>复制后的变量 / The copied variable.</returns>
    private static ModelViewVariable WithUpperBound(ModelViewVariable variable, Flt64 upperBound) =>
        new(
            index: variable.Index,
            lowerBound: variable.LowerBound,
            upperBound: upperBound,
            type: variable.Type,
            name: variable.Name,
            initialResult: variable.InitialResult,
            slack: variable.Slack,
            origin: variable.Origin,
            identity: variable.Identity,
            identityNamespace: variable.IdentityNamespace,
            identitySchemaVersion: variable.IdentitySchemaVersion);

    /// <summary>是否边界松弛 / Whether the variable is a bound slack.</summary>
    /// <param name="variable">变量 / Variable.</param>
    /// <returns>判断结果 / The verdict.</returns>
    private static bool IsBoundSlack(ModelViewVariable variable) =>
        variable.Slack?.LowerBound is not null || variable.Slack?.UpperBound is not null;

    /// <summary>是否行松弛 / Whether the variable is a row slack.</summary>
    /// <param name="variable">变量 / Variable.</param>
    /// <returns>判断结果 / The verdict.</returns>
    private static bool IsRowSlack(ModelViewVariable variable) => variable.Slack?.Constraint is not null;

    /// <summary>只判定可行性，不读取解向量 / Decide feasibility only and never read the solution vector.</summary>
    /// <param name="model">待判定模型 / Model to decide on.</param>
    /// <param name="solver">二次求解器 / Quadratic solver.</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token.</param>
    /// <returns>是否可行 / Whether it is feasible.</returns>
    private static async Task<Result<bool, ErrorCode, Error<ErrorCode>>> SolveFeasibilityAsync(
        IQuadraticTetradModelView model,
        IAbstractQuadraticSolver solver,
        CancellationToken cancellationToken) {
        Result<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> report = await solver.InvokeReportAsync(
            model, solvingStatusCallBack: null, cancellationToken: cancellationToken);

        if (report is Failed<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> failed) {
            return failed.Error.Code is ErrorCode.ORModelInfeasible or ErrorCode.ORModelInfeasibleOrUnbounded
                ? Results.Ok(false)
                : Results.Failed<bool>(failed.Error);
        }
        if (report is not Ok<SolveReport<Flt64>, ErrorCode, Error<ErrorCode>> ok) {
            return Propagate<bool, SolveReport<Flt64>>(report);
        }
        return ok.Value.ProblemStatus switch {
            ProblemStatus.Feasible => Results.Ok(true),
            ProblemStatus.Infeasible => Results.Ok(false),
            ProblemStatus.InfeasibleOrUnbounded => Results.Ok(false),
            ProblemStatus status => Results.Failed<bool>(new Err<ErrorCode>(
                ErrorCode.OREngineSolvingException,
                $"IIS 删除过滤收到无法判定可行性的终态：{status} / IIS deletion filtering received an inconclusive terminal status: {status}")),
        };
    }

    /// <summary>把同码型的失败/致命结果转成本函数的返回类型 / Re-shape a failure or fatal result into this function's return type.</summary>
    /// <typeparam name="T">目标载荷类型 / Target payload type.</typeparam>
    /// <typeparam name="U">源载荷类型 / Source payload type.</typeparam>
    /// <param name="result">源结果 / Source result.</param>
    /// <returns>转换后的结果 / The re-shaped result.</returns>
    private static Result<T, ErrorCode, Error<ErrorCode>> Propagate<T, U>(
        Result<U, ErrorCode, Error<ErrorCode>> result) =>
        result switch {
            Failed<U, ErrorCode, Error<ErrorCode>> failed => Results.Failed<T>(failed.Error),
            Fatal<U, ErrorCode, Error<ErrorCode>> fatal => new Fatal<T, ErrorCode, Error<ErrorCode>>(fatal.Errors),
            _ => Results.Ok(default(T)!),
        };

    /// <summary>把 `Try` 转成本函数的返回类型 / Re-shape a `Try` into this function's return type.</summary>
    /// <typeparam name="T">目标载荷类型 / Target payload type.</typeparam>
    /// <param name="result">源结果 / Source result.</param>
    /// <returns>转换后的结果 / The re-shaped result.</returns>
    private static Result<T, ErrorCode, Error<ErrorCode>> Propagate<T>(Try result) =>
        result switch {
            Failed<Success, ErrorCode, Error<ErrorCode>> failed => Results.Failed<T>(failed.Error),
            Fatal<Success, ErrorCode, Error<ErrorCode>> fatal => new Fatal<T, ErrorCode, Error<ErrorCode>>(fatal.Errors),
            _ => Results.Ok(default(T)!),
        };

    /// <summary>
    /// **二次 IIS 的完整入口** / The complete entry point for quadratic IIS.
    /// </summary>
    /// <param name="solver">二次求解器 / Quadratic solver.</param>
    /// <param name="model">待分析模型 / Model to analyze.</param>
    /// <param name="config">IIS 配置 / IIS config.</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token.</param>
    /// <returns>IIS 子系统，或失败 / The IIS subsystem, or a failure.</returns>
    /// <remarks>
    /// **之前这里恒返回 `Failed`**，因为弹性过滤与删除过滤都还没实现。现在两个都就位，本函数接上完整流程：
    ///
    /// | 阶段 | 说明 |
    /// | --- | --- |
    /// | 1. 校验 | `config.ValidateRuntime`，参数非法直接失败 |
    /// | 2. 弹性化 | `model.Elastic` 生成带松弛列的模型 |
    /// | 3. 弹性过滤 | 三层递进放宽，**命中即可行** ⇒ 直接出 IIS |
    /// | 4. 删除过滤 | 弹性过滤全不可行 ⇒ 逐个关掉松弛列求最小松弛集 |
    /// | 5. 回溯 | 由松弛列回溯原模型的行与列 |
    ///
    /// **弹性过滤命中时 `OriginRows` 为空是正常的**：那表示"原始模型本身可行"，
    /// 不存在不可行子系统。同样情形下也返回空集。
    ///
    /// **Before this always returned `Failed`** because neither filter was implemented. Both are now in place, so this wires the full flow (table above). **An empty
 /// `OriginRows` when the elastic filter succeeds is expected**: it means the original model was feasible, so there is no infeasible subsystem — this implementation returns
    /// an empty set in that case too.
    /// </remarks>
    public static async Task<Result<QuadraticIisModel, ErrorCode, Error<ErrorCode>>> ComputeLegacyIISAsync(
        IQuadraticTetradModelView model,
        IAbstractQuadraticSolver solver,
        IisConfig config,
        CancellationToken cancellationToken = default) {
        Try valid = config.ValidateRuntime();
        if (!valid.IsOk) {
            return Propagate<QuadraticIisModel>(valid);
        }

        if (model is not QuadraticTetradModel origin) {
            return Results.Failed<QuadraticIisModel>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "Legacy IIS 需要可构建弹性模型的二次四元模型 / " +
                    "Legacy IIS needs a quadratic tetrad model that can build an elastic model"));
        }
        QuadraticTetradModel elastic = origin.Elastic();
        var started = global::System.Diagnostics.Stopwatch.StartNew();
        IReadOnlyList<ConstraintSource> sources = elastic.Constraints.Sources;
        ulong boundAmount = (ulong)sources.Count(
            source => source is ConstraintSource.ElasticLowerBound or ConstraintSource.ElasticUpperBound);
        ulong constraintAmount = (ulong)sources.Count(source => source == ConstraintSource.Elastic);

        Result<QuadraticElasticFilterResult, ErrorCode, Error<ErrorCode>> filtering =
            await PerformElasticFilteringAsync(elastic, solver, config.SlackTolerance, cancellationToken);
        if (filtering is not Ok<QuadraticElasticFilterResult, ErrorCode, Error<ErrorCode>> filter) {
            return Propagate<QuadraticIisModel, QuadraticElasticFilterResult>(filtering);
        }

        if (filter.Value.Feasible) {
            Try notified = Notify(
                config: config,
                elapsed: started.Elapsed,
                status: new IisComputingStatus(
                    RestBoundAmount: (ulong)filter.Value.Relaxed.Keys.Count(
                        index => IsBoundSlack(elastic.Variables[index])),
                    TotalBoundAmount: boundAmount,
                    RestConstraintAmount: (ulong)filter.Value.Relaxed.Keys.Count(
                        index => IsRowSlack(elastic.Variables[index])),
                    TotalConstraintAmount: constraintAmount));
            if (!notified.IsOk) {
                return Propagate<QuadraticIisModel>(notified);
            }
            return Results.Ok(Dump(elastic, filter.Value.Relaxed.Keys));
        }

        Try total = Notify(
            config: config,
            elapsed: started.Elapsed,
            status: new IisComputingStatus(
                RestBoundAmount: boundAmount,
                TotalBoundAmount: boundAmount,
                RestConstraintAmount: constraintAmount,
                TotalConstraintAmount: constraintAmount));
        if (!total.IsOk) {
            return Propagate<QuadraticIisModel>(total);
        }

        Result<IReadOnlyList<int>, ErrorCode, Error<ErrorCode>> deletion = await PerformDeletionFilteringAsync(
            elasticModel: elastic,
            solver: solver,
            tolerance: config.SlackTolerance,
            elapsed: started.Elapsed,
            boundAmount: boundAmount,
            constraintAmount: constraintAmount,
            config: config,
            cancellationToken: cancellationToken);
        if (deletion is not Ok<IReadOnlyList<int>, ErrorCode, Error<ErrorCode>> reduced) {
            return Propagate<QuadraticIisModel, IReadOnlyList<int>>(deletion);
        }

        return Results.Ok(Dump(elastic, reduced.Value));
    }

    /// <summary>通知进度回调 / Notify the progress callback.</summary>
    /// <param name="config">IIS 配置 / IIS config.</param>
    /// <param name="elapsed">已耗时 / Elapsed time.</param>
    /// <param name="status">当前状态 / Current status.</param>
    /// <returns>回调结果 / The callback result.</returns>
    private static Try Notify(IisConfig config, global::System.TimeSpan elapsed, IisComputingStatus status) =>
        config.ComputingStatusCallBack is { } callback
            ? callback(true, elapsed, status)
            : Results.OkInstance;

    /// <summary>
    /// 由松弛列集合回溯原模型行、列并构造 IIS 模型 / Trace origin rows and columns from the slack set and build the IIS model.
    /// </summary>
    /// <param name="elastic">弹性模型 / Elastic model.</param>
    /// <param name="slackIndices">必须保持松弛的列索引 / Indices of columns that must stay relaxed.</param>
    /// <returns>IIS 子系统 / The IIS subsystem.</returns>
    private static QuadraticIisModel Dump(QuadraticTetradModel elastic, IEnumerable<int> slackIndices) {
        List<int> slacks = [.. slackIndices];
        List<int> rows = IisStructuralSupport.RelatedRows(elastic.Variables, slacks);
        var constraints = (QuadraticConstraintBatch)elastic.Constraints;
        var rowMap = new Dictionary<int, int>();
        for (int index = 0; index < rows.Count; index++) {
            rowMap[rows[index]] = index;
        }

        List<IisRelatedVariable> related = IisStructuralSupport.RelatedVariables(
            elastic.Variables, constraints, rows, slacks);

        var columns = new List<int>();
        var variables = new List<ModelViewVariable>(related.Count);
        foreach (IisRelatedVariable entry in related) {
            columns.Add(entry.Variable.Index);
            variables.Add(LinearIisCompute.ProjectColumn(
                source: entry.Variable,
                index: variables.Count,
                lowerBound: entry.LowerBound ?? entry.Variable.LowerBound,
                upperBound: entry.UpperBound ?? entry.Variable.UpperBound));
        }

        var subsystem = new QuadraticTetradModel(
            variables: variables,
            constraints: IisStructuralSupport.FilterConstraintByRowIndex(constraints, rows, rowMap),
            objective: elastic.Objective,
            name: $"{elastic.Name}_iis");

        return new QuadraticIisModel(
            Subsystem: subsystem,
            OriginRows: rows,
            OriginColumns: columns);
    }
}
