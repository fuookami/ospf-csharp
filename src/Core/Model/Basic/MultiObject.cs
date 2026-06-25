#nullable enable

using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;

namespace Fuookami.Ospf.Core.Model.Basic;
/// <summary>
/// 多目标位置（优先级+权重）/ Multi-objective location (priority + weight)
/// </summary>
/// <typeparam name="V">数值类型 / The number type</typeparam>
/// <param name="Priority">优先级 / Priority</param>
/// <param name="Weight">权重 / Weight</param>
public sealed record MultiObjectLocation<V>(UInt64 Priority, V Weight)
    where V : struct, IRealNumber<V>, INumberField<V>;
