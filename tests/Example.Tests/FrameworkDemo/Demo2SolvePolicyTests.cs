using System;
using System.Collections.Generic;
using FluentAssertions;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Application;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Domain.Stowage;
using Fuookami.Ospf.Example.FrameworkDemo.Demo2.Infrastructure;
using Fuookami.Ospf.Core.Plugin.Scip;
using Xunit;

namespace Fuookami.Ospf.Example.Tests.FrameworkDemo;

/// <summary>
/// 分区列生成的两条策略校验 / The two partition column-generation policy checks.
/// </summary>
/// <remarks>
/// `SolvePolicy` 有三个字段。**第三个存在与否不是"整洁度问题"**：它是
/// "分区列生成与 Benders 不能同时启用"那条校验的**唯一输入**。输入不在时，校验只能是假的，
/// 所以此前明确记着"实现不了"；补上字段后，校验随之落地。
///
/// `SolvePolicy` has three fields; this implementation had two. **Whether the third exists is not a tidiness question**: it is the **only input** to the
/// "partition CG and Benders cannot both be enabled" check. With no input, that check could only be faked, which is why this implementation recorded it as "cannot move";
/// The field is added and the check lands with it.
/// </remarks>
public sealed class Demo2SolvePolicyTests
{
    [Fact]
    public void TheDefaultPolicy_ShouldNotAskForPartitionColumnGeneration() {
        // **默认必须关着**：新增字段一旦默认 `true`，所有既有请求都会走进"未实现"那条拒绝分支——
        // 表现是"升级之后全都解不出来了"，而原因藏在默认值里。
        //
        // **It must default off**: a new field defaulting to `true` sends every existing request into the "not implemented" refusal — which looks like
        // "everything stopped solving after the upgrade", with the cause hidden in a default.
        SolvePolicy policy = new();
        policy.PartitionColumnGeneration.Should().BeFalse();
        policy.PreferBenders.Should().BeFalse();
        policy.BendersFallbackToMilp.Should().BeTrue();
    }

    [Fact]
    public void TheConflict_ShouldBeRefusedWithTheBaselinesWording() {
        RequestDTO request = AircraftLoadingDemo.CreateSampleRequest() with {
            SolvePolicy = new SolvePolicy(PreferBenders: true, PartitionColumnGeneration: true),
        };

        ResponseDTO response = Demo2ApplicationService.Solve(request, StowageMode.FullLoad).Response;

        response.Succeed.Should().BeFalse();
        response.Status.Should().Be("PolicyConflict");
        response.Notes.Should().Contain(note => note.Contains("不能同时启用"),
            "the baseline's wording says the two strategies are independent, not that one is unsupported");
        response.Notes.Should().NotContain(note => note.Contains("NotYetPorted"),
            "a mutually-exclusive pair is a request error, not a missing port");
    }

    /// <summary>
    /// **只要列生成、没要 Benders → 明确说未实现** / Column generation alone without Benders reports "not implemented" explicitly.
    /// </summary>
    /// <remarks>
    /// 这条与上一条成对：上一条证明"两者同时"被拒，这条证明**只开一个不会被同一句话挡掉**——否则"全都拒"也能让上一条绿。
    ///
    /// 而它必须是 **`NotYetPorted`** 而不是悄悄解出一个 MILP 答案：**静默回落会给出一个看起来成功的、不是用户要的结果**，
    /// 这正是本仓反复记录的那类缺陷。
    ///
    /// Paired with the criterion above: the first proves "both" is refused, this proves **one alone is not refused by the same rule** — otherwise "refuse
    /// everything" would keep the first green. And it must be a **named refusal** rather than quietly producing a MILP answer: **a silent fallback hands back
    /// something that looks successful and is not what was asked for**, the defect class this implementation has recorded repeatedly.
    /// </remarks>
    [Fact]
    public void ColumnGenerationWithoutASolver_ShouldSayTheSolverIsMissingRatherThanFallBack() {
        RequestDTO request = AircraftLoadingDemo.CreateSampleRequest() with {
            SolvePolicy = new SolvePolicy(PartitionColumnGeneration: true),
        };

        ResponseDTO response = Demo2ApplicationService.Solve(request, StowageMode.FullLoad).Response;

        response.Succeed.Should().BeFalse();
        // **换了状态码**：原来的 `NotYetPorted` 是错的——注册一直都在，分区配置也已落地，
        // 分派现在只缺**求解器**。说"未实现"会把人送去查模型，而真正缺的是调用方没给后端。
        // **changed the status code**: the earlier `NotYetPorted` was wrong — the registration was always there and the partition configuration landed, so the
        // dispatch now lacks only a **solver**. Saying "not implemented" sends people to inspect the model when the caller simply supplied no backend.
        response.Status.Should().Be("ColumnGenerationSolverNotSupplied");
        response.Notes.Should().Contain(
            note => note.Contains("IColumnGenerationSolver"),
            "the note must name the type the caller has to supply");
    }
    /// <summary>
    /// **校验失败也要发收尾心跳** / A refuted policy still emits the finishing heartbeat.
    /// </summary>
    /// <remarks>
    /// 策略校验放在心跳**之前**，所以这两条提前返回很容易漏掉收尾心跳。而"只报成功的心跳等于没有心跳"——
    /// 调用方最需要知道的恰恰是"它结束了、而且失败了"。 把收尾抽成 `Finish` 就是为了这个。
    ///
 /// The checks run **before** the heartbeat (. "Heartbeats that only report
    /// success are no heartbeats at all" — what a caller most needs to know is that it **ended and failed**. extracted `Finish` for exactly this.
    /// </remarks>
    [Fact]
    public void ARefusedPolicy_ShouldStillEmitTheFinishingHeartbeat() {
        RequestDTO request = AircraftLoadingDemo.CreateSampleRequest() with {
            SolvePolicy = new SolvePolicy(PartitionColumnGeneration: true),
        };
        var beats = new List<FinnishHeartBeatDTO>();

        Demo2ApplicationService.Solve(request, StowageMode.FullLoad, finished: beats.Add);

        beats.Should().ContainSingle("one run, one finishing beat — even when the request is refused before dispatch");
        // **失败的心跳带原因**：成功那条 `Code` 为 0、`Message` 为空；失败那条正好相反。
        // **A failed beat carries the reason**: the success one has code 0 and an empty message; the failed one is the mirror image.
        beats[0].Message.Should().NotBeEmpty("a refused run's beat must say why it failed");
    }

    /// <summary>
    /// **机型守卫只在 `Unknown` 上触发** / The aircraft guard fires only on `Unknown`.
    /// </summary>
    /// <remarks>
    /// 本仓此前把 `SupportedAircraft` 写成 ` || `，于是 **/ 会被判为不支持** —— 而排除 `Unknown`。
    /// 这条判据同时钉住两端：三个"支持"的机型**必须能被分派**（不报 `UnsupportedAircraft`），`Unknown` **必须被拒**。
    /// **只钉后者的话，"全都不支持"也能让它绿。**
    ///
 /// This implementation used to define `SupportedAircraft` as ` || `, so **and reported unsupported** — the implementation excludes only `Unknown` (measured by
    /// ). This criterion pins both ends: the three supported types **must reach dispatch** (no `UnsupportedAircraft`), and `Unknown` **must be refused**. With only
    /// the second half, "nothing is supported" would stay green.
    /// </remarks>
    [Theory]
    [InlineData(AircraftTypeInput.B737)]
    [InlineData(AircraftTypeInput.B757)]
    [InlineData(AircraftTypeInput.B767)]
    [InlineData(AircraftTypeInput.B747)]
    public void TheSupportedAircraft_ShouldReachDispatch(AircraftTypeInput aircraftType) {
        RequestDTO request = AircraftLoadingDemo.CreateSampleRequest() with { AircraftType = aircraftType };

        ResponseDTO response = Demo2ApplicationService.Solve(request, StowageMode.FullLoad).Response;

        response.Status.Should().NotBe("UnsupportedAircraft",
            "only Unknown is unsupported; every concrete aircraft type reaches the solver");
    }

    /// <summary>
    /// **`Unknown` 必须在分派处被拒，而不是走进求解** / `Unknown` is refused at dispatch rather than reaching the solver.
    /// </summary>
    [Fact]
    public void UnknownAircraft_ShouldBeRefusedByTheDispatcher() {
        RequestDTO request = AircraftLoadingDemo.CreateSampleRequest() with { AircraftType = AircraftTypeInput.Unknown };

        ResponseDTO response = Demo2ApplicationService.Solve(request, StowageMode.FullLoad).Response;

        response.Succeed.Should().BeFalse();
        response.Status.Should().Be("UnsupportedAircraft");
        response.Notes.Should().Contain(note => note.Contains("不支持") || note.Contains("Unsupported aircraft type"),
            "the note must name the aircraft type and the path, like the baseline's response mapper");
    }

    /// <summary>
    /// **机型守卫在策略校验之前** / The aircraft guard runs before the policy checks.
    /// </summary>
    /// <remarks>
    /// 顺序是有意义的：`Unknown` 机型 + 策略互斥同时成立时，报哪一个决定了调用方先去改什么。
    /// 这里报机型，**先改策略的人会白改一遍**。这条判据钉的就是那个顺序。
    ///
 /// The order matters: with `Unknown` **and** a policy conflict both present, which one is reported decides what the caller fixes first. The implementation reports the
    /// aircraft, so **someone who fixed the policy first would have wasted the effort**. This criterion pins that order.
    /// </remarks>
    [Fact]
    public void TheAircraftGuard_ShouldWinOverThePolicyConflict() {
        RequestDTO request = AircraftLoadingDemo.CreateSampleRequest() with {
            AircraftType = AircraftTypeInput.Unknown,
            SolvePolicy = new SolvePolicy(PreferBenders: true, PartitionColumnGeneration: true),
        };

        ResponseDTO response = Demo2ApplicationService.Solve(request, StowageMode.FullLoad).Response;

        response.Status.Should().Be("UnsupportedAircraft",
            "the baseline checks the aircraft before the strategy pair");
    }

    [Fact]
    public void TheSampleRequest_ShouldStillSolveThroughTheOrdinaryPath() {
        // 三条校验都不触发时，行为必须与 之前完全一致——**新增分支不能改变既有路径**。
        // With none of the checks tripped, behaviour must match pre- exactly — **a new branch must not change the existing path**.
        RequestDTO request = AircraftLoadingDemo.CreateSampleRequest();

        (ResponseDTO Response, RenderDTO? Render) executed =
            new FullLoadApplication().Execute(request, milpSolver: new ScipLinearSolver());

        executed.Response.Status.Should().Be("Optimal");
        executed.Response.Objective.Should().Be(0.0);
    }
}
