#nullable enable

using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Core.Analysis;

/// <summary>约束在多个目标条件下的分类 / Classification of a constraint across objective targets.</summary>
public enum CriticalityKind {
    /// <summary>单个目标中的局部瓶颈 / Local bottleneck in one target.</summary>
    LocalBottleneck,
    /// <summary>多个目标中重复出现的瓶颈 / Bottleneck repeated across targets.</summary>
    PersistentBottleneck,
    /// <summary>所有已证明目标中的结构瓶颈 / Structural bottleneck across all proven targets.</summary>
    StructuralBottleneck,
}

/// <summary>用于构建多目标 profile 的单个目标结果 / One target result used to build a multi-target profile.</summary>
/// <param name="Target">目标边界 / Objective target.</param>
/// <param name="Status">分析状态 / Analysis status.</param>
/// <param name="BlockingConstraintIds">阻塞约束身份 / Blocking constraint identities.</param>
public sealed record CriticalityObservation(
    ObjectiveTarget Target,
    AnalysisStatus Status,
    IReadOnlySet<ConstraintId> BlockingConstraintIds) {
    /// <summary>创建没有阻塞证据的观测 / Create an observation without blocking evidence.</summary>
    /// <param name="target">目标边界 / Objective target.</param>
    /// <param name="status">分析状态 / Analysis status.</param>
    public CriticalityObservation(ObjectiveTarget target, AnalysisStatus status)
        : this(target, status, FrozenSet<ConstraintId>.Empty) { }
}

/// <summary>稳定的多目标临界约束 profile / Stable multi-target criticality profile.</summary>
public sealed class CriticalityProfile {
    /// <summary>目标观测，按输入顺序保留 / Target observations retained in input order.</summary>
    public IReadOnlyList<CriticalityObservation> Observations { get; }
    /// <summary>约束分类 / Constraint classifications.</summary>
    public IReadOnlyDictionary<ConstraintId, IReadOnlySet<CriticalityKind>> Classifications { get; }
    /// <summary>profile schema 版本 / Profile schema version.</summary>
    public string SchemaVersion { get; }

    /// <summary>创建 profile / Create a profile.</summary>
    /// <param name="observations">目标观测 / Target observations.</param>
    /// <param name="classifications">约束分类；省略时从证据推导 / Constraint classifications, derived when omitted.</param>
    /// <param name="schemaVersion">schema 版本 / Schema version.</param>
    public CriticalityProfile(
        IReadOnlyList<CriticalityObservation> observations,
        IReadOnlyDictionary<ConstraintId, IReadOnlySet<CriticalityKind>>? classifications = null,
        string schemaVersion = "1.0") {
        Observations = Array.AsReadOnly(observations
            .Select(observation => observation with {
                BlockingConstraintIds = observation.BlockingConstraintIds.ToFrozenSet(),
            })
            .ToArray());
        Classifications = (classifications ?? Classify(Observations))
            .ToFrozenDictionary(
                pair => pair.Key,
                pair => (IReadOnlySet<CriticalityKind>)pair.Value.ToFrozenSet());
        SchemaVersion = schemaVersion;
    }

    /// <summary>已证明不可达的目标数量 / Number of proven unreachable targets.</summary>
    public int ProvenTargetCount => Observations.Count(observation =>
        observation.Status == AnalysisStatus.Unreachable);

    /// <summary>获取指定分类下的约束 / Get constraints classified under a kind.</summary>
    /// <param name="kind">分类 / Classification kind.</param>
    /// <returns>约束身份集合 / Constraint identities.</returns>
    public IReadOnlySet<ConstraintId> ConstraintsOf(CriticalityKind kind) =>
        Classifications
            .Where(pair => pair.Value.Contains(kind))
            .Select(pair => pair.Key)
            .ToFrozenSet();

    /// <summary>校验目标身份、重复目标及由证据推导的分类 / Validate target identity, duplicates, and derived classifications.</summary>
    /// <returns>结构化校验结果 / Structured validation result.</returns>
    public Try Validate() {
        if (string.IsNullOrWhiteSpace(SchemaVersion)) {
            return Failure("criticality profile schema 版本不得为空 / criticality profile schema must not be blank");
        }

        var targetIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (CriticalityObservation observation in Observations) {
            Try validation = observation.Target.Validate();
            if (!validation.IsOk) {
                return validation;
            }
            if (!targetIds.Add(observation.Target.StableId)) {
                return Failure("criticality profile 不得包含重复 target / criticality profile contains duplicate targets");
            }
            if (observation.BlockingConstraintIds.Any(id => string.IsNullOrWhiteSpace(id.Value))) {
                return Failure("criticality profile 约束身份不能为空 / criticality profile constraint ID must not be blank");
            }
        }
        if (Classifications.Keys.Any(id => string.IsNullOrWhiteSpace(id.Value))) {
            return Failure("criticality profile 约束身份不能为空 / criticality profile ID must not be blank");
        }

        IReadOnlyDictionary<ConstraintId, IReadOnlySet<CriticalityKind>> expected = Classify(Observations);
        bool same = Classifications.Count == expected.Count &&
            Classifications.All(pair =>
                expected.TryGetValue(pair.Key, out IReadOnlySet<CriticalityKind>? other) &&
                pair.Value.SetEquals(other));
        return same
            ? Results.Ok(Results.SuccessInstance)
            : Failure("criticality profile 分类与原始证据不一致 / criticality profile classifications do not match original evidence");
    }

    private static IReadOnlyDictionary<ConstraintId, IReadOnlySet<CriticalityKind>> Classify(
        IReadOnlyList<CriticalityObservation> observations) {
        IReadOnlyList<CriticalityObservation> proven = observations
            .Where(observation => observation.Status == AnalysisStatus.Unreachable)
            .ToList();
        var counts = proven
            .SelectMany(observation => observation.BlockingConstraintIds)
            .GroupBy(id => id)
            .ToDictionary(group => group.Key, group => group.Count());
        int total = proven.Count;

        return counts.Keys
            .OrderBy(id => id.Value, StringComparer.Ordinal)
            .ToDictionary(
                id => id,
                id => {
                    var kinds = new HashSet<CriticalityKind>();
                    int count = counts[id];
                    if (count == 1) {
                        kinds.Add(CriticalityKind.LocalBottleneck);
                    }
                    if (count > 1) {
                        kinds.Add(CriticalityKind.PersistentBottleneck);
                    }
                    if (total > 1 && count == total) {
                        kinds.Add(CriticalityKind.StructuralBottleneck);
                    }
                    return (IReadOnlySet<CriticalityKind>)kinds.ToFrozenSet();
                });
    }

    private static Try Failure(string message) =>
        Results.Failed<Success>(new Err<ErrorCode>(ErrorCode.IllegalArgument, message));
}

/// <summary>从目标观测构建确定性 profile / Build a deterministic profile from target observations.</summary>
public static class CriticalityProfileBuilder {
    /// <summary>构建 profile / Build a profile.</summary>
    /// <param name="observations">目标观测 / Target observations.</param>
    /// <returns>profile / Profile.</returns>
    public static CriticalityProfile Build(IReadOnlyList<CriticalityObservation> observations) =>
        new(observations);

    /// <summary>构建并校验 profile / Build and validate a profile.</summary>
    /// <param name="observations">目标观测 / Target observations.</param>
    /// <returns>结构化 profile / Structured profile.</returns>
    public static Result<CriticalityProfile, ErrorCode, Error<ErrorCode>> TryBuild(
        IReadOnlyList<CriticalityObservation> observations) {
        var profile = new CriticalityProfile(observations);
        Try validation = profile.Validate();
        return validation.IsOk
            ? Results.Ok(profile)
            : Results.Failed<CriticalityProfile>(
                validation is Failed<Success, ErrorCode, Error<ErrorCode>> failed
                    ? failed.Error
                    : new Err<ErrorCode>(ErrorCode.IllegalArgument, "criticality profile 校验失败 / Criticality profile validation failed"));
    }

    /// <summary>
    /// 校验一批目标 / Validate a batch of targets.
    /// </summary>
    /// <param name="targets">目标列表 / The target list.</param>
    /// <returns>空成功或结构化错误 / A unit success, or a structured error.</returns>
    /// <remarks>
    /// **空列表被拒，重复也被拒，但两者是不同的错误。** 空列表是"你没给我东西"，
    /// 重复是"你给了我两次同一个东西"——**混成一句话，调用方无法判断该补数据还是该去重。**
    ///
    /// **An empty list is refused and duplicates are refused, but they are different errors.** Empty means "you gave me nothing"; a duplicate means "you gave me the same thing twice" —
    /// **merged into one message, the caller cannot tell whether to add data or de-duplicate.**
    /// </remarks>
    public static Result<Success, ErrorCode, Error<ErrorCode>> ValidateTargetBatch(
        IReadOnlyList<ObjectiveTarget> targets) {
        ArgumentNullException.ThrowIfNull(targets);

        if (targets.Count == 0) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "至少需要一个 ObjectiveTarget / At least one ObjectiveTarget is required"));
        }

        string? duplicate = targets
            .GroupBy(target => target.StableId, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .FirstOrDefault();

        return duplicate is not null
            ? Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                $"ObjectiveTarget 不得重复：{duplicate} / Duplicate ObjectiveTarget: {duplicate}"))
            : Results.OkInstance;
    }
}
