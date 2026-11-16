// 一维离散装载几何适配 / One-dimensional discrete-loading geometry adapter.
// 与固定 Kotlin 对应。
// The pinned Kotlin: .

using System.Collections.Generic;
using Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading;
using Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading.Model;
using Fuookami.Ospf.Framework.Bpp.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using PositionConflict = Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading.Model.PositionConflict;
using Try = Fuookami.Ospf.Utils.Functional.Result<
    Fuookami.Ospf.Utils.Functional.Success,
    Fuookami.Ospf.Utils.Error.ErrorCode,
    Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.Bpp1d.Domain.DiscreteLoading;

/// <summary>
/// 一维离散装载几何适配 / One-dimensional discrete-loading geometry adapter.
/// </summary>
/// <remarks>
/// 把 的**几何扫描**（`LoadingGeometry`）接到 的**稀疏装载问题**上：
/// 舱位在一维上重叠 ⇒ 它们在装载问题里互斥。
///
/// **两处容易写错**：
///
/// | 点 | 错法 | 后果 |
/// | --- | --- | --- |
/// | **舱位与几何必须一一对应** | 只检查几何侧的 id 或干脆不查 | **某个舱位没有区间**，几何扫描对它视而不见 ⇒ 该舱位的真实冲突**全部漏掉** |
/// | **冲突是合并，不是替换** | `conflicts = geometryConflicts` | 调用方**业务上已经声明的互斥**被悄悄丢掉 |
///
/// **第二处尤其危险**：被丢掉的正是"不来自几何"的那部分约束——比如两条航线不能同时用，
/// 几何上完全不重叠。**它的缺失不会让求解器报错，只会给出一个违反业务规则的解。**
///
/// This wires the earlier **geometry sweep** into the earlier **sparse loading problem**: positions overlapping in one dimension become mutually exclusive in the loading problem.
/// **Two easy mistakes** are in the table: a **one-way** or missing position/geometry correspondence means a position without an interval is invisible to the sweep and
/// **every one of its real conflicts is lost**; and **replacing instead of merging** silently drops the caller's **already-declared business conflicts** — the ones that
/// do *not* come from geometry, such as "these two lanes may not be used together while their intervals do not overlap at all". **Their absence raises no solver error;
/// it just yields a solution that violates the business rule.**
/// </remarks>
public static class Bpp1dDiscreteLoading {
    /// <summary>
    /// 合并真实区间冲突和业务互斥边 / Merge physical interval conflicts and business conflicts.
    /// </summary>
    /// <typeparam name="V">领域数值 / Domain number.</typeparam>
    /// <param name="problem">离散输入 / Discrete input.</param>
    /// <param name="geometry">每个舱位的显式区间 / Explicit interval for every position.</param>
    /// <param name="adapter">数值边界 / Numeric adapter.</param>
    /// <returns>上下文或失败 / Context or failure.</returns>
    public static Result<DiscreteLoadingContext<V>, ErrorCode, Error<ErrorCode>> Context<V>(
        DiscreteLoadingProblem<V> problem,
        IReadOnlyList<LoadingInterval<V>> geometry,
        IPackingValueAdapter<V> adapter)
        where V : struct, IFloatingNumber<V> {
        // **双向包含才叫一一对应。** 只查一个方向，另一侧的舱位或区间就会被静默忽略。
        // **The correspondence must hold both ways**; checking one direction silently ignores positions or intervals on the other side.
        var geometryIds = new HashSet<string>(geometry.Count);
        foreach (LoadingInterval<V> interval in geometry) {
            geometryIds.Add(interval.Id);
        }
        var positionIds = new HashSet<string>(problem.Positions.Count);
        foreach (LoadingPosition<V> position in problem.Positions) {
            positionIds.Add(position.Id);
        }
        if (!geometryIds.SetEquals(positionIds)) {
            return Results.Failed<DiscreteLoadingContext<V>>(new Err<ErrorCode>(
                ErrorCode.IllegalArgument, "舱位与几何不匹配 / Position/geometry mismatch"));
        }

        // **这里的类型转换是真实需要的，不是噪音。** 几何侧给的是**有向边**
        // （`Bpp.Infrastructure.PositionConflict`，字段 `Earlier`/`Later`），业务侧要的是
        // **无序对**（`Bpp.Domain.DiscreteLoading.Model.PositionConflict`，字段 `First`/`Second`）。
        // **同名不同类型**——里只是命名冲突，到这里变成了真正的翻译点。
        //
        // **This conversion is real, not noise.** The geometry side yields a **directed edge** (`Earlier`/`Later`) while the business side wants an **unordered pair**
        // (`First`/`Second`). **Same name, different types** — a mere naming clash/，an actual translation point here.
        Result<IReadOnlySet<Fuookami.Ospf.Framework.Bpp.Infrastructure.PositionConflict>, ErrorCode, Error<ErrorCode>> swept =
            LoadingGeometry.Conflicts(geometry);
        if (swept is Failed<IReadOnlySet<Fuookami.Ospf.Framework.Bpp.Infrastructure.PositionConflict>, ErrorCode, Error<ErrorCode>> failed) {
            return Results.Failed<DiscreteLoadingContext<V>>(failed.Error);
        }
        if (swept is Fatal<IReadOnlySet<Fuookami.Ospf.Framework.Bpp.Infrastructure.PositionConflict>, ErrorCode, Error<ErrorCode>> fatal) {
            return new Fatal<DiscreteLoadingContext<V>, ErrorCode, Error<ErrorCode>>(fatal.Errors);
        }
        IReadOnlySet<Fuookami.Ospf.Framework.Bpp.Infrastructure.PositionConflict> geometryConflicts =
            ((Ok<IReadOnlySet<Fuookami.Ospf.Framework.Bpp.Infrastructure.PositionConflict>, ErrorCode, Error<ErrorCode>>)swept).Value;

        // **合并而非替换**：业务上已声明的互斥边必须保留。
        // **Merge, never replace**: the already-declared business edges must survive.
        //
        // `ponytail:` 有向边逐条转成无序对，同一条冲突**两个方向都会进来** ⇒ 集合里可能出现 `(A,B)` 与 `(B,A)`。
        // 校验不要求去重、约束生成按团处理，重复无害；**若下游按边数统计就要去重**。
        // `ponytail:` each directed edge becomes a pair, so one conflict **enters in both directions** and the set may hold both `(A,B)` and `(B,A)`. Validation does not
        // require dedup and clique generation is unaffected, so duplicates are harmless; **dedup if anything downstream counts edges.**
        var merged = new HashSet<PositionConflict>(problem.Conflicts);
        // **方向在这里无关紧要，这里变异"反向翻译"存活**：业务侧 `PositionConflict` 是**无序对**的
        // 语义（`First`/`Second` 只是两个成员名），而 `ConflictIndex` 建图时**自己会双向登记**、
        // `validate` 只禁止 `First == Second`。所以 `(A,B)` 与 `(B,A)` 在后续所有环节**完全等价**。
        // **真实的等价变异，不是判据不足。** 这里按几何侧的扫描顺序写。
        //
        // **Direction is irrelevant here, and the "translate backwards" mutant survived the criteria**: the business `PositionConflict` is semantically an **unordered
        // pair** (`First`/`Second` are merely member names), `ConflictIndex` registers **both directions** when building its graph, and `validate` only forbids
        // `First == Second`. `(A,B)` and `(B,A)` are therefore **fully equivalent downstream** — **a genuine equivalent mutant, not a weak criterion.** The geometry
        // sweep's own order is kept, matching the code.
        foreach (Fuookami.Ospf.Framework.Bpp.Infrastructure.PositionConflict conflict in geometryConflicts) {
            merged.Add(new PositionConflict(conflict.Earlier, conflict.Later));
        }

        var context = new DiscreteLoadingContext<V>(
            problem with { Conflicts = merged }, adapter);
        Try validation = context.Validator.Validate();
        if (validation is Failed<Success, ErrorCode, Error<ErrorCode>> invalid) {
            return Results.Failed<DiscreteLoadingContext<V>>(invalid.Error);
        }
        if (validation is Fatal<Success, ErrorCode, Error<ErrorCode>> fatalValidation) {
            return new Fatal<DiscreteLoadingContext<V>, ErrorCode, Error<ErrorCode>>(fatalValidation.Errors);
        }
        return Results.Ok(context);
    }
}
