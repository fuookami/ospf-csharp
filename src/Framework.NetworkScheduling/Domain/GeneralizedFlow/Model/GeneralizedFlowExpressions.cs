// 已注册广义流弧表达式的只读视图 / Read-only view of registered generalized-flow arc expressions.
//
// 这一层只依赖 `LinearPolynomial`，不依赖元模型，因此不需要元模型层存在也能工作。
// This layer depends only on `LinearPolynomial`, not on the meta-model, so it can land before the Core meta-model layer exists.

using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Framework.NetworkScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Domain.GeneralizedFlow.Model;

/// <summary>
/// 已注册广义流弧表达式的只读视图 / Read-only view of registered generalized-flow arc expressions.
/// </summary>
/// <remarks>
/// **投入与产出分成两张表**：同一弧的投入以起点单位计、产出以终点单位计，两者量纲可能不同，混在一张表里就再也无法区分
/// "这条弧的输入表达式"与"它的输出表达式"，而业务扩展恰恰需要分别对它们加约束。
/// **Inputs and outputs live in two separate tables**: one arc's input is in origin units and its output in destination units, so the
/// two may have different dimensions. Merged into one table it becomes impossible to tell "this arc's input expression" from "its
/// output expression", and business extensions need to constrain them separately.
/// </remarks>
public sealed class GeneralizedFlowExpressions {
    private readonly IReadOnlyDictionary<NetworkArcId, LinearPolynomial<Flt64>> _inputExpressions;
    private readonly IReadOnlyDictionary<NetworkArcId, LinearPolynomial<Flt64>> _outputExpressions;

    /// <summary>
    /// 构造只读表达式视图 / Construct the read-only expression view.
    /// </summary>
    /// <param name="inputExpressions">起点单位下的弧投入表达式 / Arc input expressions in origin-node units.</param>
    /// <param name="outputExpressions">终点单位下的到达产出表达式 / Arrival-output expressions in destination-node units.</param>
    public GeneralizedFlowExpressions(
        IReadOnlyDictionary<NetworkArcId, LinearPolynomial<Flt64>> inputExpressions,
        IReadOnlyDictionary<NetworkArcId, LinearPolynomial<Flt64>> outputExpressions) {
        ArgumentNullException.ThrowIfNull(inputExpressions);
        ArgumentNullException.ThrowIfNull(outputExpressions);
        _inputExpressions = inputExpressions.ToDictionary(pair => pair.Key, pair => pair.Value);
        _outputExpressions = outputExpressions.ToDictionary(pair => pair.Key, pair => pair.Value);
    }

    /// <summary>
    /// 读取弧投入表达式 / Get an arc input expression.
    /// </summary>
    /// <param name="arcId">稳定弧标识 / Stable arc identity.</param>
    /// <returns>起点单位下的投入表达式或缺失错误 / The origin-unit input expression, or a missing-arc error.</returns>
    public Result<LinearPolynomial<Flt64>, ErrorCode, Error<ErrorCode>> Input(NetworkArcId arcId) {
        ArgumentNullException.ThrowIfNull(arcId);
        return _inputExpressions.TryGetValue(arcId, out LinearPolynomial<Flt64>? expression)
            ? Results.Ok(expression)
            : NetworkSchedulingErrors.Failure<LinearPolynomial<Flt64>>(
                $"广义流投入弧未注册：{arcId.Value} / Generalized-flow input arc is not registered: {arcId.Value}");
    }

    /// <summary>
    /// 读取弧到达产出表达式 / Get an arc arrival-output expression.
    /// </summary>
    /// <param name="arcId">稳定弧标识 / Stable arc identity.</param>
    /// <returns>终点单位下的产出表达式或缺失错误 / The destination-unit output expression, or a missing-arc error.</returns>
    public Result<LinearPolynomial<Flt64>, ErrorCode, Error<ErrorCode>> Output(NetworkArcId arcId) {
        ArgumentNullException.ThrowIfNull(arcId);
        return _outputExpressions.TryGetValue(arcId, out LinearPolynomial<Flt64>? expression)
            ? Results.Ok(expression)
            : NetworkSchedulingErrors.Failure<LinearPolynomial<Flt64>>(
                $"广义流产出弧未注册：{arcId.Value} / Generalized-flow output arc is not registered: {arcId.Value}");
    }
}
