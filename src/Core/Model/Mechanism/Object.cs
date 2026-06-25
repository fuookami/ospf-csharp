#nullable enable

using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Math.Algebra.Concept;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Fuookami.Ospf.Core.Model.Mechanism;
/// <summary>
/// 目标函数标记接口 / Objective function marker interface.
/// </summary>
public interface IObject {
}

/// <summary>
/// 单目标 / Single objective.
/// <para>封装单一优化目标，包含目标类别和子目标列表。</para>
/// <para>Encapsulates a single optimization objective with category and sub-objectives.</para>
/// </summary>
public sealed class SingleObject : IObject {
    /// <summary>目标类别（最大化/最小化）/ Objective category (maximum/minimum)</summary>
    public ObjectCategory Category { get; }
    /// <summary>子目标列表（类型为 SubObject&lt;V&gt; 的具体实例）/ Sub-objective list</summary>
    public IReadOnlyList<object> SubObjects { get; }

    public SingleObject(ObjectCategory category, IReadOnlyList<object> subObjects) {
        Category = category;
        SubObjects = subObjects;
    }

    /// <summary>
    /// 获取指定类型的子目标 / Get sub-objectives of the specified type.
    /// </summary>
    /// <typeparam name="V">数值类型 / Numeric type</typeparam>
    /// <returns>匹配的子目标列表 / Matching sub-objectives</returns>
    public IReadOnlyList<SubObject<V>> GetSubObjects<V>()
        where V : struct, IRealNumber<V>, INumberField<V> => SubObjects.OfType<SubObject<V>>().ToList();

    /// <inheritdoc/>
    public override string ToString() =>
        $"SingleObject[{Category}, subObjects={SubObjects.Count}]";
}

/// <summary>
/// 多目标（占位符）/ Multi-objective (placeholder).
/// </summary>
public sealed class MultiObject : IObject {
    /// <summary>单目标列表 / Single objective list</summary>
    public IReadOnlyList<SingleObject> Objectives { get; }

    public MultiObject(IReadOnlyList<SingleObject> objectives) {
        Objectives = objectives;
    }

    /// <inheritdoc/>
    public override string ToString() =>
        $"MultiObject[objectives={Objectives.Count}]";
}
