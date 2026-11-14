// 水平安定面配平上下界 / The upper and lower bounds on horizontal stabilizer trim.

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Mac.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.AirworthinessSecurity.Service.Limits;

/// <summary>
/// 水平安定面配平上下界 / The bounds on horizontal stabilizer trim.
/// </summary>
/// <remarks>
/// 逐条对齐基线 `service/limits/HorizontalStabilizerLimit.kt`。每个安定面配置最多**三行**，且取值随装载模式变化：
///
/// | 行 | 模式 | 关系 | 名称后缀 |
/// | --- | --- | --- | --- |
/// | 上界 | 非建议 | `trim ≤ maxTrim` | `_ub` |
/// | 上界 | 建议 + 有 `warnMaxTrim` | `trim ≤ warnMaxTrim` | `_warn_ub` |
/// | 上界 | 建议 + 无 `warnMaxTrim` | `trim ≤ maxTrim` | `_ub` |
/// | 警告下界 | 建议 + 有 `warnMinTrim` | `trim ≥ warnMinTrim` | `_warn_lb` |
/// | 下界 | 总是 | `trim ≥ minTrim` | `_lb` |
///
/// 四处容易做错：
///
/// 1. **建议模式下警告上界**取 `warnMaxTrim ?? maxTrim`，而**后缀**只在 `warnMaxTrim` 真存在时才是 `_warn_ub`。
///    取值用 `??`、命名用"是否非空"，是两个不同的条件。写成同一个条件时，建议模式下没有警告值会产生 `_warn_ub` 这个名字却没有警告值。
/// 2. **警告下界只在建议模式下加**，且**不替代**常规下界——两者可能同时存在。
/// 3. **常规下界总是加**（只要 `minTrim` 非空），与模式无关。它是硬边界，警告只是额外的收紧。
/// 4. **上界与下界各自判断 `null`**，`maxTrim` 为空时连警告上界也不加。
///
/// Mirrors the baseline. Up to **three rows** per stabilizer configuration with mode-dependent values, tabulated above. Four easy mistakes: (1) **the recommended-mode upper
/// bound takes `warnMaxTrim ?? maxTrim`** while the **suffix** is `_warn_ub` only when `warnMaxTrim` really exists — a `??` for the value and a null test for the name are two
/// different conditions, and using one for both produces a `_warn_ub` row with no warning value; (2) **the warning lower bound is recommended-mode only** and does **not** replace
/// the regular one — both can be present; (3) **the regular lower bound is always added** when `minTrim` is non-null, regardless of mode, because it is the hard limit and the
/// warning merely tightens it; (4) **each bound tests its own `null`**, and a null `maxTrim` drops even the warning upper bound.
/// </remarks>
internal sealed class HorizontalStabilizerLimit
{
    private readonly IReadOnlyDictionary<HorizontalStabilizer.Key, HorizontalStabilizer> _horizontalStabilizers;
    private readonly StowageMode _stowageMode;
    private readonly string _name;

    /// <summary>
    /// 构造水平安定面配平上下界 / Construct the trim bounds.
    /// </summary>
    /// <param name="horizontalStabilizers">水平安定面 / The horizontal stabilizers.</param>
    /// <param name="stowageMode">装载模式 / The stowage mode.</param>
    /// <param name="name">约束名前缀 / The constraint name prefix.</param>
    public HorizontalStabilizerLimit(
        IReadOnlyDictionary<HorizontalStabilizer.Key, HorizontalStabilizer> horizontalStabilizers,
        StowageMode stowageMode,
        string name = "horizontal_stabilizer_limit") {
        _horizontalStabilizers = horizontalStabilizers;
        _stowageMode = stowageMode;
        _name = name;
    }

    /// <summary>上一次执行加上的约束名 / The constraint names added by the last invocation</summary>
    public IReadOnlyList<string> LastAddedNames { get; private set; } = Array.Empty<string>();

    /// <summary>
    /// 执行限制 / Invoke the limit.
    /// </summary>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>操作结果 / Operation result.</returns>
    public Try Invoke(LinearMetaModel<Flt64> model) {
        ArgumentNullException.ThrowIfNull(model);
        var added = new List<string>();
        bool recommendation = _stowageMode == StowageMode.WeightRecommendation;

        foreach (KeyValuePair<HorizontalStabilizer.Key, HorizontalStabilizer> pair in _horizontalStabilizers) {
            HorizontalStabilizer stabilizer = pair.Value;
            if (stabilizer.TrimSymbol is not { } trim) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.ApplicationFailed,
                    "水平安定面配平符号未注册 / the horizontal stabilizer trim symbol is not registered: " + pair.Key));
            }

            double? maxTrim = recommendation
                ? stabilizer.Bounds.WarnMaxTrim ?? stabilizer.Bounds.MaxTrim
                : stabilizer.Bounds.MaxTrim;
            if (maxTrim is { } upperBound) {
                // 后缀看 `warnMaxTrim` **是否存在**，不看取值来自哪一支。两者在"建议模式 + 有警告值"时一致，
                // 在"建议模式 + 无警告值"时不同：那时取值退回 `maxTrim`，名字也退回 `_ub`。
                // The suffix follows whether `warnMaxTrim` **exists**, not which branch supplied the value. They agree in "recommended with a warning value" and differ in
                // "recommended without one": the value falls back to `maxTrim` and the name falls back to `_ub`.
                bool warning = recommendation && stabilizer.Bounds.WarnMaxTrim is not null;
                string upperName = _name + "_" + pair.Key + (warning ? "_warn_ub" : "_ub");
                Try upper = Add(model, trim, upperBound, Comparison.LE, upperName);
                if (!upper.IsOk) {
                    return upper;
                }
                added.Add(upperName);
            }

            if (recommendation && stabilizer.Bounds.WarnMinTrim is { } warnMinTrim) {
                string warnName = _name + "_" + pair.Key + "_warn_lb";
                Try warn = Add(model, trim, warnMinTrim, Comparison.GE, warnName);
                if (!warn.IsOk) {
                    return warn;
                }
                added.Add(warnName);
            }

            if (stabilizer.Bounds.MinTrim is { } minTrim) {
                string lowerName = _name + "_" + pair.Key + "_lb";
                Try lower = Add(model, trim, minTrim, Comparison.GE, lowerName);
                if (!lower.IsOk) {
                    return lower;
                }
                added.Add(lowerName);
            }
        }
        LastAddedNames = added;
        return Results.Ok<Success>(Results.SuccessInstance);
    }

    private static Try Add(
        LinearMetaModel<Flt64> model,
        LinearExpressionSymbol trim,
        double bound,
        Comparison comparison,
        string name) {
        var row = new LinearInequality<Flt64>(
            new LinearPolynomial<Flt64>(trim.Polynomial.Monomials, trim.Polynomial.Constant),
            new LinearPolynomial<Flt64>(new LinearMonomial<Flt64>[0], new Flt64(bound)),
            comparison,
            name);
        return model.AddConstraint(row, group: null, name: name);
    }
}
