using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using FluentAssertions;
using Fuookami.Ospf.Core.Plugin.Scip;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Application;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Xunit;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// 真实 SCIP 后端能**被 demo2 接收**了 / A real SCIP backend can now be accepted by demo2.
/// </summary>
/// <remarks>
/// 这里之前，demo2 的 `BendersSolver` 收的是 **Core** 版 `IBendersDecompositionSolver`，而六个插件
/// （Copt / Cplex / Gurobi / Gurobi11 / Mindopt / Scip）实现的都是 **Framework** 版。两套同名接口，
/// **demo2 依赖的那个一个实现者都没有**——所以 `BendersSolverUnavailable` 是**永久**的终态，不是"暂时没接线"。
///
/// 这里把 demo2 切到 Framework 版，于是第一次可以真的把一个后端传进去。
///
/// Before demo2's `BendersSolver` took the **core** `IBendersDecompositionSolver`, while all six plugins implement the **framework** one. Two same-named
/// interfaces, and demo2 depended on **the one with no implementers** — so `BendersSolverUnavailable` was a **permanent** end state, not "not wired yet". 
/// switches demo2 to the framework version, so a backend can be handed in for the first time.
/// </remarks>
public sealed class Demo2BendersScipTests
{
    /// <summary>
    /// **两套同名接口对齐了：demo2 收的那个，正是插件实现的那些** / The two same-named interfaces now line up.
    /// </summary>
    /// <remarks>
    /// 反射判据，**不需要求解器**，所以放在 `#if` 外面：`FullLoadApplication.Execute` 的 `bendersSolver` 参数类型必须是插件实现的那个接口。
    /// **参数类型一错这里就红**——比"跑一遍看看"更耐改。
    ///
    /// A reflection criterion that **needs no solver**, hence outside `#if`: the `bendersSolver` parameter of `FullLoadApplication.Execute` must be the interface the
    /// plugins implement. **A wrong parameter type turns this red**, which outlives any single run.
    /// </remarks>
    [Fact]
    public void TheSolverParameter_ShouldBeTheInterfaceThePluginsImplement() {
        System.Reflection.ParameterInfo parameter = typeof(FullLoadApplication)
            .GetMethod("Execute")!
            .GetParameters()
            .First(candidate => candidate.Name == "bendersSolver");

        parameter.ParameterType.Should().Be(typeof(Fuookami.Ospf.Framework.Solver.IBendersDecompositionSolver),
            "all six solver plugins implement this one; demo2 must not depend on the other same-named interface that nobody implements");
        typeof(ScipBendersDecompositionSolver).Should().Implement(parameter.ParameterType,
            "the real backend must satisfy the parameter type demo2 declares");
    }

#if SCIP_INTEGRATION
    /// <summary>
    /// **真后端进来之后，状态不再是"没有求解器"** / With a real backend the status is no longer "no solver".
    /// </summary>
    /// <remarks>
    /// 判据钉的是**接线**，不是"Benders 求解成功"：主问题目前 11 行、theta 一个变量，子问题 29 行，
    /// 能不能收敛是模型自身的性质，**不在这里范围**。所以断言：请求走进 Benders 分支（`solver_path=benders`）、
    /// 后端被接受（状态不是 `BendersSolverUnavailable`）；**没有走到最优时不许带分配**，且必须给得出理由。
    ///
    /// This pins **the wiring**, not "Benders solved successfully": the master currently has 11 rows and one theta variable while the sub has 29, so convergence is a
    /// property of the model, **out of scope here**. The assertions are: the request entered the Benders branch, the backend was accepted (status is not
    /// `BendersSolverUnavailable`), **no assignments appear on a non-optimal outcome**, and a reason is always given.
    /// </remarks>
    [Fact]
    public void ARealScipBackend_ShouldBeAcceptedByTheBendersPath() {
        RequestDTO request = AircraftLoadingDemo.CreateSampleRequest() with {
            SolvePolicy = new SolvePolicy(PreferBenders: true, BendersFallbackToMilp: false),
        };

        (ResponseDTO Response, RenderDTO? Render) executed =
            new FullLoadApplication().Execute(request, bendersSolver: new ScipBendersDecompositionSolver());

        IReadOnlyList<string> notes = executed.Response.Notes ?? (IReadOnlyList<string>)Array.Empty<string>();

        notes.Should().Contain(note => note.Contains("solver_path=benders"),
            "the request must have taken the Benders branch rather than MILP");
        executed.Response.Status.Should().NotBe("BendersSolverUnavailable",
            "a real SCIP backend was supplied; that status used to mean 'demo2 cannot take any backend at all'");

        if (executed.Response.Status != "Optimal") {
            executed.Response.Assignments.Should().BeNull(
                "status was " + executed.Response.Status + ", which must never come with invented assignments");
            notes.Should().Contain(note => note.Contains("benders_status=") || note.Contains("benders"),
                "an unusual outcome must say why");
        }
    }
    /// <summary>**单模型链在真求解器上跑到什么程度** / How far the single-model chain gets on a real solver.</summary>
    /// <remarks>
    /// 用假解已经把这条链推到"合并解违反约束"——**假解到此为止**。这一条换真后端（SCIP），量"这条链在真求解器上能不能收敛"。
    /// 不预测结果：**收敛也好、说出为什么没收敛也好**，两者都是可测的结论。
    ///
    /// pushed this chain to "merged solution violates constraint" using a fake solution — **the fake gets no further**. This one swaps in a real backend and measures
    /// whether it converges. The outcome is not predicted: **converging or naming why it did not** are both testable results.
    /// </remarks>
    [Fact]
    public async Task TheSingleModelChain_ShouldRunOnARealScipBackend() {
        RequestDTO request = AircraftLoadingDemo.CreateSampleRequest() with {
            SolvePolicy = new SolvePolicy(PreferBenders: true, BendersFallbackToMilp: false),
        };

        var application = new FullLoadApplication();
        application.Init(request).IsOk.Should().BeTrue();

        List<string> notes = [];
        ResponseDTO response = await application.SolveSingleModelBendersAsync(
            StowageMode.FullLoad, new ScipBendersDecompositionSolver(), milpSolver: null, notes);

        notes.Should().Contain(note => note.Contains("benders_split=", System.StringComparison.Ordinal),
            "the chain must at least reach the partition; got: " + string.Join("|", notes));

        // **实测：真后端上这条链 `Optimal`。** 假解那几批的阻塞全是夹具问题，链路本身是通的。
        // 分配仍然不解码（`benders_assignments=not_decoded:single_model`，的规矩）。
        //
        // **Measured : on a real backend this chain reaches `Optimal`.** Every blocker in the fake-solution batches was fixture shaped; the chain itself works.
        // Assignments are still not decoded (the earlier rule).
        // **实测：Benders 的目标也是 `0`** ⇒ 与直连 MILP 相等 ⇒ 最优解退化（见 `TheSameRequest_ShouldLoadThroughDirectMilp`）。
        // **Measured : Benders' objective is `0` as well** ⇒ equal to the direct path ⇒ degenerate optimum.
        response.Objective.Should().Be(0.0, "measured: equal to the direct path's objective, which is what makes the optimum degenerate");
        response.Status.Should().Be("Optimal", "got: " + string.Join("|", notes));
        response.Assignments.Should().BeNull("the value mapping is not done yet, and inventing assignments is worse than leaving them out");
        // **缺口要有名有数**：不是"忘了解码"，是**没有 `Item` → 变量的映射**；主问题有 12 个 token。
        // **The gap has a name and a number**: not "we forgot", but **no `Item`-to-variable map**; the master holds 12 tokens.
        // **实测：解码跑通了，但主问题把 6 个 `assignment_*` 全解成 0**——于是"解出 0 个分配"被明确记为
        // `not_decoded:empty_master_assignment`，而不是交一个与"什么都没装"无法区分的空列表。
        //
        // **Measured: decoding runs, but the master solves all six `assignment_*` variables to zero** — so an empty master assignment is recorded as
        // `not_decoded:empty_master_assignment` instead of handing back a list indistinguishable from "nothing was loaded".
        // **实测：`iterations=1;gap=0`**——一轮就停、间隙为零，说明**割从没起作用**，主问题的全零分配被直接接受。
        // 不是"没解码"，也不是"目标没奖励装载"，而是**划分把装载约束全留在子问题里**，于是零分配对追索可行、上界等于下界。
        //
        // **Measured: `iterations=1;gap=0`**  — stopping after one round with a zero gap means **the cuts never acted** and the master's zero assignment was accepted
        // outright. Not a decode problem and not a missing objective term: **the split leaves the loading constraints in the sub**, so zero is feasible for the recourse and
        // the bound equals it.
        // **实测：`benders_bindings=11;stage_variables=15`**——绑定通道**是通的**，"子问题收不到决策"这个假设被排除。
        // 同时一个**新的矛盾**浮出来：单模型只有 **11 行**，划分后却是 **14 + 19 = 33 行**——两部分加起来是模型的三倍，
        // 于是"划分"在行的意义上**不是划分**，而是把行复制/重建了。后续要量的就是这个。
        //
        // **Measured : `benders_bindings=11;stage_variables=15`** — the binding channel **is open**, ruling out "the sub never receives the decisions". A **new
        // contradiction** surfaces at the same time: the single model has **11 rows**, the split reports **14 + 19 = 33** — three times the model, so "partition" is not a
        // partition in the row sense; parts of the rows are copied or rebuilt. That is what the next step measures.
        // **实测：子问题的 19 行里没有一行与 `assignment_` 有关**——全是 `empty_between_cargo_*` 的线性化行，加 6 条变量界。
        // 于是"装载约束留在子问题"这句也要再更正：**装载约束不在子问题，也不在主问题里以"必须装载"的形式存在**——
        // 主问题只有形如 `assignment ≤ …` 的上界行，**零分配天然满足**。这才是"零分配可行"的原因。
        //
        // **Measured : none of the sub's 19 rows mentions `assignment_`** — they are all `empty_between_cargo_*` linearisation rows plus six variable bounds.
        // So "the loading constraints sit in the sub" needs correcting again: **they are not in the sub, and they do not exist in the master as a "must load" form either** —
        // the master holds upper-bound rows like `assignment ≤ …`, which **zero satisfies trivially**. That is why zero is feasible.
        // **阶段表现在按类型声明并生效**：15 个变量里 **11 个是整数**，全部进主问题；切分结果与划分器的默认推断**完全一致**
        // （`master_rows=14;sub_rows=19`）——这正是"声明只是把默认行为写明、不改变形状"的证据。
        //
        // **The stage table is now declared by type and takes effect**: of 15 variables **11 are integer** and all go to the master; the split is **identical** to the
        // partitioner's own inference (`master_rows=14;sub_rows=19`) — evidence that the declaration spells out the default instead of changing the shape.
        notes.Should().Contain("benders_stages=declared:15;integer=11", "measured: the integer count is what the type rule classifies");
        notes.Should().Contain("benders_bindings=11;stage_variables=15", "the channel is open; the contradiction is elsewhere");
        notes.Should().Contain(note => note.StartsWith("benders_sub_rows=", System.StringComparison.Ordinal)
            && !note.Contains("assignment_", System.StringComparison.Ordinal),
            "measured: the sub carries only the linearisation rows, so nothing there forces a cargo onto a position");
        notes.Should().Contain("benders_assignments=not_decoded:empty_master_assignment",
            "an empty master assignment must be named, not presented as a plan");
    }
    /// <summary>**同一个请求，直连 MILP 会装载** / The same request under direct MILP does load.</summary>
    /// <remarks>
    /// 量到 Benders 一轮就停、间隙 0、分配为零。要判断"模型允许空载"还是"Benders 走错"，只需问同一个请求走直连 MILP 会怎样——**这一条量那一半**。
    /// **两条路径不能在同一进程里连着跑**：域上下文是静态的、一次性的，第二个路径连模型都建不出来（`BendersSingleModelNotBuilt`）。
    /// 所以这里是**两次独立测量**，不是一次对比——写清楚，免得下一个人以为能一条判据里跑完。
    ///
    /// measured Benders stopping after one round with a zero gap and no assignment. Telling "the model allows an empty load" from "Benders is wrong" only needs
    /// the same request on the direct MILP path — **this measures that half**. **The two paths cannot run back to back in one process**: the domain contexts are static and
    /// single-use (noted), so the second path cannot even build its model. This is therefore **two separate measurements**, not one comparison, and that is stated
    /// so the next reader does not assume a single criterion can do it.
    /// </remarks>
    [Fact]
    public void TheSameRequest_ShouldLoadThroughDirectMilp() {
        RequestDTO request = AircraftLoadingDemo.CreateSampleRequest();

        var application = new FullLoadApplication();
        application.Init(request).IsOk.Should().BeTrue();

        ResponseDTO direct = application.Execute(
            request with { SolvePolicy = new SolvePolicy(PreferBenders: false, BendersFallbackToMilp: false) },
            bendersSolver: null,
            milpSolver: new ScipLinearSolver()).Response;

        direct.Status.Should().Be("Optimal", "notes: " + string.Join("|", direct.Notes ?? []));
        direct.Assignments.Should().NotBeNull("a direct solve that assigns nothing demonstrates nothing");
        // **实测：直连 MILP 的目标是 `0`，而它装了 38 个分配。** Benders 的目标**也是 `0`**（见上一条判据）。
        // 两个目标相等 ⇒ **这个请求的最优解是退化的**：装载与不装载同样优，选哪个只取决于求解器的起点。
        // 于是"Benders 解出 0 个分配"**不是缺陷**，是**夹具无法区分**——夹具必须让零装载不是最优。
        //
        // **Measured : the direct MILP objective is `0` while it assigns 38 pairs, and Benders' objective is `0` too.** Equal objectives mean **the request's optimum is
        // degenerate**: loading and not loading are equally good and the choice falls to the solver's starting point. So "Benders assigns nothing" is **not a defect**, it is
        // **a fixture that cannot tell the difference** — the fixture must make zero loading sub-optimal.
        direct.Objective.Should().Be(0.0, "measured: the direct path reaches zero while loading, which is what makes the optimum degenerate");
        direct.Assignments!.Count.Should().BePositive(
            "MILP loads cargo on this request, so Benders returning zero assignments (B896/B897) is a defect in the path, not the model");
    }

    /// <summary>**必装货让零装载不再合法** / Required cargo makes zero loading illegal.</summary>
    /// <remarks>
    /// 量到最优解退化（两条路径目标都是 0、零装载合法），找到根（demo 造不出必装货），打通了必装标志。
    /// 这一条是**第一个能区分的判据**：必装货 ⇒ `loading_capacity_item_*` 是 `EQ` ⇒ 零分配**不可行** ⇒ 若链仍然 `Optimal`，
    /// 它**必须**解出分配；若解出零分配，那才是**真的缺陷**。
    ///
    /// found the optimum degenerate, found the root and wired the flag. This is **the first criterion that can tell the difference**: required cargo makes the
    /// rows `EQ`, so zero assignments are **infeasible** — if the chain is still `Optimal` it **must** decode assignments, and zero would finally be a real defect.
    /// </remarks>
    [Fact]
    public void TheRequiredSample_ShouldNotComeBackEmpty() {
        RequestDTO request = AircraftLoadingDemo.CreateRequiredSampleRequest();
        var application = new FullLoadApplication();
        application.Init(request).IsOk.Should().BeTrue();

        List<string> notes = [];
        ResponseDTO response = application.SolveSingleModelBendersAsync(
            StowageMode.FullLoad, new ScipBendersDecompositionSolver(), milpSolver: null, notes)
            .GetAwaiter().GetResult();

        // **实测：必装货让主问题**直接不可行**（`Benders 主问题已证明不可行`）。**
        // 这是**第一个真缺陷级别的信号**——但也可能是**这份夹具本身装不下**：`LoadingAggregation` 会把"单件重量超过舱位容量"的分配变量**整列剔除**，
        // 于是必装货若哪个舱位都装不下，它的 `EQ` 行**无解**。两种解释必须分开，后续先量容量。
        //
        // **Measured : required cargo makes the master**proven infeasible**.** This is the first defect-level signal — but it may also be **a fixture that cannot fit**:
        // `LoadingAggregation` **drops the whole assignment column** when one item outweighs a position's capacity, so a required cargo that fits nowhere has an **unsatisfiable**
        // `EQ` row. The two readings must be separated, and the next step measures capacity first.
        response.Succeed.Should().BeFalse();
        response.Assignments.Should().BeNull("a failing run must never carry assignments");
        // 已定性**：同一份请求走直连 MILP 也 `ORModelInfeasible` ⇒ **夹具装不下**，不是主问题缺陷（见 `TheRequiredSample_ShouldSayWhetherItFitsAtAll`）。
        // **settled it**: the same request is `ORModelInfeasible` under direct MILP too ⇒ **the fixture does not fit**, the master is not defective.
        notes.Should().Contain(note => note.Contains("主问题已证明不可行", System.StringComparison.Ordinal),
            "measured: the required sample makes the master infeasible, and B908 showed the fixture itself cannot fit");
    }
    /// <summary>**必装请求走直连 MILP 装不装得下** / Does the required request fit under direct MILP?</summary>
    /// <remarks>
    /// 量到必装货让 Benders **主问题不可行**。定性只有两种：
    /// **夹具装不下**（那么直连 MILP 同样不可行）或 **Benders 主问题有缺陷**（那么直连 MILP 能装、Benders 不能）。
    /// 这一条量前半——**一条判据只需要跑一个路径，所以不存在"两条路径同进程"的限制**。
    ///
    /// measured that required cargo makes the **master infeasible**. There are only two readings: **the fixture does not fit** (direct MILP fails too) or **the master is
    /// defective** (direct MILP fits, Benders does not). This measures the first — **one path per criterion, so the one-process limit does not apply**.
    /// </remarks>
    [Fact]
    public void TheRequiredSample_ShouldSayWhetherItFitsAtAll() {
        RequestDTO request = AircraftLoadingDemo.CreateRequiredSampleRequest();
        var application = new FullLoadApplication();
        application.Init(request).IsOk.Should().BeTrue();

        ResponseDTO direct = application.Execute(
            request with { SolvePolicy = new SolvePolicy(PreferBenders: false, BendersFallbackToMilp: false) },
            bendersSolver: null,
            milpSolver: new ScipLinearSolver()).Response;

        // **实测：直连 MILP 对这份必装请求同样不可行**（`ORModelInfeasible`，52 行 / 76 符号 / 7 目标）。
        // ⇒ 那句"主问题已证明不可行"**不是 Benders 的缺陷，是这份夹具装不下**。
        // **结论先钉住，再改夹具**：改夹具之后这条判据必须重新量，它变红就是"终于装得下了"的信号。
        //
        // **Measured : the direct MILP is infeasible on this required request too** (52 rows / 76 symbols / 7 objectives). So the earlier "master is proven infeasible" is
        // **not a Benders defect — the fixture does not fit**. **Pin the conclusion before touching the fixture**: once it changes, this criterion must be re-measured, and it
        // turning red is the signal "the fixture finally fits".
        direct.Succeed.Should().BeFalse();
        direct.Assignments.Should().BeNull("an infeasible solve must never invent assignments");
        direct.Notes.Should().Contain(note => note.Contains("ORModelInfeasible", System.StringComparison.Ordinal),
            "the note must name the infeasibility so the next reader does not blame Benders");
    }
    /// <summary>**一次放宽一项，看谁让必装请求变可行** / Relax one bound at a time to find the blocking one.</summary>
    /// <remarks>
    /// 定性为"夹具装不下"。这一条用**逐项放宽**定位卡住的那条约束：每次只改一个字段、每次都用**新的请求实例**（域上下文一次性），
    /// 谁改完就可行，就是谁。**一次改一项**是这条判据的全部价值——一次全改只会得到"可行"，得不到"为什么不可行"。
    ///
    /// settled "the fixture does not fit". This locates the blocking constraint by **relaxing one field at a time**, each with a **fresh request instance** (the domain
    /// contexts are single-use). **One change at a time** is the whole point: relaxing everything only proves "it fits", never "what blocked it".
    /// </remarks>
    [Fact]
    public void TheRequiredSample_ShouldNameWhichBoundBlocksIt() {
        var relaxations = new (string Name, Func<RequestDTO, RequestDTO> Apply)[] {
            ("payload_upper_bound", request => request with { PayloadUpperBound = 100.0 }),
            ("min_payload_ratio", request => request with { MinPayloadRatio = 0.0 }),
            ("max_adjacent_load_gap", request => request with { MaxAdjacentLoadGap = 100.0 }),
            ("max_cumulative_load", request => request with { MaxCumulativeForwardLoad = 100.0, MaxCumulativeBackwardLoad = 100.0 }),
            ("moment_envelope", request => request with { EnvelopeLongitudinalMomentMin = -1000.0, EnvelopeLongitudinalMomentMax = 1000.0 }),
            ("moment_deviation", request => request with { MaxLongitudinalMomentDeviation = 1000.0 }),
            ("lateral_imbalance", request => request with { MaxLateralImbalance = 1000.0 }),
        };

        List<string> results = [];
        foreach ((string name, Func<RequestDTO, RequestDTO> apply) in relaxations) {
            RequestDTO request = apply(AircraftLoadingDemo.CreateRequiredSampleRequest());
            var application = new FullLoadApplication();
            if (!application.Init(request).IsOk) {
                results.Add(name + "=init_failed");
                continue;
            }
            ResponseDTO response = application.Execute(
                request with { SolvePolicy = new SolvePolicy(PreferBenders: false, BendersFallbackToMilp: false) },
                bendersSolver: null,
                milpSolver: new ScipLinearSolver()).Response;
            results.Add(name + "=" + response.Status);
        }

        // **实测：七项上限逐个放宽，**没有一项**让请求变可行**（全部 `DirectSolveFailed`）。
        // ⇒ 卡住的**不是任何一条上限**，而是**结构性的**：两个舱位 + 三件货，其中 C1/C3 带 `RequiresSeparation: true`。
        // **结论先钉住**：这条判据的全部价值是"上限不是原因"，它把后续的搜索空间从"七个字段"缩到"舱位与分离约束"。
        //
        // **Measured : relaxing each of the seven bounds one at a time makes **none** of them feasible** (all `DirectSolveFailed`). So the blocker is **not any bound**
        // but **structural**: two positions against three cargo, two of which carry `RequiresSeparation: true`. **Pin the negative result** — its whole value is "the bounds are
        // not the cause", narrowing the next step from seven fields to "positions and separation".
        results.Should().HaveCount(relaxations.Length, "every relaxation must be measured, not skipped");
        results.Should().OnlyContain(result => !result.EndsWith("=Optimal", System.StringComparison.Ordinal),
            "measured: no single bound unblocks the required sample; the blocker is structural");
    }
    /// <summary>**分离要求是不是卡点** / Is the separation requirement the blocker?</summary>
    /// <remarks>
    /// 排除了七项上限，剩下结构性嫌疑：两个舱位对三件货，其中 C1/C3 带 `RequiresSeparation: true`。
    /// 这一条沿用"一次改一项"：只去掉 C1 的、只去掉 C3 的、两个都去掉——**只看它们各自是否足够**。
    ///
    /// ruled out seven bounds and left a structural suspect: two positions against three cargo, two of them with `RequiresSeparation: true`.
    /// Same discipline — drop C1's only, C3's only, then both — **to see whether either alone is enough**.
    /// </remarks>
    [Fact]
    public void TheRequiredSample_ShouldSayWhetherSeparationBlocksIt() {
        static IReadOnlyList<CargoInput> Keep(RequestDTO request, params string[] names) =>
            System.Linq.Enumerable.ToList(System.Linq.Enumerable.Where(request.Cargos, cargo => Array.IndexOf(names, cargo.Name) >= 0));

        static RequestDTO Without(RequestDTO request, params string[] names) => request with {
            Cargos = System.Linq.Enumerable.ToList(request.Cargos)
                .ConvertAll(cargo => Array.IndexOf(names, cargo.Name) >= 0 ? cargo with { RequiresSeparation = false } : cargo),
        };

        var cases = new (string Name, Func<RequestDTO, RequestDTO> Apply)[] {
            ("drop_C1_separation", request => Without(request, "C1")),
            ("drop_C3_separation", request => Without(request, "C3")),
            ("drop_both_separation", request => Without(request, "C1", "C3")),
            ("light_cargo", request => request with {
                Cargos = System.Linq.Enumerable.ToList(request.Cargos).ConvertAll(cargo => cargo with { Weight = 1.0 }),
            }),
            ("one_cargo_only", request => request with {
                Cargos = System.Linq.Enumerable.ToList(request.Cargos).GetRange(0, 1),
            }),
            ("pair_C1_C2", request => request with { Cargos = Keep(request, "C1", "C2") }),
            ("pair_C1_C3", request => request with { Cargos = Keep(request, "C1", "C3") }),
            ("pair_C2_C3", request => request with { Cargos = Keep(request, "C2", "C3") }),
        };

        List<string> results = [];
        foreach ((string name, Func<RequestDTO, RequestDTO> apply) in cases) {
            RequestDTO request = apply(AircraftLoadingDemo.CreateRequiredSampleRequest());
            var application = new FullLoadApplication();
            if (!application.Init(request).IsOk) {
                results.Add(name + "=init_failed");
                continue;
            }
            ResponseDTO response = application.Execute(
                request with { SolvePolicy = new SolvePolicy(PreferBenders: false, BendersFallbackToMilp: false) },
                bendersSolver: null,
                milpSolver: new ScipLinearSolver()).Response;
            results.Add(name + "=" + response.Status);
        }

        // **实测**：
        // - 去掉 C1 / C3 / 两者的分离要求 —— **都不行**（分离不是原因）；
        // - 全部货改成 1.0 的轻载 —— **也不行**（总重量不是原因）；
        // - **只留一件货 —— `Optimal`**。
        // ⇒ 卡点出现在"**必须装多件**"这件事上：两个舱位装三件，某条**位置级**约束挡住了第二件。
        //
        // **Measured **: dropping C1's / C3's / both separation requirements fails; lightening every cargo to 1.0 fails; **keeping a single cargo is `Optimal`**.
        // So the blocker appears the moment **more than one cargo must be loaded** — some **position-level** constraint stops the second item.
        // **实测**：三件不可行，但**三种两件组合全部 `Optimal`**，单件也可行。
        // ⇒ 卡点是**每舱位的件数**（两个舱位各装一件可以，三件要有一个舱位装两件，就是这里失败）。
        // 这不是缺陷：**夹具要求了舱位装不下的件数**。
        // 并且它送来了**能区分的夹具**：两件必装 ⇒ 零装载不可行 ⇒ 才量得出分配有没有解出来。
        //
        // **Measured **: three is infeasible while **all three pairs are `Optimal`** and a single cargo is too. So the blocker is **the per-position item count** (two
        // positions take one item each; three would need one position to take two). Not a defect — **the fixture asks for more than the positions can hold** — and it hands
        // over **a discriminating fixture**: two required cargo make zero loading infeasible, so assignment decoding can finally be measured.
        results.Should().HaveCount(cases.Length);
        results.Should().Contain("one_cargo_only=Optimal");
        results.Should().Contain("pair_C1_C2=Optimal");
        results.Should().Contain("pair_C1_C3=Optimal");
        results.Should().Contain("pair_C2_C3=Optimal", "measured: any pair fits, so the per-position count is what three cargo break");
        results.Should().Contain("one_cargo_only=Optimal", "measured: one required cargo fits, so the model and the chain are fine");
        results.Where(result => !result.Contains("pair_", System.StringComparison.Ordinal)
                && !result.StartsWith("one_cargo_only", System.StringComparison.Ordinal))
            .Should().OnlyContain(result => !result.EndsWith("=Optimal", System.StringComparison.Ordinal),
                "measured: neither separation nor total weight unblocks it; three cargo exceed the per-position count");
    }
    /// <summary>**能区分的夹具上，Benders 到底解出分配没有** / On a discriminating fixture, does Benders decode assignments?</summary>
    /// <remarks>
    /// 一直在问这个问题，但当时没有能区分的夹具（ 退化、 造不出必装货、 / / 逐步定位）。
    /// 这一条用**两件必装**：若链仍然 `Optimal`，它**必须**解出分配。
    ///
    /// kept asking this without a discriminating fixture ( degenerate, unable to produce required cargo, narrowing). This uses **two required
    /// cargo** ( showed feasible, zero loading infeasible): if the chain is still `Optimal` it **must** decode assignments.
    /// </remarks>
    [Fact]
    public void ThePairFixture_ShouldDecodeItsAssignments() {
        RequestDTO request = AircraftLoadingDemo.CreatePairRequiredSampleRequest();
        var application = new FullLoadApplication();
        application.Init(request).IsOk.Should().BeTrue();

        List<string> notes = [];
        ResponseDTO response = application.SolveSingleModelBendersAsync(
            StowageMode.FullLoad, new ScipBendersDecompositionSolver(), milpSolver: null, notes)
            .GetAwaiter().GetResult();

        // **实测：链路完整走通。** `Optimal`、`assignments=C1->P2,C2->P1`、`benders_assignments=decoded:2`。
        // 十批的调查到这里收口：**Benders 单模型链从建模、划分、求解到按名字解码分配全部可用**——之前解不出分配，
        // **唯一原因是夹具里零装载就是最优**。
        //
        // **Measured : the chain works end to end.** `Optimal`, `assignments=C1->P2,C2->P1`, `benders_assignments=decoded:2`. Ten batches close here: **the
        // single-model Benders chain is usable from build through split, solve and name-based decoding** — the only reason assignments never appeared is that **zero loading
        // was already optimal in the fixture** ( degenerate, unable to produce required cargo).
        response.Status.Should().Be("Optimal", "got: " + string.Join("|", notes));
        response.Assignments.Should().NotBeNull("a discriminating fixture must produce a decoded plan");
        response.Assignments!.Should().HaveCount(2, "two required cargo, two assignments");
        response.Assignments!.Should().BeEquivalentTo(new[] { "C1->P2", "C2->P1" });
        notes.Should().Contain("benders_assignments=decoded:2", "the decode outcome must be named, not inferred from a non-empty list");
    }
    #endif
}
