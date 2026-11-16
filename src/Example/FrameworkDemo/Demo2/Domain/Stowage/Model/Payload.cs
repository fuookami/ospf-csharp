// 业载：主舱/下舱/总业载的推荐与实际两族 / Payload: recommended and actual families for the main deck, lower hold and total.

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;

/// <summary>
/// 业载模型 / The payload model.
/// </summary>
/// <remarks>
/// 逐条对齐：六族符号（主舱/下舱/总业载 × 推荐/实际），
/// 且**两种装载模式下取重口径不同**：
///
/// | 模式 | 推荐业载取自 | 实际业载取自 |
/// | --- | --- | --- |
/// | `FullLoad` | **实际**装载重量 | 实际装载重量 |
/// | `Predistribution` / `WeightRecommendation` | **推荐**装载重量 | 实际装载重量 |
///
/// 这张表在用 `when (stowageMode)` 逐支写了四遍；C# 收紧成一个取值函数，但**口径必须逐支对上**。
/// 把 `FullLoad` 的推荐业载也写成推荐重量是这个域最典型的错——能编译、能求解，只是"全配载"模式下算出来的是
/// 一个谁也没要求的规划值。
///
/// six families (main/lower/total × recommended/actual) with **a different source per loading mode** — `FullLoad` takes the recommended payload from the
/// **actual** load weights, the other two modes from the **recommended** ones; the actual payload always comes from the actual weights. The code writes this table out four
/// times, once per family; C# folds it into one accessor but **the mapping must match branch for branch**. Writing `FullLoad`'s recommended payload from recommended weights is
/// the classic mistake: it compiles, it solves, and "full load" simply reports a planned value nobody asked for.
/// </remarks>
public sealed class Payload
{
    private readonly IReadOnlyList<StowagePosition> _positions;
    private readonly Load _load;

    /// <summary>
    /// 构造业载模型 / Construct the payload model.
    /// </summary>
    /// <param name="positions">舱位 / The stowage positions.</param>
    /// <param name="load">装载模型 / The load model.</param>
    /// <param name="plannedPayload">计划业载 / The planned payload.</param>
    /// <param name="maxPayload">最大业载 / The maximum payload.</param>
    /// <param name="computedPayload">已计算的业载；未给出则用计划业载 / The computed payload, falling back to the planned one.</param>
    public Payload(
        IReadOnlyList<StowagePosition> positions,
        Load load,
        double plannedPayload,
        double maxPayload,
        double? computedPayload = null) {
        _positions = positions;
        _load = load;
        PlannedPayload = plannedPayload;
        MaxPayload = maxPayload;
        ComputedPayload = computedPayload;
    }

    /// <summary>计划业载 / The planned payload</summary>
    public double PlannedPayload { get; }

    /// <summary>最大业载 / The maximum payload</summary>
    public double MaxPayload { get; }

    /// <summary>已计算的业载 / The computed payload</summary>
    public double? ComputedPayload { get; }

    /// <summary>主舱推荐业载符号 / The main-deck recommended payload symbol</summary>
    public LinearExpressionSymbol? MainEstimatePayload { get; private set; }

    /// <summary>下舱推荐业载符号 / The lower-hold recommended payload symbol</summary>
    public LinearExpressionSymbol? LowEstimatePayload { get; private set; }

    /// <summary>总推荐业载符号 / The total recommended payload symbol</summary>
    public LinearExpressionSymbol? EstimatePayload { get; private set; }

    /// <summary>主舱实际业载符号 / The main-deck actual payload symbol</summary>
    public LinearExpressionSymbol? MainActualPayload { get; private set; }

    /// <summary>下舱实际业载符号 / The lower-hold actual payload symbol</summary>
    public LinearExpressionSymbol? LowActualPayload { get; private set; }

    /// <summary>总实际业载符号 / The total actual payload symbol</summary>
    public LinearExpressionSymbol? ActualPayload { get; private set; }

    /// <summary>
    /// 按甲板与模式取重量多项式 / The weight polynomial for a deck and mode.
    /// </summary>
    /// <param name="mode">装载模式 / The stowage mode.</param>
    /// <param name="mainDeckOnly">是否只算主舱 / Whether to count the main deck only.</param>
    /// <param name="lowerHoldOnly">是否只算下舱 / Whether to count the lower holds only.</param>
    /// <param name="recommended">是否取推荐重量 / Whether to take the recommended weight.</param>
    /// <returns>重量多项式 / The weight polynomial.</returns>
    /// <remarks>
    /// `FullLoad` 下"推荐"也用实际重量（可选货物只按实际分配计重，固定货物也只计一次），其余模式按调用方给的
    /// `recommended` 取。**先判 `FullLoad`**，因为它是唯一会推翻 `recommended` 的模式。
    ///
    /// Under `FullLoad` the "recommended" figure also uses the actual weights (optional cargo counts once through actual assignments, fixed cargo once);
    /// every other mode honours the caller's `recommended`. `FullLoad` is checked **first** because it is the only mode that overrides `recommended`.
    /// </remarks>
    public LinearPolynomial<Flt64> Weights(StowageMode mode, bool mainDeckOnly, bool lowerHoldOnly, bool recommended) {
        bool useRecommended = recommended && mode != StowageMode.FullLoad;
        var polynomial = new MutableLinearPolynomial<Flt64>();
        for (int j = 0; j < _positions.Count; ++j) {
            bool main = _positions[j].AircraftPosition.Location.Main;
            if (mainDeckOnly && !main) {
                continue;
            }
            if (lowerHoldOnly && main) {
                continue;
            }
            LinearExpressionSymbol? weight = useRecommended
                ? _load.EstimateLoadWeightSymbol(j)
                : _load.ActualLoadWeightSymbol(j);
            if (weight is null) {
                continue;
            }
            foreach (LinearMonomial<Flt64> monomial in weight.Polynomial.Monomials) {
                polynomial.AddMonomial(monomial);
            }
            polynomial.AddConstant(weight.Polynomial.Constant);
        }
        return new LinearPolynomial<Flt64>(polynomial.Monomials, polynomial.Constant);
    }

    /// <summary>
    /// 注册六族业载符号 / Register the six payload symbols.
    /// </summary>
    /// <param name="mode">装载模式 / The stowage mode.</param>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>操作结果 / Operation result.</returns>
    public Try Register(StowageMode mode, LinearMetaModel<Flt64> model) {
        ArgumentNullException.ThrowIfNull(model);

        Try mainEstimate = AddWeighted(model, "main_estimate_payload", mode, mainDeckOnly: true, lowerHoldOnly: false, recommended: true);
        if (!mainEstimate.IsOk) {
            return mainEstimate;
        }
        MainEstimatePayload = _lastAdded;

        Try lowEstimate = AddWeighted(model, "low_estimate_payload", mode, mainDeckOnly: false, lowerHoldOnly: true, recommended: true);
        if (!lowEstimate.IsOk) {
            return lowEstimate;
        }
        LowEstimatePayload = _lastAdded;

        Try estimate = AddWeighted(model, "estimate_payload", mode, mainDeckOnly: false, lowerHoldOnly: false, recommended: true);
        if (!estimate.IsOk) {
            return estimate;
        }
        EstimatePayload = _lastAdded;

        Try mainActual = AddWeighted(model, "main_actual_payload", mode, mainDeckOnly: true, lowerHoldOnly: false, recommended: false);
        if (!mainActual.IsOk) {
            return mainActual;
        }
        MainActualPayload = _lastAdded;

        Try lowActual = AddWeighted(model, "low_actual_payload", mode, mainDeckOnly: false, lowerHoldOnly: true, recommended: false);
        if (!lowActual.IsOk) {
            return lowActual;
        }
        LowActualPayload = _lastAdded;

        Try actual = AddWeighted(model, "actual_payload", mode, mainDeckOnly: false, lowerHoldOnly: false, recommended: false);
        if (!actual.IsOk) {
            return actual;
        }
        ActualPayload = _lastAdded;

        // 预分配模式下推荐业载必须**等于目标业载**：`eq` 约束，不是目标函数里的软项。
        // 少了这一行，预分配算出来的业载可以偏离货主要多少——总量对不上，而每一舱位看起来都合法。
        // Under predistribution the recommended payload must **equal** the target: the code uses an `eq` row, not a soft objective term. Without it the predistribution can
        // drift away from the shipper's figure — the total does not add up while every position looks legal.
        if (mode == StowageMode.Predistribution && EstimatePayload is { } estimatePayload) {
            double target = ComputedPayload ?? PlannedPayload;
            var row = new LinearInequality<Flt64>(
                new LinearPolynomial<Flt64>(estimatePayload.Polynomial.Monomials, estimatePayload.Polynomial.Constant),
                new LinearPolynomial<Flt64>(new LinearMonomial<Flt64>[0], new Flt64(target)),
                Comparison.EQ,
                "predistribution_payload_conservation");
            Try added = model.AddConstraint(row, group: null, name: "predistribution_payload_conservation");
            if (!added.IsOk) {
                return added;
            }
        }

        return Results.Ok<Success>(Results.SuccessInstance);
    }

    private LinearExpressionSymbol? _lastAdded;

    private Try AddWeighted(
        LinearMetaModel<Flt64> model,
        string name,
        StowageMode mode,
        bool mainDeckOnly,
        bool lowerHoldOnly,
        bool recommended) {
        LinearPolynomial<Flt64> polynomial = Weights(mode, mainDeckOnly, lowerHoldOnly, recommended);
        var symbol = new LinearExpressionSymbol(
            new MutableLinearPolynomial<Flt64>(polynomial.Monomials, polynomial.Constant),
            name: name);
        _lastAdded = symbol;
        return model.Add(symbol);
    }
}
