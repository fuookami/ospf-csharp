#nullable enable

using Fuookami.Ospf.Core.Analysis;
using Fuookami.Ospf.Core.Solver.Report;
using System;

namespace Fuookami.Ospf.Core.Analysis;

/// <summary>
/// 公共诊断证据允许的稳定来源 / Stable sources allowed in public diagnostic evidence.
/// </summary>
/// <remarks>
/// **这个类型刻意不含求解器行、列、生成变量或后端句柄**：
/// `StableId` 要能当缓存键，也要能跨后端对照——**一旦把行号塞进来，换个后端就对不上了。**
///
/// 补**：这是 `core.analysis` 里**不依赖原生后端**的入口之一。§九第 6 条说该模块缺 18 个文件，
/// 而多数分析器要 IIS、活动约束、扰动重解这些**只有真后端给得出**的证据；`DiagnosticSource` / `ActivityStatus` /
/// `ActivityEvidence` 三个不需要，所以先做这三个。
///
/// **Solver rows, columns, generated variables and backend handles are deliberately absent**: `StableId` has to
/// work as a cache key and across backends — **a row number in it would stop matching the moment the backend changed.** ****: this is one of the `core.analysis` entry
/// points that **does not need a native backend**. §九 item 6 says the module is 18 files short, and most analyzers need IIS, active constraints or perturbation
/// re-solving — evidence **only a real backend produces**; these three do not, so they come first.
/// </remarks>
public abstract record DiagnosticSource
{
    /// <summary>稳定序列化身份 / Stable serialized identity.</summary>
    public abstract string StableId { get; }

    /// <summary>稳定来源类型 / Stable source kind.</summary>
    public abstract string Kind { get; }

    /// <summary>原始 CP 约束 / Original CP constraint.</summary>
    /// <param name="Id">约束 ID / Constraint ID.</param>
    public sealed record Constraint(ConstraintId Id) : DiagnosticSource
    {
        /// <inheritdoc/>
        public override string Kind => "constraint";

        /// <inheritdoc/>
        public override string StableId => "constraint:" + Id.Value;
    }

    /// <summary>原始变量下界 / Lower bound of an original variable.</summary>
    /// <param name="VariableId">变量 ID / Variable ID.</param>
    public sealed record VariableLowerBound(VariableId VariableId) : DiagnosticSource
    {
        /// <summary>从下界引用构造 / Construct from a lower-bound reference.</summary>
        /// <param name="reference">下界引用 / Lower-bound reference.</param>
        /// <exception cref="ArgumentException">引用的不是下界 / The reference is not a lower bound.</exception>
        public VariableLowerBound(VariableBoundRef reference)
            : this(RequireSide(reference).VariableId) {
        }

        /// <inheritdoc/>
        public override string Kind => "variable-lower-bound";

        /// <inheritdoc/>
        public override string StableId => "variable:" + VariableId.Value + ":lower";

        private static VariableBoundRef RequireSide(VariableBoundRef reference) {
            ArgumentNullException.ThrowIfNull(reference);
            // **不是下界就拒**：静默接受会让"下界来源"指向一个上界，而 `StableId` 仍然说 lower。
            // **Refuse a non-lower bound**: accepting it silently points a "lower-bound source" at an upper bound while `StableId` still says lower.
            if (reference.Side != BoundSide.Lower) {
                throw new ArgumentException("下界来源要求 BoundSide.Lower / A lower-bound source requires BoundSide.Lower", nameof(reference));
            }
            return reference;
        }
    }

    /// <summary>原始变量上界 / Upper bound of an original variable.</summary>
    /// <param name="VariableId">变量 ID / Variable ID.</param>
    public sealed record VariableUpperBound(VariableId VariableId) : DiagnosticSource
    {
        /// <summary>从上界引用构造 / Construct from an upper-bound reference.</summary>
        /// <param name="reference">上界引用 / Upper-bound reference.</param>
        /// <exception cref="ArgumentException">引用的不是上界 / The reference is not an upper bound.</exception>
        public VariableUpperBound(VariableBoundRef reference)
            : this(RequireSide(reference).VariableId) {
        }

        /// <inheritdoc/>
        public override string Kind => "variable-upper-bound";

        /// <inheritdoc/>
        public override string StableId => "variable:" + VariableId.Value + ":upper";

        private static VariableBoundRef RequireSide(VariableBoundRef reference) {
            ArgumentNullException.ThrowIfNull(reference);
            if (reference.Side != BoundSide.Upper) {
                throw new ArgumentException("上界来源要求 BoundSide.Upper / An upper-bound source requires BoundSide.Upper", nameof(reference));
            }
            return reference;
        }
    }

    /// <summary>变量边界 / A variable bound.</summary>
    /// <param name="Ref">边界引用 / Bound reference.</param>
    public sealed record VariableBound(VariableBoundRef Ref) : DiagnosticSource
    {
        /// <summary>变量 ID / Variable identifier.</summary>
        public VariableId VariableId => Ref.VariableId;

        /// <summary>
        /// 证据种类，由边界方向派生 / The evidence kind, derived from the bound side.
        /// </summary>
        /// <remarks>
        /// **这里原本返回固定的 `"variable-bound"`，而契约里没有这个词。**
        /// 契约的 `[evidence-kind]` 只有五个：`constraint`、`variable-lower-bound`、
        /// `variable-upper-bound`、`sparse-domain`、`objective-target`。
        ///
        /// **更要紧的是它与自己的 `StableId` 不一致**：`VariableBound` 的 `StableId` 是
        /// `variable:&lt;id&gt;:lower|upper`，**和 `VariableLowerBound` / `VariableUpperBound` 完全一样。**
        /// **于是同一份证据经 `InfeasibilityMember` 中转一次，`StableId` 不变而 `Kind` 变了**——
        /// **按 (stable_id, kind) 做跨语言比对时，结果取决于代码路径走了哪一支。**
        ///
        /// **This used to return a fixed `"variable-bound"`, and the contract has no such word.** The contract's `[evidence-kind]` holds exactly five.
        /// **Worse, it disagreed with its own `StableId`**: `VariableBound`'s `StableId` is `variable:&lt;id&gt;:lower|upper`, **identical to `VariableLowerBound` / `VariableUpperBound`.** **So one round trip through
 /// `InfeasibilityMember` keeps the `StableId` and changes the `Kind`** — **and a cross-language comparison keyed on (stable_id, kind) then depends on which branch the implementation took.**
        /// </remarks>
        public override string Kind => Ref.Side == BoundSide.Lower
            ? "variable-lower-bound"
            : "variable-upper-bound";

        /// <inheritdoc/>
        public override string StableId =>
            "variable:" + Ref.VariableId.Value + ":" + (Ref.Side == BoundSide.Lower ? "lower" : "upper");
    }

    /// <summary>稀疏整数值域 / Sparse integer-domain source.</summary>
    /// <param name="VariableId">变量 ID / Variable ID.</param>
    public sealed record SparseDomain(VariableId VariableId) : DiagnosticSource
    {
        /// <summary>从值域引用构造 / Construct from a domain reference.</summary>
        /// <param name="reference">值域引用 / Domain reference.</param>
        public SparseDomain(VariableDomainRef reference)
            : this((reference ?? throw new ArgumentNullException(nameof(reference))).VariableId) {
        }

        /// <summary>值域引用 / Domain reference.</summary>
        public VariableDomainRef Ref => new(VariableId);

        /// <inheritdoc/>
        public override string Kind => "sparse-domain";

        /// <inheritdoc/>
        public override string StableId => "variable:" + VariableId.Value + ":domain";
    }

    /// <summary>目标可行性分析使用的目标条件 / Objective target used by target-feasibility analysis.</summary>
    /// <param name="Target">目标条件 / Objective target.</param>
    public sealed record ObjectiveTargetSource(ObjectiveTarget Target) : DiagnosticSource
    {
        /// <inheritdoc/>
        public override string Kind => "objective-target";

        /// <inheritdoc/>
        public override string StableId => Target.StableId;
    }

    /// <summary>
    /// 投影为已有的不相容证据成员（若存在）/ Project to the existing incompatibility-evidence member when one exists.
    /// </summary>
    /// <returns>对应的不相容证据成员，不适用时为 <c>null</c> / The matching member, or <c>null</c> when not applicable.</returns>
    /// <remarks>
    /// **目标条件没有不相容成员**：目标不满足不是"某个约束不相容"，硬塞进去会让 IIS 报告多出一条不存在的成员。
    /// **An objective target has no incompatibility member**: a missed target is not "some constraint is inconsistent", and forcing one would add a member the IIS report
    /// never found.
    /// </remarks>
    public InfeasibilityMember? AsInfeasibilityMember() => this switch
    {
        Constraint constraint => new InfeasibilityMember.ConstraintMember(constraint.Id),
        VariableBound bound => new InfeasibilityMember.VariableBoundMember(bound.Ref),
        VariableLowerBound lower => new InfeasibilityMember.VariableBoundMember(
            new VariableBoundRef(lower.VariableId, BoundSide.Lower)),
        VariableUpperBound upper => new InfeasibilityMember.VariableBoundMember(
            new VariableBoundRef(upper.VariableId, BoundSide.Upper)),
        SparseDomain domain => new InfeasibilityMember.VariableDomainMember(domain.Ref),
        _ => null,
    };

    /// <summary>
    /// 从求解器报出的不可行成员反推诊断来源 / Recover a diagnostic source from a solver-reported infeasible member.
    /// </summary>
    /// <param name="member">不可行成员 / The infeasible member.</param>
    /// <returns>诊断来源 / The diagnostic source.</returns>
    /// <remarks>
    /// **这是 `ConflictAnalyzer.sourceOf` 的实现。** 求解器只说"这个约束 / 这个边界 / 这个值域让它不可行"，
    /// 冲突分析要的是"哪个诊断来源"。**两套类型各说各话，中间必须有这一次翻译。**
    ///
    /// **This is the implementation of `ConflictAnalyzer.sourceOf`.** The solver says "this constraint / this bound / this domain made it infeasible"; conflict analysis wants "which diagnostic source". **Two type families, and this
    /// translation is the only thing between them.**
    ///
    /// **边界方向不能丢。** `VariableBoundRef` 带着 `Side`，**丢掉它就等于把"下界太紧"和"上界太紧"报成同一件事**，
    /// 而修法一个要放宽下界、一个要放宽上界。
    ///
    /// **The bound side cannot be lost.** `VariableBoundRef` carries a `Side`; **dropping it reports "the lower bound is too tight" and "the upper bound is too tight" as one thing**, while one fix relaxes the
    /// lower bound and the other the upper.
    /// </remarks>
    public static DiagnosticSource FromInfeasibilityMember(InfeasibilityMember member) {
        ArgumentNullException.ThrowIfNull(member);

        return member switch
        {
            InfeasibilityMember.ConstraintMember constraint => new Constraint(constraint.Id),
            InfeasibilityMember.VariableBoundMember bound
                => new VariableBound(bound.Ref),
            InfeasibilityMember.VariableDomainMember domain
                => new SparseDomain(domain.Ref.VariableId),
            _ => throw new ArgumentOutOfRangeException(
                nameof(member),
                "未知的不可行成员类型 / Unknown infeasible-member type: " + member.GetType().Name),
        };
    }
}
