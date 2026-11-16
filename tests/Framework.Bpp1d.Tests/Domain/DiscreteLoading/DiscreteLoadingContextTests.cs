// 离散装载注册与取解判据 / Criteria for discrete-loading registration and solution extraction.

using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading;
using Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading.Model;
using Fuookami.Ospf.Framework.Bpp.Infrastructure;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;
using PositionConflict = Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading.Model.PositionConflict;
using Try = Fuookami.Ospf.Utils.Functional.Result<
    Fuookami.Ospf.Utils.Functional.Success,
    Fuookami.Ospf.Utils.Error.ErrorCode,
    Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.Bpp1d.Tests.Domain.DiscreteLoading;

/// <summary>
/// **离散装载的注册与取解** / Registration and solution extraction.
/// </summary>
/// <remarks>
/// 对应基线 `DiscreteLoadingContext.register`（前半段）与 `extractSolution`。四条拒绝条件：
///
/// | 条件 | 若放过 | 后果 |
/// | --- | --- | --- |
/// | 变量 token 或值缺失 | 悄悄跳过 | **少算一半的分配**，读起来像"装得更少" |
/// | **变量不满足二元性** | 四舍五入 | 一个 `0.5` 的分配被当成"装了"，**报告一个不可行的计划** |
/// | 同一件货物被两条分配选中 | 都收下 | 单值表静默覆盖，"装了两次"看起来像"装了一次" |
/// | 某个布局组不是恰好选中一种 | 少选或多选 | 组约束形同虚设 |
///
/// **第二条最要紧**：`0.5` 不是浮点噪声，是**求解器在说"这不是整数解"**。
///
/// Prefix and `extractSolution`. The four rejection rules are in the table; **the second matters most** — `0.5` is not floating
/// noise, it is **the solver saying "this is not an integer solution"**.
/// </remarks>
public class DiscreteLoadingContextTests {
    /// <summary>**合法解能被正确读出** / A legal solution is read back correctly.</summary>
    [Fact]
    public void ExtractSolution_ShouldReadBackALegalSolution() {
        DiscreteLoadingProblem<Flt64> problem = MakeProblem(
            items: [("I1", 1000.0, false, null)],
            positions: [("P1", 5UL, 5000.0), ("P2", 5UL, 5000.0)]);
        var context = new DiscreteLoadingContext<Flt64>(problem, Flt64PackingValueAdapter.Instance);
        LinearMetaModel<Flt64> model = new();
        Try registered = context.Register(model);
        registered.IsOk.Should().BeTrue(registered.ToString());

        SetAllZero(model, context);
        SetValue(model, context.Aggregation.Assignments[new AssignmentKey("I1", "P1")], 1.0);
        SetValue(model, context.Aggregation.Occupied["P1"], 1.0);

        Result<DiscreteLoadingSolution, ErrorCode, Error<ErrorCode>> result = context.ExtractSolution(model);

        result.IsOk.Should().BeTrue(result.ToString());
        DiscreteLoadingSolution solution =
            ((Ok<DiscreteLoadingSolution, ErrorCode, Error<ErrorCode>>)result).Value;
        solution.Assignments.Should().ContainKey("I1").WhoseValue.Should().Be("P1");
        solution.OccupiedPositions.Should().BeEquivalentTo(new[] { "P1" });
    }

    /// <summary>**非二元值必须被拒，不能四舍五入** / A non-binary value must be rejected, never rounded.</summary>
    /// <remarks>
    /// **这是这里最重要的一条判据。** 求解器返回 `0.5` 意味着"这不是整数解"——
    /// 四舍五入会把它当成"装了"或"没装"，然后**报告一个不可行的计划**。
    ///
    /// **The most important criterion in.** A `0.5` from the solver means "this is not an integer solution"; rounding turns it into "loaded" or "not loaded"
    /// and then **reports an infeasible plan**.
    /// </remarks>
    [Fact]
    public void ExtractSolution_ShouldRejectANonBinaryVariableValue() {
        DiscreteLoadingProblem<Flt64> problem = MakeProblem(
            items: [("I1", 1000.0, false, null)],
            positions: [("P1", 5UL, 5000.0)]);
        var context = new DiscreteLoadingContext<Flt64>(problem, Flt64PackingValueAdapter.Instance);
        LinearMetaModel<Flt64> model = new();
        context.Register(model).IsOk.Should().BeTrue();

        SetAllZero(model, context);
        SetValue(model, context.Aggregation.Assignments[new AssignmentKey("I1", "P1")], 0.5);

        Result<DiscreteLoadingSolution, ErrorCode, Error<ErrorCode>> result = context.ExtractSolution(model);

        result.IsFailed.Should().BeTrue("0.5 means the solver did not return an integer solution");
    }

    /// <summary>**容差内的浮点过冲仍算 1** / A floating overshoot inside the tolerance still counts as 1.</summary>
    /// <remarks>
    /// 求解器返回 `1.0000001` 必须当成 1，否则**所有正常解都会被拒**。
    /// 用 `1e-6` 容差把这一侧固定住。
    ///
    /// A solver's `1.0000001` must count as 1 or **every normal solution is rejected**. The `1e-6` tolerance pins this side.
    /// </remarks>
    [Fact]
    public void ExtractSolution_ShouldTolerateASmallOvershootAboveOne() {
        DiscreteLoadingProblem<Flt64> problem = MakeProblem(
            items: [("I1", 1000.0, false, null)],
            positions: [("P1", 5UL, 5000.0)]);
        var context = new DiscreteLoadingContext<Flt64>(problem, Flt64PackingValueAdapter.Instance);
        LinearMetaModel<Flt64> model = new();
        context.Register(model).IsOk.Should().BeTrue();

        SetAllZero(model, context);
        SetValue(model, context.Aggregation.Assignments[new AssignmentKey("I1", "P1")], 1.0 + 1e-7);
        SetValue(model, context.Aggregation.Occupied["P1"], 1.0 + 1e-7);

        Result<DiscreteLoadingSolution, ErrorCode, Error<ErrorCode>> result = context.ExtractSolution(model);

        result.IsOk.Should().BeTrue("1.0000001 is inside the 1e-6 tolerance: " + result.ToString());
    }

    /// <summary>**超出容差的偏移必须被拒** / A deviation beyond the tolerance must be rejected.</summary>
    /// <remarks>
    /// 反向对照：若容差写成 `1e-2`，上一条仍会绿。这条把容差**钉在上界**。
    ///
    /// Reverse control: a tolerance of `1e-2` would still pass the previous case. This one pins the **upper bound**.
    /// </remarks>
    [Fact]
    public void ExtractSolution_ShouldRejectADeviationBeyondTheTolerance() {
        DiscreteLoadingProblem<Flt64> problem = MakeProblem(
            items: [("I1", 1000.0, false, null)],
            positions: [("P1", 5UL, 5000.0)]);
        var context = new DiscreteLoadingContext<Flt64>(problem, Flt64PackingValueAdapter.Instance);
        LinearMetaModel<Flt64> model = new();
        context.Register(model).IsOk.Should().BeTrue();

        SetAllZero(model, context);
        SetValue(model, context.Aggregation.Assignments[new AssignmentKey("I1", "P1")], 1.0 + 1e-3);
        // **占位也必须置 1。** 第一版漏了这一步，于是 `verify` 先因为"有内容却没标占用"失败——
        // **判据绿了，但失败在别处**，把容差放宽到 `1e-2` 的变异因此存活。
        // **The occupancy flag must be 1 as well.** The first version missed it, so `verify` failed first on "content without occupancy" — **the criterion went green
        // while failing elsewhere**, letting the "tolerance widened to 1e-2" mutant survive.
        SetValue(model, context.Aggregation.Occupied["P1"], 1.0);

        Result<DiscreteLoadingSolution, ErrorCode, Error<ErrorCode>> result = context.ExtractSolution(model);

        result.IsFailed.Should().BeTrue("1.001 is outside the 1e-6 tolerance");
    }

    /// <summary>**同一件货物被两条分配选中必须被拒** / Two assignments selecting the same cargo must be rejected.</summary>
    /// <remarks>
    /// `assignments` 是 `货物→舱位` 单值表。若两条分配都被选中且静默覆盖，
    /// **"装了两次"会看起来像"装了一次"**，重量与件数统计随即少算。
    ///
    /// `assignments` is a single-valued cargo-to-position map. If two selected assignments silently overwrite each other, **"loaded twice" looks like "loaded once"** and
    /// the weight and count statistics under-report.
    /// </remarks>
    [Fact]
    public void ExtractSolution_ShouldRejectDuplicateCargoLoading() {
        DiscreteLoadingProblem<Flt64> problem = MakeProblem(
            items: [("I1", 1000.0, false, null)],
            positions: [("P1", 5UL, 5000.0), ("P2", 5UL, 5000.0)]);
        var context = new DiscreteLoadingContext<Flt64>(problem, Flt64PackingValueAdapter.Instance);
        LinearMetaModel<Flt64> model = new();
        context.Register(model).IsOk.Should().BeTrue();

        SetValue(model, context.Aggregation.Assignments[new AssignmentKey("I1", "P1")], 1.0);
        SetValue(model, context.Aggregation.Assignments[new AssignmentKey("I1", "P2")], 1.0);
        SetValue(model, context.Aggregation.Occupied["P1"], 1.0);
        SetValue(model, context.Aggregation.Occupied["P2"], 1.0);

        Result<DiscreteLoadingSolution, ErrorCode, Error<ErrorCode>> result = context.ExtractSolution(model);

        result.IsFailed.Should().BeTrue("I1 cannot sit on P1 and P2 at once");
    }

    /// <summary>**一个布局组选中两种布局必须被拒，且是"恰选一种"这条拦下的** / Selecting two layouts of one group must be rejected, and by the "exactly one" rule itself.</summary>
    /// <remarks>
    /// **第一版夹具让 `verify` 先拦下了。** 原来的布局是 `L1={P1}`、`L2={P2}`，
    /// 选两种时 `verify` 的"布局必须覆盖全部占用舱位"会先失败——**判据绿了，但没测到取解里的那条守卫**，
    /// 变异会存活。
    ///
    /// 现在让**两种布局都覆盖占用舱位**：`L1={P1,P2}`、`L2={P1}`，占用的只有 `P1`。
    /// 选 `L1` 与 `L2` 时 `verify` **完全通过**（`L2` 覆盖 `P1`），
    /// **只有"恰选一种"那条能拦住**。
    ///
    /// **The first fixture was caught by `verify` instead.** It used `L1={P1}` and `L2={P2}`, so selecting both failed `verify`'s "layout must cover every occupied
    /// position" first — **the criterion went green without exercising the extraction guard**, and the mutant would have survived. Now **both layouts cover the occupied
    /// set**: `L1={P1,P2}` and `L2={P1}` with only `P1` occupied. Selecting `L1` and `L2` **passes `verify` entirely** (`L2` covers `P1`), so **only the "exactly one"
    /// rule can catch it**.
    /// </remarks>
    [Fact]
    public void ExtractSolution_ShouldRejectTwoLayoutsOfTheSameGroup() {
        var group = new LayoutSelectionGroup(
            "G1",
            new HashSet<string> { "P1", "P2" },
            new List<LoadingLayout> {
                new("L1", new HashSet<string> { "P1", "P2" }),
                new("L2", new HashSet<string> { "P1" }),
            });
        DiscreteLoadingProblem<Flt64> problem = MakeProblem(
            items: [("I1", 1000.0, false, null)],
            positions: [("P1", 5UL, 5000.0), ("P2", 5UL, 5000.0)],
            layoutGroups: [group]);
        var context = new DiscreteLoadingContext<Flt64>(problem, Flt64PackingValueAdapter.Instance);
        LinearMetaModel<Flt64> model = new();
        context.Register(model).IsOk.Should().BeTrue();

        // **必须先置零。** 第一版漏了这一步，`assignment_I1_P2` 没有值，取解以 `DataNotFound` 失败——
        // **判据绿了，但失败在"缺值"而不是"两个布局"**，删掉那条守卫的变异因此存活。
        // **Zero everything first.** The first version missed it, leaving `assignment_I1_P2` without a value so extraction failed with `DataNotFound` — **green while
        // failing on "missing value" instead of "two layouts"**, letting the guard-removal mutant survive.
        SetAllZero(model, context);
        SetValue(model, context.Aggregation.Assignments[new AssignmentKey("I1", "P1")], 1.0);
        SetValue(model, context.Aggregation.Occupied["P1"], 1.0);
        SetValue(model, context.Aggregation.Layouts[("G1", "L1")], 1.0);
        SetValue(model, context.Aggregation.Layouts[("G1", "L2")], 1.0);

        Result<DiscreteLoadingSolution, ErrorCode, Error<ErrorCode>> result = context.ExtractSolution(model);

        result.IsFailed.Should().BeTrue("G1 must select exactly one layout, not both");
    }

    /// <summary>**缺少变量 token 时返回 `DataNotFound`，不悄悄跳过** / A missing variable token yields `DataNotFound`, never a silent skip.</summary>
    /// <remarks>
    /// 悄悄跳过会让**没被读到的分配看起来像"没装"**——一个结果美化的错误：
    /// 报告出来的用量比实际更少，没有人会察觉。
    ///
    /// A silent skip makes **unread assignments look like "not loaded"** — a result-flattering bug: reported usage is lower than reality and nobody notices.
    /// </remarks>
    [Fact]
    public void ExtractSolution_ShouldReportDataNotFoundWhenATokenIsMissing() {
        DiscreteLoadingProblem<Flt64> problem = MakeProblem(
            items: [("I1", 1000.0, false, null)],
            positions: [("P1", 5UL, 5000.0)]);
        var context = new DiscreteLoadingContext<Flt64>(problem, Flt64PackingValueAdapter.Instance);
        LinearMetaModel<Flt64> model = new();
        context.Register(model).IsOk.Should().BeTrue();

        // **只给占位变量赋值，不给分配变量赋值** ⇒ 分配变量没有结果。
        // **Only the occupancy variable gets a value** ⇒ the assignment variable has no result.
        SetValue(model, context.Aggregation.Occupied["P1"], 0.0);

        Result<DiscreteLoadingSolution, ErrorCode, Error<ErrorCode>> result = context.ExtractSolution(model);

        result.IsFailed.Should().BeTrue();
        ((Failed<DiscreteLoadingSolution, ErrorCode, Error<ErrorCode>>)result).Error.Code
            .Should().Be(ErrorCode.DataNotFound, "a missing reading is a data problem, not an application error");
    }

    /// <summary>**重复注册必须被拒** / Registering twice must be rejected.</summary>
    [Fact]
    public void Register_ShouldRejectASecondRegistration() {
        DiscreteLoadingProblem<Flt64> problem = MakeProblem(
            items: [("I1", 1000.0, false, null)],
            positions: [("P1", 5UL, 5000.0)]);
        var context = new DiscreteLoadingContext<Flt64>(problem, Flt64PackingValueAdapter.Instance);
        LinearMetaModel<Flt64> model = new();

        context.Register(model).IsOk.Should().BeTrue();
        Try second = context.Register(model);

        second.IsFailed.Should().BeTrue("one context serves one solve");
    }

    /// <summary>**未注册就取解必须被拒** / Extracting before registering must be rejected.</summary>
    [Fact]
    public void ExtractSolution_ShouldRejectAnUnregisteredContext() {
        DiscreteLoadingProblem<Flt64> problem = MakeProblem(
            items: [("I1", 1000.0, false, null)],
            positions: [("P1", 5UL, 5000.0)]);
        var context = new DiscreteLoadingContext<Flt64>(problem, Flt64PackingValueAdapter.Instance);
        LinearMetaModel<Flt64> model = new();

        Result<DiscreteLoadingSolution, ErrorCode, Error<ErrorCode>> result = context.ExtractSolution(model);

        result.IsFailed.Should().BeTrue("nothing has been registered yet");
    }

    /// <summary>**非法问题在注册阶段就被拒** / An illegal problem is rejected at registration.</summary>
    [Fact]
    public void Register_ShouldRejectAnIllegalProblem() {
        DiscreteLoadingProblem<Flt64> problem = MakeProblem(
            items: [("I1", -1000.0, false, null)],
            positions: [("P1", 5UL, 5000.0)]);
        var context = new DiscreteLoadingContext<Flt64>(problem, Flt64PackingValueAdapter.Instance);
        LinearMetaModel<Flt64> model = new();

        Try result = context.Register(model);

        result.IsFailed.Should().BeTrue("a negative cargo weight must never reach the model");
        context.Registered.Should().BeFalse("a failed registration must not flip the flag");
    }

    /// <summary>
    /// 把所有变量先置零 / Zero every variable first.
    /// </summary>
    /// <param name="model">元模型 / The meta model.</param>
    /// <param name="context">上下文 / The context.</param>
    /// <remarks>
    /// **必须先置零，再用具体用例覆盖。** 基线要求**每一个**分配/占位/布局变量都有值——
    /// 不置零的话，没赋值的变量会让取解以 `DataNotFound` 失败，**于是"非二元值被拒"那条判据会因为
    /// 错误的原因变绿**：它是失败了，但失败在"缺值"而不是"非二元"。
    /// 这是"判据通过但没证明要证明的事"的又一例。
    ///
    /// **Zero everything first, then override in the specific case.** The code requires **every** assignment/occupancy/layout variable to carry a value;
    /// without zeroing, an unassigned variable makes extraction fail with `DataNotFound`, so **the "non-binary value rejected" criterion goes green for the wrong
    /// reason**: it did fail, but on "missing value" rather than "non-binary". Another case of "the criterion passes without proving what it claims".
    /// </remarks>
    private static void SetAllZero(LinearMetaModel<Flt64> model, DiscreteLoadingContext<Flt64> context) {
        var zero = new Flt64(0.0);
        foreach (BinVar variable in context.Aggregation.Assignments.Values) {
            model.Tokens.Find(variable)!.SetResult(zero);
        }
        foreach (BinVar variable in context.Aggregation.Occupied.Values) {
            model.Tokens.Find(variable)!.SetResult(zero);
        }
        foreach (BinVar variable in context.Aggregation.Layouts.Values) {
            model.Tokens.Find(variable)!.SetResult(zero);
        }
    }

    /// <summary>写入一个变量的结果 / Write a variable's result.</summary>
    /// <param name="model">元模型 / The meta model.</param>
    /// <param name="variable">变量 / The variable.</param>
    /// <param name="value">值 / The value.</param>
    private static void SetValue(LinearMetaModel<Flt64> model, BinVar variable, double value) {
        model.Tokens.Find(variable).Should().NotBeNull("the variable must be registered in the model");
        model.Tokens.Find(variable)!.SetResult(new Flt64(value));
    }

    /// <summary>构造问题 / Build a problem.</summary>
    /// <param name="items">货物：(标识, 克重, 必装, 固定舱位) / Items: (identity, grams, required, fixed position).</param>
    /// <param name="positions">舱位：(标识, 最大件数, 克容量) / Positions: (identity, max count, gram capacity).</param>
    /// <param name="layoutGroups">布局选择组 / Layout selection groups.</param>
    /// <returns>问题 / The problem.</returns>
    private static DiscreteLoadingProblem<Flt64> MakeProblem(
        (string Id, double Weight, bool Required, string? Fixed)[] items,
        (string Id, ulong MaxCount, double Capacity)[] positions,
        IReadOnlyList<LayoutSelectionGroup>? layoutGroups = null) {
        var itemList = new List<LoadingItem<Flt64>>(items.Length);
        foreach ((string id, double weight, bool required, string? fixedPosition) in items) {
            itemList.Add(new LoadingItem<Flt64>(id, Grams(weight), Required: required, FixedPositionId: fixedPosition));
        }
        var positionList = new List<LoadingPosition<Flt64>>(positions.Length);
        foreach ((string id, ulong maxCount, double capacity) in positions) {
            positionList.Add(new LoadingPosition<Flt64>(id, maxCount, Grams(capacity)));
        }

        var assignments = new HashSet<AssignmentKey>();
        foreach (LoadingItem<Flt64> item in itemList) {
            foreach (LoadingPosition<Flt64> position in positionList) {
                assignments.Add(new AssignmentKey(item.Id, position.Id));
            }
        }

        return new DiscreteLoadingProblem<Flt64>(
            Items: itemList,
            Positions: positionList,
            AllowedAssignments: assignments,
            Conflicts: new HashSet<PositionConflict>(),
            LayoutGroups: layoutGroups ?? new List<LayoutSelectionGroup>(),
            WeightUnit: MassUnits.Gram);
    }

    /// <summary>构造克量 / Build a gram quantity.</summary>
    /// <param name="value">数值 / Value.</param>
    /// <returns>带单位的量 / The unit-carrying quantity.</returns>
    private static Quantity<Flt64> Grams(double value) =>
        new(new Flt64(value), MassUnits.Gram);
}
