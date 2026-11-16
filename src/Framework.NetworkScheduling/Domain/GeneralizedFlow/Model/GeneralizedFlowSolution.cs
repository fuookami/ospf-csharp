// 广义流的解与 solver 容差 / Generalized-flow solution and solver tolerances.
// 与固定 Kotlin （2714 字节）、（1335 字节）对应。
// And.

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Framework.NetworkScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Domain.GeneralizedFlow.Model;

/// <summary>
/// 单弧投入、产出与计费结果 / Per-arc input, output and tariff result.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <param name="Input">起点投入量，容量与资费按此计 / Origin input; capacity and tariff use this amount.</param>
/// <param name="UnitConvertedOutput">仅应用单位换算、产率取一时的产出 / Output after unit conversion only, with yield set to one.</param>
/// <param name="Output">应用单位换算及业务产率后的到达产出 / Arrival output after unit conversion and business yield.</param>
/// <param name="LossOrGain">正值为损耗、负值为增益，按终点单位计 / Positive is loss, negative is gain, in destination units.</param>
/// <param name="Cost">按投入量计费的弧费用 / Arc tariff charged on the input.</param>
public sealed record GeneralizedFlowArcResult<V>(
    Quantity<V> Input,
    Quantity<V> UnitConvertedOutput,
    Quantity<V> Output,
    Quantity<V> LossOrGain,
    Quantity<V> Cost) where V : struct, IRealNumber<V>;

/// <summary>
/// 可复核的节点广义守恒结果 / Auditable generalized node-balance result.
/// </summary>
/// <remarks>
/// **`Residual` 是这套结构存在的理由**：它把"实际净供需减声明净供需"显式摆出来，应当为零。若省略它，节点平衡算错时
/// 解里只剩一对看起来都正常的数字，审计只能靠重算才发现问题。
/// **`Residual` is the reason this structure exists**: it surfaces "realised minus declared net supply", which should be zero.
/// Omit it and a mis-computed balance leaves only a pair of plausible-looking numbers in the solution, discoverable only by
/// recomputation.
/// </remarks>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <param name="DeclaredNetSupply">节点声明净供需 / Declared node net supply.</param>
/// <param name="OutgoingInput">流出弧投入量之和 / Sum of outgoing arc inputs.</param>
/// <param name="IncomingOutput">流入弧到达产出之和 / Sum of incoming arc outputs.</param>
/// <param name="RealizedNetSupply">流出投入减流入产出 / Outgoing input minus incoming output.</param>
/// <param name="Residual">实际净供需减声明净供需，应为零 / Realised minus declared net supply; should be zero.</param>
public sealed record GeneralizedFlowNodeResult<V>(
    Quantity<V> DeclaredNetSupply,
    Quantity<V> OutgoingInput,
    Quantity<V> IncomingOutput,
    Quantity<V> RealizedNetSupply,
    Quantity<V> Residual) where V : struct, IRealNumber<V>;

/// <summary>
/// 广义流的领域解 / The generalized-flow domain solution.
/// </summary>
/// <typeparam name="V">数值类型 / Numeric type.</typeparam>
/// <param name="Arcs">按稳定弧标识索引的结果 / Results indexed by stable arc identity.</param>
/// <param name="Nodes">按稳定节点标识索引的结果 / Results indexed by stable node identity.</param>
/// <param name="TotalCost">总费用 / Total cost.</param>
public sealed record GeneralizedFlowSolution<V>(
    IReadOnlyDictionary<NetworkArcId, GeneralizedFlowArcResult<V>> Arcs,
    IReadOnlyDictionary<NetworkNodeId, GeneralizedFlowNodeResult<V>> Nodes,
    Quantity<V> TotalCost) where V : struct, IRealNumber<V>;

/// <summary>
/// Solver 边界的数值可行性容差，不属于领域物理量 / Solver-boundary feasibility tolerances; not domain quantities.
/// </summary>
/// <remarks>
/// **容差随比较值的规模放大**（绝对容差 + 相对容差 × 量级）。只用绝对容差时，一个以百万为单位的流量残差永远"超差"，而一个
/// 以千分之一为单位的残差永远"合格"——同一套容差在两种量级下给出相反的结论。
/// **The tolerance scales with the magnitude of the compared values** (absolute + relative × scale). With an absolute tolerance
/// alone, a residual measured in millions always fails while one measured in thousandths always passes — the same tolerance
/// reaching opposite conclusions at two magnitudes.
/// </remarks>
/// <param name="AbsoluteTolerance">归一化 solver 值的绝对容差 / Absolute tolerance in normalised solver values.</param>
/// <param name="RelativeTolerance">按比较值规模放大的相对容差 / Relative tolerance scaled by the compared values.</param>
public sealed record GeneralizedFlowSolverTolerance(
    Flt64 AbsoluteTolerance,
    Flt64 RelativeTolerance) {
    /// <summary>
    /// 使用基线默认值（绝对、相对各 1e-7）/ Use the code defaults (1e-7 absolute and relative).
    /// </summary>
    public GeneralizedFlowSolverTolerance() : this(new Flt64(1e-7), new Flt64(1e-7)) {
    }

    /// <summary>
    /// 容差配置是否合法 / Whether the tolerance configuration is valid.
    /// </summary>
    /// <remarks>
    /// 非有限或负的容差会让所有可行性判定失去意义（负数意味着永远超差，NaN 意味着永远不合格却也不报错），因此在求解前拒绝。
    /// Non-finite or negative tolerances make every feasibility decision meaningless (a negative value always fails, NaN always
    /// fails without reporting), so they are rejected before solving.
    /// </remarks>
    /// <returns>是否合法 / Whether it is valid.</returns>
    public bool IsValid() =>
        Finite(AbsoluteTolerance) && Finite(RelativeTolerance)
        && AbsoluteTolerance.CompareTo(Flt64.Zero) >= 0
        && RelativeTolerance.CompareTo(Flt64.Zero) >= 0;

    /// <summary>
    /// 为给定比较值计算容差 / Compute the tolerance for the given compared values.
    /// </summary>
    /// <param name="values">参与比较的值 / The values being compared.</param>
    /// <returns>容差，任一值非有限时为 null / The tolerance, or null when any value is non-finite.</returns>
    public Flt64? ForValues(IReadOnlyList<Flt64> values) {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Any(value => !Finite(value))) {
            return null;
        }
        Flt64 scale = Flt64.Zero;
        foreach (Flt64 value in values) {
            Flt64 magnitude = value.Abs();
            if (magnitude.CompareTo(scale) > 0) {
                scale = magnitude;
            }
        }
        Flt64 tolerance = AbsoluteTolerance.Plus(RelativeTolerance.Times(scale));
        return Finite(tolerance) ? tolerance : null;
    }

    /// <summary>
    /// 判断是否为有限值 / Whether the value is finite.
    /// </summary>
    /// <remarks>
    /// `IRealNumber&lt;T&gt;.IsFinite` 是**默认接口成员**，C# 不允许在实现类实例上直接调用它，必须经接口类型调用。
    /// `IRealNumber&lt;T&gt;.IsFinite` is a **default interface member**; C# will not let it be called on the implementing class
    /// instance, so it must be reached through the interface type.
    /// </remarks>
    /// <param name="value">待判断的值 / The value to check.</param>
    /// <returns>是否有限 / Whether it is finite.</returns>
    private static bool Finite(Flt64 value) => ((IRealNumber<Flt64>)value).IsFinite();
}
