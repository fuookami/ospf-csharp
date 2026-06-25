#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Symbol.Flatten;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Symbol.Monomial;

namespace Fuookami.Ospf.Core.Model.Mechanism
{
    /// <summary>
    /// 子目标基类（抽象记录）/ Sub-objective base (abstract record).
    /// <para>封装优化目标中的子目标，包含类别、名称、求值与常量。</para>
    /// <para>Encapsulates a sub-objective within an optimization objective, with category,
    /// name, evaluation, and constant.</para>
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric type</typeparam>
    public abstract record SubObject<V>
        where V : struct, IRealNumber<V>, INumberField<V>
    {
        /// <summary>目标类别（最大化/最小化）/ Objective category (maximum/minimum)</summary>
        public ObjectCategory Category { get; init; }
        /// <summary>子目标名称 / Sub-objective name</summary>
        public string Name { get; init; } = "";

        /// <summary>获取单元格列表 / Get the cell list</summary>
        public abstract IReadOnlyList<object> Cells { get; }
        /// <summary>常量项 / Constant term</summary>
        public abstract V Constant { get; }
        /// <summary>求值（使用缓存结果）/ Evaluate (cached results)</summary>
        public abstract V? Evaluate();
        /// <summary>求值（使用解向量）/ Evaluate (solution vector)</summary>
        public abstract V? Evaluate(IReadOnlyList<V> solution);

        /// <inheritdoc/>
        public override string ToString() => $"{Name} [{Category}]";
    }

    /// <summary>
    /// 线性子目标（密封记录）/ Linear sub-objective (sealed record).
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric type</typeparam>
    public sealed record LinearSubObject<V> : SubObject<V>
        where V : struct, IRealNumber<V>, INumberField<V>
    {
        private readonly LinearFlattenData<V> _flattenData;

        /// <summary>线性单项式 / Linear monomials</summary>
        public IReadOnlyList<LinearMonomial<V>> LinearTerms() => _flattenData.Monomials;

        /// <inheritdoc/>
        public override IReadOnlyList<object> Cells { get; }

        /// <inheritdoc/>
        public override V Constant => _flattenData.Constant;

        private LinearSubObject(LinearFlattenData<V> flattenData, ObjectCategory category, string name)
        {
            _flattenData = flattenData;
            Category = category;
            Name = name;
            Cells = new List<object>(flattenData.Monomials);
        }

        /// <inheritdoc/>
        public override V? Evaluate()
        {
            V sum = _flattenData.Constant;
            foreach (var m in _flattenData.Monomials)
            {
                if (m.Symbol is Fuookami.Ospf.Core.Variable.IVariableItem)
                {
                    // Variable items require a solution vector; return null if no solution is available
                    return default;
                }
            }
            return sum;
        }

        /// <inheritdoc/>
        public override V? Evaluate(IReadOnlyList<V> solution)
        {
            V sum = _flattenData.Constant;
            foreach (var m in _flattenData.Monomials)
            {
                // For linear monomials with variable items, evaluation requires token-table resolution.
                // This simplified evaluation is a placeholder for the full token-table integration.
                sum = sum.Plus(m.Coefficient);
            }
            return sum;
        }

        /// <summary>
        /// 创建线性子目标 / Create a linear sub-objective.
        /// </summary>
        /// <param name="flattenData">展平数据 / Flatten data</param>
        /// <param name="category">目标类别 / Objective category</param>
        /// <param name="name">子目标名称 / Sub-objective name</param>
        public static LinearSubObject<V> Create(
            LinearFlattenData<V> flattenData,
            ObjectCategory category,
            string name = "")
        {
            return new LinearSubObject<V>(flattenData, category, name);
        }

        /// <inheritdoc/>
        public override string ToString() => $"Linear: {Name} [{Category}]";
    }

    /// <summary>
    /// 二次子目标（密封记录）/ Quadratic sub-objective (sealed record).
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric type</typeparam>
    public sealed record QuadraticSubObject<V> : SubObject<V>
        where V : struct, IRealNumber<V>, INumberField<V>
    {
        private readonly QuadraticFlattenData<V> _flattenData;

        /// <summary>二次单项式 / Quadratic monomials</summary>
        public IReadOnlyList<QuadraticMonomial<V>> QuadraticTerms() => _flattenData.Monomials;

        /// <inheritdoc/>
        public override IReadOnlyList<object> Cells { get; }

        /// <inheritdoc/>
        public override V Constant => _flattenData.Constant;

        private QuadraticSubObject(QuadraticFlattenData<V> flattenData, ObjectCategory category, string name)
        {
            _flattenData = flattenData;
            Category = category;
            Name = name;
            Cells = new List<object>(flattenData.Monomials);
        }

        /// <inheritdoc/>
        public override V? Evaluate()
        {
            V sum = _flattenData.Constant;
            foreach (var m in _flattenData.Monomials)
            {
                if (m.Symbol1 is Fuookami.Ospf.Core.Variable.IVariableItem)
                {
                    return default;
                }
            }
            return sum;
        }

        /// <inheritdoc/>
        public override V? Evaluate(IReadOnlyList<V> solution)
        {
            V sum = _flattenData.Constant;
            foreach (var m in _flattenData.Monomials)
            {
                // Simplified evaluation placeholder for token-table integration
                sum = sum.Plus(m.Coefficient);
            }
            return sum;
        }

        /// <summary>
        /// 创建二次子目标 / Create a quadratic sub-objective.
        /// </summary>
        /// <param name="flattenData">展平数据 / Flatten data</param>
        /// <param name="category">目标类别 / Objective category</param>
        /// <param name="name">子目标名称 / Sub-objective name</param>
        public static QuadraticSubObject<V> Create(
            QuadraticFlattenData<V> flattenData,
            ObjectCategory category,
            string name = "")
        {
            return new QuadraticSubObject<V>(flattenData, category, name);
        }

        /// <inheritdoc/>
        public override string ToString() => $"Quadratic: {Name} [{Category}]";
    }
}
