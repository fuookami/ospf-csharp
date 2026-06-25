#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Symbol.Flatten;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Core.Model.Mechanism
{
    // ===== IMetaConstraintGroup =====

    /// <summary>
    /// 元约束组接口 / Meta constraint group interface.
    /// </summary>
    public interface IMetaConstraintGroup
    {
        bool Lazy { get; }
        string Name { get; }
    }

    // ===== MetaConstraintGroupExtensions =====

    /// <summary>
    /// IMetaConstraintGroup / IMetaModel DSL 扩展方法 /
    /// DSL extension methods on IMetaModel&lt;V&gt;.
    /// </summary>
    public static class MetaConstraintGroupExtensions
    {
        public static void RegisterConstraintGroup<V>(this IMetaModel<V> model, IMetaConstraintGroup group)
            where V : struct, IRealNumber<V>, INumberField<V>
        {
            model.RegisterConstraintGroup(group);
        }

        public static Range? IndicesOfConstraintGroup<V>(this IMetaModel<V> model, IMetaConstraintGroup group)
            where V : struct, IRealNumber<V>, INumberField<V>
        {
            return model.IndicesOfConstraintGroup(group);
        }

        public static IReadOnlyList<MathConstraint> ConstraintsOfGroup<V>(this IMetaModel<V> model, IMetaConstraintGroup group)
            where V : struct, IRealNumber<V>, INumberField<V>
        {
            return model.ConstraintsOfGroup(group);
        }

        // group-scoped AddConstraint overloads for linear

        public static Try AddConstraint<V>(
            this IAbstractLinearMetaModel<V> model,
            IMetaConstraintGroup group,
            IVariableItem constraint,
            bool? lazy = null,
            string? name = null,
            string? displayName = null,
            object? args = null,
            bool? withRangeSet = false)
            where V : struct, IRealNumber<V>, INumberField<V>
        {
            return model.AddConstraint(constraint, group, lazy ?? group.Lazy, name, displayName, args, withRangeSet);
        }

        public static Try AddConstraint<V>(
            this IAbstractLinearMetaModel<V> model,
            IMetaConstraintGroup group,
            LinearInequality<V> relation,
            bool? lazy = null,
            string? name = null,
            string? displayName = null,
            object? args = null,
            int? priority = null,
            bool? withRangeSet = false)
            where V : struct, IRealNumber<V>, INumberField<V>
        {
            return model.AddConstraint(relation, group, lazy ?? group.Lazy, name, displayName, args, priority, withRangeSet);
        }

        public static Try Partition<V>(
            this IAbstractLinearMetaModel<V> model,
            IMetaConstraintGroup group,
            LinearPolynomial<V> polynomial,
            bool? lazy = null,
            string? name = null,
            string? displayName = null,
            object? args = null)
            where V : struct, IRealNumber<V>, INumberField<V>
        {
            return model.Partition(polynomial, group, lazy ?? group.Lazy, name, displayName, args);
        }

        // group-scoped AddConstraint overloads for quadratic

        public static Try AddConstraint<V>(
            this IAbstractQuadraticMetaModel<V> model,
            IMetaConstraintGroup group,
            QuadraticInequalityOf<V> relation,
            bool? lazy = null,
            string? name = null,
            string? displayName = null,
            object? args = null,
            int? priority = null,
            bool? withRangeSet = null)
            where V : struct, IRealNumber<V>, INumberField<V>
        {
            return model.AddConstraint(relation, group, lazy ?? group.Lazy, name, displayName, args, priority, withRangeSet);
        }
    }

    // ===== MathConstraint =====

    /// <summary>
    /// 数学约束通用接口 / Common interface for math-based constraints.
    /// </summary>
    public interface MathConstraint
    {
        IMetaConstraintGroup? Group { get; }
        bool Lazy { get; }
        object? Args { get; }
        int Priority { get; }
        string Name { get; }
        string? DisplayName { get; }
    }

    // ===== LinearInequalityConstraint<V> =====

    /// <summary>
    /// 线性不等式约束 / Linear inequality constraint.
    /// </summary>
    public sealed record LinearInequalityConstraint<V> : MathConstraint
        where V : struct, IRealNumber<V>, INumberField<V>
    {
        public LinearInequality<V> Inequality { get; }
        public IMetaConstraintGroup? Group { get; }
        public bool Lazy { get; }
        public object? Args { get; }
        public int Priority { get; }
        public string Name { get; }
        public string? DisplayName { get; }
        public Comparison Sign => Inequality.Comparison;

        public LinearInequalityConstraint(
            LinearInequality<V> inequality,
            IMetaConstraintGroup? group = null,
            bool lazy = false,
            object? args = null,
            int priority = 0,
            string? name = null,
            string? displayName = null)
        {
            Inequality = inequality;
            Group = group;
            Lazy = lazy;
            Args = args;
            Priority = priority;
            Name = name ?? inequality.Name;
            DisplayName = displayName ?? inequality.DisplayName;
        }

        /// <summary>将内部线性不等式扁平化为 LinearFlattenData / Flatten to LinearFlattenData</summary>
        public Result<LinearFlattenData<V>, ErrorCode, Error<ErrorCode>> FlattenData()
        {
            return Inequality.ToLinearFlattenData<V>();
        }

        public override string ToString() => Inequality.ToString();
    }

    // ===== QuadraticInequalityConstraint<V> =====

    /// <summary>
    /// 二次不等式约束 / Quadratic inequality constraint.
    /// </summary>
    public sealed record QuadraticInequalityConstraint<V> : MathConstraint
        where V : struct, IRealNumber<V>, INumberField<V>
    {
        public QuadraticInequalityOf<V> Inequality { get; }
        public IMetaConstraintGroup? Group { get; }
        public bool Lazy { get; }
        public object? Args { get; }
        public int Priority { get; }
        public string Name { get; }
        public string? DisplayName { get; }
        public Comparison Sign => Inequality.Comparison;

        public QuadraticInequalityConstraint(
            QuadraticInequalityOf<V> inequality,
            IMetaConstraintGroup? group = null,
            bool lazy = false,
            object? args = null,
            int priority = 0,
            string? name = null,
            string? displayName = null)
        {
            Inequality = inequality;
            Group = group;
            Lazy = lazy;
            Args = args;
            Priority = priority;
            Name = name ?? inequality.Name;
            DisplayName = displayName ?? inequality.DisplayName;
        }

        public override string ToString() => Inequality.ToString();
    }

    // ===== QuadraticFlattenSubObject<V> =====

    /// <summary>
    /// 二次展平子目标 / Quadratic flatten sub-objective.
    /// </summary>
    public sealed record QuadraticFlattenSubObject<V>
        where V : struct, IRealNumber<V>, INumberField<V>
    {
        public ObjectCategory Category { get; }
        public string Name { get; }
        public QuadraticFlattenData<V> FlattenData { get; }

        public QuadraticFlattenSubObject(ObjectCategory category, string name, QuadraticFlattenData<V> flattenData)
        {
            Category = category;
            Name = name;
            FlattenData = flattenData;
        }

        public V Constant => FlattenData.Constant;
    }
}
