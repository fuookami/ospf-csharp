#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Variable;
using UInt64 = Fuookami.Ospf.Math.Algebra.Number.UInt64;
using Fuookami.Ospf.Framework.Bpp2d.Domain.Item.Model;
using Fuookami.Ospf.MultiArray;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.Bpp2d.Domain.LayerAssignment.Model;

/// <summary>
/// 精确层分配 / The precise layer assignment.
/// </summary>
/// <remarks>
/// **这是列生成最终阶段的地基**：
/// 每个候选层一个整数变量 `y_i`（用几份），上下界由需求估出的最大份数给出。
///
/// | 原成员 | 本仓 | 说明 |
/// | --- | --- | --- |
/// | `y: UIntVariable1` | <see cref="Y"/> | 每层一个份数变量，上界 `max(1, maxCopies[i])` |
/// | `v: LinearIntermediateSymbols1` | **未落地** | 二值化适配器；本仓没有 `BinaryzationFunction` 的对应物 |
/// | `tail: BinVariable1` | **未落地** | 尾箱变量；本仓没有 `BinVariable1` 的 bpp2d 对应物 |
///
/// **`v` 与 `tail` 缺位如实记档**：它们是 `SheetCapacityConstraint` 与 `TailBinAssignmentConstraint` 的输入，
/// 那两条约束在 bpp2d 也还没落地，所以此处不伪造一个"看起来对"的二值化符号。**缺什么写什么**。
///
/// 两处必须沿用的细节：
///
/// 1. **上界至少为 1**：`maxCopies[i]` 可能是 0（需求算出来"这一层用不上"），但份数变量上界为 0 会让该层被强制不用，
///    而列生成恰恰要靠它判断"这一层到底用不用"。写法是 `if (upper &lt; 1) 1 else upper`。
/// 2. **`Register` 幂等**：写法是"已初始化就跳过创建，但仍调 `model.add`"，所以第二次注册**复用同一个变量对象**。
///    实测 `model.Add(variable)` 对重复注册是**容忍的**（与 `ImpreciseAssignment` 的符号注册不同，那里会因重名失败）——
///    这一点是实测出来的，不是推断。
///
/// **The foundation of the column-generation final phase**: one integer variable `y_i` per candidate layer (how many copies), bounded by the
/// maximum copy count estimated from demand. **`v` and `tail` are missing and recorded as such**: they are inputs to `SheetCapacityConstraint` and `TailBinAssignmentConstraint`, which are also
/// not implemented to bpp2d yet, so no plausible-looking binarisation symbol is fabricated here. Two details must be copied: **the upper bound is at least 1**, since `maxCopies[i]` can be 0 and a
/// bound of 0 would force the layer unused while column generation needs the variable to decide exactly that; and **`Register` is idempotent** in its specific sense — it skips
/// creation when already initialised but still calls `model.add`, so a second registration **reuses the same variable object**. Measured: `model.Add(variable)` **tolerates** a
/// duplicate registration, unlike the symbol registration in `ImpreciseAssignment`, which fails on a duplicate name.
/// </remarks>
public sealed class PreciseAssignment
{
    private readonly IReadOnlyList<Layer<FltX>> _layers;
    private readonly IReadOnlyList<ulong> _maxCopies;

    /// <summary>
    /// 构造精确层分配 / Construct the precise layer assignment.
    /// </summary>
    /// <param name="layers">候选层 / The candidate layers.</param>
    /// <param name="maxCopies">每层最大份数 / The maximum copies per layer.</param>
    public PreciseAssignment(
        IReadOnlyList<Layer<FltX>> layers,
        IReadOnlyList<ulong> maxCopies)
    {
        ArgumentNullException.ThrowIfNull(layers);
        ArgumentNullException.ThrowIfNull(maxCopies);
        _layers = layers;
        _maxCopies = maxCopies;
    }

    /// <summary>候选层 / The candidate layers</summary>
    public IReadOnlyList<Layer<FltX>> Layers => _layers;

    /// <summary>每层最大份数 / The maximum copies per layer</summary>
    public IReadOnlyList<ulong> MaxCopies => _maxCopies;

    /// <summary>份数变量，注册前为 null / The copy-count variable, null before registration</summary>
    public UIntVariable1? Y { get; private set; }

    /// <summary>
    /// 第 i 层的份数变量 / The copy-count variable of layer i.
    /// </summary>
    /// <param name="layerIndex">层序号 / The layer index.</param>
    /// <returns>变量，未注册或越界时为 null / The variable, null before registration or out of range.</returns>
    public IVariableItem? YOf(int layerIndex) =>
        Y is null || layerIndex < 0 || layerIndex >= _layers.Count ? null : Y[layerIndex];

    /// <summary>
    /// 注册份数变量 / Register the copy-count variable.
    /// </summary>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>执行结果 / Execution result.</returns>
    public Try Register(LinearMetaModel<Flt64> model)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (Y is null)
        {
            Y = new UIntVariable1("y", Shape1.Invoke(_layers.Count));
            for (int index = 0; index < _layers.Count; ++index)
            {
                // **上界至少为 1**：0 会让该层被强制不用，而列生成正要用它判断"用不用"。
                // **The upper bound is at least 1**: zero would force the layer unused, which is the very decision column generation needs.
                ulong upper = index < _maxCopies.Count ? _maxCopies[index] : 1UL;
                if (upper < 1UL)
                {
                    upper = 1UL;
                }
                Y[index].Range.Leq(new UInt64(upper));
            }
        }
        return model.Add(Y);
    }
}
