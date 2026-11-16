// 纵向力矩区间限制 / The longitudinal moment interval constraint.

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Mac.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;
using FlightPhase = Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model.FlightPhase;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.AirworthinessSecurity.Service.Limits;

/// <summary>
/// 把每个飞行阶段的纵向力矩限制在给定区间 / Constrain the longitudinal moment of every flight phase to a supplied interval.
/// </summary>
/// <remarks>
/// 逐条对齐：**每个飞行阶段两行**，
/// `longitudinal_torque_{phase} ≥ min`（名字 `{name}_{phase}_minimum`）与 `≤ max`（名字 `{name}_{phase}_maximum`）。
///
/// 三处容易做错：
///
/// 1. **这里是 `_minimum` 在前**。`PayloadLimit` 是先上限后下限、且上限不带后缀；这一个两个都带后缀、顺序也相反。
///    两个文件的对称性不一样，沿用各自的样子。
/// 2. **`min` / `max` 是构造参数**，不是 `Torque` 的字段。它们由 `targetLongitudinalMoment ± maxLongitudinalMomentDeviation`
///    算出来再传进来，本类只负责用它建两行。
/// 3. **缺符号时明确失败**。`torque.longitudinalTorque[phase]!!` 会抛 `NullPointerException`；
/// C# 侧改成返回 `ApplicationFailed` 并说明是哪个阶段。
///
/// **two rows per flight phase**, `longitudinal_torque_{phase} ≥ min` (named `{name}_{phase}_minimum`) then `≤ max` (`..._maximum`).
/// Three easy mistakes: (1) **`_minimum` comes first here** — `PayloadLimit` adds the upper row first and only the lower row carries a suffix, whereas this one suffixes both and
/// reverses the order; the two files are not symmetric and each is copied as written; (2) **`min` / `max` are constructor arguments**, not `Torque` fields — they are derived
/// them from `targetLongitudinalMoment ± maxLongitudinalMomentDeviation` and only builds the rows here; (3) **a missing symbol fails explicitly** — its `!!` throws an
/// NPE, while this returns `ApplicationFailed` naming the phase.
/// </remarks>
internal sealed class LongitudinalMomentLimit
{
    private readonly Torque _torque;
    private readonly double _min;
    private readonly double _max;

    /// <summary>
    /// 构造纵向力矩区间限制 / Construct the longitudinal moment interval constraint.
    /// </summary>
    /// <param name="torque">纵向力矩模型 / The longitudinal torque model.</param>
    /// <param name="min">区间下界 / The interval lower bound.</param>
    /// <param name="max">区间上界 / The interval upper bound.</param>
    /// <param name="name">约束名前缀 / The constraint name prefix.</param>
    public LongitudinalMomentLimit(Torque torque, double min, double max, string name) {
        ArgumentNullException.ThrowIfNull(torque);
        _torque = torque;
        _min = min;
        _max = max;
        Name = name;
    }

    /// <summary>约束名前缀 / The constraint name prefix</summary>
    public string Name { get; }

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
        foreach (FlightPhase phase in new[] { FlightPhase.ZeroFuel, FlightPhase.TakeOff, FlightPhase.Landing }) {
            if (_torque.LongitudinalTorque(phase) is not { } moment) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.ApplicationFailed,
                    "纵向力矩符号未注册 / the longitudinal torque symbol is not registered: " + phase));
            }

            // `phase.name.lowercase`：`ZeroFuel` → `zerofuel`，不是 `zero_fuel`。
            // The code's `phase.name.lowercase`: `ZeroFuel` becomes `zerofuel`, not `zero_fuel`.
            string suffix = phase.ToString().ToLowerInvariant();

            string minimumName = Name + "_" + suffix + "_minimum";
            Try lower = Add(model, moment, _min, Comparison.GE, minimumName);
            if (!lower.IsOk) {
                return lower;
            }
            string maximumName = Name + "_" + suffix + "_maximum";
            Try upper = Add(model, moment, _max, Comparison.LE, maximumName);
            if (!upper.IsOk) {
                return upper;
            }
            added.Add(minimumName);
            added.Add(maximumName);
        }
        LastAddedNames = added;
        return Results.Ok<Success>(Results.SuccessInstance);
    }

    private static Try Add(
        LinearMetaModel<Flt64> model,
        LinearExpressionSymbol moment,
        double bound,
        Comparison comparison,
        string name) {
        var row = new LinearInequality<Flt64>(
            new LinearPolynomial<Flt64>(moment.Polynomial.Monomials, moment.Polynomial.Constant),
            new LinearPolynomial<Flt64>(new LinearMonomial<Flt64>[0], new Flt64(bound)),
            comparison,
            name);
        return model.AddConstraint(row, group: null, name: name);
    }
}
