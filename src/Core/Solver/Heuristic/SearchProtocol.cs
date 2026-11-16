// 启发式搜索协议 / The heuristic search protocol.
// 与固定 Kotlin （9819 字节）对应。

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Fuookami.Ospf.Core.Model.Callback;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<
    Fuookami.Ospf.Utils.Functional.Success,
    Fuookami.Ospf.Utils.Error.ErrorCode,
    Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Core.Solver.Heuristic;

/// <summary>
/// 搜索终止原因 / The search termination reason.
/// </summary>
public enum SearchTermination {
    /// <summary>片段额度用尽 / The slice allowance was spent.</summary>
    SliceComplete,

    /// <summary>评估上限用尽 / The evaluation limit was hit.</summary>
    EvaluationLimit,

    /// <summary>步数上限用尽 / The step limit was hit.</summary>
    StepLimit,

    /// <summary>时间上限用尽 / The time limit was hit.</summary>
    TimeLimit,

    /// <summary>被外部停止 / Stopped externally.</summary>
    Stopped,

    /// <summary>没有候选 / No candidate remained.</summary>
    NoCandidate,

    /// <summary>失败 / Failed.</summary>
    Failed,
}

/// <summary>
/// 解编码能力 / The solution-encoding capability.
/// </summary>
/// <remarks>
/// **`Custom` 必须显式适配**。自定义编码（变长排列、混合整数）无法从类型推断出交叉与变异算子该怎么作用；把它当成 `Continuous`
/// 会让算子按连续区间取点，得到的个体在自定义编码里根本没有意义，而求解器不会报错。
/// **`Custom` requires explicit adaptation.** A custom encoding (variable-length permutation, mixed integer) cannot have its
/// crossover and mutation operators inferred from the type; treating it as `Continuous` would make operators sample a real interval,
/// producing individuals that are meaningless in the custom encoding while nothing raises an error.
/// </remarks>
public enum SearchEncoding {
    /// <summary>连续实数 / Continuous reals.</summary>
    Continuous,

    /// <summary>排列 / Permutation.</summary>
    Permutation,

    /// <summary>自定义编码 / Custom encoding.</summary>
    Custom,
}

/// <summary>
/// 无损状态编码协议 / A lossless state codec.
/// </summary>
/// <remarks>
/// **编码必须无损**。有损编码会让检查点恢复后的搜索轨迹与原始运行不一致；这类偏差只在比较两条轨迹时才显现，单次运行看起来完全正常。
/// **Encoding must be lossless.** A lossy codec makes a restored trace diverge from the original run; that divergence only shows up
/// when comparing two traces, while either run alone looks perfectly normal.
/// </remarks>
/// <typeparam name="T">被编码的状态 / The encoded state.</typeparam>
public interface ISearchCodec<T> {
    /// <summary>
    /// 编码状态 / Encode the state.
    /// </summary>
    /// <param name="value">原始值 / The original value.</param>
    /// <returns>JSON 值 / The JSON value.</returns>
    Result<JsonElement, ErrorCode, Error<ErrorCode>> Encode(T value);

    /// <summary>
    /// 解码状态 / Decode the state.
    /// </summary>
    /// <param name="value">JSON 值 / The JSON value.</param>
    /// <returns>原始值 / The original value.</returns>
    Result<T, ErrorCode, Error<ErrorCode>> Decode(JsonElement value);
}

/// <summary>
/// 问题回调闭包的可恢复状态，令牌缓存无需保存 / Recoverable callback state; disposable token caches are not saved.
/// </summary>
public interface ISearchProblemState {
    /// <summary>
    /// 保存回调状态 / Save the callback state.
    /// </summary>
    /// <returns>状态 / The state.</returns>
    Result<JsonElement, ErrorCode, Error<ErrorCode>> Save();

    /// <summary>
    /// 恢复回调状态 / Restore the callback state.
    /// </summary>
    /// <param name="state">状态 / The state.</param>
    /// <returns>恢复结果 / The restore result.</returns>
    Try Load(JsonElement state);
}

/// <summary>
/// 问题边界，目标比较与支配由调用方定义 / The problem boundary, with caller-defined ordering and dominance.
/// </summary>
/// <remarks>
/// **`Fingerprint` 必须覆盖输入与目标定义**。只覆盖输入会让"同一输入、不同目标"的两个问题被当成同一个，检查点因此可以跨目标复用，
/// 而恢复出来的档案与当前目标根本不可比。
/// **`Fingerprint` must cover both the input and the objective definition.** Covering only the input would let two problems with the
/// same input but different objectives look identical, so a checkpoint could be reused across objectives and the restored archive
/// would be incomparable with the current one.
/// **`Admissible` 与 `Feasible` 是两件事**。前者是"允许搜索的区域"，后者是"算作可行解"。允许在不可行域里探索不等于放宽解的质量标准；
/// 把两者合并会让显式探索不可行域的搜索把不可行解写进有效档案。
/// **`Admissible` and `Feasible` are different things.** The former is "the region we allow the search into", the latter is "counts as a
/// feasible solution". Allowing infeasible exploration must not relax solution quality; merging them would let such a search write
/// infeasible individuals into the valid archive.
/// **`ConstraintViolation` 不能当作可行性证明**。它只是有限非负的归一化违约度，用于引导搜索；零违约在数值容差下也不等于严格可行。
/// **`ConstraintViolation` is never a feasibility proof.** It is only a finite nonnegative normalized violation used to guide the
/// search; zero violation under a numerical tolerance still does not mean strict feasibility.
/// </remarks>
/// <typeparam name="ObjValue">目标类型 / The objective type.</typeparam>
/// <typeparam name="V">解值类型 / The solution value type.</typeparam>
public sealed class SearchProblem<ObjValue, V> where V : struct, IRealNumber<V>, INumberField<V> {
    /// <summary>
    /// 构造问题边界 / Construct the problem boundary.
    /// </summary>
    /// <param name="id">问题标识 / The problem identifier.</param>
    /// <param name="fingerprint">输入及目标定义指纹 / The input and objective fingerprint.</param>
    /// <param name="model">独占的回调模型 / The exclusively owned callback model.</param>
    /// <param name="objectiveCodec">目标编码 / The objective codec.</param>
    /// <param name="valueCodec">解值编码 / The solution-value codec.</param>
    /// <param name="dominates">严格优于或支配 / Strict improvement or dominance.</param>
    /// <param name="equivalent">目标等价 / Objective equivalence.</param>
    /// <param name="multiObjective">是否使用偏序 / Whether the ordering is partial.</param>
    /// <param name="valid">目标有效性 / Objective validity.</param>
    /// <param name="feasible">可行性策略 / The feasibility policy.</param>
    /// <param name="state">回调恢复状态 / The recoverable callback state.</param>
    /// <param name="encoding">解编码 / The solution encoding.</param>
    /// <param name="validSolution">编码与维度校验 / Encoding and dimension validation.</param>
    /// <param name="objectiveCoordinates">显式归一化最小化坐标 / Explicit normalized minimization coordinates.</param>
    /// <param name="admissible">可搜索域 / The search admission.</param>
    /// <param name="constraintViolation">归一化违约度 / The normalized violation.</param>
    public SearchProblem(
        string id,
        string fingerprint,
        IAbstractCallBackModelInterface<object?, ObjValue, V> model,
        ISearchCodec<ObjValue> objectiveCodec,
        ISearchCodec<V> valueCodec,
        Func<ObjValue, ObjValue, bool> dominates,
        Func<ObjValue, ObjValue, bool>? equivalent = null,
        bool multiObjective = false,
        Func<ObjValue, bool>? valid = null,
        Func<IReadOnlyList<V>, bool>? feasible = null,
        ISearchProblemState? state = null,
        SearchEncoding encoding = SearchEncoding.Continuous,
        Func<IReadOnlyList<V>, bool>? validSolution = null,
        Func<ObjValue, IReadOnlyList<Flt64>>? objectiveCoordinates = null,
        Func<IReadOnlyList<V>, bool>? admissible = null,
        Func<IReadOnlyList<V>, Result<Flt64, ErrorCode, Error<ErrorCode>>>? constraintViolation = null) {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(fingerprint);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(objectiveCodec);
        ArgumentNullException.ThrowIfNull(valueCodec);
        ArgumentNullException.ThrowIfNull(dominates);
        Id = id;
        Fingerprint = fingerprint;
        Model = model;
        ObjectiveCodec = objectiveCodec;
        ValueCodec = valueCodec;
        Dominates = dominates;
        Equivalent = equivalent ?? ((lhs, rhs) => EqualityComparer<ObjValue>.Default.Equals(lhs, rhs));
        MultiObjective = multiObjective;
        Valid = valid ?? (_ => true);
        Feasible = feasible ?? (solution => model.ConstraintSatisfied(solution) == true);
        State = state;
        Encoding = encoding;
        ValidSolution = validSolution
            ?? (solution => solution.Count == model.Tokens.TokensInSolver.Count);
        ObjectiveCoordinates = objectiveCoordinates;
        Admissible = admissible ?? Feasible;
        ConstraintViolation = constraintViolation;
    }

    /// <summary>问题标识 / The problem identifier.</summary>
    public string Id { get; }

    /// <summary>输入及目标定义指纹 / The input and objective fingerprint.</summary>
    public string Fingerprint { get; }

    /// <summary>独占的回调模型 / The exclusively owned callback model.</summary>
    public IAbstractCallBackModelInterface<object?, ObjValue, V> Model { get; }

    /// <summary>目标编码 / The objective codec.</summary>
    public ISearchCodec<ObjValue> ObjectiveCodec { get; }

    /// <summary>解值编码 / The solution-value codec.</summary>
    public ISearchCodec<V> ValueCodec { get; }

    /// <summary>严格优于或支配 / Strict improvement or dominance.</summary>
    public Func<ObjValue, ObjValue, bool> Dominates { get; }

    /// <summary>目标等价 / Objective equivalence.</summary>
    public Func<ObjValue, ObjValue, bool> Equivalent { get; }

    /// <summary>是否使用偏序 / Whether the ordering is partial.</summary>
    public bool MultiObjective { get; }

    /// <summary>目标有效性 / Objective validity.</summary>
    public Func<ObjValue, bool> Valid { get; }

    /// <summary>可行性策略 / The feasibility policy.</summary>
    public Func<IReadOnlyList<V>, bool> Feasible { get; }

    /// <summary>回调恢复状态 / The recoverable callback state.</summary>
    public ISearchProblemState? State { get; }

    /// <summary>解编码 / The solution encoding.</summary>
    public SearchEncoding Encoding { get; }

    /// <summary>编码与维度校验 / Encoding and dimension validation.</summary>
    public Func<IReadOnlyList<V>, bool> ValidSolution { get; }

    /// <summary>显式归一化最小化坐标 / Explicit normalized minimization coordinates.</summary>
    public Func<ObjValue, IReadOnlyList<Flt64>>? ObjectiveCoordinates { get; }

    /// <summary>可搜索域 / The search admission.</summary>
    public Func<IReadOnlyList<V>, bool> Admissible { get; }

    /// <summary>归一化违约度 / The normalized violation.</summary>
    public Func<IReadOnlyList<V>, Result<Flt64, ErrorCode, Error<ErrorCode>>>? ConstraintViolation { get; }
}

/// <summary>
/// 一次片段的执行报告 / The execution report for one slice.
/// </summary>
/// <typeparam name="ObjValue">目标类型 / The objective type.</typeparam>
/// <typeparam name="V">解值类型 / The solution value type.</typeparam>
/// <param name="Candidates">已评估的有效候选 / The valid evaluated candidates.</param>
/// <param name="Evaluations">已消耗评估请求 / The consumed evaluation requests.</param>
/// <param name="Steps">完整提交的低层步数 / The fully committed low-level steps.</param>
/// <param name="Termination">片段终止原因 / The slice termination.</param>
/// <param name="Diagnostics">步进失败诊断 / Step-failure diagnostics.</param>
public sealed record SearchStepResult<ObjValue, V>(
    IReadOnlyList<IIndividual<ObjValue, V>> Candidates,
    long Evaluations,
    long Steps,
    SearchTermination Termination,
    IReadOnlyList<string>? Diagnostics = null) where V : struct, IRealNumber<V>, INumberField<V> {
    /// <summary>步进失败诊断 / The step-failure diagnostics.</summary>
    public IReadOnlyList<string> Diagnostics { get; init; } = Diagnostics ?? Array.Empty<string>();
}

/// <summary>
/// 搜索运行结果，多目标不伪造唯一最优 / The search result, never fabricating a unique optimum for multiobjective search.
/// </summary>
/// <remarks>
/// **`Best` 在多目标下必须为 null**。从档案里随便挑第一个当"最优"会让单目标调用方拿到一个被其它解支配的个体，而结果里没有任何信号
/// 说明这一点。
/// **`Best` must be null under multiobjective search.** Picking the first archive entry as "the best" would hand a single-objective
/// caller an individual dominated by others, with nothing in the result indicating it.
/// </remarks>
/// <typeparam name="O">目标类型 / The objective type.</typeparam>
/// <typeparam name="V">解值类型 / The solution value type.</typeparam>
/// <param name="Archive">有效解档案 / The valid solution archive.</param>
/// <param name="MultiObjective">是否为多目标 / Whether the search was multiobjective.</param>
/// <param name="Termination">终止原因 / The termination reason.</param>
/// <param name="Evaluations">已用评估 / The consumed evaluations.</param>
/// <param name="Decisions">已完成高层决策 / The completed high-level decisions.</param>
/// <param name="Steps">完整低层步数 / The complete low-level steps.</param>
/// <param name="ActiveNanos">有效运行纳秒数 / The active runtime in nanoseconds.</param>
/// <param name="Diagnostics">运行诊断 / The runtime diagnostics.</param>
/// <param name="ObjectiveCalls">实际模型目标调用数 / The actual model objective invocations.</param>
public sealed record SearchResult<O, V>(
    IReadOnlyList<IIndividual<O, V>> Archive,
    bool MultiObjective,
    SearchTermination Termination,
    long Evaluations,
    long Decisions,
    long Steps,
    long ActiveNanos,
    IReadOnlyList<string>? Diagnostics = null,
    long ObjectiveCalls = 0) where V : struct, IRealNumber<V>, INumberField<V> {
    /// <summary>运行诊断 / The runtime diagnostics.</summary>
    public IReadOnlyList<string> Diagnostics { get; init; } = Diagnostics ?? Array.Empty<string>();

    /// <summary>单目标最优，多目标为 null / The scalar optimum, null for multiobjective search.</summary>
    public IIndividual<O, V>? Best => MultiObjective ? null : Archive.FirstOrDefault();
}

/// <summary>
/// 会话快照，不保存调用栈 / A session snapshot without execution stacks.
/// </summary>
/// <remarks>
/// **不保存调用栈**。把调用栈写进快照会让快照与某一版本的代码绑定；升级后恢复得到的是一个"能读出来但语义已变"的状态，比直接拒绝恢复
/// 更危险。
/// **No execution stacks are saved.** Writing the call stack into the snapshot binds it to one code version; after an upgrade the
/// restore yields a state that reads back fine but means something else — more dangerous than refusing to restore.
/// </remarks>
/// <param name="Initialized">是否已初始化 / Whether initialized.</param>
/// <param name="Iteration">局部步数 / The local iterations.</param>
/// <param name="Stagnation">无改进步数 / The stagnation count.</param>
/// <param name="State">算法原生状态 / The native algorithm state.</param>
/// <param name="Archive">局部档案 / The local archive.</param>
/// <param name="Version">协议版本 / The protocol version.</param>
/// <param name="ActiveNanos">有效运行纳秒数 / The active runtime in nanoseconds.</param>
/// <param name="Constraints">私有约束处理状态 / The private constraint-handling state.</param>
public sealed record SearchSessionSnapshot(
    bool Initialized,
    long Iteration,
    long Stagnation,
    JsonElement State,
    JsonElement Archive,
    int Version = 1,
    long ActiveNanos = 0,
    JsonElement? Constraints = null);

/// <summary>
/// 可分片执行的搜索会话 / A sliceable search session.
/// </summary>
/// <remarks>
/// **`Step` 只在步边界返回**。允许在任意点中断会让"恢复点"落在算子内部，而算子内部状态不一定可序列化——那类不可恢复点只会在真正需要
/// 恢复时才暴露。
/// **`Step` returns only at step boundaries.** Allowing interruption at arbitrary points would place recovery points inside operators,
/// whose internal state is not necessarily serializable — and such unrecoverable points only surface when a restore is actually
/// needed.
/// </remarks>
/// <typeparam name="ObjValue">目标类型 / The objective type.</typeparam>
/// <typeparam name="V">解值类型 / The solution value type.</typeparam>
public interface ISearchSession<ObjValue, V> where V : struct, IRealNumber<V>, INumberField<V> {
    /// <summary>是否能在步边界导入解 / Whether solutions can be imported at step boundaries.</summary>
    bool SupportsImport => false;

    /// <summary>
    /// 执行预算片段 / Execute a budget slice.
    /// </summary>
    /// <param name="budget">片段预算 / The slice budget.</param>
    /// <returns>执行报告 / The execution report.</returns>
    Task<Result<SearchStepResult<ObjValue, V>, ErrorCode, Error<ErrorCode>>> Step(SearchSlice budget);

    /// <summary>
    /// 在步进边界导入解 / Import solutions at a step boundary.
    /// </summary>
    /// <param name="solutions">待重新评估的解 / The solutions to re-evaluate.</param>
    /// <param name="budget">导入评估预算 / The import budget.</param>
    /// <returns>评估报告 / The evaluation report.</returns>
    Task<Result<SearchStepResult<ObjValue, V>, ErrorCode, Error<ErrorCode>>> ImportSolutions(
        IReadOnlyList<Solution<V>> solutions, SearchSlice budget);

    /// <summary>
    /// 获取完整状态 / Capture the complete state.
    /// </summary>
    /// <returns>会话快照 / The session snapshot.</returns>
    Result<SearchSessionSnapshot, ErrorCode, Error<ErrorCode>> Snapshot();

    /// <summary>
    /// 恢复完整状态 / Restore the complete state.
    /// </summary>
    /// <param name="snapshot">会话快照 / The session snapshot.</param>
    /// <returns>恢复结果 / The restore result.</returns>
    Try Restore(SearchSessionSnapshot snapshot);

    /// <summary>
    /// 关闭会话，模型所有权由运行器管理 / Close the session; the execution owns the model.
    /// </summary>
    /// <returns>关闭结果 / The close result.</returns>
    Try Close() => Results.Ok(Results.SuccessInstance);
}

/// <summary>
/// 创建独立会话 / Create an independent session.
/// </summary>
/// <typeparam name="ObjValue">目标类型 / The objective type.</typeparam>
/// <typeparam name="V">解值类型 / The solution value type.</typeparam>
/// <param name="problem">独立问题实例 / The independent problem instance.</param>
/// <param name="random">可恢复随机流 / The restorable random stream.</param>
/// <returns>新会话 / The new session.</returns>
public delegate Result<ISearchSession<ObjValue, V>, ErrorCode, Error<ErrorCode>>
    SearchSessionFactory<ObjValue, V>(SearchProblem<ObjValue, V> problem, SearchRandom random)
    where V : struct, IRealNumber<V>, INumberField<V>;

/// <summary>
/// 把外部回调或序列化异常转换为返回错误 / Convert callback or serialization exceptions into returned errors.
/// </summary>
/// <remarks>
/// **外部边界必须兜住异常**。模型回调、编解码器与用户策略都在边界之外；让它们的异常直接冒泡会越过整个搜索栈，调用方拿到的是未处理异常
/// 而不是可判断的失败结果，重放与诊断信息也一并丢失。
/// **External boundaries must catch exceptions.** Model callbacks, codecs and user policies all live outside the boundary; letting
/// their exceptions bubble bypasses the whole search stack, so the caller gets an unhandled exception instead of a judgeable failure,
/// and the replay and diagnostic information is lost along with it.
/// </remarks>
public static class SearchBoundary {
    /// <summary>
    /// 执行外部边界操作 / Execute an external-boundary operation.
    /// </summary>
    /// <typeparam name="T">结果类型 / The result type.</typeparam>
    /// <param name="block">外部边界操作 / The external-boundary operation.</param>
    /// <returns>操作结果 / The operation result.</returns>
    public static Result<T, ErrorCode, Error<ErrorCode>> Execute<T>(Func<T> block) {
        ArgumentNullException.ThrowIfNull(block);
        try {
            return Results.Ok(block());
        } catch (Exception exception) {
            return Results.Failed<T>(new Err<ErrorCode>(
                ErrorCode.ApplicationError,
                $"搜索边界失败 / Search boundary failed: {exception.Message}"));
        }
    }
}
