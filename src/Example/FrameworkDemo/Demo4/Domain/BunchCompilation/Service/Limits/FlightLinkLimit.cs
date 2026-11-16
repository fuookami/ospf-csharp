#nullable enable

using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.BunchCompilation.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Rule.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.Task.Model;
using Fuookami.Ospf.Framework.GanttScheduling.Domain.Task.Model;
using Fuookami.Ospf.Framework.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using SymbolFlatten = Fuookami.Ospf.Core.Symbol.Flatten;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo4.Domain.BunchCompilation.Service.Limits;

/// <summary>
/// 任务链接的影子价格键 / The task-link shadow price key.
/// </summary>
/// <remarks>
/// 键是**链接本身**（前驱任务 + 后继任务），不是下标：影子价格的意义是"再让这一对任务接上一次，成本能降多少"。
///
/// The key is **the link itself** (predecessor plus successor), not an index: a shadow price means "how much cheaper if these two tasks were stitched together once more".
/// </remarks>
public sealed class FlightLinkShadowPriceKey(Link link) : ShadowPriceKey(typeof(FlightLinkShadowPriceKey)) {
    /// <summary>链接 / The link.</summary>
    public Link Link { get; } = link;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is FlightLinkShadowPriceKey other && Equals(Link, other.Link);

    /// <inheritdoc/>
    public override int GetHashCode() => Link.GetHashCode();

    /// <inheritdoc/>
    public override string ToString() => "Link (" + Link + ")";
}

/// <summary>
/// 任务链接的约束与目标 / The task links' constraints and objective.
/// </summary>
/// <remarks>
/// ：每条链接加一条 `slack ≥ 1`，再把 `Σ 系数 × slack` 放进最小化目标。
/// 与机队那一条**结构相同、阈值不同**：机队的右端项是机数，链接的右端项恒为 1——一条链接只要接上一次就算成立。
///
/// 阈值 1 与链接松弛的源多项式单位必须一致：源是 `link_k + 0.5·y[前驱] + 0.5·y[后继]`，`link_k` 数的是"这一轮几个束把它们接在一起"，
/// 所以"至少一次"就是 1。**写成机数或成本会让这条约束变成另一件事。**
///
/// The code's adds `slack ≥ 1` per link and puts `Σ coefficient × slack` into the minimisation objective. **The same shape as the fleet limit  with a
/// different threshold**: the fleet's right-hand side is an aircraft count, the link's is constantly 1 — a link holds as soon as it is stitched once.
///
/// The threshold 1 has to agree with the units of the link slack's source: the source is `link_k + 0.5·y[prev] + 0.5·y[succ]`, and `link_k` counts "how many bunches of this round
/// stitch the two together", so "at least once" is 1. **Writing an aircraft count or a cost there would turn the constraint into something else.**
/// </remarks>
public sealed class FlightLinkLimit
    : ICGPipeline<IGanttSchedulingShadowPriceArguments<Aircraft, FlightTaskAssignment>, object, GanttSchedulingShadowPriceMap<Aircraft, FlightTaskAssignment>> {
    private readonly FlightLink _flightLink;

    private readonly Func<Link, Flt64> _coefficient;

    private readonly string _name;

    /// <summary>构造函数 / Constructor.</summary>
    /// <param name="flightLink">任务链接 / The task links.</param>
    /// <param name="coefficient">每条链接的系数 / The per-link coefficient.</param>
    /// <param name="name">管道名称 / The pipeline name.</param>
    public FlightLinkLimit(
        FlightLink flightLink,
        Func<Link, Flt64>? coefficient = null,
        string name = "flight_link_limit") {
        ArgumentNullException.ThrowIfNull(flightLink);
        _flightLink = flightLink;
        _coefficient = coefficient ?? (_ => Flt64.One);
        _name = name;
    }

    /// <inheritdoc/>
    string IMetaConstraintGroup.Name => _name;

    /// <inheritdoc/>
    bool IMetaConstraintGroup.Lazy => false;

    /// <inheritdoc/>
    public Try Invoke(object model) {
        ArgumentNullException.ThrowIfNull(model);
        if (model is not IAbstractLinearMetaModel<Flt64> linear) {
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                "任务链接限制只能施加到线性元模型 / the flight link limit can only be applied to a linear meta model"));
        }

        IReadOnlyList<Link> links = _flightLink.Items;
        if (links.Count == 0) {
            return Results.Ok<Success>(Results.SuccessInstance);
        }

        for (int index = 0; index < links.Count; ++index) {
            if (!_flightLink.Slacks.TryGetValue(index, out ILinearIntermediateSymbol<Flt64>? slack)) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.ApplicationError,
                    "任务链接还没注册 / the task links have not been registered"));
            }
            string rowName = _name + "_" + index;
            Try added = linear.AddConstraint(
                new LinearInequality<Flt64>(
                    slack.Polynomial,
                    new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), Flt64.One),
                    Comparison.GE,
                    rowName),
                group: null,
                name: rowName,
                args: new FlightLinkShadowPriceKey(links[index]));
            if (!added.IsOk) {
                return added;
            }
        }

        // 目标：`Σ 系数 × slack`，系数在多项式外面乘 / The objective: `Σ coefficient × slack`, the coefficient multiplied outside the polynomial.
        var monomials = new List<LinearMonomial<Flt64>>();
        Flt64 constant = Flt64.Zero;
        for (int index = 0; index < links.Count; ++index) {
            if (!_flightLink.Slacks.TryGetValue(index, out ILinearIntermediateSymbol<Flt64>? slack)) {
                continue;
            }
            Flt64 coefficient = _coefficient(links[index]);
            LinearPolynomial<Flt64> polynomial = slack.Polynomial;
            foreach (LinearMonomial<Flt64> monomial in polynomial.Monomials) {
                monomials.Add(new LinearMonomial<Flt64>(monomial.Coefficient * coefficient, monomial.Symbol));
            }
            constant += polynomial.Constant * coefficient;
        }

        if (monomials.Count == 0 && constant.ToDouble() == 0.0) {
            return Results.Ok<Success>(Results.SuccessInstance);
        }

        return linear.AddObject(
            ObjectCategory.Minimum,
            new SymbolFlatten.LinearFlattenData<Flt64>(monomials, constant),
            "link",
            displayName: null);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// **返回零而不是抛异常**：影子价格为零只是让列生成少一个加速项，结果仍然正确；抛异常会让整条流水线停下来。
    /// 提取器按"前驱/后继的**原始**任务"反查链接，而 C# 的影子价格参数里没有前驱任务——`ponytail:` 天花板，
    /// 等参数接口需要携带时再补。
    ///
    /// **Returns zero rather than throwing**: a zero shadow price only costs column generation one acceleration term while the answer stays correct; throwing would halt the whole
    /// pipeline. The code's extractor looks the link up by the **origin** tasks of the predecessor and successor, while C#'s shadow-price arguments carry no predecessor —
    /// **`ponytail:` ceiling**, to be filled in when the argument interface needs it.
    /// </remarks>
    public ShadowPriceExtractor<IGanttSchedulingShadowPriceArguments<Aircraft, FlightTaskAssignment>, GanttSchedulingShadowPriceMap<Aircraft, FlightTaskAssignment>>? Extractor()
        => (map, args) => Flt64.Zero;

    /// <inheritdoc/>
    /// <remarks>
    /// 按**行上带的键类型**从对偶解里认回本管线的行。提取器暂时读不到，但映射本身是公开接口——手上有链接的调用方可以直接查。
    ///
    /// Recognises this pipeline's rows in the dual solution **by the key type on the row**. The extractor cannot read them yet, but the map is a public interface — a caller holding a
    /// link can look the price up directly.
    /// </remarks>
    public Try Refresh(
        GanttSchedulingShadowPriceMap<Aircraft, FlightTaskAssignment> shadowPriceMap,
        object model,
        MetaDualSolution shadowPrices) {
        ArgumentNullException.ThrowIfNull(shadowPriceMap);
        ArgumentNullException.ThrowIfNull(shadowPrices);

        foreach (KeyValuePair<MathConstraint, Flt64> pair in shadowPrices.Constraints) {
            if (pair.Key.Args is FlightLinkShadowPriceKey key) {
                shadowPriceMap.Put(new ShadowPrice(key, pair.Value));
            }
        }
        return Results.Ok<Success>(Results.SuccessInstance);
    }
}
