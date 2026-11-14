#nullable enable

using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Symbol.Flatten;
using ConstraintRelationResult = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Core.Model.Basic.ConstraintRelation, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
namespace Fuookami.Ospf.Core.Model.Mechanism;

/// <summary>
/// 线性关系接口 / Linear relation interface.
/// </summary>
public interface ILinearRelation<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    LinearFlattenData<V> FlattenData { get; }
    Comparison Sign { get; }
    string Name { get; }
    string? DisplayName { get; }
    ConstraintRelation? ConstraintRelationOrNull { get; }
    /// <summary>以结果返回约束关系 / Return the constraint relation as a result.</summary>
    /// <returns>约束关系或不支持的比较符错误 / Constraint relation or an unsupported-comparison error.</returns>
    ConstraintRelationResult ConstraintRelation();
    ILinearRelation<V> Normalize();
}

/// <summary>
/// 二次关系接口 / Quadratic relation interface.
/// </summary>
public interface IQuadraticRelation<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    QuadraticFlattenData<V> FlattenData { get; }
    Comparison Sign { get; }
    string Name { get; }
    string? DisplayName { get; }
    ConstraintRelation? ConstraintRelationOrNull { get; }
    /// <summary>以结果返回约束关系 / Return the constraint relation as a result.</summary>
    /// <returns>约束关系或不支持的比较符错误 / Constraint relation or an unsupported-comparison error.</returns>
    ConstraintRelationResult ConstraintRelation();
    IQuadraticRelation<V> Normalize();
}

/// <summary>
/// 线性关系实现（密封记录）/ Linear relation implementation (sealed record).
/// </summary>
public sealed record LinearRelationImpl<V>(
    LinearFlattenData<V> FlattenData,
    Comparison Sign,
    string Name = "",
    string? DisplayName = null
) : ILinearRelation<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    public ConstraintRelation? ConstraintRelationOrNull => Fuookami.Ospf.Core.Model.Basic.ConstraintRelation.OfOrNull(Sign);

    /// <inheritdoc/>
    public ConstraintRelationResult ConstraintRelation() =>
        Fuookami.Ospf.Core.Model.Basic.ConstraintRelation.OfSafe(Sign);

    /// <summary>
    /// 归一化（将 GT/GE 转换为 LT/LE）/ Normalize (convert GT/GE to LT/LE).
    /// </summary>
    public ILinearRelation<V> Normalize() {
        return Sign switch {
            Comparison.GT => new LinearRelationImpl<V>(
                new LinearFlattenData<V>(
                    FlattenData.Monomials.Select(m => new LinearMonomial<V>(m.Coefficient.Negate(), m.Symbol)).ToList(),
                    FlattenData.Constant.Negate()),
                Comparison.LT, Name, DisplayName),
            Comparison.GE => new LinearRelationImpl<V>(
                new LinearFlattenData<V>(
                    FlattenData.Monomials.Select(m => new LinearMonomial<V>(m.Coefficient.Negate(), m.Symbol)).ToList(),
                    FlattenData.Constant.Negate()),
                Comparison.LE, Name, DisplayName),
            _ => this
        };
    }

    public override string ToString() =>
        string.IsNullOrEmpty(Name) ? $"[{Sign}] {FlattenData}" : $"{Name}: [{Sign}] {FlattenData}";
}

/// <summary>
/// 二次关系实现（密封记录）/ Quadratic relation implementation (sealed record).
/// </summary>
public sealed record QuadraticRelationImpl<V>(
    QuadraticFlattenData<V> FlattenData,
    Comparison Sign,
    string Name = "",
    string? DisplayName = null
) : IQuadraticRelation<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    public ConstraintRelation? ConstraintRelationOrNull => Fuookami.Ospf.Core.Model.Basic.ConstraintRelation.OfOrNull(Sign);

    /// <inheritdoc/>
    public ConstraintRelationResult ConstraintRelation() =>
        Fuookami.Ospf.Core.Model.Basic.ConstraintRelation.OfSafe(Sign);

    public IQuadraticRelation<V> Normalize() {
        return Sign switch {
            Comparison.GT => new QuadraticRelationImpl<V>(
                new QuadraticFlattenData<V>(
                    FlattenData.Monomials.Select(m => new QuadraticMonomial<V>(
                        m.Coefficient.Negate(), m.Symbol1, m.Symbol2)).ToList(),
                    FlattenData.Constant.Negate()),
                Comparison.LT, Name, DisplayName),
            Comparison.GE => new QuadraticRelationImpl<V>(
                new QuadraticFlattenData<V>(
                    FlattenData.Monomials.Select(m => new QuadraticMonomial<V>(
                        m.Coefficient.Negate(), m.Symbol1, m.Symbol2)).ToList(),
                    FlattenData.Constant.Negate()),
                Comparison.LE, Name, DisplayName),
            _ => this
        };
    }

    public override string ToString() =>
        string.IsNullOrEmpty(Name) ? $"[{Sign}] {FlattenData}" : $"{Name}: [{Sign}] {FlattenData}";
}
