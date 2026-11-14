#nullable enable

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.Solver;

/// <summary>局部决策到配置块的显式映射 / Explicit local-decision ownership without packing semantics.</summary>
public sealed record ColumnDecision(
    string Id,
    string BlockId,
    IVariableItem Variable);

/// <summary>配置列的不可变数值投影 / Immutable numeric projection of a configuration column.</summary>
public sealed class ProjectedColumn<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    private readonly IReadOnlyDictionary<string, V> _values;

    public ProjectedColumn(
        string id,
        string blockId,
        IReadOnlyDictionary<string, V> values) {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        BlockId = blockId ?? throw new ArgumentNullException(nameof(blockId));
        if (values is null) {
            throw new ArgumentNullException(nameof(values));
        }
        _values = new ReadOnlyDictionary<string, V>(
            new Dictionary<string, V>(values, StringComparer.Ordinal));
    }

    public string Id { get; }
    public string BlockId { get; }
    public IReadOnlyDictionary<string, V> Values => _values;
}

/// <summary>原始模型及列投影版本 / Source-model and column-projection revision.</summary>
public readonly record struct ColumnProjectionRevision(
    LinearModelRevision Source,
    long Owner,
    long Sequence);

/// <summary>带完整同源系数和版本的定价请求 / Versioned pricing request with coefficients from one source revision.</summary>
public sealed class ColumnPricingRequest<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    private readonly IReadOnlyDictionary<string, V> _coefficients;
    private readonly IReadOnlyDictionary<string, V> _rowPrices;

    public ColumnPricingRequest(
        string blockId,
        IReadOnlyDictionary<string, V> coefficients,
        V convexityPrice,
        IReadOnlyDictionary<string, V> rowPrices,
        ColumnProjectionRevision revision) {
        BlockId = blockId ?? throw new ArgumentNullException(nameof(blockId));
        if (coefficients is null) {
            throw new ArgumentNullException(nameof(coefficients));
        }
        if (rowPrices is null) {
            throw new ArgumentNullException(nameof(rowPrices));
        }
        _coefficients = new ReadOnlyDictionary<string, V>(
            new Dictionary<string, V>(coefficients, StringComparer.Ordinal));
        _rowPrices = new ReadOnlyDictionary<string, V>(
            new Dictionary<string, V>(rowPrices, StringComparer.Ordinal));
        ConvexityPrice = convexityPrice;
        Revision = revision;
    }

    public string BlockId { get; }
    public IReadOnlyDictionary<string, V> Coefficients => _coefficients;
    public V ConvexityPrice { get; }
    public IReadOnlyDictionary<string, V> RowPrices => _rowPrices;
    public ColumnProjectionRevision Revision { get; }

    /// <summary>计算完整检验数，不将缺失值当作零 / Evaluate reduced cost without treating missing values as zero.</summary>
    public Result<V, ErrorCode, Error<ErrorCode>> ReducedCost(
        IReadOnlyDictionary<string, V> values) {
        if (values is null ||
            !values.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(_coefficients.Keys) ||
            values.Values.Any(value => !value.IsFinite()) ||
            _coefficients.Values.Any(value => !value.IsFinite()) ||
            !ConvexityPrice.IsFinite()) {
            return Failure<V>(
                ErrorCode.IllegalArgument,
                "定价向量必须完整且有限 / Pricing vector must be complete and finite");
        }

        try {
            V cost = ConvexityPrice.Negate();
            foreach ((string id, V coefficient) in _coefficients) {
                V contribution = coefficient.Times(values[id]);
                cost = cost.Plus(contribution);
            }
            return cost.IsFinite()
                ? Results.Ok(cost)
                : Failure<V>(
                    ErrorCode.IllegalArgument,
                    "检验数溢出 / Reduced cost overflow");
        }
        catch (ArithmeticException) {
            return Failure<V>(
                ErrorCode.IllegalArgument,
                "检验数溢出 / Reduced cost overflow");
        }
        catch (InvalidOperationException) {
            return Failure<V>(
                ErrorCode.IllegalArgument,
                "检验数溢出 / Reduced cost overflow");
        }
    }

    private static Result<T, ErrorCode, Error<ErrorCode>> Failure<T>(
        ErrorCode code,
        string message) =>
        Results.Failed<T>(new Err<ErrorCode>(code, message));
}

/// <summary>同一已校验投影快照的局部定价请求与模型 / Pricing request and local model from one projection snapshot.</summary>
public sealed record ColumnPricingEntry<V>(
    ColumnPricingRequest<V> Request,
    LinearMetaModel<V> Model)
    where V : struct, IRealNumber<V>, INumberField<V>;

/// <summary>一次同步准备生成的同源定价批次；异步定价后仍须重新校验 / Same-source pricing batch prepared synchronously.</summary>
public sealed class ColumnPricingBatch<V> : IDisposable
    where V : struct, IRealNumber<V>, INumberField<V> {
    private readonly IReadOnlyList<ColumnPricingEntry<V>> _entries;

    internal ColumnPricingBatch(
        object owner,
        ColumnProjectionRevision revision,
        IReadOnlyList<ColumnPricingEntry<V>> entries) {
        Owner = owner;
        Revision = revision;
        _entries = Array.AsReadOnly(entries.ToArray());
    }

    internal object Owner { get; }
    public ColumnProjectionRevision Revision { get; }
    public IReadOnlyList<ColumnPricingEntry<V>> Entries => _entries;

    public void Dispose() {
        foreach (ColumnPricingEntry<V> entry in _entries) {
            entry.Model.Dispose();
        }
    }
}

/// <summary>一次主问题注册的变量、行身份和投影版本 / Variables, row bindings and projection revision of one master registration.</summary>
public sealed class ColumnMasterRegistration<V> : IDisposable
    where V : struct, IRealNumber<V>, INumberField<V> {
    private readonly IReadOnlyDictionary<string, URealVar> _columnVariables;
    private readonly IReadOnlySet<IVariableItem> _refinedResources;
    private readonly IReadOnlyDictionary<string, MathConstraint> _rows;
    private readonly IReadOnlyDictionary<string, MathConstraint> _convexities;
    private readonly IReadOnlyList<ProjectedColumn<V>> _columns;

    internal ColumnMasterRegistration(
        LinearMetaModel<V> model,
        IReadOnlyDictionary<string, URealVar> columnVariables,
        IReadOnlySet<IVariableItem> refinedResources,
        IReadOnlyDictionary<string, MathConstraint> rows,
        IReadOnlyDictionary<string, MathConstraint> convexities,
        ColumnProjectionRevision revision,
        LinearModelRevision modelRevision,
        bool phaseOne,
        bool integer,
        IReadOnlyList<ProjectedColumn<V>> columns) {
        Model = model;
        _columnVariables = new ReadOnlyDictionary<string, URealVar>(
            new Dictionary<string, URealVar>(columnVariables, StringComparer.Ordinal));
        _refinedResources = new HashSet<IVariableItem>(refinedResources);
        _rows = new ReadOnlyDictionary<string, MathConstraint>(
            new Dictionary<string, MathConstraint>(rows, StringComparer.Ordinal));
        _convexities = new ReadOnlyDictionary<string, MathConstraint>(
            new Dictionary<string, MathConstraint>(convexities, StringComparer.Ordinal));
        Revision = revision;
        ModelRevision = modelRevision;
        PhaseOne = phaseOne;
        Integer = integer;
        _columns = Array.AsReadOnly(columns
            .Select(column => new ProjectedColumn<V>(
                column.Id,
                column.BlockId,
                column.Values))
            .ToArray());
    }

    public LinearMetaModel<V> Model { get; }
    public IReadOnlyDictionary<string, URealVar> ColumnVariables => _columnVariables;
    public IReadOnlySet<IVariableItem> RefinedResources => _refinedResources;
    public IReadOnlyDictionary<string, MathConstraint> Rows => _rows;
    public IReadOnlyDictionary<string, MathConstraint> Convexities => _convexities;
    public ColumnProjectionRevision Revision { get; }
    public LinearModelRevision ModelRevision { get; }
    public bool PhaseOne { get; }
    public bool Integer { get; }
    public IReadOnlyList<ProjectedColumn<V>> Columns => _columns;

    public void Dispose() => Model.Dispose();
}
