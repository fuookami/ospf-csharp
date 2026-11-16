#nullable enable

namespace Fuookami.Ospf.Core.Model.Intermediate;

/// <summary>原生接口需要区分的输入形状 / Input shapes distinguished by native interfaces.</summary>
/// <remarks>
/// 对齐的 `FunctionInputShape`。
///
/// **`Unknown` 不是"其他"，而是"还不知道"**——**它与 `LinearExpression` 的区别决定了
/// 原生展开能不能被允许：`Unknown` 一律走保守路径。**
///
/// Following its `FunctionInputShape`.
///
/// **`Unknown` is not "other" but "not yet known"** — **the difference from `LinearExpression` decides whether native
/// lowering is permitted at all: `Unknown` always takes the conservative path.**
/// </remarks>
public enum FunctionInputShape {
    /// <summary>单个变量 / A single variable.</summary>
    SingleVariable,
    /// <summary>线性表达式 / A linear expression.</summary>
    LinearExpression,
    /// <summary>二次表达式 / A quadratic expression.</summary>
    QuadraticExpression,
    /// <summary>未知 / Unknown.</summary>
    Unknown,
}

/// <summary>分段连接处的连续性 / Continuity at segment boundaries.</summary>
/// <remarks>
/// 对齐基线 `PiecewiseContinuity`。
///
/// **`Discontinuous` 与 `Unknown` 是两件事**：**前者是"知道有断点"，后者是"没查过"。**
/// **把后者当后者处理会拒绝本可展开的函数；把前者当后者处理会生成一条断点处的错误行。**
///
/// Following its `PiecewiseContinuity`.
///
/// **`Discontinuous` and `Unknown` differ**: **the former means "a break is known", the latter "nobody looked".**
/// **Treating the latter as the former rejects functions that could expand; treating the former as the latter emits a
/// wrong row at the break.**
/// </remarks>
public enum PiecewiseContinuity {
    /// <summary>连续 / Continuous.</summary>
    Continuous,
    /// <summary>不连续 / Discontinuous.</summary>
    Discontinuous,
    /// <summary>未知 / Unknown.</summary>
    Unknown,
}

/// <summary>函数结果的使用位置 / Where a function result is used.</summary>
/// <remarks>
/// 对齐基线 `FunctionUsageLocation`。
///
/// **`Nested` 与 `External` 排在 `ObjectiveAndConstraint` <b>之前</b>，顺序是有意义的**：
/// **一个既出现在目标又出现在约束、同时被别的结构引用的函数，
/// 它的"位置"应当是被引用——因为那是最强的保留理由。**
/// **把顺序调换会让一个被引用的函数被报成"仅目标"，而调用方据此省掉结果变量，模型就错了。**
///
/// Following its `FunctionUsageLocation`.
///
/// **`Nested` and `External` come <b>before</b> `ObjectiveAndConstraint`, and the order carries meaning**: **a function
/// that appears in both, and is also referenced by another structure, should report as referenced — that is the
/// strongest reason to retain it.**
/// **Reordering makes a referenced function report as "objective only", the caller then drops the result variable, and
/// the model is wrong.**
/// </remarks>
public enum FunctionUsageLocation {
    /// <summary>仅目标 / Objective only.</summary>
    ObjectiveOnly,
    /// <summary>仅约束 / Constraint only.</summary>
    ConstraintOnly,
    /// <summary>目标与约束都有 / Both objective and constraint.</summary>
    ObjectiveAndConstraint,
    /// <summary>被其他结构引用 / Referenced by another structure.</summary>
    Nested,
    /// <summary>外部引用 / Externally referenced.</summary>
    External,
    /// <summary>未被使用 / Unused.</summary>
    Unused,
}

/// <summary>
/// 原生展开所需的使用语境 / The usage context native lowering needs.
/// </summary>
/// <remarks>
/// 对齐的 `FunctionUsageSummary`。
///
/// **四个布尔是"收集到的事实"，三个派生属性是"据此做的决定"。**
/// **分开它们是有意的：** **事实由调用方填，决定由这里算——
/// 于是"哪些情形要保留结果变量"这条规则只有<b>一处</b>。**
///
/// **`Location` 的判定顺序不可交换**（见 <see cref="FunctionUsageLocation"/>）：
/// **`Nested` 与 `External` 优先，因为它们是最强的保留理由。**
///
/// **`RequiresResultVariable` 里 `(InObjective &amp;&amp; InConstraint)` 是<b>冗余</b>的**：
/// **`InConstraint` 已经覆盖了它。**
/// **基线保留它，因为它把"同时出现在两边"这个具体情形写在了表达式里——
/// 读的人不必回去看 `InConstraint` 是否包含了那一支。**
/// **这种冗余不是错误，但必须标出来，否则后来的人会以为漏了条件。**
///
/// **`SupportsObjectiveOnlyNative` 要求 `SingleVariable`**：
/// **目标里一个多变量的线性表达式无法被"仅目标"的原生接口表达——
/// 那个接口只接受单变量分段函数。**
///
/// Following its `FunctionUsageSummary`.
///
/// **The four booleans are "collected facts"; the three derived members are "decisions taken from them".**
/// **Keeping them apart is deliberate: the caller supplies facts and this type computes the decision, so the rule for
/// "when the result variable must be retained" lives in <b>one</b> place.**
///
/// **The `Location` precedence is not interchangeable** (see <see cref="FunctionUsageLocation"/>):
/// **`Nested` and `External` come first because they are the strongest reasons to retain.**
///
/// **`(InObjective &amp;&amp; InConstraint)` inside `RequiresResultVariable` is <b>redundant</b>**.
/// **`InConstraint` already covers it.**
/// **The code keeps it because it writes that specific case into the expression, so a reader need not go back and
/// check whether `InConstraint` subsumes it.** **That redundancy is not a mistake, but it must be marked, or the next
/// reader will think a condition is missing.**
///
/// **`SupportsObjectiveOnlyNative` demands `SingleVariable`**: **a multi-variable linear expression in the objective
/// cannot be expressed by the "objective only" native interface — that interface takes a single-variable piecewise
/// function.**
/// </remarks>
/// <param name="InObjective">出现在有效目标项中 / Appears in an effective objective term.</param>
/// <param name="InConstraint">出现在外部约束中 / Appears in an external constraint.</param>
/// <param name="NestedAsInput">被另一个结构引用 / Referenced by another structure.</param>
/// <param name="ExternallyReferenced">存在显式引用或初值 / Has an explicit reference or initial value.</param>
/// <param name="InputShape">输入形状 / The input shape.</param>
/// <param name="Continuity">连续性 / The continuity.</param>
public sealed record FunctionUsageSummary(
    bool InObjective = false,
    bool InConstraint = false,
    bool NestedAsInput = false,
    bool ExternallyReferenced = false,
    FunctionInputShape InputShape = FunctionInputShape.Unknown,
    PiecewiseContinuity Continuity = PiecewiseContinuity.Unknown) {

    /// <summary>函数结果的使用位置 / Where the function result is used.</summary>
    public FunctionUsageLocation Location =>
        NestedAsInput ? FunctionUsageLocation.Nested
            : ExternallyReferenced ? FunctionUsageLocation.External
            : InObjective && InConstraint ? FunctionUsageLocation.ObjectiveAndConstraint
            : InObjective ? FunctionUsageLocation.ObjectiveOnly
            : InConstraint ? FunctionUsageLocation.ConstraintOnly
            : FunctionUsageLocation.Unused;

    /// <summary>是否必须保留结果变量 / Whether the result variable must be retained.</summary>
    /// <remarks>
    /// **`InObjective &amp;&amp; InConstraint` 是冗余项**（**`InConstraint` 已覆盖**），
    /// **保留它是为了让这个具体情形在表达式里可见。**
    ///
    /// **The `InObjective &amp;&amp; InConstraint` term is redundant** (**`InConstraint` already covers it**), **kept so that
    /// case stays visible in the expression.**
    /// </remarks>
    public bool RequiresResultVariable =>
        InConstraint || NestedAsInput || ExternallyReferenced || (InObjective && InConstraint);

    /// <summary>是否满足仅目标函数原生接口的条件 / Whether objective-only native lowering is supported.</summary>
    public bool SupportsObjectiveOnlyNative() =>
        Location == FunctionUsageLocation.ObjectiveOnly &&
        !NestedAsInput &&
        !ExternallyReferenced &&
        InputShape == FunctionInputShape.SingleVariable;
}
