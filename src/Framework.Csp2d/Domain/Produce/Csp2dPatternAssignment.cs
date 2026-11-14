// 主问题的图案使用量变量集合 / The pattern usage variable set of the master problem.
// 与固定 Kotlin 基线 `csp2d/domain/produce/MasterProblem.kt` 的 `Csp2dPatternAssignment` 与 `safeCsp2dPolynomial` 对应。
// Mirrors `Csp2dPatternAssignment` and `safeCsp2dPolynomial` from the pinned Kotlin baseline's `MasterProblem.kt`.

using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Symbol;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Framework.Csp2d.Domain.Material.Model;
using Fuookami.Ospf.Framework.Csp2d.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.MultiArray;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;

namespace Fuookami.Ospf.Framework.Csp2d.Domain.Produce;

/// <summary>主问题建模的小工具 / Small helpers for master-problem modelling.</summary>
public static class Csp2dProduceSupport {
    /// <summary>
    /// 只有常数项的多项式，用作不等式的右端 / A constant-only polynomial used as the right-hand side of an inequality.
    /// </summary>
    /// <param name="value">常数值 / The constant value.</param>
    /// <returns>线性多项式 / A linear polynomial.</returns>
    public static LinearPolynomial<Flt64> ConstantPolynomial(Flt64 value) =>
        new MutableLinearPolynomial<Flt64>(constant: value).ToLinearPolynomial();

    /// <summary>
    /// 读取需求的目标值 / Read the target value of a demand.
    /// </summary>
    /// <param name="demand">产品需求 / The product demand.</param>
    /// <returns>目标值 / The target value.</returns>
    internal static double RequiredValue(ProductDemand2<Flt64> demand) => demand.Demand switch {
        DemandValue2<Flt64>.Amount amount => amount.Count,
        DemandValue2<Flt64>.Area area => area.QuantityArea.Value.ToDouble(),
        DemandValue2<Flt64>.Weight weight => weight.QuantityWeight.Value.ToDouble(),
        _ => 0.0,
    };

    /// <summary>
    /// 计算单个方案对某个需求的覆盖量 / Compute the coverage of one plan for one demand.
    /// </summary>
    /// <param name="plan">方案 / The plan.</param>
    /// <param name="demand">产品需求 / The product demand.</param>
    /// <returns>覆盖量 / The coverage.</returns>
    /// <remarks>
    /// **方案里没有这个产品就返回零**，这是要紧的一格：把它当成"缺省覆盖 1"会让**任何方案都能满足任何需求**。
    /// **重量口径缺单件重量时也返回零**：拿件数顶替重量会让"重量需求"被件数悄悄满足。
    ///
    /// **A plan without this product contributes zero**, and that case matters: treating it as "coverage defaults to one" would let **any plan satisfy any demand**.
    /// **The weight unit also yields zero when the unit weight is missing**: substituting the count for the weight would let a "weight demand" be silently satisfied by
    /// piece counts.
    /// </remarks>
    internal static double CoverageOf(CuttingPlan2<Flt64> plan, ProductDemand2<Flt64> demand) {
        ulong amount = 0UL;
        foreach (CuttingPlanDemandContribution2<Flt64> contribution in plan.ContributionList) {
            if (contribution.Product.Id.Value != demand.Product.Id.Value) {
                continue;
            }
            amount = contribution.Amount;
            break;
        }
        if (amount == 0UL) {
            return 0.0;
        }
        double count = amount;
        return demand.Mode switch {
            DemandMode2.Amount => count,
            DemandMode2.Area => count * plan.Pattern.Region.Area.Value.ToDouble(),
            DemandMode2.Weight => demand.Product.UnitWeight is { } unitWeight
                ? count * unitWeight.Value.ToDouble()
                : 0.0,
            _ => 0.0,
        };
    }

    /// <summary>
    /// 由单项式列表构造线性多项式，空表时退化为零常量 / Build a linear polynomial from monomials, falling back to the zero constant for an empty list.
    /// </summary>
    /// <param name="monomials">单项式列表 / The monomial list.</param>
    /// <returns>可变线性多项式 / A mutable linear polynomial.</returns>
    /// <remarks>
    /// **所有求和都走这里**：`MutableLinearPolynomial` 在**没有任何单项式**时仍需一个显式零常量，
    /// 否则表达式符号的注册会拿到一个"没有常数项的空壳"。
    ///
    /// **Every sum goes through here**: `MutableLinearPolynomial` still needs an explicit zero constant when there are **no monomials at all**, or the expression symbol
    /// registers as an empty shell without a constant term.
    /// </remarks>
    public static MutableLinearPolynomial<Flt64> SafeCsp2dPolynomial(
        IReadOnlyList<LinearMonomial<Flt64>> monomials) {
        var polynomial = new MutableLinearPolynomial<Flt64>(constant: Flt64.Zero);
        foreach (LinearMonomial<Flt64> monomial in monomials) {
            polynomial.AddMonomial(monomial);
        }
        return polynomial;
    }
}

/// <summary>
/// 图案使用量变量集合 / The pattern usage variable set.
/// </summary>
/// <remarks>
/// 一个方案被用几次，就是主问题的全部决策。四个聚合表达式（成品面积 / 余料 / 消耗料域面积 / 消耗段高）
/// 都由同一组 `y_j` 乘系数得到——**它们不是独立变量**，否则目标和约束会各说各话。
///
/// How many times a plan is used is the entire decision of the master problem. The four aggregate expressions (used area / waste / consumed region area / consumed
/// segment height) all come from the same `y_j` multiplied by coefficients: **they are not independent variables**, or the objective and the constraints would tell
/// different stories.
/// </remarks>
public sealed class Csp2dPatternAssignment {
    /// <summary>候选方案 / The candidate plans.</summary>
    public IReadOnlyList<CuttingPlan2<Flt64>> Plans { get; }

    private UIntVariable1? _usage;
    private LinearExpressionSymbol? _usedArea;
    private LinearExpressionSymbol? _wasteArea;
    private LinearExpressionSymbol? _consumedArea;
    private LinearExpressionSymbol? _consumedHeight;

    /// <summary>构造使用量变量集合 / Build the usage variable set.</summary>
    /// <param name="plans">候选方案 / The candidate plans.</param>
    public Csp2dPatternAssignment(IReadOnlyList<CuttingPlan2<Flt64>> plans) => Plans = plans;

    /// <summary>每个方案的使用次数变量 / The usage variable of each plan.</summary>
    /// <returns>变量组合 / The variable combination.</returns>
    /// <remarks>注册前读取会抛异常：**没有上界的无符号变量不可能被求解器接受**，而延迟构造是基线的口径。
    /// Reading before registration throws: **an unsigned variable without an upper bound cannot be accepted by the solver**, and lazy construction is the baseline's
    /// convention.</remarks>
    public UIntVariable1 Usage =>
        _usage ?? throw new System.InvalidOperationException("usage 尚未注册 / usage is not registered yet");

    /// <summary>成品占用面积表达式 / The expression of the area occupied by finished parts.</summary>
    public LinearExpressionSymbol? UsedArea => _usedArea;

    /// <summary>余料面积表达式 / The expression of the remnant area.</summary>
    public LinearExpressionSymbol? WasteArea => _wasteArea;

    /// <summary>消耗料域面积表达式 / The expression of the consumed region area.</summary>
    public LinearExpressionSymbol? ConsumedArea => _consumedArea;

    /// <summary>消耗段高（卷材模式）表达式 / The expression of the consumed segment height (coil mode).</summary>
    public LinearExpressionSymbol? ConsumedHeight => _consumedHeight;

    /// <summary>
    /// 注册变量与聚合表达式 / Register the variables and the aggregate expressions.
    /// </summary>
    /// <param name="model">线性元模型 / The linear meta model.</param>
    /// <param name="demandUpperBound">使用次数上界 / The usage upper bound.</param>
    /// <returns>注册结果 / The registration result.</returns>
    /// <remarks>
    /// 使用次数变量上界按「需求件数上界」收紧：**单方案重复次数不会超过全部需求件数之和**，避免无意义的无限上界。
    /// 上界为零时取 1——**零上界会把所有方案钉死成不可用**，那不是"没有需求"，而是"模型写错了"。
    ///
    /// The usage bound is tightened by the demand piece count: **a single plan cannot repeat more times than the total demanded pieces**, which avoids a meaningless
    /// infinite range. A zero bound becomes 1 — **a zero upper bound pins every plan to unusable**, which is not "no demand" but "the model is broken".
    /// </remarks>
    public Result<Success, ErrorCode, Error<ErrorCode>> Register(
        LinearMetaModel<Flt64> model,
        ulong demandUpperBound) {
        if (Plans.Count == 0) {
            return Results.Failed<Success>(new Csp2dValidationError(
                "主问题没有候选方案 / The master problem has no candidate plan"));
        }

        if (_usage is null) {
            _usage = new UIntVariable1("usage", Shape1.Invoke(Plans.Count));
            ulong bound = demandUpperBound == 0UL ? 1UL : demandUpperBound;
            for (int index = 0; index < Plans.Count; index++) {
                _usage[index].Range.Leq(new InvariantUInt64Wrapper(new UInt64(bound)));
            }
        }
        Result<Success, ErrorCode, Error<ErrorCode>> added = model.Add(_usage);
        if (added is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
            return added;
        }

        if (_usedArea is null) {
            _usedArea = new LinearExpressionSymbol(ExpressionOf(plan => plan.UsedArea()), name: "used_area");
            Result<Success, ErrorCode, Error<ErrorCode>> ok = model.Add(_usedArea);
            if (ok is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
                return ok;
            }
        }
        if (_wasteArea is null) {
            _wasteArea = new LinearExpressionSymbol(ExpressionOf(plan => plan.WasteArea()), name: "waste_area");
            Result<Success, ErrorCode, Error<ErrorCode>> ok = model.Add(_wasteArea);
            if (ok is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
                return ok;
            }
        }
        if (_consumedArea is null) {
            _consumedArea = new LinearExpressionSymbol(
                ExpressionOf(plan => Results.Ok(plan.Pattern.Region.Area)), name: "consumed_area");
            Result<Success, ErrorCode, Error<ErrorCode>> ok = model.Add(_consumedArea);
            if (ok is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
                return ok;
            }
        }
        if (_consumedHeight is null) {
            _consumedHeight = new LinearExpressionSymbol(
                ExpressionOf(plan => Results.Ok(plan.Pattern.Region.Height)), name: "consumed_height");
            Result<Success, ErrorCode, Error<ErrorCode>> ok = model.Add(_consumedHeight);
            if (ok is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
                return ok;
            }
        }
        return Results.OkInstance;
    }

    /// <summary>
    /// 由每个方案的某一面积/长度量构造 `Σ 系数_j · y_j` / Build `Σ coefficient_j · y_j` from one quantity of each plan.
    /// </summary>
    /// <param name="coefficientOf">从方案取系数的函数 / The function taking the coefficient from a plan.</param>
    /// <returns>可变线性多项式 / A mutable linear polynomial.</returns>
    /// <remarks>
    /// **系数取不到量（单位不一致）时按零处理**：`WasteArea()` / `UsedArea()` 在量纲不可加时会失败，而主问题必须能注册。
    /// 代价是**一个坏图案会让它的贡献静默消失**——所以调用方必须先 `Pattern2.Validate` 过一遍。这条代价写在这里，不藏起来。
    ///
    /// **A coefficient that cannot be read (unit mismatch) counts as zero**: `WasteArea()` / `UsedArea()` fail when the dimensions are not addable, and the master problem
    /// must still register. The price is that **a broken pattern silently loses its contribution** — so callers must run `Pattern2.Validate` first. That price is written
    /// here rather than hidden.
    /// </remarks>
    private MutableLinearPolynomial<Flt64> ExpressionOf(
        System.Func<CuttingPlan2<Flt64>,
            Result<Quantity<Flt64>, ErrorCode, Error<ErrorCode>>> coefficientOf) {
        var monomials = new List<LinearMonomial<Flt64>>();
        for (int index = 0; index < Plans.Count; index++) {
            Result<Quantity<Flt64>, ErrorCode, Error<ErrorCode>> coefficient = coefficientOf(Plans[index]);
            if (coefficient is not Ok<Quantity<Flt64>, ErrorCode, Error<ErrorCode>> ok) {
                continue;
            }
            monomials.Add(new LinearMonomial<Flt64>(ok.Value.Value, _usage![index]));
        }
        return Csp2dProduceSupport.SafeCsp2dPolynomial(monomials);
    }
}
