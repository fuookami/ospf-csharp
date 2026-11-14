#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Algebra.ValueRange;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
namespace Fuookami.Ospf.Core.Model.Mechanism;

internal sealed class MechanismModelSourceSnapshot<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    private readonly AbstractMetaModel<V> _parent;
    private readonly MathConstraint[] _constraints;
    private readonly object[] _objectives;
    private readonly IIntermediateSymbol[] _symbols;
    private readonly List<(IReadOnlyList<LinearMonomial<V>> Source, LinearMonomial<V>[] Terms)> _linearTerms = new();
    private readonly List<(IReadOnlyList<QuadraticMonomial<V>> Source, QuadraticMonomial<V>[] Terms)> _quadraticTerms = new();
    private readonly VariableSnapshot[] _variables;

    internal MechanismModelSourceSnapshot(AbstractMetaModel<V> parent) {
        _parent = parent;
        _constraints = parent.Constraints.ToArray();
        _symbols = parent.Tokens.Symbols.ToArray();
        _variables = CaptureVariables(parent.Tokens.TokensInSolver);
        if (parent is LinearMetaModel<V> linear) {
            _objectives = linear.MetaSubObjects.Cast<object>().ToArray();
            foreach (LinearInequalityConstraint<V> row in linear.RelationConstraints) {
                Capture(row.Inequality.Lhs.Monomials);
                Capture(row.Inequality.Rhs.Monomials);
            }
            foreach (MetaSubObject<V> objective in linear.MetaSubObjects) {
                Capture(objective.Polynomial.Monomials);
            }
        }
        else {
            var quadratic = (QuadraticMetaModel<V>)parent;
            _objectives = quadratic.FlattenSubObjects.Cast<object>().ToArray();
            foreach (QuadraticInequalityConstraint<V> row in quadratic.RelationConstraints) {
                Capture(row.Inequality.Lhs.Monomials);
                Capture(row.Inequality.Rhs.Monomials);
            }
            foreach (QuadraticFlattenSubObject<V> objective in quadratic.FlattenSubObjects) {
                Capture(objective.FlattenData.Monomials);
            }
        }
    }

    // shortcut: 仅检查直接单项式引用，支持深层结构版本后替换 / Only direct term references are checked; replace when deep structural revisions exist.
    private bool IsCurrent(bool checkVariables) {
        IReadOnlyList<object> objectives = _parent is LinearMetaModel<V> linear
            ? linear.MetaSubObjects
            : ((QuadraticMetaModel<V>)_parent).FlattenSubObjects;
        return SameReferences(_parent.Constraints, _constraints)
            && SameReferences(objectives, _objectives)
            && SameReferences(_parent.Tokens.Symbols, _symbols)
            && _linearTerms.All(terms => SameReferences(terms.Source, terms.Terms))
            && _quadraticTerms.All(terms => SameReferences(terms.Source, terms.Terms))
            && (!checkVariables || VariablesMatch(_parent.Tokens.TokensInSolver, _variables));
    }

    internal Try Validate(bool checkVariables = true) {
        try {
            if (IsCurrent(checkVariables)) {
                return Results.OkInstance;
            }
        }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                $"无法检查来源模型：{error.Message} / Cannot inspect the source model: {error.Message}"));
        }
        return Results.Failed<Success>(new Err<ErrorCode>(
            ErrorCode.IllegalArgument,
            "来源模型结构已变更，请重新构建 / Source model structure changed; rebuild it"));
    }

    internal static Result<T, ErrorCode, Error<ErrorCode>> Propagate<T>(Try result) => result switch {
        Failed<Success, ErrorCode, Error<ErrorCode>> failed => Results.Failed<T>(failed.Error),
        Fatal<Success, ErrorCode, Error<ErrorCode>> fatal => new Fatal<T, ErrorCode, Error<ErrorCode>>(fatal.Errors),
        _ => Results.Failed<T>(new Err<ErrorCode>(ErrorCode.ApplicationError, "来源模型检查结果无效 / Invalid source model check result")),
    };

    internal static VariableSnapshot[] CaptureVariables(IReadOnlyList<Token<V>> tokens) =>
        tokens.Select(token => new VariableSnapshot(token.Variable)).ToArray();

    internal static bool VariablesMatch(IReadOnlyList<Token<V>> tokens, IReadOnlyList<VariableSnapshot> snapshots) =>
        tokens.Count == snapshots.Count && tokens.Zip(snapshots)
            .All(pair => pair.Second.IsCurrent(pair.First.Variable));

    internal static Try ValidateVariables(IReadOnlyList<Token<V>> tokens, IReadOnlyList<VariableSnapshot> snapshots) {
        try {
            if (VariablesMatch(tokens, snapshots)) {
                return Results.OkInstance;
            }
        }
        catch (Exception error) when (error is not OutOfMemoryException and not AccessViolationException) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                $"无法检查变量：{error.Message} / Cannot inspect variables: {error.Message}"));
        }
        return Results.Failed<Success>(new Err<ErrorCode>(
            ErrorCode.IllegalArgument,
            "变量定义已变更，请重新求解 / Variable definitions changed; solve again"));
    }

    private void Capture(IReadOnlyList<LinearMonomial<V>> terms) => _linearTerms.Add((terms, terms.ToArray()));

    private void Capture(IReadOnlyList<QuadraticMonomial<V>> terms) => _quadraticTerms.Add((terms, terms.ToArray()));

    private static bool SameReferences<T>(IEnumerable<T> current, IReadOnlyList<T> expected) where T : class =>
        current.SequenceEqual(expected, ReferenceEqualityComparer.Instance);

    internal sealed class VariableSnapshot {
        private readonly string _name;
        private readonly IVariableTypeKind _type;
        private readonly object? _range;
        private readonly Bound<Flt64>? _lower;
        private readonly Bound<Flt64>? _upper;

        internal VariableSnapshot(IVariableItem variable) {
            _name = variable.Name;
            _type = variable.TypeKind;
            if (variable is IVariableRangeSnapshot range) {
                _range = range.CurrentRange;
            }
            else {
                // shortcut: 自定义变量仅有 Flt64 边界，提供精确值域后替换 / Custom variables expose Flt64 bounds only; replace when exact ranges are available.
                _lower = variable.LowerBound;
                _upper = variable.UpperBound;
            }
        }

        internal bool IsCurrent(IVariableItem variable) =>
            string.Equals(_name, variable.Name, StringComparison.Ordinal)
            && ReferenceEquals(_type, variable.TypeKind)
            && (variable is IVariableRangeSnapshot range
                ? ReferenceEquals(_range, range.CurrentRange)
                : Equals(_lower, variable.LowerBound) && Equals(_upper, variable.UpperBound));
    }
}
