#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;

namespace Fuookami.Ospf.Framework.Bpp3d.Domain.LayerAssignment.Service.Limits;

/// <summary>
/// BPP3D 目标接口 / BPP3D objective interface.
/// 列生成管线中的目标函数组件 / Objective function component in the CG pipeline.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type</typeparam>
public interface IBpp3dObjective<V> where V : struct, IFloatingNumber<V> {
    /// <summary>目标名称 / Objective name.</summary>
    string Name { get; }
}
