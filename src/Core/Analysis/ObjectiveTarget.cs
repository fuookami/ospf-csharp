#nullable enable

using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Core.Analysis;

/// <summary>
/// 目标可行性条件方向 / Direction of an objective-feasibility target.
/// </summary>
public enum ObjectiveTargetRelation {
    /// <summary>目标值不小于边界 / Objective must be at least the bound.</summary>
    AtLeast,
    /// <summary>目标值不大于边界 / Objective must be at most the bound.</summary>
    AtMost,
}

/// <summary>
/// 不可变目标边界 / Immutable objective bound.
/// </summary>
/// <remarks>
/// 目标仅携带稳定模型身份和有限数值；临时约束由后续分析阶段派生。
/// The target carries only stable model identity and a finite value; temporary constraints are
/// derived by later analysis stages.
/// </remarks>
public abstract record ObjectiveTarget(
    ObjectiveId ObjectiveId,
    Flt64 Value) {
    /// <summary>边界方向 / Bound direction.</summary>
    public abstract ObjectiveTargetRelation Relation { get; }

    /// <summary>稳定诊断键 / Stable diagnostic key.</summary>
    public string StableId => $"objective-target:{ObjectiveId.Value}:{Relation}:{Value}";

    /// <summary>校验目标身份和有限边界 / Validate target identity and finite bound.</summary>
    /// <returns>结构化校验结果 / Structured validation result.</returns>
    public Try Validate() {
        if (string.IsNullOrWhiteSpace(ObjectiveId.Value) ||
            Relation is not (ObjectiveTargetRelation.AtLeast or ObjectiveTargetRelation.AtMost) ||
            !IsFinite(Value)) {
            return Results.Failed<Success>(
                new Err<ErrorCode>(
                    ErrorCode.IllegalArgument,
                    "ObjectiveTarget 身份不能为空且边界必须有限 / ObjectiveTarget identity must not be blank and bound must be finite"));
        }
        return Results.Ok(Results.SuccessInstance);
    }

    /// <summary>检查具体目标值是否满足条件 / Check whether an objective value satisfies this target.</summary>
    /// <param name="objectiveValue">待检查的目标值 / Objective value to check.</param>
    /// <returns>是否满足 / Whether satisfied.</returns>
    public bool IsSatisfied(Flt64 objectiveValue) {
        if (!IsFinite(objectiveValue)) {
            return false;
        }
        return Relation switch {
            ObjectiveTargetRelation.AtLeast => objectiveValue.CompareTo(Value) >= 0,
            ObjectiveTargetRelation.AtMost => objectiveValue.CompareTo(Value) <= 0,
            _ => false,
        };
    }

    /// <summary>返回到目标边界的有符号距离，正值表示满足 / Return signed distance; positive means satisfied.</summary>
    /// <param name="objectiveValue">待测目标值 / Objective value to measure.</param>
    /// <returns>有符号距离 / Signed distance.</returns>
    public Flt64 SignedDistance(Flt64 objectiveValue) =>
        Relation switch {
            ObjectiveTargetRelation.AtLeast => objectiveValue.Minus(Value),
            ObjectiveTargetRelation.AtMost => Value.Minus(objectiveValue),
            _ => Flt64.Zero,
        };

    /// <summary>构造绝对下界目标 / Build an absolute lower-bound target.</summary>
    /// <param name="objectiveId">目标 ID / Objective ID.</param>
    /// <param name="value">下界值 / Lower-bound value.</param>
    /// <returns>下界目标 / Lower-bound target.</returns>
    public static AtLeast CreateAtLeast(ObjectiveId objectiveId, Flt64 value) => new(objectiveId, value);

    /// <summary>构造绝对上界目标 / Build an absolute upper-bound target.</summary>
    /// <param name="objectiveId">目标 ID / Objective ID.</param>
    /// <param name="value">上界值 / Upper-bound value.</param>
    /// <returns>上界目标 / Upper-bound target.</returns>
    public static AtMost CreateAtMost(ObjectiveId objectiveId, Flt64 value) => new(objectiveId, value);

    /// <summary>
    /// 构造相对下界目标 / Build a relative lower-bound target.
    /// </summary>
    /// <param name="objectiveId">目标 ID / Objective ID.</param>
    /// <param name="baseline">基线值 / The baseline value.</param>
    /// <param name="fraction">相对比例 / The relative fraction.</param>
    /// <returns>下界目标，值为 `baseline × (1 + fraction)` / A lower-bound target valued at `baseline × (1 + fraction)`.</returns>
    /// <remarks>
    /// **这是 `relativeAtLeast` / `atLeastRelative` 的移植。**
    ///
    /// **非有限输入必须在构造时就拒绝。** 一个 NaN 的比例会算出一个 NaN 的目标值，
    /// **而那个值此后一路合法地传下去**：`ObjectiveTargetBounds.IntegerBound` 会返回 `null`，
    /// 分析于是报 `Unsupported`——**"这个目标无法表达成整数边界"。**
    /// **那是一个错误的诊断**：真正的问题是调用方给了一个 NaN，**不是这个目标不可表达。**
    ///
    /// **Non-finite input has to be rejected at construction.** A NaN fraction yields a NaN target value, **and that value then travels onward looking legitimate**: `IntegerBound` returns `null` and the
    /// analysis reports `Unsupported` — **"this target cannot be expressed as an integer bound".** **That diagnosis is wrong**: the caller passed a NaN, **not an inexpressible target.**
    /// </remarks>
    public static AtLeast CreateRelativeAtLeast(ObjectiveId objectiveId, Flt64 baseline, Flt64 fraction) {
        RequireFinite(baseline, nameof(baseline), "Relative target values must be finite");
        RequireFinite(fraction, nameof(fraction), "Relative target values must be finite");
        return new AtLeast(objectiveId, new Flt64(baseline.ToDouble() * (1.0 + fraction.ToDouble())));
    }

    /// <summary>构造相对上界目标 / Build a relative upper-bound target.</summary>
    /// <param name="objectiveId">目标 ID / Objective ID.</param>
    /// <param name="baseline">基线值 / The baseline value.</param>
    /// <param name="fraction">相对比例 / The relative fraction.</param>
    /// <returns>上界目标 / An upper-bound target.</returns>
    public static AtMost CreateRelativeAtMost(ObjectiveId objectiveId, Flt64 baseline, Flt64 fraction) {
        RequireFinite(baseline, nameof(baseline), "Relative target values must be finite");
        RequireFinite(fraction, nameof(fraction), "Relative target values must be finite");
        return new AtMost(objectiveId, new Flt64(baseline.ToDouble() * (1.0 + fraction.ToDouble())));
    }

    /// <summary>
    /// 构造预算单位下界目标 / Build a business-unit lower-bound target.
    /// </summary>
    /// <param name="objectiveId">目标 ID / Objective ID.</param>
    /// <param name="baseline">基线值 / The baseline value.</param>
    /// <param name="delta">绝对增量 / The absolute delta.</param>
    /// <returns>下界目标，值为 `baseline + delta` / A lower-bound target valued at `baseline + delta`.</returns>
    /// <remarks>
    /// **相对与预算单位的区别只有一处，而那一处决定了能不能用**：
    /// **相对按比例缩放**（基线 100 的 5% 是 5），**预算单位按绝对量平移**（不管基线多大都是 5）。
    /// **把预算单位当成相对，基线一大就会放大十倍。**
    ///
    /// **The difference between relative and business-unit is one point, and that point decides usability**: **relative scales** (five percent of 100 is 5), **business-unit translates** (5 regardless of the
    /// baseline). **Treating a business-unit delta as a fraction multiplies it tenfold as soon as the baseline value is large.**
    /// </remarks>
    public static AtLeast CreateBusinessUnitAtLeast(ObjectiveId objectiveId, Flt64 baseline, Flt64 delta) {
        RequireFinite(baseline, nameof(baseline), "Business-unit target values must be finite");
        RequireFinite(delta, nameof(delta), "Business-unit target values must be finite");
        return new AtLeast(objectiveId, new Flt64(baseline.ToDouble() + delta.ToDouble()));
    }

    /// <summary>构造预算单位上界目标 / Build a business-unit upper-bound target.</summary>
    /// <param name="objectiveId">目标 ID / Objective ID.</param>
    /// <param name="baseline">基线值 / The baseline value.</param>
    /// <param name="delta">绝对增量 / The absolute delta.</param>
    /// <returns>上界目标 / An upper-bound target.</returns>
    public static AtMost CreateBusinessUnitAtMost(ObjectiveId objectiveId, Flt64 baseline, Flt64 delta) {
        RequireFinite(baseline, nameof(baseline), "Business-unit target values must be finite");
        RequireFinite(delta, nameof(delta), "Business-unit target values must be finite");
        return new AtMost(objectiveId, new Flt64(baseline.ToDouble() + delta.ToDouble()));
    }

    /// <summary>属性名后缀别名：`atLeastRelative` / The attribute-name alias `atLeastRelative`.</summary>
    /// <param name="objectiveId">目标 ID / Objective ID.</param>
    /// <param name="baseline">基线值 / The baseline value.</param>
    /// <param name="fraction">相对比例 / The relative fraction.</param>
    /// <returns>下界目标 / A lower-bound target.</returns>
    public static AtLeast CreateAtLeastRelative(ObjectiveId objectiveId, Flt64 baseline, Flt64 fraction) =>
        CreateRelativeAtLeast(objectiveId, baseline, fraction);

    /// <summary>属性名后缀别名：`atMostRelative` / The attribute-name alias `atMostRelative`.</summary>
    /// <param name="objectiveId">目标 ID / Objective ID.</param>
    /// <param name="baseline">基线值 / The baseline value.</param>
    /// <param name="fraction">相对比例 / The relative fraction.</param>
    /// <returns>上界目标 / An upper-bound target.</returns>
    public static AtMost CreateAtMostRelative(ObjectiveId objectiveId, Flt64 baseline, Flt64 fraction) =>
        CreateRelativeAtMost(objectiveId, baseline, fraction);

    /// <summary>属性名后缀别名：`atLeastBusinessUnit` / The attribute-name alias `atLeastBusinessUnit`.</summary>
    /// <param name="objectiveId">目标 ID / Objective ID.</param>
    /// <param name="baseline">基线值 / The baseline value.</param>
    /// <param name="delta">绝对增量 / The absolute delta.</param>
    /// <returns>下界目标 / A lower-bound target.</returns>
    public static AtLeast CreateAtLeastBusinessUnit(ObjectiveId objectiveId, Flt64 baseline, Flt64 delta) =>
        CreateBusinessUnitAtLeast(objectiveId, baseline, delta);

    /// <summary>属性名后缀别名：`atMostBusinessUnit` / The attribute-name alias `atMostBusinessUnit`.</summary>
    /// <param name="objectiveId">目标 ID / Objective ID.</param>
    /// <param name="baseline">基线值 / The baseline value.</param>
    /// <param name="delta">绝对增量 / The absolute delta.</param>
    /// <returns>上界目标 / An upper-bound target.</returns>
    public static AtMost CreateAtMostBusinessUnit(ObjectiveId objectiveId, Flt64 baseline, Flt64 delta) =>
        CreateBusinessUnitAtMost(objectiveId, baseline, delta);

    /// <summary>拒绝非有限值 / Reject a non-finite value.</summary>
    /// <param name="value">待检的值 / The value to check.</param>
    /// <param name="parameter">参数名 / The parameter name.</param>
    /// <param name="message">说明 / The message.</param>
    private static void RequireFinite(Flt64 value, string parameter, string message) {
        if (!double.IsFinite(value.ToDouble())) {
            throw new ArgumentOutOfRangeException(parameter, message);
        }
    }

    /// <summary>目标要求的最小值 / Minimum objective value required by the target.</summary>
    public sealed record AtLeast(
        ObjectiveId ObjectiveId,
        Flt64 Value) : ObjectiveTarget(ObjectiveId, Value) {
        /// <inheritdoc/>
        public override ObjectiveTargetRelation Relation => ObjectiveTargetRelation.AtLeast;
    }

    /// <summary>目标允许的最大值 / Maximum objective value allowed by the target.</summary>
    public sealed record AtMost(
        ObjectiveId ObjectiveId,
        Flt64 Value) : ObjectiveTarget(ObjectiveId, Value) {
        /// <inheritdoc/>
        public override ObjectiveTargetRelation Relation => ObjectiveTargetRelation.AtMost;
    }

    private static bool IsFinite(Flt64 value) =>
        ((IRealNumber<Flt64>)value).IsSelfWithinBounds();
}
