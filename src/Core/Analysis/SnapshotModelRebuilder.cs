#nullable enable

using Fuookami.Ospf.Core.Model.ConstraintProgramming;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using System;
using System.Collections.Generic;
using System.Linq;
using Fuookami.Ospf.Math.Algebra.Number;
using Int64 = Fuookami.Ospf.Math.Algebra.Number.Int64;

namespace Fuookami.Ospf.Core.Analysis;

/// <summary>
/// 把不可变快照重建为可变模型 / Rebuild a mutable model from an immutable snapshot.
/// </summary>
/// <remarks>
/// **这是 `ConstraintProgrammingSnapshotBuilder.buildModel` 的普通路径**（全部成员激活），
/// 剩下 4 个 `core.analysis` 分析器都要经过它。
///
/// **变量是"找回"的，不是"造"的**：遍历快照里已注册的表达式与目标，把其中的 `IVariableItem` 引用按
/// `VariableId` 收进索引，再原样注册。**新建一个同名变量不是同一个变量**——`Identifier` / `Key` 都不同，
/// 注册进去就是另一个（更正）。
///
/// **This is the ordinary path of `ConstraintProgrammingSnapshotBuilder.buildModel`** (every member active), which all four remaining `core.analysis` analyzers go through.
/// **Variables are *found*, not *created***: the registered expressions and objectives are walked, their `IVariableItem` references indexed by `VariableId`, and registered
/// as-is. **A freshly built variable with the same name is a different variable** — `Identifier` / `Key` differ, so it registers as another one ( correction).
///
/// **没实现的部分，逐条在这里说明边界**：选择性停用证据时的值域重绑（`activeSources`）、区间变量、
/// 以及目标过滤（`objectiveFilter`）。**它们不静默跳过——遇到就直接报错**，
/// 因为"少了一条约束、解出来还是个数"正是最难发现的失效。
///
/// **What is not implemented is listed here rather than silently skipped**: domain rebinding when evidence is selectively deactivated (`activeSources`), interval variables, and
/// objective filtering (`objectiveFilter`). **None of them is quietly dropped — each is an error.** A model that quietly lost a constraint still solves, and still gives a
/// number, which is the hardest kind of failure to notice.
/// </remarks>
public static class SnapshotModelRebuilder
{
    /// <summary>
    /// 重建模型 / Rebuild the model.
    /// </summary>
    /// <param name="snapshot">源快照 / The source snapshot.</param>
    /// <returns>新的可变模型，或错误 / A fresh mutable model, or an error.</returns>
    /// <param name="objectiveFilter">只注册这个目标；<c>null</c> 表示全部 / Register only this objective; null means all of them.</param>
    /// <param name="activeSources">
    /// 活跃证据集合；<c>null</c> 表示不放开任何边界 / The active evidence, or <c>null</c> to release no bound.
    /// </param>
    /// <remarks>
    /// **`activeSources` 不是可选装饰，它是删除复验能工作的前提。**
    /// 不给它时每个变量的有效定义域等于原始定义域——**那正是"不取消任何激活"的语义。**
    ///
    /// **`activeSources` is not an optional decoration; it is what makes deletion checks work at all.**
    /// Without it every variable's effective domain equals its original one — **which is exactly the "deactivate nothing" semantics.**
    /// </remarks>
    public static Result<ConstraintProgrammingModel, ErrorCode, Error<ErrorCode>> Rebuild(
        ConstraintProgrammingModelSnapshot snapshot,
        ObjectiveId? objectiveFilter = null,
        IReadOnlySet<DiagnosticSource>? activeSources = null) {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (snapshot.Intervals.Count > 0) {
            return Fail("区间变量尚未支持 / Interval variables are not supported by the rebuilder");
        }

        Dictionary<VariableId, IVariableItem> recovered = RecoverVariables(snapshot);

        var model = new ConstraintProgrammingModel(
            name: snapshot.Name,
            objectCategory: snapshot.ObjectCategory,
            identityNamespace: snapshot.IdentityNamespace,
            identitySchemaVersion: snapshot.IdentitySchemaVersion);

        Result<ConstraintProgrammingModel, ErrorCode, Error<ErrorCode>> Fail(string message) =>
            Results.Failed<ConstraintProgrammingModel>(new Err<ErrorCode>(ErrorCode.IllegalArgument, message));

        foreach (ConstraintProgrammingVariableSnapshot definition in snapshot.Variables) {
            if (!recovered.TryGetValue(definition.Id, out IVariableItem? variable)) {
                model.Dispose();
                // **不能不变 `continue`**：跳过的依据是"该变量完全没被引用"，
                // 而这里"找不到"是"我们没能从 AST 里收出来"——**两件事，跳过就是把后者当成前者。**
                //
                // **This must not `continue` here**: the skip case is "the variable is referenced nowhere", while a miss here means "we could not
                // recover it from the AST". **Those are different facts, and skipping conflates them.**
                return Fail($"无法从快照找回变量 {definition.Id.Value} / Cannot recover variable {definition.Id.Value} from the snapshot");
            }

            if (model.RegisterVariable(
                    id: definition.Id,
                    variable: variable,
                    domain: definition.Domain,
                    scope: definition.Scope,
                    origin: definition.Origin,
                    identityProvenance: definition.IdentityProvenance) is Failed<VariableId, ErrorCode, Error<ErrorCode>> variableFailure) {
                model.Dispose();
                return Results.Failed<ConstraintProgrammingModel>(variableFailure.Error);
            }
        }

        // **取消激活一个边界来源必须改变派生模型，否则删除复验永远得到同一个答案。**
        // **Deactivating a bound source has to change the derived model, or every deletion check returns the same answer.**
        IReadOnlyDictionary<VariableId, IntegerDomain> domains =
            EffectiveDomain.ActiveDomains(snapshot, activeSources);

        foreach (ConstraintProgrammingExpressionSnapshot expression in snapshot.Expressions) {
            if (model.RegisterExpression(expression.Name, EffectiveDomain.RebindDomain(expression.Expression, domains)) is not Ok<string, ErrorCode, Error<ErrorCode>>) {
                model.Dispose();
                return Fail($"无法注册表达式 {expression.Name} / Cannot register expression {expression.Name}");
            }
        }

        foreach (ConstraintProgrammingConstraintSnapshot entry in snapshot.Constraints) {
            if (model.AddConstraint(
                    constraint: entry.Constraint,
                    id: entry.Id,
                    name: entry.Name,
                    group: null,
                    scope: entry.Scope,
                    origin: entry.Origin,
                    identityProvenance: entry.IdentityProvenance) is not Ok<ConstraintId, ErrorCode, Error<ErrorCode>>) {
                model.Dispose();
                return Fail($"无法注册约束 {entry.Id.Value} / Cannot register constraint {entry.Id.Value}");
            }
        }

        foreach (ConstraintProgrammingObjectiveSnapshot objective in snapshot.Objectives) {
            if (objectiveFilter is { } filter && objective.Id != filter) {
                continue;
            }
            if (model.AddObjective(
                    category: objective.Category,
                    expression: objective.Expression,
                    id: objective.Id,
                    name: objective.Name,
                    scope: objective.Scope,
                    origin: objective.Origin,
                    identityProvenance: objective.IdentityProvenance) is not Ok<ObjectiveId, ErrorCode, Error<ErrorCode>>) {
                model.Dispose();
                return Fail($"无法注册目标 {objective.Id.Value} / Cannot register objective {objective.Id.Value}");
            }
        }

        return Results.Ok(model);
    }

    /// <summary>
    /// 从已注册的表达式与目标里找回变量 / Recover variables from the registered expressions and objectives.
    /// </summary>
    /// <remarks>
    /// **只走表达式 AST**：快照里 `Variable` 节点持有的是活的 `IVariableItem`。
    /// **不走 `TypeName`**——那是给报告看的。
    ///
    /// **Only the expression AST is walked**: a snapshot's `Variable` node holds a live `IVariableItem`. **`TypeName` is not used**; it is there for reporting.
    /// </remarks>
    private static Dictionary<VariableId, IVariableItem> RecoverVariables(ConstraintProgrammingModelSnapshot snapshot) {
        var recovered = new Dictionary<VariableId, IVariableItem>();

        foreach (ConstraintProgrammingExpressionSnapshot expression in snapshot.Expressions) {
            Collect(expression.Expression, recovered);
        }
        foreach (ConstraintProgrammingObjectiveSnapshot objective in snapshot.Objectives) {
            Collect(objective.Expression, recovered);
        }
        return recovered;
    }


    /// <summary>收集一个表达式里的变量 / Collect the variables of one expression.</summary>
    /// <param name="expression">表达式 / The expression.</param>
    /// <param name="recovered">索引 / The index.</param>
    private static void Collect(ConstraintProgrammingExpression expression, Dictionary<VariableId, IVariableItem> recovered) {
        switch (expression) {
            case ConstraintProgrammingExpression.Variable { Id: { } id } variable:
                recovered[id] = variable.VariableItem;
                break;
            case ConstraintProgrammingExpression.Linear:
                // **线性项这里收不了**：`Term` 只有 `IVariableItem`，**没有 `VariableId`**，
                // 而索引的键是 `VariableId`。用变量名当键就是猜——名字不是身份。
                // 所以这里不收集；漏掉的变量会在 `Rebuild` 里变成一条明确的报错。
                //
                // **A linear term cannot be indexed here**: `Term` carries only `IVariableItem`, **not a `VariableId`**, and the index is keyed by `VariableId`. Using the
                // variable name as the key would be a guess — a name is not an identity . A missed variable surfaces as a clear error in `Rebuild` instead.
                break;
            default:
                // **其余节点的形状这里没逐一确认过，所以不去猜**：漏收一个变量会在 `Rebuild` 里
                // 变成一条明确的报错，**而不是一个少了项的模型。**
                //
                // **The remaining node shapes were not confirmed one by one, so they are not guessed at**: a missed variable surfaces as a clear error in `Rebuild`, **not as
                // a model with something quietly missing.**
                break;
        }
    }

    /// <summary>
    /// 重建模型并追加一条目标约束 / Rebuild the model and append a target constraint.
    /// </summary>
    /// <param name="snapshot">源快照 / The source snapshot.</param>
    /// <param name="target">目标；其值域按方向取整 / The target; its bound is rounded by direction.</param>
    /// <returns>派生模型与目标约束 id，或错误 / The derived model with the target constraint id, or an error.</returns>
    /// <remarks>
    /// **取整方向不是细节**：&gt;= 用向上取整、&lt;= 用向下取整，**取错方向就等于把门槛放低一格**，
    /// 而报告照样会给出一个像样的结论。
    ///
    /// **只在派生模型里注册目标那个 objective**：多注册其余 objective 不会让求解出错，
    /// **但会让"这份派生模型是为哪个目标造的"这件事变得含糊。**
    ///
    /// **The rounding direction is not a detail**: &gt;= rounds up and &lt;= rounds down; **the wrong way lowers the bar by one unit**, and the report still produces a
    /// plausible-looking conclusion. **Only the target objective is registered in the derived model**: registering the rest would not break the solve,
    /// **but it would blur which objective the derived model was built for.**
    /// </remarks>
    public static Result<DerivedModel, ErrorCode, Error<ErrorCode>> RebuildWithTarget(
        ConstraintProgrammingModelSnapshot snapshot,
        ObjectiveTarget target) {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(target);

        ConstraintProgrammingObjectiveSnapshot? objective = snapshot.Objectives
            .FirstOrDefault(candidate => candidate.Id == target.ObjectiveId);
        if (objective is null) {
            return Results.Failed<DerivedModel>(new Err<ErrorCode>(
                ErrorCode.DataNotFound,
                $"目标引用了未注册 Objective：{target.ObjectiveId.Value} / Target references an unregistered objective: {target.ObjectiveId.Value}"));
        }

        if (!TryIntegerBound(target, out Int64 bound)) {
            return Results.Failed<DerivedModel>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument,
                $"目标值无法取整为整数：{target.Value.ToDouble()} / Target value cannot be rounded into an integer: {target.Value.ToDouble()}"));
        }

        Result<ConstraintProgrammingModel, ErrorCode, Error<ErrorCode>> rebuilt =
            Rebuild(snapshot, objectiveFilter: objective.Id);
        if (rebuilt is not Ok<ConstraintProgrammingModel, ErrorCode, Error<ErrorCode>> okModel) {
            return rebuilt is Failed<ConstraintProgrammingModel, ErrorCode, Error<ErrorCode>> failure
                ? Results.Failed<DerivedModel>(failure.Error)
                : Results.Failed<DerivedModel>(new Err<ErrorCode>(ErrorCode.ApplicationError, "重建失败 / Rebuild failed"));
        }

        ConstraintProgrammingModel model = okModel.Value!;
        var targetId = new ConstraintId($"analysis-target:{target.StableId}");
        Result<ConstraintProgrammingConstraint.IntegerComparison, ErrorCode, Error<ErrorCode>> constraint =
            target.Relation == ObjectiveTargetRelation.AtLeast
                ? ConstraintProgrammingConstraint.CreateGreaterOrEqual(objective.Expression, bound)
                : ConstraintProgrammingConstraint.CreateLessOrEqual(objective.Expression, bound);
        if (constraint is not Ok<ConstraintProgrammingConstraint.IntegerComparison, ErrorCode, Error<ErrorCode>> okConstraint) {
            model.Dispose();
            return Results.Failed<DerivedModel>(new Err<ErrorCode>(ErrorCode.IllegalArgument,
                $"无法由目标构造约束 / Cannot build a constraint from the target"));
        }

        if (model.AddConstraint(okConstraint.Value!, id: targetId, name: targetId.Value)
                is not Ok<ConstraintId, ErrorCode, Error<ErrorCode>>) {
            model.Dispose();
            return Results.Failed<DerivedModel>(new Err<ErrorCode>(ErrorCode.IllegalArgument,
                $"无法把目标约束加入派生模型 / Cannot add the target constraint to the derived model"));
        }

        return Results.Ok(new DerivedModel(model, targetId));
    }

    /// <summary>
    /// 按目标方向取整 / Round the target bound by its direction.
    /// </summary>
    /// <summary>
    /// 目标到整数边界的映射 / Map a target to an integer bound.
    /// </summary>
    /// <param name="target">目标 / The target.</param>
    /// <param name="bound">整数边界 / The integer bound.</param>
    /// <returns>能否精确表达 / Whether it can be expressed exactly.</returns>
    /// <remarks>
    /// **转发到 `ObjectiveTargetBounds.IntegerBound`，不再自己算一遍舍入。**
    /// 这里原本有一份独立实现（`9.2e18` 上下界、非 `AtLeast` 一律向下取整），
    /// **而两份相同的舍入规则只要改一份，就再也不相同**（与 `ShouldStop` 同一个形状）。
    ///
    /// **Forwards to `ObjectiveTargetBounds.IntegerBound` instead of rounding again.** There used to be an independent copy here (a `9.2e18` window, floor for anything not `AtLeast`), **and two copies of one
    /// rounding rule stop agreeing as soon as one is edited** (the same shape as `ShouldStop`).
    /// </remarks>
    public static bool TryIntegerBound(ObjectiveTarget target, out Int64 bound) {
        if (ObjectiveTargetBounds.IntegerBound(target) is { } exact) {
            bound = exact;
            return true;
        }
        bound = new Int64(0);
        return false;
    }
}

/// <summary>派生模型与其目标约束 / A derived model and its target constraint.</summary>
/// <param name="Model">派生模型 / The derived model.</param>
/// <param name="TargetConstraintId">追加的目标约束 id / The id of the appended target constraint.</param>
public sealed record DerivedModel(ConstraintProgrammingModel Model, ConstraintId TargetConstraintId);
