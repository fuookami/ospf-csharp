#nullable enable

using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Intermediate;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Symbol.Flatten;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ret = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
using SymbolFlatten = Fuookami.Ospf.Core.Symbol.Flatten;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Core.Model.Mechanism;
// ===== IMechanismModel<V> =====

/// <summary>
/// 机制模型密封接口 / Sealed mechanism-model interface.
/// </summary>
public interface IMechanismModel<V> : IDisposable
    where V : struct, IRealNumber<V>, INumberField<V> {
    string Name { get; }
    IReadOnlyList<IConstraint<V, LinearCategory>> Constraints { get; }
    IObject ObjectFunction { get; }
    Fuookami.Ospf.Core.Token.IAbstractTokenTable<V> Tokens { get; }
}

// ===== IAbstractLinearMechanismModel<V> =====

/// <summary>线性机制模型抽象 / Abstract linear mechanism model</summary>
public interface IAbstractLinearMechanismModel<V> : IMechanismModel<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    Try AddConstraint(
        LinearInequality<V> relation,
        string? name = null,
        (IIntermediateSymbol Symbol, bool Direction)? from = null);

    Try AddConstraint(
        LinearInequality<V> relation,
        string? name = null,
        IIntermediateSymbol? from = null);
}

// ===== IAbstractQuadraticMechanismModel<V> =====

/// <summary>二次机制模型抽象 / Abstract quadratic mechanism model</summary>
public interface IAbstractQuadraticMechanismModel<V> : IAbstractLinearMechanismModel<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    new IReadOnlyList<IConstraint<V, QuadraticCategory>> Constraints { get; }

    Try AddConstraint(
        QuadraticInequalityOf<V> relation,
        string? name = null,
        (IIntermediateSymbol Symbol, bool Direction)? from = null);

    Try AddConstraint(
        QuadraticInequalityOf<V> relation,
        string? name = null,
        IIntermediateSymbol? from = null);
}

// ===== ISingleObjectMechanismModel<V> =====

/// <summary>单目标机制模型 / Single-objective mechanism model</summary>
public interface ISingleObjectMechanismModel<V> : IMechanismModel<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    new SingleObject ObjectFunction { get; }
}

// ===== BasicMechanismModel<V> =====

/// <summary>
/// 机制模型基础层 / Mechanism model base layer.
/// </summary>
public abstract class BasicMechanismModel<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    public string Name { get; }
    public Fuookami.Ospf.Core.Token.IAbstractTokenTable<V> Tokens { get; }

    protected BasicMechanismModel(string name, Fuookami.Ospf.Core.Token.IAbstractTokenTable<V> tokens) {
        Name = name;
        Tokens = tokens;
    }

    /// <summary>变量数量 / Number of variables</summary>
    public int NumVariables => Tokens.TokensInSolver.Count;
}

// ===== LinearMechanismModel<V> =====

/// <summary>
/// 线性机制模型实现 / Linear mechanism model implementation.
/// </summary>
public sealed class LinearMechanismModel<V> : BasicMechanismModel<V>,
    IAbstractLinearMechanismModel<V>, ISingleObjectMechanismModel<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    internal LinearMetaModel<V> Parent { get; }
    private readonly List<LinearConstraintImpl<V>> _constraints;

    IReadOnlyList<IConstraint<V, LinearCategory>> IMechanismModel<V>.Constraints => _constraints;
    IObject IMechanismModel<V>.ObjectFunction => ObjectFunction;
    public SingleObject ObjectFunction { get; }
    internal bool Concurrent => Parent.Configuration.Concurrent;
    internal IReadOnlyList<LinearConstraintImpl<V>> LinearConstraints => _constraints;
    public int NumConstraints => _constraints.Count;

    internal LinearMechanismModel(
        LinearMetaModel<V> parent,
        string name,
        IReadOnlyList<LinearConstraintImpl<V>> constraints,
        SingleObject objectFunction,
        Fuookami.Ospf.Core.Token.IAbstractTokenTable<V> tokens)
        : base(name, tokens) {
        Parent = parent;
        _constraints = constraints.ToList();
        ObjectFunction = objectFunction;
    }

    /// <summary>
    /// V 类型异步工厂 / V-generic async factory.
    /// </summary>
    public static async Task<Result<LinearMechanismModel<V>, ErrorCode, Error<ErrorCode>>> InvokeAsync(
        LinearMetaModel<V> metaModel,
        bool? concurrent = null,
        bool? blocking = null,
        IReadOnlyDictionary<IVariableItem, V>? fixedVariables = null,
        RegistrationStatusCallBack? registrationStatusCallBack = null,
        MechanismModelDumpingStatusCallBack? dumpingStatusCallBack = null) {
        // Build tokens (simplified: copy mutable table as immutable)
        var tokens = (Fuookami.Ospf.Core.Token.IAbstractTokenTable<V>)metaModel.Tokens;

        // Build constraints
        var constraints = new List<LinearConstraintImpl<V>>();
        foreach (LinearInequalityConstraint<V> mc in metaModel.RelationConstraints) {
            Result<LinearFlattenData<V>, ErrorCode, Error<ErrorCode>> flattenResult = mc.FlattenData();
            if (flattenResult is Failed<SymbolFlatten.LinearFlattenData<V>, ErrorCode, Error<ErrorCode>> f) {
                return Results.Failed<LinearMechanismModel<V>>(f.Error);
            }

            LinearFlattenData<V> flattenData = ((Ok<SymbolFlatten.LinearFlattenData<V>, ErrorCode, Error<ErrorCode>>)flattenResult).Value;
            var relation = new LinearRelationImpl<V>(flattenData, mc.Sign, mc.Name, mc.DisplayName);
            Result<LinearConstraintImpl<V>, ErrorCode, Error<ErrorCode>> constraintResult = LinearConstraintImpl<V>.Create(relation, tokens, metaModel.Converter,
                mc.Lazy, mc.Name, mc);
            if (constraintResult is Failed<LinearConstraintImpl<V>, ErrorCode, Error<ErrorCode>> cf) {
                return Results.Failed<LinearMechanismModel<V>>(cf.Error);
            }

            constraints.Add(((Ok<LinearConstraintImpl<V>, ErrorCode, Error<ErrorCode>>)constraintResult).Value);
        }

        // Build objective sub-objects
        var subObjects = new List<object>();
        foreach (LinearFlattenData<V> source in metaModel.FlattenSubObjects) {
            List<ILinearCell<V>> cells = ConstraintCellFactory.CreateLinearCells(
                source.Monomials.Select(m => new LinearMonomial<Flt64>(
                    metaModel.Converter.FromValue(m.Coefficient), m.Symbol)).ToList(),
                tokens, metaModel.Converter);
            var subObj = LinearSubObject<V>.Create(source, metaModel.ObjectCategory, "obj");
            subObjects.Add(subObj);
        }
        foreach (MetaSubObject<V> source in metaModel.MetaSubObjects) {
            var flattenData = new SymbolFlatten.LinearFlattenData<V>(
                source.Polynomial.Monomials.ToList(), source.Polynomial.Constant);
            var subObj = LinearSubObject<V>.Create(flattenData, source.Category, source.Name);
            subObjects.Add(subObj);
        }

        var objectFunction = new SingleObject(metaModel.ObjectCategory, subObjects);
        return Results.Ok(new LinearMechanismModel<V>(metaModel, metaModel.Name, constraints, objectFunction, tokens));
    }

    public Try AddConstraint(
        LinearInequality<V> relation,
        string? name = null,
        (IIntermediateSymbol Symbol, bool Direction)? from = null) {
        Result<LinearFlattenData<V>, ErrorCode, Error<ErrorCode>> flattenResult = relation.ToLinearFlattenData<V>();
        if (flattenResult is Failed<SymbolFlatten.LinearFlattenData<V>, ErrorCode, Error<ErrorCode>> f) {
            return Results.Failed<Success>(f.Error);
        }

        LinearFlattenData<V> flattenData = ((Ok<SymbolFlatten.LinearFlattenData<V>, ErrorCode, Error<ErrorCode>>)flattenResult).Value;
        var rel = new LinearRelationImpl<V>(flattenData, relation.Comparison, name ?? "", relation.DisplayName);
        Result<LinearConstraintImpl<V>, ErrorCode, Error<ErrorCode>> result = LinearConstraintImpl<V>.Create(rel, Tokens, Parent.Converter, false, name ?? "", from: from);
        if (result is Failed<LinearConstraintImpl<V>, ErrorCode, Error<ErrorCode>> cf) {
            return Results.Failed<Success>(cf.Error);
        }

        _constraints.Add(((Ok<LinearConstraintImpl<V>, ErrorCode, Error<ErrorCode>>)result).Value);
        return Results.Ok(Results.SuccessInstance);
    }

    public Try AddConstraint(
        LinearInequality<V> relation,
        string? name = null,
        IIntermediateSymbol? from = null) => AddConstraint(relation, name, from is not null ? (from, false) : null);

    public void Dispose() => Tokens.Dispose();
    public override string ToString() => Name;
}

// ===== QuadraticMechanismModel<V> =====

/// <summary>
/// 二次机制模型实现 / Quadratic mechanism model implementation.
/// </summary>
public sealed class QuadraticMechanismModel<V> : BasicMechanismModel<V>,
    IAbstractQuadraticMechanismModel<V>, ISingleObjectMechanismModel<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    internal QuadraticMetaModel<V> Parent { get; }
    private readonly List<QuadraticConstraintImpl<V>> _quadraticConstraints;
    private readonly List<LinearConstraintImpl<V>> _linearConstraints;

    IReadOnlyList<IConstraint<V, LinearCategory>> IMechanismModel<V>.Constraints => _linearConstraints;
    IReadOnlyList<IConstraint<V, QuadraticCategory>> IAbstractQuadraticMechanismModel<V>.Constraints => _quadraticConstraints;
    IObject IMechanismModel<V>.ObjectFunction => ObjectFunction;
    public SingleObject ObjectFunction { get; }
    internal IReadOnlyList<QuadraticConstraintImpl<V>> QuadraticConstraints => _quadraticConstraints;

    internal QuadraticMechanismModel(
        QuadraticMetaModel<V> parent,
        string name,
        IReadOnlyList<QuadraticConstraintImpl<V>> constraints,
        SingleObject objectFunction,
        Fuookami.Ospf.Core.Token.IAbstractTokenTable<V> tokens)
        : base(name, tokens) {
        Parent = parent;
        _quadraticConstraints = constraints.ToList();
        _linearConstraints = new List<LinearConstraintImpl<V>>();
        ObjectFunction = objectFunction;
    }

    /// <summary>
    /// V 类型异步工厂 / V-generic async factory.
    /// </summary>
    public static async Task<Result<QuadraticMechanismModel<V>, ErrorCode, Error<ErrorCode>>> InvokeAsync(
        QuadraticMetaModel<V> metaModel,
        bool? concurrent = null,
        bool? blocking = null,
        IReadOnlyDictionary<IVariableItem, V>? fixedVariables = null,
        RegistrationStatusCallBack? registrationStatusCallBack = null,
        MechanismModelDumpingStatusCallBack? dumpingStatusCallBack = null) {
        var tokens = (Fuookami.Ospf.Core.Token.IAbstractTokenTable<V>)metaModel.Tokens;

        var constraints = new List<QuadraticConstraintImpl<V>>();
        foreach (QuadraticInequalityConstraint<V> mc in metaModel.RelationConstraints) {
            var flattenData = mc.Inequality.ToQuadraticFlattenData<V>();
            var relation = new QuadraticRelationImpl<V>(flattenData, mc.Sign, mc.Name, mc.DisplayName);
            Result<QuadraticConstraintImpl<V>, ErrorCode, Error<ErrorCode>> result = QuadraticConstraintImpl<V>.Create(relation, tokens, metaModel.Converter,
                mc.Lazy, mc.Name, mc);
            if (result is Failed<QuadraticConstraintImpl<V>, ErrorCode, Error<ErrorCode>> cf) {
                return Results.Failed<QuadraticMechanismModel<V>>(cf.Error);
            }

            constraints.Add(((Ok<QuadraticConstraintImpl<V>, ErrorCode, Error<ErrorCode>>)result).Value);
        }

        var subObjects = new List<object>();
        foreach (QuadraticFlattenSubObject<V> source in metaModel.FlattenSubObjects) {
            var subObj = QuadraticSubObject<V>.Create(source.FlattenData, source.Category, source.Name);
            subObjects.Add(subObj);
        }

        var objectFunction = new SingleObject(metaModel.ObjectCategory, subObjects);
        return Results.Ok(new QuadraticMechanismModel<V>(metaModel, metaModel.Name, constraints, objectFunction, tokens));
    }

    public Try AddConstraint(
        LinearInequality<V> relation,
        string? name = null,
        (IIntermediateSymbol Symbol, bool Direction)? from = null) {
        // Promote to quadratic
        Result<LinearFlattenData<V>, ErrorCode, Error<ErrorCode>> flattenResult = relation.ToLinearFlattenData<V>();
        if (flattenResult is Failed<SymbolFlatten.LinearFlattenData<V>, ErrorCode, Error<ErrorCode>> f) {
            return Results.Failed<Success>(f.Error);
        }

        LinearFlattenData<V> flattenData = ((Ok<SymbolFlatten.LinearFlattenData<V>, ErrorCode, Error<ErrorCode>>)flattenResult).Value;
        var qMonomials = flattenData.Monomials.Select(m =>
            new QuadraticMonomial<V>(m.Coefficient, m.Symbol, null)).ToList();
        var qFlattenData = new SymbolFlatten.QuadraticFlattenData<V>(qMonomials, flattenData.Constant);
        var rel = new QuadraticRelationImpl<V>(qFlattenData, relation.Comparison, name ?? "", relation.DisplayName);
        Result<QuadraticConstraintImpl<V>, ErrorCode, Error<ErrorCode>> result = QuadraticConstraintImpl<V>.Create(rel, Tokens, Parent.Converter, false, name ?? "", from: from);
        if (result is Failed<QuadraticConstraintImpl<V>, ErrorCode, Error<ErrorCode>> cf) {
            return Results.Failed<Success>(cf.Error);
        }

        _quadraticConstraints.Add(((Ok<QuadraticConstraintImpl<V>, ErrorCode, Error<ErrorCode>>)result).Value);
        return Results.Ok(Results.SuccessInstance);
    }

    public Try AddConstraint(
        LinearInequality<V> relation,
        string? name = null,
        IIntermediateSymbol? from = null) => AddConstraint(relation, name, from is not null ? (from, false) : null);

    public Try AddConstraint(
        QuadraticInequalityOf<V> relation,
        string? name = null,
        (IIntermediateSymbol Symbol, bool Direction)? from = null) {
        var flattenData = relation.ToQuadraticFlattenData<V>();
        var rel = new QuadraticRelationImpl<V>(flattenData, relation.Comparison, name ?? "", relation.DisplayName);
        Result<QuadraticConstraintImpl<V>, ErrorCode, Error<ErrorCode>> result = QuadraticConstraintImpl<V>.Create(rel, Tokens, Parent.Converter, false, name ?? "", from: from);
        if (result is Failed<QuadraticConstraintImpl<V>, ErrorCode, Error<ErrorCode>> cf) {
            return Results.Failed<Success>(cf.Error);
        }

        _quadraticConstraints.Add(((Ok<QuadraticConstraintImpl<V>, ErrorCode, Error<ErrorCode>>)result).Value);
        return Results.Ok(Results.SuccessInstance);
    }

    public Try AddConstraint(
        QuadraticInequalityOf<V> relation,
        string? name = null,
        IIntermediateSymbol? from = null) => AddConstraint(relation, name, from is not null ? (from, false) : null);

    public void Dispose() => Tokens.Dispose();
    public override string ToString() => Name;
}

// ===== Extension method for QuadraticInequalityOf flatten =====

internal static class QuadraticFlattenExtensions {
    public static SymbolFlatten.QuadraticFlattenData<V> ToQuadraticFlattenData<V>(this QuadraticInequalityOf<V> inequality)
        where V : struct, IRealNumber<V>, INumberField<V> {
        var merged = new Dictionary<(object?, object?), QuadraticMonomial<V>>();

        foreach (QuadraticMonomial<V> mono in inequality.Lhs.Monomials) {
            (ISymbol Symbol1, ISymbol? Symbol2) key = (mono.Symbol1, mono.Symbol2);
            if (merged.TryGetValue(key, out QuadraticMonomial<V>? existing)) {
                merged[key] = new QuadraticMonomial<V>(existing.Coefficient.Plus(mono.Coefficient), mono.Symbol1, mono.Symbol2);
            }
            else {
                merged[key] = mono;
            }
        }
        foreach (QuadraticMonomial<V> mono in inequality.Rhs.Monomials) {
            (ISymbol Symbol1, ISymbol? Symbol2) key = (mono.Symbol1, mono.Symbol2);
            if (merged.TryGetValue(key, out QuadraticMonomial<V>? existing)) {
                merged[key] = new QuadraticMonomial<V>(existing.Coefficient.Minus(mono.Coefficient), mono.Symbol1, mono.Symbol2);
            }
            else {
                merged[key] = new QuadraticMonomial<V>(default(V)!.Minus(mono.Coefficient), mono.Symbol1, mono.Symbol2);
            }
        }

        return new SymbolFlatten.QuadraticFlattenData<V>(
            merged.Values.ToList(),
            inequality.Lhs.Constant.Minus(inequality.Rhs.Constant));
    }
}

// ===== LinearInequality flatten extension =====

internal static class LinearFlattenExtensions {
    public static Result<SymbolFlatten.LinearFlattenData<V>, ErrorCode, Error<ErrorCode>> ToLinearFlattenData<V>(
        this LinearInequality<V> inequality)
        where V : struct, IRealNumber<V>, INumberField<V> {
        var merged = new Dictionary<IVariableItem, LinearMonomial<V>>();

        foreach (LinearMonomial<V> mono in inequality.Lhs.Monomials) {
            if (mono.Symbol is not IVariableItem vi) {
                return Results.Failed<SymbolFlatten.LinearFlattenData<V>>(
                    new Err<ErrorCode>(ErrorCode.IllegalArgument, $"Non-variable symbol in LHS: {mono.Symbol}"));
            }

            if (merged.TryGetValue(vi, out LinearMonomial<V>? existing)) {
                merged[vi] = new LinearMonomial<V>(existing.Coefficient.Plus(mono.Coefficient), vi);
            }
            else {
                merged[vi] = mono;
            }
        }
        foreach (LinearMonomial<V> mono in inequality.Rhs.Monomials) {
            if (mono.Symbol is not IVariableItem vi) {
                return Results.Failed<SymbolFlatten.LinearFlattenData<V>>(
                    new Err<ErrorCode>(ErrorCode.IllegalArgument, $"Non-variable symbol in RHS: {mono.Symbol}"));
            }

            if (merged.TryGetValue(vi, out LinearMonomial<V>? existing)) {
                merged[vi] = new LinearMonomial<V>(existing.Coefficient.Minus(mono.Coefficient), vi);
            }
            else {
                merged[vi] = new LinearMonomial<V>(default(V)!.Minus(mono.Coefficient), vi);
            }
        }

        return Results.Ok(new SymbolFlatten.LinearFlattenData<V>(
            merged.Values.ToList(),
            inequality.Lhs.Constant.Minus(inequality.Rhs.Constant)));
    }
}
