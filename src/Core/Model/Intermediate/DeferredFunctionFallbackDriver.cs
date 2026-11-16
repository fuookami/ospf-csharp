#nullable enable

using System;
using System.Collections.Generic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Math.Symbol.Inequality;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Try = Fuookami.Ospf.Utils.Functional.Result<Fuookami.Ospf.Utils.Functional.Success, Fuookami.Ospf.Utils.Error.ErrorCode, Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Core.Model.Intermediate;

/// <summary>
/// 接收 fallback 约束并支持事务回滚的目标 / A target that accepts fallback rows and supports rollback.
/// </summary>
/// <remarks>
/// 对齐的 `DeferredFunctionFallbackTarget`。
///
/// **`Append` 与 `Rollback` 返回的都是"可能失败"，而这不是形式主义：**
/// **目标可能是一个已经写完的模型层（追加会被拒绝）、
/// 也可能是一个固定容量的缓冲区（追加会溢出）。**
/// **驱动必须能处理这些失败，所以它们不能是 `void`。**
///
/// **Aligned with its `DeferredFunctionFallbackTarget`.**
///
/// **Both `Append` and `Rollback` report possible failure, and that is not formalism**: **the target may be a finished model
/// layer that refuses appends, or a fixed-capacity buffer that overflows.** **The driver must handle those, so neither can
/// return void.**
/// </remarks>
public interface IDeferredFunctionFallbackTarget
{
    /// <summary>当前约束行数 / The current constraint row count.</summary>
    int ConstraintCount { get; }

    /// <summary>追加约束 / Append rows.</summary>
    Result<Success, ErrorCode, Error<ErrorCode>> Append(
        IReadOnlyList<LinearInequality<Flt64>> constraints);

    /// <summary>回滚到指定行数 / Roll back to the given row count.</summary>
    Result<Success, ErrorCode, Error<ErrorCode>> Rollback(int constraintCount);

    /// <summary>
    /// **记下一段刚追加出来的区域** / Record one slice that was just appended.
    /// </summary>
    /// <remarks>
    /// 这个成员存在的理由，是<b>驱动返回的区域列表没有消费者</b>：
    ///
    /// **驱动把它们放进返回值，而返回值此前唯一的消费者是测试。**
    /// **于是"某个结构已经物化过"这个事实只存在于一个即将被丢弃的列表里，
    /// 而 `DeferredFunctionConstraintRegions` 永远是空的——**第三个过滤条件因此永远不会生效。**
    ///
    /// **"过滤条件依赖的数据永远为空"是一种<b>看起来对</b>的错误：** **过滤逻辑正确、判据通过
    /// （因为判据自己造了那个非空集合），而真实路径上集合一直是空的。**
    ///
    /// **This member exists because <b>nothing consumed the driver's returned region list</b>: the driver put the
    /// regions in its return value, and the only consumer of that value was tests.** **So "this structure was already
    /// materialised" lived in a soon-to-be-discarded list while `DeferredFunctionConstraintRegions` stayed empty — and the
    /// third filter condition could therefore never fire.**
    ///
    /// **"The data a filter depends on is permanently empty" is a <b>plausible-looking</b> defect: the filtering logic is
    /// right and the criteria pass (because they build that non-empty set themselves) while on the real path the set is
    /// always empty.**
    /// </remarks>
    /// <param name="region">要记下的区域 / The region to record.</param>
    /// <returns>成功或错误 / Success or an error.</returns>
    Result<Success, ErrorCode, Error<ErrorCode>> RecordRegion(DeferredFunctionConstraintRegion region);
}

/// <summary>
/// 事务化物化一组延迟函数 fallback / Materialize a group of deferred-function fallbacks transactionally.
/// </summary>
/// <remarks>
/// 对齐的 `materializeDeferredFunctionFallbacks` 与 `rollbackErrors`。
///
/// **整个循环是一个事务：任一结构失败就回滚到<b>开始时的行数</b>，而不是回滚一步。**
///
/// **回滚目标是 `checkpoint`（循环开始前的行数），不是"上一个结构之前"。**
/// **差别在于：已经成功的那些结构也要被撤掉——
/// 否则调用方拿到一个"部分成功"的模型层，而它既不是全有也不是全无。**
///
/// **失败有三种结局，而第三种最容易被忽略：**
///
/// | 情况 | 结果 |
/// | --- | --- |
/// | **物化/追加失败，回滚成功** | **`Failed`，只带原始错误** |
/// | **物化/追加失败，回滚<b>也</b>失败** | **`Fatal`，带<b>两个</b>错误** |
/// | **全部成功** | **`Ok`，每个结构一个区域** |
///
/// **第二种从 `Failed` 升级成 `Fatal` 的理由：**
/// **`Failed` 的语义是"可以再试一次"，而回滚失败意味着模型层停在一个未知状态——
/// 再试一次不会更糟，但也不会更好。所以它必须被当作不可恢复。**
///
/// **两个错误都要留：** **原始错误说明"为什么开始回滚"，
/// 回滚错误说明"为什么现在回不去"。** **只留一个，下一次排查就得靠猜。**
///
/// **The whole loop is one transaction: any structure failing rolls back to <b>the starting row count</b>, not one step.**
///
/// **The rollback target is `checkpoint` (the count before the loop), not "before the previous structure".**
/// **The difference is that already-successful structures are undone too — otherwise the caller receives a
/// "partly successful" model layer that is neither all nor nothing.**
///
/// **Failure has three outcomes, and the third is the easiest to overlook:**
///
/// | Case | Outcome |
/// | --- | --- |
/// | **materialize/append fails, rollback succeeds** | **`Failed` with the original error only** |
/// | **materialize/append fails and rollback <b>also</b> fails** | **`Fatal` with <b>both</b> errors** |
/// | **everything succeeds** | **`Ok` with one region per structure** |
///
/// **The second is upgraded from `Failed` to `Fatal` because `Failed` means "retryable", while a failed rollback leaves
/// the model layer in an unknown state — retrying is neither worse nor better, so it must count as unrecoverable.**
///
/// **Both errors are kept**: **the original says "why the rollback started", the rollback error says "why it cannot get
/// back now".** **Keeping one leaves the next investigation guessing.**
/// </remarks>
/// <summary>
/// **把 `LinearMechanismModel` 接到物化驱动上的适配器** /
/// The adapter that connects `LinearMechanismModel` to the materialisation driver.
/// </summary>
/// <remarks>
/// 对齐。
///
/// **这里缺的不是能力，而是<b>接线</b>。** **驱动、事务、回滚、五个 materializer 都已经写好了，
/// 而它们此前只在测试里被调用过——产品代码零调用。**
///
/// **"能力齐备但没有调用方"是一种不会报错的缺口：** **代码全部编译、判据全部绿，
/// 因为判据直接调那个能力；而真实路径从来不经过它。**
///
/// **它与"读得有、写不出"是同一族、方向相反：** **那里缺的是一个实现，
/// 这里是实现齐全而<b>没有入口</b>。** **后者的判据必须是"谁调用了它"，而不是"它对不对"。**
///
/// **What is missing here is not a capability but <b>the wiring</b>.** **The driver, the
/// transaction, the rollback and five materializers — and they were called only from tests, never from product
/// code.**
///
/// **"Everything implemented, nothing calls it" is a gap that cannot report itself: it all compiles and the criteria are
/// green, because the criteria call the capability directly, while the real path never reaches it.**
///
/// **Same family as "readable but unwritable", opposite direction: there an implementation was missing; here it
/// is complete and has <b>no caller</b>.** **The criterion for the latter has to ask "who calls it", not "is it
/// correct".**
/// </remarks>
public sealed class LinearMechanismFallbackTarget : IDeferredFunctionFallbackTarget
{
    private readonly LinearMechanismModel<Flt64> _model;

    /// <summary>用要物化的模型构造 / Construct over the model to materialise into.</summary>
    /// <param name="model">目标模型 / The target model.</param>
    public LinearMechanismFallbackTarget(LinearMechanismModel<Flt64> model) {
        ArgumentNullException.ThrowIfNull(model);
        _model = model;
    }

    /// <inheritdoc />
    public int ConstraintCount => _model.NumConstraints;

    /// <inheritdoc />
    public Result<Success, ErrorCode, Error<ErrorCode>> Append(
        IReadOnlyList<LinearInequality<Flt64>> constraints) {
        ArgumentNullException.ThrowIfNull(constraints);

        foreach (LinearInequality<Flt64> relation in constraints) {
            Try added = _model.AddConstraint(relation, name: null, from: ((Fuookami.Ospf.Core.Symbol.IIntermediateSymbol Symbol, bool Direction)?)null);
            if (added is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
                // **一条失败就把它已经追加的那几条留给调用方的 `Rollback` 处理**，
                // **因为回滚点是由驱动持有的 `checkpoint`，不是这里。**
                // **One failure leaves the already-appended rows to the caller's `Rollback`, because the rollback point
                // is the driver's `checkpoint`, not this method's.**
                return Results.Failed<Success>(new Err<ErrorCode>(
                    ErrorCode.OREngineModelingException,
                    "线性机制模型拒绝了一条 fallback 约束 / The linear mechanism model rejected a fallback constraint"));
            }
        }

        return Results.OkInstance;
    }

    /// <inheritdoc />
    public Result<Success, ErrorCode, Error<ErrorCode>> RecordRegion(DeferredFunctionConstraintRegion region) {
        _model.RecordDeferredFunctionConstraintRegion(region);
        return Results.OkInstance;
    }

    /// <inheritdoc />
    public Result<Success, ErrorCode, Error<ErrorCode>> Rollback(int constraintCount) {
        if (constraintCount < 0 || constraintCount > _model.NumConstraints) {
            // **越界的回滚点是一个真错误，而不是"没有东西要回滚"：**
            // **它说明持有 checkpoint 的那一方算错了行数，而静默接受会让模型停在一个既不是开始也不是结束的状态。**
            // **An out-of-range checkpoint is a real error, not "nothing to roll back": it means whoever holds the
            // checkpoint miscounted, and accepting it silently would leave the model in a state that is neither the start
            // nor the end.**
            return Results.Failed<Success>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                $"回滚行数越界：{constraintCount} / Rollback row count out of range"));
        }

        _model.TruncateConstraintsTo(constraintCount);
        return Results.OkInstance;
    }
}

/// <summary>
/// **延迟物化的入口判定** / The entry decision for deferred materialisation.
/// </summary>
/// <remarks>
/// 这里是延迟物化的<b>第一个守卫</b>。
///
/// **个函数有四段，这里只做第一段，理由写明：**
///
/// | 段 | 依赖 | 这里 |
/// | --- | --- | --- |
/// | `policy == EAGER` → 原样返回 | **只有策略属性** | ✅ |
/// | 收集已被区域占用的结果键 | `DeferredFunctionConstraintRegions` | ❌ 模型上没有 |
/// | 过滤待处理结构 | `DeferredFunctionStructures` | ❌ 模型上没有 |
/// | 逐个校验并物化 | 上面两样 + 驱动 | ❌ |
///
/// **第一段是四段里唯一<b>只需要一个只读属性</b>的一段。** **所以它可以独立成立、独立可测，
/// 而不需要先给模型补那两样集合。**
///
/// **"把能单独成立的一段先做出来"不是拆碎了交付，而是<b>让后面那两段的缺口保持清晰</b>：
/// 现在缺的是两个集合，而不是"整个函数"。**
///
/// **Follows the <b>first guard</b> of its `materializeMechanismFunctionFallbacks`.**
///
/// **That function has four parts and only the first is here, for a stated reason: it is the only part that needs
/// exactly one read-only property. So it can stand and be measured on its own, without first giving the model those two
/// collections.**
///
/// **"Do the one part that stands alone first" is not fragmenting a delivery — it keeps the gap for the remaining parts
/// <b>sharp</b>: what is missing is two collections, not "the whole function".**
/// </remarks>
public static class DeferredFunctionMaterializationGate
{
    /// <summary>
    /// **策略为 `Eager` 时不物化，原样返回** /
    /// With an `Eager` policy nothing is materialised and the model comes back unchanged.
    /// </summary>
    /// <remarks>
    /// **这一条判据的意义不在"返回自己"，而在<b>默认值的安全方向</b>：**
    /// **一个没有配置过策略的模型拿到的是 `Eager`，于是它永远走这条捷径，
    /// 而不会在没人要求的情况下走进延迟物化那条更复杂的路径。**
    ///
    /// **反过来写（默认 `Auto`）会让每个模型都尝试延迟——包括那些根本没有延迟结构的。
    /// 那不会错，但会让"延迟"从一个策略变成一种默认行为。**
    ///
    /// **Its point is not "returns itself" but <b>which way the default is safe</b>: a model that was never configured
    /// gets `Eager`, so it always takes this short path and never drifts into the more complicated materialisation route
    /// unasked.**
    ///
    /// **The opposite default (`Auto`) would make every model try deferring, including those with no deferred structures at
    /// all. That is not wrong, it just turns "deferring" from a policy into a default behaviour.**
    /// </remarks>
    /// <param name="model">待判定的模型 / The model to judge.</param>
    /// <returns>是否应该<b>跳过</b>物化 / Whether materialisation should be <b>skipped</b>.</returns>
    public static bool ShouldSkip(LinearMechanismModel<Flt64> model) {
        ArgumentNullException.ThrowIfNull(model);
        return model.FunctionExpansionPolicy == FunctionExpansionPolicy.Eager;
    }

    /// <summary>
    /// **挑出真正需要物化的结构** / Pick the structures that genuinely need materialising.
    /// </summary>
    /// <remarks>
    /// `materializeMechanismFunctionFallbacks` 的<b>第二段</b>，前两个条件在此处判定；
    /// 第三个条件同样在此处判定，<b>不存在临时开关</b>。
    ///
    /// **三个条件现在都在：**
    ///
    /// | 条件 | 依赖 | 批次 |
    /// | --- | --- | --- |
    /// | `supportsDeferredFallback` | 结构自身 | — |
    /// | 结果键不在 `nativeFunctionKeys` 里 | 调用方参数 | — |
    /// | 结果键不在已绑定区域里 | `DeferredFunctionConstraintRegions` | — |
    ///
    /// **为什么不留开关：** **开关的用途是"让缺口在被使用的那一刻出现"，而缺口一旦补上，它只剩两个状态里多余的一个。**
    /// **缺口补上之后，开关如果留着，就变成了一个<b>可以合法地物化两次</b>的入口——
    /// 那比没有开关更糟，因为它让错误看起来是被允许的。**
    ///
    /// **所以"临时开关"这个做法有一个必须履行的后半部分：补上之后把它删掉。**
    /// **只做前半部分的临时开关会沉淀成永久缺陷。**
    ///
    /// **The first two conditions and the third are all decided here, and <b>there is no temporary switch</b>.**
    ///
    /// **All three conditions are now present.**
    ///
    /// **Why the switch does not stay: its purpose was "make the gap appear at the moment
    /// it is used".** **Once the gap is closed, keeping the switch turns it into an entry point where materialising twice is
    /// <b>legal</b> — worse than no switch, because it makes the error look permitted.**
    ///
    /// **So "temporary switch" has a mandatory second half: delete it once the gap is closed.** **A switch that only gets
    /// the first half hardens into a permanent defect.**
    /// </remarks>
    /// <param name="model">所属模型 / The owning model.</param>
    /// <param name="nativeFunctionKeys">
    /// 由调用方负责原生建模的结果变量键 / Result keys whose native relations the caller builds.
    /// </param>
    /// <returns>待物化的结构 / The structures awaiting materialisation.</returns>
    public static IReadOnlyList<DeferredFunctionStructure> Pending(
        LinearMechanismModel<Flt64> model,
        IReadOnlySet<VariableItemKey> nativeFunctionKeys) {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(nativeFunctionKeys);

        // **已绑定区域的结果键：这些结构已经物化过，再物化一次会得到两条描述同一个 `y` 的约束。**
        // **Bound result keys: those structures were already materialised, and doing it again would leave two constraint
        // sets describing the same `y`.**
        var boundKeys = new HashSet<VariableItemKey>();
        foreach (DeferredFunctionConstraintRegion region in model.DeferredFunctionConstraintRegions) {
            IVariableItem? bound = region.Structure.ResultVariableOrNull();
            if (bound is not null) {
                boundKeys.Add(bound.Key);
            }
        }

        var pending = new List<DeferredFunctionStructure>();
        foreach (DeferredFunctionStructure structure in model.DeferredFunctionStructures) {
            if (!structure.SupportsDeferredFallback()) {
                continue;
            }
            IVariableItem? result = structure.ResultVariableOrNull();
            if (result is null) {
                continue;
            }
            if (nativeFunctionKeys.Contains(result.Key)) {
                continue;
            }
            if (boundKeys.Contains(result.Key)) {
                continue;
            }
            pending.Add(structure);
        }
        return pending;
    }
}

/// <summary>
/// **延迟函数物化的整体入口** / The whole entry point for deferred-function materialisation.
/// </summary>
/// <remarks>
/// 这里是剩余的两段（第三段的逐结构校验、第四段的复制并物化）。
///
/// **四段现在全在，而且它们在一个函数里：**
///
/// 1. **`Eager` 直接返回原模型**
/// 2. **过滤待处理结构**（三个条件）
/// 3. **逐结构校验输入边界**（这里）
/// 4. **复制模型、搬区域记录、调用驱动**（这里）
///
/// **第三段只对两类结构做事：** **`UnivariateLinearPiecewiseStructure` 与 `MaxStructure` 需要校验输入边界，
/// 别的结构 `ok`。** **这不是遗漏——只有这两个结构的 fallback 会用到创建时捕获的输入范围，
/// 而范围失效时 `bigM` 就不再成立。** **别的结构没有这个依赖，校验它们是在检查一件不存在的事。**
///
/// **第四段先复制再物化，而不是原地改：** **注释写着"保留源模型"。**
/// **而"保留"在这里有两层意思：** **失败时源模型不被污染（由事务保证），
/// 以及成功时源模型也不被改（这一点只能靠复制）。**
/// **两者都要，所以复制是必需的，不是优化。**
///
/// **The code's four parts all live in one function: return the model
/// unchanged when `Eager`; filter the pending structures (three conditions); validate
/// each structure's input bounds; copy the model, carry over the region records and call the driver)
/// **
///
/// **The third part touches only two structure kinds: `UnivariateLinearPiecewiseStructure` and `MaxStructure` need the
/// bounds check, everything else is `ok`.** **That is not an omission — only those two use the input range captured at
/// creation, and when the range goes stale their `bigM` no longer holds.** **Other kinds do not have that dependency, and
/// validating them would be checking something that does not exist.**
///
/// **The fourth part copies before materialising rather than mutating in place: its comment says "preserving
/// the source".** **That means two things here — a failure must not pollute the source (the transaction covers it) and
/// a success must not change it either (only copying covers that).** **Both are needed, so the copy is required, not an
/// optimisation.**
/// </remarks>
/// <summary>
/// **求解入口处的延迟物化判定** / The deferred-materialisation decision at the solve entry.
/// </summary>
/// <remarks>
/// 对齐的入口判断：**两个策略，任一为 `Eager` 就走常规求解。**
///
/// **为什么要把这个判断单独拿出来做成函数，而不是写进求解器：**
/// **它是"两个开关里更保守的那个说了算"这条语义的唯一实现点。**
/// **写进求解器之后，每个后端各写一遍——而四个后端各写一遍的东西，迟早会有一处写反。**
///
/// **而写反的后果是<b>不可见</b>的：** **配置说"立刻展开"、模型说"可以延迟"，如果实现写成"任一是非 Eager 就延迟"，
/// 那么"全局关掉延迟"这个动作在个别模型上就失效了，而求解仍然成功，只是走了另一条路径。**
///
/// **Follows its entry check: two policies, and if <b>either</b> is `Eager` the
/// ordinary solve runs.**
///
/// **Why this is a separate function rather than inline in the solver: it is the single point implementing "the more
/// conservative of the two switches wins".** **Inlined, each backend writes it again — and something written four times
/// eventually gets written backwards once.**
///
/// **Getting it backwards is <b>invisible</b>: with the config saying "expand immediately" and the model saying "I could
/// defer", an implementation reading "either non-Eager means defer" would make "turn deferring off globally" fail on
/// individual models — and the solve still succeeds, just by another route.**
/// </remarks>
public static class DeferredFunctionSolveEntry
{
    /// <summary>
    /// **是否应该尝试延迟物化** / Whether deferred materialisation should be attempted.
    /// </summary>
    /// <remarks>
    /// **两个来源，任一 `Eager` 就返回 `false`。**
    ///
    /// **判据必须把四种组合都测到，因为这是一个<b>二选一的短路</b>：
    /// 只测"两个都 Eager"与"两个都非 Eager"，会把"任一是 Eager"这个真正的规则漏掉。**
    ///
    /// **Two sources; if either is `Eager` this returns `false`.**
    ///
    /// **The criteria must cover all four combinations, because this is a <b>short-circuit between two</b>: testing only
    /// "both Eager" and "both non-Eager" would miss the actual rule, "either one".**
    /// </remarks>
    /// <param name="configPolicy">求解器配置上的策略 / The policy from the solver configuration.</param>
    /// <param name="modelPolicy">模型上的策略 / The policy from the model.</param>
    /// <returns>是否尝试物化 / Whether to attempt materialisation.</returns>
    public static bool ShouldAttempt(
        FunctionExpansionPolicy configPolicy,
        FunctionExpansionPolicy modelPolicy) =>
        configPolicy != FunctionExpansionPolicy.Eager
        && modelPolicy != FunctionExpansionPolicy.Eager;
}

public static class DeferredFunctionMaterialization
{
    /// <summary>
    /// **展开延迟节点，返回物化后的模型** / Expand the deferred nodes and return the materialised model.
    /// </summary>
    /// <param name="model">尚未编号的机制模型 / The mechanism model before solver indexing.</param>
    /// <param name="nativeFunctionKeys">
    /// 由调用方负责原生建模的结果变量键 / Result keys whose native relations the caller builds.
    /// </param>
    /// <returns>物化后的模型，或原模型 / The materialised model, or the source.</returns>
    public static Result<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>> Materialize(
        LinearMechanismModel<Flt64> model,
        IReadOnlySet<VariableItemKey>? nativeFunctionKeys = null) {
        ArgumentNullException.ThrowIfNull(model);

        // **第一段。**
        // **Part one.**
        if (DeferredFunctionMaterializationGate.ShouldSkip(model)) {
            return Results.Ok(model);
        }

        // **第二段。**
        // **Part two.**
        IReadOnlyList<DeferredFunctionStructure> pending = DeferredFunctionMaterializationGate.Pending(
            model,
            nativeFunctionKeys ?? new HashSet<VariableItemKey>());
        if (pending.Count == 0) {
            // **没有待处理结构时返回<b>原模型</b>，不是副本，
            // 这样调用方可以用引用相等判断"有没有发生物化"。**
            // **With nothing pending the <b>source</b> comes back, not a copy, so
            // a caller can use reference equality to tell whether materialisation happened.**
            return Results.Ok(model);
        }

        // **第三段：逐结构校验输入边界。**
        // **Part three: validate each structure's input bounds.**
        foreach (DeferredFunctionStructure structure in pending) {
            Result<Success, ErrorCode, Error<ErrorCode>> validation = structure switch {
                UnivariateLinearPiecewiseStructure piecewise => piecewise.ValidateFallbackInputBounds(),
                MaxStructure max => max.ValidateFallbackInputBounds(),
                _ => Results.OkInstance,
            };
            if (validation is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
                return Results.Failed<LinearMechanismModel<Flt64>>(
                    ((Failed<Success, ErrorCode, Error<ErrorCode>>)validation).Error);
            }
        }

        // **第四段：复制、搬区域记录、物化。**
        // **Part four: copy, carry the region records, materialise.**
        var copy = new LinearMechanismModel<Flt64>(
            parent: model.Parent,
            name: model.Name,
            constraints: model.LinearConstraints,
            objectFunction: model.ObjectFunction,
            tokens: model.Tokens,
            functionExpansionPolicy: model.FunctionExpansionPolicy,
            deferredFunctionStructures: model.DeferredFunctionStructures);
        // **副本必须继承来源快照，否则它<b>不能转成三元模型</b>——而接线正接在转换处。
        // 见 `InheritSourceSnapshotFrom` 的说明。**
        // **The copy must inherit the source snapshot, or it <b>cannot become a triad model</b> — and the wiring sits at the
        // conversion point. See the note on `InheritSourceSnapshotFrom`.**
        copy.InheritSourceSnapshotFrom(model);
        foreach (DeferredFunctionConstraintRegion region in model.DeferredFunctionConstraintRegions) {
            copy.RecordDeferredFunctionConstraintRegion(region);
        }

        Result<IReadOnlyList<DeferredFunctionConstraintRegion>, ErrorCode, Error<ErrorCode>> materialised =
            DeferredFunctionFallbackDriver.Materialize(
                pending,
                new LinearMechanismFallbackTarget(copy),
                DeferredFunctionFallbackMaterializers.Core);
        if (materialised is Failed<
                IReadOnlyList<DeferredFunctionConstraintRegion>, ErrorCode, Error<ErrorCode>> failure) {
            return Results.Failed<LinearMechanismModel<Flt64>>(failure.Error);
        }
        if (materialised is Fatal<
                IReadOnlyList<DeferredFunctionConstraintRegion>, ErrorCode, Error<ErrorCode>> fatal) {
            return new Fatal<LinearMechanismModel<Flt64>, ErrorCode, Error<ErrorCode>>(fatal.Errors);
        }

        // **区域由驱动通过 `target.RecordRegion` 记到模型上，这里不再重复记一遍。
        // 记两遍会让 `DeferredFunctionConstraintRegions` 里出现重复条目，
        // 而重复条目会让"已物化"的判断看起来正确、实际上把行号算了两遍。**
        // **The driver records regions onto the model through `target.RecordRegion`; they are not
        // recorded again here. Doing it twice would duplicate entries, and a duplicate entry would make the "already
        // materialised" check look right while counting the rows twice.**
        return Results.Ok(copy);
    }
}

public static class DeferredFunctionFallbackDriver
{
    /// <summary>事务化物化 / Materialize transactionally.</summary>
    public static Result<IReadOnlyList<DeferredFunctionConstraintRegion>, ErrorCode, Error<ErrorCode>>
        Materialize(
            IReadOnlyList<DeferredFunctionStructure> structures,
            IDeferredFunctionFallbackTarget target,
            IDeferredFunctionFallbackMaterializer materializer) {
        ArgumentNullException.ThrowIfNull(structures);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(materializer);

        int checkpoint = target.ConstraintCount;
        var regions = new List<DeferredFunctionConstraintRegion>(structures.Count);

        foreach (DeferredFunctionStructure structure in structures) {
            Result<DeferredFunctionFallbackConstraints, ErrorCode, Error<ErrorCode>> materialized =
                materializer.Materialize(structure);
            switch (materialized) {
                case Failed<DeferredFunctionFallbackConstraints, ErrorCode, Error<ErrorCode>> failure:
                    return Abort(target, checkpoint, failure.Error);
                case Ok<DeferredFunctionFallbackConstraints, ErrorCode, Error<ErrorCode>> ok:
                    // **先记下起始行号，再追加——顺序反过来的话区域会指到上一个结构去。**
                    // **Record the start index before appending; reversed, the region points at the previous structure.**
                    int first = target.ConstraintCount;
                    Result<Success, ErrorCode, Error<ErrorCode>> appended =
                        target.Append(ok.Value.Constraints);
                    if (appended is Failed<Success, ErrorCode, Error<ErrorCode>> appendFailure) {
                        return Abort(target, checkpoint, appendFailure.Error);
                    }

                    var region = new DeferredFunctionConstraintRegion(
                        structure, first, ok.Value.Constraints.Count);
                    Result<Success, ErrorCode, Error<ErrorCode>> recorded = target.RecordRegion(region);
                    if (recorded is Failed<Success, ErrorCode, Error<ErrorCode>> recordFailure) {
                        return Abort(target, checkpoint, recordFailure.Error);
                    }
                    regions.Add(region);
                    break;
            }
        }

        return new Ok<IReadOnlyList<DeferredFunctionConstraintRegion>, ErrorCode, Error<ErrorCode>>(
            regions);
    }

    private static Result<IReadOnlyList<DeferredFunctionConstraintRegion>, ErrorCode, Error<ErrorCode>>
        Abort(
            IDeferredFunctionFallbackTarget target,
            int checkpoint,
            Error<ErrorCode> primary) {
        Result<Success, ErrorCode, Error<ErrorCode>> rollback = target.Rollback(checkpoint);

        if (rollback is Failed<Success, ErrorCode, Error<ErrorCode>> rollbackFailure) {
            // **回滚失败：`Failed` 升级成 `Fatal`，并且两个错误都留。**
            // **A failed rollback upgrades Failed to Fatal and keeps both errors.**
            return new Fatal<IReadOnlyList<DeferredFunctionConstraintRegion>, ErrorCode, Error<ErrorCode>>(
                new[] { primary, rollbackFailure.Error });
        }

        return new Failed<
            IReadOnlyList<DeferredFunctionConstraintRegion>, ErrorCode, Error<ErrorCode>>(
            primary.Code, primary.Message);
    }
}
