// 各飞行阶段总重上限 / The upper bound on total weight per flight phase.

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Aircraft.Model;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.AirworthinessSecurity.Service.Limits;

/// <summary>
/// 各阶段总重上限 / The total weight upper bound per flight phase.
/// </summary>
/// <remarks>
/// 逐条对齐：对**每个**飞行阶段，若该阶段有最大总重就加一行
/// `estimate_total_weight_{阶段} ≤ maxTotalWeight`，名字 `{name}_{阶段}`。
///
/// 三处容易做错：
///
/// 1. **缺总重表达式是失败，缺最大总重是跳过**。这两者的处理**故意不同**：表达式缺失说明 `TotalWeight.Register` 没跑（或跑失败），
///    是集成错误；而"这个阶段没有最大总重"是合法的配置（例如零油重不设限）。
/// 2. **遍历全部三个阶段**，不是只做起飞。只做起飞会让零油重与着陆超重而求解器不知道。
/// 3. **名字带阶段后缀**。三段同名时"第 2 条约束不可行"不知道是哪个阶段。
///
/// for **each** flight phase, add `estimate_total_weight_{phase} ≤ maxTotalWeight` when the phase has a maximum, named `{name}_{phase}`. Three easy mistakes:
/// (1) **a missing estimate is a failure, a missing maximum is a skip** — deliberately different: a missing estimate means `TotalWeight.Register` never ran (or failed) and is
/// an integration error, while "no maximum for this phase" is a legitimate configuration; (2) **all three phases are iterated**, not just take-off, or zero-fuel and landing
/// go overweight unnoticed; (3) **the name carries the phase**, or "constraint 2 is infeasible" does not say which phase.
/// </remarks>
internal sealed class TotalWeightLimit
{
    private readonly TotalWeight _totalWeight;
    private readonly string _name;

    /// <summary>
    /// 构造总重上限 / Construct the total weight bounds.
    /// </summary>
    /// <param name="totalWeight">总重模型 / The total weight model.</param>
    /// <param name="name">约束名前缀 / The constraint name prefix.</param>
    public TotalWeightLimit(TotalWeight totalWeight, string name = "total_weight_limit") {
        _totalWeight = totalWeight;
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
        foreach (FlightPhase phase in Enum.GetValues<FlightPhase>()) {
            if (_totalWeight.EstimateTotalWeight(phase) is not { } estimate) {
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.ApplicationFailed,
                    "总重表达式缺失 / the total weight expression is missing: "
                    + phase.ToString().ToLowerInvariant()));
            }
            if (!_totalWeight.MaxTotalWeight.TryGetValue(phase, out double maximum)) {
                continue;
            }

            string name = _name + "_" + phase.ToString().ToLowerInvariant();
            var row = new LinearInequality<Flt64>(
                new LinearPolynomial<Flt64>(estimate.Polynomial.Monomials, estimate.Polynomial.Constant),
                new LinearPolynomial<Flt64>(new LinearMonomial<Flt64>[0], new Flt64(maximum)),
                Comparison.LE,
                name);
            Try result = model.AddConstraint(row, group: null, name: name);
            if (!result.IsOk) {
                return result;
            }
            added.Add(name);
        }
        LastAddedNames = added;
        return Results.Ok<Success>(Results.SuccessInstance);
    }
}
