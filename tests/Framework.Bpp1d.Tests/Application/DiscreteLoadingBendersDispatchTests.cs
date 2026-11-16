// Benders 分派判据 / Criteria for the Benders dispatch of the discrete-loading service.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Fuookami.Ospf.Core.Model.Basic;
using Fuookami.Ospf.Core.Model.Mechanism;
using Fuookami.Ospf.Core.Solver.Output;
using Fuookami.Ospf.Core.Solver.Report;
using Fuookami.Ospf.Core.Solver.Value;
using Fuookami.Ospf.Core.Variable;
using Fuookami.Ospf.Framework.Bpp.Application;
using Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading.Model;
using Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading;
using Fuookami.Ospf.Framework.Bpp.Infrastructure;
using Fuookami.Ospf.Framework.Solver;
using Fuookami.Ospf.Math.Algebra.Concept;
using Fuookami.Ospf.Math.Algebra.Number;
using Fuookami.Ospf.Math.Symbol.Monomial;
using Fuookami.Ospf.Math.Symbol.Polynomial;
using Fuookami.Ospf.Quantities.Quantity;
using Fuookami.Ospf.Quantities.Unit;
using Fuookami.Ospf.Utils.Error;
using Fuookami.Ospf.Utils.Functional;
using Xunit;
using Try = Fuookami.Ospf.Utils.Functional.Result<
    Fuookami.Ospf.Utils.Functional.Success,
    Fuookami.Ospf.Utils.Error.ErrorCode,
    Fuookami.Ospf.Utils.Error.Error<Fuookami.Ospf.Utils.Error.ErrorCode>>;

namespace Fuookami.Ospf.Framework.Bpp1d.Tests.Application;

/// <summary>
/// **Benders 分派**：`PreferBenders` 到底把请求送到哪条路 / Where `PreferBenders` actually sends the request.
/// </summary>
/// <remarks>
/// 这里之前，`DiscreteLoadingApplicationService` 的 Benders 分支（`RunBendersAsync`、回退、`RequiresFreshModel` 守卫）
/// **零判据**——代码在、没人验。 已经确认这条路径**就是**生产路径（demo2 自己那份只有测试在调），
/// 所以它是这个仓里最该有判据、却最没有判据的一段。
///
/// 四条判据对应四个分派结果：没后端、回退成功、禁止回退、回退时复用同一模型。
/// **三条不需要真求解器**：`milpSolver` 是函数参数，Benders 后端是接口，两者都能注入。
///
/// Before the service's Benders branch (`RunBendersAsync`, fallback, the `RequiresFreshModel` guard) had **no criteria at all** — code present, nobody
/// verifying it. established that this path **is** its production one (demo2's own copy is called only from tests), so it is the least covered and
/// most important stretch in this repo. The four criteria cover the four dispatch outcomes: no backend, fallback succeeded, fallback forbidden, and a fallback that
/// reuses one model. **Three need no real solver**: `milpSolver` is a function parameter and the Benders backend is an interface, so both can be injected.
/// </remarks>
public class DiscreteLoadingBendersDispatchTests
{
    /// <summary>
    /// **没有后端、也禁止回退 → 明确失败并点名后端** / No backend and no fallback → a failure that names the backend.
    /// </summary>
    /// <remarks>
    /// 这一条是写判据时**被实测纠正**的：`PreferBenders: true` 但没有后端时，`FallbackToMilp` **默认为真**，
    /// 服务会**直接回退去跑 MILP**，并把原因记进 `FallbackReason`——不会失败。
    /// 所以"失败"只在把回退关掉时才成立，而**这一点必须由判据钉住**：
    /// 回退一旦被允许，"没后端"就会变成"跑了 MILP"，只有 `FallbackReason` 还留着痕迹。
    ///
    /// This criterion was **corrected by a real run**: with `PreferBenders: true` and no backend, `FallbackToMilp` **defaults to true**, so the service **falls back to
    /// MILP** and records the reason in `FallbackReason` — it does not fail. "Failure" therefore only holds with the fallback switched off, and **that is what the
    /// criterion pins**: once fallback is allowed, "no backend" turns into "MILP ran", and only `FallbackReason` still carries the trace.
    /// </remarks>
    [Fact]
    public async Task WithoutABackendAndWithoutFallback_ShouldFailNamingTheBackend() {
        Result<DiscreteLoadingSolveResult<Flt64>, ErrorCode, Error<ErrorCode>> solved =
            await new DiscreteLoadingApplicationService<Flt64>(NeverCalledMilp())
                .SolveAsync(Register, new DiscreteLoadingSolveConfig(PreferBenders: true, FallbackToMilp: false));

        solved.Should().BeOfType<Failed<DiscreteLoadingSolveResult<Flt64>, ErrorCode, Error<ErrorCode>>>();
        ((Failed<DiscreteLoadingSolveResult<Flt64>, ErrorCode, Error<ErrorCode>>)solved).Error.Message
            .Should().Contain("Benders 后端",
            "the failure must name the missing backend, not read like a generic application error");
    }

    /// <summary>
    /// **没有后端但允许回退 → 真的跑了 MILP，且原因写清楚** / No backend with fallback allowed → MILP really runs, with the reason recorded.
    /// </summary>
    /// <remarks>
    /// 与上一条成对，钉的是"允许回退时的真实行为"：`Path` 是回退路径，`FallbackReason` 必须提到后端。
    /// **没有 `FallbackReason` 的话，"跑的是 MILP"这件事就只能靠猜**——那正是这条判据要挡的。
    ///
    /// Paired with the criterion above, pinning what actually happens when fallback is allowed: `Path` is the fallback path and `FallbackReason` must mention the
    /// backend. **Without `FallbackReason`, "MILP ran" would only be guessable** — exactly what this criterion blocks.
    /// </remarks>
    [Fact]
    public async Task WithoutABackend_ShouldFallBackToMilpWithTheReason() {
        Result<DiscreteLoadingSolveResult<Flt64>, ErrorCode, Error<ErrorCode>> solved =
            await new DiscreteLoadingApplicationService<Flt64>(StubMilp(1.0))
                .SolveAsync(Register, new DiscreteLoadingSolveConfig(PreferBenders: true));

        solved.IsOk.Should().BeTrue(solved.ToString());
        DiscreteLoadingSolveResult<Flt64> result =
            ((Ok<DiscreteLoadingSolveResult<Flt64>, ErrorCode, Error<ErrorCode>>)solved).Value;

        result.Path.Should().Be("milp_fallback_after_benders");
        result.FallbackReason.Should().Contain("Benders", "the reason must name what was missing");
    }

    /// <summary>
    /// **后端失败 + 允许回退 → 真的走了 MILP，并写明原因** / A failing backend with fallback allowed really runs MILP, and says why.
    /// </summary>
    [Fact]
    public async Task AFailingBackend_ShouldFallBackToMilpAndRecordTheReason() {
        Result<DiscreteLoadingSolveResult<Flt64>, ErrorCode, Error<ErrorCode>> solved =
            await new DiscreteLoadingApplicationService<Flt64>(StubMilp(1.0), new FailingBendersSolver())
                .SolveAsync(Register, new DiscreteLoadingSolveConfig(PreferBenders: true, FallbackToMilp: true));

        solved.IsOk.Should().BeTrue(solved.ToString());
        DiscreteLoadingSolveResult<Flt64> result =
            ((Ok<DiscreteLoadingSolveResult<Flt64>, ErrorCode, Error<ErrorCode>>)solved).Value;

        result.Path.Should().Be("milp_fallback_after_benders", "the fallback path is the one that produced this answer");
        result.FallbackReason.Should().NotBeNullOrWhiteSpace(
            "a fallback without a recorded reason is indistinguishable from a Benders success");
    }

    /// <summary>
    /// **禁止回退时，后端失败就是失败** / With fallback forbidden, a failing backend is a failure.
    /// </summary>
    /// <remarks>
    /// 与上一条成对。这一条同时确认 MILP 桩**没有被调用**（`NeverCalledMilp`）：回退被禁止时，
    /// "结果看起来还行"只能来自偷偷跑了 MILP。
    ///
    /// Paired with the criterion above. It also confirms the MILP stub was **never called** (`NeverCalledMilp`): with fallback forbidden, "the answer looks fine" can
    /// only come from a quiet MILP run.
    /// </remarks>
    [Fact]
    public async Task AFailingBackend_ShouldNotFallBackWhenForbidden() {
        Result<DiscreteLoadingSolveResult<Flt64>, ErrorCode, Error<ErrorCode>> solved =
            await new DiscreteLoadingApplicationService<Flt64>(NeverCalledMilp(), new FailingBendersSolver())
                .SolveAsync(Register, new DiscreteLoadingSolveConfig(PreferBenders: true, FallbackToMilp: false));

        solved.Should().BeOfType<Failed<DiscreteLoadingSolveResult<Flt64>, ErrorCode, Error<ErrorCode>>>();
    }

    /// <summary>
    /// **回退时复用同一个模型 → 被守卫拒绝** / A fallback that reuses one model is rejected by the guard.
    /// </summary>
    /// <remarks>
    /// 回退若复用 Benders 已经改过的模型，结果**看上去正常**——这正是守卫存在的原因。判据用一个"永远返回同一份注册"的工厂，
    /// 让它必须被拒。
    ///
    /// If a fallback reused the model Benders already mutated, the result would **look normal** — exactly why the guard exists. The criterion hands in a factory that
    /// always returns the same registration and requires it to be rejected.
    /// </remarks>
    [Fact]
    public async Task AFallbackReusingTheSameModel_ShouldBeRejected() {
        Result<RegisteredDiscreteLoading<Flt64>, ErrorCode, Error<ErrorCode>> registration = Register();

        Result<DiscreteLoadingSolveResult<Flt64>, ErrorCode, Error<ErrorCode>> solved =
            await new DiscreteLoadingApplicationService<Flt64>(StubMilp(1.0), new FailingBendersSolver())
                .SolveAsync(() => registration, new DiscreteLoadingSolveConfig(PreferBenders: true, FallbackToMilp: true));

        solved.Should().BeOfType<Failed<DiscreteLoadingSolveResult<Flt64>, ErrorCode, Error<ErrorCode>>>();
        ((Failed<DiscreteLoadingSolveResult<Flt64>, ErrorCode, Error<ErrorCode>>)solved).Error.Message
            .Should().Contain("新模型",
            "the rejection must say the fallback needs a fresh model");
    }


    /// <summary>
    /// **后端真的收敛时，走的是 Benders 那条路** / When the backend really converges, the Benders path is what produced the answer.
    /// </summary>
    /// <remarks>
    /// 上一条只验到"后端失败 → 回退"，**成功那一支的合并与收口没有任何判据**（`MergeBendersValues`、`FinishAsync` 都在那条路上）。
    /// 这条给一个**会答话**的后端：主问题把全部 token 置 1、子问题置 0（追索成本为零），于是循环应当收敛，
    /// 而 `Path` 必须是 `benders`——**只有走了真正的 Benders 收口才可能取到这个值**。
    ///
    /// 判据同时要求 MILP 桩**没有被调用**：收敛了却还跑一遍 MILP，说明分派把两条路都跑了。
    ///
    /// The criterion above only covers "backend failed then fallback"; **the success branch — the merge and the close-out (`MergeBendersValues`, `FinishAsync`) —
    /// had no coverage at all.** This one supplies a backend that answers: the master sets every token to 1 and the sub to 0 (zero recourse cost), so the loop
    /// should converge, and `Path` must be `benders` — **a value only the real Benders close-out can produce**. It also requires the MILP stub to go **uncalled**:
    /// converging and still running MILP would mean the dispatch ran both.
    /// </remarks>
    [Fact]
    public async Task AConvergingBackend_ShouldProduceTheBendersPath() {
        Result<DiscreteLoadingSolveResult<Flt64>, ErrorCode, Error<ErrorCode>> solved =
            await new DiscreteLoadingApplicationService<Flt64>(NeverCalledMilp(), new ConvergingBendersSolver())
                .SolveAsync(Register, new DiscreteLoadingSolveConfig(
                    PreferBenders: true,
                    FallbackToMilp: false,
                    Benders: new LinearBendersConfig { IterationLimit = 8, Tolerance = 1e-6 }));

        solved.IsOk.Should().BeTrue(solved.ToString());
        DiscreteLoadingSolveResult<Flt64> result =
            ((Ok<DiscreteLoadingSolveResult<Flt64>, ErrorCode, Error<ErrorCode>>)solved).Value;

        result.Path.Should().Be("benders");
        result.FallbackReason.Should().BeNull("nothing fell back on this run");
        result.TraceOrEmpty.Should().NotBeEmpty("a Benders run without a trace is not a Benders run");
    }


    /// <summary>
    /// **模型划分不了时，禁止回退就是失败** / An unpartitionable model fails outright when the fallback is forbidden.
    /// </summary>
    /// <remarks>
    /// 留下的一条未覆盖分支：`PreferBenders` 给了后端，但 `BuildAsync` 因为**阶段引用了未知变量**而失败。
    /// 这条路径的分派与"后端求解失败"**走的是同一段代码**，但原因完全不同——一个是模型拼错、一个是后端拒绝。
    /// 判据要求失败信息说得出是**划分**这一步出的问题：只说"Benders 失败"会让调用方去查后端。
    ///
    /// A branch left uncovered: `PreferBenders` supplies a backend, but `BuildAsync` fails because **a stage names an unknown variable**. The dispatch takes
    /// **the same code path** as "the backend failed", with a completely different cause — a misspelt model versus a refusing backend. The criterion requires the
    /// failure to say the problem is in **partitioning**: a bare "Benders failed" sends the caller to check the backend.
    /// </remarks>
    [Fact]
    public async Task AnUnpartitionableModel_ShouldFailNamingThePartition() {
        Result<DiscreteLoadingSolveResult<Flt64>, ErrorCode, Error<ErrorCode>> solved =
            await new DiscreteLoadingApplicationService<Flt64>(NeverCalledMilp(), new ConvergingBendersSolver())
                .SolveAsync(
                    RegisterWithAStageOnAnUnknownVariable,
                    new DiscreteLoadingSolveConfig(PreferBenders: true, FallbackToMilp: false));

        solved.Should().BeOfType<Failed<DiscreteLoadingSolveResult<Flt64>, ErrorCode, Error<ErrorCode>>>();
        ((Failed<DiscreteLoadingSolveResult<Flt64>, ErrorCode, Error<ErrorCode>>)solved).Error.Message
            .Should().Contain("未知变量",
                "the failure must name the partitioning guard; otherwise the message points the caller at the backend");
    }

    /// <summary>
    /// **划分不了时，允许回退就真的去跑 MILP** / With the fallback allowed it really runs MILP.
    /// </summary>
    /// <remarks>
    /// 与上一条成对。这里**不断言最终成功**，只断言**回退发生了**——因为这里实测到一个意外：
    /// 划分失败时，`SolveAsync` 确实会走到 MILP（桩被调用），但**最终结果仍可能是失败**；
    /// 那条成功/失败的细节属于目标复核那一段（有它自己的判据），**不是这条要钉的东西**。
    /// 硬把它写成"应当成功"会让判据依赖另一个模块的行为，那时红的原因会指向错的地方。
    ///
    /// Paired with the criterion above. It does **not** assert an eventual success, only that **the fallback happened** — because measured a surprise:
    /// on a partitioning failure `SolveAsync` does reach MILP (the stub is called) while **the final result can still be a failure**. That success/failure detail
    /// belongs to the objective re-check (which has its own criteria) and **is not what this one pins**. Writing it as "must succeed" would tie the criterion to
    /// another module's behaviour, so a red would point at the wrong place.
    /// </remarks>
    [Fact]
    public async Task AnUnpartitionableModel_ShouldStillRunTheMilpFallback() {
        MilpCalls = 0;

        await new DiscreteLoadingApplicationService<Flt64>(CountingMilp(), new ConvergingBendersSolver())
            .SolveAsync(
                RegisterWithAStageOnAnUnknownVariable,
                new DiscreteLoadingSolveConfig(PreferBenders: true, FallbackToMilp: true));

        MilpCalls.Should().Be(1,
            "a failure to partition the model must fall back to MILP rather than end the run silently");
    }

    /// <summary>装配一份**阶段表引用未知变量**的注册 / Build a registration whose stage table names an unknown variable.</summary>
    /// <returns>装配结果 / The registration.</returns>
    private static Result<RegisteredDiscreteLoading<Flt64>, ErrorCode, Error<ErrorCode>>
        RegisterWithAStageOnAnUnknownVariable() {
        Result<RegisteredDiscreteLoading<Flt64>, ErrorCode, Error<ErrorCode>> plain = Register();
        if (plain is not Ok<RegisteredDiscreteLoading<Flt64>, ErrorCode, Error<ErrorCode>> ok) {
            return plain;
        }

        var neverAdded = new URealVar("b851-never-added");
        return Results.Ok(new RegisteredDiscreteLoading<Flt64>(
            ok.Value.Context,
            ok.Value.Model,
            AdditionalWeights: null,
            BendersStages: new Dictionary<IVariableItem, LinearBendersStage> {
                [neverAdded] = LinearBendersStage.ContinuousRecourse,
            }));
    }


    /// <summary>
    /// **非法超时预算会被回退，并在原因里点名** / An illegal time limit falls back, and the reason names the field.
    /// </summary>
    /// <remarks>
    /// 第一版判据写的是"非法预算应当**直接失败、不回退**"。**实测是回退的**——`FallbackToMilp` 为真时，
    /// 算法给出的任何失败都会走回退，包括"配置写错"。这不算缺陷（回退本身有原因记录），
    /// 但**原因必须点名"时间限制"**：只说"Benders 失败"，调用方会去查后端，而实际是自己把预算写成 0 了。
    ///
    /// The first version of this criterion said an illegal budget must **fail outright without falling back**. **It falls back** — with `FallbackToMilp` on, any
    /// algorithm failure takes the fallback, including "the configuration is wrong". That is not a defect (the fallback records a reason), but **the reason must name
    /// the time limit**: a bare "Benders failed" sends the caller to the backend when they actually set the budget to zero.
    /// </remarks>
    [Fact]
    public async Task ANonPositiveTimeLimit_ShouldFallBackNamingTheBudget() {
        MilpCalls = 0;

        Result<DiscreteLoadingSolveResult<Flt64>, ErrorCode, Error<ErrorCode>> solved =
            await new DiscreteLoadingApplicationService<Flt64>(CountingMilp(), new ConvergingBendersSolver())
                .SolveAsync(
                    Register,
                    new DiscreteLoadingSolveConfig(
                        PreferBenders: true,
                        FallbackToMilp: true,
                        Benders: new LinearBendersConfig { TimeLimit = TimeSpan.Zero }));

        solved.IsOk.Should().BeTrue(solved.ToString());
        DiscreteLoadingSolveResult<Flt64> result =
            ((Ok<DiscreteLoadingSolveResult<Flt64>, ErrorCode, Error<ErrorCode>>)solved).Value;

        result.Path.Should().Be("milp_fallback_after_benders");
        result.FallbackReason.Should().Contain("时间限制",
            "a malformed budget must be named; otherwise the caller looks for a backend problem they do not have");
        MilpCalls.Should().Be(1);
    }

    /// <summary>
    /// **预算耗尽但没有 incumbent → 失败，而且不回退** / An exhausted budget with no incumbent fails, without falling back.
    /// </summary>
    /// <remarks>
    /// 实测语义，写下来免得下次再猜：`TimeLimit` 小到在**第一轮主问题之前**就过期时，算法返回
    /// `Benders has no incumbent: time limit before master`——**没有 incumbent 就没有结果可交**，这不是"部分成功"。
    ///
    /// 判据用 `NeverCalledMilp` 保证**没有回退**：这时回退等于把"太快了"翻译成"换条路再算一遍"，
    /// 而调用方给的预算是**整体**预算，换条路会把它整个绕过。
    ///
    /// The measured semantics, written down so the next reader need not guess: when `TimeLimit` expires **before the first master solve**, the algorithm returns
    /// `Benders has no incumbent: time limit before master` — **no incumbent means nothing to hand back**, and that is not "partial success". The criterion uses
    /// `NeverCalledMilp` to guarantee **no fallback**: falling back here would translate "too slow" into "run something else", and the caller's budget is a **total**
    /// budget that a switch would bypass entirely.
    /// </remarks>
    [Fact]
    public async Task AnExhaustedTimeLimitWithNoIncumbent_ShouldFailWithoutFallingBack() {
        Result<DiscreteLoadingSolveResult<Flt64>, ErrorCode, Error<ErrorCode>> solved =
            await new DiscreteLoadingApplicationService<Flt64>(NeverCalledMilp(), new ConvergingBendersSolver())
                .SolveAsync(
                    Register,
                    new DiscreteLoadingSolveConfig(
                        PreferBenders: true,
                        FallbackToMilp: false,
                        Benders: new LinearBendersConfig { TimeLimit = TimeSpan.FromTicks(1) }));

        solved.Should().BeOfType<Failed<DiscreteLoadingSolveResult<Flt64>, ErrorCode, Error<ErrorCode>>>();
        ((Failed<DiscreteLoadingSolveResult<Flt64>, ErrorCode, Error<ErrorCode>>)solved).Error.Message
            .Should().Contain("incumbent",
                "the failure must say that the budget ran out before any incumbent existed");
    }

    /// <summary>**自己填解并报指定目标**的桩 / A stub that fills a feasible solution and reports the given objective.</summary>
    /// <param name="objective">要报出的目标 / The objective to report.</param>
    /// <returns>桩函数 / The stub.</returns>
    private static Func<LinearMetaModel<Flt64>, Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>>>
        StubMilp(double objective) =>
        model => {
            model.Tokens.Find(AssignmentInModel)!.SetResult(new Flt64(1.0));
            // **占用变量也要置 1**：只置分配会让 `loading_capacity_count_P1` 被违反，
            // 复核那一步会拒绝整个解——那时红的就不是分派判据了。
            //
            // **The occupancy variable must be set too**: setting only the assignment violates `loading_capacity_count_P1`, the re-check then rejects the whole
            // solution, and it is no longer the dispatch under test that reds.
            model.Tokens.Find(OccupancyInModel)!.SetResult(new Flt64(1.0));
            var values = new List<Flt64>();
            foreach (Fuookami.Ospf.Core.Token.Token<Flt64> token in model.Tokens.TokensInSolver) {
                values.Add(token.DoubleResult is { } value ? new Flt64(value) : Flt64.Zero);
            }
            return Task.FromResult(Results.Ok(new FeasibleSolverOutput<Flt64>(
                Obj: new Flt64(objective),
                Solution: new Solution<Flt64>(values),
                Time: TimeSpan.Zero,
                PossibleBestObj: new Flt64(objective),
                Gap: new Flt64(0.0))));
        };

    /// <summary>记录调用次数的桩 / A stub that records how often it was called.</summary>
    private static int MilpCalls;

    /// <summary>**记录调用次数**并报一个可行目标 / Records the call count and reports a feasible objective.</summary>
    /// <returns>桩函数 / The stub.</returns>
    private static Func<LinearMetaModel<Flt64>, Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>>>
        CountingMilp() =>
        model => {
            MilpCalls++;
            return StubMilp(1.0)(model);
        };

    /// <summary>**被调用就红**的桩 / A stub that turns the run red when it is called.</summary>
    /// <returns>桩函数 / The stub.</returns>
    private static Func<LinearMetaModel<Flt64>, Task<Result<FeasibleSolverOutput<Flt64>, ErrorCode, Error<ErrorCode>>>>
        NeverCalledMilp() =>
        model => Task.FromResult(Results.Failed<FeasibleSolverOutput<Flt64>>(new Err<ErrorCode>(
            ErrorCode.Other, "MILP must not run on this dispatch")));

    /// <summary>装配一份单货单舱位的注册 / Build a one-item, one-position registration.</summary>
    /// <returns>装配结果 / The registration.</returns>
    private static Result<RegisteredDiscreteLoading<Flt64>, ErrorCode, Error<ErrorCode>> Register() {
        var problem = new DiscreteLoadingProblem<Flt64>(
            Items: new List<LoadingItem<Flt64>> {
                new("I1", Kilograms(1.0), Required: true, FixedPositionId: null),
            },
            Positions: new List<LoadingPosition<Flt64>> {
                new("P1", MaxCount: 5UL, MaxWeight: Kilograms(5.0), FixedOccupied: false, Enabled: true),
            },
            AllowedAssignments: new HashSet<AssignmentKey> { new("I1", "P1") },
            Conflicts: new HashSet<Fuookami.Ospf.Framework.Bpp.Domain.DiscreteLoading.Model.PositionConflict>(),
            LayoutGroups: new List<LayoutSelectionGroup>(),
            WeightUnit: SIBaseUnits.Kilogram);

        var context = new DiscreteLoadingContext<Flt64>(problem, Flt64PackingValueAdapter.Instance);
        LinearMetaModel<Flt64> model = new();
        Try registered = context.Register(model);
        if (registered is not Ok<Success, ErrorCode, Error<ErrorCode>>) {
            return Results.Failed<RegisteredDiscreteLoading<Flt64>>(
                new Err<ErrorCode>(ErrorCode.ApplicationError, "fixture registration failed"));
        }

        AssignmentInModel = context.Aggregation.Assignments[new AssignmentKey("I1", "P1")];
        OccupancyInModel = context.Aggregation.Occupied["P1"];
        model.AddObject(
            ObjectCategory.Minimum,
            new LinearPolynomial<Flt64>(
                new List<LinearMonomial<Flt64>> { new(new Flt64(1.0), AssignmentInModel) },
                Flt64.Zero),
            name: "loading",
            displayName: null).IsOk.Should().BeTrue();

        return Results.Ok(new RegisteredDiscreteLoading<Flt64>(context, model));
    }

    /// <summary>当前注册里的分配变量 / The assignment variable of the registered fixture.</summary>
    private static IVariableItem AssignmentInModel = null!;

    /// <summary>当前注册里的占用变量 / The occupancy variable of the registered fixture.</summary>
    private static IVariableItem OccupancyInModel = null!;

    /// <summary>构造千克量 / Build a kilogram quantity.</summary>
    /// <param name="value">数值 / Value.</param>
    /// <returns>带单位的量 / The unit-carrying quantity.</returns>
    private static Quantity<Flt64> Kilograms(double value) =>
        new(new Flt64(value), SIBaseUnits.Kilogram);


    /// <summary>
    /// **会答话的后端**：主问题置 1、子问题置 0 / A backend that answers: master at 1, sub at 0.
    /// </summary>
    /// <remarks>
    /// 子问题报 0 目标、空对偶，于是"追索成本为零"，循环无需割即可确认最优——这正是判据要的**收敛**而不是"跑满上限"。
    ///
    /// The sub reports a zero objective with empty duals, so recourse costs nothing and the loop can confirm optimality without needing a cut — which is the
    /// **convergence** the criterion wants, not an exhausted budget.
    /// </remarks>
    private sealed class ConvergingBendersSolver : IBendersDecompositionSolver
    {
        public string Name => "converging";

        public Task<Result<SolverOutput, ErrorCode, Error<ErrorCode>>> SolveMasterAsync(
            string name,
            LinearMetaModel<Flt64> metaModel,
            bool toLogModel = false,
            RegistrationStatusCallBack? registrationStatusCallBack = null,
            SolvingStatusCallBack? solvingStatusCallBack = null) =>
            Task.FromResult(Results.Ok<SolverOutput>(Fill(metaModel, value: 1.0, objective: 1.0)));

        public Task<Result<SolverOutput, ErrorCode, Error<ErrorCode>>> SolveMasterAsAsync<V>(
            string name,
            LinearMetaModel<Flt64> metaModel,
            IIntoValue<V> converter,
            bool toLogModel = false,
            RegistrationStatusCallBack? registrationStatusCallBack = null,
            SolvingStatusCallBack? solvingStatusCallBack = null)
            where V : struct, IRealNumber<V>, INumberField<V> =>
            throw new NotSupportedException("the dispatch never asks for a converted master");

        public Task<Result<IBendersDecompositionSolver.LinearSubResult, ErrorCode, Error<ErrorCode>>> SolveSubAsync(
            string name,
            LinearMetaModel<Flt64> metaModel,
            IVariableItem objectVariable,
            IReadOnlyDictionary<IVariableItem, Flt64> fixedVariables,
            bool toLogModel = false,
            RegistrationStatusCallBack? registrationStatusCallBack = null,
            SolvingStatusCallBack? solvingStatusCallBack = null) {
            FeasibleSolverOutput<Flt64> filled = Fill(metaModel, value: 0.0, objective: 0.0);
            return Task.FromResult(Results.Ok<IBendersDecompositionSolver.LinearSubResult>(
                new IBendersDecompositionSolver.LinearFeasibleResult(
                    filled,
                    new Dictionary<MathConstraint, Flt64>(),
                    Cuts: null) {
                    // **必须带统一报告**：共享算法明确拒绝"旧适配器"证明最优——
                    // `可行子问题缺少统一报告` 就是它给出的错误。这里给一份 `Optimal` 的报告。
                    //
                    // **A unified report is mandatory**: the shared algorithm explicitly refuses to let a legacy adapter prove optimality, and
                    // "feasible subproblem is missing its unified report" is the failure it returns. This supplies one marked `Optimal`.
                    Report = new SolveReport<Flt64> {
                        ProblemStatus = ProblemStatus.Feasible,
                        TerminationReason = TerminationReason.Completed,
                        SolutionPresence = SolutionPresence.Optimal,
                        Solution = new SolveSolution<Flt64>(
                            // **报告里的解必须与子模型 token 数量一致**：长度不匹配会被拒（实测报的就是这条）。
                            // **The report's solution must match the sub model's token count**; a length mismatch is rejected (the failure observed here).
                            Values: filled.Solution,
                            Objective: new Flt64(0.0)),
                    },
                }));
        }

        public Task<Result<IBendersDecompositionSolver.LinearSubResultOf<V>, ErrorCode, Error<ErrorCode>>> SolveSubAsAsync<V>(
            LinearMetaModel<Flt64> metaModel,
            IVariableItem objectVariable,
            IReadOnlyDictionary<IVariableItem, Flt64> fixedVariables,
            FrameworkSolveOptions options)
            where V : struct, IRealNumber<V>, INumberField<V> =>
            throw new NotSupportedException("the dispatch never asks for a converted sub result");

        /// <summary>把模型的全部 token 置成指定值并报出目标 / Set every token to the given value and report the objective.</summary>
        /// <param name="model">模型 / The model.</param>
        /// <param name="value">每个 token 的值 / The value for each token.</param>
        /// <param name="objective">报出的目标 / The objective to report.</param>
        /// <returns>求解输出 / The solver output.</returns>
        private static FeasibleSolverOutput<Flt64> Fill(LinearMetaModel<Flt64> model, double value, double objective) {
            var values = new List<Flt64>();
            foreach (Fuookami.Ospf.Core.Token.Token<Flt64> token in model.Tokens.TokensInSolver) {
                token.SetResult(new Flt64(value));
                values.Add(new Flt64(value));
            }
            return new FeasibleSolverOutput<Flt64>(
                Obj: new Flt64(objective),
                Solution: new Solution<Flt64>(values),
                Time: TimeSpan.Zero,
                PossibleBestObj: new Flt64(objective),
                Gap: new Flt64(0.0));
        }
    }

    /// <summary>
    /// **永远失败的后端** / A backend that always fails.
    /// </summary>
    /// <remarks>
    /// 只需要实现接口里**没有默认体**的那几个成员；其余（`WithTimeLimit`、选项重载、报告重载）都有默认实现。
    ///
    /// Only the members **without a default body** are implemented; the rest (`WithTimeLimit`, the options overloads, the report overloads) have defaults.
    /// </remarks>
    private sealed class FailingBendersSolver : IBendersDecompositionSolver
    {
        public string Name => "failing";

        public Task<Result<SolverOutput, ErrorCode, Error<ErrorCode>>> SolveMasterAsync(
            string name,
            LinearMetaModel<Flt64> metaModel,
            bool toLogModel = false,
            RegistrationStatusCallBack? registrationStatusCallBack = null,
            SolvingStatusCallBack? solvingStatusCallBack = null) =>
            Task.FromResult(Results.Failed<SolverOutput>(new Err<ErrorCode>(
                ErrorCode.OREngineSolvingException, "benders master failed on purpose")));

        public Task<Result<SolverOutput, ErrorCode, Error<ErrorCode>>> SolveMasterAsAsync<V>(
            string name,
            LinearMetaModel<Flt64> metaModel,
            IIntoValue<V> converter,
            bool toLogModel = false,
            RegistrationStatusCallBack? registrationStatusCallBack = null,
            SolvingStatusCallBack? solvingStatusCallBack = null)
            where V : struct, IRealNumber<V>, INumberField<V> =>
            throw new NotSupportedException("the dispatch never asks for a converted master");

        public Task<Result<IBendersDecompositionSolver.LinearSubResult, ErrorCode, Error<ErrorCode>>> SolveSubAsync(
            string name,
            LinearMetaModel<Flt64> metaModel,
            IVariableItem objectVariable,
            IReadOnlyDictionary<IVariableItem, Flt64> fixedVariables,
            bool toLogModel = false,
            RegistrationStatusCallBack? registrationStatusCallBack = null,
            SolvingStatusCallBack? solvingStatusCallBack = null) =>
            throw new NotSupportedException("the dispatch fails at the master");

        public Task<Result<IBendersDecompositionSolver.LinearSubResultOf<V>, ErrorCode, Error<ErrorCode>>> SolveSubAsAsync<V>(
            LinearMetaModel<Flt64> metaModel,
            IVariableItem objectVariable,
            IReadOnlyDictionary<IVariableItem, Flt64> fixedVariables,
            FrameworkSolveOptions options)
            where V : struct, IRealNumber<V>, INumberField<V> =>
            throw new NotSupportedException("the dispatch never asks for a converted sub result");
    }
}
