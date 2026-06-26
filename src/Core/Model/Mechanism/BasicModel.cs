#nullable enable

using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;

namespace Fuookami.Ospf.Core.Model.Mechanism;

/// <summary>
/// 基础模型抽象类，提供模型名称管理。
/// Basic model abstract class providing model name management.
/// </summary>
/// <typeparam name="V">数值类型 / The number type</typeparam>
public abstract class BasicModel<V> : IModel<V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    /// <summary>
    /// 模型名称 / Model name.
    /// </summary>
    public string Name { get; protected set; }

    /// <summary>
    /// 创建基础模型实例 / Create basic model instance.
    /// </summary>
    /// <param name="name">模型名称 / Model name</param>
    protected BasicModel(string name = "") {
        Name = name;
    }

    /// <summary>
    /// 返回模型名称的字符串表示 / Return string representation of model name.
    /// </summary>
    public override string ToString() => Name;
}
