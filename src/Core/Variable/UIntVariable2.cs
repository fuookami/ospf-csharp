#nullable enable

using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.MultiArray;

namespace Fuookami.Ospf.Core.Variable;

/// <summary>
/// 二维无符号整数变量组合 / Two-dimensional unsigned-integer variable combination.
/// </summary>
/// <remarks>
///
/// **之前它不存在**，而它的一维兄弟 `UIntVariable1`、以及 `BinVariable1/2/3`、`TerVariable1/2`、`IntVariable1`
/// 都在——这是逐个补齐时漏掉的一个。它挡住的不只是自己：bpp3d 层分配的 `PreciseAssignment` 用 `UIntVariable2`
/// 建"箱 × 层"的装载量矩阵，没有它，`BinLoadingOrderConstraint` 与 `TailBinAssignmentConstraint` 两条约束都无处落脚。
///
/// 构造参数保持与兄弟类型同一形态：`(name, d1, d2)`，而不是 `Shape2`——`UIntVariable1` 用的是 `Shape1`，但那是它自己
/// 的历史形态；二维及以上在 `Variable2`/`Variable3` 里统一收两个/三个长度，本类型跟随后者，避免同一族里两种签名并存。
///
/// `UIntVariable2` is one-to-one with its own type. **It did not exist at first**, while its one-dimensional sibling `UIntVariable1` and the `BinVariable1/2/3`, `TerVariable1/2`
/// and `IntVariable1` families all did — an omission from implementation the family piece by piece. It blocks more than itself: bpp3d layer assignment's `PreciseAssignment` builds the
/// (bin × layer) load matrix with `UIntVariable2`, without which neither `BinLoadingOrderConstraint` nor `TailBinAssignmentConstraint` has anywhere to land. The constructor keeps
/// the `(name, d1, d2)` shape of the `Variable2`/`Variable3` siblings rather than `UIntVariable1`'s `Shape1`, so one family does not carry two signatures.
/// </remarks>
public sealed class UIntVariable2 : VariableCombination<UInt64, UInteger, Shape2> {
    /// <summary>
    /// 创建二维无符号整数变量组合 / Create a two-dimensional unsigned-integer variable combination.
    /// </summary>
    /// <param name="name">变量名称 / Variable name.</param>
    /// <param name="d1">第一维度长度 / Length of the first dimension.</param>
    /// <param name="d2">第二维度长度 / Length of the second dimension.</param>
    public UIntVariable2(string name, int d1, int d2)
        : base(UInteger.Instance, name, NumericConstantsRegistry.For<UInt64>(), Shape2.Invoke(d1, d2)) {
    }
}
