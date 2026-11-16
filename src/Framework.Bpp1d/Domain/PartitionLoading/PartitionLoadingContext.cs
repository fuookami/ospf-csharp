// 分区装载生命周期 / Partition-loading lifecycle.

using System.Collections.Generic;
using System.Threading.Tasks;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading;
using Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading.Model;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Framework.Solver;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using PositionConflictAlias = Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading.Model.PositionConflict;
using Try = Fuookami.Ospf.Utils.Functional.Result<
    Fuookami.Ospf.Utils.Functional.Success,
    Fuookami.Ospf.Utils.Error.ErrorCode,
    Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.Bpp.Domain.PartitionLoading;

/// <summary>显式分区，不从空间名称、几何或列表顺序推断 / An explicit partition, never inferred from names, geometry or order.</summary>
/// <param name="Id">分区标识 / Partition identity.</param>
/// <param name="PositionIds">唯一归属的逻辑舱位 / Uniquely owned logical positions.</param>
public sealed record LoadingPartition(string Id, IReadOnlySet<string> PositionIds);

/// <summary>下游局部决策的稳定绑定 / Stable binding of a downstream local decision.</summary>
/// <param name="Id">决策标识 / Decision identity.</param>
/// <param name="PartitionId">所属分区 / Owning partition.</param>
/// <param name="Variable">完整模型中的变量身份 / Variable identity in the full model.</param>
public sealed record PartitionDecision(string Id, string PartitionId, IVariableItem Variable);

/// <summary>一套局部完整装载配置 / A complete local loading configuration.</summary>
/// <typeparam name="V">领域数值 / Domain number.</typeparam>
/// <param name="Id">列标识 / Column identity.</param>
/// <param name="PartitionId">所属分区 / Owning partition.</param>
/// <param name="Decisions">局部决策的具体值 / Concrete local decision values.</param>
/// <param name="Assignments">货物到舱位 / Cargo to position.</param>
/// <param name="OccupiedPositions">整体占位 / Whole-position occupancy.</param>
/// <param name="CargoUsage">货物使用向量 / Cargo-use vector.</param>
/// <param name="Counts">各舱位件数 / Counts per position.</param>
/// <param name="Weights">各舱位实际货重 / Actual cargo weights per position.</param>
/// <param name="CompatibleLayouts">每个选择组的兼容布局 / Compatible layouts per selection group.</param>
public sealed record PartitionLoadingColumn<V>(
    string Id,
    string PartitionId,
    IReadOnlyDictionary<string, Flt64> Decisions,
    IReadOnlyDictionary<string, string> Assignments,
    IReadOnlySet<string> OccupiedPositions,
    IReadOnlyDictionary<string, ulong> CargoUsage,
    IReadOnlyDictionary<string, ulong> Counts,
    IReadOnlyDictionary<string, Quantity<V>> Weights,
    IReadOnlyDictionary<string, IReadOnlySet<string>> CompatibleLayouts)
    where V : struct, IFloatingNumber<V>;

/// <summary>
/// 分区列生命周期 / Partition-column lifecycle.
/// </summary>
/// <typeparam name="V">领域数值 / Domain number.</typeparam>
/// <remarks>
/// **装配、加列、定价与主问题注册都在这里**：`CreateAsync` / `RefreshAsync` / `RegisterAsync` / `AddColumnsAsync`，
/// 以及定价侧的 `PricingModelAsync` / `PricingRequestAsync` / `RefreshShadowPriceAsync` 与主问题侧的
/// `MasterAsync` / `ExtractSolutionAsync`。`price` / `applyPricing` 由 `PricingRequestAsync` 与
/// `ColumnMasterRegistration` 承接，**不是缺口**。
///
/// **加列时独立复核两件事**：局部变量的取值范围与整数性、以及**只涉及本分区变量**的完整模型行。
/// 这两类错误在局部模型里看不出来——局部模型本身就是按"这些约束"建的。
///
/// **Assembly, column addition, pricing and master registration all live here**: `CreateAsync` / `RefreshAsync` / `RegisterAsync` / `AddColumnsAsync`, plus the pricing side
/// (`PricingModelAsync` / `PricingRequestAsync` / `RefreshShadowPriceAsync`) and the master side (`MasterAsync` / `ExtractSolutionAsync`). **Adding a column independently
/// re-checks two things**: each local variable's range and integrality, and the full-model
/// rows that **touch only this partition's variables** — errors the local model cannot see, since it was built from those constraints in the first place.
/// </remarks>
public sealed class PartitionLoadingContext<V>
    where V : struct, IFloatingNumber<V> {
    private readonly Dictionary<string, PartitionLoadingColumn<V>> _pool = new();

    private PartitionLoadingContext(
        DiscreteLoadingContext<V> loading,
        LinearMetaModel<Flt64> source,
        IReadOnlyList<LoadingPartition> partitions,
        IReadOnlyList<PartitionDecision> decisions,
        ColumnProjection<Flt64> projection,
        LinearModelSnapshot<Flt64> structure) {
        Loading = loading;
        Source = source;
        Partitions = partitions;
        Decisions = decisions;
        Projection = projection;
        Structure = structure;
    }

    /// <summary>几何适配后的离散上下文 / The geometry-adapted discrete context.</summary>
    public DiscreteLoadingContext<V> Loading { get; }

    /// <summary>完整模型 / The full model.</summary>
    public LinearMetaModel<Flt64> Source { get; }

    /// <summary>显式分区 / The explicit partitions.</summary>
    public IReadOnlyList<LoadingPartition> Partitions { get; }

    /// <summary>局部决策归属 / Local decision ownership.</summary>
    public IReadOnlyList<PartitionDecision> Decisions { get; }

    /// <summary>投影器 / The projection.</summary>
    public ColumnProjection<Flt64> Projection { get; }

    /// <summary>当前结构快照 / The current structure snapshot.</summary>
    public LinearModelSnapshot<Flt64> Structure { get; private set; }

    /// <summary>已注册的列 / The registered columns.</summary>
    public IReadOnlyList<PartitionLoadingColumn<V>> Columns {
        get {
            var columns = new List<PartitionLoadingColumn<V>>();
            foreach (PartitionLoadingColumn<V> column in _pool.Values) {
                columns.Add(column);
            }
            return columns;
        }
    }

    /// <summary>可在已生成轮廓内精确补全的下游资源变量 / Downstream resources completable inside generated profiles.</summary>
    public IReadOnlyList<PartitionDecision> ResourceDecisions {
        get {
            var core = new HashSet<IVariableItem>();
            foreach (IVariableItem variable in Loading.Aggregation.Assignments.Values) {
                core.Add(variable);
            }
            foreach (IVariableItem variable in Loading.Aggregation.Occupied.Values) {
                core.Add(variable);
            }
            var result = new List<PartitionDecision>();
            foreach (PartitionDecision decision in Decisions) {
                if (!core.Contains(decision.Variable)) {
                    result.Add(decision);
                }
            }
            return result;
        }
    }

    /// <summary>
    /// 装配 / Assemble.
    /// </summary>
    /// <param name="loading">几何适配后的离散上下文 / Geometry-adapted discrete context.</param>
    /// <param name="source">完整模型 / The full model.</param>
    /// <param name="partitions">显式分区 / The explicit partitions.</param>
    /// <param name="extraDecisions">其他局部变量的显式归属 / Explicit ownership of other local variables.</param>
    /// <returns>生命周期上下文或错误 / The lifecycle context or an error.</returns>
    /// <remarks>
    /// **分区必须唯一且完整地覆盖舱位**（五条）：非空、标识与舱位非空、标识互不相同、
    /// 归属集合恰好等于全部舱位、**归属条数等于舱位数**（最后一条排掉"同一舱位被两个分区都列了"）。
    ///
    /// 少了第五条，一个舱位可以被两个分区同时"拥有"，而两条归属各自看起来都合法。
    ///
    /// **Partitions must uniquely and exhaustively own the positions** (five rules): non-empty, non-blank identities and position sets, distinct identities, ownership
    /// covering exactly every position, and **as many ownership entries as positions**. The last one rules out "the same position listed by two partitions", which would
    /// otherwise leave each ownership looking legitimate on its own.
    /// </remarks>
    public static async Task<Result<PartitionLoadingContext<V>, ErrorCode, Error<ErrorCode>>> CreateAsync(
        DiscreteLoadingContext<V> loading,
        LinearMetaModel<Flt64> source,
        IReadOnlyList<LoadingPartition> partitions,
        IReadOnlyList<PartitionDecision>? extraDecisions = null) {
        extraDecisions ??= new List<PartitionDecision>();

        var ids = new HashSet<string>();
        foreach (LoadingPosition<V> position in loading.Problem.Positions) {
            ids.Add(position.Id);
        }

        var memberships = new List<(string Position, string Partition)>();
        foreach (LoadingPartition partition in partitions) {
            foreach (string positionId in partition.PositionIds) {
                memberships.Add((positionId, partition.Id));
            }
        }

        if (!PartitionCoverageIsValid(partitions, memberships, ids)) {
            return Results.Failed<PartitionLoadingContext<V>>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "分区必须唯一且完整地覆盖舱位 / Partitions must uniquely and exhaustively own positions"));
        }

        var owners = new Dictionary<string, string>();
        foreach ((string position, string partition) in memberships) {
            owners[position] = partition;
        }

        var bindings = new List<PartitionDecision>();
        foreach (KeyValuePair<AssignmentKey, Core.Variable.BinVar> pair in loading.Aggregation.Assignments) {
            if (!owners.TryGetValue(pair.Key.PositionId, out string? owner)) {
                return Results.Failed<PartitionLoadingContext<V>>(new Err<ErrorCode>(
                    ErrorCode.DataNotFound, "分配引用了未归属的舱位 / Assignment references an unowned position"));
            }
            bindings.Add(new PartitionDecision(
                DecisionId("assignment", pair.Key.ItemId, pair.Key.PositionId), owner, pair.Value));
        }
        foreach (KeyValuePair<string, Core.Variable.BinVar> pair in loading.Aggregation.Occupied) {
            if (!owners.TryGetValue(pair.Key, out string? owner)) {
                return Results.Failed<PartitionLoadingContext<V>>(new Err<ErrorCode>(
                    ErrorCode.DataNotFound, "占位引用了未归属的舱位 / Occupancy references an unowned position"));
            }
            bindings.Add(new PartitionDecision(DecisionId("occupied", pair.Key), owner, pair.Value));
        }
        bindings.AddRange(extraDecisions);

        var partitionIds = new HashSet<string>();
        foreach (LoadingPartition partition in partitions) {
            partitionIds.Add(partition.Id);
        }
        var decisionIds = new HashSet<string>();
        var decisionVariables = new HashSet<IVariableItem>();
        foreach (PartitionDecision decision in bindings) {
            if (decision.Id.Length == 0
                || !partitionIds.Contains(decision.PartitionId)
                || !decisionIds.Add(decision.Id)
                || !decisionVariables.Add(decision.Variable)) {
                return Results.Failed<PartitionLoadingContext<V>>(new Err<ErrorCode>(
                    ErrorCode.IllegalArgument, "局部决策身份或归属重复 / Duplicate local decision identity or ownership"));
            }
        }

        var columnDecisions = new List<ColumnDecision>();
        foreach (PartitionDecision decision in bindings) {
            columnDecisions.Add(new ColumnDecision(decision.Id, decision.PartitionId, decision.Variable));
        }
        var projection = new ColumnProjection<Flt64>(source, columnDecisions, partitionIds);

        Result<LinearModelSnapshot<Flt64>, ErrorCode, Error<ErrorCode>> snapshot = await projection.SnapshotAsync();
        if (snapshot is not Ok<LinearModelSnapshot<Flt64>, ErrorCode, Error<ErrorCode>> snapshotOk) {
            return Propagate<PartitionLoadingContext<V>, LinearModelSnapshot<Flt64>>(snapshot);
        }

        return Results.Ok(new PartitionLoadingContext<V>(loading, source, partitions, bindings, projection, snapshotOk.Value));
    }

    /// <summary>
    /// 刷新模型基础层快照 / Refresh the model-owned snapshot.
    /// </summary>
    /// <returns>结果 / The result.</returns>
    public async Task<Result<Success, ErrorCode, Error<ErrorCode>>> RefreshAsync() {
        Result<LinearModelSnapshot<Flt64>, ErrorCode, Error<ErrorCode>> snapshot = await Projection.SnapshotAsync();
        if (snapshot is not Ok<LinearModelSnapshot<Flt64>, ErrorCode, Error<ErrorCode>> snapshotOk) {
            return Propagate<Success, LinearModelSnapshot<Flt64>>(snapshot);
        }
        Structure = snapshotOk.Value;
        return Results.OkInstance;
    }

    /// <summary>
    /// 注册种子列 / Register the seed columns.
    /// </summary>
    /// <param name="values">分区与局部向量 / Partitions and local vectors.</param>
    /// <returns>结果 / The result.</returns>
    public Task<Result<Success, ErrorCode, Error<ErrorCode>>> RegisterAsync(
        IReadOnlyList<(string PartitionId, IReadOnlyDictionary<string, Flt64> Values)> values) =>
        AddColumnsAsync(values);

    /// <summary>
    /// 增列，独立验证局部边界与所有局部硬约束 / Add columns after independently checking local bounds and every local hard row.
    /// </summary>
    /// <param name="values">分区与具体配置 / Partitions and concrete configurations.</param>
    /// <returns>结果 / The result.</returns>
    public async Task<Result<Success, ErrorCode, Error<ErrorCode>>> AddColumnsAsync(
        IReadOnlyList<(string PartitionId, IReadOnlyDictionary<string, Flt64> Values)> values) {
        Result<Success, ErrorCode, Error<ErrorCode>> refreshed = await RefreshAsync();
        if (refreshed is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
            return refreshed;
        }

        foreach ((string partitionId, IReadOnlyDictionary<string, Flt64> supplied) in values) {
            var local = new List<PartitionDecision>();
            foreach (PartitionDecision decision in Decisions) {
                if (decision.PartitionId == partitionId) {
                    local.Add(decision);
                }
            }
            if (local.Count == 0 || supplied.Count != local.Count) {
                return Invalid("列必须包含分区全部局部决策 / A column must contain every local decision");
            }
            foreach (PartitionDecision decision in local) {
                if (!supplied.ContainsKey(decision.Id)) {
                    return Invalid("列必须包含分区全部局部决策 / A column must contain every local decision");
                }
            }

            var vector = new Dictionary<string, Flt64>();
            foreach (PartitionDecision decision in local) {
                Flt64 value = supplied[decision.Id];
                if (!double.IsFinite(value.ToDouble()) || ViolatesBounds(decision.Variable, value)) {
                    return Invalid("局部列违反变量范围 / Local column violates variable bounds");
                }
                // **只归一化已验证的整数决策；连续资源不得用阈值抹掉正重量。**
                // **Only validated integer decisions are normalised; a continuous resource must never have its positive weight thresholded away.**
                Flt64 normalized = IsIntegerType(decision.Variable)
                    ? new Flt64(global::System.Math.Round(value.ToDouble(), global::System.MidpointRounding.AwayFromZero))
                    : value;
                vector[decision.Id] = normalized.ToDouble() == 0.0 ? Flt64.Zero : normalized;
            }

            var byVariable = new Dictionary<IVariableItem, Flt64>();
            foreach (PartitionDecision decision in local) {
                byVariable[decision.Variable] = vector[decision.Id];
            }
            foreach (LinearModelRow<Flt64> row in Structure.Rows) {
                bool isLocalRow = true;
                foreach (IVariableItem variable in row.Coefficients.Keys) {
                    if (!byVariable.ContainsKey(variable)) {
                        isLocalRow = false;
                        break;
                    }
                }
                if (!isLocalRow) {
                    continue;
                }
                Flt64 total = Flt64.Zero;
                foreach (KeyValuePair<IVariableItem, Flt64> term in row.Coefficients) {
                    total = total.Plus(term.Value.Times(byVariable[term.Key]));
                }
                bool valid = row.Comparison switch {
                    Comparison.LE => total.ToDouble() <= row.Rhs.ToDouble() + 1e-6,
                    Comparison.GE => total.ToDouble() >= row.Rhs.ToDouble() - 1e-6,
                    _ => global::System.Math.Abs(total.ToDouble() - row.Rhs.ToDouble()) <= 1e-6,
                };
                if (!valid) {
                    return Invalid("局部列违反完整模型的局部行 / Local column violates a local full-model row: " + row.Id);
                }
            }

            string id = ColumnId(partitionId, vector);
            if (_pool.ContainsKey(id)) {
                continue;
            }

            var assignmentKeys = new List<AssignmentKey>();
            foreach (KeyValuePair<AssignmentKey, Core.Variable.BinVar> pair in Loading.Aggregation.Assignments) {
                if (byVariable.TryGetValue(pair.Value, out Flt64 value) && value.ToDouble() > 0.5) {
                    assignmentKeys.Add(pair.Key);
                }
            }
            var itemIds = new HashSet<string>();
            foreach (AssignmentKey key in assignmentKeys) {
                if (!itemIds.Add(key.ItemId)) {
                    return Invalid("局部列重复使用货物 / A local column uses cargo more than once");
                }
            }

            var assignments = new Dictionary<string, string>();
            foreach (AssignmentKey key in assignmentKeys) {
                assignments[key.ItemId] = key.PositionId;
            }

            var occupied = new HashSet<string>();
            foreach (KeyValuePair<string, Core.Variable.BinVar> pair in Loading.Aggregation.Occupied) {
                if (byVariable.TryGetValue(pair.Value, out Flt64 value) && value.ToDouble() > 0.5) {
                    occupied.Add(pair.Key);
                }
            }
            foreach (PositionConflictAlias conflict in Loading.Problem.Conflicts) {
                if (occupied.Contains(conflict.First) && occupied.Contains(conflict.Second)) {
                    return Invalid("局部列存在内部占位冲突 / A local column has an internal occupancy conflict");
                }
            }
            foreach (LayoutSelectionGroup group in Loading.Problem.LayoutGroups) {
                var overlap = new HashSet<string>();
                foreach (string position in occupied) {
                    if (group.PositionIds.Contains(position)) {
                        overlap.Add(position);
                    }
                }
                bool compatible = false;
                foreach (LoadingLayout layout in group.Layouts) {
                    if (layout.PositionIds.IsSupersetOf(overlap)) {
                        compatible = true;
                        break;
                    }
                }
                if (!compatible) {
                    return Invalid("局部列没有兼容布局 / Local column has no compatible layout");
                }
            }

            var positions = new HashSet<string>();
            foreach (LoadingPartition partition in Partitions) {
                if (partition.Id == partitionId) {
                    foreach (string positionId in partition.PositionIds) {
                        positions.Add(positionId);
                    }
                    break;
                }
            }

            var weights = new Dictionary<string, Quantity<V>>();
            foreach (string position in positions) {
                double weight = 0.0;
                foreach (AssignmentKey key in assignmentKeys) {
                    if (!Loading.Aggregation.Weights.TryGetValue(position, out LinearPolynomial<Flt64>? positionWeight)) {
                        continue;
                    }
                    if (!Loading.Aggregation.Assignments.TryGetValue(key, out Core.Variable.BinVar? variable)) {
                        continue;
                    }
                    foreach (LinearMonomial<Flt64> term in positionWeight.Monomials) {
                        if (term.Symbol == variable) {
                            weight += term.Coefficient.ToDouble();
                        }
                    }
                }
                weights[position] = new Quantity<V>(Loading.Adapter.FromSolver(new Flt64(weight)), Loading.Problem.WeightUnit);
            }

            var cargoUsage = new Dictionary<string, ulong>();
            foreach (string itemId in assignments.Keys) {
                cargoUsage[itemId] = 1UL;
            }
            var counts = new Dictionary<string, ulong>();
            foreach (string position in positions) {
                ulong count = 0UL;
                foreach (string target in assignments.Values) {
                    if (target == position) {
                        count += 1UL;
                    }
                }
                counts[position] = count;
            }
            var compatibleLayouts = new Dictionary<string, IReadOnlySet<string>>();
            foreach (LayoutSelectionGroup group in Loading.Problem.LayoutGroups) {
                var overlap = new HashSet<string>();
                foreach (string position in occupied) {
                    if (group.PositionIds.Contains(position)) {
                        overlap.Add(position);
                    }
                }
                var layoutIds = new HashSet<string>();
                foreach (LoadingLayout layout in group.Layouts) {
                    if (layout.PositionIds.IsSupersetOf(overlap)) {
                        layoutIds.Add(layout.Id);
                    }
                }
                compatibleLayouts[group.Id] = layoutIds;
            }

            _pool[id] = new PartitionLoadingColumn<V>(
                Id: id,
                PartitionId: partitionId,
                Decisions: vector,
                Assignments: assignments,
                OccupiedPositions: occupied,
                CargoUsage: cargoUsage,
                Counts: counts,
                Weights: weights,
                CompatibleLayouts: compatibleLayouts);
        }

        return Results.OkInstance;
    }

    /// <summary>
    /// 验证定价请求在异步求解后仍对应当前完整模型 / Revalidate that a pricing request still matches the full model after asynchronous solving.
    /// </summary>
    /// <param name="revision">定价请求使用的版本 / The revision the request was built from.</param>
    /// <returns>结果 / The result.</returns>
    public Task<Result<Success, ErrorCode, Error<ErrorCode>>> RequireCurrentAsync(ColumnProjectionRevision revision) =>
        Projection.RequireCurrentAsync(revision);

    /// <summary>
    /// 验证整个定价批次仍对应当前完整模型 / Revalidate that the whole pricing batch still matches the full model.
    /// </summary>
    /// <param name="batch">待验证批次 / The batch to validate.</param>
    /// <returns>结果 / The result.</returns>
    public Task<Result<Success, ErrorCode, Error<ErrorCode>>> RequireCurrentAsync(ColumnPricingBatch<Flt64> batch) =>
        Projection.RequireCurrentAsync(batch);

    /// <summary>
    /// 构造包含所有局部硬行的定价模型 / Build a pricing model retaining every local hard row.
    /// </summary>
    /// <param name="request">定价请求 / The pricing request.</param>
    /// <returns>局部 MILP 或失败 / The local MILP or a failure.</returns>
    public async Task<Result<LinearMetaModel<Flt64>, ErrorCode, Error<ErrorCode>>> PricingModelAsync(
        ColumnPricingRequest<Flt64> request) {
        LoadingPartition? partition = null;
        foreach (LoadingPartition candidate in Partitions) {
            if (candidate.Id == request.BlockId) {
                partition = candidate;
                break;
            }
        }
        if (partition is null) {
            return Results.Failed<LinearMetaModel<Flt64>>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument, "未知定价分区 / Unknown pricing partition: " + request.BlockId));
        }

        Result<LinearMetaModel<Flt64>, ErrorCode, Error<ErrorCode>> built =
            await Projection.PricingModelAsync(request).ConfigureAwait(false);
        if (built is not Ok<LinearMetaModel<Flt64>, ErrorCode, Error<ErrorCode>> builtOk) {
            return Propagate<LinearMetaModel<Flt64>, LinearMetaModel<Flt64>>(built);
        }

        Result<Success, ErrorCode, Error<ErrorCode>> appended =
            AppendLocalPricingConstraints(builtOk.Value, partition);
        return appended is Ok<Success, ErrorCode, Error<ErrorCode>>
            ? built
            : Propagate<LinearMetaModel<Flt64>, Success>(appended);
    }

    /// <summary>
    /// 补上只涉及本分区的硬约束 / Append the hard constraints that only involve this partition.
    /// </summary>
    /// <param name="model">定价模型 / The pricing model.</param>
    /// <param name="partition">分区 / The partition.</param>
    /// <returns>结果 / The result.</returns>
    /// <remarks>
    /// **三类约束都要补，缺一类局部定价就会给出装不进完整模型的列**：每件货物至多一次、冲突团内至多一个占位、布局组恰好选一种并覆盖占位。
    ///
    /// **All three kinds must be appended, or local pricing returns columns that cannot live in the full model**: each item at most once, at most one occupancy per
    /// conflict clique, and one layout per group covering the occupied positions.
    /// </remarks>
    private Result<Success, ErrorCode, Error<ErrorCode>> AppendLocalPricingConstraints(
        LinearMetaModel<Flt64> model,
        LoadingPartition partition) {
        // 每件货物在本分区内至多分配一次。
        // Each item at most once inside this partition.
        foreach (LoadingItem<V> item in Loading.Problem.Items) {
            var terms = new List<LinearMonomial<Flt64>>();
            foreach (KeyValuePair<AssignmentKey, Core.Variable.BinVar> pair in Loading.Aggregation.Assignments) {
                if (pair.Key.ItemId == item.Id && partition.PositionIds.Contains(pair.Key.PositionId)) {
                    terms.Add(new LinearMonomial<Flt64>(Flt64.One, pair.Value));
                }
            }
            if (terms.Count == 0) {
                continue;
            }
            Try added = model.AddConstraint(
                new LinearInequality<Flt64>(
                    new LinearPolynomial<Flt64>(terms, Flt64.Zero),
                    new LinearPolynomial<Flt64>(new List<LinearMonomial<Flt64>>(), Flt64.One),
                    Comparison.LE),
                group: null,
                name: "local_unique_" + item.Id);
            if (!added.IsOk) {
                return Propagate<Success, Success>(added);
            }
        }

        // 冲突团内至多一个占位。
        // At most one occupancy per conflict clique.
        for (int index = 0; index < Loading.ConflictIndex.Cliques.Count; ++index) {
            var local = new HashSet<string>();
            foreach (string position in Loading.ConflictIndex.Cliques[index]) {
                if (partition.PositionIds.Contains(position)) {
                    local.Add(position);
                }
            }
            if (local.Count < 2) {
                continue;
            }
            var terms = new List<LinearMonomial<Flt64>>();
            foreach (string position in local) {
                if (Loading.Aggregation.Occupied.TryGetValue(position, out Core.Variable.BinVar? variable)) {
                    terms.Add(new LinearMonomial<Flt64>(Flt64.One, variable));
                }
            }
            Try added = model.AddConstraint(
                new LinearInequality<Flt64>(
                    new LinearPolynomial<Flt64>(terms, Flt64.Zero),
                    new LinearPolynomial<Flt64>(new List<LinearMonomial<Flt64>>(), Flt64.One),
                    Comparison.LE),
                group: null,
                name: "local_conflict_" + index.ToString(global::System.Globalization.CultureInfo.InvariantCulture));
            if (!added.IsOk) {
                return Propagate<Success, Success>(added);
            }
        }

        // 布局组：恰好选一种，并覆盖本分区的占位。
        // Layout groups: exactly one layout, covering this partition's occupied positions.
        for (int index = 0; index < Loading.Problem.LayoutGroups.Count; ++index) {
            LayoutSelectionGroup group = Loading.Problem.LayoutGroups[index];
            var local = new HashSet<string>();
            foreach (string position in group.PositionIds) {
                if (partition.PositionIds.Contains(position)) {
                    local.Add(position);
                }
            }
            if (local.Count == 0) {
                continue;
            }

            var layoutVariables = new List<Core.Variable.BinVar>();
            for (int layoutIndex = 0; layoutIndex < group.Layouts.Count; ++layoutIndex) {
                var variable = new Core.Variable.BinVar(
                    "local_layout_" + index.ToString(global::System.Globalization.CultureInfo.InvariantCulture) + "_"
                    + layoutIndex.ToString(global::System.Globalization.CultureInfo.InvariantCulture));
                Try addedVariable = model.Add(variable);
                if (!addedVariable.IsOk) {
                    return Propagate<Success, Success>(addedVariable);
                }
                layoutVariables.Add(variable);
            }

            var chooseTerms = new List<LinearMonomial<Flt64>>();
            foreach (Core.Variable.BinVar variable in layoutVariables) {
                chooseTerms.Add(new LinearMonomial<Flt64>(Flt64.One, variable));
            }
            Try choice = model.AddConstraint(
                new LinearInequality<Flt64>(
                    new LinearPolynomial<Flt64>(chooseTerms, Flt64.Zero),
                    new LinearPolynomial<Flt64>(new List<LinearMonomial<Flt64>>(), Flt64.One),
                    Comparison.EQ),
                group: null,
                name: "local_layout_choose_" + index.ToString(global::System.Globalization.CultureInfo.InvariantCulture));
            if (!choice.IsOk) {
                return Propagate<Success, Success>(choice);
            }

            foreach (string position in local) {
                var covering = new List<LinearMonomial<Flt64>>();
                for (int layoutIndex = 0; layoutIndex < group.Layouts.Count; ++layoutIndex) {
                    if (group.Layouts[layoutIndex].PositionIds.Contains(position)) {
                        covering.Add(new LinearMonomial<Flt64>(Flt64.One, layoutVariables[layoutIndex]));
                    }
                }
                if (!Loading.Aggregation.Occupied.TryGetValue(position, out Core.Variable.BinVar? occupied)) {
                    continue;
                }
                Try added = model.AddConstraint(
                    new LinearInequality<Flt64>(
                        new LinearPolynomial<Flt64>(
                            new List<LinearMonomial<Flt64>> { new(Flt64.One, occupied) }, Flt64.Zero),
                        new LinearPolynomial<Flt64>(covering, Flt64.Zero),
                        Comparison.LE),
                    group: null,
                    name: "local_layout_" + index.ToString(global::System.Globalization.CultureInfo.InvariantCulture) + "_" + position);
                if (!added.IsOk) {
                    return Propagate<Success, Success>(added);
                }
            }
        }

        return Results.OkInstance;
    }

    /// <summary>
    /// 一次准备多个分区的定价模型 / Prepare the pricing models of several partitions at once.
    /// </summary>
    /// <param name="partitionIds">分区编号 / The partition ids.</param>
    /// <param name="phaseOne">是否人工变量可行性阶段 / Whether this is the artificial-variable feasibility phase.</param>
    /// <returns>定价批次 / The pricing batch.</returns>
    /// <remarks>
    /// **批量准备与逐个准备的区别只在"什么时候建模型"**：一批里把所有分区的局部模型先建好，交给定价器连着一轮用完；
    /// 逐个准备则是"建一个、解一个、丢一个"。**两者的检验数必须一致**——不一致说明影子价格或局部模型在两次之间变了。
    ///
    /// **The difference between batch and one-by-one preparation is only *when* the models are built**: a batch builds every partition's local model up front and the pricer consumes
    /// them in one round, while the one-by-one path builds, solves and drops each. **The reduced costs must agree** — a disagreement means the shadow prices or a local model changed in
    /// between.
    /// </remarks>
    public Task<Result<ColumnPricingBatch<Flt64>, ErrorCode, Error<ErrorCode>>> PreparePricingBatchAsync(
        IEnumerable<string> partitionIds,
        bool phaseOne) =>
        Projection.PreparePricingBatchAsync(partitionIds, phaseOne);

    /// <summary>
    /// 重建受限 LP 或最终整数主问题 / Rebuild the restricted LP or the final integer master.
    /// </summary>
    /// <param name="phaseOne">是否使用无大 M 的可行性阶段 / Whether to run a big-M-free feasibility phase.</param>
    /// <param name="integer">是否最终整数列选择 / Whether to select integer columns.</param>
    /// <param name="refineResources">是否仅在已有轮廓内补全附加资源 / Whether to complete resources only inside existing profiles.</param>
    /// <returns>注册结果或失败 / The registration or a failure.</returns>
    public async Task<Result<ColumnMasterRegistration<Flt64>, ErrorCode, Error<ErrorCode>>> MasterAsync(
        bool phaseOne,
        bool integer,
        bool refineResources = false) {
        Result<Success, ErrorCode, Error<ErrorCode>> refreshed = await RefreshAsync().ConfigureAwait(false);
        if (refreshed is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
            return Propagate<ColumnMasterRegistration<Flt64>, Success>(refreshed);
        }

        var projected = new List<ProjectedColumn<Flt64>>();
        foreach (PartitionLoadingColumn<V> column in Columns) {
            projected.Add(new ProjectedColumn<Flt64>(column.Id, column.PartitionId, column.Decisions));
        }

        IReadOnlySet<IVariableItem> refined;
        if (refineResources) {
            var variables = new HashSet<IVariableItem>();
            foreach (PartitionDecision decision in ResourceDecisions) {
                variables.Add(decision.Variable);
            }
            refined = variables;
        }
        else {
            refined = new HashSet<IVariableItem>();
        }

        return await Projection.MasterAsync(projected, phaseOne, integer, refined, new Flt64(1e-6))
            .ConfigureAwait(false);
    }

    /// <summary>
    /// 按列与原变量身份合并完整模型解 / Merge columns and original global variables by identity.
    /// </summary>
    /// <param name="registration">当前主问题 / The current master.</param>
    /// <param name="values">主问题结果 / The master's values.</param>
    /// <returns>完整模型自身 token 顺序的结果 / Values in full-model token order.</returns>
    public Task<Result<IReadOnlyList<Flt64>, ErrorCode, Error<ErrorCode>>> ExtractSolutionAsync(
        ColumnMasterRegistration<Flt64> registration,
        IReadOnlyList<Flt64> values) =>
        Projection.ExtractSolutionAsync(registration, values, new Flt64(1e-6));

    /// <summary>
    /// 生成一次分区定价请求 / Build one partition pricing request.
    /// </summary>
    /// <param name="partitionId">分区 / The partition.</param>
    /// <param name="phaseOne">是否可行性阶段 / Whether this is the feasibility phase.</param>
    /// <param name="seed">是否种子阶段 / Whether this is the seed phase.</param>
    /// <returns>请求或失败 / The request or a failure.</returns>
    public Task<Result<ColumnPricingRequest<Flt64>, ErrorCode, Error<ErrorCode>>> PricingRequestAsync(
        string partitionId,
        bool phaseOne,
        bool seed = false) =>
        Projection.PricingRequestAsync(partitionId, phaseOne, seed);

    /// <summary>
    /// 把主问题 LP 的对偶价灌进投影 / Feed the master LP dual prices into the projection.
    /// </summary>
    /// <param name="master">主问题注册 / The master registration.</param>
    /// <param name="lp">LP 结果 / The LP result.</param>
    /// <returns>结果 / The result.</returns>
    /// <remarks>
    /// **`LpResult.DualSolution` 已经是 `MathConstraint` 键**，与 `RefreshShadowPriceAsync` 需要的形状完全一致——
    /// **没有"按行序贴价"这层映射**，所以贴错行在结构上不可能发生。
    ///
    /// **`LpResult.DualSolution` is already keyed by `MathConstraint`**, exactly the shape `RefreshShadowPriceAsync` needs — **there is no row-order mapping**, so
    /// attaching a price to the wrong row is structurally impossible (measured).
    /// </remarks>
    public Task<Result<Success, ErrorCode, Error<ErrorCode>>> RefreshShadowPriceAsync(
        ColumnMasterRegistration<Flt64> master,
        IColumnGenerationSolver.LpResult lp) =>
        Projection.RefreshShadowPriceAsync(master, lp.DualSolution);

    /// <summary>分区覆盖是否合法 / Whether partition coverage is legal.</summary>
    /// <param name="partitions">分区 / The partitions.</param>
    /// <param name="memberships">归属 / The memberships.</param>
    /// <param name="ids">全部舱位 / Every position.</param>
    /// <returns>判断结果 / The verdict.</returns>
    internal static bool PartitionCoverageIsValid(
        IReadOnlyList<LoadingPartition> partitions,
        IReadOnlyList<(string Position, string Partition)> memberships,
        IReadOnlySet<string> ids) {
        if (partitions.Count == 0) {
            return false;
        }
        var partitionIds = new HashSet<string>();
        foreach (LoadingPartition partition in partitions) {
            if (partition.Id.Length == 0 || partition.PositionIds.Count == 0 || !partitionIds.Add(partition.Id)) {
                return false;
            }
        }
        var covered = new HashSet<string>();
        foreach ((string position, string _) in memberships) {
            if (!covered.Add(position)) {
                // 同一舱位被两个分区都列了 / the same position is listed by two partitions.
                return false;
            }
        }
        return covered.SetEquals(ids);
    }

    /// <summary>决策标识 / The decision identity.</summary>
    /// <param name="prefix">前缀 / The prefix.</param>
    /// <param name="first">第一个身份 / The first identity.</param>
    /// <param name="second">第二个身份 / The second identity.</param>
    /// <returns>标识 / The identity.</returns>
    internal static string DecisionId(string prefix, string first, string second = "") =>
        $"{prefix}:{first.Length}:{first}:{second.Length}:{second}";

    /// <summary>列标识 / The column identity.</summary>
    /// <param name="partitionId">分区 / The partition.</param>
    /// <param name="vector">局部向量 / The local vector.</param>
    /// <returns>标识 / The identity.</returns>
    internal static string ColumnId(string partitionId, IReadOnlyDictionary<string, Flt64> vector) {
        var keys = new List<string>();
        foreach (string key in vector.Keys) {
            keys.Add(key);
        }
        keys.Sort(string.CompareOrdinal);
        var parts = new List<string>();
        foreach (string key in keys) {
            parts.Add($"{key.Length}:{key}={vector[key]}");
        }
        return $"{partitionId.Length}:{partitionId}|" + string.Join(';', parts);
    }

    /// <summary>是否整数类型变量 / Whether the variable is integer-typed.</summary>
    /// <param name="variable">变量 / The variable.</param>
    /// <returns>判断结果 / The verdict.</returns>
    internal static bool IsIntegerType(IVariableItem variable) =>
        variable is Core.Variable.AbstractVariableItem<UInt64, UInteger>
            or Core.Variable.AbstractVariableItem<Int64, Integer>
            or Core.Variable.AbstractVariableItem<UInt8, Binary>
            or Core.Variable.AbstractVariableItem<UInt8, Ternary>;

    /// <summary>是否违反变量范围 / Whether the value violates the variable's bounds.</summary>
    /// <param name="variable">变量 / The variable.</param>
    /// <param name="value">取值 / The value.</param>
    /// <returns>判断结果 / The verdict.</returns>
    internal static bool ViolatesBounds(IVariableItem variable, Flt64 value) {
        if (variable.LowerBound is { } lower && value.ToDouble() < lower.Value.ToFlt64().ToDouble() - 1e-6) {
            return true;
        }
        if (variable.UpperBound is { } upper && value.ToDouble() > upper.Value.ToFlt64().ToDouble() + 1e-6) {
            return true;
        }
        return IsIntegerType(variable)
            && global::System.Math.Abs(value.ToDouble() - global::System.Math.Round(value.ToDouble(),
                global::System.MidpointRounding.AwayFromZero)) > 1e-6;
    }

    /// <summary>构造非法参数失败 / Build an invalid-argument failure.</summary>
    /// <param name="reason">原因 / The reason.</param>
    /// <returns>失败 / The failure.</returns>
    private static Result<Success, ErrorCode, Error<ErrorCode>> Invalid(string reason) =>
        Results.Failed<Success>(new Err<ErrorCode>(ErrorCode.IllegalArgument, reason));

    /// <summary>把失败结果换成目标成功类型 / Re-shape a failed result into the target success type.</summary>
    /// <typeparam name="S">目标成功类型 / Target success type.</typeparam>
    /// <typeparam name="TSource">源成功类型 / Source success type.</typeparam>
    /// <param name="result">源结果 / Source result.</param>
    /// <returns>转换后的结果 / The re-shaped result.</returns>
    private static Result<S, ErrorCode, Error<ErrorCode>> Propagate<S, TSource>(
        Result<TSource, ErrorCode, Error<ErrorCode>> result)
        where S : notnull
        where TSource : notnull =>
        result switch {
            Failed<TSource, ErrorCode, Error<ErrorCode>> failed => Results.Failed<S>(failed.Error),
            Fatal<TSource, ErrorCode, Error<ErrorCode>> fatal =>
                new Fatal<S, ErrorCode, Error<ErrorCode>>(fatal.Errors),
            _ => Results.Failed<S>(new Err<ErrorCode>(ErrorCode.ApplicationError, "unreachable")),
        };
}
