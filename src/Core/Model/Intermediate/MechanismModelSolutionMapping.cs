#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
namespace Fuookami.Ospf.Core.Model.Intermediate;

internal sealed class MechanismModelSolutionMapping {
    private readonly object _source;
    private readonly object[] _tokenIdentities;
    private readonly object _variableStates;
    private readonly ModelViewVariable[] _variables;
    private readonly int[] _indexes;
    private readonly Flt64[] _fixedValues;
    private readonly object _fixedConversions;
    private readonly object _converterSnapshot;
    private readonly object _rows;
    private readonly MechanismMatrixSnapshot _matrix;
    private readonly object? _identityRevision;

    private MechanismModelSolutionMapping(
        object source,
        object[] tokenIdentities,
        object variableStates,
        IReadOnlyList<ModelViewVariable> variables,
        int[] indexes,
        Flt64[] fixedValues,
        object fixedConversions,
        object converterSnapshot,
        object rows,
        MechanismMatrixSnapshot matrix,
        object? identityRevision) {
        _source = source;
        _tokenIdentities = tokenIdentities;
        _variableStates = variableStates;
        _variables = variables.ToArray();
        _indexes = indexes;
        _fixedValues = fixedValues;
        _fixedConversions = fixedConversions;
        _converterSnapshot = converterSnapshot;
        _rows = rows;
        _matrix = matrix;
        _identityRevision = identityRevision;
    }

    internal static MechanismModelSolutionMapping Create<V>(
        object source,
        IReadOnlyList<Token<V>> tokens,
        MechanismModelSourceSnapshot<V>.VariableSnapshot[] variableStates,
        IReadOnlyList<ModelViewVariable> variables,
        IReadOnlyDictionary<VariableItemKey, int> indexes,
        IReadOnlyDictionary<VariableItemKey, double> fixedValues,
        IReadOnlyList<(V Native, double Encoded)> fixedConversions,
        (V Zero, V One) converterSnapshot,
        MechanismModelRowsSnapshot<V> rows,
        MechanismMatrixSnapshot matrix,
        object? identityRevision)
        where V : struct, IRealNumber<V>, INumberField<V> {
        int[] tokenIndexes = new int[tokens.Count];
        var tokenFixedValues = new Flt64[tokens.Count];
        for (int i = 0; i < tokens.Count; i++) {
            if (indexes.TryGetValue(tokens[i].Key, out int index)) {
                tokenIndexes[i] = index;
            }
            else {
                tokenIndexes[i] = -1;
                tokenFixedValues[i] = new Flt64(fixedValues[tokens[i].Key]);
            }
        }
        return new MechanismModelSolutionMapping(
            source: source,
            tokenIdentities: tokens.Select(token => token.Identity).ToArray(),
            variableStates: variableStates,
            variables: variables,
            indexes: tokenIndexes,
            fixedValues: tokenFixedValues,
            fixedConversions: fixedConversions.ToArray(),
            converterSnapshot: converterSnapshot,
            rows: rows,
            matrix: matrix,
            identityRevision: identityRevision
        );
    }

    internal Result<IReadOnlyList<Flt64>, ErrorCode, Error<ErrorCode>> Restore(
        object source,
        IReadOnlyList<ModelViewVariable> variables,
        IReadOnlyList<Flt64>? solution) {
        Try validation = Validate(source, variables);
        if (!validation.IsOk) {
            return validation.Map<IReadOnlyList<Flt64>>(_ => Array.Empty<Flt64>());
        }
        if (solution is null) {
            return Failure("求解器结果为空 / Solver solution is null");
        }
        var reduced = new Flt64[_variables.Length];
        try {
            if (solution.Count != reduced.Length) {
                return Failure("求解器结果长度无效 / Invalid solver solution length");
            }
            for (int i = 0; i < reduced.Length; i++) {
                reduced[i] = solution[i];
            }
            if (solution.Count != reduced.Length) {
                return Failure("求解器结果长度已改变 / Solver solution length changed");
            }
        }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) {
            return Failure($"无法读取求解器结果 / Cannot read solver solution: {error.Message}");
        }
        validation = Validate(source, variables);
        if (!validation.IsOk) {
            return validation.Map<IReadOnlyList<Flt64>>(_ => Array.Empty<Flt64>());
        }
        var restored = new Flt64[_tokenIdentities.Length];
        for (int i = 0; i < restored.Length; i++) {
            restored[i] = _indexes[i] < 0 ? _fixedValues[i] : reduced[_indexes[i]];
        }
        return Results.Ok<IReadOnlyList<Flt64>>(restored);
    }

    internal Try Validate(object source, IReadOnlyList<ModelViewVariable> variables) {
        if (!ReferenceEquals(_source, source)) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "矩阵不属于来源模型 / Matrix does not belong to the source model"));
        }
        if (variables.Count != _variables.Length
            || !variables.Zip(_variables).All(pair => ReferenceEquals(pair.First, pair.Second))) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "矩阵列已变更，请重新求解 / Matrix columns changed; solve again"));
        }
        return Results.OkInstance;
    }

    internal bool MatchesTokens<V>(IReadOnlyList<Token<V>> tokens) where V : struct, IRealNumber<V> =>
        tokens.Count == _tokenIdentities.Length && tokens.Zip(_tokenIdentities)
            .All(pair => ReferenceEquals(pair.First.Identity, pair.Second));

    internal Try ValidateVariables<V>(IReadOnlyList<Token<V>> tokens)
        where V : struct, IRealNumber<V>, INumberField<V> =>
        _variableStates is MechanismModelSourceSnapshot<V>.VariableSnapshot[] states
            ? MechanismModelSourceSnapshot<V>.ValidateVariables(tokens, states)
            : Results.Failed<Success>(new Err<ErrorCode>(ErrorCode.IllegalArgument, "矩阵类型无效 / Invalid matrix type"));

    internal Try ValidateStructure<V>(BasicMechanismModel<V> model, AbstractMetaModel<V> parent, object matrix)
        where V : struct, IRealNumber<V>, INumberField<V> {
        try {
            if (_converterSnapshot is not ValueTuple<V, V> converterSnapshot ||
                !MechanismModelDumpVariables.ConverterCurrent(parent.Converter, converterSnapshot) ||
                _fixedConversions is not (V Native, double Encoded)[] conversions ||
                !MechanismModelDumpVariables.FixedConversionsCurrent(parent.Converter, conversions)) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.IllegalArgument,
                    "数值转换已变更，请重新转储和求解 / Numeric conversion changed; dump and solve again"));
            }
            if (!ReferenceEquals(_identityRevision, parent.IdentityRegistry?.Revision)) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.IllegalArgument,
                    "模型身份已变更，请重新转储和求解 / Model identities changed; dump and solve again"));
            }
            Try identity = matrix switch {
                LinearTriadModel linear => linear.ValidateIdentity(),
                QuadraticTetradModel quadratic => quadratic.ValidateIdentity(),
                _ => Results.Failed<Success>(new Err<ErrorCode>(ErrorCode.IllegalArgument, "矩阵类型无效 / Invalid matrix type")),
            };
            if (!identity.IsOk) {
                return identity;
            }
            Try rows = (model, matrix, _rows) switch {
                (LinearMechanismModel<V> linear, LinearTriadModel, MechanismModelRowsSnapshot<V> snapshot) =>
                    snapshot.Validate(((IMechanismModel<V>)linear).Constraints),
                (QuadraticMechanismModel<V> quadratic, QuadraticTetradModel, MechanismModelRowsSnapshot<V> snapshot) =>
                    snapshot.Validate(((IAbstractQuadraticMechanismModel<V>)quadratic).Constraints),
                _ => Results.Failed<Success>(new Err<ErrorCode>(ErrorCode.IllegalArgument, "矩阵类型无效 / Invalid matrix type")),
            };
            if (!rows.IsOk) {
                return rows;
            }
            bool matches = matrix switch {
                LinearTriadModel linear => _matrix.Matches(linear),
                QuadraticTetradModel quadratic => _matrix.Matches(quadratic),
                _ => false,
            };
            return matches ? Results.OkInstance : Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "矩阵已变更，请重新转储和求解 / Matrix changed; dump and solve again"));
        }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                $"无法检查矩阵：{error.Message} / Cannot inspect matrix: {error.Message}"));
        }
    }

    private static Result<IReadOnlyList<Flt64>, ErrorCode, Error<ErrorCode>> Failure(string message) =>
        Results.Failed<IReadOnlyList<Flt64>>(new Err<ErrorCode>(ErrorCode.IllegalArgument, message));
}
