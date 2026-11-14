#nullable enable

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Framework.Solver;

/// <summary>
/// 定价完成状态；空列集合不表示穷尽搜索 / Pricing completion; an empty batch is not an exhaustion certificate.
/// </summary>
public enum ColumnGenerationCompletion {
    /// <summary>完整搜索已穷尽 / The complete scoped search was exhausted.</summary>
    Exhausted,

    /// <summary>搜索受预算限制 / The search stopped at a budget limit.</summary>
    BudgetLimit,

    /// <summary>搜索被中断 / The search was interrupted.</summary>
    Interrupted,

    /// <summary>结果来自启发式搜索 / The result came from a heuristic search.</summary>
    Heuristic,
}

/// <summary>
/// 定价证据作用域；必须精确绑定模型、对偶、分支和执行器时隙域。
/// Pricing evidence scope bound to a model, duals, branch and executor-slot domain.
/// </summary>
public sealed class ColumnGenerationScope : IEquatable<ColumnGenerationScope> {
    /// <summary>创建定价证据作用域 / Create a pricing evidence scope.</summary>
    /// <param name="request">单次定价请求唯一标识 / Unique identity for one pricing request.</param>
    /// <param name="group">请求覆盖的执行器或执行器分组 / Executors or executor groups covered by the request.</param>
    /// <param name="branch">当前分支状态标识 / Current branch-state identity.</param>
    /// <param name="global">是否穷尽该请求作用域内的完整业务域 / Whether the full business domain within this request scope is covered.</param>
    /// <param name="model">主模型标识 / Master model identity.</param>
    /// <param name="dual">产生当前影子价格的对偶版本 / Dual revision used to produce the current shadow prices.</param>
    /// <param name="executorSlots">执行器与时隙组合标识 / Executor-slot combinations covered by the request.</param>
    public ColumnGenerationScope(
        string request,
        string group,
        string branch,
        bool global = false,
        string model = "",
        string dual = "",
        IEnumerable<string>? executorSlots = null) {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(branch);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(dual);
        Request = request;
        Group = group;
        Branch = branch;
        Global = global;
        Model = model;
        Dual = dual;
        ExecutorSlots = (executorSlots ?? Array.Empty<string>())
            .ToFrozenSet(StringComparer.Ordinal);
    }

    /// <summary>单次定价请求唯一标识 / Unique identity for one pricing request.</summary>
    public string Request { get; }

    /// <summary>请求覆盖的执行器或执行器分组 / Executors or executor groups covered by the request.</summary>
    public string Group { get; }

    /// <summary>当前分支状态标识 / Current branch-state identity.</summary>
    public string Branch { get; }

    /// <summary>是否穷尽该请求作用域内的完整业务域 / Whether the full business domain within this request scope is covered.</summary>
    public bool Global { get; }

    /// <summary>主模型标识 / Master model identity.</summary>
    public string Model { get; }

    /// <summary>产生当前影子价格的对偶版本 / Dual revision used to produce the current shadow prices.</summary>
    public string Dual { get; }

    /// <summary>执行器与时隙组合标识 / Executor-slot combinations covered by the request.</summary>
    public IReadOnlySet<string> ExecutorSlots { get; }

    /// <summary>按值比较完整作用域 / Compare the complete scope by value.</summary>
    /// <param name="other">待比较作用域 / Scope to compare.</param>
    /// <returns>作用域是否完全相同 / Whether the scopes are identical.</returns>
    public bool Equals(ColumnGenerationScope? other) {
        return other is not null &&
            string.Equals(Request, other.Request, StringComparison.Ordinal) &&
            string.Equals(Group, other.Group, StringComparison.Ordinal) &&
            string.Equals(Branch, other.Branch, StringComparison.Ordinal) &&
            Global == other.Global &&
            string.Equals(Model, other.Model, StringComparison.Ordinal) &&
            string.Equals(Dual, other.Dual, StringComparison.Ordinal) &&
            ExecutorSlots.SetEquals(other.ExecutorSlots);
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as ColumnGenerationScope);

    /// <inheritdoc/>
    public override int GetHashCode() {
        var hash = new HashCode();
        hash.Add(Request, StringComparer.Ordinal);
        hash.Add(Group, StringComparer.Ordinal);
        hash.Add(Branch, StringComparer.Ordinal);
        hash.Add(Global);
        hash.Add(Model, StringComparer.Ordinal);
        hash.Add(Dual, StringComparer.Ordinal);
        int slotHash = 0;
        foreach (string slot in ExecutorSlots) {
            slotHash ^= StringComparer.Ordinal.GetHashCode(slot);
        }
        hash.Add(slotHash);
        return hash.ToHashCode();
    }
}

/// <summary>
/// 单次列生成结果；只承诺精确作用域内的完成度，不承诺全局整数最优。
/// Per-request result; completion is scoped and is not an integer-optimality claim.
/// </summary>
/// <typeparam name="C">候选列类型 / Candidate-column type.</typeparam>
public sealed class ColumnGenerationBatch<C> {
    /// <summary>创建列生成批次 / Create a column-generation batch.</summary>
    /// <param name="columns">候选列 / Candidate columns.</param>
    /// <param name="completion">完成状态 / Completion.</param>
    /// <param name="scope">证据作用域，无证据时为空 / Evidence scope, or null without evidence.</param>
    /// <param name="noImprovingColumns">独立的无改进列证明；未提供时为空 / Independent no-improving-column proof, or null when unavailable.</param>
    /// <param name="children">分执行器或时隙的子请求结果 / Child request results split by executor or time slot.</param>
    public ColumnGenerationBatch(
        IEnumerable<C> columns,
        ColumnGenerationCompletion completion = ColumnGenerationCompletion.Heuristic,
        ColumnGenerationScope? scope = null,
        bool? noImprovingColumns = null,
        IEnumerable<ColumnGenerationBatch<C>>? children = null) {
        ArgumentNullException.ThrowIfNull(columns);
        Columns = Array.AsReadOnly(columns.ToArray());
        Completion = completion;
        Scope = scope;
        NoImprovingColumns = noImprovingColumns;
        Children = Array.AsReadOnly((children ?? Array.Empty<ColumnGenerationBatch<C>>()).ToArray());
    }

    /// <summary>候选列 / Candidate columns.</summary>
    public IReadOnlyList<C> Columns { get; }

    /// <summary>完成状态 / Completion.</summary>
    public ColumnGenerationCompletion Completion { get; }

    /// <summary>证据作用域，无证据时为空 / Evidence scope, or null without evidence.</summary>
    public ColumnGenerationScope? Scope { get; }

    /// <summary>独立的无改进列证明；未提供时为空 / Independent no-improving-column proof, or null when unavailable.</summary>
    public bool? NoImprovingColumns { get; }

    /// <summary>分执行器或时隙的子请求结果 / Child request results split by executor or time slot.</summary>
    public IReadOnlyList<ColumnGenerationBatch<C>> Children { get; }

    /// <summary>
    /// 验证穷尽证据严格属于指定的完整请求域。
    /// Check exhaustion evidence belongs to the exact complete request domain.
    /// </summary>
    /// <param name="expected">预期作用域 / Expected scope.</param>
    /// <returns>是否具有完整有效证据 / Whether evidence is complete and applicable.</returns>
    public bool Certifies(ColumnGenerationScope expected) {
        ArgumentNullException.ThrowIfNull(expected);
        return Completion == ColumnGenerationCompletion.Exhausted &&
            expected.Global &&
            !string.IsNullOrWhiteSpace(expected.Request) &&
            !string.IsNullOrWhiteSpace(expected.Model) &&
            !string.IsNullOrWhiteSpace(expected.Dual) &&
            !string.IsNullOrWhiteSpace(expected.Branch) &&
            Scope is not null &&
            Scope.Equals(expected) &&
            HasConsistentChildren(expected);
    }

    /// <summary>
    /// 验证所有子结果完整覆盖父请求，且绑定同一模型、对偶和分支状态。
    /// Check child results cover the parent request and share its model, duals and branch state.
    /// </summary>
    /// <param name="expected">预期父作用域 / Expected parent scope.</param>
    /// <returns>子结果是否完整且一致 / Whether child results are complete and consistent.</returns>
    public bool HasConsistentChildren(ColumnGenerationScope expected) {
        ArgumentNullException.ThrowIfNull(expected);
        if (Children.Count == 0) {
            return true;
        }

        var childScopes = new List<ColumnGenerationScope>(Children.Count);
        foreach (ColumnGenerationBatch<C> child in Children) {
            if (child.Scope is not { } childScope) {
                return false;
            }
            if (!string.Equals(childScope.Request, expected.Request, StringComparison.Ordinal) &&
                !childScope.Request.StartsWith($"{expected.Request}:", StringComparison.Ordinal)) {
                return false;
            }
            if (!string.Equals(childScope.Model, expected.Model, StringComparison.Ordinal) ||
                !string.Equals(childScope.Dual, expected.Dual, StringComparison.Ordinal) ||
                !string.Equals(childScope.Branch, expected.Branch, StringComparison.Ordinal)) {
                return false;
            }
            childScopes.Add(childScope);
        }

        HashSet<string> requestedGroups = Groups(expected.Group);
        var childGroups = childScopes
            .SelectMany(childScope => Groups(childScope.Group))
            .ToHashSet(StringComparer.Ordinal);
        var childSlots = childScopes
            .SelectMany(childScope => childScope.ExecutorSlots)
            .ToHashSet(StringComparer.Ordinal);
        if (!childGroups.SetEquals(requestedGroups) ||
            !childSlots.SetEquals(expected.ExecutorSlots)) {
            return false;
        }

        for (int index = 0; index < Children.Count; ++index) {
            if (!Children[index].Certifies(childScopes[index])) {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// 验证当前对偶和请求域内不存在改进列，必须有独立证明。
    /// Check that no improving column exists for this dual and request scope using explicit evidence.
    /// </summary>
    /// <param name="expected">预期作用域 / Expected scope.</param>
    /// <returns>是否有完整的无改进列证明 / Whether an exhaustive no-improving-column certificate is available.</returns>
    public bool CertifiesNoImprovingColumns(ColumnGenerationScope expected) {
        ArgumentNullException.ThrowIfNull(expected);
        if (NoImprovingColumns != true || !Certifies(expected)) {
            return false;
        }
        foreach (ColumnGenerationBatch<C> child in Children) {
            if (child.Scope is not { } childScope ||
                !child.CertifiesNoImprovingColumns(childScope)) {
                return false;
            }
        }
        return true;
    }

    private static HashSet<string> Groups(string value) =>
        value
            .Split('|', StringSplitOptions.None)
            .Where(group => group.Length != 0)
            .ToHashSet(StringComparer.Ordinal);
}
