#nullable enable

using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.Math.Algebra.Law;
/// <summary>
/// 环定律验证器 / Ring law validator.
/// 继承 GroupLaw 的加法群验证，增加乘法结合律、乘法单位元、分配律。
/// Inherits GroupLaw for additive group verification, adds multiplicative associativity,
/// multiplicative identity, and distributivity.
/// </summary>
/// <typeparam name="TSelf">环元素类型 / Ring element type.</typeparam>
public sealed class RingLaw<TSelf> {
    private readonly GroupLaw<TSelf> _additiveGroup;
    private readonly IReadOnlyCollection<TSelf> _samples;
    private readonly Func<TSelf, TSelf, TSelf> _add;
    private readonly Func<TSelf, TSelf, TSelf> _mul;
    private readonly TSelf _one;
    private readonly Func<TSelf, TSelf, bool> _equal;

    public RingLaw(
        IReadOnlyCollection<TSelf> samples,
        Func<TSelf, TSelf, TSelf> add,
        Func<TSelf, TSelf, TSelf> mul,
        TSelf zero,
        TSelf one,
        Func<TSelf, TSelf> negate,
        Func<TSelf, TSelf, bool> equal) {
        _samples = samples;
        _add = add;
        _mul = mul;
        _one = one;
        _equal = equal;
        _additiveGroup = new GroupLaw<TSelf>(samples, add, zero, negate, equal);
    }

    /// <summary>验证加法交换律 / Verify additive commutativity.</summary>
    public bool CheckAdditiveCommutative() {
        foreach (TSelf? lhs in _samples) {
            foreach (TSelf? rhs in _samples) {
                if (!_equal(_add(lhs, rhs), _add(rhs, lhs))) {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>验证乘法结合律 / Verify multiplicative associativity.</summary>
    public bool CheckMultiplicativeAssociative() {
        foreach (TSelf? lhs in _samples) {
            foreach (TSelf? mid in _samples) {
                foreach (TSelf? rhs in _samples) {
                    if (!_equal(_mul(_mul(lhs, mid), rhs), _mul(lhs, _mul(mid, rhs)))) {
                        return false;
                    }
                }
            }
        }

        return true;
    }

    /// <summary>验证乘法单位元存在性 / Verify multiplicative identity existence.</summary>
    public bool CheckMultiplicativeIdentity() {
        foreach (TSelf? v in _samples) {
            if (!_equal(_mul(v, _one), v)) {
                return false;
            }

            if (!_equal(_mul(_one, v), v)) {
                return false;
            }
        }
        return true;
    }

    /// <summary>验证分配律 / Verify distributivity.</summary>
    public bool CheckDistributive() {
        foreach (TSelf? lhs in _samples) {
            foreach (TSelf? mid in _samples) {
                foreach (TSelf? rhs in _samples) {
                    if (!_equal(_mul(lhs, _add(mid, rhs)), _add(_mul(lhs, mid), _mul(lhs, rhs)))) {
                        return false;
                    }

                    if (!_equal(_mul(_add(lhs, mid), rhs), _add(_mul(lhs, rhs), _mul(mid, rhs)))) {
                        return false;
                    }
                }
            }
        }

        return true;
    }

    /// <summary>验证所有环定律（含群定律）/ Validate all ring laws (including group laws).</summary>
    public bool Validate() =>
        _additiveGroup.Validate() && CheckAdditiveCommutative()
        && CheckMultiplicativeAssociative() && CheckMultiplicativeIdentity() && CheckDistributive();
}
