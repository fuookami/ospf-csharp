#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.LoadingEffectiveness.Model;

/// <summary>
/// 建议装载松弛 / The advised-load slack.
/// </summary>
/// <remarks>
/// 与对齐：每个舱位两个**单侧松弛**符号，衡量"实际装载超出建议值多少"。
///
/// 五处必须沿用：
///
/// 1. **用 `SlackFunction` 的阈值形式**：基线经 `exampleThresholdSlack` 转成
///    `SlackFunction(x = 装载量, y = 阈值, withNegative = false, withPositive = true, threshold = true, constraint = true)`。
///    **`withNegative = false`** 是关键：带下侧会让松弛在装载量低于建议值时也变大，惩罚方向就反了。
/// 2. **阈值为 `null` 的舱位给恒零符号**，不是跳过——矩阵维度必须与舱位数一致。
/// 3. **数量松弛的被松弛量是 `Load.LoadAmount`**，重量松弛的是 `Load.EstimateLoadWeight`。
/// 4. **两族符号名分别是 `advice_amount_slack_{position}` 与 `advice_weight_slack_{position}`**，
///    依赖 `StowagePosition.ToString` 覆写（补上），否则全部重名。
/// 5. 只包一层 `LinearFunctionSymbolAdapter`，因为抽象模型只接受中间符号。
///
/// **一处刻意的单位简化**：被松弛量与阈值都换算到 `aircraftModel.weightUnit` 再相减；本仓要求调用方
/// **用同一单位**给出 `Alw` 与 `EstimateLoadWeight`。两种做法只差一个**正比例因子**，而该因子会被目标系数
/// `parameter.AdviceLoadWeight` 吸收，所以行为等价；换成"在符号里做换算"会引入一个没有任何消费者的单位路径。
///
/// Two **one-sided** slack symbols per position, measuring how far the actual load exceeds the advised value. Five things must be copied: **the threshold form of
/// `SlackFunction`** — the code goes through `exampleThresholdSlack`, which becomes `SlackFunction(x = load, y = threshold, withNegative = false, withPositive = true, threshold = true,
/// constraint = true)`, and **`withNegative = false`** is the key, since a lower side would grow the slack when the load is *below* the advice and invert the penalty; **a position with a null
/// threshold gets a constant-zero symbol** rather than being skipped, since the matrix must match the position count; **the amount slack relaxes `Load.LoadAmount` and the weight slack
/// `Load.EstimateLoadWeight`**; **the two families are named `advice_amount_slack_{position}` and `advice_weight_slack_{position}`**, relying on the `StowagePosition.ToString` override
/// Added
///
/// **One deliberate unit simplification**: the code converts both the relaxed quantity and the threshold into `aircraftModel.weightUnit` before subtracting, while this implementation requires the
/// caller to supply `Alw` and `EstimateLoadWeight` **in the same unit**. The two differ only by a **positive scale factor**, which the objective coefficient
/// `parameter.AdviceLoadWeight` absorbs, so behaviour is equivalent; doing the conversion inside the symbol would introduce a unit path with no consumer.
/// </remarks>
public sealed class AdviceLoading
{
    private readonly IReadOnlyList<StowagePosition> _positions;
    private readonly Load _load;
    private readonly Dictionary<int, ILinearIntermediateSymbol<Flt64>> _amountSlack = new();
    private readonly Dictionary<int, ILinearIntermediateSymbol<Flt64>> _weightSlack = new();

    /// <summary>
    /// 构造建议装载松弛 / Construct the advised-load slack.
    /// </summary>
    /// <param name="positions">舱位 / The positions.</param>
    /// <param name="load">装载模型 / The load model.</param>
    public AdviceLoading(IReadOnlyList<StowagePosition> positions, Load load)
    {
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentNullException.ThrowIfNull(load);
        _positions = positions;
        _load = load;
    }

    /// <summary>
    /// 取数量松弛 / Fetch an amount slack.
    /// </summary>
    /// <param name="positionIndex">舱位下标 / The position index.</param>
    /// <returns>松弛符号 / The slack symbol.</returns>
    public ILinearIntermediateSymbol<Flt64>? AmountSlack(int positionIndex) =>
        _amountSlack.TryGetValue(positionIndex, out ILinearIntermediateSymbol<Flt64>? symbol) ? symbol : null;

    /// <summary>
    /// 取重量松弛 / Fetch a weight slack.
    /// </summary>
    /// <param name="positionIndex">舱位下标 / The position index.</param>
    /// <returns>松弛符号 / The slack symbol.</returns>
    public ILinearIntermediateSymbol<Flt64>? WeightSlack(int positionIndex) =>
        _weightSlack.TryGetValue(positionIndex, out ILinearIntermediateSymbol<Flt64>? symbol) ? symbol : null;

    /// <summary>
    /// 注册到模型 / Register with the model.
    /// </summary>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>操作结果 / Operation result.</returns>
    public Try Register(LinearMetaModel<Flt64> model)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (_amountSlack.Count > 0)
        {
            return Results.Ok<Success>(Results.SuccessInstance);
        }

        for (int j = 0; j < _positions.Count; ++j)
        {
            StowagePosition position = _positions[j];
            string amountName = "advice_amount_slack_" + position;
            ILinearIntermediateSymbol<Flt64> amount = position.Ala is { } ala
                ? BuildSlack(_load.LoadAmountSymbol(j), ala, amountName)
                : new LinearExpressionSymbol(new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero), name: amountName);
            Try addedAmount = model.Add(amount);
            if (!addedAmount.IsOk)
            {
                return addedAmount;
            }
            _amountSlack[j] = amount;

            string weightName = "advice_weight_slack_" + position;
            ILinearIntermediateSymbol<Flt64> weight = position.Alw is { } alw
                ? BuildSlack(_load.EstimateLoadWeightSymbol(j), alw, weightName)
                : new LinearExpressionSymbol(new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero), name: weightName);
            Try addedWeight = model.Add(weight);
            if (!addedWeight.IsOk)
            {
                return addedWeight;
            }
            _weightSlack[j] = weight;
        }
        return Results.Ok<Success>(Results.SuccessInstance);
    }

    /// <summary>
    /// 造一个单侧阈值松弛 / Build one one-sided threshold slack.
    /// </summary>
    /// <param name="relaxed">被松弛量 / The relaxed quantity.</param>
    /// <param name="threshold">阈值 / The threshold.</param>
    /// <param name="name">符号名 / The symbol name.</param>
    /// <returns>松弛符号 / The slack symbol.</returns>
    private static ILinearIntermediateSymbol<Flt64> BuildSlack(
        LinearExpressionSymbol? relaxed,
        double threshold,
        string name)
    {
        // 被松弛量缺失时给恒零——与"阈值为 `null`"同样处理，保持矩阵完整。
        // A missing relaxed quantity also yields zero, matching the null-threshold case and keeping the matrix complete.
        if (relaxed is null)
        {
            return new LinearExpressionSymbol(
                new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero), name: name);
        }
        // **`withNegative = false`**：只有上侧，惩罚"超出建议值"。
        // **`withNegative = false`**: upper side only, so the penalty is for exceeding the advice.
        var slack = new SlackFunction<Flt64>(
            x: relaxed.Polynomial,
            y: new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), new Flt64(threshold)),
            converter: IIntoValue<Flt64>.Identity,
            withNegative: false,
            withPositive: true,
            threshold: true,
            constraint: true,
            name: name);
        return new LinearFunctionSymbolAdapter<Flt64>(slack);
    }
}
