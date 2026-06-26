#nullable enable

using Fuookami.Ospf.Core.Model.Intermediate;
using System.Collections.Generic;

namespace Fuookami.Ospf.Core.Model.Basic;

/// <summary>
/// 约束优先级扩展方法 / Constraint priority extension methods.
/// </summary>
public static class ConstraintPriorityExtensions {
    /// <summary>
    /// 计算线性模型视图中非空约束优先级数量。
    /// Count non-null constraint priorities in linear model view.
    /// </summary>
    /// <param name="modelView">线性三元模型视图 / Linear triad model view</param>
    /// <returns>非零约束优先级数量 / Number of non-zero constraint priorities</returns>
    public static int NonNullConstraintPriorityAmount(this ILinearTriadModelView modelView) {
        int count = 0;
        IReadOnlyList<int>? priorities = modelView.Constraints.Priorities;
        if (priorities is not null) {
            for (int i = 0; i < priorities.Count; i++) {
                if (priorities[i] != 0) {
                    count++;
                }
            }
        }
        return count;
    }

    /// <summary>
    /// 计算二次模型视图中非空约束优先级数量。
    /// Count non-null constraint priorities in quadratic model view.
    /// </summary>
    /// <param name="modelView">二次四元模型视图 / Quadratic tetrad model view</param>
    /// <returns>非零约束优先级数量 / Number of non-zero constraint priorities</returns>
    public static int NonNullConstraintPriorityAmount(this IQuadraticTetradModelView modelView) {
        int count = 0;
        IReadOnlyList<int>? priorities = modelView.Constraints.Priorities;
        if (priorities is not null) {
            for (int i = 0; i < priorities.Count; i++) {
                if (priorities[i] != 0) {
                    count++;
                }
            }
        }
        return count;
    }
}
