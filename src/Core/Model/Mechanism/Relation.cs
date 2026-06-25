#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Symbol.Flatten;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;

namespace Fuookami.Ospf.Core.Model.Mechanism
{
    /// <summary>
    /// 线性关系接口 / Linear relation interface.
    /// </summary>
    public interface ILinearRelation<V>
        where V : struct, IRealNumber<V>, INumberField<V>
    {
        LinearFlattenData<V> FlattenData { get; }
        Comparison Sign { get; }
        string Name { get; }
        string? DisplayName { get; }
        ConstraintRelation? ConstraintRelationOrNull { get; }
        ConstraintRelation ConstraintRelation();
        ILinearRelation<V> Normalize();
    }

    /// <summary>
    /// 二次关系接口 / Quadratic relation interface.
    /// </summary>
    public interface IQuadraticRelation<V>
        where V : struct, IRealNumber<V>, INumberField<V>
    {
        QuadraticFlattenData<V> FlattenData { get; }
        Comparison Sign { get; }
        string Name { get; }
        string? DisplayName { get; }
        ConstraintRelation? ConstraintRelationOrNull { get; }
        ConstraintRelation ConstraintRelation();
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
        where V : struct, IRealNumber<V>, INumberField<V>
    {
        public ConstraintRelation? ConstraintRelationOrNull => Fuookami.Ospf.Core.Model.Basic.ConstraintRelation.OfOrNull(Sign);

        public Fuookami.Ospf.Core.Model.Basic.ConstraintRelation ConstraintRelation() =>
            Fuookami.Ospf.Core.Model.Basic.ConstraintRelation.OfOrNull(Sign)
                ?? throw new InvalidOperationException($"No constraint relation for comparison: {Sign}");

        /// <summary>
        /// 归一化（将 GT/GE 转换为 LT/LE）/ Normalize (convert GT/GE to LT/LE).
        /// </summary>
        public ILinearRelation<V> Normalize()
        {
            return Sign switch
            {
                Comparison.GT => new LinearRelationImpl<V>(
                    new LinearFlattenData<V>(
                        FlattenData.Monomials.Select(m => new LinearMonomial<V>(default(V)!.Minus(m.Coefficient), m.Symbol)).ToList(),
                        default(V)!.Minus(FlattenData.Constant)),
                    Comparison.LT, Name, DisplayName),
                Comparison.GE => new LinearRelationImpl<V>(
                    new LinearFlattenData<V>(
                        FlattenData.Monomials.Select(m => new LinearMonomial<V>(default(V)!.Minus(m.Coefficient), m.Symbol)).ToList(),
                        default(V)!.Minus(FlattenData.Constant)),
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
        where V : struct, IRealNumber<V>, INumberField<V>
    {
        public ConstraintRelation? ConstraintRelationOrNull => Fuookami.Ospf.Core.Model.Basic.ConstraintRelation.OfOrNull(Sign);

        public Fuookami.Ospf.Core.Model.Basic.ConstraintRelation ConstraintRelation() =>
            Fuookami.Ospf.Core.Model.Basic.ConstraintRelation.OfOrNull(Sign)
                ?? throw new InvalidOperationException($"No constraint relation for comparison: {Sign}");

        public IQuadraticRelation<V> Normalize()
        {
            return Sign switch
            {
                Comparison.GT => new QuadraticRelationImpl<V>(
                    new QuadraticFlattenData<V>(
                        FlattenData.Monomials.Select(m => new QuadraticMonomial<V>(
                            default(V)!.Minus(m.Coefficient), m.Symbol1, m.Symbol2)).ToList(),
                        default(V)!.Minus(FlattenData.Constant)),
                    Comparison.LT, Name, DisplayName),
                Comparison.GE => new QuadraticRelationImpl<V>(
                    new QuadraticFlattenData<V>(
                        FlattenData.Monomials.Select(m => new QuadraticMonomial<V>(
                            default(V)!.Minus(m.Coefficient), m.Symbol1, m.Symbol2)).ToList(),
                        default(V)!.Minus(FlattenData.Constant)),
                    Comparison.LE, Name, DisplayName),
                _ => this
            };
        }

        public override string ToString() =>
            string.IsNullOrEmpty(Name) ? $"[{Sign}] {FlattenData}" : $"{Name}: [{Sign}] {FlattenData}";
    }
}
