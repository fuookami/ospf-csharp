#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;

namespace Fuookami.Ospf.Core.Model.Basic;

/// <summary>
/// 模型接口，表示一个可求解的优化模型。
/// Model interface, representing a solvable optimization model.
/// </summary>
/// <typeparam name="V">数值类型 / The number type</typeparam>
public interface IModel<V> where V : struct, IRealNumber<V>, INumberField<V> {
    /// <summary>模型名称 / Model name</summary>
    string Name { get; }
}

/// <summary>
/// 线性模型接口，表示仅包含线性约束和目标的优化模型。
/// Linear model interface, representing an optimization model with only linear constraints and objectives.
/// </summary>
/// <typeparam name="V">数值类型 / The number type</typeparam>
public interface ILinearModel<V> : IModel<V> where V : struct, IRealNumber<V>, INumberField<V> { }

/// <summary>
/// 二次模型接口，表示包含二次约束或目标的优化模型。
/// Quadratic model interface, representing an optimization model with quadratic constraints or objectives.
/// </summary>
/// <typeparam name="V">数值类型 / The number type</typeparam>
public interface IQuadraticModel<V> : IModel<V> where V : struct, IRealNumber<V>, INumberField<V> { }
