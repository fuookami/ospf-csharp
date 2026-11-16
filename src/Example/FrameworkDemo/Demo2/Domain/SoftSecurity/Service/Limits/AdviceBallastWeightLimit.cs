// 建议压舱重量目标 / The advised-ballast-weight objective.

using System;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Symbol.Function;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.SoftSecurity.Service.Limits;

/// <summary>
/// 建议压舱重量目标 / The advised-ballast-weight objective.
/// </summary>
/// <remarks>
/// 与对齐：**当且仅当**存在建议压舱重量时，建立一个
/// **只有负偏差**的阈值松弛（`withNegative = true`、`withPositive = false`），并把它作为**最小化目标**——即"实际压舱不超过建议值"。
///
/// 四处必须沿用：
///
/// 1. **没有建议值时什么都不做**（不是加一个恒零项）。加零项会让模型里多一个无意义的符号。
/// 2. **只有负偏差**：`withPositive = false`。两个方向都要，惩罚会变成"越接近越好"，而语义是**单边**的。
/// 3. **阈值单位是飞机的重量单位**（`aircraftModel.weightUnit`），而 `ballast.ballastWeight` 自带单位，需要转换。
/// 4. **它既是约束也是目标**：`SlackFunction` 的 `constraint: true` 会注册松弛约束，随后的 `AddObject(Minimum, …)` 才是目标。
///
/// **only when** an advised ballast weight exists, build a **negative-only** threshold slack (`withNegative = true`, `withPositive = false`) and add it
/// as a **minimisation objective** — that is, "actual ballast does not exceed the advised value". Four things are carried over: **with no advised value, do nothing** (not "add a
/// constant-zero term", which would leave a meaningless symbol in the model); **negative deviation only**, since allowing both makes the penalty "the closer the better" while
/// its meaning is one-sided; **the threshold is in the aircraft weight unit** while `ballast.ballastWeight` carries its own, so a conversion is needed; and **it is
/// both a constraint and an objective** — `SlackFunction`'s `constraint: true` registers the slack constraint, and the subsequent `AddObject(Minimum, …)` is the objective.
/// </remarks>
public sealed class AdviceBallastWeightLimit
{
    private readonly Ballast _ballast;
    private readonly Func<Flt64> _coefficient;

    /// <summary>
    /// 构造建议压舱重量目标 / Construct the advised-ballast-weight objective.
    /// </summary>
    /// <param name="ballast">压舱 / The ballast.</param>
    /// <param name="coefficient">系数 / The coefficient.</param>
    /// <param name="name">管线名称 / The pipeline name.</param>
    public AdviceBallastWeightLimit(
        Ballast ballast,
        Func<Flt64>? coefficient = null,
        string name = "advice_ballast_weight_limit")
    {
        ArgumentNullException.ThrowIfNull(ballast);
        _ballast = ballast;
        _coefficient = coefficient ?? (() => Flt64.One);
        Name = name;
    }

    /// <summary>管线名称 / The pipeline name</summary>
    public string Name { get; }

    /// <summary>
    /// 施加约束与目标 / Apply the constraint and objective.
    /// </summary>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <returns>操作结果 / Operation result.</returns>
    public Try Invoke(LinearMetaModel<Flt64> model)
    {
        ArgumentNullException.ThrowIfNull(model);

        if (_ballast.AdviceBallastWeight is not { } advice)
        {
            return Results.Ok<Success>(Results.SuccessInstance);
        }

        if (_ballast.BallastWeightSymbol is not { } ballastWeight)
        {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.ApplicationFailed,
                "压舱重量符号未注册 / the ballast weight symbol is not registered"));
        }
        LinearPolynomial<Flt64> x = ballastWeight.Polynomial;
        double threshold = advice.Value.ToDouble();
        var slack = new SlackFunction<Flt64>(
            x,
            new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), new Flt64(threshold)),
            IIntoValue<Flt64>.Identity,
            withNegative: true,
            withPositive: false,
            threshold: true,
            constraint: true,
            name: "advice_ballast_weight_slack");
        var adapter = new LinearFunctionSymbolAdapter<Flt64>(slack);
        Try added = model.Add(adapter);
        if (!added.IsOk)
        {
            return added;
        }

        // 目标是**最小化**这个松弛量，系数由外部注入。
        // The objective **minimises** the slack, with the coefficient injected.
        return model.AddObject(
            ObjectCategory.Minimum,
            new LinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(_coefficient(), adapter) },
                Flt64.Zero),
            "advice ballast weight",
            displayName: null);
    }
}
