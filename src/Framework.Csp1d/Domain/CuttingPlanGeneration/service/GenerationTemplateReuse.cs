#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Model;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Service
{
    /// <summary>
    /// 切片模板复用资格检查 / Slice template reuse eligibility check.
    /// </summary>
    internal static class GenerationTemplateReuse
    {
        /// <summary>
        /// 检查约束是否允许切片模板复用 / Check whether constraints allow slice template reuse.
        ///
        /// 只有标准约束类型（MaxKnifeCount、MinKnifeCount、MaxOverProduceLength、WidthUpperBound）
        /// 才允许模板复用；自定义约束会禁用模板复用。
        /// Only standard constraint types allow template reuse; custom constraints disable it.
        /// </summary>
        /// <typeparam name="V">数值类型 / Numeric value type.</typeparam>
        /// <param name="constraints">约束列表 / Constraint list.</param>
        /// <returns>true 如果允许模板复用 / true if template reuse is allowed.</returns>
        public static bool CanReuseMaterialSliceTemplates<V>(
            IReadOnlyList<ICuttingPlanConstraint<V>> constraints) where V : struct, IComparable<V>
        {
            foreach (var constraint in constraints)
            {
                if (constraint is not MaxKnifeCountConstraint<V> &&
                    constraint is not MinKnifeCountConstraint<V> &&
                    constraint is not MaxOverProduceLengthConstraint<V> &&
                    constraint is not WidthUpperBoundConstraint<V>)
                {
                    return false;
                }
            }
            return true;
        }
    }

    /// <summary>
    /// 切片模板记录器 / Slice template recorder.
    /// </summary>
    /// <typeparam name="T">切片类型 / Slice type.</typeparam>
    internal sealed class GenerationSliceTemplateRecorder<T>
    {
        private readonly List<IReadOnlyList<T>> _recordedTemplates = new();

        /// <summary>已记录的模板列表 / Recorded template list.</summary>
        public IReadOnlyList<IReadOnlyList<T>> Templates => _recordedTemplates;

        /// <summary>
        /// 记录切片模板 / Record a slice template.
        /// </summary>
        /// <param name="slices">切片列表 / Slice list.</param>
        public void Record(IReadOnlyList<T> slices)
        {
            _recordedTemplates.Add(new List<T>(slices));
        }
    }
}
