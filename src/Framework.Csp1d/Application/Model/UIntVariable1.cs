#nullable enable

using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.MultiArray;
using System.Collections;
using System.Collections.Generic;

namespace Fuookami.Ospf.Framework.Csp1d.Application.Model;
/// <summary>
/// 一维无符号整数变量组合 / 1D unsigned integer variable combination.
///
/// 封装一组 UIntVar 决策变量，支持按索引访问和注册到元模型。
/// Wraps a set of UIntVar decision variables, supporting index access and registration to meta model.
/// </summary>
public sealed class UIntVariable1 : VariableCombination<UInt64, UInteger, Shape1>, IEnumerable<IVariableItem> {
    /// <summary>
    /// 构造一维无符号整数变量组合 / Construct 1D unsigned integer variable combination.
    /// </summary>
    /// <param name="name">变量名称前缀 / Variable name prefix.</param>
    /// <param name="shape">一维形状 / 1D shape.</param>
    public UIntVariable1(string name, Shape1 shape)
        : base(UInteger.Instance, name, NumericConstantsRegistry.For<UInt64>(), shape) {
    }

    /// <summary>
    /// 获取枚举器 / Get enumerator.
    /// </summary>
    public IEnumerator<IVariableItem> GetEnumerator() {
        for (int i = 0; i < Items.Length; i++) {
            yield return Items[i];
        }
    }

    /// <inheritdoc/>
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
