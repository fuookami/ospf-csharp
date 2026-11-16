// Phase-I 人工覆盖变量 / Phase-I artificial coverage variables.

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Token;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Framework.NetworkScheduling.Domain.Vrp.Model;
using Fuookami.Ospf.Framework.NetworkScheduling.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.MultiArray;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<
    Fuookami.Ospf.Utils.Functional.Success,
    Fuookami.Ospf.Utils.Error.ErrorCode,
    Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.NetworkScheduling.Domain.RouteCompilation.Model;

/// <summary>
/// Phase-I 人工覆盖变量 / Phase-I artificial coverage variables.
/// </summary>
/// <remarks>
/// 为每个客户维护一个非负人工变量 `a[i]`，使 `sum(visit[r,i] · x[r]) + a[i] = 1`。Phase I 最小化 `sum(a[i])`，Phase II 把所有
/// `a[i]` 固定为 0。
/// Maintains a non-negative artificial variable `a[i]` per customer so that `sum(visit[r,i] · x[r]) + a[i] = 1`. Phase I minimises
/// `sum(a[i])`; Phase II fixes every `a[i]` to zero.
/// **没有人工变量，列生成的第一阶段就无法在"覆盖不全"时保持可行**：初始列集通常只覆盖部分客户，等式约束会立刻不可行，pricer 也就
/// 拿不到任何对偶信息。人工变量把缺失的覆盖显式记账，让第一阶段总能给出一个可行基。
/// 用泛型参数 `V` 表达"任意客户类型"（相当于星投影）：人工覆盖只关心客户标识，不关心具体数值类型。
/// The code declares `List&lt;Customer&lt;*&gt;&gt;` (a star projection); the C# side expresses the same with the generic
/// parameter `V`: artificial coverage only cares about customer identity, not the numeric type.
///
/// **Without artificial variables the first phase of column generation cannot stay feasible while coverage is incomplete**: the
/// initial column set usually covers only some customers, the equality rows go infeasible immediately, and the pricer gets no dual
/// information at all. Artificial variables book the missing coverage explicitly so the first phase always has a feasible basis.
/// </remarks>
public sealed class ArtificialCoverage<V> where V : struct, IRealNumber<V> {
    private readonly IReadOnlyList<Customer<V>> _customers;
    private bool _registered;
    private bool _phaseTwo;

    /// <summary>
    /// 构造人工覆盖 / Construct the artificial coverage.
    /// </summary>
    /// <param name="customers">客户列表 / The customers.</param>
    public ArtificialCoverage(IReadOnlyList<Customer<V>> customers) {
        ArgumentNullException.ThrowIfNull(customers);
        _customers = customers;
    }

    /// <summary>人工变量 / The artificial variables.</summary>
    public URealVariable1? Variables { get; private set; }

    /// <summary>人工覆盖中间符号 / The artificial-coverage intermediate symbols.</summary>
    public SymbolCombination<LinearExpressionSymbol, Shape1>? Coverage { get; private set; }

    /// <summary>人工成本中间符号 / The artificial-cost intermediate symbol.</summary>
    public LinearExpressionSymbol? Cost { get; private set; }

    /// <summary>是否处于 Phase II / Whether currently in Phase II.</summary>
    public bool IsPhaseTwo => _phaseTwo;

    /// <summary>
    /// 注册到模型 / Register into the model.
    /// </summary>
    /// <param name="model">元模型 / Meta model.</param>
    /// <returns>操作结果 / The operation result.</returns>
    public Try Register(LinearMetaModel<Flt64> model) {
        ArgumentNullException.ThrowIfNull(model);
        if (_registered) {
            return Results.Ok(Results.SuccessInstance);
        }

        // ponytail: 逐个加入变量。`UIntVariable1` 实现了 `IEnumerable<IVariableItem>` 因而能整组 Add，
        // `URealVariable1` 没有（Core 里两者不对称）。逐个添加语义等价、零额外代码；
        // 若将来 1-D 变量组普遍需要整组注册，把枚举器上提到 `VariableCombination<,>` 基类。
        // ponytail: add one by one. `UIntVariable1` implements `IEnumerable<IVariableItem>` and can be added as a group;
        // `URealVariable1` does not (Core is asymmetric here). Adding individually is equivalent and costs nothing;
        // if 1-D groups generally need group registration, lift the enumerator into the `VariableCombination<,>` base.
        var variables = new URealVariable1("a", _customers.Count);
        for (int index = 0; index < _customers.Count; index++) {
            variables[index].Name = $"a_{_customers[index].Id.Value}";
            Try addedVariable = model.Add(variables[index]);
            if (addedVariable is Failed<Success, ErrorCode, Error<ErrorCode>> variableFailure) {
                return Results.Failed<Success>(variableFailure.Error);
            }
        }

        var coverage = new SymbolCombination<LinearExpressionSymbol, Shape1>(
            "artificial_coverage",
            Shape1.Invoke(_customers.Count),
            (index, _) => new LinearExpressionSymbol(
                new MutableLinearPolynomial<Flt64>(
                    new[] { new LinearMonomial<Flt64>(Flt64.One, variables[index]) }, Flt64.Zero),
                name: $"artificial_coverage_{_customers[index].Id.Value}"));
        Try addedCoverage = model.Add((IEnumerable<IIntermediateSymbol>)coverage);
        if (addedCoverage is Failed<Success, ErrorCode, Error<ErrorCode>> coverageFailure) {
            return Results.Failed<Success>(coverageFailure.Error);
        }

        var costPolynomial = new MutableLinearPolynomial<Flt64>();
        for (int index = 0; index < _customers.Count; index++) {
            costPolynomial.AddMonomial(new LinearMonomial<Flt64>(Flt64.One, variables[index]));
        }
        var cost = new LinearExpressionSymbol(costPolynomial, name: "artificial_cost");
        Try addedCost = model.Add(cost);
        if (addedCost is Failed<Success, ErrorCode, Error<ErrorCode>> costFailure) {
            return Results.Failed<Success>(costFailure.Error);
        }

        Variables = variables;
        Coverage = coverage;
        Cost = cost;
        _registered = true;
        return Results.Ok(Results.SuccessInstance);
    }

    /// <summary>
    /// 切换到 Phase II：把所有人工变量固定为 0 / Switch to Phase II: fix every artificial variable to zero.
    /// </summary>
    /// <remarks>
    /// **C# 侧用等式行而不是改写变量的声明值域**：改写值域会追溯性地动一个已经注册进模型的变量，
    /// 模型里看不到任何痕迹；显式等式行则是可查询、可解释的。两种写法语义等价：`a[i] == 0` 与把值域钉成 `[0, 0]` 对求解器是同一件事。
    /// **The C# side uses equality rows rather than rewriting the variable's declared range.** Rewriting the range would do
    /// `a[i].range.set(zero range)`, which retroactively changes a variable already registered into the model and leaves no trace in
    /// it; an explicit equality row is queryable and explainable. The semantics are equivalent: `a[i] == 0` and pinning the range to
    /// `[0, 0]` are the same thing to a solver.
    /// </remarks>
    /// <param name="model">元模型 / Meta model.</param>
    /// <returns>操作结果 / The operation result.</returns>
    public Try SwitchToPhaseTwo(LinearMetaModel<Flt64> model) {
        ArgumentNullException.ThrowIfNull(model);
        if (_phaseTwo) {
            return Results.Ok(Results.SuccessInstance);
        }
        if (Variables is not { } variables) {
            return NetworkSchedulingErrors.TryFailure(
                "切换到 Phase II 失败：人工变量尚未注册 / Failed to switch to Phase II: artificial variables are not registered");
        }
        for (int index = 0; index < _customers.Count; index++) {
            var expression = new LinearPolynomial<Flt64>(
                new[] { new LinearMonomial<Flt64>(Flt64.One, variables[index]) }, Flt64.Zero);
            Try fixedAtZero = model.AddConstraint(
                expression.Eq(new LinearPolynomial<Flt64>(Array.Empty<LinearMonomial<Flt64>>(), Flt64.Zero)),
                null,
                name: $"artificial_phase_two_{_customers[index].Id.Value}");
            if (fixedAtZero is Failed<Success, ErrorCode, Error<ErrorCode>> fixFailure) {
                return Results.Failed<Success>(fixFailure.Error);
            }
        }
        _phaseTwo = true;
        return Results.Ok(Results.SuccessInstance);
    }

    /// <summary>
    /// 检查 Phase I 是否收敛 / Check whether Phase I has converged.
    /// </summary>
    /// <remarks>
    /// **任何一个缺失解值都直接失败，绝不当作 0**。缺值意味着模型没解或解没回填；当成 0 会让"人工变量总和 ≤ 容差"恒为真，于是
    /// Phase I 被误判为收敛，主问题带着未覆盖的客户继续跑下去。
    /// **Any missing value fails outright and is never treated as zero.** A missing value means the model was not solved or the
    /// solution was not populated; treating it as zero makes "sum of artificials ≤ tolerance" always true, so Phase I is wrongly judged
    /// converged and the master continues with uncovered customers.
    /// </remarks>
    /// <param name="model">元模型 / Meta model.</param>
    /// <param name="feasibilityTolerance">可行性容差 / Feasibility tolerance.</param>
    /// <returns>人工变量总和是否在容差内 / Whether the sum of artificial variables is within tolerance.</returns>
    public Result<bool, ErrorCode, Error<ErrorCode>> IsPhaseOneConverged(
        LinearMetaModel<Flt64> model,
        Flt64 feasibilityTolerance) {
        ArgumentNullException.ThrowIfNull(model);
        if (Variables is not { } variables) {
            return NetworkSchedulingErrors.Failure<bool>(
                "Phase I 收敛检查失败：人工变量尚未注册 / Phase I convergence check failed: artificial variables are not "
                + "registered");
        }
        Flt64 sum = Flt64.Zero;
        foreach (Token<Flt64> token in model.Tokens.Tokens) {
            if (!token.BelongsTo(variables)) {
                continue;
            }
            if (token.Result is not { } value) {
                return NetworkSchedulingErrors.Failure<bool>(
                    $"Phase I 收敛检查失败：人工变量 {token.Name} 无解值 / Phase I convergence check failed: artificial variable "
                    + $"{token.Name} has no solution value");
            }
            sum = sum.Plus(value);
        }
        return Results.Ok(sum.CompareTo(feasibilityTolerance) <= 0);
    }
}
