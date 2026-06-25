#nullable enable

using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;

namespace Fuookami.Ospf.Core.Model.Callback;
/// <summary>
/// 回调模型抽象接口 / Abstract call-back model interface.
/// </summary>
public interface IAbstractCallBackModelInterface<Obj, ObjValue, V> : IDisposable
    where V : struct, IRealNumber<V>, INumberField<V> {
    ObjValue DefaultObjective { get; }
    ObjectCategory ObjectCategory { get; }
    Fuookami.Ospf.Core.Token.IAbstractMutableTokenTable<V> Tokens { get; }
    IReadOnlyList<(Func<IReadOnlyList<V>, bool?> Extractor, string Name)> Constraints { get; }
    IReadOnlyList<(Func<IReadOnlyList<V>, Obj?> Extractor, string Name)> ObjectiveFunctions { get; }

    IReadOnlyList<IReadOnlyList<V>> InitialSolutions(ulong initialSolutionAmount = 1) =>
        Array.Empty<IReadOnlyList<V>>();

    ObjValue Operation(ObjValue lhs, ObjValue rhs);
    ObjValue ObjectiveValue();
    ObjValue ObjectiveValue(Obj obj);
    ObjValue? Objective(IReadOnlyList<V> solution);

    /// <summary>比较两个目标值（任一为 null 时另一个更优）/ Compare objective values (null = worst)</summary>
    Order? CompareObjective(ObjValue? lhs, ObjValue? rhs);

    bool? ConstraintSatisfied(IReadOnlyList<V> solution);
    void Flush();
}

/// <summary>
/// 单目标回调模型接口 / Single-objective call-back model interface (Obj = ObjValue = V).
/// </summary>
public interface ICallBackModelInterface<V> : IAbstractCallBackModelInterface<V, V, V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    IFlt64ValueConverter<V> Converter();
    V NegativeInfinity();
    V Infinity();
}

/// <summary>
/// 多目标回调模型接口 / Multi-objective call-back model interface.
/// </summary>
public interface IMultiObjectiveModelInterface<V>
    : IAbstractCallBackModelInterface<List<(MultiObjectLocation<V> Location, V Value)>, List<V>, V>
    where V : struct, IRealNumber<V>, INumberField<V> {
    IReadOnlyList<MultiObjectLocation<V>> ObjectiveLocation { get; }
    int ObjectiveSize => ObjectiveLocation.Count;
    IFlt64ValueConverter<V> Converter();
    V NegativeInfinity();
    V Infinity();
}
