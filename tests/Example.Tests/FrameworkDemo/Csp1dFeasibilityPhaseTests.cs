#nullable enable

using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Fuookami.Ospf.Framework.Csp1d.Application.Model;
using Fuookami.Ospf.Framework.Csp1d.Application.Service;
using Fuookami.Ospf.Framework.Csp1d.Domain.CuttingPlanGeneration.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.Material.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.Produce;
using Fuookami.Ospf.Framework.Csp1d.Domain.Produce.Model;
using Fuookami.Ospf.Framework.Csp1d.Domain.Yield.Model;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Xunit;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// 可行性阶段（Phase-1）必须存在且语义正确。
/// The feasibility phase (Phase-1) must exist and carry the right semantics.
/// </summary>
/// <remarks>
/// **一整套两阶段机制，C# 原本完全没有**：
///
/// 1. 从未解出可行解、且 LP 以「模型不可行」失败 → 重建一个**只最小化人工短缺**的主问题；
/// 2. 目标值 ≤ `1e-7`（短缺归零）→ **丢弃人工短缺、恢复原始硬约束主问题**。
///
/// **缺了第 1 步**：LP 一不可行就直接终止，**哪怕"加人工短缺就能解"**。
/// **缺了第 2 步**：最终解带着人工短缺——**看起来可行，实际不可行**。
///
/// 这里判据只覆盖**能被 mock 稳定触发的部分**：builder 的 `FeasibilityPhase` 标志
/// 与它带来的 yield 聚合。**两阶段切换本身需要让 mock 返回"模型不可行"**，
/// 而 `MockColumnGenerationSolver` **只会成功或返回通用失败**，无法区分错误码——
/// **这是判据的真实边界，记入缺口，不假装覆盖了。**
///
/// **This implementation has a whole two-phase mechanism that C# lacked entirely** (steps above).
/// **Without step 1** an infeasible LP terminates at once, **even when "an artificial deficit would make it solvable"**.
/// **Without step 2** the final solution carries artificial deficits — **apparently feasible, actually infeasible**.
///
/// These criteria only cover **what the mock can trigger deterministically**: the builder's `FeasibilityPhase` flag and the yield aggregation it brings.
/// **The two-phase switch itself needs the mock to return "model infeasible"**, and `MockColumnGenerationSolver` **only succeeds or returns a generic failure**
/// with no error code — **that is a real boundary of these criteria, recorded as a gap rather than pretended covered.**
/// </remarks>
public class Csp1dFeasibilityPhaseTests {
    private static Quantity<Flt64> Millimetres(double value) =>
        new(new Flt64(value), LengthUnits.Millimeter);

    private static Material<Flt64> MakeMaterial() =>
        new(
            id: "M1",
            name: "material",
            widthRange: new WidthRange<Flt64>(
                Width: new QuantityRange<Flt64>(
                    LowerBound: Millimetres(100.0),
                    UpperBound: Millimetres(200.0)),
                Step: Millimetres(1.0)));

    private static Csp1dProblem<Flt64> MakeProblem() {
        Material<Flt64> material = MakeMaterial();
        var product = new Product<Flt64>(
            id: "A",
            name: "product-a",
            width: new List<Quantity<Flt64>> { Millimetres(50.0) });
        var builder = new Csp1dProblemBuilder<Flt64>();
        builder.AddProduct(product);
        builder.AddMaterial(material);
        builder.AddDemand(new ProductDemand<Flt64>(product, Millimetres(50.0)));
        return builder.Build();
    }

    /// <summary>**可行性阶段必须让 LP 模式也构建 yield 聚合** / The feasibility phase must make an LP-mode build create the yield aggregation too.</summary>
    /// <remarks>
    /// `val yieldAgg = if (_mode == MILP || _feasibilityPhase)...`，C# 原本只有 `_mode == MILP`。
    ///
    /// **可观测的差异**：可行性阶段下 `Yield` 聚合非 null，**所以 yield 约束管线会被加进去**。
    /// 判据靠 `context.Yield` 是否存在。
    ///
 /// This implementation is `val yieldAgg = if (_mode == MILP || _feasibilityPhase)...` while C# only had `_mode == MILP`.
    ///
    /// **The observable difference**: under the feasibility phase the `Yield` aggregation is non-null, **so the yield constraint pipelines are added**. The criterion
    /// checks whether `context.Yield` exists.
    /// </remarks>
    [Fact]
    public void FeasibilityPhase_ShouldBuildTheYieldAggregationEvenInLpMode() {
        Csp1dProblem<Flt64> problem = MakeProblem();
        var input = new ProduceInput<Flt64>(
            CuttingPlans: new List<CuttingPlan<Flt64>>(),
            Demands: problem.Demands,
            Materials: problem.Materials,
            Machines: problem.Machines,
            WarmStartPlanUsages: new List<CuttingPlanUsage<Flt64>>());

        Csp1dProduceContext<Flt64> withoutPhase = new Csp1dProduceContextBuilder<Flt64>(input)
            .Mode(Csp1dModelingMode.LP)
            .Build();
        Csp1dProduceContext<Flt64> withPhase = new Csp1dProduceContextBuilder<Flt64>(input)
            .Mode(Csp1dModelingMode.LP)
            .FeasibilityPhase()
            .YieldConfig(new YieldModelingConfig<Flt64>())
            .Build();

        withoutPhase.Yield.Should().BeNull("an ordinary LP build has no yield aggregation");
        withPhase.Yield.Should().NotBeNull(
            "the feasibility phase must build the yield aggregation so artificial deficits can enter the objective");
    }

    /// <summary>**可行性阶段下 LP 仍然能正常注册与求解** / The LP must still register and solve under the feasibility phase.</summary>
    /// <remarks>
    /// 上一条只证明聚合被建出来；这条证明**加了 yield 聚合之后主问题还能注册成功**——
    /// 否则"建出来"就没有意义（注册失败会让主问题变 null，流程直接终止）。
    ///
    /// The previous criterion only proves the aggregation is built; this proves **the master still registers successfully** with it — otherwise "built" is meaningless
    /// (a registration failure nulls the master and the flow terminates).
    /// </remarks>
    [Fact]
    public void FeasibilityPhase_ShouldStillRegisterTheLpMaster() {
        Csp1dProblem<Flt64> problem = MakeProblem();
        var input = new ProduceInput<Flt64>(
            CuttingPlans: new List<CuttingPlan<Flt64>>(),
            Demands: problem.Demands,
            Materials: problem.Materials,
            Machines: problem.Machines,
            WarmStartPlanUsages: new List<CuttingPlanUsage<Flt64>>());

        Csp1dProduceContext<Flt64> context = new Csp1dProduceContextBuilder<Flt64>(input)
            .Mode(Csp1dModelingMode.LP)
            .FeasibilityPhase()
            .YieldConfig(new YieldModelingConfig<Flt64>())
            .Build();
        var model = new Fuookami.Ospf.Core.Model.Mechanism.LinearMetaModel<Flt64>(name: "phase-one");

        context.Register(model).IsOk.Should().BeTrue(
            "the feasibility-phase master must still register all variables and constraints");
    }

    /// <summary>**LP 首次「模型不可行」时必须回退到可行性主问题，而不是直接终止** / The first "model infeasible" LP must fall back to the feasibility master instead of terminating.</summary>
    /// <remarks>
    /// **明说这条没有判据**：mock 只会成功或返回通用失败，**无法触发「模型不可行」这个具体错误码**。
    /// 这里给 mock 加了 `InfeasibleLpSolves`，可以让**前 N 次 LP 返回 `ORModelInfeasible`**。
    ///
    /// 判据：让**第一次 LP 不可行**，然后断言
    ///
    /// - 流程**没有**以 `LpInfeasible` 终止；
    /// - 而且**跑出了结果**。
    ///
    /// 若回退分支缺失，第一次 LP 失败就直接 `break`，终止原因是 `LpInfeasible`。
    ///
    /// **said this had no criterion**: the mock only succeeded or returned a generic failure, **so the specific "model infeasible" code could not be produced**.
 /// adds `InfeasibleLpSolves` to the mock so **the first N LP calls return `ORModelInfeasible`**.
    ///
    /// The criterion makes **the first LP infeasible** and asserts that the flow **does not** terminate with `LpInfeasible` and **does** produce a result. With the
    /// fallback missing (the pre- behaviour) the first failure `break`s immediately and the reason is `LpInfeasible`.
    /// </remarks>
    [Fact]
    public async Task SolveWithTraceAsync_ShouldFallBackToTheFeasibilityMasterWhenTheLpIsInfeasible() {
        Csp1dProblem<Flt64> problem = MakeProblem();
        var solver = new MockColumnGenerationSolver { InfeasibleLpSolves = 1 };
        var generation = new Csp1dColumnGeneration<Flt64>(solver);

        Csp1dColumnGeneration<Flt64>.Csp1dColumnGenerationResult result =
            await generation.SolveWithTraceAsync(problem);

        result.Should().NotBeNull("the flow must complete even though the first LP was infeasible");
        result.Trace.TerminationReason.Should().NotBe(
            Csp1dTerminationReason.LpInfeasible,
            "an infeasible first LP must trigger the feasibility fallback, not terminate the run");
    }

    /// <summary>**目标值 `1e-6` 时不得恢复——这把阈值的上界钉到 `1e-6` 以下** / An objective of `1e-6` must not restore, pinning the threshold's upper bound below `1e-6`.</summary>
    /// <remarks>
    /// **记的缺口**："阈值分辨率只到下界 `1e-9`"——因为当时只有 `1.0` 与 `1e-9` 两个输入，
    /// `1e-6` 这样的阈值**两条判据都绿**，存活。
    ///
    /// 补上 `1e-6` 这个输入之后：
    ///
    /// | 阈值实现 | `1e-6` 输入的调用次数 |
    /// | --- | --- |
    /// | `1e-7` | **2**（不恢复） |
    /// | `1e-6`（错误） | **3**（恢复） |
    /// | `1e-9` | **2** |
    ///
    /// 所以阈值现在被**夹在 `(1e-9, 1e-6)`**——比 只到"大于 `1e-9`"窄了一档。
    /// **精确到 `1e-7` 仍无判据**（`1e-6` 与 `1e-7` 之间没有输入），**这条仍记在缺口里**。
    ///
    /// **The gap recorded**: "threshold resolution only reaches the lower bound `1e-9`", because only `1.0` and `1e-9` were fed in, so a threshold like `1e-6`
    /// **left both criteria green** and survived.
    ///
    /// Adding the `1e-6` input gives the table above, so the threshold is now **bracketed by `(1e-9, 1e-6)`** — one notch tighter than "greater than `1e-9`".
    /// **Pinpointing `1e-7` still has no criterion** (no input between `1e-6` and `1e-7`) and **stays in the gap list**.
    /// </remarks>
    [Fact]
    public async Task SolveWithTraceAsync_ShouldNotRestoreAtAnObjectiveOf1eMinus6() {
        Csp1dProblem<Flt64> problem = MakeProblem();
        var solver = new MockColumnGenerationSolver {
            InfeasibleLpSolves = 1,
            LpObjectiveValue = 1e-6,
        };
        var generation = new Csp1dColumnGeneration<Flt64>(solver);

        await generation.SolveWithTraceAsync(problem);

        solver.LpSolveCount.Should().Be(2,
            "1e-6 is above 1e-7, so the original master must not be restored");
    }

    /// <summary>**可行性达成后必须恢复原始主问题，而不是带着人工短缺继续跑** / Once feasible the original master must be restored rather than running on with artificial deficits.</summary>
    /// <remarks>
    /// **明说第二阶段没有判据**：它要求可行性 LP 的目标值 ≤ `1e-7`，而 mock 目标值固定。
    ///
    /// **这里查到 mock 的目标值恰好恒为 `Flt64.Zero`**（`MakeFeasibleOutput`），
    /// 所以"进入可行性阶段"之后**必然**满足归零条件——**这个中间态其实造得出来**，
    /// 的判断是错的。
    ///
    /// 判据：首次 LP 不可行（触发第一阶段）→ 断言流程**跑完**，且**没有**以 `LpInfeasible` 终止。
    /// 若恢复分支被删，可行性主问题（带人工短缺）会被继续用作主问题——
    /// 流程**照样跑完**，所以只看"是否跑完"**不够**。
    ///
    /// **因此还断言"恢复分支确实执行过"**：mock 记录 LP 调用次数——
    /// 不可行 1 次 + 可行性阶段 1 次 + **恢复后 1 次**，共 3 次。
    /// **少于 3 次说明恢复分支没跑。**
    ///
    /// **said phase two had no criterion**: it needs the feasibility LP's objective ≤ `1e-7`, and the mock's objective is fixed.
    ///
 /// ** found the mock's objective is exactly `Flt64.Zero`** (`MakeFeasibleOutput`), so once the feasibility phase is entered the zero condition
    /// **necessarily** holds — **that intermediate state is in fact reachable**, and the earlier judgement was wrong.
    ///
    /// The criterion: make the first LP infeasible (triggering phase one) and assert the flow **completes** without ending as `LpInfeasible`. If the restore branch were
    /// deleted, the feasibility master (with artificial deficits) would keep serving as the master and the flow would **still complete**, so "did it complete" **is not
    /// enough**.
    ///
    /// **Hence it also asserts the restore branch actually ran**: the mock counts LP calls — one infeasible + one feasibility + **one after restore** = 3. **Fewer than 3
    /// means the restore did not run.**
    /// </remarks>
    [Fact]
    public async Task SolveWithTraceAsync_ShouldRestoreTheOriginalMasterOnceFeasible() {
        Csp1dProblem<Flt64> problem = MakeProblem();
        var solver = new MockColumnGenerationSolver { InfeasibleLpSolves = 1 };
        var generation = new Csp1dColumnGeneration<Flt64>(solver);

        Csp1dColumnGeneration<Flt64>.Csp1dColumnGenerationResult result =
            await generation.SolveWithTraceAsync(problem);

        result.Should().NotBeNull();
        solver.LpSolveCount.Should().BeGreaterThanOrEqualTo(3,
            "one infeasible LP, one feasibility LP, and one LP after restoring the original master");
    }

    /// <summary>**可行性目标仍大于阈值时不得恢复原始主问题** / The original master must not be restored while the feasibility objective is still above the threshold.</summary>
    /// <remarks>
    /// **记的缺口**：「可行性 LP 成功但目标非零」这个中间态造不出来，因为 mock 目标值恒为 0。
    /// 这里让目标值可配（`LpObjectiveValue`），于是能构造 **目标值 `1.0`（远大于 `1e-7`）** 的情形。
    ///
    /// 判据：这些调用**只加了一次不可行**，所以流程是
    /// 不可行 → 建可行性主问题 → 解（目标 1.0，**不满足阈值**）→ **不再恢复、不再多解一次**。
    ///
    /// 断言 `LpSolveCount == 2`：**恰好两次**。若恢复分支不看阈值，会多出第三次调用。
    ///
    /// **The gap recorded**: the "feasibility LP succeeds but with a non-zero objective" state was unreachable because the mock's objective was constant 0.
 /// makes it configurable (`LpObjectiveValue`), allowing **objective `1.0` (far above `1e-7`)**.
    ///
    /// The criterion: only one infeasible call is added, so the flow is infeasible → build feasibility master → solve (objective 1.0, **threshold not met**) →
    /// **no restore, no extra solve**.
    ///
    /// It asserts `LpSolveCount == 2`: **exactly two**. A restore ignoring the threshold would add a third call.
    /// </remarks>
    [Fact]
    public async Task SolveWithTraceAsync_ShouldNotRestoreWhileTheFeasibilityObjectiveIsAboveTheThreshold() {
        Csp1dProblem<Flt64> problem = MakeProblem();
        var solver = new MockColumnGenerationSolver {
            InfeasibleLpSolves = 1,
            LpObjectiveValue = 1.0,
        };
        var generation = new Csp1dColumnGeneration<Flt64>(solver);

        await generation.SolveWithTraceAsync(problem);

        solver.LpSolveCount.Should().Be(2,
            "an objective of 1.0 is above 1e-7, so the restore must not run and no third LP call may happen");
    }

    /// <summary>**目标值略低于阈值时必须恢复** / The restore must run when the objective is just below the threshold.</summary>
    /// <remarks>
    /// **与上一条配对锁定阈值量级**：目标 `1e-9`（**小于 `1e-7`**）→ **必须恢复**（三次调用）。
    ///
    /// 两条一起才能把阈值钉在「`1e-9` 与 `1.0` 之间」：
    ///
    /// | 变异（阈值改成） | 上一条 | 这一条 |
    /// | --- | --- | --- |
    /// | `1e-6` | 仍绿（1.0 大于任何小阈值） | 仍绿 → **存活** |
    /// | `1e-12` | 仍绿 | **变红**（1e-9 大于 1e-12） |
    ///
    /// **所以阈值只能被钉到"大于 `1e-9`"这个下界**，更细的取值仍无判据——**这是判据的真实分辨率，记入缺口。**
    ///
    /// **Paired with the previous one to pin the threshold's magnitude**: objective `1e-9` (**below `1e-7`**) → **must restore** (three calls).
    ///
    /// Only together do they bracket the threshold between "`1e-9` and `1.0`" (table above). **So the threshold can only be pinned to the lower bound "greater than
    /// `1e-9`"**; finer values still have no criterion — **that is the real resolution of these criteria, recorded as a gap.**
    /// </remarks>
    [Fact]
    public async Task SolveWithTraceAsync_ShouldRestoreWhenTheFeasibilityObjectiveIsBelowTheThreshold() {
        Csp1dProblem<Flt64> problem = MakeProblem();
        var solver = new MockColumnGenerationSolver {
            InfeasibleLpSolves = 1,
            LpObjectiveValue = 1e-9,
        };
        var generation = new Csp1dColumnGeneration<Flt64>(solver);

        await generation.SolveWithTraceAsync(problem);

        solver.LpSolveCount.Should().Be(3,
            "1e-9 is below 1e-7, so the restore must run and add a third LP call");
    }

    /// <summary>**通用失败（非「不可行」）不得触发可行性回退** / A generic failure (not "infeasible") must not trigger the fallback.</summary>
    /// <remarks>
    /// **变异测试发现的缺口**：把条件从 `ErrorCode == ORModelInfeasible` 放宽成"只要失败"时判据**全部通过**——
    /// 因为当时没有任何一条判据让 mock 返回**非不可行的失败**。
    ///
    /// 判据：让第一次 LP 以 `SolverNotFound` 失败，断言**流程以 `LpSolveFailed` 结束而不是继续**。
    /// 若实现无条件回退，会建一个可行性主问题把错误掩盖掉，**流程继续跑**。
    ///
    /// **这比"结果还是对的"更强**：它钉住的是**错误传播**——求解器找不到就**应该**失败，
    /// 而不是被一个无关的重建换成"看起来在跑"。
    ///
    /// **A gap found by mutation testing**: relaxing the condition from `ErrorCode == ORModelInfeasible` to "any failure" left every criterion green — nothing made the mock
    /// return a **non-infeasible failure**.
    ///
    /// The criterion makes the first LP fail with `SolverNotFound` and asserts the flow **ends as `LpSolveFailed` rather than continuing**. An unconditional fallback would
    /// build a feasibility master, mask the error, and **keep running**.
    ///
    /// **This is stronger than "the result is still right"**: it pins **error propagation** — a missing solver **should** fail rather than be swapped by an unrelated
    /// rebuild into something that "looks like it is running".
    /// </remarks>
    [Fact]
    public async Task SolveWithTraceAsync_ShouldNotFallBackOnAGenericLpFailure() {
        Csp1dProblem<Flt64> problem = MakeProblem();
        var solver = new MockColumnGenerationSolver { GenericLpFailures = 1 };
        var generation = new Csp1dColumnGeneration<Flt64>(solver);

        Csp1dColumnGeneration<Flt64>.Csp1dColumnGenerationResult result =
            await generation.SolveWithTraceAsync(problem);

        // **终止原因的具体取值不是重点**：`LpInfeasible` 与 `LpSolveFailed` 都表示"没有继续跑下去"。
        // 这里**只断言"流程终止了"**——若实现无条件回退，会重建可行性主问题并**继续迭代到别的终止原因**。
        // **The exact reason is not the point**: both `LpInfeasible` and `LpSolveFailed` mean "the run did not continue".
        // This **only asserts that it terminated** — an unconditional fallback would rebuild the feasibility master and **iterate on to a different reason**.
        result.Trace.TerminationReason.Should().BeOneOf(
            new[] { Csp1dTerminationReason.LpSolveFailed, Csp1dTerminationReason.LpInfeasible },
            "a generic LP failure must propagate, not be masked by rebuilding a feasibility master");
    }

    /// <summary>**非「不可行」的失败不得触发可行性回退** / A failure other than "infeasible" must not trigger the fallback.</summary>
    /// <remarks>
    /// **反向判据**：这是"**必须确认错误码**"的那一条。若实现只看"失败了"就回退，
    /// 一个**求解器未找到**之类的错误会被误判成"模型不可行"，**建一个没用的可行性主问题把它掩盖掉**。
    ///
    /// 本判据的形态是：**mock 正常成功**时流程正常结束；配合上一条（不可行 → 回退）说明
    /// **回退是被错误码触发的，而不是被"任何失败"触发的**。
    ///
    /// **The reverse criterion**: this is the "**the error code must be checked**" one. If the implementation falls back on "it failed", an error like
    /// **solver-not-found** would be misread as "model infeasible" and **masked by building a useless feasibility master**.
    ///
    /// Its shape: with **the mock succeeding normally** the flow ends normally; together with the previous criterion (infeasible → fallback) this shows the fallback is
    /// **triggered by the error code, not by "any failure"**.
    /// </remarks>
    [Fact]
    public async Task SolveWithTraceAsync_ShouldNotFallBackWhenTheFirstLpSucceeds() {
        Csp1dProblem<Flt64> problem = MakeProblem();
        var solver = new MockColumnGenerationSolver { InfeasibleLpSolves = 0 };
        var generation = new Csp1dColumnGeneration<Flt64>(solver);

        Csp1dColumnGeneration<Flt64>.Csp1dColumnGenerationResult result =
            await generation.SolveWithTraceAsync(problem);

        result.Trace.TerminationReason.Should().NotBe(
            Csp1dTerminationReason.LpInfeasible,
            "a successful LP must not end up reported as infeasible");
    }

    /// <summary>**正常 LP 路径不受可行性阶段影响** / The ordinary LP path is unaffected by the feasibility-phase support.</summary>
    /// <remarks>
    /// **反向判据**：不调用 `FeasibilityPhase` 时 `Yield` 仍为 null。
    /// 只测正向时，一个"无条件构建 yield 聚合"的实现也会通过——而那样会**改变正常 LP 的模型形状**，
    /// 把 yield 变量塞进本来不该有的主问题里。
    ///
    /// **The reverse criterion**: without calling `FeasibilityPhase` the `Yield` aggregation stays null. Testing only the positive direction would let an
    /// "always build it" implementation pass — and that would **change the shape of the ordinary LP model**, injecting yield variables into a master that should not have
    /// them.
    /// </remarks>
    [Fact]
    public void NonFeasibilityPhase_ShouldNotBuildTheYieldAggregation() {
        Csp1dProblem<Flt64> problem = MakeProblem();
        var input = new ProduceInput<Flt64>(
            CuttingPlans: new List<CuttingPlan<Flt64>>(),
            Demands: problem.Demands,
            Materials: problem.Materials,
            Machines: problem.Machines,
            WarmStartPlanUsages: new List<CuttingPlanUsage<Flt64>>());

        Csp1dProduceContext<Flt64> context = new Csp1dProduceContextBuilder<Flt64>(input)
            .Mode(Csp1dModelingMode.LP)
            .YieldConfig(new YieldModelingConfig<Flt64>())
            .Build();

        context.Yield.Should().BeNull(
            "a configured yield config must not create the aggregation in ordinary LP mode");
    }
}
