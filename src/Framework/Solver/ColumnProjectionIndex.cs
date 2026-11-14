#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;

namespace Fuookami.Ospf.Framework.Solver;

internal sealed record ColumnProjectionTerm<V>(
    ColumnDecision Decision,
    V Coefficient)
    where V : struct, IRealNumber<V>, INumberField<V>;

/// <summary>单个已验证结构版本的稀疏投影索引 / Sparse projection index for one validated structural revision.</summary>
internal sealed class ColumnProjectionIndex<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    private sealed record CapturedTerm(
        int Position,
        ColumnDecision Decision,
        V Coefficient);

    private readonly IReadOnlyDictionary<VariableItemKey, (int Position, ColumnDecision Decision)> _positions;
    private readonly IReadOnlyList<CapturedTerm> _objectiveCapture;
    private readonly IReadOnlyList<(string RowId, IReadOnlyList<CapturedTerm> Terms)> _rowCapture;
    private readonly Lazy<IReadOnlyDictionary<string, IReadOnlyList<ColumnProjectionTerm<V>>>> _objectiveTerms;
    private readonly Lazy<
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<ColumnProjectionTerm<V>>>>> _rowTerms;

    public ColumnProjectionIndex(
        IReadOnlyList<ColumnDecision> decisions,
        IReadOnlyList<string> blockIds,
        LinearModelSnapshot<V> snapshot) {
        Revision = snapshot.Revision;
        _positions = decisions
            .Select((decision, position) => (decision, position))
            .ToDictionary(
                pair => pair.decision.Variable.Key,
                pair => (pair.position, pair.decision));

        DecisionsByBlock = blockIds.ToDictionary(
            blockId => blockId,
            blockId => (IReadOnlyList<ColumnDecision>)Array.AsReadOnly(
                decisions.Where(decision =>
                    string.Equals(decision.BlockId, blockId, StringComparison.Ordinal)).ToArray()),
            StringComparer.Ordinal);
        IdsByBlock = blockIds.ToDictionary(
            blockId => blockId,
            blockId => (IReadOnlySet<string>)new HashSet<string>(
                DecisionsByBlock[blockId].Select(decision => decision.Id),
                StringComparer.Ordinal),
            StringComparer.Ordinal);
        MappedVariables = new HashSet<IVariableItem>(decisions.Select(decision => decision.Variable));

        var objectiveCapture = new List<CapturedTerm>();
        foreach ((IVariableItem variable, V coefficient) in snapshot.Objective) {
            if (_positions.TryGetValue(variable.Key, out (int Position, ColumnDecision Decision) position)) {
                objectiveCapture.Add(new CapturedTerm(position.Position, position.Decision, coefficient));
            }
        }
        _objectiveCapture = Array.AsReadOnly(objectiveCapture.ToArray());

        var rowCapture = new List<(string RowId, IReadOnlyList<CapturedTerm> Terms)>();
        foreach (LinearModelRow<V> row in snapshot.Rows) {
            var terms = new List<CapturedTerm>();
            foreach ((IVariableItem variable, V coefficient) in row.Coefficients) {
                if (_positions.TryGetValue(variable.Key, out (int Position, ColumnDecision Decision) position)) {
                    terms.Add(new CapturedTerm(position.Position, position.Decision, coefficient));
                }
            }
            rowCapture.Add((row.Id, Array.AsReadOnly(terms.ToArray())));
        }
        _rowCapture = Array.AsReadOnly(rowCapture.ToArray());

        var local = blockIds.ToDictionary(
            blockId => blockId,
            _ => new List<LinearModelRow<V>>(),
            StringComparer.Ordinal);
        foreach (LinearModelRow<V> row in snapshot.Rows) {
            if (row.Coefficients.Count == 0) {
                foreach (List<LinearModelRow<V>> rows in local.Values) {
                    rows.Add(row);
                }
                continue;
            }

            string? owner = null;
            bool whollyLocal = true;
            foreach (IVariableItem variable in row.Coefficients.Keys) {
                if (!_positions.TryGetValue(variable.Key, out (int Position, ColumnDecision Decision) position) ||
                    (owner is not null &&
                        !string.Equals(owner, position.Decision.BlockId, StringComparison.Ordinal))) {
                    whollyLocal = false;
                    break;
                }
                owner = position.Decision.BlockId;
            }
            if (whollyLocal && owner is not null) {
                local[owner].Add(row);
            }
        }
        LocalRows = local.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<LinearModelRow<V>>)Array.AsReadOnly(pair.Value.ToArray()),
            StringComparer.Ordinal);

        _objectiveTerms = new Lazy<IReadOnlyDictionary<string, IReadOnlyList<ColumnProjectionTerm<V>>>>(
            () => GroupTerms(_objectiveCapture),
            isThreadSafe: true);
        _rowTerms = new Lazy<
            IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<ColumnProjectionTerm<V>>>>>(
            () => GroupRowTerms(_rowCapture),
            isThreadSafe: true);
    }

    public LinearModelRevision Revision { get; }
    public IReadOnlyDictionary<string, IReadOnlyList<ColumnDecision>> DecisionsByBlock { get; }
    public IReadOnlyDictionary<string, IReadOnlySet<string>> IdsByBlock { get; }
    public IReadOnlySet<IVariableItem> MappedVariables { get; }
    public IReadOnlyDictionary<string, IReadOnlyList<LinearModelRow<V>>> LocalRows { get; }
    public IReadOnlyDictionary<string, IReadOnlyList<ColumnProjectionTerm<V>>> ObjectiveTerms => _objectiveTerms.Value;
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<ColumnProjectionTerm<V>>>>
        RowTerms => _rowTerms.Value;

    /// <summary>已构建派生表的观测快照，不增加热路径计数 / Observe built derived tables without hot-path counters.</summary>
    internal IReadOnlySet<string> InitializedTermIndexes() {
        var initialized = new HashSet<string>(StringComparer.Ordinal);
        if (_objectiveTerms.IsValueCreated) {
            initialized.Add("objective");
        }
        if (_rowTerms.IsValueCreated) {
            initialized.Add("rows");
        }
        return initialized;
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<ColumnProjectionTerm<V>>> GroupTerms(
        IReadOnlyList<CapturedTerm> captured) =>
        captured
            .OrderBy(term => term.Position)
            .GroupBy(term => term.Decision.BlockId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<ColumnProjectionTerm<V>>)Array.AsReadOnly(
                    group.Select(term => new ColumnProjectionTerm<V>(
                        term.Decision,
                        term.Coefficient)).ToArray()),
                StringComparer.Ordinal);

    private static IReadOnlyDictionary<
        string,
        IReadOnlyDictionary<string, IReadOnlyList<ColumnProjectionTerm<V>>>> GroupRowTerms(
        IReadOnlyList<(string RowId, IReadOnlyList<CapturedTerm> Terms)> captured) =>
        captured.ToDictionary(
            pair => pair.RowId,
            pair => GroupTerms(pair.Terms),
            StringComparer.Ordinal);
}
