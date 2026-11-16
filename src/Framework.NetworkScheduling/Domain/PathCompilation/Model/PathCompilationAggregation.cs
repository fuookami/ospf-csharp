// 路径编译聚合 / Path compilation aggregation.
// 与固定 Kotlin （24507 字节）对应。

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Framework.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.PathCompilation.Service;
using Fuookami.Ospf.Framework.NetworkScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<
    Fuookami.Ospf.Utils.Functional.Success,
    Fuookami.Ospf.Utils.Error.ErrorCode,
    Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Domain.PathCompilation.Model;

/// <summary>
/// 当前模型/修订/业务范围的对偶快照，不是最优性证书 / A dual snapshot bound to model, revision and scope — not an optimality certificate.
/// </summary>
/// <remarks>
/// **快照必须携带它所属的聚合、修订号与定价范围**。只带价格表会让过期的对偶被静默复用：分支变了、固定变了、模型变了，价格却还是旧的，
/// 定价据此算出的 reduced cost 看着合理，指向的却是已经不存在的列。
/// **The snapshot must carry its owning aggregation, revision and pricing scope.** A bare price table lets stale duals be reused in
/// silence: branches changed, fixings changed, model changed, but the prices are old, so the reduced costs look plausible while
/// pointing at columns that no longer exist.
/// </remarks>
public sealed class PathCompilationDuals {
    /// <summary>
    /// 构造对偶快照 / Construct the dual snapshot.
    /// </summary>
    /// <param name="owner">所属聚合 / The owning aggregation.</param>
    /// <param name="revision">修订号 / The revision number.</param>
    /// <param name="scope">定价范围版本 / The pricing-scope version.</param>
    /// <param name="prices">按规范行单位的有符号对偶 / Signed duals in normalized row units.</param>
    internal PathCompilationDuals(
        object owner,
        long revision,
        string scope,
        IReadOnlyDictionary<string, Flt64> prices) {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(prices);
        Owner = owner;
        Revision = revision;
        Scope = scope;
        Prices = prices.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    }

    /// <summary>所属聚合 / The owning aggregation.</summary>
    internal object Owner { get; }

    /// <summary>修订号 / The revision number.</summary>
    internal long Revision { get; }

    /// <summary>定价范围版本 / The pricing-scope version.</summary>
    public string Scope { get; }

    /// <summary>按规范行单位的有符号对偶 / Signed duals in normalized row units.</summary>
    public IReadOnlyDictionary<string, Flt64> Prices { get; }
}

/// <summary>
/// 通用路径主问题的独立注册态 / Independent master registration state for a generic path problem.
/// </summary>
/// <remarks>
/// **失败后禁止继续使用部分装配模型**。注册中途失败时，模型里已经落了一部分行与变量；继续用它求解会得到"看起来完整"的结果，而
/// 缺失的那几行永远不会有人检查。因此 `attempted` 一旦置位就拒绝一切后续操作，唯一出路是重建上下文。
/// **A partial assembly must not be reused after a failure.** When registration fails halfway, some rows and variables are already in
/// the model; solving it yields a result that looks complete, and the missing rows are never noticed. So once `attempted` is set,
/// every later call is refused and the only way forward is a fresh context.
/// **`Usable` 在装配期间临时为 false**，防止重入：注册本身会调用 `AddColumns`，而 `AddColumns` 又要求"已可用"。
/// **`Usable` is temporarily false during assembly** to prevent re-entry: registration itself calls `AddColumns`, which in turn
/// demands "already usable".
/// **对偶快照必须绑定模型签名**。`RefreshShadowPrices` 记下当时的模型签名，定价前再比对；模型在两次调用之间被外部改动时，价格
/// 会与现状不符，而 reduced cost 却照常算出合理数字。
/// **A dual snapshot must be bound to the model signature.** `RefreshShadowPrices` records the model signature at that moment and
/// pricing compares it again; if the model is changed externally in between, the prices no longer match reality while the reduced
/// costs still come out plausible.
/// </remarks>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <typeparam name="C">完整路径载荷类型 / Complete path payload type.</typeparam>
public sealed class PathCompilationAggregation<V, C> where V : struct, IRealNumber<V> {
    private const double IntegralityTolerance = 1e-6;
    private readonly Dictionary<string, CompiledPathColumn<V, C>> _columns = new(StringComparer.Ordinal);
    private readonly List<PathCompilationRows> _pipelines = new();
    private LinearMetaModel<Flt64>? _model;
    private bool _attempted;
    private bool _usable;
    private long _revision;
    private long _serial;
    private PathCompilationDuals? _currentDuals;
    private IReadOnlyList<object?> _dualModelSignature = Array.Empty<object?>();

    /// <summary>
    /// 构造路径编译聚合 / Construct the path compilation aggregation.
    /// </summary>
    /// <param name="problem">不可变问题 / The immutable problem.</param>
    /// <param name="adapter">solver 边界 / The solver boundary.</param>
    /// <param name="name">命名空间 / The namespace.</param>
    public PathCompilationAggregation(
        PathCompilationProblem<V, C> problem,
        INetworkSchedulingSolverValueAdapter<V> adapter,
        string name) {
        ArgumentNullException.ThrowIfNull(problem);
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentNullException.ThrowIfNull(name);
        Problem = problem;
        Adapter = adapter;
        Name = name;
        // 重复行标识会在这里抛 ，而调用方期待的是可处理的失败结果。先去重、再构造，让重复行在 
        // 里被当成校验错误拒绝。
        // Duplicate row identities would throw  here while the caller expects a handleable failure. De-duplicate
        // first, then construct, so duplicate rows are rejected as a validation error inside .
        var rowIds = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (PathCompilationRow<V> row in problem.Rows) {
            if (seen.Add(row.Id)) {
                rowIds.Add(row.Id);
            }
        }
        Compilation = new PathCompilation(rowIds, name);
    }

    /// <summary>不可变问题 / The immutable problem.</summary>
    public PathCompilationProblem<V, C> Problem { get; }

    /// <summary>solver 边界 / The solver boundary.</summary>
    public INetworkSchedulingSolverValueAdapter<V> Adapter { get; }

    /// <summary>命名空间 / The namespace.</summary>
    public string Name { get; }

    /// <summary>变量及行组件 / The variable and row component.</summary>
    public PathCompilation Compilation { get; }

    /// <summary>当前已注册的行管线 / The currently registered row pipelines.</summary>
    public IReadOnlyList<PathCompilationRows> Pipelines => _pipelines;

    /// <summary>当前已注册的列 / The currently registered columns.</summary>
    public IReadOnlyDictionary<string, CompiledPathColumn<V, C>> Columns => _columns;

    /// <summary>
    /// 在注册边界归一化完整列，费用与定价使用同一函数 / Normalize a complete column at the registration boundary.
    /// </summary>
    /// <param name="column">业务候选 / The business candidate.</param>
    /// <returns>规范化列 / The normalized column.</returns>
    public Result<CompiledPathColumn<V, C>, ErrorCode, Error<ErrorCode>> Compile(PathColumn<V, C> column) {
        ArgumentNullException.ThrowIfNull(column);
        if (string.IsNullOrWhiteSpace(column.Id)
            || column.Coefficients.Keys.Any(key => Problem.Rows.All(row => row.Id != key))) {
            return Failure<CompiledPathColumn<V, C>>("列标识或行引用无效 / Invalid column identity or row reference");
        }
        Result<Flt64, ErrorCode, Error<ErrorCode>> cost = Normalize(column.Cost, Problem.CostUnit);
        if (cost is Failed<Flt64, ErrorCode, Error<ErrorCode>> costFailure) {
            return Results.Failed<CompiledPathColumn<V, C>>(costFailure.Error);
        }
        var coefficients = new Dictionary<string, Flt64>(StringComparer.Ordinal);
        foreach (PathCompilationRow<V> row in Problem.Rows) {
            if (!column.Coefficients.TryGetValue(row.Id, out Quantity<V>? quantity)) {
                coefficients[row.Id] = Flt64.Zero;
                continue;
            }
            Result<Flt64, ErrorCode, Error<ErrorCode>> normalized = Normalize(quantity, row.Unit);
            if (normalized is Failed<Flt64, ErrorCode, Error<ErrorCode>> rowFailure) {
                return Results.Failed<CompiledPathColumn<V, C>>(rowFailure.Error);
            }
            coefficients[row.Id] = ((Ok<Flt64, ErrorCode, Error<ErrorCode>>)normalized).Value;
        }
        return Results.Ok(new CompiledPathColumn<V, C>(
            column,
            ((Ok<Flt64, ErrorCode, Error<ErrorCode>>)cost).Value,
            coefficients));
    }

    /// <summary>
    /// 注册变量、标准行管线与完整列目标 / Register variables, standard-row pipelines and full-column objectives.
    /// </summary>
    /// <param name="target">当前模型 / The current model.</param>
    /// <returns>注册结果 / The registration result.</returns>
    public Try Register(LinearMetaModel<Flt64> target) {
        ArgumentNullException.ThrowIfNull(target);
        if (_usable && ReferenceEquals(_model, target)) {
            return Results.Ok(Results.SuccessInstance);
        }
        if (_attempted) {
            return Failure("路径上下文不可跨模型或失败后复用 / Path context cannot be reused across models or after failure");
        }
        _attempted = true;
        if (target.ObjectCategory != ObjectCategory.Minimum) {
            return Failure("路径费用主问题必须最小化 / Path-cost master must minimise");
        }
        if (Problem.CostUnit.IsAffine || Problem.Rows.Any(row => row.Unit.IsAffine)) {
            return Failure("线性路径模型不支持仿射单位 / Linear path models do not support affine units");
        }
        if (Problem.Rows.Any(row => string.IsNullOrWhiteSpace(row.Id) || (row.Lower is null && row.Upper is null))
            || Problem.Rows.Select(row => row.Id).Distinct(StringComparer.Ordinal).Count() != Problem.Rows.Count) {
            return Failure("路径行重复、无标识或无边界 / Duplicate, unnamed or unbounded path rows");
        }
        foreach (PathCompilationRow<V> row in Problem.Rows) {
            Flt64? lower = null;
            Flt64? upper = null;
            if (row.Lower is { } lowerQuantity) {
                Result<Flt64, ErrorCode, Error<ErrorCode>> normalized = Normalize(lowerQuantity, row.Unit);
                if (normalized is Failed<Flt64, ErrorCode, Error<ErrorCode>> lowerFailure) {
                    return Results.Failed<Success>(lowerFailure.Error);
                }
                lower = ((Ok<Flt64, ErrorCode, Error<ErrorCode>>)normalized).Value;
            }
            if (row.Upper is { } upperQuantity) {
                Result<Flt64, ErrorCode, Error<ErrorCode>> normalized = Normalize(upperQuantity, row.Unit);
                if (normalized is Failed<Flt64, ErrorCode, Error<ErrorCode>> upperFailure) {
                    return Results.Failed<Success>(upperFailure.Error);
                }
                upper = ((Ok<Flt64, ErrorCode, Error<ErrorCode>>)normalized).Value;
            }
            if (lower is { } low && upper is { } high && low.CompareTo(high) > 0) {
                return Failure($"路径行单位或边界无效：{row.Id} / Invalid path row unit or bounds: {row.Id}");
            }
            _pipelines.Add(new PathCompilationRows(
                row.Id,
                Compilation.Rows[row.Id],
                lower,
                upper));
        }
        _model = target;
        foreach (LinearExpressionSymbol symbol in Compilation.Rows.Values) {
            Try added = target.Add(symbol);
            if (added is Failed<Success, ErrorCode, Error<ErrorCode>> symbolFailure) {
                return Results.Failed<Success>(symbolFailure.Error);
            }
        }
        _usable = true;
        Result<IReadOnlyList<PathColumn<V, C>>, ErrorCode, Error<ErrorCode>> initial =
            AddColumns(Problem.Columns, target);
        if (initial is Failed<IReadOnlyList<PathColumn<V, C>>, ErrorCode, Error<ErrorCode>> initialFailure) {
            return Results.Failed<Success>(initialFailure.Error);
        }
        _usable = false;
        foreach (PathCompilationRows pipeline in _pipelines) {
            Try registered = pipeline.Invoke(target);
            if (registered is Failed<Success, ErrorCode, Error<ErrorCode>> pipelineFailure) {
                return Results.Failed<Success>(pipelineFailure.Error);
            }
        }
        _usable = true;
        return Results.Ok(Results.SuccessInstance);
    }

    /// <summary>
    /// 增量添加完整列；同标识同规范系数去重，载荷或系数冲突失败 / Add complete columns incrementally.
    /// </summary>
    /// <param name="candidates">新候选 / The new candidates.</param>
    /// <param name="target">当前模型 / The current model.</param>
    /// <returns>实际新增的候选 / The candidates actually added.</returns>
    public Result<IReadOnlyList<PathColumn<V, C>>, ErrorCode, Error<ErrorCode>> AddColumns(
        IReadOnlyList<PathColumn<V, C>> candidates,
        LinearMetaModel<Flt64> target) {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(target);
        if (!_usable || !ReferenceEquals(_model, target)) {
            return Failure<IReadOnlyList<PathColumn<V, C>>>(
                "路径上下文未注册或已失效 / Path context is unregistered or invalid");
        }
        var additions = new Dictionary<string, CompiledPathColumn<V, C>>(StringComparer.Ordinal);
        foreach (PathColumn<V, C> candidate in candidates) {
            Result<CompiledPathColumn<V, C>, ErrorCode, Error<ErrorCode>> compiled = Compile(candidate);
            if (compiled is Failed<CompiledPathColumn<V, C>, ErrorCode, Error<ErrorCode>> compileFailure) {
                return Results.Failed<IReadOnlyList<PathColumn<V, C>>>(compileFailure.Error);
            }
            CompiledPathColumn<V, C> value =
                ((Ok<CompiledPathColumn<V, C>, ErrorCode, Error<ErrorCode>>)compiled).Value;
            CompiledPathColumn<V, C>? existing = _columns.TryGetValue(candidate.Id, out CompiledPathColumn<V, C>? known)
                ? known
                : additions.TryGetValue(candidate.Id, out CompiledPathColumn<V, C>? pending) ? pending : null;
            if (existing is not null
                && (!existing.Cost.Equals(value.Cost)
                    || !CoefficientsEqual(existing.Coefficients, value.Coefficients)
                    || !Equals(existing.Column.Payload, candidate.Payload))) {
                return Failure<IReadOnlyList<PathColumn<V, C>>>(
                    $"重复路径标识冲突：{candidate.Id} / Conflicting path identity: {candidate.Id}");
            }
            if (existing is null) {
                additions[candidate.Id] = value;
            }
        }
        if (additions.Count == 0) {
            return Results.Ok<IReadOnlyList<PathColumn<V, C>>>(Array.Empty<PathColumn<V, C>>());
        }
        _usable = false;
        _currentDuals = null;
        _revision += 1;
        foreach ((string id, CompiledPathColumn<V, C> compiled) in additions) {
            Core.Variable.UIntVariable1 variable = Compilation.Add(
                id, compiled.Coefficients, $"{Name}_column_{_serial++}");
            Try added = target.Add(variable);
            if (added is Failed<Success, ErrorCode, Error<ErrorCode>> variableFailure) {
                return Results.Failed<IReadOnlyList<PathColumn<V, C>>>(variableFailure.Error);
            }
            Try objective = target.AddObject(
                ObjectCategory.Minimum,
                new Math.Symbol.Polynomial.LinearPolynomial<Flt64>(
                    new[] {
                        new Math.Symbol.Monomial.LinearMonomial<Flt64>(
                            compiled.Cost, variable.First()),
                    },
                    Flt64.Zero),
                $"{variable.Name}_cost",
                null);
            if (objective is Failed<Success, ErrorCode, Error<ErrorCode>> objectiveFailure) {
                return Results.Failed<IReadOnlyList<PathColumn<V, C>>>(objectiveFailure.Error);
            }
            _columns[id] = compiled;
        }
        _usable = true;
        return Results.Ok<IReadOnlyList<PathColumn<V, C>>>(
            additions.Values.Select(value => value.Column).ToList());
    }

    /// <summary>
    /// 删除当前列并使所有旧对偶失效 / Remove current columns and invalidate every old dual.
    /// </summary>
    /// <param name="ids">待删除列标识 / The column IDs to remove.</param>
    /// <param name="target">当前模型 / The current model.</param>
    /// <returns>删除结果 / The removal result.</returns>
    public Try RemoveColumns(IReadOnlySet<string> ids, LinearMetaModel<Flt64> target) {
        ArgumentNullException.ThrowIfNull(ids);
        ArgumentNullException.ThrowIfNull(target);
        if (!_usable || !ReferenceEquals(_model, target)) {
            return Failure("路径上下文未注册或已失效 / Path context is unregistered or invalid");
        }
        var existing = ids.Where(_columns.ContainsKey).ToList();
        if (existing.Count == 0) {
            return Results.Ok(Results.SuccessInstance);
        }
        _usable = false;
        _currentDuals = null;
        _revision += 1;
        var variables = existing
            .SelectMany(id => Compilation.Variables[id].Cast<Core.Variable.IVariableItem>())
            .ToHashSet();
        Try removed = target.RemoveObjectiveTerms(variables);
        if (removed is Failed<Success, ErrorCode, Error<ErrorCode>> objectiveFailure) {
            return Results.Failed<Success>(objectiveFailure.Error);
        }
        foreach (string id in existing) {
            target.Remove(Compilation.Variables[id].First());
            Compilation.Remove(id);
            _columns.Remove(id);
        }
        _usable = true;
        return Results.Ok(Results.SuccessInstance);
    }

    /// <summary>
    /// 提取当前 LP 的完整行对偶 / Extract every current LP row dual.
    /// </summary>
    /// <param name="target">当前模型 / The current model.</param>
    /// <param name="duals">当前 LP 对偶 / The current LP dual solution.</param>
    /// <param name="scope">定价范围版本 / The pricing-scope version.</param>
    /// <returns>本轮快照，不构成全局证明 / The current snapshot, not a global proof.</returns>
    public Result<PathCompilationDuals, ErrorCode, Error<ErrorCode>> RefreshShadowPrices(
        LinearMetaModel<Flt64> target,
        MetaDualSolution duals,
        string scope) {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(duals);
        ArgumentNullException.ThrowIfNull(scope);
        _currentDuals = null;
        if (!_usable || !ReferenceEquals(_model, target) || string.IsNullOrWhiteSpace(scope)
            || !SameConstraintSet(target, _pipelines)) {
            return Failure<PathCompilationDuals>(
                "模型或定价范围不完整：额外行必须声明在问题中 / Incomplete model or pricing scope: declare extra rows in "
                + "the problem");
        }
        Try objective = ValidateObjective(target);
        if (objective is Failed<Success, ErrorCode, Error<ErrorCode>> objectiveFailure) {
            return Results.Failed<PathCompilationDuals>(objectiveFailure.Error);
        }
        var prices = new Dictionary<string, Flt64>(StringComparer.Ordinal);
        foreach (PathCompilationRows pipeline in _pipelines) {
            Flt64 price = Flt64.Zero;
            foreach (MathConstraint constraint in pipeline.Constraints) {
                if (!duals.Constraints.TryGetValue(constraint, out Flt64 value)) {
                    return Failure<PathCompilationDuals>(
                        $"缺少当前行对偶：{pipeline.Id} / Missing current row dual: {pipeline.Id}");
                }
                if (!IsFinite(value)) {
                    return Failure<PathCompilationDuals>("对偶必须有限 / Duals must be finite");
                }
                price = price.Plus(value);
                if (!IsFinite(price)) {
                    return Failure<PathCompilationDuals>("聚合对偶必须有限 / Aggregated duals must be finite");
                }
            }
            prices[pipeline.Id] = price;
        }
        var snapshot = new PathCompilationDuals(this, _revision, scope, prices);
        _currentDuals = snapshot;
        _dualModelSignature = ModelSignature();
        return Results.Ok(snapshot);
    }

    /// <summary>
    /// 以注册时同一完整列定义计算 reduced cost / Compute a reduced cost from the exact registered column definition.
    /// </summary>
    /// <param name="candidate">完整业务候选 / The complete business candidate.</param>
    /// <param name="duals">当前对偶快照 / The current dual snapshot.</param>
    /// <param name="scope">当前业务定价范围 / The current business pricing scope.</param>
    /// <returns>solver 边界的 reduced cost / The reduced cost at the solver boundary.</returns>
    public Result<Flt64, ErrorCode, Error<ErrorCode>> ReducedCost(
        PathColumn<V, C> candidate,
        PathCompilationDuals duals,
        string scope) {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(duals);
        ArgumentNullException.ThrowIfNull(scope);
        Try state = ValidatePricingState(duals, scope);
        if (state is Failed<Success, ErrorCode, Error<ErrorCode>> stateFailure) {
            return Results.Failed<Flt64>(stateFailure.Error);
        }
        return CalculateReducedCost(candidate, duals);
    }

    /// <summary>
    /// 在完整前后模型审计之间按输入顺序编译并计算一批候选 / Compile and price a batch in input order between complete audits.
    /// </summary>
    /// <remarks>
    /// **本方法不挂起**。每个候选仍独立调用列编译与数值适配器，不假设适配器无状态；若外部适配器在编译期间改动模型，后置审计会拒绝整批
    /// 结果。逐个异步定价应使用标量入口，不得把批次审计跨越异步边界复用。
    /// **This method does not suspend.** Each candidate still runs column compilation and the value adapter independently, without
    /// assuming the adapter is stateless; if an external adapter changes the model during compilation, the trailing audit rejects the
    /// whole batch. Asynchronous pricing should use the scalar entry, and the batch audit must not be reused across an async boundary.
    /// </remarks>
    /// <param name="candidates">按调用方优先级排列的完整候选 / The complete candidates in caller priority order.</param>
    /// <param name="duals">当前对偶快照 / The current dual snapshot.</param>
    /// <param name="scope">当前业务定价范围 / The current business pricing scope.</param>
    /// <returns>与输入顺序对应的 reduced costs / The reduced costs in input order.</returns>
    public Result<IReadOnlyList<Flt64>, ErrorCode, Error<ErrorCode>> ReducedCosts(
        IReadOnlyList<PathColumn<V, C>> candidates,
        PathCompilationDuals duals,
        string scope) {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(duals);
        ArgumentNullException.ThrowIfNull(scope);
        Try state = ValidatePricingState(duals, scope);
        if (state is Failed<Success, ErrorCode, Error<ErrorCode>> stateFailure) {
            return Results.Failed<IReadOnlyList<Flt64>>(stateFailure.Error);
        }
        var values = new List<Flt64>();
        ErrorCode? failureCode = null;
        string? failureMessage = null;
        foreach (PathColumn<V, C> candidate in candidates) {
            Result<Flt64, ErrorCode, Error<ErrorCode>> cost = CalculateReducedCost(candidate, duals);
            if (cost is Failed<Flt64, ErrorCode, Error<ErrorCode>> costFailure) {
                failureCode = costFailure.Error.Code;
                failureMessage = costFailure.Error.Message;
                break;
            }
            values.Add(((Ok<Flt64, ErrorCode, Error<ErrorCode>>)cost).Value);
        }
        Try trailing = ValidatePricingState(duals, scope);
        if (trailing is Failed<Success, ErrorCode, Error<ErrorCode>> trailingFailure) {
            return Results.Failed<IReadOnlyList<Flt64>>(trailingFailure.Error);
        }
        if (failureCode is { } code && failureMessage is { } message) {
            return Results.Failed<IReadOnlyList<Flt64>>(new Err<ErrorCode>(code, message));
        }
        return Results.Ok<IReadOnlyList<Flt64>>(values);
    }

    /// <summary>
    /// 提取并独立复核二元解，结果只对最终候选集合有效 / Extract and independently validate a binary solution.
    /// </summary>
    /// <param name="target">已求解模型 / The solved model.</param>
    /// <returns>选中的路径与完整用量 / The selected paths and complete usage.</returns>
    public Result<PathCompilationSolution<V, C>, ErrorCode, Error<ErrorCode>> ExtractSolution(
        LinearMetaModel<Flt64> target) {
        ArgumentNullException.ThrowIfNull(target);
        if (!_usable || !ReferenceEquals(_model, target)) {
            return Failure<PathCompilationSolution<V, C>>(
                "路径上下文未注册或已失效 / Path context is unregistered or invalid");
        }
        Try objective = ValidateObjective(target);
        if (objective is Failed<Success, ErrorCode, Error<ErrorCode>> objectiveFailure) {
            return Results.Failed<PathCompilationSolution<V, C>>(objectiveFailure.Error);
        }
        var selected = new List<PathColumn<V, C>>();
        var usage = Problem.Rows.ToDictionary(row => row.Id, _ => Flt64.Zero, StringComparer.Ordinal);
        Flt64 total = Flt64.Zero;
        foreach ((string id, CompiledPathColumn<V, C> compiled) in _columns) {
            Flt64? value = null;
            foreach (Token<Flt64> token in target.Tokens.Tokens) {
                if (!token.BelongsTo(Compilation.Variables[id])) {
                    continue;
                }
                value = token.Result;
                break;
            }
            if (value is not { } columnValue) {
                return Failure<PathCompilationSolution<V, C>>($"缺少路径列解值：{id} / Missing path-column value: {id}");
            }
            if (!IsFinite(columnValue)
                || (global::System.Math.Abs(columnValue.ToDouble()) > IntegralityTolerance
                    && global::System.Math.Abs((columnValue.Minus(Flt64.One)).ToDouble()) > IntegralityTolerance)) {
                return Failure<PathCompilationSolution<V, C>>("路径列解不是二元值 / Path-column solution is not binary");
            }
            if (columnValue.CompareTo(new Flt64(0.5)) > 0) {
                selected.Add(compiled.Column);
                total = total.Plus(compiled.Cost);
                foreach ((string row, Flt64 amount) in compiled.Coefficients) {
                    usage[row] = usage[row].Plus(amount);
                }
            }
        }
        if (!IsFinite(total) || usage.Values.Any(value => !IsFinite(value))) {
            return Failure<PathCompilationSolution<V, C>>(
                "路径解累计值必须有限 / Accumulated path-solution values must be finite");
        }
        foreach (PathCompilationRows pipeline in _pipelines) {
            Flt64 amount = usage[pipeline.Id];
            if ((pipeline.Lower is { } lower
                    && amount.CompareTo(lower.Minus(new Flt64(IntegralityTolerance))) < 0)
                || (pipeline.Upper is { } upper
                    && amount.CompareTo(upper.Plus(new Flt64(IntegralityTolerance))) > 0)) {
                return Failure<PathCompilationSolution<V, C>>(
                    $"路径解违反行：{pipeline.Id} / Path solution violates row: {pipeline.Id}");
            }
        }
        var amounts = new Dictionary<string, Quantity<V>>(StringComparer.Ordinal);
        foreach (PathCompilationRow<V> row in Problem.Rows) {
            Result<V, ErrorCode, Error<ErrorCode>> value = Adapter.FromSolverValue(usage[row.Id]);
            if (value is Failed<V, ErrorCode, Error<ErrorCode>> valueFailure) {
                return Results.Failed<PathCompilationSolution<V, C>>(valueFailure.Error);
            }
            amounts[row.Id] = new Quantity<V>(
                ((Ok<V, ErrorCode, Error<ErrorCode>>)value).Value, row.Unit);
        }
        Result<V, ErrorCode, Error<ErrorCode>> cost = Adapter.FromSolverValue(total);
        if (cost is Failed<V, ErrorCode, Error<ErrorCode>> costFailure) {
            return Results.Failed<PathCompilationSolution<V, C>>(costFailure.Error);
        }
        return Results.Ok(new PathCompilationSolution<V, C>(
            selected,
            new Quantity<V>(((Ok<V, ErrorCode, Error<ErrorCode>>)cost).Value, Problem.CostUnit),
            amounts,
            _columns.Keys.ToHashSet(StringComparer.Ordinal)));
    }

    /// <summary>
    /// 校验目标与完整列费用一致 / Validate that the objective agrees with the complete column costs.
    /// </summary>
    /// <param name="target">当前模型 / The current model.</param>
    /// <returns>校验结果 / The validation result.</returns>
    private Try ValidateObjective(LinearMetaModel<Flt64> target) {
        if (target.ObjectCategory != ObjectCategory.Minimum) {
            return Failure("路径主问题需要可审计的最小化目标 / Path master requires an auditable minimisation objective");
        }
        foreach ((string id, CompiledPathColumn<V, C> compiled) in _columns) {
            Core.Variable.UIntVariable1 variable = Compilation.Variables[id];
            Flt64 coefficient = Flt64.Zero;
            foreach (SubObject<Flt64> objective in target.SubObjects) {
                if (objective is not LinearSubObject<Flt64> linear
                    || linear.Category is not ObjectCategory.Minimum and not ObjectCategory.Maximum) {
                    continue;
                }
                Flt64 sign = linear.Category == ObjectCategory.Minimum ? Flt64.One : Flt64.One.Negate();
                foreach (Math.Symbol.Monomial.LinearMonomial<Flt64> term in linear.LinearTerms()) {
                    if (!term.Symbol.Name.StartsWith(variable.Name, StringComparison.Ordinal)) {
                        continue;
                    }
                    coefficient = coefficient.Plus(sign.Times(term.Coefficient));
                }
            }
            if (!IsFinite(coefficient) || !coefficient.Equals(compiled.Cost)) {
                return Failure(
                    $"路径目标与完整列费用不一致：{id} / Path objective differs from complete-column cost: {id}");
            }
        }
        return Results.Ok(Results.SuccessInstance);
    }

    /// <summary>
    /// 归一化物理量到目标单位 / Normalize a quantity to the target unit.
    /// </summary>
    /// <param name="quantity">物理量 / The quantity.</param>
    /// <param name="unit">目标单位 / The target unit.</param>
    /// <returns>规范化值 / The normalized value.</returns>
    private Result<Flt64, ErrorCode, Error<ErrorCode>> Normalize(Quantity<V> quantity, PhysicalUnit unit) {
        if (quantity.Unit.IsAffine || unit.IsAffine) {
            return Failure<Flt64>("线性路径模型不支持仿射单位 / Linear path models do not support affine units");
        }
        Result<Flt64, ErrorCode, Error<ErrorCode>> normalized = Adapter.Normalize(quantity, unit);
        if (normalized is Failed<Flt64, ErrorCode, Error<ErrorCode>> failure) {
            return Results.Failed<Flt64>(failure.Error);
        }
        Flt64 value = ((Ok<Flt64, ErrorCode, Error<ErrorCode>>)normalized).Value;
        if (!IsFinite(value)) {
            return Failure<Flt64>("规范化数值必须有限 / Normalized values must be finite");
        }
        return Results.Ok(value);
    }

    /// <summary>
    /// 校验定价状态是否仍然新鲜 / Validate that the pricing state is still fresh.
    /// </summary>
    /// <param name="duals">对偶快照 / The dual snapshot.</param>
    /// <param name="scope">定价范围 / The pricing scope.</param>
    /// <returns>校验结果 / The validation result.</returns>
    private Try ValidatePricingState(PathCompilationDuals duals, string scope) {
        if (!_usable || !ReferenceEquals(duals, _currentDuals) || !ReferenceEquals(duals.Owner, this)
            || duals.Revision != _revision || duals.Scope != scope
            || !SignatureEqual(_dualModelSignature, ModelSignature())) {
            return Failure("对偶已过期或定价范围不匹配 / Stale duals or mismatched pricing scope");
        }
        return _model is { } target
            ? ValidateObjective(target)
            : Failure("路径模型不存在 / Path model is absent");
    }

    /// <summary>
    /// 计算单个候选的 reduced cost / Compute one candidate's reduced cost.
    /// </summary>
    /// <param name="candidate">完整候选 / The complete candidate.</param>
    /// <param name="duals">对偶快照 / The dual snapshot.</param>
    /// <returns>reduced cost / The reduced cost.</returns>
    private Result<Flt64, ErrorCode, Error<ErrorCode>> CalculateReducedCost(
        PathColumn<V, C> candidate,
        PathCompilationDuals duals) {
        Result<CompiledPathColumn<V, C>, ErrorCode, Error<ErrorCode>> compiled = Compile(candidate);
        if (compiled is Failed<CompiledPathColumn<V, C>, ErrorCode, Error<ErrorCode>> failure) {
            return Results.Failed<Flt64>(failure.Error);
        }
        CompiledPathColumn<V, C> column =
            ((Ok<CompiledPathColumn<V, C>, ErrorCode, Error<ErrorCode>>)compiled).Value;
        Flt64 value = column.Cost;
        foreach ((string id, Flt64 coefficient) in column.Coefficients) {
            value = value.Minus(coefficient.Times(duals.Prices[id]));
            if (!IsFinite(value)) {
                return Failure<Flt64>("reduced cost 必须有限 / Reduced costs must be finite");
            }
        }
        return Results.Ok(value);
    }

    /// <summary>
    /// 快照当前模型的可比较签名 / Snapshot the model's comparable signature.
    /// </summary>
    /// <returns>模型签名 / The model signature.</returns>
    private IReadOnlyList<object?> ModelSignature() {
        if (_model is not { } target) {
            return Array.Empty<object?>();
        }
        var signature = new List<object?> { target.ObjectCategory };
        foreach (MathConstraint constraint in target.Constraints) {
            signature.Add(constraint.ToString());
        }
        foreach (SubObject<Flt64> sub in target.SubObjects) {
            signature.Add($"{sub.Category}|{sub.Name}");
        }
        foreach (Token<Flt64> token in target.Tokens.TokensInSolver) {
            signature.Add($"{token.Name}|{token.Variable.Name}");
        }
        return signature;
    }

    /// <summary>比较两个模型签名 / Compare two model signatures.</summary>
    /// <param name="left">左侧签名 / The left signature.</param>
    /// <param name="right">右侧签名 / The right signature.</param>
    /// <returns>是否相等 / Whether they are equal.</returns>
    private static bool SignatureEqual(IReadOnlyList<object?> left, IReadOnlyList<object?> right) {
        if (left.Count != right.Count) {
            return false;
        }
        for (int index = 0; index < left.Count; index++) {
            if (!Equals(left[index], right[index])) {
                return false;
            }
        }
        return true;
    }

    /// <summary>比较两个系数字典 / Compare two coefficient dictionaries.</summary>
    /// <param name="left">左侧 / The left dictionary.</param>
    /// <param name="right">右侧 / The right dictionary.</param>
    /// <returns>是否相等 / Whether they are equal.</returns>
    private static bool CoefficientsEqual(
        IReadOnlyDictionary<string, Flt64> left,
        IReadOnlyDictionary<string, Flt64> right) {
        if (left.Count != right.Count) {
            return false;
        }
        foreach ((string key, Flt64 value) in left) {
            if (!right.TryGetValue(key, out Flt64 other) || !value.Equals(other)) {
                return false;
            }
        }
        return true;
    }

    /// <summary>判断模型约束集与行管线一致 / Whether the model's constraints match the row pipelines.</summary>
    /// <param name="target">当前模型 / The current model.</param>
    /// <param name="pipelines">行管线 / The row pipelines.</param>
    /// <returns>是否一致 / Whether they match.</returns>
    private static bool SameConstraintSet(
        LinearMetaModel<Flt64> target,
        IReadOnlyList<PathCompilationRows> pipelines) {
        var expected = pipelines.SelectMany(pipeline => pipeline.Constraints).ToHashSet();
        IReadOnlyList<MathConstraint> actual = target.Constraints;
        return actual.Count == expected.Count && actual.All(expected.Contains);
    }

    /// <summary>判断数值是否有限 / Whether a value is finite.</summary>
    /// <param name="value">数值 / The value.</param>
    /// <returns>是否有限 / Whether it is finite.</returns>
    private static bool IsFinite(Flt64 value) => double.IsFinite(value.ToDouble());

    /// <summary>构造失败结果 / Build a failure result.</summary>
    /// <param name="message">错误信息 / The error message.</param>
    /// <returns>失败结果 / The failure result.</returns>
    private static Try Failure(string message) =>
        Results.Failed<Success>(new Err<ErrorCode>(ErrorCode.IllegalArgument, message));

    /// <summary>构造带类型的失败结果 / Build a typed failure result.</summary>
    /// <typeparam name="T">结果类型 / The result type.</typeparam>
    /// <param name="message">错误信息 / The error message.</param>
    /// <returns>失败结果 / The failure result.</returns>
    private static Result<T, ErrorCode, Error<ErrorCode>> Failure<T>(string message) =>
        Results.Failed<T>(new Err<ErrorCode>(ErrorCode.IllegalArgument, message));
}
